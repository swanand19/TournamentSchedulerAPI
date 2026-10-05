-- Change who may call a service - until the admin portal exists (phase 5 of the plan).
--
-- Fill in the values below and run in SSMS on WJLP-3571\SUBSMANAGEMENTDB. The API picks the change
-- up within a minute (Auth:PermissionRefreshSeconds), no restart needed.
--
--   TICK     let @RoleId call it                UNTICK   stop @RoleId calling it
--   DISABLE  nobody may call it (any role)      ENABLE   undo a DISABLE
--
-- The same guards as the API (it enforces them too, whatever this table says):
--   - an admin-only service can't be ticked for EndUser (2);
--   - a protected service (signing out, your own account) can't be disabled or unticked for Admin (1).
-- Unticking keeps the row (IsDisabled = 1), so a later deploy knows an admin said no and leaves it.
--
-- Service ids are on the [ServiceRequestId] attributes, and listed by the last query below.

USE [TournamentSchedulerDB];
GO

DECLARE @ServiceRequestId nvarchar(100) = N'TOURNAMENT_DELETE';
DECLARE @Action           nvarchar(10)  = N'UNTICK';   -- TICK | UNTICK | DISABLE | ENABLE
DECLARE @RoleId           int           = 2;           -- 1 Admin, 2 EndUser (TICK / UNTICK only)
DECLARE @ByUserId         int           = 1;           -- recorded as LmdBy; 1 = System

SET NOCOUNT ON;
DECLARE @PermissionId int, @IsAdminOnly bit, @IsProtected bit, @Now datetime2 = SYSUTCDATETIME();

SELECT @PermissionId = PermissionId, @IsAdminOnly = IsAdminOnly, @IsProtected = IsProtected
FROM dbo.Permissions WHERE ServiceRequestId = @ServiceRequestId;

IF @Action NOT IN (N'TICK', N'UNTICK', N'DISABLE', N'ENABLE')
BEGIN
    RAISERROR(N'@Action must be TICK, UNTICK, DISABLE or ENABLE. Nothing was changed.', 16, 1);
    RETURN;
END

IF @PermissionId IS NULL
BEGIN
    RAISERROR(N'There is no service %s. The API adds services when it starts - check the id, or start the new API first. Nothing was changed.', 16, 1, @ServiceRequestId);
    RETURN;
END

IF @Action IN (N'TICK', N'UNTICK') AND NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleId = @RoleId)
BEGIN
    RAISERROR(N'There is no role %d. Nothing was changed.', 16, 1, @RoleId);
    RETURN;
END

IF @Action = N'TICK' AND @IsAdminOnly = 1 AND @RoleId = 2
BEGIN
    RAISERROR(N'%s is an admin-portal service; EndUser can never have it. Nothing was changed.', 16, 1, @ServiceRequestId);
    RETURN;
END

IF (@Action = N'UNTICK' AND @IsProtected = 1 AND @RoleId = 1) OR (@Action = N'DISABLE' AND @IsProtected = 1)
BEGIN
    RAISERROR(N'%s is protected (switching it off would lock people out). Nothing was changed.', 16, 1, @ServiceRequestId);
    RETURN;
END

IF @Action IN (N'TICK', N'UNTICK')
BEGIN
    DECLARE @Off bit = CASE WHEN @Action = N'UNTICK' THEN 1 ELSE 0 END;

    IF EXISTS (SELECT 1 FROM dbo.RolePermissions WHERE RoleId = @RoleId AND PermissionId = @PermissionId)
        UPDATE dbo.RolePermissions SET IsDisabled = @Off, Lmd = @Now, LmdBy = @ByUserId
        WHERE RoleId = @RoleId AND PermissionId = @PermissionId;
    ELSE
        INSERT dbo.RolePermissions (RoleId, PermissionId, Crd, CrdBy, Lmd, LmdBy, IsDisabled)
        VALUES (@RoleId, @PermissionId, @Now, @ByUserId, @Now, @ByUserId, @Off);

    PRINT CONCAT(@Action, N': ', @ServiceRequestId, N' for role ', @RoleId, N'.');
END
ELSE
BEGIN
    UPDATE dbo.Permissions SET IsDisabled = CASE WHEN @Action = N'DISABLE' THEN 1 ELSE 0 END, Lmd = @Now, LmdBy = @ByUserId
    WHERE PermissionId = @PermissionId;
    PRINT CONCAT(@Action, N': ', @ServiceRequestId, N' for everyone.');
END

-- Where this service stands now.
SELECT p.ServiceRequestId, p.ControllerName, p.ActionName, p.IsDisabled, p.IsObsolete, p.IsAdminOnly, p.IsProtected,
       STUFF((SELECT N', ' + r.RoleName FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.RoleId = rp.RoleId
              WHERE rp.PermissionId = p.PermissionId AND rp.IsDisabled = 0 ORDER BY r.RoleId FOR XML PATH('')), 1, 2, N'') AS TickedFor
FROM dbo.Permissions p WHERE p.PermissionId = @PermissionId;
GO

-- Every service and who has it (run on its own to browse).
SELECT p.ServiceRequestId, p.ControllerName, p.ActionName, p.HttpMethod, p.URL,
       p.IsAnonymous, p.IsAdminOnly, p.IsProtected, p.IsDisabled, p.IsObsolete,
       STUFF((SELECT N', ' + r.RoleName FROM dbo.RolePermissions rp JOIN dbo.Roles r ON r.RoleId = rp.RoleId
              WHERE rp.PermissionId = p.PermissionId AND rp.IsDisabled = 0 ORDER BY r.RoleId FOR XML PATH('')), 1, 2, N'') AS TickedFor
FROM dbo.Permissions p
ORDER BY p.ControllerName, p.ServiceRequestId;
GO
