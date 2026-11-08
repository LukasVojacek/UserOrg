# 🚀 CreateTemplateEndpoint - Detailní Analýza

## 📋 Přehled

Endpoint pro vytváření HEAppE command templates (šablony příkazů). Propojuje projektový management systém (UserOrg) s HEAppE klastrovacím systémem.

**Cíl**: Vytvořit command template na HEAppE a v lokálním systému jednoduše a bezpečně

---

## 🏗️ Architektura - Fluent Pipeline Pattern

Endpoint implementuje **fluent chain pattern** (řetězení asynchronních operací):

```
Request
   ↓
[Validace & Příprava] → PreparedResourceData
   ↓
[Načtení HEAppE Projektu] → (PreparedResourceData, ProjectModel)
   ↓
[Načtení NodeType] → (PreparedResourceData, ProjectModel, NodeTypeModel)
   ↓
[Vytvoření Šablony] → VoidResult / Error
   ↓
Response (202 Accepted / 400 Bad Request)
```

---

## 📊 Datové Struktury

### 1. **Vstupní Požadavek** (`CreateTemplateRequest`)

```csharp
{
  ProjectIdentifier {
    ProjectShortName: string,      // např. "PROJECT1"
    ResourceName: string           // název zdroje
  },
  PartitionName: string,           // HEAppE queue
  LocationName: string,            // fyzické umístění
  AggregationName?: string,        // volitelné seskupení
  Name: string,                    // název šablony
  Description?: string,            // popis
  PreparationScriptPath?: string,  // cesta k prep skriptu (ÚPLNÁ cesta!)
  ExecutionScriptPath?: string,    // cesta k exec skriptu (ÚPLNÁ cesta!)
  CommandTemplateParameters: [
    {
      Name: string,                // parametr
      Description: string
    }
  ]
}
```

### 2. **Interní Data** (`PreparedResourceData`)

Agreguje všechna nezbytná data během pipeline:

```csharp
record PreparedResourceData(
  ProjectResourceInfoEntry Project,        // projektový zdroj z UserOrg
  ProjectResourceInfoSyncEntry Resource,   // synchronizovaný zdroj
  Guid LocationId,                         // ID lokace
  string LocationName,                     // jméno lokace
  BaseHeappeConnectionInfo ConnectionInfo, // HEAppE spojení
  LocationPartition Partition              // queue + agregace
);
```

---

## 🔄 Detailní Flow - Krok za Krokem

### **KROK 1️⃣: Validace a Příprava Zdrojů**

```
ValidateAndPrepareResourceDataAsync()
│
├─ userorgService.GetProjectResourceAllocation()
│  └─ ✓ Ověří existenci projektu a zdroje
│
├─ userorgService.GetValidLocationNames()
│  └─ ✓ Načte seznam validních lokací (HPC + Quantum)
│
├─ LocationName validace
│  └─ ✗ InvalidLocation: Pokud LocationName neexistuje
│
├─ userorgService.GetProjectResourceAllocationWithLocations()
│  └─ ✓ Načte detaily zdroje s lokacemi
│
├─ ResourceLocation nalezení
│  └─ ✗ ResourceLocationNotFound: Pokud lokace není linked
│
├─ HeappeInstance validace
│  └─ ✗ InvalidHeappeInstance: Chybí Identifier nebo Uri
│
├─ credentialsService.GetCredentials()
│  └─ ✓ Načte API klíč pro HEAppE
│
└─ LocationPartition lookup
   └─ ✗ PartitionNotFound: Pokud queue neexistuje
```

**Výstup**: `PreparedResourceData` - všechno co potřebujeme

---

### **KROK 2️⃣: Načtení HEAppE Projektu**

```
GetHeappeProjectAsync(PreparedResourceData, CreateTemplateRequest)
│
└─ heappeService.GetProjectByResourceName(connectionInfo, resourceName)
   └─ ✗ Error z HEAppE API (projekt neexistuje)
   
Výstup: (PreparedResourceData, ProjectModel)
```

**Důvod**: Potřebujeme HEAppE Project ID pro vytvoření šablony

---

### **KROK 3️⃣: Vyhledání NodeType (Typ Výpočetního Uzlu)**

