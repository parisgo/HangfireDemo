using System.Data;
using HangfireDemo.Core.Commandes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Core.Commandes;

public sealed class SqlCommandeImportService(
    string connectionString,
    ILogger<SqlCommandeImportService> logger) : ICommandeImportService
{
    private const int MaximumBatchIdLength = 100;
    private const int MaximumRequestedByLength = 200;
    private const int MaximumJobIdLength = 100;

    public async Task<CommandeImportResult> ImportAsync(
        CommandeImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BatchId);

        if (request.BatchId.Length > MaximumBatchIdLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"BatchId cannot exceed {MaximumBatchIdLength} characters.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SET XACT_ABORT ON;

            IF EXISTS
            (
                SELECT 1
                FROM [app].[CommandeImportExecution] WITH (UPDLOCK, HOLDLOCK)
                WHERE [BatchId] = @BatchId
            )
            BEGIN
                SELECT CAST(0 AS bit);
            END
            ELSE
            BEGIN
                -- Put the real import/upsert statements in this transaction.
                -- The completion marker must commit atomically with business changes.
                INSERT INTO [app].[CommandeImportExecution]
                    ([BatchId], [RequestedBy], [HangfireJobId], [CompletedAtUtc])
                VALUES
                    (@BatchId, @RequestedBy, @HangfireJobId, SYSUTCDATETIME());

                SELECT CAST(1 AS bit);
            END;
            """;

        command.Parameters.Add(new SqlParameter("@BatchId", SqlDbType.NVarChar, MaximumBatchIdLength)
        {
            Value = request.BatchId
        });
        command.Parameters.Add(new SqlParameter("@RequestedBy", SqlDbType.NVarChar, MaximumRequestedByLength)
        {
            Value = Truncate(request.RequestedBy, MaximumRequestedByLength)
        });
        command.Parameters.Add(new SqlParameter("@HangfireJobId", SqlDbType.NVarChar, MaximumJobIdLength)
        {
            Value = request.JobId is null
                ? DBNull.Value
                : Truncate(request.JobId, MaximumJobIdLength)
        });

        var imported = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        if (imported)
        {
            logger.LogInformation(
                "Commande import batch {BatchId} committed",
                request.BatchId);
        }
        else
        {
            logger.LogInformation(
                "Commande import batch {BatchId} was already completed; skipping duplicate execution",
                request.BatchId);
        }

        return new CommandeImportResult(
            request.BatchId,
            imported,
            imported ? "Completed" : "AlreadyCompleted");
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
