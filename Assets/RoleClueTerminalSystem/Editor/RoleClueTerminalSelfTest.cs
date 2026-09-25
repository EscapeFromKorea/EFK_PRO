using UnityEditor;
using UnityEngine;

/// <summary>
/// RoleClueTerminal 시스템(RoleSlot / RoleAssignmentManager / BookPanel / QuizTerminal)의 순수 로직
/// 자가검증. 씬·물리·입력 없이 docs/PRD/RoleClueTerminal.md §6 완료 조건 중 로직으로 재현 가능한 것
/// (R-01~R-07)과 팀 회신 규칙(사물 점유·1인 1사물·Esc 해제·Tab 전환·이탈/재접속/재개)을 확인한다.
///
/// [왜 Test Framework가 아닌가] 저장소에 패키지·asmdef가 없어 공용 Packages/manifest.json을 바꾸지
/// 않으려고 IsolationRescueSelfTest와 같은 Editor 메뉴 + 배치모드 진입점 방식을 쓴다.
///
/// [입력 대신 메서드 직접 호출] Input.GetKeyDown은 EditMode에서 흉내낼 수 없다. 그래서 키 처리
/// (Update)와 로직(HandleInteract / ReleaseByPlayer / Tick 등)을 분리해 뒀고, 여기서는 로직만 부른다.
///
/// 실행: 메뉴 Tools &gt; Role Clue Terminal &gt; Run Logic Self-Test, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod RoleClueTerminalSelfTest.RunFromCommandLine`
/// (실패가 하나라도 있으면 종료 코드 1).
///
/// [다루지 않는 것] 화면 그리기(OnGUI), 트리거 볼륨/콜라이더 겹침, 두 화면에서의 정보 격리 체감(R-08),
/// 실제 넷코드 이탈 감지 — 씬 플레이테스트/팀 결정이 필요한 항목이다.
/// </summary>
public static class RoleClueTerminalSelfTest
{
    private static int passed;
    private static int failed;

    [MenuItem("Tools/Role Clue Terminal/Run Logic Self-Test")]
    public static void RunFromMenu()
    {
        Run();
    }

    // -executeMethod RoleClueTerminalSelfTest.RunFromCommandLine
    public static void RunFromCommandLine()
    {
        bool ok = Run();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private class MockReceiver : IParticipantPauseReceiver
    {
        public int calls;
        public bool last;

        public void SetParticipantPaused(bool paused)
        {
            calls++;
            last = paused;
        }
    }

    private class Rig
    {
        public GameObject root;
        public PlayerMover a, b, c, d;
        public PlayerMover controlled;
        public RoleAssignmentManager manager;
        public RoleSlot book, computer, power;
        public QuizTerminal quiz;
        public BookPanel bookPanel;
        public MockReceiver receiver;
        public int wrong, allCleared;
        public System.Collections.Generic.List<int> cleared = new System.Collections.Generic.List<int>();
    }

    private static PlayerMover NewPlayer(string name)
    {
        return new GameObject(name).AddComponent<PlayerMover>();
    }

    private static Rig NewRig()
    {
        Rig r = new Rig();
        r.root = new GameObject("RoleClueSelfTest");
        r.a = NewPlayer("A");
        r.b = NewPlayer("B");
        r.c = NewPlayer("C");
        r.d = NewPlayer("D");
        r.controlled = r.a;

        r.manager = r.root.AddComponent<RoleAssignmentManager>();
        r.manager.ControlledPlayerResolver = () => r.controlled;
        r.manager.showStatusOverlay = false;

        r.book = NewSlot(r, "Book", false);
        r.computer = NewSlot(r, "Computer", false);
        r.power = NewSlot(r, "Power", true);

        r.quiz = r.root.AddComponent<QuizTerminal>();
        r.quiz.slot = r.computer;
        r.quiz.questions = new[]
        {
            NewQuestion(1), // Q1 정답 = 선택지 1
            NewQuestion(0), // Q2 정답 = 선택지 0
            NewQuestion(2)  // Q3 정답 = 선택지 2
        };
        r.quiz.onWrongAnswer.AddListener(() => r.wrong++);
        r.quiz.onAllCleared.AddListener(() => r.allCleared++);
        r.quiz.onQuestionCleared.AddListener(i => r.cleared.Add(i));

        r.receiver = new MockReceiver();
        r.manager.Subscribe(r.receiver);
        r.quiz.Bind();

        r.bookPanel = r.root.AddComponent<BookPanel>();
        r.bookPanel.slot = r.book;
        r.bookPanel.pages = new[] { "p1", "p2", "p3" };
        r.bookPanel.Bind();
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
        return new QuizTerminal.Question
        {
            prompt = "q",
            choices = new[] { "c0", "c1", "c2" },
            correctIndex = correct
        };
    }

    private static void Dispose(Rig r)
    {
        Object.DestroyImmediate(r.book.gameObject);
        Object.DestroyImmediate(r.computer.gameObject);
        Object.DestroyImmediate(r.power.gameObject);
        foreach (PlayerMover p in new[] { r.a, r.b, r.c, r.d })
            if (p != null) Object.DestroyImmediate(p.gameObject);
        Object.DestroyImmediate(r.root);
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            passed++;
            Debug.Log($"[RoleSelfTest] PASS  {name}");
        }
        else
        {
            failed++;
            Debug.LogError($"[RoleSelfTest] FAIL  {name}");
        }
    }

