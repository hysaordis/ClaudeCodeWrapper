# Changelog

## v1.1.0 - Usage Monitoring & Error Handling

### Added

- **UsageMonitor service**: Track API usage limits in real-time
  - 5-hour session limit monitoring
  - 7-day weekly limit monitoring
  - Automatic credential retrieval from macOS Keychain or credentials file
  - Configurable cache expiry
- **Rate limit exceptions**:
  - `RateLimitException` for HTTP 429 errors (with RequestId, ErrorType, RetryAfter)
  - `OverloadedException` for HTTP 529 errors
- **Automatic error detection**: CLI output is parsed for rate limit errors
- **New options**:
  - `EnableUsageMonitoring` - Enable/disable usage monitoring
  - `UsageCacheExpiry` - Configure cache duration
- **New methods**:
  - `GetUsageAsync()` - Get current usage info
  - `IsWithinLimitsAsync()` - Check if within safe limits

### Models

- `UsageInfo` - Usage data (FiveHour, SevenDay limits)
- `SessionUsage` - Individual limit info (Utilization, ResetsAt, Available)

---

## v1.0.0 - Initial Release

C# wrapper for Claude Code CLI.

### Features

- **Simple API**: `ClaudeCode.Initialize()`, `SendAsync()`, `StreamAsync()`
- **Real-time activity monitoring** with sub-agent support
- **Smart PermissionMode enum**:
  - `PermissionMode.Default` - Standard permissions
  - `PermissionMode.Plan` - Read-only analysis mode
  - `PermissionMode.AcceptEdits` - Auto-accept file edits
  - `PermissionMode.BypassPermissions` - No permission prompts
- **Response metrics**: model, session ID, duration, token usage
- **Session management**: Resume and continue sessions

### Requirements

- .NET 8.0+
- Claude Code CLI installed
- Claude Code Max subscription
