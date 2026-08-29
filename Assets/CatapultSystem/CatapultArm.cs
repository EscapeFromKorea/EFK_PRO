using UnityEngine;

/// <summary>
/// 투석기 팔의 상태(대기/장전 중/장전 완료/발사)를 관리하고, 발사 시점에 버킷 탑승자(정육면체)의
/// Rigidbody에 결정론적 velocity를 대입한다.
///
/// 개편 이력(라운드별로 무엇을 왜 바꿨는지)은 코드가 아니라 `CatapultSystem/CLAUDE.md`에 기록돼
/// 있다 — 이 요약은 "지금 코드가 왜 이렇게 동작하는가"만 담는다.
///
/// [왜 팔의 물리 반동을 그대로 쓰지 않고 velocity를 대입하나]
/// `PlayerJump.LaunchToHeight`가 질량과 무관하게 항상 정확히 H까지 오르는 것과 같은 철학이다(PRD §2
/// 단계 D). 팔 애니메이션은 순수 연출이고, 발사 속도는 "당김 비율 → 속도" 계산식으로만 정해야
/// 레벨 디자인이 도달 거리를 예측할 수 있다.
///
/// [왜 발사 방향을 armPivot이 아니라 별도 aimRoot에서 구하나]
/// armPivot 자신은 당김 연출로 로컬 X 회전이 계속 바뀐다(장전 각도). 그 회전을 그대로 발사 방향에
/// 쓰면 "당김 정도에 따라 발사 방향이 흔들리는" 부작용이 생긴다. 발사 방향은 오직 조향(요 회전,
/// `CatapultSteerHandle`이 돌리는 투석기 루트)에만 반응해야 하므로, 팔의 장전 회전과 무관한 루트
/// Transform(aimRoot)에서 forward/right를 읽어 고정 피치각(launchPitch)만큼 위로 꺾어 쓴다.
/// 실제 발사 방향은 `-aimRoot.forward` 기준이다(당김 앵커 쪽으로 날아가야 한다는 플레이 실측으로
/// 확정, `Fire()` 참고) — 기준 벡터를 뒤집을 때는 회전 부호(`+launchPitch`)도 함께 뒤집어야 "위로
/// 꺾는다"는 기하학적 의미가 유지된다(부호 하나만 바꾸면 위/아래가 반대로 나온다).
///
/// [restAngle/pulledAngle — armPivot 로컬 +Y 블루프린트 자식 구조와 파묻힘 여유]
/// `armVisual`/`bucket`(버킷 그룹)은 armPivot의 로컬 +Y 오프셋 자식이라, 회전 후 버킷 높이는
/// `armLength * cos(currentAngle)`로 결정된다 — cos가 최대인 0°에 가까울수록 버킷이 높다. 그래서
/// rest는 0°에 가깝게(쉬는 동안 버킷이 높게), pulled는 그보다 90°에 가깝게(당길수록 버킷이 낮게)
/// 잡는다. 두 값은 버킷 치수가 바뀔 때마다 "바닥 모서리가 받침대 윗면 아래로 파고들지 않는지"를
/// 재검산해 조정돼 왔다 — 현재 파묻힘 공식과 상수 유도는 `CatapultMenuItem.cs` 상단 주석,
/// `docs/PRD/Catapult.md` §6 참고. `pulledAngle`이 90°가 아니라 80°인 이유는 파묻힘이 아니라
/// 조향석 도킹 지점과의 물리적 겹침 회피다.
///
/// [발사 — "가속 스윙 후 분리"로 관통 버그를 원천 차단]
/// 발사 즉시 velocity를 대입하면 그 순간부터 반동 스프링이 restAngle로 빠르게 되감기 시작해,
/// 사출된 정육면체와 되감기는 버킷이 discrete 충돌 감지로 못 잡을 속도로 스쳐 지나가 관통이 났다
/// (터널링과 같은 종류). 그래서 발사는 `ArmState.Launching` 램프(팔이 pulledAngle에서 restAngle까지
/// `launchSwingDuration` 동안 `t01*t01` ease-in으로 가속 스윙)로 처리한다 — 스윙 동안 탑승자는
/// 계속 버킷에 부모화+킨네마틱 상태로 남아 팔과 같은 몸처럼 움직이므로 관통 자체가 물리적으로
/// 불가능하고, 스윙이 끝나 팔이 restAngle에 도달한 순간에만 `bucket.ConsumeOccupant()`로 분리하며
/// 결정론적 velocity를 대입한다. **"결정론적 velocity 대입" 철학은 그대로다 — 바뀐 건 "언제
/// 대입하는가"뿐이다.** 발사 속도는 `ratio01`(입력)이 아니라 `launchStartAngle`(`Fire()` 호출
/// 시점에 캡처한, 팔이 실제로 도달한 각도)을 `Mathf.InverseLerp(restAngle, pulledAngle, ...)`로
/// 역산한 비율 기준이다(사용자 확정 — "당김 각도에 따라 힘의 크기도 구분되게"; 방향 계산은 각도와
/// 무관하게 그대로다). 캡처를 `Fire()` 시점에 고정하는 이유는 램프 종료 시 재계산하면 그 사이
/// 조향이 돌아 의도한 방향과 어긋날 수 있기 때문이다. 빈 버킷 발사는 램프 없이 즉시
/// `ArmState.Firing`(반동 연출만)으로 들어간다(관통할 대상 자체가 없으므로). `BeginPull`/`Fire`
/// 둘 다 `State == ArmState.Launching`이면 재입력을 무시한다 — 스윙 도중 재연결이 `State`를 덮어써
/// `ConsumeOccupant()`가 영원히 안 불리는(정육면체가 버킷에 영구히 갇히는) 버그를 막는 가드다.
///
/// [당김 — 노치(딸깍임) 양자화 + 시간 기반 ease 전환]
/// 당김 비율(0~1)을 `pullNotchCount`개의 이산 걸쇠로 양자화해 걸쇠 사이에서는 팔이 반응하지 않는
/// 래칫 질감을 낸다. 목표 노치가 바뀔 때만 `pullTransitionDuration` 동안 `Mathf.SmoothStep`(표준
/// 라이브러리, 오버슈트 없음)으로 그 각도까지 보간한다 — "노치 단계는 유지하되 전환만 부드럽게"라는
/// 확정 방향이다. 목표 노치가 다시 바뀌면(휠을 계속 돌리는 중) 그 시점의 현재 각도에서 새 램프를
/// 다시 시작한다 — 발사 램프와 달리 당김은 "중간에 방해받으면 안 된다"는 제약이 없다.
///
/// [발사 반동 — 노치와 분리된 전용 스프링 + 정착 후 클런크]
/// 반동(팔이 restAngle로 돌아오는 것)은 `StepSpringToward`(2차 스프링-댐퍼 직접 적분, 세미-임플리시트
/// 오일러)로 처리하며 전용 상수(`recoilSpringStiffness=500`/`recoilSpringDamping=22`)를 쓴다 —
/// 감쇠비 `ζ = damping/(2·√(stiffness·mass))`(질량=1 암묵 가정)가 약 0.492가 되도록 잡아, 스텝
/// 오버슈트 공식 `%OS = exp(-ζπ/√(1-ζ²))·100`으로 계산한 오버슈트가 약 16.3%(눈에 띄는 스윙백)가
/// 나온다. 노치 전환은 더 낮은 감쇠비(ζ≈0.75, 오버슈트 2.8%로 거의 안 보임)가 필요해 상수를
/// 분리했다 — 장전은 "딸깍, 딸깍" 짧고 톡톡 끊기는 클릭이 목적이고, 반동은 "하나의 연속된 스윙백"이
/// 목적이라 서로 다른 질감이 필요했기 때문이다. 스프링만으로는 "다 멈춘 뒤 한 번 더 튕기는" 이산적
/// 정착을 만들지 못하므로, 첫 정착 순간(오버슈트-감쇠 스윙이 `RecoilSettleAngleEpsilon`/
/// `RecoilSettleVelocityEpsilon` 안에 들어오는 순간)을 감지해 `angleVelocity`에 작은 충격
/// (`recoilSettleJoltVelocity`)을 한 번 더한다 — 그 뒤에도 같은 반동 스프링이 이 충격을 뒤쫓아
/// 감쇠시켜 "스윙백 → 잦아듦 → 작은 덜컥 → 정지"의 2단계 정착이 된다. `recoilJolted` 플래그로 한
/// 발사당 클런크가 정확히 한 번만 나가게 막는다(`Fire()`에서 초기화).
///
/// [ScalePad 연동 — 작아지면 더 멀리]
/// 탑승자의 `rb.mass / PlayerShapeIdentity.stats.mass` 비율의 역수를 `[1, maxWeightSpeedMultiplier]`
/// 범위로 clamp해 발사 속도에 곱한다 — 가벼울수록(비율&lt;1) 더 멀리 날아가고, 하한 1로 정상 이상
/// 무거워도 페널티는 없다(실전에서는 `CatapultBucket.heavyBoardBlockScaleRatio`가 무거운 탑승 자체를
/// 막아 이 경우가 안 생긴다).
/// </summary>
public class CatapultArm : MonoBehaviour
{
    public enum ArmState { Idle, Pulling, Loaded, Firing, Launching }

