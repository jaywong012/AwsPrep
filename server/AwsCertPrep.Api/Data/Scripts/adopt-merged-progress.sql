/*
    adopt-merged-progress.sql — hand the merged anonymous progress to a real account.

    Run this ONCE, after registering the account that should own the history that
    merge-user-keys.sql consolidated. Until then the rows sit under the old browser key and no
    account can see them.

    HOW TO RUN
        1. Register your account in the app (or POST /api/auth/register).
        2. Put that email in @Email below.
        3. sqlcmd -S localhost -d AwsCertPrep -E -C -i adopt-merged-progress.sql

    IDEMPOTENT: a second run matches zero rows, because the first one left nothing under @OldKey.

    SAFE AGAINST THE UNIQUE INDEX: merge-user-keys.sql already guaranteed one LessonProgress row
    per topic under @OldKey, and the assertion below refuses to run if the target account has any
    rows of its own - so IX_LessonProgress_UserKey_LessonTopicId cannot be violated by this.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Email nvarchar(256) = N'hoang.do@saigontechnology.com';
DECLARE @OldKey nvarchar(100) = N'user-daa12a67';

DECLARE @NewKey nvarchar(100) = (
    SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = UPPER(@Email));

IF @NewKey IS NULL
BEGIN
    RAISERROR('No account found for %s. Register it first, then re-run.', 16, 1, @Email);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.LessonProgress WHERE UserKey = @OldKey)
   AND NOT EXISTS (SELECT 1 FROM dbo.ExamSessions WHERE UserKey = @OldKey)
BEGIN
    PRINT 'Nothing left under the old key - already adopted.';
    RETURN;
END

-- Adopting into an account that has already studied something could collide on the unique index,
-- and would mix two histories with no way to tell them apart afterwards. Refuse instead.
IF EXISTS (SELECT 1 FROM dbo.LessonProgress WHERE UserKey = @NewKey)
   OR EXISTS (SELECT 1 FROM dbo.ExamSessions WHERE UserKey = @NewKey)
BEGIN
    RAISERROR('That account already owns progress. Adopt into a fresh account, or merge by hand.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

UPDATE dbo.ExamSessions   SET UserKey = @NewKey WHERE UserKey = @OldKey;
UPDATE dbo.LessonProgress SET UserKey = @NewKey WHERE UserKey = @OldKey;

IF EXISTS (SELECT 1 FROM dbo.LessonProgress WHERE UserKey = @OldKey)
   OR EXISTS (SELECT 1 FROM dbo.ExamSessions WHERE UserKey = @OldKey)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Rows remain under the old key. Rolled back.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

PRINT '--- adopted ---';
SELECT Account   = @Email,
       Sessions  = (SELECT COUNT(*) FROM dbo.ExamSessions   WHERE UserKey = @NewKey),
       Lessons   = (SELECT COUNT(*) FROM dbo.LessonProgress WHERE UserKey = @NewKey),
       Completed = (SELECT COUNT(*) FROM dbo.LessonProgress
                    WHERE UserKey = @NewKey AND CompletedAt IS NOT NULL);
