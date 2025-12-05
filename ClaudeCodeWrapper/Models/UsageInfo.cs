using System.Text.Json.Serialization;

namespace ClaudeCodeWrapper.Models;

/// <summary>
/// Usage information from Claude API.
/// </summary>
public class UsageInfo
{
    /// <summary>
    /// 5-hour session usage limit.
    /// </summary>
    [JsonPropertyName("five_hour")]
    public SessionUsage FiveHour { get; set; } = new();

    /// <summary>
    /// 7-day weekly usage limit.
    /// </summary>
    [JsonPropertyName("seven_day")]
    public SessionUsage SevenDay { get; set; } = new();

    /// <summary>
    /// 7-day Opus-specific usage limit (if applicable).
    /// </summary>
    [JsonPropertyName("seven_day_opus")]
    public SessionUsage? SevenDayOpus { get; set; }

    /// <summary>
    /// 7-day OAuth apps usage limit (if applicable).
    /// </summary>
    [JsonPropertyName("seven_day_oauth_apps")]
    public SessionUsage? SevenDayOAuthApps { get; set; }
}

/// <summary>
/// Usage information for a specific time window.
/// </summary>
public class SessionUsage
{
    /// <summary>
    /// Percentage of limit used (0-100).
    /// </summary>
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    /// <summary>
    /// When this limit resets (UTC).
    /// </summary>
    [JsonPropertyName("resets_at")]
    public DateTime? ResetsAt { get; set; }

    /// <summary>
    /// Remaining percentage available.
    /// </summary>
    [JsonIgnore]
    public double Available => Math.Max(0, 100 - Utilization);

    /// <summary>
    /// Time until the limit resets.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? TimeUntilReset => ResetsAt?.Subtract(DateTime.UtcNow);

    /// <summary>
    /// Whether the limit has been reached.
    /// </summary>
    [JsonIgnore]
    public bool IsExhausted => Utilization >= 100;

    /// <summary>
    /// Whether we're approaching the limit (>80%).
    /// </summary>
    [JsonIgnore]
    public bool IsApproachingLimit => Utilization >= 80;
}

/// <summary>
/// OAuth credentials from Claude Code keychain.
/// </summary>
public class ClaudeCredentials
{
    [JsonPropertyName("claudeAiOauth")]
    public ClaudeOAuthInfo? ClaudeAiOauth { get; set; }
}

/// <summary>
/// OAuth token information.
/// </summary>
public class ClaudeOAuthInfo
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    [JsonPropertyName("subscriptionType")]
    public string? SubscriptionType { get; set; }

    /// <summary>
    /// Whether the token has the required scope for usage API.
    /// </summary>
    public bool HasProfileScope => Scopes?.Contains("user:profile") ?? false;
}

/// <summary>
/// Warning levels for usage alerts.
/// </summary>
public enum UsageWarningLevel
{
    /// <summary>
    /// Usage is normal.
    /// </summary>
    Normal,

    /// <summary>
    /// Usage is moderate (50-70%).
    /// </summary>
    Moderate,

    /// <summary>
    /// Usage is high (70-90%).
    /// </summary>
    High,

    /// <summary>
    /// Usage is critical (>90%).
    /// </summary>
    Critical,

    /// <summary>
    /// Limit has been reached.
    /// </summary>
    Exhausted
}

/// <summary>
/// Event args for usage warnings.
/// </summary>
public class UsageWarningEventArgs : EventArgs
{
    /// <summary>
    /// Warning severity level.
    /// </summary>
    public UsageWarningLevel Level { get; set; }

    /// <summary>
    /// Human-readable warning message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Current utilization percentage.
    /// </summary>
    public double Utilization { get; set; }

    /// <summary>
    /// When the limit resets.
    /// </summary>
    public DateTime? ResetsAt { get; set; }

    /// <summary>
    /// Time until reset.
    /// </summary>
    public TimeSpan? TimeUntilReset { get; set; }

    /// <summary>
    /// Which limit type triggered the warning.
    /// </summary>
    public string LimitType { get; set; } = "session";
}
