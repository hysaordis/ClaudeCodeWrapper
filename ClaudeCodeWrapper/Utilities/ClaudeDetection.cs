using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeCodeWrapper.Utilities;

/// <summary>
/// Utility for detecting Claude Code CLI installation.
/// </summary>
public static class ClaudeDetection
{
    /// <summary>
    /// Check if Claude Code CLI is installed and accessible.
    /// </summary>
    /// <returns>True if CLI is found</returns>
    public static bool IsClaudeCodeInstalled()
    {
        return TryDetectClaudePath(out _);
    }

    /// <summary>
    /// Try to detect the path to Claude Code CLI.
    /// </summary>
    /// <param name="path">Output parameter with the detected path</param>
    /// <returns>True if path was detected</returns>
    public static bool TryDetectClaudePath(out string? path)
    {
        path = null;

        // Try common locations first
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "local", "claude"),
            "/usr/local/bin/claude",
            "/opt/homebrew/bin/claude"
        };

        foreach (var possiblePath in possiblePaths)
        {
            if (File.Exists(possiblePath))
            {
                path = possiblePath;
                return true;
            }
        }

        // Try 'which' command on Unix-like systems
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "claude",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        path = output;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore errors from 'which' command
            }
        }

        // Try 'where' command on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "claude",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        // 'where' returns all matches, take the first one
                        var firstPath = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (!string.IsNullOrEmpty(firstPath))
                        {
                            path = firstPath;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors from 'where' command
            }
        }

        return false;
    }

    /// <summary>
    /// Get detailed information about Claude Code installation.
    /// </summary>
    /// <returns>Installation information</returns>
    public static ClaudeInstallationInfo GetInstallationInfo()
    {
        var isInstalled = TryDetectClaudePath(out var path);
        string? version = null;
        bool hasValidAuth = false;

        if (isInstalled && path != null)
        {
            // Try to get version
            version = TryGetClaudeVersion(path);

            // Try to check authentication (simple check)
            hasValidAuth = TryCheckAuthentication(path);
        }

        return new ClaudeInstallationInfo
        {
            IsInstalled = isInstalled,
            Path = path,
            Version = version,
            HasValidAuth = hasValidAuth
        };
    }

    /// <summary>
    /// Try to get Claude Code version.
    /// </summary>
    private static string? TryGetClaudeVersion(string claudePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = claudePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000); // 5 second timeout

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    return output;
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    /// <summary>
    /// Try to check if Claude is authenticated.
    /// This is a basic check - it runs 'claude --print "test"' and checks if it succeeds.
    /// </summary>
    private static bool TryCheckAuthentication(string claudePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = claudePath,
                Arguments = "--print \"test\" --model haiku",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(10000); // 10 second timeout

                // If no authentication, usually we get an error about login
                if (error.Contains("login") || error.Contains("authentication") || error.Contains("not authenticated"))
                {
                    return false;
                }

                // If it completes successfully (even with any output), auth is likely OK
                return process.ExitCode == 0;
            }
        }
        catch
        {
            // Errors during check suggest auth issues
        }

        return false;
    }
}

/// <summary>
/// Information about Claude Code CLI installation.
/// </summary>
public class ClaudeInstallationInfo
{
    /// <summary>
    /// Whether Claude Code CLI is installed.
    /// </summary>
    public bool IsInstalled { get; init; }

    /// <summary>
    /// Path to the Claude CLI executable.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Claude Code version string.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Whether Claude appears to have valid authentication.
    /// </summary>
    public bool HasValidAuth { get; init; }

    /// <summary>
    /// Returns a string representation of the Claude installation status.
    /// </summary>
    /// <returns>A human-readable string describing the installation state.</returns>
    public override string ToString()
    {
        if (!IsInstalled)
        {
            return "Claude Code CLI: Not installed";
        }

        return $"Claude Code CLI: Installed at {Path}" +
               (Version != null ? $", Version: {Version}" : "") +
               $", Authenticated: {HasValidAuth}";
    }
}
