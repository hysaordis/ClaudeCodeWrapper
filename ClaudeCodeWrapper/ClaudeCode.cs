using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ClaudeCodeWrapper.Formatters;
using ClaudeCodeWrapper.Models;
using ClaudeCodeWrapper.Services;
using ClaudeCodeWrapper.Utilities;

namespace ClaudeCodeWrapper;

/// <summary>
/// Client for Claude Code CLI.
/// </summary>
public class ClaudeCode : IDisposable
{
    private static readonly Regex RateLimitErrorPattern = new(
        @"429\s*\{""type"":""error"",""error"":\{""type"":""rate_limit_error"",""message"":""([^""]+)""\},""request_id"":""([^""]+)""\}",
        RegexOptions.Compiled);

    private readonly ClaudeCodeOptions _options;
    private readonly string _claudePath;
    private readonly UsageMonitor? _usageMonitor;
    private const int FinalEventsDelayMs = 100;
    private bool _disposed;

    private ClaudeCode(string claudePath, ClaudeCodeOptions options)
    {
        _claudePath = claudePath;
        _options = options;

        // Initialize usage monitor only if enabled
        if (options.EnableUsageMonitoring)
        {
            _usageMonitor = new UsageMonitor(options.UsageCacheExpiry);
        }
    }

    /// <summary>
    /// Initialize Claude Code client. Throws if Claude CLI is not installed.
    /// </summary>
    public static ClaudeCode Initialize(ClaudeCodeOptions? options = null)
    {
        var opts = options ?? new ClaudeCodeOptions();

        if (!string.IsNullOrEmpty(opts.ClaudePath))
        {
            if (!File.Exists(opts.ClaudePath))
                throw new ClaudeNotInstalledException($"Claude CLI not found at: {opts.ClaudePath}");
            return new ClaudeCode(opts.ClaudePath, opts);
        }

        if (!ClaudeDetection.TryDetectClaudePath(out var path) || path == null)
        {
            throw new ClaudeNotInstalledException(
                "Claude Code CLI is not installed. Install it with: npm install -g @anthropic-ai/claude-code");
        }

        return new ClaudeCode(path, opts);
    }

    /// <summary>
    /// Check if Claude Code CLI is installed.
    /// </summary>
    public static bool IsInstalled()
    {
        return ClaudeDetection.TryDetectClaudePath(out _);
    }

    /// <summary>
    /// Options used by this client.
    /// </summary>
    public ClaudeCodeOptions Options => _options;

    /// <summary>
    /// Usage monitor for checking limits. Null if EnableUsageMonitoring is false.
    /// </summary>
    public UsageMonitor? UsageMonitor => _usageMonitor;

    #region Send Methods

