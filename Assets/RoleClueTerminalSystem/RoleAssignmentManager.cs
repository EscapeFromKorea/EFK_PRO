using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 역할 사물들의 공유 상태 — 챕터당 1개(docs/PRD/RoleClueTerminal.md §3, 팀 회신 1·2·3).
/// 점유 배정(중복·1인 1사물), Esc 사용 종료, Tab 전환 시 화면 닫기, 참가자 이탈/재접속에 따른
/// 일시정지·재개를 책임진다.
///
/// [자발적 반환과 이탈은 다른 일이다 — 이전 "공석 → 일시정지" 규칙을 대체]
/// - 자발적 반환(Esc): 점유만 풀린다. 퍼즐 전체는 멈추지 않고 제한시간·전력 감소·관리자 행동이
///   계속된다. 그래서 "필수 역할 공석" 개념과 그에 따른 브로드캐스트를 없앴다.
/// - 참가자 이탈: NotifyParticipantLeft가 그 사람의 점유를 풀고 챕터를 멈춘다(재접속 대기).
///   돌아오면 NotifyParticipantRejoined가 "재개 대기"로 넘기고, 시작 담당자가 RequestResume(이어서
///   시작)을 눌러야 재개된다 — 재접속한 순간 위험 장치가 바로 움직이지 않게 하려는 목적이다.
///
/// [이탈 "감지"는 이 컴포넌트가 하지 않는다]
/// 어느 참가자가 튕겼는지는 세션/네트워크 계층이 알 일이다. 이 저장소는 아직 그 계층이 없어(Tab으로
/// 도형을 돌려 쓰는 1PC 구조) NotifyParticipantLeft/Rejoined를 밖에서 부르는 창구만 열어 뒀다.
/// 그래서 이 컴포넌트의 로직은 로컬에서 자가검증할 수 있지만, 실제 멀티플레이 판정(권한 주체)은
/// 팀 결정(넷코드 전제) 뒤에 붙는다.
///
/// [팀 결정이 나지 않아 정책을 넣지 않은 곳]
/// "시작 담당자"가 이탈자이거나 끝내 돌아오지 않을 때, 이탈 중 두 번째 이탈이 나올 때의 확정 정책은
/// 회신에 없다. 두 번째 이탈은 집합으로 세서 전원이 돌아와야 재개 대기로 넘어가게 했고(가장 보수적),
/// 시작 담당자가 지정됐는데 사라진 경우는 임의로 다른 사람에게 권한을 넘기지 않고 거부하며
/// ForceResume(레벨/팀 정책이 정해지면 그쪽에서 호출)만 열어 뒀다.
///
/// [슬롯 등록 = RoleSlot이 스스로 구독]
/// WindupAxle↔IWindupReceiver와 같은 방향의 관례 — 슬롯이 자기 OnEnable/OnDisable에서
/// RegisterSlot/UnregisterSlot을 부른다(수동 배열 나열 없음).
/// </summary>
public class RoleAssignmentManager : MonoBehaviour
{
    /// <summary>챕터 진행 상태. Running 이외는 모두 "정지" — 수신자에게는 paused=true로 나간다.</summary>
    public enum SessionState
    {
        Running,
        WaitingReconnect,   // 필수 참가자가 이탈해 있다.
        WaitingResume       // 전원이 돌아왔고 시작 담당자의 "이어서 시작"을 기다린다.
    }

    [Header("입력")]
    [Tooltip("사물 사용 종료(점유 해제). 화면에 \"Esc: 사용 종료\"로 안내한다.")]
    public KeyCode releaseKey = KeyCode.Escape;

    [Tooltip("재개 대기 상태에서 \"이어서 시작\"을 누르는 키.")]
    public KeyCode resumeKey = KeyCode.Return;

    [Header("임시 표시")]
    [Tooltip("재접속 대기·재개 안내와 사용 중 메시지를 화면에 그린다. UI 시스템이 생기면 그쪽으로 " +
             "옮긴다(RespawnController.OnGUI와 같은 임시 표시 관례).")]
    public bool showStatusOverlay = true;

