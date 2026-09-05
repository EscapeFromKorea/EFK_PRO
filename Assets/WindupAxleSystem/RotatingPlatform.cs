using UnityEngine;

/// <summary>
/// 태엽 축의 파생 장치 1호 — 회전판. 손잡이를 밀 때마다(<see cref="IWindupReceiver.OnCrankSwing"/>)
/// 바로 돌지 않고 누적만 해뒀다가, <see cref="releaseDelay"/>(마지막으로 민 시점 기준) 뒤에 그동안
/// 쌓인 횟수만큼 발동한다(2026-09-05, 플레이테스트 피드백 — "감는 동작"과 "장치가 실제로 움직이는
/// 동작"을 분리해 태엽을 다 감아야 발동하는 느낌을 내려는 것). 발동도 한 번에 다 돌지 않고,
/// <see cref="stepReleaseInterval"/> 간격으로 한 걸음씩 끊어서 돈다 — "기어가 하나씩 맞물려 돈다"는
/// 느낌을 내려는 것(같은 날 추가 피드백). 방전 곡선을 타는 연속 출력(<see cref="ApplyOutput"/>)은
/// 쓰지 않는다. 축은 이 컴포넌트의 존재를 모른다(전체 브로드캐스트 신호를 구독만 할 뿐) — 회전
/// 계단 등 다른 파생 장치도 같은 방식으로 독립적으로 만들 수 있다.
/// </summary>
public class RotatingPlatform : MonoBehaviour, IWindupReceiver
{
    [Tooltip("신호를 받을 태엽 축.")]
    public WindupAxle axle;

    [Tooltip("기어 이빨 하나에 해당하는 각도. 걸음 하나당 정확히 이만큼 돈다.")]
    [Min(0.01f)]
    public float gearStepDegrees = 15f;
    [Tooltip("한 걸음을 얼마나 빨리 스냅하는지(초당 도). 높을수록 더 '빡빡하게' 튀어 들어간다.")]
    public float gearSnapSpeed = 720f;
    [Tooltip("마지막으로 민 시점부터 이 시간(초)이 지나야 그동안 쌓인 횟수만큼 발동을 시작한다. " +
             "그 사이에 또 밀면 타이머가 그 시점부터 다시 시작된다.")]
    public float releaseDelay = 3f;
    [Tooltip("발동 중 걸음 사이의 간격(초). 쌓인 횟수를 한 번에 다 돌리지 않고 이 간격으로 한 걸음씩 " +
             "끊어서 돌려 기어가 맞물려 돌아가는 느낌을 낸다.")]
    public float stepReleaseInterval = 0.3f;

    private float pendingSteps; // 아직 발동 안 하고 쌓아둔 횟수(부호 있음) — releaseDelay 뒤 큐로 넘어간다
    private float lastSwingTime = float.NegativeInfinity;
    private int queuedSteps;    // 발동 큐에 든, 아직 한 걸음씩 못 돌린 남은 횟수(부호 있음)
    private float nextQueuedStepTime;
    private float stepTarget;   // 반영이 확정된 목표각
    private float appliedAngle; // 실제로 transform에 반영된 누적각(스텝 단위로만 쫓아감)

    void OnEnable()
    {
        if (axle != null) axle.Subscribe(this);
    }

    void OnDisable()
    {
        if (axle != null) axle.Unsubscribe(this);
    }

    void FixedUpdate()
    {
        if (pendingSteps != 0f && Time.time - lastSwingTime >= releaseDelay)
        {
            queuedSteps += Mathf.RoundToInt(pendingSteps);
            pendingSteps = 0f;
            nextQueuedStepTime = Time.time; // 첫 걸음은 바로 시작
        }

        if (queuedSteps != 0 && Time.time >= nextQueuedStepTime)
        {
            int sign = queuedSteps > 0 ? 1 : -1;
            stepTarget += sign * gearStepDegrees;
            queuedSteps -= sign;
            nextQueuedStepTime = Time.time + stepReleaseInterval;
        }

        float next = Mathf.MoveTowards(appliedAngle, stepTarget, gearSnapSpeed * Time.fixedDeltaTime);
        if (next == appliedAngle) return;
        transform.Rotate(Vector3.up, next - appliedAngle, Space.World);
        appliedAngle = next;
    }

    /// <summary>연속 출력 기반 장치가 아니라 쓰지 않는다 — <see cref="OnCrankSwing"/> 참고.</summary>
    public void ApplyOutput(float power, float ratio) { }

    public void OnCrankSwing(float direction)
    {
        pendingSteps += Mathf.Sign(direction);
        lastSwingTime = Time.time;
    }
}
