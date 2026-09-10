using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 관성 축전기. 태엽 축(<see cref="WindupAxle"/>)과 같은 "충전-방전 축적기"지만, 입력이 손잡이
/// 회전이 아니라 <b>충돌 운동량</b>이다. 물체가 부딪히면 그 충격량을 저장했다가, 손을 뗀 뒤에도
/// 스스로 풀어내며 연결 장치를 구동한다. 자신에게 무엇이 연결됐는지는 모른 채
/// (<see cref="IWindupReceiver"/> 전체 브로드캐스트로) 신호만 낸다 — 태엽 축용으로 만든 수신자
/// (레일카·회전판 등)가 코드 수정 없이 그대로 붙는다.
///
/// [입력 경계] 충전에 영향을 주는 경로는 <see cref="AddImpulse"/> 하나뿐이다. OnCollisionEnter는
/// 그 얇은 래퍼일 뿐이라, 다른 기믹이 임의 충전을 주입하고 싶으면 AddImpulse만 호출하면 된다.
///
/// [중복 충전 방지] 같은 Rigidbody는 OnCollisionEnter 1회 + rechargeCooldown 동안만 충전한다.
/// OnCollisionExit로 접촉이 끊기면 다시 충전 가능 — "계속 닿아 있는 동안 중복 누적 없음" 요구.
///
/// [폭주 억제] charge는 항상 0~maxCharge로 클램프한다. 최대 충전 상태의 추가 충돌은 클램프로
/// 흡수된다. 전역 Physics.* 미변경(MP-01). 축전기 자신은 정적 콜라이더라 부딪혀도 안 밀린다.
///
/// [도형 차등 없음] 무거울수록·빠를수록 Collision.impulse가 커져 충전량이 자연히 달라진다.
/// 포탈을 통과한 고속 물체도 impulse가 그만큼 커서 대량 충전된다(maxImpulsePerHit로 상한 선택).
/// </summary>
public class InertiaCapacitor : MonoBehaviour
{
    [Header("충전 (입력: 충돌 운동량)")]
    [Tooltip("저장량 최대치. 이 이상은 안 쌓인다.")]
    public float maxCharge = 10f;
    [Tooltip("Collision.impulse 크기에 이 값을 곱해 충전량으로 환산한다. 여러 번 부딪혀도 계속 " +
             "0 근처면 이 값을 올려라(제안값, 그레이박스 실측 튜닝 대상).")]
    public float chargeGainPerImpulse = 0.15f;
    [Tooltip("이보다 작은 충격량은 무시한다 — 스침·미세 접촉과 실제 충돌을 구분한다.")]
    public float minImpulseToCharge = 1.5f;
    [Tooltip("같은 Rigidbody가 다시 충전 판정을 받기까지의 최소 간격(초). 접촉이 끊기면(Exit) 즉시 초기화된다.")]
    public float rechargeCooldown = 0.4f;
    [Tooltip("한 번의 충돌로 더할 수 있는 충전량 상한. 0이면 무제한(포탈 고속 물체 대량 충전). " +
             "글리치성 단발 폭주만 막고 싶을 때 값을 넣는다.")]
    public float maxChargePerHit = 0f;

