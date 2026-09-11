using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools &gt; SpacePortalSystem &gt; ... 씬 배치 편의 메뉴(PRD §4.7). 에너지볼 초기 배치와 가변
/// 전이 패널 그레이박스 생성을 담당한다. 실제 씬(SampleScene.unity)에 배치/배선하는 것은 이
/// 세션의 범위 밖이다 — 이 메뉴는 다음 세션에서 씬 배치를 할 때 쓴다.
/// </summary>
public static class SpacePortalMenuItem
{
    [MenuItem("Tools/SpacePortalSystem/Create Energy Ball (Orange)")]
    private static void CreateOrangeBall() => CreateEnergyBall(EnergyColor.Orange);

    [MenuItem("Tools/SpacePortalSystem/Create Energy Ball (Blue)")]
    private static void CreateBlueBall() => CreateEnergyBall(EnergyColor.Blue);

    [MenuItem("Tools/SpacePortalSystem/Create Portal Surface")]
    private static void CreatePortalSurfaceMenu() => CreatePortalSurface();

    [MenuItem("Tools/SpacePortalSystem/Create Panel Lever")]
    private static void CreatePanelLeverMenu()
    {
        Vector3 origin = ScenePivot();
        MovablePortalPanel nearest = FindNearestPanel(origin);
        GameObject body = BuildPanelLever(origin, nearest);
        Undo.RegisterCreatedObjectUndo(body, "Create Panel Lever");

        Selection.activeGameObject = body;
        Debug.Log(nearest != null
            ? $"[SpacePortalSystem] PanelLever 생성 완료 — 패널 '{nearest.name}'에 연결했습니다. " +
              "플레이어가 몸통을 미는 방향으로 패널이 구동됩니다."
            : "[SpacePortalSystem] PanelLever 생성 완료. 씬에 MovablePortalPanel이 없어 연결하지 " +
              "못했습니다 — 패널을 만든 뒤 PanelLever.panel에 직접 꽂으세요.");
    }

    /// <summary>격리된 씬 생성기(씬 이식 기법)에서도 그대로 호출할 수 있도록 위치/대상 패널을
    /// 매개변수로 받는 형태로 분리했다. 메뉴 항목은 씬 뷰 피벗 + 최근접 패널을 넘기는 얇은
    /// 래퍼일 뿐이다.</summary>
    public static GameObject BuildPanelLever(Vector3 position, MovablePortalPanel panel)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "PanelLever_Body";
        body.transform.position = position;
        body.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
        body.GetComponent<Renderer>().sharedMaterial = LoadOrCreateLeverMaterial("Body", new Color(0.45f, 0.45f, 0.5f));

        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "PanelLever_Arm";
        arm.transform.SetParent(body.transform, false);
        // 손잡이 길이 — 처음엔 1.2였는데 플레이테스트에서 너무 짧아 회전이 잘 안 보인다는
        // 피드백을 받아 늘렸다(2026-09-11). 근쪽 끝(-0.1) 위치는 유지하고 먼 쪽만 늘어난다.
        arm.transform.localPosition = new Vector3(0f, 0.6f, 0.9f);
        arm.transform.localScale = new Vector3(0.15f, 0.15f, 2f);
        arm.GetComponent<Renderer>().sharedMaterial = LoadOrCreateLeverMaterial("Arm", new Color(0.8f, 0.35f, 0.3f));
        Object.DestroyImmediate(arm.GetComponent<Collider>()); // 충돌 판정은 body가 담당, arm은 시각 전용

