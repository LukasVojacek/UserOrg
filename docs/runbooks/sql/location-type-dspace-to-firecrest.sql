-- Run before deploying the release that renames ELocationType 6 from DSpace to FirecREST.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM [LocationType] WHERE [Id] = 0)
BEGIN
    SET IDENTITY_INSERT [LocationType] ON;
    INSERT INTO [LocationType] ([Id], [Name], [Description]) VALUES (0, N'Unspecified', NULL);
    SET IDENTITY_INSERT [LocationType] OFF;
END;

IF EXISTS (SELECT 1 FROM [LocationType] WHERE [Id] = 6 AND [Name] = N'DSpace')
BEGIN
    UPDATE [Location] SET [LocationTypeId] = 0 WHERE [LocationTypeId] = 6;
    UPDATE [LocationType] SET [Name] = N'FirecREST', [Description] = NULL WHERE [Id] = 6;
END;

COMMIT TRANSACTION;
