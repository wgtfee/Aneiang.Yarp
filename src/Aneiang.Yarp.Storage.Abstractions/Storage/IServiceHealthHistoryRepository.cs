namespace Aneiang.Yarp.Storage;

public interface IServiceHealthHistoryRepository
{
    Task SaveAsync(ServiceHealthHistoryEntity entry, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceHealthHistoryEntity>> ListAsync(string? clusterId = null, string? destinationId = null, int limit = 100, CancellationToken ct = default);
    Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
