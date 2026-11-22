namespace WindowsMcp.Core;

/// <summary>Thrown by tools for bad arguments; ToolGuard turns it into an MCP tool error.</summary>
public sealed class ToolInputException(string message) : Exception(message);
