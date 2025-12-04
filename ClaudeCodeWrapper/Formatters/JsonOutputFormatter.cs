using System.Text.Json;

namespace ClaudeCodeWrapper.Formatters;

/// <summary>
/// Formatter for JSON output format.
/// </summary>
internal static class JsonOutputFormatter
{
    /// <summary>
    /// Parse JSON response from Claude CLI to Response type.
    /// </summary>
    public static Response ParseToResponse(string jsonOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            var response = new Response
            {
                Content = root.TryGetProperty("result", out var result) ? result.GetString() ?? "" : "",
                SessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() : null,
                DurationMs = root.TryGetProperty("duration_ms", out var dur) ? dur.GetInt64() : null
            };

            // Extract model from modelUsage (first key)
            if (root.TryGetProperty("modelUsage", out var modelUsage))
            {
                foreach (var prop in modelUsage.EnumerateObject())
                {
                    response.Model = prop.Name;
                    break;
                }
            }

            // Parse token usage
            if (root.TryGetProperty("usage", out var usage))
            {
                response.Tokens = new TokenUsage
                {
                    Input = usage.TryGetProperty("input_tokens", out var input) ? input.GetInt32() : 0,
                    Output = usage.TryGetProperty("output_tokens", out var output) ? output.GetInt32() : 0,
                    CacheCreation = usage.TryGetProperty("cache_creation_input_tokens", out var cc) ? cc.GetInt32() : 0,
                    CacheRead = usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0
                };
            }

            return response;
        }
        catch (JsonException ex)
        {
            throw new ClaudeCodeException($"Failed to parse JSON response: {ex.Message}", ex);
        }
    }
}
