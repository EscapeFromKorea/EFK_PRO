using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > FrictionStickerSystem 메뉴. 마찰 스티커 시스템의 씬 세팅을 한 번에 배선한다.
///  - Create Controller: 튜닝용 FrictionStickerController를 씬에 만든다(없어도 런타임 자동 생성되지만,
///    인스펙터에서 마찰값·키를 조정하려면 이걸로 만들어 둔다).
///  - Add Sticker Surface To Selection: 선택한 오브젝트(콜라이더 보유)에 StickerSurface를 붙인다.
///  - Create Demo Sticker Surface: 콜라이더 + StickerSurface가 붙은 데모 큐브를 씬 뷰 중앙에 만든다.
/// </summary>
public static class FrictionStickerMenuItem
{
    [MenuItem("Tools/FrictionStickerSystem/Create Controller")]
    private static void CreateController()
    {
        FrictionStickerController existing = Object.FindObjectOfType<FrictionStickerController>();
        if (existing != null)
        {
            Debug.LogWarning("[FrictionStickerSystem] 씬에 이미 FrictionStickerController가 있습니다.");
            Selection.activeObject = existing;
            return;
        }

        GameObject go = new GameObject("FrictionStickerController");
        Undo.RegisterCreatedObjectUndo(go, "Create Friction Sticker Controller");
        go.AddComponent<FrictionStickerController>();

        Selection.activeGameObject = go;
        Debug.Log("[FrictionStickerSystem] FrictionStickerController 생성 완료");
    }

    [MenuItem("Tools/FrictionStickerSystem/Add Sticker Surface To Selection")]
    private static void AddSurfaceToSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[FrictionStickerSystem] 먼저 스티커 부착 표면으로 만들 오브젝트를 선택하세요.");
            return;
        }

        int added = 0;
        foreach (GameObject go in selected)
        {
            if (go.GetComponent<Collider>() == null)
            {
                Debug.LogWarning($"[FrictionStickerSystem] '{go.name}'에 Collider가 없어 건너뜁니다.");
                continue;
            }
            if (go.GetComponent<StickerSurface>() != null) continue;

            Undo.AddComponent<StickerSurface>(go);
            added++;
        }

        Debug.Log($"[FrictionStickerSystem] StickerSurface {added}개 추가 완료");
    }

    [MenuItem("Tools/FrictionStickerSystem/Create Demo Sticker Surface")]
    private static void CreateDemoSurface()
    {
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "StickerSurface_Demo";
        Undo.RegisterCreatedObjectUndo(go, "Create Demo Sticker Surface");
        go.transform.position = spawnPos;
        go.transform.localScale = new Vector3(3f, 0.5f, 3f);

        StickerSurface surface = go.AddComponent<StickerSurface>();
        surface.targetCollider = go.GetComponent<Collider>();

        Selection.activeGameObject = go;
        Debug.Log("[FrictionStickerSystem] 데모 스티커 표면 생성 완료");
    }
}
