using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace HangfireDemo.Core.Plugins;

public interface IPluginStore
{
    Task<IReadOnlyList<PluginVersion>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(PluginManifest manifest, string directory, CancellationToken cancellationToken);
    Task<PluginVersion?> ActiveAsync(string pluginId, CancellationToken cancellationToken);
    Task ActivateAsync(PluginVersion version, CancellationToken cancellationToken);
    Task DeleteAsync(long sequence, CancellationToken cancellationToken);
    Task FailAsync(PluginVersion version, string error, CancellationToken cancellationToken);
    Task ReportAsync(PluginVersion version, string workerId, string status, string? error, CancellationToken cancellationToken);
    Task<IReadOnlyList<PluginWorkerStatus>> WorkersAsync(CancellationToken cancellationToken);
}

public sealed class SqlPluginStore(string connectionString) : IPluginStore
{
    private static void Parameter(SqlCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string, object?)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters) Parameter(command, name, value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<PluginVersion>> ReadAsync(string sql, string? id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        if (id is not null) Parameter(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<PluginVersion>();
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetInt64(0), PluginValidation.ReadManifest(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return list;
    }

    public Task<IReadOnlyList<PluginVersion>> ListAsync(CancellationToken ct) => ReadAsync(
        "SELECT Sequence, Manifest, Directory, Status, Error FROM app.PluginVersion ORDER BY Sequence", null, ct);

    public async Task<PluginVersion?> ActiveAsync(string pluginId, CancellationToken ct) =>
        (await ReadAsync("SELECT v.Sequence,v.Manifest,v.Directory,v.Status,v.Error FROM app.PluginVersion v JOIN app.PluginActive a ON a.Sequence=v.Sequence WHERE a.PluginId=@id", pluginId, ct)).SingleOrDefault();

    public async Task AddAsync(PluginManifest manifest, string directory, CancellationToken ct)
    {
        try
        {
            await ExecuteAsync("INSERT INTO app.PluginVersion(PluginId,Version,Manifest,Directory) VALUES(@id,@version,@manifest,@directory)", ct,
                ("@id", manifest.Id), ("@version", manifest.Version),
                ("@manifest", JsonSerializer.Serialize(manifest, PluginValidation.Json)), ("@directory", directory));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        { throw new PluginConflictException("This plugin version already exists. Upload a new version."); }
    }

    public Task ActivateAsync(PluginVersion version, CancellationToken ct) => ExecuteAsync("""
        SET XACT_ABORT ON;
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        BEGIN TRAN;
        DECLARE @current bigint;
        SELECT @current=Sequence FROM app.PluginActive WITH(UPDLOCK,HOLDLOCK) WHERE PluginId=@id;
        IF NOT EXISTS (SELECT 1 FROM app.PluginVersion WHERE Sequence=@sequence AND Status IN (N'Pending',N'Active',N'Superseded'))
        BEGIN
            COMMIT;
            RETURN;
        END;
        IF @current IS NULL
            INSERT INTO app.PluginActive(PluginId,Sequence) VALUES(@id,@sequence);
        ELSE IF @current < @sequence
            UPDATE app.PluginActive SET Sequence=@sequence WHERE PluginId=@id;
        UPDATE v SET Status=CASE WHEN a.Sequence=v.Sequence THEN N'Active' ELSE N'Superseded' END, Error=NULL
        FROM app.PluginVersion v JOIN app.PluginActive a ON a.PluginId=v.PluginId
        WHERE v.PluginId=@id AND (v.Sequence=@sequence OR v.Status=N'Active');
        COMMIT;
        """, ct, ("@id", version.Manifest.Id), ("@sequence", version.Sequence));

    public async Task DeleteAsync(long sequence, CancellationToken ct)
    {
        try
        {
            await ExecuteAsync("""
                SET XACT_ABORT ON;
                SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                DECLARE @id nvarchar(64), @current bigint;
                SELECT @id=PluginId FROM app.PluginVersion WHERE Sequence=@sequence;
                IF @id IS NULL THROW 50001, 'Plugin version not found.', 1;
                BEGIN TRAN;
                SELECT @current=Sequence FROM app.PluginActive WITH(UPDLOCK,HOLDLOCK) WHERE PluginId=@id;
                DELETE FROM app.PluginActive WHERE PluginId=@id AND Sequence=@sequence;
                UPDATE app.PluginVersion SET Status=N'Deleted', Error=NULL WHERE Sequence=@sequence;
                COMMIT;
                """, ct, ("@sequence", sequence));
        }
        catch (SqlException ex) when (ex.Number == 50001)
        { throw new PluginNotFoundException("Plugin version not found."); }
    }

    public Task FailAsync(PluginVersion version, string error, CancellationToken ct) => ExecuteAsync(
        "UPDATE app.PluginVersion SET Status=N'Failed', Error=@error WHERE Sequence=@sequence AND Status=N'Pending'", ct,
        ("@sequence", version.Sequence), ("@error", error[..Math.Min(error.Length, 4000)]));

    public Task ReportAsync(PluginVersion version, string workerId, string status, string? error, CancellationToken ct) => ExecuteAsync("""
        SET XACT_ABORT ON;
        BEGIN TRAN;
        UPDATE app.PluginWorkerStatus WITH(UPDLOCK,SERIALIZABLE)
        SET Status=@status,Error=@error,HeartbeatUtc=SYSUTCDATETIME()
        WHERE WorkerId=@worker AND PluginId=@id AND Version=@version;
        IF @@ROWCOUNT=0 INSERT INTO app.PluginWorkerStatus VALUES(@worker,@id,@version,@status,@error,SYSUTCDATETIME());
        COMMIT;
        """, ct, ("@worker", workerId), ("@id", version.Manifest.Id), ("@version", version.Manifest.Version),
        ("@status", status), ("@error", error is null ? null : error[..Math.Min(error.Length, 4000)]));

    public async Task<IReadOnlyList<PluginWorkerStatus>> WorkersAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT WorkerId,PluginId,Version,Status,Error,HeartbeatUtc FROM app.PluginWorkerStatus", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<PluginWorkerStatus>();
        while (await reader.ReadAsync(ct)) list.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), DateTime.SpecifyKind(reader.GetDateTime(5),DateTimeKind.Utc)));
        return list;
    }
}
