using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 격리실 협력 구출 컨트롤러(챕터당 1개, 상태 보유자) — 실험실 CH2.
/// docs/PRD/IsolationRescue.md §3, §4(상태 전이표).
///
/// [역할 범위]
/// 상태 전이, 현재 단계, 입력 목록, 문제 버전(정답 순환 횟수), 시도 번호, 순서 판정(SequenceJudge
/// 를 컨트롤러 내부 로직으로 둔다 — PRD §3이 허용한 방식)을 책임진다. 문/레버/스위치/출구/표시는
/// 이 컴포넌트가 직접 다루지 않고 UnityEvent와 Report/Submit 창구로만 주고받는다(저장소 공통
/// 발신자-수신자 패턴).
///
/// [RoleClueTerminal과 별개 구현]
/// CH2의 안쪽/바깥쪽은 위치를 골라 맡는 역할이 아니라 진입 이벤트로 자동 결정된다(PRD 상단 스코프
/// 메모). 그래서 RoleAssignmentManager를 쓰지 않는다.
///
/// [아직 이 컴포넌트가 다루지 않는 것 — 의도적]
/// - 참가자 이탈/재접속: PRD §4는 "중단(보상 없음)"이지만 최신 팀 회신은 "완료 단계·열린 문·남은
///   시간을 유지하고 재접속 대기"라 서로 충돌한다. 정해질 때까지 이탈 판정 입력을 연결하지 않았고,
///   Abort는 "취소/중단" 창구만 제공한다.
/// - 개인 추락 복귀: SectionRespawn의 SectionSafePoint(PR #101)를 쓰는 부분이라 그 PR이 머지된
///   뒤 레벨 배선(C2_IN_SAFE/C2_OUT_SAFE)으로 붙인다. 이 컴포넌트는 단계·입력·시간을 건드리지
///   않으므로 복귀와 충돌할 지점이 없다(§2.8).
/// - 문 안전검사 판정 자체: doorPhysics가 끼임 상태(isBlocked)를 공개하지 않아 이 컴포넌트는 결과
///   신호만 받는다(ReportDoorClosedSafe / ReportDoorBlocked).
/// </summary>
public class IsolationRescueController : MonoBehaviour
{
    /// <summary>PRD §4 상태 전이표의 상태들(대기/준비/진행/최종 해제 대기/성공/시간 초과/중단).</summary>
    public enum State
    {
        Idle,
        Preparing,
        InProgress,
        AwaitFinalRelease,
        Success,
        TimedOut,
        Aborted
    }

    /// <summary>레버 입력 하나의 판정 결과. Ignored = 입력 가능한 상태가 아니거나 잘못된 값.</summary>
    public enum SubmitResult
    {
        Ignored,
        Correct,
        Wrong,
        StageCompleted
    }

    /// <summary>단계당 레버 수 = 입력 수. 원본 문서가 3개로 확정했다(§4 "단계당 3개 입력").</summary>
    public const int LeverCount = 3;

    [Serializable]
    public class Stage
    {
        [Tooltip("이 단계의 기본 정답 순서(레버 번호 1~3, 중복 없이 3개). 오답이 날 때마다 왼쪽으로 한 칸 " +
                 "순환한 값이 실제 정답이 된다. 실제 콘텐츠(기호/레버 매핑, 예: 달=2/파도=1/십자=3)는 " +
                 "'미확정 콘텐츠 구성안'이라 값은 씬에서 정한다.")]
        public int[] answerOrder = { 1, 2, 3 };

        [Tooltip("이 단계를 완료했을 때 발신. 대응 문을 영구 개방하는 곳에 건다 — " +
                 "예: doorPhysics.SetPadPressed(true)를 호출해 다시 닫지 않는 문으로 쓴다(§3 IsolationDoor).")]
        public UnityEvent onCompleted = new UnityEvent();
    }

    [Header("콘텐츠")]
    [Tooltip("단계 목록(원본 예시는 3단계). 비어 있거나 정답 순서가 잘못되면 격리 시작을 거부한다 — " +
             "콘텐츠 오류를 런타임에 잡는다(WiringCircuitData.Validate와 같은 방침).")]
    public Stage[] stages = new Stage[0];

