using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > RotatingPlateSystem > Create Rotating Plate 메뉴. 납작한 판 + Rigidbody + HingeJoint +
/// RotatingPlate + 와이어 앵커 마커 2개까지 배선해, 씬에 놓자마자 축·범위만 튜닝하면 되게 한다.
/// (WindZoneMenuItem 스타일)
/// </summary>
public static class RotatingPlateMenuItem
{
    [MenuItem("Tools/RotatingPlateSystem/Create Rotating Plate")]
    private static void CreateRotatingPlate()
    {
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "RotatingPlate";
        Undo.RegisterCreatedObjectUndo(plate, "Create Rotating Plate");
        plate.transform.position = spawnPos;
        plate.transform.localScale = new Vector3(3f, 0.2f, 1.6f);

        Rigidbody rb = plate.AddComponent<Rigidbody>();
        rb.mass = 2f;
        rb.useGravity = false;
        rb.angularDrag = 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        HingeJoint hinge = plate.AddComponent<HingeJoint>();
        hinge.axis = Vector3.right;
        hinge.connectedBody = null;
        hinge.useMotor = false;
        hinge.useSpring = false;
        hinge.useLimits = true;
        hinge.limits = new JointLimits { min = -75f, max = 75f };

        plate.AddComponent<RotatingPlate>();

        CreateAnchor(plate.transform, "WireAnchor_A", new Vector3(-1.2f, 0.4f, 0f));
        CreateAnchor(plate.transform, "WireAnchor_B", new Vector3(1.2f, 0.4f, 0f));

        Selection.activeGameObject = plate;
        Debug.Log("[RotatingPlateSystem] RotatingPlate 생성 완료 (축·회전 범위는 인스펙터에서 튜닝)");
    }

    // 와이어(실타래) 스윙 지점. DreamThreadSystem의 순수 위치 마커 컴포넌트를 그대로 재사용한다
    // (파일 수정 아님 — public 컴포넌트를 자식에 붙이는 것). 콜라이더 없음.
    private static void CreateAnchor(Transform parent, string name, Vector3 localPos)
    {
        GameObject anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = localPos;
        anchor.AddComponent<ThreadAnchor>().connectRange = 4f;
    }
}
