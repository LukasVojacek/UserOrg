## Database operations 

### Dotnet core - Create new database migration:
```bash
# from folder /userorg-v2
cd src/Core/Infrastructure.DataAccess
$env:ASPNETCORE_RUNTYPE_ENVIRONMENT='Migration'
dotnet ef migrations add <MIGRATION_NAME> -c DatabaseContext -o Migrations  --msbuildprojectextensionspath ../../../artifacts/obj/Infrastructure.DataAccess
```

### Dotnet core - Remove migration:
```bash
# from folder /userorg-v2
cd src/Core/Infrastructure.DataAccess
$env:ASPNETCORE_RUNTYPE_ENVIRONMENT='Migration'
dotnet ef migrations remove -c DatabaseContext
```

### Dotnet core - Update database:
```bash
# from folder /userorg-v2
cd src/Core/Infrastructure.DataAccess
$env:ASPNETCORE_RUNTYPE_ENVIRONMENT='Migration'
dotnet ef database update -c DatabaseContext
```

### Dotnet core - Remove database:
```bash
# from folder /userorg-v2
cd src/Core/Infrastructure.DataAccess
$env:ASPNETCORE_RUNTYPE_ENVIRONMENT='Migration'
dotnet ef database drop -c DatabaseContext
```

### SQL migration script
```bash
# from folder /userorg-v2
cd src/Core/Infrastructure.DataAccess
$env:ASPNETCORE_RUNTYPE_ENVIRONMENT='Migration'
dotnet ef migrations script -c DatabaseContext
```