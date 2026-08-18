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

    private static Material CreateFrameMaterial()
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");
        return new Material(shader) { color = new Color(0.55f, 0.45f, 0.85f) };
    }
}
