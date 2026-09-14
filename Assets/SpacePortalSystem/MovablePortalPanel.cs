using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "가변 전이 패널" 이동/회전 확장. <see cref="PortalSurface"/>에 필드로 합치지 않고 별도
/// 컴포넌트로 분리했다(설계 확정 §13-7) — `PortalSurface` 자체는 이 컴포넌트 존재 여부와
/// 무관하게 그대로 동작한다. 진행도(<see cref="Progress"/>, 0~1) 기반 위치 제어기로,
/// 입력이 끊기면 관성 없이 그 자리에서 즉시 멈춘다(태엽 축과의 핵심 차이 — 축은 스스로
/// 방전되지만 이 패널은 자체 방전이 없다). 상세 설계는 저장소 루트
/// `가변전이패널_구현사항.md` 참고.
///
/// 킨네마틱 이동 + 전진 전 <see cref="Physics.OverlapBox"/> 사전 감지로 끼임을 처리한다 —
/// 물리 힘으로 밀어붙이지 않으므로 Joint 폭주/진동 경로 자체가 없다(§6). 포탈은 설치 시
/// 이 패널의 자식 Transform이 되므로(PRD §4.4) 이 컴포넌트는 자신의 Transform만 옮기면 되고
/// 포탈 쪽 코드는 한 줄도 참조하지 않는다.
/// </summary>
[RequireComponent(typeof(PortalSurface))]
[RequireComponent(typeof(Rigidbody))]
public class MovablePortalPanel : MonoBehaviour
{
    public enum PanelMode { Rail, Pivot }

    [Header("모드")]
    public PanelMode mode = PanelMode.Rail;

    [Header("Rail 모드 전용 — 직선 2점만 지원(§13-1 확정, 곡선 없음)")]
    [Tooltip("이동 시작 지점(진행도 0). 씬의 빈 오브젝트 Transform으로 마커를 둔다.")]
    public Transform startPose;
    [Tooltip("이동 끝 지점(진행도 1).")]
    public Transform endPose;
    [Tooltip("초당 실제 이동 거리(Unit/s).")]
    public float travelSpeed = 1f;

    [Header("Pivot 모드 전용")]
    [Tooltip("회전 중심(패널 초기 위치 기준 로컬 오프셋).")]
    public Vector3 pivotPointLocal = Vector3.zero;
    [Tooltip("회전축(패널 초기 회전 기준 로컬 방향).")]
    public Vector3 pivotAxisLocal = Vector3.up;
    [Tooltip("진행도 0에서의 각도(도).")]
    public float minAngle = 0f;
    [Tooltip("진행도 1에서의 각도(도).")]
    public float maxAngle = 90f;
    [Tooltip("초당 각속도 상한(도/s).")]
    public float rotationSpeed = 45f;

    [Header("스냅(선택, §13-4 확정 — 멈춘 순간에만 개입)")]
    [Tooltip("비어 있으면 스냅 비활성. 값은 0~1 진행도.")]
    public float[] snapPoints;

    [Header("고착 복구(§13-5 확정 — 장애물 사라질 때까지 대기)")]
    [Tooltip("이 시간(초) 이상 고착되면, 장애물이 사라지는 즉시 초기 위치(progress=0)로 복구한다. " +
             "장애물이 안 사라지면 이 시간이 지나도 복구하지 않고 계속 대기한다.")]
    public float stuckRecoveryDelay = 3f;

    [Header("라이더 감지(장애물 판정에서 제외용, §6 — 실제 운반은 PlayerMover.GroundVelocity가 담당)")]
    [Tooltip("패널 위 라이더를 감지하는 트리거 콜라이더. 비워두면 Reset()이 자동 생성한다.")]
    public Collider riderSensor;

    [Header("ponytail: 테스트용 임시 드라이버 (§13-8 범위 밖 — 실제 레버/전력/물리력 구현 전까지만)")]
    [Tooltip("켜면 Q/E 키로 직접 ApplyDrive를 호출해 플레이테스트할 수 있다. 실제 드라이버가 붙으면 꺼라.")]
    public bool debugKeyboardDrive = false;

    public float Progress { get; private set; }
    public bool IsDriving { get; private set; }
    public bool IsJammed { get; private set; }

    private Rigidbody rb;
    private PortalSurface surface;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float requestedDelta;
    private bool wasDriving;
    private float jammedSince = float.NegativeInfinity;
    private bool snapping;
    private float snapTarget;
    private readonly Dictionary<Collider, int> riderOverlap = new Dictionary<Collider, int>();
    private static readonly Collider[] overlapBuffer = new Collider[16];

