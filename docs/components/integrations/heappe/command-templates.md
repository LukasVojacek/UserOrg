# Create Command Template - Complete User Guide

## Overview
Vytvoření command template v HEAppE je proces se 3 fázemi, kde se postupně ověřují data v databázi a volají HEAppE API.

---

## STEP 0: Prepare Your HTTP Request Input

**Endpoint:** `POST /api/Heappe/CommandTemplates`

**Request Body JSON:**
```json
{
  "projectIdentifier": {
    "projectShortName": "testproject",
    "resourceName": "atr-25-4"
  },
  "locationName": "Barbora",
  "partitionName": "qcpu_exp",
  "aggregationName": "BARBORA CPU",
  "name": "MyTemplate",
  "description": "Test template",
  "executionScriptPath": "/scripts/job.sh",
  "preparationScriptPath": "/scripts/prep.sh",
  "commandTemplateParameters": [
    {
      "name": "NODES",
      "description": "Number of nodes"
    }
  ]
}
```

**Key Points:**
- `executionScriptPath` a `preparationScriptPath` musí být **COMPLETE paths** (e.g., `/scripts/job.sh`, ne jen `job.sh`)
- `aggregationName` může být `null` (volitelné)
- `commandTemplateParameters` je seznam parametrů šablony

---

## STEP 1: Validate & Prepare Resource Data

Tato fáze zajišťuje, že všechna vstupní data existují a jsou správně mapovaná.

### 1a) GetProjectResourceAllocation
```
Call: userorgService.GetProjectResourceAllocation(projectShortName, resourceName)
Input: "testproject", "atr-25-4"
Output: ProjectResourceInfoEntry - potvrzuje že projekt existuje a uživatel má přístup
Error: Project not found OR user has no access
```

### 1b) GetValidLocationNames
```
Call: userorgService.GetValidLocationNames(project, [HPC, Quantum])
Input: ProjectShortName, ResourceName, location type IDs
Output: HashSet<ProviderLocationInfo> (LocationId, LocationName, AggregationName, ProviderName) - unikátní podle objektu
Action: Hledáme: locationName="Barbora" → LocationId="a1b2c3d4-..."
Error: InvalidLocation - LocationName neexistuje nebo není dostupný
```

### 1c) Query Database: LocationPartitions
```
Query: SELECT * FROM LocationPartitions WHERE
  Name = 'qcpu_exp' AND
  AggregationName = 'BARBORA CPU' AND
  LocationId = 'a1b2c3d4-...'

Result: LocationPartition found ✓ OR PartitionNotFound ✗

Key: VŠECHNY 3 podmínky musí být splněny najednou!
- Name (partitionName) MUSÍ odpovídat
- AggregationName (aggregationName) MUSÍ odpovídat (pokud není null)
- LocationId (nalezen v kroku 1b) MUSÍ odpovídat
```

---

## STEP 2: Call HEAppE APIs

Když jsou data validní, zavoláme HEAppE pro získání projektů a matching NodeType.

### 2a) GET /Projects/{resourceName}
```
Call: heappeService.GetProjectByResourceName(connectionInfo, "atr-25-4")
Output: ProjectModel
{
  "id": 42,  ← KRITICKÉ! Toto ID použijeme v kroku 3
  "name": "TestProject",
  "accountingString": "...",
  "commandTemplates": [
    { "id": 10, "name": "ExistingTemplate" },
    ...
  ],
  "nodeTypes": [...],
  ...
}

Error: HEAppE unreachable OR invalid credentials OR resourceName not found in HEAppE
```