```
GetNodeTypeAsync(PreparedResourceData, ProjectModel)
│
├─ heappeService.GetClusters(connectionInfo)
│  └─ ✓ Vrátí clustery s NodeTypes
│
└─ Filtrování:
   1. Nalezení clusteru dle LocationName (case-insensitive)
   2. Filtr: NodeType musí obsahovat HEAppE projekt
   3. Filtr: queue musí odpovídat PartitionName
   4. Filtr: agregace musí odpovídat AggregationName
   
   └─ ✗ NodeTypeForQueueNotFound: Žádný match
```

**Výstup**: `(PreparedResourceData, ProjectModel, NodeTypeModel)`

**Proč**: NodeType definuje specifickou hardware konfiguraci

---

### **KROK 4️⃣: Vytvoření Command Šablony**

```
CreateCommandTemplateAsync(
  PreparedResourceData,
  ProjectModel,
  NodeTypeModel,
  CreateTemplateRequest
)
│
├─ BuildCommandTemplateRequest()
│  ├─ Normalizace cest (null → null, prázdný string → null)
│  ├─ Transformace parametrů
│  └─ Vytvoření CommandTemplateCreateModel
│
└─ createService.CreateCommandTemplateAsync()
   ├─ Vytvoření na HEAppE (pokud neexistuje)
   ├─ Vytvoření lokálně (pokud neexistuje)
   └─ ✓ Synchronizace států
```

---

## 🛡️ Validační Řetězec

```
┌─────────────────────────────────────────────────────────┐
│ VALIDACE A CHYBY NA CESTĚ                              │
├─────────────────────────────────────────────────────────┤
│                                                          │
│ 1. Project + Resource existence                         │
│    └─ UserOrg service error                            │
│                                                          │
│ 2. Location validace                                    │
│    └─ InvalidLocation (LocationName neexistuje)        │
│                                                          │
│ 3. Resource-Location asociace                           │
│    └─ ResourceLocationNotFound                         │
│                                                          │
│ 4. HEAppE Instance korektnost                           │
│    └─ InvalidHeappeInstance (Identifier/Uri chybí)     │
│                                                          │
│ 5. Credentials                                          │
│    └─ CredentialsService error                         │
│                                                          │
│ 6. Partition (queue + agregace) existence              │
│    └─ PartitionNotFound                                │
│                                                          │
│ 7. HEAppE Project existence                             │
│    └─ HEAppE API error                                 │
│                                                          │
│ 8. NodeType matching                                    │
│    └─ NodeTypeForQueueNotFound                         │
│                                                          │
│ 9. Šablona vytvoření                                    │
│    └─ FailedToCreateProjectCommandTemplate             │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## 🔌 Integrační Body

### **Služby Použité**

| Služba | Odpovědnost |
|--------|-------------|
| **IUserorgService** | Projekt, zdroj, lokace, přidělení |
| **IHeappeService** | Projekt na HEAppE, clustery, node types |
| **ICredentialsService** | API klíče pro HEAppE instance |
| **HeappeIntegrationDbContext** | Lokální DB (partice) |
| **IProjectCommandTemplateCreateService** | Vytvoření šablony |

### **Důležité Poznámky**

⚠️ **Cesty skriptů**:
- `ExecutionScriptPath` a `PreparationScriptPath` musí být **KOMPLETNÍ cesty**
- Nejsou transformovány ani doplňovány
- Příklad: `/scripts/jobscript.sh` (nikoliv jen `jobscript.sh`)

---

## 📝 HTTP Endpoint Konfigurace

```
POST /CommandTemplates
```

### **Dokumentace**

- **Název**: CreateTemplateEndpoint
- **Shrnutí**: Create HEAppE command template
- **Popis**: Creates a command template in both HEAppE and the project resource management system
- **Tag**: Heappe
- **Content-Type**: application/json

### **Odpovědi**

| Status | Popis |
|--------|-------|
| **202 Accepted** | Šablona bude vytvořena (asynchronně) |
| **400 Bad Request** | Validační chyba v požadavku |

### **Bezpečnost**

- ⚡ Deaktivován v režimu `ReadOnlyMode`

---

## 🚨 Chybové Scénáře a Řešení

### Chyba: `InvalidLocation`
**Příčina**: LocationName neexistuje v UserOrg
**Řešení**: Ověřit dostupné lokace pro projekt

### Chyba: `ResourceLocationNotFound`
**Příčina**: Zdroj není propojen s danou lokací
**Řešení**: Zkonfigurovat resource-location mapping

### Chyba: `InvalidHeappeInstance`
**Příčina**: HEAppE instance bez Identifier nebo Uri
**Řešení**: Zkonfigurovat HEAppE instanci v databázi

### Chyba: `PartitionNotFound`
**Příčina**: Queue/agregace neexistuje v lokaci
**Řešení**: Vytvořit LocationPartition v databázi

### Chyba: `NodeTypeForQueueNotFound`
**Příčina**: Queue neobsahuje projekt nebo agregace neodpovídá
**Řešení**: Ověřit konfiguraci v HEAppE

---

## 📈 Diagram Toku

```
┌──────────────────────┐
│ HTTP POST Request    │
│ CreateTemplateRequest│
└──────────┬───────────┘
           │
           v