    [Header("팔 회전 (연출, 씬 튜닝 전제 — PRD 미확정 수치)")]
    [Tooltip("팔이 회전할 축. 비우면 이 컴포넌트가 붙은 Transform을 쓴다.")]
    public Transform armPivot;
    [Tooltip("당김 비율 0(연결 안 됨/막 연결)일 때의 로컬 X 각도. 발사 직후 되돌아오는 목표 각도이기도 " +
             "하다. armVisual/bucket이 armPivot 로컬 +Y 블루프린트(위쪽)로 붙어 있어, 0°에 가까울수록 " +
             "버킷이 높이 선다(클래스 상단 주석 참고). 11차 개편(2026-08-05, 사용자 확정 — '최소 0도/ " +
             "최대 90도')으로 20°→0°로 낮췄다 — 파묻힘 여유는 오히려 더 커진다(재검산 결과 대비 " +
             "+5.31, 클래스 상단 주석 참고).")]
    public float restAngle = 0f;
    [Tooltip("당김 비율 1(완전 장전)일 때의 로컬 X 각도. rest보다 90°에 가까워질수록 버킷이 낮아진다. " +
             "2026-08-04: 바구니(벽+바닥) 모서리 기준 파묻힘 검산으로 100°→70°(3차), 버킷 확대로 " +
             "70°→55°(4차), 버킷 내부 공간 재설계+ArmLength 연장으로 55°→45°(6차)로 낮췄다. " +
             "7차 개편(2026-08-05, 버킷 그룹 Transform 재배치)으로 여유가 오히려 크게 늘어 45°를 " +
             "유지했다가, 8차 개편(2026-08-05, '당김이 더 극적으로 보였으면 한다')으로 45°→90°로 " +
             "올렸다(파묻힘 마진 +0.168, 재검산 근거는 클래스 상단 주석 참고). **25차 개편" +
             "(2026-08-06)으로 90°→80°로 다시 낮췄다** — 파묻힘 때문이 아니라, 90°에서 팔이 완전히 " +
             "당겨졌을 때 버킷/팔이 조향석에 도킹된 구와 물리적으로 겹치는 버그가 실측으로 재현돼 " +
             "(사용자 보고) 여유를 두기 위해서다. 파묻힘 여유는 각도가 작아질수록 커지는 단조 관계라 " +
             "(8차 개편 재검산 참고) 80°는 90°보다 파묻힘 쪽으로는 오히려 더 안전하다 — 이번 조정은 " +
             "순수하게 조향석과의 간섭 회피가 목적이다. 3배 균등 확대(6차 3단계)는 각도를 바꾸지 " +
             "않는다 — 길이만 스케일된다.")]
    public float pulledAngle = 80f;

