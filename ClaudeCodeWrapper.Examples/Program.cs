using ClaudeCodeWrapper;
using ClaudeCodeWrapper.Examples;
using ClaudeCodeWrapper.Models;

Console.WriteLine("ClaudeCodeWrapper SDK - Examples\n");

// Initialize (checks if Claude Code is installed)
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
    Console.WriteLine("  1. Quick Start - Simple SendAsync example");
    Console.WriteLine("  2. Full Feature Test - Complete SDK test with all features");
    Console.WriteLine("  3. Check Usage Limits - View current API usage");
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
                await Example_QuickStart(claude);
                break;
            case "2":
                await FullFeatureTestExample.RunAsync();
                break;
            case "3":
                await Example_CheckUsage();
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

// Example 1: Quick Start - Simple SendAsync
async Task Example_QuickStart(ClaudeCode client)
{
    Console.WriteLine("=== Quick Start ===\n");
    Console.WriteLine("This is the simplest way to use the SDK.\n");

    Console.Write("Enter prompt: ");
    var prompt = Console.ReadLine() ?? "Hello!";

    Console.WriteLine("\nSending...\n");
    var response = await client.SendAsync(prompt);

    Console.WriteLine($"Response:\n{response}");
}

// Example 3: Check Usage Limits
async Task Example_CheckUsage()
{
    Console.WriteLine("=== Check Usage Limits ===\n");
    Console.WriteLine("Fetching current usage from Claude API...\n");

    using var monitorClient = ClaudeCode.Initialize(new ClaudeCodeOptions
    {
        WorkingDirectory = Directory.GetCurrentDirectory(),
        EnableUsageMonitoring = true
    });

    var usage = await monitorClient.GetUsageAsync();

    if (usage == null)
    {
        Console.WriteLine("Unable to fetch usage data.");
        Console.WriteLine("This requires Claude Pro/Max subscription with 'user:profile' scope.");
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

    if (usage.FiveHour.IsApproachingLimit)
        Console.WriteLine("\nWARNING: Session limit approaching (>80%)!");
    if (usage.SevenDay.IsApproachingLimit)
        Console.WriteLine("\nWARNING: Weekly limit approaching (>80%)!");

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
