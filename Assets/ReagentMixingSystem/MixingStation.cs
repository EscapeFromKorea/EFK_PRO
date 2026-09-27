using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 가상 시약 혼합대(실험실 CH8) — 재료 선택·투입·비우기·오조합 판정·완성물 권한.
/// docs/PRD/ReagentMixing.md. RoleSlot(roleId "Mixer")에 붙는 패널이고 QuizTerminal과 같은 관례를 따른다.
///
/// [선택과 투입은 두 단계]
/// 1~3은 강조만 하고 Enter를 눌러야 한 번 들어간다. 판정은 마지막 칸까지 채운 투입에서 한 번만 하고, 정답 순서와
/// 다르면 버퍼·선택을 전부 비우고 "조합이 맞지
/// 않습니다. 혼합물을 비웠습니다"를 띄운다. 재료는 무제한 재사용이고 책 내용은 건드리지 않는다.
///
/// [조합식은 시도마다 랜덤 — 2026-09-26 사용자 결정]
/// 시도 = 챕터 1회 도전(처음 1, 잡혀서 ChapterReset될 때마다 +1). 오조합은 같은 시도 안이라 조합식이 그대로다.
/// 새 시도마다 재료 전체를 섞어 직전 시도와 다른 순서를 만들고, 책(BookPanel.pages) 본문을 그 순서로 다시 쓴다 —
/// BookPanel은 RoleClueTerminalSystem 소유라 파일은 고치지 않고 public 필드에 값만 넣는다. 완성물 이름의 번호
/// (안정화 용액 S-01 → S-02 …)도 시도 횟수를 따른다.
///
/// [완성물 권한 = 이 컴포넌트 안의 카운터 — TeamCompletionToken을 따로 만들지 않았다]
/// PRD는 별도 컴포넌트를 예상했지만 그 권한은 "팀 공유, 운반·분실 없음, ChapterReset에 0"인 순수 카운터라
/// 혼합대가 들고 있는 편이 진실이 한 벌로 남는다. 소비는 ConsumeToken() 하나 — 관리자 쪽 컨트롤러가
/// 수면 확정과 같은 자리에서 부른다. 소비 후 병 표시는 "사용 완료"로 남는다(병 복제·삭제 없음, M-05).
///
/// [혼합대는 관리자를 모른다]
/// 감시 시작은 관리자 쪽 컨트롤러가 구역 진입으로 스스로 정한다(2026-09-26 — 예전 "[Space] 실험 시작" 패널 액션
/// 훅은 쓰는 곳이 없어져 걷어냈다). 혼합대는 완성물 권한(HasToken/ConsumeToken)만 내놓는다.
///
/// [새 시도에서 옛 완성물 — 시도 번호를 두지 않았다]
/// 팀 실패(잡힘)는 같은 프레임 안에서 RoleAssignmentManager.ResetChapter → ChapterReset → ResetAll로 권한을
/// 0으로 만든다. 옛 시도의 완성물이 새 시도까지 살아남을 경로가 없어 시도 번호 비교는 죽은 코드가 된다.
///
/// [화면 정보 격리]
/// 재료 이름·색·표식과 이미 투입한 목록만 그린다. recipe는 판정에만 쓰고 화면에 절대 그리지 않는다.
/// </summary>
public class MixingStation : MonoBehaviour, IParticipantPauseReceiver
{
    [Serializable]
    public class Reagent
    {
        [Tooltip("판정용 ID(예: R_L). recipe가 이 값을 참조한다.")]
        public string id;
        [Tooltip("혼합대 화면 표시 이름.")]
        public string displayName;
        [Tooltip("화면 표시 색.")]
        public Color color = Color.white;
        [Tooltip("책 본문에 쓰는 색 이름(예: 파랑). 책 담당은 이름이 아니라 색·표식으로 전달받는다.")]
        public string colorName;
        [Tooltip("표식(모양) 이름. 책 담당에게 말로 전달하는 단서다.")]
        public string mark;
    }

