/*
  ADR-0026 — DATA FIX ONLY: backfill ProviderId, split the 3 cross-provider resources, repoint children.

  SCHEMA IS NOT HERE. The column/FK and the unique index are EF migrations. This DATA fix runs in the
  manual bracket BETWEEN them (deploy procedure: 0026-deployment-runbook.html):
      EF migration #1  20260627143515_AddProjectResourceProviderId              (column + FK)   ← apply first
      THIS SCRIPT      backfill + split + repoint + verify                       (data)          ← then this
      EF migration #2  20260627143547_AddProjectResourceProviderIdNameUniqueIndex (unique IX)    ← then M2

  Apply M1 and M2 MANUALLY (each from its own `dotnet ef migrations script <from> <to>` output); do NOT let
  the global/auto migration apply them together — the data fix must sit between. The guard below enforces
  that M1 (the ProviderId column) precedes this script.

  ponytail: one-shot data migration in a single TRAN; re-running after success changes nothing
  (backfill is `WHERE ProviderId IS NULL`, clones skipped once nothing spans >1 provider). On a new
  environment re-run the pre-flight SELECTs first — counts below are for 127.0.0.1,1433.

  Live audit (2026-06-27, UserOrgDBContext): 77 active resources, 14 providers, 3 cross-provider rows
  (OPEN-33-82, VLQ-FI, VLQ-PL-P0001), 0 duplicate active names, UserProjectAccess empty (0 rows).

  Requests are NOT auto-repointed (no provider signal) — the script LISTS the 2 affected rows for manual
  resolution at the end.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

-- Guard: EF migration #1 must have run first.
IF COL_LENGTH('dbo.ProjectResource', 'ProviderId') IS NULL
    THROW 50026, 'ProjectResource.ProviderId is missing — apply EF migration #1 before this data fix.', 1;
GO

/* ───────────────────────── DATA FIX (one-shot) ──────────────────────── */

BEGIN TRAN;

-- Shared = active resources whose allocations span >1 provider (the bug rows).
IF OBJECT_ID('tempdb..#shared') IS NOT NULL DROP TABLE #shared;
SELECT pr.Id AS ResId
INTO #shared
FROM dbo.ProjectResource pr
JOIN dbo.AssignedLocation al ON al.ProjectResourceId = pr.Id
JOIN dbo.Location l ON l.Id = al.LocationId
WHERE pr.IsDeleted = 0
GROUP BY pr.Id
HAVING COUNT(DISTINCT l.ProviderId) > 1;

-- 1) Backfill ProviderId for resources allocated under exactly ONE provider (the 74 clean rows).
;WITH ResProv AS (
    SELECT pr.Id AS ResId, MIN(l.ProviderId) AS ProviderId, COUNT(DISTINCT l.ProviderId) AS Nprov
    FROM dbo.ProjectResource pr
    JOIN dbo.AssignedLocation al ON al.ProjectResourceId = pr.Id
    JOIN dbo.Location l ON l.Id = al.LocationId
    WHERE pr.IsDeleted = 0
    GROUP BY pr.Id
)
UPDATE pr SET pr.ProviderId = rp.ProviderId
FROM dbo.ProjectResource pr
JOIN ResProv rp ON rp.ResId = pr.Id
WHERE rp.Nprov = 1 AND pr.ProviderId IS NULL;
-- NB: zero-allocation resources keep ProviderId NULL (cannot be derived) — see ADR-0026 edge.

-- 2) Rank (resource, provider) for the shared rows. rn=1 (most allocations) keeps the ORIGINAL row;
--    rn>1 gets a freshly-cloned resource row.
IF OBJECT_ID('tempdb..#split') IS NOT NULL DROP TABLE #split;
SELECT pr.Id AS ResId, l.ProviderId,
       COUNT(*) AS Allocs,
       ROW_NUMBER() OVER (PARTITION BY pr.Id ORDER BY COUNT(*) DESC, l.ProviderId) AS rn,
       CAST(NULL AS uniqueidentifier) AS NewResId
INTO #split
FROM dbo.ProjectResource pr
JOIN dbo.AssignedLocation al ON al.ProjectResourceId = pr.Id
JOIN dbo.Location l ON l.Id = al.LocationId
WHERE pr.IsDeleted = 0 AND pr.Id IN (SELECT ResId FROM #shared)
GROUP BY pr.Id, l.ProviderId;

-- original row keeps the primary provider
UPDATE pr SET pr.ProviderId = s.ProviderId
FROM dbo.ProjectResource pr
JOIN #split s ON s.ResId = pr.Id AND s.rn = 1
WHERE pr.ProviderId IS NULL;

-- mint a new id for each secondary provider
UPDATE #split SET NewResId = NEWID() WHERE rn > 1;

-- 3) Clone a resource row per secondary provider (same natural fields, new Id, its own ProviderId).
INSERT INTO dbo.ProjectResource
    (Id, Name, Description, PrimaryInvestigatorEmail, StartDate, EndDate,
     CreatedAt, ModifiedAt, IsDeleted, ExternalId, Mode, ServiceName, ProviderId)
