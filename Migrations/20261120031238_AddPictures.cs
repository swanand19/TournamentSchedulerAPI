using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    /// <summary>
    /// Pictures (HLD P-26, 2026-11-20): a profile photo per account, a logo and a banner per tournament. Only
    /// file names are stored; the files live in the pictures folder. (The seed row's PhotoId is null already, so
    /// the UpdateData EF generated is left out — it named the new column in the same batch.)
    /// </summary>
    public partial class AddPictures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoId",
                table: "Users",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BannerId",
                table: "Tournaments",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoId",
                table: "Tournaments",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BannerId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "LogoId",
                table: "Tournaments");
        }
    }
}
