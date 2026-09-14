using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > InertiaCapacitorSystem 메뉴. 관성 축전기 씬 세팅용.
///  - Create Capacitor: 1×1×1 박스 + BoxCollider + InertiaCapacitor 생성(정적 콜라이더, Rigidbody 없음).
///  - Add Capacitor To Selection: 선택한 오브젝트(Collider 보유)에 InertiaCapacitor 부착.
///  - Create Demo Rig (+ Rotating Platform): 캡시터 + WindupAxleSystem의 회전판을 나란히 놓고
///    Output Targets까지 미리 배선해, "충전이 실제로 장치를 돌리는지"를 클릭 한 번으로 테스트
///    가능한 상태로 만든다(QA 편의용, mnppi 요청 2026-09-14).
///
/// 축전기 자신은 Rigidbody가 없어 부딪혀도 안 밀린다. 부딪히는 쪽에 non-kinematic Rigidbody가
/// 있으면 OnCollisionEnter가 정상 발생한다.
/// </summary>
public static class InertiaCapacitorMenuItem
{
    [MenuItem("Tools/InertiaCapacitorSystem/Create Capacitor")]
    private static void CreateCapacitor()
    {
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "InertiaCapacitor";
        Undo.RegisterCreatedObjectUndo(go, "Create Inertia Capacitor");
        go.transform.position = spawnPos;

        InertiaCapacitor cap = go.AddComponent<InertiaCapacitor>();
        cap.bodyRenderer = go.GetComponent<Renderer>();

        Selection.activeGameObject = go;
        Debug.Log("[InertiaCapacitorSystem] InertiaCapacitor 생성 완료");
    }

    [MenuItem("Tools/InertiaCapacitorSystem/Add Capacitor To Selection")]
    private static void AddToSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[InertiaCapacitorSystem] 먼저 축전기로 만들 오브젝트를 선택하세요.");
            return;
        }

        int added = 0;
        foreach (GameObject go in selected)
        {
            if (go.GetComponent<Collider>() == null)
            {
                Debug.LogWarning($"[InertiaCapacitorSystem] '{go.name}'에 Collider가 없어 건너뜁니다.");
                continue;
            }
            if (go.GetComponent<InertiaCapacitor>() != null) continue;

            InertiaCapacitor cap = Undo.AddComponent<InertiaCapacitor>(go);
            if (cap.bodyRenderer == null) cap.bodyRenderer = go.GetComponent<Renderer>();
            added++;
        }

        Debug.Log($"[InertiaCapacitorSystem] InertiaCapacitor {added}개 추가 완료");
    }

    // WindupAxleSystem/Editor/WindupAxleMenuItem.CreateRotatingPlatform과 같은 레시피를 재사용한다
    // (십자 스포크 2개 — 매끈한 원판만으로는 도는 게 눈에 안 띔). WindupAxleSystem 파일은 건드리지
    // 않고, 그쪽이 공개한 RotatingPlatform 컴포넌트 타입만 가져다 쓴다.
    [MenuItem("Tools/InertiaCapacitorSystem/Create Demo Rig (+ Rotating Platform)")]
    private static void CreateDemoRig()
    {
        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        // 캡시터.
        GameObject capGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        capGo.name = "InertiaCapacitor_Demo";
        Undo.RegisterCreatedObjectUndo(capGo, "Create Inertia Capacitor Demo Rig");
        capGo.transform.position = origin;

        InertiaCapacitor cap = capGo.AddComponent<InertiaCapacitor>();
        cap.bodyRenderer = capGo.GetComponent<Renderer>();
        cap.dischargeKey = KeyCode.X; // 다른 시스템과 안 겹치는 키(E/V/Q/C/G/F/T는 전부 선점됨).

        // 회전판 — 4유닛 옆. 캡시터에 부딪혀 충전한 뒤 걸어가서 X로 방출하는 동선을 만든다.
        Vector3 platformPos = origin + Vector3.right * 4f;
        GameObject platformGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        platformGo.name = "RotatingPlatform_Demo";
        Undo.RegisterCreatedObjectUndo(platformGo, "Create Inertia Capacitor Demo Rig");
        platformGo.transform.position = platformPos;
        platformGo.transform.localScale = new Vector3(3f, 0.2f, 3f);

        RotatingPlatform platform = platformGo.AddComponent<RotatingPlatform>();
        platformGo.GetComponent<Renderer>().sharedMaterial =
            new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.55f, 0.6f) };
        CreateSpoke(platformGo.transform, new Vector3(2.7f / 3f, 0.15f / 0.2f, 0.2f / 3f));
        CreateSpoke(platformGo.transform, new Vector3(0.2f / 3f, 0.15f / 0.2f, 2.7f / 3f));

        // 배선 — Output Targets에 회전판을 미리 연결해둔다(직접 드래그할 필요 없게).
        cap.outputTargets = new MonoBehaviour[] { platform };

        Selection.objects = new Object[] { capGo, platformGo };
        Debug.Log("[InertiaCapacitorSystem] 데모 리그 생성 완료 — 캡시터에 부딪혀 충전한 뒤 " +
                  "'X' 키로 방출해보세요. 회전판은 버스트(OnCrankSwing)에만 반응하고, 연속 충전 " +
                  "동안은 안 돕니다(회전판 자체 설계 — 기본 releaseDelay 3초 뒤 걸음 단위로 돕니다).");
    }

    private static void CreateSpoke(Transform parent, Vector3 localScale)
    {
        GameObject spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spoke.name = "Spoke";
        Object.DestroyImmediate(spoke.GetComponent<Collider>());
        spoke.transform.SetParent(parent, false);
        spoke.transform.localPosition = new Vector3(0f, 1.05f, 0f); // 디스크 윗면 바로 위.
        spoke.transform.localScale = localScale;
        spoke.GetComponent<Renderer>().sharedMaterial =
            new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.15f, 0.1f) };
    }
}
