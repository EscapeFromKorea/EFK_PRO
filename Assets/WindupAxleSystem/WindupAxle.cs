using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 태엽 축. "충전-방전 축적기" — 입력으로 들어온 회전을 저장했다가, 손을 뗀 뒤에도 스스로 풀어내며
/// 연결 장치를 구동한다. 자신에게 무엇이 연결됐는지는 모른 채(<see cref="IWindupReceiver"/> 전체
/// 브로드캐스트로) 신호만 낸다. 입력 방식(패들/드래그/그랩 등)은 이 컴포넌트의 관심사가 아니다 —
/// <see cref="ApplyRotation"/> 하나가 유일한 입력 경계다. 상세: docs/PRD/WindupAxle.md
/// </summary>
public class WindupAxle : MonoBehaviour
{
    [Header("확정값 (PRD §4, 씬 튜닝 대상)")]
    [Tooltip("저장량 최대치. 이 이상 감아도 더 안 쌓인다.")]
    public float maxCharge = 10f;
    [Tooltip("한 번 밀 때 늘어나는 충전량. 너무 낮으면 방전+자연 감쇠로 빠지는 양을 못 이겨서 " +
             "여러 번 밀어도 계속 0 근처에 머문다 — 그럴 땐 이 값을 올려라.")]
    public float chargeRate = 3f;
    [Tooltip("초당 방출 속도. 저장된 힘이 이 속도로 계속 빠지면서 연결 장치를 움직인다 — 손을 " +
             "떼도 저장량이 남아 있는 한 계속된다.")]
    public float dischargeRate = 0.4f;
    [Tooltip("초당 자연 누수량. 방출과 달리 장치를 움직이지 않고 그냥 새는 손실이다.")]
    public float decayRate = 0.1f;
    [Tooltip("충전 비율(0~1)에 따라 출력 세기가 어떻게 변하는지. 직선이면 방출되는 내내 힘이 " +
             "일정하고, 끝에서 처지는 곡선이면 다 풀릴수록 힘이 약해지는 태엽 느낌이 난다.")]
    public AnimationCurve dischargeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("이 값보다 작은 입력은 그냥 무시한다. 손떨림 같은 아주 작은 입력 때문에 방향이 " +
             "엉뚱하게 바뀌는 것을 막기 위함이다.")]
    public float directionDeadzone = 0.01f;

    [Header("이산 이벤트 (저장소 관례 UnityEvent)")]
    public UnityEvent onFullyCharged;
    public UnityEvent onFullyDischarged;

    [Header("시각 피드백 (PRD §9 — 축 자신의 표현, 전부 선택)")]
    [Tooltip("충전된 정도만큼 빛나게 할 렌더러. 비워두면 발광 효과를 안 낸다.")]
    public Renderer bodyRenderer;
    [Tooltip("완전히 충전됐을 때의 발광 색상.")]
    public Color emissionColor = new Color(1f, 0.55f, 0.1f);
    [Tooltip("발광 밝기 배율. 높을수록 완충 시 더 밝게 빛난다.")]
    public float emissionIntensity = 3f;
    [Tooltip("손잡이(크랭크) 막대. 한 번 밀 때마다 crankSwingDegrees만큼 즉시 돌아가서 '지금 " +
             "밀었다'는 것을 바로 보여주는 시각 피드백이다(방출은 느리고 은은해서 즉각적인 " +
             "반응용으로는 안 맞는다). 비워두면 안 돈다.")]
    public Transform crank;
    [Tooltip("손잡이가 목표 각도까지 도는 속도(초당 도). 높을수록 더 빠르고 팍팍하게 돈다.")]
    public float crankDegreesPerSecond = 540f;
    [Tooltip("한 번 밀 때마다 손잡이가 돌아가는 각도(도).")]
    public float crankSwingDegrees = 90f;
    [Tooltip("한 번 밀고 나서 다음 밀기를 받아들이기까지 기다리는 시간(초). 너무 짧으면 손잡이가 " +
             "채 다 돌기도 전에 같은 밀기가 중복으로 감지될 수 있다.")]
    public float crankSwingCooldown = 1f;

    private Material bodyMat;
    private float crankTargetAngle;
    private float crankCurrentAngle;
    private float nextCrankSwingTime;

    /// <summary>부호 있는 저장량. 범위 -maxCharge ~ +maxCharge.</summary>
    public float CurrentCharge { get; private set; }

    /// <summary>이번 프레임에 ApplyRotation이 호출됐는가. 다음 Tick에서 자동으로 꺼진다.</summary>
    public bool IsWinding { get; private set; }

    /// <summary>|CurrentCharge| / maxCharge, 범위 0~1.</summary>
    public float ChargeRatio => maxCharge > 0f ? Mathf.Abs(CurrentCharge) / maxCharge : 0f;

    /// <summary>부호 있는 출력 세기 = dischargeCurve.Evaluate(ChargeRatio) * sign(CurrentCharge).</summary>
    public float OutputPower { get; private set; }

    private readonly List<IWindupReceiver> receivers = new List<IWindupReceiver>();
    private bool wasFullyCharged;
    private bool wasNonZero;

    public void Subscribe(IWindupReceiver receiver)
    {
        if (receiver != null && !receivers.Contains(receiver)) receivers.Add(receiver);
    }

    public void Unsubscribe(IWindupReceiver receiver) => receivers.Remove(receiver);

    void Awake()
    {
        if (bodyRenderer != null)
        {
            bodyMat = bodyRenderer.material; // 인스턴스화 — 원본 공유 머티리얼은 안 건드린다
            bodyMat.EnableKeyword("_EMISSION");
            bodyMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    /// <summary>입력 경계. 조작 방식이 무엇이든 이 메서드를 통해서만 축의 상태에 영향을 준다.
    /// 반대 방향 입력은 기존 충전량을 먼저 상쇄한 뒤 반대 부호로 쌓인다(부호 있는 단일 값이라
    /// 별도 분기가 필요 없다). <see cref="crankSwingCooldown"/> 유예시간 중에는 통째로 무시한다 —
    /// 아암이 물리적으로 도는 동안의 재발화를 막아야 해서, 저장량 반영까지 함께 잠긴다.</summary>
    public void ApplyRotation(float signedDelta)
    {
        if (Mathf.Abs(signedDelta) < directionDeadzone) return;
        if (Time.time < nextCrankSwingTime) return;

        CurrentCharge = ApplyChargeRotation(CurrentCharge, signedDelta, chargeRate, maxCharge, directionDeadzone);
        IsWinding = true;
        crankTargetAngle += Mathf.Sign(signedDelta) * crankSwingDegrees;
        nextCrankSwingTime = Time.time + crankSwingCooldown;

        float swingSign = Mathf.Sign(signedDelta);
        for (int i = 0; i < receivers.Count; i++)
            receivers[i].OnCrankSwing(swingSign);
    }

    void FixedUpdate()
    {
        CurrentCharge = Drain(CurrentCharge, dischargeRate, decayRate, Time.fixedDeltaTime);
        OutputPower = dischargeCurve.Evaluate(ChargeRatio) * (CurrentCharge >= 0f ? 1f : -1f);

        bool fullyCharged = ChargeRatio >= 1f;
        if (fullyCharged && !wasFullyCharged) onFullyCharged.Invoke();
        wasFullyCharged = fullyCharged;

        bool nonZero = CurrentCharge != 0f;
        if (!nonZero && wasNonZero) onFullyDischarged.Invoke();
        wasNonZero = nonZero;

        for (int i = 0; i < receivers.Count; i++)
            receivers[i].ApplyOutput(OutputPower, ChargeRatio);

        if (bodyMat != null)
            bodyMat.SetColor("_EmissionColor", emissionColor * (ChargeRatio * emissionIntensity));

        if (crank != null)
        {
            crankCurrentAngle = Mathf.MoveTowards(crankCurrentAngle, crankTargetAngle,
                crankDegreesPerSecond * Time.fixedDeltaTime);
            crank.localRotation = Quaternion.AngleAxis(crankCurrentAngle, Vector3.up);
        }

        IsWinding = false;
    }

    // ── 순수 함수 (저장소 관례 — PortalSystem의 Self-Check처럼 인스턴스 상태 없이 에디터에서
    //    검증 가능하게 뽑아 둔다. Tools/WindupAxleSystem/Self-Check 참고) ──────────────────────

    public static float ApplyChargeRotation(float currentCharge, float signedDelta, float chargeRate,
        float maxCharge, float directionDeadzone)
    {
        if (Mathf.Abs(signedDelta) < directionDeadzone) return currentCharge;
        return Mathf.Clamp(currentCharge + signedDelta * chargeRate, -maxCharge, maxCharge);
    }

    public static float Drain(float currentCharge, float dischargeRate, float decayRate, float dt)
    {
        if (currentCharge == 0f) return 0f;
        float sign = Mathf.Sign(currentCharge);
        float totalDrain = (dischargeRate + decayRate) * dt;
        return sign * Mathf.Max(0f, Mathf.Abs(currentCharge) - totalDrain);
    }

    /// <summary>PortalSystem의 SelfCheck 관례를 따르는 순수 함수 점검. 실패 항목만 나열하고,
    /// 전부 통과하면 "OK"를 돌려준다.</summary>
    public static string SelfCheck()
    {
        var failures = new List<string>();

        if (ApplyChargeRotation(0f, 0.005f, 1f, 10f, 0.01f) != 0f)
            failures.Add("데드존 미만 입력이 저장량을 바꿨다");

        if (ApplyChargeRotation(9.5f, 5f, 1f, 10f, 0.01f) != 10f)
            failures.Add("오버차지가 clamp되지 않았다");

        // 양수로 감다가 역방향 입력 → 먼저 상쇄, 그 다음에야 반대 부호로 쌓인다(별도 분기 없이).
        float charge = ApplyChargeRotation(3f, -5f, 1f, 10f, 0.01f);
        if (charge >= 0f)
            failures.Add($"반대 방향 입력이 기존 충전량을 상쇄 후 반대 부호로 못 쌓였다 (결과={charge})");

        if (Drain(0f, 1f, 1f, 0.1f) != 0f)
            failures.Add("완전 방전 상태에서 자연 감쇠가 적용됐다");

        float drained = Drain(1f, 1f, 1f, 10f); // 큰 dt로 과소모 유도
        if (drained != 0f)
            failures.Add($"방전이 0 아래로 내려가지 않아야 하는데 {drained}");

        float drainedSign = Drain(-1f, 1f, 0f, 0.1f);
        if (drainedSign >= 0f)
            failures.Add("음수 저장량의 방전이 부호를 보존하지 못했다");

        return failures.Count == 0 ? "OK" : string.Join("\n", failures);
    }
}
