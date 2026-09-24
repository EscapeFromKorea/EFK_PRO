using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 문답 단말(CH7 전용) — 컴퓨터 사물의 문제·선택지·제출·정오답 판정·문항 진행.
/// docs/PRD/RoleClueTerminal.md §2.3~2.5, §3, §4, 팀 회신 2.
///
/// [선택과 제출은 두 단계]
/// 선택만으로는 제출되지 않는다. 정답이면 다음 문항(미선택 상태로 시작), 오답이면 선택만 지우고 같은
/// 문제를 유지한다. 마지막 정답이면 전체 성공을 정확히 한 번 발신한다.
///
/// [사용자 검증 — 컴퓨터가 아닌 역할의 제출 거부(R-04)]
/// 선택/제출은 이 사물의 화면을 열어 둔 참가자(slot.PanelUser)만 할 수 있다. 다른 참가자의 시도는 상태를
/// 바꾸지 않고 거부한다.
///
/// [전력 게이트 — 제출만 잠근다(§2.4, R-06/R-07)]
/// SetSubmitGate(false)면 제출이 거부될 뿐 오답 처리가 되지 않고 선택도 유지된다. 이미 맞힌 문항은
/// 정답 수락 시점에 저장(CurrentIndex 전진)되어 이후 게이트가 닫혀도 취소되지 않는다.
///
/// [정답 수락 = 저장 + 발신이 한 번에 — 중복 방지]
/// 정답이 수락되면 CurrentIndex를 먼저 올린 뒤 이벤트를 발신한다. 같은 문항은 다시 제출할 수 없으므로
/// (이미 지나갔다) 문 열림·보상 같은 결과가 두 번 발생하지 않는다. 네트워크 연결이 정답 연출 도중 끊기는
/// 상황은 넷코드 전제가 정해진 뒤 별도로 다룬다 — 이 컴포넌트는 로컬 상태 저장까지가 범위다.
///
/// [참가자 이탈 — 진행 중 단계는 처음부터, 완료 단계는 보존(회신 2)]
/// 이탈(SetParticipantPaused(true)) 시 미제출 선택을 지우고 입력을 막는다. 문항 진행(CurrentIndex)은
/// 그대로라 "완료한 단계는 보존하고 진행 중이던 단계는 처음부터"가 성립한다. 예: 6문제 중 4번까지
/// 맞히고 5번을 풀다 이탈하면 1~4번은 유지되고 5번을 미선택 상태에서 다시 시작한다.
///
/// [정보 격리 — 정답 데이터는 화면에 그리지 않는다(§4)]
/// 화면은 문제와 선택지만 그리고 correctIndex는 어디에도 노출하지 않는다. 그리는 조건은
/// RoleSlot.CanShowTo(조작 중인 참가자)다.
///
/// [입력은 키보드]
/// 숫자 키로 선택, Enter로 제출. 커서 잠금 때문에 OnGUI 버튼은 평소에 누를 수 없다(BookPanel 주석 참고).
/// </summary>
public class QuizTerminal : MonoBehaviour, IParticipantPauseReceiver
{
    [Serializable]
    public class Question
    {
        [TextArea(2, 5)] public string prompt;
        public string[] choices = new string[0];

        [Tooltip("정답 선택지 인덱스(0부터). 화면에는 절대 노출하지 않는다.")]
        public int correctIndex;
    }

    public enum SubmitResult
    {
        Rejected,       // 상태 변화 없음(권한 없음, 선택 없음, 정전, 정지 중, 이미 끝남)
        Wrong,          // 오답 — 선택 초기화, 같은 문제 유지
        Correct,        // 정답 — 다음 문항으로
        AllCleared      // 마지막 정답 — 전체 성공
    }

    [Tooltip("이 문답 단말이 속한 역할 사물(컴퓨터). 화면 열림·사용자 판별을 여기서 읽는다.")]
    public RoleSlot slot;

