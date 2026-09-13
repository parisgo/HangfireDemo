# AGENTS.md

## 适用范围与协作

本文件适用于整个仓库。开发前阅读相关源码和现有测试；功能及部署说明见 [README.md](README.md)（英语）和 [README.zh-CN.md](README.zh-CN.md)（中文）。

- 默认使用中文与用户沟通，按用户要求完成实现、必要验证和文档更新。
- 保留工作区已有修改，不将与当前任务无关的内容回退、重写或提交。
- 优先沿用现有实现和项目约定，避免为小功能引入新框架或无关重构。
- 本项目按 Windows、PowerShell、SQL Server 环境开发；不要使用 Docker 启动或部署服务。
- 不在源码、文档或输出中写入真实密钥。机器路径、数据库实例和监听地址以当前配置为准，不将开发者本机值当作通用默认值。

## 项目与架构边界

平台有四个主项目，解决方案还包含示例项目：

| 路径 | 职责 |
|---|---|
| `src/HangfireDemo.Api` | 鉴权、上传、通用调度接口、Razor Pages、Dashboard |
| `src/HangfireDemo.Worker` | Hangfire Server、插件发现和执行 |
| `src/HangfireDemo.Core` | 插件契约及通用加载、执行、调度、存储、日志能力 |
| `tests/HangfireDemo.Tests` | 单元测试、真实插件测试、SQL 集成测试 |
| `examples/SendReport.Plugin` | 报表模拟插件 |
| `examples/ReportFormatting` | 报表插件的私有托管依赖 |
| `examples/ImportCommandes.Plugin` | 订单导入业务入口、事务和批次去重 |
| `examples/WebApiDemo` | 独立 WeatherForecast WebAPI 示例 |

Core 可以包含平台通用实现，但不能添加 `ImportCommandeJob`、`SendReportJob` 等具体业务实现，也不能引用业务插件程序集。具体功能放在插件项目中。添加普通插件功能不应要求修改 Api 的 Controller 或宿主功能目录源码。

订单导入示例的真实业务 SQL 仍是预留位置；不要将其描述为已经完成的生产订单导入。

## 插件契约与热加载

- 使用 `IPluginJob.ExecuteAsync(JobExecutionContext, JsonElement, CancellationToken)`，稳定功能标识为 `pluginId/jobId`。
- 插件面向 .NET 8，入口公开、非抽象并有无参构造函数；依赖和接口校验沿用现有加载器。
- ZIP 根目录包含 `plugin.json`、入口 DLL、`.deps.json` 和私有托管依赖。示例引用匹配的 Core Release DLL，保持 `Private=false` 和现有打包方式。
- Api 只校验、保存包及元数据，不执行插件代码。Worker 使用独立 `AssemblyLoadContext` / `AssemblyDependencyResolver`，共享宿主 Core 契约。
- Api 与 Worker 共用同一绝对插件目录和 SQL 存储。Worker 每五秒发现版本，校验成功后事务切换激活状态；失败保留原激活版本。
- 每次执行或重试开始时使用当时最新激活版本，本次执行固定实例。正在执行的任务继续旧版；排队、延迟和下次周期执行使用新版。
- 不覆盖同版本目录，不强制热卸载。删除插件版本采用逻辑删除，保留文件；删除激活版本不自动回退旧版，也不自动删除周期计划。
- 保留路径穿越、链接、原生依赖、包大小及重复版本校验。当前范围是可信托管插件，加载上下文不是安全沙箱。
- 插件验证业务 JSON 字段、响应取消并通过异常报告失败。业务幂等由插件负责。
- 数据库插件通过 `context.CreateConnection` 获取连接，负责打开和释放；任务参数传连接名称，不传连接字符串。

## 调度与持久化兼容

- Hangfire 保存宿主通用 Runner 调用，不保存插件具体 CLR 类型。修改方法签名必须兼容数据库中已有调用；必要时保留旧重载并添加序列化回归测试。
- 新建任务/更新计划的 `name` 必填，最多 200 字符，拒绝全空白。名称在任务列表和 Dashboard 显示，并在重试、重新入队后保留。
- 保留 Fire-and-forget、Delayed、Recurring 三种方式。延迟范围 1–1440 分钟；Cron 为五段，默认时区 `Europe/Paris`；统一使用 `default` 队列。
- 周期计划由 Hangfire 存储，不增加第二套调度状态表。周期每次执行生成独立批次标识，同次重试保持标识。
- 默认最多重试三次，间隔 30/60/120 秒。异常应交给 Hangfire，不能记录后吞掉并返回成功。
- 重启任务保留原 Hangfire ID 和参数，禁止重复启动正在运行/排队的任务。重启和删除沿用 `expectedState` 并发校验。
- 已删除执行在任务管理列表隐藏；删除不撤销已有业务结果，也不删除周期计划。
- WebAPI 任务使用 `IHttpClientFactory` 发出无请求体的 GET，30 秒超时、最多五次重定向，非 2xx 失败。保留 HTTPS 证书验证，不能使用全局证书绕过来修复开发证书问题。
- 旧内置 `/api/jobs` 和业务类型解析已移除；不要重新引入它们。通用 Runner 旧签名兼容与旧业务类兼容是两件不同的事。

## 页面与权限

