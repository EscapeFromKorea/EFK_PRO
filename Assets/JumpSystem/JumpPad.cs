using UnityEngine;
public class JumpPad : MonoBehaviour
{
    [Header("Jump Settings")]
    public float jumpForce = 20f;
    public string playerTag = "Player";

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("충돌 감지: " + collision.gameObject.name);
        if (collision.gameObject.CompareTag(playerTag))
        {
            Debug.Log("normal.y: " + collision.contacts[0].normal.y);
            if (collision.contacts[0].normal.y > 0.5f) return;

            Rigidbody rb = collision.gameObject.GetComponentInParent<Rigidbody>();
            Debug.Log("Rigidbody: " + (rb != null ? "찾음" : "null"));
            if (rb != null)
            {
                // 기존 y축 속도 초기화 후 위로 힘 적용
                rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                //var playerJump = collision.gameObject.GetComponent<PlayerJump>();
                //if (playerJump != null)
                //    playerJump.LaunchFromPad(jumpForce);
                //else
                //    rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
                rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
            }
        }
    }
}