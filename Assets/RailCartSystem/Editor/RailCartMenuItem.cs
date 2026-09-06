using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/RailCartSystem 메뉴 — 씬에 U자형 광산차 모양 레일카 + 곡선 가능한 임시 레일을 생성하고
/// 순수 함수를 점검한다. `WindupAxle`(딱딱 블록 시스템 미구현)은 씬에서 직접 연결한다.
///
/// [2026-09-05 재설계] 사용자가 실제 플레이테스트 후 다섯 가지를 요청했다 — (1) RotatingPlatform과
/// 같은 3초 지연 발동(RailCart.cs에서 처리), (2) 레일을 곡선으로도 꺾을 수 있게(RailPath에 곡선
/// 제어점 추가), (3) 시각적으로 진짜 철도처럼(RailTrackVisual 신규), (4) 카트 외형을 U자형 광산차로,
/// (5) 걸어서 타는 대신 C키로 탑승/하차(RailCartRider 재설계). 이 파일은 (3)(4)를 반영해 생성
/// 로직을 전면 교체했다 — 카트 루트는 항상 `localScale=(1,1,1)`을 유지한다(탑승자 부모화 시 전단이
/// 생기지 않도록, `RailCartRider.cs` 상단 주석 참고). 피벗(로컬 Y=0)은 레일 상면(바퀴 접지 높이)과
/// 일치시켰다 — `RailPath` 웨이포인트도 이 높이에 놓아야 카트가 자연스럽게 레일 위에 앉는다.
/// </summary>
public static class RailCartMenuItem
{
    // 전부 로컬(피벗=레일 상면, 스케일 없는 자식 오프셋) 기준 — 카트 루트 자신은 스케일 1을 유지한다.
    // 2026-09-05: 1차 크기(Scale=1 기준 아래 raw 값)로는 플레이어가 탑승할 때 짐칸에 낑겨 튕겨
    // 나가는 문제가 실측됐다 — 사용자 확정으로 카트·레일 전체를 2.5배 키운다(CatapultMenuItem의
    // `Scale` 상수와 같은 패턴 — raw 값은 그대로 두고 배율만 곱해 비율을 유지한다).
    private const float Scale = 2.5f;
    private const float BodyHeight = 0.5f * Scale;
    private const float BodyWidth = 0.8f * Scale;
    private const float BodyLength = 1.2f * Scale;
    private const float WallThickness = 0.08f * Scale;
    private const float FloorThickness = 0.08f * Scale;
    private const float WheelRadius = 0.12f * Scale;
    // 피벗(레일면=바퀴 접지점)에서 몸체 바닥까지 여유 — 바퀴 전체(지름)가 들어갈 공간으로 잡아야
    // 바퀴 윗부분이 바닥판을 뚫고 올라오지 않는다(2026-09-05, 바퀴 Y를 피벗=0에서 +WheelRadius로
    // 옮기며 함께 재계산 — 아래 "레일과 겹쳐 보이는 문제" 수정 참고).
    private const float BodyBottomY = WheelRadius * 2f;
    private const float WheelThickness = 0.08f * Scale;
    private const float DefaultGauge = 0.5f * Scale;
    private const float DefaultRailWidth = 0.06f * Scale;
    private static readonly Vector3 DefaultSleeperSize = new Vector3(0.9f, 0.06f, 0.18f) * Scale;

    [MenuItem("Tools/RailCartSystem/Create Rail Cart")]
    private static void CreateRailCart()
    {
        Vector3 spawnPos = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        CreateRailCartAt(spawnPos);
    }

