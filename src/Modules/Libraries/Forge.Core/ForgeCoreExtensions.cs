using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission;
using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;
using Forge.Core.Features.UserOrgRoles;
using Forge.Core.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.Persistance;

namespace Forge.Core;
#pragma warning disable CA1034 // Nested types should not be visible
#pragma warning disable CA1708 // Identifiers should differ by more than case

public static class ForgeCoreExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Registers complete Forge Core infrastructure including persistence, all services and use cases.
        /// </summary>
        public void AddForgeCore()
        {
            ArgumentNullException.ThrowIfNull(builder);

            RegisterPersistence(builder);

            builder.Services.AddScoped<RoleService>();
            builder.Services.AddScoped<IRoleService>(sp => sp.GetRequiredService<RoleService>());
            builder.Services.AddScoped<IRoleListService>(sp => sp.GetRequiredService<RoleService>());
            builder.Services.AddScoped<IEndpointPermissionService, EndpointPermissionService>();
            builder.Services.AddScoped<IPermissionService, PermissionService>();
            builder.Services.AddScoped<IUserOrgRolesService, UserOrgRolesService>();
            builder.Services.AddScoped<IProviderIdentityResolver, ProviderIdentityResolver>();

            builder.Services.AddScoped<IForgeImportExport, PermissionImportExport>();
            builder.Services.AddScoped<IForgeImportExport, Features.Role.RoleImportExport>();
            builder.Services.AddScoped<IForgeImportExport, Features.EndpointMap.EndpointMappingImportExport>();
        }

        /// <summary>
        /// Registers minimal infrastructure for read-only role queries.
        /// Only includes ForgeDbContext and IRoleListService.
        /// </summary>
        public void AddForgeRoleQueries()
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.RegisterForgeDbContext();

            builder.Services.AddScoped<RoleService>();
            builder.Services.AddScoped<IRoleListService>(sp => sp.GetRequiredService<RoleService>());
        }

        private void RegisterPersistence()
        {
            RegisterForgeDbContext(builder);
            RegisterUserOrgDbContext(builder);
        }

        private void RegisterForgeDbContext()
        {
            var historyTableName = $"__EFMigrationsHistory";

            var connectionString = builder.Configuration.GetConnectionString("ForgeDBContext");
            builder.Services.AddPooledDbContextFactory<ForgeDbContext>(opt =>
            {
                opt.ConfigureSqlCommandLogging(builder.Environment.IsDevelopment());
                opt.UseSqlServer(connectionString, sqlOpt =>
                {
                    sqlOpt.MigrationsAssembly(typeof(ForgeDbContext).Assembly);
                    sqlOpt.MigrationsHistoryTable(historyTableName, ForgeDbContext.SchemaName);
                });
            });

            builder.EnrichSqlServerDbContext<ForgeDbContext>(settings =>
            {
                settings.DisableTracing = true;
                settings.DisableHealthChecks = true;
            });

            builder.Services.AddHostedService<ForgeMigrationStartupService>();
        }

        private void RegisterUserOrgDbContext()
        {
            var connectionString = builder.Configuration.GetConnectionString("ForgeDBContext");
            builder.Services.AddPooledDbContextFactory<UserOrgDbContext>(opt =>
            {
                opt.ConfigureSqlCommandLogging(builder.Environment.IsDevelopment());
                opt.UseSqlServer(connectionString);
            });

            builder.EnrichSqlServerDbContext<UserOrgDbContext>(settings =>
            {
                settings.DisableTracing = true;
                settings.DisableHealthChecks = true;
            });

            builder.Services.AddHostedService<UserOrgMigrationStartupService>();
        }
    }
}