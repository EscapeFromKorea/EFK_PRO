using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CH8(가상 시약 혼합 + 관리자 감시) 그레이박스 테스트 방 배치 도구. 지금 열린 씬의 떨어진 공간(BasePos)에
/// 바닥·경계벽·가림벽, 역할 사물 3개(책/혼합대/감시), 관리자(닫힌 순찰 경로), 열쇠, CCTV 3대, 열쇠 문(북쪽 벽), 자동 시작 구역, 참가자별 시작 지점
/// 3개, 컨트롤러를 만들고 서로 배선한다. 전부 "CH8_TestRoom" 루트 하나 아래에 두어 지우기 쉽다(Undo 한 번).
///
/// 배치 요약(로컬 x,z — 바닥 상면 = BasePos.y):
///  - 시작 지점·역할 사물은 남서쪽 모서리. 순찰 경로(-2..12 정사각형)와 D=10 이상 떨어져 준비 중엔 안 들킨다.
///  - 가림벽은 순찰 경로 남쪽 변 앞 — 벽 뒤 은폐(A-02/A-03) 확인용.
///  - 열쇠는 순찰 경로 북동쪽 밖.
/// 이미 있으면 건너뛴다. 플레이어는 "Move Players To CH8 Start"로 시작 지점에 옮긴다.
/// </summary>
public static class Ch8TestRoomMenuItem
{
    private static readonly Vector3 BasePos = new Vector3(300f, 50f, 0f); // 기존 레벨과 겹치지 않는 열린 공간
    private const string RootName = "CH8_TestRoom";

    [MenuItem("Tools/ManagerSurveillanceSystem/Create CH8 Test Room")]
    public static void CreateTestRoom()
    {
        if (GameObject.Find(RootName) != null)
        {
            Debug.LogWarning($"[CH8] 씬에 이미 '{RootName}'이 있다 — 다시 만들려면 먼저 지워라.");
            return;
        }

        RespawnController respawn = Object.FindObjectOfType<RespawnController>();
        if (respawn == null)
        {
            Debug.LogWarning("[CH8] 씬에 RespawnController가 없어 전원 복귀를 배선할 수 없다 " +
                             "(Tools > Respawn > Create Respawn Controller를 먼저 실행해라).");
            return;
        }

        GameObject root = new GameObject(RootName);
        root.transform.position = BasePos;

        // 1. 바닥·경계벽(높이 4 — 점프로 넘어가 열쇠 문을 우회하지 못하게)·가림벽. 북쪽 벽 x=8~12에 문 자리를 비운다.
        Box(root, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), new Color(0.55f, 0.55f, 0.6f));
        Box(root, "Wall_N_W", new Vector3(-6.5f, 2f, 20.5f), new Vector3(29f, 4f, 1f), Color.gray);
        Box(root, "Wall_N_E", new Vector3(16.5f, 2f, 20.5f), new Vector3(9f, 4f, 1f), Color.gray);
        Box(root, "Wall_S", new Vector3(0f, 2f, -20.5f), new Vector3(42f, 4f, 1f), Color.gray);
        Box(root, "Wall_E", new Vector3(20.5f, 2f, 0f), new Vector3(1f, 4f, 40f), Color.gray);
        Box(root, "Wall_W", new Vector3(-20.5f, 2f, 0f), new Vector3(1f, 4f, 40f), Color.gray);
        Box(root, "Exit_Floor", new Vector3(10f, -0.5f, 26f), new Vector3(10f, 1f, 10f), new Color(0.4f, 0.7f, 0.45f));
        Box(root, "Cover", new Vector3(5f, 1.5f, -6f), new Vector3(5f, 3f, 1f), new Color(0.4f, 0.3f, 0.25f));

        // 2. 역할 매니저(CH8 전용 — 다른 챕터 매니저와 분리)
        GameObject managerGo = Child(root, "CH8_RoleAssignmentManager", Vector3.zero);
        RoleAssignmentManager roles = managerGo.AddComponent<RoleAssignmentManager>();
        roles.respawnController = respawn;

