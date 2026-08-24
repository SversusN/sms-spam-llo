-- Скрипт для создания таблиц очереди SMS и лога в базе SmsRecipesApp
-- Выполните вручную на вашем SQL Server

IF COL_LENGTH(N'[dbo].[SmsLog]', N'DeliveryStatus') IS NULL
BEGIN
    ALTER TABLE [dbo].[SmsLog] ADD [DeliveryStatus] [nvarchar](50) NULL;
END;
GO

IF OBJECT_ID(N'[dbo].[Consent]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Consent](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [UserGuid] [uniqueidentifier] NOT NULL,
        [PatientSnils] [nvarchar](50) NOT NULL,
        [PatientName] [nvarchar](500) NOT NULL,
        [BirthDate] [date] NULL,
        [Phone] [nvarchar](50) NULL,
        [IsConsentGiven] [bit] NOT NULL DEFAULT (0),
        [ConsentType] [nvarchar](200) NULL,
        [CreatedAt] [datetime] NOT NULL DEFAULT (GETDATE()),
        [RevokedAt] [datetime] NULL,
        CONSTRAINT [PK_Consent] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE NONCLUSTERED INDEX [IX_Consent_UserGuid] ON [dbo].[Consent]([UserGuid] ASC);
    CREATE NONCLUSTERED INDEX [IX_Consent_PatientSnils] ON [dbo].[Consent]([PatientSnils] ASC);
    CREATE NONCLUSTERED INDEX [IX_Consent_CreatedAt] ON [dbo].[Consent]([CreatedAt] DESC);
END;
GO

IF COL_LENGTH(N'[dbo].[Consent]', N'RevokedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[Consent] ADD [RevokedAt] [datetime] NULL;
END;
GO

IF OBJECT_ID(N'[dbo].[SmsQueue]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SmsQueue](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [RecipeId] [int] NOT NULL,
        [UserGuid] [uniqueidentifier] NOT NULL,
        [IndividualSnils] [nvarchar](50) NULL,
        [CreatedAt] [datetime] NOT NULL,
        [Status] [nvarchar](50) NOT NULL DEFAULT ('Pending'),
        [ErrorMessage] [nvarchar](max) NULL,
        [ProcessedAt] [datetime] NULL,
        CONSTRAINT [PK_SmsQueue] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE NONCLUSTERED INDEX [IX_SmsQueue_Status] ON [dbo].[SmsQueue]([Status] ASC);
    CREATE NONCLUSTERED INDEX [IX_SmsQueue_RecipeId_Status] ON [dbo].[SmsQueue]([RecipeId] ASC, [Status] ASC);
    CREATE NONCLUSTERED INDEX [IX_SmsQueue_UserGuid] ON [dbo].[SmsQueue]([UserGuid] ASC);
END;

IF OBJECT_ID(N'[dbo].[SmsLog]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SmsLog](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [RecipeId] [int] NOT NULL,
        [UserGuid] [uniqueidentifier] NOT NULL,
        [IndividualSnils] [nvarchar](50) NULL,
        [Phone] [nvarchar](50) NOT NULL,
        [Message] [nvarchar](max) NOT NULL,
        [Status] [nvarchar](50) NOT NULL,
        [ProviderResponse] [nvarchar](max) NULL,
        [DeliveryStatus] [nvarchar](50) NULL,
        [CreatedAt] [datetime] NOT NULL,
        CONSTRAINT [PK_SmsLog] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE NONCLUSTERED INDEX [IX_SmsLog_UserGuid] ON [dbo].[SmsLog]([UserGuid] ASC);
    CREATE NONCLUSTERED INDEX [IX_SmsLog_CreatedAt] ON [dbo].[SmsLog]([CreatedAt] DESC);
    CREATE NONCLUSTERED INDEX [IX_SmsLog_RecipeId] ON [dbo].[SmsLog]([RecipeId] ASC);
END;
GO
