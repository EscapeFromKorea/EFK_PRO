using UnityEngine;

/// <summary>
/// 시각 검증용 - 위에서 계속 떨어지는 낙석 파편. 바닥에 멈춰있는 오브젝트는 중력/속도를
/// 얼마나 줄여도 눈에 안 보인다(이미 정지 상태라 줄일 게 없음) - SlowZone의 감속 효과를
/// 실제로 "보려면" 낙하 중인 대상이 있어야 한다. 이 오브젝트는 일정 높이 밑으로 내려가면
/// 위로 되돌려 계속 떨어지게 반복시켜, 구역이 켜졌을 때/꺼졌을 때 낙하 속도 차이를
/// 바로 비교할 수 있게 한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RespawningFallingDebris : MonoBehaviour
{
    public float respawnHeight = 12f;
    public float despawnHeight = -2f;

    private Rigidbody rb;
    private float x, z;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        x = transform.position.x;
        z = transform.position.z;
    }

    private void FixedUpdate()
    {
        if (transform.position.y < despawnHeight)
        {
            transform.position = new Vector3(x, respawnHeight, z);
            rb.velocity = Vector3.zero;
        }
    }
}
