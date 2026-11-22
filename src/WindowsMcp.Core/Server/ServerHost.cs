using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using WindowsMcp.Core.Files;
using WindowsMcp.Core.Screen;
using WindowsMcp.Core.Shell;

namespace WindowsMcp.Core.Server;

public sealed record ServerHostSettings(
    IPAddress Address,
    int Port,
    string Token,
    RequestLog Log,
    FileToolsOptions FileOptions,
    Func<IProcessContainment> Containment,
    IDetachedLauncher Launcher,
    IDesktop Desktop);

public static class ServerHost
{
    public static WebApplication Build(ServerHostSettings settings)
    {
        if (!IPAddress.IsLoopback(settings.Address))
            throw new ArgumentException($"The server only listens on loopback, not {settings.Address}.", nameof(settings));

        // No args, no appsettings.json from the working directory, and no environment variables: nothing
        // outside this method may add a Kestrel endpoint (Kestrel:Endpoints) or otherwise reconfigure the host.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.Sources.Clear();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k => k.Listen(settings.Address, settings.Port));

        builder.Services.AddSingleton(settings.Log);
        builder.Services.AddSingleton(settings.FileOptions);
        builder.Services.AddSingleton(settings.Desktop);
        builder.Services.AddSingleton(settings.Launcher);
        builder.Services.AddSingleton<IProcessRunner>(new ProcessRunner(settings.Containment));
        builder.Services.AddHostedService<LoopbackGuardService>();

        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<FileTools>()
            .WithTools<ShellTools>()
            .WithTools<ScreenTools>()
            .WithRequestFilters(f => f.AddCallToolFilter(next => async (context, ct) =>
            {
                try
                {
                    return await next(context, ct);
                }
                catch (McpException ex) when (ex is not McpProtocolException)
                {
                    // The SDK would prefix "An error occurred invoking '<tool>': "; the agent gets the
                    // ToolGuard message as is. ToolGuard has already written it to the request log.
                    return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ex.Message }] };
                }
            }));

        var app = builder.Build();
        app.UseBearerAuth(settings.Token, settings.Log);
        app.MapMcp("/mcp");
        return app;
    }
}
