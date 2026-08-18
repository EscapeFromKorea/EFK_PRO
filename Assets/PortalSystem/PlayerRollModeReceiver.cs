using UnityEngine;

/// <summary>
/// 포탈을 통과한 도형의 이동 방식을 <b>모서리 굴리기(이산 스텝 텀블)</b>로 갈아끼우는 수신자.
/// 발신자는 <see cref="Portal"/>이고, 이 컴포넌트는 포탈이 최초 접촉 시 플레이어 Root에 런타임으로
/// 붙인다 — 그래서 PlayerSystem 폴더도, 씬의 플레이어 3종도 건드리지 않는다.
///
/// [왜 이산 스텝인가 — 세 번째 시도다]
/// 이 저장소는 모서리 굴리기를 두 번 걷어냈다(RollMode.EdgeTumble, PlayerMover.useTorqueRolling).
/// 두 실패의 공통 원인은 "평면 접촉 다면체의 회전을 스크립트와 PhysX 솔버가 <b>동시에</b> 소유"였다.
/// 텀블이 도는 동안 isKinematic으로 두어 솔버에게 회전 발언권을 아예 주지 않는 것이 이 방식의
/// 전부다. 그래서 조향 보정 코드도, 각운동량이라는 상태도 없다. 상세: docs/PRD/Portal.md §3.
///
/// [피벗 고정 — 텀블 중 크기 변경까지 안전한 이유]
/// 회전 중심을 무게중심이 아니라 <b>접촉 모서리</b>로 잡고 그 피벗선을 월드에 고정한다. 피벗→Root
/// 벡터를 매 스텝 현재 스케일로 다시 재기 때문에, ScalingSystem이 텀블 도중에 크기를 바꿔도 몸이
/// 위·뒤로만 자라고 바닥을 파고들거나 떠오르지 않는다(PRD §6). 무게중심 기준으로 돌리면 크기가
/// 변할 때 접촉점이 떠오르거나 파고든다 — 회전축이 접촉 모서리라는 점이 이 안전성의 전부다.
///
/// [격자 원점을 저장하지 않는다]
/// PRD §5.2는 "진입 지점 + N × 스텝" 상대 격자를 요구하는데, 매 텀블의 피벗을 <b>그때의 접촉
/// 모서리에서 다시 구하면</b> 그 성질이 상태 없이 그대로 나온다. 낙하 후 착지·크기 변경 후
/// 재정렬(§5.2의 3가지 재원점 시점)도 전부 공짜로 성립한다 — 되잡을 원점 자체가 없다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerRollModeReceiver : MonoBehaviour
{
    [Header("조정 노브 (PRD §12)")]
    [Tooltip("텀블 1회 소요 시간 배율. 기본 소요 시간은 '한 칸 이동거리 ÷ 그 도형의 이속'으로 자동 " +
             "계산된다(정육면체 0.286초 / 정사면체 0.163초) — 굴리는 속도가 걷는 속도와 어긋나지 " +
             "않게 하려는 것이라 초 단위로 직접 넣게 하지 않는다. 느리거나 빠르면 이 배율만 만진다.")]
    public float tumbleDurationScale = 1f;

    [Tooltip("방향키를 누르고 있을 때 다음 텀블까지의 간격(초). 0으로 두지 마라 — 굴리는 내내 몸을 " +
             "붙잡고 있게 되어 그 구간에서 R 리스폰이 거절된다(PRD §7.2).")]
    public float holdRepeatInterval = 0.05f;

    [Tooltip("입력 방향을 유효 방향으로 스냅할 때 허용하는 최대 각도(도). 벗어나면 아무것도 하지 " +
             "않는다 — 경계에 애매하게 걸친 입력으로 엉뚱한 칸으로 굴러가지 않게 하려는 것이다.")]
    public float inputSnapTolerance = 30f;

    /// <summary>현재 굴리기 모드인가. 포탈이 토글 판정에 쓴다.</summary>
    public bool RollModeActive { get; private set; }

    // ── 도형별 기하 (PRD §5.3) ────────────────────────────────────────────────────────────
    //
    // [콜라이더 bounds에서 재면 안 된다] 정사면체의 솔리드 콜라이더는 뾰족한 정사면체가 아니라
    // 꼭짓점을 15% 깎은 convex MeshCollider다(PlayerObjectMenuItem이 PhysX 접촉 안정성을 위해
    // 일부러 그렇게 만든다). bounds가 1.1928/0.9815/1.1928로 이상적 정사면체(1.366/1.1547/1.366)와
    // 다르고, 밑면이 삼각형이라 AABB 중심도 무게중심과 어긋난다. 한 칸이 0.8165 대신 ~0.69가 되어
    // 매 칸 15% 오차가 누적된다 — 씬에서는 "몇 칸 굴리니까 벽에 낀다"로만 보인다.
    //
    // [유도] 한 칸 이동거리 = 2 × (밑면 중심 → 피벗 모서리 수평거리 = 밑면 내접원 반지름).
    //   정육면체: 모서리 1.0, 내접원 0.5            → 한 칸 1.0
    //   정사면체: 모서리 √2, 밑면 정삼각형 내접원 a/(2√3) = 0.40825 → 한 칸 0.8165 (= √⅔)
    //   (정사면체 꼭짓점은 "정육면체에 내접하는 정사면체" → 모서리 = √2 × 반경 2 = 1.4142)
    // baseOffsetY = Root 원점에서 밑면까지의 로컬 높이. PlayerShapeController가 자식 콜라이더를
    // 매 스텝 localPosition.y = 0.5로 강제하므로(PRD §6.2) 정육면체는 0.5 − 0.5 = 0,
    // 정사면체는 0.5 − 0.28868 = 0.21132다. Root 원점은 무게중심도, 접지점도 아니다.
    private struct ShapeGeometry
    {
        public float inradius;    // 밑면 중심 → 피벗 모서리 수평거리 (스케일 1 기준)
        public float baseOffsetY; // Root 원점 → 밑면 평면 높이 (스케일 1 기준)
        public float tumbleAngle; // 텀블각(도)
        public float firstYaw;    // 첫 유효 방향의 yaw (진입 시 몸 방위 기준 상대)
        public float yawStep;     // 유효 방향 사이 간격
        public int dirCount;      // 한 번에 유효한 방향 수
        public float flipYaw;     // 텀블 1회 후 정준 자세가 도는 각 (정사면체 60, 정육면체 0)
    }

    private static readonly ShapeGeometry CubeGeometry = new ShapeGeometry
    {
        inradius = 0.5f, baseOffsetY = 0f, tumbleAngle = 90f,
        firstYaw = 0f, yawStep = 90f, dirCount = 4, flipYaw = 0f,
    };

    // 텀블각 109.4712° = 180° − 이면각 70.5288°. 밑면이 한 칸마다 뒤집혀(▲↔▼) 방향 세트가 60°
    // 돌기 때문에 같은 방향으로 두 번 연속 갈 수 없다 — 이 지그재그가 정사면체의 정체성이다.
    private static readonly ShapeGeometry TetrahedronGeometry = new ShapeGeometry
    {
        inradius = 0.40824829f, baseOffsetY = 0.21132487f, tumbleAngle = 109.4712206f,
        firstYaw = 45f, yawStep = 120f, dirCount = 3, flipYaw = 60f,
    };

    private enum State { Idle, Tumbling, Rewinding }

    private Rigidbody rb;
    private PlayerMover mover;
    private PlayerShapeIdentity identity;
    private PlayerJump jump;
    private PlayerVisualRoll visualRoll;
    private PlayerAccelReceiver accel;
    private PlayerGroundContact groundContact;
    private Collider solidCollider;
    private Collider[] ownColliders;

    private ShapeGeometry geo;
    private State state = State.Idle;
    private bool grabbed;      // 우리가 ExternallyDriven/isKinematic을 켜둔 상태인가
    private bool flipped;      // 정사면체 A/B 정준 자세. 정육면체는 항상 false와 같다
    private float entryYaw;    // 모드 진입 시 몸의 yaw. 방향표를 이 방위 기준으로 돈다

    // 진행 중인 텀블 (전부 θ의 순수 함수라 되감기가 공짜다 — 별도 상태가 없다)
    private Vector3 pivotPoint;   // 월드에 고정된 피벗선 위의 한 점(접촉 모서리 중점)
    private Vector3 pivotAxis;    // 피벗선 방향
    private Vector3 pivotLocal;   // Root 로컬에서의 피벗(스케일 1 기준)
    private Vector3 tumbleDir;    // 진행 방향(수평 단위벡터)
    private Quaternion startRot;
    private float tumbleDuration;
    private float elapsed;
    private float nextTumbleTime;

    // PlayerVisualRoll을 끄지 않고 "중립화"하기 위해 저장해 두는 원래 값 (아래 SetVisualRoll 주석)
    private float savedMaxTumbleAngle;
    private float savedSlideTiltAngle;
    private bool savedAlignToGround;
    private bool visualRollNeutralized;

    private readonly Collider[] overlapBuffer = new Collider[16];

    private float CurrentScale => transform.localScale.x;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mover = GetComponent<PlayerMover>();
        identity = GetComponent<PlayerShapeIdentity>();
        jump = GetComponent<PlayerJump>();
        visualRoll = GetComponent<PlayerVisualRoll>();
        accel = GetComponent<PlayerAccelReceiver>();
        ownColliders = GetComponentsInChildren<Collider>(true);

        if (identity != null)
        {
            solidCollider = identity.solidCollider;
            geo = identity.Kind == PlayerShapeStats.ShapeKind.Tetrahedron
                ? TetrahedronGeometry
                : CubeGeometry;
        }
        if (solidCollider != null)
            groundContact = solidCollider.GetComponent<PlayerGroundContact>();
        if (groundContact == null)
            groundContact = GetComponentInChildren<PlayerGroundContact>();
    }

    /// <summary>포탈이 부른다. 구는 애초에 이 컴포넌트를 받지 않으므로 여기서 도형을 다시 걸러내지 않는다.</summary>
    public void SetRollMode(bool on)
    {
        if (on == RollModeActive) return;

        if (on)
        {
            RollModeActive = true;
            flipped = false;
            entryYaw = transform.eulerAngles.y;
            nextTumbleTime = 0f;

            // 부스트 중이면 매 FixedUpdate velocity를 통째 대입해 텀블을 그대로 뚫는다.
            // ExternallyDriven을 안 보는 컴포넌트라 enabled 토글로는 state/holdTimer가 남는다.
            if (accel != null) accel.CancelBoost();

            // 텀블 사이 휴지기(dynamic + 접지)에 Space가 정상 발사되어 격자 정렬이 깨진다.
            // PlayerJump에는 OnEnable/OnDisable이 없어 로스터 부작용이 없다(PlayerMover와 다르다).
            // ⚠ PlayerJump에 ExternallyDriven 게이트를 추가하지 마라 — Assets/CLAUDE.md에서
            //   근거가 틀린 것으로 확인돼 되돌린 수정이다.
            if (jump != null) jump.enabled = false;

            SetVisualRollNeutralized(true);
        }
        else
        {
            Release();
            state = State.Idle;
            RollModeActive = false;
            if (jump != null) jump.enabled = true;
            SetVisualRollNeutralized(false);
        }
    }

    public void ToggleRollMode() => SetRollMode(!RollModeActive);

    // [왜 PlayerVisualRoll을 끄지 않고 중립화하는가]
    // 이 컴포넌트는 회전만 쓰지 않는다 — 매 프레임 침하 보정으로 localPosition도 쓴다. 그냥
    // enabled = false로 끄면 <b>가라앉은 위치가 그대로 굳어</b> 모드 내내 시각 메쉬가 바닥에 박힌
    // 채 굴러간다. 기준 위치(baseLocalPosition)는 private이라 밖에서 되돌릴 수도 없다.
    // 대신 기울임 각을 0, 바닥 정렬을 끔으로 만들면 컴포넌트가 <b>스스로</b> identity/침하 0으로
    // 수렴시키고 그 뒤로 기준 위치를 유지한다 — 이중 적용도 없고 복원 코드도 필요 없다.
    private void SetVisualRollNeutralized(bool neutral)
    {
        if (visualRoll == null) return;

        if (neutral)
        {
            if (visualRollNeutralized) return;
            savedMaxTumbleAngle = visualRoll.maxTumbleAngle;
            savedSlideTiltAngle = visualRoll.groundSlideTiltAngle;
            savedAlignToGround = visualRoll.alignToGround;
            visualRoll.maxTumbleAngle = 0f;
            visualRoll.groundSlideTiltAngle = 0f;
            visualRoll.alignToGround = false;
            visualRollNeutralized = true;
        }
        else
        {
            if (!visualRollNeutralized) return;
            visualRoll.maxTumbleAngle = savedMaxTumbleAngle;
            visualRoll.groundSlideTiltAngle = savedSlideTiltAngle;
            visualRoll.alignToGround = savedAlignToGround;
            visualRollNeutralized = false;
        }
    }

    void FixedUpdate()
    {
        if (!RollModeActive || rb == null || mover == null) return;

        if (state != State.Idle)
        {
            AdvanceTumble();
            return;
        }

        // 남이 이 몸을 붙잡았으면(리스폰·실타래 등) 모드를 취소한다. 체크포인트가 굴리기 구역
        // 밖일 수 있어, 거기서 굴리기로 부활하면 LD-01 필수 경로를 굴리기로 지나가게 된다.
        // 신호가 하나가 아니다 — 킬 라인 리스폰은 isKinematic을 켜지 않고 ExternallyDriven만
        // 켠다(useFade:false 경로). 페이드 리스폰만 isKinematic을 켠다.
        if (!grabbed && (mover.ExternallyDriven || rb.isKinematic))
        {
            SetRollMode(false);
            return;
        }

        // 공중이면 굴릴 바닥이 없다. 붙잡기를 놓아 그대로 낙하하게 하고(§7.3), 동시에 킬 라인·R
        // 리스폰이 정상 동작하게 한다 — 붙잡은 채로 두면 RespawnController.IsHeld가 true라
        // 장외로 떨어져도 리스폰이 무한 연기되어 소프트락이 된다.
        if (!IsGrounded())
        {
            Release();
            return;
        }

        if (Time.time < nextTumbleTime) return;

        Vector3 dir;
        if (!TryReadDirection(out dir))
        {
            // 입력이 없으면 몸을 놓는다. 이 창이 있어야 굴리기 구간 안에서 R이 먹는다.
            Release();
            return;
        }

        BeginTumble(dir);
    }

    private bool IsGrounded() => groundContact == null || groundContact.IsGrounded;

    /// <summary>입력을 현재 유효한 방향 중 가장 가까운 것으로 스냅한다. 허용 각도를 벗어나면 false.</summary>
    private bool TryReadDirection(out Vector3 dir)
    {
        dir = Vector3.zero;

        // Tab으로 조작권이 없는 몸이 남의 입력을 따라 굴러가면 안 된다. 진행 중인 텀블은
        // 완주시키되(모서리로 선 채 굳는 것을 막는다) 새 텀블은 조작권이 있을 때만 시작한다.
        if (!mover.IsControlled) return false;

        // GetAxis(스무딩)가 아니라 GetAxisRaw다 — 이산 스텝에서는 키를 누른 첫 프레임의 값이
        // 아직 0에 가까워 첫 텀블의 방향 스냅이 엉뚱한 쪽으로 걸린다.
        Vector3 raw = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (raw.sqrMagnitude < 0.01f) return false;

        // PlayerMover는 입력을 그대로 쓰지 않고 inputYawOffset만큼 월드 up 축으로 돌린 뒤 쓴다
        // (씬의 플레이어 3종 모두 90). 같은 보정을 하지 않으면 90° 어긋난 방향으로 굴러가는데,
        // 정사면체는 원래 월드 축으로 못 가므로 증상이 "원래 그런 기믹인가?"로 보여 잡기 어렵다.
        Vector3 want = Quaternion.AngleAxis(mover.inputYawOffset, Vector3.up) * raw;
        want.y = 0f;
        if (want.sqrMagnitude < 0.0001f) return false;
        want.Normalize();

        float bestDot = -2f;
        for (int i = 0; i < geo.dirCount; i++)
        {
            Vector3 candidate = DirectionAt(i);
            float dot = Vector3.Dot(candidate, want);
            if (dot > bestDot)
            {
                bestDot = dot;
                dir = candidate;
            }
        }

        return bestDot >= Mathf.Cos(inputSnapTolerance * Mathf.Deg2Rad);
    }

    /// <summary>i번째 유효 방향(월드 수평 단위벡터). 정사면체는 정준 자세 A/B에 따라 세트가 60° 돈다.</summary>
    private Vector3 DirectionAt(int i) => DirectionOf(geo, entryYaw, flipped, i);

    private Quaternion CanonicalRotation(bool flip) => CanonicalRotationOf(geo, entryYaw, flip);

    // ── 아래 4개는 인스턴스 상태를 안 쓰는 순수 함수다. 자체 점검(SelfCheck)이 런타임과 정확히
    //    같은 식을 검사할 수 있도록 static으로 뽑아 두고, 인스턴스 메서드는 여기로 위임한다.

    private static Vector3 DirectionOf(ShapeGeometry g, float entryYaw, bool flipped, int i)
    {
        float yaw = entryYaw + g.firstYaw + g.yawStep * i + (flipped ? g.flipYaw : 0f);
        return Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
    }

    private static Quaternion CanonicalRotationOf(ShapeGeometry g, float entryYaw, bool flip) =>
        Quaternion.AngleAxis(entryYaw + (flip ? g.flipYaw : 0f), Vector3.up);

    /// <summary>피벗선을 고정한 순수 θ 함수. r(피벗→Root)을 인자로 받은 스케일로 다시 잰다.</summary>
    private static void PoseOf(Vector3 pivotPoint, Vector3 pivotAxis, Vector3 pivotLocal,
                              Quaternion startRot, float theta, float scale,
                              out Vector3 pos, out Quaternion rot)
    {
        Quaternion spin = Quaternion.AngleAxis(theta, pivotAxis);
        rot = spin * startRot;
        pos = pivotPoint - spin * (startRot * (pivotLocal * scale));
    }

    private static void CanonicalPoseOf(ShapeGeometry g, Vector3 pivotPoint, Vector3 dir,
                                       float entryYaw, bool flip, float scale,
                                       out Vector3 pos, out Quaternion rot)
    {
        rot = CanonicalRotationOf(g, entryYaw, flip);
        // 새 밑면 중심 = 피벗에서 진행 방향으로 (내접원 × 종료 스케일).
        // 이동거리 = (시작 내접원 + 종료 내접원) → 크기가 안 바뀌면 정확히 한 칸이 된다(PRD §6.3).
        Vector3 newBaseCenter = pivotPoint + dir * (g.inradius * scale);
        pos = newBaseCenter - rot * (new Vector3(0f, g.baseOffsetY, 0f) * scale);
    }

    private void BeginTumble(Vector3 dir)
    {
        float scale = CurrentScale;
        startRot = CanonicalRotation(flipped);
        tumbleDir = dir;

        // 피벗 = 진행 방향 쪽 밑면 모서리의 중점. 밑면 중심은 Root에서 로컬 y = baseOffsetY다.
        Vector3 dirLocal = Quaternion.Inverse(startRot) * dir;
        pivotLocal = new Vector3(dirLocal.x * geo.inradius, geo.baseOffsetY, dirLocal.z * geo.inradius);
        pivotPoint = transform.position + startRot * (pivotLocal * scale);

        // 진행 방향으로 넘어가는 회전축. up이 dir로 가는 방향이라 몸이 dir 쪽으로 넘어간다.
        pivotAxis = Vector3.Cross(Vector3.up, dir).normalized;

        // 소요 시간 = 한 칸 ÷ 그 도형의 이속. 굴리는 속도를 걷는 속도에 묶어 둔다.
        float step = 2f * geo.inradius * scale;
        float speed = (identity != null && identity.stats != null && identity.stats.moveSpeed > 0.01f)
            ? identity.stats.moveSpeed
            : 3.5f;
        tumbleDuration = Mathf.Max(0.02f, step / speed * Mathf.Max(0.01f, tumbleDurationScale));

        // 1단계 사전 검사 — 도착 자세가 이미 막혀 있으면 시작하지 않는다. 여기서 걸러내는 것이
        // §9의 레벨 디자인 게이트다(폭 1.0~1.2U 복도 = 정육면체 전용, 높이 1.25~1.40U 터널 =
        // 정사면체 전용). 텀블 중 최고점도 같이 보려고 호(弧) 중간 자세까지 검사한다.
        Vector3 endPos; Quaternion endRot;
        CanonicalPose(geo.flipYaw > 0f && !flipped, scale, out endPos, out endRot);
        Vector3 midPos; Quaternion midRot;
        PoseAt(geo.tumbleAngle * 0.5f, scale, out midPos, out midRot);
        if (Blocked(endPos, endRot, scale) || Blocked(midPos, midRot, scale))
        {
            nextTumbleTime = Time.time + Mathf.Max(0.05f, holdRepeatInterval);
            return;
        }

        Grab();
        elapsed = 0f;
        state = State.Tumbling;
    }

    private void AdvanceTumble()
    {
        elapsed += state == State.Rewinding ? -Time.fixedDeltaTime : Time.fixedDeltaTime;
        float t = Mathf.Clamp01(elapsed / tumbleDuration);
        float scale = CurrentScale;

        // 되감기가 끝났다 — 출발 셀로 무사히 복귀했다. 정준 자세(CanonicalPose)는 도착 셀을
        // 가리키므로 여기서 쓰면 안 된다. θ=0 자세가 곧 출발 셀이고, 크기가 변했더라도
        // 피벗선이 고정돼 있어 그 자리에서 다시 재진다.
        if (state == State.Rewinding && elapsed <= 0f)
        {
            Vector3 backPos; Quaternion backRot;
            PoseAt(0f, scale, out backPos, out backRot);
            rb.MovePosition(backPos);
            rb.MoveRotation(backRot);
            FinishStep();
            return;
        }

        Vector3 pos; Quaternion rot;
        PoseAt(geo.tumbleAngle * Ease(t), scale, out pos, out rot);

        // 2·3단계 — 기하는 피벗 고정으로 안전해졌지만 크기가 커지면 벽·천장에 새로 닿을 수 있다.
        // kinematic이라 물리가 밀어내 주지 않으므로 명시적으로 다룬다.
        if (Blocked(pos, rot, scale))
        {
            if (state == State.Tumbling)
            {
                state = State.Rewinding; // 2단계: θ를 역재생해 출발 셀로 복귀
                return;
            }

            // 3단계: 되감기도 막혔다(커지느라 출발 셀조차 더 이상 안 들어간다). dynamic으로
            // 돌려 물리에 맡긴다 — 격자는 다음 텀블에서 그 자리 접촉 모서리로 다시 구해진다.
            //
            // ⚠ 놓기 전에 회전을 반드시 정준 자세로 세운다. 정육면체·정사면체는 FreezeRotation이라
            //   물리가 자세를 바로잡아 주지 않는다 — 기울어진 채 놓으면 그 각도로 영구히 굳는다.
            //   위치는 그대로 두고(겹침은 PhysX의 침투 해소가 밀어낸다) 자세만 세운다.
            rb.MoveRotation(CanonicalRotation(flipped));
            Release();
            FinishStep();
            return;
        }

        rb.MovePosition(pos);
        rb.MoveRotation(rot);

        if (state == State.Tumbling && t >= 1f)
        {
            // 정사면체는 한 칸마다 밑면이 뒤집혀 정준 자세가 A↔B로 교대한다(방향 세트가 60° 돈다).
            // 정육면체는 flipYaw가 0이라 교대 자체가 의미 없으므로 항상 A로 둔다.
            flipped = geo.flipYaw > 0f && !flipped;
            ApplyCanonical(flipped, scale);
            FinishStep();
        }
    }

    /// <summary>텀블 종료 처리. 자세는 호출 전에 이미 확정해 둔다.</summary>
    private void FinishStep()
    {
        state = State.Idle;
        nextTumbleTime = Time.time + Mathf.Max(0f, holdRepeatInterval);

        // 목적지에 바닥이 없으면 그대로 낙하한다. 착지 지점에서 격자가 다시 잡힌다(상태 없음).
        if (!IsGrounded()) Release();
    }

    private void PoseAt(float theta, float scale, out Vector3 pos, out Quaternion rot) =>
        PoseOf(pivotPoint, pivotAxis, pivotLocal, startRot, theta, scale, out pos, out rot);

    /// <summary>텀블 완료 자세 — 회전을 정준 자세로 스냅하고 밑면 중심을 피벗 반대쪽에 놓는다.</summary>
    private void CanonicalPose(bool flip, float scale, out Vector3 pos, out Quaternion rot) =>
        CanonicalPoseOf(geo, pivotPoint, tumbleDir, entryYaw, flip, scale, out pos, out rot);

    private void ApplyCanonical(bool flip, float scale)
    {
        Vector3 pos; Quaternion rot;
        CanonicalPose(flip, scale, out pos, out rot);
        rb.MovePosition(pos);
        rb.MoveRotation(rot);
    }

    /// <summary>투영 자세에서 자기 아닌 콜라이더와 실제로 겹치는가.</summary>
    private bool Blocked(Vector3 rootPos, Quaternion rootRot, float scale)
    {
        if (solidCollider == null) return false;

        // 콜라이더는 Root의 자식이므로, 투영 Root 자세에서의 콜라이더 월드 자세를 다시 만든다.
        Vector3 colLocal = Quaternion.Inverse(transform.rotation)
                           * (solidCollider.transform.position - transform.position);
        Vector3 colPos = rootPos + rootRot * colLocal;
        Quaternion colRot = rootRot * (Quaternion.Inverse(transform.rotation) * solidCollider.transform.rotation);

        // 광역: 어떤 자세로 돌아도 덮이도록 외접 반경으로 쓸어 후보만 모은다.
        float radius = solidCollider.bounds.extents.magnitude;
        int count = Physics.OverlapBoxNonAlloc(colPos, Vector3.one * radius, overlapBuffer,
                                              Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            if (other == null || IsOwn(other)) continue;

            // 정밀: convex MeshCollider엔 OverlapMesh가 없고 AABB로 재면 정사면체가 안 닿는데도
            // 막혔다고 나와 §9.2의 정사면체 전용 터널이 통째로 막힌다. ComputePenetration은
            // 몸을 실제로 그 자세로 옮기지 않고 위치·회전을 인자로 받으므로, 옮겨보고 되돌리는
            // 방식이 흘리는 한 스텝짜리 트리거·충돌 콜백 누수가 없다.
            Vector3 dir; float dist;
            if (Physics.ComputePenetration(solidCollider, colPos, colRot,
                                           other, other.transform.position, other.transform.rotation,
                                           out dir, out dist) && dist > 0.005f * scale)
                return true;
        }

        return false;
    }

    private bool IsOwn(Collider c)
    {
        for (int i = 0; i < ownColliders.Length; i++)
            if (ownColliders[i] == c) return true;
        return false;
    }

    private void Grab()
    {
        if (grabbed) return;
        grabbed = true;
        mover.ExternallyDriven = true; // 이동 대입이 텀블을 짓밟는 것을 막는다
        rb.isKinematic = true;         // 솔버에게 회전 발언권을 주지 않는다
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void Release()
    {
        if (!grabbed) return;
        grabbed = false;
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        mover.ExternallyDriven = false;
        // mover.enabled는 절대 건드리지 않는다 — PlayerControlSwitcher 로스터에서 빠져 Tab
        // 순환이 깨진다(실타래 Phase 1에서 이미 치른 부채).
    }

    // ── 자체 점검 ────────────────────────────────────────────────────────────────────────
    //
    // 텀블 기하가 깨졌는지 눈으로는 못 잡는다("몇 칸 굴리니까 벽에 낀다" 정도로만 보인다). 그래서
    // 위의 순수 함수들을 그대로 불러 아래 4가지 불변식을 검사한다 — 런타임과 같은 식을 쓰므로
    // 공식을 고치면 이 점검이 같이 움직인다. `Tools/PortalSystem/Self-Check Tumble Geometry`.
    //
    //   1. 피벗 불변 — 텀블 도중 크기가 바뀌어도 접촉 모서리가 월드 피벗선에 붙박여 있는가.
    //      (무게중심 기준으로 돌리는 "최적화"를 하면 여기서 즉시 깨진다)
    //   2. 이동거리 = 내접원 × (시작 스케일 + 종료 스케일) — 크기 불변이면 정확히 한 칸.
    //   3. 침하·부양 0 — 종료 밑면 평면이 시작 밑면 평면과 같은 높이인가.
    //   4. 방향 세트 — 정육면체 0/90/180/270, 정사면체 A 45/165/285 · B 105/225/345.
    public static string SelfCheck()
    {
        var report = new System.Text.StringBuilder();
        int failures = 0;

        float[,] scalePairs = { { 1f, 1f }, { 1f, 2f }, { 1f, 0.5f }, { 2f, 1f } };
        // entryYaw를 0이 아닌 값으로도 돌려본다 — 방위 보정을 빼먹은 식은 0에서만 우연히 맞는다.
        float[] entryYaws = { 0f, 37f };

        for (int shape = 0; shape < 2; shape++)
        {
            ShapeGeometry g = shape == 0 ? CubeGeometry : TetrahedronGeometry;
            string label = shape == 0 ? "정육면체" : "정사면체";

            foreach (float entryYaw in entryYaws)
            {
                for (int dirIndex = 0; dirIndex < g.dirCount; dirIndex++)
                {
                    for (int p = 0; p < scalePairs.GetLength(0); p++)
                    {
                        float s0 = scalePairs[p, 0], s1 = scalePairs[p, 1];
                        Vector3 dir = DirectionOf(g, entryYaw, false, dirIndex);
                        Quaternion startRot = CanonicalRotationOf(g, entryYaw, false);
                        Vector3 dirLocal = Quaternion.Inverse(startRot) * dir;
                        Vector3 pivotLocal = new Vector3(dirLocal.x * g.inradius, g.baseOffsetY,
                                                         dirLocal.z * g.inradius);
                        Vector3 rootStart = Vector3.zero;
                        Vector3 pivotPoint = rootStart + startRot * (pivotLocal * s0);
                        Vector3 pivotAxis = Vector3.Cross(Vector3.up, dir).normalized;

                        // 1. 피벗 불변 (스케일을 s0 → s1로 램프시키며 호 전체를 표본)
                        float worstPivotDrift = 0f;
                        const int samples = 24;
                        for (int i = 0; i <= samples; i++)
                        {
                            float u = i / (float)samples;
                            float scale = Mathf.Lerp(s0, s1, u);
                            Vector3 pos; Quaternion rot;
                            PoseOf(pivotPoint, pivotAxis, pivotLocal, startRot,
                                   g.tumbleAngle * u, scale, out pos, out rot);
                            Vector3 pivotNow = pos + rot * (pivotLocal * scale);
                            worstPivotDrift = Mathf.Max(worstPivotDrift,
                                                        Vector3.Distance(pivotNow, pivotPoint));
                        }

                        // 2·3. 종료 자세
                        bool endFlip = g.flipYaw > 0f;
                        Vector3 endPos; Quaternion endRot;
                        CanonicalPoseOf(g, pivotPoint, dir, entryYaw, endFlip, s1, out endPos, out endRot);

                        Vector3 baseStart = rootStart + startRot * (new Vector3(0f, g.baseOffsetY, 0f) * s0);
                        Vector3 baseEnd = endPos + endRot * (new Vector3(0f, g.baseOffsetY, 0f) * s1);
                        float travel = Vector3.ProjectOnPlane(baseEnd - baseStart, Vector3.up).magnitude;
                        float expected = g.inradius * (s0 + s1);
                        float sink = Mathf.Abs(baseEnd.y - baseStart.y);

                        bool ok = worstPivotDrift < 1e-4f
                                  && Mathf.Abs(travel - expected) < 1e-4f
                                  && sink < 1e-4f;
                        if (!ok)
                        {
                            failures++;
                            report.AppendLine(
                                $"  ✗ {label} yaw={entryYaw} dir#{dirIndex} {s0}→{s1}: " +
                                $"피벗드리프트={worstPivotDrift:e2} 이동={travel:F5}(기대 {expected:F5}) 침하={sink:e2}");
                        }
                    }
                }
            }

            // 4. 방향 세트
            for (int flip = 0; flip < 2; flip++)
            {
                var yaws = new System.Collections.Generic.List<float>();
                for (int i = 0; i < g.dirCount; i++)
                {
                    Vector3 d = DirectionOf(g, 0f, flip == 1, i);
                    float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    yaws.Add(Mathf.Repeat(yaw, 360f));
                }
                yaws.Sort();
                report.AppendLine($"  · {label} 방향 세트 {(flip == 1 ? "B" : "A")}: " +
                                  string.Join(" / ", yaws.ConvertAll(y => y.ToString("F0")).ToArray()));
            }

            float cell = 2f * g.inradius;
            report.AppendLine($"  · {label} 한 칸 {cell:F4} U · 텀블각 {g.tumbleAngle:F2}° · " +
                              $"Root→밑면 {g.baseOffsetY:F5}");
        }

        string header = failures == 0
            ? "[Portal] 자체 점검 통과 — 피벗 불변 / 이동거리 / 침하 0 / 방향 세트\n"
            : $"[Portal] 자체 점검 실패 {failures}건\n";
        return header + report.ToString();
    }

    // 등속 / ease-out / 살짝 오버슛 중 어느 것이 "굴러가는" 느낌인지는 씬에서 볼 사안이다
    // (PRD §14). 우선 양끝이 부드러운 곡선으로 두고, 바꿀 때 이 한 줄만 만진다.
    private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, t);

    void OnDisable()
    {
        // 씬 전환·오브젝트 파괴 도중 kinematic + ExternallyDriven이 켜진 채로 남으면 그 몸은
        // 영구히 조작 불가가 된다.
        if (RollModeActive) SetRollMode(false);
    }
}
