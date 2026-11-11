using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > StepRotatingBridgeSystem > Create Step Rotating Bridge` — SceneView 중앙에 그레이박스로
/// 생성한다. 확정 사양은 `docs/PRD/StepRotatingBridge.md`, 폴더 설계 배경은
/// `StepRotatingBridgeSystem/CLAUDE.md` 참고.
///
/// [콜라이더 치수를 여기서 또 정의하는 이유] `StepRotatingBridge.Reset()`이 이미 지지/센서 콜라이더를
/// 만든다. 여기서 같은 값을 반복하는 이유는 `Reset()`이 에디터에서 실제로 도는지와 무관하게 최종
/// 결과가 같아지게 하려는 것이다(`LiftMenuItem`/`PeriodicTrapMenuItem`과 같은 저장소 관례).
///
/// [회전각/속도/예고시간/판 크기가 전부 그레이박스] PRD §6이 전부 미정으로 남긴 값이다. 이 메뉴는
/// 스크립트 기본값을 그대로 쓰고 강제하지 않는다 — 배치 후 인스펙터에서 실측 조정하는 것이 맞다.
/// </summary>
public static class StepRotatingBridgeMenuItem
{
    private const string SystemFolder = "Assets/StepRotatingBridgeSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    // StepRotatingBridge.Reset()과 동일한 값(단일 정본은 StepRotatingBridge.cs).
    private static readonly Vector3 SupportSize = new Vector3(2f, 0.3f, 4f);
    private static readonly Vector3 SensorSize = new Vector3(2f, 0.4f, 4f);

    [MenuItem("Tools/StepRotatingBridgeSystem/Create Step Rotating Bridge")]
    private static void CreateBridge()
    {
        GameObject go = new GameObject("StepRotatingBridge");
        go.transform.position = SceneOrigin();

        BoxCollider support = go.AddComponent<BoxCollider>();
        support.isTrigger = false;
        support.size = SupportSize;
        support.center = Vector3.zero;

        // 라이더 감지 전용 트리거 — kinematic-kinematic 쌍에서 OnCollision*이 발화하지 않을 수
        // 있어 반드시 트리거로 감지해야 한다(LiftSystem 선례, StepRotatingBridge.cs 클래스 주석 참고).
        BoxCollider sensor = go.AddComponent<BoxCollider>();
        sensor.isTrigger = true;
        sensor.size = SensorSize;
        sensor.center = new Vector3(0f, SupportSize.y * 0.5f + SensorSize.y * 0.5f, 0f);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        StepRotatingBridge bridge = go.AddComponent<StepRotatingBridge>();
        bridge.supportCollider = support;
        bridge.riderSensor = sensor;

        // 시각 — 지지 콜라이더 크기와 맞춘 순수 렌더용 큐브(콜라이더 없음).
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "StepRotatingBridge_Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = SupportSize;
        visual.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Bridge", new Color(0.75f, 0.55f, 0.3f, 1f));

        Undo.RegisterCreatedObjectUndo(go, "Create Step Rotating Bridge");
        Finish(go, "[StepRotatingBridgeSystem] 다리 생성 완료. localRotationAxis(기본 Z)를 다리 " +
                   "길이 방향에 맞추고, 회전각/속도/예고시간/판 크기는 그레이박스 기본값이니 배치 후 " +
                   "실측해 조정하세요. OnPlayerFell을 RespawnController.RespawnPlayer에 배선하세요.");
    }

    // ── 공통 ─────────────────────────────────────────────────────────────────────────

    private static Vector3 SceneOrigin() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    private static void Finish(GameObject select, string message)
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = select;
        Debug.Log(message);
    }

    // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(LiftMenuItem과 같은 방식).
    private static Material LoadOrCreateMaterial(string key, Color color)
    {
        string path = $"{MaterialSavePath}/StepRotatingBridge_{key}_Mat.mat";
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
