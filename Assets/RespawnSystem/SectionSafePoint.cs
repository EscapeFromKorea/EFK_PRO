using UnityEngine;

/// <summary>
/// 이름 있는 개인 복귀 목적지 — 구간별 위험 장치(낙석·레이저·함정 등)가 직접 참조하는 고정 지점.
/// RespawnZone(밟으면 저장되는 공용 체크포인트)과 달리, 레벨 디자이너가 직접 배치하고 위험 장치가
/// 직접 참조한다. 여러 개가 동시에 존재해도 서로 덮어쓰지 않는다(docs/PRD/SectionRespawn.md §3).
///
/// [좌표를 레이로 찾지 않는다]
/// RespawnZone은 볼륨 중앙에서 바닥을 레이로 찾지만, 이 컴포넌트는 "고정 지점"이라 transform.position을
/// 그대로 쓴다 — 배치한 그 자리가 곧 착지 지점이다. RespawnController가 그 위에 mover별 pivot-to-bottom
/// 오프셋(FadeSpawnPosition)을 얹어 최종 좌표를 만든다(연출·안전 로직은 복제하지 않는다).
///
/// [점유 검사와 보조 지점]
/// 목적지가 다른 참가자로 막혀 있으면 겹쳐서 순간이동시키지 않는다. 보조 지점을 순서대로 검사해 첫
/// 가용 지점을 쓰고, 전부 막히면 강제로 밀어 넣지 않고 실패를 알린다 — 호출자(RespawnController)가
/// 이번 호출을 포기한다. 다음 피격/재시도에서 다시 시도된다.
///
/// [선택: 수동 복귀 시 카운터 리셋]
/// counter를 지정하고 Collider(Is Trigger)를 붙이면, 참가자가 실제로 이 지점에 도달했을 때(수동 R로
/// 걸어옴 등) 연결된 SectionHitCounter를 0으로 되돌린다(§4 "수동 복귀와의 상호작용"). Collider가 없으면
/// 이 기능은 그냥 꺼진 채로 동작한다 — 필수 부품이 아니다.
/// </summary>
public class SectionSafePoint : MonoBehaviour
{
    [Tooltip("이 안전점의 식별자(로그·디버그용). 예: \"CH4_FallingRock\", \"CH4_Laser\", \"CH1_SafePre\".")]
    public string sectionId;

    [Tooltip("점유 판정 반경(Unit). 이 반경 안에 다른 참가자가 있으면 그 지점은 막힌 것으로 본다.")]
    public float occupancyRadius = 0.6f;

    [Tooltip("주 지점이 막혔을 때 순서대로 시도할 보조 지점. 레벨 배치 사항 — 코드가 자동 생성하지 " +
             "않는다(docs/PRD/SectionRespawn.md §4 '안전점 다중화').")]
    public Transform[] backupPoints;

    [Header("선택 — 수동 복귀 감지 (Collider Is Trigger 필요)")]
    [Tooltip("참가자가 이 지점에 실제로 도달했을 때 0으로 되돌릴 카운터. 비워두면 이 기능은 꺼진다.")]
    public SectionHitCounter counter;

    private Collider triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
            Debug.LogWarning($"[SectionSafePoint] '{name}'의 Collider가 Trigger가 아니다 — 수동 복귀 " +
                             "감지가 동작하지 않는다(참가자를 물리적으로 막을 수도 있다). isTrigger를 켜라.", this);
    }

    /// <summary>가용한 순간이동 좌표를 찾는다. 주 지점 → 보조 지점 순으로 점유 여부를 검사한다.
    /// excluding은 지금 복귀시키려는 당사자 — 자기 자신과의 겹침은 점유로 치지 않는다.</summary>
    public bool TryGetAvailablePoint(PlayerMover excluding, out Vector3 point)
    {
        if (!IsOccupied(transform.position, excluding))
        {
            point = transform.position;
            return true;
        }

        if (backupPoints != null)
        {
            foreach (Transform backup in backupPoints)
            {
                if (backup == null) continue;
                if (!IsOccupied(backup.position, excluding))
                {
                    point = backup.position;
                    return true;
                }
            }
        }

        point = default;
        return false;
    }

    private bool IsOccupied(Vector3 worldPoint, PlayerMover excluding)
    {
        Collider[] hits = Physics.OverlapSphere(worldPoint, occupancyRadius);
        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag("Player")) continue;
            PlayerMover mover = hit.GetComponentInParent<PlayerMover>();
            if (mover == null || mover == excluding) continue;
            return true;
        }
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (counter == null) return;
        if (!other.CompareTag("Player")) return;

        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover != null) counter.ResetFor(mover.gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        Gizmos.DrawSphere(transform.position, 0.25f);
        Gizmos.DrawWireSphere(transform.position, occupancyRadius);

        if (backupPoints == null) return;
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.4f);
        foreach (Transform backup in backupPoints)
        {
            if (backup == null) continue;
            Gizmos.DrawWireSphere(backup.position, occupancyRadius);
            Gizmos.DrawLine(transform.position, backup.position);
        }
    }
}
