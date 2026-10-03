using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// `Tools > Isolation Rescue > Create Test Room` — 격리 협력 구출 장치 한 세트를 배선까지 마친 시험 배치로 만든다.
/// docs/PRD/IsolationRescue.md §3.
///
/// [무엇을 만드나 / 무엇이 아닌가]
/// 컨트롤러·타이머·HUD, 진입 트리거와 진입문, 방 3개와 사이 문 2개, 순서판 3개, 대응표, 레버 3개, 전원 스위치,
/// 준비 버튼 2개, 해제 버튼, 비상문 3개, 해제문, 추락 복귀 지점을 만들고 컴포넌트·이벤트를 전부 연결한다.
/// PRD가 방 배치와 비상 동선을 "세부 배치 미정"으로 남겨 뒀으므로, 이것은 <b>정식 레벨이 아니라 플레이로 동작을
/// 확인하기 위한 시험 배치</b>다. 실제 레벨 디자이너는 같은 컴포넌트를 자기 맵에 놓고 아래 이벤트 배선(Wire)만
/// 참고하면 된다.
///
/// [배치 규칙]
/// 안쪽은 +Z 방향이다. 바깥(z &lt; 0)에 대응표·레버·전원 스위치·바깥 준비 버튼, 안쪽에 방1~3(순서판·안쪽 준비
/// 버튼·해제 버튼), 동쪽 벽에 방마다 비상문, 맨 끝에 해제문(CH4 숏컷)이 있다. 문은 DoorSystem의 문 규격
/// (15.45 폭) 그대로라 방 폭도 거기에 맞춘다.
///
/// [콘텐츠는 임시]
/// 단계별 정답 순서는 자가검증과 같은 예시 값이다(PRD §5: 3단계 구성·기호·레버 매핑은 미확정 콘텐츠 구성안).
///
/// 실행: 메뉴, 또는 배치모드에서 <see cref="Build"/>를 자가검증이 부른다. 씬 저장은 사용자가 한다.
/// </summary>
public static class IsolationRescueMenuItem
{
    private const string SystemFolder = "Assets/IsolationRescueSystem";
    private const string MaterialFolder = SystemFolder + "/Materials";

    // 문(DoorSystem 규격) 폭 = 방 안쪽 폭. 문과 벽 사이에 틈이 생기지 않게 한다.
    private const float RoomWidth = 15.45f;
    private const float RoomLength = 18f;
    private const float WallThickness = 1f;
    private const float WallHeight = 8f;
    private const float DoorCenterY = 3.936f; // 문 높이 7.8715의 절반 — 바닥(y=0)에 문 아래가 닿는다
    private const float OutsideDepth = 24f;
    private const float ShortcutDepth = 18f;
    private const int RoomCount = 3;
    // 레버 막대는 피벗보다 약 0.12 위에 놓인다. 플레이어는 1유닛 크기라(큐브 1x1x1, 구 반지름 0.5) 막대가
    // 바닥에서 1.5 위에 있으면 닿지 못한다 — 몸 높이에 오도록 낮춘다.
    private const float LeverPivotY = 0.4f;
    // 레버는 방 중앙 쪽(서쪽)에서 동쪽으로 밀어 당긴다. 막대는 쉬는 위치에서 북쪽(+Z)을 향하고, 밀면 동쪽을 향하도록
    // 90도 돌아간다. 막대 길이가 약 3.8이라 동쪽 벽(안쪽 면 x=7.7)에 닿지 않게 피벗을 x=2에 둔다.
    private const float LeverX = 2f;
    private const float LeverSpacing = 6f;
    // LeverHead는 밀린 쪽으로 막대를 돌린다(막대가 향하는 방향이 '밀린 방향'이 된다). SpawnLever의 기본 방위로는
    // 쉬는 막대가 남서쪽을 향하므로, 북쪽을 향하게 조립 전체를 +135도 돌린다.
    private const float LeverMountYaw = 135f;

