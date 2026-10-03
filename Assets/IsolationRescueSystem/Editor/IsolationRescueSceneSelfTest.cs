using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 격리 구출 씬 컴포넌트(레버·전원 스위치·진입 트리거·진입문·버튼·표시판·복귀)의 로직 자가검증과 시험 배치
/// 메뉴의 배선 검증. docs/PRD/IsolationRescue.md §2~§4, §6.
///
/// [IsolationRescueSelfTest와의 역할 분담]
/// 그쪽은 컨트롤러/타이머의 순수 로직이다. 이쪽은 컨트롤러에 입력을 넣어 주는 씬 컴포넌트들이 PRD대로
/// 신호를 만들고 가리는지를 본다. 물리 이벤트(OnTriggerEnter 등)는 EditMode에서 오지 않으므로 컴포넌트가
/// 공개한 HandleEnter/HandleExit를 직접 부른다(WiringPortSelfTest는 리플렉션을 썼지만 여기는 공개 창구를 뒀다).
///
/// [이 검증이 다루지 않는 것 — 플레이로만 확인 가능]
/// 실제 물리 충돌, 문이 내려오며 끼는 느낌, 레버를 밀 때의 감각, OnGUI 화면 배치, 입력 키(E) 반응,
/// 유니티 이벤트가 PlayMode에서 문을 움직이는 것(EditMode의 UnityEvent는 RuntimeOnly라 호출되지 않는다 —
/// 대신 배선 구조를 검사한다).
///
/// 실행: 메뉴 Tools &gt; Isolation Rescue &gt; Run Scene Component Self-Test, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod IsolationRescueSceneSelfTest.RunFromCommandLine`
/// (실패가 하나라도 있으면 종료 코드 1).
/// </summary>
public static class IsolationRescueSceneSelfTest
{
    private static int passed;
    private static int failed;
    private static readonly List<GameObject> created = new List<GameObject>();

    [MenuItem("Tools/Isolation Rescue/Run Scene Component Self-Test")]
    public static void RunFromMenu()
    {
        Run();
    }

