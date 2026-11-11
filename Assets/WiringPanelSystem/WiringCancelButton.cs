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

    // 참가자별 겹친 콜라이더 수 — 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider)를 함께 가져
    // 참가자 단위 목록이면 콜라이더 하나가 빠지는 순간 아직 안에 있는 참가자가 사라진다(WiringPort와 동일).
    private readonly Dictionary<PlayerMover, int> occupants = new Dictionary<PlayerMover, int>();

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null) return;
        occupants.TryGetValue(mover, out int n);
        occupants[mover] = n + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null || !occupants.TryGetValue(mover, out int n)) return;
        if (n > 1) occupants[mover] = n - 1;
        else occupants.Remove(mover);
    }

    private System.Action cancel;

    private void OnEnable() => InteractionController.Register(this);
    private void OnDisable() => InteractionController.Unregister(this);

    // E는 InteractionController가 한 번만 읽는다(키맵 통합안 §3).
    public void CollectActions(List<InteractionAction> into)
    {
        PlayerMover p = InteractionController.Controlled;
        if (p == null || !occupants.ContainsKey(p)) return;
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
        // 트리거 안에서 파괴된 참가자(Exit가 오지 않음)를 정리한다.
        List<PlayerMover> dead = null;
        foreach (PlayerMover m in occupants.Keys)
            if (m == null) (dead ?? (dead = new List<PlayerMover>())).Add(m);
        if (dead != null) foreach (PlayerMover m in dead) occupants.Remove(m);

        if (panel == null)
        {
            Debug.LogWarning($"[WiringCancelButton] '{name}'에 WiringPanel이 연결되지 않았다.", this);
            return;
        }

        panel.CancelSelection();
    }

}
