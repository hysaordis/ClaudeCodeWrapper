namespace ClaudeCodeWrapper.Models;

/// <summary>
/// Represents an activity detected from Claude's session log.
/// Emitted in real-time as the agent executes tools and generates output.
/// </summary>
public record SessionActivity
{
    /// <summary>
    /// Activity type: "tool_call", "tool_result", or "thought"
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Tool name for tool_call activities (e.g., "Bash", "Read", "Edit", "Glob", "Grep")
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Tool input summary for tool_call activities (command, file path, pattern, etc.)
    /// </summary>
    public string? ToolInput { get; init; }

    /// <summary>
    /// Content for thought and tool_result activities
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Success status for tool_result activities (null for other types)
    /// </summary>
    public bool? Success { get; init; }

    /// <summary>
    /// ISO timestamp from Claude's session log
    /// </summary>
    public string Timestamp { get; init; } = "";

    /// <summary>
    /// Session ID this activity belongs to (if known)
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Unique identifier for the message containing this activity.
    /// Used for building conversation trees.
    /// </summary>
    public string? Uuid { get; init; }

    /// <summary>
    /// UUID of the parent message for building conversation chains.
    /// Null for root messages.
    /// </summary>
    public string? ParentUuid { get; init; }

    /// <summary>
    /// Tool use ID for correlating tool calls with their results.
    /// </summary>
    public string? ToolUseId { get; init; }

    /// <summary>
    /// Model that generated this activity (e.g., "claude-sonnet-4-20250514")
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Agent ID for subagent activities (from agent-{agentId}.jsonl files).
    /// Null for main session activities.
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// Whether this activity is from a sidechain (subagent).
    /// True for activities from agent-*.jsonl files.
    /// </summary>
    public bool IsSidechain { get; init; }

    /// <summary>
    /// Creates a tool_call activity
    /// </summary>
    public static SessionActivity ToolCall(
        string toolName,
        string? toolInput,
        string timestamp,
        string? sessionId = null,
        string? uuid = null,
        string? parentUuid = null,
        string? toolUseId = null,
        string? model = null,
        string? agentId = null,
        bool isSidechain = false) => new()
    {
        Type = "tool_call",
        ToolName = toolName,
        ToolInput = toolInput,
        Timestamp = timestamp,
        SessionId = sessionId,
        Uuid = uuid,
        ParentUuid = parentUuid,
        ToolUseId = toolUseId,
        Model = model,
        AgentId = agentId,
        IsSidechain = isSidechain
    };

    /// <summary>
    /// Creates a tool_result activity
    /// </summary>
    public static SessionActivity ToolResult(
        string? content,
        bool success,
        string timestamp,
        string? sessionId = null,
        string? uuid = null,
        string? parentUuid = null,
        string? toolUseId = null,
        string? agentId = null,
        bool isSidechain = false) => new()
    {
        Type = "tool_result",
        Content = content,
        Success = success,
        Timestamp = timestamp,
        SessionId = sessionId,
        Uuid = uuid,
        ParentUuid = parentUuid,
        ToolUseId = toolUseId,
        AgentId = agentId,
        IsSidechain = isSidechain
    };

    /// <summary>
    /// Creates a thought activity
    /// </summary>
    public static SessionActivity Thought(
        string content,
        string timestamp,
        string? sessionId = null,
        string? uuid = null,
        string? parentUuid = null,
        string? model = null,
        string? agentId = null,
        bool isSidechain = false) => new()
    {
        Type = "thought",
        Content = content,
        Timestamp = timestamp,
        SessionId = sessionId,
        Uuid = uuid,
        ParentUuid = parentUuid,
        Model = model,
        AgentId = agentId,
        IsSidechain = isSidechain
    };
}
