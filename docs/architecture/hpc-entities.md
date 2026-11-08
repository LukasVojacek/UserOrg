# HPC compute entities — HEAppE ↔ Userorg

Zaměřeno na **HPC (high performance computing) výpočetní entity**: cluster,
partitiony, agregace, účtovací formule a command templates. Org/admin entity
(Provider, ProjectResource, User) jsou mimo rozsah — viz
[creation lifecycle](heappe-userorg-entity-creation.md) a
[ownership](heappe-userorg-entity-ownership.md).

HPC výpočetní model vlastní **HEAppE** (source of truth). Userorg na něj jen
**odkazuje** referenčním id, nekopíruje jeho stav.

## HPC topologie a vazba do Userorgu

```mermaid
flowchart LR
    subgraph HEAppE["HPC compute model — owned by HEAppE"]
        Cluster["Cluster<br/>(login node, scheduler,<br/>timezone, port)"]
        NodeType["ClusterNodeType (partition)<br/>CoresPerNode, Queue, QoS,<br/>MaxWalltime, MaxNodesPerJob/User"]
        Aggregation["ClusterNodeTypeAggregation<br/>AllocationType, ValidityFrom/To"]
        Accounting["Accounting<br/>Formula, ValidityFrom/To, IsValid()"]
        CmdTemplate["CommandTemplate<br/>ExecutableFile, params,<br/>IsGeneric/IsEnabled, CreatedFrom"]
        Cluster -->|1:N| NodeType
        NodeType -->|N:1| Aggregation
        Aggregation -->|M:N via join| Accounting
        NodeType -->|1:N PossibleCommands| CmdTemplate
    end

    subgraph Userorg["Userorg references (no copy)"]
        Location["Location"]
        AssignedLocation["AssignedLocation (allocation)<br/>AggregationName"]
        ProjCmd["ProjectCommandTemplate<br/>(selection + governance)"]
    end

    Location -. "HEAppE instance URL + token" .-> Cluster
    AssignedLocation -. "AggregationId" .-> Aggregation
    ProjCmd -. "CommandTemplateId" .-> CmdTemplate
```

## Životní cyklus v čase

```mermaid
timeline
    title Životní cyklus HPC entit v čase
    Cluster registrace (HEAppE) : Admin vytvoří Cluster : + ClusterNodeType (partition) : + Aggregation (ValidityFrom) : + Accounting (Formula, ValidityFrom)
    Endpoint v Userorgu : Location ukazuje na HEAppE instanci (URL + token)
    Alokace schválena : AssignedLocation → AggregationId : StartDate začíná
    Command templates dostupné : sync/read-through : ProjectCommandTemplate → Active
    Provoz : joby běží na partitionách : Accounting účtuje dokud IsValid(now)
    Expirace alokace : EndDate < now (datová, žádný job) : partition states → Inactive
    Retirement : soft-delete IsDeleted : Cluster/partition/aggregation/accounting
```

## Časová platnost a její průnik

HPC entity nesou **dva různé typy času**:
- **Platnost (validity window):** `Aggregation.ValidityFrom/To` a
  `Accounting.ValidityFrom/To` — vyhodnocuje se `IsValid(now)` při čtení.
- **Alokace (allocation window):** `AssignedLocation`/`ProjectResource`
  `StartDate/EndDate` — čistě datové porovnání vůči `UtcNow`.

Command template je v okamžiku *T* skutečně použitelný jen v **průniku** všech
oken (alokace aktivní ∧ aggregation platná ∧ accounting platné ∧ template
Active/Enabled ∧ ne kill-switch):

```mermaid
gantt
    title Časová platnost HPC entit a jejich průnik (ilustrativní data)
    dateFormat YYYY-MM-DD
    axisFormat %Y-%m
    section Cluster
    Cluster (bez konce, dokud IsDeleted)        :active, c1, 2025-01-01, 730d
    section Aggregation
    ValidityFrom .. ValidityTo                  :a1, 2025-06-01, 2026-12-01
    section Accounting (formule)
    Formula v1 (ValidityFrom/To)                :done, f1, 2025-06-01, 2026-01-01
    Formula v2 (nová sazba)                      :f2, 2026-01-01, 2026-12-01
    section Alokace
    StartDate .. EndDate                        :crit, al1, 2025-09-01, 2026-03-01
    section Efektivně použitelné
    Command templates usable (průnik)           :active, u1, 2025-09-01, 2026-03-01
```

Pozorování z grafu:
- **Accounting se verzuje na časové ose** — formule v1 skončí, v2 (nová sazba)
  navazuje; job se účtuje formulí platnou v čase jeho startu (`IsValid(startTime)`).
- **Alokace je vnitřní okno** — obvykle užší než platnost aggregation/accounting;
  proto je efektivně použitelné okno určeno alokací, dokud ji nepřekročí konec
  platnosti aggregation/accounting.
- **Cluster nemá konec** — žije dokud `IsDeleted=false`.

## HPC entity — pole a vlastnictví

Vše vlastní **HEAppE**; entity žijí v `DomainObjects/`.