- 页面位于 `src/HangfireDemo.Api/Pages`，脚本及样式位于 `src/HangfireDemo.Api/wwwroot`。
- `/job-manager` 显示任务列表，通过弹窗创建任务；`/plugin-library` 为独立列表页面，通过弹窗上传插件。
- 任务列表当前覆盖最近 100 条，默认每页 20 条，每五秒刷新。变更列表时保留分页、刷新后的页码校正和权限状态。
- 保持中英文支持，默认英语；新增静态及动态文案同步更新 `i18n.js`。语言选择器位于按钮行最右侧，使用 `data-language-picker`，不要误用通用样式类选择分页下拉框。
- 上传成功关闭弹窗并刷新列表，失败保留输入和错误反馈。保留原生表单验证、label、键盘关闭与焦点行为。
- Reader 读取，Operator/Admin 操作任务，Admin 上传/删除插件。服务端执行权限检查，不能仅隐藏按钮。
- 浏览器写操作保留防伪令牌校验，API Key 客户端沿用现有认证路径。当前配置提供 Admin/Reader 密钥，Admin 满足 Operator 权限。

## 日志

- 业务与插件使用 `Microsoft.Extensions.Logging.ILogger` 或 `context.Logger`，保留结构化消息模板及异常对象，不用字符串拼接替代结构化属性。
- 使用 `AddApplicationLogging` 注册宿主日志。Seq 通过 `Seq.Extensions.Logging.AddSeq()` 直接接入；Serilog provider 单独用于滚动文件，不改成 Serilog Seq sink。
- Api、Worker、WebApiDemo 是独立日志生产者。插件日志属于 Worker，WeatherForecast Controller 日志属于 WebApiDemo；调用方不会自动收集被调用进程的内部日志。
- 文件默认按天/50 MB 滚动并保留 31 个文件，级别遵循 `Logging:LogLevel`。开发日志位于 `App_Data/logs`，注意现有 Api/Worker 配置中的绝对路径。
- Seq 使用本机 Windows 服务，地址和 API Key 通过 `Logging:Seq` 配置。排查缺失日志时检查生产进程、级别、文件、Seq 配置和筛选范围；不能将“构建通过”当作“事件已在 Seq 入库”的证据。

## 构建与验证

SDK 以 `global.json` 为准（当前 8.0.204，允许最新补丁）。`Directory.Build.props` 启用 nullable、隐式 using、警告视为错误和依赖锁文件；依赖版本集中在 `Directory.Packages.props`。

在仓库根目录运行：

```powershell
dotnet restore HangfireDemo.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File examples/pack-send-report.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File examples/pack-import-commandes.ps1
dotnet build HangfireDemo.sln -c Release --no-restore
dotnet test HangfireDemo.sln -c Release --no-build
```

- 按修改范围选择验证，不为纯文档、简单样式变更强制运行整套数据库测试。
- 修改包版本时正常还原并检查锁文件变化，再验证 locked restore。不要手工编造锁文件内容。
- 两个插件分别打包，默认输出 `artifacts/plugins`，支持 `-OutputDirectory`。不要重新合并为一个插件打包脚本。
- 修改 Runner/契约后，先构建对应 Api 和 Worker，再运行依赖子进程的集成测试，避免测试旧二进制。
- Windows 下不要并发执行写入相同输出目录的 build/test；等待前一进程完成，以免 DLL 锁定。
- 关注 `CoreBoundaryTests`、`JobNameTests`、插件验证/运行时测试、`WebApiJobTests` 和日志测试等相关回归。

SQL 集成测试默认跳过。使用有建库权限的测试连接启用：

```powershell
$env:HANGFIRE_PLUGIN_TEST_SQL = 'Server=YOUR_SERVER;Integrated Security=True;TrustServerCertificate=True'
dotnet test HangfireDemo.sln -c Release --no-build --filter Category=SqlIntegration
```

测试创建并清理独立的 `HangfirePluginE2E_<Guid>` 数据库，启动隔离的 Api/Worker，包含真实一分钟调度。不要改为操作现有业务数据库。输出在 `artifacts/sql-e2e` 和 `artifacts/plugin-tests`。

本地运行：

```powershell
dotnet run --project src/HangfireDemo.Worker
dotnet run --project src/HangfireDemo.Api
dotnet run --project examples/WebApiDemo --launch-profile https
```

每个宿主使用独立终端。测试预览使用独立端口，结束后只关闭自己启动的进程，不中断用户正在调试的服务。配置/宿主代码变更后的重启要求应在交付中说明。

## 数据库与文档维护

- 插件表迁移为 `database/002_create_plugins.sql`；业务导入表为 `database/001_create_commande_import_execution.sql`。分清平台数据库与业务数据库，新增迁移应保留已有数据并可明确部署。
- `README.md` 是完整英文文档，`README.zh-CN.md` 是对应中文文档；功能、接口、配置和部署说明变更时同步维护。
- `docs/plugins.md` 仅保留双语 README 导航入口，不重新维护一份重复插件说明。
- README 截图使用 `docs/doc_1_Job_manager.jpg`、`docs/doc_2_hangfire.jpg`、`docs/doc_3_seq.jpg` 的仓库相对路径。
- 文档修改检查本地链接、目录锚点、JSON 示例和双语接口一致性。新增任务请求示例必须包含 `name`。
- 最终说明实际修改、验证结果和未验证的限制；不要声称未执行的测试或未确认的运行结果。
