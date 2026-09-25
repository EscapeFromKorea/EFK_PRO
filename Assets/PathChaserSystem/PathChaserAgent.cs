using UnityEngine;

/// <summary>
/// 순수 이동체 — 지정된 웨이포인트를 순서대로 순회하는 kinematic Rigidbody. 잡힘/벽/카트/종료 같은
/// CH1 전용 상태는 전혀 모른다 — <c>docs/PRD/ManagerSurveillance.md</c>(CH8)가 이 컴포넌트를 완전히
/// 별도 인스턴스로 재사용할 예정이라, 여기에는 웨이포인트 순회 외의 로직을 절대 넣지 않는다. CH1
/// 전용 흐름 통제는 <see cref="PathChaserController"/>가 <see cref="maxWaypointIndex"/>를 조정해서 한다.
/// 경로 이탈(근접 추격 등)도 판단은 외부가 하고, 이 컴포넌트는 <see cref="MoveToOverride"/>로 받은
/// 지점으로 가는 이동만 한다 — 오버라이드가 없으면 CH8처럼 순수 경로 순회체 그대로다.
///
/// 이동 방식은 이 저장소의 "스크립트가 통제하는 이동체" 관례(WindupAxleSystem/RotatingPlatform,
/// StepRotatingBridgeSystem)를 따른다 — kinematic Rigidbody + MoveTowards 보간. 잡힘은 별도 트리거
/// 컴포넌트(PathChaserCatchZone)가 판정하므로 플레이어를 물리적으로 막을 솔리드 콜라이더는 없다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PathChaserAgent : MonoBehaviour
{
    [Tooltip("순서대로 순회할 경로 지점(시작 → 벽 앞 정지 한계 → 종점).")]
    public Transform[] waypoints;

    [Tooltip("이동 속도(U/s).")]
    public float speed = 3f;

    [Tooltip("이 인덱스를 넘어서는 이동하지 않고 그 자리에서 대기한다. 외부 컨트롤러가 이 값을 " +
             "조정해 '벽 앞에서 멈춤 → 벽 파괴 후 상한 해제'를 구현한다.")]
    public int maxWaypointIndex = int.MaxValue;

    private const float ArriveThreshold = 0.05f;

    private Rigidbody body;
    private int currentIndex;
    private bool hasOverride;
    private Vector3 overrideTarget;

    /// <summary>경로 대신 이 지점으로 이동한다(웨이포인트 진행은 멈춘 채 유지). 매 틱 갱신해도 된다.</summary>
    public void MoveToOverride(Vector3 target)
    {
        hasOverride = true;
        overrideTarget = target;
    }

    /// <summary>오버라이드를 풀고 멈췄던 웨이포인트부터 경로 순회를 재개한다.</summary>
    public void ClearOverride() => hasOverride = false;

    /// <summary>현재 허용된 상한(<see cref="maxWaypointIndex"/>)까지의 경로 구간 중 <paramref name="from"/>에서
    /// 가장 가까운 점. <paramref name="nextIndex"/>는 그 점에서 경로를 이어갈 다음 웨이포인트다(그 점이 놓인
    /// 구간의 끝). 상한까지만 보므로 벽 앞 상한일 때 벽 너머 구간으로 복귀점이 잡히지 않는다.</summary>
    public Vector3 NearestPathPoint(Vector3 from, out int nextIndex)
    {
        nextIndex = 0;
        if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null) return from;

        int limit = Mathf.Clamp(maxWaypointIndex, 0, waypoints.Length - 1);
        Vector3 best = waypoints[0].position;
        float bestSqr = (from - best).sqrMagnitude;
        for (int i = 0; i < limit; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            Vector3 a = waypoints[i].position, ab = waypoints[i + 1].position - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(from - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 p = a + ab * t;
            float d = (from - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = p; nextIndex = i + 1; }
        }
        return best;
    }

    /// <summary>오버라이드를 풀고 <paramref name="nextIndex"/> 웨이포인트를 향해 경로 순회를 이어간다 —
    /// <see cref="NearestPathPoint"/>로 구한 점에 복귀한 뒤 부른다.</summary>
    public void RejoinPath(int nextIndex)
    {
        hasOverride = false;
        if (waypoints != null && waypoints.Length > 0) currentIndex = Mathf.Clamp(nextIndex, 0, waypoints.Length - 1);
    }

    /// <summary>경로 첫 지점으로 순간이동하고 순회를 처음부터 다시 시작한다(오버라이드 해제 포함).
    /// 챕터 재시작용 — 이동체 수준의 개념이라 CH1/CH8 어느 쪽 규칙도 모른다. 켜진 뒤(Awake 이후)에
    /// 불러야 한다.</summary>
    public void ResetToStart()
    {
        hasOverride = false;
        currentIndex = 0;
        if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null) return;
        Vector3 p = waypoints[0].position;
        body.position = p;        // 물리 쪽 위치
        transform.position = p;   // 렌더/즉시 조회 쪽 위치(RespawnController와 같은 관례)
    }

    /// <summary>현재 허용된 상한(<see cref="maxWaypointIndex"/>와 배열 끝 중 작은 쪽)에 도달해
    /// 멈춰 있는가. 상한이 벽 앞 지점이면 "벽 대기" 신호가 되고, 상한이 종점이면 "종점 도달"
    /// 신호가 된다 — 컨트롤러가 그 순간 상한이 무엇인지 이미 알고 있으므로 둘을 구분하는 별도
    /// 프로퍼티를 만들지 않는다.</summary>
    public bool ReachedLimit
    {
        get
        {
            if (hasOverride || waypoints == null || waypoints.Length == 0) return false;
            int limit = Mathf.Clamp(maxWaypointIndex, 0, waypoints.Length - 1);
            if (currentIndex != limit) return false;
            Transform target = waypoints[limit];
            return target != null && Vector3.Distance(transform.position, target.position) <= ArriveThreshold;
        }
    }

    private void Reset()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void FixedUpdate()
    {
        if (hasOverride)
        {
            body.MovePosition(Vector3.MoveTowards(body.position, overrideTarget, speed * Time.fixedDeltaTime));
            return;
        }

        if (waypoints == null || waypoints.Length == 0) return;

        int limit = Mathf.Clamp(maxWaypointIndex, 0, waypoints.Length - 1);
        if (currentIndex > limit) currentIndex = limit; // 상한이 내려가면 즉시 그 자리에서 대기

        Transform target = waypoints[currentIndex];
        if (target == null) return;

        Vector3 next = Vector3.MoveTowards(body.position, target.position, speed * Time.fixedDeltaTime);
        body.MovePosition(next);

        if (currentIndex < limit && Vector3.Distance(next, target.position) <= ArriveThreshold)
            currentIndex++;
    }

    private void OnDrawGizmos()
    {
        if (waypoints == null) return;

        Gizmos.color = Color.red;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawWireSphere(waypoints[i].position, 0.3f);
            if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
        }
    }
}
