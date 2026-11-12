using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 관리자 열쇠(C8_KEY_POINT) — 수면 확정 뒤 한 번 노출되고, E 탭(중앙 입력)으로 팀 열쇠 1개를 얻는다.
/// 물리로 굴리지 않는 고정 표시물이라 "사라진 표시를 지정점에 복원" 규칙이 필요 없다(떨어지거나 밀리지 않는다).
/// 획득은 팀 상태라 개인 복귀로 잃지 않고, 챕터 재시작(ChapterReset)만 숨김·미획득으로 되돌린다.
/// 출구 조건은 onKeyTaken 또는 HasKey를 읽어 레벨에서 배선한다.
/// </summary>
public class ManagerKeyPoint : MonoBehaviour, IInteractionProvider
{
    [Tooltip("조작 중인 참가자 조회와 챕터 재시작 신호를 받을 매니저.")]
    public RoleAssignmentManager manager;

    [Tooltip("열쇠 시각물. 노출 전·획득 후엔 꺼진다.")]
    public GameObject visual;

    [Tooltip("획득 가능 반경(U).")]
    public float pickupRadius = 1.5f;

    [Tooltip("열쇠 획득 시(시도당 1회). 출구 조건 배선용.")]
    public UnityEvent onKeyTaken = new UnityEvent();

    public bool Revealed { get; private set; }
    /// <summary>팀이 열쇠를 가졌는가.</summary>
    public bool HasKey { get; private set; }

    private bool bound;
    private System.Action onInteract;

    private void Awake() => onInteract = () => TryTake(manager != null ? manager.ControlledPlayer() : null);

    private void OnEnable()
    {
        InteractionController.Register(this);
        if (bound || manager == null) return;
        bound = true;
        manager.ChapterReset += ResetKey;
    }

    private void OnDisable()
    {
        InteractionController.Unregister(this);
        if (!bound) return;
        bound = false;
        if (manager != null) manager.ChapterReset -= ResetKey;
    }

    private void Start() => RefreshVisual();

    /// <summary>수면 확정 시 1회 노출. 이미 노출·획득했으면 아무것도 하지 않는다(열쇠 중복 생성 없음).</summary>
    public void Reveal()
    {
        if (Revealed || HasKey) return;
        Revealed = true;
        RefreshVisual();
        LokiTelemetry.Event("ch8_key_reveal");
    }

    public void ResetKey()
    {
        Revealed = false;
        HasKey = false;
        RefreshVisual();
        LokiTelemetry.Event("ch8_key_reset");
    }

    public bool TryTake(PlayerMover p)
    {
        if (!Revealed || HasKey || !ManagerAgent.IsAlive(p)) return false;
        if (manager != null && manager.IsPaused) return false; // 재접속 대기 중엔 팀 진행을 바꾸지 않는다.
        if (Vector3.Distance(p.transform.position, transform.position) > pickupRadius) return false;
        HasKey = true;
        RefreshVisual();
        Debug.Log("[Manager] 팀 열쇠 획득.", this);
        LokiTelemetry.Event("ch8_key_taken", $"by={p.name}");
        onKeyTaken.Invoke();
        return true;
    }

    // 키를 직접 읽지 않는다 — 가능할 때만 E 탭 액션을 중앙 입력에 올린다(키맵 통합안 §2-1).
    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        if (!Revealed || HasKey || manager == null || manager.IsPaused) return;
        PlayerMover p = manager.ControlledPlayer();
        if (!ManagerAgent.IsAlive(p)) return;
        float d = Vector3.Distance(p.transform.position, transform.position);
        if (d > pickupRadius) return;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
            verb = "열쇠 줍기", enabled = true,
            priority = InteractionPriority.Panel, distance = d, execute = onInteract,
        });
    }

    private void RefreshVisual()
    {
        if (visual != null) visual.SetActive(Revealed && !HasKey);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
