using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 열쇠 문(CH8 출구) — 팀이 열쇠(ManagerKeyPoint.HasKey)를 가진 상태에서 문 앞에서 E를 탭(중앙 입력)하면 열린다.
/// 열쇠는 팀 권한이라 누가 주웠든 팀원 누구나 연다. 열쇠는 소모하지 않는다(문이 하나뿐이라 셀 이유가 없다).
///
/// [문 몸체는 DoorSystem/doorPhysics를 수정 없이 재사용]
/// doorPhysics.SetPadPressed(true)가 "목표 높이로 올린다"는 창구라 그대로 부른다(레버를 비워 두면 평소엔 닫힘 위치).
/// 챕터 재시작(ChapterReset)에서 다시 닫는다 — 열쇠도 같은 신호로 미획득으로 돌아간다.
/// </summary>
public class ManagerKeyDoor : MonoBehaviour, IInteractionProvider
{
    [Tooltip("조작 중인 참가자 조회와 챕터 재시작 신호를 받을 매니저.")]
    public RoleAssignmentManager manager;
    [Tooltip("팀 열쇠 보유 여부를 읽는다.")]
    public ManagerKeyPoint keyPoint;
    [Tooltip("움직일 문 몸체(DoorSystem). Rigidbody가 있어야 doorPhysics가 동작한다.")]
    public doorPhysics door;

    [Tooltip("상호작용 반경(U) — 이 오브젝트 위치 기준.")]
    public float useRadius = 2.5f;

    public UnityEvent onOpened = new UnityEvent();

    public bool IsOpen { get; private set; }

    private string feedback;
    private float feedbackUntil;
    private bool bound;
    private System.Action onInteract;

    private void Awake() => onInteract = () => TryOpen(LocalInRange());

    private void OnEnable()
    {
        InteractionController.Register(this);
        if (bound || manager == null) return;
        bound = true;
        manager.ChapterReset += Close;
    }

    private void OnDisable()
    {
        InteractionController.Unregister(this);
        if (!bound) return;
        bound = false;
        if (manager != null) manager.ChapterReset -= Close;
    }

    // 키를 직접 읽지 않는다 — 범위 안이면 E 탭 액션을 올리고, 열 수 없으면 회색 사유로 보여 준다.
    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        if (IsOpen || manager == null) return;
        PlayerMover p = LocalInRange();
        if (p == null) return;

        string reason = manager.IsPaused ? "참가자 재접속 대기 중입니다."
            : keyPoint == null || !keyPoint.HasKey ? "잠긴 문 — 열쇠가 필요합니다."
            : null;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
            verb = "문 열기", enabled = reason == null, reason = reason,
            priority = InteractionPriority.Panel,
            distance = Vector3.Distance(p.transform.position, transform.position), execute = onInteract,
        });
    }

    /// <summary>문 열기 시도. 열쇠가 없으면 안내만 하고 false.</summary>
    public bool TryOpen(PlayerMover p)
    {
        if (IsOpen || p == null) return false;
        if (manager != null && manager.IsPaused)
        {
            Say("참가자 재접속 대기 중입니다.");
            return false;
        }
        if (keyPoint == null || !keyPoint.HasKey)
        {
            Say("열쇠가 필요합니다.");
            LokiTelemetry.Event("ch8_door_locked", $"by={p.name}");
            return false;
        }

        IsOpen = true;
        if (door != null) door.SetPadPressed(true);
        Say("문이 열렸습니다.");
        LokiTelemetry.Event("ch8_door_open", $"by={p.name}");
        onOpened.Invoke();
        return true;
    }

    public void Close()
    {
        IsOpen = false;
        if (door != null) door.SetPadPressed(false);
        LokiTelemetry.Event("ch8_door_close");
    }

    private PlayerMover LocalInRange()
    {
        PlayerMover p = manager.ControlledPlayer();
        return p != null && Vector3.Distance(p.transform.position, transform.position) <= useRadius ? p : null;
    }

    private void Say(string message)
    {
        feedback = message;
        feedbackUntil = Time.unscaledTime + 2.5f;
    }

    private void OnGUI()
    {
        if (manager == null) return;
        Rect r = new Rect((Screen.width - 420f) * 0.5f, Screen.height - 170f, 420f, 24f);
        if (!string.IsNullOrEmpty(feedback) && Time.unscaledTime < feedbackUntil)
            GUI.Label(new Rect(r.x, r.y + 24f, r.width, 24f), feedback);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, useRadius);
    }
}
