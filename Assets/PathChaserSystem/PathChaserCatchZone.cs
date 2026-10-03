using UnityEngine;

/// <summary>
/// PathChaserAgent와 같은 GameObject에 붙는 잡힘 판정 전용 트리거 — CH1 전용 로직이라
/// PathChaserAgent(순수 이동체, CH8 재사용 대상) 클래스 밖에 별도로 둔다. 플레이어가 닿으면
/// 컨트롤러의 NotifyCaught를 <b>코드로 직접 호출</b>한다(발신자-수신자 패턴) — 복귀 목적지가 그
/// 순간의 벽 파괴 상태에 따라 갈려 UnityEvent 인스펙터 고정 배선으로는 표현할 수 없기 때문이다.
///
/// [머무는 동안 재시도] 닿은 순간(Enter)에 판정이 무시되면 — 시작 지연 중이거나, 낙하 복귀·탑승으로 몸이 이미
/// 붙잡혀 복귀 요청이 거절된 경우 — 영역 안에 그대로 있어도 Enter는 다시 오지 않는다. 그래서 그 위로 떨어진
/// 플레이어는 잡히지 않았다. Stay에서 주기적으로 다시 알린다(SectionHitCounter의 무적 시간이 중복을 합친다).
///
/// 종료(State.Finished) 이후에는 컨트롤러가 이 오브젝트(에이전트와 같은 GameObject)를 통째로
/// SetActive(false)하므로 트리거 자체가 꺼진다 — 별도의 "잡힘 무시" 플래그가 필요 없다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PathChaserCatchZone : MonoBehaviour
{
    [Tooltip("잡힘 발생 시 알릴 컨트롤러.")]
    public PathChaserController controller;

    public string playerTag = "Player";

    [Tooltip("영역 안에 머무는 동안 잡힘을 다시 알리는 간격(초).")]
    public float retryInterval = 0.25f;

    private float nextRetryTime;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        if (col is SphereCollider sphere) sphere.radius = 1f;
    }

    private void OnTriggerEnter(Collider other) => TryCatch(other);

    private void OnTriggerStay(Collider other)
    {
        if (Time.time < nextRetryTime) return;
        nextRetryTime = Time.time + retryInterval;
        TryCatch(other);
    }

    private void TryCatch(Collider other)
    {
        if (controller == null || !other.CompareTag(playerTag)) return;

        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null) return;

        controller.NotifyCaught(mover.gameObject);
    }
}
