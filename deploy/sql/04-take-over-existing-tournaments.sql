-- Makes you an owner of every tournament that still belongs to the System user - the ones created
-- before accounts existed (2026-10-01). Until then they show on nobody's home page.
--
-- 1. Sign in to the app once with your email (that creates your account).
-- 2. Put that email below and run this in SSMS on WJLP-3571\SUBSMANAGEMENTDB.
-- Safe to run again; run it with someone else's email to share them too.
--
-- You become an owner, not the creator: the creator stays System, so nobody can delete these
-- tournaments from the apps (deleting is the creator's alone). Delete one in SSMS if you need to.

USE [TournamentSchedulerDB];
GO

DECLARE @Email nvarchar(256) = N'you@example.com';

SET NOCOUNT ON;
DECLARE @UserId int = (SELECT UserId FROM dbo.Users WHERE EmailId = LOWER(LTRIM(RTRIM(@Email))) AND DeletedOn IS NULL);

IF @UserId IS NULL
BEGIN
    RAISERROR(N'There is no account for %s. Sign in to the app with it once first. Nothing was changed.', 16, 1, @Email);
    RETURN;
END

DECLARE @Now datetime2 = SYSUTCDATETIME();

-- Already a scorer somewhere: becomes an owner there.
UPDATE m SET MemberRole = N'Owner', IsDisabled = 0, Lmd = @Now, LmdBy = 1
FROM dbo.TournamentMembers m
JOIN dbo.Tournaments t ON t.Id = m.TournamentId
WHERE m.UserId = @UserId AND t.CreatedByUserId = 1 AND (m.MemberRole <> N'Owner' OR m.IsDisabled = 1);

INSERT dbo.TournamentMembers (TournamentId, UserId, MemberRole, Crd, CrdBy, Lmd, LmdBy, IsDisabled)
SELECT t.Id, @UserId, N'Owner', @Now, 1, @Now, 1, 0
FROM dbo.Tournaments t
WHERE t.CreatedByUserId = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.TournamentMembers m WHERE m.TournamentId = t.Id AND m.UserId = @UserId);

PRINT CONCAT(@@ROWCOUNT, N' tournament(s) added for ', @Email, N'.');

SELECT t.Id, t.Name, t.Sport, t.Status, m.MemberRole AS YourRole
FROM dbo.Tournaments t
JOIN dbo.TournamentMembers m ON m.TournamentId = t.Id AND m.UserId = @UserId
WHERE t.CreatedByUserId = 1
ORDER BY t.Id;
GO
