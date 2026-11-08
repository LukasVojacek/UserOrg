namespace Core.DataAccess.Queries.Contracts;

public interface IStatisticsQuery
{
    Task<CreationTimestamps> GetCreationTimestampsAsync(CancellationToken ct);
}