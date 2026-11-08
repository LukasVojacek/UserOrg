## Database operations 

### Dotnet core - Create new database migration:
```bash
# from folder /userorg-v2
cd src/Modules/Integrations/Heappe
$env:ConnectionStrings_HeappeIntegrationDbContext='<CONNECTION_STRING>'
dotnet ef migrations add <MIGRATION_NAME> -c HeappeIntegrationDbContext -o Persistence/Migrations  --msbuildprojectextensionspath ../../../../artifacts/obj/Heappe
```

### Dotnet core - Remove migration:
```bash
# from folder /userorg-v2
cd src/Modules/Integrations/Heappe
$env:ConnectionStrings_HeappeIntegrationDbContext='<CONNECTION_STRING>'
dotnet ef migrations remove -c HeappeIntegrationDbContext
```

### Dotnet core - Update database:
```bash
# from folder /userorg-v2
cd src/Modules/Integrations/Heappe
$env:ConnectionStrings_HeappeIntegrationDbContext='<CONNECTION_STRING>'
dotnet ef database update -c HeappeIntegrationDbContext
```

### Dotnet core - Remove database:
```bash
# from folder /userorg-v2
cd src/Modules/Integrations/Heappe
$env:ConnectionStrings_HeappeIntegrationDbContext='<CONNECTION_STRING>'
dotnet ef database drop -c HeappeIntegrationDbContext
```

### SQL migration script
```bash
# from folder /userorg-v2
cd src/Modules/Integrations/Heappe
$env:ConnectionStrings_HeappeIntegrationDbContext='<CONNECTION_STRING>'
dotnet ef migrations script -c HeappeIntegrationDbContext
```