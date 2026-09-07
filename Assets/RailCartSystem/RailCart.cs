using UnityEngine;

/// <summary>
/// 태엽 축 파생 장치 2호 — 레일카. `RotatingPlatform`과 똑같은 "감았다 다 감아야 발동하는" 지연
/// 구동 방식을 쓴다(2026-09-05, 사용자 확정) — `OnCrankSwing`이 호출될 때마다(=한 번씩 밀 때마다)
/// `lastSwingTime`을 갱신하고, 그 뒤 `releaseDelay`(기본 3초)만큼 더는 밀리지 않아야 실제로
/// 구동력이 걸린다. 그 사이 또 밀면 타이머가 그 시점부터 다시 시작된다 — `RotatingPlatform.
/// releaseDelay`와 정확히 같은 규칙. 다만 회전판처럼 걸음 단위로 끊어 도는 대신, 레일카는 열린
/// 뒤 `WindupAxle.ApplyOutput`의 방전 곡선을 그대로 타는 연속 구동력을 쓴다 — "감았다 서서히
/// 풀리며 굴러간다"는 태엽 느낌이 걸음 단위 이산 이동보다 물리 차량에 맞기 때문이다.
///
/// 항상 dynamic Rigidbody다 — 온레일 구동도 위치를 직접 대입하지 않고 `AddForce`로만 미는다
/// (`FallingRockSystem`의 "velocity 대입 금지" 원칙과 같은 이유). 그래서 플레이어가 직접 밀어도
/// 물리가 자연스럽게 반응하고, 탈선 즉시 별도 전환 없이 그대로 자유 물리 객체가 된다.
///
/// 레일은 `RailPath`의 웨이포인트 배열이고, 구간마다 선택적으로 곡선 제어점을 꽂을 수 있다 —
/// 매 틱 실제 물리 위치에서 현재 구간 위 최근접 매개변수(t)를 다시 찾아(`RailPath.ClosestT`)
/// 그 지점의 접선 방향으로 밀고, 그 지점으로 옆 이탈을 되돌린다. 별도의 누적 진행값을 따로 들고
/// 다니지 않아 직접 밀려도 자연히 보정된다.
///
/// 탈선 조건은 "속도 임계"와 "분기"를 별개로 두지 않는다 — `RailPath`가 구간마다 갖는
/// `maxSafeSpeed`를 넘으면 탈선한다(분기는 그 값을 낮게 잡아둔 구간일 뿐). 탈선 후에는 "낙하"
/// 또는 "완전 전복"이 확정될 때만(그리고 그 뒤로도 `overturnRecoveryDelaySec`만큼 더 기다렸다가)
/// 복구한다 — 그 외에는 다리·발판·추로 영구히 남는다(docs/PRD/RailCart.md §3.4).
///
/// [에디터 자동 스냅 — 2026-09-05, `RailTrackVisual`과 같은 패턴] 레벨 디자이너가 씬 뷰에서
/// 웨이포인트나 곡선 제어점을 옮기면, 플레이 중이 아닐 때 매 프레임 카트를 레일 위 가장 가까운
/// 지점(위치+접선 방향)으로 스냅시킨다(`Update`) — 레일을 고친 뒤 카트 위치를 손으로 다시
/// 맞출 필요가 없다. 온레일 구동 로직(`FixedUpdate`)과는 완전히 분리된 별개 경로라 플레이 중에는
/// 전혀 개입하지 않는다("정렬 버튼" 같은 수동 트리거 방식도 검토했지만, 이 시스템이 이미
/// `RailTrackVisual`로 "편집 중 계속 따라간다"는 패턴을 쓰고 있어 일관성을 택했다).
/// 상세: docs/PRD/RailCart.md
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Rigidbody))]
public class RailCart : MonoBehaviour, IWindupReceiver
{
    private enum CartState { OnRail, Derailed }

    [Header("연결")]
    [Tooltip("신호를 받을 태엽 축.")]
    public WindupAxle axle;
    [Tooltip("따라갈 레일 경로.")]
    public RailPath path;
    [Tooltip("화물 적재 트리거(가칭 RailCartCargoBay). 비워두면 화물 없이 항상 기본 질량으로 구동.")]
    public RailCartCargoBay cargoBay;

