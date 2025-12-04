# ClaudeCodeWrapper

C# wrapper for **Claude Code CLI**.

## Requirements

- .NET 8.0+
- [Claude Code CLI](https://www.anthropic.com/claude-code) installed
- Claude Code Max subscription

## Installation

This package is hosted on **GitHub Packages** (private).

### 1. Create a Personal Access Token

1. Go to [GitHub Settings → Tokens](https://github.com/settings/tokens/new)
2. Select scope: `read:packages`
3. Generate and copy the token

### 2. Add GitHub NuGet Source

```bash
dotnet nuget add source "https://nuget.pkg.github.com/hysaordis/index.json" \
  --name "github-hysaordis" \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_TOKEN \
  --store-password-in-clear-text
```

Or add to your `NuGet.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-hysaordis" value="https://nuget.pkg.github.com/hysaordis/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-hysaordis>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_GITHUB_TOKEN" />
    </github-hysaordis>
  </packageSourceCredentials>
</configuration>
```

### 3. Install the Package

```bash
dotnet add package ClaudeCodeWrapper --source github-hysaordis
```

## Quick Start

```csharp
using ClaudeCodeWrapper;

// Initialize (throws if Claude CLI not installed)
var claude = ClaudeCode.Initialize();

// Send a message
var response = await claude.SendAsync("Hello Claude!");
Console.WriteLine(response);
```

## API

### Initialize

```csharp
// Basic
var claude = ClaudeCode.Initialize();

// With options
var claude = ClaudeCode.Initialize(new ClaudeCodeOptions
{
    Model = "sonnet",
    WorkingDirectory = "/path/to/project",
    SystemPrompt = "You are a helpful assistant"
});

// Check if installed (without throwing)
if (ClaudeCode.IsInstalled())
{
    var claude = ClaudeCode.Initialize();
}
```

### Send Message

```csharp
// Simple - returns text response
var text = await claude.SendAsync("Explain C#");

// With metrics - returns Response object
var response = await claude.SendWithResponseAsync("Explain C#");
Console.WriteLine($"Response: {response.Content}");
Console.WriteLine($"Tokens: {response.Tokens?.Input} in, {response.Tokens?.Output} out");
Console.WriteLine($"Session: {response.SessionId}");
```

### Stream Activities (Real-time Monitoring)

Monitor Claude's tool calls, results, and thoughts in real-time:

```csharp
// Simple streaming
var text = await claude.StreamAsync(
    "Refactor this code",
    activity => Console.WriteLine($"[{activity.Type}] {activity.Tool}: {activity.Summary}")
);

// With metrics
var response = await claude.StreamWithResponseAsync(
    "Search for bugs",
    activity =>
    {
        // activity.Type: "tool_call", "tool_result", "thought"
        // activity.Tool: "Bash", "Read", "Write", "Edit", "Glob", "Grep", etc.
        // activity.Input: tool arguments
        // activity.Content: result content
        // activity.Success: true/false for tool_result
        // activity.IsSubAgent: true if from parallel sub-agent
        // activity.AgentId: sub-agent ID (if IsSubAgent)

        Console.WriteLine($"[{activity.Type}] {activity.Summary}");
    }
);
```

### Session Management

```csharp
// Resume a previous session
var response = await claude.ResumeAsync("session-id", "Continue...");

// Continue the last session
var response = await claude.ContinueAsync("What were we discussing?");
```

## Configuration Options

```csharp
var options = new ClaudeCodeOptions
{
    ClaudePath = null,                          // Auto-detect
    Model = "sonnet",                           // sonnet, opus, haiku
    SystemPrompt = "...",                       // Custom system prompt
    AppendSystemPrompt = "...",                 // Append to default
    WorkingDirectory = "...",                   // Project directory
    PermissionMode = PermissionMode.Default,    // See below
    MaxTurns = 0,                               // Max agent turns (0 = unlimited)
    AllowedTools = [...],                       // Tools allowed without prompt
    DisallowedTools = [...],                    // Blocked tools
    EnvironmentVariables = {...}                // Custom env vars
};
```

## Permission Modes

Control Claude's permission behavior:

```csharp
// List all available modes
foreach (var mode in PermissionMode.All)
{
    Console.WriteLine($"{mode.Value}: {mode.Description}");
}

// Use a specific mode
var claude = ClaudeCode.Initialize(new ClaudeCodeOptions
{
    PermissionMode = PermissionMode.AcceptEdits
});
```

| Mode | Value | Description |
|------|-------|-------------|
| `PermissionMode.Default` | `default` | Standard - allows reads, asks permission for other operations |
| `PermissionMode.Plan` | `plan` | Read-only analysis, no file modifications or command execution |
| `PermissionMode.AcceptEdits` | `acceptEdits` | Auto-accept file edits without prompting |
| `PermissionMode.BypassPermissions` | `bypassPermissions` | YOLO mode - no permission prompts (use with caution!) |

## License

MIT
