using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 밟기 발동 회전 다리 — 상면을 밟으면 예고 한 번 → 지정 각도까지 단일 회전 → 역방향 자동 복귀 →
/// 재무장 대기 순서를 스크립트가 결정론적으로 통제하는 함정. 실험실 CH5 전용. 상세: docs/PRD/StepRotatingBridge.md.
///
/// [기존 RotatingPlateSystem/RotatingPlate를 확장하지 않는 이유] 그 컴포넌트는 HingeJoint를
/// useSpring=false/useMotor=false로 고정해 "플레이어가 밀어 임의 각도에서 자유롭게 멈춘다"가 핵심
/// 설계다(PRD §3). 이번 요구("예고 후 지정 각도까지 정확히 회전했다가 스스로 복귀")는 그 설계와
/// 정반대라 새 폴더에 독립 컴포넌트로 만들었다.
///
/// [회전 방식 — WindupAxleSystem/RotatingPlatform과 같은 접근을 이 폴더에 독립 재구현] HingeJoint를
/// 쓰지 않고 kinematic Rigidbody + Rigidbody.MoveRotation으로 스크립트가 직접 목표 자세까지 보간한다.
/// 교차 폴더 재사용이 아니라 같은 "스크립트가 transform을 직접 돌린다"는 접근 방식만 가져와 이 폴더
/// 안에서 새로 짰다(PRD가 명시).
///
/// [상면 지지 판정 — LiftSystem 선례] kinematic Rigidbody는 OnCollisionEnter/Stay가 오지 않을 수
/// 있는 라이더(예: 다른 기믹에 붙잡혀 isKinematic인 상태)를 놓친다(LiftSystem/CLAUDE.md 실측 참고).
/// 그래서 라이더를 물리적으로 떠받치는 솔리드 콜라이더(<see cref="supportCollider"/>) 위에 감지 전용
/// 트리거 콜라이더(<see cref="riderSensor"/>)를 겹쳐 얹고, OnTriggerEnter/Stay로만 지지 여부를
/// 판정한다. 트리거가 상면에만 있으므로 아래/옆에서의 접촉은 지지로 잡히지 않는다(PRD B-01).
///
/// [끼임 안전 정지 — PeriodicTrapBase.CanAdvance와 같은 판정 로직, 클래스 상속은 하지 않음] 알고리즘
/// (겹친 플레이어 기준 이동 방향 쪽 짧은 거리 안에 고정 지형이 있으면 그 스텝을 건너뛴다)은 그대로
/// 가져왔지만, 이 다리는 무피해 장치라 PeriodicTrapBase가 함께 갖고 있는 "피격 알림"(OnHazardHit)
/// 개념이 안 맞는다 — 상속하면 다리인데 함정처럼 보이는 필드가 인스펙터에 남는다. 그래서 <see
/// cref="CanAdvanceRotation"/>에 같은 판정 로직만 독립적으로 다시 짰다. 위치 이동이 아니라 회전이라,
/// 지지 콜라이더 중심이 이번 스텝에 그리는 접선 방향을 "이동 방향" 대신 써서 같은 레이캐스트 판정을
/// 적용한다.
///
/// [낙하 판정 → 임시 배선] SectionRespawn(목적지 지정 개인 복귀)이 아직 없어 낙석
/// (FallingRockSystem/FallingRockSpawner)과 같은 방식으로 UnityEvent&lt;GameObject&gt;만 발신한다.
/// 판 접촉 자체는 피해가 아니다(PRD 확정) — 상면 트리거를 벗어난 뒤에도 계속 공중이면서
/// <see cref="fallDropDistance"/>만큼 더 떨어져야("접촉 종료 + 공중 판정") 실제 낙하로 본다. 안전
/// 발판으로 건너뛴 경우는 PlayerShapeController.IsGrounded()가 곧 true가 되어 감시가 취소된다.
/// 이 감시는 라이더별로 독립이라, 한 명이 떨어져도 같은 판에 남은 다른 라이더의 riderOverlap/상태
/// 머신에는 전혀 관여하지 않는다(PRD B-06).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class StepRotatingBridge : MonoBehaviour
{
    private enum BridgeState { Idle, Telegraph, Rotating, Returning, RearmWait }

    [Header("회전축 (PRD 확정 — 다리 길이 방향으로 고정)")]
    [Tooltip("다리 길이 방향 로컬 축. 기본 Z(forward) — 배치 시 다리 메쉬의 실제 길이 방향에 맞춰라. " +
             "(0,0,0)이면 Start가 시작을 거부한다(PRD '참조 없으면 시작 거부').")]
    public Vector3 localRotationAxis = Vector3.forward;

    [Header("회전 사이클 (수치 전부 미정 — PRD §6, 배치 시험 후 확정할 자리값)")]
    [Tooltip("정지 자세에서 목표 자세까지 회전하는 각도(도, 부호 있음). 360도 전체 회전은 PRD가 " +
             "채택하지 않았다 — 반드시 한 바퀴보다 작게 두어라.")]
    public float rotationAngle = 70f;

    [Tooltip("예고 종료 → 목표 자세로 도는 속도(도/초).")]
    public float rotationSpeed = 90f;

    [Tooltip("목표 자세 → 최초 자세로 되돌아오는 속도(도/초).")]
    public float returnSpeed = 90f;

    [Tooltip("상면 지지가 처음 감지된 뒤 실제 회전이 시작되기까지 예고 시간(초). 예고 중 상면에서 " +
             "모두 내려가도 이미 예약된 회전 주기는 취소되지 않고 계속 진행된다(PRD 확정).")]
    public float telegraphDuration = 1f;

    [Header("상면 지지 판정 (LiftSystem 선례 — 트리거 + OnTriggerEnter/Stay)")]
    [Tooltip("라이더 감지 전용 트리거 콜라이더(상면에만 배치). 비워두면 Awake가 이 오브젝트의 " +
             "콜라이더 중 isTrigger인 첫 번째를 자동으로 찾는다. Reset()이 수동 부착 시 기본값을 만든다.")]
    public Collider riderSensor;

    [Tooltip("라이더를 실제로 떠받치는 솔리드(비-트리거) 콜라이더. 끼임 안전 정지 판정의 기준 " +
             "바운즈로도 쓰인다.")]
    public Collider supportCollider;

    public string playerTag = "Player";

    [Header("끼임 안전 정지 (PeriodicTrapBase.CanAdvance와 같은 판정 로직 — 독립 재구현, 클래스 요약 참고)")]
    [Tooltip("압착 판정 여유 거리(U). 지지 콜라이더에 겹쳐 있는 플레이어 기준으로, 이번 스텝 회전이 " +
             "그리는 접선 방향 쪽 이 거리 안에 고정 지형이 있으면 그 스텝의 회전을 건너뛴다. 플레이어 " +
             "콜라이더 반경(기본 스케일 기준 약 0.5U)보다 살짝 크게 잡아라.")]
    public float squeezeCheckDistance = 0.7f;

    [System.Serializable]
    public class PlayerFellEvent : UnityEvent<GameObject> { }

    [Header("낙하 판정 → 리스폰 임시 배선 (SectionRespawn 완성 전, 낙석과 같은 방식)")]
    [Tooltip("상면 지지를 잃은 시점 Y에서 이 거리(U)만큼 더 공중으로 떨어지면 '실제로 떨어졌다'로 " +
             "보고 발화한다. 접촉 자체는 피해가 아니다 — 안전 발판으로 건너간 경우는 착지가 먼저 " +
             "감지돼 발화하지 않는다.")]
    public float fallDropDistance = 3f;

    [Tooltip("실제로 떨어진 플레이어 Root를 인자로 발화한다. 비어 있으면 아무 일도 없다 — 씬 " +
             "인스펙터에서 RespawnController.RespawnPlayer(GameObject)에 수동 배선한다(SectionRespawn " +
             "완성 전 임시 방식, FallingRockSpawner.OnHitThresholdExceeded와 같은 패턴).")]
    public PlayerFellEvent OnPlayerFell;

    private Rigidbody body;
    private BridgeState state = BridgeState.Idle;
    private float telegraphTimer;
    private Quaternion restRotation;
    private Quaternion targetRotation;
    private Vector3 worldAxis;   // 회전축의 월드 방향 — 자기 축 회전이라 Start 이후 불변이다
    private Vector3 pivotWorld;

    // 동일 도형의 다중 콜라이더, 동시 두 도형 진입 모두 하나의 주기로 취급한다(PRD 확정) — 바디별
    // 겹침 콜라이더 수를 세어 카운트가 0이 될 때만 완전히 내려간 것으로 본다(LiftPlatform과 같은 이유).
    private readonly Dictionary<Rigidbody, int> riderOverlap = new Dictionary<Rigidbody, int>();

    // 상면 지지를 잃은 시점의 Y. 값이 있는 동안만 낙하를 감시한다(다른 곳에 착지하거나 다시 이 판에
    // 오르면 지운다).
    private readonly Dictionary<Rigidbody, float> fallWatch = new Dictionary<Rigidbody, float>();

    private readonly List<Rigidbody> cleanupBuffer = new List<Rigidbody>();
    private static readonly Collider[] squeezeOverlapBuffer = new Collider[8];

    private void Reset()
    {
        Collider[] existing = GetComponents<Collider>();
        BoxCollider support = null;
        BoxCollider sensor = null;
        foreach (Collider c in existing)
        {
            BoxCollider box = c as BoxCollider;
            if (box == null) continue;
            if (box.isTrigger && sensor == null) sensor = box;
            else if (!box.isTrigger && support == null) support = box;
        }

        // 자리값 — 길이(Z)가 회전축과 같은 방향이라 회전판다운 긴 판 모양. 실제 판 크기는 미정
        // (PRD §5) 이라 배치 시 다시 잡아야 한다.
        if (support == null) support = gameObject.AddComponent<BoxCollider>();
        support.isTrigger = false;
        support.size = new Vector3(2f, 0.3f, 4f);
        support.center = Vector3.zero;
        supportCollider = support;

        if (sensor == null) sensor = gameObject.AddComponent<BoxCollider>();
        sensor.isTrigger = true;
        sensor.size = new Vector3(2f, 0.4f, 4f);
        sensor.center = new Vector3(0f, support.size.y * 0.5f + sensor.size.y * 0.5f, 0f);
        riderSensor = sensor;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true; // Reset을 거치지 않고 수동 부착됐어도 강제한다
        body.useGravity = false;

        if (riderSensor == null)
            foreach (Collider c in GetComponents<Collider>())
                if (c.isTrigger) { riderSensor = c; break; }
        if (supportCollider == null)
            foreach (Collider c in GetComponents<Collider>())
                if (!c.isTrigger) { supportCollider = c; break; }

        if (riderSensor == null)
            Debug.LogWarning($"[StepRotatingBridge] {name}: 라이더 감지용 트리거 콜라이더가 없다. " +
                             "Reset()으로 기본 콜라이더를 만들거나 직접 붙여라.", this);
        if (supportCollider == null)
            Debug.LogWarning($"[StepRotatingBridge] {name}: 솔리드 지지 콜라이더가 없어 끼임 안전 " +
                             "정지가 동작하지 않는다.", this);
    }

    private void Start()
    {
        if (localRotationAxis.sqrMagnitude < 1e-6f)
        {
            Debug.LogError($"[StepRotatingBridge] {name}: localRotationAxis가 비어 있어 시작을 " +
                           "거부한다 — 다리 길이 방향 로컬 축을 지정해라(PRD 확정).", this);
            enabled = false;
            return;
        }

        restRotation = body.rotation;
        worldAxis = (restRotation * localRotationAxis).normalized;
        pivotWorld = body.position;
        targetRotation = restRotation * Quaternion.AngleAxis(rotationAngle, localRotationAxis.normalized);
    }

    private void FixedUpdate()
    {
        CleanupDeadRiders();
        UpdateFallWatch();

        switch (state)
        {
            case BridgeState.Idle:
                if (riderOverlap.Count > 0)
                {
                    state = BridgeState.Telegraph;
                    telegraphTimer = 0f;
                }
                break;

            case BridgeState.Telegraph:
                // 상면이 비어도 타이머를 멈추거나 취소하지 않는다 — 예약된 한 주기는 계속 진행된다.
                telegraphTimer += Time.fixedDeltaTime;
                if (telegraphTimer >= telegraphDuration)
                    state = BridgeState.Rotating;
                break;

            case BridgeState.Rotating:
                StepRotation(targetRotation, rotationSpeed, Mathf.Sign(rotationAngle));
                if (RotationReached(targetRotation))
                    state = BridgeState.Returning;
                break;

            case BridgeState.Returning:
                StepRotation(restRotation, returnSpeed, -Mathf.Sign(rotationAngle));
                if (RotationReached(restRotation))
                    state = BridgeState.RearmWait; // 도달 즉시 추가 밟기는 무시한다(재무장 대기로만 진행)
                break;

            case BridgeState.RearmWait:
                if (riderOverlap.Count == 0)
                    state = BridgeState.Idle; // 상면이 완전히 비어야만 다음 발동을 허용한다
                break;
        }
    }

    private bool RotationReached(Quaternion target) => Quaternion.Angle(body.rotation, target) <= 0.05f;

    private void StepRotation(Quaternion target, float speedDegPerSec, float directionSign)
    {
        Quaternion next = Quaternion.RotateTowards(body.rotation, target, speedDegPerSec * Time.fixedDeltaTime);
        if (Quaternion.Angle(body.rotation, next) < 1e-5f) { body.MoveRotation(next); return; }

        if (!CanAdvanceRotation(directionSign)) return; // 압착 위험 — 이번 스텝만 건너뛰고 다음 프레임 재판정
        body.MoveRotation(next);
    }

    /// <summary>PeriodicTrapBase.CanAdvance와 같은 판정 로직(클래스 요약의 "끼임 안전 정지" 절 참고) —
    /// 회전판이라 "이동 델타"가 없으므로, 지지 콜라이더 중심이 이번 스텝 회전으로 그리는 접선 방향을
    /// 대신 이동 방향으로 써서 같은 레이캐스트 판정을 적용한다.</summary>
    private bool CanAdvanceRotation(float directionSign)
    {
        if (supportCollider == null) return true;

        Bounds b = supportCollider.bounds;
        float radius = b.extents.magnitude;
        int count = Physics.OverlapSphereNonAlloc(b.center, radius, squeezeOverlapBuffer, ~0,
                                                    QueryTriggerInteraction.Ignore);
        if (count == 0) return true;

        Vector3 angularDir = worldAxis * directionSign;

        for (int i = 0; i < count; i++)
        {
            Collider c = squeezeOverlapBuffer[i];
            if (c == null || c == supportCollider || !c.CompareTag(playerTag)) continue;
            if (!b.Intersects(c.bounds)) continue; // 구 판정은 넉넉해서 실제로 안 닿았으면 대상이 아니다

            Vector3 radial = c.bounds.center - pivotWorld;
            Vector3 tangent = Vector3.Cross(angularDir, radial);
            if (tangent.sqrMagnitude < 1e-6f) continue; // 축 위에 있으면 회전으로 인한 이동이 없다
            Vector3 moveDir = tangent.normalized;

            if (Physics.Raycast(c.bounds.center, moveDir, out RaycastHit hit, squeezeCheckDistance,
                                 ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == c || hit.collider == supportCollider) continue;
                return false; // 물러날 자리가 없다 — 이번 스텝 회전을 건너뛴다
            }
        }
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        riderOverlap.TryGetValue(rb, out int n);
        riderOverlap[rb] = n + 1;
        fallWatch.Remove(rb); // 다시 지지를 얻었다 — 낙하 감시 취소(재탑승)
    }

    // Enter만으로 충분한 정상 경로지만, 물리 타이밍상 Enter 콜백을 놓쳤을 경우를 대비한 자가 회복이다
    // (LiftPlatform과 같은 이유). 이미 등록된 바디는 건드리지 않는다 — 여기서 매 프레임 카운트를 올리면
    // Exit 하나로는 절대 0에 도달하지 못해 재무장이 영구히 막힌다.
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null || riderOverlap.ContainsKey(rb)) return;
        riderOverlap[rb] = 1;
        fallWatch.Remove(rb);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null || !riderOverlap.TryGetValue(rb, out int n)) return;

        if (n <= 1)
        {
            riderOverlap.Remove(rb);
            fallWatch[rb] = rb.position.y; // 상면 지지 상실 — 낙하 감시 시작(접촉 종료)
        }
        else
        {
            riderOverlap[rb] = n - 1;
        }
    }

    private void CleanupDeadRiders()
    {
        if (riderOverlap.Count == 0) return;
        cleanupBuffer.Clear();
        foreach (Rigidbody r in riderOverlap.Keys)
            if (r == null) cleanupBuffer.Add(r);
        foreach (Rigidbody r in cleanupBuffer) riderOverlap.Remove(r);
    }

    /// <summary>접촉 종료 뒤에도 공중이면서 fallDropDistance만큼 더 내려가면 실제 낙하로 보고 발화한다.
    /// 다른 라이더의 riderOverlap/상태 머신에는 전혀 관여하지 않으므로, 한 명이 떨어져도 같은 판에 남은
    /// 다른 라이더는 영향받지 않는다(PRD B-06).</summary>
    private void UpdateFallWatch()
    {
        if (fallWatch.Count == 0) return;

        cleanupBuffer.Clear();
        foreach (KeyValuePair<Rigidbody, float> kv in fallWatch)
        {
            Rigidbody rb = kv.Key;
            if (rb == null) { cleanupBuffer.Add(rb); continue; }

            if (IsGroundedSafely(rb))
            {
                cleanupBuffer.Add(rb); // 다른 곳에 착지했다 — 낙하가 아니다
                continue;
            }

            if (kv.Value - rb.position.y >= fallDropDistance)
            {
                OnPlayerFell?.Invoke(rb.gameObject);
                cleanupBuffer.Add(rb);
            }
        }
        foreach (Rigidbody r in cleanupBuffer) fallWatch.Remove(r);
    }

    // RespawnController.WaitForLanding과 같은 창구 — Root의 PlayerShapeController.IsGrounded()가
    // 우선이고, 없으면 자식의 PlayerGroundContact를 본다(둘 다 없으면 계속 공중으로 취급한다).
    private static bool IsGroundedSafely(Rigidbody rb)
    {
        PlayerShapeController shape = rb.GetComponent<PlayerShapeController>();
        if (shape != null) return shape.IsGrounded();

        PlayerGroundContact contact = rb.GetComponentInChildren<PlayerGroundContact>();
        return contact != null && contact.IsGrounded;
    }

    // 회전축과 지지 범위를 씬 뷰에 그려 배치를 돕는다. localRotationAxis가 TBD 값이라 특히 중요하다.
    private void OnDrawGizmosSelected()
    {
        Vector3 axisWorld = Application.isPlaying
            ? worldAxis
            : (transform.rotation * localRotationAxis).normalized;
        if (axisWorld.sqrMagnitude < 1e-6f) return;

        Vector3 center = Application.isPlaying ? pivotWorld : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(center - axisWorld * 1.5f, center + axisWorld * 1.5f);
    }
}
