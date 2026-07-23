using UnityEngine;
public class JumpPad : MonoBehaviour
{
    [Header("Jump Settings")]
    [Tooltip("플레이어를 띄울 목표 높이 H(Unit). PlayerJump가 velocity.y = √(2gH)로 역산 대입하므로 " +
             "도형 질량과 무관하게 정확히 이 높이까지 오른다(레벨 도달 규격표와 동일한 방식).")]
    public float jumpHeight = 3f;
    [Tooltip("플레이어가 아닌 일반 Rigidbody용 폴백 발사 힘(Impulse). 플레이어에는 쓰이지 않는다.")]
    public float jumpForce = 20f;
    public string playerTag = "Player";

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag(playerTag)) return;

        // 플레이어가 자유롭게 회전하며 모서리/꼭짓점으로 부딪힐 수 있어 접점이 여러 개
        // 생길 수 있다. 모든 접점이 "아래에서 부딪힘"(normal.y > 0.5f)으로 판정될 때만
        // 발사를 건너뛴다 — 하나라도 위/옆에서 온 접점이 있으면 정상 착지로 간주한다.
        bool hitFromBelow = true;
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y <= 0.5f)
            {
                hitFromBelow = false;
                break;
            }
        }
        if (hitFromBelow) return;

        Rigidbody rb = collision.gameObject.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        // PlayerSystem의 PlayerJump가 있으면 목표 높이(H)로 위임한다 — AddForce가 아니라 velocity
        // 대입이라 도형 질량과 무관하게 정확히 jumpHeight까지 오른다(요구사항 #3: 모든 발사원 동일 방식).
        // 플레이어가 아닌 일반 Rigidbody는 결정론적 도달 요구가 없으므로 기존처럼 힘(Impulse)을 가한다.
        PlayerJump playerJump = collision.gameObject.GetComponentInParent<PlayerJump>();
        if (playerJump != null)
        {
            playerJump.LaunchToHeight(jumpHeight);
        }
        else
        {
            rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
        }
    }
}