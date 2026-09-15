using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > CubeDockSystem 메뉴. 정육면체 블록 도킹 씬 세팅용.
///  - Create Controller: 튜닝용 CubeDockController 생성(없어도 런타임 자동 생성됨).
///
/// 도킹 대상 블록은 SnapBlockSystem의 SnapBlock을 그대로 쓴다
/// (Tools > SnapBlockSystem > Create Snap Block).
/// </summary>
public static class CubeDockMenuItem
{
    [MenuItem("Tools/CubeDockSystem/Create Controller")]
    private static void CreateController()
    {
        CubeDockController existing = Object.FindObjectOfType<CubeDockController>();
        if (existing != null)
        {
            Debug.LogWarning("[CubeDockSystem] 씬에 이미 CubeDockController가 있습니다.");
            Selection.activeObject = existing;
            return;
        }

        GameObject go = new GameObject("CubeDockController");
        Undo.RegisterCreatedObjectUndo(go, "Create Cube Dock Controller");
        go.AddComponent<CubeDockController>();

        Selection.activeGameObject = go;
        Debug.Log("[CubeDockSystem] CubeDockController 생성 완료");
    }
}