    // 시험용 단계별 정답 순서(레버 번호). IsolationRescueSelfTest의 기본값과 같다 — 미확정 콘텐츠 구성안.
    private static readonly int[][] StageOrders =
    {
        new[] { 2, 1, 3 },
        new[] { 1, 2, 3 },
        new[] { 3, 2, 1 }
    };

    /// <summary>만든 것들 중 배선 검증에 필요한 핸들.</summary>
    public class Result
    {
        public GameObject root;
        public IsolationRescueController controller;
        public IsolationTimer timer;
        public IsolationRescueHud hud;
        public IsolationCaptureTrigger capture;
        public IsolationEntryDoor entryDoor;
        public doorPhysics[] passDoors;
        public doorPhysics[] emergencyDoors;
        public doorPhysics releaseDoor;
        public ClueBoard[] clueBoards;
        public CorrespondenceMap map;
        public SequenceLever[] levers;
        public PowerHoldSwitch power;
        public IsolationReadyButton readyIn;
        public IsolationReadyButton readyOut;
        public ReleaseButton release;
        public IsolationRoleReturn roleReturn;
        public SectionSafePoint insideSafe;
        public SectionSafePoint outsideSafe;
    }

    [MenuItem("Tools/Isolation Rescue/Create Test Room")]
    private static void CreateMenu()
    {
        Vector3 origin = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        Result r = Build(origin);
        Undo.RegisterCreatedObjectUndo(r.root, "Create Isolation Test Room");
        Selection.activeGameObject = r.root;
        AssetDatabase.SaveAssets();

        Debug.Log("[IsolationRescue] 시험 격리실 생성 완료. 안쪽은 +Z 방향이다. 플레이어를 바깥(z<0)에서 시작해 " +
                  "진입 트리거를 지나 안쪽으로 걸어 들어가면 격리가 시작된다. 이 배치는 시험용이며 정식 레벨이 아니다. " +
                  "RespawnController가 씬에 없으면 추락 복귀 연결은 비어 있다.", r.root);
    }

