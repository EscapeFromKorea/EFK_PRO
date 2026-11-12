using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 관리자 사용 지점(C8_USE, 발신자) — 완성물로 관리자를 재우는 "요청"만 보낸다. docs/PRD/ReagentMixing.md §3.
/// 역할 사물이 아니다: 점유 없이 팀원 누구나 E 탭(중앙 입력)으로 쓴다. 한때 E가 RoleSlot 등과 겹쳐 LeftControl을 썼으나 키맵 통합(E=손 채널)으로
/// E로 돌아왔다 — 아직 이관 전인 RoleSlot이 같은 자리에서 E를 직접 읽으면 둘이 같이 반응하니 한 곳에 겹쳐 두지 않는다.
///
/// [관리자 로직을 모른다]
/// 완성물이 없으면 안내만 하고 요청을 보내지 않는다. 있으면 onUseRequested(요청자)를 발신하고, 수락/거부와 그
/// 사유는 ManagerSurveillanceSystem 쪽 컨트롤러가 판정해 SetFeedback으로 돌려준다. 실제 사용 거리(U) 검증도
/// 그쪽 몫이다 — 여기 반경은 안내·입력을 받을 범위일 뿐이다.
///
/// [관리자의 자식으로 배치 — 트리거가 아니라 거리로 잰다]
/// 관리자와 함께 움직여야 등 뒤로 몰래 다가가 쓰는 구조가 된다. 그런데 관리자는 kinematic Rigidbody라, 여기에
/// 트리거 콜라이더를 달면 관리자 compound의 일부가 되어 트리거 메시지가 관리자 루트에도 간다 — 루트에 잡힘
/// 판정이 있으면 "사용 범위 진입"이 "잡힘"으로 읽힌다. 그래서 콜라이더 없이 조작 중인 참가자와의 거리만 잰다.
/// </summary>
public class ManagerUsePoint : MonoBehaviour, IInteractionProvider
{
    [Tooltip("완성물 보유 여부를 읽을 혼합대. 조작 중인 참가자 조회도 이 혼합대의 RoleAssignmentManager로 한다.")]
    public MixingStation station;

    [Tooltip("안내 표시·입력을 받는 반경(U). 실제 사용 가능 거리는 관리자 쪽 컨트롤러가 따로 검증한다 " +
             "(둘을 같은 값으로 두는 걸 권장).")]
    public float promptRadius = 3f;

    public string promptText = "완성물을 사용해 관리자를 재우기";

    [Tooltip("false면 안내·입력을 모두 끈다(피드백 표시는 유지). 관리자가 잠든 뒤 같은 키를 쓰는 열쇠와 겹치지 " +
             "않게 관리자 쪽 컨트롤러가 끈다.")]
    public bool available = true;

    [Tooltip("사용 요청(완성물이 있을 때만). 인자 = 요청한 참가자.")]
    public UnityEvent<PlayerMover> onUseRequested = new UnityEvent<PlayerMover>();

    public string LastFeedback { get; private set; }

    private float feedbackUntil;
    private System.Action onInteract;

    private void Awake() => onInteract = () => RequestUse(LocalInRange());

    private void OnEnable() => InteractionController.Register(this);

    private void OnDisable() => InteractionController.Unregister(this);

    // 키를 직접 읽지 않는다 — 범위 안이면 E 탭 액션을 올리고, 완성물이 없으면 회색 사유로 보여 준다.
    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        if (!available) return;
        PlayerMover p = LocalInRange();
        if (p == null) return;

        bool has = station != null && station.HasToken;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
            verb = promptText, enabled = has, reason = "완성물이 없습니다.",
            priority = InteractionPriority.Panel,
            distance = Vector3.Distance(p.transform.position, transform.position), execute = onInteract,
        });
    }

    /// <summary>사용 요청 한 번. 완성물이 없으면 안내만 하고 false. 키 입력과 분리해 입력 없이 재현할 수 있다.</summary>
    public bool RequestUse(PlayerMover requester)
    {
        if (!available || requester == null) return false;
        if (station == null || !station.HasToken)
        {
            LokiTelemetry.Event("ch8_use_no_token", $"requester={requester.name}");
            SetFeedback("사용할 완성물이 없습니다.");
            return false;
        }
        LokiTelemetry.Event("ch8_use_request",
            $"requester={requester.name} dist={Vector3.Distance(requester.transform.position, transform.position):F2}");
        onUseRequested.Invoke(requester);
        return true;
    }

    public void SetFeedback(string message)
    {
        LastFeedback = message;
        feedbackUntil = Time.unscaledTime + 2.5f;
    }

    private PlayerMover LocalInRange()
    {
        if (station == null || station.slot == null || station.slot.manager == null) return null;
        PlayerMover p = station.slot.manager.ControlledPlayer();
        if (p == null || Vector3.Distance(p.transform.position, transform.position) > promptRadius) return null;
        return p;
    }

    private void OnGUI()
    {
        Rect r = new Rect((Screen.width - 420f) * 0.5f, Screen.height - 110f, 420f, 24f);
        if (!string.IsNullOrEmpty(LastFeedback) && Time.unscaledTime < feedbackUntil)
            GUI.Label(new Rect(r.x, r.y + 24f, r.width, 24f), LastFeedback);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, promptRadius);
    }
}
