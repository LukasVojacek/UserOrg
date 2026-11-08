BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143547_AddProjectResourceProviderIdNameUniqueIndex'
)
BEGIN
    DROP INDEX [IX_ProjectResource_ProviderId] ON [ProjectResource];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143547_AddProjectResourceProviderIdNameUniqueIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProjectResource_ProviderId_Name] ON [ProjectResource] ([ProviderId], [Name]) WHERE [IsDeleted] = 0 AND [ProviderId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627143547_AddProjectResourceProviderIdNameUniqueIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260627143547_AddProjectResourceProviderIdNameUniqueIndex', N'10.0.9');
END;

COMMIT;
GO

