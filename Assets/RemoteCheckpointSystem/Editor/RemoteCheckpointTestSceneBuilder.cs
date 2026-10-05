using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Tools > RemoteCheckpointSystem > Create Test Scene` — Assets/Scenes/RemoteCheckpointTest.unity를 새로 만든다.
/// 기존 씬은 건드리지 않는다. +X 방향 일직선 배치:
///   CH2_Start(순서10) → CH2_Mid(20) → CH2_Back(5, "아직 안 밟은 과거 구역") → A(원격 게이트, x=0)
///   → Legacy(순서 없음, 배치 오류 시험용) → B_CH4Start(순서100, id "CH4_Start").
/// 시험 순서는 docs/PRD/RemoteCheckpoint.md §8 참고. 배치모드에서도 돈다(-executeMethod).
/// </summary>
public static class RemoteCheckpointTestSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/RemoteCheckpointTest.unity";

    [MenuItem("Tools/RemoteCheckpointSystem/Create Test Scene")]
    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 바닥 — 윗면이 y=0. 폭 z ±10이라 A 구역(z 20)이 우회를 막는다.
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(5f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 20f);

        RespawnMenuItem.CreateController();

        RespawnZone start = Zone("CH2_Start", -25f, "", true, 10);
        RespawnZone mid = Zone("CH2_Mid", -12f, "", true, 20);
        RespawnZone back = Zone("CH2_Back", -4f, "", true, 5);
        RespawnZone legacy = Zone("Legacy_NoOrder", 20f, "", false, 0);
        RespawnZone b = Zone("B_CH4Start", 40f, "CH4_Start", true, 100);

        RemoteCheckpointGate gate = CreateGate(start, new[] { start, mid, back, legacy, b });

        GameObject panel = new GameObject("RemoteCheckpointDebugPanel");
        panel.AddComponent<RemoteCheckpointDebugPanel>().gate = gate;

        // 플레이어 셋 — 메뉴 메서드가 private라 리플렉션으로 부른다(PlayerSystem 파일은 수정하지 않는다).
        string[] makers = { "CreateSpherePlayer", "CreateCubePlayer", "CreateTetrahedronPlayer" };
        float[] zs = { -3f, 0f, 3f };
        for (int i = 0; i < makers.Length; i++)
        {
            typeof(PlayerObjectMenuItem).GetMethod(makers[i], BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
            Selection.activeGameObject.transform.position = new Vector3(-25f, 1.5f, zs[i]);
        }

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        Debug.Log($"[RemoteCheckpointSystem] 테스트 씬을 만들었다: {ScenePath}");
    }

    private static RespawnZone Zone(string name, float x, string id, bool ordered, int order)
    {
        RespawnMenuItem.CreateCheckpoint();
        GameObject go = Selection.activeGameObject;
        go.name = name;
        go.transform.position = new Vector3(x, 0f, 0f);
        RespawnZone z = go.GetComponent<RespawnZone>();
        z.checkpointId = id;
        z.useProgressOrder = ordered;
        z.progressOrder = order;
        return z;
    }

    private static RemoteCheckpointGate CreateGate(RespawnZone restartReturn, RespawnZone[] clear)
    {
        GameObject a = new GameObject("A_RemoteGate");
        a.transform.position = new Vector3(0f, 0f, 0f);

        BoxCollider box = a.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2f, 3f, 20f);
        box.center = new Vector3(0f, 1.5f, 0f);

        ZoneEntryDetector detector = a.AddComponent<ZoneEntryDetector>();
        detector.mode = ZoneEntryDetector.EntryMode.EveryNewEntry;

        RemoteCheckpointGate gate = a.AddComponent<RemoteCheckpointGate>();
        gate.targetCheckpointId = "CH4_Start";
        gate.restartReturnZone = restartReturn;
        gate.restartClearZones = clear;

        // 눈에 보이는 표식(콜라이더 없음). 감지기는 자기 렌더러만 끄므로 자식으로 둔다.
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "A_Marker";
        marker.transform.SetParent(a.transform, false);
        marker.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        marker.transform.localScale = new Vector3(2f, 0.1f, 20f);
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        return gate;
    }
}
