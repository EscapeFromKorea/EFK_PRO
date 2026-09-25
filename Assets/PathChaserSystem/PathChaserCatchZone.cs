using UnityEngine;

/// <summary>
/// PathChaserAgent와 같은 GameObject에 붙는 잡힘 판정 전용 트리거 — CH1 전용 로직이라
/// PathChaserAgent(순수 이동체, CH8 재사용 대상) 클래스 밖에 별도로 둔다. 플레이어가 닿으면
/// 컨트롤러의 NotifyCaught를 <b>코드로 직접 호출</b>한다(발신자-수신자 패턴) — 복귀 목적지가 그
/// 순간의 벽 파괴 상태에 따라 갈려 UnityEvent 인스펙터 고정 배선으로는 표현할 수 없기 때문이다.
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

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        if (col is SphereCollider sphere) sphere.radius = 1f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (controller == null || !other.CompareTag(playerTag)) return;

        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null) return;

        controller.NotifyCaught(mover.gameObject);
    }
}
