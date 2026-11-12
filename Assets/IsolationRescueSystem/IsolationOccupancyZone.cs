using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 격리실 씬 컴포넌트 공통 베이스 — 트리거 안에 있는 참가자를 콜라이더 수 단위로 집계하고, 그중 "지금
/// 조작 중인" 참가자를 찾는다. docs/PRD/IsolationRescue.md §3.
///
/// [WiringPort/RoleSlot과 같은 관례를 이 폴더 안에서만 공유한다]
/// 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider)를 함께 가져 한 도형당 Enter/Exit가 여러 번
/// 불린다. 참가자 단위 목록이면 콜라이더 하나가 빠지는 순간 아직 안에 있는 참가자가 사라지므로
/// 콜라이더 수로 센다(WiringPort와 동일). 다른 시스템 폴더의 코드는 참조하지 않고 복제한다(저장소 관례).
/// 이 폴더의 ReadyButton/ReleaseButton/ClueBoard/CorrespondenceMap이 같은 로직을 쓰므로 한 곳에 둔다.
///
/// [조작 중인 참가자 = PlayerControlSwitcher.ActiveTarget]
/// 스위처가 없는 씬에서는 ActiveTarget이 null이라 IsControlled가 켜진 참가자(기본값 true)를 쓴다.
/// </summary>
public abstract class IsolationOccupancyZone : MonoBehaviour
{
    private readonly Dictionary<PlayerMover, int> overlaps = new Dictionary<PlayerMover, int>();

    /// <summary>이 참가자가 지금 트리거 안에 있는가.</summary>
    public bool Contains(PlayerMover mover)
    {
        PruneDestroyed();
        return mover != null && overlaps.ContainsKey(mover);
    }

    /// <summary>OnTriggerEnter가 부른다. 콜라이더 하나가 들어올 때마다 그 참가자의 카운트를 올린다.</summary>
    public void HandleEnter(Collider other)
    {
        PlayerMover mover = other != null ? other.GetComponentInParent<PlayerMover>() : null;
        if (mover == null) return;
        overlaps.TryGetValue(mover, out int n);
        overlaps[mover] = n + 1;
        OnOccupantEntered(mover);
    }

    /// <summary>OnTriggerExit가 부른다. 마지막 콜라이더가 나갈 때만 참가자를 목록에서 뺀다.</summary>
    public void HandleExit(Collider other)
    {
        PlayerMover mover = other != null ? other.GetComponentInParent<PlayerMover>() : null;
        if (mover == null || !overlaps.TryGetValue(mover, out int n)) return;

        if (n > 1)
        {
            overlaps[mover] = n - 1;
            return;
        }

        overlaps.Remove(mover);
        OnOccupantLeft(mover);
    }

    private void OnTriggerEnter(Collider other) => HandleEnter(other);
    private void OnTriggerExit(Collider other) => HandleExit(other);

    /// <summary>참가자가 처음 트리거에 들어왔을 때(콜라이더 카운트 0→1). 파생 클래스가 필요하면 쓴다.</summary>
    protected virtual void OnOccupantEntered(PlayerMover mover) { }

    /// <summary>참가자의 마지막 콜라이더가 트리거를 벗어났을 때. 파생 클래스가 필요하면 쓴다.</summary>
    protected virtual void OnOccupantLeft(PlayerMover mover) { }

    /// <summary>트리거 안에 있는 참가자 중 지금 조작 중인 한 명. 없으면 null.</summary>
    protected PlayerMover ControlledOccupant()
    {
        PruneDestroyed();

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

    /// <summary>
    /// 지금 이 화면을 보고 있는 참가자(조작 중인 한 명). 스위처가 없으면 null — HUD처럼 트리거 밖에서도
    /// 보는 사람을 알아야 하는 곳이 쓴다. 보는 사람을 모르면 비밀 정보는 그리지 않는 쪽으로 처리한다.
    /// </summary>
    public static PlayerMover ControlledViewer()
    {
        Transform active = PlayerControlSwitcher.ActiveTarget;
        return active != null ? active.GetComponent<PlayerMover>() : null;
    }

    // 트리거 안에서 파괴된 참가자(Exit가 오지 않음)를 정리한다.
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
}
