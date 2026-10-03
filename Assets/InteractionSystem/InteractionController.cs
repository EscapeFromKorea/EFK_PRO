using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 손(E)·장비(V) 키를 저장소에서 "한 번만" 읽는 중앙 입력. 기믹(<see cref="IInteractionProvider"/>)은
/// 지금 가능한 액션만 올리고, 이 컨트롤러가 탭/홀드를 판정해 후보 중 하나만 실행한다
/// (키맵 통합안 §3 입력 소비 규칙). 제공자를 매 프레임 조회(pull)하므로 스크립트 실행 순서에 의존하지
/// 않는다.
///
/// [붙잡힘 판정] 조작 대상이 <c>ExternallyDriven || isKinematic || InputLocked</c>이면 액션 수집과 실행을
/// 막고 진행 중인 홀드는 취소한다. 소유 신호가 셋인 이유는 Assets/CLAUDE.md "소유 신호가 하나가 아니다".
///
/// [프롬프트] 저장소에 UI 시스템이 없어 <c>OnGUI</c>로 임시 표시한다(리스폰과 같은 방식). 규격은
/// UI 담당과 합의 후 교체한다(통합안 §7 4단계).
///
/// [씬에 안 놔도 동작] 없으면 RuntimeInitializeOnLoadMethod가 기본값 인스턴스를 만든다. 씬 무수정.
/// </summary>
[DisallowMultipleComponent]
public class InteractionController : MonoBehaviour
{
    [Header("입력")]
    public KeyCode handKey = KeyCode.E;
    public KeyCode gearKey = KeyCode.V;
    [Tooltip("이 시간(초) 이상 누르면 홀드로 확정. E·V 공통, 임시 1초 — 플레이테스트에서 길게 느껴지면 줄인다.")]
    public float holdSeconds = 1f;

    [Header("프롬프트")]
    public float toastSeconds = 2f;

    private static readonly List<IInteractionProvider> providers = new List<IInteractionProvider>();
    private static InteractionController instance;

    public static void Register(IInteractionProvider p)
    {
        if (p != null && !providers.Contains(p)) providers.Add(p);
    }

    public static void Unregister(IInteractionProvider p) => providers.Remove(p);

    /// <summary>지금 조작 중인 플레이어(없으면 null). 제공자가 씬을 훑지 않고 쓰는 창구 — 매 프레임 갱신된다.</summary>
    public static PlayerMover Controlled { get; private set; }

