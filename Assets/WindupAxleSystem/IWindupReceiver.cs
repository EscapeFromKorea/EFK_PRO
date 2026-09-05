/// <summary>
/// 태엽 축(<see cref="WindupAxle"/>)이 매 틱 내보내는 연속 신호를 받는 수신자 계약. 레일카·
/// 회전판·회전 계단처럼 서로 다른 장치가 같은 축에 붙을 수 있어(저장소 최초의 다중 수신자 타입
/// 발신자 사례) 강타입 인터페이스를 쓴다 — 완충/완전방전 같은 이산 이벤트는 저장소 기존 관례대로
/// <c>UnityEvent</c>로 별도 발신한다(<see cref="WindupAxle.onFullyCharged"/> 등).
/// </summary>
public interface IWindupReceiver
{
    /// <param name="power">부호 있는 출력 세기. 양수 = 정방향, 음수 = 역방향.</param>
    /// <param name="ratio">현재 충전 비율(0~1). 세기와 별개로 필요한 연출(발광 등)에 쓴다.</param>
    void ApplyOutput(float power, float ratio);

    /// <summary>손잡이를 한 번 밀 때(<see cref="WindupAxle.ApplyRotation"/> 성공 시점)마다 정확히
    /// 한 번 호출되는 이산 이벤트. 방전 곡선을 타는 <see cref="ApplyOutput"/>과 달리 충전량이나
    /// 방전 속도와 무관하게 "몇 번 밀었는가"만 그대로 전달한다(회전판처럼 미는 횟수와 결과가 1:1로
    /// 대응해야 하는 장치용, 2026-09-05 추가). 방전 곡선 기반 출력이 맞는 장치는 이걸 무시하고
    /// <see cref="ApplyOutput"/>만 써도 된다.</summary>
    /// <param name="direction">이번 밀기의 부호. 양수 = 정방향, 음수 = 역방향.</param>
    void OnCrankSwing(float direction);
}
