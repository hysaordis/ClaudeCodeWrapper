using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Text.Json;
using ClaudeCodeWrapper.Models;

namespace ClaudeCodeWrapper.Core;

/// <summary>
/// Monitors Claude session logs and emits activities as they occur.
/// Implements IObservable for flexible event consumption using Reactive Extensions.
///
/// Usage:
/// <code>
/// var monitor = new SessionMonitor(new SessionMonitorOptions { WorkingDirectory = "/path/to/project" });
/// monitor.Subscribe(activity => Console.WriteLine($"{activity.Type}: {activity.ToolName}"));
/// monitor.Start();
/// // ... execute Claude commands ...
/// monitor.Stop();
/// monitor.Dispose();
/// </code>
/// </summary>
public class SessionMonitor : IObservable<SessionActivity>, IDisposable
{
    private readonly SessionMonitorOptions _options;
    private readonly Subject<SessionActivity> _subject = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    private FileSystemWatcher? _parentDirectoryWatcher;
    private FileSystemWatcher? _projectDirectoryWatcher;
    private FileSystemWatcher? _sessionFileWatcher;
    private Timer? _pollingTimer;

    private string? _sessionFilePath;
    private string? _projectDirectory;
    private string? _currentSessionId;
    private long _lastPosition;
    private DateTime _watchStartTime;
    private bool _isMonitoring;
    private bool _disposed;

    // Track multiple files (main session + agent files)
    private readonly Dictionary<string, long> _filePositions = new();
    private readonly HashSet<string> _trackedFiles = new();

    /// <summary>
    /// Polling interval in milliseconds (FileSystemWatcher can be unreliable on macOS).
    /// </summary>
    private const int PollingIntervalMs = 100;

    /// <summary>
    /// Creates a new session monitor with the specified options.
    /// </summary>
    public SessionMonitor(SessionMonitorOptions? options = null)
    {
        _options = options ?? new SessionMonitorOptions();
    }

    /// <summary>
    /// Current session ID being monitored (if known).
    /// </summary>
    public string? CurrentSessionId => _currentSessionId;

    /// <summary>
    /// Whether monitoring is currently active.
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// Raised when an error occurs during monitoring.
    /// </summary>
    public event EventHandler<Exception>? Error;

    /// <summary>
    /// Subscribe to session activities.
    /// </summary>
    public IDisposable Subscribe(IObserver<SessionActivity> observer)
    {
        return _subject.Subscribe(observer);
    }

    /// <summary>
    /// Start monitoring for session activities.
    /// Call this BEFORE starting Claude execution.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SessionMonitor));

        if (_isMonitoring)
            return;

        _isMonitoring = true;
        _watchStartTime = DateTime.UtcNow;
        _lastPosition = 0;
        _sessionFilePath = null;