    [Tooltip("문항 목록. 실제 문항·정답(CH7 Q1~Q3)은 '콘텐츠 구성안(미확정)'이라 씬/데이터에서 채운다.")]
    public Question[] questions = new Question[0];

    [Header("입력 키")]
    public KeyCode submitKey = KeyCode.Return;

    [Header("이벤트 — 레벨/연출 배선용")]
    [Tooltip("문항 하나를 맞혔을 때. 인자 = 맞힌 문항 인덱스. 문 열림·게이지 보상 등에 건다.")]
    public UnityEvent<int> onQuestionCleared = new UnityEvent<int>();

    [Tooltip("모든 문항을 맞혔을 때(전체 성공, 정확히 1회).")]
    public UnityEvent onAllCleared = new UnityEvent();

    [Tooltip("오답 제출 시.")]
    public UnityEvent onWrongAnswer = new UnityEvent();

    /// <summary>지금 풀고 있는 문항(0부터). 맞힌 문항은 이 값이 전진한 것으로 저장된다.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>현재 문항에서 선택한(미제출) 선택지. 없으면 -1.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public bool AllCleared { get; private set; }

    /// <summary>외부 전력 유지 장치가 발신하는 제출 게이트. 기본은 열림.</summary>
    public bool SubmitGateOpen { get; private set; } = true;

    /// <summary>참가자 이탈로 입력이 막혀 있는가.</summary>
    public bool ParticipantPaused { get; private set; }

    /// <summary>화면에 표시할 마지막 피드백("정답입니다" / "자료를 다시 확인하세요" 등).</summary>
    public string LastFeedback { get; private set; }

    private bool bound;

    private void OnEnable()
    {
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    /// <summary>슬롯의 화면 닫힘 신호와 매니저의 이탈 신호를 구독한다. OnEnable에서 자동으로 부르며,
    /// 여러 번 불러도 한 번만 걸린다(Editor 자가검증이 OnEnable 없이 직접 부르기도 한다).</summary>
    public void Bind()
    {
        if (bound) return;
        bound = true;
        if (slot != null)
        {
            slot.PanelClosed += OnPanelClosed;
            if (slot.manager != null) slot.manager.Subscribe(this);
        }
    }

    public void Unbind()
    {
        if (!bound) return;
        bound = false;
        if (slot != null)
        {
            slot.PanelClosed -= OnPanelClosed;
            if (slot.manager != null) slot.manager.Unsubscribe(this);
        }
    }

    private void OnPanelClosed(PlayerMover closedBy)
    {
        // Esc 사용 종료·Tab 전환·개인 복귀·거리 이탈·이탈 모두 미제출 선택만 취소한다. 맞힌 문항은 유지.
        CancelPending();
    }

    /// <summary>제출 게이트를 바꾼다(전력 유지 장치가 호출). 닫혀도 이미 맞힌 문항은 취소되지 않는다.</summary>
    public void SetSubmitGate(bool open)
    {
        SubmitGateOpen = open;
    }

    void IParticipantPauseReceiver.SetParticipantPaused(bool paused)
    {
        ParticipantPaused = paused;
        if (paused) CancelPending(); // 진행 중 단계는 처음부터 — 미제출 선택을 지운다.
    }

    /// <summary>미제출 선택을 취소한다. 맞힌 문항 진행에는 영향 없다.</summary>
    public void CancelPending()
    {
        SelectedIndex = -1;
    }

    /// <summary>챕터 종료/전체 재시작 시 첫 문항으로 초기화한다(§2.7).</summary>
    public void ResetProgress()
    {
        CurrentIndex = 0;
        SelectedIndex = -1;
        AllCleared = false;
        LastFeedback = null;
    }

    /// <summary>선택지를 고른다(제출은 아니다). 이 사물의 화면을 연 참가자만, 정지 중이 아닐 때만 가능.</summary>
    public bool Select(int choiceIndex, PlayerMover actor)
    {
        if (!CanAct(actor)) return false;
        Question q = CurrentQuestion();
        if (q == null || q.choices == null || choiceIndex < 0 || choiceIndex >= q.choices.Length) return false;

        SelectedIndex = choiceIndex;
        return true;
    }

    /// <summary>선택한 답을 제출한다. 권한/선택/게이트/정지 조건을 통과해야 판정한다.</summary>
    public SubmitResult Submit(PlayerMover actor)
    {
        if (!CanAct(actor)) return SubmitResult.Rejected;

        Question q = CurrentQuestion();
        if (q == null || SelectedIndex < 0) return SubmitResult.Rejected;

        if (!SubmitGateOpen)
        {
            // 정전 중: 오답 처리 없이 제출 자체를 막는다(R-06). 선택은 유지한다.
            LastFeedback = "전력이 부족해 제출할 수 없다.";
            return SubmitResult.Rejected;
        }

        if (SelectedIndex != q.correctIndex)
        {
            SelectedIndex = -1;
            LastFeedback = "자료를 다시 확인하세요.";
            onWrongAnswer.Invoke();
            return SubmitResult.Wrong;
        }

        // 정답 수락: 진행(저장)을 먼저 확정한 뒤 이벤트를 낸다 — 같은 문항의 재제출·중복 발신을 막는다.
        int cleared = CurrentIndex;
        CurrentIndex++;
        SelectedIndex = -1;
        LastFeedback = "정답입니다.";
        onQuestionCleared.Invoke(cleared);

        if (CurrentIndex >= questions.Length)
        {
            AllCleared = true;
            onAllCleared.Invoke();
            return SubmitResult.AllCleared;
        }

        return SubmitResult.Correct;
    }

    private bool CanAct(PlayerMover actor)
    {
        if (actor == null || AllCleared || ParticipantPaused) return false;
        return slot != null && slot.IsPanelOpen && slot.PanelUser == actor;
    }

    private Question CurrentQuestion()
    {
        if (questions == null || CurrentIndex < 0 || CurrentIndex >= questions.Length) return null;
        return questions[CurrentIndex];
    }

    // ── 입력 / 표시 ───────────────────────────────────────────────────

    private void Update()
    {
        if (slot == null || slot.manager == null) return;
        PlayerMover local = slot.manager.ControlledPlayer();
        if (!slot.CanShowTo(local)) return;

        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                Select(i, local);
                return;
            }
        }

