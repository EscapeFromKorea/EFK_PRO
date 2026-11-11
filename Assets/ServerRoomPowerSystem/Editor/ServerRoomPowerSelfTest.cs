using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// PowerMaintenanceController의 순수 로직 자가검증. 씬·물리·입력 없이 docs/PRD/ServerRoomPower.md §6
/// 완료 조건(E-01~E-07)과 §4 확정 규칙(히스테리시스·배정당 1회 회복·정지·전체 재시작 등)을 확인한다.
///
/// [왜 Test Framework가 아닌가] 저장소에 패키지·asmdef가 없어 공용 Packages/manifest.json을 바꾸지 않으려고
/// RoleClueTerminalSelfTest와 같은 Editor 메뉴 + 배치모드 진입점 방식을 쓴다.
///
/// [시간과 신호는 직접 흉내] Update 대신 Tick(dt)를, 배선 완료는 WiringPanel.OnCircuitCompleted를 직접
/// 발신한다. EditMode에서는 OnEnable이 불리지 않아 Bind()도 직접 부른다.
///
/// 실행: 메뉴 Tools &gt; Server Room Power &gt; Run Logic Self-Test, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod ServerRoomPowerSelfTest.RunFromCommandLine`
/// (실패가 하나라도 있으면 종료 코드 1).
///
/// [다루지 않는 것] 화면 그리기(OnGUI), 실제 배선 조작, 회로 콘텐츠 밸런스(수치 미확정) — 씬 플레이테스트와
/// 팀 결정이 필요한 항목이다.
/// </summary>
public static class ServerRoomPowerSelfTest
{
    private static int passed;
    private static int failed;

    [MenuItem("Tools/Server Room Power/Run Logic Self-Test")]
    public static void RunFromMenu()
    {
        Run();
    }

