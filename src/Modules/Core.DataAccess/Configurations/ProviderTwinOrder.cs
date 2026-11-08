using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Configurations;

internal static class ProviderTwinOrder
{
    public static IOrderedQueryable<Domain.Provider.Provider> OrderByTwinResolution(this IQueryable<Domain.Provider.Provider> providers) =>
        providers.OrderBy(p => p.IsDeleted).ThenByTwinKeys();

    public static IOrderedQueryable<Domain.Provider.Provider> ThenByTwinResolution(this IOrderedQueryable<Domain.Provider.Provider> providers) =>
        providers.ThenBy(p => p.IsDeleted).ThenByTwinKeys();

    private static IOrderedQueryable<Domain.Provider.Provider> ThenByTwinKeys(this IOrderedQueryable<Domain.Provider.Provider> providers) =>
        providers
            .ThenByDescending(p => EF.Property<DateTimeOffset?>(p, "ModifiedAt"))
            .ThenByDescending(p => EF.Property<DateTime>(p, "CreatedAt"))
            .ThenBy(p => EF.Property<ProviderId>(p, "Id"));
}