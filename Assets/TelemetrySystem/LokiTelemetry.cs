using System.Collections.Generic;
using System.Globalization;

public static class LokiTelemetry
{
    public static void Event(string name, string detail = null) =>
        LokiClient.Enqueue(new Dictionary<string, string> { { "type", "event" }, { "name", name } }, detail ?? name);

    public static void Metric(string name, float value) =>
        LokiClient.Enqueue(new Dictionary<string, string> { { "type", "metric" }, { "name", name } },
            value.ToString("F3", CultureInfo.InvariantCulture));
}
