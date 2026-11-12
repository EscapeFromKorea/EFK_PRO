using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CH1(경로 추격) 테스트 방 — 지금 열린 아무 씬(RespawnController만 있으면 됨)의 떨어진 공간(BasePos)에
/// Lab_CH1과 같은 구성을 "CH1_TestRoom" 루트 하나 아래 만들고 배선까지 끝낸다(Undo 한 번).
///
/// 각 기믹은 그 폴더의 생성 메뉴로 만들고(교차 폴더 파일은 수정하지 않는다), 배치·배선은
/// <see cref="Ch1LevelLayoutMenuItem.LayoutCore"/>를 원점만 옮겨 그대로 쓴다 — Lab_CH1과 방이 같은 규칙으로
/// 배치되게 하려는 것. Layout이 모르는 것(포탈 한 쌍·태엽 발판·카트 HoldPad 배선)만 여기서 더한다. 이 셋은
/// 사용자가 Lab_CH1 씬에서 직접 놓은 값을 옮겨 왔다.
///
/// 바닥은 Lab_CH1 바닥("Cube")과 같은 크기·중심이지만 평평하다(원본은 미세하게 기울어져 있다).
/// </summary>
public static class Ch1TestRoomMenuItem
{
    private static readonly Vector3 BasePos = new Vector3(-300f, 50f, 0f); // CH8 방(300,50,0)과 반대편
    private const string RootName = "CH1_TestRoom";
    private const string UndoName = "Create CH1 Test Room";

    // Lab_CH1 "Cube"의 위치·스케일(레벨 좌표, 윗면 = 0).
    private static readonly Vector3 FloorLocalPos = new Vector3(0.27262f, -0.5f, -15.1f);
    private static readonly Vector3 FloorScale = new Vector3(40.720196f, 1f, 131.24861f);

    // Lab_CH1에서 사용자가 축(-9, -62) 앞에 놓은 입·출구 포탈과 발판(레벨 x,z). 입구로 들어가 굴리기 모드로
    // 축 막대를 밀고, 출구로 나와 원래 이동으로 돌아간다.
    private static readonly Vector2 PortalEnterXZ = new Vector2(-7.96f, -58.09f);
    private static readonly Vector2 PortalExitXZ = new Vector2(-10.74f, -58.24f);
    private static readonly Vector2 PadXZ = new Vector2(-12.86f, -60.62f);

    // 체크포인트(-12, 44, 폭 6) 안 — 시작하자마자 체크포인트가 잡혀 떨어져도 방 안으로 복귀한다.
    private static readonly Vector3[] StartLocal =
    {
        new Vector3(-13f, 1.5f, 44f), new Vector3(-11.5f, 1.5f, 44f), new Vector3(-10f, 1.5f, 44f)
    };