┌──────────────────────────────────────────┐
│ KROK 1: Validace & Příprava              │
│ ├─ Projekt existence                     │
│ ├─ Lokace validace                       │
│ ├─ Resource-location lookup              │
│ ├─ HEAppE credentials                    │
│ └─ Partition lookup                      │
└──────────┬───────────────────────────────┘
           │ PreparedResourceData
           v
┌──────────────────────────────────────────┐
│ KROK 2: Načtení HEAppE Projektu          │
│ ├─ HEAppE API call                       │
│ └─ ProjectModel                          │
└──────────┬───────────────────────────────┘
           │ (PreparedResourceData, ProjectModel)
           v
┌──────────────────────────────────────────┐
│ KROK 3: Vyhledání NodeType               │
│ ├─ HEAppE clustery                       │
│ ├─ Filtrování dle lokace                 │
│ ├─ Filtrování dle projektu               │
│ ├─ Filtrování dle queue                  │
│ └─ NodeTypeModel                         │
└──────────┬───────────────────────────────┘
           │ (All data + NodeTypeModel)
           v
┌──────────────────────────────────────────┐
│ KROK 4: Vytvoření Šablony                │
│ ├─ Build CommandTemplateRequest          │
│ ├─ HEAppE creation                       │
│ ├─ Local DB creation                     │
│ └─ VoidResult                            │
└──────────┬───────────────────────────────┘
           │
           v
┌──────────────────────────────────────────┐
│ HTTP Response                             │
│ ├─ 202 Accepted (success)                │
│ └─ 400 Bad Request + Error (failure)     │
└──────────────────────────────────────────┘
```

---

## 💡 Klíčové Vzory a Praktiky

### **1. Fluent Pipeline Pattern**
```csharp
result = await request
    .ValidateAndPrepareResourceDataAsync(...)
    .GetHeappeProjectAsync(...)
    .GetNodeTypeAsync(...)
    .CreateCommandTemplateAsync(...);
```

**Výhody**:
- Čitelný řetězec operací
- Snadná přidání nových kroků
- Error propagation na konci

### **2. Result<T, Error> Pattern**
- Explicitní error handling (bez exceptions)
- Kombinovatelný s LINQ
- Type-safe úspěch/selhání

### **3. Separation of Concerns**
- Endpoint = pouze orchestrace
- `CreateTemplateFlowExtensions` = business logika
- `IProjectCommandTemplateCreateService` = vytváření

### **4. Location-based Dispatching**
Systém má koncept "Location" (fyzické umístění):
- HPC locations (high-performance computing)
- Quantum locations
- Každá má vlastní HEAppE instanci a partice

---

## 🎯 Shrnutí

**Endpoint zajišťuje**:
✅ Komplexní validaci na více vrstvách  
✅ Propojení UserOrg s HEAppE  
✅ Atomické operace (vše nebo nic)  
✅ Explicitní error handling  
✅ Bezpečný transfer dat mezi systémy  

**Klíčová rozhodnutí**:
- Fluent chain místo callback hell
- Validace zdrojů v kroku 1
- Lazy loading dat (až když je potřeba)
- Normalizace null/empty stringů

---

# 🔗 Externí Endpointy a API Volání

## 📡 IUserorgService

Zprostředkovává komunikaci s UserOrg systémem (správa projektů a zdrojů).

### 1. **GetProjectResourceAllocation()**

```csharp
Task<Result<ProjectResourceInfoEntry, Error>> GetProjectResourceAllocation(
    string projectShortName,
    string resourceName,
    CancellationToken cancellationToken)