    [Header("지연 발동 — RotatingPlatform과 동일 규칙(2026-09-05 확정)")]
    [Tooltip("축을 마지막으로 민 시점부터 이 시간(초)이 지나야 구동력이 걸리기 시작한다. 그 " +
             "사이에 또 밀면 타이머가 그 시점부터 다시 시작된다(RotatingPlatform.releaseDelay와 " +
             "같은 규칙).")]
    public float releaseDelay = 3f;

    [Header("시각 회전 — 레일 접선 방향 추종(2026-09-06 추가)")]
    [Tooltip("카트가 레일 접선 방향으로 회전하는 속도(도/초). 곡선 구간에서 시각적으로 카트가 " +
             "진행 방향을 향하도록 한다 — 크면 급커브에서 더 빨리 돈다.")]
    public float rotationSpeed = 360f;

    [Header("확정값 / 기본값 (씬 튜닝 대상, PRD §5)")]
    [Tooltip("레일 위 최고 속도(m/s, 레일 방향 성분 기준).")]
    public float maxSpeed = 6f;
    [Tooltip("태엽 신호 1(|power|=1)당 구동력.")]
    public float drivingForce = 4000f;
    [Tooltip("레일에서 옆으로 벗어난 만큼 되돌리는 복원력 계수 — 탈선과는 별개로 '안정적으로 " +
             "주행'하는 근거다.")]
    public float railRestoreForce = 80f;
    [Tooltip("적재 질량이 가속·충돌 세기에 반영되는 계수. 매 틱 rb.mass = 기본 질량 + 적재 질량 " +
             "합 × 이 값으로 갱신한다 — 같은 힘(drivingForce)이 무거울수록 덜 가속하고, 충돌 시에도 " +
             "그 무게가 그대로 물리 운동량에 반영된다(가속·충돌 두 요구를 질량 하나로 통합).")]
    public float cargoMassMultiplier = 1f;
    [Tooltip("레일 방향 속도에 비례해 항상 걸리는 구름 마찰(2026-09-06 추가) — Rigidbody 자체의 " +
             "drag(=0)는 낙하 등 다른 축까지 건드리므로 쓰지 않고, 이 값만 탄젠트 방향 속도에 반대로 " +
             "건다.\n" +
             "**값을 40에서 560으로 올렸다(2026-09-06 재조정)** — 40일 때는 drivingForce(4000)가 " +
             "훨씬 세서 태엽 출력이 6% 밑으로 떨어지기 전까지는 maxSpeed 클램프에 속도가 그대로 " +
             "눌러 붙어 마찰 효과가 안 보였다(실측). 560이면 균형 속도(drivingForce×power÷" +
             "rollingFriction)가 출력 0.84 근처에서 maxSpeed와 같아져, **그 아래로는 태엽 출력이 " +
             "줄어드는 만큼 속도도 바로 같이 줄어든다** — 관성 주행 구간이 짧아지는 대신(전속력에서 " +
             "구동력이 뚝 끊기면 이제 4~5초가 아니라 0.3초 안에 선다) 태엽이 풀리는 동안 속도가 " +
             "계속 눈에 보이게 줄어드는 쪽을 사용자가 확정했다.")]
    public float rollingFriction = 560f;

    [Header("완전 전복 판정 — 확정(PRD §3.4)")]
    [Tooltip("전복 판정을 시작하는 최소 기울기(도). transform.up과 월드 Up 사이 각도.")]
    public float overturnAngleThreshold = 100f;
    [Tooltip("기울기가 임계값에 막 걸쳤을 때 버텨야 하는 시간(초, 김).")]
    public float overturnHoldSecAtThreshold = 3f;
    [Tooltip("완전히 뒤집혔을 때(180도) 버텨야 하는 시간(초, 짧음).")]
    public float overturnHoldSecAtFullFlip = 0.5f;
    [Tooltip("전복/낙하 '확정' 후 실제 복구까지 추가로 기다리는 유예 시간(초) — 즉시 원위치 금지 " +
             "요구사항을 확정 이후에도 지킨다.")]
    public float overturnRecoveryDelaySec = 2f;
    [Tooltip("이 월드 Y 아래로 떨어지면 '낙하'로 즉시 확정한다(월드 밖으로 이탈 — 지속시간 없이 " +
             "바로 확정, 전복과 달리 애매한 경계가 없다).")]
    public float fallYThreshold = -20f;

