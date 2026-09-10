using System.IO;
using UnityEditor;
using UnityEngine;

namespace TelemetrySystem.Editor
{
    public static class LokiConfigSync
    {
        const string AssetDir = "Assets/TelemetrySystem/Resources";
        const string AssetPath = AssetDir + "/LokiConfig.asset";

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
                var value = line.Substring(eq + 1).Trim();
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
