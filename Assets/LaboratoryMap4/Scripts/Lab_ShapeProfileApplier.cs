using UnityEngine;

/// <summary>
/// Player Variant 프리팹에 부착(F1-1 "적용기"). Awake에서 Lab_ShapeProfile 값을 팀 컴포넌트
/// 필드에 적용한다.
///
/// [DefaultExecutionOrder(-100)] — PlayerShapeIdentity(기본 실행 순서 0, Start()에서 stats를 읽어
/// rb.mass/mover.moveSpeed/jump.jumpHeight/콜라이더 물리 머티리얼에 대입, PlayerShapeIdentity.cs:23-52)
/// 보다 반드시 먼저 이 컴포넌트의 Awake가 실행되도록 한다. Unity의 스크립트 실행 순서 설정은
/// Awake·OnEnable·Start·Update 등에 모두 적용된다(PlayerJump.cs:13의 [DefaultExecutionOrder(10)]과
/// 같은 전제를 그대로 따름).
/// </summary>
[DefaultExecutionOrder(-100)]
public class Lab_ShapeProfileApplier : MonoBehaviour
{
    public Lab_ShapeProfile profile;

    void Awake()
    {
        if (profile == null)
        {
            Debug.LogError($"[Lab_ShapeProfileApplier] '{name}'에 profile이 지정되지 않았다 — 적용할 값이 없다.");
            return;
        }

        ApplyStats();
        ApplyRigidbody();
        ApplyMover();
        ApplyGroundContact();
    }

    private void ApplyStats()
    {
        PlayerShapeIdentity identity = GetComponent<PlayerShapeIdentity>();
        if (identity == null)
        {
            Debug.LogError($"[Lab_ShapeProfileApplier] '{name}'에 PlayerShapeIdentity가 없다 — Base 프리팹이 " +
                            "팀 생성기(Tools/PlayerSystem/Create Player/*)로 만들어졌는지 확인해라.");
            return;
        }
        if (profile.statsAsset != null)
            identity.stats = profile.statsAsset; // PlayerShapeIdentity.Start()가 이후 이 참조를 읽어 적용한다.
    }

    private void ApplyRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) return;

        rb.constraints = profile.constraints;
        rb.angularDrag = profile.angularDrag;
        rb.maxAngularVelocity = profile.maxAngularVelocity;
        rb.interpolation = profile.interpolation;
        // [F1 재작업 판정 C1] 예전엔 "Discrete가 아닐 때만" 대입하는 단방향 가드가 있었다 —
        // Discrete(enum 기본값 0)를 명시적으로 걸고 싶은 프로필도 있을 수 있는데(도형별로 값이
        // 다를 수 있어야 한다는 F1-1 명세), 그 경우 이 가드가 항상 대입을 막아버렸다. 이제 항상
        // 그대로 대입한다 — 명세 "구도 ContinuousDynamic"은 Lab_SphereProfile 에셋의 값으로
        // 표현한다(Lab_PlayerBuilder.EnsureProfile 기본값 참고).
        rb.collisionDetectionMode = profile.collisionDetectionMode;
        if (profile.solverIterations > 0) rb.solverIterations = profile.solverIterations;
        if (profile.solverVelocityIterations > 0) rb.solverVelocityIterations = profile.solverVelocityIterations;
    }

    private void ApplyMover()
    {
        PlayerMover mover = GetComponent<PlayerMover>();
        if (mover == null) return;
        if (profile.airControlMultiplierOverride > 0f) mover.airControlMultiplier = profile.airControlMultiplierOverride;
        if (profile.rollRadiusOverride > 0f) mover.rollRadius = profile.rollRadiusOverride;
    }

    private void ApplyGroundContact()
    {
        PlayerGroundContact contact = GetComponentInChildren<PlayerGroundContact>();
        if (contact == null) return;
        if (profile.groundCheckRadiusOverride > 0f) contact.groundCheckRadius = profile.groundCheckRadiusOverride;
        if (profile.groundCheckDistanceOverride > 0f) contact.groundCheckDistance = profile.groundCheckDistanceOverride;
    }
}
