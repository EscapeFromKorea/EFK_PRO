using System.IO;
using UnityEditor;
using UnityEngine;

namespace TelemetrySystem.Editor
{
    public static class LokiConfigSync
    {
        const string AssetDir = "Assets/TelemetrySystem/Resources";
        const string AssetPath = AssetDir + "/LokiConfig.asset";

        // 흔한 .env 관례 — LOKI_TOKEN="abcdef" 처럼 따옴표로 감싸거나 뒤에 # 주석을 붙이는 경우를
        // 그대로 두면 값에 따옴표/주석이 섞여 들어가 Basic Auth가 조용히 깨진다(2026-09-10 리뷰에서
        // 발견 — 401/403만 나고 원인은 로그에 안 남는다).
        private static string CleanEnvValue(string raw)
        {
            string v = raw;
            if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
                return v.Substring(1, v.Length - 2);

            int hashIdx = v.IndexOf('#');
            if (hashIdx >= 0) v = v.Substring(0, hashIdx).TrimEnd();
            return v;
        }

        [MenuItem("Tools/Telemetry/Sync Loki Config from .env")]
        public static void Sync()
        {
            var envPath = Path.Combine(Application.dataPath, "../.env");
            if (!File.Exists(envPath))
            {
                Debug.LogWarning($"[LokiConfigSync] .env not found at {envPath} — skipped.");
                return;
            }

            string url = null, user = null, token = null;
            foreach (var line in File.ReadAllLines(envPath))
            {
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = CleanEnvValue(line.Substring(eq + 1).Trim());
                if (key == "LOKI_URL") url = value;
                else if (key == "LOKI_USER") user = value;
                else if (key == "LOKI_TOKEN") token = value;
            }

            if (url == null || user == null || token == null)
            {
                Debug.LogWarning("[LokiConfigSync] LOKI_URL/LOKI_USER/LOKI_TOKEN missing from .env — skipped.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(AssetDir))
            {
                Directory.CreateDirectory(AssetDir);
                AssetDatabase.Refresh();
            }

            var config = AssetDatabase.LoadAssetAtPath<LokiConfig>(AssetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<LokiConfig>();
                AssetDatabase.CreateAsset(config, AssetPath);
            }

            config.url = url;
            config.user = user;
            config.token = token;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[LokiConfigSync] LokiConfig synced from .env.");
        }
    }
}
