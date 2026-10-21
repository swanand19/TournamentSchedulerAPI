using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    // Phase 6C: owners and scorers invited by an email or number that has no account yet.
    /// <inheritdoc />
    public partial class AddTournamentInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TournamentInvites",
                columns: table => new
                {
                    TournamentInviteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MemberRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    Crd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CrdBy = table.Column<int>(type: "int", nullable: false),
                    Lmd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LmdBy = table.Column<int>(type: "int", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentInvites", x => x.TournamentInviteId);
                    table.ForeignKey(
                        name: "FK_TournamentInvites_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentInvites_Target",
                table: "TournamentInvites",
                column: "Target");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentInvites_TournamentId_Target",
                table: "TournamentInvites",
                columns: new[] { "TournamentId", "Target" },
                unique: true,
                filter: "[AcceptedAt] IS NULL AND [IsDisabled] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentInvites");
        }
    }
}
