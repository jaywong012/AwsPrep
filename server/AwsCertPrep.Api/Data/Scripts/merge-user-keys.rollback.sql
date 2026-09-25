/*
    merge-user-keys.rollback.sql — undo merge-user-keys.sql from the tables it backed up.

    Run only if the merge produced something wrong. It restores the five browser keys exactly as
    they were, which is only useful before anyone has registered an account and adopted the merged
    key — after that, use adopt-merged-progress.sql in reverse or restore the .bak.

    THE TWO TABLES NEED DIFFERENT INVERSES, and getting this backwards loses data:

    ExamSessions are RE-KEYED by the merge, never deleted, so the inverse is an UPDATE by Id.
    Deleting and re-inserting them would cascade ExamSessionQuestions away
    (FK_ExamSessionQuestions_ExamSessions is ON DELETE CASCADE) and the backup does not hold the
    children — every answer in every exam would be gone.

    LessonProgress rows ARE deleted by the fold, so they must be re-inserted. Id is an identity
    int, so IDENTITY_INSERT and an explicit column list are both mandatory.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.LessonProgress_PreMerge') IS NULL OR OBJECT_ID('dbo.ExamSessions_PreMerge') IS NULL
BEGIN
    RAISERROR('Backup tables are missing. Nothing to roll back from.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

-- Exam sessions: put each row's original key back, matched by Id. No delete, no cascade.
UPDATE e
SET    e.UserKey = b.UserKey
FROM   dbo.ExamSessions e
JOIN   dbo.ExamSessions_PreMerge b ON b.Id = e.Id;

-- Lesson progress: the fold deleted rows and overwrote the keepers, so restore wholesale.
DELETE FROM dbo.LessonProgress
WHERE UserKey IN (SELECT UserKey FROM dbo.LessonProgress_PreMerge);

SET IDENTITY_INSERT dbo.LessonProgress ON;

INSERT INTO dbo.LessonProgress (Id, LessonTopicId, UserKey, CompletedAt, LastViewedAt, ViewCount)
SELECT Id, LessonTopicId, UserKey, CompletedAt, LastViewedAt, ViewCount
FROM   dbo.LessonProgress_PreMerge;

SET IDENTITY_INSERT dbo.LessonProgress OFF;

COMMIT TRANSACTION;

PRINT '--- rolled back ---';
SELECT UserKey,
       Sessions = (SELECT COUNT(*) FROM dbo.ExamSessions   e WHERE e.UserKey = k.UserKey),
       Lessons  = (SELECT COUNT(*) FROM dbo.LessonProgress p WHERE p.UserKey = k.UserKey)
FROM (SELECT DISTINCT UserKey FROM dbo.ExamSessions_PreMerge
      UNION SELECT DISTINCT UserKey FROM dbo.LessonProgress_PreMerge) k
ORDER BY UserKey;

-- The backup tables are left in place deliberately: dropping them would make a second rollback
-- impossible, and they are small. Drop them by hand once the merge is settled:
--   DROP TABLE dbo.LessonProgress_PreMerge, dbo.ExamSessions_PreMerge;