    /// <summary>조작 중인 참가자 조회를 교체하는 지점(자가검증·멀티플레이 확장용). null이면 기본 조회
    /// (PlayerControlSwitcher.ActiveTarget 우선, 없으면 단일 플레이어 폴백).</summary>
    public System.Func<PlayerMover> ControlledPlayerResolver;

    public SessionState State { get; private set; } = SessionState.Running;

    /// <summary>수신자에게 나가는 값과 같다. Running이 아니면 true.</summary>
    public bool IsPaused => State != SessionState.Running;

    /// <summary>가장 최근에 화면에 알린 메시지(사용 중 등). 자가검증과 오버레이가 쓴다.</summary>
    public string LastMessage { get; private set; }

    private readonly List<RoleSlot> slots = new List<RoleSlot>();
    private readonly List<IParticipantPauseReceiver> receivers = new List<IParticipantPauseReceiver>();
    private readonly HashSet<PlayerMover> away = new HashSet<PlayerMover>();
    private readonly List<PlayerMover> roster = new List<PlayerMover>();

    private PlayerMover startOwner;
    private bool startOwnerAssigned;
    private PlayerMover lastControlled;
    private float messageUntil;
    private float nextRosterRefresh;

    // ── 슬롯 / 수신자 등록 ───────────────────────────────────────────

    public void RegisterSlot(RoleSlot slot)
    {
        if (slot != null && !slots.Contains(slot)) slots.Add(slot);
    }

    public void UnregisterSlot(RoleSlot slot)
    {
        slots.Remove(slot);
    }

    public void Subscribe(IParticipantPauseReceiver receiver)
    {
        if (receiver == null || receivers.Contains(receiver)) return;
        receivers.Add(receiver);
        receiver.SetParticipantPaused(IsPaused); // 늦게 구독해도 현재 상태를 놓치지 않게 즉시 동기화.
    }

    public void Unsubscribe(IParticipantPauseReceiver receiver) => receivers.Remove(receiver);

    // ── 조작 중인 참가자 ──────────────────────────────────────────────

    /// <summary>지금 조작 중인 참가자. RespawnController.ControlledPlayer와 같은 조회 순서를 쓴다 —
    /// 스위처가 있으면 그 대상, 없으면(단일 플레이어 테스트 씬) IsControlled인 첫 PlayerMover.</summary>
    public PlayerMover ControlledPlayer()
    {
        if (ControlledPlayerResolver != null) return ControlledPlayerResolver();

        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active != null) return active.GetComponent<PlayerMover>();

