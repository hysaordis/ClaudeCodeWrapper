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

/// <summary>
/// Thrown when Claude API rate limit is exceeded (HTTP 429).
/// </summary>
public class RateLimitException : ClaudeCodeException
{
    /// <summary>
    /// The request ID from the API for debugging.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// The error type from the API (e.g., "rate_limit_error").
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// Suggested time to wait before retrying.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// When the rate limit will reset.
    /// </summary>
    public DateTime? ResetTime { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitException"/> class.
    /// </summary>
    public RateLimitException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitException"/> class with details.
    /// </summary>
    public RateLimitException(
        string message,
        string? requestId = null,
        string? errorType = null,
        TimeSpan? retryAfter = null,
        DateTime? resetTime = null)
        : base(message)
    {
        RequestId = requestId;
        ErrorType = errorType;
        RetryAfter = retryAfter;
        ResetTime = resetTime;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitException"/> class with inner exception.
    /// </summary>
    public RateLimitException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when the API is temporarily overloaded (HTTP 529).
/// </summary>
public class OverloadedException : ClaudeCodeException
{
    /// <summary>
    /// The request ID from the API for debugging.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// Suggested time to wait before retrying.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverloadedException"/> class.
    /// </summary>
    public OverloadedException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="OverloadedException"/> class with details.
    /// </summary>
    public OverloadedException(string message, string? requestId = null, TimeSpan? retryAfter = null)
        : base(message)
    {
        RequestId = requestId;
        RetryAfter = retryAfter;
    }
}
