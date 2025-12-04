namespace ClaudeCodeWrapper;

/// <summary>
/// Response from Claude Code with metrics.
/// </summary>
public class Response
{
    /// <summary>
    /// Response content/text.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Session ID (for resuming conversation).
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Model used.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Token usage.
    /// </summary>
    public TokenUsage? Tokens { get; set; }
}

/// <summary>
/// Token usage metrics.
/// </summary>
public class TokenUsage
{
    /// <summary>
    /// Input tokens (prompt).
    /// </summary>
    public int Input { get; set; }

    /// <summary>
    /// Output tokens (response).
    /// </summary>
    public int Output { get; set; }

    /// <summary>
    /// Cache creation tokens.
    /// </summary>
    public int CacheCreation { get; set; }

    /// <summary>
    /// Cache read tokens.
    /// </summary>
    public int CacheRead { get; set; }

    /// <summary>
    /// Total tokens.
    /// </summary>
    public int Total => Input + Output + CacheCreation;
}
