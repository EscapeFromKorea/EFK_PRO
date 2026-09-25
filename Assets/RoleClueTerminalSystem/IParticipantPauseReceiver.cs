/// <summary>
/// RoleAssignmentManager가 "필수 참가자 이탈로 챕터 진행을 멈춰라/재개해라"를 내보내는 신호의 수신자
/// 계약. 서버실 전력 유지 장치·가상 시약 혼합 장치·QuizTerminal처럼 서로 다른 장치가 같은 매니저에
/// 구독할 수 있어(<see cref="WindupAxle"/>의 <see cref="IWindupReceiver"/>와 같은 다중 수신자 타입
/// 발신자 사례) 강타입 인터페이스를 쓴다(docs/PRD/RoleClueTerminal.md §3).
///
/// [이 인터페이스의 의미가 바뀐 이유 — IRolePauseReceiver 대체]
/// 이전 이름은 "필수 역할이 공석이면 정지"였다. 팀 회신이 이 규칙을 바꿨다: 사물을 스스로 내려놓는
/// 것(자발적 반환)으로는 퍼즐 전체가 멈추지 않고 제한시간·전력 감소·관리자 행동이 계속된다. 정지는
/// **참가자가 튕기거나 나간 경우에만** 일어난다. 그래서 이름과 책임을 "역할"이 아니라 "참가자"로
/// 옮겼다. 다른 폴더에서 코드로 참조하는 곳이 없어(주석 언급뿐) 이름을 바꿔도 깨지는 곳이 없다.
/// </summary>
public interface IParticipantPauseReceiver
{
    /// <param name="paused">true = 필수 참가자가 이탈했거나(재접속 대기) 돌아왔지만 아직 시작 담당자가
    /// "이어서 시작"을 누르기 전이다. 퍼즐 입력·제한시간·전력 감소·위험 장치의 움직임과 피격 판정을
    /// 멈춰야 한다. false = 재개. 구독 시점에 현재 상태로 한 번 즉시 호출된다
    /// (<see cref="RoleAssignmentManager.Subscribe"/> 참고 — 상태값이라 늦게 구독해도 놓치면 안 된다).
    /// 재접속한 순간이 아니라 "이어서 시작" 시점에 false가 나가므로, 재접속 즉시 위험 장치가 움직이지
    /// 않는다(§ 회신 2).</param>
    void SetParticipantPaused(bool paused);
}