    private void Reset()
    {
        BoxCollider solid = GetComponent<BoxCollider>(); // PortalSurface가 이미 요구하는 콜라이더
        BoxCollider sensor = null;
        foreach (BoxCollider b in GetComponents<BoxCollider>())
            if (b.isTrigger) { sensor = b; break; }
        if (sensor == null) sensor = gameObject.AddComponent<BoxCollider>();
        sensor.isTrigger = true;
        if (solid != null)
        {
            // 바깥 법선은 PortalSurface 규약대로 로컬 +Z다(OnDrawGizmos의 청록 화살표와 같은 축).
            // 패널을 바닥용으로 배치하면(법선을 world up으로 돌려서 씀) 이 축이 곧 "위"가 되므로
            // 라이더 센서도 여기에 둬야 한다 — 항상 로컬 +Y라고 가정하면 바닥 배치에서 센서가
            // 옆으로 붙는 실수가 난다.
            sensor.size = solid.size + new Vector3(0f, 0f, 0.2f);
            sensor.center = solid.center + new Vector3(0f, 0f, solid.size.z * 0.5f + 0.1f);
        }
        riderSensor = sensor;

        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; // 수동 부착 대비 강제(LiftPlatform과 같은 이유)
        rb.useGravity = false;
        surface = GetComponent<PortalSurface>();
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        Progress = 0f;
    }

    /// <summary>유일한 입력 경계(WindupAxle.ApplyRotation과 같은 철학). 레버/전력/플레이어 물리력
    /// 중 무엇이 부르든 이 메서드 하나만 거친다 — 구체 드라이버 구현은 범위 밖(§13-8).</summary>
    public void ApplyDrive(float signedDelta)
    {
        requestedDelta += signedDelta;
        IsDriving = true;
    }

    private void Update()
    {
        if (!debugKeyboardDrive) return;
        if (Input.GetKey(KeyCode.E)) ApplyDrive(1f);
        if (Input.GetKey(KeyCode.Q)) ApplyDrive(-1f);
    }

    private void FixedUpdate()
    {
        bool drivingNow = IsDriving;
        float dt = Time.fixedDeltaTime;
        float speedLimit = ProgressSpeedLimit();

        if (drivingNow)
        {
            float delta = Mathf.Clamp(requestedDelta, -speedLimit * dt, speedLimit * dt);
            float candidate = Mathf.Clamp01(Progress + delta);
            if (!Mathf.Approximately(candidate, Progress))
            {
                // 막힌 방향이든 반대 방향이든 매 틱 그대로 재시도한다 — 그래서 반대 입력이 별도
                // 처리 없이도 즉시 통과한다(§13-3 확정: 재검사 유예 없음).
                if (WouldBeBlocked(candidate))
                {
                    if (!IsJammed)
                    {
                        IsJammed = true;
                        jammedSince = Time.time;
                        LokiTelemetry.Event("panel_jam", $"{name} mode={mode} progress={Progress:F2}");
                    }
                }
                else
                {
                    if (IsJammed)
                    {
                        IsJammed = false;
                        LokiTelemetry.Event("panel_unjam", $"{name} progress={candidate:F2}");
                    }
                    Progress = candidate;
                    snapping = false;
                }
            }
        }

        if (wasDriving && !drivingNow && !IsJammed && snapPoints != null && snapPoints.Length > 0)
        {
            snapTarget = NearestSnapPoint(Progress);
            snapping = !Mathf.Approximately(snapTarget, Progress);
            if (snapping) LokiTelemetry.Event("panel_snap_start", $"{name} from={Progress:F2} to={snapTarget:F2}");
        }

        if (!drivingNow && snapping)
        {
            float next = Mathf.MoveTowards(Progress, snapTarget, speedLimit * dt);
            if (WouldBeBlocked(next))
            {
                snapping = false;
            }
            else
            {
                Progress = next;
                if (Mathf.Approximately(Progress, snapTarget))
                {
                    snapping = false;
                    LokiTelemetry.Event("panel_snap_done", $"{name} progress={Progress:F2}");
                }
            }
        }

        if (IsJammed && Time.time - jammedSince >= stuckRecoveryDelay && !WouldBeBlocked(0f))
        {
            Progress = 0f;
            IsJammed = false;
            snapping = false;
            LokiTelemetry.Event("panel_stuck_recover", $"{name}");
        }

        ApplyTransform(dt);

        wasDriving = drivingNow;
        IsDriving = false;
        requestedDelta = 0f;
    }

    private float ProgressSpeedLimit()
    {
        if (mode == PanelMode.Rail)
        {
            float dist = startPose != null && endPose != null ? Vector3.Distance(startPose.position, endPose.position) : 0f;
            return dist > 0.0001f ? travelSpeed / dist : 0f;
        }
        float range = Mathf.Abs(maxAngle - minAngle);
        return range > 0.0001f ? rotationSpeed / range : 0f;
    }

    private Vector3 ComputePosition(float t)
    {
        if (mode == PanelMode.Rail)
            return startPose != null && endPose != null ? Vector3.Lerp(startPose.position, endPose.position, t) : initialPosition;

        Vector3 worldPivot = initialPosition + initialRotation * pivotPointLocal;
        Vector3 dir = initialPosition - worldPivot;
        return worldPivot + PivotRotation(t) * dir;
    }