    /// <summary>시험 격리실을 origin 기준으로 만들고 전부 연결한다. 씬 저장·선택·Undo 등록은 호출자 몫.</summary>
    public static Result Build(Vector3 origin)
    {
        var r = new Result();
        EnsureFolders();

        r.root = new GameObject("IsolationRescue_TestRoom");
        r.root.transform.position = origin;
        Transform root = r.root.transform;

        // ── 정답 로직(컨트롤러·타이머·HUD) ───────────────────────────────
        GameObject logic = NewChild(root, "C2_Controller", Vector3.zero);
        r.controller = logic.AddComponent<IsolationRescueController>();
        r.timer = logic.AddComponent<IsolationTimer>();
        r.timer.duration = 90f; // 시험용. 실제 값은 플레이테스트로 정한다(PRD §5).
        r.controller.timer = r.timer;
        r.controller.stages = BuildStages();

        r.hud = NewChild(root, "C2_Hud", Vector3.zero).AddComponent<IsolationRescueHud>();
        r.hud.controller = r.controller;

        // ── 바닥·벽 ───────────────────────────────────────────────────────
        BuildShell(root);

        // ── 문 ────────────────────────────────────────────────────────────
        float entryZ = 0f;
        float TotalRoomZ(int room) => entryZ + RoomLength * room; // room 0..3 경계 평면

        GameObject entryDoorGo = SpawnDoorAt(root, "C2_ENTRY", new Vector3(0f, DoorCenterY, entryZ), 0f);
        r.entryDoor = entryDoorGo.AddComponent<IsolationEntryDoor>();
        r.entryDoor.controller = r.controller;
        r.entryDoor.door = entryDoorGo.GetComponent<doorPhysics>();

        r.passDoors = new doorPhysics[RoomCount - 1];
        for (int i = 0; i < r.passDoors.Length; i++)
        {
            GameObject d = SpawnDoorAt(root, $"C2_PASS_{i + 1}", new Vector3(0f, DoorCenterY, TotalRoomZ(i + 1)), 0f);
            r.passDoors[i] = d.GetComponent<doorPhysics>();
        }

        GameObject releaseGo = SpawnDoorAt(root, "C2_RELEASE_DOOR",
            new Vector3(0f, DoorCenterY, TotalRoomZ(RoomCount)), 0f);
        r.releaseDoor = releaseGo.GetComponent<doorPhysics>();

        // 비상문은 방마다 동쪽 벽에 둔다 — "모든 내부 공간에서 접근 가능"(PRD §3). 90도 돌려 벽 면에 맞춘다.
        float eastX = RoomWidth * 0.5f + WallThickness * 0.5f;
        r.emergencyDoors = new doorPhysics[RoomCount];
        for (int i = 0; i < RoomCount; i++)
        {
            float zc = entryZ + RoomLength * (i + 0.5f);
            GameObject d = SpawnDoorAt(root, $"C2_EMERGENCY_{i + 1}", new Vector3(eastX, DoorCenterY, zc), 90f);
            r.emergencyDoors[i] = d.GetComponent<doorPhysics>();
        }

        // ── 진입 트리거 ───────────────────────────────────────────────────
        GameObject captureGo = NewChild(root, "C2_CAPTURE", new Vector3(0f, 3f, entryZ));
        BoxCollider captureCol = captureGo.AddComponent<BoxCollider>();
        captureCol.isTrigger = true;
        captureCol.size = new Vector3(RoomWidth, 6f, 4f);
        r.capture = captureGo.AddComponent<IsolationCaptureTrigger>();
        r.capture.controller = r.controller;

        // ── 바깥: 대응표·레버·전원 스위치·준비 버튼 ───────────────────────
        r.map = Zone<CorrespondenceMap>(root, "C2_MAP", new Vector3(-5f, 0.15f, -8f), 5f, "Info");
        r.map.controller = r.controller;

        r.levers = new SequenceLever[IsolationRescueController.LeverCount];
        for (int i = 0; i < r.levers.Length; i++)
            r.levers[i] = BuildLever(root, r.controller, i + 1, new Vector3(LeverX, LeverPivotY, -6f - LeverSpacing * i));

        r.power = Zone<PowerHoldSwitch>(root, "C2_POWER", new Vector3(0f, 0.15f, -22f), 3.5f, "Power");
        r.power.controller = r.controller;

        r.readyOut = Zone<IsolationReadyButton>(root, "C2_READY_OUT", new Vector3(-5f, 0.15f, -20f), 3f, "Zone");
        r.readyOut.controller = r.controller;
        r.readyOut.inside = false;
        r.readyOut.hud = r.hud;

        // ── 안쪽: 방마다 순서판, 방1에 준비 버튼, 방3에 해제 버튼 ─────────
        r.clueBoards = new ClueBoard[RoomCount];
        for (int i = 0; i < RoomCount; i++)
        {
            float zc = entryZ + RoomLength * (i + 0.5f);
            r.clueBoards[i] = Zone<ClueBoard>(root, $"C2_CLUE_{i + 1}", new Vector3(0f, 0.15f, zc), 5f, "Info");
            r.clueBoards[i].controller = r.controller;
            r.clueBoards[i].stageIndex = i;
        }

        r.readyIn = Zone<IsolationReadyButton>(root, "C2_READY_IN",
            new Vector3(-5f, 0.15f, entryZ + RoomLength * 0.5f), 3f, "Zone");
        r.readyIn.controller = r.controller;
        r.readyIn.inside = true;
        r.readyIn.hud = r.hud;

        r.release = Zone<ReleaseButton>(root, "C2_RELEASE",
            new Vector3(-5f, 0.15f, entryZ + RoomLength * (RoomCount - 0.5f)), 3f, "Release");
        r.release.controller = r.controller;
        r.release.hud = r.hud;

        // ── 추락 복귀(SectionRespawn 경로 재사용) ─────────────────────────
        r.insideSafe = SafePoint(root, "C2_IN_SAFE", new Vector3(5f, 1.5f, entryZ + RoomLength * 0.5f));
        r.outsideSafe = SafePoint(root, "C2_OUT_SAFE", new Vector3(5f, 1.5f, -14f));

        GameObject fall = NewChild(root, "C2_FALL_RETURN", new Vector3(0f, -10f, 24f));
        BoxCollider fallCol = fall.AddComponent<BoxCollider>();
        fallCol.isTrigger = true;
        fallCol.size = new Vector3(80f, 2f, 130f);
        r.roleReturn = fall.AddComponent<IsolationRoleReturn>();
        r.roleReturn.controller = r.controller;
        r.roleReturn.respawn = Object.FindObjectOfType<RespawnController>();
        r.roleReturn.insideSafePoint = r.insideSafe;
        r.roleReturn.outsideSafePoint = r.outsideSafe;

        Wire(r);

        EditorUtility.SetDirty(r.controller);
        return r;
    }

