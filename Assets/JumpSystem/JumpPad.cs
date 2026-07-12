using UnityEngine;
public class JumpPad : MonoBehaviour
{
    [Header("Jump Settings")]
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

        // PlayerSystem의 PlayerJump가 있으면 위임하고, 없으면(플레이어가 아닌 일반 Rigidbody 오브젝트 등)
        // 기존처럼 직접 힘을 가한다.
        PlayerJump playerJump = collision.gameObject.GetComponentInParent<PlayerJump>();
        if (playerJump != null)
        {
            playerJump.LaunchFromPad(jumpForce);
        }
        else
        {
            rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
        }
    }
}