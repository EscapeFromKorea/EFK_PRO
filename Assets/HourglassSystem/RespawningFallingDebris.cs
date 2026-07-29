using UnityEngine;

/// <summary>
/// 시각 검증용 - 위에서 계속 떨어지는 낙석 파편. 바닥에 멈춰있는 오브젝트는 중력/속도를
/// 얼마나 줄여도 눈에 안 보인다(이미 정지 상태라 줄일 게 없음) - SlowZone의 감속 효과를
/// 실제로 "보려면" 낙하 중인 대상이 있어야 한다. 이 오브젝트는 일정 거리만큼 내려가면
/// 시작 위치로 되돌려 계속 떨어지게 반복시켜, 구역이 켜졌을 때/꺼졌을 때 낙하 속도 차이를
/// 바로 비교할 수 있게 한다.
///
/// [기준을 절대 높이가 아니라 시작 위치로 두는 이유]
/// 예전에는 respawnHeight/despawnHeight를 절대 월드 Y로 받았다. 그런데 이 오브젝트는
/// SlowZone의 자식이라, 에디터에서 구역을 옮기면 두 값만 그 자리에 남아 조용히 어긋났다
/// (실제로 구역을 y 9.5 -> 4.6으로 옮긴 뒤 파편이 구역 상단에서 사라져 감속 구간을 통과하지
/// 않았다). 시작 위치를 Awake에 스스로 읽으면 부모를 어디로 옮겨도 따라오고, 남는 파라미터는
/// "얼마나 떨어뜨릴지" 하나뿐이라 어긋날 값 자체가 없어진다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RespawningFallingDebris : MonoBehaviour
{
    [Tooltip("시작 위치에서 이만큼 아래로 내려가면 시작 위치로 되돌린다. " +
             "기본값 7.5는 6칸 구역의 상단에서 바닥 살짝 위까지의 거리다.")]
    public float fallDistance = 7.5f;

    private Rigidbody rb;
    private Vector3 startPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
    }

    private void FixedUpdate()
    {
        if (rb.position.y < startPosition.y - fallDistance)
        {
            rb.position = startPosition;
            rb.velocity = Vector3.zero;
        }
    }
}