    // ── 이벤트 배선 ───────────────────────────────────────────────────────

    // PRD §4 상태 전이표의 결과를 문 동작으로 옮긴다. 정식 레벨도 이 표를 그대로 따르면 된다.
    private static void Wire(Result r)
    {
        IsolationRescueController c = r.controller;

        // 단계 완료 → 그 단계의 다음 방 문을 영구 개방(다시 닫지 않음). 마지막 단계는 최종 해제 대기로 넘어간다.
        for (int i = 0; i < r.passDoors.Length; i++)
            OpenDoor(c.stages[i].onCompleted, r.passDoors[i]);

        // 대기 → 준비: 진입문을 닫으며 끼임 검사. 취소되면 다시 연다.
        UnityEventTools.AddVoidPersistentListener(c.onCaptureConfirmed, r.entryDoor.BeginClose);
        UnityEventTools.AddVoidPersistentListener(c.onCaptureCancelled, r.entryDoor.Open);

        // 시간 초과: 비상문 개방(패널티 아님 — CH3 경로 합류).
        foreach (doorPhysics d in r.emergencyDoors) OpenDoor(c.onTimedOut, d);

        // 중단: 진입문과 비상문 개방, 보상 없음.
        UnityEventTools.AddVoidPersistentListener(c.onAborted, r.entryDoor.Open);
        foreach (doorPhysics d in r.emergencyDoors) OpenDoor(c.onAborted, d);

        // 성공: 격리 해제 + CH4 숏컷 경로(해제문) 개방.
        OpenDoor(c.onSuccess, r.releaseDoor);

        // 전체 재시작: 열렸던 문을 모두 닫고 진입문은 연다.
        UnityEventTools.AddVoidPersistentListener(c.onRestarted, r.entryDoor.Open);
        foreach (doorPhysics d in r.passDoors) CloseDoor(c.onRestarted, d);
        foreach (doorPhysics d in r.emergencyDoors) CloseDoor(c.onRestarted, d);
        CloseDoor(c.onRestarted, r.releaseDoor);
    }

    private static void OpenDoor(UnityEventBase evt, doorPhysics door)
    {
        UnityEventTools.AddBoolPersistentListener(evt, door.SetPadPressed, true);
    }

    private static void CloseDoor(UnityEventBase evt, doorPhysics door)
    {
        UnityEventTools.AddBoolPersistentListener(evt, door.SetPadPressed, false);
    }

