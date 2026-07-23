using UnityEngine;

/// <summary>
/// Player_Collider(솔리드 콜라이더)에 부착해 접지 여부를 판단한다. 두 신호를 OR로 합친다:
///
/// (1) 실제 충돌 접촉의 법선(월드 스페이스라 물체 회전과 무관)이 위를 향하는지 — OnCollisionStay.
/// (2) 콜라이더 중심에서 월드 아래로 쏘는 SphereCast가 바닥에 닿는지 — 매 FixedUpdate.
///
/// 원래 (1)만 썼지만, 정사면체 솔리드 콜라이더는 단일 Convex MeshCollider + ContinuousDynamic
/// 조합이라 좁은 접촉 패치에서 PhysX가 지속 접촉(OnCollisionStay)을 안정적으로 만들지 못해
/// 접지가 계속 false로 잡혔다("공중" 취급 → 구르기 토크가 안 걸리고 약한 공중 제어만 먹어
/// 면으로 미동만 함). 접촉 콜백에만 의존하는 판정 자체가 이 도형에 부적합했다. (2)는 회전과
/// 무관하게 아래를 확인하므로 그 변덕을 우회한다. 정육면체/구는 (1)만으로도 잘 잡히므로 (2)는
/// 안전망으로만 작동한다.
///
/// [유예 창(grace)] 마지막으로 접지를 확인한 시각으로부터 groundedGraceTime 이내면 접지로
/// 간주한다. 스텝 사이 순간 끊김을 메우고, 점프에는 코요테 타임으로 자연스럽게 작용한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlayerGroundContact : MonoBehaviour
{
    [Tooltip("이 값보다 위쪽을 향한 접촉면/바닥만 '바닥'으로 인정한다 (완전한 수평면 = 1).")]
    public float groundNormalThreshold = 0.5f;

    [Tooltip("마지막 접지 확인 이후 이 시간(초) 동안은 접지로 유지한다(순간 끊김 흡수 + 코요테 타임).")]
    public float groundedGraceTime = 0.1f;

    [Header("아래 방향 SphereCast 보조 판정")]
    [Tooltip("콜라이더 중심에서 아래로 쏘는 구의 반지름. 살짝 기울어 굴러도 바닥을 놓치지 않게 한다.")]
    public float groundCheckRadius = 0.25f;
    [Tooltip("콜라이더 중심에서 아래로 확인할 거리. 도형 절반 높이 + 여유(≈0.6)면 접지/근접을 잡는다.")]
    public float groundCheckDistance = 0.6f;

    // 마지막으로 접지를 확인한 시각. 이 시각으로부터 grace 이내면 접지로 본다.
    private float lastGroundedTime = -999f;

    // 자기 자신(Player_Root 계층 전체)의 콜라이더 — 아래 캐스트가 자기 몸에 맞는 것을 제외한다.
    private Collider[] ownColliders;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    public bool IsGrounded => Time.time - lastGroundedTime <= groundedGraceTime;

    private void Awake()
    {
        ownColliders = transform.root.GetComponentsInChildren<Collider>(true);
    }

    private void FixedUpdate()
    {
        // (2) 아래 방향 SphereCast 보조 판정. 자기 콜라이더는 무시하고, 위를 향한 면에 닿으면 접지.
        int hitCount = Physics.SphereCastNonAlloc(
            transform.position, groundCheckRadius, Vector3.down, hitBuffer,
            groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (System.Array.IndexOf(ownColliders, hitBuffer[i].collider) >= 0) continue;
            if (hitBuffer[i].normal.y > groundNormalThreshold)
            {
                lastGroundedTime = Time.time;
                return;
            }
        }
    }

    // (1) 실제 충돌 접촉 기반 판정(정육면체/구는 이쪽이 주력, 정사면체는 보조).
    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player")) return;

        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > groundNormalThreshold)
            {
                lastGroundedTime = Time.time;
                return;
            }
        }
    }
}
