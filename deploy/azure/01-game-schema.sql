-- Production (Azure SQL): every game table, from nothing. Run FIRST, on the new empty database, in SSMS
-- connected as the server's Microsoft Entra admin (Authentication: "Microsoft Entra MFA"), with the
-- database itself selected in the toolbar (Azure SQL has no USE). Then 02-log-schema.sql, then
-- 03-app-identity.sql.
--
-- Safe to run again: every step checks __EFMigrationsHistory first. Later releases ship the same way: a
-- script generated from the new migrations only (see README.md, "Database changes").
--
-- The fresh database gets the two roles and the System user from the migrations; the message wording is
-- added by the API the first time it needs it, permissions at start-up. None of deploy/sql/01-24 are needed here:
-- those upgraded the laptop's existing data.
--
-- Generated from the EF migrations - don't edit by hand; regenerate with
--   dotnet ef migrations script --context TournamentDbContext --idempotent --output deploy/azure/01-game-schema.raw.sql
-- and put this header back on top.

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE TABLE [Tournaments] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Tournaments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE TABLE [SavedSchedules] (
        [Id] int NOT NULL IDENTITY,
        [CreatedAt] datetime2 NOT NULL,
        [MatchesPerTeam] int NOT NULL,
        [TotalMatches] int NOT NULL,
        [IsActive] bit NOT NULL,
        [TournamentId] int NOT NULL,
        CONSTRAINT [PK_SavedSchedules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SavedSchedules_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE TABLE [Teams] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [TournamentId] int NOT NULL,
        CONSTRAINT [PK_Teams] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Teams_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE TABLE [SavedGroups] (
        [Id] int NOT NULL IDENTITY,
        [GroupName] nvarchar(max) NOT NULL,
        [SavedScheduleId] int NOT NULL,
        CONSTRAINT [PK_SavedGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SavedGroups_SavedSchedules_SavedScheduleId] FOREIGN KEY ([SavedScheduleId]) REFERENCES [SavedSchedules] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE TABLE [Players] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(max) NOT NULL,
        [Position] nvarchar(max) NULL,
        [JerseyNumber] int NULL,
        [TeamId] int NOT NULL,
        CONSTRAINT [PK_Players] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Players_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE TABLE [SavedFixtures] (
        [Id] int NOT NULL IDENTITY,
        [MatchNumber] int NOT NULL,
        [Home] nvarchar(max) NOT NULL,
        [Away] nvarchar(max) NOT NULL,
        [HomeTeamId] int NULL,
        [AwayTeamId] int NULL,
        [SavedGroupId] int NOT NULL,
        CONSTRAINT [PK_SavedFixtures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SavedFixtures_SavedGroups_SavedGroupId] FOREIGN KEY ([SavedGroupId]) REFERENCES [SavedGroups] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Players_TeamId] ON [Players] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedFixtures_SavedGroupId] ON [SavedFixtures] ([SavedGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedGroups_SavedScheduleId] ON [SavedGroups] ([SavedScheduleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedSchedules_TournamentId] ON [SavedSchedules] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Teams_TournamentId_Name] ON [Teams] ([TournamentId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717092641_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260717092641_InitialCreate', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717102636_UniqueJerseyNumberPerTeam'
)
BEGIN
    DROP INDEX [IX_Players_TeamId] ON [Players];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717102636_UniqueJerseyNumberPerTeam'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Players_TeamId_JerseyNumber] ON [Players] ([TeamId], [JerseyNumber]) WHERE [JerseyNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717102636_UniqueJerseyNumberPerTeam'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260717102636_UniqueJerseyNumberPerTeam', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [IsStarted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [StartedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE TABLE [Matches] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [SavedFixtureId] int NOT NULL,
        [GroupName] nvarchar(max) NOT NULL,
        [MatchNumber] int NOT NULL,
        [HomeTeamId] int NULL,
        [AwayTeamId] int NULL,
        [HomeTeamName] nvarchar(max) NOT NULL,
        [AwayTeamName] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [MaxPlayersPerSide] int NULL,
        [MinutesPerHalf] int NULL,
        [ExtraTimeAllowed] bit NOT NULL,
        [DrawAllowed] bit NOT NULL,
        [MaxSubstitutions] int NULL,
        [RollingSubsAllowed] bit NOT NULL,
        [CurrentMinute] int NOT NULL,
        [CurrentHalf] int NOT NULL,
        [IsClockRunning] bit NOT NULL,
        [HomeScore] int NOT NULL,
        [AwayScore] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_Matches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Matches_SavedFixtures_SavedFixtureId] FOREIGN KEY ([SavedFixtureId]) REFERENCES [SavedFixtures] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Matches_Teams_AwayTeamId] FOREIGN KEY ([AwayTeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Matches_Teams_HomeTeamId] FOREIGN KEY ([HomeTeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Matches_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE TABLE [MatchEvents] (
        [Id] int NOT NULL IDENTITY,
        [MatchId] int NOT NULL,
        [EventType] int NOT NULL,
        [MinuteOfMatch] int NOT NULL,
        [TeamId] int NULL,
        [PlayerId] int NULL,
        [RelatedPlayerId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_MatchEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MatchEvents_Matches_MatchId] FOREIGN KEY ([MatchId]) REFERENCES [Matches] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE TABLE [MatchPlayers] (
        [Id] int NOT NULL IDENTITY,
        [MatchId] int NOT NULL,
        [PlayerId] int NOT NULL,
        [TeamId] int NOT NULL,
        [SquadStatus] int NOT NULL,
        CONSTRAINT [PK_MatchPlayers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MatchPlayers_Matches_MatchId] FOREIGN KEY ([MatchId]) REFERENCES [Matches] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MatchPlayers_Players_PlayerId] FOREIGN KEY ([PlayerId]) REFERENCES [Players] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MatchPlayers_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_Matches_AwayTeamId] ON [Matches] ([AwayTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_Matches_HomeTeamId] ON [Matches] ([HomeTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_Matches_SavedFixtureId] ON [Matches] ([SavedFixtureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_Matches_TournamentId] ON [Matches] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_MatchEvents_MatchId] ON [MatchEvents] ([MatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_MatchPlayers_MatchId] ON [MatchPlayers] ([MatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_MatchPlayers_PlayerId] ON [MatchPlayers] ([PlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    CREATE INDEX [IX_MatchPlayers_TeamId] ON [MatchPlayers] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717122819_AddMatchModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260717122819_AddMatchModule', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720090729_StartMatchModule'
)
BEGIN
    ALTER TABLE [Matches] ADD [PausedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720090729_StartMatchModule'
)
BEGIN
    ALTER TABLE [Matches] ADD [PausedDurationMs] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720090729_StartMatchModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260720090729_StartMatchModule', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720104014_PropertyAddedInMatchModel'
)
BEGIN
    ALTER TABLE [Matches] ADD [ExtraMinutesAddedThisHalf] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720104014_PropertyAddedInMatchModel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260720104014_PropertyAddedInMatchModel', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722110623_AddHalfStartedAtAndSubbedOffStatus'
)
BEGIN
    ALTER TABLE [Matches] ADD [HalfStartedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722110623_AddHalfStartedAtAndSubbedOffStatus'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722110623_AddHalfStartedAtAndSubbedOffStatus', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722114631_AddSentOffSquadStatus'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722114631_AddSentOffSquadStatus', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723073902_AddStoppageMinuteToMatchEvent'
)
BEGIN
    ALTER TABLE [MatchEvents] ADD [StoppageMinute] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723073902_AddStoppageMinuteToMatchEvent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723073902_AddStoppageMinuteToMatchEvent', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723100521_AddExtraTimeMinutesPerHalf'
)
BEGIN
    ALTER TABLE [Matches] ADD [ExtraTimeMinutesPerHalf] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723100521_AddExtraTimeMinutesPerHalf'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723100521_AddExtraTimeMinutesPerHalf', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [MatchEvents] ADD [PenaltyAwayScoreAfter] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [MatchEvents] ADD [PenaltyHomeScoreAfter] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [MatchEvents] ADD [PenaltyScored] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [Matches] ADD [FinalWhistleMinute] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [Matches] ADD [IsPenaltyShootout] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [Matches] ADD [PenaltyTakersPerSide] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    ALTER TABLE [Matches] ADD [PenaltyWinnerTeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    CREATE TABLE [PenaltyKicks] (
        [Id] int NOT NULL IDENTITY,
        [MatchId] int NOT NULL,
        [TeamId] int NOT NULL,
        [PlayerId] int NULL,
        [RoundNumber] int NOT NULL,
        [IsSuddenDeath] bit NOT NULL,
        [Scored] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PenaltyKicks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PenaltyKicks_Matches_MatchId] FOREIGN KEY ([MatchId]) REFERENCES [Matches] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    CREATE INDEX [IX_PenaltyKicks_MatchId] ON [PenaltyKicks] ([MatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724120508_AddPenaltyTimelineFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724120508_AddPenaltyTimelineFields', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726055256_AddPenaltyScoreToMatch'
)
BEGIN
    ALTER TABLE [Matches] ADD [PenaltyAwayScore] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726055256_AddPenaltyScoreToMatch'
)
BEGIN
    ALTER TABLE [Matches] ADD [PenaltyHomeScore] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726055256_AddPenaltyScoreToMatch'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726055256_AddPenaltyScoreToMatch', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726081641_AddForfeitWinner'
)
BEGIN
    ALTER TABLE [Matches] ADD [ForfeitWinnerTeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726081641_AddForfeitWinner'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726081641_AddForfeitWinner', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104841_AddPeriodStateAndFixtureRound'
)
BEGIN
    ALTER TABLE [SavedFixtures] ADD [Round] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104841_AddPeriodStateAndFixtureRound'
)
BEGIN
    ALTER TABLE [Matches] ADD [MinPlayersPerSide] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104841_AddPeriodStateAndFixtureRound'
)
BEGIN
    ALTER TABLE [Matches] ADD [PeriodState] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104841_AddPeriodStateAndFixtureRound'
)
BEGIN

                    EXEC(N'UPDATE [Matches]
                    SET [PeriodState] = CASE [Status]
                        WHEN 1 THEN 1
                        WHEN 2 THEN 2
                        WHEN 3 THEN 3
                        WHEN 4 THEN 3
                        ELSE 0
                    END;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104841_AddPeriodStateAndFixtureRound'
)
BEGIN

                    EXEC(N'UPDATE [Matches]
                    SET [MinPlayersPerSide] = 1
                    WHERE [Status] <> 0 AND [MinPlayersPerSide] IS NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104841_AddPeriodStateAndFixtureRound'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824104841_AddPeriodStateAndFixtureRound', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824125417_AddStartedMatchToMatchPlayer'
)
BEGIN
    ALTER TABLE [MatchPlayers] ADD [StartedMatch] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824125417_AddStartedMatchToMatchPlayer'
)
BEGIN

                    EXEC(N'UPDATE mp
                    SET mp.[StartedMatch] = 1
                    FROM [MatchPlayers] mp
                    WHERE mp.[SquadStatus] IN (0, 3, 4)
                      AND NOT EXISTS (
                          SELECT 1 FROM [MatchEvents] e
                          WHERE e.[MatchId] = mp.[MatchId]
                            AND e.[EventType] = 3
                            AND e.[RelatedPlayerId] = mp.[PlayerId]);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824125417_AddStartedMatchToMatchPlayer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824125417_AddStartedMatchToMatchPlayer', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901093715_AddSportToTournament'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [Sport] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901093715_AddSportToTournament'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901093715_AddSportToTournament', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902090852_AddCricketMatches'
)
BEGIN
    CREATE TABLE [CricketMatches] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [SavedFixtureId] int NOT NULL,
        [GroupName] nvarchar(max) NOT NULL,
        [MatchNumber] int NOT NULL,
        [HomeTeamId] int NULL,
        [AwayTeamId] int NULL,
        [HomeTeamName] nvarchar(max) NOT NULL,
        [AwayTeamName] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [Rules_Format] int NOT NULL,
        [Rules_InningsPerSide] int NOT NULL,
        [Rules_OversPerInnings] int NULL,
        [Rules_BallsPerOver] int NOT NULL,
        [Rules_MaxOversPerBowler] int NULL,
        [Rules_PlayersPerSide] int NOT NULL,
        [Rules_LastManStanding] bit NOT NULL,
        [Rules_WidePenaltyRuns] int NOT NULL,
        [Rules_NoBallPenaltyRuns] int NOT NULL,
        [Rules_FreeHitAfterNoBall] bit NOT NULL,
        [Rules_FreeHitAfterWide] bit NOT NULL,
        [Rules_ByesAllowed] bit NOT NULL,
        [Rules_LegByesAllowed] bit NOT NULL,
        [Rules_PenaltyRunsAllowed] bit NOT NULL,
        [Rules_OverthrowsAllowed] bit NOT NULL,
        [Rules_LbwEnabled] bit NOT NULL,
        [Rules_PowerplayOvers] nvarchar(max) NULL,
        [Rules_TieResolution] int NOT NULL,
        [Rules_DrawAllowed] bit NOT NULL,
        [Rules_FollowOnMargin] int NULL,
        [Rules_DlsEnabled] bit NOT NULL,
        [Rules_BallType] int NOT NULL,
        [Rules_PitchType] int NOT NULL,
        [TossWinnerTeamId] int NULL,
        [TossDecision] int NULL,
        [CurrentInningsNumber] int NOT NULL,
        [WinnerTeamId] int NULL,
        [IsTie] bit NOT NULL,
        [IsNoResult] bit NOT NULL,
        [IsDraw] bit NOT NULL,
        [WonByDls] bit NOT NULL,
        [ForfeitWinnerTeamId] int NULL,
        [ResultSummary] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_CricketMatches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CricketMatches_SavedFixtures_SavedFixtureId] FOREIGN KEY ([SavedFixtureId]) REFERENCES [SavedFixtures] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CricketMatches_Teams_AwayTeamId] FOREIGN KEY ([AwayTeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CricketMatches_Teams_HomeTeamId] FOREIGN KEY ([HomeTeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CricketMatches_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902090852_AddCricketMatches'
)
BEGIN
    CREATE INDEX [IX_CricketMatches_AwayTeamId] ON [CricketMatches] ([AwayTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902090852_AddCricketMatches'
)
BEGIN
    CREATE INDEX [IX_CricketMatches_HomeTeamId] ON [CricketMatches] ([HomeTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902090852_AddCricketMatches'
)
BEGIN
    CREATE INDEX [IX_CricketMatches_SavedFixtureId] ON [CricketMatches] ([SavedFixtureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902090852_AddCricketMatches'
)
BEGIN
    CREATE INDEX [IX_CricketMatches_TournamentId] ON [CricketMatches] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902090852_AddCricketMatches'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902090852_AddCricketMatches', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923123220_AddCricketPlayerProfile'
)
BEGIN
    ALTER TABLE [Teams] ADD [DefaultCaptainPlayerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923123220_AddCricketPlayerProfile'
)
BEGIN
    ALTER TABLE [Players] ADD [PersonId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923123220_AddCricketPlayerProfile'
)
BEGIN
    CREATE TABLE [PlayerCricketProfiles] (
        [PlayerId] int NOT NULL,
        [PrimaryRole] int NOT NULL,
        [BattingStyle] int NULL,
        [BowlingArm] int NULL,
        [BowlingType] int NULL,
        [BattingOrderPreference] int NULL,
        CONSTRAINT [PK_PlayerCricketProfiles] PRIMARY KEY ([PlayerId]),
        CONSTRAINT [FK_PlayerCricketProfiles_Players_PlayerId] FOREIGN KEY ([PlayerId]) REFERENCES [Players] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923123220_AddCricketPlayerProfile'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923123220_AddCricketPlayerProfile', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [FollowOnEnforced] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE TABLE [CricketInnings] (
        [Id] int NOT NULL IDENTITY,
        [CricketMatchId] int NOT NULL,
        [InningsNumber] int NOT NULL,
        [BattingTeamId] int NOT NULL,
        [BowlingTeamId] int NOT NULL,
        [BattingTeamName] nvarchar(max) NOT NULL,
        [BowlingTeamName] nvarchar(max) NOT NULL,
        [BattingTeamInningsIndex] int NOT NULL,
        [Status] int NOT NULL,
        [EndReason] int NULL,
        [BallsPerOver] int NOT NULL,
        [OversLimit] int NULL,
        [WicketsToEndInnings] int NOT NULL,
        [BattingSideSize] int NOT NULL,
        [MaxOversPerBowler] int NULL,
        [Runs] int NOT NULL,
        [Wickets] int NOT NULL,
        [LegalBalls] int NOT NULL,
        [Wides] int NOT NULL,
        [NoBalls] int NOT NULL,
        [Byes] int NOT NULL,
        [LegByes] int NOT NULL,
        [PenaltyRuns] int NOT NULL,
        [OpeningStrikerId] int NULL,
        [OpeningNonStrikerId] int NULL,
        [OpeningBowlerId] int NULL,
        [StrikerId] int NULL,
        [NonStrikerId] int NULL,
        [CurrentBowlerId] int NULL,
        [PreviousBowlerId] int NULL,
        [FreeHitPending] bit NOT NULL,
        [LoneBatter] bit NOT NULL,
        [Target] int NULL,
        [IsFollowOn] bit NOT NULL,
        [IsSuperOver] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_CricketInnings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CricketInnings_CricketMatches_CricketMatchId] FOREIGN KEY ([CricketMatchId]) REFERENCES [CricketMatches] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE TABLE [CricketMatchEvents] (
        [Id] int NOT NULL IDENTITY,
        [CricketMatchId] int NOT NULL,
        [EventType] int NOT NULL,
        [InningsNumber] int NULL,
        [TeamId] int NULL,
        [PlayerId] int NULL,
        [Description] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CricketMatchEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CricketMatchEvents_CricketMatches_CricketMatchId] FOREIGN KEY ([CricketMatchId]) REFERENCES [CricketMatches] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE TABLE [CricketMatchPlayers] (
        [Id] int NOT NULL IDENTITY,
        [CricketMatchId] int NOT NULL,
        [PlayerId] int NOT NULL,
        [TeamId] int NOT NULL,
        [PlayerName] nvarchar(max) NOT NULL,
        [SquadStatus] int NOT NULL,
        [IsCaptain] bit NOT NULL,
        [IsWicketKeeper] bit NOT NULL,
        [BattingOrder] int NULL,
        CONSTRAINT [PK_CricketMatchPlayers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CricketMatchPlayers_CricketMatches_CricketMatchId] FOREIGN KEY ([CricketMatchId]) REFERENCES [CricketMatches] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CricketMatchPlayers_Players_PlayerId] FOREIGN KEY ([PlayerId]) REFERENCES [Players] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CricketMatchPlayers_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE TABLE [CricketBalls] (
        [Id] int NOT NULL IDENTITY,
        [InningsId] int NOT NULL,
        [SequenceNumber] int NOT NULL,
        [OverNumber] int NOT NULL,
        [BallInOver] int NOT NULL,
        [BowlerId] int NOT NULL,
        [StrikerId] int NOT NULL,
        [NonStrikerId] int NULL,
        [RunsOffBat] int NOT NULL,
        [IsWide] bit NOT NULL,
        [IsNoBall] bit NOT NULL,
        [WideExtraRuns] int NOT NULL,
        [Byes] int NOT NULL,
        [LegByes] int NOT NULL,
        [PenaltyRuns] int NOT NULL,
        [WidePenalty] int NOT NULL,
        [NoBallPenalty] int NOT NULL,
        [IsFreeHit] bit NOT NULL,
        [WicketType] int NULL,
        [DismissedPlayerId] int NULL,
        [FielderId] int NULL,
        [BattersCrossed] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CricketBalls] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CricketBalls_CricketInnings_InningsId] FOREIGN KEY ([InningsId]) REFERENCES [CricketInnings] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CricketBalls_InningsId_SequenceNumber] ON [CricketBalls] ([InningsId], [SequenceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CricketInnings_CricketMatchId_InningsNumber] ON [CricketInnings] ([CricketMatchId], [InningsNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE INDEX [IX_CricketMatchEvents_CricketMatchId] ON [CricketMatchEvents] ([CricketMatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CricketMatchPlayers_CricketMatchId_PlayerId] ON [CricketMatchPlayers] ([CricketMatchId], [PlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE INDEX [IX_CricketMatchPlayers_PlayerId] ON [CricketMatchPlayers] ([PlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    CREATE INDEX [IX_CricketMatchPlayers_TeamId] ON [CricketMatchPlayers] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923125708_AddCricketScoringModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923125708_AddCricketScoringModule', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924104618_AddDlsInterruptionsAndRunOutCredit'
)
BEGIN
    ALTER TABLE [CricketBalls] ADD [IsDirectHit] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924104618_AddDlsInterruptionsAndRunOutCredit'
)
BEGIN
    ALTER TABLE [CricketBalls] ADD [RunOutReceiverId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924104618_AddDlsInterruptionsAndRunOutCredit'
)
BEGIN
    CREATE TABLE [CricketInterruptions] (
        [Id] int NOT NULL IDENTITY,
        [InningsId] int NOT NULL,
        [AtLegalBalls] int NOT NULL,
        [WicketsAtTime] int NOT NULL,
        [BallsAllowedBefore] int NOT NULL,
        [BallsAllowedAfter] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CricketInterruptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CricketInterruptions_CricketInnings_InningsId] FOREIGN KEY ([InningsId]) REFERENCES [CricketInnings] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924104618_AddDlsInterruptionsAndRunOutCredit'
)
BEGIN
    CREATE INDEX [IX_CricketInterruptions_InningsId] ON [CricketInterruptions] ([InningsId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924104618_AddDlsInterruptionsAndRunOutCredit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924104618_AddDlsInterruptionsAndRunOutCredit', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924160123_NewChanges_CricketStats'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924160123_NewChanges_CricketStats', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [OtpChallenges] (
        [OtpChallengeId] bigint NOT NULL IDENTITY,
        [Purpose] nvarchar(30) NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [Target] nvarchar(256) NOT NULL,
        [CodeHash] varchar(64) NOT NULL,
        [Attempts] int NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        [DeviceId] nvarchar(100) NULL,
        [ClientIp] nvarchar(64) NULL,
        [Crd] datetime2 NOT NULL,
        CONSTRAINT [PK_OtpChallenges] PRIMARY KEY ([OtpChallengeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [Roles] (
        [RoleId] int NOT NULL IDENTITY,
        [RoleName] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([RoleId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [Users] (
        [UserId] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [EmailId] nvarchar(256) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [EmailVerifiedAt] datetime2 NULL,
        [RoleId] int NOT NULL,
        [LastLoginAt] datetime2 NULL,
        [DeletedOn] datetime2 NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE TABLE [UserSessions] (
        [SessionId] uniqueidentifier NOT NULL,
        [UserId] int NOT NULL,
        [TokenHash] varchar(64) NOT NULL,
        [Channel] nvarchar(30) NULL,
        [DeviceId] nvarchar(100) NULL,
        [Crd] datetime2 NOT NULL,
        [LastSeenAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedReason] nvarchar(100) NULL,
        CONSTRAINT [PK_UserSessions] PRIMARY KEY ([SessionId]),
        CONSTRAINT [FK_UserSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'Crd', N'CrdBy', N'Description', N'IsDisabled', N'Lmd', N'LmdBy', N'RoleName') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] ON;
    EXEC(N'INSERT INTO [Roles] ([RoleId], [Crd], [CrdBy], [Description], [IsDisabled], [Lmd], [LmdBy], [RoleName])
    VALUES (1, ''2026-10-01T00:00:00.0000000Z'', 1, N''Runs the admin portal.'', CAST(0 AS bit), ''2026-10-01T00:00:00.0000000Z'', 1, N''Admin''),
    (2, ''2026-10-01T00:00:00.0000000Z'', 1, N''Everyone who signs up in the apps.'', CAST(0 AS bit), ''2026-10-01T00:00:00.0000000Z'', 1, N''EndUser'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'RoleId', N'Crd', N'CrdBy', N'Description', N'IsDisabled', N'Lmd', N'LmdBy', N'RoleName') AND [object_id] = OBJECT_ID(N'[Roles]'))
        SET IDENTITY_INSERT [Roles] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'UserId', N'Crd', N'CrdBy', N'DeletedOn', N'EmailId', N'EmailVerifiedAt', N'IsDisabled', N'LastLoginAt', N'Lmd', N'LmdBy', N'Name', N'PhoneNumber', N'RoleId') AND [object_id] = OBJECT_ID(N'[Users]'))
        SET IDENTITY_INSERT [Users] ON;
    EXEC(N'INSERT INTO [Users] ([UserId], [Crd], [CrdBy], [DeletedOn], [EmailId], [EmailVerifiedAt], [IsDisabled], [LastLoginAt], [Lmd], [LmdBy], [Name], [PhoneNumber], [RoleId])
    VALUES (1, ''2026-10-01T00:00:00.0000000Z'', 1, NULL, NULL, NULL, CAST(0 AS bit), NULL, ''2026-10-01T00:00:00.0000000Z'', 1, N''System'', NULL, 1)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'UserId', N'Crd', N'CrdBy', N'DeletedOn', N'EmailId', N'EmailVerifiedAt', N'IsDisabled', N'LastLoginAt', N'Lmd', N'LmdBy', N'Name', N'PhoneNumber', N'RoleId') AND [object_id] = OBJECT_ID(N'[Users]'))
        SET IDENTITY_INSERT [Users] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_OtpChallenges_ClientIp_Crd] ON [OtpChallenges] ([ClientIp], [Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_OtpChallenges_DeviceId_Crd] ON [OtpChallenges] ([DeviceId], [Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_OtpChallenges_Target_Crd] ON [OtpChallenges] ([Target], [Crd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_RoleName] ON [Roles] ([RoleName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_EmailId] ON [Users] ([EmailId]) WHERE [EmailId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_PhoneNumber] ON [Users] ([PhoneNumber]) WHERE [PhoneNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserSessions_TokenHash] ON [UserSessions] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    CREATE INDEX [IX_UserSessions_UserId] ON [UserSessions] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930184354_AddAccounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260930184354_AddAccounts', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE TABLE [Permissions] (
        [PermissionId] int NOT NULL IDENTITY,
        [ServiceRequestId] nvarchar(100) NOT NULL,
        [ControllerName] nvarchar(100) NOT NULL,
        [ActionName] nvarchar(100) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [URL] nvarchar(300) NOT NULL,
        [IsAnonymous] bit NOT NULL,
        [IsAdminOnly] bit NOT NULL,
        [IsProtected] bit NOT NULL,
        [IsObsolete] bit NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([PermissionId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RolePermissionId] int NOT NULL IDENTITY,
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RolePermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([PermissionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_ServiceRequestId] ON [Permissions] ([ServiceRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [RolePermissions] ([RoleId], [PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001065758_AddPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001065758_AddPermissions', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [CompletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [CompletedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [CreatedByUserId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [EndDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [StartDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [Status] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [TimeZoneId] nvarchar(64) NOT NULL DEFAULT N'Asia/Kolkata';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ActiveScorerUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ScorerLastSeenAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ScoringRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [Matches] ADD [ScoringRequestedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ActiveScorerUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ScorerLastSeenAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ScoringRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [ScoringRequestedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE TABLE [TournamentMembers] (
        [TournamentMemberId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [UserId] int NOT NULL,
        [MemberRole] nvarchar(20) NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_TournamentMembers] PRIMARY KEY ([TournamentMemberId]),
        CONSTRAINT [FK_TournamentMembers_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TournamentMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE INDEX [IX_Tournaments_Status_EndDate] ON [Tournaments] ([Status], [EndDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TournamentMembers_TournamentId_UserId] ON [TournamentMembers] ([TournamentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    CREATE INDEX [IX_TournamentMembers_UserId] ON [TournamentMembers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    EXEC(N'UPDATE [Tournaments] SET [Status] = 1 WHERE [IsStarted] = 1;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    EXEC(N'INSERT INTO [TournamentMembers] ([TournamentId], [UserId], [MemberRole], [Crd], [CrdBy], [Lmd], [LmdBy], [IsDisabled])
    SELECT t.[Id], 1, N''Owner'', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 1, 0
    FROM [Tournaments] t
    WHERE NOT EXISTS (SELECT 1 FROM [TournamentMembers] m WHERE m.[TournamentId] = t.[Id] AND m.[UserId] = 1);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085439_AddTournamentAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001085439_AddTournamentAccess', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC sp_rename N'[Players].[PersonId]', N'UserId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Teams] ADD [JoinCode] varchar(6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Players] ADD [EmailId] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Players] ADD [TournamentId] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    CREATE TABLE [UserCricketProfiles] (
        [UserId] int NOT NULL,
        [PrimaryRole] int NOT NULL,
        [BattingStyle] int NULL,
        [BowlingArm] int NULL,
        [BowlingType] int NULL,
        [BattingOrderPreference] int NULL,
        CONSTRAINT [PK_UserCricketProfiles] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_UserCricketProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'UPDATE [Players] SET [UserId] = NULL WHERE [UserId] IS NOT NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'UPDATE p SET p.[TournamentId] = t.[TournamentId] FROM [Players] p JOIN [Teams] t ON t.[Id] = p.[TeamId];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'
    DECLARE @alphabet varchar(32) = ''ABCDEFGHJKLMNPQRSTUVWXYZ23456789'';
    DECLARE @id int, @code varchar(6), @bytes varbinary(6), @i int;
    DECLARE teams CURSOR LOCAL FAST_FORWARD FOR SELECT [Id] FROM [Teams] WHERE [JoinCode] IS NULL;
    OPEN teams;
    FETCH NEXT FROM teams INTO @id;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        WHILE 1 = 1
        BEGIN
            SET @bytes = CRYPT_GEN_RANDOM(6);
            SET @code = '''';
            SET @i = 1;
            WHILE @i <= 6
            BEGIN
                SET @code = @code + SUBSTRING(@alphabet, CAST(SUBSTRING(@bytes, @i, 1) AS int) % 32 + 1, 1);
                SET @i = @i + 1;
            END
            IF NOT EXISTS (SELECT 1 FROM [Teams] WHERE [JoinCode] = @code) BREAK;
        END
        UPDATE [Teams] SET [JoinCode] = @code WHERE [Id] = @id;
        FETCH NEXT FROM teams INTO @id;
    END
    CLOSE teams;
    DEALLOCATE teams;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Teams_JoinCode] ON [Teams] ([JoinCode]) WHERE [JoinCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    CREATE INDEX [IX_Players_EmailId] ON [Players] ([EmailId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Players_TournamentId_UserId] ON [Players] ([TournamentId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    CREATE INDEX [IX_Players_UserId] ON [Players] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    ALTER TABLE [Players] ADD CONSTRAINT [FK_Players_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001112247_AddPlayerAccounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001112247_AddPlayerAccounts', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [UserSessions] ADD [IsPortal] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [FailedPasswordCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PasswordHash] varchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PasswordLockedUntil] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PasswordSetAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005181759_AddAdminPortal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005181759_AddAdminPortal', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    ALTER TABLE [Users] ADD [PhoneVerifiedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    ALTER TABLE [Players] ADD [PhoneNumber] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    CREATE INDEX [IX_Players_PhoneNumber] ON [Players] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013181209_AddPhoneIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013181209_AddPhoneIdentity', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    CREATE TABLE [TournamentInvites] (
        [TournamentInviteId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [Target] nvarchar(256) NOT NULL,
        [MemberRole] nvarchar(20) NOT NULL,
        [LastSentAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [AcceptedAt] datetime2 NULL,
        [AcceptedByUserId] int NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_TournamentInvites] PRIMARY KEY ([TournamentInviteId]),
        CONSTRAINT [FK_TournamentInvites_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    CREATE INDEX [IX_TournamentInvites_Target] ON [TournamentInvites] ([Target]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TournamentInvites_TournamentId_Target] ON [TournamentInvites] ([TournamentId], [Target]) WHERE [AcceptedAt] IS NULL AND [IsDisabled] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016061051_AddTournamentInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016061051_AddTournamentInvites', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    CREATE TABLE [Notifications] (
        [NotificationId] bigint NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Kind] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Body] nvarchar(1000) NOT NULL,
        [TournamentId] int NULL,
        [Sport] nvarchar(20) NULL,
        [MatchId] int NULL,
        [DedupeKey] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationId]),
        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_CreatedAt] ON [Notifications] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Notifications_DedupeKey] ON [Notifications] ([DedupeKey]) WHERE [DedupeKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_NotificationId] ON [Notifications] ([UserId], [NotificationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016102757_AddNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016102757_AddNotifications', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailAttempts] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailBody] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailSentAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailSubject] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [EmailTo] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    ALTER TABLE [Notifications] ADD [InInbox] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    CREATE TABLE [NotificationTemplates] (
        [NotificationTemplateId] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [IsEmail] bit NOT NULL,
        [EmailSubject] nvarchar(200) NULL,
        [EmailBody] nvarchar(4000) NULL,
        [IsSms] bit NOT NULL,
        [SmsBody] nvarchar(1000) NULL,
        [SmsDltTemplateId] nvarchar(50) NULL,
        [IsNotification] bit NOT NULL,
        [NotificationTitle] nvarchar(200) NULL,
        [NotificationBody] nvarchar(1000) NULL,
        [IsPush] bit NOT NULL,
        [PushTitle] nvarchar(100) NULL,
        [PushBody] nvarchar(500) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY ([NotificationTemplateId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Notifications_EmailSentAt] ON [Notifications] ([EmailSentAt]) WHERE [EmailTo] IS NOT NULL AND [EmailSentAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationTemplates_Code] ON [NotificationTemplates] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016115217_AddNotificationTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016115217_AddNotificationTemplates', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021050529_AllowSharedJerseyNumbers'
)
BEGIN
    DROP INDEX [IX_Players_TeamId_JerseyNumber] ON [Players];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021050529_AllowSharedJerseyNumbers'
)
BEGIN
    CREATE INDEX [IX_Players_TeamId_JerseyNumber] ON [Players] ([TeamId], [JerseyNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021050529_AllowSharedJerseyNumbers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021050529_AllowSharedJerseyNumbers', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Matches]') AND [c].[name] = N'SavedFixtureId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Matches] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Matches] ALTER COLUMN [SavedFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [Matches] ADD [KnockoutFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [Matches] ADD [RoundLabel] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CricketMatches]') AND [c].[name] = N'SavedFixtureId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CricketMatches] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [CricketMatches] ALTER COLUMN [SavedFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [KnockoutFixtureId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD [RoundLabel] nvarchar(60) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE TABLE [KnockoutFixtures] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Order] int NOT NULL,
        [Round] int NOT NULL,
        [Label] nvarchar(60) NOT NULL,
        [Home_Kind] int NOT NULL,
        [Home_TeamId] int NULL,
        [Home_GroupName] nvarchar(20) NULL,
        [Home_Place] int NULL,
        [Home_FromOrder] int NULL,
        [Home_OverrideTeamId] int NULL,
        [Away_Kind] int NOT NULL,
        [Away_TeamId] int NULL,
        [Away_GroupName] nvarchar(20) NULL,
        [Away_Place] int NULL,
        [Away_FromOrder] int NULL,
        [Away_OverrideTeamId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByUserId] int NOT NULL,
        CONSTRAINT [PK_KnockoutFixtures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_KnockoutFixtures_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE INDEX [IX_Matches_KnockoutFixtureId] ON [Matches] ([KnockoutFixtureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE INDEX [IX_CricketMatches_KnockoutFixtureId] ON [CricketMatches] ([KnockoutFixtureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_KnockoutFixtures_TournamentId_Order] ON [KnockoutFixtures] ([TournamentId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [CricketMatches] ADD CONSTRAINT [FK_CricketMatches_KnockoutFixtures_KnockoutFixtureId] FOREIGN KEY ([KnockoutFixtureId]) REFERENCES [KnockoutFixtures] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    ALTER TABLE [Matches] ADD CONSTRAINT [FK_Matches_KnockoutFixtures_KnockoutFixtureId] FOREIGN KEY ([KnockoutFixtureId]) REFERENCES [KnockoutFixtures] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021052937_AddKnockouts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021052937_AddKnockouts', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [IsPublic] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [PublicCode] nvarchar(8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    CREATE TABLE [TournamentFollows] (
        [TournamentFollowId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        CONSTRAINT [PK_TournamentFollows] PRIMARY KEY ([TournamentFollowId]),
        CONSTRAINT [FK_TournamentFollows_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TournamentFollows_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Tournaments_PublicCode] ON [Tournaments] ([PublicCode]) WHERE [PublicCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TournamentFollows_TournamentId_UserId] ON [TournamentFollows] ([TournamentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    CREATE INDEX [IX_TournamentFollows_UserId] ON [TournamentFollows] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021123835_AddPublicTournaments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021123835_AddPublicTournaments', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    ALTER TABLE [UserSessions] ADD [AppVersion] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    CREATE TABLE [AppStarts] (
        [AppStartId] int NOT NULL IDENTITY,
        [Platform] nvarchar(20) NOT NULL,
        [AppVersion] nvarchar(20) NOT NULL,
        [ApiVersion] nvarchar(20) NULL,
        [Description] nvarchar(500) NULL,
        [IsForceUpdate] bit NOT NULL,
        [ReleaseDate] datetime2 NOT NULL,
        [StoreUrl] nvarchar(500) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_AppStarts] PRIMARY KEY ([AppStartId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppStarts_Platform_AppVersion] ON [AppStarts] ([Platform], [AppVersion]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022060521_AddAppStarts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022060521_AddAppStarts', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [Auctions] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Unit] nvarchar(8) NOT NULL,
        [Purse] int NOT NULL,
        [MinSquad] int NOT NULL,
        [MaxSquad] int NOT NULL,
        [KeptPlayerPrice] int NOT NULL,
        [NoTierBasePrice] int NOT NULL,
        [BidSteps] nvarchar(200) NOT NULL,
        [ClockOn] bit NOT NULL,
        [FirstBidSeconds] int NOT NULL,
        [NextBidSeconds] int NOT NULL,
        [Order] nvarchar(20) NOT NULL,
        [UnsoldPlan] nvarchar(20) NOT NULL,
        [Round] int NOT NULL,
        [AuctioneerUserId] int NULL,
        [OpenLotId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [FinishedAt] datetime2 NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_Auctions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Auctions_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [PlayerPools] (
        [TournamentId] int NOT NULL,
        [JoinCode] varchar(7) NULL,
        [IsOpen] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PlayerPools] PRIMARY KEY ([TournamentId]),
        CONSTRAINT [FK_PlayerPools_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [PoolPlayers] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [UserId] int NULL,
        [EmailId] nvarchar(256) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [Position] nvarchar(40) NULL,
        [CricketRole] nvarchar(30) NULL,
        [TierId] int NULL,
        [Order] int NOT NULL,
        [JoinedByCode] bit NOT NULL,
        [IsWithdrawn] bit NOT NULL,
        [PlayerId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByUserId] int NOT NULL,
        CONSTRAINT [PK_PoolPlayers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PoolPlayers_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PoolPlayers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [PoolTiers] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Name] nvarchar(30) NOT NULL,
        [Order] int NOT NULL,
        [BasePrice] int NOT NULL,
        [MaxPerTeam] int NULL,
        CONSTRAINT [PK_PoolTiers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PoolTiers_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [AuctionLots] (
        [Id] int NOT NULL IDENTITY,
        [AuctionId] int NOT NULL,
        [PoolPlayerId] int NOT NULL,
        [Round] int NOT NULL,
        [Order] int NOT NULL,
        [BasePrice] int NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OpenedAt] datetime2 NULL,
        [ClosesAt] datetime2 NULL,
        [PausedMsLeft] int NULL,
        [ClosedAt] datetime2 NULL,
        [SoldTeamId] int NULL,
        [Price] int NULL,
        [Assigned] bit NOT NULL,
        CONSTRAINT [PK_AuctionLots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuctionLots_Auctions_AuctionId] FOREIGN KEY ([AuctionId]) REFERENCES [Auctions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [AuctionTeams] (
        [Id] int NOT NULL IDENTITY,
        [AuctionId] int NOT NULL,
        [TournamentId] int NOT NULL,
        [TeamId] int NOT NULL,
        [BidderUserId] int NULL,
        [KeptPlayers] int NOT NULL,
        CONSTRAINT [PK_AuctionTeams] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuctionTeams_Auctions_AuctionId] FOREIGN KEY ([AuctionId]) REFERENCES [Auctions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE TABLE [AuctionBids] (
        [Id] bigint NOT NULL IDENTITY,
        [LotId] int NOT NULL,
        [TeamId] int NOT NULL,
        [Amount] int NOT NULL,
        [ByUserId] int NOT NULL,
        [FromRoom] bit NOT NULL,
        [At] datetime2 NOT NULL,
        [IsUndone] bit NOT NULL,
        CONSTRAINT [PK_AuctionBids] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuctionBids_AuctionLots_LotId] FOREIGN KEY ([LotId]) REFERENCES [AuctionLots] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionBids_LotId_Id] ON [AuctionBids] ([LotId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionLots_AuctionId_Round_Order] ON [AuctionLots] ([AuctionId], [Round], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionLots_Status_ClosesAt] ON [AuctionLots] ([Status], [ClosesAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Auctions_TournamentId] ON [Auctions] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AuctionTeams_AuctionId_TeamId] ON [AuctionTeams] ([AuctionId], [TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_AuctionTeams_TournamentId_BidderUserId] ON [AuctionTeams] ([TournamentId], [BidderUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PlayerPools_JoinCode] ON [PlayerPools] ([JoinCode]) WHERE [JoinCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolPlayers_EmailId] ON [PoolPlayers] ([EmailId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolPlayers_PhoneNumber] ON [PoolPlayers] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PoolPlayers_TournamentId_UserId] ON [PoolPlayers] ([TournamentId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolPlayers_UserId] ON [PoolPlayers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    CREATE INDEX [IX_PoolTiers_TournamentId_Order] ON [PoolTiers] ([TournamentId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117064541_AddPlayerAuction'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117064541_AddPlayerAuction', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    ALTER TABLE [PlayerPools] ADD [Method] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    ALTER TABLE [AuctionLots] ADD [MovedFromTeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE TABLE [TeamDraws] (
        [Id] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [TeamOrder] nvarchar(1000) NOT NULL,
        [DrawerUserId] int NULL,
        [AutoSeconds] int NULL,
        [NextAutoAt] datetime2 NULL,
        [Times] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [StartedAt] datetime2 NULL,
        [ConfirmedAt] datetime2 NULL,
        CONSTRAINT [PK_TeamDraws] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TeamDraws_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE TABLE [DrawPicks] (
        [Id] int NOT NULL IDENTITY,
        [DrawId] int NOT NULL,
        [TournamentId] int NOT NULL,
        [PoolPlayerId] int NOT NULL,
        [TeamId] int NOT NULL,
        [Order] int NOT NULL,
        [TierId] int NULL,
        [PickedAt] datetime2 NOT NULL,
        [LandsAt] datetime2 NOT NULL,
        [Announced] bit NOT NULL,
        [MovedFromTeamId] int NULL,
        CONSTRAINT [PK_DrawPicks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DrawPicks_TeamDraws_DrawId] FOREIGN KEY ([DrawId]) REFERENCES [TeamDraws] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE INDEX [IX_DrawPicks_DrawId_Order] ON [DrawPicks] ([DrawId], [Order]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DrawPicks_DrawId_PoolPlayerId] ON [DrawPicks] ([DrawId], [PoolPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE INDEX [IX_DrawPicks_TournamentId] ON [DrawPicks] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE INDEX [IX_TeamDraws_Status_NextAutoAt] ON [TeamDraws] ([Status], [NextAutoAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TeamDraws_TournamentId] ON [TeamDraws] ([TournamentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117111819_AddTeamDraw'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117111819_AddTeamDraw', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117121006_AddAuctionHistory'
)
BEGIN
    ALTER TABLE [AuctionLots] ADD [ByDraw] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117121006_AddAuctionHistory'
)
BEGIN
    ALTER TABLE [AuctionLots] ADD [CancelledAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117121006_AddAuctionHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117121006_AddAuctionHistory', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118085306_RenameAppToKickCricket'
)
BEGIN
    UPDATE [NotificationTemplates] SET [EmailSubject] = REPLACE([EmailSubject], N'Tournament Scheduler', N'KickCricket'), [EmailBody] = REPLACE([EmailBody], N'Tournament Scheduler', N'KickCricket'), [SmsBody] = REPLACE([SmsBody], N'Tournament Scheduler', N'KickCricket'), [NotificationTitle] = REPLACE([NotificationTitle], N'Tournament Scheduler', N'KickCricket'), [NotificationBody] = REPLACE([NotificationBody], N'Tournament Scheduler', N'KickCricket'), [PushTitle] = REPLACE([PushTitle], N'Tournament Scheduler', N'KickCricket'), [PushBody] = REPLACE([PushBody], N'Tournament Scheduler', N'KickCricket');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118085306_RenameAppToKickCricket'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261118085306_RenameAppToKickCricket', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    ALTER TABLE [Users] ADD [PhotoId] varchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [BannerId] varchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [LogoId] varchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120031238_AddPictures'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120031238_AddPictures', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    ALTER TABLE [Players] ADD [LeftOn] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE TABLE [TeamAdmins] (
        [TeamAdminId] int NOT NULL IDENTITY,
        [TeamId] int NOT NULL,
        [TournamentId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_TeamAdmins] PRIMARY KEY ([TeamAdminId]),
        CONSTRAINT [FK_TeamAdmins_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TeamAdmins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TeamAdmins_TeamId_UserId] ON [TeamAdmins] ([TeamId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE INDEX [IX_TeamAdmins_TournamentId_UserId] ON [TeamAdmins] ([TournamentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    CREATE INDEX [IX_TeamAdmins_UserId] ON [TeamAdmins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120121343_AddTeamAdmins'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120121343_AddTeamAdmins', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Teams] ADD [ClubId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Players] ADD [ClubPlayerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Notifications] ADD [ClubId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Notifications] ADD [TeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE TABLE [Clubs] (
        [ClubId] int NOT NULL IDENTITY,
        [Name] nvarchar(60) NOT NULL,
        [Sport] nvarchar(20) NOT NULL,
        [Area] nvarchar(60) NULL,
        [JoinCode] varchar(8) NOT NULL,
        [LogoId] varchar(40) NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Clubs] PRIMARY KEY ([ClubId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE TABLE [ClubAdmins] (
        [ClubAdminId] int NOT NULL IDENTITY,
        [ClubId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_ClubAdmins] PRIMARY KEY ([ClubAdminId]),
        CONSTRAINT [FK_ClubAdmins_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClubAdmins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE TABLE [ClubPlayers] (
        [ClubPlayerId] int NOT NULL IDENTITY,
        [ClubId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [JerseyNumber] int NULL,
        [Position] nvarchar(50) NULL,
        [IsCaptain] bit NOT NULL,
        [UserId] int NULL,
        [EmailId] nvarchar(256) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [Cricket_PrimaryRole] int NULL,
        [Cricket_BattingStyle] int NULL,
        [Cricket_BowlingArm] int NULL,
        [Cricket_BowlingType] int NULL,
        [Cricket_BattingOrderPreference] int NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_ClubPlayers] PRIMARY KEY ([ClubPlayerId]),
        CONSTRAINT [FK_ClubPlayers_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClubPlayers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_Teams_ClubId] ON [Teams] ([ClubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_Players_ClubPlayerId] ON [Players] ([ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClubAdmins_ClubId_UserId] ON [ClubAdmins] ([ClubId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubAdmins_UserId] ON [ClubAdmins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ClubPlayers_ClubId_UserId] ON [ClubPlayers] ([ClubId], [UserId]) WHERE [UserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubPlayers_EmailId] ON [ClubPlayers] ([EmailId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubPlayers_PhoneNumber] ON [ClubPlayers] ([PhoneNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE INDEX [IX_ClubPlayers_UserId] ON [ClubPlayers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clubs_JoinCode] ON [Clubs] ([JoinCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Players] ADD CONSTRAINT [FK_Players_ClubPlayers_ClubPlayerId] FOREIGN KEY ([ClubPlayerId]) REFERENCES [ClubPlayers] ([ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    ALTER TABLE [Teams] ADD CONSTRAINT [FK_Teams_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261120172036_AddClubs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261120172036_AddClubs', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [EntriesOpen] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [EntryCode] varchar(9) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Notifications] ADD [ClubPlayerId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    ALTER TABLE [Clubs] ADD [PublicCode] varchar(8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE TABLE [TournamentEntries] (
        [TournamentEntryId] int NOT NULL IDENTITY,
        [TournamentId] int NOT NULL,
        [ClubId] int NOT NULL,
        [TeamName] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RequestedByUserId] int NOT NULL,
        [RequestedAt] datetime2 NOT NULL,
        [DecidedByUserId] int NULL,
        [DecidedAt] datetime2 NULL,
        [TeamId] int NULL,
        CONSTRAINT [PK_TournamentEntries] PRIMARY KEY ([TournamentEntryId]),
        CONSTRAINT [FK_TournamentEntries_Clubs_ClubId] FOREIGN KEY ([ClubId]) REFERENCES [Clubs] ([ClubId]) ON DELETE CASCADE,
        CONSTRAINT [FK_TournamentEntries_Tournaments_TournamentId] FOREIGN KEY ([TournamentId]) REFERENCES [Tournaments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE TABLE [TournamentEntryPlayers] (
        [TournamentEntryPlayerId] int NOT NULL IDENTITY,
        [TournamentEntryId] int NOT NULL,
        [ClubPlayerId] int NOT NULL,
        CONSTRAINT [PK_TournamentEntryPlayers] PRIMARY KEY ([TournamentEntryPlayerId]),
        CONSTRAINT [FK_TournamentEntryPlayers_TournamentEntries_TournamentEntryId] FOREIGN KEY ([TournamentEntryId]) REFERENCES [TournamentEntries] ([TournamentEntryId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Tournaments_EntryCode] ON [Tournaments] ([EntryCode]) WHERE [EntryCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Clubs_PublicCode] ON [Clubs] ([PublicCode]) WHERE [PublicCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE INDEX [IX_TournamentEntries_ClubId] ON [TournamentEntries] ([ClubId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE INDEX [IX_TournamentEntries_TeamId] ON [TournamentEntries] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TournamentEntries_TournamentId_ClubId] ON [TournamentEntries] ([TournamentId], [ClubId]) WHERE [Status] IN (''Waiting'', ''Accepted'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE INDEX [IX_TournamentEntryPlayers_ClubPlayerId] ON [TournamentEntryPlayers] ([ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TournamentEntryPlayers_TournamentEntryId_ClubPlayerId] ON [TournamentEntryPlayers] ([TournamentEntryId], [ClubPlayerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261121182754_AddClubEntries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261121182754_AddClubEntries', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    ALTER TABLE [Notifications] ADD [ProfileUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    CREATE TABLE [UserFollows] (
        [FollowerUserId] int NOT NULL,
        [FollowedUserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        CONSTRAINT [PK_UserFollows] PRIMARY KEY ([FollowerUserId], [FollowedUserId]),
        CONSTRAINT [FK_UserFollows_Users_FollowedUserId] FOREIGN KEY ([FollowedUserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserFollows_Users_FollowerUserId] FOREIGN KEY ([FollowerUserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    CREATE INDEX [IX_UserFollows_FollowedUserId] ON [UserFollows] ([FollowedUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123110634_AddFollows'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123110634_AddFollows', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123174025_EveryTournamentPublic'
)
BEGIN
    EXEC(N'
    DECLARE @alphabet varchar(32) = ''ABCDEFGHJKLMNPQRSTUVWXYZ23456789'';
    DECLARE @id int, @code varchar(8), @i int;
    UPDATE [Tournaments] SET [IsPublic] = CAST(1 AS bit) WHERE [IsPublic] = CAST(0 AS bit);
    WHILE 1 = 1
    BEGIN
        SET @id = NULL;
        SELECT TOP 1 @id = [Id] FROM [Tournaments] WHERE [PublicCode] IS NULL ORDER BY [Id];
        IF @id IS NULL BREAK;
        SET @code = '''';
        SET @i = 0;
        WHILE @i < 8
        BEGIN
            SET @code = @code + SUBSTRING(@alphabet, 1 + CAST(CRYPT_GEN_RANDOM(1) AS int) % 32, 1);
            SET @i = @i + 1;
        END;
        IF NOT EXISTS (SELECT 1 FROM [Tournaments] WHERE [PublicCode] = @code)
            UPDATE [Tournaments] SET [PublicCode] = @code WHERE [Id] = @id;
    END;
    ')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123174025_EveryTournamentPublic'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123174025_EveryTournamentPublic', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Tournaments] ADD [LeagueId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Teams] ADD [BroughtBackFromTeamId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Notifications] ADD [LeagueId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE TABLE [Leagues] (
        [LeagueId] int NOT NULL IDENTITY,
        [Name] nvarchar(60) NOT NULL,
        [ShortName] nvarchar(8) NOT NULL,
        [Sport] nvarchar(20) NOT NULL,
        [Area] nvarchar(60) NULL,
        [LogoId] varchar(40) NULL,
        [PublicCode] varchar(8) NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_Leagues] PRIMARY KEY ([LeagueId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE TABLE [LeagueOrganisers] (
        [LeagueOrganiserId] int NOT NULL IDENTITY,
        [LeagueId] int NOT NULL,
        [UserId] int NOT NULL,
        [Crd] datetime2 NOT NULL,
        [CrdBy] int NOT NULL,
        [Lmd] datetime2 NOT NULL,
        [LmdBy] int NOT NULL,
        [IsDisabled] bit NOT NULL,
        CONSTRAINT [PK_LeagueOrganisers] PRIMARY KEY ([LeagueOrganiserId]),
        CONSTRAINT [FK_LeagueOrganisers_Leagues_LeagueId] FOREIGN KEY ([LeagueId]) REFERENCES [Leagues] ([LeagueId]) ON DELETE CASCADE,
        CONSTRAINT [FK_LeagueOrganisers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE INDEX [IX_Tournaments_LeagueId] ON [Tournaments] ([LeagueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE INDEX [IX_Teams_BroughtBackFromTeamId] ON [Teams] ([BroughtBackFromTeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LeagueOrganisers_LeagueId_UserId] ON [LeagueOrganisers] ([LeagueId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE INDEX [IX_LeagueOrganisers_UserId] ON [LeagueOrganisers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Leagues_PublicCode] ON [Leagues] ([PublicCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    ALTER TABLE [Tournaments] ADD CONSTRAINT [FK_Tournaments_Leagues_LeagueId] FOREIGN KEY ([LeagueId]) REFERENCES [Leagues] ([LeagueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124060334_AddLeagues'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124060334_AddLeagues', N'10.0.9');
END;

COMMIT;
GO

