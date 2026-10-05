using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PersonId",
                table: "Players",
                newName: "UserId");

            migrationBuilder.AddColumn<string>(
                name: "JoinCode",
                table: "Teams",
                type: "varchar(6)",
                unicode: false,
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailId",
                table: "Players",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TournamentId",
                table: "Players",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "UserCricketProfiles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PrimaryRole = table.Column<int>(type: "int", nullable: false),
                    BattingStyle = table.Column<int>(type: "int", nullable: true),
                    BowlingArm = table.Column<int>(type: "int", nullable: true),
                    BowlingType = table.Column<int>(type: "int", nullable: true),
                    BattingOrderPreference = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCricketProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserCricketProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            // Data for the columns above, before their indexes. Wrapped in EXEC because the
            // idempotent script runs as one batch, compiled before these columns exist.

            // PersonId was a reserved seam nothing ever wrote; clear it anyway so the new foreign key can't fail.
            migrationBuilder.Sql("EXEC(N'UPDATE [Players] SET [UserId] = NULL WHERE [UserId] IS NOT NULL;');");

            // Each squad place's tournament, from its team (teams never move).
            migrationBuilder.Sql("EXEC(N'UPDATE p SET p.[TournamentId] = t.[TournamentId] FROM [Players] p JOIN [Teams] t ON t.[Id] = p.[TeamId];');");

            // A join code for every existing team: six characters from the same alphabet the API
            // uses (no 0/O, 1/I), from the server's secure random source, retried on a clash.
            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_JoinCode",
                table: "Teams",
                column: "JoinCode",
                unique: true,
                filter: "[JoinCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Players_EmailId",
                table: "Players",
                column: "EmailId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_TournamentId_UserId",
                table: "Players",
                columns: new[] { "TournamentId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Players_UserId",
                table: "Players",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Users_UserId",
                table: "Players",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Users_UserId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "UserCricketProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Teams_JoinCode",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Players_EmailId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_TournamentId_UserId",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_UserId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "JoinCode",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "EmailId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TournamentId",
                table: "Players");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Players",
                newName: "PersonId");
        }
    }
}
