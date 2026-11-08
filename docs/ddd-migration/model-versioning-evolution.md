# Model Versioning Strategy - Incremental Evolution

**Document Version:** 1.0  
**Created:** 2026-04-24  
**Purpose:** Handle model changes without creating new API endpoint versions

---

## 📋 Overview

When the domain model evolves (adding/modifying parameters), we don't create new endpoint versions (`/api/v3/...`). Instead, we:

1. **Create evolved model** - New DTO with additional fields
2. **Mark old model as deprecated** - Add `[Obsolete]` attribute
3. **Keep same endpoint** - Respond with evolved model, accept both models
4. **Ensure backward compatibility** - Old model still works

### When to Use

```
✅ Adding optional field to response:
   ProjectV2Dto (v1)
   ProjectV2Dto (v1.1) - with new optional field
   
✅ Adding optional parameter to request:
   CreateProjectV2Dto (v1)
   CreateProjectV2Dto (v1.1) - with new optional field
   
❌ Removing required field (breaking change):
   Use /api/v3/ endpoint instead
   
❌ Changing meaning of field:
   Use /api/v3/ endpoint instead
```

---

## 🎯 Example: Adding New Field to Project Model

### Scenario: Adding "Category" Field

**Current Model (v2.0):**
```csharp
public record ProjectV2Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags);
```

**New Requirement:** Add optional "Category" field to projects.

### Step 1: Create Evolved Model

File: `src/Core/API/Dto/V2/ProjectV2Dto.cs` (evolve in place)

```csharp
namespace UserOrg.Core.API.Dto.V2;

/// <summary>
/// V2 Project DTO - Natural ID-based (recommended)
/// v2.1: Added optional Category field
/// </summary>
public record ProjectV2Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags,
    string? Category = null)  // ← NEW optional field (v2.1)
{
    public static ProjectV2Dto Empty => new(
        string.Empty, string.Empty,
        DateTime.MinValue, null, Array.Empty<string>(), null);
}

/// <summary>
/// Legacy DTO - kept for reference, use ProjectV2Dto instead
/// </summary>
[Obsolete("Use ProjectV2Dto instead. This DTO is deprecated as of v2.1.")]
public record ProjectV2DtoV1(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags);
```

### Step 2: Update Controller (Keep Same Endpoint)

```csharp
/// <summary>
/// Get project by short name (natural identifier)
/// 
/// v2.1 Changes:
/// - Response now includes optional "category" field
/// - Both requests and responses accept category
/// </summary>
[HttpGet("{projectShortName}")]
[ProducesResponseType(typeof(ProjectV2Dto), StatusCodes.Status200OK)]
public async Task<IActionResult> GetProject(string projectShortName)
{
    try
    {
        var shortNameResult = ProjectShortName.Create(projectShortName);
        if (!shortNameResult.IsSuccess)
        {
            return BadRequest(new { error = "Invalid project identifier" });
        }

        var project = await _readContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ShortName == shortNameResult.Value);

        if (project == null)
        {
            return NotFound(new { error = "Project not found" });
        }

        var dto = _mapper.Map<ProjectV2Dto>(project);
        return Ok(dto);  // ← Returns evolved dto with Category
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error retrieving project");
        return StatusCode(500, new { error = "Internal server error" });
    }
}
```

### Step 3: Update AutoMapper (Handle Both)

```csharp
public class ProjectMappingProfile : Profile
{
    public ProjectMappingProfile()
    {
        // V2 Mapping - evolved to include Category
        CreateMap<Project, ProjectV2Dto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.ShortName, 
                opt => opt.MapFrom(src => src.ShortName.Value))
            .ForMember(dest => dest.StartDate, 
                opt => opt.MapFrom(src => src.StartDate))
            .ForMember(dest => dest.EndDate, 
                opt => opt.MapFrom(src => src.EndDate))
            .ForMember(dest => dest.Tags,
                opt => opt.MapFrom(src => 
                    src.ProjectTags.Select(t => t.Name).ToList()))
            .ForMember(dest => dest.Category,  // ← NEW
                opt => opt.MapFrom(src => src.Category));

        // Legacy mapping (for backward compat if needed)
        CreateMap<Project, ProjectV2DtoV1>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.ShortName,
                opt => opt.MapFrom(src => src.ShortName.Value))
            .ForMember(dest => dest.Tags,
                opt => opt.MapFrom(src => 
                    src.ProjectTags.Select(t => t.Name).ToList()));
    }
}
```

### Step 4: Update Request DTO (If Adding Input Parameter)

```csharp
/// <summary>
/// Create or Update Project DTO (V2)
/// v2.1: Added optional Category field
/// </summary>
public record CreateUpdateProjectV2Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    string? Category = null);  // ← NEW optional parameter

/// <summary>
/// Legacy DTO - kept for reference
/// </summary>
[Obsolete("Use CreateUpdateProjectV2Dto instead")]
public record CreateUpdateProjectV2DtoV1(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate);
```