        RefreshRoster();
        foreach (PlayerMover m in roster)
            if (m != null && m.IsControlled) return m;
        return null;
    }

    // ponytail: 1초에 한 번 씬을 다시 훑는다(RespawnController.RefreshRoster와 같은 이유 — 플레이어는
    // 씬에 미리 배치된 3개뿐이라 이 주기로 충분하고 매 프레임 FindObjectsOfType는 비싸다).
    private void RefreshRoster()
    {
        if (Time.unscaledTime < nextRosterRefresh) return;
        nextRosterRefresh = Time.unscaledTime + 1f;
        roster.Clear();
        roster.AddRange(Object.FindObjectsOfType<PlayerMover>());
    }

    // ── 점유 배정 / 사용 종료 ─────────────────────────────────────────

    /// <summary>
    /// 사물 점유를 시도한다. 거부 사유: (1) 이미 다른 사물을 쓰는 중이면 먼저 Esc로 내려놓아야 한다
    /// (1인 1사물, 회신 1 "각자 사용하던 사물을 내려놓은 뒤 다른 사물을 사용"), (2) 단독 슬롯을 다른
    /// 참가자가 쓰는 중이면 "다른 플레이어가 사용 중입니다". 상대의 사물을 강제로 가져오거나 자동으로
    /// 교환하지 않는다(§4). 거부는 아무 상태도 바꾸지 않는다 — 요청자의 기존 점유도 그대로다(R-02).
    /// </summary>
    public bool TryAssign(RoleSlot slot, PlayerMover requester)
    {
        if (slot == null || requester == null) return false;

        RoleSlot held = HeldSlot(requester);
        if (held != null && held != slot)
        {
            Say($"'{held.roleId}' 사용 중입니다. Esc로 사용을 종료한 뒤 다른 사물을 사용하세요.", slot);
            return false;
        }

        if (!slot.allowMultipleUsers && slot.CurrentOwner != null && slot.CurrentOwner != requester)
        {
            Say("다른 플레이어가 사용 중입니다.", slot);
            return false;
        }

        slot.AddUser(requester);
        Say($"{ShapeLabel(requester)} → '{slot.roleId}' 사용 시작.", slot);
        return true;
    }

    /// <summary>참가자가 점유한 사물의 사용을 종료한다(Esc). 화면을 닫고 점유를 푼다. 퍼즐은 멈추지
    /// 않는다(회신 1) — 이미 맞힌 진행은 유지되고 미제출 선택만 화면 닫힘 신호로 취소된다.</summary>
    public bool ReleaseByPlayer(PlayerMover p)
    {
        RoleSlot held = HeldSlot(p);
        if (held == null) return false;

        held.Release(p);
        Say($"{ShapeLabel(p)}가 '{held.roleId}' 사용을 종료했다.", held);
        return true;
    }

    /// <summary>이 참가자의 열려 있는 화면을 모두 닫는다(점유 유지). Tab 전환, 개인 복귀(추락 등)에서
    /// 쓴다 — 역할과 진행 상태는 유지하고 화면·미제출 선택만 정리한다(§2.5, 회신 3).</summary>
    public void ClosePanelsOf(PlayerMover p)
    {
        if (p == null) return;
        foreach (RoleSlot s in slots)
            if (s != null && s.PanelUser == p) s.ClosePanel();
    }

    private RoleSlot HeldSlot(PlayerMover p)
    {
        if (p == null) return null;
        foreach (RoleSlot s in slots)
            if (s != null && s.HasUser(p)) return s;
        return null;
    }

    // ── 참가자 이탈 / 재접속 / 재개 ───────────────────────────────────

    /// <summary>필수 참가자가 튕기거나 나갔다. 그 사람의 사물 점유를 풀고 챕터를 정지한다. 이미 이탈
    /// 중인 사람이 다시 들어와도 무시하고, 다른 참가자가 추가로 이탈하면 집합에 더한다.</summary>
    public void NotifyParticipantLeft(PlayerMover p)
    {
        if (p == null || !away.Add(p)) return;

        foreach (RoleSlot s in new List<RoleSlot>(slots))
            if (s != null && s.HasUser(p)) s.Release(p);

        bool wasRunning = State == SessionState.Running;
        State = SessionState.WaitingReconnect;
        Say($"{ShapeLabel(p)} 이탈 — 참가자 재접속 대기 중.", this);
        if (wasRunning) Broadcast(true);
    }

    /// <summary>이탈했던 참가자가 돌아왔다. 이탈자 전원이 돌아오면 "재개 대기"로 넘어간다 — 재개는
    /// 아직 아니다(시작 담당자의 이어서 시작이 필요). 역할은 다시 선택해야 한다(점유는 이미 풀렸다).</summary>
    public void NotifyParticipantRejoined(PlayerMover p)
    {
        if (p == null || !away.Remove(p)) return;

        if (away.Count == 0 && State == SessionState.WaitingReconnect)
        {
            State = SessionState.WaitingResume;
            Say("전원 재접속 완료 — 시작 담당자가 '이어서 시작'을 눌러 주세요.", this);
        }
    }

    /// <summary>"이어서 시작"을 누를 기존 시작 담당자를 지정한다(챕터를 시작한 참가자). null이면 미지정.</summary>
    public void SetStartOwner(PlayerMover p)
    {
        startOwner = p;
        startOwnerAssigned = p != null;
    }

    /// <summary>"이어서 시작". 재개 대기 상태에서 시작 담당자만 누를 수 있다. 시작 담당자가 지정되지
    /// 않았으면 누구나 누를 수 있다. 지정됐는데 사라진 경우는 정책이 없어 거부한다(클래스 주석 참고).</summary>
    public bool RequestResume(PlayerMover requester)
    {
        if (State != SessionState.WaitingResume || requester == null) return false;

        if (startOwnerAssigned)
        {
            if (startOwner == null)
            {
                Say("시작 담당자가 사라져 재개할 수 없다(대체 정책 미정 — 팀 결정 필요).", this);
                return false;
            }
            if (requester != startOwner)
            {
                Say("시작 담당자만 '이어서 시작'을 누를 수 있다.", this);
                return false;
            }
        }

        Resume();
        return true;
    }

    /// <summary>시작 담당자 확인 없이 재개한다. 시작 담당자 부재 등의 정책이 팀에서 정해지면 그
    /// 정책을 구현한 호출부가 사용한다 — 이 컴포넌트는 그 정책을 스스로 정하지 않는다.</summary>
    public void ForceResume()
    {
        if (State == SessionState.WaitingResume) Resume();
    }

    private void Resume()
    {
        State = SessionState.Running;
        Say("재개.", this);
        Broadcast(false);
    }

    private void Broadcast(bool paused)
    {
        for (int i = 0; i < receivers.Count; i++)
            receivers[i]?.SetParticipantPaused(paused);
    }

    // ── 매 프레임 ─────────────────────────────────────────────────────

    private void Update()
    {
        Tick();

        if (Input.GetKeyDown(releaseKey))
        {
            PlayerMover p = ControlledPlayer();
            if (p != null) ReleaseByPlayer(p);
        }

        if (State == SessionState.WaitingResume && Input.GetKeyDown(resumeKey))
        {
            PlayerMover p = ControlledPlayer();
            if (p != null) RequestResume(p);
        }
    }

    /// <summary>조종 캐릭터가 바뀌었으면(Tab) 이전 캐릭터의 화면을 닫는다. 이전 캐릭터의 역할과 사물
    /// 점유는 유지된다 — 다시 그 캐릭터로 돌아와 상호작용하면 화면이 열린다(회신 3). Tab 전환 자체로
    /// 시간이나 위험 장치가 멈추지 않는다.</summary>
    public void Tick()
    {
        PlayerMover now = ControlledPlayer();
        if (lastControlled != null && now != lastControlled) ClosePanelsOf(lastControlled);
        lastControlled = now;
    }

    // ── 표시 / 유틸 ───────────────────────────────────────────────────

    private void Say(string message, Object context)
    {
        LastMessage = message;
        messageUntil = Time.unscaledTime + 2.5f;
        Debug.Log($"[Role] {message}", context);
    }

    private void OnGUI()
    {
        if (!showStatusOverlay) return;

        string banner = null;
        if (State == SessionState.WaitingReconnect) banner = "참가자 재접속 대기 중";
        else if (State == SessionState.WaitingResume) banner = "재접속 완료 — 시작 담당자: Enter = 이어서 시작";

        float w = 520f;
        Rect area = new Rect((Screen.width - w) * 0.5f, 12f, w, 60f);
        if (banner != null) GUI.Box(area, banner);

        if (!string.IsNullOrEmpty(LastMessage) && Time.unscaledTime < messageUntil)
            GUI.Label(new Rect(area.x, area.y + 64f, w, 24f), LastMessage);
    }

    private static string ShapeLabel(PlayerMover mover)
    {
        if (mover == null) return "?";
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        return identity != null ? identity.Kind.ToString() : mover.gameObject.name;
    }
}
