using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClaudeCodeWrapper.Models;

namespace ClaudeCodeWrapper.Services;

/// <summary>
/// Monitors Claude Code usage limits and provides real-time usage data.
/// </summary>
public class UsageMonitor : IDisposable
{
    private const string UsageApiUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string KeychainService = "Claude Code-credentials";
    private const string UserAgent = "claude-code/2.0.53";
    private const string BetaHeader = "oauth-2025-04-20";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheExpiry;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);

    private UsageInfo? _cachedUsage;
    private DateTime _lastFetchTime = DateTime.MinValue;
    private string? _cachedAccessToken;
    private bool _tokenHasProfileScope = true;
    private bool _disposed;

    /// <summary>
    /// Fired when usage data is updated.
    /// </summary>
    public event EventHandler<UsageInfo>? UsageUpdated;

    /// <summary>
    /// Fired when usage approaches or exceeds thresholds.
    /// </summary>
    public event EventHandler<UsageWarningEventArgs>? UsageWarning;

    /// <summary>
    /// Fired when an error occurs during usage fetch.
    /// </summary>
    public event EventHandler<Exception>? FetchError;

    /// <summary>
    /// Creates a new UsageMonitor instance.
    /// </summary>
    /// <param name="cacheExpiry">How long to cache usage data before refetching.</param>
    public UsageMonitor(TimeSpan? cacheExpiry = null)
    {
        _cacheExpiry = cacheExpiry ?? TimeSpan.FromMinutes(1);
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Whether the API is available (token has required scope).
    /// </summary>
    public bool IsApiAvailable => _tokenHasProfileScope;

    /// <summary>
    /// Last known usage data (may be cached).
    /// </summary>
    public UsageInfo? CachedUsage => _cachedUsage;

    /// <summary>
    /// Time of last successful fetch.
    /// </summary>
    public DateTime LastFetchTime => _lastFetchTime;

    /// <summary>
    /// Get current usage, fetching from API if cache is expired.
    /// </summary>
    public async Task<UsageInfo?> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        if (!_tokenHasProfileScope)
            return null;

        // Return cached if still valid
        if (_cachedUsage != null && DateTime.UtcNow - _lastFetchTime < _cacheExpiry)
            return _cachedUsage;

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedUsage != null && DateTime.UtcNow - _lastFetchTime < _cacheExpiry)
                return _cachedUsage;

            return await FetchUsageFromApiAsync(cancellationToken);
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Force a refresh of usage data from the API.
    /// </summary>
    public async Task<UsageInfo?> RefreshUsageAsync(CancellationToken cancellationToken = default)
    {
        if (!_tokenHasProfileScope)
            return null;

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            return await FetchUsageFromApiAsync(cancellationToken);
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Check if current usage is within safe limits.
    /// </summary>
    public async Task<bool> IsWithinLimitsAsync(
        double sessionThreshold = 95,
        double weeklyThreshold = 90,
        CancellationToken cancellationToken = default)
    {
        var usage = await GetUsageAsync(cancellationToken);
        if (usage == null)
            return true; // Assume OK if we can't check

        return usage.FiveHour.Utilization < sessionThreshold &&
               usage.SevenDay.Utilization < weeklyThreshold;
    }

    private async Task<UsageInfo?> FetchUsageFromApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _tokenHasProfileScope = false;
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, UsageApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", BetaHeader);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Check for scope error
                if (errorContent.Contains("user:profile"))
                {
                    _tokenHasProfileScope = false;
                    FetchError?.Invoke(this, new InvalidOperationException(
                        "OAuth token does not have 'user:profile' scope. Usage monitoring unavailable."));
                    return null;
                }

                throw new HttpRequestException($"Usage API returned {response.StatusCode}: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var usage = JsonSerializer.Deserialize<UsageInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (usage != null)
            {
                _cachedUsage = usage;
                _lastFetchTime = DateTime.UtcNow;
                UsageUpdated?.Invoke(this, usage);
                CheckAndRaiseWarnings(usage);
            }

            return usage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FetchError?.Invoke(this, ex);
            return _cachedUsage; // Return stale cache on error
        }
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedAccessToken))
            return _cachedAccessToken;

        try
        {
            string? credentialsJson = null;

            // Try platform-specific secure storage first
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                credentialsJson = await GetFromMacOsKeychainAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                credentialsJson = await GetFromWindowsCredentialStoreAsync();
            }

            // Fallback to credentials file (Linux, Docker, or if secure storage failed)
            if (string.IsNullOrEmpty(credentialsJson))
            {
                credentialsJson = await GetFromCredentialsFileAsync();
            }

            if (string.IsNullOrEmpty(credentialsJson))
                return null;

            var credentials = JsonSerializer.Deserialize<ClaudeCredentials>(credentialsJson);
            _cachedAccessToken = credentials?.ClaudeAiOauth?.AccessToken;

            // Check if token has the required scope
            if (credentials?.ClaudeAiOauth?.Scopes != null &&
                !credentials.ClaudeAiOauth.Scopes.Contains("user:profile"))
            {
                _tokenHasProfileScope = false;
            }

            return _cachedAccessToken;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetFromMacOsKeychainAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"find-generic-password -s \"{KeychainService}\" -w",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static Task<string?> GetFromWindowsCredentialStoreAsync()
    {
        // Windows implementation would use Windows Credential Manager
        // For now, fall through to credentials file
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Read credentials from ~/.claude/.credentials.json file.
    /// This is the standard storage on Linux/Docker and fallback for other platforms.
    /// </summary>
    private static async Task<string?> GetFromCredentialsFileAsync()
    {
        try
        {
            // Try multiple possible locations
            var possiblePaths = new[]
            {
                // Standard Claude Code credentials file
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json"),
                // Alternative location
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "credentials.json"),
                // Docker/container common locations
                "/root/.claude/.credentials.json",
                "/home/.claude/.credentials.json",
                // Environment variable override
                Environment.GetEnvironmentVariable("CLAUDE_CREDENTIALS_FILE") ?? ""
            };

            foreach (var path in possiblePaths.Where(p => !string.IsNullOrEmpty(p)))
            {
                if (File.Exists(path))
                {
                    return await File.ReadAllTextAsync(path);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private void CheckAndRaiseWarnings(UsageInfo usage)
    {
        // Check 5-hour session limit
        var sessionLevel = GetWarningLevel(usage.FiveHour.Utilization);
        if (sessionLevel > UsageWarningLevel.Normal)
        {
            UsageWarning?.Invoke(this, new UsageWarningEventArgs
            {
                Level = sessionLevel,
                Message = $"Session usage at {usage.FiveHour.Utilization:F1}%",
                Utilization = usage.FiveHour.Utilization,
                ResetsAt = usage.FiveHour.ResetsAt,
                TimeUntilReset = usage.FiveHour.TimeUntilReset,
                LimitType = "session"
            });
        }

        // Check 7-day weekly limit
        var weeklyLevel = GetWarningLevel(usage.SevenDay.Utilization);
        if (weeklyLevel > UsageWarningLevel.Normal)
        {
            UsageWarning?.Invoke(this, new UsageWarningEventArgs
            {
                Level = weeklyLevel,
                Message = $"Weekly usage at {usage.SevenDay.Utilization:F1}%",
                Utilization = usage.SevenDay.Utilization,
                ResetsAt = usage.SevenDay.ResetsAt,
                TimeUntilReset = usage.SevenDay.TimeUntilReset,
                LimitType = "weekly"
            });
        }
    }

    private static UsageWarningLevel GetWarningLevel(double utilization) => utilization switch
    {
        >= 100 => UsageWarningLevel.Exhausted,
        >= 90 => UsageWarningLevel.Critical,
        >= 70 => UsageWarningLevel.High,
        >= 50 => UsageWarningLevel.Moderate,
        _ => UsageWarningLevel.Normal
    };

    /// <summary>
    /// Invalidate the cached token (e.g., after re-authentication).
    /// </summary>
    public void InvalidateToken()
    {
        _cachedAccessToken = null;
        _tokenHasProfileScope = true; // Reset assumption
    }

    /// <summary>
    /// Invalidate the cached usage data.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedUsage = null;
        _lastFetchTime = DateTime.MinValue;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        _fetchLock.Dispose();
    }
}
