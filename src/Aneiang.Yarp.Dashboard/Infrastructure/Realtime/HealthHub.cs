using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

/// <summary>SignalR hub for service health transitions.</summary>
public sealed class HealthHub : Hub
{
    private readonly IDashboardAuthorizationService _authorizationService;

    public HealthHub(IDashboardAuthorizationService authorizationService) => _authorizationService = authorizationService;

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null || !await _authorizationService.IsAuthorizedAsync(httpContext))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, "health");
        await base.OnConnectedAsync();
    }
}
