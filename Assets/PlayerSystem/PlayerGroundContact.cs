using UnityEngine;

/// <summary>
/// Player_Collider(솔리드 콜라이더)에 부착해 실제 충돌 접촉으로 접지 여부를 판단한다.
/// Rigidbody 회전을 자유롭게 허용(정육면체/정사면체가 실제로 통통 튀며 구르는 연출)한
/// 뒤로는, Root 원점에서 고정된 로컬 방향으로 쏘는 Raycast로는 접지를 신뢰할 수 없다 —
/// 물체가 회전하면 Root 원점과 실제 지면 사이의 상대 방향이 매 프레임 달라지기 때문이다.
/// 대신 실제 충돌 접촉의 법선(항상 월드 스페이스 기준이라 물체 자신의 회전과 무관하다)이
/// 위쪽을 향하는지로 접지를 판단한다.
/// </summary>
[RequireComponent(typeof(Collider))]
[DefaultExecutionOrder(20)]
public class PlayerGroundContact : MonoBehaviour
{
    [Tooltip("이 값보다 위쪽을 향한 접촉면만 '바닥'으로 인정한다 (완전한 수평면 = 1).")]
    public float groundNormalThreshold = 0.5f;

    public bool IsGrounded { get; private set; }

    // FixedUpdate는 같은 프레임의 물리 스텝보다 먼저 실행되고, OnCollisionStay는 그 물리
    // 스텝 이후에 실행된다. 이 컴포넌트의 실행 순서를 PlayerJump(10)/PlayerShapeController(0)
    // 보다 뒤로(20) 둬서, 그 스크립트들이 IsGrounded를 읽을 때는 "지난 스텝에서 계산된" 값을
    // 읽고, 그 다음에야 이번 스텝을 위해 false로 리셋되도록 순서를 보장한다.
    private void FixedUpdate()
    {
        IsGrounded = false;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player")) return;

        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y > groundNormalThreshold)
            {
                IsGrounded = true;
                return;
            }
        }
    }
}