    public enum MixResult
    {
        Rejected,   // 상태 변화 없음(권한 없음·선택 없음·정지 중·완성 후 잠금)
        Added,      // 정답 재료 — 버퍼에 추가
        Wrong,      // 오조합 — 버퍼·선택 전부 비움
        Completed   // 마지막 정답 — 완성 + 권한 1개
    }

    [Tooltip("이 혼합대가 속한 역할 사물(roleId \"Mixer\"). 화면 열림·사용자 판별을 여기서 읽는다.")]
    public RoleSlot slot;

    [Header("콘텐츠 (PRD §6 미확정 구성안 — 데이터로만 교체)")]
    [Tooltip("재료 목록. 1~9 키가 이 순서에 대응한다.")]
    public Reagent[] reagents =
    {
        new Reagent { id = "R_L", displayName = "루멘", color = new Color(0.25f, 0.45f, 1f), colorName = "파란", mark = "삼각형" },
        new Reagent { id = "R_N", displayName = "노바", color = new Color(1f, 0.55f, 0.1f), colorName = "주황", mark = "원" },
        new Reagent { id = "R_V", displayName = "베일", color = Color.white, colorName = "흰", mark = "사각형" },
    };

    [Tooltip("조합법을 보여 줄 책. 시도마다 본문을 새 조합식으로 다시 쓴다. 비우면 책은 건드리지 않는다.")]
    public BookPanel book;

    [Tooltip("완성물 표시 이름 앞부분. 뒤에 시도 번호가 붙는다(예: 안정화 용액 S-01).")]
    public string productName = "안정화 용액 S-";

    [Tooltip("완성물 ID 앞부분(로그용). 뒤에 시도 번호가 붙는다(예: SEDATIVE_S01).")]
    public string resultIdPrefix = "SEDATIVE_S";

    /// <summary>현재 시도 번호(1부터). 잡혀서 챕터가 재시작될 때마다 1씩 오른다.</summary>
    public int Attempt { get; private set; }
    public string ProductLabel => $"{productName}{Attempt:00}";
    public string ResultId => $"{resultIdPrefix}{Attempt:00}";
    /// <summary>이번 시도의 정답 순서(재료 ID). 판정에만 쓰고 혼합대 화면에는 노출하지 않는다.</summary>
    public IReadOnlyList<string> Recipe => recipe;

    private string[] recipe = new string[0];

    [Tooltip("완성 병 시각물(선택). 완성 상태 동안 켜진다. 비우면 패널 표시만 한다.")]
    public GameObject bottleVisual;

    [Header("입력 키")]
    public KeyCode submitKey = KeyCode.Return;
    public KeyCode clearKey = KeyCode.Backspace;

    [Header("이벤트 — 레벨/연출 배선용")]
    [Tooltip("완성 시(시도당 1회).")]
    public UnityEvent onCompleted = new UnityEvent();
    [Tooltip("오조합으로 비웠을 때.")]
    public UnityEvent onWrongMix = new UnityEvent();
    [Tooltip("완성물 권한이 소비됐을 때(관리자 수면 수락).")]
    public UnityEvent onTokenConsumed = new UnityEvent();

    /// <summary>현재 미제출 선택(reagents 인덱스). 없으면 -1.</summary>
    public int SelectedIndex { get; private set; } = -1;
    public IReadOnlyList<int> Buffer => buffer;
    public bool IsComplete { get; private set; }
    /// <summary>팀 공유 완성물 권한 보유 여부(최대 1개).</summary>
    public bool HasToken { get; private set; }
    /// <summary>권한을 이미 소비했다(병 표시 "사용 완료").</summary>
    public bool TokenUsed { get; private set; }
    public bool ParticipantPaused { get; private set; }
    public string LastFeedback { get; private set; }

