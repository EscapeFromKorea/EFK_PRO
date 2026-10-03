using UnityEngine;

/// <summary>
/// 준비 확인 버튼(READY_IN / READY_OUT) — 안쪽·바깥쪽이 각자 눌러 둘 다 끝나면 1단계가 시작된다.
/// docs/PRD/IsolationRescue.md §2.3, §4 상태 전이표(준비 → 진행).
///
/// 판정은 <see cref="IsolationRescueController.SetReady"/>가 한다. 이 컴포넌트는 "누른 쪽이 어느 쪽인가"만
/// 전달한다 — 안쪽 버튼은 닫힌 격리실 안에, 바깥쪽 버튼은 바깥에 있어 물리적으로 구분되므로 누른 사람의
/// 신원은 따로 검사하지 않는다.
/// </summary>
public class IsolationReadyButton : IsolationInteractZone
{
    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    [Tooltip("true = 안쪽 준비 확인(READY_IN), false = 바깥쪽 준비 확인(READY_OUT).")]
    public bool inside;

    public override void Press()
    {
        if (controller == null)
        {
            Debug.LogWarning($"[IsolationReadyButton] '{name}'에 컨트롤러가 연결되지 않았다.", this);
            return;
        }

        if (controller.Current != IsolationRescueController.State.Preparing)
        {
            Say(controller.Current == IsolationRescueController.State.Idle
                ? "아직 격리가 시작되지 않았다."
                : "지금은 준비 확인을 받을 수 없다.");
            return;
        }

        controller.SetReady(inside);
    }
}
