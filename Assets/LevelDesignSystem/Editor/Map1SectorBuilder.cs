using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// `.claude/plan/Map1_DayDream_레벨디자인_기획서.md`의 섹터를 정확히 그대로, 요청받은 섹터 단위로만
/// 그레이박스로 배치하는 1회성 스크립트. 대상 씬은 "Assets/Scenes/Map1_sector.unity" —
/// 처음 실행하면 새로 만들고, 이후 다른 섹터를 추가할 때는 같은 씬을 열어 이어붙인다.
/// 재실행하면 해당 섹터가 씬에 중복 생성된다 — 다시 돌리기 전 기존 SectorN_* 루트를 지울 것.
/// </summary>
public static class Map1SectorBuilder
{
    private const string ScenePath = "Assets/Scenes/Map1_sector.unity";

    [MenuItem("Tools/Map1/Sector 1 - 잠듦의 문턱")]
    private static void BuildSector1()
    {
        EnsureSceneView();
        Scene scene = OpenOrCreateSectorScene();
        EditorSceneManager.SetActiveScene(scene);

        Transform sectorsRoot = FindOrCreateRoot("Map1_Sectors");
        Transform sector = NewChild(sectorsRoot, "Sector1_ThresholdOfSleep");

        // 1) 시작 광장 (기획서: 시작점 0,0,0)
        Bounds start = Place("Platform_GrassBlock_L", Vector3.zero, sector, "Island_Start");
        float topY = start.max.y;

        // 2) 점프대·가속대 — 광장 바로 옆 사이드 공간, 본 경로(디딤돌→계단)와 분리
        CreateViaMenu(Reflected(typeof(JumpPadCreator), "CreateJumpPad"), new Vector3(-2.5f, topY, 1.0f), sector, "JumpPad_Tutorial");
        CreateViaMenu(Reflected(typeof(AccelPadMenuItem), "CreateAccelPad"), new Vector3(2.5f, topY, 1.0f), sector, "AccelPad_Tutorial");

        // 3) 디딤돌 3개 (간격 1.6U)
        Vector3 cursor = new Vector3(0, topY, start.max.z + 1.6f);
        for (int i = 0; i < 3; i++)
        {
            Bounds stone = Place("Platform_SteppingStone", cursor, sector, $"SteppingStone_{i + 1}");
            cursor = new Vector3(0, stone.max.y, stone.max.z + 1.6f);
        }

        // 4) 완만한 계단 4단 (GrassBlock + GrassRamp 교대, 틈 없이 이어붙임)
        for (int i = 0; i < 4; i++)
        {
            Bounds block = Place("Platform_GrassBlock", cursor, sector, $"Stairs_Block_{i + 1}");
            cursor = new Vector3(0, block.max.y, block.max.z);
            Bounds ramp = Place("Platform_GrassRamp", cursor, sector, $"Stairs_Ramp_{i + 1}");
            cursor = new Vector3(0, ramp.max.y, ramp.max.z);
        }

        // 5) 무게 발판 + 문 (계단 끝) — 네모가 밟는 동안만 열림
        CreateViaMenu(Reflected(typeof(DoorSystemMenuItem), "CreateExitPlateMenu"), cursor, sector, "WeightGate_CubeGatekeeper");

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = sector.gameObject;
        Debug.Log($"Map1 Sector 1(잠듦의 문턱) 배치 완료 → {ScenePath}");
    }

    // ── 씬/하이어라키 헬퍼 ──────────────────────────────────────────────────
    private static Scene OpenOrCreateSectorScene()
    {
        if (File.Exists(ScenePath))
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        Scene created = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
        EditorSceneManager.SaveScene(created, ScenePath);
        return created;
    }

    private static Transform FindOrCreateRoot(string name)
    {
        Scene active = EditorSceneManager.GetActiveScene();
        foreach (var go in active.GetRootGameObjects())
            if (go.name == name) return go.transform;

        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Build Map1 Sector");
        return root.transform;
    }

    private static Transform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build Map1 Sector");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    // ── 지형(FBX) 배치 헬퍼 ─────────────────────────────────────────────────
    private static Bounds Place(string fbxName, Vector3 pos, Transform parent, string customName)
    {
        string path = $"Assets/Models/{fbxName}.fbx";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            Debug.LogError($"Map1SectorBuilder: 모델을 찾지 못했습니다 - {path}");
            return new Bounds(pos, Vector3.one);
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        Undo.RegisterCreatedObjectUndo(go, "Place Terrain");
        go.name = customName;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;

        var meshFilter = go.GetComponentInChildren<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            var collider = meshFilter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
        }

        var renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(pos, Vector3.one);
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // ── 기믹 MenuItem 호출 헬퍼 ─────────────────────────────────────────────
    // Tools 메뉴들은 대부분 SceneView.pivot 위치에 생성되고 위치 파라미터를 받지 않는다.
    // 호출 직전 pivot을 원하는 좌표로 옮긴 뒤, 호출 전/후 씬 루트 오브젝트를 비교해
    // 새로 생긴 것들만 골라 지정한 섹터 부모 아래로 정리한다.
    private static void CreateViaMenu(Action creatorCall, Vector3 pivot, Transform parent, string label)
    {
        SetPivot(pivot);
        var before = new System.Collections.Generic.HashSet<GameObject>(EditorSceneManager.GetActiveScene().GetRootGameObjects());
        creatorCall?.Invoke();
        var after = EditorSceneManager.GetActiveScene().GetRootGameObjects();
        var created = after.Where(g => !before.Contains(g)).ToList();

        if (created.Count == 0)
        {
            Debug.LogWarning($"Map1SectorBuilder: '{label}' 호출 후 새 루트 오브젝트를 찾지 못했습니다.");
            return;
        }

        foreach (var g in created)
            g.transform.SetParent(parent, true);
    }

    private static Action Reflected(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            Debug.LogError($"Map1SectorBuilder: {type.Name}.{methodName} 메서드를 찾지 못했습니다.");
            return () => { };
        }
        return () => method.Invoke(null, null);
    }

    private static void SetPivot(Vector3 pos)
    {
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.pivot = pos;
    }

    private static void EnsureSceneView()
    {
        if (SceneView.lastActiveSceneView == null)
            EditorWindow.GetWindow<SceneView>();
    }
}