    private static bool Run()
    {
        passed = 0;
        failed = 0;

        // ── R-01: 동시에 같은 사물 → 한 명만 점유, 다른 사람은 "사용 중" ────────────────
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.book.HandleInteract(r.b);
            Check("R-01: 먼저 상호작용한 A만 점유", r.book.CurrentOwner == r.a && r.book.Users.Count == 1);
            Check("R-01: A의 화면이 열림", r.book.IsPanelOpen && r.book.PanelUser == r.a);
            Check("R-01: B에게 '다른 플레이어가 사용 중' 안내", r.manager.LastMessage.Contains("다른 플레이어가 사용 중"));
            Dispose(r);
        }

        // ── R-02: A가 책 점유 중 B 점유 컴퓨터에 시도 → 거부, 기존 점유 유지 ─────────────
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.computer.HandleInteract(r.b);
            r.computer.HandleInteract(r.a);
            Check("R-02: A의 컴퓨터 시도 거부", r.computer.CurrentOwner == r.b);
            Check("R-02: A의 책 점유 유지", r.book.CurrentOwner == r.a);
            Check("R-02: Esc로 먼저 종료하라는 안내", r.manager.LastMessage.Contains("Esc"));
            Dispose(r);
        }

        // ── 1인 1사물 + 교환은 각자 내려놓은 뒤 다른 사물 ─────────────────────────────
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.computer.HandleInteract(r.b);
            // 자동 교환/강제 교체 없음: 서로 상대 사물을 요청해도 아무 변화 없다.
            r.book.HandleInteract(r.b);
            r.computer.HandleInteract(r.a);
            Check("교환: 강제/자동 교환 없음", r.book.CurrentOwner == r.a && r.computer.CurrentOwner == r.b);

            // R-03: A 반환 → B가 그 사물 선택. 단, B는 자기 사물을 먼저 내려놓아야 한다.
            Check("R-03: A 반환 성공", r.manager.ReleaseByPlayer(r.a));
            Check("R-03: 반환 후 책이 비어 있음", r.book.CurrentOwner == null && !r.book.IsPanelOpen);
            r.book.HandleInteract(r.b);
            Check("R-03: B가 내려놓기 전에는 책 선택 불가", r.book.CurrentOwner == null);
            r.manager.ReleaseByPlayer(r.b);
            r.book.HandleInteract(r.b);
            r.computer.HandleInteract(r.a);
            Check("교환: 각자 내려놓은 뒤 서로 바꿔 점유", r.book.CurrentOwner == r.b && r.computer.CurrentOwner == r.a);
            Dispose(r);
        }

        // ── Esc는 화면이 닫혀 있어도 점유를 푼다(Tab 복귀 직후 등) ───────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.manager.ClosePanelsOf(r.a);
            Check("Esc: 화면만 닫으면 점유는 유지", !r.computer.IsPanelOpen && r.computer.CurrentOwner == r.a);
            Check("Esc: 화면이 닫혀 있어도 사용 종료 가능", r.manager.ReleaseByPlayer(r.a) && r.computer.CurrentOwner == null);
            Check("Esc: 점유 사물이 없으면 아무 일도 없음", !r.manager.ReleaseByPlayer(r.a));
            Dispose(r);
        }

        // ── Tab: 이전 캐릭터의 화면만 닫고 점유·시간은 유지, 복귀 후 상호작용으로 다시 열림 ─
        {
            Rig r = NewRig();
            r.controlled = r.a;
            r.manager.Tick();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(1, r.a);
            r.controlled = r.b; // Tab으로 조종 캐릭터 전환
            r.manager.Tick();
            Check("Tab: 이전 캐릭터의 화면이 닫힘", !r.computer.IsPanelOpen);
            Check("Tab: 이전 캐릭터의 점유는 유지", r.computer.CurrentOwner == r.a);
            Check("Tab: 미제출 선택 취소", r.quiz.SelectedIndex == -1);
            Check("Tab: 전환만으로 챕터가 멈추지 않음", r.manager.State == RoleAssignmentManager.SessionState.Running);
            r.computer.HandleInteract(r.b);
            Check("Tab: 다른 캐릭터는 점유된 사물을 가져갈 수 없음", r.computer.CurrentOwner == r.a);
            r.controlled = r.a;
            r.manager.Tick();
            r.computer.HandleInteract(r.a);
            Check("Tab: 돌아와 상호작용하면 화면이 다시 열림", r.computer.IsPanelOpen && r.computer.PanelUser == r.a);
            Dispose(r);
        }

        // ── 자발적 반환은 정지시키지 않는다(회신 1) ────────────────────────────────
        {
            Rig r = NewRig();
            int before = r.receiver.calls;
            r.computer.HandleInteract(r.a);
            r.manager.ReleaseByPlayer(r.a);
            Check("자발 반환: 정지 신호 없음", r.receiver.calls == before && !r.receiver.last);
            Check("자발 반환: 챕터 상태 Running", r.manager.State == RoleAssignmentManager.SessionState.Running && !r.manager.IsPaused);
            Dispose(r);
        }

        // ── 서버실 배선만 다인 조작 ───────────────────────────────────────────────
        {
            Rig r = NewRig();
            r.power.HandleInteract(r.a);
            r.power.HandleInteract(r.b);
            Check("배선: 여러 명이 함께 점유", r.power.Users.Count == 2);
            r.manager.ReleaseByPlayer(r.a);
            Check("배선: 한 명이 내려놔도 다른 사람은 유지", r.power.Users.Count == 1 && r.power.HasUser(r.b));
            r.power.HandleInteract(r.a);
            r.book.HandleInteract(r.a);
            Check("배선: 1인 1사물 규칙은 공유 슬롯에도 적용", !r.book.HasUser(r.a));
            Dispose(r);
        }

        // ── 이탈: 점유 해제 + 챕터 정지 + 미제출 선택 취소 ────────────────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(1, r.a);
            int before = r.receiver.calls;
            r.manager.NotifyParticipantLeft(r.a);
            Check("이탈: 이탈자의 사물 점유 해제", r.computer.CurrentOwner == null && !r.computer.IsPanelOpen);
            Check("이탈: 재접속 대기 상태", r.manager.State == RoleAssignmentManager.SessionState.WaitingReconnect);
            Check("이탈: 수신자에게 정지 신호 1회", r.receiver.calls == before + 1 && r.receiver.last);
            Check("이탈: 미제출 선택 취소 + 입력 잠금", r.quiz.SelectedIndex == -1 && r.quiz.ParticipantPaused);
            r.manager.NotifyParticipantLeft(r.a);
            Check("이탈: 같은 사람 중복 이탈 신호는 무시", r.receiver.calls == before + 1);
            Dispose(r);
        }

        // ── 재접속 → 재개 대기 → 시작 담당자가 이어서 시작 ─────────────────────────────
        {
            Rig r = NewRig();
            r.manager.SetStartOwner(r.b);
            r.manager.NotifyParticipantLeft(r.a);
            int afterLeave = r.receiver.calls;
            r.manager.NotifyParticipantRejoined(r.a);
            Check("재접속: 재개 대기 상태(아직 정지 유지)",
                r.manager.State == RoleAssignmentManager.SessionState.WaitingResume && r.manager.IsPaused);
            Check("재접속: 재접속 즉시 재개 신호는 나가지 않음", r.receiver.calls == afterLeave && r.receiver.last);
            Check("재개: 시작 담당자가 아니면 거부", !r.manager.RequestResume(r.c) && r.manager.IsPaused);
            Check("재개: 시작 담당자가 이어서 시작", r.manager.RequestResume(r.b));
            Check("재개: Running으로 복귀 + 재개 신호", r.manager.State == RoleAssignmentManager.SessionState.Running
                                                  && !r.receiver.last && r.receiver.calls == afterLeave + 1);
            Dispose(r);
        }

        // ── 이중 이탈: 전원이 돌아와야 재개 대기 ────────────────────────────────────
        {
            Rig r = NewRig();
            r.manager.NotifyParticipantLeft(r.a);
            r.manager.NotifyParticipantLeft(r.b);
            r.manager.NotifyParticipantRejoined(r.a);
            Check("이중 이탈: 한 명만 돌아오면 아직 재접속 대기", r.manager.State == RoleAssignmentManager.SessionState.WaitingReconnect);
            r.manager.NotifyParticipantRejoined(r.b);
            Check("이중 이탈: 전원 복귀 시 재개 대기", r.manager.State == RoleAssignmentManager.SessionState.WaitingResume);
            Dispose(r);
        }

        // ── 시작 담당자 부재: 임의로 넘기지 않고 거부, ForceResume만 허용 ─────────────
        {
            Rig r = NewRig();
            r.manager.SetStartOwner(r.d);
            r.manager.NotifyParticipantLeft(r.a);
            r.manager.NotifyParticipantRejoined(r.a);
            Object.DestroyImmediate(r.d.gameObject); // 시작 담당자가 사라짐
            Check("담당자 부재: 다른 사람이 대신 재개하지 못함(정책 미정)", !r.manager.RequestResume(r.b));
            r.manager.ForceResume();
            Check("담당자 부재: ForceResume만 재개 가능", r.manager.State == RoleAssignmentManager.SessionState.Running);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.manager.NotifyParticipantLeft(r.a);
            r.manager.NotifyParticipantRejoined(r.a);
            Check("담당자 미지정: 누구나 이어서 시작", r.manager.RequestResume(r.c));
            Dispose(r);
        }

        // ── 늦게 구독한 수신자도 현재 상태를 즉시 받는다 ─────────────────────────────
        {
            Rig r = NewRig();
            r.manager.NotifyParticipantLeft(r.a);
            MockReceiver late = new MockReceiver();
            r.manager.Subscribe(late);
            Check("구독: 정지 중 늦게 구독해도 즉시 정지 상태 수신", late.calls == 1 && late.last);
            Dispose(r);
        }

        // ── QuizTerminal: R-04 사용자 검증 ─────────────────────────────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            Check("R-04: 컴퓨터를 쓰지 않는 B의 선택 거부", !r.quiz.Select(1, r.b));
            r.quiz.Select(1, r.a);
            Check("R-04: 컴퓨터를 쓰지 않는 B의 제출 거부",
                r.quiz.Submit(r.b) == QuizTerminal.SubmitResult.Rejected);
            Check("R-04: 거부는 문제 진행/선택 상태를 바꾸지 않음", r.quiz.CurrentIndex == 0 && r.quiz.SelectedIndex == 1);
            Dispose(r);
        }

        // ── 선택 없이 제출 / 오답 처리 ──────────────────────────────────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            Check("제출: 선택 없이는 거부", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Rejected);
            Check("제출: 선택 없이 거부되면 안내 문구, 오답·진행 없음",
                r.quiz.LastFeedback != null && r.quiz.LastFeedback.Contains("선택해주세요")
                && r.wrong == 0 && r.quiz.CurrentIndex == 0);
            r.quiz.Select(0, r.a);
            Check("제출: 오답은 Wrong", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Wrong);
            Check("제출: 오답 시 선택만 초기화, 같은 문제 유지", r.quiz.SelectedIndex == -1 && r.quiz.CurrentIndex == 0);
            Check("제출: 오답 이벤트 1회·안내 문구", r.wrong == 1 && r.quiz.LastFeedback.Contains("자료를 다시"));
            Check("제출: 범위 밖 선택지 거부", !r.quiz.Select(5, r.a) && r.quiz.SelectedIndex == -1);

            // 패널 액션 훅(서버실 "실험 시작" 같은 다른 시스템의 동작을 패널에 얹는 연결점)
            int requested = 0;
            PlayerMover requestedBy = null;
            r.quiz.onActionRequested.AddListener(p => { requested++; requestedBy = p; });
            Check("패널 액션: 라벨이 없으면 요청을 받지 않는다", !r.quiz.RequestAction(r.a) && requested == 0);
            r.quiz.actionLabel = "시험 동작";
            Check("패널 액션: 화면을 연 참가자가 요청하면 이벤트 1회·요청자 전달", r.quiz.RequestAction(r.a) && requested == 1 && requestedBy == r.a);
            Check("패널 액션: 화면을 열지 않은 참가자의 요청은 거부", !r.quiz.RequestAction(r.b) && requested == 1);
            r.quiz.SetFeedback("안내 문구");
            Check("패널 액션: SetFeedback으로 피드백 줄에 문구를 띄운다", r.quiz.LastFeedback == "안내 문구");
            ((IParticipantPauseReceiver)r.quiz).SetParticipantPaused(true);
            Check("패널 액션: 참가자 이탈 정지 중에는 거부", !r.quiz.RequestAction(r.a) && requested == 1);
            Dispose(r);
        }

        // ── R-05: 오답 → 정답 → 연타 제출 ─────────────────────────────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(0, r.a);
            r.quiz.Submit(r.a); // Q1 오답
            r.quiz.Select(1, r.a);
            Check("R-05: 정답 제출은 Correct", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Correct);
            Check("R-05: Q2는 미선택 상태로 시작", r.quiz.CurrentIndex == 1 && r.quiz.SelectedIndex == -1);
            Check("R-05: 연타 제출은 거부", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Rejected);
            Check("R-05: 연타가 진행/이벤트를 늘리지 않음", r.quiz.CurrentIndex == 1 && r.cleared.Count == 1);
            Dispose(r);
        }

        // ── R-06/R-07: 정전 중 제출 거부, 정답 수락 후 정전은 취소하지 않음 ───────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(1, r.a);
            r.quiz.Submit(r.a); // Q1 정답 → Q2
            r.quiz.Select(0, r.a);
            r.quiz.SetSubmitGate(false);
            int wrongBefore = r.wrong;
            Check("R-06: 정전 중 제출 거부", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Rejected);
            Check("R-06: 오답 처리 없음·선택 유지·진행 불변", r.wrong == wrongBefore && r.quiz.SelectedIndex == 0 && r.quiz.CurrentIndex == 1);

            r.quiz.SetSubmitGate(true);
            Check("R-07 준비: 전력 복구 후 Q2 정답", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Correct);
            r.quiz.SetSubmitGate(false); // 정답 수락 직후 정전
            Check("R-07: 정전이 이미 인정된 정답을 취소하지 않음", r.quiz.CurrentIndex == 2 && r.cleared.Count == 2);

            r.quiz.Select(2, r.a);
            Check("R-07: 정전 중 마지막 답 제출은 거부", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Rejected);
            r.quiz.SetSubmitGate(true);
            Check("전체 성공: 마지막 정답은 AllCleared", r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.AllCleared);
            Check("전체 성공: 이벤트 정확히 1회", r.allCleared == 1 && r.quiz.AllCleared);
            Check("전체 성공: 이후 제출은 거부·중복 발신 없음",
                r.quiz.Submit(r.a) == QuizTerminal.SubmitResult.Rejected && r.allCleared == 1);
            Dispose(r);
        }

        // ── R-03(이전 입력 무효): 사용 종료 시 미제출 선택 취소, 진행은 유지 ──────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(1, r.a);
            r.quiz.Submit(r.a); // Q1 정답
            r.quiz.Select(2, r.a);
            r.manager.ReleaseByPlayer(r.a);
            Check("R-03: 사용 종료 시 미제출 선택 취소", r.quiz.SelectedIndex == -1);
            Check("R-03: 이미 맞힌 문항 진행은 유지", r.quiz.CurrentIndex == 1);
            Check("R-03: 이전 사용자는 더 이상 입력 못함", !r.quiz.Select(0, r.a));
            r.computer.HandleInteract(r.b);
            Check("R-03: 새 사용자가 같은 문항에서 이어서 시작", r.quiz.Select(0, r.b) && r.quiz.CurrentIndex == 1);
            Dispose(r);
        }

        // ── 이탈 중 문항: 완료 문항 보존, 진행 중 문항은 미선택으로 다시 ──────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(1, r.a);
            r.quiz.Submit(r.a);     // Q1 정답
            r.quiz.Select(0, r.a);  // Q2 풀이 중(미제출)
            r.manager.NotifyParticipantLeft(r.b);
            Check("이탈 중: 완료 문항 보존", r.quiz.CurrentIndex == 1);
            Check("이탈 중: 진행 중 문항의 미제출 선택 삭제", r.quiz.SelectedIndex == -1);
            Check("이탈 중: 입력 잠금", !r.quiz.Select(0, r.a));
            r.manager.NotifyParticipantRejoined(r.b);
            Check("재접속 직후에도 입력 잠금 유지(이어서 시작 전)", !r.quiz.Select(0, r.a));
            r.manager.ForceResume();
            Check("재개 후: 같은 문항을 미선택 상태에서 다시 풀 수 있음", r.quiz.Select(0, r.a) && r.quiz.CurrentIndex == 1);
            Dispose(r);
        }

        // ── 정보 격리: 화면은 그 사물을 연 참가자에게만 ─────────────────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            Check("격리: 화면을 연 A에게만 표시", r.computer.CanShowTo(r.a) && !r.computer.CanShowTo(r.b));
            r.manager.ClosePanelsOf(r.a);
            Check("격리: 화면이 닫히면 누구에게도 표시하지 않음", !r.computer.CanShowTo(r.a) && !r.computer.CanShowTo(r.b));
            Dispose(r);
        }

        // ── BookPanel: 페이지 이동/초기화, 화면 닫았다 열어도 위치 유지 ──────────────
        {
            Rig r = NewRig();
            Check("책: 첫 페이지에서 이전은 불가", !r.bookPanel.PrevPage() && r.bookPanel.PageIndex == 0);
            Check("책: 다음 페이지 이동", r.bookPanel.NextPage() && r.bookPanel.PageIndex == 1);
            r.bookPanel.NextPage();
            Check("책: 마지막 페이지에서 다음은 불가", !r.bookPanel.NextPage() && r.bookPanel.PageIndex == 2);
            Check("책: 본문 텍스트", r.bookPanel.CurrentText == "p3");
            r.book.HandleInteract(r.a);
            r.manager.ClosePanelsOf(r.a);
            r.book.HandleInteract(r.a);
            Check("책: 화면을 닫았다 열어도 읽던 위치 유지", r.bookPanel.PageIndex == 2);
            r.bookPanel.ResetToFirstPage();
            Check("책: 챕터 재시작 시 첫 페이지로", r.bookPanel.PageIndex == 0);
            Dispose(r);
        }

        // ── 문답 진행 초기화 ─────────────────────────────────────────────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.quiz.Select(1, r.a);
            r.quiz.Submit(r.a);
            r.quiz.ResetProgress();
            Check("초기화: 첫 문항·미선택·미완료로 복귀", r.quiz.CurrentIndex == 0 && r.quiz.SelectedIndex == -1 && !r.quiz.AllCleared);
            Dispose(r);
        }

        // ── 개인 복귀(추락 등): 그 참가자의 화면만 닫고 점유·진행은 유지(§2.5) ─────────────
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.book.HandleInteract(r.b);
            r.quiz.Select(1, r.a);
            r.quiz.Submit(r.a);      // Q1 정답
            r.quiz.Select(0, r.a);   // Q2 미제출 선택
            r.manager.NotifyPlayerRespawned(r.a.gameObject);
            Check("복귀: 복귀한 참가자의 화면이 닫힘", !r.computer.IsPanelOpen);
            Check("복귀: 역할 점유는 유지", r.computer.CurrentOwner == r.a);
            Check("복귀: 미제출 선택은 취소", r.quiz.SelectedIndex == -1);
            Check("복귀: 이미 맞힌 문항 진행은 유지", r.quiz.CurrentIndex == 1);
            Check("복귀: 다른 참가자의 화면은 그대로", r.book.IsPanelOpen && r.book.PanelUser == r.b);
            Check("복귀: 챕터는 멈추지 않음", r.manager.State == RoleAssignmentManager.SessionState.Running);
            r.manager.NotifyPlayerRespawned(null);
            Check("복귀: null 대상은 무시(예외 없음)", r.book.IsPanelOpen);
            r.computer.HandleInteract(r.a);
            Check("복귀: 돌아온 뒤 상호작용하면 화면이 다시 열림", r.computer.IsPanelOpen);
            Dispose(r);
        }

        // ── 챕터 종료/전체 재시작: 역할 전부 해제 + 책 첫 페이지 + 문항 첫 문제(§2.7) ─────
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.computer.HandleInteract(r.b);
            r.power.HandleInteract(r.c);
            r.bookPanel.NextPage();
            r.bookPanel.NextPage();
            r.quiz.Select(1, r.b);
            r.quiz.Submit(r.b);      // Q1 정답
            r.quiz.Select(0, r.b);   // Q2 미제출
            r.manager.ResetChapter();
            Check("재시작: 모든 사물의 점유 해제",
                r.book.Users.Count == 0 && r.computer.Users.Count == 0 && r.power.Users.Count == 0);
            Check("재시작: 모든 화면 닫힘", !r.book.IsPanelOpen && !r.computer.IsPanelOpen);
            Check("재시작: 책은 첫 페이지", r.bookPanel.PageIndex == 0);
            Check("재시작: 문답은 첫 문항·미선택·미완료", r.quiz.CurrentIndex == 0 && r.quiz.SelectedIndex == -1 && !r.quiz.AllCleared);
            r.book.HandleInteract(r.b);
            Check("재시작: 해제된 사물을 다른 참가자가 새로 점유할 수 있음", r.book.CurrentOwner == r.b);
            Dispose(r);
        }
        {
            // 세션 정지 상태(이탈 대기)는 역할 재시작이 건드리지 않는다.
            Rig r = NewRig();
            r.manager.NotifyParticipantLeft(r.d);
            r.manager.ResetChapter();
            Check("재시작: 이탈 대기(세션 정지) 상태는 유지",
                r.manager.State == RoleAssignmentManager.SessionState.WaitingReconnect && r.manager.IsPaused);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            int resets = 0;
            r.manager.ChapterReset += () => resets++;
            r.manager.ResetChapter();
            r.manager.ResetChapter();
            Check("재시작: ChapterReset 이벤트가 호출 횟수만큼 발신", resets == 2);
            Dispose(r);
        }

        // ── 입력 잠금: 패널이 열려 있는 동안 그 참가자의 이동·점프 입력만 잠근다 ────────────
        {
            Rig r = NewRig();
            Check("잠금: 처음엔 아무도 잠겨 있지 않음", !r.a.InputLocked && !r.b.InputLocked);
            r.book.HandleInteract(r.a);
            Check("잠금: 패널을 열면 그 참가자만 잠김", r.a.InputLocked && !r.b.InputLocked);
            r.computer.HandleInteract(r.b);
            Check("잠금: 다른 참가자의 잠금과 서로 독립", r.a.InputLocked && r.b.InputLocked);
            r.manager.ReleaseByPlayer(r.a);
            Check("잠금: Esc 사용 종료 시 해제(다른 참가자는 유지)", !r.a.InputLocked && r.b.InputLocked);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.manager.ClosePanelsOf(r.a); // Tab 전환·거리 이탈과 같은 경로
            Check("잠금: 화면만 닫혀도(점유 유지) 해제", r.book.CurrentOwner == r.a && !r.a.InputLocked);
            r.book.HandleInteract(r.a);
            Check("잠금: 다시 화면을 열면 다시 잠김", r.a.InputLocked);
            r.manager.NotifyPlayerRespawned(r.a.gameObject);
            Check("잠금: 개인 복귀로 화면이 닫히면 해제", !r.a.InputLocked && r.book.CurrentOwner == r.a);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.computer.HandleInteract(r.a);
            r.manager.NotifyParticipantLeft(r.a);
            Check("잠금: 이탈로 점유가 풀리면 해제", !r.a.InputLocked);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.computer.HandleInteract(r.b);
            r.manager.ResetChapter();
            Check("잠금: 챕터 전체 재시작 시 모두 해제", !r.a.InputLocked && !r.b.InputLocked);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.power.HandleInteract(r.a);
            r.power.HandleInteract(r.b);
            Check("잠금: 여러 명이 쓰는 슬롯(배선)은 잠그지 않음", !r.a.InputLocked && !r.b.InputLocked);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.book.HandleInteract(r.b); // 사용 중이라 거부
            Check("잠금: 거부된 요청은 잠그지 않음", !r.b.InputLocked && r.a.InputLocked);
            r.computer.HandleInteract(r.a); // 1인 1사물로 거부
            Check("잠금: 1인 1사물로 거부돼도 기존 패널의 잠금은 그대로", r.a.InputLocked);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.book.HandleInteract(r.a);
            r.manager.UnregisterSlot(r.book);
            Check("잠금: 화면이 열린 채 슬롯이 빠지면 해제", !r.a.InputLocked);
            Dispose(r);
        }

        Debug.Log($"[RoleSelfTest] 결과: PASS {passed} / FAIL {failed}");
        return failed == 0;
    }
}
