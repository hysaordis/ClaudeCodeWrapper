namespace ClaudeCodeWrapper.Models;

/// <summary>
/// Configuration options for session monitoring.
/// </summary>
public class SessionMonitorOptions
{
    /// <summary>
    /// Working directory used by Claude (for deriving the project path).
    /// This is typically the same as ClaudeMaxOptions.WorkingDirectory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Session ID to monitor directly.
    /// If specified, skips directory watching and monitors this specific session.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Base path for Claude projects directory.
    /// Default: ~/.claude/projects
    /// </summary>
    public string? ClaudeProjectsPath { get; set; }

    /// <summary>
    /// Whether to emit activities from existing log content when starting.
    /// Default: false (only emit new activities after monitoring starts)
    /// </summary>
    public bool IncludeExistingContent { get; set; } = false;

    /// <summary>
    /// Tolerance window (seconds) for detecting new session files.
    /// Files created within this window before monitoring started are included.
    /// This helps handle race conditions between starting the monitor and file creation.
    /// Default: 2 seconds
    /// </summary>
    public int NewFileToleranceSeconds { get; set; } = 2;

    /// <summary>
    /// Gets the Claude projects directory path.
    /// </summary>
    public string GetClaudeProjectsPath()
    {
        return ClaudeProjectsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    /// <summary>
    /// Derives the project directory from the working directory.
    /// Claude sanitizes paths by replacing /, \, and . with -
    /// </summary>
    public string? GetDerivedProjectPath()
    {
        if (string.IsNullOrEmpty(WorkingDirectory))
            return null;

        // Claude sanitizes paths: /Users/ordis/Project.Name/... -> -Users-ordis-Project-Name-...
        var sanitizedPath = WorkingDirectory
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(".", "-");
        return Path.Combine(GetClaudeProjectsPath(), sanitizedPath);
    }
}
