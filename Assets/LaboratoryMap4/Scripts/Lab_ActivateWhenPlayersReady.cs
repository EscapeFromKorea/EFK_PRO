using UnityEngine;

/// <summary>
/// 도형이 실제로 섹터에 "배치 완료"된 뒤 대상 오브젝트를 켜는 공용 어댑터.
///
/// [왜 필요한가] 섹터 씬은 Master가 비동기로 차례차례 로드한다(Map4Director). 팀 기믹 중 Start에서
/// 시계를 시작하는 것(예: PathChaserController의 시작 지연 2초)은 씬이 로드되는 순간 돌기 시작해,
/// 나머지 섹터가 로드되고 도형이 스폰되기도 전에 추격이 시작될 수 있다. 그래서 그런 기믹은 빌더가
/// 꺼 둔 채로 저장하고, 이 컴포넌트가 "중력이 켜진(=Director가 배치를 끝낸) 도형"이 zone 안에 처음
/// 들어온 순간 켠다. Map4Director는 로드 중 도형의 useGravity를 끄고 배치 후 되돌린다(Map4Director
/// 클래스 주석 C4) — 그 신호를 그대로 읽는다. Director 없이 섹터 씬만 열어 시험할 때는 중력이 처음부터
/// 켜져 있어 바로 동작한다.
///
/// 판정은 트리거 이벤트가 아니라 좌표(ClosestPoint)로 한다 — 팀 TeamExitZone/OutOfBoundsVolume과 같은
/// 이유(도형 콜라이더 2개 중복, 잠든 Rigidbody에 Stay 없음).
/// </summary>
public class Lab_ActivateWhenPlayersReady : MonoBehaviour
{
    [Tooltip("도형이 이 안에 들어오면 켠다(isTrigger 권장).")]
    public Collider zone;

    [Tooltip("켤 오브젝트(빌더가 비활성으로 저장해 둔 것).")]
    public GameObject[] targets;

    [Tooltip("도형 목록을 다시 훑는 주기(초).")]
    public float scanInterval = 0.25f;

    private float nextScan;

    void FixedUpdate()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + scanInterval;
        if (zone == null) return;

        foreach (PlayerMover mover in Object.FindObjectsOfType<PlayerMover>())
        {
            Rigidbody rb = mover.GetComponent<Rigidbody>();
            if (rb != null && !rb.useGravity) continue; // 아직 Director가 배치 중(C4).

            Vector3 p = mover.transform.position;
            if (zone.ClosestPoint(p) != p) continue;

            foreach (GameObject t in targets)
                if (t != null) t.SetActive(true);
            Debug.Log($"[Lab_ActivateWhenPlayersReady] '{mover.name}' 진입 — 대상 {targets.Length}개를 켰다.", this);
            enabled = false;
            return;
        }
    }
}