        // 3. 역할 사물 3개
        RoleSlot bookSlot = Slot(root, roles, "Book", new Vector3(-12f, 0f, -8f), new Color(0.3f, 0.6f, 1f));
        BookPanel book = bookSlot.gameObject.AddComponent<BookPanel>();
        book.slot = bookSlot; // 본문은 MixingStation이 시도마다 랜덤 조합식으로 다시 쓴다.

        RoleSlot mixerSlot = Slot(root, roles, "Mixer", new Vector3(-6f, 0f, -14f), new Color(1f, 0.6f, 0.2f));
        MixingStation station = mixerSlot.gameObject.AddComponent<MixingStation>();
        station.slot = mixerSlot;
        station.book = book;
        GameObject bottle = Primitive(PrimitiveType.Cylinder, mixerSlot.gameObject, "Bottle",
            new Vector3(0f, 1.4f, 0f), new Vector3(0.3f, 0.3f, 0.3f), new Color(0.6f, 1f, 0.8f));
        bottle.SetActive(false);
        station.bottleVisual = bottle;

        RoleSlot monitorSlot = Slot(root, roles, "Monitor", new Vector3(-16f, 0f, -3f), new Color(0.5f, 1f, 0.5f));
        ManagerCctvPanel cctv = monitorSlot.gameObject.AddComponent<ManagerCctvPanel>();
        cctv.slot = monitorSlot;
        cctv.cameras = new[]
        {
            Cctv(root, "CCTV_1", new Vector3(-18f, 12f, -18f), new Vector3(5f, 0f, 5f)),
            Cctv(root, "CCTV_2", new Vector3(18f, 12f, 18f), new Vector3(5f, 0f, 5f)),
            Cctv(root, "CCTV_3", new Vector3(18f, 12f, -18f), new Vector3(5f, 0f, 5f)),
        };

        // 4. 관리자 — 닫힌 순찰 경로(마지막 = 첫 지점). 루트 피벗은 캡슐 중심(바닥 + 1).
        Vector3[] loop =
        {
            new Vector3(-2f, 1f, -2f), new Vector3(12f, 1f, -2f), new Vector3(12f, 1f, 12f),
            new Vector3(-2f, 1f, 12f), new Vector3(-2f, 1f, -2f)
        };
        GameObject routeGo = Child(root, "C8_ROUTE", Vector3.zero);
        var waypoints = new List<Transform>();
        for (int i = 0; i < loop.Length; i++) waypoints.Add(Child(routeGo, $"WP{i}", loop[i]).transform);

        GameObject agentGo = Child(root, "C8_MANAGER", loop[0]);
        Rigidbody rb = agentGo.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        PathChaserAgent agent = agentGo.AddComponent<PathChaserAgent>();
        agent.waypoints = waypoints.ToArray();

        // 시각물은 콜라이더 없음 — 있으면 시야 Linecast가 자기 몸에 막힌다(ManagerAgent 주석).
        GameObject visual = Primitive(PrimitiveType.Capsule, agentGo, "Visual", Vector3.zero, Vector3.one, Color.gray);
        GameObject nose = Primitive(PrimitiveType.Cube, visual, "Nose", new Vector3(0f, 0.5f, 0.5f),
            new Vector3(0.4f, 0.2f, 0.4f), Color.black);
        visual.transform.rotation = Quaternion.LookRotation(Vector3.right); // 첫 구간(+X) 방향을 바라보고 시작

        ManagerAgent managerAgent = agentGo.AddComponent<ManagerAgent>();
        managerAgent.agent = agent;
        managerAgent.visual = visual.transform;

        ManagerStatusIndicator indicator = agentGo.AddComponent<ManagerStatusIndicator>();
        indicator.managerAgent = managerAgent;
        indicator.renderers = new[] { visual.GetComponent<Renderer>() };

