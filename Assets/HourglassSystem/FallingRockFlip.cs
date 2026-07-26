using System.Collections;
using UnityEngine;

/// <summary>
/// 모래시계/낙석에 부착. 충돌 운동량(부딪힌 쪽의 질량 x 상대속도)이 임계값 이상이면
/// 뒤집혀 연결된 SlowZone을 활성화한다.
///
/// 운동량은 "때린 쪽"(플레이어 등, collision.rigidbody)의 질량을 쓴다 - 이 오브젝트
/// 자신의 질량이 아니다. 그래야 네모(3.0)가 가장 쉽게, 세모(1.0)는 그보다 약하게 트리거하는
/// 요구사항의 도형별 차등이 실제로 성립한다. 상대가 Rigidbody가 없는 경우(정적 바닥 등)에는
/// 자기 자신의 질량으로 대체한다.
///
/// 도형별 반발(구가 튕겨나가 조준이 어렵게)은 이 컴포넌트가 아니라 캐릭터 쪽 물리 머티리얼
/// 반발 계수로 만든다 - 여기는 판정/발동만 담당한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FallingRockFlip : MonoBehaviour
{
    public float requiredMomentum = 4f;
    public float cooldown = 2f;
    public float flipDuration = 0.3f;
    public SlowZone targetSlowZone;
    [Tooltip("있으면 \"Flip\" 트리거로 애니메이션 재생. 없으면 코드로 180도 회전.")]
    public Animator animator;

    private Rigidbody rb;
    private float nextAllowedTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (targetSlowZone == null)
            Debug.LogError("[FallingRockFlip] targetSlowZone이 비어있다. Inspector에서 연결해라.", this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextAllowedTime) return;

        float impactMass = collision.rigidbody != null ? collision.rigidbody.mass : rb.mass;
        float momentum = impactMass * collision.relativeVelocity.magnitude;
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
