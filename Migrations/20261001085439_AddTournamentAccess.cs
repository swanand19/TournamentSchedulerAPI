using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Tournaments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletedByUserId",
                table: "Tournaments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "Tournaments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "Tournaments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Tournaments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Tournaments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Kolkata");

            migrationBuilder.AddColumn<int>(
                name: "ActiveScorerUserId",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScorerLastSeenAt",
                table: "Matches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScoringRequestedAt",
                table: "Matches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoringRequestedByUserId",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveScorerUserId",
                table: "CricketMatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScorerLastSeenAt",
                table: "CricketMatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScoringRequestedAt",
                table: "CricketMatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoringRequestedByUserId",
                table: "CricketMatches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentMembers",
                columns: table => new
                {
                    TournamentMemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MemberRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMembers", x => x.TournamentMemberId);
                    table.ForeignKey(
                        name: "FK_TournamentMembers_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_Status_EndDate",
                table: "Tournaments",
                columns: new[] { "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMembers_TournamentId_UserId",
                table: "TournamentMembers",
                columns: new[] { "TournamentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMembers_UserId",
                table: "TournamentMembers",
                column: "UserId");

            // Tournaments from before accounts: started ones are Live, and System (UserId 1, which
            // CreatedByUserId already defaults to) is their owner until someone takes them over with
            // deploy/sql/04-take-over-existing-tournaments.sql. No dates, so they never complete by themselves.
            // In EXEC: the generated SQL script runs all of this as one batch, and SQL Server would
            // otherwise reject the batch for naming a column the same batch only adds.
            migrationBuilder.Sql("EXEC(N'UPDATE [Tournaments] SET [Status] = 1 WHERE [IsStarted] = 1;');");
            migrationBuilder.Sql("""
                EXEC(N'INSERT INTO [TournamentMembers] ([TournamentId], [UserId], [MemberRole], [Crd], [CrdBy], [Lmd], [LmdBy], [IsDisabled])
                SELECT t.[Id], 1, N''Owner'', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 1, 0
                FROM [Tournaments] t
                WHERE NOT EXISTS (SELECT 1 FROM [TournamentMembers] m WHERE m.[TournamentId] = t.[Id] AND m.[UserId] = 1);');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentMembers");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_Status_EndDate",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "ActiveScorerUserId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ScorerLastSeenAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ScoringRequestedAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ScoringRequestedByUserId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ActiveScorerUserId",
                table: "CricketMatches");

            migrationBuilder.DropColumn(
                name: "ScorerLastSeenAt",
                table: "CricketMatches");

            migrationBuilder.DropColumn(
                name: "ScoringRequestedAt",
                table: "CricketMatches");

            migrationBuilder.DropColumn(
                name: "ScoringRequestedByUserId",
                table: "CricketMatches");
        }
    }
}
