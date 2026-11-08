-- Idempotent seed script for E2E tests
-- Uses IF NOT EXISTS checks to avoid duplicate inserts
-- Table names are SINGULAR (EF Core convention without pluralisation)
-- Batches are separated by GO so SET IDENTITY_INSERT session flags work correctly.

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- =====================================================================
-- Users
-- Project owner user maps to Keycloak user "test" (password: test)
-- Project manager user maps to Keycloak user "test2" (password: test)
-- Project member user maps to Keycloak user "test3" (password: test)
-- =====================================================================
-- A caller that reaches an endpoint before this script runs is provisioned just-in-time by the
-- gateway: the same `sub`, a freshly generated Id. UX_User_IdentityProviderUserId is unique on
-- IdentityProviderUserId alone - AssignedProviderId and Id are included columns, not key columns - so
-- the INSERT below does not create a second identity, it collides and takes the whole seed down with
-- it. The ids in this block are named verbatim by later statements and cannot move, so the
-- just-in-time row is removed and the canonical one written in its place. Such a row only ever carries
-- a baseline permission; anything else attached to it makes the delete fail on its foreign key, which
-- is the right outcome - the seed stops instead of discarding data somebody produced.
DECLARE @seededIdentities TABLE (Id UNIQUEIDENTIFIER, IdpId NVARCHAR(64));

INSERT INTO @seededIdentities (Id, IdpId) VALUES
    ('AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA', '__PROJECT_OWNER_IDP_ID__'),
    ('BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB', '__PROJECT_MANAGER_IDP_ID__');

-- Only when it is a third identity: the member and the manager are the same Keycloak user in some
-- runs, and two rows sharing an IdpId would make each one's Id look foreign to the other.
IF '__PROJECT_MEMBER_IDP_ID__' <> '__PROJECT_MANAGER_IDP_ID__'
BEGIN
    INSERT INTO @seededIdentities (Id, IdpId) VALUES
        ('CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC', '__PROJECT_MEMBER_IDP_ID__');
END

DELETE up
FROM [dbo].[UserPermission] up
INNER JOIN [dbo].[User] u ON u.[Id] = up.[UserId]
INNER JOIN @seededIdentities s ON s.[IdpId] = u.[IdentityProviderUserId]
WHERE u.[Id] <> s.[Id]
  AND NOT EXISTS (SELECT 1 FROM @seededIdentities x WHERE x.[Id] = u.[Id]);

DELETE u
FROM [dbo].[User] u
INNER JOIN @seededIdentities s ON s.[IdpId] = u.[IdentityProviderUserId]
WHERE u.[Id] <> s.[Id]
  AND NOT EXISTS (SELECT 1 FROM @seededIdentities x WHERE x.[Id] = u.[Id]);

IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [Id] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[User] (Id, IdentityProviderUserId, Email, UserName)
    VALUES ('AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA','__PROJECT_OWNER_IDP_ID__','test@test.cz','__PROJECT_OWNER_IDP_ID__')
END

UPDATE [dbo].[User]
SET [IdentityProviderUserId] = '__PROJECT_OWNER_IDP_ID__',
    [Email] = 'test@test.cz',
    [UserName] = '__PROJECT_OWNER_IDP_ID__'
WHERE [Id] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA';

IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [Id] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[User] (Id, IdentityProviderUserId, Email, UserName)
    VALUES ('BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB','__PROJECT_MANAGER_IDP_ID__','test2@test.cz','__PROJECT_MANAGER_IDP_ID__')
END

UPDATE [dbo].[User]
SET [IdentityProviderUserId] = '__PROJECT_MANAGER_IDP_ID__',
    [Email] = 'test2@test.cz',
    [UserName] = '__PROJECT_MANAGER_IDP_ID__'
WHERE [Id] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB';

IF '__PROJECT_MEMBER_IDP_ID__' <> '__PROJECT_MANAGER_IDP_ID__'
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [Id] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC')
    BEGIN
        INSERT INTO [dbo].[User] (Id, IdentityProviderUserId, Email, UserName)
        VALUES ('CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC','__PROJECT_MEMBER_IDP_ID__','test3@test.cz','__PROJECT_MEMBER_IDP_ID__')
    END

    UPDATE [dbo].[User]
    SET [IdentityProviderUserId] = '__PROJECT_MEMBER_IDP_ID__',
        [Email] = 'test3@test.cz',
        [UserName] = '__PROJECT_MEMBER_IDP_ID__'
    WHERE [Id] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC';
END

GO