    [Header("진단(문제 재현 시에만 켠다)")]
    [Tooltip("매 물리 스텝 위치/속도/회전 상태를 콘솔(Editor.log)에 기록한다 — 레일 이탈·공중 이상 " +
             "동작을 재현할 때만 켜고, 재현 후엔 꺼서 로그 스팸을 남기지 않는다.")]
    public bool logDiagnostics = false;

    private Rigidbody rb;
    private float baseMass;
    private CartState state = CartState.OnRail;
    private int segmentIndex;
    private float outputPower;
    private float lastSwingTime = float.NegativeInfinity;

    private Vector3 recoverPosition;
    private Quaternion recoverRotation;

    // 낙하 시 되돌아갈 절대 기준점 — 레벨 디자이너가 씬에 배치해 둔 "최초 스폰 위치"다(레일
    // 배열의 인덱스 0이 아니다 — 카트가 반드시 웨이포인트 0 근처에 스폰한다는 보장이 없다).
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private float overturnTimer;
    private bool overturnConfirmed;
    private float recoveryDelayTimer;
    private float fallRecoveryTimer;

    public bool IsOnRail => state == CartState.OnRail;
    public Vector3 Velocity => rb.velocity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        baseMass = rb.mass;
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        // segmentIndex는 직렬화되지 않는 필드라 에디터 자동 스냅이 골라둔 값이 플레이 시작 시
        // 살아남지 않고 항상 0으로 돌아온다 — 실제 위치가 다른 구간에 있으면 몇 프레임 동안 엉뚱한
        // 구간의 먼 지점으로 복원력이 튀는 원인이었다. Awake에서 한 번 직접 찾아 바로잡는다.
        if (TryFindClosestSegment(out int seg, out _)) segmentIndex = seg;
        recoverPosition = transform.position;
        recoverRotation = transform.rotation;
    }

    void OnEnable()
    {
        if (axle != null) axle.Subscribe(this);
    }

    void OnDisable()
    {
        if (axle != null) axle.Unsubscribe(this);
    }

    public void ApplyOutput(float power, float ratio)
    {
        outputPower = power;
    }

    /// <summary>한 번 밀 때마다 호출되는 이산 이벤트 — 여기서는 구동력 크기를 정하지 않고
    /// "마지막으로 민 시점"만 갱신한다. 실제 구동은 `ApplyOutput`의 연속 신호가 맡되,
    /// `releaseDelay`가 지나기 전까지는 잠겨 있다(클래스 상단 주석 참고).</summary>
    public void OnCrankSwing(float direction)
    {
        lastSwingTime = Time.time;
    }

    // 플레이 중이 아닐 때만 레일 위 가장 가까운 지점으로 스냅한다(클래스 상단 "에디터 자동 스냅"
    // 참고) — 웨이포인트/곡선 제어점을 드래그하는 동안 매 프레임 다시 계산돼 즉시 따라온다.
    void Update()
    {
        if (Application.isPlaying) return;
        SnapToPathInEditor();
    }

    private void SnapToPathInEditor()
    {
        if (!TryFindClosestSegment(out int bestSegment, out float bestT)) return;

        transform.position = path.Evaluate(bestSegment, bestT);
        transform.rotation = Quaternion.LookRotation(path.Tangent(bestSegment, bestT), Vector3.up);
        segmentIndex = bestSegment;
    }

    /// <summary>전체 구간을 훑어 지금 위치에서 가장 가까운 구간/t를 찾는다(에디터 스냅과
    /// Awake의 segmentIndex 보정이 공유).</summary>
    private bool TryFindClosestSegment(out int bestSegment, out float bestT)
    {
        bestSegment = 0;
        bestT = 0f;
        if (path == null || path.SegmentCount == 0) return false;

        float bestDistSq = float.MaxValue;
        for (int seg = 0; seg < path.SegmentCount; seg++)
        {
            float t = path.ClosestT(seg, transform.position);
            float distSq = (path.Evaluate(seg, t) - transform.position).sqrMagnitude;
            if (distSq < bestDistSq) { bestDistSq = distSq; bestSegment = seg; bestT = t; }
        }
        return true;
    }

    void FixedUpdate()
    {
        float cargoMass = cargoBay != null ? cargoBay.TotalCargoMass() : 0f;
        rb.mass = baseMass + cargoMass * cargoMassMultiplier;

        // 낙하는 상태(OnRail/Derailed)와 무관하게 항상 감시한다 — RespawnSystem의 킬 라인과 같은
        // 안전망. "마지막으로 있던 자리"(recoverPosition, 전복 복구용)가 아니라 레일 시작으로
        // 되돌리는 이유: 카트가 궤도를 벗어난 채로 떨어졌다면 그 "마지막 자리"부터가 이미 틀렸을
        // 수 있어, 항상 확실히 정상인 지점(레일 시작)으로 돌아가는 편이 안전하다.
        if (transform.position.y < fallYThreshold)
        {
            fallRecoveryTimer += Time.fixedDeltaTime;
            if (logDiagnostics)
                Debug.Log($"[RailCart] FALLING y={transform.position.y:F2} timer={fallRecoveryTimer:F2}/{overturnRecoveryDelaySec:F2}", this);
            if (fallRecoveryTimer >= overturnRecoveryDelaySec)
            {
                RespawnAtRailStart();
                return;
            }
        }
        else
        {
            fallRecoveryTimer = 0f;
        }

        if (state == CartState.OnRail)
        {
            DriveOnRail();
            recoverPosition = transform.position;
            recoverRotation = transform.rotation;
        }
        else
        {
            UpdateRecoveryCheck();
        }
    }

    private void DriveOnRail()
    {
        if (path == null || path.SegmentCount == 0) return;

        float t = path.ClosestT(segmentIndex, transform.position);
        Vector3 point = path.Evaluate(segmentIndex, t);
        Vector3 tangent = path.Tangent(segmentIndex, t);

        // 지연/구동 여부와 무관하게 항상 레일 접선을 향해 회전한다 — 정지 대기 중에도 커브 위에
        // 서 있으면 진행 방향을 미리 보여준다(위 railRestoreForce와 같은 "항상 작동" 성격).
        // **요(Yaw)만 돌린다** — 바퀴+벽이 지면과 계속 접촉하는 동적 Rigidbody라, 접선의 Y 성분까지
        // 반영해 피치/롤까지 매 스텝 강제로 맞추면 접지 콘택트와 스크립트가 자세 소유권을 다퉈
        // 저장소가 이미 두 번(PortalSystem 텀블, PlayerMover 토크 구르기) 겪은 "스크립트와 PhysX
        // 솔버가 다면체 회전을 동시에 소유" 함정과 같은 불안정을 낳는다(실측: 활성화 시 공중으로
        // 튐). 수평 성분만 뽑아 LookRotation하면 피치/롤은 항상 지면 접촉이 결정하게 남는다.
        Quaternion targetYaw = Quaternion.LookRotation(FlattenTangent(tangent), Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetYaw, rotationSpeed * Time.fixedDeltaTime));

        // 레일 양 끝에서는 그 이상 벗어나는 방향의 구동력을 걸지 않는다 — 웨이포인트 배열은
        // 유한한데(§6) 태엽은 그 사실을 모르고 신호만 계속 낸다. 끝에 닿은 뒤에도 구동력이 계속
        // 밀면 관성 + 구동력이 유한한 restoreForce를 압도해 끝을 한참 지나쳐 날아가다가(다음
        // 구간이 없어 t가 경계에 고정된 채로) 속도가 오히려 늘어나며 탈선하는 것이 실측됐다
        // (2026-09-06) — 마찰이 안 걸리는 게 아니라 이 끝단 폭주가 마찰을 압도했던 것.
        bool atStart = segmentIndex == 0 && t <= 0f;
        bool atEnd = segmentIndex == path.SegmentCount - 1 && t >= 1f;
        float drivePower = outputPower;
        if ((atStart && drivePower < 0f) || (atEnd && drivePower > 0f)) drivePower = 0f;

        bool released = Time.time - lastSwingTime >= releaseDelay;
        if (released)
            rb.AddForce(tangent * (drivingForce * drivePower), ForceMode.Force);

        // 레일 방향 속도 상한 — 그 방향 성분만 clamp하고 나머지(중력 낙하 등)는 그대로 둔다.
        float alongSpeed = Vector3.Dot(rb.velocity, tangent);
        if (Mathf.Abs(alongSpeed) > maxSpeed)
        {
            float excess = alongSpeed - Mathf.Sign(alongSpeed) * maxSpeed;
            rb.velocity -= tangent * excess;
            alongSpeed = Mathf.Sign(alongSpeed) * maxSpeed;
        }

        // 구름 마찰 — 항상 걸린다(released 여부와 무관, 위 railRestoreForce와 같은 성격). 태엽
        // 출력이 줄거나 다 빠지면 이 힘만 남아 관성을 서서히 깎아 자연스럽게 멈춘다. Rigidbody의
        // drag 대신 탄젠트 성분에만 걸어 낙하 등 다른 축은 건드리지 않는다(위 속도 상한과 같은 이유).
        rb.AddForce(-tangent * (alongSpeed * rollingFriction), ForceMode.Force);

        // 레일(곡선 포함)에서 옆으로 벗어난 만큼 되돌리는 복원력 — 탈선(별도 조건)과 무관하게
        // 항상 작동해 "안정적으로 주행한다"는 요구를 만족시킨다. 지연 중에도 유지해 대기하는
        // 동안 레일에서 미끄러지지 않는다.
        rb.AddForce((point - transform.position) * railRestoreForce, ForceMode.Force);

        if (logDiagnostics)
            Debug.Log($"[RailCart] seg={segmentIndex} t={t:F3} pos={transform.position:F3} pathPt={point:F3} " +
                      $"dist={(point - transform.position).magnitude:F3} tangent={tangent:F3} vel={rb.velocity:F3} " +
                      $"speed={rb.velocity.magnitude:F2} maxSafe={path.MaxSafeSpeed(segmentIndex):F2} " +
                      $"angVel={rb.angularVelocity:F3} rot={rb.rotation.eulerAngles:F1} released={released} " +
                      $"power={outputPower:F2} drivePower={drivePower:F2} atStart={atStart} atEnd={atEnd}", this);

        if (rb.velocity.magnitude > path.MaxSafeSpeed(segmentIndex))
        {
            Derail();
            return;
        }

        if (t >= 1f && segmentIndex < path.SegmentCount - 1) segmentIndex++;
        else if (t <= 0f && segmentIndex > 0) segmentIndex--;
    }

    private void Derail()
    {
        if (logDiagnostics)
            Debug.Log($"[RailCart] DERAIL pos={transform.position:F3} speed={rb.velocity.magnitude:F2}", this);
        state = CartState.Derailed;
        overturnTimer = 0f;
        overturnConfirmed = false;
        recoveryDelayTimer = 0f;
    }

    private void UpdateRecoveryCheck()
    {
        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle >= overturnAngleThreshold)
        {
            overturnTimer += Time.fixedDeltaTime;
            float requiredHold = RequiredOverturnHoldSeconds(
                tiltAngle, overturnAngleThreshold, overturnHoldSecAtThreshold, overturnHoldSecAtFullFlip);
            if (overturnTimer >= requiredHold) overturnConfirmed = true;
        }
        else
        {
            overturnTimer = 0f;
            overturnConfirmed = false;
        }

        // 낙하 판정은 FixedUpdate 상단의 전역 체크가 전담한다(레일 시작으로 리스폰) — 여기는
        // 완전 전복만 본다(직전까지의 정상 위치로 되돌리는 로컬 복구).
        if (overturnConfirmed)
        {
            recoveryDelayTimer += Time.fixedDeltaTime;
            if (recoveryDelayTimer >= overturnRecoveryDelaySec) Recover();
        }
        else
        {
            recoveryDelayTimer = 0f;
        }
    }

    private void Recover()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = recoverPosition;
        transform.rotation = recoverRotation;
        overturnTimer = 0f;
        overturnConfirmed = false;
        recoveryDelayTimer = 0f;
        state = CartState.OnRail;
        // 속도만 0으로 돌려놔도 태엽 축이 이미 방전 중이면(outputPower!=0) 바로 다음 물리 스텝에
        // 구동력이 다시 걸려 "멈췄다가 곧장 다시 움직이는" 것처럼 보인다. lastSwingTime을 지금
        // 시점으로 밀어 releaseDelay만큼은 다시 감아야 구동되는 기존 "지연 발동" 규칙을 그대로
        // 타게 해 실제로 정지 상태를 유지시킨다.
        lastSwingTime = Time.time;
    }

    /// <summary>낙하 안전망 — RespawnSystem의 킬 라인과 같은 역할. "마지막 정상 위치"가 아니라
    /// 항상 씬에 배치된 최초 스폰 위치(initialPosition/Rotation)로 되돌린다 — 그 지점이 반드시
    /// 웨이포인트 0은 아니므로 레일 배열 인덱스로 되찾지 않는다.</summary>
    private void RespawnAtRailStart()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        if (TryFindClosestSegment(out int seg, out _)) segmentIndex = seg;
        state = CartState.OnRail;
        overturnTimer = 0f;
        overturnConfirmed = false;
        recoveryDelayTimer = 0f;
        fallRecoveryTimer = 0f;
        recoverPosition = transform.position;
        recoverRotation = transform.rotation;
        // Recover()와 같은 이유 — 태엽이 이미 방전 중이면 속도만 0으로 돌려도 다음 스텝에 바로
        // 다시 밀린다. releaseDelay를 다시 타게 해 실제로 멈춰 있게 한다.
        lastSwingTime = Time.time;

        if (logDiagnostics) Debug.Log("[RailCart] RESPAWN at initial spawn position", this);
    }

    /// <summary>접선의 수평(XZ) 성분만 남긴다 — 회전 추종이 요(yaw)만 다루는 것과 같은 이유(위
    /// DriveOnRail 주석 참고). 수직 접선(0,±1,0)처럼 수평 성분이 없으면 앞쪽으로 폴백한다.</summary>
    private static Vector3 FlattenTangent(Vector3 tangent)
    {
        Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
        return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
    }

    // ── 순수 함수 (저장소 관례 — WindupAxle.SelfCheck처럼 인스턴스 상태 없이 에디터에서 검증
    //    가능하게 뽑아 둔다. Tools/RailCartSystem/Self-Check 참고) ──────────────────────────

    /// <summary>기울기가 클수록 필요한 버팀 시간이 짧아지는 보간. threshold에서 hold at threshold,
    /// 180도에서 hold at full flip으로 선형 보간한다.</summary>
    public static float RequiredOverturnHoldSeconds(
        float tiltAngle, float overturnAngleThreshold, float holdAtThreshold, float holdAtFullFlip)
    {
        float severity = Mathf.InverseLerp(overturnAngleThreshold, 180f, tiltAngle);
        return Mathf.Lerp(holdAtThreshold, holdAtFullFlip, severity);
    }

    public static string SelfCheck()
    {
        var failures = new System.Collections.Generic.List<string>();

        // 기울기가 클수록 필요 지속시간이 짧아져야 한다(단조 감소).
        float holdAtThreshold = RequiredOverturnHoldSeconds(100f, 100f, 3f, 0.5f);
        float holdAtHalfway = RequiredOverturnHoldSeconds(140f, 100f, 3f, 0.5f);
        float holdAtFull = RequiredOverturnHoldSeconds(180f, 100f, 3f, 0.5f);
        if (!(holdAtThreshold > holdAtHalfway && holdAtHalfway > holdAtFull))
            failures.Add($"기울기에 따른 필요 지속시간이 단조 감소하지 않는다 " +
                         $"(threshold={holdAtThreshold}, halfway={holdAtHalfway}, full={holdAtFull})");
        if (Mathf.Abs(holdAtThreshold - 3f) > 0.001f)
            failures.Add($"임계 기울기에서 hold 값이 holdAtThreshold와 다르다 ({holdAtThreshold})");
        if (Mathf.Abs(holdAtFull - 0.5f) > 0.001f)
            failures.Add($"완전 전복(180도)에서 hold 값이 holdAtFullFlip과 다르다 ({holdAtFull})");

        return failures.Count == 0 ? "OK" : string.Join("\n", failures);
    }
}
