using UnityEngine;

/// <summary>
/// 관리자 잡힘 접촉 영역(C8_CATCH). 깨어 있고 잡힘이 활성인 관리자(ManagerAgent.CatchActive)의 영역에 살아 있는
/// 참가자가 들어온 사건만 컨트롤러에 알린다 — 거리·빨간색만으로는 잡히지 않는다(PRD §4). 잠든 관리자는 접촉
/// 사건 자체를 만들지 않는다(A-06).
///
/// CH1 PathChaserCatchZone은 CH1 상태에 묶여 있어 재사용하지 않는다(PRD §3).
///
/// [Stay도 받는 이유] 이탈 정지에서 재개하거나 시작 직후 이미 영역 안에 서 있던 참가자는 Enter가 다시 오지 않는다.
/// 첫 잡힘 이후의 중복 호출은 컨트롤러가 실패 플래그로 거른다.
///
/// [배치] 관리자 루트의 자식 오브젝트에 트리거로 둔다. 영역 크기는 모델/도형 콜라이더 실측 후 정한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ManagerCatchZone : MonoBehaviour
{
    public ManagerChapterController controller;
    public ManagerAgent managerAgent;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        if (col is SphereCollider sphere) sphere.radius = 1f;
        if (managerAgent == null) managerAgent = GetComponentInParent<ManagerAgent>();
    }

    private float nextIgnoredLog;

    private void OnTriggerEnter(Collider other) => Handle(other);
    private void OnTriggerStay(Collider other) => Handle(other);

    private void Handle(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();

        if (controller == null || managerAgent == null || !managerAgent.CatchActive || !ManagerAgent.IsAlive(mover))
        {
            // "닿았는데 안 잡혔다"를 설명하는 로그 — Stay가 매 물리 스텝 오므로 0.5초에 한 번만.
            if (Time.time >= nextIgnoredLog)
            {
                nextIgnoredLog = Time.time + 0.5f;
                LokiTelemetry.Event("ch8_catch_ignored",
                    $"who={(mover != null ? mover.name : other.name)} controller={(controller != null)} " +
                    $"agentState={(managerAgent != null ? managerAgent.Current.ToString() : "null")} " +
                    $"catchActive={(managerAgent != null && managerAgent.CatchActive)} alive={ManagerAgent.IsAlive(mover)}");
            }
            return;
        }
        controller.NotifyCaught(mover);
    }
}
