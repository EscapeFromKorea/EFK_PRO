using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ToyWorldPlayModeSmokeRunner
{
    private const string SessionKey = "ToyWorld.PlayModeSmoke.Pending";

    static ToyWorldPlayModeSmokeRunner()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(SessionKey, true);
        EditorSceneManager.OpenScene(ToyWorldPrototypeBuilder.ScenePath);
        EditorApplication.isPlaying = true;
    }

    public static void BuildAndRunFromCommandLine()
    {
        ToyWorldPrototypeBuilder.BuildFromCommandLine();
        RunFromCommandLine();
    }

    public static void RunArtVerificationFromCommandLine()
    {
        SessionState.SetBool("ToyWorld.ArtCapture", true);
        RunFromCommandLine();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, false);
        GameObject probe = new GameObject("ToyWorld_PlayModeSmokeProbe");
        probe.AddComponent<ToyWorldPlayModeSmokeProbe>();
    }
}
