using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > ZoneEntrySystem > Create Zone Entry Detector` — SceneView 중앙에 보이지 않는 트리거 박스
/// 감지기를 만든다. 상세: docs/PRD/ZoneEntry.md. 렌더러는 만들지 않는다(구역은 게임 화면에 안 보여야
/// 한다) — 에디터에서 선택하면 기즈모로 경계가 보인다.
/// </summary>
public static class ZoneEntryMenuItem
{
    [MenuItem("Tools/ZoneEntrySystem/Create Zone Entry Detector")]
    private static void Create()
    {
        GameObject go = new GameObject("ZoneEntryDetector");
        go.transform.position = SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.pivot
            : Vector3.zero;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 3f, 4f);

        go.AddComponent<ZoneEntryDetector>();

        Undo.RegisterCreatedObjectUndo(go, "Create Zone Entry Detector");
        Selection.activeGameObject = go;
        Debug.Log("[ZoneEntrySystem] 구역 진입 감지기를 만들었다 — BoxCollider 크기·위치를 조정하고 " +
                  "OnValidEntry를 각 함정/발사구/레이저의 Activate()에 연결해라.");
    }
}
