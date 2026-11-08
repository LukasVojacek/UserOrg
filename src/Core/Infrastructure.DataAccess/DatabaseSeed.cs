using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Infrastructure.DataAccess.Configurations;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Infrastructure.DataAccess;

public sealed class DatabaseSeedService
{
    private readonly DatabaseContext _context;
    private readonly DataAccessOptions _options;

    public DatabaseSeedService(DatabaseContext context, IOptions<DataAccessOptions> optionsConfiguration)
    {
        _context = context;
        ArgumentNullException.ThrowIfNull(optionsConfiguration);
        _options = optionsConfiguration.Value;
    }

    /// <summary>
    /// SeedDatabase
    /// </summary>
    public void SeedDatabase()
    {
        ArgumentNullException.ThrowIfNull(_options);
        ArgumentException.ThrowIfNullOrEmpty(_options.UserOrgDataSeedPath);

        using var sr = new StreamReader(_options.UserOrgDataSeedPath);

        var json = sr.ReadToEnd();
        var data = JsonSerializer.Deserialize<UserOrgData>(json);
        try
        {
            _context.Database.BeginTransaction();

            UpdateOrSkip<ProjectDomainType, int>(data.ProjectDomainTypes);
            UpdateOrSkip<LocationType, int>(data.LocationTypes);
            UpdateOrSkip<AllocationUnitType, int>(data.AllocationUnitTypes);
            UpdateOrSkip<Country, int>(data.Countries);
        }
        finally
        {
            _context.Database.CommitTransaction();
        }


        void UpdateOrSkip<TT, KT>(IList<TT> data) where KT : struct, IEquatable<KT> where TT : class, IEntity<KT>
        {
            ArgumentNullException.ThrowIfNull(data);
            var dataIds = data.Select(x => x.Id).ToList();
            var dbEntities = _context.Set<TT>()
               .Where(x => dataIds.Contains(x.Id))
               .ToList();
            var toUpdate = data.Where(x => !dbEntities.Exists(y => y.Id.Equals(x.Id)))
                .ToList();
            var toUpdateCnt = toUpdate.Count;
            if (toUpdateCnt == 0)
            {
                return;
            }

            var type = typeof(TT);
            var sqlCommandOn = $"SET IDENTITY_INSERT {type.Name} ON";
            var sqlCommandOff = $"SET IDENTITY_INSERT {type.Name} OFF";

            _context.Database.ExecuteSqlRaw(sqlCommandOn);

            _context.Set<TT>().AddRange(toUpdate);
            var changes = _context.SaveChanges();
            _context.Database.ExecuteSqlRaw(sqlCommandOff);
            if (changes != toUpdateCnt)
            {
                throw new SeedFailedException();
            }
        }
    }
}