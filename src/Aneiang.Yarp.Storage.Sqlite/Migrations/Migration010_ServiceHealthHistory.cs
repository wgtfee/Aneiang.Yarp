using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

internal sealed class Migration010_ServiceHealthHistory : ISchemaMigration
{
    public int Version => 10;
    public string Id => "010_service_health_history";
    public string Description => "Create service health transition history table";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS service_health_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                cluster_id TEXT NOT NULL,
                destination_id TEXT NOT NULL,
                service_role TEXT NOT NULL,
                status TEXT NOT NULL,
                active_status TEXT,
                passive_status TEXT,
                reason TEXT,
                observed_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_service_health_history_lookup
                ON service_health_history(cluster_id, destination_id, observed_at DESC);
            """, ct);
}
