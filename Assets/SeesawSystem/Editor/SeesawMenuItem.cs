using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > SeesawSystem > Create Seesaw 메뉴. 판 하나(Rigidbody + BoxCollider + HingeJoint)만 놓으면
/// 끝나는 시소 생성기 — `docs/PRD/Seesaw.md`의 핵심 결정대로 물리 로직을 짜는 런타임 스크립트는
/// 없다(`SeesawTuningNotes`는 인스펙터 hover 설명용 주석 컴포넌트일 뿐 Update/FixedUpdate가 없다).
/// 양쪽 무게 차이/
/// 충격 발사/쐐기 고정 전부 이 HingeJoint 하나가 PhysX로 자동 처리한다(RotatingPlateMenuItem과 같은
/// 구조지만, RotatingPlate와 달리 useGravity를 켜 실제 하중으로 기울게 한다 — 태엽 신호로 도는
/// 회전판과 달리 시소는 스스로 물리로 반응해야 하는 "자유 물리 장치"이기 때문). 받침대(Fulcrum)·
/// 좌석 손잡이는 전부 순수 시각/정적 장식(콜라이더는 있어도 Rigidbody 없음)이라 물리 설계에는
/// 관여하지 않는다. 무게·충격 테스트용 계단은 이 메뉴가 만들지 않는다 — 필요할 때만 씬에 따로
/// 배치한다(2026-09-07, 매번 자동으로 딸려 나오지 않게 분리).
/// </summary>
public static class SeesawMenuItem
{
    [MenuItem("Tools/SeesawSystem/Create Seesaw")]
    private static void CreateSeesaw()
    {
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        BuildSeesaw(spawnPos);
    }

    /// <summary>월드 좌표를 받아 시소를 짓는다(SlingCatapultMenuItem.BuildSlingCatapult과 같은
    /// 패턴) — 메뉴 항목은 SceneView 피벗을 넘기는 얇은 래퍼일 뿐, 격리된 씬에서 좌표를 직접 지정해
    /// 호출하는 배치 도구에서도 재사용한다.</summary>
    public static GameObject BuildSeesaw(Vector3 spawnPos)
    {
        GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plank.name = "Seesaw";
        Undo.RegisterCreatedObjectUndo(plank, "Create Seesaw");
        plank.transform.position = spawnPos;
        plank.transform.localScale = new Vector3(5f, 0.3f, 2f);

        Rigidbody rb = plank.AddComponent<Rigidbody>();
        rb.mass = 5f;
        // 0.5는 무게가 오르내릴 때 판이 스스로 좌우로 왔다갔다 계속 흔들리는(감쇠 부족) 문제가
        // 실측으로 확인됐다(2026-09-07) — 회전 저항을 6배 올려 진동이 빠르게 잦아들게 했다.
        rb.angularDrag = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        HingeJoint hinge = plank.AddComponent<HingeJoint>();
        // Unity가 HingeJoint를 처음 붙일 때(에디터 Reset()) anchor를 콜라이더 윗면(y=+0.5,
        // 문 경첩을 겨냥한 기본값으로 추정)으로 자동 설정해 둔다 — 명시적으로 center로 되돌리지
        // 않으면 판이 중심축이 아니라 윗면을 축으로 돌아 "양쪽 방향으로 회전"이 깨진다(실측으로
        // 확인, 2026-09-07 격리 씬 검증).
        hinge.anchor = Vector3.zero;
        hinge.axis = Vector3.forward;
        hinge.connectedBody = null;
        hinge.useMotor = false;
        hinge.useSpring = false;
        hinge.useLimits = true;
        hinge.limits = new JointLimits { min = -35f, max = 35f };

        // 물리 값은 전부 위 Rigidbody/HingeJoint가 들고 있고, 이 컴포넌트는 그 옆에서 hover로
        // "왜 이 값인지"만 보여준다(런타임 로직 없음, SeesawTuningNotes.cs 참고).
        plank.AddComponent<SeesawTuningNotes>();

        // 탑승자가 기운 판 위에서 미끄러지지 않을 만큼 마찰을 높게 잡는다(RailCartSystem의
        // CreateLowFrictionMaterial과 반대 방향 — 여기는 오히려 안 미끄러져야 한다).
        plank.GetComponent<Collider>().sharedMaterial = new PhysicMaterial("Seesaw_Grip")
        {
            staticFriction = 0.8f,
            dynamicFriction = 0.8f,
            frictionCombine = PhysicMaterialCombine.Maximum,
        };
        plank.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Seesaw_Plank_Mat", new Color(0.55f, 0.35f, 0.18f));

        CreateSeat(plank.transform, 2.1f);
        CreateSeat(plank.transform, -2.1f);
        BuildFulcrum(spawnPos);

        Selection.activeGameObject = plank;
        Debug.Log("[SeesawSystem] Seesaw 생성 완료 (질량/회전 저항/최대 각도는 인스펙터에서 튜닝)");
        return plank;
    }

