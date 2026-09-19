using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > BlockCarrySystem 메뉴. 경량 도형 블록 들어올리기 씬 세팅용.
///  - Create Controller: 튜닝용 BlockCarryController 생성(없어도 런타임 자동 생성됨).
///
/// 들 대상 블록은 SnapBlockSystem의 SnapBlock을 그대로 쓴다
/// (Tools > SnapBlockSystem > Create Snap Block).
/// </summary>
public static class BlockCarryMenuItem
{
    [MenuItem("Tools/BlockCarrySystem/Create Controller")]
    private static void CreateController()
    {
        BlockCarryController existing = Object.FindObjectOfType<BlockCarryController>();
        if (existing != null)
        {
            Debug.LogWarning("[BlockCarrySystem] 씬에 이미 BlockCarryController가 있습니다.");
            Selection.activeObject = existing;
            return;
        }

        GameObject go = new GameObject("BlockCarryController");
        Undo.RegisterCreatedObjectUndo(go, "Create Block Carry Controller");
        go.AddComponent<BlockCarryController>();

        Selection.activeGameObject = go;
        Debug.Log("[BlockCarrySystem] BlockCarryController 생성 완료");
    }
}