    [Header("장전 노치(딸깍임), 2026-08-04 7차 개편 [TBD, 임시값]")]
    [Tooltip("당김 비율(0~1)을 몇 단계의 걸쇠(노치)로 양자화할지. 크면 촘촘해 부드럽게 느껴지고, " +
             "작으면 한 걸음씩 크게 걸리는 래칫 느낌이 강해진다. 감각적 기본값.")]
    public int pullNotchCount = 10;

    [Header("장전 전환 부드러움 — 18차 개편 신규(오버슈트 없는 시간 기반 ease) [TBD, 임시값]")]
    [Tooltip("목표 노치가 바뀔 때 그 각도로 부드럽게 전환되는 데 걸리는 시간(초). 노치 자체(걸쇠 단계)는 " +
             "그대로 유지하고 전환 구간만 매끈하게 만든다(사용자 확정 — 클래스 상단 '18차 개편 (3)' " +
             "주석 참고). 너무 길면 마우스 휠을 빠르게 돌릴 때 목표를 따라가지 못해 밀리는 느낌이 " +
             "난다 — 노치 간격보다 확실히 짧게(0.15~0.3초 감각값).")]
    public float pullTransitionDuration = 0.2f;

    [Header("발사 반동 전용 스프링 [TBD, 임시값]")]
    [Tooltip("발사 후 팔이 restAngle로 되돌아갈 때 쓰는 전용 강성. 노치 전환(시간 기반 ease)과 " +
             "공유하지 않는다 — 장전은 짧고 톡톡 끊기는 클릭, 반동은 하나의 연속된 스윙백이라 서로 " +
             "다른 질감이 필요하다(클래스 상단 주석 '발사 반동' 참고). 감쇠비 ζ≈0.5(더 눈에 띄는 " +
             "오버슈트)가 되도록 잡았다.")]
    public float recoilSpringStiffness = 500f;
    [Tooltip("위 반동 스프링의 감쇠. ζ≈0.5(오버슈트 16.3%)가 되도록 잡아 스윙백이 눈에 띄게 했다. " +
             "감각적 기본값.")]
    public float recoilSpringDamping = 22f;
    [Tooltip("반동 스윙이 처음 정착하는 순간 한 번만 더해지는 각속도 충격(도/초) — '정착 후 매끈하게 " +
             "멈추는 대신 작은 덜컥(clunk)이 한 번 더 있었으면 한다'는 요청 반영. 이 충격도 같은 반동 " +
             "스프링이 뒤쫓아 감쇠시켜 작은 2차 정착을 만든다. 너무 크면 반동이 두 번째 큰 스윙처럼 " +
             "보이므로 첫 스윙보다 훨씬 작게 잡을 것. 감각적 기본값.")]
    public float recoilSettleJoltVelocity = 25f;

