using UnityEngine;

/// <summary>
/// 격리 진입문(C2_ENTRY)의 닫힘 안전검사 — 격리 대상이 확정되면 문을 닫으면서 끼임 여부를 보고, 결과를
/// 컨트롤러에 알린다. docs/PRD/IsolationRescue.md §2.1, §4(대기 → 준비, 준비 → 대기).
///
/// [컨트롤러가 비워 둔 자리]
/// 컨트롤러는 "문이 안전하게 닫혔다/막혔다"는 결과 신호(ReportDoorClosedSafe / ReportDoorBlocked)만 받는다
/// — 클래스 상단 주석이 doorPhysics가 끼임 상태를 공개하지 않아 판정을 못 붙였다고 밝혀 뒀다.
/// 이제 doorPhysics가 IsBlocked/IsAtClosedPosition을 읽기 전용으로 공개하므로 이 컴포넌트가 그 결과를 만든다.
///
/// [판정]
/// - 닫힘 위치에 도달했고 끼어 있지 않다 → 안전: ReportDoorClosedSafe.
/// - 끼임이 blockedGraceSeconds 이상 이어졌다 → 막힘: ReportDoorBlocked(격리 취소, 문 다시 열림).
///   걸어서 지나가는 한순간을 막힘으로 보지 않으려고 유예를 둔다.
/// - closeTimeoutSeconds 안에 닫히지 못했다 → 막힘으로 처리한다(어떤 이유로든 닫히지 않는 문으로 격리를
///   확정하지 않는다).
///
/// [열림이 평상시]
/// 진입문은 격리 전에는 열려 있어야 하므로 시작 시 열린다(doorPhysics는 패드가 눌려야 열리는 문). 취소·중단·
/// 전체 재시작 시 다시 열도록 컨트롤러 이벤트(onCaptureCancelled 등)에 Open을 배선한다.
///
/// [범위]
/// 끼임 감지는 doorPhysics의 플레이어 태그 트리거 기준이다. 상자(InteractionItem)가 문에 끼는 경우는
/// doorPhysics가 보지 않으므로 이 검사도 보지 못한다.
/// </summary>
public class IsolationEntryDoor : MonoBehaviour
{
    public enum Phase { Open, Closing, Closed }

    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    [Tooltip("진입문. 패드 눌림(SetPadPressed)으로 열고 닫는 doorPhysics.")]
    public doorPhysics door;

    [Tooltip("끼임이 이 시간(초) 이상 이어지면 막힘으로 본다.")]
    public float blockedGraceSeconds = 1f;

    [Tooltip("이 시간(초) 안에 닫히지 못하면 막힘으로 본다.")]
    public float closeTimeoutSeconds = 6f;

    public Phase Current { get; private set; } = Phase.Open;

    private float elapsed;
    private float blockedFor;

    private void Start()
    {
        if (door == null)
            Debug.LogWarning($"[IsolationEntryDoor] '{name}'에 doorPhysics가 연결되지 않았다.", this);
        Open();
    }

    private void FixedUpdate()
    {
        if (Current != Phase.Closing || door == null) return;
        Tick(Time.fixedDeltaTime, door.IsBlocked, door.IsAtClosedPosition);
    }

    /// <summary>문을 닫기 시작한다(컨트롤러 onCaptureConfirmed에 배선).</summary>
    public void BeginClose()
    {
        Current = Phase.Closing;
        elapsed = 0f;
        blockedFor = 0f;
        if (door != null) door.SetPadPressed(false);
    }

    /// <summary>문을 연다(취소·중단·재시작에 배선, 시작 시 호출).</summary>
    public void Open()
    {
        Current = Phase.Open;
        elapsed = 0f;
        blockedFor = 0f;
        if (door != null) door.SetPadPressed(true);
    }

    /// <summary>
    /// 닫히는 동안 한 스텝을 평가한다. 물리 상태는 인자로 받아 자가검증이 문 물리 없이 경계를 재현한다.
    /// </summary>
    public void Tick(float deltaTime, bool blocked, bool atClosedPosition)
    {
        if (Current != Phase.Closing) return;

        // 그 사이 컨트롤러가 준비 상태를 벗어났다(취소·중단 등) — 결과를 보고할 대상이 없다.
        if (controller == null || controller.Current != IsolationRescueController.State.Preparing)
        {
            Current = Phase.Open;
            return;
        }

        elapsed += deltaTime;
        blockedFor = blocked ? blockedFor + deltaTime : 0f;

        if (atClosedPosition && !blocked)
        {
            Current = Phase.Closed;
            controller.ReportDoorClosedSafe();
            return;
        }

        if (blockedFor >= blockedGraceSeconds || elapsed >= closeTimeoutSeconds)
        {
            Debug.Log($"[IsolationEntryDoor] 문이 막혔다(끼임 {blockedFor:0.0}초, 경과 {elapsed:0.0}초) — 격리 취소.", this);
            Current = Phase.Open;
            if (door != null) door.SetPadPressed(true);
            controller.ReportDoorBlocked();
        }
    }
}
