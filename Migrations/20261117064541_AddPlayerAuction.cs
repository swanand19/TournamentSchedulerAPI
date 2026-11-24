using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAuction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Auctions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Purse = table.Column<int>(type: "int", nullable: false),
                    MinSquad = table.Column<int>(type: "int", nullable: false),
                    MaxSquad = table.Column<int>(type: "int", nullable: false),
                    KeptPlayerPrice = table.Column<int>(type: "int", nullable: false),
                    NoTierBasePrice = table.Column<int>(type: "int", nullable: false),
                    BidSteps = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClockOn = table.Column<bool>(type: "bit", nullable: false),
                    FirstBidSeconds = table.Column<int>(type: "int", nullable: false),
                    NextBidSeconds = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnsoldPlan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    AuctioneerUserId = table.Column<int>(type: "int", nullable: true),
                    OpenLotId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Auctions_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerPools",
                columns: table => new
                {
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    JoinCode = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: true),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPools", x => x.TournamentId);
                    table.ForeignKey(
                        name: "FK_PlayerPools_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PoolPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    EmailId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Position = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CricketRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TierId = table.Column<int>(type: "int", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    JoinedByCode = table.Column<bool>(type: "bit", nullable: false),
                    IsWithdrawn = table.Column<bool>(type: "bit", nullable: false),
                    PlayerId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoolPlayers_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PoolPlayers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PoolTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    BasePrice = table.Column<int>(type: "int", nullable: false),
                    MaxPerTeam = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoolTiers_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuctionLots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuctionId = table.Column<int>(type: "int", nullable: false),
                    PoolPlayerId = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    BasePrice = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PausedMsLeft = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SoldTeamId = table.Column<int>(type: "int", nullable: true),
                    Price = table.Column<int>(type: "int", nullable: true),
                    Assigned = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionLots_Auctions_AuctionId",
                        column: x => x.AuctionId,
                        principalTable: "Auctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuctionTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuctionId = table.Column<int>(type: "int", nullable: false),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    BidderUserId = table.Column<int>(type: "int", nullable: true),
                    KeptPlayers = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionTeams_Auctions_AuctionId",
                        column: x => x.AuctionId,
                        principalTable: "Auctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuctionBids",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    ByUserId = table.Column<int>(type: "int", nullable: false),
                    FromRoom = table.Column<bool>(type: "bit", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUndone = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionBids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionBids_AuctionLots_LotId",
                        column: x => x.LotId,
                        principalTable: "AuctionLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionBids_LotId_Id",
                table: "AuctionBids",
                columns: new[] { "LotId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionLots_AuctionId_Round_Order",
                table: "AuctionLots",
                columns: new[] { "AuctionId", "Round", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionLots_Status_ClosesAt",
                table: "AuctionLots",
                columns: new[] { "Status", "ClosesAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_TournamentId",
                table: "Auctions",
                column: "TournamentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionTeams_AuctionId_TeamId",
                table: "AuctionTeams",
                columns: new[] { "AuctionId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionTeams_TournamentId_BidderUserId",
                table: "AuctionTeams",
                columns: new[] { "TournamentId", "BidderUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerPools_JoinCode",
                table: "PlayerPools",
                column: "JoinCode",
                unique: true,
                filter: "[JoinCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PoolPlayers_EmailId",
                table: "PoolPlayers",
                column: "EmailId");

            migrationBuilder.CreateIndex(
                name: "IX_PoolPlayers_PhoneNumber",
                table: "PoolPlayers",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PoolPlayers_TournamentId_UserId",
                table: "PoolPlayers",
                columns: new[] { "TournamentId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PoolPlayers_UserId",
                table: "PoolPlayers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PoolTiers_TournamentId_Order",
                table: "PoolTiers",
                columns: new[] { "TournamentId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionBids");

            migrationBuilder.DropTable(
                name: "AuctionTeams");

            migrationBuilder.DropTable(
                name: "PlayerPools");

            migrationBuilder.DropTable(
                name: "PoolPlayers");

            migrationBuilder.DropTable(
                name: "PoolTiers");

            migrationBuilder.DropTable(
                name: "AuctionLots");

            migrationBuilder.DropTable(
                name: "Auctions");
        }
    }
}