```

**Kdy se volá**: Krok 1 - Validace & Příprava

**Co dělá**:
- Vyhledá projekt dle `projectShortName`
- Vyhledá zdroj dle `resourceName` v projektu
- Vrátí základní info o přidělení

**Selhání scénáře**:
- Projekt neexistuje → Error
- Zdroj neexistuje → Error
- Práce není přidělena → Error

**Data vrácena**:
```csharp
ProjectResourceInfoEntry {
  Project.Id: Guid,
  Project.Name: string,
  // ... další metadata
}
```

---

### 2. **GetValidLocationNames()**

```csharp
Task<Result<HashSet<ProviderLocationInfo>, Error>> GetValidLocationNames(
    string projectShortName,
    string resourceName,
    int[] locationTypeIds,  // [QuantumLocationTypeId=7, HpcLocationTypeId=1]
    CancellationToken cancellationToken)
```

**Kdy se volá**: Krok 1 - Validace lokace

**Co dělá**:
- Vrátí všechny dostupné lokace pro projekt+zdroj
- Filtruje dle `locationTypeIds` (jen HPC a Quantum)
- Vrátí množinu `ProviderLocationInfo` (LocationId, LocationName, AggregationName, ProviderName)

**Selhání scénáře**:
- Projekt/zdroj neexistuje → Error
- Žádné lokace dostupné → Error (prázdný dict)

**Data vrácena**:
```csharp
Dictionary<Guid, LocationInfo> {
  [locationId1] = { LocationName: "HPC-Prague", ... },
  [locationId2] = { LocationName: "Quantum-Vienna", ... }
}
```

**Kritický check** 🔍:
```csharp
var locationKV = locationNames.FirstOrDefault(x => x.Value.LocationName == request.LocationName);
if (locationKV.Key == default)  // Pokud klíč je default (0), lokace neexistuje
    return Errors.InvalidLocation();
```

---

### 3. **GetProjectResourceAllocationWithLocations()**

```csharp
Task<Result<ProjectResourceInfoSyncEntry, Error>> GetProjectResourceAllocationWithLocations(
    string projectShortName,
    string resourceName,
    CancellationToken cancellationToken)
```

**Kdy se volá**: Krok 1 - Detailní lookup

**Co dělá**:
- Vrátí projekt+zdroj s fullem detailem lokací
- Vrátí seznam `HeappeInstances` s URI a credentials

**Data vrácena**:
```csharp
ProjectResourceInfoSyncEntry {
  Project: ProjectResourceInfoEntry,
  Resource: ResourceInfo {
    Id: Guid,
    Locations: IList<ResourceLocation> {
      LocationId: Guid,
      HeappeInstance: {
        Identifier: string,  // "heappe-prod"
        Uri: Uri             // https://heappe.example.com
      }
    }
  }
}
```

---

## 🔗 IHeappeService

Komunikace s HEAppE klastrovacím systémem.

### 1. **GetProjectByResourceName()**

```csharp
Task<Result<ProjectModel, Error>> GetProjectByResourceName(
    BaseHeappeConnectionInfo connectionInfo,
    string resourceName,
    CancellationToken cancellationToken)
```

**Kdy se volá**: Krok 2 - Načtení HEAppE projektu

**Co dělá**:
- Volá HEAppE API s autentizací (`connectionInfo`)
- Hledá projekt dle `resourceName`

**Selhání scénáře**:
- HEAppE API chyba → Error
- Projekt neexistuje na HEAppE → Error

**Data vrácena**:
```csharp
ProjectModel {
  Id: long,           // HEAppE ID
  Name: string,
  // ... metadata
}
```

---

### 2. **GetClusters()**

```csharp
Task<Result<IEnumerable<ClusterModel>, Error>> GetClusters(
    BaseHeappeConnectionInfo connectionInfo,
    CancellationToken ct)
