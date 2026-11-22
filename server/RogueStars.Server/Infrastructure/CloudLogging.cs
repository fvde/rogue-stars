using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace RogueStars.Server.Infrastructure;

/// <summary>
/// One JSON object per line with the field names Cloud Logging understands (<c>severity</c>, <c>message</c>),
/// so Cloud Run logs are structured and filterable. Structured-logging arguments become top-level fields.
/// </summary>
public sealed class CloudLoggingFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "cloud";

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        string message = entry.Formatter(entry.State, entry.Exception);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("severity", Severity(entry.LogLevel));
            json.WriteString("message", entry.Exception == null ? message : message + "\n" + entry.Exception);
            json.WriteString("category", entry.Category);
            if (entry.EventId.Id != 0) json.WriteNumber("eventId", entry.EventId.Id);
            if (entry.State is IReadOnlyList<KeyValuePair<string, object?>> props)
            {
                foreach (var kv in props)
                {
                    if (kv.Key == "{OriginalFormat}" || kv.Key is "severity" or "message" or "category") continue;
                    json.WriteString(kv.Key, Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            json.WriteEndObject();
        }
        writer.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static string Severity(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "DEFAULT",
    };
}