    private static IsolationRescueController.Stage[] BuildStages()
    {
        var stages = new IsolationRescueController.Stage[StageOrders.Length];
        for (int i = 0; i < stages.Length; i++)
        {
            stages[i] = new IsolationRescueController.Stage { answerOrder = (int[])StageOrders[i].Clone() };
        }
        return stages;
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────────────────

    private static GameObject NewChild(Transform parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go;
    }

    private static GameObject SpawnDoorAt(Transform parent, string name, Vector3 localPosition, float yaw)
    {
        // 월드 위치를 부모 기준으로 환산해야 doorPhysics.Awake가 기록하는 시작 위치가 맞는다.
        GameObject door = DoorSystemMenuItem.SpawnDoor(parent.TransformPoint(localPosition));
        door.name = name;
        door.transform.rotation = parent.rotation * Quaternion.Euler(0f, yaw, 0f);
        door.transform.SetParent(parent, true);
        return door;
    }

    // 상호작용/표시 구역: 바닥에 깔린 얇은 트리거 판 + 컴포넌트.
    private static T Zone<T>(Transform parent, string name, Vector3 localPosition, float size, string materialKey)
        where T : Component
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = name;
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = localPosition;
        plate.transform.localScale = new Vector3(size, 0.3f, size);
        plate.GetComponent<Renderer>().sharedMaterial = GetMaterial(materialKey);
        plate.GetComponent<Collider>().isTrigger = true;
        return plate.AddComponent<T>();
    }

    private static SequenceLever BuildLever(Transform parent, IsolationRescueController controller, int number,
                                            Vector3 localPosition)
    {
        Vector3 world = parent.TransformPoint(localPosition);
        GameObject pivot = DoorSystemMenuItem.SpawnLever(world, null);
        pivot.name = "lever_pivot";

        // 조립 전체를 돌리는 마운트. LeverHead는 피벗의 '로컬' 각도(-45 쉼 ~ +45 당김)로 동작하므로 부모를 돌려도 된다.
        GameObject mount = NewChild(parent, $"C2_LEVER_{number}", localPosition);
        mount.transform.localRotation = Quaternion.Euler(0f, LeverMountYaw, 0f);
        pivot.transform.SetParent(mount.transform, false);
        pivot.transform.localPosition = Vector3.zero;
        pivot.transform.localRotation = Quaternion.Euler(0f, -45f, 0f);

        // SpawnLever가 받침대(lever_body)를 씬 루트에 따로 만든다 — 같은 위치의 것을 찾아 마운트 아래로 옮긴다.
        // (받침대는 피벗의 자식이 아니라 돌지 않지만, 마운트를 돌려도 크기 대칭이라 모양은 같다.)
        foreach (Transform t in Object.FindObjectsOfType<Transform>())
        {
            if (t.name != "lever_body" || t.parent != null) continue;
            if ((t.position - (world + new Vector3(0.2f, -0.5835f, 0.4338f))).sqrMagnitude > 0.01f) continue;
            t.SetParent(mount.transform, true);
            // 받침대는 모양만 남긴다. 솔리드로 두면 플레이어가 막대에 닿기도 전에 받침대에 막힌다 — 정육면체는 높이
            // 0.9의 받침대를 넘지 못해 막대를 밀 수 없었다(2026-10-03 실측).
            Collider baseCollider = t.GetComponent<Collider>();
            if (baseCollider != null) Object.DestroyImmediate(baseCollider);
            break;
        }

        LeverHead head = pivot.GetComponentInChildren<LeverHead>();

        // 순서 입력은 다음 레버를 바로 조작해야 하므로 되돌림을 빠르게 한다(SequenceLever 주석 참고).
        // 문 레버의 기본(returnDelay 1 / returnSpeed 0.1)은 전체 복귀에 수십 초가 걸린다.
        head.returnDelay = 0.5f;
        head.returnSpeed = 3f;

        SequenceLever lever = pivot.AddComponent<SequenceLever>();
        lever.controller = controller;
        lever.lever = head;
        lever.leverNumber = number;
        return lever;
    }

    private static SectionSafePoint SafePoint(Transform parent, string id, Vector3 localPosition)
    {
        GameObject go = NewChild(parent, id, localPosition);
        SectionSafePoint sp = go.AddComponent<SectionSafePoint>();
        sp.sectionId = id;
        return sp;
    }

