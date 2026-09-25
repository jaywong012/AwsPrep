/*
    merge-user-keys.sql — fold the anonymous browser keys of one learner into a single key.

    WHY THIS EXISTS
    Before authentication, the SPA minted a random `user-xxxxxxxx` key into localStorage and sent
    it as X-User-Key. localStorage is scoped per ORIGIN, and the Vite dev server had no
    `strictPort`, so a busy 5173 silently became 5174 and minted a fresh learner. A different
    browser profile did the same. One person therefore accumulated five keys, each holding part of
    their history, and no account could ever see all of it.

    WHY IT IS A SCRIPT AND NOT A MIGRATION
    A migration is replayed on every database, including a fresh one where none of these keys
    exist, and it would bake one developer's data into the schema history forever. This is a
    one-off, hand-run, idempotent job. It changes no schema.

    HOW TO RUN
        sqlcmd -S localhost -d AwsCertPrep -E -C -i merge-user-keys.sql      -- reports only
    then set @DryRun = 0 below and run it again to apply.

    IDEMPOTENT: re-running after a successful apply is a no-op. The fold ASSIGNS the group
    aggregate rather than incrementing it, so a second pass over a group of one row writes that
    row's own values back. An `UPDATE ... SET ViewCount = ViewCount + x` would double on re-run.

    ROLLBACK: see merge-user-keys.rollback.sql. The backup tables this script creates are the
    rollback source, and they are created only if absent so a second run cannot overwrite them
    with already-merged data.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

------------------------------------------------------------------------------------------------
-- Settings
------------------------------------------------------------------------------------------------

DECLARE @DryRun bit = 1;            -- 1 = report only. Set to 0 to apply.
DECLARE @PurgeFixtures bit = 0;     -- 1 = also delete the test/fixture keys (see the block below).

-- The survivor. Chosen as the key with the most rows, so the fold moves the fewest.
DECLARE @Target nvarchar(100) = N'user-daa12a67';

DECLARE @Sources TABLE (UserKey nvarchar(100) PRIMARY KEY);
INSERT INTO @Sources (UserKey) VALUES
    (N'user-8dfe84c3'),
    (N'user-7b3b42f5'),
    (N'user-bb989be7'),
    (N'user-782e5db7');

-- Target + sources. The fold has to consider the target's own rows or it would leave a duplicate
-- behind for every topic both it and a source had touched.
DECLARE @All TABLE (UserKey nvarchar(100) PRIMARY KEY);
INSERT INTO @All (UserKey) SELECT UserKey FROM @Sources;
INSERT INTO @All (UserKey) VALUES (@Target);

-- What the apply must produce. Asserted before the transaction commits.
DECLARE @ExpectedSessions int = 16;
DECLARE @ExpectedLessons  int = 25;

------------------------------------------------------------------------------------------------
-- Guard: refuse to run if a retired key has been used again
--
-- These keys stopped being reachable the moment the SPA started sending a bearer token. If one
-- of them has new activity, the assumption behind this script - that all five are the same
-- person, and that nobody else is writing under them - no longer holds.
------------------------------------------------------------------------------------------------

DECLARE @Cutoff datetime2(7) = '2026-09-19T00:00:00';

IF EXISTS (SELECT 1 FROM dbo.ExamSessions
           WHERE UserKey IN (SELECT UserKey FROM @Sources) AND StartedAt > @Cutoff)
   OR EXISTS (SELECT 1 FROM dbo.LessonProgress
              WHERE UserKey IN (SELECT UserKey FROM @Sources) AND LastViewedAt > @Cutoff)
BEGIN
    RAISERROR('A source key has activity after the cutoff. Review before merging.', 16, 1);
    RETURN;
END

------------------------------------------------------------------------------------------------
-- Backups (created only if absent, so a re-run keeps the original rollback point)
------------------------------------------------------------------------------------------------

IF OBJECT_ID('dbo.LessonProgress_PreMerge') IS NULL
    SELECT * INTO dbo.LessonProgress_PreMerge
    FROM dbo.LessonProgress WHERE UserKey IN (SELECT UserKey FROM @All);

IF OBJECT_ID('dbo.ExamSessions_PreMerge') IS NULL
    SELECT * INTO dbo.ExamSessions_PreMerge
    FROM dbo.ExamSessions WHERE UserKey IN (SELECT UserKey FROM @All);

-- ExamSessionQuestions needs no backup: it is never touched. Ownership reaches it through
-- ExamSession.UserKey, and the sessions are re-keyed in place rather than deleted.

------------------------------------------------------------------------------------------------
-- Report
------------------------------------------------------------------------------------------------

PRINT '--- per key, before ---';
SELECT a.UserKey,
       Sessions  = (SELECT COUNT(*) FROM dbo.ExamSessions   e WHERE e.UserKey = a.UserKey),
       Lessons   = (SELECT COUNT(*) FROM dbo.LessonProgress p WHERE p.UserKey = a.UserKey),
       Completed = (SELECT COUNT(*) FROM dbo.LessonProgress p WHERE p.UserKey = a.UserKey
                                                                AND p.CompletedAt IS NOT NULL),
       IsTarget  = CASE WHEN a.UserKey = @Target THEN 'yes' ELSE '' END
FROM @All a
ORDER BY IsTarget DESC, a.UserKey;

PRINT '--- topics claimed by more than one key (these are what the unique index would reject) ---';
SELECT t.Slug,
       Keys         = COUNT(*),
       KeepCompleted = MIN(p.CompletedAt),
       TotalViews   = SUM(p.ViewCount),
       LastViewed   = MAX(p.LastViewedAt)
FROM dbo.LessonProgress p
JOIN dbo.LessonTopics t ON t.Id = p.LessonTopicId
WHERE p.UserKey IN (SELECT UserKey FROM @All)
GROUP BY t.Slug
HAVING COUNT(*) > 1
ORDER BY t.Slug;

PRINT '--- totals ---';
SELECT LessonRowsNow   = (SELECT COUNT(*) FROM dbo.LessonProgress
                          WHERE UserKey IN (SELECT UserKey FROM @All)),
       LessonRowsAfter = (SELECT COUNT(DISTINCT LessonTopicId) FROM dbo.LessonProgress
                          WHERE UserKey IN (SELECT UserKey FROM @All)),
       CompletionsNow  = (SELECT COUNT(DISTINCT LessonTopicId) FROM dbo.LessonProgress
                          WHERE UserKey IN (SELECT UserKey FROM @All) AND CompletedAt IS NOT NULL),
       SessionsToMove  = (SELECT COUNT(*) FROM dbo.ExamSessions
                          WHERE UserKey IN (SELECT UserKey FROM @All)),
       ExpectedLessons = @ExpectedLessons,
       ExpectedSessions = @ExpectedSessions;

IF @DryRun = 1
BEGIN
    PRINT '';
    PRINT 'DRY RUN - nothing was written. Set @DryRun = 0 to apply.';
    RETURN;
END

------------------------------------------------------------------------------------------------
-- Apply
------------------------------------------------------------------------------------------------

BEGIN TRANSACTION;

-- (a) Exam sessions: a plain re-key. IX_ExamSessions_UserKey_CertificationId is not unique, so
--     nothing can collide.
UPDATE dbo.ExamSessions
SET    UserKey = @Target
WHERE  UserKey IN (SELECT UserKey FROM @Sources);

-- (b) Lesson progress: fold first, re-key last.
--
--     IX_LessonProgress_UserKey_LessonTopicId is UNIQUE. Re-keying before folding would put two
--     rows with the same (UserKey, LessonTopicId) in the table and the statement would fail
--     halfway. So: choose one keeper per topic, move the group's history onto it, delete the
--     others, and only then re-key what is left.

IF OBJECT_ID('tempdb..#Keepers') IS NOT NULL DROP TABLE #Keepers;

-- Prefer a row the target already owns, so its Id survives and the fewest rows move.
WITH ranked AS (
    SELECT p.Id,
           p.LessonTopicId,
           rn = ROW_NUMBER() OVER (
                    PARTITION BY p.LessonTopicId
                    ORDER BY CASE WHEN p.UserKey = @Target THEN 0 ELSE 1 END,
                             p.LastViewedAt DESC,
                             p.Id)
    FROM dbo.LessonProgress p
    WHERE p.UserKey IN (SELECT UserKey FROM @All)
)
SELECT Id, LessonTopicId
INTO   #Keepers
FROM   ranked
WHERE  rn = 1;

-- Assignment, not increment - this is what makes the script re-runnable. MIN() skips NULLs, so
-- the earliest real completion wins and "never completed" stays NULL.
UPDATE p
SET    CompletedAt  = agg.MinCompleted,
       ViewCount    = agg.TotalViews,
       LastViewedAt = agg.MaxViewed
FROM   dbo.LessonProgress p
JOIN   #Keepers k ON k.Id = p.Id
CROSS APPLY (
    SELECT MinCompleted = MIN(s.CompletedAt),
           TotalViews   = SUM(s.ViewCount),
           MaxViewed    = MAX(s.LastViewedAt)
    FROM   dbo.LessonProgress s
    WHERE  s.LessonTopicId = p.LessonTopicId
      AND  s.UserKey IN (SELECT UserKey FROM @All)
) agg;

DELETE p
FROM   dbo.LessonProgress p
WHERE  p.UserKey IN (SELECT UserKey FROM @All)
  AND  p.Id NOT IN (SELECT Id FROM #Keepers);

UPDATE dbo.LessonProgress
SET    UserKey = @Target
WHERE  UserKey IN (SELECT UserKey FROM @Sources);

DROP TABLE #Keepers;

------------------------------------------------------------------------------------------------
-- Verify inside the transaction, and roll back rather than commit something surprising
------------------------------------------------------------------------------------------------

DECLARE @Sessions int = (SELECT COUNT(*) FROM dbo.ExamSessions   WHERE UserKey = @Target);
DECLARE @Lessons  int = (SELECT COUNT(*) FROM dbo.LessonProgress WHERE UserKey = @Target);

DECLARE @Orphans int =
      (SELECT COUNT(*) FROM dbo.ExamSessions   WHERE UserKey IN (SELECT UserKey FROM @Sources))
    + (SELECT COUNT(*) FROM dbo.LessonProgress WHERE UserKey IN (SELECT UserKey FROM @Sources));

DECLARE @Dupes int = (SELECT COUNT(*) FROM (
    SELECT LessonTopicId FROM dbo.LessonProgress
    WHERE UserKey = @Target GROUP BY LessonTopicId HAVING COUNT(*) > 1) d);

IF @Orphans <> 0 OR @Dupes <> 0 OR @Sessions <> @ExpectedSessions OR @Lessons <> @ExpectedLessons
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Merge verification failed (sessions %d expected %d, lessons %d expected %d, orphans %d, duplicate topics %d). Rolled back.',
              16, 1, @Sessions, @ExpectedSessions, @Lessons, @ExpectedLessons, @Orphans, @Dupes);
    RETURN;
END

COMMIT TRANSACTION;

PRINT '--- merged ---';
SELECT MergedInto = @Target, Sessions = @Sessions, LessonRows = @Lessons,
       Completed = (SELECT COUNT(*) FROM dbo.LessonProgress
                    WHERE UserKey = @Target AND CompletedAt IS NOT NULL);

------------------------------------------------------------------------------------------------
-- Optional: purge the test and fixture keys
--
-- Off by default and deliberately separate from the merge, so the two are independently
-- reversible. These are keys left behind by smoke tests, perf runs and bug repros, plus `local`
-- (the fallback the API used when no X-User-Key header arrived at all, which is why it holds a
-- whole curriculum's worth of lesson views). None of them can ever belong to an account.
--
-- ExamSessionQuestions goes with its parent: FK_ExamSessionQuestions_ExamSessions is ON DELETE
-- CASCADE, verified against the live database rather than assumed from the model.
------------------------------------------------------------------------------------------------

IF @PurgeFixtures = 1
BEGIN
    DECLARE @Fixtures TABLE (UserKey nvarchar(100) PRIMARY KEY);
    INSERT INTO @Fixtures (UserKey) VALUES
        (N'bugtest-0001'), (N'fix'), (N'flow-test'), (N'local'), (N'mltest'), (N'mltest2'),
        (N'nplus1'), (N'owner-a'), (N'perf-test-141'), (N'revealcheck'), (N'smoke'),
        (N'smoke-cqrs'), (N'spot'), (N't'), (N'test-lessons'), (N'tz-check-0001'),
        (N'userA'), (N'v');

    IF OBJECT_ID('dbo.LessonProgress_PreFixturePurge') IS NULL
        SELECT * INTO dbo.LessonProgress_PreFixturePurge
        FROM dbo.LessonProgress WHERE UserKey IN (SELECT UserKey FROM @Fixtures);

    IF OBJECT_ID('dbo.ExamSessions_PreFixturePurge') IS NULL
        SELECT * INTO dbo.ExamSessions_PreFixturePurge
        FROM dbo.ExamSessions WHERE UserKey IN (SELECT UserKey FROM @Fixtures);

    -- Backed up with the parents, because the cascade takes them and a restore would not.
    IF OBJECT_ID('dbo.ExamSessionQuestions_PreFixturePurge') IS NULL
        SELECT q.* INTO dbo.ExamSessionQuestions_PreFixturePurge
        FROM dbo.ExamSessionQuestions q
        JOIN dbo.ExamSessions e ON e.Id = q.ExamSessionId
        WHERE e.UserKey IN (SELECT UserKey FROM @Fixtures);

    BEGIN TRANSACTION;

    DELETE FROM dbo.LessonProgress WHERE UserKey IN (SELECT UserKey FROM @Fixtures);
    DELETE FROM dbo.ExamSessions   WHERE UserKey IN (SELECT UserKey FROM @Fixtures);

    -- Nothing outside the five real keys may survive.
    IF EXISTS (SELECT 1 FROM dbo.ExamSessions   WHERE UserKey IN (SELECT UserKey FROM @Fixtures))
       OR EXISTS (SELECT 1 FROM dbo.LessonProgress WHERE UserKey IN (SELECT UserKey FROM @Fixtures))
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('Fixture purge left rows behind. Rolled back.', 16, 1);
        RETURN;
    END

    COMMIT TRANSACTION;

    PRINT '--- fixture keys purged ---';
    SELECT RemainingKeys = COUNT(DISTINCT UserKey) FROM (
        SELECT UserKey FROM dbo.ExamSessions
        UNION SELECT UserKey FROM dbo.LessonProgress) k;
END
