using System.Text.Json;

namespace Setu.Core.Server;

public sealed record ServerOptions(string DataDirectory, int Port)
{
    public const int DefaultPort = 47631;

    /// <summary>Reads <c>config.json</c> in the data directory if present; otherwise uses the default port.</summary>
    public static ServerOptions Load(string dataDirectory)
    {
        var file = Path.Combine(dataDirectory, "config.json");
        if (!File.Exists(file)) return new ServerOptions(dataDirectory, DefaultPort);

        int port;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("port", out var p))
                return new ServerOptions(dataDirectory, DefaultPort);
            if (p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out port))
                throw new InvalidDataException($"Invalid config in {file}: \"port\" must be a whole number from 1 to 65535.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid config in {file}: {ex.Message}", ex);
        }

        if (port is < 1 or > 65535)
            throw new InvalidDataException($"Invalid config in {file}: \"port\" must be a whole number from 1 to 65535, not {port}.");
        return new ServerOptions(dataDirectory, port);
    }
}
