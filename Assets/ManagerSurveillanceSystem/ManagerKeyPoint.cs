using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 관리자 열쇠(C8_KEY_POINT) — 수면 확정 뒤 한 번 노출되고, LeftControl로 팀 열쇠 1개를 얻는다.
/// 물리로 굴리지 않는 고정 표시물이라 "사라진 표시를 지정점에 복원" 규칙이 필요 없다(떨어지거나 밀리지 않는다).
/// 획득은 팀 상태라 개인 복귀로 잃지 않고, 챕터 재시작(ChapterReset)만 숨김·미획득으로 되돌린다.
/// 출구 조건은 onKeyTaken 또는 HasKey를 읽어 레벨에서 배선한다.
/// </summary>
public class ManagerKeyPoint : MonoBehaviour
{
    [Tooltip("조작 중인 참가자 조회와 챕터 재시작 신호를 받을 매니저.")]
    public RoleAssignmentManager manager;

    [Tooltip("열쇠 시각물. 노출 전·획득 후엔 꺼진다.")]
    public GameObject visual;

    public KeyCode takeKey = KeyCode.LeftControl;
    [Tooltip("획득 가능 반경(U).")]
    public float pickupRadius = 1.5f;

    [Tooltip("열쇠 획득 시(시도당 1회). 출구 조건 배선용.")]
    public UnityEvent onKeyTaken = new UnityEvent();

    public bool Revealed { get; private set; }
    /// <summary>팀이 열쇠를 가졌는가.</summary>
    public bool HasKey { get; private set; }

    private bool bound;

    private void OnEnable()
    {
        if (bound || manager == null) return;
        bound = true;
        manager.ChapterReset += ResetKey;
    }

    private void OnDisable()
    {
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
        if (Vector3.Distance(p.transform.position, transform.position) > pickupRadius) return false;
        HasKey = true;
        RefreshVisual();
        Debug.Log("[Manager] 팀 열쇠 획득.", this);
        LokiTelemetry.Event("ch8_key_taken", $"by={p.name}");
        onKeyTaken.Invoke();
        return true;
    }

    private void Update()
    {
        if (!Revealed || HasKey || manager == null || !Input.GetKeyDown(takeKey)) return;
        TryTake(manager.ControlledPlayer());
    }

    private void RefreshVisual()
    {
        if (visual != null) visual.SetActive(Revealed && !HasKey);
    }

    private void OnGUI()
    {
        if (!Revealed || HasKey || manager == null) return;
        PlayerMover p = manager.ControlledPlayer();
        if (p == null || Vector3.Distance(p.transform.position, transform.position) > pickupRadius) return;
        GUI.Label(new Rect((Screen.width - 420f) * 0.5f, Screen.height - 140f, 420f, 24f), $"[{takeKey}] 열쇠 줍기");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
