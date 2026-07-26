using UnityEngine;

/// <summary>
/// 플레이어의 점프를 담당한다. "Jump" 입력 축(기본 Space)을 사용한다.
/// 같은 오브젝트에 ScalingSystem의 PlayerShapeController가 있으면 그쪽의 접지 판정을 재사용하고,
/// 없으면 자체 레이캐스트로 접지 여부를 판단한다.
/// JumpSystem/JumpPad.cs 등 외부 발사 기믹이 호출하는 LaunchToHeight(H)도 제공한다.
/// PlayerShapeController(기본 실행 순서 0)가 이번 프레임의 IsGrounded()를 먼저 갱신한
/// 뒤에 이 컴포넌트가 그 값을 읽도록, Project Settings의 Script Execution Order 수동
/// 설정 여부와 무관하게 코드로 순서를 보장한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(10)]
public class PlayerJump : MonoBehaviour
{
    [Header("점프 설정")]
    [Tooltip("목표 점프 높이 H(Unit). velocity.y = √(2·g·H)로 역산해 대입한다(AddForce·가산 아님). " +
             "질량이 개입하지 않아 도형 질량과 무관하게 정확히 이 높이까지 오른다.")]
    public float jumpHeight = 1.6f;

    [Header("자체 접지 판정 (PlayerShapeController가 없을 때만 사용)")]
    [Tooltip("바닥 감지 Raycast 거리")]
    public float groundCheckDistance = 0.15f;
    [Tooltip("바닥으로 인정할 레이어 마스크")]
    public LayerMask groundLayer = ~0;

    private Rigidbody rb;
    private PlayerMover mover;
    private PlayerShapeController shapeController;
    private bool jumpQueued;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mover = GetComponent<PlayerMover>();
        shapeController = GetComponent<PlayerShapeController>();

        if (shapeController == null)
            groundLayer &= ~(1 << gameObject.layer);
    }

    void Update()
    {
        if (mover != null && !mover.IsControlled) return;
        if (Input.GetButtonDown("Jump"))
            jumpQueued = true;
    }

    void FixedUpdate()
    {
        // 조작권이 없으면(Tab으로 다른 오브젝트 조작 중) 전환 직전에 큐된 점프가 남아 있더라도
        // 실행하지 않고 비운다 — 조작권을 잃은 플레이어가 뒤늦게 점프해버리는 것을 막는다.
        if (mover != null && !mover.IsControlled)
        {
            jumpQueued = false;
            return;
        }

        if (!jumpQueued) return;
        jumpQueued = false;

        if (!IsGrounded()) return;

        LaunchToHeight(jumpHeight);
    }

    private bool IsGrounded()
    {
        if (shapeController != null)
            return shapeController.IsGrounded();

        Vector3 rayOrigin = transform.position + Vector3.up * 0.05f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>목표 높이 H(Unit)에 도달하도록 수직 속도를 역산해 "대입"한다(가산·AddForce 아님).
    /// 질량이 개입하지 않아 도형 질량과 무관하게 항상 정확히 H까지 오른다(레벨 도달 규격표의 전제).
    /// 기본 점프·점프대·시소·강제 점프 등 모든 발사원이 이 진입점 하나로 수렴한다.
    /// 수평 속도는 보존한다 — 이동 중 점프의 가로 도달(D = 지상속도 × 0.6 × 체공)이 성립하도록.
    /// g는 Physics.gravity에서 읽어, 프로젝트 중력을 바꿔도 "정확히 H까지"라는 계약은 유지된다.
    /// 무중력 버블처럼 이 바디의 중력만 개별로 낮추는 기믹 안에서는 전역 중력이 실제 g가 아니므로,
    /// PlayerGravityOverride가 있으면 거기서 실효 중력을 읽는다 — 안 그러면 배율의 역수만큼 초과한다.
    /// 같은 이유로 "그 구역에서 더 높이 뛴다"는 연출도 중력을 낮추는 부작용에 맡기지 않고
    /// JumpHeightMultiplier라는 명시적 배율로만 H를 키운다 — 도달 높이는 항상 예측 가능해야 한다.</summary>
    public void LaunchToHeight(float heightUnits)
    {
        if (heightUnits <= 0f) return;
        // 런타임에 기믹이 AddComponent로 붙이는 컴포넌트라 Start에 캐시할 수 없다(발사 시점에 조회).
        PlayerGravityOverride gravityOverride = GetComponent<PlayerGravityOverride>();
        float g = gravityOverride != null
            ? gravityOverride.EffectiveGravityMagnitude
            : Mathf.Abs(Physics.gravity.y);
        float h = gravityOverride != null
            ? heightUnits * gravityOverride.JumpHeightMultiplier
            : heightUnits;
        float vy = Mathf.Sqrt(2f * g * h);
        rb.velocity = new Vector3(rb.velocity.x, vy, rb.velocity.z);
    }
}