```

**Kdy se volá**: Krok 3 - Vyhledání NodeType

**Co dělá**:
- Vrátí všechny clustery v HEAppE instanci
- Každý cluster má `NodeTypes` s `Queue` informacemi

**Data vrácena**:
```csharp
IEnumerable<ClusterModel> {
  [0] {
    Name: "HPC-Prague",          // Lokace
    NodeTypes: [
      {
        Id: long,
        Name: "GPU-Node",
        Queue: "gpu.q",           // Queue jméno
        Projects: [{ Id: long }], // Projekty které mají přístup
        ClusterNodeTypeAggregation: {
          Name: "gpu-agg"         // Agregace (muže být null)
        }
      }
    ]
  }
}
```

**Komplexní Filtrování** 🔍:
```csharp
var nodeType = nodeTypesResult.Value
    .Where(x=> x.Name.Equals(resourceData.LocationName, StringComparison.OrdinalIgnoreCase))
    .SelectMany(x => x.NodeTypes.Where(n => n.Projects.Any(p => p.Id == heappeProject.Id)))
    .Where(x=>x.Queue.Equals(resourceData.Partition.Name, StringComparison.OrdinalIgnoreCase)
        && (x.ClusterNodeTypeAggregation.Name != null 
            && x.ClusterNodeTypeAggregation.Name.Equals(resourceData.Partition.AggregationName, StringComparison.OrdinalIgnoreCase)))
    .FirstOrDefault();
```

Hledáme NodeType kde:
1. Cluster se jmenuje jako `LocationName` (case-insensitive)
2. NodeType má přístup k našemu HEAppE projektu
3. NodeType queue se rovná `PartitionName` (case-insensitive)
4. Agregace se rovná `AggregationName` (case-insensitive)

---

### 3. **CreateCommandTemplateAsync()**

```csharp
Task<Result<CommandTemplateModel, Error>> CreateCommandTemplateAsync(
    BaseHeappeConnectionInfo connectionInfo,
    CommandTemplateCreateModel createModel,
    CancellationToken ct)
```

**Kdy se volá**: Krok 4 - Vytvoření šablony (standardní)

**Co dělá**:
- Vytvoří command template na HEAppE
- Vrátí model s ID z HEAppE

**Parametry**:
```csharp
CommandTemplateCreateModel {
  Name: string,
  Description: string?,
  ExecutableFile: string?,         // MUSÍ být úplná cesta!
  PreparationScript: string?,      // MUSÍ být úplná cesta!
  ProjectId: long,                 // HEAppE project ID
  NodeTypeId: long,                // HEAppE node type ID
  TemplateParameters: IList<...>   // Dynamické parametry
}
```

**Selhání scénáře**:
- HEAppE API error → Error
- Šablona s tímto jménem již existuje → Error
- Projekt/NodeType neexistuje → Error

---

### 4. **CreateGenericCommandTemplateAsync()**

```csharp
Task<Result<CommandTemplateModel, Error>> CreateGenericCommandTemplateAsync(
    BaseHeappeConnectionInfo connectionInfo,
    GenericCommandTemplateCreateModel model,
    CancellationToken ct)
```

**Kdy se volá**: Krok 4 - Vytvoření šablony (generický typ)

**Rozdíl od standard**:
- Bez parametrů
- Bez ExecutableFile
- Pouze PreparationCommand

---

## 🔑 ICredentialsService

Správa přihlašovacích údajů pro HEAppE instance.

### **GetCredentials()**

```csharp
Task<Result<HeappeInstanceCredentials, Error>> GetCredentials(
    string heappeInstanceIdentifier,
    CancellationToken cancellationToken)
