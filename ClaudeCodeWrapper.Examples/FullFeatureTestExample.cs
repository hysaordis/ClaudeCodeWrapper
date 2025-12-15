// FullFeatureTestExample.cs
// COMPLETE test of all SDK features:
// - Web Search / Web Fetch monitoring
// - File operations (Write, Edit, Read, Bash)
// - Full session activity monitoring with tool correlation
// - Token usage with cache stats
// - File history tracking
// - Todos tracking
// - Sub-agents tracking
// - Summaries
// - Navigation (threads, children, root messages)
// - Request/Response metadata
// - Session timestamps and paths

using ClaudeCodeWrapper;
using ClaudeCodeWrapper.Models;
using ClaudeCodeWrapper.Models.Blocks;
using ClaudeCodeWrapper.Models.Records;

namespace ClaudeCodeWrapper.Examples;

public static class FullFeatureTestExample
{
    // Track pending tool calls for correlation
    private static readonly Dictionary<string, ToolCallInfo> _pendingToolCalls = new();

    /// <summary>
    /// Runs the comprehensive test that exercises ALL SDK features.
    /// </summary>
    public static async Task RunAsync()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       COMPLETE FEATURE TEST - ClaudeCodeWrapper SDK v1.2.0          ║");
        Console.WriteLine("║                                                                      ║");
        Console.WriteLine("║  Testing: ALL features including correlation, file history, todos   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝\n");

        // Initialize Claude Code with monitoring enabled
        ClaudeCode claude;
        try
        {
            claude = ClaudeCode.Initialize(new ClaudeCodeOptions
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                PermissionMode = PermissionMode.BypassPermissions,
                EnableUsageMonitoring = true
            });
            Console.WriteLine("[OK] Claude Code initialized\n");
        }
        catch (ClaudeNotInstalledException ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            return;
        }

        // Test file path
        var testFilePath = Path.Combine(Directory.GetCurrentDirectory(), "test_features_output.txt");

        // Activity tracker
        var tracker = new ActivityTracker();

        Console.WriteLine("┌────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│                    EXECUTING COMPREHENSIVE TEST                    │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────────┘\n");

        // The prompt that will trigger all features
        var prompt = $@"I need you to perform several tasks in sequence to test the SDK:

1. FIRST: Search the web for 'C# 12 new features 2024' and give me a brief summary (tests WebSearch)

2. SECOND: Fetch the content from https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12 and summarize (tests WebFetch)

3. THIRD: Create a new file at '{testFilePath}' with:
   - Header 'SDK Feature Test Results'
   - Today's date
   - Placeholder for web search results
   - Placeholder for web fetch results
   (tests Write tool)

4. FOURTH: Edit the file to replace placeholders with actual summaries (tests Edit tool)

5. FIFTH: Run 'cat {testFilePath}' to display the file contents (tests Bash tool)

