// 이 파일은 반드시 "Editor" 폴더 안에 있어야 한다. UnityEditor를 참조하므로 밖에 두면 플레이어
// 빌드에서 컴파일 에러가 난다(에디터에서는 멀쩡해 보여 빌드할 때까지 안 드러난다). 원래
// JumpSystem 루트에 있던 것을 2026-07-26에 옮겼다 — 다른 기믹의 에디터 도구와 같은 배치다.

using UnityEngine;
using UnityEditor;

public class JumpPadCreator
{
    // 다른 기믹과 같은 "Tools/<기믹>/<동작>" 형태로 맞춘다. 리프 항목("Tools/Create JumpPad")으로 두면
    // 같은 이름 아래 항목이 하나라도 생기는 순간 경고 없이 사라진다(ZeroGravityBubble에서 실제로 겪음).
    [MenuItem("Tools/JumpPad/Create Jump Pad")]
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