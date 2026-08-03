using UnityEngine;

/// <summary>
/// 플레이어의 Rigidbody에 부착. WindZone과 겹쳐있는 동안 그 방향으로 지속적으로 밀리는 힘을
/// 받는다. AccelPad의 순간 부스트와 달리 조작을 대신 넘겨받지 않는다 — PlayerMover(또는 토크
/// 구르기 경로)가 만든 velocity 위에 바람 세기만 더하고, 그 외의 조향/이동은 그대로 둔다.
///
/// [PlayerMover.useTorqueRolling에 따라 적용 방식이 다른 이유]
/// 구(Sphere, 레거시 경로)는 PlayerMover가 매 FixedUpdate 수평 velocity를 입력 기반 값으로
/// 통째로 다시 대입해 이전 프레임에 더한 바람을 스스로 지운다 — 그래서 이 스크립트가 나중에
/// 실행되며(DefaultExecutionOrder) 매 프레임 목표 바람 세기 전체를 다시 더하면 된다. 반면
/// 정육면체/정사면체(토크 구르기 경로)는 velocity를 대입하지 않고 AddForce/AddTorque로
/// 유지·누적하므로, 매 프레임 전체를 또 더하면 있는 그대로 계속 쌓여 폭주한다 — 그래서 "이전에
/// 더한 만큼만 갱신"하는 차분 방식으로 순수 오프셋만 유지한다.
///
/// [리스폰 중에는 완전히 개입하지 않는다 — 그 외에는 최대한 정상 개입한다 (2026-08-03 정정)]
/// `Rigidbody.isKinematic`(리스폰 페이드 전체 구간)이거나, `ExternallyDriven`이 세워져 있는데
/// 실타래에 매달린 상태가 **아닌** 경우(=리스폰의 낙하 리스폰 구간. 조인트 없이 낙하 중 조작만
/// 차단한다)에는 바람을 완전히 끊고 내부 상태(`currentPush`)도 0으로 리셋한다 — 안 그러면 리스폰
/// 도중(kinematic이라 실제로는 안 움직여도) `currentPush`가 계속 목표치까지 램프업되다가, 리스폰이
/// 끝나 조작이 돌아오는 순간 그동안 쌓인 세기가 한꺼번에 터져 나간다.
///
/// 반면 **실타래에 매달린 동안은 막지 않는다** — `ExternallyDriven`은 매달림·리스폰이 공유하는
/// 신호라 이것만으로는 못 가르지만, 매달림은 `DreamThreadController`가 몸에 `ConfigurableJoint`를
/// 붙였다 떼는 방식이라 그 컴포넌트 존재 여부로 "지금 매달려 있는가"를 정확히 가려낼 수 있다
/// (DreamThreadSystem 파일은 건드리지 않고 컴포넌트 존재만 읽는다). 매달린 동안 바람을 막지
/// 않는 이유는 "바람이 부는 실타래 위에서는 그네가 흔들려야 자연스럽다"는 상호작용을 살리기
/// 위함이며, 대신 **airMultiplier는 적용하지 않는다** — 매달림은 `IsGrounded()`가 항상
/// false(공중 판정)라 그대로 두면 배율까지 겹쳐 과가속으로 이어졌던 것이 실제 신고된 버그였다.
///
/// [2026-08-04 추가, 같은 날 재조정] 배율만 빼도 매달린 채로는 여전히 많이 돈다는 플레이테스트
/// 피드백으로, 매달린 동안은 기본 세기 자체도 `hangingWindMultiplier`만큼 더 줄인다. 진자
/// 운동(ConfigurableJoint)은 조향으로 힘을 상쇄할 수 없는 만큼, 걷거나 구를 때보다 같은 세기의
/// 바람에도 훨씬 쉽게 흔들려 통제를 잃는다 — 그 차이를 흡수하는 별도 노브다. 처음엔 0.25로
/// 잡았으나 실제 플레이테스트로 그 정도로는 부족해 **0.007**까지 내려야 원하는 느낌이 났다
/// (진자가 힘에 얼마나 민감한지가 직관보다 훨씬 컸다는 뜻이라, 이 수치 자체가 튜닝 근거다).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(150)]
public class PlayerWindReceiver : MonoBehaviour
{
    [Tooltip("바람 세기가 목표치까지 붙는 가속도(m/s²). 0이면 즉시 목표 세기로 스냅.")]
    public float rampAccel = 12f;

    [Range(0f, 1f)]
    [Tooltip("실타래에 매달린 동안 바람 세기에 곱하는 배율(0~1). 진자 운동은 조향으로 힘을 " +
        "상쇄할 수 없어 같은 세기에도 훨씬 쉽게 통제를 잃으므로, 걷거나 구를 때보다 더 약하게 " +
        "받는다. airMultiplier와는 별개다 — 매달림 중에는 airMultiplier를 아예 적용하지 않고 " +
        "이 배율만 적용한다. 기본값 0.007은 플레이테스트로 확정한 값이다(0.25는 부족했다).")]
    public float hangingWindMultiplier = 0.007f;

    private Rigidbody rb;
    private PlayerMover mover;
    private PlayerShapeController shapeController;

    private Vector3 targetPush;
    private Vector3 currentPush;
    private Vector3 lastAppliedPush;
    private bool insideZoneThisStep;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mover = GetComponent<PlayerMover>();
        shapeController = GetComponent<PlayerShapeController>();
    }

    /// <summary>WindZone이 겹쳐있는 동안 매 물리 스텝(OnTriggerStay) 호출한다.
    /// baseWindVelocity = 구역 forward * windSpeed, airMultiplier = 공중일 때 곱할 배율(≥1 권장).</summary>
    public void SetWindTarget(Vector3 baseWindVelocity, float airMultiplier)
    {
        bool hanging = GetComponent<ConfigurableJoint>() != null;
        if (hanging)
        {
            // 배율(airMultiplier)은 적용하지 않되, 기본 세기 자체를 줄인다 — 진자는 조향으로
            // 힘을 못 상쇄해 같은 세기에도 훨씬 쉽게 통제를 잃는다(플레이테스트로 확인).
            targetPush = baseWindVelocity * Mathf.Clamp01(hangingWindMultiplier);
        }
        else
        {
            bool grounded = shapeController == null || shapeController.IsGrounded();
            targetPush = grounded ? baseWindVelocity : baseWindVelocity * Mathf.Max(1f, airMultiplier);
        }
        insideZoneThisStep = true;
    }

    private void FixedUpdate()
    {
        bool hanging = GetComponent<ConfigurableJoint>() != null;
        bool respawning = mover != null && mover.ExternallyDriven && !hanging;
        if (rb.isKinematic || respawning)
        {
            currentPush = Vector3.zero;
            lastAppliedPush = Vector3.zero;
            insideZoneThisStep = false;
            return;
        }

        currentPush = insideZoneThisStep
            ? (rampAccel > 0f ? Vector3.MoveTowards(currentPush, targetPush, rampAccel * Time.fixedDeltaTime) : targetPush)
            : Vector3.zero; // 구역을 벗어나 이번 스텝에 갱신이 없었으면 즉시 제거
        insideZoneThisStep = false;

        bool accumulates = mover != null && mover.useTorqueRolling;
        Vector3 delta = accumulates ? (currentPush - lastAppliedPush) : currentPush;
        lastAppliedPush = currentPush;

        if (delta.sqrMagnitude < 0.0001f) return;
        rb.velocity = new Vector3(rb.velocity.x + delta.x, rb.velocity.y, rb.velocity.z + delta.z);
    }
}
