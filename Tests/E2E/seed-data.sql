-- Idempotent seed script for E2E tests
-- Uses IF NOT EXISTS checks to avoid duplicate inserts

SET NOCOUNT ON;

-- Users
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [IdentityProviderUserId] = 'admin-sub')
BEGIN
    INSERT INTO [dbo].[Users] (IdentityProviderUserId, Email, UserName)
    VALUES ('admin-sub','admin@example.test','admin')
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [IdentityProviderUserId] = 'test-sub')
BEGIN
    INSERT INTO [dbo].[Users] (IdentityProviderUserId, Email, UserName)
    VALUES ('test-sub','user@example.test','testuser')
END

-- Projects
IF NOT EXISTS (SELECT 1 FROM [dbo].[Projects] WHERE [ProjectShortName] = 'TESTPRJ')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.Projects'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.Projects ON;
        INSERT INTO [dbo].[Projects] (Id, ProjectShortName, Name) VALUES ('11111111-1111-1111-1111-111111111111','TESTPRJ','Test project');
        SET IDENTITY_INSERT dbo.Projects OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[Projects] (Id, ProjectShortName, Name) VALUES ('11111111-1111-1111-1111-111111111111','TESTPRJ','Test project');
    END
END

-- ProjectResources
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResources] WHERE [Name] = 'res1' AND ProjectId = '11111111-1111-1111-1111-111111111111')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.ProjectResources'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.ProjectResources ON;
        INSERT INTO [dbo].[ProjectResources] (Id, Name, ProjectId) VALUES ('22222222-2222-2222-2222-222222222222','res1','11111111-1111-1111-1111-111111111111');
        SET IDENTITY_INSERT dbo.ProjectResources OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[ProjectResources] (Id, Name, ProjectId) VALUES ('22222222-2222-2222-2222-222222222222','res1','11111111-1111-1111-1111-111111111111');
    END
END

-- ProjectResourceAssignments with Specifications JSON including HEAPPE_URL
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResourceAssignments] WHERE [Id] = '33333333-3333-3333-3333-333333333333')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.ProjectResourceAssignments'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.ProjectResourceAssignments ON;
        INSERT INTO [dbo].[ProjectResourceAssignments] (Id, ProjectResourceId, Specifications)
        VALUES ('33333333-3333-3333-3333-333333333333','22222222-2222-2222-2222-222222222222', '{"HEAPPE_URL":"http://localhost:9000"}');
        SET IDENTITY_INSERT dbo.ProjectResourceAssignments OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[ProjectResourceAssignments] (Id, ProjectResourceId, Specifications)
        VALUES ('33333333-3333-3333-3333-333333333333','22222222-2222-2222-2222-222222222222', '{"HEAPPE_URL":"http://localhost:9000"}');
    END
END

-- CommandTemplate (Heappe)
IF NOT EXISTS (SELECT 1 FROM [dbo].[CommandTemplates] WHERE [Name] = 'ct1')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.CommandTemplates'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.CommandTemplates ON;
        INSERT INTO [dbo].[CommandTemplates] (Id, ProjectShortName, ProjectResourceName, Name, State)
        VALUES ('77777777-7777-7777-7777-777777777777','TESTPRJ','res1','ct1', 1);
        SET IDENTITY_INSERT dbo.CommandTemplates OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[CommandTemplates] (Id, ProjectShortName, ProjectResourceName, Name, State)
        VALUES ('77777777-7777-7777-7777-777777777777','TESTPRJ','res1','ct1', 1);
    END
END

-- Providers
IF NOT EXISTS (SELECT 1 FROM [dbo].[Providers] WHERE [Name] = 'prov1')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.Providers'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.Providers ON;
        INSERT INTO [dbo].[Providers] (Id, Name, CountryCode) VALUES ('66666666-6666-6666-6666-666666666666','prov1','CZ');
        SET IDENTITY_INSERT dbo.Providers OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[Providers] (Id, Name, CountryCode) VALUES ('66666666-6666-6666-6666-666666666666','prov1','CZ');
    END
END

-- Locations
IF NOT EXISTS (SELECT 1 FROM [dbo].[Locations] WHERE [Name] = 'loc1')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.Locations'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.Locations ON;
        INSERT INTO [dbo].[Locations] (Id, ProviderId, Name, LocationTypeId) VALUES ('88888888-8888-8888-8888-888888888888', '66666666-6666-6666-6666-666666666666','loc1', 1);
        SET IDENTITY_INSERT dbo.Locations OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[Locations] (Id, ProviderId, Name, LocationTypeId) VALUES ('88888888-8888-8888-8888-888888888888', '66666666-6666-6666-6666-666666666666','loc1', 1);
    END
END

-- ProjectTags
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectTags] WHERE [Name] = 'tag1')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.ProjectTags'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.ProjectTags ON;
        INSERT INTO [dbo].[ProjectTags] (Id, Name) VALUES ('55555555-5555-5555-5555-555555555555','tag1');
        SET IDENTITY_INSERT dbo.ProjectTags OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[ProjectTags] (Id, Name) VALUES ('55555555-5555-5555-5555-555555555555','tag1');
    END
END

-- Requests
IF NOT EXISTS (SELECT 1 FROM [dbo].[Requests] WHERE [Id] = '44444444-4444-4444-4444-444444444444')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.Requests'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.Requests ON;
        INSERT INTO [dbo].[Requests] (Id, ProjectShortName, RequestType, State) VALUES ('44444444-4444-4444-4444-444444444444','TESTPRJ',0,0);
        SET IDENTITY_INSERT dbo.Requests OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[Requests] (Id, ProjectShortName, RequestType, State) VALUES ('44444444-4444-4444-4444-444444444444','TESTPRJ',0,0);
    END
END

-- Roles and Permissions (simple entries)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Name] = 'ProjectOwner')
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.Roles'),'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.Roles ON;
        INSERT INTO [dbo].[Roles] (Id, Name) VALUES ('99999999-9999-9999-9999-999999999999','ProjectOwner');
        SET IDENTITY_INSERT dbo.Roles OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[Roles] (Id, Name) VALUES ('99999999-9999-9999-9999-999999999999','ProjectOwner');
    END
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Key] = 'perm:project:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Key], Description) VALUES ('perm:project:manage','Manage project')
END

-- Map role to permission
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE RoleId = (SELECT Id FROM [dbo].[Roles] WHERE [Name]='ProjectOwner') AND PermissionKey = 'perm:project:manage')
BEGIN
    INSERT INTO [dbo].[RolePermissions] (RoleId, PermissionKey) VALUES ((SELECT Id FROM [dbo].[Roles] WHERE [Name]='ProjectOwner'),'perm:project:manage')
END

PRINT 'Seed script executed.'