-- =====================================================================
-- UserPreference — the opaque per-user blob read by GET /api/User/Preferences (v1)
-- and GET /api/v2/users/me/preferences (v2). Without a row both stacks coalesce to
-- "{}" and UserParityTests compares an empty object against an empty object; the
-- payload below is nested and non-alphabetically ordered so nesting, key order and
-- value types are actually exercised. The per-user "owner" marker differs so a v2
-- query that resolved the wrong user is observable too.
-- The platform-admin user (DDDD…) is created after this script runs, so its row is
-- seeded by GatewayAttachFixture.EnsurePlatformAdminAccessAsync.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[UserPreference] WHERE [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[UserPreference] (Id, UserId, Data)
    VALUES ('EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEE1','AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
        '{"theme":"dark","dashboard":{"layout":{"columns":3,"compact":true},"widgets":["projects","resources","requests"]},"notifications":{"email":true,"digest":"weekly","mutedUntil":null},"owner":"e2e-owner"}')
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[UserPreference] WHERE [UserId] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[UserPreference] (Id, UserId, Data)
    VALUES ('EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEE2','BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB',
        '{"theme":"dark","dashboard":{"layout":{"columns":3,"compact":true},"widgets":["projects","resources","requests"]},"notifications":{"email":true,"digest":"weekly","mutedUntil":null},"owner":"e2e-manager"}')
END

IF EXISTS (SELECT 1 FROM [dbo].[User] WHERE [Id] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC')
   AND NOT EXISTS (SELECT 1 FROM [dbo].[UserPreference] WHERE [UserId] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC')
BEGIN
    INSERT INTO [dbo].[UserPreference] (Id, UserId, Data)
    VALUES ('EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEE3','CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC',
        '{"theme":"dark","dashboard":{"layout":{"columns":3,"compact":true},"widgets":["projects","resources","requests"]},"notifications":{"email":true,"digest":"weekly","mutedUntil":null},"owner":"e2e-member"}')
END

GO

-- =====================================================================
-- Lookup: Country (Id=1 = BD/Bangladesh from UserOrgDataSeed.json)
-- Core API seeds this on startup; inserted here as a fallback guard.
-- =====================================================================
SET IDENTITY_INSERT [dbo].[Country] ON

IF NOT EXISTS (SELECT 1 FROM [dbo].[Country] WHERE [Id] = 1)
    INSERT INTO [dbo].[Country] (Id, Name, Description) VALUES (1, 'BD', 'Bangladesh')

SET IDENTITY_INSERT [dbo].[Country] OFF

GO

-- =====================================================================
-- Lookup: LocationType (Id=1 = HPC)
-- =====================================================================
SET IDENTITY_INSERT [dbo].[LocationType] ON

IF NOT EXISTS (SELECT 1 FROM [dbo].[LocationType] WHERE [Id] = 1)
    INSERT INTO [dbo].[LocationType] (Id, Name) VALUES (1, 'HPC')

SET IDENTITY_INSERT [dbo].[LocationType] OFF

GO

-- =====================================================================
-- Lookup: AllocationUnitType (Id=2 = node-hours, required by AssignedLocation)
-- =====================================================================
SET IDENTITY_INSERT [dbo].[AllocationUnitType] ON

IF NOT EXISTS (SELECT 1 FROM [dbo].[AllocationUnitType] WHERE [Id] = 2)
    INSERT INTO [dbo].[AllocationUnitType] (Id, Name, Description) VALUES (2, 'node-hours', 'Unit for HPC resources')

SET IDENTITY_INSERT [dbo].[AllocationUnitType] OFF

GO

-- =====================================================================
-- Lookup: ProjectDomainType (Id=1 = Agricultural...)
-- =====================================================================
SET IDENTITY_INSERT [dbo].[ProjectDomainType] ON

IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectDomainType] WHERE [Id] = 1)
    INSERT INTO [dbo].[ProjectDomainType] (Id, Name) VALUES (1, 'Agricultural and Veterinary Sciences')

SET IDENTITY_INSERT [dbo].[ProjectDomainType] OFF

GO

-- =====================================================================
-- Owner user holds the Admin system role (T-059-03), ONLY when the caller sets
-- __SEED_OWNER_AS_ADMIN__ to 'true': the `aspire run` db-seed-e2e route has no separate
-- platform-admin bootstrap the way GatewayAttachFixture.EnsurePlatformAdminAccessAsync gives the
-- E2E suite. The E2E fixture never substitutes this placeholder, so its auth matrix (Owner -> 403
-- on provider-scoped writes) is unchanged.
-- =====================================================================
IF '__SEED_OWNER_AS_ADMIN__' = 'true'
BEGIN
    DECLARE @adminSystemRoleId INT = (
        SELECT TOP (1) r.[Id]
        FROM [forge].[Roles] r
        WHERE r.[Name] = 'Admin');

    IF @adminSystemRoleId IS NULL
    BEGIN
        THROW 50002, 'Admin role was not found in forge.Roles for local seed.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM [dbo].[UserPermission]
        WHERE [ProjectId] IS NULL
          AND [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
    BEGIN
        INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
        VALUES (NEWID(), NULL, 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA', @adminSystemRoleId);
    END
    ELSE
    BEGIN
        UPDATE [dbo].[UserPermission]
        SET [SystemRoleId] = @adminSystemRoleId
        WHERE [ProjectId] IS NULL
          AND [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA';
    END;
END;

GO

-- =====================================================================
-- Provider (CountryId=1 -> BD/Bangladesh)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[Provider] WHERE [Id] = '66666666-6666-6666-6666-666666666666')
BEGIN
    INSERT INTO [dbo].[Provider] (Id, Name, CountryId, SupportEmail)
    VALUES ('66666666-6666-6666-6666-666666666666', 'prov1', 1, 'support@prov1.example.test')
END

UPDATE [dbo].[Provider]
SET [SupportEmail] = 'support@prov1.example.test'
WHERE [Id] = '66666666-6666-6666-6666-666666666666';

-- Second provider row (parity seed, ADR-0055): the provider-list endpoint must have >=2 rows so
-- list-ordering parity between v1 /api/Provider and v2 /api/v2/providers is provable (SeedConstants.SecondProviderId).
IF NOT EXISTS (SELECT 1 FROM [dbo].[Provider] WHERE [Id] = '66666666-6666-6666-6666-666666666667')
BEGIN
    INSERT INTO [dbo].[Provider] (Id, Name, CountryId, SupportEmail)
    VALUES ('66666666-6666-6666-6666-666666666667', 'prov2', 1, 'support@prov2.example.test')
END

UPDATE [dbo].[Provider]
SET [SupportEmail] = 'support@prov2.example.test'
WHERE [Id] = '66666666-6666-6666-6666-666666666667';

GO

-- =====================================================================
-- Location (LocationTypeId=1 = HPC, ProviderId = provider above)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[Location] WHERE [Id] = '88888888-8888-8888-8888-888888888888')
BEGIN
    INSERT INTO [dbo].[Location] (Id, Name, Endpoint, ProviderId, LocationTypeId)
    VALUES ('88888888-8888-8888-8888-888888888888', 'loc1', '__HEAPPE_MOCK_URL__',
            '66666666-6666-6666-6666-666666666666', 1)
END

UPDATE [dbo].[Location]
SET [Endpoint] = '__HEAPPE_MOCK_URL__'
WHERE [Id] = '88888888-8888-8888-8888-888888888888';

-- Second location row (parity seed, ADR-0055): the location-list endpoint must have >=2 rows so
-- list-ordering parity between v1 /api/Location and v2 /api/v2/locations is provable (SeedConstants.SecondLocationId).
IF NOT EXISTS (SELECT 1 FROM [dbo].[Location] WHERE [Id] = '88888888-8888-8888-8888-888888888889')
BEGIN
    INSERT INTO [dbo].[Location] (Id, Name, Endpoint, ProviderId, LocationTypeId)
    VALUES ('88888888-8888-8888-8888-888888888889', 'loc2', 'http://test-hpc2.example.com',
            '66666666-6666-6666-6666-666666666667', 1)
END

GO

-- =====================================================================
-- Project (ShortName=TESTPRJ, ProjectDomainTypeId=1)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[Project] WHERE [ShortName] = 'TESTPRJ')
BEGIN
    INSERT INTO [dbo].[Project] (Id, ShortName, Name, CreatedByUserId, ProjectDomainTypeId, StartDate, EndDate)
    VALUES ('11111111-1111-1111-1111-111111111111', 'TESTPRJ', 'Test project',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA', 1,
            '2024-01-01', '2029-12-31')
END

GO

-- =====================================================================
-- Second project (ShortName=NOROLE) — the runtime platform admin has NO
-- role here. Used to assert ValidRoles returns an empty set for a system
-- admin without a project-scoped role (intended user-space behaviour, ADR-0006).
-- CreatedByUserId reuses the owner test user; creator != membership.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[Project] WHERE [ShortName] = 'NOROLE')
BEGIN
    INSERT INTO [dbo].[Project] (Id, ShortName, Name, CreatedByUserId, ProjectDomainTypeId, StartDate, EndDate)
    VALUES ('77777777-7777-7777-7777-777777777777', 'NOROLE', 'No-role project',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA', 1,
            '2024-01-01', '2029-12-31')