    [Header("방전 (출력)")]
    [Tooltip("초당 방출 속도. 저장량이 이 속도로 빠지면서 연결 장치를 구동한다 — 충돌이 멈춰도 " +
             "저장량이 남아 있는 한 계속된다. 0이면 유지(leakPerSecond만 적용).")]
    public float drainPerSecond = 0.8f;
    [Tooltip("초당 자연 누수량. 방출과 달리 장치를 구동하지 않고 그냥 새는 손실이다.")]
    public float leakPerSecond = 0.1f;
    [Tooltip("충전 비율(0~1)에 따라 출력 세기가 어떻게 변하는지. 직선이면 방출 내내 일정, 끝에서 " +
             "처지는 곡선이면 다 풀릴수록 약해진다.")]
    public AnimationCurve dischargeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("완충 시 출력 세기(IWindupReceiver.ApplyOutput의 power 상한).")]
    public float maxOutputPower = 10f;
    [Tooltip("0이면 연속 출력. 1 이상이면 충전 비율을 이 개수의 단계로 양자화해 '단계적' 작동을 만든다.")]
    public int outputSteps = 0;

    [Header("한 번에 방출 (버스트 — 선택)")]
    [Tooltip("이 키를 누르면 Discharge()가 호출돼 저장량을 빠르게 쏟아낸다. None이면 비활성.")]
    public KeyCode dischargeKey = KeyCode.None;
    [Tooltip("버스트가 지속되는 시간(초).")]
    public float burstSeconds = 0.6f;
    [Tooltip("버스트 동안 drainPerSecond에 곱해지는 배율.")]
    public float burstMultiplier = 6f;

    [Header("연결 (1:N, CM-01)")]
    [Tooltip("출력을 받을 대상. IWindupReceiver를 구현한 컴포넌트만 Start에서 구독된다. " +
             "씬 뷰포트에서 자유 교체.")]
    public MonoBehaviour[] outputTargets;

    /// <summary>Inspector에 노출되는 float 인자 이벤트(제네릭 UnityEvent&lt;float&gt;는 콘크리트
    /// 서브클래스가 있어야 직렬화된다).</summary>
    [System.Serializable]
    public class ChargeRatioEvent : UnityEvent<float> { }

    [Header("이산 이벤트 (저장소 관례 UnityEvent)")]
    public UnityEvent onFullyCharged;
    public UnityEvent onFullyDischarged;
    public ChargeRatioEvent onChargeRatioChanged;

    [Header("시각/청각 피드백 (전부 선택)")]
    [Tooltip("충전된 정도만큼 빛나게 할 렌더러. 비워두면 발광 안 함.")]
    public Renderer bodyRenderer;
    [Tooltip("완전히 충전됐을 때의 발광 색상.")]
    public Color emissionColor = new Color(0.3f, 0.7f, 1f);
    [Tooltip("발광 밝기 배율.")]
    public float emissionIntensity = 3f;
    [Tooltip("충전 비율에 따라 pitch가 오르는 루프 사운드. 비워두면 소리 없음.")]
    public AudioSource chargeHum;
    [Tooltip("chargeHum의 pitch 범위 (비율 0 → x, 비율 1 → y).")]
    public Vector2 humPitchRange = new Vector2(0.6f, 1.6f);

    [Header("초기화")]
    [Tooltip("ResetCharge() 호출 시 저장량을 0으로 비운다. false면 유지한다.")]
    public bool resetClearsCharge = true;

    /// <summary>저장량(0 ~ maxCharge).</summary>
    public float CurrentCharge { get; private set; }

    /// <summary>CurrentCharge / maxCharge, 0~1.</summary>
    public float ChargeRatio => maxCharge > 0f ? Mathf.Clamp01(CurrentCharge / maxCharge) : 0f;

    /// <summary>현재 출력 세기 = dischargeCurve.Evaluate(양자화된 ChargeRatio) * maxOutputPower.</summary>
    public float OutputPower { get; private set; }

    private readonly List<IWindupReceiver> receivers = new List<IWindupReceiver>();
    private readonly Dictionary<Rigidbody, float> nextChargeTime = new Dictionary<Rigidbody, float>();

    private Material bodyMat;
    private float burstUntil;
    private bool wasFullyCharged;
    private bool wasNonZero;
    private float lastReportedRatio = -1f;

    public void Subscribe(IWindupReceiver receiver)
    {
        if (receiver != null && !receivers.Contains(receiver)) receivers.Add(receiver);
    }

    public void Unsubscribe(IWindupReceiver receiver) => receivers.Remove(receiver);

    /// <summary>충전 입력 경계. impulseMagnitude = 이번 충돌의 운동량 크기(Collision.impulse.magnitude 등).</summary>
    public void AddImpulse(float impulseMagnitude)
    {
        if (impulseMagnitude < minImpulseToCharge) return;

        float gain = impulseMagnitude * chargeGainPerImpulse;
        if (maxChargePerHit > 0f) gain = Mathf.Min(gain, maxChargePerHit);

        CurrentCharge = Mathf.Clamp(CurrentCharge + gain, 0f, maxCharge);
    }

    /// <summary>버스트 방출을 시작한다(한 번에 쏟아내기). 수동 키 또는 외부 기믹이 호출.</summary>
    public void Discharge()
    {
        burstUntil = Time.time + Mathf.Max(0f, burstSeconds);
        for (int i = 0; i < receivers.Count; i++)
            receivers[i].OnCrankSwing(1f);
    }

    /// <summary>저장량을 초기화한다(퍼즐 리셋 시스템이 호출). resetClearsCharge가 false면 무시.</summary>
    public void ResetCharge()
    {
        if (!resetClearsCharge) return;
        CurrentCharge = 0f;
        burstUntil = 0f;
        nextChargeTime.Clear();
    }

    private void Awake()
    {
        if (outputTargets != null)
        {
            foreach (MonoBehaviour mb in outputTargets)
                if (mb is IWindupReceiver r) Subscribe(r);
        }

        if (bodyRenderer != null)
        {
            bodyMat = bodyRenderer.material; // 인스턴스화 — 공유 머티리얼은 안 건드린다
            bodyMat.EnableKeyword("_EMISSION");
            bodyMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    private void Update()
    {
        if (dischargeKey != KeyCode.None && Input.GetKeyDown(dischargeKey))
            Discharge();
    }

    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody rb = collision.rigidbody;
        if (rb == null) return;

        if (nextChargeTime.TryGetValue(rb, out float t) && Time.time < t) return;
        nextChargeTime[rb] = Time.time + Mathf.Max(0f, rechargeCooldown);

        AddImpulse(collision.impulse.magnitude);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.rigidbody != null) nextChargeTime.Remove(collision.rigidbody);
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // 방출(장치 구동) + 자연 누수. 버스트 중이면 방출만 가속.
        float drain = drainPerSecond * (Time.time < burstUntil ? burstMultiplier : 1f);
        CurrentCharge = Mathf.Max(0f, CurrentCharge - (drain + leakPerSecond) * dt);

        float ratio = ChargeRatio;
        float shaped = outputSteps > 0
            ? Mathf.Round(ratio * outputSteps) / outputSteps
            : ratio;
        OutputPower = dischargeCurve.Evaluate(shaped) * maxOutputPower;

        bool fullyCharged = ratio >= 1f;
        if (fullyCharged && !wasFullyCharged) onFullyCharged.Invoke();
        wasFullyCharged = fullyCharged;

        bool nonZero = CurrentCharge > 0f;
        if (!nonZero && wasNonZero) onFullyDischarged.Invoke();
        wasNonZero = nonZero;

        if (!Mathf.Approximately(ratio, lastReportedRatio))
        {
            onChargeRatioChanged.Invoke(ratio);
            lastReportedRatio = ratio;
        }

        for (int i = 0; i < receivers.Count; i++)
            receivers[i].ApplyOutput(OutputPower, ratio);

        if (bodyMat != null)
            bodyMat.SetColor("_EmissionColor", emissionColor * (ratio * emissionIntensity));

        if (chargeHum != null)
            chargeHum.pitch = Mathf.Lerp(humPitchRange.x, humPitchRange.y, ratio);
    }
}
