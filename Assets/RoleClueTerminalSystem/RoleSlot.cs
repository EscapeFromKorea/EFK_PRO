using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 역할 사물(가이드북/컴퓨터/혼합대/전력 등) — 참가자가 이 사물에 상호작용하면 그 사물을 사용하며
/// 역할을 맡는다(docs/PRD/RoleClueTerminal.md §2, §3, 팀 회신 1). 챕터당 여러 개가 하나의
/// RoleAssignmentManager를 공유한다.
///
/// [역할 = 사물 점유 — 별도 역할 선택 메뉴 없음]
/// 처음 상호작용하면 배정 + 화면 열기가 한 번에 일어난다. 이미 자기가 점유한 사물이면 (Tab 등으로
/// 닫힌) 화면만 다시 연다. 사물을 내려놓는 건 Esc뿐이고 그 처리는 RoleAssignmentManager가 맡는다 —
/// Esc가 "지금 조작 중인 참가자가 점유한 사물"을 찾는 일이라 슬롯 하나가 아니라 매니저 단위 정보다.
///
/// [사물 하나는 한 사람 — 서버실 배선만 예외]
/// allowMultipleUsers가 켜진 슬롯(서버실 배선 담당)만 여러 명이 함께 쓸 수 있다. 나머지는 먼저
/// 상호작용한 한 명이 점유하고, 다른 사람은 "다른 플레이어가 사용 중입니다"만 본다(대기열·예약 없음).
///
/// [트리거 + "조작 중인" 참가자만 반응]
/// RespawnController.ControlledPlayer와 같은 조회 순서(매니저.ControlledPlayer)로 Tab으로 다른 도형을
/// 보는 동안 트리거 안에 남아있는 도형이 우연히 사물을 가져가지 못하게 한다.
///
/// [겹침 집계는 콜라이더 수 단위 — 이전 스캐폴딩의 버그 수정]
/// 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider) 콜라이더를 함께 가져 한 도형당
/// Enter/Exit가 여러 번 불린다(ExitWeightPlate 주석 참고). 이전 구현은 참가자당 목록 한 칸이라
/// 콜라이더 하나가 빠지는 순간 아직 안에 있는 참가자가 목록에서 사라졌다. 이제 바디별 카운트를 쓴다.
///
/// [화면 열림 상태]
/// PanelUser = 지금 이 사물의 화면을 열어 둔 참가자. 점유(Users)와는 별개다 — Tab으로 조종 캐릭터를
/// 바꾸거나 개인 복귀·거리 이탈이 있으면 화면만 닫히고 점유는 유지된다(§2.5, 회신 3).
/// </summary>
public class RoleSlot : MonoBehaviour
{
    [Tooltip("이 슬롯의 역할 ID. 챕터 안에서 유일해야 한다 " +
             "(예: \"Book\", \"Computer\", \"Power\" — CH8은 \"Mixer\", \"Monitor\").")]
    public string roleId;

    [Tooltip("이 슬롯이 속한 챕터의 매니저. 씬에 하나뿐이라 직접 참조한다(동일 시스템 내부 참조 — " +
             "RespawnZone→RespawnController와 같은 관례로, 이벤트로 분리하지 않는다).")]
    public RoleAssignmentManager manager;

    [Tooltip("상호작용 키.")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("여러 명이 함께 쓸 수 있는 사물인가. 서버실 배선(전력 담당)만 켠다 — 나머지는 한 사람만 " +
             "사용한다(회신 1).")]
    public bool allowMultipleUsers = false;

    /// <summary>화면이 열렸을 때(배정 직후 또는 재상호작용). 인자 = 화면을 연 참가자.</summary>
    public event System.Action<PlayerMover> PanelOpened;

    /// <summary>화면이 닫혔을 때(Esc 사용 종료·Tab 전환·개인 복귀·거리 이탈·이탈). 미제출 선택은 이
    /// 신호를 받는 패널이 취소한다. 인자 = 화면을 닫은 참가자.</summary>
    public event System.Action<PlayerMover> PanelClosed;

    /// <summary>점유가 해제됐을 때(Esc·이탈). 인자 = 사물을 내려놓은 참가자.</summary>
    public event System.Action<PlayerMover> Released;