    [Tooltip("제한시간 타이머(C2_TIMER). 비워 두면 같은 오브젝트에서 찾는다.")]
    public IsolationTimer timer;

    [Header("이벤트 — 레벨/연출 배선용")]
    [Tooltip("격리 대상이 확정됐을 때(대기→준비). 여기에 문 닫힘을 걸고, 문이 안전하게 닫히면 " +
             "ReportDoorClosedSafe, 막히면 ReportDoorBlocked를 불러준다.")]
    public UnityEvent onCaptureConfirmed = new UnityEvent();

    [Tooltip("준비 중 격리가 취소됐을 때(문 열림·타이머 정지). 대기 상태로 돌아간다.")]
    public UnityEvent onCaptureCancelled = new UnityEvent();

    [Tooltip("양쪽 준비가 끝나 1단계가 시작될 때(단서 표시·입력 허용).")]
    public UnityEvent onRoundStarted = new UnityEvent();

    [Tooltip("단계/입력 진행도/정답 순서가 바뀔 때마다. ClueBoard 같은 표시 컴포넌트가 여기에 걸어 " +
             "컨트롤러의 읽기 프로퍼티를 다시 읽는다(표시 전용 — 판정 로직 없음).")]
    public UnityEvent onPuzzleChanged = new UnityEvent();

    [Tooltip("모든 단계를 끝내 최종 해제 대기로 넘어갈 때.")]
    public UnityEvent onFinalReleaseAwaiting = new UnityEvent();

    [Tooltip("성공(시간 내 최종 해제). CH4 숏컷 권한 부여·격리 해제에 건다.")]
    public UnityEvent onSuccess = new UnityEvent();

    [Tooltip("시간 초과. 입력 잠금·비상문 개방·격리 해제·CH3 경로 합류(숏컷 없음)에 건다. 패널티가 " +
             "아니라 분기다.")]
    public UnityEvent onTimedOut = new UnityEvent();

    [Tooltip("중단(보상 없음·문/비상통로 개방).")]
    public UnityEvent onAborted = new UnityEvent();

    [Tooltip("전체 재시작 처리 완료 시점.")]
    public UnityEvent onRestarted = new UnityEvent();

    // ── 읽기 전용 상태 ────────────────────────────────────────────────

    public State Current { get; private set; } = State.Idle;

    /// <summary>격리된(안쪽) 참가자 Root. 대기 상태에서는 null.</summary>
    public GameObject InsidePlayer { get; private set; }

    /// <summary>현재 단계 인덱스(0부터).</summary>
    public int StageIndex { get; private set; }

    /// <summary>현재 단계에서 맞힌 입력 수(진행도 예: 1/3).</summary>
    public int InputCount => inputs.Count;

    /// <summary>문제 버전 = 현재 단계에서 오답으로 정답 순서가 순환된 횟수.</summary>
    public int QuestionVersion { get; private set; }

    /// <summary>시도 번호. 전체 재시작마다 증가해 이전 시도의 지연 입력을 구분할 수 있게 한다.</summary>
    public int Attempt { get; private set; }

    public bool PowerOn { get; private set; }

    public int StageCount => stages != null ? stages.Length : 0;

    /// <summary>현재 정답 순서(안쪽 표시판이 읽는다). 진행 중이 아니면 전부 0.</summary>
    public IReadOnlyList<int> CurrentOrder => orderCache;

    public IsolationTimer Timer
    {
        get
        {
            if (timer == null) timer = GetComponent<IsolationTimer>();
            return timer;
        }
    }

    private readonly List<int> inputs = new List<int>();
    private readonly int[] orderCache = new int[LeverCount];
    private bool doorSafe;
    private bool readyInside;
    private bool readyOutside;

    private void Update()
    {
        CheckDeadline();
    }

    // ── 콘텐츠 검증 ───────────────────────────────────────────────────

