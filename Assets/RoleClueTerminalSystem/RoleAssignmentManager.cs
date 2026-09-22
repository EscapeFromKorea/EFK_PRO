using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 역할 슬롯들의 공유 상태 — 챕터당 1개(docs/PRD/RoleClueTerminal.md §3). 중복 배정 거부, 반환
/// 처리, 필수 역할 공석 시 진행 시간(전력/판정)을 멈추라는 신호를 브로드캐스트한다.
///
/// [슬롯 등록 = RoleSlot이 스스로 구독]
/// WindupAxle↔IWindupReceiver와 같은 방향의 관례를 따른다 — 슬롯이 자기 OnEnable/OnDisable에서
/// RegisterSlot/UnregisterSlot을 부른다(수동으로 슬롯 배열을 인스펙터에 나열하지 않는다). 차이는
/// 여기서는 "다수 슬롯 → 매니저 1개"이고 WindupAxle은 "발신자 1개 → 다수 수신자"라는 것뿐, 구독
/// 방향(자기 자신을 등록하는 쪽이 자신을 안다)은 같다.
///
/// [타인 강제 교체 불가 — 확정]
/// TryAssign은 슬롯이 공석일 때만 성공한다. 이미 다른 참가자가 가진 슬롯을 요청하면 거부만 하고
/// 아무것도 바꾸지 않는다 — 요청자의 기존 다른 슬롯 소유는 이 호출과 무관하므로 건드리지 않는다
/// (§4 "타인 강제 교체 불가", 완료조건 R-02).
///
/// [필수 역할 공석 → 일시정지 브로드캐스트]
/// RefreshRequiredCoverage가 매 배정/반환/슬롯 등록·해제마다 재계산한다. 상태가 실제로
/// 바뀔 때만 IRolePauseReceiver.SetRolePaused를 쏜다(매번 재전송하지 않는다) — 단, 새로
/// Subscribe하는 수신자에게는 그 시점의 현재 상태를 즉시 한 번 보내 동기화한다. paused는 이산
/// 신호라(WindupAxle의 매프레임 ApplyOutput과 달리) 놓치면 다음 상태 변화 전까지 수신자가 틀린
/// 값을 들고 있게 되기 때문이다.
///
/// [초기값이 true인 이유]
/// 씬 시작 시 슬롯들이 모두 등록되기 전까지 짧은 창이 있을 수 있다(등록 순서는 Unity가 보장하지
/// 않는다). "아직 아무도 역할을 안 맡았다"가 기본 상태이므로, 첫 RegisterSlot 호출 전까지는
/// 안전 쪽(정지)으로 시작한다.
/// </summary>
public class RoleAssignmentManager : MonoBehaviour
{
    private readonly List<RoleSlot> slots = new List<RoleSlot>();
    private readonly List<IRolePauseReceiver> pauseReceivers = new List<IRolePauseReceiver>();

    private bool paused = true;

    /// <summary>지금 필수 역할이 하나라도 공석이라 진행이 일시 정지된 상태인가.</summary>
    public bool IsPaused => paused;

    public void RegisterSlot(RoleSlot slot)
    {
        if (slot == null || slots.Contains(slot)) return;
        slots.Add(slot);
        RefreshRequiredCoverage();
    }

    public void UnregisterSlot(RoleSlot slot)
    {
        if (!slots.Remove(slot)) return;
        RefreshRequiredCoverage();
    }

    public void Subscribe(IRolePauseReceiver receiver)
    {
        if (receiver == null || pauseReceivers.Contains(receiver)) return;
        pauseReceivers.Add(receiver);
        receiver.SetRolePaused(paused); // 늦게 구독해도 현재 상태를 놓치지 않게 즉시 동기화.
    }

    public void Unsubscribe(IRolePauseReceiver receiver) => pauseReceivers.Remove(receiver);

    /// <summary>슬롯 배정을 시도한다. 공석일 때만 성공한다 — 타인 소유 슬롯은 강제 교체하지 않고
    /// 거부만 한다(§4 확정). 요청자가 이미 이 슬롯의 소유자인 경우는 RoleSlot.Update()가 재상호작용
    /// (패널 열기)으로 먼저 걸러내므로 여기까지 오지 않는다.</summary>
    public bool TryAssign(RoleSlot slot, PlayerMover requester)
    {
        if (slot == null || requester == null) return false;

        if (slot.CurrentOwner != null)
        {
            Debug.Log($"[Role] '{slot.roleId}' — {ShapeLabel(slot.CurrentOwner)}님이 사용 중입니다.", slot);
            return false;
        }

        slot.SetOwner(requester);
        Debug.Log($"[Role] {ShapeLabel(requester)} → '{slot.roleId}' 역할 배정.", slot);
        RefreshRequiredCoverage();
        return true;
    }

    /// <summary>역할을 반환한다. 소유자 본인만 반환할 수 있다 — 역할 교환은 각자 반환 후 새로
    /// 선택하는 2단계로만 이뤄진다(§4 확정). 실제로 이 메서드를 부르는 입력(별도 키/패널 버튼 등)은
    /// 아직 RoleSlot에 연결하지 않았다(§5 "역할 반환/교환/공석 대기의 세부 정책" — 팀 확인 필요).</summary>
    public bool ReturnRole(RoleSlot slot, PlayerMover requester)
    {
        if (slot == null || requester == null) return false;

        if (slot.CurrentOwner != requester)
        {
            Debug.LogWarning($"[Role] '{slot.roleId}'는 {ShapeLabel(requester)}의 소유가 아니라 " +
                             "반환할 수 없다.", slot);
            return false;
        }

        slot.SetOwner(null);
        Debug.Log($"[Role] {ShapeLabel(requester)}가 '{slot.roleId}' 역할을 반환했다.", slot);
        RefreshRequiredCoverage();
        return true;
    }

    private void RefreshRequiredCoverage()
    {
        bool anyVacant = false;
        foreach (RoleSlot s in slots)
        {
            if (s != null && s.isRequired && s.CurrentOwner == null)
            {
                anyVacant = true;
                break;
            }
        }

        if (anyVacant == paused) return; // 상태 변화 없음 — 재전송하지 않는다.
        paused = anyVacant;

        for (int i = 0; i < pauseReceivers.Count; i++)
            pauseReceivers[i]?.SetRolePaused(paused);

        Debug.Log($"[Role] 필수 역할 공석 상태 변경 — {(paused ? "일시 정지" : "재개")}.", this);
    }

    private static string ShapeLabel(PlayerMover mover)
    {
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        return identity != null ? identity.Kind.ToString() : mover.gameObject.name;
    }
}
