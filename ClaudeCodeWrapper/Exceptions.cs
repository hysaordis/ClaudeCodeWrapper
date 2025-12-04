namespace ClaudeCodeWrapper;

/// <summary>
/// Base exception for Claude Code errors.
/// </summary>
public class ClaudeCodeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeCodeException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ClaudeCodeException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeCodeException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public ClaudeCodeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when Claude Code CLI is not installed.
/// </summary>
public class ClaudeNotInstalledException : ClaudeCodeException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeNotInstalledException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ClaudeNotInstalledException(string message) : base(message) { }
}
