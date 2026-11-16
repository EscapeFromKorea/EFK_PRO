using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MpEditor
{
    const string LobbyPrefab = "Assets/MultiplayerSystem/Resources/MpLobby.prefab";
    const string HomeScenePath = "Assets/Scenes/MpHome.unity";

    // 배치모드: -executeMethod MpEditor.CreateAssets  (로비 프리팹 + 홈 씬)
    [MenuItem("Tools/Multiplayer/Create Lobby Prefab And Home Scene")]
    public static void CreateAssets()
    {
        var go = new GameObject("MpLobby");
        go.AddComponent<NetworkObject>();
        go.AddComponent<MpLobby>();
        PrefabUtility.SaveAsPrefabAsset(go, LobbyPrefab);
        Object.DestroyImmediate(go);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        var c = cam.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        cam.AddComponent<AudioListener>();
        new GameObject("MpMenu").AddComponent<MpMenu>();
        EditorSceneManager.SaveScene(scene, HomeScenePath);
        Debug.Log("[Mp] 생성 완료 로비프리팹=" + (AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefab) != null) + " 홈씬=" + System.IO.File.Exists(HomeScenePath));
    }
}
