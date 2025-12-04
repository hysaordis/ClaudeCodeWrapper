using System.Diagnostics;
using System.Text;
using ClaudeCodeWrapper.Formatters;
using ClaudeCodeWrapper.Models;
using ClaudeCodeWrapper.Utilities;

namespace ClaudeCodeWrapper;

/// <summary>
/// Client for Claude Code CLI.
/// </summary>
public class ClaudeCode
{
    private readonly ClaudeCodeOptions _options;
    private readonly string _claudePath;
    private const int FinalEventsDelayMs = 100;

    private ClaudeCode(string claudePath, ClaudeCodeOptions options)
    {
        _claudePath = claudePath;
        _options = options;
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
            throw new ClaudeCodeException($"Claude CLI exited with code {process.ExitCode}: {errorText}");

        return outputText;
    }

    #endregion
}
