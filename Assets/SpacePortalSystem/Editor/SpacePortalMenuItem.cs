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
            // 바닥/천장(법선이 월드 up과 거의 평행)이면 up 힌트로 up을 쓰면 LookRotation이
            // 퇴화한다 — 그때만 forward를 힌트로 바꾼다.
            Vector3 upHint = Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            rot = Quaternion.LookRotation(hit.normal, upHint);
        }
        else
        {
            pos = ScenePivot();
            rot = Quaternion.identity;
            Debug.LogWarning("[SpacePortalSystem] 씬 뷰 카메라 앞에서 벽을 못 찾아 기본 회전(identity)으로 " +
                              "생성했다 — 청록 화살표(바깥 법선)가 방 안쪽을 향하는지 직접 확인해라.");
        }

        // localScale은 항상 (1,1,1)로 유지한다 — 크기는 BoxCollider.size로만 낸다(RailCart 루트와
        // 같은 규약, PortalSurface.cs 상단 주석 참고). 전단 왜곡 없이 조준 판정 수식이 그대로 맞는다.
        GameObject root = new GameObject("PortalSurface");
        Undo.RegisterCreatedObjectUndo(root, "Create Portal Surface");
        root.transform.SetPositionAndRotation(pos, rot);

        Vector3 size = new Vector3(2.4f, 3.0f, 0.2f);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        box.isTrigger = false;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(visual, "Create Portal Surface");
        visual.name = "PortalSurface_Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = size;
        visual.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateSurfaceMaterial();

        root.AddComponent<PortalSurface>();

        Selection.activeGameObject = root;
        Debug.Log("[SpacePortalSystem] PortalSurface 생성 완료 — 시각 자식(PortalSurface_Visual)은 " +
                  "BoxCollider.size를 씬에서 바꿔도 자동으로 따라오지 않는다(그레이박스 한계, 필요시 손으로 맞춘다).");
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
