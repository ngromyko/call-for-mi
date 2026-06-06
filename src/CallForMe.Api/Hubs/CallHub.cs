using Microsoft.AspNetCore.SignalR;

namespace CallForMe.Api.Hubs;

public sealed class CallHub : Hub
{
    public Task SubscribeCall(Guid callId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(callId));

    public Task UnsubscribeCall(Guid callId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(callId));

    public static string GroupName(Guid callId) => $"call:{callId:N}";
}