END

GO

-- =====================================================================
-- Seed deterministic project membership and roles for project/read/request
-- scenarios. Runtime token identity is attached dynamically in the fixture.
-- Role ids are resolved from forge.Roles to keep E2E membership aligned with
-- the RBAC seed used by Gateway/Core.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedUserToProject]
               WHERE [ProjectId] = '11111111-1111-1111-1111-111111111111'
                 AND [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[AssignedUserToProject] (ProjectId, UserId, AssignedByUserId)
    VALUES ('11111111-1111-1111-1111-111111111111',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedUserToProject]
               WHERE [ProjectId] = '11111111-1111-1111-1111-111111111111'
                  AND [UserId] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[AssignedUserToProject] (ProjectId, UserId, AssignedByUserId)
    VALUES ('11111111-1111-1111-1111-111111111111',
            'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
END

IF '__PROJECT_MEMBER_IDP_ID__' <> '__PROJECT_MANAGER_IDP_ID__'
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedUserToProject]
                   WHERE [ProjectId] = '11111111-1111-1111-1111-111111111111'
                     AND [UserId] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC')
    BEGIN
        INSERT INTO [dbo].[AssignedUserToProject] (ProjectId, UserId, AssignedByUserId)
        VALUES ('11111111-1111-1111-1111-111111111111',
                'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC',
                'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
    END
END

IF NOT EXISTS (
    SELECT 1
    FROM [dbo].[UserPermission] up
    INNER JOIN [forge].[Roles] r ON r.[Id] = up.[SystemRoleId]
    WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111'
      AND up.[UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA'
      AND r.[Name] = 'Owner')
BEGIN
    INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
    SELECT
        NEWID(),
        '11111111-1111-1111-1111-111111111111',
        'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
        r.[Id]
    FROM [forge].[Roles] r
    WHERE r.[Name] = 'Owner'
END

UPDATE up
SET up.[SystemRoleId] = r.[Id]
FROM [dbo].[UserPermission] up
INNER JOIN [forge].[Roles] r ON r.[Name] = 'Owner'
WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111'
  AND up.[UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA';

IF NOT EXISTS (
    SELECT 1
    FROM [dbo].[UserPermission] up
    INNER JOIN [forge].[Roles] r ON r.[Id] = up.[SystemRoleId]
    WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111'
      AND up.[UserId] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB'
      AND r.[Name] = 'Manager')
BEGIN
    INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
    SELECT
        NEWID(),
        '11111111-1111-1111-1111-111111111111',
        'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB',
        r.[Id]
    FROM [forge].[Roles] r
    WHERE r.[Name] = 'Manager'
END

UPDATE up
SET up.[SystemRoleId] = r.[Id]
FROM [dbo].[UserPermission] up
INNER JOIN [forge].[Roles] r ON r.[Name] = 'Manager'
WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111'
  AND up.[UserId] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB';

IF '__PROJECT_MEMBER_IDP_ID__' <> '__PROJECT_MANAGER_IDP_ID__'
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM [dbo].[UserPermission] up
        INNER JOIN [forge].[Roles] sr ON sr.[Id] = up.[SystemRoleId]
        WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111'
          AND up.[UserId] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC'
          AND sr.[Name] = 'Member')
    BEGIN
        INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
        SELECT
            NEWID(),
            '11111111-1111-1111-1111-111111111111',
            'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC',
            sr.[Id]
        FROM [forge].[Roles] sr
        WHERE sr.[Name] = 'Member'
    END

    UPDATE up
    SET up.[SystemRoleId] = sr.[Id]
    FROM [dbo].[UserPermission] up
    INNER JOIN [forge].[Roles] sr ON sr.[Name] = 'Member'
    WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111'
      AND up.[UserId] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC';
END

GO

-- =====================================================================
-- Baseline system role for the manager and the member
-- Every user must carry exactly one system (ProjectId IS NULL) permission: the v2 userinfo path
-- enforces it in DatabaseContext.UserRepository.ValidateSystemRole, and without it that endpoint
-- answers 500 for these two while v1 still answers 200. GatewayAttachFixture already writes this
-- row for the users it bootstraps, which is why the E2E suite never sees the failure and a plain
-- `aspire run` against this seed does. The owner gets Admin above, so only B and C are missing.
-- =====================================================================
DECLARE @baselineRoleId INT = (SELECT TOP (1) [Id] FROM [forge].[Roles] WHERE [Name] = 'User');

IF @baselineRoleId IS NULL
BEGIN
    THROW 50005, 'Baseline ''User'' system role was not found in forge.Roles for local seed.', 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermission]
    WHERE [ProjectId] IS NULL AND [UserId] = 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
    VALUES (NEWID(), NULL, 'BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB', @baselineRoleId);
END;

IF '__PROJECT_MEMBER_IDP_ID__' <> '__PROJECT_MANAGER_IDP_ID__'
   AND NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermission]
    WHERE [ProjectId] IS NULL AND [UserId] = 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC')
BEGIN
    INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
    VALUES (NEWID(), NULL, 'CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC', @baselineRoleId);
END;

GO

-- =====================================================================
-- Role coverage fixtures
-- One user per role the three main seed users do not cover, so every role in forge.Roles can be
-- exercised against a running stack. Their IdentityProviderUserId is the user id itself, and the
-- imported Keycloak realm carries a login whose Keycloak user id is that same GUID
-- (src/UserOrg.AppHost/externalServices/keycloakRealm/master-realm.json): the `sub` of a token the
-- gateway's gettoken command mints therefore resolves to the row below. The two files move together -
-- a GUID changed in one and not the other silently produces a caller with no permissions. The same
-- GUID is also the whole credential under the develop-only auth override (ADR-0045), where the Bearer
-- string is the idpid. Fixed literals rather than __PLACEHOLDERS__ because no caller substitutes them.
-- =====================================================================
-- Re-declared: GO ends the batch above, and a scalar does not survive it.
DECLARE @baselineRoleId INT = (SELECT TOP (1) [Id] FROM [forge].[Roles] WHERE [Name] = 'User');

IF @baselineRoleId IS NULL
BEGIN
    THROW 50006, 'Baseline ''User'' system role was not found in forge.Roles for role coverage.', 1;
END;

DECLARE @coverage TABLE (UserId UNIQUEIDENTIFIER, IdpId NVARCHAR(64), RoleName NVARCHAR(64), ProjectScoped BIT);

INSERT INTO @coverage (UserId, IdpId, RoleName, ProjectScoped) VALUES
    ('0D000000-0000-0000-0000-000000000007', '0d000000-0000-0000-0000-000000000007', 'ProviderAdmin',         0),
    ('0D000000-0000-0000-0000-000000000005', '0d000000-0000-0000-0000-000000000005', 'DataPublisher',         1),
    ('0D000000-0000-0000-0000-000000000008', '0d000000-0000-0000-0000-000000000008', 'JobscriptOperator',     1),
    ('0D000000-0000-0000-0000-000000000009', '0d000000-0000-0000-0000-000000000009', 'JobscriptWriter',       1),
    ('0D000000-0000-0000-0000-000000000010', '0d000000-0000-0000-0000-000000000010', 'JobscriptAccessMember', 1);

-- Nothing outside this block names these ids, so a just-in-time row for the same identity is adopted
-- rather than deleted: the permissions below land on the row the gateway already resolves, and the
-- INSERT that would otherwise collide with UX_User_IdentityProviderUserId never runs (see the note on
-- @seededIdentities above).
UPDATE c
SET c.UserId = u.[Id]
FROM @coverage c
INNER JOIN [dbo].[User] u ON u.[IdentityProviderUserId] = c.IdpId;

INSERT INTO [dbo].[User] (Id, IdentityProviderUserId, Email, UserName)
SELECT c.UserId, c.IdpId, c.IdpId + '@roles.seed', c.IdpId
FROM @coverage c
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[User] u WHERE u.[Id] = c.UserId);

