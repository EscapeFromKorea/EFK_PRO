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
// [실행 순서 200 — 이동 관련 컴포넌트 중 마지막]
// 저장소 관례가 PlayerMover(미지정 = 0) → PlayerAccelReceiver(100) → PlayerWindReceiver(150)이라,
// 그 뒤에 서야 아래 FixedUpdate의 "수평 velocity 0"이 같은 스텝에 남이 쓴 값을 확실히 덮는다.
// 순서가 앞이면 키를 누른 첫 프레임에 PlayerMover의 대입이 우리 뒤에 와서 그대로 미끄러진다.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class PlayerRollModeReceiver : MonoBehaviour
{
    [Header("조정 노브 (PRD §12)")]
    [Tooltip("텀블 1회 소요 시간 배율. 기본 소요 시간은 '한 칸 이동거리 ÷ 그 도형의 이속'으로 자동 " +
             "계산된다(정육면체 0.286초 / 정사면체 0.163초) — 굴리는 속도가 걷는 속도와 어긋나지 " +
             "않게 하려는 것이라 초 단위로 직접 넣게 하지 않는다. 느리거나 빠르면 이 배율만 만진다. " +
             "⚠ 배율의 대상은 '속도'가 아니라 '소요 시간'이다 — 1보다 크면 느려지고(1.25 = 25% 느림), " +
             "1보다 작으면 빨라진다(0.8 = 25% 빠름). 회전을 늦추고 싶으면 1보다 큰 값을 넣어라.")]
    public float tumbleDurationScale = 1f;

    [Tooltip("방향키를 누르고 있을 때 다음 텀블까지의 간격(초). 0으로 두지 마라 — 굴리는 내내 몸을 " +
             "붙잡고 있게 되어 그 구간에서 R 리스폰이 거절된다(PRD §7.2).")]
    public float holdRepeatInterval = 0.03f;

    [Tooltip("입력 방향을 유효 방향으로 스냅할 때 허용하는 최대 각도(도). 벗어나면 아무것도 하지 " +
             "않는다 — 경계에 애매하게 걸친 입력으로 엉뚱한 칸으로 굴러가지 않게 하려는 것이다. " +
             "⚠ 30° 미만으로 낮추지 마라 — 정사면체는 firstYaw=45(직진 방향엔 유효 텀블 방향이 " +
             "없다)라, 통로를 걸어 들어가며 계속 누르는 '직진' 입력이 entryYaw와 무관하게 항상 " +
             "정확히 45° 벗어난다(2026-08-21 정사면체 첫 플레이테스트에서 실측 — 영구 동결로 재현). " +
             "45° + 여유를 커버해야 한다.")]
    public float inputSnapTolerance = 46f;

    [Tooltip("굴리기 중 발밑이 비면 굴리기 모드를 끄고 기존 이동 방식으로 돌아간다(기본). " +
             "끄면 굴리기 모드를 유지한 채 몸만 놓아 수직으로 떨어진다 — 포탈 뒤에 리스폰 깃발을 둔 맵용.")]
    public bool exitRollModeOnFall = true;

    [Tooltip("발밑이 빈 채로 이 시간(초) 안에 다시 착지하면 exitRollModeOnFall과 무관하게 모드를 " +
             "유지한다(붙잡기만 놓아 그동안 수직으로 떨어진다) — 턱·계단을 내려갈 때 순간적으로 뜨는 것과 " +
             "진짜 낙하를 구분하려는 것이다. 경사면에는 안 듣는다(텀블 모델은 완전 수평 전용, PRD §9).")]
    public float fallExitGrace = 0.35f;

    [Tooltip("텀블이 막혀 되감기로 돌아설 때, 무엇이 막았는지 한 줄 경고 로그를 찍는다(텀블 하나당 1회). " +
             "\"평지인데 왔다갔다한다\" 같은 증상은 막은 콜라이더의 하이어라키 경로와 침투 깊이를 봐야 " +
             "특정된다. 조사가 끝나면 꺼라.")]
    public bool logBlocking = true;

    [Tooltip("텀블로 올라갈 수 있는 최대 단차(Unit, 스케일 1 기준). 도착 칸 바닥이 지금 칸보다 이 " +
             "값 이하로 높으면 그 높이에 맞춰 올라가고, 넘으면 지금까지처럼 제자리 무반응이다. " +
             "0으로 두면 기능이 꺼진다(= 이 수정 이전 동작). ⚠ 내려가는 쪽 허용치" +
             "(GroundSnapCorrectionCap = 0.25)와 일부러 대칭이 아니다 — 내려가는 건 못 흡수해도 " +
             "낙하로 자연스럽게 처리되지만, 올라가는 쪽을 크게 잡으면 턱을 타고 기어오르는 꼴이 " +
             "되므로 훨씬 빡빡해야 한다. 기본 0.05의 근거: 실제로 관측된 단차(2026-08-21 정사면체 " +
             "플레이테스트의 0.0352U, 리프트 정지 높이 오차)는 넘고, 정사면체 전용 터널 게이트의 " +
             "천장 여유(1.30U 게이트 기준 0.0753U)는 밑돈다 — 호 전체가 이 값만큼 들리므로 천장 " +
             "여유를 정확히 그만큼 잠식한다(PRD §9.2 게이트가 깨지지 않는 상한이 곧 0.0753U다).")]
    public float stepUpCap = 0.05f;

    /// <summary>현재 굴리기 모드인가. 포탈이 토글 판정에 쓴다.</summary>
    public bool RollModeActive { get; private set; }

    /// <summary>지금 한 칸이 실제로 돌아가는 중인가(되감기 포함). 텀블 중에는 Root가 회전하고
    /// isKinematic이라, 몸의 회전·정지 여부를 밖에서 봐야 하는 기믹(실타래 네모 닻)이 이걸 읽는다.</summary>
    public bool IsTumbling => state != State.Idle;

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

    /// <summary>한 칸 이동거리(스케일 1 기준) = 내접원 반지름 × 2. 편집 도구(테스트 바닥 격자
    /// 시각화 등)가 §5.3의 기하 상수를 다시 베끼지 않고 여기서 그대로 가져다 쓰게 공개한다.</summary>
    public static readonly float CubeCellSize = CubeGeometry.inradius * 2f;
    public static readonly float TetrahedronCellSize = TetrahedronGeometry.inradius * 2f;

    /// <summary>매 텀블 재접지가 흡수할 수 있는 낙차 상한(스케일 1 기준). <see cref="SnapPivotToGround"/>
    /// 참고 — 이보다 큰 낙차는 절벽과 똑같이 취급돼 "텀블→뜸→낙하→재접지"가 반복되는 스타카토
    /// 동작이 된다(2026-08-20 계단 테스트에서 실측 확인). 계단형 지형을 설계할 땐 단 높이를 이
    /// 값(× 스케일) 이하로 잡아라.</summary>
    public const float GroundSnapCorrectionCap = 0.25f;

    /// <summary>레이 XZ 샘플 지점을 피벗에서 진행 방향 반대쪽으로 물러나는 거리(스케일 1 기준).
    /// <see cref="SnapPivotToGround"/> 참고 — 피벗 자체는 항상 "지금 서 있는 칸"의 리딩 엣지(칸
    /// 경계선 그 자체)라, 그 자리에서 레이를 쏘면 부동소수 오차로 옆 칸(다음 칸)을 잘못 짚을 수
    /// 있다(2026-08-20 계단 경계 재접지 오판 — 원인 3). 진행 방향 반대쪽(= 지금 칸의 안쪽)으로
    /// 살짝 물러나면 항상 지금 칸 위를 짚는다. 평지에서는 이 정도 수평 이동으로 바닥 높이가
    /// 거의 안 변한다(0.16° 기울기 기준 오차 0.0001U 수준) — 보정 대상인 pivotPoint의 XZ 자체는
    /// 이 상수와 무관하게 그대로 유지된다(회전축 위치는 안 옮긴다, 레이 샘플 지점만 옮긴다).</summary>
    private const float GroundSnapRayBackoff = 0.05f;

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

    // [무한 왕복 차단] 되감기로 끝난 텀블의 진행 방향. 키를 누르고 있으면 홀드 간격(0.03초) 뒤
    // 같은 방향으로 곧바로 다시 시도해 같은 지점에서 또 막히고, "한 칸 갔다가 되돌아오기"를 무한
    // 반복해 순 이동이 0이 된다. 그 방향만 기억했다가 흘려보내면 한 번 튕기고 멈춘다.
    // 시간 쿨다운이 아니라 방향 기억인 이유: 쿨다운은 벽에서 옆으로 빠지는 조향까지 굼뜨게 만든다.
    private Vector3 rewoundDir;
    private bool rewoundDirValid;

    // 연속으로 발밑이 빈 시간. 착지하는 순간 0으로 돌아간다(순간적으로 뜬 것과 진짜 낙하를
    // 가르는 것은 "얼마나 오래" 떠 있었는가뿐이라, 별도 상태 없이 누적 시간 하나로 충분하다).
    private float airborneElapsed;

    // 진단용 — Blocked가 true를 돌린 그 콜라이더와 침투량. 텀블 하나당 한 번만 찍는다(스팸 금지).
    private Collider blockCollider;
    private Vector3 blockDir;
    private float blockDist;
    private bool blockLogged;

    // PlayerVisualRoll을 끄지 않고 "중립화"하기 위해 저장해 두는 원래 값 (아래 SetVisualRoll 주석)
    private float savedMaxTumbleAngle;
    private float savedSlideTiltAngle;
    private bool savedAlignToGround;
    private bool visualRollNeutralized;

    private readonly Collider[] overlapBuffer = new Collider[16];
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[8];

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

        // 모드 전환은 한 통과에 한 번뿐이라 스팸이 안 된다. 반대로 이 줄이 물리 스텝 간격으로
        // 쏟아지면 그 자체가 증상이다 — 2026-08-23 `Portal`의 Toggle 재발화 루프를 이걸로 잡았다.
        if (logBlocking)
            Debug.Log($"[RollMode] {name}: 모드 {(on ? "ON " : "OFF")} @ x={transform.position.x:F3} " +
                      $"z={transform.position.z:F3} t={Time.time:F3}", this);

        if (on)
        {
            RollModeActive = true;
            flipped = false;
            rewoundDirValid = false;
            airborneElapsed = 0f;
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
            // [텀블 도중에 꺼질 수 있다 — 자세를 세우지 않고 놓으면 그 각도로 영구히 굳는다]
            // 포탈을 되지나면 Toggle이 호 한가운데서 모드를 끈다(계단을 올라 포탈로 되돌아가는
            // 경로가 정확히 그것이다). 정육면체·정사면체는 FreezeRotation이라 물리가 자세를
            // 바로잡아 주지 않아, 모서리로 선 채 굳고 그대로 기존 이동 방식으로 돌아간다.
            // AdvanceTumble의 3단계(되감기도 막힘)가 이미 같은 이유로 같은 처리를 한다 — 위치는
            // 그대로 두고(겹침은 PhysX의 침투 해소가 밀어낸다) 자세만 정준으로 세운 뒤 놓는다.
            if (state != State.Idle && rb != null) rb.MoveRotation(CanonicalRotation(flipped));

            Release();
            state = State.Idle;
            rewoundDirValid = false;
            RollModeActive = false;
            if (jump != null) jump.enabled = true;
            SetVisualRollNeutralized(false);
        }
    }

    public void ToggleRollMode() => SetRollMode(!RollModeActive);

    /// <summary>포탈을 지날 때 위치를 문 기준으로 맞춘다 — 폭 축(포탈의 로컬 X)은 문 중앙으로
    /// 통째로 스냅하고, 진행 축은 문 평면을 원점으로 한 <b>칸 격자에 양자화</b>한다(정육면체만,
    /// 아래 참고). 그래서 굴리기 격자가 매번 같은 자리에 잡힌다.
    ///
    /// [왜 필요한가] 피벗축(<c>Cross(up, dir)</c>)은 항상 폭 방향과 평행이라, 회전 중 그 축과
    /// 나란한 성분(폭 좌표)은 절대 바뀌지 않는다 — 즉 문을 치우쳐 지나간 순간의 좌우 오차가
    /// 이후 몇 칸을 굴러도 그대로 보존된 채 벽과의 여유를 갉아먹는다. 통로 폭이 빠듯하면(예:
    /// 셀 1.0U에 여유 ±0.05U) 이 오차 하나로 벽에 스치는 막힘→되감기가 반복된다.
    ///
    /// [격자 원점을 저장한다는 뜻이 아니다] §5.2/폴더 CLAUDE.md의 "갈라진 지점 #1"과 충돌하지
    /// 않는다 — 이 메서드는 그 순간의 실제 위치를 옮길 뿐, 옮긴 값을 따로 기억하지 않는다. 다음
    /// 텀블은 여느 때처럼 <see cref="BeginTumble"/>이 그 시점의 접촉 모서리에서 피벗을 다시
    /// 구한다. 순간 스냅인 이유: 텀블 시작 전(아직 dynamic)에 한 번만 일어나는 보정이고, 필요한
    /// 이동량이 트리거 폭(2.4U 기준 최대 ±1.2U, 실사용은 그보다 훨씬 작다) 안으로 제한되어 있어
    /// 보간 틀어짐이 눈에 띄지 않는다 — 짧은 보간을 얹을 이유가 없다.
    ///
    /// [왜 진행 축까지 맞추게 됐나 — 2026-08-23, 계단이 매번 다르게 동작했다]
    /// 원래는 "플레이어가 실제로 지나간 그 지점을 그대로 첫 텀블의 출발점으로 쓴다"였다. 그런데 그
    /// 지점은 <b>트리거 면 + 콜라이더 반폭 + 그 프레임의 이동량</b>이라 눈에 보이지도 않고 매번
    /// 흔들린다(이속 5면 물리 스텝당 0.1U). 칸 크기와 트레드 깊이가 같은 계단에서는 그 위상 오차가
    /// 그대로 "몸 뒤쪽이 윗단 처마 밑에 걸침"이 되고, 재접지가 낙차를 흡수해 몸을 내리는 순간
    /// 윗단을 파고들어 막힌다(2026-08-23 실측: 위상 어긋남 0.377U → 침투 0.195, 매 단 막힘).
    /// <c>Blocked</c> 허용치가 0.005라 씬에서 계단을 옮겨 맞춰도 다음 플레이에 또 어긋난다 —
    /// 원점을 <b>보이는 것</b>(문 평면)에 못박는 것 말고는 재현 가능하게 만들 방법이 없다.
    /// 대가는 통과 순간 최대 반 칸(정육면체 0.5U) 위치 스냅이다. §5.2의 "격자 원점을 저장하지
    /// 않는다"와 충돌하지 않는다 — 저장하는 상태는 여전히 없고, 그 순간 위치만 옮긴다.
    ///
    /// [정사면체는 제외한다] 정사면체의 유효 방향은 진입 기준 45°/120° 간격이라 <b>문의 진행축과
    /// 나란한 방향이 아예 없다</b>(§5.3의 지그재그). 그 축으로 0.8165U 격자에 양자화하면 실제
    /// 이동 격자와 무관한 자리로 옮기는 것이라 안 하느니만 못하다. 정육면체는 유효 방향이 90°
    /// 간격이라 문을 격자축에 맞춰 놓으면 진행축이 곧 격자축이다 — <b>문을 비스듬히 놓으면 이
    /// 양자화도 의미가 없다</b>(레벨 디자인 제약).</summary>
    public void AlignAcrossPortal(Vector3 portalCenter, Vector3 widthAxis)
    {
        if (rb == null || state != State.Idle) return; // 텀블 도중엔 피벗 계산을 건드리면 안 된다

        Vector3 axis = widthAxis;
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f) return;
        axis.Normalize();

        Vector3 current = rb.isKinematic ? transform.position : rb.position;
        Vector3 target = current + axis * Vector3.Dot(portalCenter - current, axis);

        // 진행 축 = 폭 축과 직교하는 수평축. 문 평면을 원점으로 한 칸 격자에서 가장 가까운 칸
        // 중심으로 양자화한다(이동량은 항상 반 칸 이하). 위 요약의 [왜 진행 축까지…] 참고.
        // ⚠ 격자 원점은 **칸 중심이 문 평면에 오는** 규약이다 — 계단·통로는 문 평면에서 칸 정수배
        //   거리에 트레드 중심이 오도록 놓아라(정육면체 1.0U). 반 칸 어긋나면 매 단 걸린다.
        if (geo.flipYaw <= 0f)
        {
            Vector3 travel = Vector3.Cross(Vector3.up, axis).normalized;
            float cell = 2f * geo.inradius * CurrentScale;
            float along = Vector3.Dot(target - portalCenter, travel);
            target += travel * (Mathf.Round(along / cell) * cell - along);
        }

        if ((target - current).sqrMagnitude < 1e-8f) return; // 이미 정렬됨
        if (rb.isKinematic)
        {
            transform.position = target;
        }
        else
        {
            rb.position = target;
            transform.position = target; // 렌더/즉시 조회 쪽도 같이 맞춘다(RespawnController와 같은 관용구)
        }
    }

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

        // 남이 이 몸을 붙잡았으면(리스폰·실타래·투석기 등) 모드를 취소한다. 체크포인트가 굴리기
        // 구역 밖일 수 있어, 거기서 굴리기로 부활하면 LD-01 필수 경로를 굴리기로 지나가게 된다.
        // 신호가 하나가 아니다 — 킬 라인 리스폰은 isKinematic을 켜지 않고 ExternallyDriven만
        // 켠다(useFade:false 경로). 페이드 리스폰만 isKinematic을 켠다.
        // ⚠ 이 검사는 반드시 아래 "수평 이동 봉쇄"보다 **앞**에 있어야 한다. 뒤에 두면 남이 몸을
        //   가져간 그 스텝에 우리가 수평 velocity를 한 번 지우고 나서 모드를 끈다 — 투석기 발사처럼
        //   그 스텝의 velocity 대입이 전부인 기믹은 수평 성분을 통째로 잃는다(지연이 아니라 소실이다).
        //   grabbed가 true인 동안(텀블 중)은 조건이 false라 아래 상태 분기로 그대로 흘러간다.
        if (!grabbed && (mover.ExternallyDriven || rb.isKinematic))
        {
            SetRollMode(false);
            return;
        }

        // [수평 이동 봉쇄 — 굴리기 모드의 수평 이동 출처는 텀블뿐이어야 한다]
        // 우리가 몸을 붙잡는 구간(ExternallyDriven + isKinematic)은 실질적으로 "텀블 중"뿐이라,
        // 그 밖의 프레임에서는 PlayerMover가 그대로 velocity를 대입해 기존 방식으로 미끄러진다 —
        // 스냅 실패 / 막힌 칸 / 홀드 간격 / 공중 / 키를 누른 첫 프레임이 전부 그 구간이다.
        // 그렇다고 ExternallyDriven을 모드 전체로 넓히면 RespawnController.IsHeld가 항상 true라
        // 장외 추락 시 킬 라인 리스폰이 무한 연기돼 소프트락이 된다(폴더 CLAUDE.md). 그래서
        // 붙잡기 규칙은 그대로 두고, 붙잡지 않은 프레임의 수평 성분만 매 스텝 지운다.
        // Y는 절대 덮어쓰지 않는다 — 중력·낙하의 소유물이다.
        // 조기 return이 여럿이므로 반드시 그 전부보다 앞에 둔다.
        if (!rb.isKinematic) rb.velocity = new Vector3(0f, rb.velocity.y, 0f);

        if (state != State.Idle)
        {
            AdvanceTumble();
            return;
        }

        // 공중이면 굴릴 바닥이 없다. 어느 쪽이든 붙잡기를 놓아(§7.3) 킬 라인·R 리스폰이 정상
        // 동작하게 한다 — 붙잡은 채로 두면 RespawnController.IsHeld가 true라 장외로 떨어져도
        // 리스폰이 무한 연기되어 소프트락이 된다. 모드까지 풀지는 exitRollModeOnFall이 정한다.
        if (!IsGrounded())
        {
            rewoundDirValid = false; // 착지 지점의 격자는 새로 잡힌다 — 옛 막힘 기억을 끌고 가지 않는다
            airborneElapsed += Time.fixedDeltaTime;

            // 유예 안이면 턱·계단을 내려가다 순간적으로 뜬 것으로 보고 붙잡기만 놓는다(수직 낙하,
            // 모드는 유지). 격자는 상태가 없어 착지 지점에서 저절로 이어진다. 유예를 넘겨야 비로소
            // exitRollModeOnFall이 정한 대로 간다 — 진짜 절벽 낙하다.
            if (airborneElapsed <= fallExitGrace) Release();
            else if (exitRollModeOnFall) SetRollMode(false); // Release는 SetRollMode 안에서 함께 돈다
            else Release();
            return;
        }
        airborneElapsed = 0f;

        if (Time.time < nextTumbleTime) return;

        Vector3 dir; bool hasInput;
        if (!TryReadDirection(out dir, out hasInput))
        {
            // 입력이 "아예 없을" 때만 몸을 놓는다. 이 창이 있어야 굴리기 구간 안에서 R이 먹는다.
            // 반대로 입력은 있는데 스냅만 실패한 경우까지 놓으면 키를 누르고 있는 내내
            // Grab↔Release로 isKinematic이 매 프레임 토글돼 Rigidbody 보간 이력이 리셋되고
            // 눈에 띄는 팝이 된다 — 그때는 붙잡은 채로 그냥 아무것도 하지 않는다(제자리 무반응).
            if (!hasInput)
            {
                rewoundDirValid = false;
                Release();
            }
            return;
        }

        // 되감기로 튕겨 돌아온 그 방향은 즉시 재시도하지 않는다(위 rewoundDir 주석). 방향이 달라지면
        // 그 자리에서 기억이 풀리므로 다른 방향키는 한 프레임도 지연 없이 먹는다.
        if (rewoundDirValid && Vector3.Dot(dir, rewoundDir) > 0.99f) return;
        rewoundDirValid = false;

        BeginTumble(dir);
    }

    private bool IsGrounded() => groundContact == null || groundContact.IsGrounded;

    /// <summary>입력을 현재 유효한 방향 중 가장 가까운 것으로 스냅한다. 허용 각도를 벗어나면 false.
    /// <paramref name="hasInput"/>은 "방향키가 눌려 있긴 한가"로, 호출부가 <b>스냅 실패</b>와
    /// <b>입력 없음</b>을 구분해 붙잡기를 유지할지 놓을지 가른다.</summary>
    private bool TryReadDirection(out Vector3 dir, out bool hasInput)
    {
        dir = Vector3.zero;
        hasInput = false;

        // Tab으로 조작권이 없는 몸이 남의 입력을 따라 굴러가면 안 된다. 진행 중인 텀블은
        // 완주시키되(모서리로 선 채 굳는 것을 막는다) 새 텀블은 조작권이 있을 때만 시작한다.
        if (!mover.IsControlled) return false;

        // GetAxis(스무딩)가 아니라 GetAxisRaw다 — 이산 스텝에서는 키를 누른 첫 프레임의 값이
        // 아직 0에 가까워 첫 텀블의 방향 스냅이 엉뚱한 쪽으로 걸린다.
        Vector3 raw = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (raw.sqrMagnitude < 0.01f) return false;
        hasInput = true;

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
        SnapPivotToGround(scale);

        // 진행 방향으로 넘어가는 회전축. up이 dir로 가는 방향이라 몸이 dir 쪽으로 넘어간다.
        pivotAxis = Vector3.Cross(Vector3.up, dir).normalized;

        // 소요 시간 = 한 칸 ÷ 그 도형의 이속. 굴리는 속도를 걷는 속도에 묶어 둔다.
        float step = 2f * geo.inradius * scale;
        float speed = (identity != null && identity.stats != null && identity.stats.moveSpeed > 0.01f)
            ? identity.stats.moveSpeed
            : 3.5f;
        tumbleDuration = Mathf.Max(0.02f, step / speed * Mathf.Max(0.01f, tumbleDurationScale));

        // 1단계 사전 검사 — 호(弧) 중간 자세만 본다. **도착 자세는 일부러 검사하지 않는다.**
        // 막힘의 반응을 3단으로 나누기 위해서다:
        //   · 벽에 딱 붙음(호 중간부터 막힘) → 여기서 걸려 완전 무반응(제자리). 시작조차 못 하는
        //     상황이라 그게 맞다. §9의 레벨 디자인 게이트(폭 1.0~1.2U 복도 = 정육면체 전용,
        //     높이 1.25~1.40U 터널 = 정사면체 전용)도 이 검사가 그대로 판정한다.
        //   · 애매하게 막힘(넘어갈 공간은 있는데 도착 칸이 막힘) → 통과시켜 텀블을 시작하고,
        //     진행 중 Blocked가 걸려 **기존 되감기(State.Rewinding)** 가 그대로 "굴러가다 튕겨
        //     돌아오는" 연출이 된다. 새 상태를 만들지 않고 이미 있는 경로를 쓰는 것이 요점이다.
        //   · 절벽 → 애초에 막힌 게 없으니 굴러가서 떨어진다(FinishStep의 접지 검사가 받는다).
        Vector3 midPos; Quaternion midRot;
        PoseAt(geo.tumbleAngle * 0.5f, scale, out midPos, out midRot);
        if (Blocked(midPos, midRot, scale))
        {
            // [2026-08-21 진단 추가] 이 사전 검사는 원래 "제자리 무반응"이라 로그가 없었다 — 그런데
            // 정사면체가 매 시도 이 분기에서 막히면 사용자 눈에는 AdvanceTumble 쪽 되감기와 똑같이
            // "아예 안 움직인다"로 보이면서도 logBlocking이 한 번도 안 찍혀 원인 특정이 안 됐다.
            // LogBlocking은 blockLogged 가드가 있어 성공적으로 Grab()할 때까지 한 번만 찍는다 —
            // 계속 막히는 동안 스팸이 안 된다.
            LogBlocking(geo.tumbleAngle * 0.5f, 0.5f, scale, midPos);
            nextTumbleTime = Time.time + Mathf.Max(0.05f, holdRepeatInterval);
            return;
        }

        Grab();
        elapsed = 0f;
        blockLogged = false;
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
            rewoundDir = tumbleDir;
            rewoundDirValid = true;
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
                LogBlocking(geo.tumbleAngle * Ease(t), t, scale, pos);
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

    /// <summary>피벗의 Y만 실제 바닥에 다시 얹는다. 수평 성분은 건드리지 않는다.</summary>
    // [왜 매 텀블 재접지하는가 — "평지처럼 보이는 기울어진 바닥"이 굴리기를 무한 왕복시킨다]
    // 텀블 모델은 월드가 완벽한 수평이라고 가정한다: 피벗축이 Cross(up, dir)라 수평이고, 도착
    // 밑면 평면이 출발과 정확히 같은 Y다(자체 점검의 "침하 0" 불변식이 곧 그 가정이다). 그런데
    // 굴리는 내내 isKinematic이라 칸 사이에 물리가 다시 접지시켜 주지 않는다. 그래서 바닥이
    // 0.16°만 기울어 있어도 — SampleScene의 주 바닥이 실제로 그렇다(-Z가 오르막, 1칸당 0.0028U) —
    // 오르막 쪽 침투가 칸마다 쌓여 2칸째에 Blocked 허용치(0.005)를 넘고, 그 뒤로는 "한 칸 갔다가
    // 되감기"를 무한 반복해 순 이동이 0이 된다. 내리막에서는 반대로 칸마다 0.0028씩 떠오른다.
    //
    // 여기서 피벗 Y만 매 텀블 실제 바닥에 다시 얹으면 오차가 그 칸 안에서 끝나고 누적되지 않는다.
    // 씬 바닥의 회전을 0으로 되돌리는 쪽은 이번 한 판만 고치는 대증요법이다 — 0.16°는 에디터에서
    // 실수로 한 번 끌면 다시 생기고 눈으로는 절대 안 보여서, 새 맵마다 같은 버그가 재발한다.
    // 그래서 허용치(0.005)도 올리지 않았다. 누적을 끊었으므로 잔여 오차는 영원히 그 안에 머문다.
    //
    // 보정으로 pivotPoint가 움직이면 PoseAt(0)이 지금 몸 위치와 그 δ만큼 어긋나는데, 그게 정상이다
    // — 가라앉은(또는 떠오른) 만큼 되올리는 보정이고, 텀블 첫 MovePosition에서 함께 반영된다.
    private void SnapPivotToGround(float scale)
    {
        // [원인 3 — 레이 XZ는 피벗 그 자체가 아니라 진행 방향 반대쪽(지금 칸의 안쪽)으로 물러난
        // 지점이다] 피벗은 정의상 "지금 서 있는 칸"의 리딩 엣지 = 칸 경계선 그 자체다(정육면체는
        // 중심에서 inradius=0.5 떨어진 곳, 딱 옆 칸과 맞닿는 선). 그 자리에서 그대로 레이를 쏘면
        // 부동소수 오차로 좌표가 경계 반대쪽(다음 칸)으로 살짝 넘어갈 수 있고, 다음 칸 바닥이 지금
        // 칸보다 낮으면(계단 등) 그 낮은 높이를 "지금 칸 바닥"으로 잘못 채택한다 — 침투 상한
        // (GroundSnapCorrectionCap) 미만이면 그대로 받아들여져 몸을 실제보다 낮게 내려앉힌다
        // (2026-08-20 실측: 계단 두 단이 딱 경계에서 맞닿고 높이차 0.2 < 0.25라 그대로 통과, 그
        // 결과 침투 0.195가 났다). 진행 방향 반대쪽으로 GroundSnapRayBackoff만큼 물러나면 항상
        // "지금 칸" 안쪽을 짚는다 — 물러나는 거리가 어떤 도형의 inradius보다도 훨씬 작아 정상 칸
        // 안쪽 판정을 절대 벗어나지 않는다. 평지에서는 이 정도 수평 이동으로 바닥 높이가 거의 안
        // 바뀐다(SampleScene 바닥 기울기 0.16° 기준 오차 0.0001U 수준). pivotPoint 자체(회전축
        // 위치)는 이 지점에서 손대지 않는다 — 아래 top 비교·대입 전부 pivotPoint.y만 쓴다.
        float top = SampleGroundTop(pivotPoint - tumbleDir * (GroundSnapRayBackoff * scale), scale);

        // [전방 프로브 — "오르는 단차"만, 그것도 아주 얕게 흡수한다 (2026-08-23 리프트 플레이테스트)]
        // 위 백오프는 지금 칸만 보게 만든 장치라(원인 3), 도착 칸이 δ만큼 **높을 때** 그 높이를
        // 코드 어디서도 보지 않는다. 도착 밑면 평면은 pivotPoint.y로 확정되므로(CanonicalPoseOf)
        // 몸이 회전 내내 그 높은 바닥을 파고들고, 그대로 BeginTumble의 **사전 검사**(호 중간 자세)에
        // 걸려 "제자리 무반응"이 된다 — 되감기까지 가지도 않는다. 사전 검사 침투는 정육면체 δ,
        // 정사면체 0.707δ라 Blocked 허용치(0.005×scale) 기준 δ가 5~7 mm만 넘어도 시작조차 못 한다.
        // 반대로 도착 칸이 **낮으면** 회전 호가 피벗 평면 아래로 내려가는 일이 없어 막힐 게 없다
        // (정육면체 y'(θ) = a·cosθ + b·sinθ ≥ 0) — 내려가는 단차는 되고 올라가는 단차만 안 되던
        // 비대칭의 전부가 여기다. 피벗 Y를 도착 바닥까지 끌어올리면 몸 전체가 δ만큼 들린 채 돌아
        // 호가 도착 평면 위에 머물고, 끝나면 정확히 그 위에 앉는다.
        //
        // 샘플 지점 = 도착 밑면의 중심(피벗에서 진행 방향으로 내접원 반지름). 도착 칸은 피벗에서
        // 0~2×inradius를 차지하므로 정확히 그 한가운데라 다음다음 칸으로 절대 안 샌다. 후방과
        // 대칭으로 GroundSnapRayBackoff(0.05)만 나가는 것은 안 된다 — 피벗은 격자가 아니라 그때그때
        // 몸 위치에서 나오므로, 단의 모서리가 피벗보다 0.05U 넘게 앞에 있으면 그대로 놓친다.
        //
        // ⚠ **지금 칸보다 낮은 값은 절대 채택하지 않는다(ahead > top).** 낮은 쪽을 받아들이는 순간
        //   원인 3(경계에서 옆 칸을 짚어 몸이 아래층으로 내려앉는 오판)이 그대로 되살아난다.
        // ⚠ 상한(stepUpCap) 검사를 여기서 따로 도는 이유: 앞이 벽이라 상한을 넘을 때 뭉뚱그려
        //   대입하면 아래 최종 검사가 통째로 실패해 **지금 칸의 정상 보정까지 잃는다**.
        // 평지 드리프트는 생기지 않는다 — pivotPoint.y는 누적 가산이 아니라 매 텀블 실측값 절대
        // 대입이라(아래 한 줄) 이전 보정이 다음 측정에 되먹임되는 경로 자체가 없다. 그래서 데드존도
        // 두지 않는다(데드존을 두면 "그 문턱 미만 단차는 여전히 못 오른다"는 원래 버그가 남는다).
        if (stepUpCap > 0f)
        {
            // 두 점을 짚는다 — 도착 칸 **한가운데**와 **먼 끝**. 한 점(한가운데)만으로는 단의 모서리가
            // 도착 칸의 먼 절반에 있을 때 통째로 놓친다: 2026-08-23 리프트가 정확히 그 배치였다
            // (리프트 동쪽 끝 X=-20.2, 메인 지면 서쪽 면 X=-20.0875 — 정사면체가 가장자리에서 0.3U
            // 넘게 떨어져 서면 한가운데 점이 아직 리프트 위라 단차를 못 본다. 텀블은 0.8165U 단위라
            // 가장자리에 더 붙을 수도 없어 그 자리에 영영 갇힌다).
            // 먼 끝은 백오프만큼 물러나 짚는다 — 도착 칸의 리딩 엣지 그 자체를 짚으면 원인 3과 같은
            // 이유로 다음다음 칸을 잘못 짚을 수 있다. 두 점 다 도착 칸(피벗에서 0~2×inradius) 안이라
            // 계단처럼 트레드가 셀 크기와 같은 지형에서도 다음 단을 넘겨보지 않는다.
            float ahead = Mathf.Max(
                SampleGroundTop(pivotPoint + tumbleDir * (geo.inradius * scale), scale),
                SampleGroundTop(pivotPoint + tumbleDir * ((2f * geo.inradius - GroundSnapRayBackoff) * scale), scale));

            // 채택하면 여기서 바로 확정하고 나간다. 아래 하강용 상한(GroundSnapCorrectionCap)을
            // 다시 통과시키면 **stepUpCap을 0.25보다 크게 잡아도 아무 일이 안 일어난다** — 노브가
            // 조용히 죽는다. 오르는 쪽 상한은 stepUpCap 하나뿐이고, 그 검사는 이미 여기서 끝났다.
            if (ahead > top && ahead > pivotPoint.y && ahead - pivotPoint.y <= stepUpCap * scale)
            {
                pivotPoint.y = ahead;
                return;
            }
        }

        // 보정 상한 — 피벗이 절벽 모서리나 계단 턱에 걸치면 히트가 아래층 바닥이라, 보정했다간 몸이
        // 통째로 한 층 아래로 순간이동한다. 그럴 바엔 모델 값을 그대로 쓰는 편이 안전하다(막히면
        // 되감기가 받고, 발밑이 비면 FinishStep의 접지 검사가 받는다). 히트가 없으면 top이 -inf라
        // 이 조건이 그대로 걸러 준다 — 별도 플래그가 필요 없다.
        // 1/4칸이라는 값: 칸당 높이차가 그만큼이면 경사 약 14°다. 그보다 가파른 면은 굴리기 격자로
        // 다룰 지형이 아니고, 계단·턱과 구분되지도 않는다.
        if (Mathf.Abs(top - pivotPoint.y) <= GroundSnapCorrectionCap * scale) pivotPoint.y = top;
    }

    /// <summary>주어진 XZ에서 아래로 쏴 "딛고 설 수 있는 면" 중 가장 높은 Y. 히트가 없으면 -inf.</summary>
    // 피벗은 정의상 바닥 평면 위의 점이라 그 자리에서 쏘면 시작점이 이미 바닥 안쪽일 수 있다
    // (가라앉은 경우). 도형 반높이만큼 띄워서 쏘고, 사거리는 띄운 만큼 + 아래 여유로 한 칸.
    // 아래 여유(0.5×scale)는 아래 보정 상한(0.25×scale)의 두 배라, 받아들일 보정은 항상 사거리 안이다.
    // 띄우는 높이 0.5×scale은 전방 프로브에도 그대로 유효하다 — stepUpCap(≤0.25) 이하로 높은 도착
    // 바닥은 언제나 시작점보다 아래에 있다. 그보다 높은 벽은 시작점이 콜라이더 안이라 Unity가 히트를
    // 주지 않고, 그 결과 -inf가 되어 "오를 수 없는 것"으로 저절로 걸러진다.
    private float SampleGroundTop(Vector3 rayXZ, float scale)
    {
        int n = Physics.RaycastNonAlloc(rayXZ + Vector3.up * (0.5f * scale), Vector3.down,
                                        groundHitBuffer, 1.0f * scale, ~0, QueryTriggerInteraction.Ignore);

        // RaycastNonAlloc은 거리순 정렬을 보장하지 않는다. 아래로 쏜 것 중 가장 높은 히트가 곧
        // "딛고 선 면"이라 순서와 무관하게 그것만 고른다.
        float top = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (IsOwn(groundHitBuffer[i].collider)) continue;   // 피벗은 자기 몸 바로 밑이다 — 필터 없으면 자기를 때린다
            if (groundHitBuffer[i].normal.y <= 0.5f) continue;  // PlayerGroundContact의 기본 문턱과 같은 감각. 벽 옆면은 바닥이 아니다
            if (groundHitBuffer[i].point.y > top) top = groundHitBuffer[i].point.y;
        }
        return top;
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
            {
                blockCollider = other;
                blockDir = dir;
                blockDist = dist;
                return true;
            }
        }

        return false;
    }

    // [왜 이 로그가 필요한가] 막힘은 씬에서 "왔다갔다한다"로만 보이고, transform.name만 찍으면
    // 씬에 같은 이름이 여럿이라 무엇이 막았는지 못 찾는다. 하이어라키 전체 경로와 침투 깊이가
    // 있어야 "바닥인가 / 다른 플레이어인가 / 허용치가 빡빡한가"를 로그 한 줄로 가른다.
    private void LogBlocking(float theta, float t, float scale, Vector3 rootPos)
    {
        if (!logBlocking || blockLogged || blockCollider == null) return;
        blockLogged = true;

        string kind = identity != null ? identity.Kind.ToString() : "?";
        Debug.LogWarning(
            $"[Portal] 텀블 막힘→되감기 | 도형={kind} scale={scale:F3} | θ={theta:F1}° t={t:F3} state={state} | " +
            $"막은 것={HierarchyPath(blockCollider.transform)} ({blockCollider.GetType().Name}) | " +
            $"침투={blockDist:F5} 방향=({blockDir.x:F3},{blockDir.y:F3},{blockDir.z:F3}) | " +
            $"Root=({rootPos.x:F3},{rootPos.y:F3},{rootPos.z:F3}) " +
            $"진행=({tumbleDir.x:F3},{tumbleDir.y:F3},{tumbleDir.z:F3})", blockCollider);
    }

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
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
    // (PRD §14). 바꿀 때 이 한 줄만 만진다.
    //
    // [SmoothStep(ease-in-out)에서 ease-in으로 바꾼 이유 — 연속 회전이 버벅였다]
    // SmoothStep은 매 칸 끝에서 속도가 0으로 죽었다가 다음 칸을 다시 0에서 출발한다. 거기에
    // 홀드 반복 간격까지 겹쳐 칸 사이 "죽은 시간"이 두 배가 되고, 그게 버벅임으로 읽혔다.
    // ease-in은 균형점을 넘어 넘어지는 실제 텀블 곡선이라 칸 끝이 (멈춤이 아니라) 착지 비트로
    // 읽힌다 — 칸 리듬은 그대로 남고 이어지는 느낌만 살아난다.
    private static float Ease(float t) => 1f - Mathf.Cos(t * Mathf.PI * 0.5f);

    void OnDisable()
    {
        // 씬 전환·오브젝트 파괴 도중 kinematic + ExternallyDriven이 켜진 채로 남으면 그 몸은
        // 영구히 조작 불가가 된다.
        if (RollModeActive) SetRollMode(false);
    }
}