```

**Kdy se volá**: Krok 1 - Příprava připojení

**Co dělá**:
- Načte API klíč pro konkrétní HEAppE instanci
- Dle `heappeInstanceIdentifier` (např. "heappe-prod")

**Selhání scénáře**:
- Credentials neexistují → Error
- Credentials jsou expired → Error
- Přístup odepřen → Error

**Data vrácena**:
```csharp
HeappeInstanceCredentials {
  SecretApiKey: string  // Pro autentizaci
}
```

---

## 💾 HeappeIntegrationDbContext (Database)

Lokální databáze s entitami.

### **LocationPartitions Tabulka**

Mapuje queue do lokací.

```csharp
LocationPartition {
  Id: Guid,
  Name: string,                    // Queue jméno (např. "gpu.q")
  AggregationName: string?,        // Agregace (null nebo "gpu-agg")
  LocationId: Guid,
  // ... vztahy
}
```

**Lookup v kroku 1**:
```csharp
var partition = await context.LocationPartitions
    .FirstOrDefaultAsync(p => 
        p.Name == request.PartitionName 
        && p.AggregationName == requestedAggregationName  // null == null
        && p.LocationId == locationId,
        cancellationToken);
```

---

### **ProjectCommandTemplates Tabulka**

Lokální evidence šablon.

```csharp
ProjectCommandTemplate {
  Id: Guid,
  Name: string,
  Type: ECommandTemplateType,      // Generic, Standard, UserDefined
  ProjectId: Guid,
  ProjectResourceId: Guid,
  ProjectCommandTemplatePartitions: ICollection<...>
}
```

**Lookup**:
```csharp
var projectCmdTemplate = await dbContext.Set<ProjectCommandTemplate>()
    .FirstOrDefaultAsync(f =>
        f.Name == templateName
        && f.ProjectId == projectId
        && f.ProjectResourceId == resourceId,
        ct);
```

---

### **ProjectCommandTemplatePartitions Tabulka**

Mapuje šablonu k partici s stavem.

```csharp
ProjectCommandTemplatePartition {
  Id: Guid,
  CommandTemplateId: long?,                      // HEAppE ID (null pokud HEAppE selhalo)
  State: ECommandTemplateState,                  // Pending, Active, Inactive
  LocationPartition: LocationPartition           // Reference na queue
}
```

**Stavy**:
- `Pending` - Čeká na HEAppE vytvoření
- `Active` - Vytvořeno a povoleno
- `Inactive` - Vytvořeno ale vypnuto

---

# 🔥 Edge Cases

## 1. **Null vs. Empty String Normalizace**

```csharp
private static string? NormalizeNullableStringInternal(string? value)
    => string.IsNullOrEmpty(value) ? null : value;
```

**Edge Case**: 
- Input: `"  "` (pouze mezery)
- Problém: Normalizace vrátí `"  "` (není null)
- **Řešení**: Mělo by být `string.IsNullOrWhiteSpace()`

**Scénář**:
```json
{
  "ExecutionScriptPath": "   ",
  "PreparationScriptPath": null
}
```
↓
```csharp
{
  ExecutableFile: "   ",      // ← PROBLÉM! Projde validaci
  PreparationScript: null     // ✓ OK
}
```

---

## 2. **Case-Insensitive Srovnání Ale Case-Sensitive Uložení**

```csharp
// Vyhledání (case-insensitive)
var locationKV = locationNames.FirstOrDefault(
    x => x.Value.LocationName == request.LocationName);

// Uložení (original case)
new PreparedResourceData(..., locationName: request.LocationName, ...)
```

**Edge Case**:
- Input: `LocationName: "hpc-prague"`
- DB má: `LocationName: "HPC-Prague"`
- ✓ Vyhledání projde (case-insensitive)
- ✗ Pozdější filtrování může selhati, pokud bude case-sensitive

**Scénář**:
```
Request: LocationName = "gpu-vienna"
DB: LocationName = "GPU-VIENNA"
                    ↓
Vyhledání OK, ale string se uloží s originálním case!
```

---

## 3. **Agregace NULL vs. prázdný String**

```csharp
string? requestedAggregationName = string.IsNullOrEmpty(request.AggregationName) 
    ? null 
    : request.AggregationName;

// Pozdější filtrování
var partition = await context.LocationPartitions
    .FirstOrDefaultAsync(p => 
        p.Name == request.PartitionName 
        && p.AggregationName == requestedAggregationName  // null == null ✓
        && p.LocationId == locationId,
        cancellationToken);
