namespace ClaudeCodeWrapper;

/// <summary>
/// Base exception for Claude Code errors.
/// </summary>
public class ClaudeCodeException : Exception
{
    public ClaudeCodeException(string message) : base(message) { }
    public ClaudeCodeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when Claude Code CLI is not installed.
/// </summary>
public class ClaudeNotInstalledException : ClaudeCodeException
{
    public ClaudeNotInstalledException(string message) : base(message) { }
}
