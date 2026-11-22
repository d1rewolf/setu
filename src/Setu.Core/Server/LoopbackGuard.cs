using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Setu.Core.Server;

/// <summary>Fails host start if Kestrel ended up bound to anything other than a loopback address.</summary>
public static class LoopbackGuard
{
    public static void EnsureLoopback(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            // BindingAddress keeps hosts like "localhost" and "+" unresolved, so only literal loopback IPs pass.
            var host = BindingAddress.Parse(url).Host.Trim('[', ']');
            if (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip))
                throw new InvalidOperationException($"Refusing to run: the server is bound to {url}, which is not a loopback address.");
        }
    }
}

/// <summary>Runs <see cref="LoopbackGuard"/> once every hosted service, including the server, has started.</summary>
internal sealed class LoopbackGuardService(IServer server) : IHostedLifecycleService
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("Refusing to run: the server's bound addresses are unknown.");
        LoopbackGuard.EnsureLoopback(addresses);
        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