-- ProviderAdmin reads and writes the provider it is assigned to. Without the assignment the role is
-- carried but unusable: every provider-scoped call resolves to no provider.
UPDATE u
SET u.[AssignedProviderId] = '66666666-6666-6666-6666-666666666666'
FROM [dbo].[User] u
INNER JOIN @coverage c ON c.UserId = u.[Id]
WHERE c.RoleName = 'ProviderAdmin'
  AND u.[AssignedProviderId] IS NULL;

-- The system row. ProviderAdmin is itself a system role, so it stands alone; the project-scoped
-- roles carry the baseline 'User' system row next to their project row.
--
-- Set before inserted, because an adopted just-in-time row already has a system row: the gateway gives
-- every caller it provisions the baseline, and inserting only when none exists would leave
-- ProviderAdmin sitting on a plain 'User' row - the one role this block exists to make reachable.
UPDATE up
SET up.[SystemRoleId] = CASE WHEN c.ProjectScoped = 1 THEN @baselineRoleId ELSE r.[Id] END
FROM [dbo].[UserPermission] up
INNER JOIN @coverage c ON c.UserId = up.[UserId]
INNER JOIN [forge].[Roles] r ON r.[Name] = c.RoleName
WHERE up.[ProjectId] IS NULL;

INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
SELECT NEWID(), NULL, c.UserId,
       CASE WHEN c.ProjectScoped = 1 THEN @baselineRoleId ELSE r.[Id] END
