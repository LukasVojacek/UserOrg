BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143515_AddProjectResourceProviderId'
)
BEGIN
    ALTER TABLE [ProjectResource] ADD [ProviderId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143515_AddProjectResourceProviderId'
)
BEGIN
    CREATE INDEX [IX_ProjectResource_ProviderId] ON [ProjectResource] ([ProviderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143515_AddProjectResourceProviderId'
)
BEGIN
    ALTER TABLE [ProjectResource] ADD CONSTRAINT [FK_ProjectResource_Provider_ProviderId] FOREIGN KEY ([ProviderId]) REFERENCES [Provider] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143515_AddProjectResourceProviderId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260627143515_AddProjectResourceProviderId', N'10.0.9');
END;

COMMIT;
GO