    // 놀이터 시소 느낌을 내는 받침대. 위쪽은 판 피벗과 같은 높이의 능선(모서리)으로 좁아지고
    // 아래쪽은 평평한 밑면 그대로 유지되는 쐐기(삼각기둥) 모양이다 — 예전엔 45도 돌린 큐브(다이아몬드)
    // 라 바닥 쪽도 뾰족했는데, 지면보다 얕게 파묻히면 그 아래쪽 뾰족점이 지면 위로 삐져나와 보이는
    // 문제가 있었다(2026-09-07 플레이테스트 지적). 평평한 밑면은 지면 높이 추정이 살짝 어긋나도
    // 뾰족점처럼 튀어 보이지 않는다.
    private static void BuildFulcrum(Vector3 pivotPos)
    {
        const float width = 2.4f;   // 능선까지 벌어지는 폭(X)
        const float height = 1.7f;  // 밑면~능선 높이 — 기존 다이아몬드 half-diagonal과 비슷하게 유지
        const float depth = 1.6f;   // 능선 방향(Z) 길이, 판보다 살짝 좁게

        GameObject fulcrum = new GameObject("Seesaw_Fulcrum");
        Undo.RegisterCreatedObjectUndo(fulcrum, "Create Seesaw");
        fulcrum.transform.position = pivotPos - new Vector3(0f, height * 0.5f, 0f);
        fulcrum.transform.localScale = new Vector3(width, height, depth);

        Mesh wedge = CreateWedgeMesh();
        fulcrum.AddComponent<MeshFilter>().sharedMesh = wedge;
        fulcrum.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial("Seesaw_Fulcrum_Mat", new Color(0.5f, 0.5f, 0.52f));
        MeshCollider collider = fulcrum.AddComponent<MeshCollider>();
        collider.sharedMesh = wedge;
        collider.convex = true;
    }

    // 로컬 -0.5~0.5 큐브와 같은 크기 범위의 쐐기 메쉬 — 밑면(y=-0.5)은 사각형 그대로, 위쪽은
    // y=+0.5에서 폭 0인 능선(x=0) 한 줄로 좁아진다. 능선이 Z축과 나란해 시소의 HingeJoint 축
    // (Vector3.forward)과 자연히 맞는다. 컬링 방향을 눈으로 확인하기 전까지는 각 삼각형을 양쪽
    // 감김 순서로 다 넣어 어느 면이 안 보이는 사고를 원천 차단한다(ponytail: 와인딩 확인 후 절반은
    // 지워도 되는 임시 안전장치).
    private static Mesh CreateWedgeMesh()
    {
        Vector3[] verts =
        {
            new Vector3(-0.5f, -0.5f, -0.5f), // 0 바닥-좌-앞
            new Vector3(0.5f, -0.5f, -0.5f),  // 1 바닥-우-앞
            new Vector3(0.5f, -0.5f, 0.5f),   // 2 바닥-우-뒤
            new Vector3(-0.5f, -0.5f, 0.5f),  // 3 바닥-좌-뒤
            new Vector3(0f, 0.5f, -0.5f),     // 4 능선-앞
            new Vector3(0f, 0.5f, 0.5f),      // 5 능선-뒤
        };
        int[] tris =
        {
            0, 1, 2, 0, 2, 3, // 바닥
            0, 3, 5, 0, 5, 4, // 왼쪽 경사면
            1, 4, 5, 1, 5, 2, // 오른쪽 경사면
            0, 4, 1,          // 앞 삼각면
            3, 2, 5,          // 뒤 삼각면
        };

        int[] doubled = new int[tris.Length * 2];
        for (int i = 0; i < tris.Length; i += 3)
        {
            doubled[i * 2] = tris[i];
            doubled[i * 2 + 1] = tris[i + 1];
            doubled[i * 2 + 2] = tris[i + 2];
            doubled[i * 2 + 3] = tris[i];
            doubled[i * 2 + 4] = tris[i + 2];
            doubled[i * 2 + 5] = tris[i + 1];
        }

        Mesh mesh = new Mesh { name = "Seesaw_Wedge" };
        mesh.vertices = verts;
        mesh.triangles = doubled;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // 판 양 끝에 얹는 손잡이 겸 좌석 표시 — 콜라이더 없는 순수 장식(판의 BoxCollider가 이미 전체
    // 길이를 덮어 탑승 판정에는 영향 없다).
    private static void CreateSeat(Transform plank, float worldOffsetX)
    {
        GameObject seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        seat.name = "Seesaw_Seat";
        Undo.RegisterCreatedObjectUndo(seat, "Create Seesaw");
        Object.DestroyImmediate(seat.GetComponent<Collider>());
        seat.transform.SetParent(plank, worldPositionStays: false);

        Vector3 parentScale = plank.localScale;
        seat.transform.localPosition = new Vector3(worldOffsetX / parentScale.x, 0.5f, 0f); // y=0.5 → 판 윗면과 같은 높이
        seat.transform.localScale = new Vector3(0.5f / parentScale.x, 0.5f / parentScale.y, 1.6f / parentScale.z);
        seat.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Seesaw_Seat_Mat", new Color(0.85f, 0.15f, 0.15f));
    }

    private static Material MakeMaterial(string name, Color color)
    {
        return new Material(Shader.Find("Standard")) { name = name, color = color };
    }
}
