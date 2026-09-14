/// <summary>
/// 태엽 파생 장치의 발동 방식. 축(<see cref="WindupAxle"/>) 자신은 이 개념을 모른다 — 각 파생
/// 장치가 스스로 "충전 후 언제 실제로 움직이는가"를 판단하는 데만 쓴다. 별도 장치로 분리하지
/// 않고 enum으로 둔 이유는 전력 공급 등 후속 발동 방식을 추가하기 쉽게 하려는 것(2026-09-14).
/// </summary>
public enum WindupActivationMode
{
    /// <summary>마지막으로 감은 시점부터 지정된 시간이 지나면 자동 발동(기존 동작).</summary>
    ReleaseDelay,
    /// <summary>지정된 발판을 밟고 있는 동안만 발동. 발판에서 내려오면 즉시 정지한다.</summary>
    HoldPad,
}