### 2b) GET /Clusters → Find Matching NodeType
```
Call: heappeService.GetClusters(connectionInfo)

Output: ClusterModel[]
[
  {
    "name": "Barbora",  ← Hledáme cluster s tímto jménem
    "nodeTypes": [
      {
        "id": 155,  ← KRITICKÉ! Toto ID použijeme v kroku 3
        "name": "CPU Node",
        "queue": "qcpu_exp",  ← Musí odpovídat partitionName
        "projects": [{ "id": 42 }],  ← Musí obsahovat projectId z 2a!
        "clusterNodeTypeAggregation": {
          "name": "BARBORA CPU"  ← Musí odpovídat aggregationName
        }
      },
      ...
    ]
  },
  ...
]

Matching Algorithm (kod):
nodeType = clusters
  .Where(cluster => cluster.name == "Barbora")  // locationName
  .SelectMany(c => c.NodeTypes.Where(n => n.Projects.Any(p => p.Id == 42)))  // projectId
  .Where(n => n.queue == "qcpu_exp"  // partitionName
    && n.ClusterNodeTypeAggregation.name == "BARBORA CPU")  // aggregationName
  .FirstOrDefault();

Result: nodeTypeId=155 ✓ OR NodeTypeForQueueNotFound ✗

KRITICKÉ: Všechny 4 kritéria musí matchovat NAJEDNOU!
```

---

## STEP 3: Create Command Template in HEAppE

Když máme ProjectId (z 2a) a NodeTypeId (z 2b), můžeme vytvořit šablonu.

### POST /CommandTemplates
```
Call: heappeService.CreateCommandTemplate(...)

Request Body (agregovaná data):
{
  "name": "testproject_MyTemplate",  ← POZOR: name se mění!
                                       Format: projectShortName + "_" + templateName
  "projectId": 42,                     ← Z Step 2a
  "nodeTypeId": 155,                   ← Z Step 2b
  "executionCommand": "/scripts/job.sh",  ← Z requestu (bez změny)
  "preparationCommand": "/scripts/prep.sh",  ← Z requestu (bez změny)
  "description": "Test template",
  "type": 0,
  "parameters": [
    { "name": "NODES", "description": "Number of nodes" }
  ]
}

Response: 202 Accepted
Local Database: ProjectCommandTemplate, ProjectCommandTemplatePartition records vytvořeny
```

---

## ✓ SUCCESS

Template je vytvořena! Co se stalo:
- Template vytvořena v HEAppE s projectId=42, nodeTypeId=155
- Lokální databáze aktualizována: ProjectCommandTemplate records
- Response: 202 Accepted
- Template je nyní dostupná pro odesílání jobů na partition 'qcpu_exp' v 'Barbora'

---

## ✗ COMMON ERROR SCENARIOS

| Chyba | Krok | Příčina | Řešení |
|-------|------|---------|--------|
| `InvalidLocation` | 1b | LocationName neexistuje | Ověřit dostupné lokace v GET /Partitions endpoint |
| `PartitionNotFound` | 1c | Partition neexistuje v DB | Kontrola: partitionName + aggregationName v DB |
| HEAppE connection error | 2a/2b | HEAppE nedostupný | Ověřit heappeInstance config, credentials, URI |
| `NodeTypeForQueueNotFound` | 2b | Žádný NodeType nevyhovuje | Partition existuje, ale queue na clusteru ne |

---

## CRITICAL: The 4 Matching Points (Where Things Fail Most Often)

### ❶ LOCATION NAME MATCHING
```
Your Input: locationName = "Barbora"
Source: GetValidLocationNames() response
Required: Exact match (case-insensitive in code)
Failure: InvalidLocation → LocationName not available for this project
```

### ❷ PARTITION IN DATABASE
```
Your Inputs: partitionName = "qcpu_exp", aggregationName = "BARBORA CPU"
Source: LocationPartitions table in local DB
Required: BOTH must match (+ LocationId)
Failure: PartitionNotFound → Partition hasn't been synced or typo in names
Note: aggregationName can be NULL in request/DB
```

### ❸ HEAPPE PROJECT ID
```
Your Input: resourceName = "atr-25-4"
Source: HEAppE API /Projects/{resourceName} endpoint
Critical Output: ProjectModel.id = 42
Used For: Filtering NodeTypes in Step 2b (nodeType.projects must include this id)
Failure: HEAppE unreachable/credentials invalid
```

### ❹ NODETYPE MATCHING (Most Complex!)
```
From HEAppE GET /Clusters response
ALL 4 criteria must match together (not just one or two):
  1. cluster.name == "Barbora"                (locationName)
  2. nodeType.queue == "qcpu_exp"            (partitionName)
  3. nodeType.aggregation.name == "BARBORA CPU"  (aggregationName)
  4. nodeType.projects.contains(id=42)       (projectId from Step 2a)

If ANY of these 4 don't match → NodeTypeForQueueNotFound
Most Common Cause: Partition exists in DB, but cluster config is different
```