    // -executeMethod IsolationRescueSceneSelfTest.RunFromCommandLine
    public static void RunFromCommandLine()
    {
        bool ok = Run();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Run()
    {
        passed = 0;
        failed = 0;
        created.Clear();

        try
        {
            TestSymbolsAndBoards();
            TestSequenceLever();
            TestPowerHoldSwitch();
            TestCaptureTrigger();
            TestEntryDoor();
            TestButtons();
            TestHud();
            TestRoleReturn();
            TestEndToEnd();
            TestBuilder();
        }
        finally
        {
            foreach (GameObject go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        Debug.Log($"[SceneSelfTest] 결과: PASS {passed} / FAIL {failed}");
        return failed == 0;
    }

    // ── 리그 ──────────────────────────────────────────────────────────────

    private class Rig
    {
        public IsolationRescueController c;
        public IsolationTimer t;
        public float now;
    }

    private static readonly int[][] Orders =
    {
        new[] { 2, 1, 3 },
        new[] { 1, 2, 3 },
        new[] { 3, 2, 1 }
    };

    private static Rig NewRig()
    {
        var r = new Rig();
        GameObject go = Track(new GameObject("Rig"));
        r.c = go.AddComponent<IsolationRescueController>();
        r.t = go.AddComponent<IsolationTimer>();
        r.t.duration = 60f;
        r.t.Clock = () => r.now;
        r.c.timer = r.t;
        r.c.stages = new IsolationRescueController.Stage[Orders.Length];
        for (int i = 0; i < Orders.Length; i++)
            r.c.stages[i] = new IsolationRescueController.Stage { answerOrder = (int[])Orders[i].Clone() };
        return r;
    }

    private static GameObject Track(GameObject go)
    {
        created.Add(go);
        return go;
    }

    private class Player
    {
        public PlayerMover mover;
        public Collider mesh;
        public Collider body;
    }

    // 도형 하나 = 트리거(Player_Mesh) + 솔리드(Player_Collider) 두 콜라이더, 둘 다 Player 태그.
    private static Player NewPlayer(string name, Vector3 position)
    {
        GameObject root = Track(new GameObject("Player_" + name));
        root.transform.position = position;
        var p = new Player { mover = root.AddComponent<PlayerMover>() };

        GameObject meshObj = new GameObject("Player_Mesh");
        meshObj.transform.SetParent(root.transform, false);
        meshObj.tag = "Player";
        p.mesh = meshObj.AddComponent<BoxCollider>();

        GameObject bodyObj = new GameObject("Player_Collider");
        bodyObj.transform.SetParent(root.transform, false);
        bodyObj.tag = "Player";
        p.body = bodyObj.AddComponent<BoxCollider>();
        return p;
    }

    private static void EnterBoth(IsolationOccupancyZone zone, Player p)
    {
        zone.HandleEnter(p.mesh);
        zone.HandleEnter(p.body);
    }

    // 준비 상태까지 끌어올린다(대상 확정 → 문 안전 → 양쪽 준비 직전).
    private static void ToPreparing(Rig r, Player inside)
    {
        r.c.RequestCapture(inside.mover.gameObject);
        r.c.ReportDoorClosedSafe();
    }

    private static void ToInProgress(Rig r, Player inside)
    {
        ToPreparing(r, inside);
        r.c.SetReady(true);
        r.c.SetReady(false);
    }

    private static SequenceLever NewLever(Rig r, int number)
    {
        GameObject go = Track(new GameObject("Lever_" + number));
        SequenceLever l = go.AddComponent<SequenceLever>();
        l.controller = r.c;
        l.leverNumber = number;
        return l;
    }

    // ── 기호/표시판 ──────────────────────────────────────────────────────

    private static void TestSymbolsAndBoards()
    {
        Check("기호: 레버 1~3 이름 = 파도/달/십자",
            IsolationSymbols.NameOf(IsolationSymbols.DefaultNames, 1) == "파도" &&
            IsolationSymbols.NameOf(IsolationSymbols.DefaultNames, 2) == "달" &&
            IsolationSymbols.NameOf(IsolationSymbols.DefaultNames, 3) == "십자");
        Check("기호: 범위 밖·null 이름은 '?'로 화면이 깨지지 않음",
            IsolationSymbols.NameOf(IsolationSymbols.DefaultNames, 0) == "?" &&
            IsolationSymbols.NameOf(IsolationSymbols.DefaultNames, 4) == "?" &&
            IsolationSymbols.NameOf(null, 1) == "?" &&
            IsolationSymbols.NameOf(new[] { "" }, 1) == "?");

        Rig r = NewRig();
        Player inside = NewPlayer("In", Vector3.zero);
        Player outside = NewPlayer("Out", Vector3.zero);

        ClueBoard board = Track(new GameObject("Clue")).AddComponent<ClueBoard>();
        board.controller = r.c;
        board.stageIndex = 0;
        CorrespondenceMap map = Track(new GameObject("Map")).AddComponent<CorrespondenceMap>();
        map.controller = r.c;

        // 격리 전: 순서판은 아무에게도 안 보이고, 대응표는 앞에 선 누구에게나 보인다.
        EnterBoth(board, inside);
        EnterBoth(board, outside);
        EnterBoth(map, inside);
        EnterBoth(map, outside);
        Check("순서판: 대기 중에는 아무에게도 안 보임", !board.ShouldShowTo(inside.mover) && !board.ShouldShowTo(outside.mover));
        Check("순서판: 대기 중 텍스트 비어 있음", board.BuildText() == string.Empty);
        Check("대응표: 격리 전에는 앞에 선 누구에게나 보임", map.ShouldShowTo(inside.mover) && map.ShouldShowTo(outside.mover));

        ToInProgress(r, inside);
        Check("순서판: 안쪽 참가자에게만 보임(바깥은 같은 자리에 서도 안 보임)",
            board.ShouldShowTo(inside.mover) && !board.ShouldShowTo(outside.mover));
        Check("순서판: 텍스트 = 달 → 파도 → 십자 (정답 [2,1,3])", board.BuildText() == "달 → 파도 → 십자");
        Check("대응표: 안쪽에게는 가려지고 바깥에게는 보임", !map.ShouldShowTo(inside.mover) && map.ShouldShowTo(outside.mover));
        Check("대응표: 텍스트 = 파도=1/달=2/십자=3", map.BuildText() == "파도 = 레버 1\n달 = 레버 2\n십자 = 레버 3");

        // 오답 → 정답 순서가 왼쪽으로 한 칸 순환. 표시도 따라가야 하고 대응표는 불변.
        string mapBefore = map.BuildText();
        r.c.SubmitLever(2 == r.c.CurrentOrder[0] ? 3 : 2);
        Check("순서판: 오답 후 텍스트가 순환(파도 → 십자 → 달)", board.BuildText() == "파도 → 십자 → 달");
        Check("대응표: 오답 후에도 불변(C2-03 재도전 규칙)", map.BuildText() == mapBefore);

        board.stageIndex = 1;
        Check("순서판: 맡은 단계가 현재 단계가 아니면 안 보임", !board.ShouldShowTo(inside.mover));
        board.stageIndex = 0;

        board.HandleExit(inside.mesh);
        board.HandleExit(inside.body);
        Check("순서판: 판 앞을 벗어나면 안쪽이어도 안 보임", !board.ShouldShowTo(inside.mover));
        Check("표시판: 보는 사람 null이면 안 그림", !board.ShouldShowTo(null) && !map.ShouldShowTo(null));
    }

    // ── 순서 레버 ────────────────────────────────────────────────────────

    private static void TestSequenceLever()
    {
        Rig r = NewRig();
        Player inside = NewPlayer("In", Vector3.zero);
        SequenceLever l2 = NewLever(r, 2);

        // 격리 시작 전 입력은 컨트롤러가 무시하지만, 레버는 문턱 에지를 한 번만 센다.
        Check("레버: 문턱 미만에서는 신호 없음", !l2.Evaluate(0.3f));
        Check("레버: 문턱(0.9) 도달 시 신호 1회", l2.Evaluate(0.9f));
        Check("레버: 당긴 채 유지하면 반복 신호 없음(PRD '반복 신호 금지')", !l2.Evaluate(1f) && !l2.Evaluate(0.95f));
        Check("레버: 문턱 아래·해제선 위(0.7)에서는 아직 무장 해제 상태", !l2.Evaluate(0.7f) && !l2.Armed);
        Check("레버: 해제선(0.5) 이하로 돌아오면 다시 무장", !l2.Evaluate(0.5f) && l2.Armed);
        Check("레버: 다시 당기면 신호 1회", l2.Evaluate(1f));
        Check("레버: 진행 전(준비 아님) 입력은 컨트롤러가 무시", r.c.InputCount == 0);

        // 진행 중 정답 흐름 — 레버 3개로 1단계(정답 [2,1,3])를 푼다.
        Rig r2 = NewRig();
        Player p2 = NewPlayer("In2", Vector3.zero);
        ToInProgress(r2, p2);
        SequenceLever a = NewLever(r2, 1), b = NewLever(r2, 2), c = NewLever(r2, 3);

        b.Evaluate(1f);
        Check("레버: 정답 첫 입력 → 진행도 1/3", r2.c.InputCount == 1);
        c.Evaluate(1f); // 오답(두 번째는 1이어야 함)
        Check("레버: 오답 → 입력 삭제·정답 순서 순환(버전 1)", r2.c.InputCount == 0 && r2.c.QuestionVersion == 1);
        Check("레버: 오답 후 현재 순서 [1,3,2]", r2.c.CurrentOrder[0] == 1 && r2.c.CurrentOrder[1] == 3 && r2.c.CurrentOrder[2] == 2);

        // 이미 당긴 레버(b, c)를 놓아야 다시 입력된다. 새 순서 1→3→2.
        b.Evaluate(0f);
        c.Evaluate(0f);
        a.Evaluate(1f);
        c.Evaluate(1f);
        b.Evaluate(1f);
        Check("레버: 순환된 순서로 풀면 1단계 완료 → 2단계", r2.c.StageIndex == 1 && r2.c.InputCount == 0);

        // NormalizedPosition: LeverHead 각도 → 0~1
        GameObject pivot = Track(new GameObject("Pivot"));
        LeverHead head = pivot.AddComponent<LeverHead>();
        head.leverPivot = pivot.transform;
        head.maxAngle = 45f;
        pivot.transform.localRotation = Quaternion.Euler(0f, -45f, 0f);
        float lo = SequenceLever.NormalizedPosition(head);
        pivot.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        float hi = SequenceLever.NormalizedPosition(head);
        pivot.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        float mid = SequenceLever.NormalizedPosition(head);
        Check("레버: 각도 정규화 -45° → 0, +45° → 1, 0° → 0.5",
            Mathf.Approximately(lo, 0f) && Mathf.Approximately(hi, 1f) && Mathf.Abs(mid - 0.5f) < 0.001f);
        Check("레버: LeverHead 없으면 0", SequenceLever.NormalizedPosition(null) == 0f);

        // 컨트롤러 없이도 예외 없이 무장만 해제된다.
        SequenceLever lone = Track(new GameObject("Lone")).AddComponent<SequenceLever>();
        Check("레버: 컨트롤러 없으면 신호 안 보냄(예외 없음)", !lone.Evaluate(1f));
    }

    // ── 전원 스위치 ──────────────────────────────────────────────────────

    private static void TestPowerHoldSwitch()
    {
        Rig r = NewRig();
        PowerHoldSwitch sw = Track(new GameObject("Power")).AddComponent<PowerHoldSwitch>();
        sw.controller = r.c;
        Player p = NewPlayer("P", Vector3.zero);

        Check("전원: 초기 꺼짐", !sw.IsOn && !r.c.PowerOn);

        sw.HandleEnter(p.mesh);
        Check("전원: Player 콜라이더가 올라서면 켜짐 → 컨트롤러 반영", sw.IsOn && r.c.PowerOn);
        sw.HandleEnter(p.body);
        sw.HandleExit(p.mesh);
        Check("전원: 콜라이더 하나만 나가도 유지(도형은 콜라이더 2개)", sw.IsOn && r.c.PowerOn);
        sw.HandleExit(p.body);
        Check("전원: 마지막 콜라이더가 나가면 꺼짐 → 컨트롤러 반영", !sw.IsOn && !r.c.PowerOn);

        GameObject plain = Track(new GameObject("Untagged"));
        Collider other = plain.AddComponent<BoxCollider>();
        sw.HandleEnter(other);
        Check("전원: 태그 없는 콜라이더는 무시", !sw.IsOn && !r.c.PowerOn);

        GameObject item = Track(new GameObject("Crate"));
        item.tag = "InteractionItem";
        Collider crate = item.AddComponent<BoxCollider>();
        sw.HandleEnter(crate);
        Check("전원: InteractionItem(상자)도 누름으로 침(PadTrigger와 동일)", sw.IsOn && r.c.PowerOn);
        sw.HandleExit(crate);

        // Exit 없이 파괴된 콜라이더는 Refresh에서 정리 — 눌림이 영영 안 풀리는 사고 방지.
        Player q = NewPlayer("Q", Vector3.zero);
        sw.HandleEnter(q.mesh);
        Object.DestroyImmediate(q.mover.gameObject);
        sw.Refresh();
        Check("전원: Exit 없이 파괴된 참가자는 정리되어 꺼짐", !sw.IsOn && !r.c.PowerOn);

        // C2-08: 스위치 OFF에서 해제 시도 → 거부(진행 보존) → ON 후 재시도 성공.
        Rig r2 = NewRig();
        Player pi = NewPlayer("In", Vector3.zero);
        PowerHoldSwitch sw2 = Track(new GameObject("Power2")).AddComponent<PowerHoldSwitch>();
        sw2.controller = r2.c;
        ToInProgress(r2, pi);
        FinishStages(r2);
        Check("전원: 최종 해제 대기 상태 도달", r2.c.Current == IsolationRescueController.State.AwaitFinalRelease);
        Check("전원 C2-08: OFF에서 해제 거부·진행 보존",
            !r2.c.TryRelease() && r2.c.Current == IsolationRescueController.State.AwaitFinalRelease);
        sw2.HandleEnter(p.mesh);
        Check("전원 C2-08: ON 후 재시도 성공", r2.c.TryRelease() && r2.c.Current == IsolationRescueController.State.Success);
    }

    // 남은 단계를 전부 정답으로 푼다. 이미 일부 입력한 단계도 현재 입력 위치부터 이어서 넣는다.
    private static void FinishStages(Rig r)
    {
        int guard = 0;
        while (r.c.Current == IsolationRescueController.State.InProgress && guard++ < 20)
            r.c.SubmitLever(r.c.CurrentOrder[r.c.InputCount]);
    }

    // IReadOnlyList → 배열 복사(진행 중 순서가 바뀌므로 먼저 떠 둔다).
    private static int[] ToArray2(this IReadOnlyList<int> list)
    {
        var arr = new int[list.Count];
        for (int i = 0; i < arr.Length; i++) arr[i] = list[i];
        return arr;
    }

    // ── 진입 트리거 ──────────────────────────────────────────────────────

    private static IsolationCaptureTrigger NewCapture(Rig r, float minDepth = 0f)
    {
        GameObject go = Track(new GameObject("Capture"));
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity; // forward = +Z = 안쪽
        go.AddComponent<BoxCollider>().isTrigger = true; // RequireComponent(Collider)는 추상 타입이라 직접 붙여야 한다
        IsolationCaptureTrigger t = go.AddComponent<IsolationCaptureTrigger>();
        t.controller = r.c;
        t.minDepth = minDepth;
        return t;
    }

    // 참가자가 트리거를 통과해 z 위치에 서 있는 상태로 두 콜라이더가 모두 나갔다.
    private static void PassTo(IsolationCaptureTrigger t, Player p, float z)
    {
        t.HandleEnter(p.mesh);
        t.HandleEnter(p.body);
        p.mover.transform.position = new Vector3(0f, 0f, z);
        t.HandleExit(p.mesh);
        t.HandleExit(p.body);
    }

    private static void TestCaptureTrigger()
    {
        // 안쪽으로 통과 → 격리 후보 선정
        Rig r = NewRig();
        IsolationCaptureTrigger t = NewCapture(r);
        Player a = NewPlayer("A", Vector3.zero);
        PassTo(t, a, 3f);
        Check("진입: 통과만으로는 아직 확정 아님(다음 스텝에 판정)", r.c.Current == IsolationRescueController.State.Idle);
        PlayerMover chosen = t.ResolvePending();
        Check("진입: 안쪽으로 통과한 참가자가 격리 대상으로 확정",
            chosen == a.mover && r.c.Current == IsolationRescueController.State.Preparing && r.c.InsidePlayer == a.mover.gameObject);

        // 바깥으로 나가면 후보 아님
        Rig r2 = NewRig();
        IsolationCaptureTrigger t2 = NewCapture(r2);
        Player b = NewPlayer("B", Vector3.zero);
        PassTo(t2, b, -3f);
        Check("진입: 바깥으로 나간 참가자는 후보 아님", t2.ResolvePending() == null && r2.c.Current == IsolationRescueController.State.Idle);

        // 문턱에 걸치기만 (콜라이더 하나만 나감) — 아직 트리거 안이므로 후보 아님
        Rig r3 = NewRig();
        IsolationCaptureTrigger t3 = NewCapture(r3);
        Player c = NewPlayer("C", Vector3.zero);
        t3.HandleEnter(c.mesh);
        t3.HandleEnter(c.body);
        c.mover.transform.position = new Vector3(0f, 0f, 3f);
        t3.HandleExit(c.mesh);
        Check("진입: 콜라이더 하나만 나간 걸침 상태는 후보 아님", t3.ResolvePending() == null && r3.c.Current == IsolationRescueController.State.Idle);
        t3.HandleExit(c.body);
        Check("진입: 마지막 콜라이더까지 나가야 후보", t3.ResolvePending() == c.mover);

        // 동시 통과: 안쪽으로 가장 깊은 한 명만
        Rig r4 = NewRig();
        IsolationCaptureTrigger t4 = NewCapture(r4);
        Player shallow = NewPlayer("Shallow", Vector3.zero);
        Player deep = NewPlayer("Deep", Vector3.zero);
        PassTo(t4, shallow, 2f);
        PassTo(t4, deep, 5f);
        PlayerMover winner = t4.ResolvePending();
        Check("진입: 동시 통과 시 가장 깊이 들어간 한 명만 선정",
            winner == deep.mover && r4.c.InsidePlayer == deep.mover.gameObject);
        Check("진입: 나머지는 선정되지 않고 상태는 준비 하나(두 명 동시 격리 금지)",
            r4.c.InsidePlayer != shallow.mover.gameObject && r4.c.Current == IsolationRescueController.State.Preparing);

        // 깊이가 같으면 인스턴스 ID 순(결정적)
        Rig r5 = NewRig();
        IsolationCaptureTrigger t5 = NewCapture(r5);
        Player x = NewPlayer("X", Vector3.zero);
        Player y = NewPlayer("Y", Vector3.zero);
        PassTo(t5, x, 4f);
        PassTo(t5, y, 4f);
        PlayerMover expected = x.mover.GetInstanceID() < y.mover.GetInstanceID() ? x.mover : y.mover;
        Check("진입: 깊이가 같으면 인스턴스 ID가 작은 쪽(순서 결정적)", t5.ResolvePending() == expected);

        // 판정 전에 다시 트리거로 돌아온 후보는 거름
        Rig r6 = NewRig();
        IsolationCaptureTrigger t6 = NewCapture(r6);
        Player back = NewPlayer("Back", Vector3.zero);
        PassTo(t6, back, 3f);
        t6.HandleEnter(back.mesh);
        Check("진입: 판정 전에 되돌아와 트리거 안인 후보는 선정 안 됨", t6.ResolvePending() == null && r6.c.Current == IsolationRescueController.State.Idle);

        // minDepth
        Rig r7 = NewRig();
        IsolationCaptureTrigger t7 = NewCapture(r7, 1f);
        Player edge = NewPlayer("Edge", Vector3.zero);
        PassTo(t7, edge, 0.5f);
        Check("진입: minDepth보다 얕게 나가면 통과 아님", t7.ResolvePending() == null);

        // 준비 중 바깥으로 되돌아 나오면 취소(§2.2)
        Rig r8 = NewRig();
        IsolationCaptureTrigger t8 = NewCapture(r8);
        Player turn = NewPlayer("Turn", Vector3.zero);
        PassTo(t8, turn, 3f);
        t8.ResolvePending();
        PassTo(t8, turn, -3f);
        Check("진입: 준비 중 후보가 바깥으로 되돌아 나오면 격리 취소(대기 복귀)",
            r8.c.Current == IsolationRescueController.State.Idle && r8.c.InsidePlayer == null);

        // 진행 중에는 되돌아 나가도 취소되지 않음(컨트롤러가 준비 상태에서만 취소 허용)
        Rig r9 = NewRig();
        IsolationCaptureTrigger t9 = NewCapture(r9);
        Player run = NewPlayer("Run", Vector3.zero);
        PassTo(t9, run, 3f);
        t9.ResolvePending();
        r9.c.ReportDoorClosedSafe();
        r9.c.SetReady(true);
        r9.c.SetReady(false);
        PassTo(t9, run, -3f);
        Check("진입: 진행 중에는 트리거를 나가도 취소되지 않음", r9.c.Current == IsolationRescueController.State.InProgress);

        // 이미 격리가 진행 중이면 새 통과자는 후보가 못 됨
        Player late = NewPlayer("Late", Vector3.zero);
        PassTo(t9, late, 4f);
        Check("진입: 격리 진행 중 다른 참가자의 통과는 무시", t9.ResolvePending() == null && r9.c.InsidePlayer == run.mover.gameObject);

        Check("진입: 컨트롤러 없이도 예외 없음", NoThrow(() =>
        {
            GameObject loneGo = Track(new GameObject("LoneCapture"));
            loneGo.AddComponent<BoxCollider>();
            IsolationCaptureTrigger lone = loneGo.AddComponent<IsolationCaptureTrigger>();
            Player pl = NewPlayer("Lone", Vector3.zero);
            lone.HandleEnter(pl.mesh);
            lone.HandleExit(pl.mesh);
            lone.ResolvePending();
        }));
    }

    // ── 진입문 ───────────────────────────────────────────────────────────

    private static IsolationEntryDoor NewEntryDoor(Rig r, out doorPhysics door)
    {
        GameObject go = Track(new GameObject("EntryDoor"));
        door = go.AddComponent<doorPhysics>();
        IsolationEntryDoor e = go.AddComponent<IsolationEntryDoor>();
        e.controller = r.c;
        e.door = door;
        return e;
    }

    private static bool PadPressed(doorPhysics d)
    {
        FieldInfo f = typeof(doorPhysics).GetField("isPadPressed", BindingFlags.Instance | BindingFlags.NonPublic);
        return f != null && (bool)f.GetValue(d);
    }

    private static void TestEntryDoor()
    {
        // 안전 닫힘 → ReportDoorClosedSafe (이후 양쪽 준비로 진행 가능)
        Rig r = NewRig();
        Player p = NewPlayer("P", Vector3.zero);
        IsolationEntryDoor e = NewEntryDoor(r, out doorPhysics door);
        r.c.RequestCapture(p.mover.gameObject);
        e.BeginClose();
        Check("진입문: 닫기 시작하면 패드 해제(문이 내려옴)", e.Current == IsolationEntryDoor.Phase.Closing && !PadPressed(door));
        e.Tick(0.1f, false, false);
        Check("진입문: 아직 닫히는 중이면 보고 없음", e.Current == IsolationEntryDoor.Phase.Closing);
        e.Tick(0.1f, false, true);
        Check("진입문: 안전하게 닫힘 → Closed", e.Current == IsolationEntryDoor.Phase.Closed);
        r.c.SetReady(true);
        r.c.SetReady(false);
        Check("진입문: 안전 보고가 컨트롤러에 전달돼 양쪽 준비 후 1단계 시작", r.c.Current == IsolationRescueController.State.InProgress);

        // 끼임 유예 초과 → 취소
        Rig r2 = NewRig();
        Player p2 = NewPlayer("P2", Vector3.zero);
        IsolationEntryDoor e2 = NewEntryDoor(r2, out doorPhysics door2);
        r2.c.RequestCapture(p2.mover.gameObject);
        e2.BeginClose();
        e2.Tick(0.5f, true, false);
        Check("진입문: 끼임이 유예(1초) 안이면 아직 취소 안 함", r2.c.Current == IsolationRescueController.State.Preparing);
        e2.Tick(0.6f, true, false);
        Check("진입문: 끼임이 유예를 넘으면 격리 취소·대기 복귀·문 다시 열림",
            r2.c.Current == IsolationRescueController.State.Idle &&
            e2.Current == IsolationEntryDoor.Phase.Open && PadPressed(door2));

        // 끼임이 풀리면 누적 초기화
        Rig r3 = NewRig();
        Player p3 = NewPlayer("P3", Vector3.zero);
        IsolationEntryDoor e3 = NewEntryDoor(r3, out _);
        r3.c.RequestCapture(p3.mover.gameObject);
        e3.BeginClose();
        e3.Tick(0.9f, true, false);
        e3.Tick(0.1f, false, false);
        e3.Tick(0.9f, true, false);
        Check("진입문: 끼임이 끊기면 누적이 초기화(스치듯 지나가도 취소 안 됨)", r3.c.Current == IsolationRescueController.State.Preparing);

        // 닫힘 위치지만 끼어 있으면 안전 아님
        Rig r4 = NewRig();
        Player p4 = NewPlayer("P4", Vector3.zero);
        IsolationEntryDoor e4 = NewEntryDoor(r4, out _);
        r4.c.RequestCapture(p4.mover.gameObject);
        e4.BeginClose();
        e4.Tick(0.1f, true, true);
        Check("진입문: 닫힘 위치여도 끼어 있으면 안전 보고 안 함", e4.Current == IsolationEntryDoor.Phase.Closing);

        // 닫히지 못하는 문 → 시간 초과로 취소
        Rig r5 = NewRig();
        Player p5 = NewPlayer("P5", Vector3.zero);
        IsolationEntryDoor e5 = NewEntryDoor(r5, out _);
        r5.c.RequestCapture(p5.mover.gameObject);
        e5.BeginClose();
        e5.Tick(6.1f, false, false);
        Check("진입문: 시간 안에 못 닫히면 막힘으로 취소", r5.c.Current == IsolationRescueController.State.Idle);

        // 외부에서 취소됨 → 결과 보고 없이 열림 상태로
        Rig r6 = NewRig();
        Player p6 = NewPlayer("P6", Vector3.zero);
        IsolationEntryDoor e6 = NewEntryDoor(r6, out _);
        r6.c.RequestCapture(p6.mover.gameObject);
        e6.BeginClose();
        r6.c.CancelCapture();
        e6.Tick(0.1f, false, true);
        Check("진입문: 컨트롤러가 이미 준비를 벗어났으면 보고하지 않고 열림으로 정리",
            e6.Current == IsolationEntryDoor.Phase.Open && r6.c.Current == IsolationRescueController.State.Idle);

        e6.Tick(0.1f, true, false);
        Check("진입문: 열림 상태의 Tick은 아무 일도 안 함", e6.Current == IsolationEntryDoor.Phase.Open);

        // doorPhysics 신규 읽기 전용 프로퍼티 기본값
        Check("doorPhysics: IsBlocked/IsAtClosedPosition 기본값 false(리지드바디 없음에도 예외 없음)",
            !door.IsBlocked && !door.IsAtClosedPosition);
    }

    // ── 버튼 ─────────────────────────────────────────────────────────────

    private static void TestButtons()
    {
        Rig r = NewRig();
        IsolationRescueHud hud = Track(new GameObject("Hud")).AddComponent<IsolationRescueHud>();
        hud.controller = r.c;
        IsolationReadyButton ri = Track(new GameObject("ReadyIn")).AddComponent<IsolationReadyButton>();
        ri.controller = r.c; ri.inside = true; ri.hud = hud;
        IsolationReadyButton ro = Track(new GameObject("ReadyOut")).AddComponent<IsolationReadyButton>();
        ro.controller = r.c; ro.inside = false; ro.hud = hud;
        Player p = NewPlayer("In", Vector3.zero);

        ri.Press();
        Check("준비 버튼: 격리 전에 눌러도 상태 불변 + 사유 표시",
            r.c.Current == IsolationRescueController.State.Idle && hud.CurrentMessage.Contains("격리가 시작되지"));

        ToPreparing(r, p);
        ri.Press();
        Check("준비 버튼: 안쪽만 눌러서는 시작 안 함", r.c.Current == IsolationRescueController.State.Preparing);
        ro.Press();
        Check("준비 버튼: 양쪽이 모두 누르면 1단계 시작", r.c.Current == IsolationRescueController.State.InProgress);

        Check("준비 버튼: 컨트롤러 없어도 예외 없음", NoThrow(() =>
        {
            IsolationReadyButton lone = Track(new GameObject("LoneReady")).AddComponent<IsolationReadyButton>();
            lone.Press();
        }));

        // 해제 버튼
        Rig r2 = NewRig();
        IsolationRescueHud hud2 = Track(new GameObject("Hud2")).AddComponent<IsolationRescueHud>();
        hud2.controller = r2.c;
        ReleaseButton rel = Track(new GameObject("Release")).AddComponent<ReleaseButton>();
        rel.controller = r2.c; rel.hud = hud2;
        Player p2 = NewPlayer("In2", Vector3.zero);

        ToInProgress(r2, p2);
        rel.Press();
        Check("해제 버튼: 단계를 끝내기 전에는 거부 + 사유", r2.c.Current == IsolationRescueController.State.InProgress &&
                                                  hud2.CurrentMessage.Contains("모든 단계"));
        FinishStages(r2);
        rel.Press();
        Check("해제 버튼: 전원 OFF면 거부·진행 보존 + 사유(전원이 꺼져 있다)",
            r2.c.Current == IsolationRescueController.State.AwaitFinalRelease && hud2.CurrentMessage.Contains("전원"));
        r2.c.SetPowerHold(true);
        rel.Press();
        Check("해제 버튼: 전원 ON이면 성공", r2.c.Current == IsolationRescueController.State.Success);

        // C2-06: 입력 시각 == 마감 시각 → 시간 초과만
        Rig r3 = NewRig();
        IsolationRescueHud hud3 = Track(new GameObject("Hud3")).AddComponent<IsolationRescueHud>();
        hud3.controller = r3.c;
        ReleaseButton rel3 = Track(new GameObject("Release3")).AddComponent<ReleaseButton>();
        rel3.controller = r3.c; rel3.hud = hud3;
        Player p3 = NewPlayer("In3", Vector3.zero);
        ToInProgress(r3, p3);
        FinishStages(r3);
        r3.c.SetPowerHold(true);
        r3.now = r3.t.Deadline; // 정확히 마감 시각
        rel3.Press();
        Check("해제 버튼 C2-06: 마감과 같은 시각에 누르면 시간 초과만(성공 아님)",
            r3.c.Current == IsolationRescueController.State.TimedOut && hud3.CurrentMessage.Contains("제한시간"));

        Check("해제 버튼: 컨트롤러 없어도 예외 없음", NoThrow(() =>
        {
            ReleaseButton lone = Track(new GameObject("LoneRelease")).AddComponent<ReleaseButton>();
            lone.Press();
        }));
    }

    // ── HUD ──────────────────────────────────────────────────────────────

    private static void TestHud()
    {
        Rig r = NewRig();
        IsolationRescueHud hud = Track(new GameObject("Hud")).AddComponent<IsolationRescueHud>();
        hud.controller = r.c;
        Player p = NewPlayer("In", Vector3.zero);

        List<string> idle = hud.BuildLines(false, true);
        Check("HUD: 대기 상태는 제목 한 줄", idle.Count == 1 && idle[0].Contains("대기"));

        ToInProgress(r, p);
        List<string> outside = hud.BuildLines(false, true);
        List<string> insideL = hud.BuildLines(true, true);
        List<string> unknown = hud.BuildLines(false, false);
        Check("HUD: 바깥 참가자에게는 단계/입력 진행도가 보임", JoinHas(outside, "단계 1/3") && JoinHas(outside, "입력 0/3"));
        Check("HUD: 안쪽 참가자에게는 진행도를 숨김(말로 확인해야 하는 퍼즐)", !JoinHas(insideL, "단계 ") && !JoinHas(insideL, "입력 "));
        Check("HUD: 보는 사람을 모르면 진행도를 숨김", !JoinHas(unknown, "단계 "));
        Check("HUD: 진행 중 남은 시간이 모두에게 보임", JoinHas(outside, "남은 시간 60초") && JoinHas(insideL, "남은 시간 60초"));

        r.now = 10.2f;
        Check("HUD: 남은 시간은 올림(49.8 → 50초)", JoinHas(hud.BuildLines(false, true), "남은 시간 50초"));

        FinishStages(r);
        Check("HUD: 최종 해제 대기 + 전원 OFF 안내", JoinHas(hud.BuildLines(false, true), "전원 OFF"));
        r.c.SetPowerHold(true);
        Check("HUD: 전원 ON 안내", JoinHas(hud.BuildLines(false, true), "전원 ON"));

        hud.Say("테스트 메시지");
        Check("HUD: 메시지가 줄에 포함", JoinHas(hud.BuildLines(false, true), "테스트 메시지"));

        Check("HUD: 컨트롤러 없으면 빈 목록", NoThrow(() =>
        {
            IsolationRescueHud lone = Track(new GameObject("LoneHud")).AddComponent<IsolationRescueHud>();
            if (lone.BuildLines(false, true).Count != 0) throw new System.Exception("not empty");
        }));
        Check("HUD: 모든 상태에 라벨이 있음", AllStatesLabeled());
    }

    private static bool JoinHas(List<string> lines, string needle) => string.Join("\n", lines).Contains(needle);

    private static bool AllStatesLabeled()
    {
        foreach (IsolationRescueController.State s in System.Enum.GetValues(typeof(IsolationRescueController.State)))
        {
            string label = IsolationRescueHud.StateLabel(s);
            if (string.IsNullOrEmpty(label) || label == s.ToString()) return false;
        }
        return true;
    }

    // ── 역할별 추락 복귀 ─────────────────────────────────────────────────

    private static void TestRoleReturn()
    {
        Rig r = NewRig();
        Player inside = NewPlayer("In", Vector3.zero);
        Player outside = NewPlayer("Out", Vector3.zero);
        SectionSafePoint inSafe = Track(new GameObject("InSafe")).AddComponent<SectionSafePoint>();
        SectionSafePoint outSafe = Track(new GameObject("OutSafe")).AddComponent<SectionSafePoint>();
        IsolationRoleReturn rr = Track(new GameObject("RoleReturn")).AddComponent<IsolationRoleReturn>();
        rr.controller = r.c;
        rr.insideSafePoint = inSafe;
        rr.outsideSafePoint = outSafe;
        GameObject inRoot = inside.mover.gameObject;
        GameObject outRoot = outside.mover.gameObject;

        Check("복귀: 격리 전에는 공용 복귀(목적지 없음)", rr.PickDestination(inRoot) == null && rr.PickDestination(outRoot) == null);

        ToPreparing(r, inside);
        Check("복귀: 준비 중에도 역할대로 — 안쪽은 C2_IN_SAFE, 바깥은 C2_OUT_SAFE",
            rr.PickDestination(inRoot) == inSafe && rr.PickDestination(outRoot) == outSafe);

        r.c.SetReady(true);
        r.c.SetReady(false);
        Check("복귀: 진행 중 역할대로", rr.PickDestination(inRoot) == inSafe && rr.PickDestination(outRoot) == outSafe);

        // 복귀는 단계·입력·시간을 건드리지 않는다(§2.8): 선택만 하고 상태는 그대로.
        r.c.SubmitLever(r.c.CurrentOrder[0]);
        int stage = r.c.StageIndex, input = r.c.InputCount;
        float deadline = r.t.Deadline;
        rr.PickDestination(inRoot);
        Check("복귀: 목적지 선택은 단계·입력·마감을 바꾸지 않음",
            r.c.StageIndex == stage && r.c.InputCount == input && Mathf.Approximately(r.t.Deadline, deadline));

        FinishStages(r);
        Check("복귀: 최종 해제 대기에서도 역할대로", rr.PickDestination(inRoot) == inSafe);
        r.c.SetPowerHold(true);
        r.c.TryRelease();
        Check("복귀: 성공(격리 해제) 뒤에는 공용 복귀", rr.PickDestination(inRoot) == null);

        Rig r2 = NewRig();
        Player pi = NewPlayer("In2", Vector3.zero);
        IsolationRoleReturn rr2 = Track(new GameObject("RoleReturn2")).AddComponent<IsolationRoleReturn>();
        rr2.controller = r2.c;
        rr2.insideSafePoint = inSafe;
        rr2.outsideSafePoint = outSafe;
        ToInProgress(r2, pi);
        r2.now = 100f;
        r2.c.CheckDeadline();
        Check("복귀: 시간 초과 뒤에는 공용 복귀", rr2.PickDestination(pi.mover.gameObject) == null);

        rr2.insideSafePoint = null;
        Rig r3 = NewRig();
        Player p3 = NewPlayer("In3", Vector3.zero);
        rr2.controller = r3.c;
        ToInProgress(r3, p3);
        Check("복귀: 안전점이 비어 있으면 null(공용 복귀로 폴백)", rr2.PickDestination(p3.mover.gameObject) == null);

        Check("복귀: RespawnController 없으면 false(예외 없음)", !rr.Return(inRoot));
        Check("복귀: 컨트롤러 없으면 목적지 null", Track(new GameObject("NoCtl")).AddComponent<IsolationRoleReturn>()
            .PickDestination(inRoot) == null);
    }

    // ── 처음부터 끝까지 ──────────────────────────────────────────────────

    private static void TestEndToEnd()
    {
        Rig r = NewRig();
        Player inside = NewPlayer("In", Vector3.zero);
        Player outside = NewPlayer("Out", new Vector3(0f, 0f, -5f));

        IsolationCaptureTrigger capture = NewCapture(r);
        IsolationEntryDoor entry = NewEntryDoor(r, out _);
        IsolationReadyButton rIn = Track(new GameObject("RIn")).AddComponent<IsolationReadyButton>();
        rIn.controller = r.c; rIn.inside = true;
        IsolationReadyButton rOut = Track(new GameObject("ROut")).AddComponent<IsolationReadyButton>();
        rOut.controller = r.c; rOut.inside = false;
        ReleaseButton release = Track(new GameObject("Rel")).AddComponent<ReleaseButton>();
        release.controller = r.c;
        PowerHoldSwitch power = Track(new GameObject("Pow")).AddComponent<PowerHoldSwitch>();
        power.controller = r.c;
        SequenceLever[] lv = { NewLever(r, 1), NewLever(r, 2), NewLever(r, 3) };

        // 1) 안쪽 플레이어가 진입점을 통과 → 확정 → 문이 안전하게 닫힘
        PassTo(capture, inside, 3f);
        capture.ResolvePending();
        entry.BeginClose();
        entry.Tick(0.2f, false, true);
        // 2) 양쪽 준비
        rIn.Press();
        rOut.Press();
        Check("종단: 통과→확정→문→준비 확인으로 1단계 시작", r.c.Current == IsolationRescueController.State.InProgress);

        // 3) 단계마다 레버를 정답 순서대로 당기고, 다음 단계 전에 모두 놓는다
        r.now = 10f;
        for (int s = 0; s < 3; s++)
        {
            int[] order = r.c.CurrentOrder.ToArray2();
            foreach (int number in order) lv[number - 1].Evaluate(1f);
            foreach (SequenceLever l in lv) l.Evaluate(0f);
            if (s < 2)
                Check($"종단: {s + 1}단계 완료 → {s + 2}단계", r.c.StageIndex == s + 1 && r.c.Current == IsolationRescueController.State.InProgress);
        }
        Check("종단: 3단계까지 끝내면 최종 해제 대기", r.c.Current == IsolationRescueController.State.AwaitFinalRelease);

        // 4) 바깥 전원 ON + 안쪽 해제, 시간 내
        power.HandleEnter(outside.mesh);
        r.now = 30f;
        release.Press();
        Check("종단: 전원 ON + 해제 버튼 + 마감 전 → 성공(타이머 정지)",
            r.c.Current == IsolationRescueController.State.Success && !r.t.IsRunning);

        // 같은 흐름에서 시간 초과 분기
        Rig q = NewRig();
        Player qi = NewPlayer("QIn", Vector3.zero);
        ToInProgress(q, qi);
        q.now = 61f;
        q.c.CheckDeadline();
        Check("종단: 마감을 넘기면 시간 초과(패널티 없는 분기)", q.c.Current == IsolationRescueController.State.TimedOut);
    }

    // ── 시험 배치 메뉴 ───────────────────────────────────────────────────

    private static void TestBuilder()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        IsolationRescueMenuItem.Result b = IsolationRescueMenuItem.Build(Vector3.zero);
        created.Add(b.root);

        Check("배치: 콘텐츠 검증 통과(3단계)", b.controller.ValidateContent(out string err) && b.controller.StageCount == 3);
        if (err != null) Debug.Log($"[SceneSelfTest] 콘텐츠 오류: {err}");
        Check("배치: 타이머가 컨트롤러와 연결되고 양수", b.controller.Timer == b.timer && b.timer.duration > 0f);
        Check("배치: 문 개수(진입 1·사이 2·비상 3·해제 1)",
            b.entryDoor.door != null && b.passDoors.Length == 2 && b.emergencyDoors.Length == 3 && b.releaseDoor != null);

        bool leversOk = b.levers.Length == 3;
        for (int i = 0; leversOk && i < 3; i++)
            leversOk = b.levers[i].leverNumber == i + 1 && b.levers[i].lever != null && b.levers[i].controller == b.controller;
        Check("배치: 순서 레버 3개가 번호 1~3으로 컨트롤러·LeverHead와 연결", leversOk);
        Check("배치: 순서 레버는 빠른 되돌림(returnDelay 0.5 / returnSpeed 3)",
            b.levers[0].lever.returnDelay == 0.5f && b.levers[0].lever.returnSpeed == 3f);

        bool boardsOk = b.clueBoards.Length == 3;
        for (int i = 0; boardsOk && i < 3; i++)
            boardsOk = b.clueBoards[i].stageIndex == i && b.clueBoards[i].controller == b.controller;
        Check("배치: 순서판 3개가 단계 0~2에 연결", boardsOk);

        Check("배치: 대응표·전원·버튼이 컨트롤러에 연결",
            b.map.controller == b.controller && b.power.controller == b.controller &&
            b.readyIn.controller == b.controller && b.readyOut.controller == b.controller &&
            b.release.controller == b.controller && b.hud.controller == b.controller);
        Check("배치: 안쪽/바깥 준비 버튼 구분", b.readyIn.inside && !b.readyOut.inside);
        Check("배치: 진입 트리거는 트리거 콜라이더 + 컨트롤러 연결",
            b.capture.controller == b.controller && b.capture.GetComponent<Collider>() != null &&
            b.capture.GetComponent<Collider>().isTrigger);
        Check("배치: 추락 복귀가 두 안전점·컨트롤러에 연결",
            b.roleReturn.controller == b.controller && b.roleReturn.insideSafePoint == b.insideSafe &&
            b.roleReturn.outsideSafePoint == b.outsideSafe &&
            b.insideSafe.sectionId == "C2_IN_SAFE" && b.outsideSafe.sectionId == "C2_OUT_SAFE");
        Check("배치: 컨트롤러·진입문 연결", b.entryDoor.controller == b.controller);

        // 이벤트 배선 구조 — EditMode의 UnityEvent는 호출되지 않으므로 구성과 대상을 직접 확인.
        IsolationRescueController c = b.controller;
        Check("배선: 단계1·2 완료 → 사이 문1·2 개방, 단계3은 없음(최종 해제 대기로)",
            Targets(c.stages[0].onCompleted, "SetPadPressed", b.passDoors[0]) &&
            Targets(c.stages[1].onCompleted, "SetPadPressed", b.passDoors[1]) &&
            c.stages[2].onCompleted.GetPersistentEventCount() == 0);
        Check("배선: 대상 확정 → 진입문 닫기 시작", Targets(c.onCaptureConfirmed, "BeginClose", b.entryDoor) &&
                                          c.onCaptureConfirmed.GetPersistentEventCount() == 1);
        Check("배선: 격리 취소 → 진입문 열기", Targets(c.onCaptureCancelled, "Open", b.entryDoor) &&
                                       c.onCaptureCancelled.GetPersistentEventCount() == 1);
        Check("배선: 시간 초과 → 비상문 3개 개방", c.onTimedOut.GetPersistentEventCount() == 3 &&
                                         Targets(c.onTimedOut, "SetPadPressed", b.emergencyDoors[0]) &&
                                         Targets(c.onTimedOut, "SetPadPressed", b.emergencyDoors[1]) &&
                                         Targets(c.onTimedOut, "SetPadPressed", b.emergencyDoors[2]));
        Check("배선: 중단 → 진입문 열기 + 비상문 3개 개방", c.onAborted.GetPersistentEventCount() == 4 &&
                                                Targets(c.onAborted, "Open", b.entryDoor));
        Check("배선: 성공 → 해제문 개방(CH4 숏컷)", c.onSuccess.GetPersistentEventCount() == 1 &&
                                         Targets(c.onSuccess, "SetPadPressed", b.releaseDoor));
        Check("배선: 전체 재시작 → 진입문 열기 + 문 6개 닫기", c.onRestarted.GetPersistentEventCount() == 7 &&
                                                  Targets(c.onRestarted, "Open", b.entryDoor) &&
                                                  Targets(c.onRestarted, "SetPadPressed", b.releaseDoor));

        // 열기 배선은 true, 닫기 배선은 false 인자인지(인자를 거꾸로 꽂으면 문이 반대로 움직인다).
        Check("배선: 열기 배선의 인자는 true, 닫기 배선의 인자는 false",
            BoolArgOf(c.stages[0].onCompleted, 0) == true && BoolArgOf(c.onSuccess, 0) == true &&
            BoolArgOf(c.onRestarted, 1) == false);

        // 깨진 스크립트 참조 없음
        int missing = 0;
        foreach (Component comp in b.root.GetComponentsInChildren<Component>(true))
            if (comp == null) missing++;
        Check("배치: 계층에 깨진(Missing) 컴포넌트 없음", missing == 0);
    }

    private static bool Targets(UnityEventBase evt, string method, Object target)
    {
        for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            if (evt.GetPersistentTarget(i) == target && evt.GetPersistentMethodName(i) == method) return true;
        return false;
    }

    // 지속 리스너의 bool 인자(직렬화된 PersistentCall을 SerializedObject로 읽는다).
    private static bool? BoolArgOf(UnityEventBase evt, int index)
    {
        // UnityEventBase는 직렬화 필드 m_PersistentCalls를 가진다. 인자 값은 리플렉션으로 꺼낸다.
        FieldInfo calls = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic);
        if (calls == null) return null;
        object group = calls.GetValue(evt);
        FieldInfo list = group.GetType().GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
        if (list == null) return null;
        var items = (System.Collections.IList)list.GetValue(group);
        if (items == null || index >= items.Count) return null;
        object call = items[index];
        FieldInfo args = call.GetType().GetField("m_Arguments", BindingFlags.Instance | BindingFlags.NonPublic);
        if (args == null) return null;
        object arguments = args.GetValue(call);
        FieldInfo boolArg = arguments.GetType().GetField("m_BoolArgument", BindingFlags.Instance | BindingFlags.NonPublic);
        return boolArg != null ? (bool?)boolArg.GetValue(arguments) : null;
    }

    // ── 공통 ─────────────────────────────────────────────────────────────

    private static bool NoThrow(System.Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (System.Exception e)
        {
            Debug.Log($"[SceneSelfTest] 예외: {e.Message}");
            return false;
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition) passed++;
        else failed++;
        Debug.Log($"[SceneSelfTest] {(condition ? "PASS" : "FAIL")}  {name}");
    }
}