    [MenuItem("Tools/PathChaserSystem/Create CH1 Test Room")]
    public static void CreateTestRoom()
    {
        if (GameObject.Find(RootName) != null)
        {
            Debug.LogWarning($"[CH1 Room] 씬에 이미 '{RootName}'이 있다 — 다시 만들려면 먼저 지워라.");
            return;
        }

        RespawnController respawn = Object.FindObjectOfType<RespawnController>();
        if (respawn == null)
        {
            Debug.LogWarning("[CH1 Room] 씬에 RespawnController가 없어 구간 복귀를 배선할 수 없다 " +
                             "(Tools > Respawn > Create Respawn Controller를 먼저 실행해라).");
            return;
        }

        Undo.SetCurrentGroupName(UndoName);
        int undoGroup = Undo.GetCurrentGroup();
        // 생성 도중 예외가 나면 기믹들이 각자 기본 위치에 흩어진 채 남는다(2026-09-26 실제 발생) — 통째로 되돌린다.
        try { Build(respawn, undoGroup); }
        catch
        {
            Debug.LogError("[CH1 Room] 방 생성 중 예외 — 만든 것을 전부 되돌린다. 아래 예외를 확인해라.");
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
    }

    private static void Build(RespawnController respawn, int undoGroup)
    {
        GameObject root = new GameObject(RootName);
        root.transform.position = BasePos;
        Undo.RegisterCreatedObjectUndo(root, UndoName);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "CH1_Floor";
        Undo.RegisterCreatedObjectUndo(floor, UndoName);
        floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = FloorLocalPos;
        floor.transform.localScale = FloorScale;
        floor.isStatic = true;

        // ── 각 폴더의 생성 메뉴로 만들고 루트 아래로 옮긴다(위치는 LayoutCore와 아래에서 정한다) ──
        var catapult = Adopt(root, SlingCatapultMenuItem.BuildSlingCatapult(BasePos, CatapultMenuItem.Scale, "SlingCatapult"))
            ?.GetComponent<CatapultLoadController>();
        var wall = Menu<BreakableObject>(root, "Tools/DestructionSystem/Create Breakable/Cube");
        var axle = Menu<WindupAxle>(root, "Tools/WindupAxleSystem/Create Windup Axle");
        var pad = Menu<WindupActivationPad>(root, "Tools/WindupAxleSystem/Create Activation Pad");
        var portalEnter = Menu<Portal>(root, "Tools/PortalSystem/Create Portal (Enter — 굴리기 켬)");
        var portalExit = Menu<Portal>(root, "Tools/PortalSystem/Create Portal (Exit — 원래 이동으로 복귀)");

        RespawnMenuItem.CreateCheckpoint();
        var checkpoint = Adopt(root, Selection.activeGameObject)?.GetComponent<RespawnZone>();
        RespawnMenuItem.CreateRespawnScale();
        var oob = Adopt(root, Selection.activeGameObject)?.GetComponent<OutOfBoundsVolume>();

        var carts = new RailCart[3];
        for (int i = 0; i < carts.Length; i++)
        {
            carts[i] = Adopt(root, RailCartMenuItem.CreateRailCartAt(BasePos)).GetComponent<RailCart>();
            Adopt(root, carts[i].path.gameObject);
        }

        PathChaserController controller = PathChaserMenuItem.BuildRig(BasePos, respawn);
        Adopt(root, controller.gameObject);
        Adopt(root, controller.agent.gameObject);
        foreach (Transform wp in controller.agent.waypoints) Adopt(root, wp.gameObject);
        Adopt(root, controller.safePreCounter.gameObject);
        Adopt(root, controller.safePostCounter.gameObject);

        if (catapult == null || wall == null || axle == null || pad == null || portalEnter == null
            || portalExit == null || checkpoint == null || oob == null)
        {
            Debug.LogError("[CH1 Room] 기믹 생성 메뉴 중 하나가 실패해 방 생성을 되돌린다 — 위 로그를 확인해라.");
            Undo.RevertAllDownToGroup(undoGroup);
            return;
        }

        // ── Lab_CH1과 같은 배치·배선 ──
        Ch1LevelLayoutMenuItem.LayoutCore(new Ch1LevelLayoutMenuItem.Targets
        {
            origin = BasePos, parent = root.transform, floor = floor.GetComponent<Collider>(), respawn = respawn,
            controller = controller, wall = wall, catapult = catapult, axle = axle, carts = carts,
            players = null, checkpoint = checkpoint, oob = oob,
        });

        // ── Layout이 모르는 것: 포탈 쌍·발판(바닥이 평평해 윗면 = 루트 y), 카트 HoldPad 배선 ──
        Place(root, portalEnter.transform, PortalEnterXZ, 0f); // 포탈 피벗은 문 바닥
        Place(root, portalExit.transform, PortalExitXZ, 0f);
        Place(root, pad.transform, PadXZ, pad.transform.localScale.y * 0.5f);
        Undo.RecordObject(pad, UndoName);
        pad.latchOnFirstPress = true; // 한 번 밟으면 계속 구동 — 발판 지킴이 없이 카트 3대가 끝까지 간다(Lab_CH1과 동일)
        foreach (RailCart cart in carts)
        {
            Undo.RecordObject(cart, UndoName);
            cart.activationMode = WindupActivationMode.HoldPad;
            cart.activationPad = pad;
        }

        ReportMissing(controller, pad);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = controller.gameObject;
        Debug.Log("[CH1 Room] 테스트 방 배치 완료. Tools > PathChaserSystem > Move Players To CH1 Start로 플레이어를 " +
                  "옮긴 뒤 플레이하라.", controller);
    }

    /// <summary>씬의 플레이어(Kind 순)를 방 체크포인트 안 시작 지점 위로 옮긴다(Undo 가능).</summary>
    [MenuItem("Tools/PathChaserSystem/Move Players To CH1 Start")]
    public static void MovePlayersToStart()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning($"[CH1 Room] 씬에 '{RootName}'이 없다 — 테스트 방부터 만들어라.");
            return;
        }

