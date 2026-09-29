using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// Applies a procedure's .sql file from Data/StoredProcedures/Sql inside a migration, so creating or
/// changing a procedure ships through the same `dotnet ef database update` as every table change and
/// the database can never drift from the code that calls it.
/// <code>
/// protected override void Up(MigrationBuilder migrationBuilder) =>
///     migrationBuilder.SqlFile("usp_GetStandings.sql");
/// </code>
/// The files are embedded in the build (see the .csproj), so a published API carries them. Write
/// them as CREATE OR ALTER PROCEDURE so re-running is harmless; "GO" separates batches.
/// </summary>
public static class StoredProcedureMigrations
{
    public static OperationBuilder<SqlOperation> SqlFile(this MigrationBuilder migrationBuilder, string fileName)
    {
        var assembly = typeof(StoredProcedureMigrations).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(".StoredProcedures.Sql." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No embedded SQL file named {fileName} under Data/StoredProcedures/Sql.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return migrationBuilder.Sql(reader.ReadToEnd());
    }
}