    /// <summary>단계 데이터가 유효한지 검사한다. 빈 목록, 길이 불일치, 범위 밖/중복 레버 번호를 거부.</summary>
    public bool ValidateContent(out string error)
    {
        if (stages == null || stages.Length == 0)
        {
            error = "stages가 비어 있다.";
            return false;
        }

        for (int i = 0; i < stages.Length; i++)
        {
            Stage s = stages[i];
            if (s == null || s.answerOrder == null || s.answerOrder.Length != LeverCount)
            {
                error = $"단계 {i}의 answerOrder는 정확히 {LeverCount}개여야 한다.";
                return false;
            }

            bool[] seen = new bool[LeverCount + 1];
            foreach (int v in s.answerOrder)
            {
                if (v < 1 || v > LeverCount)
                {
                    error = $"단계 {i}의 레버 번호 {v}가 1~{LeverCount} 범위를 벗어났다.";
                    return false;
                }
                if (seen[v])
                {
                    error = $"단계 {i}의 레버 번호 {v}가 중복됐다.";
                    return false;
                }
                seen[v] = true;
            }
        }

        error = null;
        return true;
    }

    // ── 대기 → 준비 ───────────────────────────────────────────────────

    /// <summary>
    /// 격리 후보 확정 요청(IsolationCaptureTrigger가 부른다). 대기 상태에서 처음 들어온 유효한
    /// 요청만 받아들이고, 같은 프레임에 여러 명이 요청해도 상태가 이미 준비로 넘어갔으므로 나머지는
    /// 거부된다 — "두 명이 함께 갇히는 연출 금지"(§2.2).
    /// </summary>
    public bool RequestCapture(GameObject candidate)
    {
        if (Current != State.Idle || candidate == null) return false;

        if (!ValidateContent(out string error))
        {
            Debug.LogError($"[IsolationRescue] 콘텐츠 오류로 격리 시작을 거부한다: {error}", this);
            return false;
        }

        if (Timer == null || Timer.duration <= 0f)
        {
            Debug.LogError("[IsolationRescue] 양수 제한시간을 가진 IsolationTimer가 없어 격리 시작을 " +
                           "거부한다(타이머 없는 격리 금지).", this);
            return false;
        }

        InsidePlayer = candidate;
        doorSafe = false;
        readyInside = false;
        readyOutside = false;
        Current = State.Preparing;
        Debug.Log($"[IsolationRescue] 격리 대상 확정: {candidate.name}. 문 안전검사 대기.");
        onCaptureConfirmed.Invoke();
        return true;
    }

    /// <summary>문이 참가자/상자 끼임 없이 안전하게 닫혔음을 알린다(준비 상태에서만 유효).</summary>
    public void ReportDoorClosedSafe()
    {
        if (Current != State.Preparing) return;
        doorSafe = true;
        TryStartRound();
    }

    /// <summary>문이 막혀 안전검사에 실패했음을 알린다 → 격리 취소, 대기로 복귀(§4).</summary>
    public void ReportDoorBlocked()
    {
        CancelCapture();
    }

    /// <summary>준비 중 격리를 취소한다(격리 후보가 되돌아 나옴, 안전검사 실패). 대기로 복귀.</summary>
    public void CancelCapture()
    {
        if (Current != State.Preparing) return;
        ClearRound();
        Timer?.Stop();
        Current = State.Idle;
        Debug.Log("[IsolationRescue] 준비 중 격리 취소 — 대기로 복귀.");
        onCaptureCancelled.Invoke();
    }

    /// <summary>준비 확인(READY_IN=안쪽 true / READY_OUT=바깥쪽 false). 양쪽이 끝나면 시작.</summary>
    public void SetReady(bool inside)
    {
        if (Current != State.Preparing) return;
        if (inside) readyInside = true; else readyOutside = true;
        TryStartRound();
    }

