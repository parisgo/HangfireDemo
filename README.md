# HangfireDemo

一个将 HTTP 管理面与后台执行面拆分为独立进程的 .NET 8 / Hangfire 示例。

## 架构

```text
Client
  │
  ▼
HangfireDemo.Api ──► SQL Server / HangFire schema ◄── HangfireDemo.Worker
  │                         │                              │
  ├─ 鉴权与任务入队          │ 持久化队列                   ├─ default/imports Worker
  ├─ Dashboard              │                              ├─ Recurring Job 注册
  └─ Readiness              └─ app 幂等记录                └─ Core 任务与业务实现
```

- `HangfireDemo.Api`：鉴权、任务入队、Dashboard、Swagger 和健康检查；不消费任务。
- `HangfireDemo.Worker`：消费 `default` / `imports` 队列并注册定时任务；不暴露业务 API。
- `HangfireDemo.Core`：共享类库，包含业务接口、SQL Server 实现、Job、队列、定时任务和 Hangfire 公共配置。
- `HangfireDemo.Tests`：配置、任务类型兼容性及业务参数校验测试。

解决方案共四个项目，只有 Api 和 Worker 作为独立进程启动。Api、Worker 和 Tests 均引用 Core。

```text
src/
  HangfireDemo.Api/
  HangfireDemo.Worker/
  HangfireDemo.Core/
    Commandes/          # 导入接口、请求、结果和 SQL 实现
    Jobs/               # 任务入口与定时任务注册
      Configuration/    # Hangfire 配置、队列、时区与类型兼容
      Health/           # 健康检查
    DependencyInjection.cs
tests/
  HangfireDemo.Tests/
```

原 Application、Infrastructure 和 Jobs 已合并到 Core。类型解析器兼容数据库中以旧 `HangfireDemo.Jobs` 类型名保存的导入任务；新任务使用 Core 类型名。升级时应停止旧 Api 和 Worker，再一起切换到新版，避免旧 Worker 读取新版任务。

## 初始化数据库

创建数据库：

```sql
CREATE DATABASE HangfireDemo;
GO
```

开发环境将 `Hangfire:PrepareSchemaIfNecessary` 设为 `true`，Hangfire 会创建自己的 schema。生产环境默认关闭自动建表，应由部署账号预先创建 Hangfire schema，让运行账号仅保留读写权限。

随后在 `HangfireDemo` 数据库执行：

```powershell
sqlcmd -S "XYU-PC\SQL2016" -d HangfireDemo -E -i database/001_create_commande_import_execution.sql
```

`app.CommandeImportExecution` 使用 `BatchId` 主键阻止同一批次重复提交。真实导入 SQL 必须与完成标记处于同一个事务中。

## 配置

开发配置沿用本机 `XYU-PC\SQL2016` 实例；其他开发机按需修改 `appsettings.Development.json`。非开发环境不包含连接字符串或密钥，必须通过 Secret Store 或环境变量提供：

```powershell
$env:ConnectionStrings__HangfireConnection = "Server=..."
$env:ConnectionStrings__ApplicationConnection = "Server=..."
$env:Security__AdminApiKey = "至少 32 字节的随机密钥"
$env:Security__ReaderApiKey = "可选的只读 Dashboard 密钥"
$env:AllowedHosts = "hangfire.example.internal"
```

生产环境没有 `Security:AdminApiKey` 时，API 会拒绝启动。管理员密钥拥有任务操作和 Dashboard 管理权限；Reader 密钥只能查看 Dashboard。

API 支持两种凭据传递方式：

- API 客户端：`X-Hangfire-Api-Key` Header。
- Dashboard 浏览器：HTTP Basic，用户名任意，密码填写 API Key。

公司 SSO 接入时，替换 `ApiKeyAuthenticationHandler`，保留 `HangfireAdmin`、`HangfireOperator` 和 `HangfireReader` 三个角色策略即可。

## 运行两个进程

终端一：

```powershell
dotnet run --project src/HangfireDemo.Worker
```

终端二：

```powershell
dotnet run --project src/HangfireDemo.Api
```

开发地址：

- API Dashboard：`https://localhost:7180/hangfire`
- API Swagger：`https://localhost:7180/swagger`
- API readiness：`https://localhost:7180/health/ready`
- Worker readiness：`https://localhost:7181/health/ready`

Readiness 会实际查询 Hangfire 存储，并要求最近 120 秒内至少存在一个 Worker heartbeat；liveness 只表示当前进程仍在运行。

## 调用任务

```http
POST /api/jobs/import-commandes?batchId=<可选 Guid>
POST /api/jobs/import-commandes/delayed?minutes=5&batchId=<可选 Guid>
POST /api/jobs/import-commandes/trigger-recurring
```

客户端重试请求时应重复使用同一个 `batchId`。定时任务使用 Hangfire Job ID 生成稳定批次键，因此 Hangfire 自动重试不会产生新的业务批次。

定时任务 `import-commandes-daily` 默认每天巴黎时间 02:00 执行。Cron、时区、队列、Worker 数量及 heartbeat 超时均可在 Worker 的 `Hangfire` 配置节中调整。

## 构建与测试

```powershell
dotnet restore HangfireDemo.sln
dotnet build HangfireDemo.sln --no-restore
dotnet test HangfireDemo.sln --no-build
```

项目启用了 nullable、警告即错误、集中包版本、NuGet lock file，并包含 GitHub Actions 构建测试流水线。

## 生产注意事项

- `DisableConcurrentExecution` 和 Hangfire 重试不能替代业务幂等。
- Job 参数会存入 Hangfire 数据库，只应传批次 ID，不要传密码或大对象。
- 对失败数、重试数、队列长度和 Worker heartbeat 配置监控告警。
- 导入负载增长时，可以为 `imports` 队列部署独立 Worker 实例。
- `TrustServerCertificate=True` 仅用于本机开发；生产应配置并验证 SQL Server TLS 证书。
