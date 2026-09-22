using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 역할 위치(트리거) — 참가자가 상호작용해 이 슬롯의 역할을 맡거나, 이미 자신이 맡은 슬롯이면
/// 해당 패널(책 패널/문답 단말)을 연다. 챕터당 여러 개(책/컴퓨터/전력 등)가 하나의
/// RoleAssignmentManager를 공유한다(docs/PRD/RoleClueTerminal.md §2, §3).
///
/// [트리거 + "조작 중인" 참가자만 반응]
/// 볼륨 안에 있는 참가자 목록(occupants)은 전부 추적하되, 실제 요청은 그중 지금 조작 중인
/// 참가자로만 좁힌다 — RespawnController.ControlledPlayer와 동일한 조회 순서(PlayerControlSwitcher.
/// ActiveTarget 우선, 없으면 단일 플레이어 폴백)를 쓴다. Tab으로 다른 도형을 보는 동안 트리거
/// 안에 남아있는 도형이 우연히 역할을 가져가는 것을 막는다.
///
/// [패널 열기는 아직 스텁 — BookPanel/QuizTerminal 미구현]
/// 이미 자신이 소유한 슬롯에서 다시 상호작용하면 OnOwnerReinteract만 발신하고 끝낸다(§2.2). 실제
/// 패널은 별도 컴포넌트(§3, 이번 스캐폴딩 범위 밖)라, 나중에 이 이벤트에 인스펙터로 걸면 된다 —
/// 여기 로직을 복제하지 않는다.
///
/// [역할 반환 트리거 — 아직 미구현, PRD 미결정]
/// "역할 반환/교환/공석 대기의 세부 정책"은 원본 PRD가 미결정 동작안으로 남겼다(§5). 반환은
/// RoleAssignmentManager.ReturnRole(slot, requester)로 이미 가능하지만, 이 컴포넌트는 그걸 부르는
/// 구체적인 입력(별도 키 vs 패널 내 버튼)을 아직 연결하지 않았다 — 팀 확인 후 붙인다.
/// </summary>
public class RoleSlot : MonoBehaviour
{
    [System.Serializable]
    public class RoleReinteractEvent : UnityEvent<GameObject> { }

    [Tooltip("이 슬롯의 역할 ID. 챕터 안에서 유일해야 한다 " +
             "(예: \"Book\", \"Computer\", \"Power\" — CH8은 \"Mixer\", \"Monitor\").")]
    public string roleId;

    [Tooltip("이 역할이 챕터 진행에 필수인가. 필수 역할이 하나라도 공석이면 RoleAssignmentManager가 " +
             "진행 시간(전력/판정)을 일시 정지시킨다(docs/PRD/RoleClueTerminal.md §2.6).")]
    public bool isRequired = true;

    [Tooltip("이 슬롯이 속한 챕터의 매니저. 씬에 하나뿐이라 직접 참조한다(동일 시스템 내부 참조 — " +
             "RespawnZone→RespawnController와 같은 관례로, 이벤트로 분리하지 않는다).")]
    public RoleAssignmentManager manager;

    [Tooltip("상호작용 키.")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("소유자가 자기 슬롯에서 다시 상호작용했을 때 발신(패널 열기 훅). 인자는 플레이어 " +
             "Root — 저장소 관례(RespawnPlayer, FallingRockSpawner 이벤트 등)와 맞춘다.")]
    public RoleReinteractEvent OnOwnerReinteract;

    /// <summary>이 슬롯의 현재 소유자. 비어 있으면 null(공석).</summary>
    public PlayerMover CurrentOwner { get; private set; }

    private readonly List<PlayerMover> occupants = new List<PlayerMover>();

    private void OnEnable()
    {
        if (manager != null) manager.RegisterSlot(this);
    }

    private void OnDisable()
    {
        if (manager != null) manager.UnregisterSlot(this);
    }

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

    private void Update()
    {
        occupants.RemoveAll(m => m == null);
        if (!Input.GetKeyDown(interactKey)) return;

        PlayerMover requester = ControlledOccupant();
        if (requester == null) return;

        if (CurrentOwner == requester)
        {
            OnOwnerReinteract?.Invoke(requester.gameObject);
            return;
        }

        if (manager == null)
        {
            Debug.LogWarning($"[RoleSlot] '{name}'에 RoleAssignmentManager가 연결되지 않아 배정을 " +
                             "요청할 수 없다.", this);
            return;
        }

        manager.TryAssign(this, requester);
    }

    // RespawnController.ControlledPlayer와 동일 조회 순서 — 스위처가 있으면 그 대상만, 없으면
    // (단일 플레이어 테스트 씬) IsControlled인 첫 occupant.
    private PlayerMover ControlledOccupant()
    {
        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active != null)
        {
            PlayerMover m = active.GetComponent<PlayerMover>();
            return (m != null && occupants.Contains(m)) ? m : null;
        }

        foreach (PlayerMover m in occupants)
            if (m != null && m.IsControlled) return m;
        return null;
    }

    /// <summary>RoleAssignmentManager 전용 — 소유자를 갱신한다. 외부에서 직접 호출하지 않는다
    /// (중복 배정 검사·공석 브로드캐스트는 매니저가 책임진다).</summary>
    internal void SetOwner(PlayerMover owner) => CurrentOwner = owner;

    private void OnDrawGizmos()
    {
        Gizmos.color = CurrentOwner != null
            ? new Color(1f, 0.6f, 0.1f, 0.6f)   // 주황 = 배정됨
            : new Color(0.2f, 0.9f, 0.4f, 0.6f); // 초록 = 공석
        Gizmos.DrawWireCube(transform.position, Vector3.one);
    }
}
