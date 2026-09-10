using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LokiClient : MonoBehaviour
{
    const float FlushIntervalSeconds = 10f;
    const string JobLabel = "efk-pro";

    static LokiClient _instance;
    LokiConfig _config;
    readonly List<(string key, Dictionary<string, string> labels, long ts, string line)> _buffer = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("LokiClient");
        DontDestroyOnLoad(go);
        go.AddComponent<LokiClient>(); // _instance는 Awake()가 스스로 대입한다
    }

    void Awake()
    {
        _instance = this; // AddComponent()가 반환되기 전에 Awake가 도니, Bootstrap의 대입을 기다리면 이 안에서 부르는 이벤트가 유실된다
        _config = Resources.Load<LokiConfig>("LokiConfig");
        if (_config == null || string.IsNullOrEmpty(_config.token))
        {
            Debug.LogWarning("[LokiClient] LokiConfig missing — telemetry disabled. Run Tools > Telemetry > Sync Loki Config from .env.");
            enabled = false;
            return;
        }
        Application.logMessageReceived += OnLogMessage;
        InvokeRepeating(nameof(Flush), FlushIntervalSeconds, FlushIntervalSeconds);
        LokiTelemetry.Event("game_boot", Application.unityVersion);
    }

    void OnDestroy() => Application.logMessageReceived -= OnLogMessage;

    void OnApplicationQuit() => Flush();

    void OnLogMessage(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        var line = string.IsNullOrEmpty(stackTrace) ? message : $"{message}\n{stackTrace}";
        EnqueueInternal(new Dictionary<string, string> { { "type", "error" }, { "level", type.ToString().ToLowerInvariant() } }, line);
    }

    public static void Enqueue(Dictionary<string, string> labels, string line) => _instance?.EnqueueInternal(labels, line);

    void EnqueueInternal(Dictionary<string, string> labels, string line)
    {
        if (!enabled) return;
        labels["job"] = JobLabel;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000L;
        _buffer.Add((LabelsKey(labels), labels, ts, line));
    }

    static string LabelsKey(Dictionary<string, string> labels)
    {
        var keys = new List<string>(labels.Keys);
        keys.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var k in keys) sb.Append(k).Append('=').Append(labels[k]).Append(';');
        return sb.ToString();
    }

    void Flush()
    {
        if (_buffer.Count == 0) return;
        var toSend = new List<(string key, Dictionary<string, string> labels, long ts, string line)>(_buffer);
        _buffer.Clear();

        var streams = new Dictionary<string, (Dictionary<string, string> labels, List<(long ts, string line)> values)>();
        foreach (var e in toSend)
        {
            if (!streams.TryGetValue(e.key, out var s))
            {
                s = (e.labels, new List<(long, string)>());
                streams[e.key] = s;
            }
            s.values.Add((e.ts, e.line));
        }

        StartCoroutine(PostAsync(BuildPushBody(streams.Values)));
    }

    public static string BuildPushBody(IEnumerable<(Dictionary<string, string> labels, List<(long ts, string line)> values)> streams)
    {
        var sb = new StringBuilder();
        sb.Append("{\"streams\":[");
        var firstStream = true;
        foreach (var stream in streams)
        {
            if (!firstStream) sb.Append(',');
            firstStream = false;
            sb.Append("{\"stream\":{");
            var firstLabel = true;
            foreach (var kv in stream.labels)
            {
                if (!firstLabel) sb.Append(',');
                firstLabel = false;
                sb.Append('"').Append(EscapeJson(kv.Key)).Append("\":\"").Append(EscapeJson(kv.Value)).Append('"');
            }
            sb.Append("},\"values\":[");
            var firstValue = true;
            foreach (var (ts, line) in stream.values)
            {
                if (!firstValue) sb.Append(',');
                firstValue = false;
                sb.Append("[\"").Append(ts).Append("\",\"").Append(EscapeJson(line)).Append("\"]");
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    static string EscapeJson(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    IEnumerator PostAsync(string body)
    {
        var url = _config.url.TrimEnd('/') + "/loki/api/v1/push";
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.user}:{_config.token}"));
        req.SetRequestHeader("Authorization", $"Basic {auth}");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[LokiClient] push failed: {req.error}");
    }
}