### Step 5: Response Examples

**Response (Before - v2.0):**
```json
{
  "name": "LEXIS",
  "shortName": "LEX",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-12-31T23:59:59Z",
  "tags": ["important"]
}
```

**Response (After - v2.1):**
```json
{
  "name": "LEXIS",
  "shortName": "LEX",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-12-31T23:59:59Z",
  "tags": ["important"],
  "category": "Research"  // ← NEW field (optional)
}
```

**Request (Before - v2.0):**
```json
{
  "name": "LEXIS",
  "shortName": "LEX",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-12-31T23:59:59Z"
}
```

**Request (After - v2.1):**
```json
{
  "name": "LEXIS",
  "shortName": "LEX",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-12-31T23:59:59Z",
  "category": "Research"  // ← NEW optional field
}
```

---

## 🔄 Guidelines for Model Evolution

### When Adding Fields (Non-Breaking)

**✅ DO:**
- Make new fields **optional** (with default values)
- Add `[Obsolete]` to old DTO variant
- Keep endpoint URL the same
- Update documentation
- Use same endpoint version

**Example:**
```csharp
// Before
public record ProjectV2Dto(string Name, string ShortName, ...);

// After (v2.1) - field is optional
public record ProjectV2Dto(
    string Name, 
    string ShortName, 
    ...,
    string? NewField = null);  // Optional with default
```

### When Modifying Existing Fields

**If Optional → Required (Breaking Change):**
```csharp
❌ DON'T: Make it required in same DTO

✅ DO: Create new endpoint version
  /api/v2/  → ProjectV2Dto (optional field)
  /api/v3/  → ProjectV3Dto (required field)
```

**If Required → Optional (Non-Breaking):**
```csharp
✅ OK: Make optional in evolved DTO
  public record ProjectV2Dto(
      string RequiredField,
      string PreviouslyRequiredField = null);  // Now optional
```

### When Removing Fields (Always Breaking)

```csharp
❌ DON'T: Remove from existing DTO

✅ DO: Create new endpoint version
  /api/v2/  → ProjectV2Dto (has field)
  /api/v3/  → ProjectV3Dto (field removed)
```

---

## 📋 Model Evolution Checklist

### For Adding New Optional Field

- [ ] Create evolved DTO with new optional field (default value)
- [ ] Mark old DTO as `[Obsolete]`
- [ ] Keep endpoint URL the same (no /v3)
- [ ] Update controller to use evolved DTO
- [ ] Update AutoMapper profile
- [ ] Update domain entity if needed
- [ ] Update database migration (add nullable column)
- [ ] Write tests for backward compatibility
- [ ] Update OpenAPI/Swagger documentation
- [ ] Update client migration guide
- [ ] No version bump needed (same endpoint)

### For Modifying Required Fields

- [ ] Evaluate if change is truly breaking
- [ ] If breaking: Create new endpoint version (/api/v3)
- [ ] If non-breaking: Follow "adding field" checklist
- [ ] Document breaking change clearly
- [ ] Provide migration path for clients

---

## 🧪 Testing Model Evolution

### Backward Compatibility Test

```csharp
[TestClass]
public class ProjectV2ModelEvolutionTests
{
    [TestMethod]
    public async Task GetProject_V2_1_IncludesNewCategoryField()
    {
        // Arrange
        var shortName = "LEX";

        // Act
        var response = await _client.GetAsync($"/api/v2/Projects/{shortName}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsAsync<ProjectV2Dto>();
        Assert.IsNotNull(json.Category);  // New field present
    }

    [TestMethod]
    public async Task GetProject_V2_1_CategoryIsOptional()
    {
        // Arrange
        var shortName = "TEST";

        // Act
        var response = await _client.GetAsync($"/api/v2/Projects/{shortName}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsAsync<ProjectV2Dto>();
        // No error even if category is not set - null is acceptable
        Assert.IsTrue(json.Category == null || json.Category is string);
    }

    [TestMethod]
    public async Task CreateProject_WithoutCategory_Succeeds()
    {
        // Arrange - old format without category
        var request = new CreateUpdateProjectV2Dto(
            "TestProject", "TST", DateTime.Now, null);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v2/Projects", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateProject_WithCategory_Succeeds()
    {
        // Arrange - new format with category
        var request = new CreateUpdateProjectV2Dto(
            "TestProject", "TST", DateTime.Now, null, "Research");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v2/Projects", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsAsync<ProjectV2Dto>();
        Assert.AreEqual("Research", json.Category);
    }

    [TestMethod]
    public async Task DeserializeOldFormat_StillWorks()
    {
        // Arrange - simulating old client sending old format
        var oldJson = @"{
            ""name"": ""LEXIS"",
            ""shortName"": ""LEX"",
            ""startDate"": ""2024-01-01T00:00:00Z"",
            ""endDate"": ""2024-12-31T23:59:59Z"",
            ""tags"": [""important""]
        }";

        // Act
        var dto = JsonSerializer.Deserialize<ProjectV2Dto>(oldJson);

        // Assert
        Assert.IsNotNull(dto);
        Assert.AreEqual("LEX", dto.ShortName);
        Assert.IsNull(dto.Category);  // Default value
    }
}
```

