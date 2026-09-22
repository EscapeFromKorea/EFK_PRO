/// <summary>
/// RoleAssignmentManager가 필수 역할 공석 상태(잠김/해제)를 내보내는 신호를 받는 수신자 계약.
/// 서버실 전력 유지 장치·가상 시약 혼합 장치처럼 서로 다른 장치가 같은 매니저에 구독할 수 있어
/// (<see cref="WindupAxle"/>의 <see cref="IWindupReceiver"/>와 같은 다중 수신자 타입 발신자
/// 사례) 강타입 인터페이스를 쓴다(docs/PRD/RoleClueTerminal.md §3, §5 "제출 게이트 신호 계약").
/// </summary>
public interface IRolePauseReceiver
{
    /// <param name="paused">true = 필수 역할이 하나 이상 공석이라 진행 시간·판정을 멈춰야 한다.
    /// false = 전원 채워져 재개해도 된다. 구독 시점에 현재 상태로 한 번 즉시 호출된다
    /// (<see cref="RoleAssignmentManager.Subscribe"/> 참고 — 상태값이라 늦게 구독해도 놓치면 안 된다).</param>
    void SetRolePaused(bool paused);
}