    [Header("발사 스윙 (17차 개편 신규 — 버킷 관통 버그 재설계) [TBD, 임시값]")]
    [Tooltip("탑승자가 있는 발사에서, 팔이 pulledAngle에서 restAngle까지 가속 스윙하는 데 걸리는 " +
             "시간(초). 이 동안 탑승자는 버킷에 부모화+킨네마틱 상태로 계속 붙어 있어(관통 원천 " +
             "차단) 팔과 정확히 같은 속도로 움직이고, 스윙이 끝나는 순간에만 분리되며 결정론적 " +
             "velocity를 받는다(클래스 상단 '발사' 주석 참고). 0.8~1.0초 사이 감각값 — 씬 튜닝 " +
             "전 임시값.")]
    public float launchSwingDuration = 0.9f;

    [Header("조준/발사")]
    [Tooltip("발사 방향의 기준이 되는 투석기 루트(요 회전 대상). 비우면 armPivot을 대신 쓴다 — " +
             "단 그러면 장전 각도가 발사 방향에 섞이므로 반드시 조향 루트를 지정할 것.")]
    public Transform aimRoot;
    [Tooltip("당김 비율 0에서의 발사 속도(m/s). PRD 확정값. 6차 개편(투석기 3배 확대)에서도 " +
             "의도적으로 그대로 뒀다 — 이건 '탄도(ballistic) 튜닝값'이라 투석기 메시가 커졌다고 " +
             "바뀔 이유가 없다(플레이어 점프 높이·이동속도도 PlayerSystem 소관이라 변하지 않았다). " +
             "9차 개편(2026-08-05)으로 당김 '비율' 자체가 거리 계산에서 마우스 휠 누적값으로 바뀌었지만, " +
             "'비율(0~1) → 발사 속도'라는 이 계산식 자체는 그대로다(docs/PRD/Catapult.md §6, 상단 9차 개편 요약 참고).")]
    public float minLaunchSpeed = 10f;
    [Tooltip("당김 비율 1(완전 장전)에서의 발사 속도(m/s). PRD 확정값. 6차 개편에서도 스케일하지 " +
             "않았다(위 minLaunchSpeed 툴팁 참고).")]
    public float maxLaunchSpeed = 18f;
    [Tooltip("발사 피치각(도, 수평 기준 위쪽). PRD 확정값 50도 고정. 각도라 스케일과 무관.")]
    public float launchPitch = 50f;