    // 바닥과 벽. 안쪽(+Z)으로 방 3개, 바깥(-Z)으로 대기 구역, 끝에 숏컷 구역, 동쪽에 비상 복도.
    private static void BuildShell(Transform root)
    {
        float halfW = RoomWidth * 0.5f;
        float wallX = halfW + WallThickness * 0.5f;
        float zMin = -OutsideDepth;
        float zMax = RoomLength * RoomCount + ShortcutDepth;
        float zLen = zMax - zMin;
        float zMid = (zMax + zMin) * 0.5f;
        float roomsEnd = RoomLength * RoomCount;
        float corridorOuterX = wallX + 16f;

        Box(root, "Floor", new Vector3(0f, -0.5f, zMid), new Vector3(RoomWidth + WallThickness * 2f, 1f, zLen), "Floor");
        Box(root, "EmergencyCorridor_Floor", new Vector3((wallX + corridorOuterX) * 0.5f, -0.5f, roomsEnd * 0.5f),
            new Vector3(corridorOuterX - wallX, 1f, roomsEnd), "Floor");
        Box(root, "EmergencyCorridor_OuterWall", new Vector3(corridorOuterX + 0.5f, WallHeight * 0.5f, roomsEnd * 0.5f),
            new Vector3(WallThickness, WallHeight, roomsEnd), "Wall");

        // 서쪽 벽은 끝까지 한 장.
        Box(root, "Wall_W", new Vector3(-wallX, WallHeight * 0.5f, zMid), new Vector3(WallThickness, WallHeight, zLen), "Wall");

        // 동쪽 벽: 바깥·숏컷 구간은 통짜, 방 구간은 비상문(폭 15.45)을 끼우고 남는 양끝만 막는다.
        Box(root, "Wall_E_Outside", new Vector3(wallX, WallHeight * 0.5f, zMin * 0.5f),
            new Vector3(WallThickness, WallHeight, -zMin), "Wall");
        Box(root, "Wall_E_Shortcut", new Vector3(wallX, WallHeight * 0.5f, roomsEnd + ShortcutDepth * 0.5f),
            new Vector3(WallThickness, WallHeight, ShortcutDepth), "Wall");
        float filler = (RoomLength - RoomWidth) * 0.5f;
        for (int i = 0; i < RoomCount; i++)
        {
            float z0 = RoomLength * i;
            float z1 = RoomLength * (i + 1);
            Box(root, $"Wall_E_Room{i + 1}_A", new Vector3(wallX, WallHeight * 0.5f, z0 + filler * 0.5f),
                new Vector3(WallThickness, WallHeight, filler), "Wall");
            Box(root, $"Wall_E_Room{i + 1}_B", new Vector3(wallX, WallHeight * 0.5f, z1 - filler * 0.5f),
                new Vector3(WallThickness, WallHeight, filler), "Wall");
        }

        // 바깥 끝 벽.
        Box(root, "Wall_N_Back", new Vector3(0f, WallHeight * 0.5f, zMin - WallThickness * 0.5f),
            new Vector3(RoomWidth + WallThickness * 2f, WallHeight, WallThickness), "Wall");
    }

    private static void Box(Transform parent, string name, Vector3 localPosition, Vector3 size, string materialKey)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = GetMaterial(materialKey);
    }

    // ── 재질 ──────────────────────────────────────────────────────────────

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");
    }

    private static Material GetMaterial(string key)
    {
        Color color;
        switch (key)
        {
            case "Floor": color = new Color(0.55f, 0.57f, 0.6f, 1f); break;
            case "Wall": color = new Color(0.32f, 0.34f, 0.4f, 1f); break;
            case "Info": color = new Color(0.3f, 0.65f, 0.95f, 1f); break;
            case "Power": color = new Color(1f, 0.85f, 0.25f, 1f); break;
            case "Release": color = new Color(0.9f, 0.35f, 0.3f, 1f); break;
            default: color = new Color(0.4f, 0.8f, 0.5f, 1f); break;
        }

        string path = $"{MaterialFolder}/IR_{key}_Mat.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(DoorSystemMenuItem과 같은 방식).
        Shader s = Shader.Find("Universal Render Pipeline/Lit")
                   ?? Shader.Find("HDRP/Lit")
                   ?? Shader.Find("Standard");
        var mat = new Material(s);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
