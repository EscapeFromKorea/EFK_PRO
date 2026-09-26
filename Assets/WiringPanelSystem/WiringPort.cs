using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 개별 포트 버튼(트리거) — 출력(OUT_A/B/C) 또는 입력(IN_1/2/3) 하나(docs/PRD/WiringPanel.md §3).
/// 출력 포트를 상호작용하면 선택 상태가 되고, 입력 포트를 상호작용하면 패널에 판정을 요청한다.
///
/// [조작 중인 참가자만 반응 — RoleSlot과 동일 관례]
/// 트리거 안 참가자 목록을 유지하되 실제 요청은 PlayerControlSwitcher.ActiveTarget 기준 "지금
/// 조작 중인" 참가자로 좁힌다(RoleSlot.cs, RespawnController.ControlledPlayer와 동일 조회 순서).
/// 이 파일과 RoleSlot이 로직을 공유하진 않는다 — 서로 다른 시스템 폴더라 복제한다(저장소 관례,
/// SectionHitCounter가 FallingRockSpawner 패턴을 복제한 것과 같은 이유).
/// </summary>
public class WiringPort : MonoBehaviour
{
    public enum Role { Output, Input }

    [Tooltip("이 포트가 출력(OUT_A/B/C)인지 입력(IN_1/2/3)인지.")]
    public Role role;

    [Tooltip("이 포트의 ID. 출력이면 \"A\"/\"B\"/\"C\", 입력이면 \"1\"/\"2\"/\"3\" 등 — 소속 " +
             "WiringCircuitData의 outputIds/inputIds와 정확히 일치해야 한다.")]
    public string portId;

    [Tooltip("이 포트가 속한 패널. 씬에 프리팹 배치 시 직접 연결한다(RoleSlot→RoleAssignmentManager와 " +
             "같은 동일 시스템 내부 직접 참조 관례).")]
    public WiringPanel panel;

    [Tooltip("상호작용 키.")]
    public KeyCode interactKey = KeyCode.E;

    // 참가자별 겹친 콜라이더 수. 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider)를 함께 가져
    // 한 도형당 Enter/Exit가 여러 번 불린다. 참가자 단위 목록이면 콜라이더 하나가 빠지는 순간 아직
    // 안에 있는 참가자가 사라진다(RoleSlot의 같은 버그를 콜라이더 수 집계로 고친 것과 동일).
    private readonly Dictionary<PlayerMover, int> overlaps = new Dictionary<PlayerMover, int>();

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null) return;
        overlaps.TryGetValue(mover, out int n);
        overlaps[mover] = n + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null || !overlaps.TryGetValue(mover, out int n)) return;

        if (n > 1) overlaps[mover] = n - 1;
        else overlaps.Remove(mover);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(interactKey)) return;
        PruneDestroyed();
        if (ControlledOccupant() == null) return;

        if (panel == null)
        {
            Debug.LogWarning($"[WiringPort] '{name}'에 WiringPanel이 연결되지 않았다.", this);
            return;
        }

        if (role == Role.Output) panel.SelectOutput(this);
        else panel.TrySelectInput(this);
    }

    private PlayerMover ControlledOccupant()
    {
        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active != null)
        {
            PlayerMover m = active.GetComponent<PlayerMover>();
            return (m != null && overlaps.ContainsKey(m)) ? m : null;
        }

        foreach (PlayerMover m in overlaps.Keys)
            if (m != null && m.IsControlled) return m;
        return null;
    }

    // 트리거 안에서 파괴된 참가자(Exit가 오지 않음)를 정리한다. 입력이 있을 때만 돌려 매 프레임 비용을 피한다.
    private void PruneDestroyed()
    {
        List<PlayerMover> dead = null;
        foreach (PlayerMover m in overlaps.Keys)
        {
            if (m != null) continue;
            if (dead == null) dead = new List<PlayerMover>();
            dead.Add(m);
        }

        if (dead == null) return;
        foreach (PlayerMover m in dead) overlaps.Remove(m);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = role == Role.Output ? new Color(1f, 0.5f, 0.2f, 0.8f) : new Color(0.2f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
