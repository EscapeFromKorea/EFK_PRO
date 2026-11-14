using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Open the playable Map 2 scene and run its opt-in Play Mode integration probe.</summary>
    public static class KitchenConfirmedPlayVerification
    {
        private const string ScenePath = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenPlayable.unity";

        [MenuItem("Tools/Map2 Kitchen Rebuild/Verify Confirmed Gameplay In Play Mode")]
        public static void Run()
        {
            if (!Environment.GetCommandLineArgs().Contains("-map2confirmedverify"))
                throw new InvalidOperationException("Pass -map2confirmedverify so the opt-in probe can report and exit.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Editor is already entering or running Play Mode.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException("Playable Map2 scene is missing: " + ScenePath);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>() == null)
                throw new InvalidOperationException("Playable Map2 scene has no controller: " + ScenePath);
            Debug.Log("[Map2 Confirmed] Entering Editor Play Mode integration verification.");
            EditorApplication.EnterPlaymode();
        }
    }
}