        if (Input.GetKeyDown(submitKey)) Submit(local);
    }

    private void OnGUI()
    {
        if (slot == null || slot.manager == null) return;
        if (!slot.CanShowTo(slot.manager.ControlledPlayer())) return;

        float w = Mathf.Min(560f, Screen.width - 40f);
        Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f - 150f, w, 300f);
        GUI.Box(box, $"문답 단말 — {slot.roleId}");

        float y = box.y + 28f;
        if (ParticipantPaused)
        {
            GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 24f), "참가자 재접속 대기 중 — 입력 잠금");
            return;
        }

        Question q = CurrentQuestion();
        if (q == null)
        {
            GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 24f),
                AllCleared ? "모든 문항을 완료했습니다." : "(문항 없음 — 콘텐츠 미확정)");
            return;
        }

        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 60f), $"[{CurrentIndex + 1}/{questions.Length}] {q.prompt}");
        y += 64f;

        for (int i = 0; i < q.choices.Length && i < 9; i++)
        {
            string mark = i == SelectedIndex ? "▶ " : "   ";
            GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f), $"{mark}{i + 1}. {q.choices[i]}");
            y += 24f;
        }

        string submitHint = SubmitGateOpen ? "Enter: 제출" : "전력 부족 — 제출 불가";
        GUI.Label(new Rect(box.x + 12f, box.yMax - 50f, box.width - 24f, 22f), $"{LastFeedback}");
        GUI.Label(new Rect(box.x + 12f, box.yMax - 28f, box.width - 24f, 22f), $"1~9: 선택    {submitHint}    Esc: 사용 종료");
    }
}