    /// <summary>
    /// Send a prompt and get the response.
    /// </summary>
    public async Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var args = BuildArguments(prompt);
        return await ExecuteAsync(args, cancellationToken);
    }

    /// <summary>
    /// Send a prompt and get detailed response with metrics.
    /// </summary>
    public async Task<Response> SendWithResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var originalFormat = _options.OutputFormat;
        _options.OutputFormat = OutputFormat.Json;

        try
        {
            var args = BuildArguments(prompt);
            var jsonOutput = await ExecuteAsync(args, cancellationToken);
            return JsonOutputFormatter.ParseToResponse(jsonOutput);
        }
        finally
        {
            _options.OutputFormat = originalFormat;
        }
    }

    #endregion

    #region Usage Methods

    /// <summary>
    /// Get current usage information.
    /// Returns null if usage monitoring is disabled or API is unavailable.
    /// </summary>
    public async Task<UsageInfo?> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        return _usageMonitor != null
            ? await _usageMonitor.GetUsageAsync(cancellationToken)
            : null;
    }

    /// <summary>
    /// Check if current usage is within the specified limits.
    /// Returns true if monitoring is disabled (assumes OK).
    /// </summary>
    public async Task<bool> IsWithinLimitsAsync(
        double sessionThreshold = 95,
        double weeklyThreshold = 90,
        CancellationToken cancellationToken = default)
    {
        return _usageMonitor != null
            ? await _usageMonitor.IsWithinLimitsAsync(sessionThreshold, weeklyThreshold, cancellationToken)
            : true;
    }

    #endregion

    #region Stream Methods

    /// <summary>
    /// Send a prompt and stream activities in real-time.
    /// </summary>
    public async Task<string> StreamAsync(
        string prompt,
        Action<Activity> onActivity,
        CancellationToken cancellationToken = default)
    {
        using var monitor = CreateMonitor();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        monitor.Error += (_, ex) => errors.Add(ex);
        using var subscription = monitor.Subscribe(a => onActivity(Activity.From(a)));

        monitor.Start();
        var result = await SendAsync(prompt, cancellationToken);
        await Task.Delay(FinalEventsDelayMs, cancellationToken);

        if (!errors.IsEmpty)
            throw new AggregateException("Errors occurred during monitoring", errors);

        return result;
    }

    /// <summary>
    /// Send a prompt, stream activities, and get detailed response.
    /// </summary>
    public async Task<Response> StreamWithResponseAsync(
        string prompt,
        Action<Activity> onActivity,
        CancellationToken cancellationToken = default)
    {
        using var monitor = CreateMonitor();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        monitor.Error += (_, ex) => errors.Add(ex);
        using var subscription = monitor.Subscribe(a => onActivity(Activity.From(a)));

        monitor.Start();
        var result = await SendWithResponseAsync(prompt, cancellationToken);
        await Task.Delay(FinalEventsDelayMs, cancellationToken);

        if (!errors.IsEmpty)
            throw new AggregateException("Errors occurred during monitoring", errors);

        return result;
    }

    /// <summary>
    /// Send a prompt with async activity handler.
    /// </summary>
    public async Task<Response> StreamWithResponseAsync(
        string prompt,
        Func<Activity, Task> onActivityAsync,
        CancellationToken cancellationToken = default)
    {
        using var monitor = CreateMonitor();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        monitor.Error += (_, ex) => errors.Add(ex);
        using var subscription = monitor.Subscribe(activity =>
        {
            _ = Task.Run(async () =>
            {
                try { await onActivityAsync(Activity.From(activity)); }
                catch (Exception ex) { errors.Add(ex); }
            }, cancellationToken);
        });

        monitor.Start();
        var result = await SendWithResponseAsync(prompt, cancellationToken);
        await Task.Delay(FinalEventsDelayMs, cancellationToken);

        if (!errors.IsEmpty)
            throw new AggregateException("Errors occurred during monitoring", errors);

        return result;
    }

    #endregion

    #region Session Management

    /// <summary>
    /// Resume a previous session.
    /// </summary>
    public async Task<string> ResumeAsync(string sessionId, string prompt, CancellationToken cancellationToken = default)
    {
        var original = _options.ResumeSessionId;
        _options.ResumeSessionId = sessionId;

        try
        {
            return await SendAsync(prompt, cancellationToken);
        }
        finally
        {
            _options.ResumeSessionId = original;
        }
    }

    /// <summary>
    /// Continue the last session.
    /// </summary>
    public async Task<string> ContinueAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var original = _options.ContinueLastSession;
        _options.ContinueLastSession = true;

        try
        {
            return await SendAsync(prompt, cancellationToken);
        }
        finally
        {
            _options.ContinueLastSession = original;
        }
    }

    #endregion

    #region Private Methods

    private Core.SessionMonitor CreateMonitor()
    {
        return new Core.SessionMonitor(new SessionMonitorOptions
        {
            WorkingDirectory = _options.WorkingDirectory,
            IncludeExistingContent = false,
            NewFileToleranceSeconds = 2
        });
    }

    private string BuildArguments(string prompt)
    {
        var args = new List<string> { "--print" };

        args.Add($"\"{prompt.Replace("\"", "\\\"")}\"");

        if (_options.OutputFormat != OutputFormat.Text)
        {
            var format = _options.OutputFormat switch
            {
                OutputFormat.Json => "json",
                OutputFormat.StreamJson => "stream-json",
                _ => "text"
            };
            args.Add($"--output-format {format}");
        }

        if (!string.IsNullOrEmpty(_options.Model))
            args.Add($"--model {_options.Model}");

        if (!string.IsNullOrEmpty(_options.SystemPrompt))
            args.Add($"--system-prompt \"{_options.SystemPrompt.Replace("\"", "\\\"")}\"");

        if (!string.IsNullOrEmpty(_options.AppendSystemPrompt))
            args.Add($"--append-system-prompt \"{_options.AppendSystemPrompt.Replace("\"", "\\\"")}\"");

        if (_options.PermissionMode != null)
            args.Add($"--permission-mode {_options.PermissionMode.Value}");

        if (_options.MaxTurns > 0)
            args.Add($"--max-turns {_options.MaxTurns}");

        if (_options.AllowedTools.Count > 0)
            args.Add($"--allowed-tools \"{string.Join(",", _options.AllowedTools)}\"");

        if (_options.DisallowedTools.Count > 0)
            args.Add($"--disallowed-tools \"{string.Join(",", _options.DisallowedTools)}\"");

        if (!string.IsNullOrEmpty(_options.ResumeSessionId))
            args.Add($"-r \"{_options.ResumeSessionId}\"");

        if (_options.ContinueLastSession)
            args.Add("-c");

        return string.Join(" ", args);
    }

    private async Task<string> ExecuteAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _claudePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(_options.WorkingDirectory))
            startInfo.WorkingDirectory = _options.WorkingDirectory;

        startInfo.Environment.Remove("ANTHROPIC_API_KEY");

        foreach (var env in _options.EnvironmentVariables)
            startInfo.Environment[env.Key] = env.Value;

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }

        var outputText = output.ToString().Trim();
        var errorText = error.ToString();

        if (process.ExitCode != 0)
        {
            // Check for rate limit errors and throw specific exception
            var combinedOutput = $"{outputText}\n{errorText}";
            if (TryParseRateLimitError(combinedOutput, out var rateLimitException))
            {
                throw rateLimitException;
            }

            // Check for overloaded errors (529)
            if (combinedOutput.Contains("529") && combinedOutput.Contains("overloaded"))
            {
                throw new OverloadedException(
                    "Claude API is temporarily overloaded. Please retry later.",
                    retryAfter: TimeSpan.FromMinutes(1));
            }

            throw new ClaudeCodeException($"Claude CLI exited with code {process.ExitCode}: {errorText}");
        }

        return outputText;
    }

    /// <summary>
    /// Try to parse a rate limit error from CLI output.
    /// </summary>
    private static bool TryParseRateLimitError(string output, out RateLimitException exception)
    {
        exception = null!;

        if (string.IsNullOrEmpty(output))
            return false;

        // Check for 429 rate limit error pattern
        var match = RateLimitErrorPattern.Match(output);
        if (match.Success)
        {
            var message = match.Groups[1].Value;
            var requestId = match.Groups[2].Value;

            exception = new RateLimitException(
                message,
                requestId: requestId,
                errorType: "rate_limit_error");
            return true;
        }

        // Check for generic rate limit indicators
        if (output.Contains("429") &&
            (output.Contains("rate_limit_error") || output.Contains("rate limit", StringComparison.OrdinalIgnoreCase)))
        {
            exception = new RateLimitException("Rate limit exceeded (429)");
            return true;
        }

        return false;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Dispose resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _usageMonitor?.Dispose();
        }

        _disposed = true;
    }

    #endregion
}
