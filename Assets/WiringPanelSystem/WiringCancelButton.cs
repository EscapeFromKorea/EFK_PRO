using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// W_CANCEL — 현재 출력 선택을 취소하는 전용 버튼(트리거). docs/PRD/WiringPanel.md §3.
/// WiringPort와 같은 트리거+키 패턴을 쓰지만 포트가 아니라서 별도 컴포넌트로 둔다(역할이 섞이면
/// WiringPort의 Role enum에 "Cancel"을 끼워 넣는 것보다 이쪽이 더 명확하다).
/// </summary>
public class WiringCancelButton : MonoBehaviour, IInteractionProvider
{
    [Tooltip("취소를 적용할 패널.")]
    public WiringPanel panel;

    private readonly List<PlayerMover> occupants = new List<PlayerMover>();

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover != null && !occupants.Contains(mover)) occupants.Add(mover);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover != null) occupants.Remove(mover);
    }

    private System.Action cancel;

    private void OnEnable() => InteractionController.Register(this);
    private void OnDisable() => InteractionController.Unregister(this);

    // E는 InteractionController가 한 번만 읽는다(키맵 통합안 §3).
    public void CollectActions(List<InteractionAction> into)
    {
        PlayerMover p = InteractionController.Controlled;
        if (p == null || !occupants.Contains(p)) return;
        cancel ??= Cancel;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand,
            trigger = InteractionTrigger.Tap,
            verb = "배선 취소",
            enabled = true,
            priority = InteractionPriority.Panel,
            distance = Vector3.Distance(p.transform.position, transform.position),
            execute = cancel,
        });
    }

    private void Cancel()
    {
        occupants.RemoveAll(m => m == null);

        if (panel == null)
        {
            Debug.LogWarning($"[WiringCancelButton] '{name}'에 WiringPanel이 연결되지 않았다.", this);
            return;
        }

        panel.CancelSelection();
    }

}
