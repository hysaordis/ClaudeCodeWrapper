# ClaudeCodeWrapper

C# wrapper for **Claude Code CLI**.

## Requirements

- .NET 8.0+
- [Claude Code CLI](https://www.anthropic.com/claude-code) installed (`npm install -g @anthropic-ai/claude-code`)

## Installation

```bash
dotnet add package ClaudeCodeWrapper
```

## Quick Start

```csharp
using ClaudeCodeWrapper;

// Initialize
var claude = ClaudeCode.Initialize();

// Send a message
var response = await claude.SendAsync("Hello Claude!");
```

## Usage

### With Options

```csharp
var claude = ClaudeCode.Initialize(new ClaudeCodeOptions
{
    Model = "sonnet",
    WorkingDirectory = "/path/to/project",
    SystemPrompt = "You are a helpful assistant",
    PermissionMode = PermissionMode.AcceptEdits
});
```

### Stream Activities

Monitor tool calls and results in real-time:

```csharp
var response = await claude.StreamWithResponseAsync(
    "Refactor this code",
    activity => Console.WriteLine($"[{activity.Type}] {activity.Tool}: {activity.Summary}")
);
```

### With Metrics

```csharp
var response = await claude.SendWithResponseAsync("Explain this code");

Console.WriteLine($"Response: {response.Content}");
Console.WriteLine($"Tokens: {response.Tokens?.Input} in, {response.Tokens?.Output} out");
Console.WriteLine($"Session: {response.SessionId}");
```

### Session Management

```csharp
// Resume a previous session
var response = await claude.ResumeAsync("session-id", "Continue...");

// Continue the last session
var response = await claude.ContinueAsync("What were we discussing?");
```

## Permission Modes

| Mode | Description |
|------|-------------|
| `PermissionMode.Default` | Standard - asks permission for writes |
| `PermissionMode.Plan` | Read-only, no modifications |
| `PermissionMode.AcceptEdits` | Auto-accept file edits |
| `PermissionMode.BypassPermissions` | No permission prompts |

## Usage Monitoring

Track your API usage limits:

```csharp
var claude = ClaudeCode.Initialize(new ClaudeCodeOptions
{
    EnableUsageMonitoring = true
});

// Get current usage
var usage = await claude.GetUsageAsync();
if (usage != null)
{
    Console.WriteLine($"Session: {usage.FiveHour.Utilization}% used");
    Console.WriteLine($"Weekly:  {usage.SevenDay.Utilization}% used");
    Console.WriteLine($"Resets:  {usage.FiveHour.ResetsAt}");
}

// Check if within limits
if (await claude.IsWithinLimitsAsync(sessionThreshold: 90, weeklyThreshold: 80))
{
    await claude.SendAsync("Safe to send!");
}
```

## Error Handling

The wrapper throws specific exceptions for rate limit errors:

```csharp
try
{
    var response = await claude.SendAsync(prompt);
}
catch (RateLimitException ex)
{
    // HTTP 429 - Rate limit exceeded
    Console.WriteLine($"Rate limited: {ex.Message}");
    Console.WriteLine($"Request ID: {ex.RequestId}");
    // Implement your own retry logic
}
catch (OverloadedException ex)
{
    // HTTP 529 - API overloaded
    Console.WriteLine($"API overloaded, retry after: {ex.RetryAfter}");
}
```

## License

MIT