SELECT s.NewResId, pr.Name, pr.Description, pr.PrimaryInvestigatorEmail, pr.StartDate, pr.EndDate,
       SYSUTCDATETIME(), pr.ModifiedAt, pr.IsDeleted, pr.ExternalId, pr.Mode, pr.ServiceName, s.ProviderId
FROM #split s
JOIN dbo.ProjectResource pr ON pr.Id = s.ResId
WHERE s.rn > 1;

-- 4) Repoint each secondary provider's allocations to its clone (deterministic by Location.ProviderId).
UPDATE al SET al.ProjectResourceId = s.NewResId
FROM dbo.AssignedLocation al
JOIN dbo.Location l ON l.Id = al.LocationId
JOIN #split s ON s.ResId = al.ProjectResourceId AND s.ProviderId = l.ProviderId AND s.rn > 1;

-- 5) Duplicate project assignments to every split (preserve current cross-provider access).
INSERT INTO dbo.AssignedProjectResourceToProject (ProjectId, ProjectResourceId, CreatedAt, IsDeleted)
SELECT aprp.ProjectId, s.NewResId, SYSUTCDATETIME(), 0
FROM dbo.AssignedProjectResourceToProject aprp
JOIN #split s ON s.ResId = aprp.ProjectResourceId AND s.rn > 1
WHERE aprp.IsDeleted = 0
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AssignedProjectResourceToProject x
      WHERE x.ProjectId = aprp.ProjectId AND x.ProjectResourceId = s.NewResId);

/* ───────────────────────── VERIFY before COMMIT ─────────────────────── */

-- A) No active resource may still span >1 provider.
DECLARE @stillShared int = (
    SELECT COUNT(*) FROM (
        SELECT pr.Id FROM dbo.ProjectResource pr
        JOIN dbo.AssignedLocation al ON al.ProjectResourceId = pr.Id
        JOIN dbo.Location l ON l.Id = al.LocationId
        WHERE pr.IsDeleted = 0
        GROUP BY pr.Id HAVING COUNT(DISTINCT l.ProviderId) > 1) t);

-- B) Invariant #5: every allocation's location-provider matches its resource's ProviderId.
DECLARE @inv5Violations int = (
    SELECT COUNT(*) FROM dbo.AssignedLocation al
    JOIN dbo.Location l ON l.Id = al.LocationId
    JOIN dbo.ProjectResource pr ON pr.Id = al.ProjectResourceId
    WHERE pr.IsDeleted = 0 AND pr.ProviderId IS NOT NULL AND pr.ProviderId <> l.ProviderId);

-- C) No (ProviderId, Name) duplicate among active rows (the unique index must build).
DECLARE @nameDupes int = (
    SELECT COUNT(*) FROM (
        SELECT ProviderId, Name FROM dbo.ProjectResource
        WHERE IsDeleted = 0 AND ProviderId IS NOT NULL
        GROUP BY ProviderId, Name HAVING COUNT(*) > 1) t);

SELECT @stillShared AS StillSpanningMultiProvider,  -- expect 0
       @inv5Violations AS Invariant5Violations,      -- expect 0
       @nameDupes AS ProviderNameDupes;              -- expect 0

IF @stillShared <> 0 OR @inv5Violations <> 0 OR @nameDupes <> 0
BEGIN
    ROLLBACK TRAN;
    THROW 50026, 'ADR-0026 split verification failed — rolled back. Inspect the counts above.', 1;
END
ELSE
    COMMIT TRAN;
GO

-- Next step: apply EF migration #2 (IX_ProjectResource_ProviderId_Name) MANUALLY now that data is clean.
-- It builds collision-free because the split keeps every active (ProviderId, Name) unique
-- (the verify block above asserts ProviderNameDupes = 0 before COMMIT).

/* ─────────────── MANUAL FOLLOW-UP: requests referencing split resources ─────────────── */
-- These point at a resource that was split; no provider signal exists to auto-pick a side.
-- Resolve each by hand (point to the correct per-provider row or leave on the primary).
SELECT r.Id AS RequestId, r.ProjectResourceId, prn.Name AS ResourceName
FROM dbo.Request r
JOIN dbo.ProjectResource prn ON prn.Id = r.ProjectResourceId
WHERE r.ProjectResourceId IN (SELECT ResId FROM #shared);
GO

IF OBJECT_ID('tempdb..#split') IS NOT NULL DROP TABLE #split;
IF OBJECT_ID('tempdb..#shared') IS NOT NULL DROP TABLE #shared;
GO
