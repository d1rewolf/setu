using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace WindowsMcp.Core.Server;

public static class BearerAuth
{
    private const string Prefix = "Bearer ";

    /// <summary>Rejects with 401 every request that lacks <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public static IApplicationBuilder UseBearerAuth(this IApplicationBuilder app, string token, RequestLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var expected = Encoding.UTF8.GetBytes(token);
        return app.Use(async (context, next) =>
        {
            if (IsAuthorized(context.Request.Headers.Authorization.ToString(), expected))
            {
                await next(context);
                return;
            }
            log.Write($"401 {Sanitize(context.Request.Method)} {Sanitize(context.Request.Path.ToUriComponent())}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        });
    }

    private const int MaxLoggedLength = 200;

    /// <summary>Keeps client-controlled text to one short log line.</summary>
    private static string Sanitize(string value)
    {
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length <= MaxLoggedLength ? clean : clean[..MaxLoggedLength] + "...";
    }

    private static bool IsAuthorized(string header, byte[] expected)
    {
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var presented = Encoding.UTF8.GetBytes(header[Prefix.Length..]);
        // FixedTimeEquals returns false on a length mismatch without comparing contents.
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