    /// <summary>다른 기믹이 이 바디를 붙잡고 있는가(ExternallyDriven ‖ isKinematic ‖ InputLocked).</summary>
    public static bool IsGripped(PlayerMover m)
    {
        if (m == null) return true;
        if (m.ExternallyDriven || m.InputLocked) return true;
        Rigidbody rb = m.GetComponent<Rigidbody>();
        return rb != null && rb.isKinematic;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        providers.Clear();
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Object.FindObjectOfType<InteractionController>() != null) return;
        new GameObject("InteractionController (auto)").AddComponent<InteractionController>();
    }

    private sealed class Channel
    {
        public InteractionChannel id;
        public KeyCode key;
        public readonly HoldTracker tracker = new HoldTracker();
        public PlayerMover heldBy;

        public bool hasTap, hasTapBlocked, hasHold;
        public InteractionAction tap, tapBlocked, hold;

        public void Clear() { hasTap = hasTapBlocked = hasHold = false; }

        public void Offer(in InteractionAction a)
        {
            if (a.trigger == InteractionTrigger.Hold)
            {
                if (!a.enabled) return; // 비활성 홀드는 사유 표시 대상이 아니다(탭 사유만 띄움).
                if (!hasHold || InteractionLogic.IsBetter(a.priority, a.distance, hold.priority, hold.distance))
                { hold = a; hasHold = true; }
            }
            else if (a.enabled)
            {
                if (!hasTap || InteractionLogic.IsBetter(a.priority, a.distance, tap.priority, tap.distance))
                { tap = a; hasTap = true; }
            }
            else if (!hasTapBlocked || InteractionLogic.IsBetter(a.priority, a.distance, tapBlocked.priority, tapBlocked.distance))
            {
                tapBlocked = a; hasTapBlocked = true;
            }
        }
    }

    private readonly Channel[] channels = new Channel[2];
    private readonly List<InteractionAction> buffer = new List<InteractionAction>();

    private PlayerMover[] movers = new PlayerMover[0];
    private float nextMoverRefresh;
    private PlayerMover holder;

    private string toast;
    private float toastUntil;

    // 프롬프트 문자열 캐시 — OnGUI는 프레임당 여러 번 불려서 매번 이어 붙이면 GC가 쌓인다.
    private string tapText, holdText;
    private bool tapGray;
    private string lastTapKey, lastHoldKey;
    private GUIStyle style;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        channels[0] = new Channel { id = InteractionChannel.Hand };
        channels[1] = new Channel { id = InteractionChannel.Gear };
    }

    private void OnDestroy()
    {
        if (instance == this) { instance = null; Controlled = null; }
    }

    private void Update()
    {
        if (instance != this) return;
        channels[0].key = handKey;
        channels[1].key = gearKey;

        holder = FindControlled();
        Controlled = holder;
        bool gripped = holder == null || IsGripped(holder);

        buffer.Clear();
        if (holder != null)
        {
            for (int i = providers.Count - 1; i >= 0; i--)
            {
                if (providers[i] == null) { providers.RemoveAt(i); continue; }
                providers[i].CollectActions(buffer);
            }
        }

        // 붙잡힌 상태에선 해제 계열(allowWhenGripped)만 남긴다 — 탑승·도킹 중인 몸은 isKinematic이라
        // 일반 액션은 막되, 그 상태를 풀 수 있는 키는 살아 있어야 한다.
        if (gripped)
        {
            int w = 0;
            for (int i = 0; i < buffer.Count; i++)
                if (buffer[i].allowWhenGripped) buffer[w++] = buffer[i];
            buffer.RemoveRange(w, buffer.Count - w);
        }

        foreach (Channel ch in channels) Process(ch);
        RefreshPrompt(channels[0]);
    }

    private void Process(Channel ch)
    {
        ch.Clear();
        for (int i = 0; i < buffer.Count; i++)
        {
            InteractionAction a = buffer[i];
            if (a.channel == ch.id) ch.Offer(in a);
        }

        // 홀드를 시작한 도형과 지금 조작 도형이 다르면(Tab) 홀드 대상이 사라진 것으로 본다.
        bool sameHolder = !ch.tracker.IsHolding || holder == ch.heldBy;
        bool holdAvailable = ch.hasHold && sameHolder;

        HoldResult r = ch.tracker.Step(
            Input.GetKeyDown(ch.key), Input.GetKeyUp(ch.key), Input.GetKey(ch.key),
            Time.time, holdSeconds, holdAvailable);

        if (ch.tracker.IsHolding && ch.heldBy == null) ch.heldBy = holder;
        if (!ch.tracker.IsHolding && r != HoldResult.None) ch.heldBy = null;

        switch (r)
        {
            case HoldResult.TapNow:
            case HoldResult.TapOnRelease:
                if (ch.hasTap) ch.tap.execute?.Invoke();
                else if (ch.hasTapBlocked) ShowToast(ch.tapBlocked.reason);
                break;
            case HoldResult.HoldFired:
                ch.hold.execute?.Invoke();
                break;
            case HoldResult.Cancelled:
                ShowToast("길게 누르기가 취소되었습니다.");
                break;
        }
    }

    // 캐시 배열에서 조작 중인 플레이어를 찾는다. 없거나 비어 있으면 0.5초 간격으로만 씬을 다시 훑는다.
    private PlayerMover FindControlled()
    {
        foreach (PlayerMover m in movers)
            if (m != null && m.IsControlled) return m;

        if (Time.time >= nextMoverRefresh)
        {
            movers = Object.FindObjectsOfType<PlayerMover>();
            nextMoverRefresh = Time.time + 0.5f;
            foreach (PlayerMover m in movers)
                if (m != null && m.IsControlled) return m;
        }
        return null;
    }

    private void ShowToast(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        toast = text;
        toastUntil = Time.time + toastSeconds;
    }

    // ── 임시 프롬프트 ─────────────────────────────────────────

    private void RefreshPrompt(Channel hand)
    {
        string tapKey = null;
        if (hand.hasTap) { tapKey = "T" + hand.tap.verb; tapGray = false; }
        else if (hand.hasTapBlocked) { tapKey = "B" + hand.tapBlocked.verb + hand.tapBlocked.reason; tapGray = true; }
        if (tapKey != lastTapKey)
        {
            lastTapKey = tapKey;
            tapText = tapKey == null ? null
                : hand.hasTap ? $"[{hand.key}] {hand.tap.verb}"
                : $"[{hand.key}] {hand.tapBlocked.verb} — {hand.tapBlocked.reason}";
        }

        string holdKey = hand.hasHold ? hand.hold.verb : null;
        if (holdKey != lastHoldKey)
        {
            lastHoldKey = holdKey;
            holdText = holdKey == null ? null : $"[{hand.key} 길게] {hand.hold.verb}";
        }
    }

    private void OnGUI()
    {
        if (holder == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
        }

        float w = 700f, h = 28f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.78f;

        if (tapText != null) DrawLine(x, ref y, w, h, tapText, tapGray ? Color.gray : Color.white);
        if (holdText != null) DrawLine(x, ref y, w, h, holdText, Color.white);

        HoldTracker tr = channels[0] != null ? channels[0].tracker : null;
        if (tr != null && tr.IsHolding)
        {
            float p = tr.Progress(Time.time, holdSeconds);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x + w * 0.3f, y, w * 0.4f, 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x + w * 0.3f, y, w * 0.4f * p, 8f), Texture2D.whiteTexture);
            y += 12f;
        }

        if (toast != null && Time.time < toastUntil)
            DrawLine(x, ref y, w, h, toast, new Color(1f, 0.55f, 0.55f));
        GUI.color = Color.white;
    }

    private void DrawLine(float x, ref float y, float w, float h, string text, Color c)
    {
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(x + 1, y + 1, w, h), text, style);
        style.normal.textColor = c;
        GUI.Label(new Rect(x, y, w, h), text, style);
        y += h;
    }
}