| Entita | Klíčová pole | Vazby |
|---|---|---|
| **Cluster** `ClusterInformation/Cluster.cs` | `Name`, `MasterNodeName` (login node), `DomainName`, `Port`, `TimeZone`, `SchedulerType` (SLURM/PBS/SGE), `ConnectionProtocol`, `CustomConfiguration`, `IsDeleted` | `NodeTypes: List<ClusterNodeType>`, `ClusterProjects`, `ProxyConnection?` |
| **ClusterNodeType** (partition) `ClusterInformation/ClusterNodeType.cs` | `Name`, `NumberOfNodes?`, `CoresPerNode`, `Queue`, `QualityOfService`, `ClusterAllocationName`, `MaxWalltime?`, `MaxNodesPerUser/Job?`, `IsDeleted` | `ClusterId`, `ClusterNodeTypeAggregationId?`, `PossibleCommands: List<CommandTemplate>` |
| **ClusterNodeTypeAggregation** `JobManagement/ClusterNodeTypeAggregation.cs` | `Name`, `AllocationType`, `ValidityFrom` (req), `ValidityTo?`, `IsDeleted` | `ClusterNodeTypes`, `ClusterNodeTypeAggregationAccountings` (join → Accounting) |
| **Accounting** (formule) `JobManagement/Accounting.cs` | **`Formula`**, `ValidityFrom` (req), `ValidityTo?`, `ConsumedResources`, `IsValid(startTime)` | M:N s aggregation přes `ClusterNodeTypeAggregationAccounting` |
| **CommandTemplate** `JobManagement/CommandTemplate.cs` | `ExecutableFile`, `CommandParameters`, `PreparationScript`, `ExtendedAllocationCommand`, `IsGeneric`, `IsEnabled`, `IsDeleted` | `ClusterNodeTypeId?`, `ProjectId?`, `CreatedFromId?` (self-ref), `TemplateParameters` |

**Účtovací formule je oddělená.** `Accounting.Formula` má vlastní okno platnosti
a přes join se váže na aggregation → jedna formule může sloužit více agregacím,
každá s nezávislým `ValidityFrom/To`.

## Userorg reference (jediné 3 vazby přes hranici)

| Userorg entita | Ukazuje na | Přes |
|---|---|---|
| `Location` | HEAppE **instance** clusteru | URL + token (token z Vaultu podle `HeappeInstance.Identifier`) |
| `AssignedLocation` (alokace) | `ClusterNodeTypeAggregation` | `AggregationName` → doporučeno migrovat na stabilní `AggregationId` |
| `ProjectCommandTemplate` | `CommandTemplate` | numerické `CommandTemplateId` |

Žádná HPC entita nežije v Userorg tabulce. Userorg-modul drží jen *selekci*
(který template projekt zvolil) + governance (whitelist/kill-switch), ne tělo
šablony.

## Vznik HPC entit (HEAppE)

**Jednotná cesta:** Admin REST API → `ManagementController` → `ManagementLogic`
→ `Save()`. Žádný seed/config — vše runtime, gated na roli **Administrator**.

| Entita | Create metoda | Endpoint |
|---|---|---|
| Cluster | `CreateCluster(...)` | `POST /heappe/management/Cluster` |
| ClusterNodeType | `CreateClusterNodeType(..., clusterId?, clusterNodeTypeAggregationId?)` | `POST /ClusterNodeType` |
| Aggregation | `CreateClusterNodeTypeAggregation(name, description, allocationType, validityFrom, validityTo?)` | `POST /ClusterNodeTypeAggregation` |
| Accounting | `CreateAccounting(formula, validityFrom, validityTo?)` | `POST /Accounting` |
| CommandTemplate | `CreateCommandTemplate` / `CreateGenericCommandTemplate` / `CreateCommandTemplateFromGeneric` | `POST /CommandTemplate` / `/GenericCommandTemplate` / `/CommandTemplateFromGeneric` |

**Read pro Userorg (read-through):** `ClusterInformationController.ListAvailableClusters`
(stačí `sessionCode`, ne admin) vrací clustery **s vnořenými partition a command
templates** — přirozený cíl, kdyby Userorg nahradil dnešní sync živým čtením.

## CommandTemplate — flags & derivace

- `IsGeneric` — nastaven při vzniku (`CreateGenericCommandTemplate` → true), nepřepíná se.
- `CreateCommandTemplateFromGeneric` — validuje `source.IsGeneric==true`, nový má `IsGeneric=false` + `CreatedFrom=<generic>` (= „vytvořeno z" reusable masteru).
- `IsEnabled` — default true, přepíná `Modify…(modelIsEnabled)`.
- `IsDeleted` — soft delete přes `Remove…`.

## Jediný netriviální stav — výběr CommandTemplate v Userorgu

`ECommandTemplateState`: `Unknown=0` · `Pending=1` (lokálně, ne v HEAppE) ·
`Active=2` (inicializováno v HEAppE) · `Inactive=3`.

```mermaid
stateDiagram-v2
    [*] --> Unknown: partition created (no state yet)
    Unknown --> Pending: local row created,<br/>not yet in HEAppE
    Pending --> Active: initialized in HEAppE
    Active --> Inactive: partition soft-deleted /<br/>kill-switch kills partition /<br/>allocation expired (dates)
    Inactive --> Active: partition reactivated /<br/>kill-switch lifted (resync)
    Active --> [*]: template removed
    Inactive --> [*]: template removed

    note right of Unknown
        Rollup over partitions (GetCommandTemplateState):
        all Active -> Active
        all Inactive -> Inactive
        any Unknown/Pending -> Pending
        else -> Unknown
    end note
```

Expirace alokace je **čistě datová** (`StartDate/EndDate` vs `UtcNow`), žádný job:
degraduje partition states na `Inactive` při syncu, a promítá se do API i při čtení.
HEAppE `Aggregation`/`Accounting` mají `ValidityFrom/To` — platnost (vyhodnocená
`IsValid()` při čtení), ne stav.

## Cluster/partition/aggregation/accounting — lifecycle

Žádný stavový automat: `created → (aktivní) → IsDeleted (soft-delete)`.
Aggregation a Accounting navíc s časovým oknem platnosti. Cluster je source of
truth — víc HEAppE instancí pro jeden cluster musí být konfigurováno identicky
(HEAppE management umí instanci duplikovat).