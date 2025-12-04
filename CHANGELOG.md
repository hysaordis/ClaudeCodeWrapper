# Changelog

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
