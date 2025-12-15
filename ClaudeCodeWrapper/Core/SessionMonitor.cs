using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Text;
using ClaudeCodeWrapper.Models;
using ClaudeCodeWrapper.Models.Records;

namespace ClaudeCodeWrapper.Core;

/// <summary>
/// Monitors Claude session logs and emits records as they occur.
/// Implements IObservable for flexible event consumption using Reactive Extensions.
/// </summary>
public sealed class SessionMonitor : IObservable<SessionRecord>, IDisposable
{
    private readonly SessionMonitorOptions _options;

    // Subject emission must be serialized (Subject<T> isn't safe for concurrent OnNext).
    private readonly Subject<SessionRecord> _rawSubject = new();
    private readonly ISubject<SessionRecord> _subject;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    // Tracking state (thread-safe)
    private readonly ConcurrentDictionary<string, byte> _trackedFiles = new();          // set
    private readonly ConcurrentDictionary<string, long> _fileReadOffsets = new();       // filePath -> read offset (bytes read from file)
    private readonly ConcurrentDictionary<string, byte[]> _fileTailBytes = new();       // filePath -> bytes of an incomplete last line

    // Dedup (bounded)
    private readonly ConcurrentDictionary<string, byte> _seenKeys = new();              // set
    private readonly ConcurrentQueue<string> _seenOrder = new();
    private readonly int _maxSeenKeys;

    private FileSystemWatcher? _parentDirectoryWatcher;
    private FileSystemWatcher? _projectDirectoryWatcher;
    private FileSystemWatcher? _sessionFileWatcher;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    private string? _sessionFilePath;
    private string? _projectDirectory;
    private string? _currentSessionId;
    private DateTime _watchStartTime;

    private bool _isMonitoring;
    private bool _disposed;

    /// <summary>
    /// Polling interval in milliseconds (FileSystemWatcher can be unreliable on macOS).
    /// </summary>
    private const int PollingIntervalMs = 100;

    /// <summary>
    /// Upper bound per single read pass (prevents huge allocations if a file bursts).
    /// </summary>
    private const int MaxReadBytesPerPass = 4 * 1024 * 1024; // 4MB

    public SessionMonitor(SessionMonitorOptions? options = null)
    {
        _options = options ?? new SessionMonitorOptions();

        // Serialize emissions (thread-safe OnNext)
        _subject = Subject.Synchronize(_rawSubject);

        // Pick a conservative bounded size. If you want it configurable, add it to SessionMonitorOptions.
        _maxSeenKeys = 100_000;
    }

    public string? CurrentSessionId => _currentSessionId;

    public bool IsMonitoring => _isMonitoring;

    public string? SessionFilePath => _sessionFilePath;

    public IReadOnlyCollection<string> TrackedFiles => _trackedFiles.Keys.ToArray();

    public event EventHandler<Exception>? Error;

    public IDisposable Subscribe(IObserver<SessionRecord> observer) => _subject.Subscribe(observer);

