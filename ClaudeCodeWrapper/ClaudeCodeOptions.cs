namespace ClaudeCodeWrapper;

/// <summary>
/// Options for ClaudeCode client.
/// </summary>
public class ClaudeCodeOptions
{
    /// <summary>
    /// Path to Claude CLI. Auto-detected if null.
    /// </summary>
    public string? ClaudePath { get; set; }

    /// <summary>
    /// Model to use (e.g., "sonnet", "opus", "haiku").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Custom system prompt.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Append to default system prompt.
    /// </summary>
    public string? AppendSystemPrompt { get; set; }

    /// <summary>
    /// Working directory for Claude operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Permission mode for Claude operations.
    /// Use PermissionMode.All to get all available modes.
    /// </summary>
    public PermissionMode? PermissionMode { get; set; }

    /// <summary>
    /// Maximum number of agent turns.
    /// </summary>
    public int MaxTurns { get; set; }

    /// <summary>
    /// Tools allowed without permission.
    /// </summary>
    public List<string> AllowedTools { get; set; } = new();

    /// <summary>
    /// Tools that are blocked.
    /// </summary>
    public List<string> DisallowedTools { get; set; } = new();

    /// <summary>
    /// Environment variables to pass to Claude.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Output format (internal use).
    /// </summary>
    internal OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    /// <summary>
    /// Session ID to resume (internal use).
    /// </summary>
    internal string? ResumeSessionId { get; set; }

    /// <summary>
    /// Continue last session (internal use).
    /// </summary>
    internal bool ContinueLastSession { get; set; }

    #region Usage Monitoring Options

    /// <summary>
    /// Enable usage monitoring via the Claude API.
    /// When enabled, you can call GetUsageAsync() to check current usage limits.
    /// Default: false.
    /// </summary>
    public bool EnableUsageMonitoring { get; set; } = false;

    /// <summary>
    /// How long to cache usage data before refetching from API.
    /// Default: 1 minute.
    /// </summary>
    public TimeSpan UsageCacheExpiry { get; set; } = TimeSpan.FromMinutes(1);

    #endregion
}

/// <summary>
/// Output format for Claude responses.
/// </summary>
public enum OutputFormat
{
    /// <summary>Plain text output.</summary>
    Text,
    /// <summary>JSON output with metadata.</summary>
    Json,
    /// <summary>Streaming JSON output.</summary>
    StreamJson
}

/// <summary>
/// Permission mode for Claude Code operations.
/// Smart enum with metadata and helper methods.
/// </summary>
public sealed class PermissionMode
{
    /// <summary>
    /// Standard permission behavior - allows reads, asks permission for other operations.
    /// </summary>
    public static readonly PermissionMode Default = new("default", "Default", "Standard permission behavior - allows reads, asks permission for other operations");

    /// <summary>
    /// Planning mode - can analyze and read but not modify files or execute commands.
    /// </summary>
    public static readonly PermissionMode Plan = new("plan", "Plan", "Planning mode - can analyze and read but not modify files or execute commands");

    /// <summary>
    /// Auto-accept file edits - bypasses permission prompts for file modifications.
    /// </summary>
    public static readonly PermissionMode AcceptEdits = new("acceptEdits", "Accept Edits", "Auto-accept file edits - bypasses permission prompts for file modifications");

    /// <summary>
    /// Bypass all permission checks - no permission prompts (use with caution).
    /// </summary>
    public static readonly PermissionMode BypassPermissions = new("bypassPermissions", "Bypass Permissions", "Bypass all permission checks - no permission prompts (use with caution)");

    /// <summary>
    /// CLI value used in --permission-mode argument.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Description of this permission mode.
    /// </summary>
    public string Description { get; }

    private PermissionMode(string value, string displayName, string description)
    {
        Value = value;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>
    /// Get all available permission modes.
    /// </summary>
    public static IReadOnlyList<PermissionMode> All { get; } = new[]
    {
        Default,
        Plan,
        AcceptEdits,
        BypassPermissions
    };

    /// <summary>
    /// Try to parse a permission mode from its CLI value.
    /// </summary>
    public static bool TryParse(string value, out PermissionMode? mode)
    {
        mode = All.FirstOrDefault(m => m.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        return mode != null;
    }

    /// <summary>
    /// Parse a permission mode from its CLI value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when value is not a valid permission mode.</exception>
    public static PermissionMode Parse(string value)
    {
        if (TryParse(value, out var mode) && mode != null)
            return mode;

        var validValues = string.Join(", ", All.Select(m => m.Value));
        throw new ArgumentException($"Invalid permission mode: '{value}'. Valid values: {validValues}", nameof(value));
    }

    /// <summary>
    /// Returns the CLI value.
    /// </summary>
    public override string ToString() => Value;

    /// <summary>
    /// Implicit conversion to string (returns CLI value).
    /// </summary>
    public static implicit operator string(PermissionMode mode) => mode.Value;
}
