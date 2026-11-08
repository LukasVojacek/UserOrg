# Create Command Template — Quick Reference

## Step 1: Discover Available Options

```
GET /api/Heappe/Partitions/{projectShortName}/{resourceName}
```

**Example:**
```
GET /api/Heappe/Partitions/testproject/atr-25-4
```

**Poznámka:** partitions se filtrují podle dostupných lokací projektu/zdroje a párují se přes `LocationId` + `AggregationName` (`null` se bere jako prázdný řetězec). Výsledek neobsahuje duplicity.

**Response** — copy these values into Step 2:
```json
[
  {
    "PartitionName":   "qcpu_exp",
    "AggregationName": "BARBORA CPU",
    "LocationName":    "Barbora",
    "ProviderName":    "IT4I"
  }
]
```

---

## Step 2: Create the Template

```
POST /api/Heappe/CommandTemplates
```

**Request body:**
```json
{
  "projectIdentifier": {
    "projectShortName": "testproject",
    "resourceName":     "atr-25-4"
  },
  "locationName":    "Barbora",       ← from Step 1: LocationName
  "partitionName":   "qcpu_exp",      ← from Step 1: PartitionName
  "aggregationName": "BARBORA CPU",   ← from Step 1: AggregationName

  "name":                  "MyTemplate",
  "description":           "Template description",
  "executionScriptPath":   "/scripts/job.sh",
  "preparationScriptPath": "/scripts/prep.sh",
  "commandTemplateParameters": [
    { "name": "NODES", "description": "Number of nodes" }
  ]
}
```

**Success:** `201 Created`

> ⚠️ **Note:** The template name stored in HEAppE is automatically prefixed with the project short name:
> `{projectShortName}_{name}`  →  e.g. `testproject_MyTemplate`

---

## Field Reference

| Field | Value | Source |
|-------|-------|--------|
| `projectShortName` | Project short name | You know this |
| `resourceName` | Resource allocation name | You know this |
| `locationName` | Cluster name | **From Step 1** |
| `partitionName` | Queue / partition name | **From Step 1** |
| `aggregationName` | Node aggregation (CPU/GPU) | **From Step 1** (can be `null`) |
| `name` | Template name | You choose |
| `description` | Template description | Optional |
| `executionScriptPath` | Full path to execution script | Must be a complete path, e.g. `/scripts/job.sh` |
| `preparationScriptPath` | Full path to preparation script | Optional |
| `commandTemplateParameters` | List of template parameters | Optional |
