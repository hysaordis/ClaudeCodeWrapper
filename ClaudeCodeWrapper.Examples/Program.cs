using ClaudeCodeWrapper;
using ClaudeCodeWrapper.Models;

Console.WriteLine("ClaudeCodeWrapper SDK - Examples\n");

// 1. Initialize (checks if Claude Code is installed)
ClaudeCode claude;
try
{
    claude = ClaudeCode.Initialize(new ClaudeCodeOptions
    {
        WorkingDirectory = Directory.GetCurrentDirectory()
    });
    Console.WriteLine("Claude Code initialized successfully.\n");
}
catch (ClaudeNotInstalledException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return;
}

while (true)
{
    Console.WriteLine("Choose example:");
    Console.WriteLine("  1. SendAsync - Simple message");
    Console.WriteLine("  2. SendWithResponseAsync - Message with metrics");
    Console.WriteLine("  3. StreamAsync - Real-time activity monitoring");
    Console.WriteLine("  4. StreamWithResponseAsync - Full monitoring + metrics");
    Console.WriteLine("  ─────────────────────────────────────────────────");
    Console.WriteLine("  5. Permission Modes - List all available modes");
    Console.WriteLine("  6. Plan Mode - Read-only analysis (no execution)");
    Console.WriteLine("  7. AcceptEdits Mode - Auto-accept file edits");
    Console.WriteLine("  8. BypassPermissions Mode - YOLO mode (no prompts)");
    Console.WriteLine("  ─────────────────────────────────────────────────");
    Console.WriteLine("  9. Check Usage - View current usage limits");
    Console.WriteLine(" 10. Rate Limit Handling - Handle 429 errors");
    Console.WriteLine("  0. Exit");
    Console.Write("\nOption: ");

    var choice = Console.ReadLine()?.Trim();
    if (choice == "0") break;

    Console.WriteLine();

    try
    {
        switch (choice)
        {
            case "1":
                await Example_SendAsync(claude);
                break;
            case "2":
                await Example_SendWithResponseAsync(claude);
                break;
            case "3":
                await Example_StreamAsync(claude);
                break;
            case "4":
                await Example_StreamWithResponseAsync(claude);
                break;
            case "5":
                Example_ListPermissionModes();
                break;
            case "6":
                await Example_PlanMode();
                break;
            case "7":
                await Example_AcceptEditsMode();
                break;
            case "8":
                await Example_BypassPermissionsMode();
                break;
            case "9":
                await Example_CheckUsage();
                break;
            case "10":
                await Example_RateLimitHandling(claude);
                break;
            default:
                Console.WriteLine("Invalid option");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError: {ex.Message}");
    }

    Console.WriteLine("\nPress any key to continue...");
    Console.ReadKey();
    Console.Clear();
}

// Example 1: Simple send
async Task Example_SendAsync(ClaudeCode client)
{
    Console.WriteLine("=== SendAsync ===\n");
    Console.Write("Enter prompt: ");
    var prompt = Console.ReadLine() ?? "Hello!";

    Console.WriteLine("\nSending...\n");
    var response = await client.SendAsync(prompt);

    Console.WriteLine($"Response:\n{response}");
}

// Example 2: Send with response metrics
async Task Example_SendWithResponseAsync(ClaudeCode client)
{
    Console.WriteLine("=== SendWithResponseAsync ===\n");
    Console.Write("Enter prompt: ");
    var prompt = Console.ReadLine() ?? "Explain C# in one sentence";

    Console.WriteLine("\nSending...\n");
    var response = await client.SendWithResponseAsync(prompt);

    Console.WriteLine($"Response: {response.Content}\n");
    Console.WriteLine($"Model:     {response.Model}");
    Console.WriteLine($"Session:   {response.SessionId}");
    Console.WriteLine($"Duration:  {response.DurationMs}ms");
    if (response.Tokens != null)
    {
        Console.WriteLine($"Tokens In: {response.Tokens.Input}");
        Console.WriteLine($"Tokens Out:{response.Tokens.Output}");
    }
}

// Example 3: Stream with real-time activities
async Task Example_StreamAsync(ClaudeCode client)
{
    Console.WriteLine("=== StreamAsync ===\n");
    Console.Write("Enter prompt (try 'list files in current directory'): ");
    var prompt = Console.ReadLine() ?? "List files in the current directory";

    Console.WriteLine("\nExecuting with real-time monitoring...\n");

    var response = await client.StreamAsync(
        prompt,
        activity =>
        {
            Console.WriteLine($"╔══════════════════════════════════════════════════════════════");
            Console.WriteLine($"║ Type: {activity.Type}");

            if (activity.IsSubAgent)
                Console.WriteLine($"║ Agent: {activity.AgentId}");

            if (!string.IsNullOrEmpty(activity.Tool))
                Console.WriteLine($"║ Tool: {activity.Tool}");

            if (!string.IsNullOrEmpty(activity.Input))
            {
                Console.WriteLine($"║ Input:");
                foreach (var line in (activity.Input ?? "").Split('\n').Take(10))
                    Console.WriteLine($"║   {line}");
            }

            if (!string.IsNullOrEmpty(activity.Content))
            {
                Console.WriteLine($"║ Content:");
                foreach (var line in (activity.Content ?? "").Split('\n').Take(20))
                    Console.WriteLine($"║   {line}");
            }

            if (activity.Success.HasValue)
                Console.WriteLine($"║ Success: {activity.Success}");

            Console.WriteLine($"║ Time: {activity.Timestamp}");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════════\n");
        }
    );

    Console.WriteLine($"\n{'=',-60}");
    Console.WriteLine("FINAL RESPONSE:");
    Console.WriteLine($"{'=',-60}");
    Console.WriteLine(response);
}

// Example 4: Stream with response metrics
async Task Example_StreamWithResponseAsync(ClaudeCode client)
{
    Console.WriteLine("=== StreamWithResponseAsync ===\n");
    Console.Write("Enter prompt: ");
    var prompt = Console.ReadLine() ?? "Search for TODO comments in this project";

    Console.WriteLine("\nExecuting with monitoring + metrics...\n");

    var response = await client.StreamWithResponseAsync(
        prompt,
        activity =>
        {
            var icon = activity.Type switch
            {
                "tool_call" => "[TOOL]",
                "tool_result" => activity.Success == true ? "[OK]" : "[ERR]",
                "thought" => "[THINK]",
                _ => "[?]"
            };

            // Show sub-agent activities
            var prefix = activity.IsSubAgent ? $"[Agent:{activity.AgentId}] " : "";
            Console.WriteLine($"  {prefix}{icon} {activity.Summary}");
        }
    );

    Console.WriteLine($"\nResponse: {Truncate(response.Content, 200)}");
    Console.WriteLine($"\nMetrics:");
    Console.WriteLine($"  Model:     {response.Model}");
    Console.WriteLine($"  Duration:  {response.DurationMs}ms");
    if (response.Tokens != null)
    {
        Console.WriteLine($"  Tokens In: {response.Tokens.Input}");
        Console.WriteLine($"  Tokens Out:{response.Tokens.Output}");
    }
}

string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "...";

// Example 5: List all permission modes
void Example_ListPermissionModes()
{
    Console.WriteLine("=== Permission Modes ===\n");
    Console.WriteLine("Available permission modes for Claude Code:\n");

    foreach (var mode in PermissionMode.All)
    {
        Console.WriteLine($"  {mode.Value,-20} - {mode.DisplayName}");
        Console.WriteLine($"  {"",-20}   {mode.Description}\n");
    }

    Console.WriteLine("Usage example:");
    Console.WriteLine("  var claude = ClaudeCode.Initialize(new ClaudeCodeOptions");
    Console.WriteLine("  {");
    Console.WriteLine("      PermissionMode = PermissionMode.AcceptEdits");
    Console.WriteLine("  });");
}

// Example 6: Plan Mode - Read-only analysis
async Task Example_PlanMode()
{
    Console.WriteLine("=== Plan Mode ===\n");
    Console.WriteLine("Plan mode allows Claude to analyze and read but NOT modify files or execute commands.\n");

    var planClaude = ClaudeCode.Initialize(new ClaudeCodeOptions
    {
        WorkingDirectory = Directory.GetCurrentDirectory(),
        PermissionMode = PermissionMode.Plan
    });

    Console.Write("Enter prompt (try 'analyze this project structure and suggest improvements'): ");
    var prompt = Console.ReadLine() ?? "Analyze this project and suggest improvements";

    Console.WriteLine("\nExecuting in PLAN mode (read-only)...\n");

    var response = await planClaude.StreamAsync(
        prompt,
        activity =>
        {
            var icon = activity.Type switch
            {
                "tool_call" => "🔍",
                "tool_result" => activity.Success == true ? "✓" : "✗",
                "thought" => "💭",
                _ => "•"
            };
            Console.WriteLine($"  {icon} {activity.Summary}");
        }
    );

    Console.WriteLine($"\n{"═",-60}");
    Console.WriteLine("ANALYSIS RESULT:");
    Console.WriteLine($"{"═",-60}");
    Console.WriteLine(response);
}

// Example 7: AcceptEdits Mode - Auto-accept file modifications
async Task Example_AcceptEditsMode()
{
    Console.WriteLine("=== AcceptEdits Mode ===\n");
    Console.WriteLine("AcceptEdits mode auto-accepts file modifications without prompting.\n");
    Console.WriteLine("⚠️  WARNING: Claude will modify files without asking!\n");

    Console.Write("Are you sure you want to continue? (y/N): ");
    if (Console.ReadLine()?.Trim().ToLower() != "y")
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    var editsClaude = ClaudeCode.Initialize(new ClaudeCodeOptions
    {
        WorkingDirectory = Directory.GetCurrentDirectory(),
        PermissionMode = PermissionMode.AcceptEdits
    });

    Console.Write("\nEnter prompt (try 'create a file called test-output.txt with hello world'): ");
    var prompt = Console.ReadLine() ?? "Create a file called test-output.txt with the text 'Hello from AcceptEdits mode!'";

    Console.WriteLine("\nExecuting in ACCEPT EDITS mode...\n");

    var response = await editsClaude.StreamAsync(
        prompt,
        activity =>
        {
            var icon = activity.Type switch
            {
                "tool_call" => "🔧",
                "tool_result" => activity.Success == true ? "✅" : "❌",
                "thought" => "💭",
                _ => "•"
            };

            if (activity.Tool == "Write" || activity.Tool == "Edit")
                Console.WriteLine($"  {icon} [AUTO-ACCEPTED] {activity.Summary}");
            else
                Console.WriteLine($"  {icon} {activity.Summary}");
        }
    );

    Console.WriteLine($"\n{"═",-60}");
    Console.WriteLine("RESULT:");
    Console.WriteLine($"{"═",-60}");
    Console.WriteLine(response);
}

// Example 8: BypassPermissions Mode - YOLO mode
async Task Example_BypassPermissionsMode()
{
    Console.WriteLine("=== BypassPermissions Mode (YOLO) ===\n");
    Console.WriteLine("⚠️  DANGER: This mode bypasses ALL permission checks!");
    Console.WriteLine("   Claude can execute any command, modify any file, without prompting.\n");
    Console.WriteLine("   Only use in controlled environments (CI/CD, containers, etc.)\n");

    Console.Write("Type 'YOLO' to confirm: ");
    if (Console.ReadLine()?.Trim() != "YOLO")
    {
        Console.WriteLine("Cancelled. Good choice!");
        return;
    }

    var yoloClaude = ClaudeCode.Initialize(new ClaudeCodeOptions
    {
        WorkingDirectory = Directory.GetCurrentDirectory(),
        PermissionMode = PermissionMode.BypassPermissions
    });

    Console.Write("\nEnter prompt (be careful!): ");
    var prompt = Console.ReadLine() ?? "List the current directory";

    Console.WriteLine("\nExecuting in BYPASS PERMISSIONS mode...\n");

    var response = await yoloClaude.StreamAsync(
        prompt,
        activity =>
        {
            var icon = activity.Type switch
            {
                "tool_call" => "⚡",
                "tool_result" => activity.Success == true ? "✅" : "❌",
                "thought" => "💭",
                _ => "•"
            };
            Console.WriteLine($"  {icon} [NO-PROMPT] {activity.Summary}");
        }
    );

    Console.WriteLine($"\n{"═",-60}");
    Console.WriteLine("RESULT:");
    Console.WriteLine($"{"═",-60}");
    Console.WriteLine(response);
}

// Example 9: Check Usage Limits
async Task Example_CheckUsage()
{
    Console.WriteLine("=== Check Usage Limits ===\n");
    Console.WriteLine("Fetching current usage from Claude API...\n");

    // Create a client with usage monitoring enabled
    using var monitorClient = ClaudeCode.Initialize(new ClaudeCodeOptions
    {
        WorkingDirectory = Directory.GetCurrentDirectory(),
        EnableUsageMonitoring = true
    });

    var usage = await monitorClient.GetUsageAsync();

    if (usage == null)
    {
        Console.WriteLine("Unable to fetch usage data.");
        Console.WriteLine("This may be because your OAuth token doesn't have the 'user:profile' scope.");
        Console.WriteLine("\nNote: Usage monitoring is only available for Claude Pro/Max subscriptions.");
        return;
    }

    Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
    Console.WriteLine("│                    USAGE LIMITS                         │");
    Console.WriteLine("├─────────────────────────────────────────────────────────┤");

    // Session (5-hour) limit
    Console.WriteLine("│  SESSION (5-hour window):                               │");
    Console.WriteLine($"│    Utilization: {usage.FiveHour.Utilization,5:F1}%                                 │");
    PrintProgressBar(usage.FiveHour.Utilization);
    if (usage.FiveHour.ResetsAt.HasValue)
    {
        var timeUntil = usage.FiveHour.TimeUntilReset;
        Console.WriteLine($"│    Resets at:   {usage.FiveHour.ResetsAt:HH:mm} ({timeUntil?.Hours}h {timeUntil?.Minutes}m remaining)        │");
    }

    Console.WriteLine("├─────────────────────────────────────────────────────────┤");

    // Weekly limit
    Console.WriteLine("│  WEEKLY (7-day window):                                 │");
    Console.WriteLine($"│    Utilization: {usage.SevenDay.Utilization,5:F1}%                                 │");
    PrintProgressBar(usage.SevenDay.Utilization);
    if (usage.SevenDay.ResetsAt.HasValue)
    {
        Console.WriteLine($"│    Resets at:   {usage.SevenDay.ResetsAt:ddd MMM dd, HH:mm}                      │");
    }

    Console.WriteLine("└─────────────────────────────────────────────────────────┘");

    // Warnings
    if (usage.FiveHour.IsApproachingLimit)
    {
        Console.WriteLine("\n⚠️  WARNING: Session limit is approaching (>80%)!");
    }
    if (usage.SevenDay.IsApproachingLimit)
    {
        Console.WriteLine("\n⚠️  WARNING: Weekly limit is approaching (>80%)!");
    }

    void PrintProgressBar(double percentage)
    {
        var filled = (int)(percentage / 2.5);
        var empty = 40 - filled;
        var color = percentage switch
        {
            >= 90 => ConsoleColor.Red,
            >= 70 => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };

        Console.Write("│    [");
        Console.ForegroundColor = color;
        Console.Write(new string('█', filled));
        Console.ResetColor();
        Console.Write(new string('░', empty));
        Console.WriteLine("]    │");
    }
}

// Example 10: Rate Limit Handling
async Task Example_RateLimitHandling(ClaudeCode client)
{
    Console.WriteLine("=== Rate Limit Handling ===\n");
    Console.WriteLine("This example demonstrates how to handle rate limit exceptions.");
    Console.WriteLine("The wrapper throws RateLimitException on 429 errors.\n");
    Console.WriteLine("The consumer is responsible for implementing retry logic.\n");

    Console.Write("Enter prompt: ");
    var prompt = Console.ReadLine() ?? "What is 2+2?";

    Console.WriteLine("\nSending request...\n");

    try
    {
        var response = await client.SendAsync(prompt);
        Console.WriteLine($"Response:\n{response}");
    }
    catch (RateLimitException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Rate limit exceeded (429)!");
        Console.WriteLine($"   Message:    {ex.Message}");
        Console.WriteLine($"   Request ID: {ex.RequestId ?? "N/A"}");
        Console.WriteLine($"   Error Type: {ex.ErrorType ?? "N/A"}");
        Console.ResetColor();

        Console.WriteLine("\nTo handle this, you could implement retry logic like:");
        Console.WriteLine("  - Wait and retry after a delay");
        Console.WriteLine("  - Check usage limits before sending (GetUsageAsync)");
        Console.WriteLine("  - Implement exponential backoff");
    }
    catch (OverloadedException ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"API overloaded (529)!");
        Console.WriteLine($"   Message:     {ex.Message}");
        Console.WriteLine($"   Retry after: {ex.RetryAfter?.TotalSeconds ?? 60}s");
        Console.ResetColor();
    }
}
