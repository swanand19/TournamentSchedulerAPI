using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// <see cref="IStoredProcedureExecutor"/> on the request's own connection. It is Entity Framework's
/// connection, opened through EF so the two never disagree about its state, and any transaction
/// begun through <see cref="IUnitOfWork.BeginTransactionAsync"/> is joined — nothing here opens a
/// second connection or a transaction of its own.
/// </summary>
public sealed class StoredProcedureExecutor : IStoredProcedureExecutor
{
    private readonly TournamentDbContext _db;

    public StoredProcedureExecutor(TournamentDbContext db)
    {
        _db = db;
    }

    public Task<int> ExecuteAsync(INonQueryProcedure procedure, CancellationToken cancellationToken = default) =>
        RunAsync(procedure, command => command.ExecuteNonQueryAsync(cancellationToken), cancellationToken);

    public Task<List<TRow>> QueryAsync<TRow>(IQueryProcedure<TRow> procedure, CancellationToken cancellationToken = default) =>
        RunAsync(procedure, async command =>
        {
            var rows = new List<TRow>();
            // Disposed before RunAsync reads the outputs: SQL Server only sends them once the rows are done.
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return rows;

            var map = RowMapper.For<TRow>(reader);
            do rows.Add(map(reader));
            while (await reader.ReadAsync(cancellationToken));
            return rows;
        }, cancellationToken);

    public Task<TRow?> QuerySingleAsync<TRow>(IQueryProcedure<TRow> procedure, CancellationToken cancellationToken = default) =>
        RunAsync(procedure, async command =>
        {
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return default;
            return RowMapper.For<TRow>(reader)(reader);
        }, cancellationToken);

    public Task<TValue> ScalarAsync<TValue>(IScalarProcedure<TValue> procedure, CancellationToken cancellationToken = default) =>
        RunAsync(procedure, async command =>
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return (TValue)ValueConverter.ToProperty(value, typeof(TValue),
                () => $"the value returned by {procedure.GetType().Name}")!;
        }, cancellationToken);

    private async Task<T> RunAsync<T>(IStoredProcedure procedure, Func<DbCommand, Task<T>> execute, CancellationToken cancellationToken)
    {
        var definition = ProcedureDefinition.For(procedure.GetType());
        var parameters = definition.CreateParameters(procedure);

        // Reference-counted by EF: a connection EF already has open (a transaction) is left open.
        await _db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = definition.Name;
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (definition.TimeoutSeconds > 0)
                command.CommandTimeout = definition.TimeoutSeconds;
            else if (_db.Database.GetCommandTimeout() is int configured)
                command.CommandTimeout = configured;
            command.Parameters.AddRange(parameters);

            T result;
            try
            {
                result = await execute(command);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                throw new DuplicateEntryException(ex);
            }
            catch (SqlException ex)
            {
                throw new StoredProcedureException(definition.Name, ex);
            }

            definition.ReadOutputs(procedure, parameters);
            return result;
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }
}
