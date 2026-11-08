#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Tools/DatabaseMigrations/DatabaseMigrations.csproj", "Tools/DatabaseMigrations/"]
COPY ["Core/Application/Application.csproj", "Core/Application/"]
COPY ["Core/Domain/Domain.csproj", "Core/Domain/"]
COPY ["Modules/Libraries/Forge.Core/Forge.Core.csproj", "Modules/Libraries/Forge.Core/"]
COPY ["Modules/Integrations/Heappe/Heappe.csproj", "Modules/Integrations/Heappe/"]
COPY ["UserOrg.Shared.Persistance/UserOrg.Shared.Persistance.csproj", "UserOrg.Shared.Persistance/"]
COPY ["UserOrg.Shared/UserOrg.Shared.csproj", "UserOrg.Shared/"]
RUN dotnet restore "./Tools/DatabaseMigrations/./DatabaseMigrations.csproj"
COPY . .
WORKDIR "/src/Tools/DatabaseMigrations"
RUN dotnet build "./DatabaseMigrations.csproj" -c $BUILD_CONFIGURATION /p:BuildingInsideDocker=true -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DatabaseMigrations.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BuildingInsideDocker=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DatabaseMigrations.dll"]
CMD ["help"]