        GameObject catchGo = Child(agentGo, "C8_CATCH", Vector3.zero);
        SphereCollider catchCol = catchGo.AddComponent<SphereCollider>();
        catchCol.isTrigger = true;
        ManagerCatchZone catchZone = catchGo.AddComponent<ManagerCatchZone>();
        catchCol.radius = 1.2f; // AddComponent가 부르는 ManagerCatchZone.Reset이 반경을 1로 덮으므로 그 뒤에 정한다.
        catchZone.managerAgent = managerAgent;

        // 사용 지점 = 관리자 자식, 콜라이더 없음(거리로 잰다 — ManagerUsePoint 주석).
        GameObject useGo = Child(agentGo, "C8_USE", Vector3.zero);
        ManagerUsePoint usePoint = useGo.AddComponent<ManagerUsePoint>();
        usePoint.station = station;

        // 5. 열쇠
        GameObject keyGo = Child(root, "C8_KEY_POINT", new Vector3(16f, 0.8f, 16f));
        ManagerKeyPoint key = keyGo.AddComponent<ManagerKeyPoint>();
        key.manager = roles;
        key.visual = Primitive(PrimitiveType.Cube, keyGo, "KeyVisual", Vector3.zero,
            new Vector3(0.4f, 0.4f, 0.4f), new Color(1f, 0.85f, 0.2f));

        // 5-1. 열쇠 문(북쪽 벽 틈) — 몸체는 DoorSystem/doorPhysics 재사용, 상호작용은 방 안쪽 앞에서.
        GameObject doorBody = Box(root, "C8_KEY_DOOR_Body", new Vector3(10f, 2f, 20.5f), new Vector3(3.9f, 4f, 0.5f),
            new Color(0.6f, 0.45f, 0.2f));
        doorBody.AddComponent<Rigidbody>(); // doorPhysics.Awake가 키네마틱으로 만든다.
        doorPhysics body = doorBody.AddComponent<doorPhysics>();
        body.doorTargetYOffset = 4.5f;
        body.doorSpeed = 2f;
        ManagerKeyDoor keyDoor = Child(root, "C8_KEY_DOOR", new Vector3(10f, 0.5f, 19f)).AddComponent<ManagerKeyDoor>();
        keyDoor.manager = roles;
        keyDoor.keyPoint = key;
        keyDoor.door = body;

        // 6. 참가자별 시작 지점(바닥 상면 — RespawnController가 도형별 바닥 오프셋을 얹는다)
        var startPoints = new List<SectionSafePoint>();
        for (int i = 0; i < 3; i++)
        {
            GameObject sp = Child(root, $"CH8_START_{i}", new Vector3(-16f + i * 2.5f, 0f, -16f));
            SectionSafePoint point = sp.AddComponent<SectionSafePoint>();
            point.sectionId = $"CH8_START_{i}";
            startPoints.Add(point);
        }

        // 7. 컨트롤러
        GameObject ctrlGo = Child(root, "CH8_ManagerChapterController", Vector3.zero);
        ManagerChapterController controller = ctrlGo.AddComponent<ManagerChapterController>();
        controller.manager = roles;
        controller.station = station;
        controller.usePoint = usePoint;
        controller.managerAgent = managerAgent;
        controller.keyPoint = key;
        controller.respawnController = respawn;
        // 자동 시작 구역 = 방 바닥 위 전체(경계벽 안쪽). 들어오는 순간 감시 시작.
        // 구역 밑면을 바닥면보다 1 U 아래로 — 세모는 피벗이 바닥면 아래(-0.21)라 밑면이 바닥과 같으면 서 있어도 구역 밖이다.
        controller.startZone = Child(root, "CH8_StartZone", new Vector3(0f, 2f, 0f)).transform;
        controller.startZoneSize = new Vector3(40f, 6f, 40f);
        controller.startPoints = startPoints.ToArray();
        catchZone.controller = controller;

