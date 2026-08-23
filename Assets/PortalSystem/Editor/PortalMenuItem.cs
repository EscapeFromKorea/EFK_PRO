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
    private static void CreatePortalToggle() => CreatePortal(Portal.PortalAction.Toggle, "Portal");

    // [입구·출구를 따로 만드는 메뉴가 왜 필요한가 — 2026-08-23]
    // Toggle 문 하나로 왕복하면 "몸이 지금 굴리기인지"에 따라 같은 문이 반대로 동작한다. 굴리기
    // 모드는 문을 지나 맵 전체로 따라다니므로, 다른 굴리기 구간을 먼저 지나온 몸은 이 문에서
    // 모드가 **꺼진다** — 화면에는 "이 포탈만 인식이 안 된다"로 보인다(2026-08-23 실측: 정사면체가
    // 정육면체 필드 포탈에서 모드를 켠 채 굴러와, 자기 필드 포탈에서 모드가 꺼졌다).
    // 입구를 Enable(항상 켬), 출구를 Disable(항상 끔)로 깔면 몸의 이전 상태와 무관하게 결정된다 —
    // PRD §9·폴더 CLAUDE.md가 권하는 배치가 이것이다. 한 쌍으로 놓아라.
    [MenuItem("Tools/PortalSystem/Create Portal (Enter — 굴리기 켬)")]
    private static void CreatePortalEnter() => CreatePortal(Portal.PortalAction.Enable, "Portal_Enter");

    [MenuItem("Tools/PortalSystem/Create Portal (Exit — 원래 이동으로 복귀)")]
    private static void CreatePortalExit() => CreatePortal(Portal.PortalAction.Disable, "Portal_Exit");

    private static void CreatePortal(Portal.PortalAction action, string name)
    {
        GameObject root = new GameObject(name);

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(Width, Opening, TriggerDepth);
        trigger.center = new Vector3(0f, Opening * 0.5f, 0f);

        root.AddComponent<Portal>().action = action;

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

        Debug.Log($"[Portal] {name}({action})을 만들었다. 입구·출구를 한 쌍으로 놓고 그 사이 구간을 평지로, " +
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

    // 계단 한 단 = 그 도형의 텀블 한 칸과 같은 깊이로 둬서 "칸마다 한 단씩 내려간다"는 감각이
    // 자연스럽다. 정육면체는 1.0 U, 정사면체는 0.8165 U(TetrahedronCellSize) — 두 도형의 셀
    // 크기가 다르므로 계단도 도형별로 따로 만들어야 한다(하나를 공유하면 둘 중 하나는 "칸마다
    // 한 단"이 어긋난다). 단 높이는 도형과 무관하다 — 아래 `StairStepHeight` 정정 참고.
    //
    // [단 높이는 0.35가 아니라 0.25×scale 미만이어야 한다 — 2026-08-20 정정]
    // 처음엔 0.35(낙하시간이 fallExitGrace 0.35초보다 짧다는 이유)로 뒀는데, 실제 플레이테스트에서
    // "움직임이 어색해지고 결국 기본 이동으로 풀린다"가 나왔다. 원인은 fallExitGrace가 아니라
    // `PlayerRollModeReceiver.GroundSnapCorrectionCap`(0.25×scale)이었다 — 그보다 큰 낙차는
    // 매 텀블 재접지가 절대 보정하지 않는 절벽 취급이라, 단마다 "텀블 → 뜸 → 낙하 → 재접지"가
    // 반복되는 스타카토 동작이 된다(전문가 검토로 확인). 캡 미만으로 두면 매 텀블이 재접지로
    // 매끈하게 흡수된다 — 이 상수 미만이 "계단"의 정의다. `GroundSnapCorrectionCap`은 스케일에만
    // 매인 상수라(도형 기하와 무관) 정사면체도 같은 상한을 쓴다 — 도형별로 따로 낮출 이유가 없다.
    private const float StairStepHeight = PlayerRollModeReceiver.GroundSnapCorrectionCap - 0.05f;
    private const float StairWidth = 2.0f;
    // 정사면체는 직진이 안 되고 좌우로 0.408U 흔들리며 나아가므로(PRD §9.1) 정육면체와 같은 폭(2.0U)을
    // 쓰면 계단에서도 그 흔들림이 벽에 스친다. 안전폭(2.04U, PRD §9.1)보다 넉넉히 — 아래
    // TetraGateWidth와 같은 값·같은 근거를 쓴다.
    private const float StairWidthTetra = 3.0f;
    private const int StairStepCount = 6;

    [MenuItem("Tools/PortalSystem/Create Test Stairs (Fall Grace)")]
    private static void CreateTestStairsCube() => CreateTestStairs(PlayerShapeStats.ShapeKind.Cube);

    [MenuItem("Tools/PortalSystem/Create Test Stairs (Fall Grace, Tetrahedron)")]
    private static void CreateTestStairsTetrahedron() => CreateTestStairs(PlayerShapeStats.ShapeKind.Tetrahedron);

    /// <summary>
    /// `Tools/PortalSystem/Create Test Stairs (Fall Grace[, Tetrahedron])` — 계단형 낙차 테스트용.
    /// 단 높이가 `GroundSnapCorrectionCap`(0.25×scale) 미만이라 매 텀블의 재접지가 흡수해야
    /// 정상이다 — 굴리기 모드로 내려가며 매끈하게 이어지는지, 뜸·되감기 없이 모드가 유지되는지
    /// 보는 것이 이 오브젝트의 용도다(어색함이 재현되면 이 캡 미만에서도 문제가 있다는 뜻이라 별도
    /// 원인을 찾아야 한다). 단 깊이만 <paramref name="shape"/>의 한 칸 이동거리를 따르고(`Create
    /// Tumble Floor`의 `GridMode`와 같은 판단 — 도형 파라미터 하나로 메서드를 합치고 얇은 MenuItem
    /// 래퍼만 늘린다), 나머지(단 높이·폭·단 수)는 공유한다. 게임플레이용이 아니라 테스트용이라
    /// 포탈을 같이 만들지 않는다 — 계단 위쪽에 별도로 입구 포탈을 놓고 굴리기 모드로 들어와서
    /// 확인해라.
    /// </summary>
    private static void CreateTestStairs(PlayerShapeStats.ShapeKind shape)
    {
        bool tetra = shape == PlayerShapeStats.ShapeKind.Tetrahedron;
        float stepDepth = tetra ? PlayerRollModeReceiver.TetrahedronCellSize : PlayerRollModeReceiver.CubeCellSize;
        float stepWidth = tetra ? StairWidthTetra : StairWidth;
        string suffix = tetra ? "_Tetra" : "";

        GameObject root = new GameObject($"TestStairs_FallGrace{suffix}");
        Material stepMaterial = CreateFrameMaterial();

        for (int i = 0; i < StairStepCount; i++)
        {
            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = $"Step_{i}";
            step.transform.SetParent(root.transform, false);
            step.transform.localPosition = new Vector3(0f, -StairStepHeight * i, stepDepth * i);
            step.transform.localScale = new Vector3(stepWidth, 1f, stepDepth);

            MeshRenderer renderer = step.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = stepMaterial;
        }

        if (SceneView.lastActiveSceneView != null)
            root.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(root, "Create Test Stairs");
        Selection.activeGameObject = root;

        Debug.Log($"[Portal] 테스트 계단을 만들었다({shape}) — 단 {StairStepCount}개, 단당 깊이 " +
                  $"{stepDepth:F4} U / 높이 {StairStepHeight} U(재접지 보정 상한 미만, 도형 공통). " +
                  "첫 단 앞쪽에 입구 포탈을 놓고 굴리기 모드로 내려가 매끈하게 이어지는지, 어색함 " +
                  "없이 모드가 유지되는지 확인해라.", root);
    }

    // 정사면체 전용 천장 게이트(PRD §9.2) — 정육면체 텀블 최고점(√2 = 1.4142 U)은 막고 정사면체
    // 텀블 최고점(1.2247 U)은 통과시키는 사이 값. 범위 1.25~1.40 U 중 중간보다 살짝 낮은 1.30 U를
    // 골랐다 — 정사면체 쪽 여유(1.30−1.2247=0.0753)가 정육면체 쪽 여유(1.4142−1.30=0.1142)보다
    // 빠듯한 게 맞는 방향이다(막아야 하는 쪽에 여유를 더 준다). **폭은 좁히지 않는다** — 정육면체
    // 전용 게이트(복도 폭 1.0~1.2U)와 정반대로, 정사면체는 직진이 안 되고 좌우로 0.408U 흔들리며
    // 나아가므로(PRD §9.1) 폭을 좁히면 그 흔들림 자체를 막아버린다. 게이트는 높이로만 걸러야 한다.
    private const float TetraGateClearance = 1.30f;
    private const float TetraGateLength = 3.0f;   // 진행 방향(로컬 X) 길이 — 몇 칸이 지나는 동안 낮은 천장 아래에 머물게
    private const float TetraGateWidth = 3.0f;    // 폭 방향(로컬 Z) — 안전폭(2.04U, PRD §9.1)보다 넉넉히
    private const float TetraGateThickness = 0.3f;

    /// <summary>
    /// `Tools/PortalSystem/Create Tetrahedron Ceiling Gate` — 정사면체 전용 천장 게이트(PRD §9.2)
    /// 테스트용 낮은 천장 하나(측벽 없음). 정육면체는 <i>걸어서는</i> 들어가지만(정지 시 전체 높이
    /// 1.0U) <i>굴러서는</i> 못 들어가고, 정사면체는 걷든 구르든 들어간다 — 그 차이를 보려면 바닥
    /// 높이가 일정한 평지 위에 놓아야 한다(계단처럼 바닥 높이가 계속 바뀌는 구간에 놓으면 구간 내내
    /// 천장 여유가 균일하지 않아 판정이 애매해진다. `Create Tumble Floor (Tetrahedron)`로 만든
    /// 평지 위에 놓는 것을 권한다). 씬 뷰 피벗의 Y를 "바닥 Y"로 가정하고 그 위
    /// <see cref="TetraGateClearance"/>만큼 띄운 높이에 게이트 밑면을 맞춘다 — 다른 바닥 위에
    /// 놓으면 배치 후 Y를 직접 맞춰라.
    /// </summary>
    [MenuItem("Tools/PortalSystem/Create Tetrahedron Ceiling Gate")]
    private static void CreateTetraCeilingGate()
    {
        GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gate.name = "PortalCorridor_TetraGate";
        gate.transform.localScale = new Vector3(TetraGateLength, TetraGateThickness, TetraGateWidth);

        MeshRenderer renderer = gate.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = CreateFrameMaterial();

        Vector3 floorPivot = SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.pivot
            : Vector3.zero;
        float centerY = floorPivot.y + TetraGateClearance + TetraGateThickness * 0.5f;
        gate.transform.position = new Vector3(floorPivot.x, centerY, floorPivot.z);

        Undo.RegisterCreatedObjectUndo(gate, "Create Tetrahedron Ceiling Gate");
        Selection.activeGameObject = gate;

        Debug.Log($"[Portal] 정사면체 전용 천장 게이트를 만들었다 — 바닥 위 {TetraGateClearance} U " +
                  "천장고(정사면체 텀블 최고점 1.2247U는 통과, 정육면체 텀블 최고점 1.4142U는 막힘). " +
                  $"폭({TetraGateWidth} U)은 안전폭(2.04U)보다 넉넉해 좌우 흔들림을 막지 않는다 — " +
                  "이 게이트는 높이만 거른다(PRD §9.2).", gate);
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