FROM @coverage c
INNER JOIN [forge].[Roles] r ON r.[Name] = c.RoleName
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermission] up
    WHERE up.[ProjectId] IS NULL AND up.[UserId] = c.UserId);

UPDATE up
SET up.[SystemRoleId] = r.[Id]
FROM [dbo].[UserPermission] up
INNER JOIN @coverage c ON c.UserId = up.[UserId]
INNER JOIN [forge].[Roles] r ON r.[Name] = c.RoleName
WHERE c.ProjectScoped = 1
  AND up.[ProjectId] = '11111111-1111-1111-1111-111111111111';

INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
SELECT NEWID(), '11111111-1111-1111-1111-111111111111', c.UserId, r.[Id]
FROM @coverage c
INNER JOIN [forge].[Roles] r ON r.[Name] = c.RoleName
WHERE c.ProjectScoped = 1
  AND NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermission] up
    WHERE up.[ProjectId] = '11111111-1111-1111-1111-111111111111' AND up.[UserId] = c.UserId);

INSERT INTO [dbo].[AssignedUserToProject] (ProjectId, UserId, AssignedByUserId)
SELECT '11111111-1111-1111-1111-111111111111', c.UserId, 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA'
FROM @coverage c
WHERE c.ProjectScoped = 1
  AND NOT EXISTS (
    SELECT 1 FROM [dbo].[AssignedUserToProject] a
    WHERE a.[ProjectId] = '11111111-1111-1111-1111-111111111111' AND a.[UserId] = c.UserId);

GO

-- =====================================================================
-- ProjectResource
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222222')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail)
    VALUES ('22222222-2222-2222-2222-222222222222', 'res1', '2024-01-01', '2029-12-31', 'pi@res1.example.test')
END

UPDATE [dbo].[ProjectResource]
SET [PrimaryInvestigatorEmail] = 'pi@res1.example.test'
WHERE [Id] = '22222222-2222-2222-2222-222222222222';

-- Second project-resource row (parity seed, ADR-0055): the resource-list endpoint
-- (v1 /api/ProjectResource?ProjectShortName=TESTPRJ <-> v2 /api/v2/providers/{pn}/resources)
-- must have >=2 rows so list-ordering parity is provable (SeedConstants.SecondProjectResourceId).
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222223')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail)
    VALUES ('22222222-2222-2222-2222-222222222223', 'res2', '2024-01-01', '2029-12-31', 'pi@res2.example.test')
END

GO

-- =====================================================================
-- AssignedProjectResourceToProject (link project <-> resource)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedProjectResourceToProject]
               WHERE ProjectId = '11111111-1111-1111-1111-111111111111'
               AND ProjectResourceId = '22222222-2222-2222-2222-222222222222')
BEGIN
    INSERT INTO [dbo].[AssignedProjectResourceToProject] (ProjectId, ProjectResourceId)
    VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222')
END

-- Link the second resource to TESTPRJ so the project-scoped resource list returns >=2 rows.
IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedProjectResourceToProject]
               WHERE ProjectId = '11111111-1111-1111-1111-111111111111'
               AND ProjectResourceId = '22222222-2222-2222-2222-222222222223')