        try
        {
            // If session ID is provided, monitor that specific session
            if (!string.IsNullOrEmpty(_options.SessionId))
            {
                StartWatchingSession(_options.SessionId);
                return;
            }

            // Otherwise, watch for new session files in the project directory
            var claudeProjectsDir = _options.GetClaudeProjectsPath();

            if (!Directory.Exists(claudeProjectsDir))
            {
                // Claude projects directory doesn't exist yet - nothing to watch
                return;
            }

            _projectDirectory = _options.GetDerivedProjectPath();

            if (string.IsNullOrEmpty(_projectDirectory))
            {
                // No working directory specified - can't derive project path
                return;
            }

            if (Directory.Exists(_projectDirectory))
            {
                StartWatchingProjectDirectory();
                // Immediately scan for existing session files
                ScanForSessionFiles();
            }
            else
            {
                // Project directory doesn't exist yet - watch for its creation
                WatchForProjectDirectoryCreation(claudeProjectsDir);
            }

            // Start fallback polling timer immediately (dueTime=0) - FileSystemWatcher can be unreliable on macOS
            _pollingTimer = new Timer(OnPollingTimerCallback, null, 0, PollingIntervalMs);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    /// <summary>
    /// Immediately scan for session files in the project directory.
    /// Called on Start() for fast initial detection.
    /// </summary>
    private void ScanForSessionFiles()
    {
        if (_projectDirectory == null || !Directory.Exists(_projectDirectory)) return;

        try
        {
            var jsonlFiles = Directory.GetFiles(_projectDirectory, "*.jsonl");
            var toleranceTime = _watchStartTime.AddSeconds(-_options.NewFileToleranceSeconds);

            foreach (var file in jsonlFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTimeUtc >= toleranceTime)
                    {
                        TrackFile(file);
                    }
                }
                catch
                {
                    // Ignore file access errors
                }
            }
        }
        catch
        {
            // Ignore scan errors
        }
    }

    /// <summary>
    /// Track a file for monitoring.
    /// </summary>
    private void TrackFile(string file)
    {
        if (_trackedFiles.Add(file))
        {
            _filePositions[file] = 0;

            // Set main session file if this looks like a session (UUID format, not agent-)
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.StartsWith("agent-") && _sessionFilePath == null)
            {
                _currentSessionId = fileName;
                _sessionFilePath = file;
                _lastPosition = 0;
            }
        }
    }

    /// <summary>
    /// Fallback polling to detect new session files and changes.
    /// This compensates for unreliable FileSystemWatcher on macOS.
    /// </summary>
    private void OnPollingTimerCallback(object? state)
    {
        if (_disposed || !_isMonitoring) return;

        try
        {
            // If we don't have a project directory yet, check if it was created
            if (_projectDirectory != null && !Directory.Exists(_projectDirectory))
            {
                return; // Still waiting for directory creation
            }

            // Scan for new session files
            ScanForSessionFiles();

            // Read new content from ALL tracked files in parallel
            var tasks = _trackedFiles.ToArray().Select(f => ReadNewLinesFromFileAsync(f, CancellationToken.None));
            Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Ignore polling errors - will retry on next interval
        }
    }

    /// <summary>
    /// Stop monitoring. Can be restarted with Start().
    /// </summary>
    public void Stop()
    {
        _isMonitoring = false;
        CleanupWatchers();
    }

    /// <summary>
    /// Manually trigger reading of new lines from the session file.
    /// Useful when you know new content has been written.
    /// </summary>
    public async Task ReadNewActivitiesAsync(CancellationToken cancellationToken = default)
    {
        await ReadNewLinesAsync(cancellationToken);
    }

    private void StartWatchingSession(string sessionId)
    {
        var claudeProjectsDir = _options.GetClaudeProjectsPath();

        if (!Directory.Exists(claudeProjectsDir))
            return;

        // Search for the session log file
        var sessionFiles = Directory.GetFiles(claudeProjectsDir, $"{sessionId}.jsonl", SearchOption.AllDirectories);
        _sessionFilePath = sessionFiles.FirstOrDefault();

        if (_sessionFilePath == null)
            return;

        _currentSessionId = sessionId;

        // Read existing content if requested
        if (_options.IncludeExistingContent)
        {
            _ = ReadNewLinesAsync(CancellationToken.None);
        }

        // Watch for changes
        var directory = Path.GetDirectoryName(_sessionFilePath)!;
        var fileName = Path.GetFileName(_sessionFilePath);

        _sessionFileWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _sessionFileWatcher.Changed += OnSessionFileChanged;
        _sessionFileWatcher.EnableRaisingEvents = true;
    }

    private void WatchForProjectDirectoryCreation(string parentDir)
    {
        if (_projectDirectory == null) return;

        var targetSubdir = Path.GetFileName(_projectDirectory);

        _parentDirectoryWatcher = new FileSystemWatcher(parentDir)
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false
        };

        _parentDirectoryWatcher.Created += (s, e) =>
        {
            if (e.Name == targetSubdir)
            {
                _projectDirectory = e.FullPath;
                StartWatchingProjectDirectory();
            }
        };

        _parentDirectoryWatcher.EnableRaisingEvents = true;
    }

    private void StartWatchingProjectDirectory()
    {
        if (_projectDirectory == null || _disposed || !_isMonitoring) return;

        // Watch for new .jsonl files in this directory
        _projectDirectoryWatcher = new FileSystemWatcher(_projectDirectory, "*.jsonl")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _projectDirectoryWatcher.Created += OnNewSessionFileCreated;
        _projectDirectoryWatcher.Changed += OnSessionFileChanged;
        _projectDirectoryWatcher.EnableRaisingEvents = true;
    }

    private void OnNewSessionFileCreated(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !_isMonitoring) return;

        try
        {
            // Only process files created AFTER we started watching (with tolerance for race conditions)
            var fileInfo = new FileInfo(e.FullPath);
            var toleranceTime = _watchStartTime.AddSeconds(-_options.NewFileToleranceSeconds);

            if (fileInfo.CreationTimeUtc < toleranceTime)
                return; // Ignore old file

            TrackFile(e.FullPath);

            // Read initial content
            _ = ReadNewLinesFromFileAsync(e.FullPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void OnSessionFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !_isMonitoring) return;

        // Track this file if not already tracked (handles case where Created event was missed)
        if (!_trackedFiles.Contains(e.FullPath))
        {
            try
            {
                var fileInfo = new FileInfo(e.FullPath);
                var toleranceTime = _watchStartTime.AddSeconds(-_options.NewFileToleranceSeconds);

                if (fileInfo.CreationTimeUtc < toleranceTime)
                    return; // Old file, ignore

                TrackFile(e.FullPath);
            }
            catch
            {
                return; // File access error, ignore
            }
        }

        // Read new content from this specific file
        _ = ReadNewLinesFromFileAsync(e.FullPath, CancellationToken.None);
    }

    private async Task ReadNewLinesAsync(CancellationToken cancellationToken)
    {
        if (_sessionFilePath == null || _disposed || !_isMonitoring) return;
        await ReadNewLinesFromFileAsync(_sessionFilePath, cancellationToken);
    }

    private async Task ReadNewLinesFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_disposed || !_isMonitoring || !File.Exists(filePath)) return;

        // Per-file lock to allow parallel reads of different files
        var fileLock = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        if (!await fileLock.WaitAsync(0, cancellationToken))
            return; // Another read in progress for this file

        try
        {
            // Get or initialize file position
            if (!_filePositions.TryGetValue(filePath, out var lastPosition))
            {
                lastPosition = 0;
                _filePositions[filePath] = 0;
            }

            await using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            if (fs.Length <= lastPosition)
                return;

            fs.Seek(lastPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, bufferSize: 4096);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Parse the JSONL line
                foreach (var activity in ParseJsonLine(line))
                {
                    _subject.OnNext(activity);
                }
            }

            _filePositions[filePath] = fs.Position;

            // Also update legacy _lastPosition for main session file
            if (filePath == _sessionFilePath)
            {
                _lastPosition = fs.Position;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnError(ex);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private void OnError(Exception ex)
    {
        Error?.Invoke(this, ex);
    }

    private void CleanupWatchers()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;

        _parentDirectoryWatcher?.Dispose();
        _parentDirectoryWatcher = null;

        _projectDirectoryWatcher?.Dispose();
        _projectDirectoryWatcher = null;

        _sessionFileWatcher?.Dispose();
        _sessionFileWatcher = null;
    }

    /// <summary>
    /// Parse a JSONL line into activities.
    /// </summary>
    private static IEnumerable<SessionActivity> ParseJsonLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            var timestamp = root.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? "" : "";
            var sessionId = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
            var uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() : null;
            var parentUuid = root.TryGetProperty("parentUuid", out var pu) ? pu.GetString() : null;

            // Check for subagent
            string? agentId = null;
            bool isSidechain = false;
            if (root.TryGetProperty("agentId", out var aid))
            {
                agentId = aid.GetString();
                isSidechain = !string.IsNullOrEmpty(agentId);
            }

            if (type == "assistant" && root.TryGetProperty("message", out var msg))
            {
                if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        var itemType = item.TryGetProperty("type", out var it) ? it.GetString() : null;

                        if (itemType == "tool_use")
                        {
                            var toolName = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var toolId = item.TryGetProperty("id", out var id) ? id.GetString() : null;
                            string? toolInput = null;

                            if (item.TryGetProperty("input", out var inp))
                            {
                                toolInput = inp.ValueKind == JsonValueKind.String
                                    ? inp.GetString()
                                    : inp.GetRawText();
                            }

                            yield return SessionActivity.ToolCall(
                                toolName ?? "unknown",
                                toolInput,
                                timestamp,
                                sessionId,
                                uuid,
                                parentUuid,
                                toolId,
                                null,
                                agentId,
                                isSidechain);
                        }
                        else if (itemType == "text")
                        {
                            var text = item.TryGetProperty("text", out var txt) ? txt.GetString() : null;
                            if (!string.IsNullOrEmpty(text))
                            {
                                yield return SessionActivity.Thought(
                                    text,
                                    timestamp,
                                    sessionId,
                                    uuid,
                                    parentUuid,
                                    null,
                                    agentId,
                                    isSidechain);
                            }
                        }
                    }
                }
            }
            else if (type == "user" && root.TryGetProperty("message", out var userMsg))
            {
                if (userMsg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        var itemType = item.TryGetProperty("type", out var it) ? it.GetString() : null;

                        if (itemType == "tool_result")
                        {
                            var toolId = item.TryGetProperty("tool_use_id", out var tid) ? tid.GetString() : null;
                            var isError = item.TryGetProperty("is_error", out var ie) && ie.GetBoolean();
                            string? resultContent = null;

                            if (item.TryGetProperty("content", out var rc))
                            {
                                resultContent = rc.ValueKind == JsonValueKind.String
                                    ? rc.GetString()
                                    : rc.GetRawText();
                            }

                            yield return SessionActivity.ToolResult(
                                resultContent,
                                !isError,
                                timestamp,
                                sessionId,
                                uuid,
                                parentUuid,
                                toolId,
                                agentId,
                                isSidechain);
                        }
                    }
                }
            }
        }
        finally
        {
            doc?.Dispose();
        }
    }

    /// <summary>
    /// Dispose and release all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _isMonitoring = false;
        CleanupWatchers();

        // Dispose per-file locks
        foreach (var fileLock in _fileLocks.Values)
            fileLock.Dispose();
        _fileLocks.Clear();

        _subject.OnCompleted();
        _subject.Dispose();
    }
}