        PanelLever lever = body.AddComponent<PanelLever>();
        lever.leverArm = arm.transform;
        lever.panel = panel;
        return body;
    }

    private static MovablePortalPanel FindNearestPanel(Vector3 from)
    {
        MovablePortalPanel best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (MovablePortalPanel p in Object.FindObjectsOfType<MovablePortalPanel>())
        {
            float sqr = (p.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = p;
        }
        return best;
    }

    private static Material LoadOrCreateLeverMaterial(string key, Color color)
    {
        const string dir = "Assets/SpacePortalSystem/Materials";
        string path = $"{dir}/PanelLever_{key}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        EnsureFolder(dir);
        mat = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    [MenuItem("Tools/SpacePortalSystem/Add Movable Panel To Selected")]
    private static void AddMovablePanelToSelected()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null || go.GetComponent<PortalSurface>() == null)
        {
            Debug.LogWarning("[SpacePortalSystem] PortalSurface가 붙은 오브젝트를 먼저 선택하세요.");
            return;
        }
        Undo.AddComponent<Rigidbody>(go);
        Undo.AddComponent<MovablePortalPanel>(go);
        Debug.Log($"[SpacePortalSystem] {go.name}에 MovablePortalPanel 추가 완료 — Inspector에서 " +
                  "mode(Rail/Pivot)와 해당 필드를 채워라.");
    }

    private static void CreateEnergyBall(EnergyColor color)
    {
        Vector3 pos = ScenePivot();

        GameObject root = new GameObject($"EnergyBall_{color}");
        Undo.RegisterCreatedObjectUndo(root, "Create Energy Ball");
        root.transform.position = pos;

        // 트리거는 근접 판정 범위다(월드 단위) — 시각 크기와 독립적으로 둔다(AccelPad와 같은 이유).
        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.5f;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Undo.RegisterCreatedObjectUndo(visual, "Create Energy Ball");
        visual.name = "EnergyBall_Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * 0.4f;
        visual.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateBallMaterial(color);

        EnergyBall ball = root.AddComponent<EnergyBall>();
        ball.color = color;

        Selection.activeGameObject = root;
        Debug.Log($"[SpacePortalSystem] EnergyBall_{color} 생성 완료");
    }

    private static void CreatePortalSurface()
    {
        // 씬 뷰 카메라가 보고 있는 벽에 자동 정렬한다 — 화살표(바깥 법선)가 벽 속을 향하게
        // 생성돼 포탈 시각/출구 방향이 뒤집히는 실수가 반복돼(2026-09-11 실측, 파란 패널에서
        // 확인) 자리를 잡는 시점에 아예 못 틀리게 했다. 벽을 못 찾으면 기존처럼 씬 피벗 +
        // identity 회전으로 폴백하고, 그땐 직접 화살표를 확인하라고 경고한다.
        Vector3 pos;
        Quaternion rot;
        if (TryRaycastSceneViewSurface(out RaycastHit hit))
        {
            pos = hit.point;
            rot = SurfaceRotationFromNormal(hit.normal);
        }
        else
        {
            pos = ScenePivot();
            rot = Quaternion.identity;
            Debug.LogWarning("[SpacePortalSystem] 씬 뷰 카메라 앞에서 벽을 못 찾아 기본 회전(identity)으로 " +
                              "생성했다 — 청록 화살표(바깥 법선)가 방 안쪽을 향하는지 직접 확인해라.");
        }

        GameObject root = BuildPortalSurface(pos, rot, new Vector3(2.4f, 3.0f, 0.2f));
        Undo.RegisterCreatedObjectUndo(root, "Create Portal Surface");

        Selection.activeGameObject = root;
        Debug.Log("[SpacePortalSystem] PortalSurface 생성 완료 — 시각 자식(PortalSurface_Visual)은 " +
                  "BoxCollider.size를 씬에서 바꿔도 자동으로 따라오지 않는다(그레이박스 한계, 필요시 손으로 맞춘다).");
    }

    /// <summary>법선 하나로 패널의 배치 회전을 정한다 — 바닥/천장(법선이 world up과 거의 평행)이면
    /// up 힌트로 up을 쓰면 LookRotation이 퇴화하므로 그때만 forward를 힌트로 바꾼다. 씬 이식
    /// 기법에서 바닥용(Rail) 패널을 만들 때도 그대로 재사용한다.</summary>
    public static Quaternion SurfaceRotationFromNormal(Vector3 normal)
    {
        Vector3 upHint = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        return Quaternion.LookRotation(normal, upHint);
    }

    /// <summary>PortalSurface 그레이박스(솔리드 콜라이더 + 시각 큐브 + PortalSurface 컴포넌트)만
    /// 만든다. 격리된 씬 생성기(씬 이식 기법)에서도 그대로 호출할 수 있도록 위치/회전/크기를
    /// 매개변수로 받는다 — localScale은 항상 (1,1,1)로 유지하고 크기는 BoxCollider.size로만
    /// 낸다(RailCart 루트와 같은 규약).</summary>
    public static GameObject BuildPortalSurface(Vector3 pos, Quaternion rot, Vector3 size)
    {
        GameObject root = new GameObject("PortalSurface");
        root.transform.SetPositionAndRotation(pos, rot);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        box.isTrigger = false;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "PortalSurface_Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = size;
        visual.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateSurfaceMaterial();

        root.AddComponent<PortalSurface>();
        return root;
    }

    /// <summary>PortalSurface + 킨네마틱 Rigidbody + 라이더 센서(로컬 +Z, PortalSurface 바깥
    /// 법선과 같은 축) + MovablePortalPanel까지 갖춘 완성형 가변 전이 패널을 만든다. mode별
    /// 필드(Rail의 startPose/endPose, Pivot의 pivotPoint 등)는 호출자가 반환된 컴포넌트에 이어서
    /// 채운다.</summary>
    public static MovablePortalPanel BuildMovablePortalPanel(Vector3 pos, Quaternion rot, Vector3 size)
    {
        GameObject root = BuildPortalSurface(pos, rot, size);

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        BoxCollider sensor = root.AddComponent<BoxCollider>();
        sensor.isTrigger = true;
        sensor.size = size + new Vector3(0f, 0f, 0.2f);
        sensor.center = new Vector3(0f, 0f, size.z * 0.5f + 0.1f);

        MovablePortalPanel panel = root.AddComponent<MovablePortalPanel>();
        panel.riderSensor = sensor;
        return panel;
    }

    private static Material LoadOrCreateBallMaterial(EnergyColor color)
    {
        const string dir = "Assets/SpacePortalSystem/Materials";
        string path = $"{dir}/EnergyBall_{color}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        EnsureFolder(dir);
        mat = new Material(Shader.Find("Standard"));
        mat.color = color == EnergyColor.Orange ? new Color(1f, 0.55f, 0.1f) : new Color(0.25f, 0.55f, 1f);
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static Material LoadOrCreateSurfaceMaterial()
    {
        const string dir = "Assets/SpacePortalSystem/Materials";
        string path = $"{dir}/PortalSurface.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        EnsureFolder(dir);
        mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.4f, 0.9f, 0.6f, 1f);
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static void EnsureFolder(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        int slash = dir.LastIndexOf('/');
        AssetDatabase.CreateFolder(dir.Substring(0, slash), dir.Substring(slash + 1));
    }

    private static Vector3 ScenePivot() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    // 씬 뷰 카메라 위치에서 피벗을 향해 쏴서 맞은 표면을 찾는다(카메라→피벗 방향 — 씬 뷰는 항상
    // 피벗을 중심에 두고 궤도 회전하므로, 사용자가 "보고 있는" 벽과 거의 일치한다).
    private static bool TryRaycastSceneViewSurface(out RaycastHit hit)
    {
        hit = default;
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null) return false;

        Vector3 origin = sv.camera != null ? sv.camera.transform.position : sv.pivot - sv.rotation * Vector3.forward * sv.size;
        Vector3 dir = (sv.pivot - origin).normalized;
        if (dir == Vector3.zero) return false;

        return Physics.Raycast(origin, dir, out hit, 200f);
    }
}