---

## 🔍 Debugging Checklist

**Before posting to /CommandTemplates, ALWAYS verify by calling:**

```
GET /api/Heappe/Partitions/{projectShortName}/{resourceName}
```

**Example:**
```
GET /api/Heappe/Partitions/testproject/atr-25-4
```

**This endpoint shows EXACTLY what's available:**
- ✓ Available locationNames (check your locationName is here)
- ✓ Available partitions per location (verify partitionName + aggregationName combo exists)
- ✓ This is the SINGLE SOURCE OF TRUTH for valid combinations

**Response Example:**
```json
{
  "locationName": "Barbora",
  "partitions": [
    {
      "partitionName": "qcpu_exp",
      "aggregationName": "BARBORA CPU"
    },
    {
      "partitionName": "gpu_queue",
      "aggregationName": null
    }
  ]
}
```

**What to Check:**
1. Is your `locationName` in the response? If not → use the correct name
2. Is your `partitionName + aggregationName` combination there? If not → HEAppE/DB sync issue
3. Can you get ProjectModel from HEAppE? If not → credentials/URI problem
4. Can you find NodeType matching all 4 criteria? If not → cluster config mismatch

---

## Example Flow (Happy Path)

```
Input:
  projectShortName: "testproject"
  resourceName: "atr-25-4"
  locationName: "Barbora"
  partitionName: "qcpu_exp"
  aggregationName: "BARBORA CPU"
  name: "MyTemplate"

↓ STEP 1a: GetProjectResourceAllocation ✓
  Project "testproject" exists, user has access

↓ STEP 1b: GetValidLocationNames ✓
  "Barbora" → locationId = "a1b2c3d4-..."

↓ STEP 1c: Query LocationPartitions ✓
  Found: LocationPartition(name="qcpu_exp", aggregation="BARBORA CPU")

↓ STEP 2a: GET /Projects/atr-25-4 ✓
  ProjectModel.id = 42

↓ STEP 2b: GET /Clusters + Match ✓
  cluster="Barbora", queue="qcpu_exp", aggregation="BARBORA CPU", projectId=42 → nodeTypeId=155

↓ STEP 3: POST /CommandTemplates ✓
  Created with name="testproject_MyTemplate", projectId=42, nodeTypeId=155

✓ SUCCESS (202 Accepted)
```

---

## Example Flow (Error Case)

```
Input: (same as above)

↓ STEP 1a: GetProjectResourceAllocation ✓
↓ STEP 1b: GetValidLocationNames ✓
↓ STEP 1c: Query LocationPartitions ✗
  ERROR: PartitionNotFound
  
Reason: Database doesn't have LocationPartition with name="qcpu_exp"
Solution: 
  - Verify partition was synced from HEAppE
  - Check DB for typos in partition name
  - Call GET /Partitions to see available names
```

---

## Quick Reference Table

| Phase | What | Where | Fails If |
|-------|------|-------|----------|
| 0 | Request prep | Client | Invalid JSON format |
| 1a | Project validation | UserOrg | Project doesn't exist |
| 1b | Location lookup | UserOrg | Location not available |
| 1c | Partition lookup | Local DB | Partition not synced |
| 2a | Project ID fetch | HEAppE | HEAppE unreachable |
| 2b | NodeType match | HEAppE response | 4 criteria don't match |
| 3 | Create template | HEAppE | Invalid projectId/nodeTypeId |

---

## Important Notes

1. **Name transformation:** Request `name="MyTemplate"` becomes `"testproject_MyTemplate"` in HEAppE
2. **Complete paths required:** Script paths like `/scripts/job.sh` must be COMPLETE (not just filename)
3. **AggregationName is optional:** Can be `null` in request and DB
4. **Single source of truth:** Always use `/api/Heappe/Partitions/{projectShortName}/{resourceName}` endpoint to verify what's available
5. **All 4 NodeType criteria must match:** Even if 3 out of 4 match, you get NodeTypeForQueueNotFound error
