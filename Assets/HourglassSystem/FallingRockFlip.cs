using System.Collections;
using UnityEngine;

/// <summary>
/// 모래시계(낙석 대역 오브젝트)에 부착. 도형 게이트를 통과한 상대가 충분한 운동량
/// (때린 쪽의 질량 x 상대속도)으로 부딪히면 뒤집혀 연결된 SlowZone을 활성화한다.
/// 운동량은 "때린 쪽"(collision.rigidbody)의 질량으로 잰다 - 이 오브젝트 자신의 질량이 아니다.
///
/// [도형 게이트: 구(Sphere)는 모래시계를 발동시키지 못한다 — 낙석 사양 2장]
/// 사양은 "세모나 사각형이 모래시계를 쳐서 시간을 느리게 만들면, 그 틈으로 구가 통과"다. 운동량
/// 임계만으로는 이 분담이 성립하지 않는다: 실제 에셋 스탯으로 계산하면 구 1.5x7=10.5,
/// 세모 1.0x5=5.0, 네모 3.0x3.5=10.5 - 구와 네모가 완전히 같고 세모가 가장 약하다. 임계를 올려
/// 구를 막으려 하면 사양과 반대로 세모부터 잘리고, 그냥 두면 구가 혼자 치고 혼자 통과해 협동
/// 기믹이 성립하지 않는다.
/// 무게 게이트(PlayerWeight)로도 표현할 수 없다 - Assets/CLAUDE.md의 "무게로 게이트" 하드 룰은
/// "무거워서 못 한다"류 판정에만 성립하는데, 여기는 가장 가벼운 세모(1.0)가 통과하고 중간 무게인
/// 구(1.5)가 막혀야 해서 단조 임계 하나로 나눌 수 없다. 그래서 이 판정만 예외적으로 도형 판별
/// 컨벤션(GetComponentInParent&lt;PlayerShapeIdentity&gt;().Kind)을 쓴다 - CompareTag/이름 하드코딩
/// 대신이며, 게이트를 끄면 예전처럼 운동량만으로 판정한다.
///
/// [폐기된 대안: "구는 반발로 튕겨나가 조준이 어렵게"]
/// 역효과다. 반발이 커지면 collision.relativeVelocity가 커져서 운동량 판정을 오히려 더 쉽게
/// 통과한다 - 구를 막는 방향이 아니라 돕는 방향이다. 도형 분담은 물리 머티리얼이 아니라 위
/// 게이트로 만든다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FallingRockFlip : MonoBehaviour
{
    [Header("발동 조건")]
    [Tooltip("발동에 필요한 충돌 운동량(때린 쪽 질량 x 상대속도). 도형 구분은 아래 도형 게이트가 " +
             "담당하므로 이 값은 '살짝 스친 것'만 걸러낸다. 발동 주체 중 가장 약한 세모" +
             "(전속력 1.0x5=5.0) 기준으로 여유를 둔 3.0 - 전속력의 60%로 부딪혀도 발동한다" +
             "(네모는 3.0x3.5=10.5로 넉넉하다).")]
    public float requiredMomentum = 3f;
    [Tooltip("켜면 세모/네모만 모래시계를 발동시킨다 - 구는 막히고(사양 2장의 도형 분담), " +
             "PlayerShapeIdentity가 없는 물체(낙석·상자 등)도 발동시키지 못한다. " +
             "끄면 도형·플레이어 여부와 무관하게 운동량만으로 판정한다 - 사양 5장 6번" +
             "(슬로우 없이도 통과 가능한 난이도인가)을 플레이테스트로 확인할 때 끈다.")]
    public bool useShapeGate = true;

    public float cooldown = 2f;
    public float flipDuration = 0.3f;
    public SlowZone targetSlowZone;
    [Tooltip("있으면 \"Flip\" 트리거로 애니메이션 재생. 없으면 코드로 180도 회전.")]
    public Animator animator;

    private float nextAllowedTime;

    private void Awake()
    {
        if (targetSlowZone == null)
            Debug.LogError("[FallingRockFlip] targetSlowZone이 비어있다. Inspector에서 연결해라.", this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextAllowedTime) return;

        // 때린 쪽에 Rigidbody가 없으면(정적 바닥·벽) 발동 조건 자체가 아니다. 자기 질량으로 대체하면
        // 낙석이 바닥에 떨어지는 것만으로 판정을 통과해(mass 2 x 2m/s = 4.0) 아무도 안 건드렸는데
        // 감속 구역이 켜진다. "때린 쪽의 운동량"은 때린 쪽이 있을 때만 성립하는 값이다.
        if (collision.rigidbody == null) return;

        // 도형이 없는 Rigidbody(낙석·상자 등 플레이어 아닌 물체)도 게이트가 켜져 있으면 발동시키지
        // 못한다 - 사양은 "세모나 사각형이 쳐서" 발동이고, 굴러온 물체가 우연히 켜는 것을 막는 취지가
        // 위 정적 바닥 가드와 같다. 협동을 우회할 다른 경로를 열어두지 않는다.
        if (useShapeGate)
        {
            PlayerShapeIdentity shape = collision.collider.GetComponentInParent<PlayerShapeIdentity>();
            if (shape == null || shape.Kind == PlayerShapeStats.ShapeKind.Sphere) return;
        }

        float momentum = collision.rigidbody.mass * collision.relativeVelocity.magnitude;
        if (momentum < requiredMomentum) return;

        nextAllowedTime = Time.time + cooldown;
        PlayFlip();

        if (targetSlowZone != null)
        {
            targetSlowZone.Activate();
            Debug.Log($"[FallingRockFlip] '{name}' 충돌 운동량 {momentum:F2} (>= {requiredMomentum}) - 뒤집기 + 감속 구역 발동");
        }
    }

    private void PlayFlip()
    {
        if (animator != null)
        {
            animator.SetTrigger("Flip");
            return;
        }
        StopAllCoroutines();
        StartCoroutine(FlipRoutine());
    }

    private IEnumerator FlipRoutine()
    {
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(180f, 0f, 0f);
        float t = 0f;
        while (t < flipDuration)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(start, end, t / flipDuration);
            yield return null;
        }
        transform.rotation = end;
    }
}
