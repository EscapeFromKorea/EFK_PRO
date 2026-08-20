using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools/PortalSystem/Create Portal` — 트리거 박스 + 문틀 시각화를 함께 생성한다.
/// 문틀을 눈에 보이게 두는 이유는 이 기믹이 순간이동이 아니라 "지나가는 문"이라는 것을 플레이어가
/// 알아야 하기 때문이다. 시각 자식들은 콜라이더가 없어 통행을 막지 않는다.
/// </summary>
public static class PortalMenuItem
{
    // 문 안쪽 높이. 굴리기 구간의 천장 여유는 1.42 U 이상이어야 하므로(정육면체 텀블 중 최고점
    // √2 = 1.4142, PRD §9.2) 문부터 그보다 넉넉하게 둬서 "문은 지났는데 굴러지지 않는" 구간을
    // 실수로 만들지 않게 한다.
    private const float Opening = 2.0f;
    private const float Width = 2.4f;
    private const float FrameThickness = 0.15f;
    private const float TriggerDepth = 0.4f;

    [MenuItem("Tools/PortalSystem/Create Portal")]
    private static void CreatePortal()
    {
        GameObject root = new GameObject("Portal");

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(Width, Opening, TriggerDepth);
        trigger.center = new Vector3(0f, Opening * 0.5f, 0f);

        root.AddComponent<Portal>();

        Material frameMaterial = CreateFrameMaterial();
        float halfWidth = Width * 0.5f;

        AddFramePart(root, "Portal_Post_L", frameMaterial,
            new Vector3(-halfWidth - FrameThickness * 0.5f, Opening * 0.5f, 0f),
            new Vector3(FrameThickness, Opening, FrameThickness));

        AddFramePart(root, "Portal_Post_R", frameMaterial,
            new Vector3(halfWidth + FrameThickness * 0.5f, Opening * 0.5f, 0f),
            new Vector3(FrameThickness, Opening, FrameThickness));

        AddFramePart(root, "Portal_Lintel", frameMaterial,
            new Vector3(0f, Opening + FrameThickness * 0.5f, 0f),
            new Vector3(Width + FrameThickness * 2f, FrameThickness, FrameThickness));

        // 씬 뷰에서 보고 있는 지점에 놓는다(WindZone 생성 메뉴와 같은 감각).
        if (SceneView.lastActiveSceneView != null)
            root.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(root, "Create Portal");
        Selection.activeGameObject = root;

        Debug.Log("[Portal] 포탈을 만들었다. 입구·출구를 한 쌍으로 놓고 그 사이 구간을 평지로, " +
                  "천장 여유 1.42 U 이상으로 설계해라(PRD §9.2). 굴리기 구간은 선택 경로 전용이다 — " +
                  "필수 경로에 두면 네모 단독 완주(LD-01)가 깨진다.", root);
    }

    /// <summary>
    /// 텀블 기하 불변식을 런타임과 <b>같은 식</b>으로 검사한다. 씬에서는 기하가 어긋나도
    /// "몇 칸 굴리니까 벽에 낀다" 정도로만 보여 원인을 찾기 어렵기 때문에 자체 점검을 둔다
    /// (`DestructionSystem/Validate Grid Split`과 같은 판단).
    /// </summary>
    [MenuItem("Tools/PortalSystem/Self-Check Tumble Geometry")]
    public static void SelfCheckGeometry()
    {
        string report = PlayerRollModeReceiver.SelfCheck();
        if (report.Contains("실패")) Debug.LogError(report);
        else Debug.Log(report);
    }

    // 계단 한 칸 = 정육면체 텀블 한 칸(1.0 U)과 같은 깊이로 둬서 "칸마다 한 단씩 내려간다"는
    // 감각이 자연스럽다.
    //
    // [단 높이는 0.35가 아니라 0.25×scale 미만이어야 한다 — 2026-08-20 정정]
    // 처음엔 0.35(낙하시간이 fallExitGrace 0.35초보다 짧다는 이유)로 뒀는데, 실제 플레이테스트에서
    // "움직임이 어색해지고 결국 기본 이동으로 풀린다"가 나왔다. 원인은 fallExitGrace가 아니라
    // `PlayerRollModeReceiver.GroundSnapCorrectionCap`(0.25×scale)이었다 — 그보다 큰 낙차는
    // 매 텀블 재접지가 절대 보정하지 않는 절벽 취급이라, 단마다 "텀블 → 뜸 → 낙하 → 재접지"가
    // 반복되는 스타카토 동작이 된다(전문가 검토로 확인). 캡 미만으로 두면 매 텀블이 재접지로
    // 매끈하게 흡수된다 — 이 상수 미만이 "계단"의 정의다.
    private const float StairStepDepth = 1.0f;
    private const float StairStepHeight = PlayerRollModeReceiver.GroundSnapCorrectionCap - 0.05f;
    private const float StairWidth = 2.0f;
    private const int StairStepCount = 6;

    /// <summary>
    /// `Tools/PortalSystem/Create Test Stairs (Fall Grace)` — 계단형 낙차 테스트용. 단 높이가
    /// `GroundSnapCorrectionCap`(0.25×scale) 미만이라 매 텀블의 재접지가 흡수해야 정상이다 —
    /// 굴리기 모드로 내려가며 매끈하게 이어지는지, 뜸·되감기 없이 모드가 유지되는지 보는 것이 이
    /// 오브젝트의 용도다(어색함이 재현되면 이 캡 미만에서도 문제가 있다는 뜻이라 별도 원인을 찾아야
    /// 한다). 게임플레이용이 아니라 테스트용이라 포탈을 같이 만들지 않는다 — 계단 위쪽에 별도로
    /// 입구 포탈을 놓고 굴리기 모드로 들어와서 확인해라.
    /// </summary>
    [MenuItem("Tools/PortalSystem/Create Test Stairs (Fall Grace)")]
    private static void CreateTestStairs()
    {
        GameObject root = new GameObject("TestStairs_FallGrace");
        Material stepMaterial = CreateFrameMaterial();

        for (int i = 0; i < StairStepCount; i++)
        {
            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = $"Step_{i}";
            step.transform.SetParent(root.transform, false);
            step.transform.localPosition = new Vector3(0f, -StairStepHeight * i, StairStepDepth * i);
            step.transform.localScale = new Vector3(StairWidth, 1f, StairStepDepth);

            MeshRenderer renderer = step.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = stepMaterial;
        }

        if (SceneView.lastActiveSceneView != null)
            root.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(root, "Create Test Stairs");
        Selection.activeGameObject = root;

        Debug.Log($"[Portal] 테스트 계단을 만들었다 — 단 {StairStepCount}개, 단당 깊이 " +
                  $"{StairStepDepth} U / 높이 {StairStepHeight} U(재접지 보정 상한 미만). 첫 단 " +
                  "앞쪽에 입구 포탈을 놓고 굴리기 모드로 내려가 매끈하게 이어지는지, 어색함 없이 " +
                  "모드가 유지되는지 확인해라.", root);
    }

    private static void AddFramePart(GameObject parent, string name, Material material,
                                     Vector3 localPosition, Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        // 문틀은 순수 시각물이다. 콜라이더를 남기면 통행 폭이 좁아져 §9.1의 복도 폭 게이트 계산이
        // 어긋난다.
        Collider col = part.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = material;
    }

    private static Material CreateFrameMaterial() => CreateMaterial(new Color(0.55f, 0.45f, 0.85f));

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");
        return new Material(shader) { color = color };
    }

    // 바닥 실제 크기(월드 유닛). 세 변형 전부 같은 물리 크기를 써서 어떤 걸 골라도 같은 넓이의
    // 구간을 설계·비교할 수 있다 — 격자 칸 수는 도형별 칸 크기에 맞춰 알아서 달라진다.
    private const float FloorSize = 8f;
    private const float FloorThickness = 1f;
    private static readonly Color FloorTint = new Color(0.6f, 0.6f, 0.65f);

    [MenuItem("Tools/PortalSystem/Create Tumble Floor (Cube)")]
    private static void CreateTumbleFloorCube() => CreateTumbleFloor(PortalTumbleFloor.GridMode.Cube);

    [MenuItem("Tools/PortalSystem/Create Tumble Floor (Tetrahedron)")]
    private static void CreateTumbleFloorTetrahedron() => CreateTumbleFloor(PortalTumbleFloor.GridMode.Tetrahedron);

    [MenuItem("Tools/PortalSystem/Create Tumble Floor (Both)")]
    private static void CreateTumbleFloorBoth() => CreateTumbleFloor(PortalTumbleFloor.GridMode.Both);

    /// <summary>굴리기 구간 설계·테스트 전용 평지 바닥 + 격자 오버레이(<see cref="PortalTumbleFloor"/>).
    /// 물리적으로는 평범한 평지 콜라이더일 뿐이라 어떤 도형이 위에서 굴러도 된다 — 격자는 정렬을
    /// 눈으로 돕는 도구다. 기존 바닥에서 굴리는 것을 막지 않는다(대체가 아니라 추가 선택지).</summary>
    private static void CreateTumbleFloor(PortalTumbleFloor.GridMode mode)
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = $"TumbleFloor_{mode}";
        root.transform.localScale = new Vector3(FloorSize, FloorThickness, FloorSize);

        MeshRenderer renderer = root.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = CreateMaterial(FloorTint);

        PortalTumbleFloor floor = root.AddComponent<PortalTumbleFloor>();
        floor.gridMode = mode;
        floor.gridSize = FloorSize;

        // 씬 뷰 피벗을 바닥 "윗면"에 맞춘다 — Cube 프리미티브는 중심 기준이라 절반만큼 내려야
        // 피벗 위치에 실제로 발을 디딜 수 있다(Create Portal의 배치 관례와 같되, 두께의 절반을 뺀다).
        Vector3 anchor = SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.pivot
            : Vector3.zero;
        root.transform.position = anchor - new Vector3(0f, FloorThickness * 0.5f, 0f);

        Undo.RegisterCreatedObjectUndo(root, "Create Tumble Floor");
        Selection.activeGameObject = root;

        Debug.Log($"[Portal] 굴리기 전용 바닥({mode})을 만들었다 — {FloorSize}×{FloorSize} U. " +
                  "격자는 Scene 뷰에서만 보이고 Game 뷰·실제 플레이에는 안 보인다. 기존 바닥 대체가 " +
                  "아니라 설계·테스트용 추가 선택지다.", root);
    }
}
