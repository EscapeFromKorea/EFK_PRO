using UnityEngine;

/// <summary>
/// 플레이어의 점프를 담당한다. "Jump" 입력 축(기본 Space)을 사용한다.
/// 같은 오브젝트에 ScalingSystem의 PlayerShapeController가 있으면 그쪽의 접지 판정을 재사용하고,
/// 없으면 자체 레이캐스트로 접지 여부를 판단한다.
/// JumpSystem/JumpPad.cs가 직접 호출하는 LaunchFromPad()도 제공한다.
/// PlayerShapeController(기본 실행 순서 0)가 이번 프레임의 IsGrounded()를 먼저 갱신한
/// 뒤에 이 컴포넌트가 그 값을 읽도록, Project Settings의 Script Execution Order 수동
/// 설정 여부와 무관하게 코드로 순서를 보장한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(10)]
public class PlayerJump : MonoBehaviour
{
    [Header("점프 설정")]
    public float jumpForce = 7f;

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

        LaunchFromPad(jumpForce);
    }

    private bool IsGrounded()
    {
        if (shapeController != null)
            return shapeController.IsGrounded();

        Vector3 rayOrigin = transform.position + Vector3.up * 0.05f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>JumpPad 등 외부 기믹이 직접 발사시킬 때 호출하는 진입점.</summary>
    public void LaunchFromPad(float force)
    {
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        rb.AddForce(Vector3.up * force, ForceMode.Impulse);
    }
}