    private readonly List<int> buffer = new List<int>();
    private bool bound;

    private void OnEnable() => Bind();
    private void OnDisable() => Unbind();

    private void Start()
    {
        RefreshBottle();
        if (Attempt == 0) NewAttempt(); // ChapterReset이 먼저 왔다면 이미 만들어져 있다.
    }

    /// <summary>새 시도: 번호 +1, 재료 전체를 섞어 직전과 다른 조합식을 만들고 책 본문을 다시 쓴다.</summary>
    public void NewAttempt()
    {
        Attempt++;
        string[] prev = recipe;
        int n = reagents != null ? reagents.Length : 0;
        string[] next = new string[n];
        // ponytail: 재료가 1개면 순열이 하나뿐이라 "직전과 다름"을 만족할 수 없다 — 그때만 같은 순서를 허용한다.
        for (int tries = 0; tries < 20; tries++)
        {
            for (int i = 0; i < n; i++) next[i] = reagents[i].id;
            for (int i = n - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (next[i], next[j]) = (next[j], next[i]);
            }
            if (n < 2 || !SameOrder(prev, next)) break;
        }
        recipe = next;
        WriteBook();
        LokiTelemetry.Event("ch8_mix_recipe", $"attempt={Attempt} product={ProductLabel} recipe={string.Join(",", recipe)}");
    }

