using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Blue/green traffic switch endpoint. Destinations are named with a slot suffix
/// (for example <c>mes-blue-1</c> and <c>mes-green-1</c>); only the requested slot
/// remains active after the atomic cluster update.
/// </summary>
[ApiController]
[Route("api/operations/blue-green")]
public sealed class BlueGreenDeploymentController : ControllerBase
{
    private readonly DynamicYarpConfigService _config;

    public BlueGreenDeploymentController(DynamicYarpConfigService config) => _config = config;

    [HttpPost("{clusterId}/switch")]
    public async Task<IActionResult> Switch(string clusterId, [FromBody] BlueGreenSwitchRequest request)
    {
        if (!string.Equals(request.TargetSlot, "blue", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.TargetSlot, "green", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { code = 400, message = "TargetSlot must be blue or green" });

        var cluster = _config.GetCluster(clusterId);
        if (cluster == null)
            return NotFound(new { code = 404, message = $"Cluster '{clusterId}' not found" });

        var source = request.Destinations ?? cluster.Destinations?.ToDictionary(x => x.Key, x => x.Value.Address)
            ?? new Dictionary<string, string>();
        var selected = source
            .Where(d => d.Key.Contains(request.TargetSlot, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(d => d.Key, d => d.Value, StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
            return BadRequest(new { code = 400, message = $"No destinations found for slot '{request.TargetSlot}'" });

        var result = await _config.TryUpdateCluster(clusterId, new UpdateClusterRequest
        {
            Destinations = selected,
            LoadBalancingPolicy = cluster.LoadBalancingPolicy
        });
        return result.Success
            ? Ok(new { code = 200, data = new { clusterId, activeSlot = request.TargetSlot, destinations = selected } })
            : BadRequest(new { code = 400, message = result.Message });
    }
}

public sealed class BlueGreenSwitchRequest
{
    public string TargetSlot { get; set; } = string.Empty;
    public Dictionary<string, string>? Destinations { get; set; }
}
