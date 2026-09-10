using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > InertiaCapacitorSystem 메뉴. 관성 축전기 씬 세팅용.
///  - Create Capacitor: 1×1×1 박스 + BoxCollider + InertiaCapacitor 생성(정적 콜라이더, Rigidbody 없음).
///  - Add Capacitor To Selection: 선택한 오브젝트(Collider 보유)에 InertiaCapacitor 부착.
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
}