    private void TryStartRound()
    {
        if (!doorSafe || !readyInside || !readyOutside) return;

        if (!Timer.TryBegin())
        {
            // duration이 그 사이 바뀌어 0 이하가 된 경우 — 타이머 없는 격리는 시작하지 않는다.
            CancelCapture();
            return;
        }

        StageIndex = 0;
        QuestionVersion = 0;
        inputs.Clear();
        Current = State.InProgress;
        RefreshOrder();
        Debug.Log($"[IsolationRescue] 1단계 시작(시도 #{Attempt}, 제한 {Timer.duration}초).");
        onRoundStarted.Invoke();
        onPuzzleChanged.Invoke();
    }

    // ── 진행: 레버 입력 판정 ──────────────────────────────────────────

    /// <summary>
    /// 레버 입력 한 번(SequenceLever가 선택 시 1회만 부른다). 현재 정답 순서의 다음 자리와 같으면
    /// 입력 목록에 쌓고, 다르면 현재 단계 입력만 지우고 정답 순서를 왼쪽으로 한 칸 순환한다.
    /// 완료된 이전 단계·문·타이머는 그대로다(§4 "오답 처리").
    /// </summary>
    public SubmitResult SubmitLever(int leverNumber)
    {
        CheckDeadline();
        if (Current != State.InProgress) return SubmitResult.Ignored;

        if (leverNumber < 1 || leverNumber > LeverCount)
        {
            Debug.LogWarning($"[IsolationRescue] 범위 밖 레버 번호 {leverNumber} — 무시한다.", this);
            return SubmitResult.Ignored;
        }

        if (orderCache[inputs.Count] != leverNumber)
        {
            inputs.Clear();
            QuestionVersion++;
            RefreshOrder();
            Debug.Log($"[IsolationRescue] 오답(레버 {leverNumber}). 입력 삭제, 정답 순서 순환 → " +
                      $"[{string.Join(",", orderCache)}] (버전 {QuestionVersion}).");
            onPuzzleChanged.Invoke();
            return SubmitResult.Wrong;
        }

        inputs.Add(leverNumber);

        if (inputs.Count < LeverCount)
        {
            Debug.Log($"[IsolationRescue] 정답 입력 {inputs.Count}/{LeverCount}.");
            onPuzzleChanged.Invoke();
            return SubmitResult.Correct;
        }

        CompleteStage();
        return SubmitResult.StageCompleted;
    }

    private void CompleteStage()
    {
        int finished = StageIndex;
        Debug.Log($"[IsolationRescue] 단계 {finished + 1} 완료.");
        stages[finished].onCompleted.Invoke();

        if (finished + 1 < stages.Length)
        {
            // 다음 단계는 그 단계의 기본 정답 순서에서 시작한다(순환 횟수는 단계마다 0부터).
            StageIndex = finished + 1;
            QuestionVersion = 0;
            inputs.Clear();
            RefreshOrder();
            onPuzzleChanged.Invoke();
            return;
        }

        inputs.Clear();
        Current = State.AwaitFinalRelease;
        RefreshOrder();
        Debug.Log("[IsolationRescue] 모든 단계 완료 — 최종 해제 대기(외부 스위치 ON + 내부 버튼).");
        onPuzzleChanged.Invoke();
        onFinalReleaseAwaiting.Invoke();
    }

    // ── 최종 해제 ─────────────────────────────────────────────────────

    /// <summary>외부 유지 스위치(C2_POWER) 상태를 반영한다. 최종 해제의 전제 조건 중 하나.</summary>
    public void SetPowerHold(bool on)
    {
        PowerOn = on;
    }

    /// <summary>
    /// 내부 최종 해제 버튼(C2_RELEASE). 최종 해제 대기 상태에서만 유효하고, 마감 전이며 외부 스위치가
    /// 켜져 있어야 성공한다. 조건이 모자라면 거부만 하고 진행은 보존한다(C2-08). 입력 시각이 마감과
    /// 정확히 같으면 시간 초과만 발생한다(C2-06).
    /// </summary>
    public bool TryRelease()
    {
        CheckDeadline();
        if (Current != State.AwaitFinalRelease) return false;

        if (!PowerOn)
        {
            Debug.Log("[IsolationRescue] 외부 스위치가 꺼져 있어 해제를 거부한다(진행 보존).");
            return false;
        }

        Timer.Stop();
        Current = State.Success;
        RefreshOrder();
        Debug.Log("[IsolationRescue] 최종 해제 성공 — 숏컷 권한 부여.");
        onSuccess.Invoke();
        onPuzzleChanged.Invoke();
        return true;
    }