    [Header("ScalePad 연동 — 작아지면(가벼워지면) 더 멀리 (신규, 2026-08-06) [TBD, 임시값]")]
    [Tooltip("탑승자의 rb.mass / PlayerShapeIdentity.stats.mass 비율의 역수(1/비율)를 [1, 이 값] 범위로 " +
             "clamp해 발사 속도에 곱한다 — 가벼울수록(비율<1) 배율이 1보다 커져 더 멀리 날아가고, " +
             "정상 이상 무거워도 하한을 1로 둬 페널티 없이 최소 1배를 보장한다(CatapultBucket." +
             "heavyBoardBlockScaleRatio가 커진 상태는 탑승 자체를 막아 실전에서는 그 경우가 안 생기지만, " +
             "방어적으로 하한을 둔다). 감각적 기본값.")]
    public float maxWeightSpeedMultiplier = 2f;

    [Tooltip("탑승자를 보관하는 버킷. 발사 시 이 버킷에서 탑승자를 꺼낸다(빈 버킷이면 null).")]
    public CatapultBucket bucket;

    public ArmState State { get; private set; } = ArmState.Idle;

    /// <summary>12차 개편(2026-08-05) 신규 — 팔의 현재 로컬 X 각도(도). `CatapultBucket`의 탑승
    /// 각도 게이트(`boardMinArmAngle`)가 이 값을 읽어 "정사면체가 어느 정도 당겨야 정육면체가
    /// 탑승할 수 있다"는 협동 조건을 판정한다.</summary>
    public float CurrentAngle => currentAngle;

    private float currentAngle;
    private float angleVelocity; // 발사 반동(Firing) 스프링 적분용 각속도(도/초) — 18차 개편부터
                                  // 노치 전환(BeginPull)은 이 필드를 쓰지 않는다(시간 기반 ease로 교체).
    private Vector3 basePivotEuler;

    // 18차 개편 — BeginPull의 시간 기반 ease 전환 상태. 목표 노치가 바뀌는 순간의 currentAngle을
    // 시작점으로 캡처해 pullTransitionDuration에 걸쳐 목표까지 SmoothStep으로 보간한다(클래스 상단
    // "당김" 주석 참고).
    private float pullTransitionElapsed;
    private float pullTransitionStartAngle;
    private float pullTargetAngle;
    private bool pullTransitionActive;
    // 8차 개편 — 발사 1회당 "정착 후 클런크" 충격이 정확히 한 번만 나가게 막는다(Fire()에서 초기화).
    private bool recoilJolted;

    // 17차 개편 — ArmState.Launching 램프 진행 상태. launchStartAngle은 Fire() 호출 시점의
    // currentAngle(대개 pulledAngle 근처)을 캡처해 램프 시작점으로 쓴다. pendingLaunchVelocity는
    // Fire() 시점에 계산해 둔 결정론적 발사 속도 — 램프가 끝날 때 그대로 대입한다(클래스 상단 "발사"
    // 주석 참고, ratio01을 램프 종료 시점에 재계산하지 않는 이유도 그곳에 있다).
    private float launchElapsed;
    private float launchStartAngle;
    private Vector3 pendingLaunchVelocity;

    // 발사 반동이 "정착했다"고 볼 임계값 — 스프링이 미세하게 계속 진동하며 State가 영영 Firing에
    // 머무는 걸 막는다(딱 떨어지는 숫자 없이도 실제로 멈춘 것처럼 보이면 충분하다는 실용적 판단,
    // 감각적 기본값).
    private const float RecoilSettleAngleEpsilon = 0.05f;
    private const float RecoilSettleVelocityEpsilon = 2f;