        PlayerShapeIdentity[] players = Object.FindObjectsOfType<PlayerShapeIdentity>();
        System.Array.Sort(players, (a, b) => a.Kind.CompareTo(b.Kind));
        for (int i = 0; i < players.Length; i++)
        {
            Undo.RecordObject(players[i].transform, "Move Players To CH1 Start");
            players[i].transform.position = root.transform.TransformPoint(StartLocal[i % StartLocal.Length]);
        }
        Debug.Log($"[CH1 Room] 플레이어 {players.Length}명을 CH1 시작 지점으로 옮겼다. 원래 위치로는 Ctrl+Z.");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

    private static GameObject Adopt(GameObject root, GameObject go)
    {
        if (go != null) Undo.SetTransformParent(go.transform, root.transform, UndoName);
        return go;
    }

    // 생성 메뉴가 private인 폴더가 많아 메뉴 경로로 실행한다. 모든 생성 메뉴가 만든 루트를 선택해 둔다.
    private static T Menu<T>(GameObject root, string path) where T : Component
    {
        Selection.activeGameObject = null;
        if (!EditorApplication.ExecuteMenuItem(path) || Selection.activeGameObject == null)
        {
            Debug.LogError($"[CH1 Room] 메뉴 실행 실패: {path}");
            return null;
        }
        T c = Adopt(root, Selection.activeGameObject).GetComponent<T>();
        if (c == null) Debug.LogError($"[CH1 Room] '{path}'가 만든 오브젝트에 {typeof(T).Name}이 없다.");
        return c;
    }

    private static void Place(GameObject root, Transform t, Vector2 xz, float y)
    {
        Undo.RecordObject(t, UndoName);
        t.SetPositionAndRotation(root.transform.TransformPoint(new Vector3(xz.x, y, xz.y)), Quaternion.identity);
    }

    // 컨트롤러는 필수 참조가 하나라도 비면 플레이 시작을 거부한다 — 방을 만든 직후 알린다.
    private static void ReportMissing(PathChaserController c, WindupActivationPad pad)
    {
        var missing = new List<string>();
        if (c.agent == null || c.agent.waypoints == null || c.agent.waypoints.Length == 0
            || c.agent.waypoints.Any(w => w == null)) missing.Add("agent/waypoints");
        if (c.wall == null) missing.Add("wall");
        if (c.carts == null || c.carts.Length != 3 || c.carts.Any(x => x == null || x.axle == null
            || x.path == null || x.activationPad != pad)) missing.Add("carts(3대, axle/path/activationPad)");
        if (c.arrivalPoint == null) missing.Add("arrivalPoint");
        if (c.teamExitZone == null) missing.Add("teamExitZone");
        if (c.safePreCounter == null || c.safePreCounter.OnThresholdReached == null
            || c.safePreCounter.OnThresholdReached.GetPersistentEventCount() == 0)
            missing.Add("safePreCounter(+RespawnPlayer 배선)");
        if (c.safePostCounter == null || c.safePostCounter.OnThresholdReached == null
            || c.safePostCounter.OnThresholdReached.GetPersistentEventCount() == 0)
            missing.Add("safePostCounter(+RespawnPlayer 배선)");

        if (missing.Count > 0)
            Debug.LogError("[CH1 Room] 컨트롤러 참조가 비었다 — 플레이 시 시작 거부된다: " + string.Join(", ", missing), c);
    }
}