    // -executeMethod ServerRoomPowerSelfTest.RunFromCommandLine
    public static void RunFromCommandLine()
    {
        bool ok = Run();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private class Rig
    {
        public GameObject root;
        public PlayerMover a, b, c;
        public RoleAssignmentManager manager;
        public RoleSlot book, computer, power;
        public QuizTerminal quiz;
        public WiringPanel panel;
        public PowerMaintenanceController ctrl;
        public WiringCircuitData[] circuits;
        public int lockEvents, completed, started;
        public bool lastLock;
    }

    private static Rig NewRig(float d = 1f, float g = 30f, float h = 3f)
    {
        var r = new Rig();
        r.root = new GameObject("ServerRoomPowerSelfTest");
        r.a = new GameObject("A").AddComponent<PlayerMover>();
        r.b = new GameObject("B").AddComponent<PlayerMover>();
        r.c = new GameObject("C").AddComponent<PlayerMover>();

        r.manager = r.root.AddComponent<RoleAssignmentManager>();
        r.manager.ControlledPlayerResolver = () => r.a;
        r.manager.showStatusOverlay = false;
        r.book = NewSlot(r, "Book", false);
        r.computer = NewSlot(r, "Computer", false);
        r.power = NewSlot(r, "Power", true);

        r.quiz = r.root.AddComponent<QuizTerminal>();
        r.quiz.slot = r.computer;
        r.quiz.questions = new[] { NewQuestion(1), NewQuestion(0), NewQuestion(2) };
        r.quiz.Bind();

        r.panel = new GameObject("Panel").AddComponent<WiringPanel>();
        r.circuits = new[] { NewCircuit("POWER_1"), NewCircuit("POWER_2"), NewCircuit("POWER_3") };

        r.ctrl = r.root.AddComponent<PowerMaintenanceController>();
        r.ctrl.manager = r.manager;
        r.ctrl.quiz = r.quiz;
        r.ctrl.wiringPanel = r.panel;
        r.ctrl.circuits = r.circuits;
        r.ctrl.bookSlot = r.book;
        r.ctrl.computerSlot = r.computer;
        r.ctrl.powerSlot = r.power;
        r.ctrl.maxPower = 100f;
        r.ctrl.startPower = 100f;
        r.ctrl.lockBelow = 20f;
        r.ctrl.unlockAt = 50f;
        r.ctrl.drainPerSecond = d;
        r.ctrl.gainPerCircuit = g;
        r.ctrl.restSeconds = h;
        r.ctrl.onSubmitLockChanged.AddListener(v => { r.lockEvents++; r.lastLock = v; });
        r.ctrl.onCompleted.AddListener(() => r.completed++);
        r.ctrl.onStarted.AddListener(() => r.started++);
        r.ctrl.ResetAll();
        r.ctrl.Bind();
        return r;
    }

    private static RoleSlot NewSlot(Rig r, string roleId, bool shared)
    {
        RoleSlot s = new GameObject("Slot_" + roleId).AddComponent<RoleSlot>();
        s.roleId = roleId;
        s.manager = r.manager;
        s.allowMultipleUsers = shared;
        r.manager.RegisterSlot(s);
        return s;
    }

    private static QuizTerminal.Question NewQuestion(int correct)
    {
        return new QuizTerminal.Question { prompt = "q", choices = new[] { "c0", "c1", "c2" }, correctIndex = correct };
    }

    private static WiringCircuitData NewCircuit(string id)
    {
        WiringCircuitData c = ScriptableObject.CreateInstance<WiringCircuitData>();
        c.circuitId = id;
        c.outputIds = new[] { "A", "B" };
        c.inputIds = new[] { "1", "2" };
        c.pairs = new[]
        {
            new WiringCircuitData.PortPair { outputId = "A", inputId = "1" },
            new WiringCircuitData.PortPair { outputId = "B", inputId = "2" }
        };
        return c;
    }

    /// <summary>책·컴퓨터·전력 세 역할을 서로 다른 참가자가 점유하게 한다.</summary>
    private static void Occupy(Rig r, bool includePower = true)
    {
        r.book.HandleInteract(r.a);
        r.computer.HandleInteract(r.b);
        if (includePower) r.power.HandleInteract(r.c);
    }

    private static void Complete(Rig r)
    {
        r.panel.OnCircuitCompleted.Invoke(r.panel, 1, r.ctrl.AssignmentNumber, r.ctrl.AssignmentNumber == 0 ? "-" : r.panel.CurrentCircuit.circuitId);
    }

    private static bool Near(float x, float y) => Mathf.Abs(x - y) < 0.001f;

    private static void Dispose(Rig r)
    {
        r.ctrl.Unbind();
        r.quiz.Unbind();
        foreach (WiringCircuitData c in r.circuits) if (c != null) Object.DestroyImmediate(c);
        Object.DestroyImmediate(r.panel.gameObject);
        Object.DestroyImmediate(r.book.gameObject);
        Object.DestroyImmediate(r.computer.gameObject);
        Object.DestroyImmediate(r.power.gameObject);
        foreach (PlayerMover p in new[] { r.a, r.b, r.c }) if (p != null) Object.DestroyImmediate(p.gameObject);
        Object.DestroyImmediate(r.root);
    }

    private static bool Run()
    {
        passed = 0;
        failed = 0;

        // ── 설정 검증 ──────────────────────────────────────────────
        {
            Rig r = NewRig();
            Check("설정: 기본 임시값은 유효", r.ctrl.ValidateSettings(out _));
            r.ctrl.lockBelow = 0f;
            Check("설정: K=0 거부(전력 0에서 항상 잠김 보장)", !r.ctrl.ValidateSettings(out _));
            r.ctrl.lockBelow = 60f;
            Check("설정: K ≥ U 거부", !r.ctrl.ValidateSettings(out _));
            r.ctrl.lockBelow = 20f;
            r.ctrl.startPower = 30f;
            Check("설정: E0 < U 거부", !r.ctrl.ValidateSettings(out _));
            r.ctrl.startPower = 100f;
            r.ctrl.unlockAt = 150f;
            Check("설정: U > M 거부", !r.ctrl.ValidateSettings(out _));
            r.ctrl.unlockAt = 50f;
            r.ctrl.gainPerCircuit = 0f;
            Check("설정: G ≤ 0 거부", !r.ctrl.ValidateSettings(out _));
            Occupy(r);
            r.ctrl.lockBelow = 60f;
            Check("시작: 잘못된 수치로는 시작 거부", !r.ctrl.StartExperiment() && r.ctrl.Current == PowerMaintenanceController.State.Ready);
            Dispose(r);
        }

        // ── E-01 준비 상태 / 시작 요건 ────────────────────────────────
        {
            Rig r = NewRig();
            r.ctrl.Tick(30f);
            Check("E-01: 준비 상태에서 시간이 흘러도 전력 불변", Near(r.ctrl.Power, 100f));
            Check("시작: 역할이 비어 있으면 거부", !r.ctrl.StartExperiment());
            r.book.HandleInteract(r.a);
            r.computer.HandleInteract(r.b);
            Check("시작: 전력 담당만 없어도 거부", !r.ctrl.StartExperiment());
            r.power.HandleInteract(r.c);
            Check("시작: 역할 3개 점유 후 시작 성공", r.ctrl.StartExperiment() && r.ctrl.Current == PowerMaintenanceController.State.Running);
            Check("시작: 첫 회로 POWER_1이 배정번호 1로 배정", r.ctrl.AssignmentNumber == 1 && r.panel.CurrentCircuit.circuitId == "POWER_1");
            Check("시작: 시작 이벤트 1회", r.started == 1);
            Check("시작: 이미 시작했으면 다시 시작 불가", !r.ctrl.StartExperiment());
            Dispose(r);
        }

        // ── 감소 / E-02 잠금 / 히스테리시스 ───────────────────────────
        {
            Rig r = NewRig();
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(10f);
            Check("유지: E = E0 - d·Δt", Near(r.ctrl.Power, 90f));
            r.ctrl.Tick(70f);
            Check("경계: E == K에서는 새로 잠그지 않는다", Near(r.ctrl.Power, 20f) && !r.ctrl.SubmitLocked && r.quiz.SubmitGateOpen);
            r.ctrl.Tick(0.5f);
            Check("E-02: E < K이면 제출 잠김(게이트 닫힘·이벤트 1회)", r.ctrl.SubmitLocked && !r.quiz.SubmitGateOpen && r.lockEvents == 1 && r.lastLock);

            r.quiz.Select(1, r.b);
            Check("E-02: 잠긴 동안 제출 거부, 선택은 유지, 오답 처리 없음",
                r.quiz.Submit(r.b) == QuizTerminal.SubmitResult.Rejected && r.quiz.SelectedIndex == 1 && r.quiz.CurrentIndex == 0);
            Check("E-02: 책·배선 점유는 그대로(잠금은 제출만)", r.book.HasUser(r.a) && r.power.HasUser(r.c));

            // 잠금 후 K와 U 사이는 유지, U 이상에서 해제(19.5 + 30.5 = 50 == U).
            r.ctrl.gainPerCircuit = 30f;
            Complete(r);
            Check("히스테리시스: 회복 후에도 U 미만이면 계속 잠김", Near(r.ctrl.Power, 49.5f) && r.ctrl.SubmitLocked && r.lockEvents == 1);
            Dispose(r);
        }

        {
            Rig r = NewRig(d: 1f, g: 30.5f, h: 0f);
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(80.5f);
            Check("경계: E=19.5에서 잠김", Near(r.ctrl.Power, 19.5f) && r.ctrl.SubmitLocked);
            Complete(r);
            Check("경계: E == U에서 해제", Near(r.ctrl.Power, 50f) && !r.ctrl.SubmitLocked && r.quiz.SubmitGateOpen && r.lockEvents == 2 && !r.lastLock);
            Dispose(r);
        }

        // ── E-03 0에서 복구 / 회로 순환 / E-04 멱등 ────────────────────
        {
            Rig r = NewRig(d: 1f, g: 40f, h: 0f);
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(500f);
            Check("유지: 전력은 0 아래로 내려가지 않는다", Near(r.ctrl.Power, 0f) && r.ctrl.SubmitLocked);
            Complete(r);
            Check("E-03: 0에서도 배선 완료로 회복(40), 아직 U 미만이라 잠김", Near(r.ctrl.Power, 40f) && r.ctrl.SubmitLocked);
            Check("회로 순환: 휴식 0이면 즉시 POWER_2(배정번호 2)", r.ctrl.AssignmentNumber == 2 && r.panel.CurrentCircuit.circuitId == "POWER_2");
            Complete(r);
            Check("E-03: 두 번째 회로로 U(50) 이상 → 제출 재개", Near(r.ctrl.Power, 80f) && !r.ctrl.SubmitLocked && r.quiz.SubmitGateOpen);
            r.quiz.Select(1, r.b);
            Check("E-03: 같은 미완료 문제(Q1)를 정답으로 제출 가능", r.quiz.Submit(r.b) == QuizTerminal.SubmitResult.Correct && r.quiz.CurrentIndex == 1);
            Check("회로 순환: POWER_3 배정(번호 3)", r.ctrl.AssignmentNumber == 3 && r.panel.CurrentCircuit.circuitId == "POWER_3");
            Complete(r);
            Check("회로 순환: 3 다음은 POWER_1로 돌아옴(번호 4)", r.ctrl.AssignmentNumber == 4 && r.panel.CurrentCircuit.circuitId == "POWER_1");
            Dispose(r);
        }

        {
            // 휴식 H > 0: 첫 완료 뒤에도 배정번호가 그대로라 "같은 배정 재전달"을 정확히 시험할 수 있다.
            Rig r = NewRig(d: 0f, g: 10f, h: 3f);
            r.ctrl.startPower = 50f;
            r.ctrl.unlockAt = 50f;
            r.ctrl.ResetAll();
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(1f);
            r.ctrl.maxPower = 100f;
            r.panel.OnCircuitCompleted.Invoke(r.panel, 1, 1, "POWER_1");
            float afterFirst = r.ctrl.Power;
            r.panel.OnCircuitCompleted.Invoke(r.panel, 2, 1, "POWER_1");
            Check("E-04: 같은 배정 신호 재전달은 회복 1번만", Near(afterFirst, 60f) && Near(r.ctrl.Power, 60f) && r.ctrl.AssignmentNumber == 1);
            r.ctrl.Tick(3f); // 휴식이 끝나 배정번호 2로 넘어간다.
            r.panel.OnCircuitCompleted.Invoke(r.panel, 3, 1, "POWER_1");
            Check("W-06: 새 배정 뒤 옛 배정(번호 1) 완료 신호는 무효", Near(r.ctrl.Power, 60f) && r.ctrl.AssignmentNumber == 2);
            r.panel.OnCircuitCompleted.Invoke(r.panel, 4, 2, "POWER_2");
            Check("W-06: 현재 배정(번호 2) 신호는 정상 회복", Near(r.ctrl.Power, 70f));
            Dispose(r);
        }

        // ── 최대치 / 휴식 ───────────────────────────────────────────
        {
            Rig r = NewRig(d: 0f, g: 30f, h: 3f);
            Occupy(r);
            r.ctrl.StartExperiment();
            Complete(r);
            Check("최대치: 초과 회복분은 버린다(M=100 유지)", Near(r.ctrl.Power, 100f));
            Check("휴식: 완료 직후에는 옛 회로가 유지되고 휴식 중", r.ctrl.IsResting && r.panel.CurrentCircuit.circuitId == "POWER_1");
            r.ctrl.Tick(2f);
            Check("휴식: H가 끝나기 전에는 다음 회로가 배정되지 않는다", r.ctrl.AssignmentNumber == 1 && r.ctrl.IsResting);
            r.ctrl.Tick(1.5f);
            Check("휴식: H 경과 후 POWER_2 배정", r.ctrl.AssignmentNumber == 2 && r.panel.CurrentCircuit.circuitId == "POWER_2" && !r.ctrl.IsResting);
            Dispose(r);
        }

        {
            Rig r = NewRig(d: 2f, g: 10f, h: 3f);
            Occupy(r);
            r.ctrl.StartExperiment();
            Complete(r);
            float atComplete = r.ctrl.Power;
            r.ctrl.Tick(2f);
            Check("휴식: 휴식 중에도 전력은 계속 감소", Near(r.ctrl.Power, atComplete - 4f));
            Dispose(r);
        }

        // ── E-05 프레임 독립 ──────────────────────────────────────────
        {
            Rig r = NewRig(d: 1.5f);
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(30f);
            float once = r.ctrl.Power;
            r.ctrl.ResetAll();
            r.ctrl.StartExperiment();
            for (int i = 0; i < 300; i++) r.ctrl.Tick(0.1f);
            Check("E-05: 같은 활성 시간을 다른 프레임 간격으로 진행해도 같은 전력", Near(once, r.ctrl.Power));
            Dispose(r);
        }

        // ── 정지 / 재개 ───────────────────────────────────────────────
        {
            Rig r = NewRig();
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(40f);
            r.ctrl.SetParticipantPaused(true);
            r.ctrl.Tick(20f);
            Check("정지: 참가자 이탈 중에는 전력이 줄지 않는다", Near(r.ctrl.Power, 60f));
            Complete(r);
            Check("정지: 정지 중 완료 신호는 즉시 반영하지 않고 보류", Near(r.ctrl.Power, 60f) && !r.ctrl.IsResting);
            r.ctrl.SetParticipantPaused(false);
            Check("재개: 보류했던 완료 신호를 이어서 시작 때 반영(E=90), 휴식 시작", Near(r.ctrl.Power, 90f) && r.ctrl.IsResting);
            r.ctrl.Tick(1f);
            Check("재개: 정지가 풀리면 다시 감소", Near(r.ctrl.Power, 89f));
            Dispose(r);
        }

        {
            Rig r = NewRig();
            Occupy(r);
            r.manager.NotifyParticipantLeft(r.a);
            Check("연결: 매니저 정지 신호가 컨트롤러에 전달됨(Subscribe)", r.ctrl.Paused);
            Check("시작: 이탈 정지 중에는 시작 거부", !r.ctrl.StartExperiment());
            Dispose(r);
        }

        // ── 9/25 원석 회신: 기본 수치 ──────────────────────────────────
        {
            GameObject go = new GameObject("PowerDefaults");
            PowerMaintenanceController c = go.AddComponent<PowerMaintenanceController>();
            Check("회신 수치: M=100, E0=100, K=20, U=40, d=1, G=30, H=3",
                Near(c.maxPower, 100f) && Near(c.startPower, 100f) && Near(c.lockBelow, 20f) && Near(c.unlockAt, 40f)
                && Near(c.drainPerSecond, 1f) && Near(c.gainPerCircuit, 30f) && Near(c.restSeconds, 3f));
            Check("회신 수치: 기본값이 PRD 파라미터 관계를 만족", c.ValidateSettings(out _));
            Object.DestroyImmediate(go);
        }

        // ── 9/25 원석 회신: 입구 배선 + 컴퓨터 사용자가 시작 ─────────────
        {
            Rig r = NewRig();
            WiringPanel entrance = new GameObject("Entrance").AddComponent<WiringPanel>();
            FieldInfo locked = typeof(WiringPanel).GetField("locked", BindingFlags.Instance | BindingFlags.NonPublic);
            r.ctrl.entranceWiring = entrance;
            Occupy(r);
            Check("시작: 입구 배선이 안 끝났으면 거부", !r.ctrl.StartExperiment() && r.ctrl.Current == PowerMaintenanceController.State.Ready);
            Check("시작: 입구 배선이 안 끝났으면 자동 유지에서도 거부", r.ctrl.SetAutoMaintain(true) && !r.ctrl.StartExperiment());
            r.ctrl.SetAutoMaintain(false);
            locked.SetValue(entrance, true); // 입구 배선 완료 = 패널 잠김.
            Check("시작: 책 담당은 시작 요청 불가", !r.ctrl.RequestStart(r.a));
            Check("시작: 전력 담당은 시작 요청 불가", !r.ctrl.RequestStart(r.c));
            Check("시작: 컴퓨터 사용자가 요청하면 시작", r.ctrl.RequestStart(r.b) && r.ctrl.Current == PowerMaintenanceController.State.Running);
            Object.DestroyImmediate(entrance.gameObject);
            Dispose(r);
        }

        {
            Rig r = NewRig();
            PlayerMover extra = new GameObject("Extra").AddComponent<PlayerMover>();
            Occupy(r);
            r.power.HandleInteract(extra);
            Check("공동 사용: 서버실 배선은 두 참가자가 함께 점유", r.power.HasUser(r.c) && r.power.HasUser(extra));
            Check("공동 사용: 전력 담당이 준비돼 있으면 시작(독점권 불필요)", r.ctrl.StartExperiment());
            Object.DestroyImmediate(extra.gameObject);
            Dispose(r);
        }

        // ── 9/25 원석 회신: 이탈 시 이탈 순간 그대로 보존 ─────────────────
        {
            Rig r = NewRig(d: 1f, g: 30f, h: 3f);
            Occupy(r);
            r.manager.SetStartOwner(r.a);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(50f);                              // E = 50
            Complete(r);                                   // E = 80, 휴식 3초 시작
            r.ctrl.Tick(1f);                               // E = 79, 휴식 2초 남음
            r.quiz.Select(1, r.b);
            r.quiz.Submit(r.b);                            // Q1 정답 → 완료 문항 1개
            r.quiz.Select(2, r.b);                         // Q2 미제출 답
            float powerAtLeave = r.ctrl.Power;
            float restAtLeave = r.ctrl.RestRemaining;

            r.manager.NotifyParticipantLeft(r.b);          // 컴퓨터 담당 이탈
            Check("이탈: 컨트롤러가 정지됨", r.ctrl.Paused);
            Check("이탈: 완료한 문제는 보존, 풀던 문제의 미제출 답만 초기화", r.quiz.CurrentIndex == 1 && r.quiz.SelectedIndex == -1);
            r.ctrl.Tick(100f);
            Check("이탈: 전력은 이탈 순간 값 그대로", Near(r.ctrl.Power, powerAtLeave));
            Check("이탈: 회로 사이 휴식 남은 시간도 그대로(진행 정지)", Near(r.ctrl.RestRemaining, restAtLeave) && r.ctrl.AssignmentNumber == 1);
            Check("이탈: 진행 중이던 회로와 배정번호 보존", r.panel.CurrentCircuit.circuitId == "POWER_1");

            r.manager.NotifyParticipantRejoined(r.b);
            r.ctrl.Tick(100f);
            Check("재접속: 이어서 시작 전에는 계속 정지", r.ctrl.Paused && Near(r.ctrl.Power, powerAtLeave));
            r.manager.RequestResume(r.a);
            Check("재개: 이어서 시작하면 정지 해제", !r.ctrl.Paused);
            r.ctrl.Tick(1f);
            Check("재개: 남은 휴식이 이어서 줄고 전력도 이어서 감소", Near(r.ctrl.RestRemaining, restAtLeave - 1f) && Near(r.ctrl.Power, powerAtLeave - 1f));
            r.ctrl.Tick(1.5f);
            Check("재개: 휴식이 끝나면 다음 회로(POWER_2) 배정", r.ctrl.AssignmentNumber == 2 && r.panel.CurrentCircuit.circuitId == "POWER_2");
            Dispose(r);
        }

        // ── 9/25 원석 회신: 시험용 회로 데이터(POWER_1~3) ─────────────────
        {
            ServerRoomPowerTestData.EnsureAssets();
            string[] ids = { "ENTRY", "POWER_1", "POWER_2", "POWER_3" };
            string[][] expected =
            {
                new[] { "A-2", "B-3", "C-1" },
                new[] { "A-1", "B-2", "C-3" },
                new[] { "A-3", "B-1", "C-2" },
                new[] { "A-2", "B-3", "C-1" }
            };
            for (int i = 0; i < ids.Length; i++)
            {
                WiringCircuitData data = AssetDatabase.LoadAssetAtPath<WiringCircuitData>(ServerRoomPowerTestData.PathOf(ids[i]));
                bool exists = data != null;
                bool valid = exists && data.Validate(out _);
                bool pairsMatch = valid && data.pairs.Length == expected[i].Length;
                if (pairsMatch)
                    foreach (string pair in expected[i])
                        pairsMatch &= data.IsCorrectPair(pair.Split('-')[0], pair.Split('-')[1]);
                Check($"시험 데이터: {ids[i]} 에셋 존재·검증 통과·정답 쌍이 명세와 일치", exists && valid && pairsMatch);
            }
        }

        // ── 패널 안 "실험 시작"(QuizTerminal 패널 액션) ─────────────────────
        {
            Rig r = NewRig();
            Check("패널 액션: 준비 상태에서는 \"실험 시작\" 라벨이 표시됨", r.quiz.actionLabel == "실험 시작");
            Occupy(r, includePower: false);
            Check("패널 액션: 컴퓨터 사용자가 누르면 요청이 전달됨", r.quiz.RequestAction(r.b));
            Check("패널 액션: 전력 담당이 없으면 시작 거부, 준비 상태 유지", r.ctrl.Current == PowerMaintenanceController.State.Ready);
            Check("패널 액션: 거부 사유를 패널 피드백에 띄움", r.quiz.LastFeedback != null && r.quiz.LastFeedback.Contains("전력"));
            r.power.HandleInteract(r.c);
            Check("패널 액션: 책 담당이 누르면 시작되지 않음(패널을 연 참가자만)", !r.quiz.RequestAction(r.a) && r.ctrl.Current == PowerMaintenanceController.State.Ready);
            r.quiz.RequestAction(r.b);
            Check("패널 액션: 요건이 갖춰지면 시작", r.ctrl.Current == PowerMaintenanceController.State.Running && r.quiz.LastFeedback == "실험을 시작합니다.");
            Check("패널 액션: 시작 뒤에는 라벨이 사라짐", string.IsNullOrEmpty(r.quiz.actionLabel));
            r.manager.ResetChapter();
            Check("패널 액션: 챕터 재시작 후 라벨이 다시 표시됨", r.quiz.actionLabel == "실험 시작");
            r.ctrl.startActionLabel = "Go";
            r.ctrl.ResetAll();
            Check("패널 액션: 라벨 문구는 인스펙터 값", r.quiz.actionLabel == "Go");
            r.ctrl.Unbind();
            Check("패널 액션: 연결을 끊으면 라벨을 치운다", string.IsNullOrEmpty(r.quiz.actionLabel));
            Dispose(r);
        }

        {
            Rig r = NewRig(d: 1f, g: 30f, h: 0f);
            Occupy(r);
            r.ctrl.StartExperiment();
            r.quiz.Select(1, r.b); r.quiz.Submit(r.b);
            r.quiz.Select(0, r.b); r.quiz.Submit(r.b);
            r.quiz.Select(2, r.b); r.quiz.Submit(r.b);
            Check("패널 액션: 전체 성공 후에는 라벨이 없다", r.ctrl.Current == PowerMaintenanceController.State.Completed && string.IsNullOrEmpty(r.quiz.actionLabel));
            Dispose(r);
        }

        // ── 시작 전에 문답을 먼저 다 푼 경우: 시작 버튼이 사라지지 않는다 ──────────
        {
            Rig r = NewRig();
            Occupy(r);
            r.quiz.Select(1, r.b); r.quiz.Submit(r.b);
            r.quiz.Select(0, r.b); r.quiz.Submit(r.b);
            r.quiz.Select(2, r.b);
            var last = r.quiz.Submit(r.b);
            Check("시작 전 풀이: 준비 상태에서 문답을 다 풀 수 있다", last == QuizTerminal.SubmitResult.AllCleared && r.quiz.AllCleared);
            Check("시작 전 풀이: 준비 상태·전력 불변·시작 라벨 유지", r.ctrl.Current == PowerMaintenanceController.State.Ready
                && Near(r.ctrl.Power, 100f) && r.quiz.actionLabel == "실험 시작" && r.completed == 0);
            Check("시작 전 풀이: 문답이 끝난 패널에서도 시작 액션을 받는다", r.quiz.RequestAction(r.b));
            Check("시작 전 풀이: 시작과 동시에 종료 상태(시작·종료 이벤트 1회씩)", r.ctrl.Current == PowerMaintenanceController.State.Completed
                && r.started == 1 && r.completed == 1);
            r.ctrl.Tick(100f);
            Check("시작 전 풀이: 종료 뒤 전력은 줄지 않고 라벨은 사라짐", Near(r.ctrl.Power, 100f) && string.IsNullOrEmpty(r.quiz.actionLabel));
            Dispose(r);
        }

        // ── E-06 종료 ────────────────────────────────────────────────
        {
            Rig r = NewRig(d: 1f, g: 30f, h: 0f);
            Occupy(r);
            r.ctrl.StartExperiment();
            r.quiz.Select(1, r.b); r.quiz.Submit(r.b);
            r.quiz.Select(0, r.b); r.quiz.Submit(r.b);
            r.quiz.Select(2, r.b);
            var last = r.quiz.Submit(r.b);
            Check("E-06: 마지막 문항 성공 → 종료 상태·이벤트 1회", last == QuizTerminal.SubmitResult.AllCleared
                && r.ctrl.Current == PowerMaintenanceController.State.Completed && r.completed == 1);
            float p = r.ctrl.Power;
            int num = r.ctrl.AssignmentNumber;
            r.ctrl.Tick(100f);
            Complete(r);
            Check("E-06: 종료 후 시간 진행해도 감소·새 배정·정전 없음", Near(r.ctrl.Power, p) && r.ctrl.AssignmentNumber == num && !r.ctrl.SubmitLocked);
            Dispose(r);
        }

        // ── E-07 자동 유지 ───────────────────────────────────────────
        {
            Rig r = NewRig();
            Check("자동 유지: 시작 전에는 옵션 변경 가능", r.ctrl.SetAutoMaintain(true) && r.ctrl.AutoMaintain);
            Occupy(r, includePower: false);
            Check("E-07: 전력 역할 없이도 시작 가능", r.ctrl.StartExperiment());
            Check("E-07: 회로 배정 안 함", r.ctrl.AssignmentNumber == 0);
            r.ctrl.Tick(500f);
            Check("E-07: 전력은 M로 고정, 정전 없음", Near(r.ctrl.Power, 100f) && !r.ctrl.SubmitLocked && r.quiz.SubmitGateOpen);
            Complete(r);
            Check("E-07: 자동 유지 중 완료 신호는 무시", Near(r.ctrl.Power, 100f) && r.ctrl.AssignmentNumber == 0);
            Check("E-07: 시작 후 옵션 변경 거부", !r.ctrl.SetAutoMaintain(false) && r.ctrl.AutoMaintain);
            // 책·컴퓨터(입구 배선과 문답)는 여전히 필수.
            Dispose(r);
        }

        {
            Rig r = NewRig();
            r.ctrl.SetAutoMaintain(true);
            r.book.HandleInteract(r.a);
            Check("E-07: 자동 유지에서도 컴퓨터 담당은 필수", !r.ctrl.StartExperiment());
            Dispose(r);
        }

        // ── 전체 재시작 ──────────────────────────────────────────────
        {
            Rig r = NewRig(d: 1f, g: 30f, h: 3f);
            Occupy(r);
            r.ctrl.StartExperiment();
            r.ctrl.Tick(85f);
            Check("재시작 준비: 잠긴 상태", r.ctrl.SubmitLocked);
            r.manager.ResetChapter();
            Check("재시작: E0 복원·준비 상태·배정번호 초기화", Near(r.ctrl.Power, 100f)
                && r.ctrl.Current == PowerMaintenanceController.State.Ready && r.ctrl.AssignmentNumber == 0);
            Check("재시작: 제출 게이트 다시 열림", !r.ctrl.SubmitLocked && r.quiz.SubmitGateOpen);
            float p = r.ctrl.Power;
            Complete(r);
            r.ctrl.Tick(30f);
            Check("재시작: 이전 시도의 지연 완료 신호·시간은 무효", Near(r.ctrl.Power, p));
            Occupy(r);
            Check("재시작 후 다시 시작 가능", r.ctrl.StartExperiment() && r.ctrl.AssignmentNumber == 1);
            Dispose(r);
        }

        return Finish();
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            passed++;
            Debug.Log($"[PowerSelfTest] PASS  {name}");
        }
        else
        {
            failed++;
            Debug.LogError($"[PowerSelfTest] FAIL  {name}");
        }
    }

    private static bool Finish()
    {
        Debug.Log($"[PowerSelfTest] 결과: PASS {passed} / FAIL {failed}");
        return failed == 0;
    }
}