    void Reset()
    {
        armPivot = transform;
    }

    void Awake()
    {
        if (armPivot == null) armPivot = transform;
        if (aimRoot == null) aimRoot = armPivot;

        basePivotEuler = armPivot.localEulerAngles;
        currentAngle = restAngle;
        ApplyAngle();
    }

    void Update()
    {
        // 17차 개편 — 발사 결정 순간부터 팔이 restAngle까지 가속 스윙하는 동안, 탑승자는 여전히
        // 버킷에 부모화+킨네마틱 상태로 붙어 있어 팔과 함께 움직인다(클래스 상단 "발사" 주석 참고).
        // 스윙이 끝나야 비로소 분리 + velocity 대입이 일어난다.
        if (State == ArmState.Launching)
        {
            launchElapsed += Time.deltaTime;
            float t01 = launchSwingDuration > 0f ? Mathf.Clamp01(launchElapsed / launchSwingDuration) : 1f;
            float eased = t01 * t01; // ease-in — 처음엔 느리게, 끝으로 갈수록 빨라진다("가속하며", 사용자 확정)
            currentAngle = Mathf.Lerp(launchStartAngle, restAngle, eased);
            ApplyAngle();

            if (t01 >= 1f)
            {
                currentAngle = restAngle;
                ApplyAngle();

                Rigidbody occupant = bucket != null ? bucket.ConsumeOccupant() : null;
                if (occupant != null)
                {
                    occupant.velocity = pendingLaunchVelocity;
                }

                // 램프가 이미 팔을 restAngle까지 데려다 놨으므로, 이어서 8차 개편의 반동 스프링을
                // 그대로 재사용한다(정지 상태에서 시작해 오버슈트 폭은 작아지지만 "정착 후 클런크"
                // 질감 자체는 코드 변경 없이 남는다 — 클래스 상단 주석 참고).
                angleVelocity = 0f;
                State = ArmState.Firing;
            }
            return;
        }

        // 발사 후 반동 연출: 장전 각도에서 rest로 스프링 오버슈트를 타며 되돌아온다(7차 개편, 8차
        // 개편으로 전용 상수 recoilSpringStiffness/Damping으로 분리 — 클래스 상단 주석 "발사 반동"
        // 참고). 재연결로 BeginPull이 다시 호출되면 그쪽이 즉시 State를 덮어써 자연스럽게 다음
        // 장전으로 넘어간다(별도 잠금 불필요 — 어차피 C로 재연결하려면 그 순간의 거리 비율이 다시
        // 반영되어야 하므로 이 편이 맞다).
        if (State == ArmState.Firing)
        {
            StepSpringToward(restAngle, Time.deltaTime, recoilSpringStiffness, recoilSpringDamping);
            ApplyAngle();

            bool settled = Mathf.Abs(currentAngle - restAngle) < RecoilSettleAngleEpsilon &&
                            Mathf.Abs(angleVelocity) < RecoilSettleVelocityEpsilon;
            if (settled && !recoilJolted)
            {
                // 8차 개편 — 첫 정착 순간에만 작은 충격을 한 번 더해 "덜컥"거리며 한 번 더 정착하게
                // 한다(클래스 상단 주석 참고). 여기서 State를 Idle로 넘기지 않아 다음 프레임부터
                // 같은 반동 스프링이 이 충격을 뒤쫓아 감쇠시킨다.
                angleVelocity += recoilSettleJoltVelocity;
                recoilJolted = true;
            }
            else if (settled && recoilJolted)
            {
                currentAngle = restAngle;
                angleVelocity = 0f;
                ApplyAngle();
                State = ArmState.Idle;
            }
        }
    }