```

**Edge Case 1: AggregationName je null vs. Empty**
- Input: `AggregationName: null`
  - Normalizace: `null`
  - Lookup: `p.AggregationName == null` ✓
  
- Input: `AggregationName: ""`
  - Normalizace: `null` (IsNullOrEmpty)
  - Lookup: `p.AggregationName == null` ✓

- Input: `AggregationName: "  "` (mezery)
  - Normalizace: `"  "` (NOT IsNullOrEmpty!)
  - Lookup: `p.AggregationName == "  "` ✗ Nebude matchovat null

**Problém**: Whitespace-only string procházelo normalizací

---

## 4. **Filtrování NodeType s NULL Agregací**

```csharp
.Where(x=>x.Queue.Equals(resourceData.Partition.Name, StringComparison.OrdinalIgnoreCase)
    && (x.ClusterNodeTypeAggregation.Name != null 
        && x.ClusterNodeTypeAggregation.Name.Equals(
            resourceData.Partition.AggregationName, 
            StringComparison.OrdinalIgnoreCase)))
```

**Edge Case: ClusterNodeTypeAggregation je NULL**

```
Partition.AggregationName = null
Node.ClusterNodeTypeAggregation = null
                              ↓
x.ClusterNodeTypeAggregation.Name  // ✗ NullReferenceException!
```

**Logika**:
```csharp
(x.ClusterNodeTypeAggregation.Name != null 
    && x.ClusterNodeTypeAggregation.Name.Equals(...))
```

Pokud `ClusterNodeTypeAggregation` je null:
- `x.ClusterNodeTypeAggregation.Name` → NullReferenceException
- Logika je špatná: mělo by být `.ClusterNodeTypeAggregation?.Name`

---

## 5. **Duplikátní Šablony se Stejným Jménem**

```csharp
var projectCmdTemplate = await GetExistingProjectCommandTemplateAsync(
    templateName, projectId, resourceId, ct);

if (projectCmdTemplate is null)
{
    projectCmdTemplate = ProjectCommandTemplate.Create(templateName, templateType, projectId, resourceId);
    // ... přidej partition
    dbContext.Set<ProjectCommandTemplate>().Add(projectCmdTemplate);
}
else
{
    // Přidej dalšímu partition k existující šabloně
    projectCmdTemplate.ProjectCommandTemplatePartitions.Add(projectCommandTemplatePartition);
    dbContext.Set<ProjectCommandTemplate>().Update(projectCmdTemplate);
}
```

**Edge Case: Race Condition**

Thread A a B oba vytváří šablonu se stejným jménem:
1. Thread A: Lookup nenajde existující šablonu
2. Thread B: Lookup nenajde existující šablonu
3. Thread A: Create + Save
4. Thread B: Create + Save → Constraint violation!

**Řešení**: Databázový constraint by měl zabránit duplikátům

---

## 6. **HEAppE Vytvoření Selže, Lokální OK**

```csharp
// Krok 2: HEAppE vytvoření selže
var createCommandTemplateResult = await CreateHEAppECommandTemplate(...);
if (!createCommandTemplateResult.IsSuccess)
{
    logger.LogFailedToCreateHeappeTemplate(templateCreateModel.Name);
}

// Krok 3: Ale lokální se přesto vytvoří!
var projectCommandTemplateResult = await CreateProjectCommandTemplate(
    ..., 
    createCommandTemplateResult,  // ← IsSuccess=false!
    ...);
```

**Edge Case: Nekonzistentní stav**

Výsledek:
```
ProjectCommandTemplate {
  Name: "MyTemplate",
  ProjectCommandTemplatePartition {
    CommandTemplateId: 0,        // Default (null) - HEAppE selhalo
    State: Pending               // Čeká na HEAppE
  }
}
```

↓ Šablona je v DB ale není na HEAppE!

---

## 7. **IsEnabled Flag v HEAppE**

```csharp
var newState = heappeTemplate.IsEnabled
    ? ECommandTemplateState.Active
    : ECommandTemplateState.Inactive;
```

**Edge Case: IsEnabled Default Value**

Když se šablona vytvoří, jaká je default hodnota `IsEnabled`?
- ✓ True → Stav = Active
- ✗ False → Stav = Inactive

**Bez znalosti HEAppE logiky není jasné, co se stane!**

---

## 8. **ConnectionInfo Timeout**

```csharp
var connectionInfo = HeappeService.GetHeappeServiceConnectionInfo(
    resourceLocation.HeappeInstance.Uri,
    credentialsResult.Value.SecretApiKey);

