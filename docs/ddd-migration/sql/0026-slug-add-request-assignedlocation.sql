BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627145647_AddRequestAndAssignedLocationSlug'
)
BEGIN
    ALTER TABLE [Request] ADD [Slug] nvarchar(120) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627145647_AddRequestAndAssignedLocationSlug'
)
BEGIN
    ALTER TABLE [AssignedLocation] ADD [Slug] nvarchar(120) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627145647_AddRequestAndAssignedLocationSlug'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Request_Slug] ON [Request] ([Slug]) WHERE [Slug] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627145647_AddRequestAndAssignedLocationSlug'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AssignedLocation_Slug] ON [AssignedLocation] ([Slug]) WHERE [Slug] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260627145647_AddRequestAndAssignedLocationSlug'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260627145647_AddRequestAndAssignedLocationSlug', N'10.0.9');
END;

COMMIT;
GO