---

## 📊 Model Evolution History Example

### Project Model Evolution Timeline

```
v2.0 (2026-01-15) - Initial Release
  ProjectV2Dto(Name, ShortName, StartDate, EndDate, Tags)

v2.1 (2026-02-01) - Add Category
  ProjectV2Dto(Name, ShortName, StartDate, EndDate, Tags, Category?)
  Old DTO marked @Obsolete

v2.2 (2026-03-15) - Add Description
  ProjectV2Dto(Name, ShortName, StartDate, EndDate, Tags, Category?, Description?)
  Old DTO marked @Obsolete

v2.3 (2026-04-20) - Add Owner
  ProjectV2Dto(Name, ShortName, StartDate, EndDate, Tags, Category?, Description?, Owner?)
  Old DTO marked @Obsolete

✅ All changes are NON-BREAKING (optional fields)
✅ Endpoint remains: /api/v2/Projects/{shortName}
✅ No new versions created (/api/v3, /api/v4, ...)
```

---

## 🚀 Communication Strategy

### When Publishing Model Changes

**Release Notes Example:**

```markdown
## API v2.1 Release

### New Features
- ✨ Added optional `category` field to Project model
- ✨ Clients can now categorize projects

### Changes
- Project DTO now includes optional `category` parameter
- Old format still works (field is optional)
- No breaking changes

### Migration
- No action required for existing clients
- To use category feature, update your code:

  OLD:
  ```json
  { "name": "Project", "shortName": "PRJ" }
  ```

  NEW:
  ```json
  { "name": "Project", "shortName": "PRJ", "category": "Research" }
  ```

### Deprecations
- `ProjectV2DtoV1` record marked as obsolete (kept for backward compat)
- Use `ProjectV2Dto` instead

### Backward Compatibility
- ✅ Fully backward compatible
- Old clients continue to work without changes
```

---

## 📈 Monitoring Model Evolution

### Track Field Usage

```csharp
public interface IFieldUsageMetrics
{
    void RecordFieldUsed(string dtoName, string fieldName);
    Task<FieldUsageStats> GetStats(string dtoName);
}

public record FieldUsageStats(
    string FieldName,
    int RequestsWithField,
    int RequestsWithoutField,
    double UsagePercentage);

// Usage in mapper or controller
_metrics.RecordFieldUsed("ProjectV2Dto", "Category");
```

### Prometheus Metrics

```csharp
private static readonly Counter FieldUsageTotal = Counter
    .Create("api_field_usage_total", "Total field usage",
        new CounterConfiguration { 
            LabelNames = new[] { "dto", "field", "present" } 
        });

// Track if optional field was included
FieldUsageTotal
    .WithLabels("ProjectV2Dto", "Category", project.Category != null ? "yes" : "no")
    .Inc();
```

---

## ✅ Decision Tree: When to Create New Endpoint Version

```
Is this a model change?
├─ YES: Continue
└─ NO: This isn't model versioning (e.g., bug fix, logic change)

Is the change backward compatible?
├─ YES: Model Evolution
│   └─ Add optional field(s)
│   └─ Keep same endpoint
│   └─ Mark old DTO as @Obsolete
│   └─ NO new version needed (/api/v2 stays /api/v2)
│
└─ NO: Breaking Change
    └─ Remove required field(s)
    └─ Change field meaning/type
    └─ Make optional field required
    └─ Create NEW endpoint version
    └─ Example: /api/v2 → /api/v3
```

---

## 📚 Related Documentation

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[DDD Migration Phase 6](./ddd-migration-phase6-api-versioning.md)** - API endpoint refactoring (uses this strategy)
- **[API Versioning - Gateway Routing](./api-versioning-gateway-routing.md)** - Primary endpoint versioning strategy
- **[Entity Relationship Complete Map](./entity-relationship-complete-map.md)** - Understanding entity structure

---

## 🎯 Best Practices Summary

| Practice | Reason |
|----------|--------|
| Make new fields optional | Backward compatibility |
| Always mark old DTOs @Obsolete | Clear deprecation path |
| Keep same endpoint for evolution | Reduce complexity |
| Create new version for breaking changes | Clear separation |
| Document field additions | Help clients understand changes |
| Test both old and new formats | Ensure compatibility |
| Track field usage metrics | Monitor adoption |
| Communicate changes clearly | Help migration planning |

---

**Document Version:** 1.0  
**Status:** ✅ Complete  
**Last Updated:** 2026-04-24
