using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>
    /// Runs the same opt-in physical probe in a dedicated Unity Editor Play Mode
    /// session. Use -batchmode without -quit and pass -map2rebuildverify. The
    /// runtime probe writes rebuild_playmode.json and exits the batch Editor.
    /// </summary>
    public static class KitchenRebuildPlayModeVerification
    {
        private const string ScenePath = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDraft.unity";

        [MenuItem("Tools/Map2 Kitchen Rebuild/4 Verify In Editor Play Mode")]
        public static void Run()
        {
            OpenAndPlay(ScenePath);
        }

        public static void RunDetail()
        {
            OpenAndPlay(KitchenDetailScene.ScenePath);
        }

        private static void OpenAndPlay(string scenePath)
        {
            if (!Environment.GetCommandLineArgs().Contains("-map2rebuildverify"))
                throw new InvalidOperationException("Pass -map2rebuildverify so the opt-in scene probe can finish and report.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Editor is already entering or running Play Mode.");
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>() == null)
                throw new InvalidOperationException("Saved rebuild scene has no Map2 controller: " + scenePath);
            Debug.Log("[Map2 Rebuild] Entering Editor Play Mode verification. This is not a built-player execution.");
            EditorApplication.EnterPlaymode();
        }
    }
}
