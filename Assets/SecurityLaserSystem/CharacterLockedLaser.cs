using UnityEngine;

/// <summary>
/// 조준 예고 보안 레이저 — CH4 전용(캐릭터별 조준 고정 모드). 센서마다 대상 도형 종류 목록
/// (<see cref="targetKinds"/>)을 지정한다 — **"현재 조작 중인 캐릭터" 참조는 절대 쓰지 않는다**
/// (PRD §3 확정 L-02, Tab으로 조작 대상을 바꿔도 센서 대상은 바뀌지 않아야 한다). 목록에 도형 종류를
/// 2개 이상 넣으면 매 조준 시점(Telegraph 진입)마다 그 중 **가장 가까운** 유효 인스턴스를 록온한다.
/// 상태 `조준 고정 → 대기 → 발사 → 대기(재조준) → 조준 고정`을 반복한다(PRD §4 시간표). 스냅샷을 상태
/// 전환 시점에 1회만 캡처하는 이유·무효 판정에 ExternallyDriven/isKinematic을 함께 보는 이유는
/// `SecurityLaserSystem/CLAUDE.md` 참고.
/// </summary>
public class CharacterLockedLaser : MonoBehaviour
{
    private enum Phase { WaitingForTarget, Telegraph, Firing, Cooldown }

    [Header("대상 (PRD §3·L-02 확정 — 씬마다 지정, '현재 조작 캐릭터' 참조 금지)")]
    [Tooltip("록온 후보 도형 종류. 2개 이상 넣으면 조준 시점마다 그 중 가장 가까운 유효 인스턴스를 록온한다.")]
    public PlayerShapeStats.ShapeKind[] targetKinds = { PlayerShapeStats.ShapeKind.Sphere };

    [Tooltip("판정/시각을 담당하는 공용 레이저(같은 오브젝트에 부착해 인스펙터로 연결).")]
    public LaserBeam beam;

    [Header("타이밍 (PRD §4 확정값)")]
    [Tooltip("대상이 '사거리 밖/무효'로 판정되는 기준 거리(U). LaserBeam.range와 별개 필드다 — " +
             "이 필드는 타이밍/조준 로직 소유, beam.range는 시각/판정 사거리 소유.")]
    public float sensorRange = 20f;

    [Tooltip("센서 정면(transform.forward) 기준 록온 가능한 최대 각도(도, 원뿔 반각). 이 범위 밖의 " +
             "도형은 후보에서 제외된다 — 사거리 안이라도 시야 밖이면 안 잡힌다. 180이면 전방위(각도 " +
             "제한 없음).")]
    public float maxAimAngle = 60f;

    [Tooltip("최초 조준 고정 후 첫 발사까지의 지연(초). 확정 기본값 2초, 조정 가능(PRD §5 확정).")]
    public float firstFireDelay = 2f;

    [Tooltip("발사 후 재조준까지의 지연(초). 확정값 1초.")]
    public float relockDelay = 1f;

    [Tooltip("재조준 후 재발사까지의 지연(초). 확정값 2초 — firstFireDelay와 값은 같아도 의미가 다른 " +
             "별도 필드다(PRD 명시).")]
    public float refireDelay = 2f;

    [Tooltip("광선 유지시간(초). [TBD, 임시값] PRD §5 '0 < B < 1초는 제안일 뿐' — 배치 후 실측.")]
    public float beamDuration = 0.4f;

    private Phase phase = Phase.WaitingForTarget;
    private float phaseElapsed;
    private float currentTelegraphWait;
    private bool isFirstCycle = true;
    private PlayerShapeIdentity target;
    private Vector3 lockedDirection;

    private void Start()
    {
        if (targetKinds == null || targetKinds.Length == 0)
            Debug.LogWarning($"[SecurityLaser] '{name}'의 targetKinds가 비어 있다 — 최소 1개 이상 " +
                             "지정해야 록온할 수 있다.", this);

        EnterPhase(Phase.WaitingForTarget);
    }

    private void FixedUpdate()
    {
        if (beam == null) return;

        switch (phase)
        {
            case Phase.WaitingForTarget:
                beam.Tick(transform.position, transform.forward, telegraphOn: false, firingOn: false);
                if (FindNearestValidTarget() != null) EnterPhase(Phase.Telegraph);
                break;

            case Phase.Telegraph:
                if (!TargetValid()) { EnterPhase(Phase.WaitingForTarget); break; }
                phaseElapsed += Time.fixedDeltaTime;
                beam.Tick(transform.position, lockedDirection, telegraphOn: true, firingOn: false);
                if (phaseElapsed >= currentTelegraphWait) EnterPhase(Phase.Firing);
                break;

            // Firing/Cooldown은 의도적으로 무효 검사를 하지 않는다 — 이미 활성화된 발사는 끝까지
            // 진행한다(PRD는 "예약 발사 취소"만 요구하지 진행 중 발사 중단은 요구하지 않는다).
            case Phase.Firing:
                phaseElapsed += Time.fixedDeltaTime;
                beam.Tick(transform.position, lockedDirection, telegraphOn: false, firingOn: true);
                if (phaseElapsed >= beamDuration) EnterPhase(Phase.Cooldown);
                break;

            case Phase.Cooldown:
                phaseElapsed += Time.fixedDeltaTime;
                beam.Tick(transform.position, lockedDirection, telegraphOn: false, firingOn: false);
                if (phaseElapsed >= Mathf.Max(0f, relockDelay - beamDuration)) EnterPhase(Phase.Telegraph);
                break;
        }
    }