// Pozdější volání
var heappeProject = await heappeService.GetProjectByResourceName(
    prepared.Value,
    request,
    cancellationToken);
```

**Edge Case: Credentials Expiraci**

Pokud credentials vzácně expirují mezi krokem 1 a krokem 2:
1. Credentials načteny v kroku 1 ✓
2. Připojení vytvořeno v kroku 1 ✓
3. Krok 2-3: Credentials jsou invalidy → HEAppE API 401 error

---

## 9. **Cancellation Token Timeout**

```csharp
public async Task<IResult> HandleAsync(
    ...,
    CancellationToken cancellationToken)
{
    var result = await request
        .ValidateAndPrepareResourceDataAsync(..., cancellationToken)
        .GetHeappeProjectAsync(..., cancellationToken)
        .GetNodeTypeAsync(..., cancellationToken)
        .CreateCommandTemplateAsync(..., cancellationToken);
    
    // Pokud timeout mezi kroky:
    // Např. HEAppE vytvoření trvá dlouho a cancellationToken je signaled
    // ← Šablona MŮŽE být na HEAppE ale response nikdy nedojde
}
```

**Riziko**: Duplikát na HEAppE při retry

---

## 10. **LocationPartition Smazaná**

```csharp
var partition = await context.LocationPartitions
    .AsNoTracking()
    .FirstOrDefaultAsync(p => p.Name == request.PartitionName 
        && p.AggregationName == requestedAggregationName
        && p.LocationId == locationId,
        cancellationToken);

if (partition is null)
    return Errors.PartitionNotFound();
```

**Edge Case: Race Condition**

1. Validace projde (partition existuje)
2. Admin smaže partition z DB
3. Krok 4: Vytvoření selže → `PartitionNotFound`

**Řešení**: DB constraint - partition se nemůže smazat

---

# 📊 Souhrn Edge Cases

| # | Edge Case | Závažnost | Řešení |
|---|-----------|-----------|--------|
| 1 | Whitespace-only string | ⚠️ | Použít `IsNullOrWhiteSpace()` |
| 2 | Case-sensitivity mismatch | ⚠️ | Vždy case-insensitive lookup |
| 3 | Agregace null/empty | ⚠️ | Konsistentní handling |
| 4 | NullReferenceException filtrování | 🔴 | Použít safe navigation `?.` |
| 5 | Race condition - duplikáty | 🔴 | DB constraint + unique index |
| 6 | HEAppE selhání, lokální OK | 🟡 | Lepší error handling |
| 7 | IsEnabled default value | 🟡 | Dokumentovat HEAppE chování |
| 8 | Credentials expiraci | 🟡 | Retry logic, refresh creds |
| 9 | Cancellation token timeout | 🔴 | Idempotent design |
| 10 | Partition race condition | 🟡 | DB constraints |

---

# 🎯 Veřejné Shrnutí - Veškeré Volání

```
CreateTemplateEndpoint
│
└─ HandleAsync()
   │
   ├─→ ValidateAndPrepareResourceDataAsync()
   │   │
   │   ├─→ IUserorgService.GetProjectResourceAllocation()
   │   ├─→ IUserorgService.GetValidLocationNames()
   │   ├─→ IUserorgService.GetProjectResourceAllocationWithLocations()
   │   ├─→ ICredentialsService.GetCredentials()
   │   └─→ DbContext.LocationPartitions.FirstOrDefaultAsync()
   │
   ├─→ GetHeappeProjectAsync()
   │   └─→ IHeappeService.GetProjectByResourceName()
   │
   ├─→ GetNodeTypeAsync()
   │   └─→ IHeappeService.GetClusters()
   │
   └─→ CreateCommandTemplateAsync()
       │
       ├─→ IProjectCommandTemplateCreateService.CreateCommandTemplateAsync()
       │   │
       │   ├─→ IHeappeService.CreateCommandTemplateAsync()
       │   │   nebo
       │   └─→ IHeappeService.CreateGenericCommandTemplateAsync()
       │
       └─→ DbContext.ProjectCommandTemplates.*()
           └─→ DbContext.SaveChangesAsync()
```
