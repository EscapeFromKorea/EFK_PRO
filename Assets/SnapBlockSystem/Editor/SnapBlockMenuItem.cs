using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > SnapBlockSystem 메뉴. 딱딱 블록 씬 세팅용.
///  - Create Snap Block: 1×1×1 박스 + Rigidbody + BoxCollider + SnapBlock 생성.
///  - Add Snap Block To Selection: 선택한 오브젝트(BoxCollider 보유)에 SnapBlock 부착.
///  - Create Controller: 튜닝용 SnapBlockController 생성(없어도 런타임 자동 생성됨).
/// </summary>
public static class SnapBlockMenuItem
{
    [MenuItem("Tools/SnapBlockSystem/Create Snap Block")]
    private static void CreateSnapBlock()
    {
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "SnapBlock";
        Undo.RegisterCreatedObjectUndo(block, "Create Snap Block");
        block.transform.position = spawnPos;

        Rigidbody rb = block.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        block.AddComponent<SnapBlock>();

        Selection.activeGameObject = block;
        Debug.Log("[SnapBlockSystem] SnapBlock 생성 완료");
    }

    [MenuItem("Tools/SnapBlockSystem/Add Snap Block To Selection")]
    private static void AddSnapBlockToSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[SnapBlockSystem] 먼저 블록으로 만들 오브젝트를 선택하세요.");
            return;
        }

        int added = 0;
        foreach (GameObject go in selected)
        {
            if (go.GetComponent<BoxCollider>() == null)
            {
                Debug.LogWarning($"[SnapBlockSystem] '{go.name}'에 BoxCollider가 없어 건너뜁니다.");
                continue;
            }
            if (go.GetComponent<SnapBlock>() != null) continue;

            if (go.GetComponent<Rigidbody>() == null)
                Undo.AddComponent<Rigidbody>(go);
            Undo.AddComponent<SnapBlock>(go);
            added++;
        }

        Debug.Log($"[SnapBlockSystem] SnapBlock {added}개 추가 완료");
    }

    [MenuItem("Tools/SnapBlockSystem/Create Controller")]
    private static void CreateController()
    {
        SnapBlockController existing = Object.FindObjectOfType<SnapBlockController>();
        if (existing != null)
        {
            Debug.LogWarning("[SnapBlockSystem] 씬에 이미 SnapBlockController가 있습니다.");
            Selection.activeObject = existing;
            return;
        }

        GameObject go = new GameObject("SnapBlockController");
        Undo.RegisterCreatedObjectUndo(go, "Create Snap Block Controller");
        go.AddComponent<SnapBlockController>();

        Selection.activeGameObject = go;
        Debug.Log("[SnapBlockSystem] SnapBlockController 생성 완료");
    }
}
