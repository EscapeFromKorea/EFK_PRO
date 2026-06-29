using UnityEngine;
using UnityEditor;

public class JumpPadCreator
{
    [MenuItem("Tools/Create JumpPad")]
    static void CreateJumpPad()
    {
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "JumpPad";

        // SceneView 중앙에 생성
        if (SceneView.lastActiveSceneView != null)
        pad.transform.position = SceneView.lastActiveSceneView.pivot;

        // 크기 조정 (납작한 판 형태)
        pad.transform.localScale = new Vector3(2f, 0.2f, 2f);

        // JumpPad 스크립트 추가
        pad.AddComponent<JumpPad>();

        //색깔 설정
        Renderer renderer = pad.GetComponent<Renderer>();
        renderer.material.color = Color.black;

        // 하이어라키에서 선택 상태로
        Selection.activeGameObject = pad;
        Undo.RegisterCreatedObjectUndo(pad, "Create JumpPad");

        Debug.Log("JumpPad 생성 완료");
    }
}