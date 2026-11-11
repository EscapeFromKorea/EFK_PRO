using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// IsolationRescueController/IsolationTimer의 순수 로직 자가검증 — 씬·물리 없이 docs/PRD/IsolationRescue.md
/// §6 완료 조건(C2-01~08) 중 로직으로 재현 가능한 것과 §4 상태 전이표의 경계를 확인한다.
///
/// [왜 Test Framework가 아니라 Editor 스크립트인가]
/// 저장소에는 com.unity.test-framework 패키지도 asmdef도 없다. 팀 공용 Packages/manifest.json을 이
/// 기능 하나 때문에 바꾸지 않으려고, 기존 Editor 메뉴 관례(RespawnMenuItem 등)대로 메뉴 + 배치모드 진입점
/// 두 개만 둔다. 시각은 IsolationTimer.Clock 주입으로 직접 움직여 마감 경계(C2-05/06)를 결정적으로 재현한다.
///
/// 실행: 메뉴 Tools &gt; Isolation Rescue &gt; Run Logic Self-Test, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod IsolationRescueSelfTest.RunFromCommandLine`
/// (실패가 하나라도 있으면 종료 코드 1).
///
/// [이 검증이 다루지 않는 것] 문 물리(끼임), 레버 충돌, 트리거 볼륨, UI 표시 — 씬에서 직접 확인해야 하는
/// 항목(C2-07 도형별 끼임 시험 포함)이다.
/// </summary>
public static class IsolationRescueSelfTest
{
    private static int passed;
    private static int failed;

    [MenuItem("Tools/Isolation Rescue/Run Logic Self-Test")]
    public static void RunFromMenu()
    {
        Run();
    }

