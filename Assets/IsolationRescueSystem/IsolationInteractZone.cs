using UnityEngine;

/// <summary>
/// E 키로 누르는 격리실 버튼류의 공통 베이스(준비 확인·최종 해제). 트리거 안에서 "조작 중인" 참가자가
/// 상호작용 키를 눌렀을 때만 <see cref="Press"/>를 부른다. docs/PRD/IsolationRescue.md §3.
///
/// [키는 E]
/// 2026-10-02 키 매핑 통합안이 E를 "손 채널"(월드 대상과 접촉하는 모든 동작)로 정했고 배선 포트·역할 슬롯도
/// 이미 E다. 팀 공통 설정이 생기기 전까지는 WiringPort처럼 컴포넌트마다 KeyCode 필드를 둔다.
///
/// [무반응 금지]
/// 같은 통합안의 규칙 2("불가능할 땐 이유를 표시한다")에 따라, 눌렀는데 안 되는 경우는 <see cref="Say"/>로
/// 이유를 HUD에 띄운다. HUD가 없으면 로그만 남긴다.
/// </summary>
public abstract class IsolationInteractZone : IsolationOccupancyZone
{
    [Tooltip("상호작용 키.")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("거부 사유를 띄울 HUD. 비워 두면 로그만 남긴다.")]
    public IsolationRescueHud hud;

    private void Update()
    {
        if (!Input.GetKeyDown(interactKey)) return;
        if (ControlledOccupant() == null) return;
        Press();
    }

    /// <summary>버튼을 한 번 누른다. 입력 처리와 분리해 둔 이유: 자가검증이 키 입력 없이 호출한다.</summary>
    public abstract void Press();

    protected void Say(string message)
    {
        Debug.Log($"[{GetType().Name}] {message}", this);
        if (hud != null) hud.Say(message);
    }
}