    private Quaternion ComputeRotation(float t) =>
        mode == PanelMode.Rail ? initialRotation : PivotRotation(t) * initialRotation;

    private Quaternion PivotRotation(float t)
    {
        Vector3 worldAxis = (initialRotation * pivotAxisLocal).normalized;
        float angleDelta = Mathf.Lerp(minAngle, maxAngle, t) - minAngle;
        return Quaternion.AngleAxis(angleDelta, worldAxis);
    }

    private void ApplyTransform(float dt)
    {
        Vector3 newPos = ComputePosition(Progress);
        Quaternion newRot = ComputeRotation(Progress);

        // GetPointVelocity(=PlayerGroundContact.GroundVelocity)가 킨네마틱 바디에서도 올바른
        // 접촉점 속도를 돌려주려면 velocity/angularVelocity를 직접 세팅해야 한다 — 킨네마틱
        // 바디는 MovePosition/MoveRotation만으로는 이 두 필드가 자동 갱신되지 않는다(§13-6,
        // GroundVelocity 재사용 확정. Pivot 모드의 접선 속도는 GetPointVelocity가 각속도와
        // 반지름으로 자동 계산해 별도 계산 불필요).
        if (dt > 0f)
        {
            rb.velocity = (newPos - rb.position) / dt;
            Quaternion rotDelta = newRot * Quaternion.Inverse(rb.rotation);
            rotDelta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            rb.angularVelocity = axis * (angleDeg * Mathf.Deg2Rad / dt);
        }

        rb.MovePosition(newPos);
        rb.MoveRotation(newRot);
    }

    /// <summary>candidateProgress로 전진했을 때 새로 쓸어들이는 자세에 솔리드 콜라이더가 있는지
    /// 확인한다(§6). 자기 자신과 현재 라이더(riderSensor로 감지)는 제외하고, 트리거(포탈 포함)는
    /// QueryTriggerInteraction.Ignore로 자동 제외된다. 나머지는 전부 장애물이다(§13-2 확정).</summary>
    private bool WouldBeBlocked(float candidateProgress)
    {
        Vector3 pos = ComputePosition(candidateProgress);
        Quaternion rot = ComputeRotation(candidateProgress);
        Vector3 halfExtents = Vector3.Scale(surface.Box.size, transform.lossyScale) * 0.5f;
        Vector3 worldCenter = pos + rot * Vector3.Scale(surface.Box.center, transform.lossyScale);

        int count = Physics.OverlapBoxNonAlloc(worldCenter, halfExtents, overlapBuffer, rot, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = overlapBuffer[i];
            if (hit == surface.Box) continue;
            if (riderOverlap.ContainsKey(hit)) continue;
            return true;
        }
        return false;
    }

    private float NearestSnapPoint(float t)
    {
        float best = snapPoints[0];
        float bestDist = Mathf.Abs(t - best);
        for (int i = 1; i < snapPoints.Length; i++)
        {
            float d = Mathf.Abs(t - snapPoints[i]);
            if (d < bestDist) { bestDist = d; best = snapPoints[i]; }
        }
        return best;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (riderSensor == null || other == surface.Box) return;
        riderOverlap.TryGetValue(other, out int n);
        riderOverlap[other] = n + 1;
    }

    private void OnTriggerStay(Collider other)
    {
        if (riderSensor == null || riderOverlap.ContainsKey(other)) return;
        riderOverlap[other] = 1; // Enter 콜백 유실 대비 자가 회복(LiftPlatform과 같은 이유)
    }

    private void OnTriggerExit(Collider other)
    {
        if (!riderOverlap.TryGetValue(other, out int n)) return;
        if (n <= 1) riderOverlap.Remove(other);
        else riderOverlap[other] = n - 1;
    }

    private void OnDrawGizmos()
    {
        if (mode == PanelMode.Rail)
        {
            if (startPose == null || endPose == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(startPose.position, endPose.position);
            Gizmos.DrawWireSphere(startPose.position, 0.15f);
            Gizmos.DrawWireSphere(endPose.position, 0.15f);
            return;
        }

        Vector3 basePos = Application.isPlaying ? initialPosition : transform.position;
        Quaternion baseRot = Application.isPlaying ? initialRotation : transform.rotation;
        Vector3 worldPivot = basePos + baseRot * pivotPointLocal;
        Vector3 worldAxis = (baseRot * pivotAxisLocal).normalized;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(worldPivot - worldAxis * 0.5f, worldPivot + worldAxis * 0.5f);

        Vector3 dir0 = basePos - worldPivot;
        Vector3 prev = worldPivot + dir0;
        const int segments = 24;
        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.Lerp(0f, maxAngle - minAngle, (float)i / segments);
            Vector3 cur = worldPivot + Quaternion.AngleAxis(angle, worldAxis) * dir0;
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }
}