    /// <summary>실제 생성 로직 — 메뉴(SceneView 피벗)와 배치/스크립트 호출(명시적 좌표) 양쪽에서
    /// 재사용한다(`SlingCatapultMenuItem.BuildSlingCatapult`와 같은 분리 이유).</summary>
    public static GameObject CreateRailCartAt(Vector3 spawnPos)
    {
        GameObject cartObj = new GameObject("RailCart", typeof(Rigidbody));
        cartObj.transform.position = spawnPos;
        // 루트는 항상 스케일 1 — RailCartRider가 탑승자를 이 Transform에 직접 부모화해도 전단이
        // 생기지 않게 하기 위함(클래스 상단 주석 참고). 외형은 전부 아래 자식들의 로컬 스케일로만 낸다.
        // **루트 자신은 콜라이더를 안 갖는다(2026-09-05 확정 수정)** — 이전엔 몸체 전체 실루엣을
        // 덮는 BoxCollider 하나가 물리를 전담해, 시각적으로 "비어 보이는" 짐칸 내부가 실제로는
        // 꽉 찬 솔리드였다. 탑승자가 그 안(=좌석 위치)에 놓이면 킨네마틱 콜라이더가 카트 자신의
        // 솔리드 콜라이더 속에 파묻혀, 다이나믹인 카트 쪽만 매 스텝 밀려나며 "혼자 덜컹거리다 레일
        // 밖으로 나가는" 버그가 실측됐다. 대신 아래 바닥판+4벽 각각에 실제 콜라이더를 살려 둬서
        // (CatapultBucket이 이미 검증한 방식과 같다 — "벽+바닥"이 물리를 담당, 내부는 진짜 빈
        // 공간) 짐칸 내부가 물리적으로도 실제로 비어 있게 만든다. 루트에 남는 콜라이더가 없어도
        // 자식들의 콜라이더가 Rigidbody 하나로 자동 합쳐진다(컴파운드 콜라이더, Unity 기본 동작).

        float bodyCenterY = BodyBottomY + BodyHeight * 0.5f;

        Rigidbody rb = cartObj.GetComponent<Rigidbody>();
        rb.mass = 40f;

        RailCart cart = cartObj.AddComponent<RailCart>();

        Material hullMat = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.28f, 0.15f) };
        Material wheelMat = new Material(Shader.Find("Standard")) { color = new Color(0.15f, 0.15f, 0.17f) };
        // 지면과 실제로 닿는 콜라이더(바닥판·아래 ContactPad) 전용 저마찰 재질 — CatapultSystem의
        // 손수레(`CatapultMenuItem.CreateLowFrictionMaterial`)와 같은 이유(2026-09-06 추가):
        // 레일카는 railRestoreForce로 레일 옆 이탈을 되돌리는데, 기본 PhysicMaterial 마찰이 이
        // 복원력보다 커서 살짝 어긋난 채로 지면 마찰에 붙들려 버티는 문제가 실측됐다("애매하게
        // 어긋난 채 멈춰 있다"). 실제 기차 바퀴가 마찰이 아니라 플랜지(홈)로 선로에 붙들리는 것과
        // 같은 이치 — 이 카트는 restoreForce가 그 플랜지 역할을 대신하므로, 마찰이 그 역할을
        // 방해하지 않도록 최대한 낮춘다.
        PhysicMaterial lowFriction = CreateLowFrictionMaterial();

        // U자형(열린 위) 몸체 — 바닥판 + 좌우/앞뒤 벽. 각자 자기 크기만큼의 솔리드 콜라이더를
        // 그대로 갖는다(CreatePlate가 지우지 않는다) — 다섯 조각이 합쳐 "속이 빈 상자" 물리를
        // 이룬다(위 루트 주석 참고). CreatePrimitive(Cube)의 기본 BoxCollider가 이미 메시와
        // 정확히 같은 크기라 별도 계산이 필요 없다.
        CreatePlate(cartObj.transform, "Hull_Floor",
            new Vector3(0f, BodyBottomY + FloorThickness * 0.5f, 0f),
            new Vector3(BodyWidth - WallThickness * 0.5f, FloorThickness, BodyLength - WallThickness * 0.5f), hullMat, lowFriction);
        CreatePlate(cartObj.transform, "Hull_Wall_L",
            new Vector3(-(BodyWidth - WallThickness) * 0.5f, bodyCenterY, 0f),
            new Vector3(WallThickness, BodyHeight, BodyLength), hullMat);
        CreatePlate(cartObj.transform, "Hull_Wall_R",
            new Vector3((BodyWidth - WallThickness) * 0.5f, bodyCenterY, 0f),
            new Vector3(WallThickness, BodyHeight, BodyLength), hullMat);
        CreatePlate(cartObj.transform, "Hull_Wall_F",
            new Vector3(0f, bodyCenterY, (BodyLength - WallThickness) * 0.5f),
            new Vector3(BodyWidth, BodyHeight, WallThickness), hullMat);
        CreatePlate(cartObj.transform, "Hull_Wall_B",
            new Vector3(0f, bodyCenterY, -(BodyLength - WallThickness) * 0.5f),
            new Vector3(BodyWidth, BodyHeight, WallThickness), hullMat);

        // 바퀴 2개(좌/우, CatapultSystem의 손수레 바퀴와 같은 단순화 — 축 하나로 표현) — 순수
        // 장식(회전 표현 없음, WindupAxle_Pole과 같은 관례로 콜라이더 제거). **Y는 0이 아니라
        // WheelRadius다** — 피벗(Y=0)이 레일 접지면이므로, 바퀴 "중심"이 아니라 바퀴 "바닥"이
        // 피벗에 와야 한다. 예전엔 중심을 피벗에 맞춰 바퀴 아랫부분이 레일 선 아래로 파고들어
        // "카트가 레일 위가 아니라 레일에 겹쳐 있는" 것처럼 보이는 버그가 있었다(2026-09-05).
        float wheelX = (BodyWidth - WheelThickness) * 0.5f;
        CreateWheel(cartObj.transform, new Vector3(-wheelX, WheelRadius, 0f), wheelMat);
        CreateWheel(cartObj.transform, new Vector3(wheelX, WheelRadius, 0f), wheelMat);

        // 바퀴는 위처럼 콜라이더가 없는 순수 시각 자식이라, 이대로면 실제 지면 접촉은 바닥판
        // (Hull_Floor, 바닥이 피벗+BodyBottomY)이 대신하게 돼 카트가 "피벗=레일 접지면"보다
        // BodyBottomY(=바퀴 지름)만큼 낮게 가라앉은 채로 물리에 잡힌다(2026-09-06 실측 확인 —
        // 활성화 즉시 낙하·급정지·공중 요동으로 나타났다). 보이지 않는 콜라이더 전용 오브젝트로
        // 바퀴 발밑(피벗~바퀴 접지 높이)을 채워 실제 접지 높이를 시각적 바퀴 바닥과 일치시킨다.
        GameObject contactPad = new GameObject("ContactPad", typeof(BoxCollider));
        contactPad.transform.SetParent(cartObj.transform, false);
        contactPad.transform.localPosition = new Vector3(0f, BodyBottomY * 0.5f, 0f);
        BoxCollider contactPadCollider = contactPad.GetComponent<BoxCollider>();
        contactPadCollider.size =
            new Vector3(BodyWidth - WallThickness * 0.5f, BodyBottomY, BodyLength - WallThickness * 0.5f);
        contactPadCollider.sharedMaterial = lowFriction;

        // 탑승 — 2026-09-05부터 걸어서 올라타는 트리거가 아니라 C키 거리 게이트(CatapultBucket
        // 패턴). 별도 자식 없이 카트 루트에 직접 붙인다 — 루트가 스케일 1이라 부모화해도 안전하다.
        // **좌석 위치를 반드시 명시해야 한다** — `RailCartRider.seat`를 비워두면 `Board()`가
        // `transform.position`(=이 컴포넌트가 붙은 카트 루트, 곧 레일 접지면=피벗 높이)으로
        // 떨어뜨리는데, 그 높이는 짐칸 바닥(BodyBottomY+FloorThickness)보다도 낮다 — 플레이어가
        // 짐칸 "안"이 아니라 카트 "밑"에 놓여 탑승이 제대로 안 되는 버그가 실측됐다(2026-09-05).
        GameObject seatObj = new GameObject("RailCart_Seat");
        seatObj.transform.SetParent(cartObj.transform, false);
        // 바닥판 윗면 + 플레이어 반높이(CatapultBucket.OccupantHalfHeight와 같은 관례, 정육면체
        // 1×1×1 기준 0.5) + 약간의 여유 — 발이 바닥에 파묻히지 않게.
        float floorTopY = BodyBottomY + FloorThickness;
        seatObj.transform.localPosition = new Vector3(0f, floorTopY + 0.5f + 0.05f, 0f);

        RailCartRider rider = cartObj.AddComponent<RailCartRider>();
        rider.cart = cart;
        rider.seat = seatObj.transform;
        rider.boardRange = 2.5f;
        // 하차 지점도 몸체 폭(BodyWidth)에 맞춰 벽 바깥으로 확실히 나가게 잡는다 — 기본값(0.9)은
        // 확대 전 몸체 기준이라 지금은 벽 안쪽에 놓일 수 있다.
        rider.exitLocalOffset = new Vector3(BodyWidth * 0.5f + 0.5f, 0f, 0f);

        // 화물 트리거 — 짐칸(hull) 내부 공간을 덮는다.
        GameObject cargoTrigger = new GameObject("RailCart_CargoBay", typeof(BoxCollider));
        cargoTrigger.transform.SetParent(cartObj.transform, false);
        BoxCollider cargoCol = cargoTrigger.GetComponent<BoxCollider>();
        cargoCol.isTrigger = true;
        cargoCol.center = new Vector3(0f, bodyCenterY + FloorThickness * 0.5f, 0f);
        cargoCol.size = new Vector3(BodyWidth - WallThickness * 2f, BodyHeight - FloorThickness, BodyLength - WallThickness * 2f);
        RailCartCargoBay cargoBay = cargoTrigger.AddComponent<RailCartCargoBay>();
        cart.cargoBay = cargoBay;

        // 임시 레일 경로 — 카트 좌우로 두 구간을 놓고 가운데 구간에 곡선 제어점을 하나 꽂아
        // "곡선으로도 꺾을 수 있다"를 바로 확인할 수 있게 한다. 레일 높이(피벗 Y)는 카트 루트와
        // 정확히 같아야 바퀴가 레일 위에 자연스럽게 놓인다.
        GameObject pathObj = new GameObject("RailPath_" + cartObj.GetInstanceID(), typeof(RailPath), typeof(RailTrackVisual));
        pathObj.transform.position = spawnPos;
        RailPath path = pathObj.GetComponent<RailPath>();
        Transform wpA = CreateWaypoint(pathObj.transform, "Waypoint_A", spawnPos - Vector3.forward * 5f);
        Transform wpMid = CreateWaypoint(pathObj.transform, "Waypoint_Mid", spawnPos);
        Transform wpB = CreateWaypoint(pathObj.transform, "Waypoint_B", spawnPos + Vector3.forward * 5f);
        Transform curvePoint = CreateWaypoint(pathObj.transform, "CurvePoint_AMid", spawnPos + new Vector3(2f, 0f, -2.5f));
        path.waypoints = new[] { wpA, wpMid, wpB };
        path.segmentMaxSafeSpeed = new[] { 8f, 8f };
        path.curveControlPoints = new[] { curvePoint, null };

        RailTrackVisual track = pathObj.GetComponent<RailTrackVisual>();
        track.gauge = DefaultGauge;
        track.railWidth = DefaultRailWidth;
        track.sleeperSize = DefaultSleeperSize;

        cart.path = path;

        Undo.RegisterCreatedObjectUndo(cartObj, "Create Rail Cart");
        Undo.RegisterCreatedObjectUndo(pathObj, "Create Rail Cart");
        Selection.activeGameObject = cartObj;
        return cartObj;
    }

    private static void CreatePlate(Transform parent, string plateName, Vector3 localPos, Vector3 localScale,
        Material mat, PhysicMaterial groundMaterial = null)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = plateName;
        // 콜라이더를 지우지 않는다 — 이 조각(바닥판/벽)이 곧 카트의 실제 물리 형태다(위 "루트는
        // 콜라이더를 안 갖는다" 주석 참고). 다른 장식(바퀴 등)과 달리 이 다섯 조각만은 순수
        // 시각이 아니다.
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = localPos;
        plate.transform.localScale = localScale;
        plate.GetComponent<Renderer>().sharedMaterial = mat;
        if (groundMaterial != null) plate.GetComponent<Collider>().sharedMaterial = groundMaterial;
    }

    // CatapultSystem(`CatapultMenuItem.CreateLowFrictionMaterial`)과 같은 패턴 — 지면과 닿는
    // 콜라이더끼리만 공유하는 인스턴스, 별도 에셋으로 저장하지 않는다. `frictionCombine =
    // Minimum`으로 둬 지형 쪽 PhysicMaterial이 무엇이든 접촉 마찰이 항상 이 낮은 값 이하로 정해지게
    // 한다.
    private const float LowFrictionCoefficient = 0.05f;

    private static PhysicMaterial CreateLowFrictionMaterial()
    {
        return new PhysicMaterial("RailCart_LowFriction")
        {
            staticFriction = LowFrictionCoefficient,
            dynamicFriction = LowFrictionCoefficient,
            frictionCombine = PhysicMaterialCombine.Minimum,
        };
    }

    // 원통을 로컬 Z축으로 90도 눕혀 축(두께)이 카트 좌우(X)를 향하게 한다 — CatapultSystem의
    // 바퀴 생성과 같은 회전 관례. 순수 장식이라 콜라이더는 만들지 않는다.
    private static void CreateWheel(Transform parent, Vector3 localPos, Material mat)
    {
        GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = "Wheel";
        Object.DestroyImmediate(wheel.GetComponent<Collider>());
        wheel.transform.SetParent(parent, false);
        wheel.transform.localPosition = localPos;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        wheel.transform.localScale = new Vector3(WheelRadius * 2f, WheelThickness * 0.5f, WheelRadius * 2f);
        wheel.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static Transform CreateWaypoint(Transform parent, string waypointName, Vector3 worldPos)
    {
        GameObject wp = new GameObject(waypointName);
        wp.transform.SetParent(parent, false);
        wp.transform.position = worldPos;
        return wp.transform;
    }

    [MenuItem("Tools/RailCartSystem/Self-Check")]
    private static void SelfCheck()
    {
        string cartReport = RailCart.SelfCheck();
        string pathReport = RailPath.SelfCheck();
        bool ok = cartReport == "OK" && pathReport == "OK";
        if (ok) Debug.Log("[RailCart] Self-Check 통과 (RailCart + RailPath)");
        else Debug.LogError("[RailCart] Self-Check 실패:\nRailCart:\n" + cartReport + "\nRailPath:\n" + pathReport);
    }
}
