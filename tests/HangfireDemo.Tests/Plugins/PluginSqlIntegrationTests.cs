using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace HangfireDemo.Tests.Plugins;

public sealed class PluginSqlFactAttribute : FactAttribute
{
    public PluginSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HANGFIRE_PLUGIN_TEST_SQL")))
            Skip = "Set HANGFIRE_PLUGIN_TEST_SQL to a SQL Server connection with CREATE DATABASE permission.";
    }
}

public sealed class PluginSqlIntegrationTests
{
    [PluginSqlFact]
    [Trait("Category", "SqlIntegration")]
    public async Task UploadHotUpgrade_AllSchedulingModes_AndPermissions()
    {
        var database = "HangfirePluginE2E_" + Guid.NewGuid().ToString("N");
        var baseConnection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HANGFIRE_PLUGIN_TEST_SQL")) { InitialCatalog = "master" };
        var connection = new SqlConnectionStringBuilder(baseConnection.ConnectionString) { InitialCatalog = database };
        var root = Path.Combine(PluginFixtures.RepoRoot, "artifacts", "sql-e2e", database);
        Directory.CreateDirectory(root);
        var apiKey = Guid.NewGuid().ToString("N");
        var readerKey = Guid.NewGuid().ToString("N");
        HostedProcess? api = null, worker = null;
        await Sql(baseConnection.ConnectionString, $"CREATE DATABASE [{database}]");
        try
        {
            foreach (var script in new[] { "001_create_commande_import_execution.sql", "002_create_plugins.sql" })
                foreach (var sql in Regex.Split(await File.ReadAllTextAsync(Path.Combine(PluginFixtures.RepoRoot, "database", script)), @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(sql)) await Sql(connection.ConnectionString, sql);
            api = Start("Api", root, connection.ConnectionString, apiKey, readerKey);
            using var http = new HttpClient { BaseAddress = new Uri(api.Url), Timeout = TimeSpan.FromSeconds(15) };
            await Until(async () => { try { return (await http.GetAsync("/health/live")).IsSuccessStatusCode; } catch (HttpRequestException) { return false; } });
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/plugins")).StatusCode);
            http.DefaultRequestHeaders.Add("X-Hangfire-Api-Key", readerKey);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/plugins")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/webapi-jobs/executions", new { name = "Named WebAPI job", url = api.Url + "/health/live", mode = "Fire-and-forget" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/plugin-jobs/reports/send-report/executions", new { name = "Named plugin job", mode = "Fire-and-forget", parameters = new { } })).StatusCode);
            using (var file = UploadBody("1.0.0")) Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/api/plugins", file)).StatusCode);
            http.DefaultRequestHeaders.Remove("X-Hangfire-Api-Key");
            http.DefaultRequestHeaders.Add("X-Hangfire-Api-Key", apiKey);

            using (var browser = new HttpClient { BaseAddress = http.BaseAddress })
            {
                browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + apiKey)));
                var page = await browser.GetStringAsync("/job-manager");
                Assert.Contains("job-form", page);
                Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/webapi-jobs/executions", new { name = "Named WebAPI job", url = api.Url + "/health/live", mode = "Fire-and-forget" })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/plugin-jobs/reports/send-report/executions", new { name = "Named plugin job", mode = "Fire-and-forget", parameters = new { } })).StatusCode);
                var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
                Assert.NotEmpty(token);
                browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(token));
                Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsJsonAsync("/api/plugin-jobs/reports/send-report/executions", new { name = "Named plugin job", mode = "Fire-and-forget", parameters = new { } })).StatusCode);
            }

            using (var file = UploadBody("1.0.0")) await Expect(await http.PostAsync("/api/plugins", file), HttpStatusCode.Accepted);
            var pending = await http.GetFromJsonAsync<JsonElement>("/api/plugins");
            Assert.Equal("Pending", pending.GetProperty("versions")[0].GetProperty("status").GetString());
            worker = Start("Worker", root, connection.ConnectionString, apiKey, readerKey);
            await Until(async () => (await http.GetFromJsonAsync<JsonElement>("/api/plugin-jobs")).EnumerateArray().Any(x => x.GetProperty("version").GetString() == "1.0.0"));

            async Task<string> Submit(string mode, string name, int seconds = 0, string? failOnVersion = null)
            {
                var response = await http.PostAsJsonAsync("/api/plugin-jobs/reports/send-report/executions", new
                {
                    name, mode, delayMinutes = mode == "Delayed" ? (int?)1 : null,
                    parameters = new { reportName = name, recipient = "demo@example.com", simulationSeconds = seconds, failOnVersion }
                });
                await Expect(response, HttpStatusCode.Accepted);
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
            }
            async Task<bool> Succeeded(string id) => (string?)await Sql(connection.ConnectionString,
                "SELECT StateName FROM HangFire.Job WHERE Id=" + long.Parse(id)) == "Succeeded";

            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/webapi-jobs/executions", new { name = "Named WebAPI job", url = "file:///test", mode = "Fire-and-forget" })).StatusCode);
            foreach (var invalidName in new string?[] { null, "", "   ", new string('x', 201) })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/webapi-jobs/executions",
                    new { name = invalidName, url = api.Url + "/health/live", mode = "Fire-and-forget" })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/plugin-jobs/reports/send-report/executions",
                    new { name = invalidName, mode = "Fire-and-forget", parameters = new { } })).StatusCode);
            }
            async Task<string> SubmitWeb(string mode)
            {
                var response = await http.PostAsJsonAsync("/api/webapi-jobs/executions", new { name = "Named WebAPI job", url = api.Url + "/health/live", mode, delayMinutes = mode == "Delayed" ? (int?)1 : null });
                await Expect(response, HttpStatusCode.Accepted);
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
            }
            var webImmediate = await SubmitWeb("Fire-and-forget");
            var webDelayed = await SubmitWeb("Delayed");
            await Until(async () => await Succeeded(webImmediate));
            await Expect(await http.PutAsJsonAsync("/api/webapi-schedules/health-check", new { name = "Named WebAPI job", url = api.Url + "/health/live", cron = "* * * * *", timeZone = "Europe/Paris" }), HttpStatusCode.OK);
            await Expect(await http.PostAsync("/api/webapi-schedules/health-check/trigger", null), HttpStatusCode.Accepted);

            var retryId = await Submit("Fire-and-forget", "retry", failOnVersion: "1.0.0");
            await Until(async () => (string?)await Sql(connection.ConnectionString, "SELECT StateName FROM HangFire.Job WHERE Id=" + long.Parse(retryId)) == "Scheduled");
            var runningId = await Submit("Fire-and-forget", "running-old", 15);
            await Until(() => Task.FromResult(worker.Log.Contains("v1.0.0 开始执行：[Report] running-old")));
            var runningList = await http.GetFromJsonAsync<JsonElement>("/api/plugin-executions");
            var runningRow = runningList.EnumerateArray().Single(x => x.GetProperty("id").GetString() == runningId);
            Assert.Equal("Processing", runningRow.GetProperty("state").GetString());
            Assert.False(runningRow.GetProperty("canRestart").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(runningRow.GetProperty("name").GetString()));
            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/plugin-executions/{runningId}/restart", new { expectedState = "Processing" })).StatusCode);
            using (var reader = new HttpClient { BaseAddress = http.BaseAddress })
            {
                reader.DefaultRequestHeaders.Add("X-Hangfire-Api-Key", readerKey);
                Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/plugin-executions")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"/api/plugin-executions/{runningId}/restart", new { expectedState = "Processing" })).StatusCode);
                using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/plugin-executions/{runningId}") { Content = JsonContent.Create(new { expectedState = "Processing" }) };
                Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(deleteRequest)).StatusCode);
            }
            var queuedId = await Submit("Fire-and-forget", "queued-new");
            var delayedId = await Submit("Delayed", "delayed-new");
            await Expect(await http.PutAsJsonAsync("/api/plugin-schedules/e2e-report", new
            { name = "Named recurring report", pluginId = "reports", jobId = "send-report", parameters = new { reportName = "recurring-new", recipient = "demo@example.com", simulationSeconds = 0 }, cron = "* * * * *", timeZone = "Europe/Paris" }), HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync("/api/plugin-schedules/invalid", new
            { name = "Named recurring report", pluginId = "reports", jobId = "send-report", parameters = new { }, cron = "90 * * * *", timeZone = "Europe/Paris" })).StatusCode);

            using (var file = UploadBody("2.0.0")) await Expect(await http.PostAsync("/api/plugins", file), HttpStatusCode.Accepted);
            await Until(async () => (await http.GetFromJsonAsync<JsonElement>("/api/plugin-jobs")).EnumerateArray().Any(x => x.GetProperty("version").GetString() == "2.0.0"));
            await Expect(await http.PostAsync("/api/plugin-schedules/e2e-report/trigger", null), HttpStatusCode.Accepted);
            await Until(async () => await Succeeded(runningId) && await Succeeded(queuedId) && await Succeeded(retryId) && await Succeeded(delayedId), 110);
            await Until(() => Task.FromResult(worker.Log.Contains("v2.0.0 执行完成：[Report] recurring-new")));
            await Until(async () => Convert.ToInt32(await Sql(connection.ConnectionString,
                "SELECT COUNT(*) FROM HangFire.JobParameter p JOIN HangFire.Job j ON j.Id=p.JobId WHERE p.Name=N'RecurringJobId' AND p.Value=N'\"plugin:e2e-report\"' AND j.StateName=N'Succeeded'")) >= 2);
            Assert.Contains("v1.0.0 执行完成：[Report] running-old", worker.Log);
            await Until(async () => await Succeeded(webDelayed));
            var webRows = (await http.GetFromJsonAsync<JsonElement>("/api/plugin-executions")).EnumerateArray().Where(x => x.GetProperty("jobKind").GetString() == "WebAPI").ToArray();
            Assert.Contains(webRows, x => x.GetProperty("id").GetString() == webDelayed && x.GetProperty("taskType").GetString() == "Delayed");
            Assert.Contains(webRows, x => x.GetProperty("taskType").GetString() == "Recurring" && x.GetProperty("state").GetString() == "Succeeded");
            Assert.Contains("Named WebAPI job", await http.GetStringAsync("/hangfire/jobs/succeeded"));
            await Expect(await http.PostAsJsonAsync($"/api/plugin-executions/{webImmediate}/restart", new { expectedState = "Succeeded" }), HttpStatusCode.Accepted);
            await Until(async () => Convert.ToInt32(await Sql(connection.ConnectionString, "SELECT COUNT(*) FROM HangFire.State WHERE JobId=" + long.Parse(webImmediate) + " AND Name=N'Succeeded'")) >= 2);
            using (var deleteWeb = new HttpRequestMessage(HttpMethod.Delete, $"/api/plugin-executions/{webImmediate}") { Content = JsonContent.Create(new { expectedState = "Succeeded" }) })
                await Expect(await http.SendAsync(deleteWeb), HttpStatusCode.NoContent);
            await Expect(await http.DeleteAsync("/api/webapi-schedules/health-check"), HttpStatusCode.NoContent);
            foreach (var name in new[] { "queued-new", "delayed-new", "retry" }) Assert.Contains("v2.0.0 执行完成：[Report] " + name, worker.Log);
            using (var file = UploadBody("2.0.0")) Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsync("/api/plugins", file)).StatusCode);
            var schedule = await http.GetFromJsonAsync<JsonElement>("/api/plugin-schedules/e2e-report");
            Assert.Equal("Europe/Paris", schedule.GetProperty("timeZone").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync("/api/plugin-schedules/e2e-report")).StatusCode);
            var typedExecutions = (await http.GetFromJsonAsync<JsonElement>("/api/plugin-executions")).EnumerateArray().ToArray();
            foreach (var id in new[] { runningId, queuedId, retryId })
                Assert.Equal("Fire-and-forget", typedExecutions.Single(x => x.GetProperty("id").GetString() == id).GetProperty("taskType").GetString());
            Assert.Equal("Delayed", typedExecutions.Single(x => x.GetProperty("id").GetString() == delayedId).GetProperty("taskType").GetString());
            Assert.Contains(typedExecutions, x => x.GetProperty("taskType").GetString() == "Recurring");

            using (var package = new MultipartFormDataContent())
            {
                package.Add(new StreamContent(File.OpenRead(Path.Combine(PluginFixtures.RepoRoot, "artifacts", "plugins", "import-commandes-1.0.0.zip"))), "file", "import.zip");
                await Expect(await http.PostAsync("/api/plugins", package), HttpStatusCode.Accepted);
            }
            await Until(async () => (await http.GetFromJsonAsync<JsonElement>("/api/plugin-jobs")).EnumerateArray().Any(x => x.GetProperty("pluginId").GetString() == "commandes"));
            var importBatch = Guid.NewGuid();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var response = await http.PostAsJsonAsync("/api/plugin-jobs/commandes/import-commandes/executions", new
                { name = "Import job", mode = "Fire-and-forget", batchId = importBatch, parameters = new { connectionName = "ApplicationConnection" } });
                await Expect(response, HttpStatusCode.Accepted);
                var importJob = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
                await Until(async () => await Succeeded(importJob));
            }
            Assert.Equal(1, Convert.ToInt32(await Sql(connection.ConnectionString,
                "SELECT COUNT(*) FROM app.CommandeImportExecution WHERE BatchId=N'" + importBatch.ToString("N") + "'")));
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/jobs")).StatusCode);

            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/plugin-executions/{queuedId}/restart", new { expectedState = "Failed" })).StatusCode);
            await Expect(await http.PostAsJsonAsync($"/api/plugin-executions/{queuedId}/restart", new { expectedState = "Succeeded" }), HttpStatusCode.Accepted);
            await Until(async () => Convert.ToInt32(await Sql(connection.ConnectionString, "SELECT COUNT(*) FROM HangFire.State WHERE JobId=" + long.Parse(queuedId) + " AND Name=N'Succeeded'")) >= 2);
            using (var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/plugin-executions/{queuedId}") { Content = JsonContent.Create(new { expectedState = "Succeeded" }) })
                await Expect(await http.SendAsync(deleteRequest), HttpStatusCode.NoContent);
            var deletedList = await http.GetFromJsonAsync<JsonElement>("/api/plugin-executions");
            Assert.DoesNotContain(deletedList.EnumerateArray(), x => x.GetProperty("id").GetString() == queuedId);
            var deletedDashboard = await http.GetStringAsync("/hangfire/jobs/deleted");
            Assert.DoesNotContain("PluginJobRunner.ExecuteAsync", deletedDashboard);
            Assert.DoesNotContain("PluginJobRunner.ExecuteAsync", deletedDashboard);
            await Expect(await http.PostAsJsonAsync($"/api/plugin-executions/{queuedId}/restart", new { expectedState = "Deleted" }), HttpStatusCode.Accepted);
            await Until(async () => Convert.ToInt32(await Sql(connection.ConnectionString, "SELECT COUNT(*) FROM HangFire.State WHERE JobId=" + long.Parse(queuedId) + " AND Name=N'Succeeded'")) >= 3);
            Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/api/plugin-executions/99999999/restart", new { expectedState = "Failed" })).StatusCode);

            worker.Dispose();
            await File.WriteAllTextAsync(Path.Combine(root, "worker-before-restart.log"), worker.Log);
            worker = Start("Worker", root, connection.ConnectionString, apiKey, readerKey);
            var restarted = await Submit("Fire-and-forget", "restarted");
            await Until(async () => await Succeeded(restarted));
            Assert.Contains("v2.0.0 执行完成：[Report] restarted", worker.Log);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "logs"), "Api-*.log"));
            var workerLogFiles = Directory.GetFiles(Path.Combine(root, "logs"), "Worker-*.log");
            Assert.NotEmpty(workerLogFiles);
            var workerFileLog = string.Join("\n", workerLogFiles.Select(path =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }));
            Assert.Contains("v2.0.0 执行完成：[Report] restarted", workerFileLog);
            Assert.Contains("HangfireJobId", workerFileLog);
            var pluginStore = new HangfireDemo.Core.Plugins.SqlPluginStore(connection.ConnectionString);
            var reportVersions = (await pluginStore.ListAsync(default)).Where(x => x.Manifest.Id == "reports").ToArray();
            var oldVersion = reportVersions.Single(x => x.Manifest.Version == "1.0.0");
            var activeVersion = reportVersions.Single(x => x.Manifest.Version == "2.0.0");
            using (var reader = new HttpClient { BaseAddress = new Uri(api.Url) })
            {
                reader.DefaultRequestHeaders.Add("X-Hangfire-Api-Key", readerKey);
                Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"/api/plugins/{activeVersion.Sequence}")).StatusCode);
            }
            await Expect(await http.DeleteAsync($"/api/plugins/{oldVersion.Sequence}"), HttpStatusCode.NoContent);
            Assert.Equal(activeVersion.Sequence, (await pluginStore.ActiveAsync("reports", default))!.Sequence);
            await Expect(await http.DeleteAsync($"/api/plugins/{activeVersion.Sequence}"), HttpStatusCode.NoContent);
            // A Worker holding a stale discovery result must not reactivate a deleted version.
            await pluginStore.ActivateAsync(activeVersion, default);
            await pluginStore.ActivateAsync(oldVersion, default);
            Assert.Null(await pluginStore.ActiveAsync("reports", default));
            var remainingPlugins = await http.GetFromJsonAsync<JsonElement>("/api/plugins");
            Assert.DoesNotContain(remainingPlugins.GetProperty("versions").EnumerateArray(), x => x.GetProperty("id").GetString() == "reports");
            Assert.DoesNotContain((await http.GetFromJsonAsync<JsonElement>("/api/plugin-jobs")).EnumerateArray(), x => x.GetProperty("pluginId").GetString() == "reports");
            Assert.True(Directory.Exists(activeVersion.Directory));
            await Expect(await http.DeleteAsync($"/api/plugins/{activeVersion.Sequence}"), HttpStatusCode.NoContent);
            Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync("/api/plugins/99999999")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/api/plugin-jobs/reports/send-report/executions",
                new { name = "Named plugin job", mode = "Fire-and-forget", parameters = new { } })).StatusCode);
        }
        finally
        {
            if (api is not null) { api.Dispose(); await File.WriteAllTextAsync(Path.Combine(root, "api.log"), api.Log); }
            if (worker is not null) { worker.Dispose(); await File.WriteAllTextAsync(Path.Combine(root, "worker.log"), worker.Log); }
            SqlConnection.ClearAllPools();
            await Sql(baseConnection.ConnectionString, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }

    private static MultipartFormDataContent UploadBody(string version)
    {
        var form = new MultipartFormDataContent(); form.Add(new StreamContent(File.OpenRead(PluginFixtures.Package(version))), "file", "plugin.zip"); return form;
    }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode expected)
        => Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    private static async Task Until(Func<Task<bool>> condition, int seconds = 40)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds)) { if (await condition()) return; await Task.Delay(500); }
        Assert.Fail("Timed out waiting for plugin integration state.");
    }
    private static async Task<object?> Sql(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection); return await command.ExecuteScalarAsync();
    }
    private static HostedProcess Start(string project, string root, string connection, string key, string readerKey)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var url = "http://localhost:" + port;
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.Combine(PluginFixtures.RepoRoot, "src", "HangfireDemo." + project) };
        info.ArgumentList.Add(Path.Combine(info.WorkingDirectory, "bin", "Release", "net8.0", "HangfireDemo." + project + ".dll"));
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; info.Environment["ASPNETCORE_URLS"] = url;
        info.Environment["ConnectionStrings__HangfireConnection"] = connection;
        info.Environment["ConnectionStrings__ApplicationConnection"] = connection;
        info.Environment["Plugins__Directory"] = Path.Combine(root, "plugins");
        info.Environment["Logging__File__Directory"] = Path.Combine(root, "logs");
        info.Environment["Logging__Seq__Enabled"] = "false";
        info.Environment["Security__AdminApiKey"] = key; info.Environment["Security__ReaderApiKey"] = readerKey;
        info.Environment["Hangfire__WorkerCount"] = "1";
        return new HostedProcess(info, url);
    }
    private sealed class HostedProcess : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder log = new();
        public string Url { get; }
        public string Log { get { lock (log) return log.ToString(); } }
        public HostedProcess(ProcessStartInfo info, string url)
        {
            Url = url; process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        public void Dispose() { if (!process.HasExited) process.Kill(entireProcessTree: true); process.WaitForExit(); }
    }
}
