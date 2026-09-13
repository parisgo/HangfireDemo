SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'app') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [app] AUTHORIZATION [dbo];');
END;
GO

IF OBJECT_ID(N'[app].[CommandeImportExecution]', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[CommandeImportExecution]
    (
        [BatchId] nvarchar(100) NOT NULL,
        [RequestedBy] nvarchar(200) NOT NULL,
        [HangfireJobId] nvarchar(100) NULL,
        [CompletedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_CommandeImportExecution]
            PRIMARY KEY CLUSTERED ([BatchId])
    );

    CREATE INDEX [IX_CommandeImportExecution_CompletedAtUtc]
        ON [app].[CommandeImportExecution] ([CompletedAtUtc]);
END;
GO
