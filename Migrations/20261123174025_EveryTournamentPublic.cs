using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <summary>
    /// Every tournament is public (the user's choice, 2026-11-23): the owners' switch is gone, so every existing
    /// tournament is switched on and gets its link code now (codes from the join codes' alphabet, no 0/O, no 1/I;
    /// CRYPT_GEN_RANDOM, and 256 is a multiple of 32, so every letter is as likely). Data only — no column changes.
    /// In one EXEC so its variables live in their own batch inside the idempotent script.
    /// </summary>
    public partial class EveryTournamentPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"EXEC(N'
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
')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: which tournaments were private before can't be known any more, and the codes do no harm.
        }
    }
}