    // -executeMethod IsolationRescueSelfTest.RunFromCommandLine
    public static void RunFromCommandLine()
    {
        bool ok = Run();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private class Rig
    {
        public GameObject go;
        public GameObject player;
        public GameObject otherPlayer;
        public IsolationRescueController c;
        public IsolationTimer t;
        public float now;
        public int[] stageDone;
        public int success, timedOut, aborted, cancelled, captured, restarted, awaiting;
    }

    private static readonly int[][] DefaultOrders =
    {
        new[] { 2, 1, 3 },
        new[] { 1, 2, 3 },
        new[] { 3, 2, 1 }
    };

    private static Rig NewRig(int[][] orders = null, float duration = 60f)
    {
        orders = orders ?? DefaultOrders;

        Rig r = new Rig();
        r.go = new GameObject("IsolationRescueSelfTest");
        r.player = new GameObject("insidePlayer");
        r.otherPlayer = new GameObject("otherPlayer");

        r.t = r.go.AddComponent<IsolationTimer>();
        r.t.duration = duration;
        r.t.Clock = () => r.now;

        r.c = r.go.AddComponent<IsolationRescueController>();
        r.c.timer = r.t;
        r.stageDone = new int[orders.Length];
        r.c.stages = new IsolationRescueController.Stage[orders.Length];
        for (int i = 0; i < orders.Length; i++)
        {
            int idx = i;
            IsolationRescueController.Stage s = new IsolationRescueController.Stage { answerOrder = orders[i] };
            s.onCompleted.AddListener(() => r.stageDone[idx]++);
            r.c.stages[i] = s;
        }

        r.c.onSuccess.AddListener(() => r.success++);
        r.c.onTimedOut.AddListener(() => r.timedOut++);
        r.c.onAborted.AddListener(() => r.aborted++);
        r.c.onCaptureCancelled.AddListener(() => r.cancelled++);
        r.c.onCaptureConfirmed.AddListener(() => r.captured++);
        r.c.onRestarted.AddListener(() => r.restarted++);
        r.c.onFinalReleaseAwaiting.AddListener(() => r.awaiting++);
        return r;
    }

    private static void Dispose(Rig r)
    {
        Object.DestroyImmediate(r.go);
        Object.DestroyImmediate(r.player);
        Object.DestroyImmediate(r.otherPlayer);
    }

    // 격리 후보 확정 → 문 안전 닫힘 → 양쪽 준비 확인까지 한 번에.
    private static void StartRound(Rig r)
    {
        r.c.RequestCapture(r.player);
        r.c.ReportDoorClosedSafe();
        r.c.SetReady(true);
        r.c.SetReady(false);
    }

    private static void Submit(Rig r, params int[] levers)
    {
        foreach (int l in levers) r.c.SubmitLever(l);
    }

    // 모든 단계를 정답으로 끝내 최종 해제 대기까지 보낸다.
    private static void FinishAllStages(Rig r)
    {
        Submit(r, 2, 1, 3);
        Submit(r, 1, 2, 3);
        Submit(r, 3, 2, 1);
    }

    private static bool OrderIs(Rig r, params int[] expected)
    {
        if (r.c.CurrentOrder.Count != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
            if (r.c.CurrentOrder[i] != expected[i]) return false;
        return true;
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            passed++;
            Debug.Log($"[SelfTest] PASS  {name}");
        }
        else
        {
            failed++;
            Debug.LogError($"[SelfTest] FAIL  {name}");
        }
    }

    private static bool Run()
    {
        passed = 0;
        failed = 0;

        // ── C2-01: 버전0/1단계에서 2→1→3 → 1단계 완료, 문1 열림, 2단계 단서 ─────────────
        {
            Rig r = NewRig();
            StartRound(r);
            Check("시작: 준비 확인 후 진행 상태", r.c.Current == IsolationRescueController.State.InProgress);
            Check("시작: 1단계 정답 순서 [2,1,3]", OrderIs(r, 2, 1, 3));
            Check("C2-01: 2 입력은 Correct", r.c.SubmitLever(2) == IsolationRescueController.SubmitResult.Correct);
            Check("C2-01: 1 입력은 Correct", r.c.SubmitLever(1) == IsolationRescueController.SubmitResult.Correct);
            Check("C2-01: 3 입력은 StageCompleted", r.c.SubmitLever(3) == IsolationRescueController.SubmitResult.StageCompleted);
            Check("C2-01: 1단계 완료 이벤트 정확히 1회", r.stageDone[0] == 1);
            Check("C2-01: 2단계로 진행", r.c.StageIndex == 1 && r.c.InputCount == 0);
            Check("C2-01: 2단계 단서(정답 순서) [1,2,3]", OrderIs(r, 1, 2, 3));
            Dispose(r);
        }

        // ── C2-02/03: 오답 → 입력 삭제 + 순서 왼쪽 순환, 시간 페널티 없음 ──────────────
        {
            Rig r = NewRig();
            StartRound(r);
            float deadlineBefore = r.t.Deadline;
            Submit(r, 2);
            Check("C2-02: 2→2 두 번째는 Wrong", r.c.SubmitLever(2) == IsolationRescueController.SubmitResult.Wrong);
            Check("C2-02: 미완료 입력 비움", r.c.InputCount == 0);
            Check("C2-02: 순서 [2,1,3] → [1,3,2] (왼쪽 1칸)", OrderIs(r, 1, 3, 2));
            Check("C2-02: 문제 버전 1", r.c.QuestionVersion == 1);
            Check("C2-02: 마감 시각 불변(시간 페널티 없음)", Mathf.Approximately(deadlineBefore, r.t.Deadline));

            // C2-03은 "옛 순서로 입력 → 무시, 표시와 일치"인데 '무시'의 해석이 PRD에서 갈린다.
            // 여기서는 옛 순서 첫 입력(2)을 일반 오답으로 처리하는 해석으로 고정하고, 어떤 경우에도
            // 단계가 완료되지 않고 표시(CurrentOrder)가 실제 정답과 일치하는지만 검증한다.
            var res = r.c.SubmitLever(2);
            Check("C2-03(해석): 옛 순서 입력은 정답 처리되지 않음", res == IsolationRescueController.SubmitResult.Wrong);
            Check("C2-03(해석): 단계 미완료", r.stageDone[0] == 0 && r.c.StageIndex == 0);
            Check("C2-03(해석): 표시 순서 = 실제 정답(버전2 [3,2,1])", OrderIs(r, 3, 2, 1) && r.c.QuestionVersion == 2);
            Dispose(r);
        }

        // ── C2-04: 2단계 오답 시 1단계 성공 상태 유지 ─────────────────────────────────
        {
            Rig r = NewRig();
            StartRound(r);
            Submit(r, 2, 1, 3);
            r.c.SubmitLever(3); // 2단계 기본 순서 [1,2,3]의 첫 자리는 1 → 오답
            Check("C2-04: 1단계 완료 이벤트 그대로 1회", r.stageDone[0] == 1);
            Check("C2-04: 여전히 2단계·진행 상태", r.c.StageIndex == 1 && r.c.Current == IsolationRescueController.State.InProgress);
            Check("C2-04: 2단계만 순환(버전 1)", r.c.QuestionVersion == 1);
            Dispose(r);
        }

        // ── C2-05: 타이머 만료 → 시간 초과(숏컷 없음), 이후 입력 잠금 ────────────────
        {
            Rig r = NewRig();
            StartRound(r);
            r.now = 61f;
            r.c.CheckDeadline();
            Check("C2-05: 시간 초과 상태", r.c.Current == IsolationRescueController.State.TimedOut);
            Check("C2-05: 시간 초과 이벤트 1회, 성공 이벤트 없음", r.timedOut == 1 && r.success == 0);
            Check("C2-05: 이후 레버 입력 무시", r.c.SubmitLever(2) == IsolationRescueController.SubmitResult.Ignored);
            r.c.CheckDeadline();
            Check("C2-05: 시간 초과 이벤트 중복 발신 없음", r.timedOut == 1);
            Check("C2-05: 타이머 정지", !r.t.IsRunning);
            Dispose(r);
        }

        // ── C2-06: 최종 버튼 입력 시각 == 마감 → 시간 초과만 ──────────────────────────
        {
            Rig r = NewRig();
            StartRound(r);
            FinishAllStages(r);
            Check("C2-06 준비: 최종 해제 대기", r.c.Current == IsolationRescueController.State.AwaitFinalRelease && r.awaiting == 1);
            r.c.SetPowerHold(true);
            r.now = r.t.Deadline; // 정확히 마감 시각
            bool released = r.c.TryRelease();
            Check("C2-06: 마감과 같은 시각의 해제는 실패", !released);
            Check("C2-06: 시간 초과만 발생(성공 아님)", r.c.Current == IsolationRescueController.State.TimedOut && r.timedOut == 1 && r.success == 0);
            Dispose(r);
        }

        // ── C2-08: 외부 스위치 OFF 해제 거부(진행 보존) → ON 후 재시도 성공 ────────────
        {
            Rig r = NewRig();
            StartRound(r);
            FinishAllStages(r);
            bool first = r.c.TryRelease();
            Check("C2-08: 스위치 OFF 해제는 거부", !first);
            Check("C2-08: 거부 후에도 최종 해제 대기 유지(진행 보존)", r.c.Current == IsolationRescueController.State.AwaitFinalRelease);
            r.c.SetPowerHold(true);
            r.now = r.t.Deadline - 0.1f;
            Check("C2-08: 마감 전 스위치 ON 재시도 성공", r.c.TryRelease());
            Check("C2-08: 성공 상태·이벤트 1회·타이머 정지", r.c.Current == IsolationRescueController.State.Success && r.success == 1 && !r.t.IsRunning);
            Check("성공 후 추가 해제 시도는 무시", !r.c.TryRelease() && r.success == 1);
            Dispose(r);
        }

        // ── 격리 후보: 첫 유효 참가자 1명만 ────────────────────────────────────────────
        {
            Rig r = NewRig();
            Check("포획: 첫 요청 수락", r.c.RequestCapture(r.player));
            Check("포획: 두 번째 요청 거부(두 명 동시 격리 금지)", !r.c.RequestCapture(r.otherPlayer));
            Check("포획: 대상은 첫 참가자", r.c.InsidePlayer == r.player && r.captured == 1);
            Dispose(r);
        }

        // ── 안전검사 실패 → 취소 후 대기 복귀, 재시도 가능 ───────────────────────────
        {
            Rig r = NewRig();
            r.c.RequestCapture(r.player);
            r.c.ReportDoorBlocked();
            Check("취소: 대기로 복귀·취소 이벤트 1회", r.c.Current == IsolationRescueController.State.Idle && r.cancelled == 1);
            Check("취소: 대상 비워짐", r.c.InsidePlayer == null);
            Check("취소: 이후 다시 확정 가능", r.c.RequestCapture(r.otherPlayer));
            Dispose(r);
        }

        // ── 준비: 문 안전 + 양쪽 준비가 모두 있어야 시작 ─────────────────────────────
        {
            Rig r = NewRig();
            r.c.RequestCapture(r.player);
            r.c.SetReady(true);
            r.c.SetReady(false);
            Check("준비: 문 안전 확인 전에는 시작하지 않음", r.c.Current == IsolationRescueController.State.Preparing);
            r.c.ReportDoorClosedSafe();
            Check("준비: 문 안전 확인 후 시작", r.c.Current == IsolationRescueController.State.InProgress);
            Dispose(r);
        }
        {
            Rig r = NewRig();
            r.c.RequestCapture(r.player);
            r.c.ReportDoorClosedSafe();
            r.c.SetReady(true);
            Check("준비: 바깥 준비 전에는 시작하지 않음", r.c.Current == IsolationRescueController.State.Preparing);
            Dispose(r);
        }

        // ── 콘텐츠/타이머 검증: 잘못된 설정은 시작 거부 ──────────────────────────────
        {
            Rig r = NewRig(duration: 0f);
            Check("검증: 제한시간 0이면 격리 시작 거부", !r.c.RequestCapture(r.player) && r.c.Current == IsolationRescueController.State.Idle);
            Dispose(r);

            r = NewRig(new[] { new[] { 1, 1, 2 } });
            Check("검증: 중복 레버 번호 거부", !r.c.RequestCapture(r.player));
            Dispose(r);

            r = NewRig(new[] { new[] { 1, 2, 4 } });
            Check("검증: 범위 밖 레버 번호 거부", !r.c.RequestCapture(r.player));
            Dispose(r);

            r = NewRig(new[] { new[] { 1, 2 } });
            Check("검증: 길이 불일치 거부", !r.c.RequestCapture(r.player));
            Dispose(r);

            r = NewRig();
            r.c.stages = new IsolationRescueController.Stage[0];
            Check("검증: 단계 없음 거부", !r.c.RequestCapture(r.player));
            Dispose(r);
        }

        // ── 입력 방어: 범위 밖 레버 번호 무시 ───────────────────────────────────────
        {
            Rig r = NewRig();
            StartRound(r);
            Check("입력: 범위 밖 레버(4) 무시", r.c.SubmitLever(4) == IsolationRescueController.SubmitResult.Ignored);
            Check("입력: 무시된 입력은 상태 불변", r.c.InputCount == 0 && r.c.QuestionVersion == 0);
            Check("입력: 진행 전(준비 상태) 입력 무시", NewIdleIgnores());
            Dispose(r);
        }

        // ── 중단: 준비/진행에서만 유효, 이후 입력 무시 ───────────────────────────────
        {
            Rig r = NewRig();
            r.c.Abort("대기 중 중단");
            Check("중단: 대기 상태에서는 무시", r.c.Current == IsolationRescueController.State.Idle && r.aborted == 0);
            StartRound(r);
            r.c.Abort("테스트");
            Check("중단: 진행 중 중단 → Aborted·이벤트 1회·타이머 정지",
                r.c.Current == IsolationRescueController.State.Aborted && r.aborted == 1 && !r.t.IsRunning);
            Check("중단: 이후 레버 입력 무시", r.c.SubmitLever(2) == IsolationRescueController.SubmitResult.Ignored);
            Check("중단: 정답 순서 표시 비워짐", OrderIs(r, 0, 0, 0));
            Dispose(r);
        }

        // ── 전체 재시작: 시도 번호 증가, 상태 초기화, 스위치 상태는 보존 ─────────────
        {
            Rig r = NewRig();
            StartRound(r);
            Submit(r, 2, 2); // 오답 1회로 버전 1
            r.c.SetPowerHold(true);
            r.c.FullRestart();
            Check("재시작: 시도 번호 1·대기 복귀·이벤트 1회", r.c.Attempt == 1 && r.c.Current == IsolationRescueController.State.Idle && r.restarted == 1);
            Check("재시작: 단계/입력/버전 초기화", r.c.StageIndex == 0 && r.c.InputCount == 0 && r.c.QuestionVersion == 0);
            Check("재시작: 이전 시도의 정답 순서 표시가 남지 않음", OrderIs(r, 0, 0, 0));
            Check("재시작: 타이머 정지", !r.t.IsRunning);
            Check("재시작: 물리 스위치 상태(PowerOn)는 보존", r.c.PowerOn);
            Check("재시작: 다시 격리 시작 가능", r.c.RequestCapture(r.player));
            Dispose(r);
        }

        Debug.Log($"[SelfTest] 결과: PASS {passed} / FAIL {failed}");
        return failed == 0;
    }

    // 별도 리그로 "준비 전(대기) 입력 무시"를 확인한다.
    private static bool NewIdleIgnores()
    {
        Rig r = NewRig();
        bool ok = r.c.SubmitLever(1) == IsolationRescueController.SubmitResult.Ignored
                  && !r.c.TryRelease();
        Dispose(r);
        return ok;
    }
}
