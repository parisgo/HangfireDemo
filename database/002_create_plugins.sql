SET XACT_ABORT ON;
GO
IF SCHEMA_ID(N'app') IS NULL EXEC(N'CREATE SCHEMA [app] AUTHORIZATION [dbo];');
GO
IF OBJECT_ID(N'app.PluginVersion', N'U') IS NULL
CREATE TABLE app.PluginVersion (
    Sequence bigint IDENTITY PRIMARY KEY,
    PluginId nvarchar(64) NOT NULL,
    Version nvarchar(64) NOT NULL,
    Manifest nvarchar(max) NOT NULL,
    Directory nvarchar(1024) NOT NULL,
    Status nvarchar(20) NOT NULL DEFAULT N'Pending',
    Error nvarchar(4000) NULL,
    UploadedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_PluginVersion UNIQUE (PluginId, Version)
);
IF OBJECT_ID(N'app.PluginActive', N'U') IS NULL
CREATE TABLE app.PluginActive (
    PluginId nvarchar(64) NOT NULL PRIMARY KEY,
    Sequence bigint NOT NULL REFERENCES app.PluginVersion(Sequence)
);
IF OBJECT_ID(N'app.PluginWorkerStatus', N'U') IS NULL
CREATE TABLE app.PluginWorkerStatus (
    WorkerId nvarchar(200) NOT NULL,
    PluginId nvarchar(64) NOT NULL,
    Version nvarchar(64) NOT NULL,
    Status nvarchar(20) NOT NULL,
    Error nvarchar(4000) NULL,
    HeartbeatUtc datetime2 NOT NULL,
    CONSTRAINT PK_PluginWorkerStatus PRIMARY KEY (WorkerId, PluginId, Version)
);
GO