    /// <summary>CatapultLoadController가 연결(C)된 동안 매 프레임 호출한다. ratio01은 "거리→비율" 계산
    /// 결과다 — 7차 개편으로 이 비율을 그대로 보간하지 않고 노치로 양자화한다. 18차 개편부터 노치가
    /// 바뀌는 전환은 스프링 오버슈트가 아니라 시간 기반 ease로 부드럽게 이어진다(클래스 상단 "당김"
    /// 주석 참고).</summary>
    public void BeginPull(float ratio01)
    {
        // 18차 개편(1) — 발사 스윙(Launching) 도중 재연결/재당김이 끼어들면 State가 덮어써져
        // ConsumeOccupant()가 영원히 안 불리는 버그의 근본 원인이었다(클래스 상단 "발사" 주석 참고)
        // — 스윙이 끝날 때까지 이 함수는 아무 일도 하지 않는다.
        if (State == ArmState.Launching) return;

        ratio01 = Mathf.Clamp01(ratio01);
        State = ratio01 >= 1f ? ArmState.Loaded : (ratio01 > 0f ? ArmState.Pulling : ArmState.Idle);

        // 연속 비율을 노치로 양자화한다 — 같은 걸쇠 안에서 조준자가 미세하게 움직이는 동안은 목표
        // 각도가 안 바뀌어 팔이 반응하지 않는다(실제 래칫이 걸쇠 사이에서 헛도는 것과 같다).
        int notch = pullNotchCount > 0 ? Mathf.RoundToInt(ratio01 * pullNotchCount) : 0;
        float notchedRatio = pullNotchCount > 0 ? (float)notch / pullNotchCount : ratio01;
        float targetAngle = Mathf.Lerp(restAngle, pulledAngle, notchedRatio);

        // 18차 개편(3) — 목표 노치가 바뀌는 순간에만 새 램프를 시작한다(그 시점의 currentAngle에서).
        // 같은 노치 안에서 매 프레임 재호출돼도 램프를 다시 시작하지 않는다.
        if (!pullTransitionActive || !Mathf.Approximately(targetAngle, pullTargetAngle))
        {
            pullTransitionStartAngle = currentAngle;
            pullTargetAngle = targetAngle;
            pullTransitionElapsed = 0f;
            pullTransitionActive = true;
        }

        pullTransitionElapsed += Time.deltaTime;
        float t01 = pullTransitionDuration > 0f ? Mathf.Clamp01(pullTransitionElapsed / pullTransitionDuration) : 1f;
        // 표준 라이브러리 SmoothStep(S자형 ease-in-out, 오버슈트 없음)을 그대로 재사용한다 — 새
        // 커브를 직접 발명할 이유가 없었다.
        currentAngle = Mathf.Lerp(pullTransitionStartAngle, pullTargetAngle, Mathf.SmoothStep(0f, 1f, t01));
        angleVelocity = 0f; // 노치 전환은 더 이상 스프링 적분을 쓰지 않는다 — 잔여 각속도를 남기지 않는다.
        ApplyAngle();
    }

    /// <summary>C로 연결을 해제하는 순간 호출한다. 탑승자가 있으면 즉시 발사하지 않고 팔이
    /// restAngle까지 가속 스윙하는 동안 붙잡아 뒀다가 스윙이 끝나는 순간 결정론적 velocity를
    /// 대입한다(17차 개편, 클래스 상단 주석 참고). 버킷이 비어 있으면(PRD 확정: 허용) 램프 없이
    /// 예전처럼 즉시 반동 연출만 재생한다 — 관통을 막을 탑승자 자체가 없기 때문이다.</summary>
    public void Fire(float ratio01)
    {
        // PR #54 코드검토 반영(2026-08-06, P0) — BeginPull()은 State==Launching일 때 재입력을
        // 무시하는 가드가 있지만(18차 개편), Fire()에는 같은 가드가 없었다. CatapultLoadController가
        // 재연결(TryConnect, BeginPull 무시됨) 후 바로 다시 C를 누르면 Disconnect(fire:true)가
        // 곧바로 이 함수를 호출하는데, 그 시점에도 버킷엔 탑승자가 여전히 남아 있어(스윙이 아직 안
        // 끝났으므로) 아래 조건을 그대로 통과해 launchElapsed를 0으로 되돌린다 — C를 연타하면
        // ConsumeOccupant()가 영원히 불리지 않아 정육면체가 버킷에 영구 고정된다. BeginPull과
        // 정확히 같은 가드를 여기도 둔다 — 스윙이 끝날 때까지 재호출을 전부 무시한다.
        if (State == ArmState.Launching) return;

        ratio01 = Mathf.Clamp01(ratio01);
        recoilJolted = false; // 8차 개편 — 이번 발사의 "정착 후 클런크"를 다시 한 번 쓸 수 있게 리셋.

        if (bucket == null || !bucket.HasOccupant)
        {
            State = ArmState.Firing;
            Debug.LogWarning("[Catapult] 버킷이 비어 있어 발사 연출만 재생합니다(빈 발사, 허용된 동작).");
            return;
        }

        // 18차 개편(2) — launchStartAngle을 먼저 캡처하고, 속도는 ratio01(입력)이 아니라 "팔이 실제로
        // 도달한 각도"에서 역산한다(사용자 확정 — 클래스 상단 "발사" 주석 참고). restAngle(0°)일 때
        // 최소, pulledAngle(80°)일 때 최대가 되도록 InverseLerp로 0~1 비율을 되돌린다.
        launchStartAngle = currentAngle;
        float angleRatio = Mathf.InverseLerp(restAngle, pulledAngle, launchStartAngle);
        float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, angleRatio);