    /// <summary>이미 록온한 <see cref="target"/> 자체의 무효 판정(Telegraph 도중 대상이 무효해졌는지
    /// 매 프레임 확인하는 용도). "붙잡혔다"는 저장소 관례대로 ExternallyDriven || isKinematic 둘 다 본다.</summary>
    private bool TargetValid()
    {
        if (target == null) return false;
        return IsCandidateValid(target) &&
               Vector3.Distance(transform.position, AimPoint(target)) <= sensorRange &&
               WithinAimAngle(target);
    }

    /// <summary>대상이 센서 정면(transform.forward) 기준 <see cref="maxAimAngle"/> 원뿔 안에 있는지.
    /// 사거리와 별개 조건이다 — 가까워도 시야(정면) 밖이면 록온 후보에서 제외된다.</summary>
    private bool WithinAimAngle(PlayerShapeIdentity id) =>
        Vector3.Angle(transform.forward, AimPoint(id) - transform.position) <= maxAimAngle;

    /// <summary>록온 기준점 — <c>transform.position</c>(피벗) 대신 솔리드 콜라이더의 world-space
    /// bounds 중심을 쓴다(2026-09-23 수정, 실측으로 발견). 구/정육면체는 콜라이더가 피벗에 대칭이라
    /// 둘이 같지만, 정사면체는 무게중심(피벗)이 밑면에서 전체 높이의 1/4 지점에 있어(뾰족한 꼭짓점이
    /// 반대쪽으로 3배 더 뻗어있는 정사면체 기하 특성) 피벗을 그대로 조준하면 도형의 아래쪽을 겨냥하게
    /// 된다 — bounds 중심은 도형이 어떤 모양이든 시각적 중앙과 일치한다.</summary>
    private static Vector3 AimPoint(PlayerShapeIdentity id) =>
        id.solidCollider != null ? id.solidCollider.bounds.center : id.transform.position;

    private bool IsCandidateValid(PlayerShapeIdentity id)
    {
        if (id == null || !id.gameObject.activeInHierarchy) return false;

        PlayerMover mover = id.GetComponent<PlayerMover>();
        if (mover != null && mover.ExternallyDriven) return false;

        Rigidbody rb = id.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic) return false;

        return true;
    }

    /// <summary>targetKinds 중 하나라도 일치하고 유효·사거리 이내인 후보 중 가장 가까운 것을 고른다.
    /// 매 조준 시점(Telegraph 진입)마다 새로 호출된다 — 록온 도중에는 다시 부르지 않는다(스냅샷 유지).</summary>
    private PlayerShapeIdentity FindNearestValidTarget()
    {
        if (targetKinds == null) return null;

        PlayerShapeIdentity nearest = null;
        float nearestDist = float.MaxValue;

        foreach (PlayerShapeIdentity id in FindObjectsOfType<PlayerShapeIdentity>())
        {
            if (System.Array.IndexOf(targetKinds, id.Kind) < 0) continue;
            if (!IsCandidateValid(id)) continue;
            if (!WithinAimAngle(id)) continue;

            float dist = Vector3.Distance(transform.position, AimPoint(id));
            if (dist > sensorRange || dist >= nearestDist) continue;

            nearest = id;
            nearestDist = dist;
        }

        return nearest;
    }

    private void EnterPhase(Phase next)
    {
        if (next == Phase.Telegraph)
        {
            // 조준 시점마다 가장 가까운 유효 후보를 새로 록온한다(사용자 확정: 단일 도형 고정이 아니라
            // targetKinds 중 최근접 도형). Cooldown→Telegraph 전이는 FixedUpdate가 무효 검사를 안 거치므로
            // (위 FixedUpdate 주석 참고) 여기서 재선정이 실패하면 WaitingForTarget으로 보낸다.
            PlayerShapeIdentity nearest = FindNearestValidTarget();
            if (nearest == null) { EnterPhase(Phase.WaitingForTarget); return; }
            target = nearest;
        }

        phase = next;
        phaseElapsed = 0f;

        switch (next)
        {
            case Phase.WaitingForTarget:
                // 재유효화되면 처음부터(2초 예고부터) 재개한다(PRD §5 확정).
                isFirstCycle = true;
                break;

            case Phase.Telegraph:
                // 스냅샷 조준 — 이 시점에 딱 1회 고정하고 다음 Telegraph 진입 전까지 재계산하지 않는다.
                lockedDirection = (AimPoint(target) - transform.position).normalized;
                currentTelegraphWait = isFirstCycle ? firstFireDelay : refireDelay;
                isFirstCycle = false; // 대기시간 캡처 뒤에 내린다 — 순서를 바꾸면 이번 사이클이 틀어진다.
                break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 3f);

        // maxAimAngle 원뿔 시각화(배치 시 참고용).
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.25f);
        Quaternion left = Quaternion.AngleAxis(-maxAimAngle, transform.up);
        Quaternion right = Quaternion.AngleAxis(maxAimAngle, transform.up);
        Gizmos.DrawLine(transform.position, transform.position + left * transform.forward * sensorRange);
        Gizmos.DrawLine(transform.position, transform.position + right * transform.forward * sensorRange);
    }
}