    /// <summary>지금 이 사물을 점유한 참가자들. 단독 슬롯은 0~1명.</summary>
    public IReadOnlyList<PlayerMover> Users => users;

    /// <summary>점유자(단독 슬롯) 또는 첫 점유자(공유 슬롯). 비어 있으면 null.</summary>
    public PlayerMover CurrentOwner => users.Count > 0 ? users[0] : null;

    /// <summary>화면을 열어 둔 참가자. 화면이 닫혀 있으면 null.</summary>
    public PlayerMover PanelUser { get; private set; }

    public bool IsPanelOpen => PanelUser != null;

    private readonly List<PlayerMover> users = new List<PlayerMover>();
    private readonly Dictionary<PlayerMover, int> overlaps = new Dictionary<PlayerMover, int>();

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
        if (mover == null) return;
        overlaps.TryGetValue(mover, out int n);
        overlaps[mover] = n + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null || !overlaps.TryGetValue(mover, out int n)) return;

        if (n > 1)
        {
            overlaps[mover] = n - 1;
            return;
        }

        // 마지막 콜라이더까지 빠졌다 = 거리 이탈. 화면만 닫고 점유는 유지한다(§2.5).
        overlaps.Remove(mover);
        if (PanelUser == mover) ClosePanel();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(interactKey)) return;
        if (manager == null)
        {
            Debug.LogWarning($"[RoleSlot] '{name}'에 RoleAssignmentManager가 연결되지 않아 상호작용을 " +
                             "처리할 수 없다.", this);
            return;
        }

        PlayerMover requester = manager.ControlledPlayer();
        if (requester == null || !overlaps.ContainsKey(requester)) return;
        HandleInteract(requester);
    }

    /// <summary>
    /// 상호작용 한 번. 이미 점유 중이면 (닫혀 있던) 화면을 다시 열고, 아니면 배정을 요청해 성공하면
    /// 화면을 연다. 키 입력과 분리해 둔 이유는 Editor 자가검증이 입력 없이 재현하기 위해서다.
    /// </summary>
    public void HandleInteract(PlayerMover requester)
    {
        if (requester == null) return;

        if (users.Contains(requester))
        {
            if (!IsPanelOpen) OpenPanel(requester);
            return;
        }

        if (manager == null) return;
        if (manager.TryAssign(this, requester)) OpenPanel(requester);
    }

    /// <summary>이 참가자에게 이 사물의 상세 화면을 보여도 되는가 — 정보 격리(§4). 화면이 열려 있고
    /// 그 화면의 사용자와 같은 참가자일 때만 true. 패널은 이 값이 true일 때만 내용을 그린다.</summary>
    public bool CanShowTo(PlayerMover viewer)
    {
        return viewer != null && PanelUser == viewer;
    }

    public bool HasUser(PlayerMover p) => p != null && users.Contains(p);

    /// <summary>화면만 닫는다(점유 유지). 열려 있지 않으면 아무 일도 없다.</summary>
    public void ClosePanel()
    {
        if (PanelUser == null) return;
        PlayerMover closedBy = PanelUser;
        PanelUser = null;
        PanelClosed?.Invoke(closedBy);
    }

    private void OpenPanel(PlayerMover p)
    {
        PanelUser = p;
        PanelOpened?.Invoke(p);
    }

    /// <summary>RoleAssignmentManager 전용 — 점유자를 추가한다. 중복 배정 검사는 매니저가 책임진다.</summary>
    internal void AddUser(PlayerMover p)
    {
        if (p != null && !users.Contains(p)) users.Add(p);
    }

    /// <summary>RoleAssignmentManager 전용 — 사물 사용을 종료한다(화면 닫기 + 점유 해제).</summary>
    internal void Release(PlayerMover p)
    {
        if (p == null || !users.Contains(p)) return;
        if (PanelUser == p) ClosePanel();
        users.Remove(p);
        Released?.Invoke(p);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = users.Count > 0
            ? new Color(1f, 0.6f, 0.1f, 0.6f)   // 주황 = 점유 중
            : new Color(0.2f, 0.9f, 0.4f, 0.6f); // 초록 = 비어 있음
        Gizmos.DrawWireCube(transform.position, Vector3.one);
    }
}