        // ScalePad 연동 신규(2026-08-06) — 탑승자가 작아진(가벼워진) 상태면 더 멀리 날아가게 배율을
        // 곱한다. bucket.HasOccupant가 true인 시점부터 실제 ConsumeOccupant()(스윙 종료)까지는
        // 탑승자가 바뀌지 않으므로, 소비하지 않는 OccupantBody로 미리 들여다봐도 안전하다(클래스
        // 상단 "ScalePad 연동" 헤더 참고, 실제 게이트/식 근거는 CatapultBucket.cs 상단 주석에 있다).
        Rigidbody occupantForSpeed = bucket.OccupantBody;
        PlayerShapeIdentity occupantIdentity = occupantForSpeed != null
            ? occupantForSpeed.GetComponent<PlayerShapeIdentity>() : null;
        float weightSpeedMultiplier = 1f;
        if (occupantIdentity != null && occupantIdentity.stats != null && occupantIdentity.stats.mass > 0f)
        {
            float scaleRatio = occupantForSpeed.mass / occupantIdentity.stats.mass; // 1=Normal, <1=Shrunk(가벼움)
            weightSpeedMultiplier = Mathf.Clamp(1f / Mathf.Max(0.01f, scaleRatio), 1f, maxWeightSpeedMultiplier);
        }
        speed *= weightSpeedMultiplier;

        // 15차 개편 — 앵커 쪽(-Z)으로 발사하도록 기준 방향과 회전 부호를 함께 뒤집었다(클래스 상단
        // "왜 발사 방향을" 주석 참고 — 둘 중 하나만 뒤집으면 위/아래가 반대로 나온다).
        Vector3 dir = Quaternion.AngleAxis(launchPitch, aimRoot.right) * (-aimRoot.forward);
        pendingLaunchVelocity = dir.normalized * speed;

        State = ArmState.Launching;
        launchElapsed = 0f;
    }

    // 7차 개편 신규 — 2차 스프링-댐퍼를 직접 적분한다(질량=1 암묵 가정, 세미-임플리시트 오일러).
    // 원래는 노치 전환(BeginPull)과 발사 반동(Update의 Firing) 둘 다 이 함수를 썼지만, 18차 개편
    // (2026-08-06)으로 BeginPull이 시간 기반 ease(SmoothStep)로 바뀌면서 지금은 발사 반동
    // (recoilSpringStiffness/Damping)만 이 함수를 쓴다(클래스 상단 "당김" 주석 참고) — 함수 자체는
    // stiffness/damping을 호출부에서 받는 범용 구조 그대로 남겨 뒀다(다른 상태가 다시 이 스프링
    // 질감을 필요로 할 경우를 위해, YAGNI로 삭제하지 않음).
    private void StepSpringToward(float targetAngle, float dt, float stiffness, float damping)
    {
        float displacement = currentAngle - targetAngle;
        float accel = -stiffness * displacement - damping * angleVelocity;
        angleVelocity += accel * dt;
        currentAngle += angleVelocity * dt;
    }

    private void ApplyAngle()
    {
        armPivot.localRotation = Quaternion.Euler(currentAngle, basePivotEuler.y, basePivotEuler.z);
    }
}
