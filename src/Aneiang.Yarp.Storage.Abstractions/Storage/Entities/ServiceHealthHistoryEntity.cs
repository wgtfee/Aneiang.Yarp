namespace Aneiang.Yarp.Storage;

/// <summary>Persisted health state transition for a YARP destination.</summary>
public sealed class ServiceHealthHistoryEntity
{
    public long Id { get; set; }
    public string ClusterId { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public string ServiceRole { get; set; } = "backend";
    public string Status { get; set; } = "Unknown";
    public string? ActiveStatus { get; set; }
    public string? PassiveStatus { get; set; }
    public string? Reason { get; set; }
    public DateTime ObservedAt { get; set; } = DateTime.UtcNow;
}
