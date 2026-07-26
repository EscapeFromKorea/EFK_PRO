using UnityEngine;

/// <summary>
/// 대상 Rigidbody 하나의 중력을 "개별로" 오버라이드한다. 기믹(무중력 버블, 추후 모래시계 감속
/// 구역 등)이 중력을 조정해야 할 때 이 컴포넌트 하나를 거치면 된다 — PlayerJump.LaunchToHeight가
/// "발사"의 단일 창구이듯, 이건 "중력 변경"의 단일 창구다.
///
/// [왜 PlayerSystem에 있나 — 원래 ZeroGravityBubbleSystem에 있었다 (2026-07-26 이동)]
/// 위 주석대로 처음부터 "여러 기믹이 공유하는 단일 창구"로 설계된 물건이라, 특정 기믹 폴더에 두면
/// 의존이 거꾸로 흐른다: PlayerJump가 실효 중력을 알아야 하고(√(2gH) 역산), PlayerWeight가 무게를
/// 재야 하는데(질량 × 실효 중력 배율) 둘 다 코어라 **코어 → 기믹** 참조가 생겼다. 중력 오버라이드는
/// 버블의 소유물이 아니라 플레이어 바디의 물리 속성이므로 코어로 옮겼다. 이제 버블·구름 트램펄린·
/// 실타래·문 어느 기믹이든 **기믹 → 코어** 한 방향으로만 참조한다.
///
/// 전역 Physics.gravity는 절대 건드리지 않는다(다른 플레이어까지 영향받는 사고 방지). 대신 이
/// 오브젝트만 useGravity를 끄고, 매 FixedUpdate에 원하는 배율만큼 직접 중력 가속도를 얹는다
/// (AddForce, Acceleration 모드) — 질량이 개입하지 않아 배율이 곧 실제 체감 배율이 된다.
///
/// 안전 복원: OnDisable/OnDestroy에서 즉시 원상 복구해, 파괴·리스폰·씬 전환 어느 경로로 빠져나가도
/// 중력이 꺼진 채로 영구 부양하는 사고를 막는다.
///
/// 씬에 미리 붙여둘 필요 없다 — 기믹이 대상에 이 컴포넌트가 없으면 그 자리에서 자동으로 붙인다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerGravityOverride : MonoBehaviour
{
    private Rigidbody rb;
    private float defaultDrag;

    private bool overriding;
    private float currentScale = 1f;
    private float targetScale = 1f;
    private float lerpSpeed;
    private float uplift;
    private float jumpMultiplier = 1f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        defaultDrag = rb.drag;
    }

    void OnEnable() => ForceRestoreImmediate();
    void OnDisable() => ForceRestoreImmediate();
    void OnDestroy() => ForceRestoreImmediate();

    /// <summary>중력 배율을 scale로, 저항을 기본값 x dragMultiplier로 transitionTime에 걸쳐 바꾼다.
    /// uplift는 중력 감소와 별개로 추가로 미는 위쪽 가속도(선택, 기본 0 = 순수 감소만).
    /// jumpMultiplier는 구역 안에 있는 동안 점프 목표 높이에 곱할 배율(1이면 평소와 같은 높이).</summary>
    public void SetGravityScale(float scale, float dragMultiplier, float uplift, float transitionTime,
                                float jumpMultiplier)
    {
        overriding = true;
        rb.useGravity = false;
        targetScale = scale;
        lerpSpeed = transitionTime > 0f ? 1f / transitionTime : 999f;
        rb.drag = defaultDrag * dragMultiplier;
        this.uplift = uplift;
        this.jumpMultiplier = jumpMultiplier;
    }

    /// <summary>지금 이 바디의 점프 목표 높이에 곱할 배율. 구역 밖이면 1.
    /// PlayerJump.LaunchToHeight가 √(2gH)의 H에 이 값을 곱한다 — 중력을 낮춰 "체공이 길어지는" 것과
    /// "더 높이 뛰는" 것은 별개라, 도달 높이는 이 값 하나로만 결정되게 분리해 둔다.</summary>
    public float JumpHeightMultiplier => overriding ? jumpMultiplier : 1f;

    /// <summary>지금 이 바디에 실제로 걸리는 아래 방향 가속도(m/s²). 오버라이드 중이 아니면 전역 중력 그대로.
    /// PlayerJump가 velocity.y = √(2gH) 역산에 쓸 g가 바로 이 값이다 — 전역 중력으로 역산하면
    /// 버블 안에서 목표 높이를 배율의 역수만큼 초과한다(배율 0.16 → 약 6.25배).</summary>
    public float EffectiveGravityMagnitude
    {
        get
        {
            float g = Mathf.Abs(Physics.gravity.y);
            if (!overriding) return g;
            // ponytail: uplift가 중력을 넘어서면 유한한 도달 높이 자체가 없다. 그때는 역산이 발산하지
            // 않게 전역 중력의 10%로 하한을 둔다(도달 높이 최대 약 3.16배). 정확한 처리가 필요해지면
            // 그때 uplift 구간 전용 발사 공식을 따로 만든다 — upliftAcceleration 기본값은 0이다.
            return Mathf.Max(g * 0.1f, g * currentScale - uplift);
        }
    }

    /// <summary>정상 중력(배율 1)/기본 저항으로 되돌린다. 구역 이탈 시 호출.</summary>
    public void RestoreDefault(float transitionTime)
    {
        if (!overriding) return; // 이미 정상이면 할 일 없음.
        targetScale = 1f;
        lerpSpeed = transitionTime > 0f ? 1f / transitionTime : 999f;
        rb.drag = defaultDrag;
        uplift = 0f;
        jumpMultiplier = 1f; // 구역을 벗어난 순간 점프 강화는 바로 끊는다(중력 복원 전이와 무관하게).
    }

    void FixedUpdate()
    {
        if (!overriding) return;
        // 세모 벽 부착(ThreadPinPlacer)이 isKinematic=true로 바꿔둔 동안에는 AddForce가 에러다.
        // 배율은 그대로 얼려두고 넘어간다 — 탈착해서 다시 다이내믹이 되면 이어서 적용된다.
        if (rb.isKinematic) return;

        currentScale = Mathf.MoveTowards(currentScale, targetScale, lerpSpeed * Time.fixedDeltaTime);
        rb.AddForce(Physics.gravity * currentScale + Vector3.up * uplift, ForceMode.Acceleration);

        if (Mathf.Approximately(targetScale, 1f) && Mathf.Approximately(currentScale, 1f))
            ForceRestoreImmediate();
    }

    private void ForceRestoreImmediate()
    {
        overriding = false;
        currentScale = 1f;
        targetScale = 1f;
        uplift = 0f;
        jumpMultiplier = 1f;
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        rb.drag = defaultDrag;
    }
}