BEGIN
    INSERT INTO [dbo].[AssignedProjectResourceToProject] (ProjectId, ProjectResourceId)
    VALUES ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222223')
END

GO

-- =====================================================================
-- AssignedLocation (AllocationUnitTypeId=2=node-hours, Id>1 required by service)
-- This is what /api/ProjectResourceAssignment/{Id} queries
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedLocation] WHERE [Id] = '33333333-3333-3333-3333-333333333333')
BEGIN
    INSERT INTO [dbo].[AssignedLocation] (Id, LocationId, ProjectResourceId, AllocationUnitTypeId)
    VALUES ('33333333-3333-3333-3333-333333333333',
            '88888888-8888-8888-8888-888888888888',
            '22222222-2222-2222-2222-222222222222',
            2)
END

-- Second resource's allocation on loc1 (prov1). The v2 resource list is provider-nested
-- (/api/v2/providers/{pn}/resources, ADR-0022): a resource surfaces there only through an
-- assignment on one of the provider's locations. Without this, res2 (assigned to TESTPRJ but to
-- no prov1 location) would appear in the project-scoped v1 list yet be absent from the
-- provider-nested v2 list, so the B05 (ADR-0055) resource-list ordering parity could not be
-- proven across >=2 rows. res1 (2222) and res2 (2223) then order the same both stacks (res1<res2).
IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedLocation] WHERE [Id] = '33333333-3333-3333-3333-333333333334')
BEGIN
    INSERT INTO [dbo].[AssignedLocation] (Id, LocationId, ProjectResourceId, AllocationUnitTypeId)
    VALUES ('33333333-3333-3333-3333-333333333334',
            '88888888-8888-8888-8888-888888888888',
            '22222222-2222-2222-2222-222222222223',
            2)
END

GO

-- =====================================================================
-- AssignedLocationValue: HEAPPE_URL for the seeded AssignedLocation (ADR-0051).
-- __HEAPPE_MOCK_URL__ is substituted by the fixture with the WireMock endpoint so
-- HEAppE-backed endpoints (GetProjectTemplates, CreateResourceOnLocation) hit the fake.
-- =====================================================================
UPDATE [dbo].[AssignedLocationValue]
SET [Name] = 'HEAPPE_URL',
    [Value] = '__HEAPPE_MOCK_URL__',
    [AssignedLocationId] = '33333333-3333-3333-3333-333333333333'
WHERE [Id] = 'A0A0A0A0-A0A0-A0A0-A0A0-A0A0A0A0A0A0';

IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedLocationValue] WHERE [Id] = 'A0A0A0A0-A0A0-A0A0-A0A0-A0A0A0A0A0A0')
BEGIN
    INSERT INTO [dbo].[AssignedLocationValue] (Id, Name, Value, AssignedLocationId)
    VALUES ('A0A0A0A0-A0A0-A0A0-A0A0-A0A0A0A0A0A0',
            'HEAPPE_URL',
            '__HEAPPE_MOCK_URL__',
            '33333333-3333-3333-3333-333333333333')
END

GO

-- =====================================================================
-- AssignedLocationValue: HEAPPE_IDENTIFIER for the same AssignedLocation. Every HEAppE call is routed
-- through NexusApi by this identifier (#349); the URL above is kept for logs only. heappe-mock is the
-- instance the AppHost's NexusApi import and the WireMock stubs (310-nexus-heappe-instances.json) list.
-- =====================================================================
UPDATE [dbo].[AssignedLocationValue]
SET [Name] = 'HEAPPE_IDENTIFIER',
    [Value] = 'heappe-mock',
    [AssignedLocationId] = '33333333-3333-3333-3333-333333333333'
WHERE [Id] = 'A0A0A0A0-A0A0-A0A0-A0A0-A0A0A0A0A0A1';

IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedLocationValue] WHERE [Id] = 'A0A0A0A0-A0A0-A0A0-A0A0-A0A0A0A0A0A1')
BEGIN
    INSERT INTO [dbo].[AssignedLocationValue] (Id, Name, Value, AssignedLocationId)
    VALUES ('A0A0A0A0-A0A0-A0A0-A0A0-A0A0A0A0A0A1',
            'HEAPPE_IDENTIFIER',
            'heappe-mock',
            '33333333-3333-3333-3333-333333333333')
END

GO

-- =====================================================================
-- Request (State=1=pending, Type=2=ProjectResourceAssignment)
-- Used by /api/Requests and /api/Public/RequestInfo
-- =====================================================================
UPDATE [dbo].[Request]
SET [ProjectId] = '11111111-1111-1111-1111-111111111111',
    [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
    [State] = 1,
    [Type] = 2
WHERE [Id] = '44444444-4444-4444-4444-444444444444';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Request] WHERE [Id] = '44444444-4444-4444-4444-444444444444')
BEGIN
    INSERT INTO [dbo].[Request] (Id, ProjectId, UserId, State, Type)
    VALUES ('44444444-4444-4444-4444-444444444444',
            '11111111-1111-1111-1111-111111111111',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
            1, 2)
END

