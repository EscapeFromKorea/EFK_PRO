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
    [Tooltip("콜라이더 중심에서 아래로 쏘는 구의 반지름(Normal 스케일 기준). 살짝 기울어 굴러도 " +
             "바닥을 놓치지 않게 한다 — 실제로 캐스트에 쓰는 값은 FixedUpdate가 이 값에 현재 " +
             "transform.lossyScale.y를 곱해서 구한다(아래 '2026-08-31 ScalePad 연동' 주석 참고).")]
    public float groundCheckRadius = 0.25f;
    [Tooltip("콜라이더 중심에서 아래로 확인할 거리(Normal 스케일 기준). 도형 절반 높이 + 여유(≈0.6)면 " +
             "접지/근접을 잡는다 — 실제로 캐스트에 쓰는 값은 위와 같은 방식으로 스케일된다.")]
    public float groundCheckDistance = 0.6f;

    // 마지막으로 접지를 확인한 시각. 이 시각으로부터 grace 이내면 접지로 본다.
    private float lastGroundedTime = -999f;

    // 자기 자신(Player_Root 계층 전체)의 콜라이더 — 아래 캐스트가 자기 몸에 맞는 것을 제외한다.
    private Collider[] ownColliders;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    public bool IsGrounded => Time.time - lastGroundedTime <= groundedGraceTime;

    /// <summary>마지막으로 확인된 바닥 법선(둘 중 어느 판정이 잡았든). 평지 기본값은 Vector3.up —
    /// PlayerMover의 경사면 롤링(레거시 경로, 구 전용)이 자기 raycast를 새로 쏘는 대신 이 값을
    /// 읽는다. 이 컴포넌트는 이미 ownColliders로 자기 자신을 제외하고 판정하므로, PlayerMover가
    /// 별도 raycast를 쏘면 자기 자신/다른 플레이어에 걸려 불안정한 법선을 얻을 위험이 있었다
    /// (2026-08-05 실제로 이동이 구불거리는 버그로 드러남 — 판정 소스를 하나로 합쳤다).</summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.up;

    private void Awake()
    {
        ownColliders = transform.root.GetComponentsInChildren<Collider>(true);
    }

    private void FixedUpdate()
    {
        // 2026-08-31 ScalePad 연동 — groundCheckRadius/groundCheckDistance는 원래 절대(월드) 단위
        // 고정값이라 ScalePad로 축소(Shrunk)된 플레이어에게는 자기 몸 크기 대비 과도하게 긴
        // 캐스트가 됐다(예: 반높이 0.25인 축소 정육면체에 0.6유닛 캐스트 = 자기 크기의 2.4배).
        // CatapultSystem의 미니 투석기(버킷/팔이 전부 조밀하게 붙어 있다)에서 발사 직후 이 캐스트가
        // 투석기 자신의 구조물에 걸려 "착지"로 오판되고, `CatapultBucket.Update()`가 그 순간
        // `PlayerMover.ExternallyDriven`을 풀어버려 발사 속도(수평 성분)가 `PlayerMover.
        // DampWhenUncontrolled()`에 조용히 감쇠당하는 버그로 실측됐다(진단 로그로 확인 —
        // `CatapultArm.cs`/`CatapultBucket.cs`는 건드리지 않는다, 원인이 이 컴포넌트에 있었다).
        // Player_Collider(이 컴포넌트가 붙는 곳)는 Player_Root의 자식이라, PlayerShapeController가
        // Root에 적용하는 배율이 `transform.lossyScale`에 그대로 반영된다 — 그 배율만큼 캐스트
        // 반지름/거리를 함께 줄이면(Grown 상태는 늘어나면) 캐스트가 항상 "지금 이 몸 크기 기준"으로
        // 일관되게 반응한다. 스케일이 0/음수가 될 일은 없지만(PlayerShapeController.minScale=0.2가
        // 하한을 보장) 방어적으로 최소값을 둔다.
        float scale = Mathf.Max(0.01f, transform.lossyScale.y);
        float scaledRadius = groundCheckRadius * scale;
        float scaledDistance = groundCheckDistance * scale;

        // (2) 아래 방향 SphereCast 보조 판정. 자기 콜라이더는 무시하고, 위를 향한 면에 닿으면 접지.
        int hitCount = Physics.SphereCastNonAlloc(
            transform.position, scaledRadius, Vector3.down, hitBuffer,
            scaledDistance, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (System.Array.IndexOf(ownColliders, hitBuffer[i].collider) >= 0) continue;
            if (hitBuffer[i].normal.y > groundNormalThreshold)
            {
                lastGroundedTime = Time.time;
                GroundNormal = hitBuffer[i].normal;
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
                GroundNormal = contact.normal;
                return;
            }
        }
    }
}