Execute all tasks in order.";

        Console.WriteLine("[PROMPT] Starting comprehensive test with FULL monitoring...\n");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                     REAL-TIME ACTIVITY STREAM");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════\n");

        // Execute with streaming to monitor all activities in real-time
        var response = await claude.StreamRecordsWithResponseAsync(
            prompt,
            record => ProcessRecordComplete(record, tracker));

        // Load the full session for complete statistics
        Session? session = null;
        if (!string.IsNullOrEmpty(response.SessionId))
        {
            session = await claude.LoadSessionAsync(response.SessionId);
        }

        // Display comprehensive results
        Console.WriteLine("\n");
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                       COMPLETE TEST RESULTS                          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝\n");

        // 1. Activity Statistics with correlations
        PrintActivityStats(tracker);

        // 2. Tool Correlations
        PrintToolCorrelations(tracker);

        // 3. Full Session Statistics
        if (session != null)
        {
            PrintFullSessionStats(session);
        }

        // 4. Response Metrics
        PrintResponseMetrics(response);

        // 5. Final Output
        Console.WriteLine("\n┌────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│                       FINAL RESPONSE                               │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
        Console.WriteLine(Truncate(response.Content, 500));

        // Cleanup
        if (File.Exists(testFilePath))
        {
            Console.WriteLine($"\n[CLEANUP] Test file: {testFilePath}");
            Console.Write("Delete? (y/N): ");
            if (Console.ReadLine()?.Trim().ToLower() == "y")
            {
                File.Delete(testFilePath);
                Console.WriteLine("[OK] Deleted");
            }
        }

        Console.WriteLine("\n[DONE] Complete feature test finished!");
    }

    /// <summary>
    /// Process each session record with COMPLETE tracking including correlation.
    /// </summary>
    private static void ProcessRecordComplete(SessionRecord record, ActivityTracker tracker)
    {
        tracker.TotalRecords++;
        tracker.RecordTypes.TryAdd(record.GetType().Name, 0);
        tracker.RecordTypes[record.GetType().Name]++;

        switch (record)
        {
            case AssistantRecord assistant:
                ProcessAssistantRecord(assistant, tracker);
                break;

            case UserRecord user:
                ProcessUserRecord(user, tracker);
                break;

            case SummaryRecord summary:
                tracker.Summaries++;
                tracker.SummaryContents.Add(Truncate(summary.Summary, 100) ?? "");
                PrintActivity("SUMMARY", $"Context summarized: {Truncate(summary.Summary, 50)}", ConsoleColor.Yellow);
                break;

            case SystemRecord system:
                tracker.SystemRecords++;
                PrintActivity("SYSTEM", Truncate(system.Content, 60), ConsoleColor.DarkGray);
                break;

            case FileHistorySnapshotRecord fileHistory:
                tracker.FileHistorySnapshots++;
                var fileCount = fileHistory.Snapshot.TrackedFileBackups.Count;
                foreach (var (path, backup) in fileHistory.Snapshot.TrackedFileBackups)
                {
                    tracker.ModifiedFiles.Add(path);
                    tracker.FileBackups.Add(new FileBackupRecord
                    {
                        OriginalPath = path,
                        BackupFileName = backup.BackupFileName,
                        Version = backup.Version,
                        BackupTime = backup.BackupTime
                    });
                }
                PrintActivity("FILE_HIST", $"{fileCount} file(s) backed up", ConsoleColor.Magenta);
                break;
        }
    }

    private static void ProcessAssistantRecord(AssistantRecord assistant, ActivityTracker tracker)
    {
        tracker.AssistantRecords++;

        // Track metadata
        if (assistant.RequestId != null)
            tracker.RequestIds.Add(assistant.RequestId);

        if (assistant.Message.StopReason != null)
            tracker.StopReasons.TryAdd(assistant.Message.StopReason, 0);
        tracker.StopReasons[assistant.Message.StopReason ?? "null"]++;

        if (assistant.Message.ContextManagement?.Truncated == true)
            tracker.ContextTruncations++;

        // Track token usage
        if (assistant.Message.Usage != null)
        {
            var usage = assistant.Message.Usage;
            tracker.TotalInputTokens += usage.InputTokens;
            tracker.TotalOutputTokens += usage.OutputTokens;
            tracker.TotalCacheReadTokens += usage.CacheReadInputTokens;
            tracker.TotalCacheCreationTokens += usage.CacheCreationInputTokens;

            if (usage.ServerToolUse != null)
            {
                tracker.WebSearchRequests += usage.ServerToolUse.WebSearchRequests;
                tracker.WebFetchRequests += usage.ServerToolUse.WebFetchRequests;
            }
        }

        // Track model
        tracker.Models.TryAdd(assistant.Message.Model, 0);
        tracker.Models[assistant.Message.Model]++;

        // Track content blocks
        foreach (var block in assistant.Message.Content)
        {
            switch (block)
            {
                case ToolUseBlock toolUse:
                    tracker.ToolCalls++;
                    tracker.ToolUsage.TryAdd(toolUse.Name, 0);
                    tracker.ToolUsage[toolUse.Name]++;

                    // Store for correlation
                    var toolInfo = new ToolCallInfo
                    {
                        Id = toolUse.Id,
                        Name = toolUse.Name,
                        Input = toolUse.Input?.GetRawText(),
                        Timestamp = assistant.Timestamp ?? DateTime.Now
                    };
                    _pendingToolCalls[toolUse.Id] = toolInfo;
                    tracker.AllToolCalls.Add(toolInfo);

                    PrintActivity("TOOL", $"[{toolUse.Id[^8..]}] {toolUse.Name}", ConsoleColor.Cyan);
                    break;

                case TextBlock text:
                    tracker.TextBlocks++;
                    PrintActivity("TEXT", Truncate(text.Text, 60), ConsoleColor.White);
                    break;

                case ThinkingBlock thinking:
                    tracker.ThinkingBlocks++;
                    PrintActivity("THINK", Truncate(thinking.Thinking, 60), ConsoleColor.DarkGray);
                    break;
            }
        }
    }

    private static void ProcessUserRecord(UserRecord user, ActivityTracker tracker)
    {
        tracker.UserRecords++;

        // Track todos from this record
        if (user.Todos != null && user.Todos.Count > 0)
        {
            tracker.TodoSnapshots++;
            tracker.LatestTodos = user.Todos.ToList();
        }

        // Track tool results with correlation
        if (user.Message.ContentBlocks != null)
        {
            foreach (var block in user.Message.ContentBlocks)
            {
                if (block is ToolResultBlock toolResult)
                {
                    tracker.ToolResults++;

                    // Correlate with tool call
                    if (_pendingToolCalls.TryGetValue(toolResult.ToolUseId, out var toolCall))
                    {
                        toolCall.ResultContent = toolResult.Content;
                        toolCall.IsError = toolResult.IsError;
                        toolCall.ResultTimestamp = user.Timestamp ?? DateTime.Now;
                        toolCall.Duration = toolCall.ResultTimestamp - toolCall.Timestamp;
                        _pendingToolCalls.Remove(toolResult.ToolUseId);
                    }

                    // Track tool use result metadata
                    if (user.ToolUseResult != null)
                    {
                        if (!string.IsNullOrEmpty(user.ToolUseResult.Stdout))
                            tracker.ToolsWithStdout++;
                        if (!string.IsNullOrEmpty(user.ToolUseResult.Stderr))
                            tracker.ToolsWithStderr++;
                        if (user.ToolUseResult.Interrupted)
                            tracker.ToolsInterrupted++;
                        if (user.ToolUseResult.IsImage)
                            tracker.ToolsWithImages++;
                    }

                    var status = toolResult.IsError ? "ERR" : "OK";
                    var color = toolResult.IsError ? ConsoleColor.Red : ConsoleColor.Green;
                    PrintActivity($"RESULT[{status}]", $"[{toolResult.ToolUseId[^8..]}] {Truncate(toolResult.Content, 40)}", color);
                }
            }
        }
        else if (user.Message.ContentString != null)
        {
            // Plain user message
            PrintActivity("USER", Truncate(user.Message.ContentString, 60), ConsoleColor.Blue);
        }
    }

    private static void PrintActivity(string type, string? content, ConsoleColor color)
    {
        Console.Write($"  [{DateTime.Now:HH:mm:ss.fff}] ");
        Console.ForegroundColor = color;
        Console.Write($"[{type,-12}] ");
        Console.ResetColor();
        Console.WriteLine(content ?? "");
    }

    private static void PrintActivityStats(ActivityTracker tracker)
    {
        Console.WriteLine("┌────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│                     ACTIVITY STATISTICS                            │");
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine($"│  Total Records:      {tracker.TotalRecords,-10}                              │");
        Console.WriteLine("│  RECORD TYPES:                                                     │");
        foreach (var (type, count) in tracker.RecordTypes.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"│    {type,-25} : {count,-5}                            │");
        }
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine($"│  Tool Calls:         {tracker.ToolCalls,-10}                              │");
        Console.WriteLine($"│  Tool Results:       {tracker.ToolResults,-10}                              │");
        Console.WriteLine($"│  Text Blocks:        {tracker.TextBlocks,-10}                              │");
        Console.WriteLine($"│  Thinking Blocks:    {tracker.ThinkingBlocks,-10}                              │");
        Console.WriteLine($"│  Summaries:          {tracker.Summaries,-10}                              │");
        Console.WriteLine($"│  File History Snaps: {tracker.FileHistorySnapshots,-10}                              │");
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  TOOL USAGE:                                                       │");
        foreach (var (tool, count) in tracker.ToolUsage.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"│    {tool,-25} : {count,-5}                            │");
        }
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  SERVER TOOL USE (from API):                                       │");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"│    WebSearchRequests: {tracker.WebSearchRequests,-10}                             │");
        Console.WriteLine($"│    WebFetchRequests:  {tracker.WebFetchRequests,-10}                             │");
        Console.ResetColor();
        Console.WriteLine("│  CLIENT TOOL CALLS (from ToolUseBlock):                            │");
        var ws = tracker.ToolUsage.GetValueOrDefault("WebSearch", 0);
        var wf = tracker.ToolUsage.GetValueOrDefault("WebFetch", 0);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"│    WebSearch:         {ws,-10}                             │");
        Console.WriteLine($"│    WebFetch:          {wf,-10}                             │");
        Console.ResetColor();
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  TOOL RESULT METADATA:                                             │");
        Console.WriteLine($"│    With stdout:       {tracker.ToolsWithStdout,-10}                             │");
        Console.WriteLine($"│    With stderr:       {tracker.ToolsWithStderr,-10}                             │");
        Console.WriteLine($"│    Interrupted:       {tracker.ToolsInterrupted,-10}                             │");
        Console.WriteLine($"│    With images:       {tracker.ToolsWithImages,-10}                             │");
        Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
    }

    private static void PrintToolCorrelations(ActivityTracker tracker)
    {
        Console.WriteLine("\n┌────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│                     TOOL CALL CORRELATIONS                         │");
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");

        var completed = tracker.AllToolCalls.Where(t => t.ResultTimestamp != default).ToList();
        var pending = tracker.AllToolCalls.Where(t => t.ResultTimestamp == default).ToList();

        Console.WriteLine($"│  Completed:          {completed.Count,-10}                              │");
        Console.WriteLine($"│  Pending/Uncorr.:    {pending.Count,-10}                              │");

        if (completed.Any())
        {
            Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│  TOOL CALL → RESULT (with duration):                               │");
            foreach (var tool in completed.Take(10))
            {
                var status = tool.IsError ? "ERR" : "OK";
                var duration = tool.Duration.TotalMilliseconds;
                Console.WriteLine($"│    {tool.Name,-15} [{tool.Id[^6..]}] → [{status}] {duration,6:F0}ms         │");
            }
            if (completed.Count > 10)
                Console.WriteLine($"│    ... and {completed.Count - 10} more                                          │");

            var avgDuration = completed.Average(t => t.Duration.TotalMilliseconds);
            var maxDuration = completed.Max(t => t.Duration.TotalMilliseconds);
            Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine($"│  Avg duration:       {avgDuration,8:F0} ms                              │");
            Console.WriteLine($"│  Max duration:       {maxDuration,8:F0} ms                              │");
        }

        Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
    }

    private static void PrintFullSessionStats(Session session)
    {
        Console.WriteLine("\n┌────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│                      FULL SESSION STATISTICS                       │");
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");

        // Basic Info
        Console.WriteLine("│  SESSION INFO:                                                     │");
        Console.WriteLine($"│    ID:               {Truncate(session.Id, 36),-36}   │");
        Console.WriteLine($"│    Slug:             {session.Slug ?? "N/A",-36}   │");
        Console.WriteLine($"│    Version:          {session.Version ?? "N/A",-36}   │");
        Console.WriteLine($"│    Git Branch:       {session.GitBranch ?? "N/A",-36}   │");

        // Paths
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  PATHS:                                                            │");
        Console.WriteLine($"│    Cwd:              {Truncate(session.Cwd, 45),-45}│");
        Console.WriteLine($"│    Session File:     {Truncate(session.SessionFilePath, 45),-45}│");
        Console.WriteLine($"│    Debug Log:        {(session.DebugLogPath != null ? "Available" : "N/A"),-36}   │");

        // Timestamps
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  TIMESTAMPS:                                                       │");
        Console.WriteLine($"│    Started:          {session.StartedAt?.ToString("HH:mm:ss") ?? "N/A",-36}   │");
        Console.WriteLine($"│    Last Activity:    {session.LastActivityAt?.ToString("HH:mm:ss") ?? "N/A",-36}   │");
        if (session.StartedAt.HasValue && session.LastActivityAt.HasValue)
        {
            var duration = session.LastActivityAt.Value - session.StartedAt.Value;
            Console.WriteLine($"│    Total Duration:   {duration.TotalSeconds:F1} seconds{"",-25}│");
        }

        // Messages
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  MESSAGES:                                                         │");
        Console.WriteLine($"│    Total:            {session.MessageCount,-10}                              │");
        Console.WriteLine($"│    User:             {session.UserRecords.Count(),-10}                              │");
        Console.WriteLine($"│    Assistant:        {session.AssistantRecords.Count(),-10}                              │");
        Console.WriteLine($"│    Root Messages:    {session.RootMessages.Count(),-10}                              │");

        // Tokens
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  TOKEN USAGE:                                                      │");
        Console.WriteLine($"│    Total:            {session.TotalTokens,-10}                              │");
        Console.WriteLine($"│    Input:            {session.TotalInputTokens,-10}                              │");
        Console.WriteLine($"│    Output:           {session.TotalOutputTokens,-10}                              │");
        Console.WriteLine($"│    Cache Read:       {session.TotalCacheReadTokens,-10}                              │");
        var cacheRate = $"{session.AverageCacheHitRate:P1}";
        Console.WriteLine($"│    Avg Cache Rate:   {cacheRate,-10}                              │");

        // Web tools
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  WEB TOOL USAGE:                                                   │");
        var webSearchCalls = session.ToolUsageCounts.GetValueOrDefault("WebSearch", 0);
        var webFetchCalls = session.ToolUsageCounts.GetValueOrDefault("WebFetch", 0);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"│    WebSearch calls:  {webSearchCalls,-10}  (ToolUseBlock)              │");
        Console.WriteLine($"│    WebFetch calls:   {webFetchCalls,-10}  (ToolUseBlock)              │");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"│    ServerToolUse WS: {session.TotalWebSearchRequests,-10}  (API usage)                 │");
        Console.WriteLine($"│    ServerToolUse WF: {session.TotalWebFetchRequests,-10}  (API usage)                 │");
        Console.ResetColor();

        // Models
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  MODELS USED:                                                      │");
        foreach (var (model, count) in session.ModelUsage)
        {
            Console.WriteLine($"│    {Truncate(model, 30),-30} : {count,-5}                      │");
        }

        // All tools
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  ALL TOOL USAGE:                                                   │");
        foreach (var (tool, count) in session.ToolUsageCounts.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"│    {tool,-25} : {count,-5}                            │");
        }

        // Sub-agents
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  SUB-AGENTS:                                                       │");
        Console.WriteLine($"│    Count:            {session.Agents.Count,-10}                              │");
        foreach (var agent in session.Agents.Take(5))
        {
            Console.WriteLine($"│    [{agent.AgentId}] Records: {agent.Records.Count,-5} Type: {agent.AgentType ?? "N/A",-10}│");
        }

        // Todos
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  TODOS:                                                            │");
        Console.WriteLine($"│    Count:            {session.Todos.Count,-10}                              │");
        foreach (var todo in session.Todos.Take(5))
        {
            var statusIcon = todo.Status switch
            {
                "completed" => "[✓]",
                "in_progress" => "[→]",
                _ => "[ ]"
            };
            Console.WriteLine($"│    {statusIcon} {Truncate(todo.Content, 50),-50}│");
        }
        if (session.Todos.Count > 5)
            Console.WriteLine($"│    ... and {session.Todos.Count - 5} more                                          │");

        // File History
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  FILE HISTORY:                                                     │");
        Console.WriteLine($"│    Snapshots:        {session.FileHistorySnapshots.Count(),-10}                              │");
        Console.WriteLine($"│    Backup Files:     {session.FileHistory.Count,-10}                              │");
        var modifiedFiles = session.ModifiedFiles.ToList();
        Console.WriteLine($"│    Modified Files:   {modifiedFiles.Count,-10}                              │");
        foreach (var file in modifiedFiles.Take(5))
        {
            Console.WriteLine($"│      {Truncate(file, 55),-55}│");
        }

        // Summaries
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine("│  SUMMARIES:                                                        │");
        var summaryCount = session.Summaries.Count();
        Console.WriteLine($"│    Count:            {summaryCount,-10}                              │");

        // Errors
        if (session.HasErrors)
        {
            Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"│  ERRORS:             {session.Errors.Count(),-10}                              │");
            foreach (var error in session.Errors.Take(3))
            {
                Console.WriteLine($"│    {error.Error ?? "Unknown error",-55}│");
            }
            Console.ResetColor();
        }

        Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
    }

    private static void PrintResponseMetrics(Response response)
    {
        Console.WriteLine("\n┌────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│                      RESPONSE METRICS                              │");
        Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        Console.WriteLine($"│  Model:              {response.Model ?? "N/A",-36}   │");
        Console.WriteLine($"│  Session ID:         {Truncate(response.SessionId, 36),-36}   │");
        Console.WriteLine($"│  Duration:           {response.DurationMs,-10} ms                           │");
        if (response.Tokens != null)
        {
            Console.WriteLine($"│  Input Tokens:       {response.Tokens.Input,-10}                              │");
            Console.WriteLine($"│  Output Tokens:      {response.Tokens.Output,-10}                              │");
        }
        Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
    }

    private static string Truncate(string? s, int max) =>
        s == null ? "" : s.Length <= max ? s : s[..max] + "...";

    /// <summary>
    /// Complete activity tracking with correlation support.
    /// </summary>
    private class ActivityTracker
    {
        // Record counts
        public int TotalRecords { get; set; }
        public int AssistantRecords { get; set; }
        public int UserRecords { get; set; }
        public int SystemRecords { get; set; }
        public int Summaries { get; set; }
        public int FileHistorySnapshots { get; set; }

        // Record types
        public Dictionary<string, int> RecordTypes { get; } = new();

        // Content blocks
        public int ToolCalls { get; set; }
        public int ToolResults { get; set; }
        public int TextBlocks { get; set; }
        public int ThinkingBlocks { get; set; }

        // Tool details
        public Dictionary<string, int> ToolUsage { get; } = new();
        public List<ToolCallInfo> AllToolCalls { get; } = new();

        // Tool result metadata
        public int ToolsWithStdout { get; set; }
        public int ToolsWithStderr { get; set; }
        public int ToolsInterrupted { get; set; }
        public int ToolsWithImages { get; set; }

        // Token usage
        public int TotalInputTokens { get; set; }
        public int TotalOutputTokens { get; set; }
        public int TotalCacheReadTokens { get; set; }
        public int TotalCacheCreationTokens { get; set; }

        // Server tool use
        public int WebSearchRequests { get; set; }
        public int WebFetchRequests { get; set; }

        // Metadata
        public List<string> RequestIds { get; } = new();
        public Dictionary<string, int> StopReasons { get; } = new();
        public Dictionary<string, int> Models { get; } = new();
        public int ContextTruncations { get; set; }

        // Summaries
        public List<string> SummaryContents { get; } = new();

        // Todos
        public int TodoSnapshots { get; set; }
        public List<TodoItem> LatestTodos { get; set; } = new();

        // File history
        public HashSet<string> ModifiedFiles { get; } = new();
        public List<FileBackupRecord> FileBackups { get; } = new();
    }

    private class ToolCallInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Input { get; set; }
        public DateTime Timestamp { get; set; }
        public string? ResultContent { get; set; }
        public bool IsError { get; set; }
        public DateTime ResultTimestamp { get; set; }
        public TimeSpan Duration { get; set; }
    }

    private record FileBackupRecord
    {
        public string OriginalPath { get; init; } = "";
        public string? BackupFileName { get; init; }
        public int Version { get; init; }
        public DateTime BackupTime { get; init; }

    }
}