-- Second request row (parity seed, ADR-0055): the request-list endpoints
-- (v1 /api/Requests <-> v2 /api/v2/requests, and /SentRequests <-> /requests/sent) must have >=2
-- rows so list-ordering parity is provable (SeedConstants.SecondRequestId). Same project/owner/state
-- as the seed request so existing State=1 + ProjectShortName=TESTPRJ filter assertions still hold.
UPDATE [dbo].[Request]
SET [ProjectId] = '11111111-1111-1111-1111-111111111111',
    [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
    [State] = 1,
    [Type] = 2
WHERE [Id] = '44444444-4444-4444-4444-444444444445';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Request] WHERE [Id] = '44444444-4444-4444-4444-444444444445')
BEGIN
    INSERT INTO [dbo].[Request] (Id, ProjectId, UserId, State, Type)
    VALUES ('44444444-4444-4444-4444-444444444445',
            '11111111-1111-1111-1111-111111111111',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
            1, 2)
END

GO

-- =====================================================================
-- Dedicated public mutation request used by POST /api/Public/Handle/{actionLink}
-- =====================================================================
UPDATE [dbo].[Request]
SET [ProjectId] = '11111111-1111-1111-1111-111111111111',
    [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
    [State] = 1,
    [Type] = 2
WHERE [Id] = '99999999-9999-9999-9999-999999999999';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Request] WHERE [Id] = '99999999-9999-9999-9999-999999999999')
BEGIN
    INSERT INTO [dbo].[Request] (Id, ProjectId, UserId, State, Type)
    VALUES ('99999999-9999-9999-9999-999999999999',
            '11111111-1111-1111-1111-111111111111',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
            1, 2)
END

GO

-- =====================================================================
-- ActionLink entries required by /api/Public/RequestInfo/{requestId}
-- =====================================================================
UPDATE [dbo].[ActionLink]
SET [Action] = 1,
    [Link] = 'accept-seed-request',
    [ValidTo] = DATEADD(DAY, 7, SYSUTCDATETIME()),
    [RequestId] = '44444444-4444-4444-4444-444444444444',
    [IsDeleted] = 0
WHERE [Id] = '44444444-4444-4444-4444-AAAAAAAAAAAA';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ActionLink] WHERE [Id] = '44444444-4444-4444-4444-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[ActionLink] (Id, Action, Link, ValidTo, RequestId)
    VALUES ('44444444-4444-4444-4444-AAAAAAAAAAAA', 1, 'accept-seed-request',
            DATEADD(DAY, 7, SYSUTCDATETIME()),
            '44444444-4444-4444-4444-444444444444')
END

UPDATE [dbo].[ActionLink]
SET [Action] = 2,
    [Link] = 'reject-seed-request',
    [ValidTo] = DATEADD(DAY, 7, SYSUTCDATETIME()),
    [RequestId] = '44444444-4444-4444-4444-444444444444',
    [IsDeleted] = 0
WHERE [Id] = '44444444-4444-4444-4444-BBBBBBBBBBBB';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ActionLink] WHERE [Id] = '44444444-4444-4444-4444-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[ActionLink] (Id, Action, Link, ValidTo, RequestId)
    VALUES ('44444444-4444-4444-4444-BBBBBBBBBBBB', 2, 'reject-seed-request',
            DATEADD(DAY, 7, SYSUTCDATETIME()),
            '44444444-4444-4444-4444-444444444444')
END

GO

-- =====================================================================
-- ActionLink entries for public mutation flow
-- =====================================================================
UPDATE [dbo].[ActionLink]
SET [Action] = 1,
    [Link] = 'accept-public-mutation-request',
    [ValidTo] = DATEADD(DAY, 7, SYSUTCDATETIME()),
    [RequestId] = '99999999-9999-9999-9999-999999999999',
    [IsDeleted] = 0
WHERE [Id] = '99999999-9999-9999-9999-AAAAAAAAAAAA';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ActionLink] WHERE [Id] = '99999999-9999-9999-9999-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[ActionLink] (Id, Action, Link, ValidTo, RequestId)
    VALUES ('99999999-9999-9999-9999-AAAAAAAAAAAA', 1, 'accept-public-mutation-request',
            DATEADD(DAY, 7, SYSUTCDATETIME()),
            '99999999-9999-9999-9999-999999999999')
END

UPDATE [dbo].[ActionLink]
SET [Action] = 2,
    [Link] = 'reject-public-mutation-request',
    [ValidTo] = DATEADD(DAY, 7, SYSUTCDATETIME()),
    [RequestId] = '99999999-9999-9999-9999-999999999999',
    [IsDeleted] = 0
WHERE [Id] = '99999999-9999-9999-9999-BBBBBBBBBBBB';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ActionLink] WHERE [Id] = '99999999-9999-9999-9999-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[ActionLink] (Id, Action, Link, ValidTo, RequestId)
    VALUES ('99999999-9999-9999-9999-BBBBBBBBBBBB', 2, 'reject-public-mutation-request',
            DATEADD(DAY, 7, SYSUTCDATETIME()),
            '99999999-9999-9999-9999-999999999999')
END

GO

-- =====================================================================
-- ProjectTag (Id=55555555)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectTag] WHERE [Id] = '55555555-5555-5555-5555-555555555555')
BEGIN
    INSERT INTO [dbo].[ProjectTag] (Id, Name, CreatedByUserId)
    VALUES ('55555555-5555-5555-5555-555555555555', 'tag1',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA')
END

GO

-- =====================================================================
-- AssignedProjectTagToProject (link project <-> tag)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[AssignedProjectTagToProject]
               WHERE [ProjectsId] = '11111111-1111-1111-1111-111111111111'
                 AND [ProjectTagsId] = '55555555-5555-5555-5555-555555555555')
