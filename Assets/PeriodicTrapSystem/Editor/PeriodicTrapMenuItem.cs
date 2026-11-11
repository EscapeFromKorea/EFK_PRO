using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > PeriodicTrapSystem > ...` — 도끼/망치/가시를 SceneView 중앙에 그레이박스로 생성한다.
/// 확정 사양은 `docs/PRD/PeriodicTraps.md`, 폴더 설계 배경은 `PeriodicTrapSystem/CLAUDE.md` 참고.
///
/// [콜라이더 치수를 여기서 또 정의하는 이유] 각 트랩의 `Reset()`이 이미 기본 콜라이더를 만든다.
/// 여기서 같은 값을 반복하는 이유는 `Reset()`이 에디터에서 실제로 도는지와 무관하게 최종 결과가
/// 같아지게 하려는 것이다(`LiftMenuItem`과 같은 저장소 관례).
///
/// [모든 수치가 그레이박스] `A`/`P`/각 상태 지속시간/이동 거리는 PRD §4가 전부 미정으로 남긴 값이다.
/// 이 메뉴는 스크립트 기본값을 그대로 쓰고 강제하지 않는다 — 배치 후 인스펙터에서 실측 조정하는
/// 것이 맞다(PRD §4 "값 승격 금지").
///
/// [가시 시각 메쉬가 `PlayerSystem/Editor/TetrahedronMeshGenerator`를 재사용하는 이유] 정사면체
/// 메쉬는 Unity 기본 Primitive에 없고, 이 프로젝트는 이미 플레이어 정사면체용으로 같은 문제를 풀어둔
/// 생성기를 갖고 있다(면마다 정점을 나눈 플랫 셰이딩, 무게중심 원점, 한 면이 바닥에 닿도록 정렬).
/// 새로 만들면 똑같은 계산을 중복하는 것이라 그 public static 메서드만 읽어서 쓴다 — PlayerSystem
/// 파일은 한 줄도 고치지 않는다(다른 기믹이 `PlayerWeight.Of`를 읽기만 하는 것과 같은 방식).
/// </summary>
public static class PeriodicTrapMenuItem
{
    private const string SystemFolder = "Assets/PeriodicTrapSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    // AxeTrap.Reset()과 동일한 값(단일 정본은 AxeTrap.cs).
    private static readonly Vector3 BladeSize = new Vector3(1f, 0.2f, 0.15f);
    private static readonly Vector3 BladeCenter = new Vector3(0f, 1f, 0f);
    private static readonly Vector3 SupportSize = new Vector3(0.2f, 0.8f, 0.2f);
    private static readonly Vector3 SupportCenter = new Vector3(0f, 0.4f, 0f);

    // HammerTrap.Reset() / SpikeTrap.Reset()과 동일한 값.
    private static readonly Vector3 HeadSize = new Vector3(1f, 0.6f, 1f);
    private static readonly Vector3 SpikeSize = new Vector3(0.6f, 1f, 0.6f);

    [MenuItem("Tools/PeriodicTrapSystem/Create Axe Trap")]
    private static void CreateAxe()
    {
        GameObject go = new GameObject("AxeTrap");
        go.transform.position = SceneOrigin();

        BoxCollider blade = go.AddComponent<BoxCollider>();
        blade.center = BladeCenter;
        blade.size = BladeSize;

        BoxCollider support = go.AddComponent<BoxCollider>();
        support.center = SupportCenter;
        support.size = SupportSize;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        AxeTrap axe = go.AddComponent<AxeTrap>();
        axe.bladeCollider = blade;
        axe.supportCollider = support;

        AddVisual(go.transform, "Blade_Visual", BladeCenter, BladeSize, new Color(0.85f, 0.25f, 0.2f));
        AddVisual(go.transform, "Support_Visual", SupportCenter, SupportSize, new Color(0.5f, 0.4f, 0.3f));

        Finish(go, "[PeriodicTrapSystem] 도끼 생성 완료. A/P/warmup은 그레이박스 기본값 — 배치 후 " +
                   "실측해 조정하세요. OnHazardHit을 RespawnController.RespawnPlayer에 배선하세요.");
    }

    [MenuItem("Tools/PeriodicTrapSystem/Create Hammer Trap")]
    private static void CreateHammer()
    {
        GameObject go = new GameObject("HammerTrap");
        go.transform.position = SceneOrigin();

        BoxCollider head = go.AddComponent<BoxCollider>();
        head.size = HeadSize;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        HammerTrap hammer = go.AddComponent<HammerTrap>();
        hammer.headCollider = head;

        AddVisual(go.transform, "Head_Visual", Vector3.zero, HeadSize, new Color(0.4f, 0.4f, 0.45f));

        Finish(go, "[PeriodicTrapSystem] 망치 생성 완료. 상태 지속시간/dropDistance는 그레이박스 " +
                   "기본값 — 배치 후 실측해 조정하세요. OnHazardHit을 RespawnController.RespawnPlayer에 " +
                   "배선하세요.");
    }

    [MenuItem("Tools/PeriodicTrapSystem/Create Spike Trap")]
    private static void CreateSpike()
    {
        GameObject go = new GameObject("SpikeTrap");
        go.transform.position = SceneOrigin();

        BoxCollider spike = go.AddComponent<BoxCollider>();
        spike.size = SpikeSize;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        SpikeTrap trap = go.AddComponent<SpikeTrap>();
        trap.spikeCollider = spike;

        AddTetrahedronVisual(go.transform, "Spike_Visual", SpikeSize, new Color(0.6f, 0.15f, 0.15f));

        Finish(go, "[PeriodicTrapSystem] 가시 생성 완료. 상태 지속시간/popDistance는 그레이박스 " +
                   "기본값 — 배치 후 실측해 조정하세요. OnHazardHit을 RespawnController.RespawnPlayer에 " +
                   "배선하세요.");
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────────────────────────────

    private static void AddVisual(Transform parent, string name, Vector3 localCenter, Vector3 size, Color color)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = name;
        Object.DestroyImmediate(visual.GetComponent<Collider>()); // 콜라이더는 부모가 이미 갖고 있다
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = localCenter;
        visual.transform.localScale = size;
        visual.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial(name, color);
    }

    /// <summary>가시 전용 — 한 면이 바닥에 닿고 반대 꼭짓점이 위(돌출 방향)를 향하는 정사면체
    /// 메쉬를 쓴다. 콜라이더는 그대로 BoxCollider(위 SpikeSize)를 쓴다 — 물리 판정까지 뾰족한
    /// 모양으로 맞출 필요는 없다(플레이어 정사면체의 솔리드 콜라이더도 완전한 뾰족 메쉬가 아니라
    /// 살짝 깎은 근사 메쉬를 쓰는 것과 같은 이유).</summary>
    private static void AddTetrahedronVisual(Transform parent, string name, Vector3 size, Color color)
    {
        GameObject visual = new GameObject(name);
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = size;

        MeshFilter mf = visual.AddComponent<MeshFilter>();
        mf.sharedMesh = TetrahedronMeshGenerator.Create();

        MeshRenderer mr = visual.AddComponent<MeshRenderer>();
        mr.sharedMaterial = LoadOrCreateMaterial(name, color);
    }

    private static Vector3 SceneOrigin() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    private static void Finish(GameObject select, string message)
    {
        Undo.RegisterCreatedObjectUndo(select, "Create Periodic Trap");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = select;
        Debug.Log(message);
    }

    // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(LiftMenuItem과 같은 방식).
    private static Material LoadOrCreateMaterial(string key, Color color)
    {
        string path = $"{MaterialSavePath}/PeriodicTrap_{key}_Mat.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");

        Shader s = Shader.Find("Universal Render Pipeline/Lit")
                   ?? Shader.Find("HDRP/Lit")
                   ?? Shader.Find("Standard");
        Material mat = new Material(s);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
