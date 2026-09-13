# HangfireDemo

[English](README.md) | 简体中文

基于 .NET 8、Hangfire 和 SQL Server 的 DLL 插件及 HTTP GET 任务平台。上传插件 ZIP 后即可配置任务，无需修改宿主源码。管理页面支持英语（默认）和中文。

## 目录

- [界面预览](#界面预览)
- [项目结构](#项目结构)
- [启动与配置](#启动与配置)
- [管理页面](#管理页面)
- [插件开发与打包](#插件开发与打包)
- [调度接口](#调度接口)
- [热升级与插件删除](#热升级与插件删除)
- [WebAPI 任务](#webapi-任务)
- [日志与 Seq](#日志与-seq)
- [构建与测试](#构建与测试)
- [从内置任务迁移](#从内置任务迁移)

## 界面预览

### 任务管理

![任务管理](docs/doc_1_Job_manager.jpg)

### Hangfire Dashboard

![Hangfire Dashboard](docs/doc_2_hangfire.jpg)

### Seq 日志

![Seq 日志](docs/doc_3_seq.jpg)

## 项目结构

平台由四个主项目组成；当前解决方案还包含便于开发、调试的示例项目。

| 项目 | 职责 |
|---|---|
| `src/HangfireDemo.Api` | 身份验证、插件上传、通用调度接口、Razor Pages 和 Hangfire Dashboard |
| `src/HangfireDemo.Worker` | 托管 Hangfire Server，发现插件并执行任务 |
| `src/HangfireDemo.Core` | 插件契约、通用执行器、加载、调度、存储、日志和健康检查 |
| `tests/HangfireDemo.Tests` | 平台边界、插件加载及 SQL Server 集成测试 |

Core 不包含订单导入、报表等具体业务任务，也不引用业务插件程序集。

| 示例 | 用途 |
|---|---|
| `examples/SendReport.Plugin` | 模拟报表，只记录日志，不发送邮件或生成报表文件 |
| `examples/ReportFormatting` | 报表插件使用的私有托管依赖 |
| `examples/ImportCommandes.Plugin` | 导入入口、SQL 事务和批次去重；真实订单导入 SQL 仍需业务实现 |
| `examples/WebApiDemo` | WeatherForecast 接口，用于验证 WebAPI 任务 |

Api 将请求保存到 Hangfire SQL 存储。Worker 中的 Hangfire Server 消费 `default` 队列，通过通用执行器完成任务。延迟和周期调度由 Hangfire 管理；插件只实现业务行为，不写死执行时间。

## 启动与配置

准备 [global.json](global.json) 指定的 .NET SDK、SQL Server，以及打包使用的 PowerShell。Api 与 Worker 同机部署并共用插件目录。Seq 为可选服务，本项目使用本机 Windows 服务方式运行。

1. 创建 `HangfireDemo` 数据库，执行 [database/002_create_plugins.sql](database/002_create_plugins.sql)。脚本可重复执行，创建 `app.PluginVersion`、`app.PluginActive`、`app.PluginWorkerStatus`，不会迁移已有任务。
2. Api 与 Worker 配置相同的 `ConnectionStrings:HangfireConnection`。开发环境启用 `Hangfire:PrepareSchemaIfNecessary`，允许 Hangfire 初始化自身表结构。生产默认为 `false`，应先完成 Hangfire 表结构部署，再使用受限运行账户启动。
3. 两个宿主的 `Plugins:Directory` 必须指向同一个绝对目录。现有开发配置包含本机路径和 SQL 实例，复制仓库后需要修改。Api 需要目录写权限，Worker 需要读权限；运行账户需要访问 Hangfire 存储和插件表。
4. 使用订单导入示例时，配置 Worker 的 `ConnectionStrings:ApplicationConnection`，并在其业务数据库执行 [database/001_create_commande_import_execution.sql](database/001_create_commande_import_execution.sql)。
5. 非 Development 环境配置 `Security:AdminApiKey`。只读账户使用 `Security:ReaderApiKey`，部署到其他主机名时调整 `AllowedHosts`。

对应环境变量：

| 环境变量 | 用途 |
|---|---|
| `ConnectionStrings__HangfireConnection` | Api/Worker 共用的 Hangfire 与插件数据库 |
| `ConnectionStrings__ApplicationConnection` | Worker 订单导入示例的业务数据库 |
| `Plugins__Directory` | 共用的绝对插件目录 |
| `Security__AdminApiKey` | Api 管理员凭据 |
| `Security__ReaderApiKey` | Api 只读凭据 |
| `Logging__File__Directory` | 可写日志目录 |
| `Logging__Seq__ServerUrl` | Seq 接收地址 |
| `Logging__Seq__ApiKey` | Seq 接收日志所需的 API Key（如启用） |

在仓库根目录打开两个终端，分别运行：

```powershell
dotnet run --project src/HangfireDemo.Worker
dotnet run --project src/HangfireDemo.Api
```

使用现有 HTTPS 启动配置时，访问[任务管理](https://localhost:7180/job-manager)、[插件库](https://localhost:7180/plugin-library)或 [Hangfire Dashboard](https://localhost:7180/hangfire)。如果启动配置不同，以宿主输出的监听地址为准。

浏览器采用 HTTP Basic：密码填 API Key，用户名不用于选择角色。HTTP 客户端发送 `X-Hangfire-Api-Key`。Development 环境两个密钥均为空时，使用开发管理员身份。当前密钥处理器提供 Admin 和 Reader 身份，Admin 同时满足 Operator 权限。

浏览器写操作必须携带页面生成的防伪令牌 `X-CSRF-TOKEN`；通过 API Key 请求头认证的客户端使用其密钥。Reader 可读取，Operator/Admin 可调度和管理执行，上传与删除插件仅允许 Admin。

## 管理页面

任务管理首页显示执行列表。插件库为独立页面，支持搜索、状态筛选、分页和“添加插件”上传弹窗。上传成功后关闭弹窗并刷新列表。Worker 异步加载：**待加载**表示文件已保存但尚未加载，**已激活**表示可使用，**加载失败**显示错误。Worker 离线时保持待加载，恢复后自动处理。

创建任务时必须填写**任务名**（最多 200 个字符，不能全为空白），选择**插件任务**或 **WebAPI 任务**，再选择执行方式：

| 执行方式 | 行为 |
|---|---|
| Fire-and-forget | 入队后，由可用 Worker 尽快执行一次 |
| Delayed | 延迟 1–1440 分钟后执行一次 |
| Recurring | 按五段 Cron 和时区重复执行，默认时区为 `Europe/Paris` |

插件任务需要选择功能并填写 JSON 对象；WebAPI 任务需要 URL，无 JSON 参数。自定义任务名保存在 Hangfire 调用参数中，用于任务列表和 Dashboard，重试与重新入队保留名称。已有任务继续使用原名称回退规则。

执行列表显示最近 100 条插件/WebAPI 执行记录，每 5 秒刷新，默认每页 20 条，可选 20/50/100。已删除任务不显示。周期计划产生的每次执行作为独立任务显示；计划定义通过 Dashboard 或接口管理，任务管理页不再单独显示周期计划列表。

- **重启**：将同一个 Hangfire 任务重新入队，保留参数和批次 ID。业务去重可能跳过已处理批次。运行中、已排队或等待前置任务的执行不能重启。
- **删除**：将执行置为 Hangfire Deleted 状态。运行中任务通过取消令牌协作停止，不撤销已有业务结果，也不删除周期计划。Hangfire 自动过期前，Deleted 任务仍可能通过 Dashboard/API 重新入队。
- 操作时状态已变化会返回 HTTP 409，应刷新后重试。

## 插件开发与打包

插件目标框架为 `net8.0`，入口类公开、非抽象，具有无参构造函数，并实现对应版本 Core 契约：

```csharp
public interface IPluginJob
{
    Task ExecuteAsync(JobExecutionContext context,
        JsonElement parameters, CancellationToken cancellationToken);
}
```

`context` 提供 `JobId`、`BatchId`、`RequestedBy`、`Logger` 和 `CreateConnection`。插件验证业务字段，通过异常报告失败，并响应取消。实现 `IDisposable` 或 `IAsyncDisposable` 的实例在执行结束后由宿主释放。不要保留执行上下文/参数，也不要启动超出任务生命周期的后台线程。

`context.CreateConnection("ApplicationConnection")` 返回未打开的 `DbConnection`，由插件打开和释放。Worker 从连接字符串配置解析该名称。任务参数传连接名称，不传连接字符串。业务 SQL 位于插件中，SQL 驱动由宿主提供。

示例通过 `<Reference>` 引用 Release 构建的 `HangfireDemo.Core.dll`，设置 `Private=false`，避免复制宿主的全部依赖。`<EnableDynamicLoading>true</EnableDynamicLoading>` 生成依赖描述。私有托管依赖随插件发布，插件应引用与宿主匹配的 Core 版本。

ZIP 根目录结构：

```text
plugin.json
SendReport.Plugin.dll
SendReport.Plugin.deps.json
ReportFormatting.dll
```

`plugin.json` 示例：

```json
{
  "id": "reports",
  "version": "1.0.0",
  "entryAssembly": "SendReport.Plugin.dll",
  "jobs": [{
    "id": "send-report",
    "name": "Send report (simulation)",
    "type": "SendReport.Plugin.SendReport",
    "parametersExample": {
      "reportName": "Monthly report",
      "recipient": "demo@example.com",
      "simulationSeconds": 1
    }
  }]
}
```

一个 DLL 可以提供多个任务，稳定功能标识为 `pluginId/jobId`。ID 使用 1–64 位小写字母、数字或连字符，首位不能为连字符；版本使用 `1.0.0` 等语义版本。升级应保持任务 ID 和参数契约兼容。清单中的 `name` 是功能名称，请求中的 `name` 是本次创建任务的名称。

分别运行两个插件的打包脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File examples/pack-send-report.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File examples/pack-import-commandes.ps1
```

默认输出到 `artifacts/plugins`，两个脚本都支持 `-OutputDirectory`。报表脚本当前生成 `1.0.0`、`2.0.0`、`2.0.1`、`2.0.2` 版本；导入脚本生成 `import-commandes-1.0.0.zip`。每个脚本先构建 Core，再发布插件。

报表参数 `reportName`、`recipient` 必填。可选 `simulationSeconds` 范围为 0–30，默认 1。测试参数 `failOnVersion: "1.0.0"` 使指定版本模拟失败，用于演示升级后重试。示例只记录日志，不发送邮件。

`commandes/import-commandes` 使用参数 `{"connectionName":"ApplicationConnection"}`。批次记录、事务和去重由插件实现，真实订单导入 SQL 仍需补充。系统不会自动注册每日导入计划。

## 调度接口

下列任务创建/计划更新请求均需携带 `name`。插件参数必须是合法 JSON 对象，序列化文本最多 64K 字符，业务字段在插件执行时验证。

| 接口 | 权限 | 用途 |
|---|---|---|
| `POST /api/plugins` | Admin | multipart 字段 `file` 上传 ZIP，202 表示待加载 |
| `GET /api/plugins` | Reader/Operator/Admin | 版本、Worker 状态、心跳和加载错误 |
| `DELETE /api/plugins/{sequence}` | Admin | 逻辑删除一个插件版本 |
| `GET /api/plugin-jobs` | Reader/Operator/Admin | 已激活功能及 JSON 示例 |
| `POST /api/plugin-jobs/{pluginId}/{jobId}/executions` | Operator/Admin | 立即或延迟执行插件任务 |
| `GET /api/plugin-executions` | Reader/Operator/Admin | 最近的插件与 WebAPI 执行记录 |
| `POST /api/plugin-executions/{id}/restart` | Operator/Admin | 重新入队 |
| `DELETE /api/plugin-executions/{id}` | Operator/Admin | 删除执行 |
| `GET /api/plugin-schedules` | Reader/Operator/Admin | 插件周期计划列表 |
| `GET /api/plugin-schedules/{scheduleId}` | Reader/Operator/Admin | 读取单个插件计划 |
| `PUT /api/plugin-schedules/{scheduleId}` | Operator/Admin | 创建或覆盖插件计划 |
| `DELETE /api/plugin-schedules/{scheduleId}` | Operator/Admin | 删除未来周期 |
| `POST /api/plugin-schedules/{scheduleId}/trigger` | Operator/Admin | 立即触发一次 |
| `POST /api/webapi-jobs/executions` | Operator/Admin | 立即或延迟执行 HTTP GET |
| `PUT /api/webapi-schedules/{scheduleId}` | Operator/Admin | 创建或覆盖 HTTP GET 计划 |
| `DELETE /api/webapi-schedules/{scheduleId}` | Operator/Admin | 删除 HTTP GET 计划 |
| `POST /api/webapi-schedules/{scheduleId}/trigger` | Operator/Admin | 立即触发 HTTP GET 计划 |

立即执行插件请求：

```json
{
  "name": "Daily report",
  "mode": "Fire-and-forget",
  "parameters": {"reportName": "Daily report", "recipient": "demo@example.com"}
}
```

延迟执行改为 `"mode":"Delayed"`，添加 `"delayMinutes":1`（范围 1–1440）。插件执行请求可携带可选 GUID `batchId`；提供批次 ID 不代表平台自动对业务操作去重。

插件周期计划请求：

```json
{
  "name": "Daily report at 09:00",
  "pluginId": "reports",
  "jobId": "send-report",
  "parameters": {"reportName": "Daily report", "recipient": "demo@example.com"},
  "cron": "0 9 * * *",
  "timeZone": "Europe/Paris"
}
```

Cron 使用五段，省略时区默认巴黎。Hangfire 按分钟检查周期计划，队列负载可能使实际开始时间晚于计划时间。所有平台任务进入 `default` 队列。计划定义只保存在 Hangfire，分别使用 `plugin:` 或 `webapi:` 前缀。

重启/删除执行请求携带当前观测状态，例如 `{"expectedState":"Succeeded"}`。删除周期计划不会取消已入队的执行。

常见响应：400 请求/包无效；401 未认证；403 权限不足；404 任务未知/未激活或计划不存在；409 版本重复或状态冲突。上传已接受后仍可能在 Worker 校验时失败，应查询状态和错误。

## 热升级与插件删除

Api 检查 ZIP 并解压到 `Plugins:Directory` 下的独立目录，记录 Pending，不执行插件代码。安装目录包含插件/版本标识和唯一后缀，原始 ZIP 不保留。Worker 每 5 秒检查一次，通过独立 `AssemblyLoadContext` 和 `AssemblyDependencyResolver` 加载各版本，共享宿主 Core 契约程序集。

依赖和接口验证成功后，通过 SQL 事务切换激活状态。加载失败保留旧激活版本。“最新”依据成功激活和上传序列确定，不按语义版本号大小比较。

`PluginJobRunner` 保存稳定 ID、JSON 和请求上下文，不保存插件 CLR 类型：

- 每次执行或重试开始时读取当前激活版本，本次尝试固定使用该实例。
- 正在执行的任务继续旧版；排队、延迟、重试及周期的下一次执行使用新版。
- 参数契约不兼容或功能已删除会明确失败，不静默回退。
- 自动重试最多 3 次，间隔为 30/60/120 秒。
- 周期每次执行根据 Hangfire Job ID 生成新批次 ID，同次执行重试保留批次 ID。业务幂等由插件实现。
- Worker 重启后从 SQL 恢复激活版本，不强制卸载程序集或自动清理旧版本。

删除插件使用版本记录的 `sequence`，不是插件 ID。版本标记为 Deleted 并从插件库隐藏。删除激活版本会移除激活指针，不自动切回旧版。运行中实例继续执行；没有激活版本时，后续尝试会失败。已有周期计划需另行删除，上传新版本可恢复功能。

DLL 目录和已删除版本记录保留。同一已删除版本不能重新上传，Worker 也不会重新激活它。上传时数据库写入失败可能留下未登记目录，但不会被执行。人工清理前先核对数据库中的目录引用，不要删除仍被引用的目录。

默认限制：ZIP 50 MB、解压后 200 MB、最多 2000 个归档条目。拒绝路径穿越、链接、原生 DLL 和重复版本，插件目录不能包含文件系统链接。`Plugins:MaxUploadBytes`、`Plugins:MaxExtractedBytes` 可调小限制。

只支持可信的 .NET 8 托管插件。插件具有 Worker 进程权限，依赖隔离不是安全沙箱。不支持多机插件分发、任意 DLL 方法调用、原生依赖或不可信第三方代码执行。

## WebAPI 任务

填写任务名和完整 HTTP/HTTPS URL。Worker 发送 GET，无请求体、JSON 参数或自定义请求头，不保存响应体。

```json
{
  "name": "Weather check",
  "url": "https://localhost:7062/WeatherForecast",
  "mode": "Fire-and-forget"
}
```

周期 HTTP 任务向 `/api/webapi-schedules/{scheduleId}` PUT 包含 `name`、`url`、`cron`、`timeZone` 的请求体。延迟任务使用 `mode: "Delayed"` 和 `delayMinutes`。

请求超时为 30 秒，最多跟随 5 次重定向，包括 HTTP 到 HTTPS。仅 2xx 成功，网络、TLS 或非 2xx 错误沿用最多 3 次重试。目标接口应能处理重复调用。`localhost` 指 Worker 所在机器。HTTPS 证书必须被 Worker 运行账户信任，平台不跳过验证。

单独启动示例：

```powershell
dotnet run --project examples/WebApiDemo --launch-profile https
```

本机 HTTPS 出现 `UntrustedRoot` 时，在对应开发账户下信任 ASP.NET Core 开发证书：

```powershell
dotnet dev-certs https --trust
```

重启相关宿主后重新入队失败任务。若 Worker 使用不同的服务账户运行，需要为该账户配置适当的证书信任。

## 日志与 Seq

Api、Worker 和 WebApiDemo 保留控制台日志，通过共享注册写入滚动文件和 Seq。Core/插件使用宿主 `ILogger`，`context.Logger` 由 Worker 输出。WebAPI Controller 日志属于 WebApiDemo，不属于调用它的 Worker。

开发环境文件位于 `App_Data/logs`：`Api-yyyyMMdd.log`、`Worker-yyyyMMdd.log`、`WebApiDemo-yyyyMMdd.log`。Api/Worker 开发路径为本机绝对路径，移动仓库后需修改。生产默认使用各宿主内容目录下的 `Logs`，可通过 `Logging__File__Directory` 指定可写绝对目录。

文件按天和 50 MB 滚动，保留 31 个文件（不一定是 31 天）。通过 `Logging:File:FileSizeLimitBytes`、`Logging:File:RetainedFileCountLimit` 调整。日志包含时间、级别、类别、结构化参数、上下文和异常，过滤遵循 `Logging:LogLevel`，默认 Information。

Seq 链路为 `Microsoft.Extensions.Logging` → `Seq.Extensions.Logging.AddSeq()`。Serilog 独立用于滚动文件，不参与 Seq 传输。

```json
{
  "Logging": {
    "LogLevel": {"Default": "Information"},
    "Seq": {
      "Enabled": true,
      "ServerUrl": "http://localhost:5341",
      "ApiKey": ""
    }
  }
}
```

使用本机安装的 Windows Seq 服务，访问 [Seq](http://localhost:5341/)。确认服务已启动，并在每个宿主分别配置地址/API Key，无需 Docker。设置 `Logging__Seq__Enabled=false` 可关闭 Seq，文件日志继续保留。网络批量发送失败不阻止本地文件输出，内存待发事件不是持久离线队列。

可搜索消息文本，或使用 `PluginId = 'reports'`、`HangfireJobId = '42'`、`SourceContext` 等属性。命名任务的执行器还在日志作用域中附加 `JobName`。找不到日志时：

1. 确认产生该日志的进程及其 Seq 是否启用。
2. 修改代码/配置后重启该进程，再次执行接口/任务；旧的未发送日志不会补传。
3. 检查本地文件、Information 级别过滤、Seq 地址/API Key，以及 Seq 时间范围和筛选条件。

天气日志需要重启 WebApiDemo，再调用 WeatherForecast 并搜索 `天气预报`。能收到 Worker 日志不代表 WebAPI 宿主已发送自己的事件。

## 构建与测试

在仓库根目录运行：

```powershell
dotnet restore HangfireDemo.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File examples/pack-send-report.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File examples/pack-import-commandes.ps1
dotnet build HangfireDemo.sln -c Release --no-restore
dotnet test HangfireDemo.sln -c Release --no-build
```

未设置 `HANGFIRE_PLUGIN_TEST_SQL` 时跳过 SQL 端到端测试。启用时使用有建库权限的测试账户：

```powershell
$env:HANGFIRE_PLUGIN_TEST_SQL = 'Server=YOUR_SERVER;Integrated Security=True;TrustServerCertificate=True'
dotnet test HangfireDemo.sln -c Release --no-build --filter Category=SqlIntegration
```

测试创建独立 `HangfirePluginE2E_<Guid>` 数据库，启动隔离 Api/Worker，结束后删除测试数据库，不操作已有业务数据库。包含真实一分钟调度，通常需要 1–2 分钟。日志/输出保留在 `artifacts/sql-e2e`，本地插件测试输出位于 `artifacts/plugin-tests`。

覆盖包校验、依赖加载、接口错误、激活/恢复、重试/升级的版本选择、权限/防伪、调度、命名/旧调用兼容、WebAPI 行为、日志及业务导入回归。

## 从内置任务迁移

旧 `/api/jobs`、具体业务 Job 类和旧业务类型解析器已移除，使用上述插件/WebAPI 接口。

从内置业务任务升级前，停止旧 Api 提交，处理完绑定旧 CLR 类型的任务，并删除旧 `import-commandes-daily` 周期计划。同步升级 Api/Worker，上传插件并重建周期计划。历史记录不会改写；旧失败业务任务需要重新提交为插件任务，不能对已移除的类直接重试。

任务名更新后，已持久化的通用 `PluginJobRunner` 和 `WebApiJobRunner` 旧签名仍可执行，新请求必须携带 `name`。宿主代码/契约变更需要同步更新并重启 Api/Worker；上传兼容的新插件版本无需重启。
