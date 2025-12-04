using ClaudeCodeWrapper.Models;

namespace ClaudeCodeWrapper;

/// <summary>
/// Real-time activity from Claude Code agent.
/// </summary>
public record Activity
{
    /// <summary>
    /// Activity type: "tool_call", "tool_result", "thought"
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Tool name (for tool_call): "Bash", "Read", "Edit", "Write", "Glob", "Grep", etc.
    /// </summary>
    public string? Tool { get; init; }

    /// <summary>
    /// Tool input/arguments summary.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Content/output of the activity.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Success status (for tool_result).
    /// </summary>
    public bool? Success { get; init; }

    /// <summary>
    /// Timestamp of the activity.
    /// </summary>
    public string Timestamp { get; init; } = "";

    /// <summary>
    /// Session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Agent ID for sub-agent activities (parallel agents).
    /// Null for main session.
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// True if this activity is from a sub-agent (parallel worker).
    /// </summary>
    public bool IsSubAgent { get; init; }

    /// <summary>
    /// Human-readable summary of the activity.
    /// </summary>
    public string Summary => Type switch
    {
        "tool_call" => $"{Tool}: {Truncate(Input, 100)}",
        "tool_result" => Success == true ? "OK" : $"Error: {Truncate(Content, 100)}",
        "thought" => Truncate(Content, 100) ?? "",
        _ => Type
    };

    private static string? Truncate(string? s, int max) =>
        s == null ? null : s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Create from internal SessionActivity.
    /// </summary>
    internal static Activity From(SessionActivity sa) => new()
    {
        Type = sa.Type,
        Tool = sa.ToolName,
        Input = sa.ToolInput,
        Content = sa.Content,
        Success = sa.Success,
        Timestamp = sa.Timestamp,
        SessionId = sa.SessionId,
        AgentId = sa.AgentId,
        IsSubAgent = sa.IsSidechain
    };
}
