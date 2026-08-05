using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

public sealed class SqliteServiceHealthHistoryRepository : IServiceHealthHistoryRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteServiceHealthHistoryRepository(SqliteConnectionFactory connections) => _connections = connections;

    public async Task SaveAsync(ServiceHealthHistoryEntity entry, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO service_health_history
                (cluster_id, destination_id, service_role, status, active_status, passive_status, reason, observed_at)
            VALUES (@cluster, @destination, @role, @status, @active, @passive, @reason, @observed)
            """;
        cmd.Parameters.AddWithValue("@cluster", entry.ClusterId);
        cmd.Parameters.AddWithValue("@destination", entry.DestinationId);
        cmd.Parameters.AddWithValue("@role", entry.ServiceRole);
        cmd.Parameters.AddWithValue("@status", entry.Status);
        cmd.Parameters.AddWithValue("@active", (object?)entry.ActiveStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@passive", (object?)entry.PassiveStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reason", (object?)entry.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@observed", entry.ObservedAt.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ServiceHealthHistoryEntity>> ListAsync(string? clusterId = null, string? destinationId = null, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, cluster_id, destination_id, service_role, status, active_status, passive_status, reason, observed_at
            FROM service_health_history
            WHERE (@cluster IS NULL OR cluster_id = @cluster)
              AND (@destination IS NULL OR destination_id = @destination)
            ORDER BY observed_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@cluster", (object?)clusterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@destination", (object?)destinationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));
        var result = new List<ServiceHealthHistoryEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ServiceHealthHistoryEntity
            {
                Id = reader.GetInt64(0),
                ClusterId = reader.GetString(1),
                DestinationId = reader.GetString(2),
                ServiceRole = reader.GetString(3),
                Status = reader.GetString(4),
                ActiveStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
                PassiveStatus = reader.IsDBNull(6) ? null : reader.GetString(6),
                Reason = reader.IsDBNull(7) ? null : reader.GetString(7),
                ObservedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        return result.AsReadOnly();
    }

    public async Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM service_health_history WHERE observed_at < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
