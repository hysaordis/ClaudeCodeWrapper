using ClaudeCodeWrapper.Models.Blocks;
using ClaudeCodeWrapper.Models.Records;

namespace ClaudeCodeWrapper;

/// <summary>
/// Simplified real-time activity from Claude Code agent.
/// For full details, use SessionRecord directly.
/// </summary>
public record Activity
{
    /// <summary>
    /// Activity type: "tool_call", "tool_result", "thought", "text", "user", "system", "summary"
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Tool name (for tool_call): "Bash", "Read", "Edit", "Write", "Glob", "Grep", etc.
    /// </summary>
    public string? Tool { get; init; }

    /// <summary>
    /// Tool use ID for correlating tool_call with tool_result.
    /// This is the unique identifier from ToolUseBlock.Id / ToolResultBlock.ToolUseId.
    /// </summary>
    public string? ToolUseId { get; init; }

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
    public DateTime? Timestamp { get; init; }

    /// <summary>
    /// Session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Message UUID.
    /// </summary>
    public string? Uuid { get; init; }

    /// <summary>
    /// Parent message UUID.
    /// </summary>
    public string? ParentUuid { get; init; }

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
    /// Model used (for assistant activities).
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Token usage (for assistant activities).
    /// </summary>
    public Models.TokenUsage? Usage { get; init; }

    /// <summary>
    /// The original record this activity was created from.
    /// </summary>
    public SessionRecord? OriginalRecord { get; init; }

    /// <summary>
    /// Human-readable summary of the activity.
    /// </summary>
    public string Summary => Type switch
    {
        "tool_call" => $"{Tool}: {Truncate(Input, 100)}",
        "tool_result" => Success == true ? "OK" : $"Error: {Truncate(Content, 100)}",
        "thought" or "thinking" => Truncate(Content, 100) ?? "",
        "text" => Truncate(Content, 100) ?? "",
        "user" => Truncate(Content, 80) ?? "(user input)",
        "summary" => Truncate(Content, 80) ?? "(summary)",
        "system" => Truncate(Content, 80) ?? "(system)",
        _ => Type
    };

    private static string? Truncate(string? s, int max) =>
        s == null ? null : s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Create activities from a SessionRecord.
    /// May yield multiple activities for assistant records with multiple content blocks.
    /// </summary>
    public static IEnumerable<Activity> FromRecord(SessionRecord record)
    {
        switch (record)
        {
            case AssistantRecord assistant:
                foreach (var activity in FromAssistantRecord(assistant))
                    yield return activity;
                break;

            case UserRecord user:
                foreach (var activity in FromUserRecord(user))
                    yield return activity;
                break;

            case SummaryRecord summary:
                yield return new Activity
                {
                    Type = "summary",
                    Content = summary.Summary,
                    Timestamp = summary.Timestamp,
                    SessionId = summary.SessionId,
                    Uuid = summary.Uuid,
                    OriginalRecord = record
                };
                break;

            case SystemRecord system:
                yield return new Activity
                {
                    Type = "system",
                    Content = system.Content,
                    Timestamp = system.Timestamp,
                    SessionId = system.SessionId,
                    Uuid = system.Uuid,
                    ParentUuid = system.ParentUuid,
                    AgentId = system.AgentId,
                    IsSubAgent = system.IsSidechain,
                    OriginalRecord = record
                };
                break;
        }
    }

    private static IEnumerable<Activity> FromAssistantRecord(AssistantRecord record)
    {
        foreach (var block in record.Message.Content)
        {
            switch (block)
            {
                case ToolUseBlock toolUse:
                    yield return new Activity
                    {
                        Type = "tool_call",
                        Tool = toolUse.Name,
                        ToolUseId = toolUse.Id, // ✅ Correlation key for matching with tool_result
                        Input = toolUse.Input?.GetRawText(),
                        Timestamp = record.Timestamp,
                        SessionId = record.SessionId,
                        Uuid = record.Uuid,
                        ParentUuid = record.ParentUuid,
                        AgentId = record.AgentId,
                        IsSubAgent = record.IsSidechain,
                        Model = record.Message.Model,
                        Usage = record.Message.Usage,
                        OriginalRecord = record
                    };
                    break;

                case TextBlock text:
                    yield return new Activity
                    {
                        Type = "text",
                        Content = text.Text,
                        Timestamp = record.Timestamp,
                        SessionId = record.SessionId,
                        Uuid = record.Uuid,
                        ParentUuid = record.ParentUuid,
                        AgentId = record.AgentId,
                        IsSubAgent = record.IsSidechain,
                        Model = record.Message.Model,
                        Usage = record.Message.Usage,
                        OriginalRecord = record
                    };
                    break;

                case ThinkingBlock thinking:
                    yield return new Activity
                    {
                        Type = "thinking",
                        Content = thinking.Thinking,
                        Timestamp = record.Timestamp,
                        SessionId = record.SessionId,
                        Uuid = record.Uuid,
                        ParentUuid = record.ParentUuid,
                        AgentId = record.AgentId,
                        IsSubAgent = record.IsSidechain,
                        Model = record.Message.Model,
                        OriginalRecord = record
                    };
                    break;
            }
        }
    }

    private static IEnumerable<Activity> FromUserRecord(UserRecord record)
    {
        // Plain user message
        if (record.Message.ContentString != null)
        {
            yield return new Activity
            {
                Type = "user",
                Content = record.Message.ContentString,
                Timestamp = record.Timestamp,
                SessionId = record.SessionId,
                Uuid = record.Uuid,
                ParentUuid = record.ParentUuid,
                AgentId = record.AgentId,
                IsSubAgent = record.IsSidechain,
                OriginalRecord = record
            };
            yield break;
        }

        // Tool results
        if (record.Message.ContentBlocks != null)
        {
            foreach (var block in record.Message.ContentBlocks)
            {
                if (block is ToolResultBlock toolResult)
                {
                    yield return new Activity
                    {
                        Type = "tool_result",
                        ToolUseId = toolResult.ToolUseId, // ✅ Correlation key for matching with tool_call
                        Content = toolResult.Content,
                        Success = !toolResult.IsError,
                        Timestamp = record.Timestamp,
                        SessionId = record.SessionId,
                        Uuid = record.Uuid,
                        ParentUuid = record.ParentUuid,
                        AgentId = record.AgentId,
                        IsSubAgent = record.IsSidechain,
                        OriginalRecord = record
                    };
                }
            }
        }
    }
}