        Undo.RegisterCreatedObjectUndo(root, "Create CH8 Test Room");
        Selection.activeGameObject = ctrlGo;
        Debug.Log("[CH8] 테스트 방 배치 완료. Tools > ManagerSurveillanceSystem > Move Players To CH8 Start로 플레이어를 " +
                  "옮긴 뒤 플레이하라. 방(CH8_StartZone)에 들어오는 순간 감시가 시작된다. 역할 사물은 E.", ctrlGo);
    }

    /// <summary>씬의 플레이어(Kind 순)를 CH8 시작 지점 위로 옮긴다(Undo 가능). 시작 지점보다 1.5U 위에 놓아 떨어뜨린다.</summary>
    [MenuItem("Tools/ManagerSurveillanceSystem/Move Players To CH8 Start")]
    public static void MovePlayersToStart()
    {
        ManagerChapterController controller = Object.FindObjectOfType<ManagerChapterController>();
        if (controller == null || controller.startPoints == null || controller.startPoints.Length == 0)
        {
            Debug.LogWarning("[CH8] 시작 지점이 연결된 ManagerChapterController가 없다 — 테스트 방부터 만들어라.");
            return;
        }

        PlayerShapeIdentity[] players = Object.FindObjectsOfType<PlayerShapeIdentity>();
        System.Array.Sort(players, (a, b) => a.Kind.CompareTo(b.Kind));
        for (int i = 0; i < players.Length; i++)
        {
            SectionSafePoint p = controller.startPoints[i % controller.startPoints.Length];
            if (p == null) continue;
            Undo.RecordObject(players[i].transform, "Move Players To CH8 Start");
            players[i].transform.position = p.transform.position + Vector3.up * 1.5f;
        }
        Debug.Log($"[CH8] 플레이어 {players.Length}명을 CH8 시작 지점으로 옮겼다. 원래 위치로는 Ctrl+Z.");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

    private static GameObject Child(GameObject parent, string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        return go;
    }

    private static GameObject Box(GameObject parent, string name, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        Tint(go, color);
        return go;
    }

    /// <summary>콜라이더 없는 시각용 프리미티브.</summary>
    private static GameObject Primitive(PrimitiveType type, GameObject parent, string name, Vector3 localPos,
                                        Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        Tint(go, color);
        return go;
    }

    // 에셋을 만들지 않고 MPB로만 칠한다 — 공유 기본 머티리얼을 오염시키지 않는다. 직렬화는 안 되므로 플레이 중
    // 색은 기본 흰색이 된다(형태로 구분). 관리자 색은 ManagerStatusIndicator가 매 상태 전환마다 다시 칠한다.
    private static void Tint(GameObject go, Color color)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (r == null) return;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", color);
        r.SetPropertyBlock(mpb);
    }

    /// <summary>역할 사물: 루트에 상호작용 트리거(RoleSlot), 자식에 책상 모양(솔리드)과 역할 이름 표지.</summary>
    private static RoleSlot Slot(GameObject parent, RoleAssignmentManager roles, string roleId, Vector3 localPos, Color color)
    {
        GameObject go = Child(parent, $"CH8_Slot_{roleId}", localPos);
        BoxCollider trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1f, 0f);
        trigger.size = new Vector3(3.5f, 2f, 3.5f);

        RoleSlot slot = go.AddComponent<RoleSlot>();
        slot.roleId = roleId;
        slot.manager = roles;

        Box(go, "Desk", new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 1f, 1f), color);

        GameObject label = Child(go, "Label", new Vector3(0f, 2.2f, 0f));
        TextMesh text = label.AddComponent<TextMesh>();
        text.text = roleId;
        text.anchor = TextAnchor.MiddleCenter;
        text.characterSize = 0.25f;
        text.fontSize = 48;
        text.color = color;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return slot;
    }

    /// <summary>CCTV 카메라 — MainCamera 태그·AudioListener 없음(화면·오디오를 빼앗지 않게). 패널이 켤 때까지 꺼 둔다.</summary>
    private static Camera Cctv(GameObject parent, string name, Vector3 localPos, Vector3 localLookAt)
    {
        GameObject go = Child(parent, name, localPos);
        go.transform.rotation = Quaternion.LookRotation(localLookAt - localPos);
        Camera cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 70f;
        return cam;
    }
}