    private static bool SameOrder(string[] a, string[] b)
    {
        if (a == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private Reagent ById(string id)
    {
        foreach (Reagent r in reagents) if (r.id == id) return r;
        return null;
    }

    private string Describe(string id)
    {
        Reagent r = ById(id);
        return r != null ? $"{r.colorName} {r.mark}" : id;
    }

    private static readonly string[] Ordinals = { "첫 번째", "두 번째", "세 번째", "네 번째", "다섯 번째", "여섯 번째", "일곱 번째", "여덟 번째", "아홉 번째" };

    // 책 본문(PRD §6 구성 유지): 1쪽 = 첫 재료, 2쪽 = 나머지 순서 + 틀리면 처음부터, 3쪽 = 사용 안내.
    private void WriteBook()
    {
        if (book == null || recipe.Length == 0) return;
        string rest = "";
        for (int i = 1; i < recipe.Length; i++)
            rest += $"{(i == recipe.Length - 1 ? "마지막" : Ordinals[Mathf.Min(i, Ordinals.Length - 1)])} 재료: {Describe(recipe[i])}.\n";
        book.pages = new[]
        {
            $"{ProductLabel}\n\n첫 번째 재료: {Describe(recipe[0])}.",
            $"{rest}\n순서가 틀리면 처음부터 다시 넣어야 한다.",
            "완성된 용액은 관리자 사용 지점에서 사용해야 한다.\n혼합 완료만으로는 작동하지 않는다.\n\n" +
            "관리자가 잠들면 열쇠를 회수해 출구로 이동하라."
        };
    }

    /// <summary>슬롯 화면 닫힘·이탈·챕터 재시작 신호를 구독한다. 여러 번 불러도 한 번만 걸린다.</summary>
    public void Bind()
    {
        if (bound || slot == null) return;
        bound = true;
        slot.PanelClosed += OnPanelClosed;
        if (slot.manager != null)
        {
            slot.manager.Subscribe(this);
            slot.manager.ChapterReset += ResetAll;
        }
    }

    public void Unbind()
    {
        if (!bound) return;
        bound = false;
        if (slot == null) return;
        slot.PanelClosed -= OnPanelClosed;
        if (slot.manager != null)
        {
            slot.manager.Unsubscribe(this);
            slot.manager.ChapterReset -= ResetAll;
        }
    }

    // 닫기(Esc·Tab·개인 복귀·거리 이탈)는 미제출 선택만 지운다. 버퍼·완성물은 유지(M-03, M-04).
    private void OnPanelClosed(PlayerMover closedBy) => CancelPending();

    void IParticipantPauseReceiver.SetParticipantPaused(bool paused)
    {
        ParticipantPaused = paused;
        if (paused) CancelPending(); // 버퍼는 보존한다(팀 회신).
        LokiTelemetry.Event("ch8_mix_pause", $"paused={paused} buffer={buffer.Count}");
    }

    public void CancelPending() => SelectedIndex = -1;

    /// <summary>챕터 전체 재시작(잡힘 포함): 버퍼·선택·완성 병·권한을 전부 초기화한다(M-07).</summary>
    public void ResetAll()
    {
        buffer.Clear();
        SelectedIndex = -1;
        IsComplete = false;
        HasToken = false;
        TokenUsed = false;
        LastFeedback = null;
        RefreshBottle();
        LokiTelemetry.Event("ch8_mix_reset");
        NewAttempt(); // 챕터 재시작 = 새 시도(조합식·완성물 번호 갱신).
    }

    public bool Select(int reagentIndex, PlayerMover actor)
    {
        if (!CanAct(actor) || IsComplete) return false;
        if (reagents == null || reagentIndex < 0 || reagentIndex >= reagents.Length) return false;
        SelectedIndex = reagentIndex;
        LokiTelemetry.Event("ch8_mix_select", $"actor={actor.name} reagent={reagents[reagentIndex].id}");
        return true;
    }

    /// <summary>선택한 재료를 한 번 투입한다.</summary>
    public MixResult Submit(PlayerMover actor)
    {
        MixResult result = SubmitInner(actor);
        LokiTelemetry.Event("ch8_mix_submit",
            $"actor={(actor != null ? actor.name : "null")} result={result} buffer={buffer.Count}/{(recipe != null ? recipe.Length : 0)} " +
            $"complete={IsComplete} token={HasToken} paused={ParticipantPaused} feedback=\"{LastFeedback}\"");
        return result;
    }

    private MixResult SubmitInner(PlayerMover actor)
    {
        if (!CanAct(actor)) return MixResult.Rejected;
        if (IsComplete)
        {
            LastFeedback = "이미 완성되었습니다.";
            return MixResult.Rejected;
        }
        if (recipe.Length == 0) return MixResult.Rejected;
        if (SelectedIndex < 0)
        {
            LastFeedback = "재료를 선택해주세요.";
            return MixResult.Rejected;
        }

        // 판정은 마지막 칸을 채운 투입에서 한 번만 한다(2026-09-26 사용자 결정) — 중간에 틀린 재료를 넣어도 알려 주지
        // 않아야 "어디서 틀렸는지"를 투입 순서로 역추적할 수 없고, 책 담당과의 대화가 퍼즐의 전부로 남는다.
        buffer.Add(SelectedIndex);
        SelectedIndex = -1;
        if (buffer.Count < recipe.Length)
        {
            LastFeedback = $"투입했습니다 ({buffer.Count}/{recipe.Length}).";
            return MixResult.Added;
        }

        for (int i = 0; i < recipe.Length; i++)
        {
            if (reagents[buffer[i]].id == recipe[i]) continue;
            buffer.Clear();
            LastFeedback = "조합이 맞지 않습니다. 혼합물을 비웠습니다";
            onWrongMix.Invoke();
            return MixResult.Wrong;
        }

        // 완성: 상태를 먼저 확정한 뒤 발신한다 — 이후 투입·비우기가 잠겨 권한이 두 번 생기지 않는다.
        IsComplete = true;
        HasToken = true;
        LastFeedback = "완성되었습니다. 관리자 사용 지점에서 사용하세요.";
        RefreshBottle();
        Debug.Log($"[Mixing] '{ResultId}' 완성 — 팀 완성물 권한 1개.", this);
        onCompleted.Invoke();
        return MixResult.Completed;
    }

    /// <summary>버퍼와 선택을 비운다. 완성 후에는 거부한다(병 삭제 없음).</summary>
    public bool Clear(PlayerMover actor)
    {
        if (!CanAct(actor)) return false;
        if (IsComplete)
        {
            LastFeedback = "완성된 혼합물은 비울 수 없습니다.";
            return false;
        }
        buffer.Clear();
        SelectedIndex = -1;
        LastFeedback = "혼합물을 비웠습니다.";
        LokiTelemetry.Event("ch8_mix_clear", $"actor={actor.name}");
        return true;
    }

    /// <summary>완성물 권한을 1개 소비한다(관리자 수면 수락 시). 없으면 false이고 아무것도 바꾸지 않는다.</summary>
    public bool ConsumeToken()
    {
        if (!HasToken) return false;
        HasToken = false;
        TokenUsed = true;
        LokiTelemetry.Event("ch8_mix_token_consumed");
        onTokenConsumed.Invoke();
        return true;
    }

    private bool CanAct(PlayerMover actor)
    {
        if (actor == null || ParticipantPaused) return false;
        return slot != null && slot.IsPanelOpen && slot.PanelUser == actor;
    }

    private void RefreshBottle()
    {
        if (bottleVisual != null) bottleVisual.SetActive(IsComplete);
    }

    // ── 입력 / 표시 ───────────────────────────────────────────────────

    private void Update()
    {
        if (slot == null || slot.manager == null) return;
        PlayerMover local = slot.manager.ControlledPlayer();
        if (!slot.CanShowTo(local)) return;

        int count = reagents != null ? Mathf.Min(reagents.Length, 9) : 0;
        for (int i = 0; i < count; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                Select(i, local);
                return;
            }
        }

        if (Input.GetKeyDown(submitKey)) Submit(local);
        else if (Input.GetKeyDown(clearKey)) Clear(local);
    }

    private void OnGUI()
    {
        if (slot == null || slot.manager == null) return;
        if (!slot.CanShowTo(slot.manager.ControlledPlayer())) return;

        float w = Mathf.Min(560f, Screen.width - 40f);
        Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f - 160f, w, 320f);
        GUI.Box(box, $"혼합대 — {slot.roleId}  (목표: {ProductLabel})");

        float x = box.x + 12f, lw = box.width - 24f, y = box.y + 28f;
        if (ParticipantPaused)
        {
            GUI.Label(new Rect(x, y, lw, 24f), "참가자 재접속 대기 중 — 입력 잠금");
            return;
        }

        Color prev = GUI.contentColor;
        for (int i = 0; reagents != null && i < reagents.Length && i < 9; i++)
        {
            Reagent r = reagents[i];
            GUI.contentColor = r.color;
            string mark = i == SelectedIndex ? "▶ " : "   ";
            GUI.Label(new Rect(x, y, lw, 22f), $"{mark}{i + 1}. {r.displayName} ({r.mark})");
            y += 24f;
        }
        GUI.contentColor = prev;

        int slots = recipe != null ? recipe.Length : 0;
        string progress = "";
        for (int i = 0; i < slots; i++)
            progress += i < buffer.Count ? $"[{reagents[buffer[i]].displayName}] " : "[   ] ";
        GUI.Label(new Rect(x, y + 6f, lw, 22f), $"투입: {progress}");

        if (IsComplete)
            GUI.Label(new Rect(x, y + 30f, lw, 22f), TokenUsed ? $"완성 병: {ProductLabel} 사용 완료" : $"완성 병: {ProductLabel} 보유");

        GUI.Label(new Rect(x, box.yMax - 50f, lw, 22f), $"{LastFeedback}");
        string keys = IsComplete ? "완성 — 투입/비우기 잠김" : "1~3: 선택    Enter: 투입    Backspace: 비우기";
        GUI.Label(new Rect(x, box.yMax - 28f, lw, 22f), $"{keys}    Esc: 사용 종료");
    }
}
