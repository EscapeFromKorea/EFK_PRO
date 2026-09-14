using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TelemetrySystem.Editor
{
    public static class LokiPushBodySelfCheck
    {
        [MenuItem("Tools/Telemetry/Run Self Check")]
        public static void Run()
        {
            var labelsA = new Dictionary<string, string> { { "job", "efk-pro" }, { "type", "event" } };
            var labelsB = new Dictionary<string, string> { { "job", "efk-pro" }, { "type", "error" } };
            var streams = new List<(Dictionary<string, string> labels, List<(long ts, string line)> values)>
            {
                (labelsA, new List<(long, string)> { (1000L, "hello \"world\"") }),
                (labelsB, new List<(long, string)> { (2000L, "line\nbreak") }),
            };

            var json = LokiClient.BuildPushBody(streams);

            Debug.Assert(json.Contains("\"job\":\"efk-pro\""), "job label missing");
            Debug.Assert(json.Contains("hello \\\"world\\\""), "quote escaping broken");
            Debug.Assert(json.Contains("line\\nbreak"), "newline escaping broken");
            Debug.Assert(json.Contains("[\"1000\","), "timestamp missing");
            Debug.Assert(json.StartsWith("{\"streams\":[") && json.EndsWith("]}"), "envelope shape broken");

            Debug.Log("[LokiPushBodySelfCheck] PASS — " + json);
        }
    }
}
