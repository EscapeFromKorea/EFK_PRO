using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > FrictionStickerSystem > Create Friction Test Playground.
/// 마찰 스티커를 경사면에서 비교하는 임시 테스트 리그. 전부 루트 하나의 자식이라 루트만 지우면 정리된다.
///
/// 구성(루트 로컬 기준):
///  - Floor : 바닥. 윗면이 로컬 y=0.
///  - Ramp_StickerSurface : 30° 경사면. 낮은 끝이 바닥에 닿고 높은 끝은 TopPad로 이어진다. StickerSurface 부착.
///  - TopPad : 경사 위 평평한 지점(경사에 붙어 있음).
///
/// [왜 30°이고 왜 정육면체로 보나]
/// PlayerMover는 입력이 없고 접지 상태면 평지에서 수평 속도를 0으로 만든다(제자리 정지). 그래서
/// "평지 활강 거리"로는 마찰 차이가 안 보인다. 대신 <b>경사면 위에 정육면체를 올려놓고 키를 떼서</b>
/// 정지 마찰만으로 버티는지 본다. 30°는 tan30°≈0.577이라:
///  - 스티커 없음(정육면체 마찰 0.5)  → 0.5 &lt; 0.577, 천천히 미끄러져 내려감
///  - 미끄럼 스티커(Multiply≈0.5×0.05) → 빠르게 주르륵
///  - 벨크로 스티커(Maximum=1.2)        → 그 자리에 붙어 안 움직임
/// 세 결과가 확연히 갈린다. 구(Sphere)로도 해보면 경사를 내려가는 속도가 상태마다 다르다.
/// </summary>
public static class FrictionTestPlaygroundMenuItem
{
    private const float RampAngleDeg = 30f;

    [MenuItem("Tools/FrictionStickerSystem/Create Friction Test Playground")]
    private static void CreatePlayground()
    {
        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;
        origin.y = 0f;

        GameObject root = new GameObject("FrictionTestPlayground");
        Undo.RegisterCreatedObjectUndo(root, "Create Friction Test Playground");
        root.transform.position = origin;

        MakeBox(root.transform, "Floor",
            new Vector3(-3f, -0.25f, 0f), Quaternion.identity, new Vector3(20f, 0.5f, 8f));

        // 경사면: 길이 8(로컬 X), 30°. 낮은 끝(-X) 밑면을 바닥 윗면(y=0)에 맞춘다.
        float half = 4f;
        float rad = RampAngleDeg * Mathf.Deg2Rad;
        Vector3 rampCenter = new Vector3(0f, half * Mathf.Sin(rad) + 0.2f, 0f);
        GameObject ramp = MakeBox(root.transform, "Ramp_StickerSurface",
            rampCenter, Quaternion.Euler(0f, 0f, RampAngleDeg), new Vector3(8f, 0.4f, 4.5f));
        StickerSurface surface = ramp.AddComponent<StickerSurface>();
        surface.targetCollider = ramp.GetComponent<Collider>();

        // 경사 높은 끝(+X)에 이어 붙는 출발 발판.
        float hx = half * Mathf.Cos(rad);
        float hy = rampCenter.y + half * Mathf.Sin(rad);
        MakeBox(root.transform, "TopPad",
            new Vector3(hx + 1.6f, hy + 0.1f, 0f), Quaternion.identity, new Vector3(4f, 0.3f, 4.5f));

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[FrictionStickerSystem] 테스트 리그 생성 완료. 'FrictionTestPlayground'를 플레이어 근처로 " +
                  "옮긴 뒤 ▶ Play → Tab으로 정육면체 선택 → 경사면 한가운데에 올려놓고 키를 뗀다 → " +
                  "스티커 없음/미끄럼(F)/벨크로(Q+F) 상태에서 각각 버티는지·미끄러지는지 본다.");
    }

    private static GameObject MakeBox(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = scale;
        return go;
    }
}
