using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > AccelPad 메뉴로 씬에 부스터 패드를 생성한다.
/// </summary>
public static class AccelPadMenuItem
{
    // 리프가 아니라 서브메뉴 아래에 둔다. 같은 이름을 리프 항목이면서 서브메뉴로 동시에 쓸 수 없어서,
    // 나중에 "Tools/AccelPad/무엇" 항목이 하나라도 추가되면 리프였던 이 항목이 경고 없이 사라진다
    // (ZeroGravityBubble에서 실제로 겪음).
    [MenuItem("Tools/AccelPad/Create Accel Pad")]
    private static void CreateAccelPad()
    {
        GameObject pad = new GameObject("AccelPad", typeof(BoxCollider), typeof(AccelPad));
        pad.transform.localScale = Vector3.one;

        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
        {
            spawnPos = SceneView.lastActiveSceneView.pivot;
        }
        pad.transform.position = spawnPos;

        BoxCollider col = pad.GetComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(2f, 0.2f, 2f);

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "AccelPad_Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(pad.transform, false);
        visual.transform.localScale = new Vector3(2f, 0.2f, 2f);
        Renderer renderer = visual.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0f, 0f, 0.5f); // 남청색(Navy)
        renderer.sharedMaterial = mat;

        Undo.RegisterCreatedObjectUndo(pad, "Create AccelPad");
        Selection.activeGameObject = pad;
    }
}