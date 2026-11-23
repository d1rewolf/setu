using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Shell;

namespace Setu.Core.Server;

/// <summary>
/// Which shell tools the host offers (powershell and cmd, or the zsh <c>shell</c> tool), and with them the
/// platform's wording of start_process and send_keys.
/// </summary>
public enum ShellFlavor { Windows, Zsh }

/// <param name="Zsh">Used only when <paramref name="Shell"/> is <see cref="ShellFlavor.Zsh"/>; null means the
/// default <see cref="ZshSettings"/>.</param>
public sealed record ServerHostSettings(
    IPAddress Address,
    int Port,
    string Token,
    RequestLog Log,
    FileToolsOptions FileOptions,
    Func<IProcessContainment> Containment,
    IDetachedLauncher Launcher,
    IDesktop Desktop,
    KeyVocabulary Keys,
    ShellFlavor Shell,
    ZshSettings? Zsh);

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
        builder.Services.AddSingleton(settings.Keys);
        builder.Services.AddSingleton(settings.Launcher);
        builder.Services.AddSingleton<IProcessRunner>(new ProcessRunner(settings.Containment));
        builder.Services.AddHostedService<LoopbackGuardService>();

        var mcp = builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<FileTools>();
        switch (settings.Shell)
        {
            case ShellFlavor.Windows:
                mcp.WithTools<WindowsShellTools>()
                    .WithTools<WindowsProcessTools>()
                    .WithTools([ScreenTools.SendKeysTool(ScreenTools.WindowsSendKeysDescription)]);
                break;
            case ShellFlavor.Zsh:
                builder.Services.AddSingleton(settings.Zsh ?? new ZshSettings());
                mcp.WithTools<ZshShellTools>()
                    .WithTools<MacProcessTools>()
                    .WithTools([ScreenTools.SendKeysTool(ScreenTools.MacSendKeysDescription)]);
                break;
            default:
                throw new ArgumentException($"Unknown shell flavour {settings.Shell}.", nameof(settings));
        }
        mcp.WithTools<ScreenTools>()
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