BEGIN
    INSERT INTO [dbo].[AssignedProjectTagToProject] (ProjectsId, ProjectTagsId)
    VALUES ('11111111-1111-1111-1111-111111111111',
            '55555555-5555-5555-5555-555555555555')
END

GO

-- =====================================================================
-- OnboardingRequest (v2 self-onboarding; ADR-0046)
-- State: 0=Pending, 1=Accepted, 2=Rejected. Slug is the GUID-free public id.
-- Seeded Pending so Get/Accept/Reject onboarding endpoints have a target even
-- while CreateOnboardingRequest is blocked by a product bug (plan §4).
-- UPDATE-then-INSERT keeps it Pending across re-seeds (mirrors [Request] above).
-- =====================================================================
UPDATE [dbo].[OnboardingRequest]
SET [ProjectId] = '11111111-1111-1111-1111-111111111111',
    [UserId] = 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
    [ContactEmail] = 'onboarding@example.test',
    [State] = 0,
    [Slug] = 'seed-onboarding-request',
    [IsDeleted] = 0,
    [ModifiedAt] = NULL
WHERE [Id] = 'EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE';

IF NOT EXISTS (SELECT 1 FROM [dbo].[OnboardingRequest] WHERE [Id] = 'EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE')
BEGIN
    INSERT INTO [dbo].[OnboardingRequest]
        (Id, ProjectId, UserId, ContactEmail, State, Slug, IsDeleted, CreatedAt, ModifiedAt)
    VALUES ('EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE',
            '11111111-1111-1111-1111-111111111111',
            'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
            'onboarding@example.test',
            0,
            'seed-onboarding-request',
            0,
            SYSUTCDATETIME(),
            NULL)
END

GO

-- =====================================================================
-- B04: unattached, provider-owned resources for the v2 resource-assignment
-- request accept/reject flows (ADR-0063). Each carries a ProviderId (prov1)
-- so the (ProviderName, ResourceName) key resolves, and a PI contact e-mail so
-- the approval can be routed. Deliberately NOT linked to any project
-- (no AssignedProjectResourceToProject row) so the create passes
-- the already-attached guard; the accept flow attaches 'reqaccept', the reject
-- flow leaves 'reqreject' detached. Separate resources keep the two flows
-- independent of the single-active-request filtered index. Additive only.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222230')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail, ProviderId)
    VALUES ('22222222-2222-2222-2222-222222222230', 'reqaccept', '2024-01-01', '2029-12-31',
            'pi-accept@res.example.test', '66666666-6666-6666-6666-666666666666')
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222231')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail, ProviderId)
    VALUES ('22222222-2222-2222-2222-222222222231', 'reqreject', '2024-01-01', '2029-12-31',
            'pi-reject@res.example.test', '66666666-6666-6666-6666-666666666666')
END

GO

-- =====================================================================
-- B05: two more unattached, provider-owned resources dedicated to the
-- EmailSender scenarios (sent-assert + 500-still-terminal), kept separate
-- from B04's 'reqaccept'/'reqreject' so none of the four accept/reject
-- flows in this suite share a resource (single-active-request filtered
-- index). Additive only.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222232')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail, ProviderId)
    VALUES ('22222222-2222-2222-2222-222222222232', 'reqemailok', '2024-01-01', '2029-12-31',
            'pi-emailok@res.example.test', '66666666-6666-6666-6666-666666666666')
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222233')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail, ProviderId)
    VALUES ('22222222-2222-2222-2222-222222222233', 'reqemail500', '2024-01-01', '2029-12-31',
            'pi-email500@res.example.test', '66666666-6666-6666-6666-666666666666')
END

GO

-- =====================================================================
-- T260719-endpointcov B03: one more unattached, provider-owned resource, dedicated to
-- WriteValidationTests' single live-token case (HandleResourceRequestActionEndpoint's
-- decision validation can only be reached past a resolved token, ADR-0064 anti-leak
-- ordering) — kept separate from the B04/B05 resources above so its permanently-parked
-- PendingPiApproval row never collides with their accept/reject flows. Additive only.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222234')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail, ProviderId)
    VALUES ('22222222-2222-2222-2222-222222222234', 'valdecision', '2024-01-01', '2029-12-31',
            'pi-valdecision@res.example.test', '66666666-6666-6666-6666-666666666666')
END

GO

-- =====================================================================
-- Issue #343: one more unattached, provider-owned resource, dedicated to
-- AssignmentUpdateWriteTests, which assigns it to a location it creates and then replaces the
-- assignment's specifications. Kept separate so no other suite's resource gains an allocation.
-- Additive only.
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Id] = '22222222-2222-2222-2222-222222222235')
BEGIN
    INSERT INTO [dbo].[ProjectResource] (Id, Name, StartDate, EndDate, PrimaryInvestigatorEmail, ProviderId)
    VALUES ('22222222-2222-2222-2222-222222222235', 'assignupdate', '2024-01-01', '2029-12-31',
            'pi-assignupdate@res.example.test', '66666666-6666-6666-6666-666666666666')
END

GO

PRINT 'Seed script executed successfully.'

