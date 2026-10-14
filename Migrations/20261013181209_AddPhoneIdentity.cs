using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Migrations
{
    // Phase 6B: email or mobile number. Users.PhoneNumber already existed (unique when set);
    // this adds when it was proved, and the number an organiser can give a squad place. The
    // scaffold's no-op seed update (System's new PhoneVerifiedAt = null) is left out: in the
    // generated script it would name a column the same batch adds.
    /// <inheritdoc />
    public partial class AddPhoneIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneVerifiedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Players",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_PhoneNumber",
                table: "Players",
                column: "PhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_PhoneNumber",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PhoneVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Players");
        }
    }
}
