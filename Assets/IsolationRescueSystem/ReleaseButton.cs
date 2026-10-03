using UnityEngine;

/// <summary>
/// 내부 최종 해제 버튼(C2_RELEASE) — 모든 단계를 끝낸 뒤 안쪽 플레이어가 누른다. 바깥 전원 스위치가 켜져
/// 있고 마감 전일 때만 성공한다. docs/PRD/IsolationRescue.md §3, §4.
///
/// 판정은 <see cref="IsolationRescueController.TryRelease"/>가 한다(마감 시각과 같은 순간이면 시간 초과만
/// 발생 — C2-06). 이 컴포넌트는 컨트롤러가 거부했을 때 "왜 안 되는지"를 플레이어에게 알리는 일만 더한다.
/// </summary>
public class ReleaseButton : IsolationInteractZone
{
    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    public override void Press()
    {
        if (controller == null)
        {
            Debug.LogWarning($"[ReleaseButton] '{name}'에 컨트롤러가 연결되지 않았다.", this);
            return;
        }

        // 마감이 이미 지났다면 TryRelease 안의 CheckDeadline이 시간 초과로 돌리므로, 거부 사유는 그 뒤 상태로 판단한다.
        bool released = controller.TryRelease();
        if (released) return;

        switch (controller.Current)
        {
            case IsolationRescueController.State.AwaitFinalRelease:
                Say("바깥 전원이 꺼져 있어 해제할 수 없다. 바깥에서 전원 스위치를 켜 두어야 한다.");
                break;
            case IsolationRescueController.State.InProgress:
                Say("아직 모든 단계를 끝내지 못했다.");
                break;
            case IsolationRescueController.State.TimedOut:
                Say("제한시간이 끝났다.");
                break;
            default:
                Say("지금은 해제할 수 없다.");
                break;
        }
    }
}
