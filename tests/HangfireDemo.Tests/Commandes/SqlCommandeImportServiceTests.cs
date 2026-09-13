using ImportCommandes.Plugin.Commandes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HangfireDemo.Tests.Commandes;

public sealed class SqlCommandeImportServiceTests
{
    [Fact]
    public async Task ImportAsync_BatchIdLongerThanDatabaseLimit_FailsBeforeConnecting()
    {
        var service = new SqlCommandeImportService(
            () => throw new InvalidOperationException("Validation must run before opening a connection."),
            NullLogger<SqlCommandeImportService>.Instance);
        var request = new CommandeImportRequest(
            new string('x', 101),
            "test-user",
            "test-job");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ImportAsync(request, CancellationToken.None));
    }
}