    /// <summary>
    /// Start monitoring for session activities. Call this BEFORE starting Claude execution.
    /// </summary>
    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionMonitor));
        if (_isMonitoring) return;

        _isMonitoring = true;
        _watchStartTime = DateTime.UtcNow;

        ResetStateForStart();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            // If session ID is provided, monitor that specific session
            if (!string.IsNullOrEmpty(_options.SessionId))
            {
                StartWatchingSession(_options.SessionId);
            }
            else
            {
                // Otherwise, watch for new session files in the project directory
                var claudeProjectsDir = _options.GetClaudeProjectsPath();
                if (!Directory.Exists(claudeProjectsDir))
                    return;

                _projectDirectory = _options.GetDerivedProjectPath();
                if (string.IsNullOrEmpty(_projectDirectory))
                    return;

                if (Directory.Exists(_projectDirectory))
                {
                    StartWatchingProjectDirectory(_projectDirectory);
                    ScanForSessionFiles();
                }
                else
                {
                    WatchForProjectDirectoryCreation(claudeProjectsDir);
                }
            }

            // Poll loop (fallback)
            _pollingTask = Task.Run(() => PollLoopAsync(token), token);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    /// <summary>
    /// Stop monitoring. Can be restarted with Start().
    /// </summary>
    public void Stop()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;

        try { _cts?.Cancel(); } catch { /* ignore */ }
        CleanupWatchers();

        // Clear tracking state for potential restart
        ResetStateForStop();
    }

    /// <summary>
    /// Manually trigger reading of new lines from all tracked files.
    /// </summary>
    public async Task ReadNewRecordsAsync(CancellationToken cancellationToken = default)
    {
        var files = _trackedFiles.Keys.ToArray();
        var tasks = files.Select(f => ReadNewLinesFromFileAsync(f, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Get the full session once monitoring is complete.
    /// </summary>
    public async Task<Session?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSessionId == null) return null;

        var repository = new SessionRepository(Path.GetDirectoryName(_options.GetClaudeProjectsPath()));
        return await repository.LoadSessionAsync(_currentSessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PollingIntervalMs));

        while (!token.IsCancellationRequested && !_disposed && _isMonitoring)
        {
            try
            {
                await timer.WaitForNextTickAsync(token).ConfigureAwait(false);

                if (_projectDirectory != null && !Directory.Exists(_projectDirectory))
                    continue;

                ScanForSessionFiles();

                var files = _trackedFiles.Keys.ToArray();
                var tasks = files.Select(f => ReadNewLinesFromFileAsync(f, token));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
                break;
            }
            catch
            {
                // polling should be resilient; swallow
            }
        }
    }

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
                    var info = new FileInfo(file);
                    if (info.CreationTimeUtc >= toleranceTime)
                        TrackFile(file);
                }
                catch
                {
                    // ignore file access errors
                }
            }
        }
        catch
        {
            // ignore scan errors
        }
    }

    private void TrackFile(string filePath)
    {
        if (!_trackedFiles.TryAdd(filePath, 0))
            return;

        // Decide initial offset (include existing content or not)
        long initialOffset = 0;
        if (!_options.IncludeExistingContent)
        {
            try
            {
                var info = new FileInfo(filePath);
                initialOffset = info.Exists ? info.Length : 0;
            }
            catch
            {
                initialOffset = 0;
            }
        }

        _fileReadOffsets[filePath] = initialOffset;
        _fileTailBytes[filePath] = Array.Empty<byte>();

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (!fileName.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) && _sessionFilePath == null)
        {
            _currentSessionId = fileName;
            _sessionFilePath = filePath;
        }
    }

    private void StartWatchingSession(string sessionId)
    {
        var claudeProjectsDir = _options.GetClaudeProjectsPath();
        if (!Directory.Exists(claudeProjectsDir))
            return;

        var sessionFiles = Directory.GetFiles(claudeProjectsDir, $"{sessionId}.jsonl", SearchOption.AllDirectories);
        _sessionFilePath = sessionFiles.FirstOrDefault();
        if (_sessionFilePath == null)
            return;

        _currentSessionId = sessionId;

        // If session id is explicit, use its directory as project directory (to pick up agent-*.jsonl too)
        _projectDirectory = Path.GetDirectoryName(_sessionFilePath)!;
        StartWatchingProjectDirectory(_projectDirectory);

        TrackFile(_sessionFilePath);

        if (_options.IncludeExistingContent)
        {
            // Best effort immediate read
            _ = ReadNewLinesFromFileAsync(_sessionFilePath, _cts?.Token ?? CancellationToken.None);
        }

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
            if (_disposed || !_isMonitoring) return;
            if (e.Name != targetSubdir) return;

            _projectDirectory = e.FullPath;
            StartWatchingProjectDirectory(_projectDirectory);
        };

        _parentDirectoryWatcher.EnableRaisingEvents = true;
    }

    private void StartWatchingProjectDirectory(string directory)
    {
        if (_disposed || !_isMonitoring) return;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

        _projectDirectoryWatcher?.Dispose();
        _projectDirectoryWatcher = new FileSystemWatcher(directory, "*.jsonl")
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
            var info = new FileInfo(e.FullPath);
            var toleranceTime = _watchStartTime.AddSeconds(-_options.NewFileToleranceSeconds);
            if (info.CreationTimeUtc < toleranceTime)
                return;

            TrackFile(e.FullPath);
            _ = ReadNewLinesFromFileAsync(e.FullPath, _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void OnSessionFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !_isMonitoring) return;

        if (!_trackedFiles.ContainsKey(e.FullPath))
        {
            try
            {
                var info = new FileInfo(e.FullPath);
                var toleranceTime = _watchStartTime.AddSeconds(-_options.NewFileToleranceSeconds);
                if (info.CreationTimeUtc < toleranceTime)
                    return;

                TrackFile(e.FullPath);
            }
            catch
            {
                return;
            }
        }

        _ = ReadNewLinesFromFileAsync(e.FullPath, _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReadNewLinesFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_disposed || !_isMonitoring) return;
        if (!File.Exists(filePath)) return;

        var gate = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        // non-blocking: if another read is already in progress for this file, skip
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            var lastReadOffset = _fileReadOffsets.GetOrAdd(filePath, 0);

            await using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                useAsync: true);

            // Handle truncation/reset
            if (fs.Length < lastReadOffset)
            {
                lastReadOffset = 0;
                _fileReadOffsets[filePath] = 0;
                _fileTailBytes[filePath] = Array.Empty<byte>();
            }

            if (fs.Length <= lastReadOffset)
                return;

            fs.Seek(lastReadOffset, SeekOrigin.Begin);

            // Read bounded chunk
            var toRead = (int)Math.Min(MaxReadBytesPerPass, fs.Length - lastReadOffset);
            var newBytes = new byte[toRead];

            var read = 0;
            while (read < toRead)
            {
                var n = await fs.ReadAsync(newBytes.AsMemory(read, toRead - read), cancellationToken).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }

            if (read <= 0)
                return;

            if (read != toRead)
                Array.Resize(ref newBytes, read);

            // Advance "read offset" (we won't re-read these bytes again)
            _fileReadOffsets[filePath] = lastReadOffset + read;

            // Combine with any previous tail bytes (incomplete last line)
            var tail = _fileTailBytes.GetOrAdd(filePath, Array.Empty<byte>());
            byte[] combined;
            if (tail.Length == 0)
            {
                combined = newBytes;
            }
            else
            {
                combined = new byte[tail.Length + newBytes.Length];
                Buffer.BlockCopy(tail, 0, combined, 0, tail.Length);
                Buffer.BlockCopy(newBytes, 0, combined, tail.Length, newBytes.Length);
            }

            // Find last newline in combined (we only process full lines)
            var lastNewline = Array.LastIndexOf(combined, (byte)'\n');
            if (lastNewline < 0)
            {
                // still no complete line; keep accumulating
                _fileTailBytes[filePath] = combined;
                return;
            }

            var commitLen = lastNewline + 1;

            // Remaining bytes after last newline are the new tail
            var remainingLen = combined.Length - commitLen;
            if (remainingLen > 0)
            {
                var remaining = new byte[remainingLen];
                Buffer.BlockCopy(combined, commitLen, remaining, 0, remainingLen);
                _fileTailBytes[filePath] = remaining;
            }
            else
            {
                _fileTailBytes[filePath] = Array.Empty<byte>();
            }

            // Decode committed bytes (complete lines)
            var text = Encoding.UTF8.GetString(combined, 0, commitLen);

            // Split and parse
            // Note: Split keeps empty last element if text ends with '\n', which is fine.
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;

                SessionRecord? record;
                try
                {
                    record = SessionRecordParser.Parse(line);
                }
                catch
                {
                    // If parsing fails, we emit nothing (best effort).
                    // The line is "committed" already; if you want retry-on-failure,
                    // store parse-failed lines in a separate queue.
                    continue;
                }

                if (record == null) continue;

                var recordKey = record.Uuid ?? $"{record.Type}_{record.Timestamp?.Ticks}_{StableHash(line)}";
                if (!TryMarkSeen(recordKey))
                    continue;

                _subject.OnNext(record);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryMarkSeen(string key)
    {
        if (!_seenKeys.TryAdd(key, 0))
            return false;

        _seenOrder.Enqueue(key);

        // Bound the set (best-effort)
        while (_seenKeys.Count > _maxSeenKeys && _seenOrder.TryDequeue(out var old))
        {
            _seenKeys.TryRemove(old, out _);
        }

        return true;
    }

    private static int StableHash(string s)
    {
        // Stable-ish hash (better than string.GetHashCode() across processes)
        unchecked
        {
            var hash = 23;
            for (var i = 0; i < s.Length; i++)
                hash = (hash * 31) + s[i];
            return hash;
        }
    }

    private void OnError(Exception ex) => Error?.Invoke(this, ex);

    private void CleanupWatchers()
    {
        _parentDirectoryWatcher?.Dispose();
        _parentDirectoryWatcher = null;

        _projectDirectoryWatcher?.Dispose();
        _projectDirectoryWatcher = null;

        _sessionFileWatcher?.Dispose();
        _sessionFileWatcher = null;
    }

    private void ResetStateForStart()
    {
        _sessionFilePath = null;
        _currentSessionId = null;

        _trackedFiles.Clear();
        _fileReadOffsets.Clear();
        _fileTailBytes.Clear();

        _seenKeys.Clear();
        while (_seenOrder.TryDequeue(out _)) { }
    }

    private void ResetStateForStop()
    {
        _trackedFiles.Clear();
        _fileReadOffsets.Clear();
        _fileTailBytes.Clear();

        _seenKeys.Clear();
        while (_seenOrder.TryDequeue(out _)) { }

        // keep _projectDirectory as-is (handy for restart), but you can null it if you prefer
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _isMonitoring = false;

        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;

        CleanupWatchers();

        // Best effort wait for polling to stop
        try { _pollingTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        _pollingTask = null;

        foreach (var sem in _fileLocks.Values)
            sem.Dispose();
        _fileLocks.Clear();

        _rawSubject.OnCompleted();
        _rawSubject.Dispose();
    }
}