    // ── 시간 초과 / 중단 / 재시작 ─────────────────────────────────────

    /// <summary>진행 또는 최종 해제 대기 중 마감 시각에 도달했으면 시간 초과로 전이한다.</summary>
    public void CheckDeadline()
    {
        if (Current != State.InProgress && Current != State.AwaitFinalRelease) return;
        if (Timer == null || !Timer.HasExpired) return;

        Timer.Stop();
        Current = State.TimedOut;
        RefreshOrder();
        Debug.Log("[IsolationRescue] 시간 초과 — 입력 잠금, 비상문 개방, CH3 경로 합류(숏컷 없음).");
        onTimedOut.Invoke();
        onPuzzleChanged.Invoke();
    }

    /// <summary>
    /// 준비/진행 중 중단(타이머 정지·문/비상통로 개방·보상 없음). 그 밖의 상태에서는 무시한다(§4).
    /// 참가자 이탈을 이 창구로 연결할지는 팀 결정 대기 — 클래스 상단 주석 참고.
    /// </summary>
    public void Abort(string reason)
    {
        if (Current != State.Preparing && Current != State.InProgress) return;

        Timer?.Stop();
        Current = State.Aborted;
        RefreshOrder();
        Debug.Log($"[IsolationRescue] 중단: {reason}");
        onAborted.Invoke();
        onPuzzleChanged.Invoke();
    }

    /// <summary>
    /// 전체 재시작(§ 전체 재시작 처리). 새 입력·타이머를 멈추고 시도 번호를 올려 이전 시도의
    /// 지연 입력을 구분할 수 있게 한 뒤, 이 챕터 상태를 초기값으로 되돌린다. 이동 제어 해제·안전
    /// 위치 이동 등 씬 쪽 복원은 onRestarted에 배선한다.
    /// </summary>
    public void FullRestart()
    {
        Timer?.Stop();
        // 상태를 먼저 대기로 돌린 뒤 지운다 — 순서가 반대면 RefreshOrder가 "아직 진행 중"으로 보고
        // 이전 시도의 정답 순서를 캐시에 남긴다.
        Current = State.Idle;
        ClearRound();
        Attempt++;
        Debug.Log($"[IsolationRescue] 전체 재시작 — 시도 #{Attempt}.");
        onRestarted.Invoke();
        onPuzzleChanged.Invoke();
    }

    // ── 내부 ──────────────────────────────────────────────────────────

    private void ClearRound()
    {
        InsidePlayer = null;
        doorSafe = false;
        readyInside = false;
        readyOutside = false;
        // PowerOn은 일부러 지우지 않는다: 물리 유지 스위치(C2_POWER)가 바뀔 때만 SetPowerHold로
        // 들어오는 "현재 스위치 상태"의 거울이라, 재시작이 꺼 버리면 실제 스위치는 켜져 있는데
        // 컨트롤러만 꺼진 것으로 어긋난다.
        StageIndex = 0;
        QuestionVersion = 0;
        inputs.Clear();
        RefreshOrder();
    }

    // 현재 단계의 기본 정답 순서를 QuestionVersion만큼 왼쪽으로 순환한 값을 캐시에 쓴다.
    // 진행 중이 아니면 0으로 채워 표시판이 옛 값을 보여주지 않게 한다.
    private void RefreshOrder()
    {
        bool active = (Current == State.InProgress) && stages != null && StageIndex < stages.Length;
        for (int i = 0; i < LeverCount; i++)
        {
            orderCache[i] = active
                ? stages[StageIndex].answerOrder[(i + QuestionVersion) % LeverCount]
                : 0;
        }
    }
}
