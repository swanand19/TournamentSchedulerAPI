using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TournamentScheduler.Api.Logging.Migrations
{
    /// <inheritdoc />
    public partial class InitialLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErrorLogs",
                columns: table => new
                {
                    ErrorLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Crd = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RequestUUID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExceptionType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InnerException = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorLogs", x => x.ErrorLogId);
                });

            migrationBuilder.CreateTable(
                name: "MBActivityLogs",
                columns: table => new
                {
                    MBActivityLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestUUID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    JourneyId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ServiceRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ControllerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: false),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: true),
                    StatusMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ViaGateway = table.Column<bool>(type: "bit", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MBActivityLogs", x => x.MBActivityLogId);
                });

            migrationBuilder.CreateTable(
                name: "MBMiddlewareLogs",
                columns: table => new
                {
                    MBMiddlewareLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestUUID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    JourneyId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KeyId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClientIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EncryptedRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: false),
                    RejectedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MBMiddlewareLogs", x => x.MBMiddlewareLogId);
                });

            migrationBuilder.CreateTable(
                name: "RemoteLogs",
                columns: table => new
                {
                    RemoteLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestUUID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RequestHeaders = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseHeaders = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteLogs", x => x.RemoteLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_Crd",
                table: "ErrorLogs",
                column: "Crd");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_RequestUUID",
                table: "ErrorLogs",
                column: "RequestUUID");

            migrationBuilder.CreateIndex(
                name: "IX_MBActivityLogs_RequestUUID",
                table: "MBActivityLogs",
                column: "RequestUUID");

            migrationBuilder.CreateIndex(
                name: "IX_MBActivityLogs_StartDate",
                table: "MBActivityLogs",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_MBMiddlewareLogs_RequestUUID",
                table: "MBMiddlewareLogs",
                column: "RequestUUID");

            migrationBuilder.CreateIndex(
                name: "IX_MBMiddlewareLogs_StartDate",
                table: "MBMiddlewareLogs",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteLogs_RequestUUID",
                table: "RemoteLogs",
                column: "RequestUUID");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteLogs_StartDate",
                table: "RemoteLogs",
                column: "StartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrorLogs");

            migrationBuilder.DropTable(
                name: "MBActivityLogs");

            migrationBuilder.DropTable(
                name: "MBMiddlewareLogs");

            migrationBuilder.DropTable(
                name: "RemoteLogs");
        }
    }
}
