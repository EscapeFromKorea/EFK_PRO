using UnityEngine;

/// <summary>
/// 플레이어 오브젝트의 수평 이동을 담당한다.
/// IsControlled가 false인 동안(Tab으로 다른 오브젝트를 조작 중일 때)에는
/// 입력을 무시하고 수평 velocity를 0으로 만들어 제자리에 멈춘다.
/// 중력/외력(점프, 부스트 등)은 그대로 물리 시뮬레이션에 맡긴다.
///
/// [구르는 연출: 구/정육면체/정사면체 모두 같은 공식을 쓴다]
/// 접지 중 매 프레임 각속도 = Cross(Vector3.up, 이동 velocity) / rollRadius를 무게중심 기준으로
/// 하드 설정해 "구르는 것처럼" 보이게 한다(v=ωr, 매끄럽게 굴러갈 때 성립하는 공식). 예전에는
/// 정육면체/정사면체만 별도의 "모서리 피벗 텀블링(RollMode.EdgeTumble)" 방식을 썼었다 — 다면체는
/// 바닥에 "면"으로 완전히 밀착하기 때문에, 이 공식을 무게중심 기준으로 그대로 적용하면 그 면의
/// 양 끝(코너)이 동시에 반대 방향(하나는 파고들고 하나는 뜨려)으로 움직이려 해 PhysX 접촉 솔버와
/// 매 스텝 충돌해 회전이 거의 안 보이거나 씹혔었다.
///
/// 하지만 사용자 피드백으로 "점프대 위에서만이 아니라 평소 이동 중에도 항상 매끄럽게 계속
/// 회전"하는 연출이 필요하다는 게 확인되어, 스텝형 텀블링을 걷어내고 다시 세 도형 모두 이
/// 연속 회전 공식 하나로 통일했다. 대신 "완전히 평평한 면 접촉 + 순간 각속도 강제"가 만드는
/// 접촉 솔버 충돌 자체는 이 스크립트의 로직이 아니라, 접촉 지오메트리와 Rigidbody 물리 설정
/// 쪽에서 완화한다(구체적인 내용은 PlayerObjectMenuItem.cs 상단 주석 참고):
/// - 정육면체: BoxCollider(정확한 모양) 자체는 그대로 두되, 저마찰 PhysicMaterial로 접지 중
///   접촉면을 따라 생기는 마찰력을 줄여 충돌 강도를 낮춘다.
/// - 정사면체: Unity에 대응하는 기본 Primitive Collider가 없다. 꼭짓점을 이웃 쪽으로 살짝 깎은
///   통짜 convex MeshCollider(TetrahedronMeshGenerator.CreateChamferedColliderMesh)를 쓴다 —
///   뾰족한 점 대신 작은 면으로 접촉해 침투가 완만해진다. (예전엔 꼭짓점 4개에 작은 SphereCollider를
///   배치한 컴파운드였으나 구 사이 미접촉 구간 때문에 폐기됐다. 상세는 PlayerObjectMenuItem.cs 참고.)
/// - 공통: Rigidbody.maxAngularVelocity를 기본 캡(7 rad/s)보다 넉넉히 올리고(v=ωr로 나온
///   각속도가 잘리지 않도록), interpolation과 solver iteration을 높여 시각적 떨림/솔버 오차를
///   줄인다.
///
/// [솔직한 한계] 이 조치들은 "다면체가 평평한 면으로 접지한 채 순간 각속도를 강제하면 다중
/// 접촉점이 서로 충돌한다"는 구조적 문제 자체를 없애지는 못한다(구가 아닌 이상 "완벽하게
/// 매끄러운 회전"은 물리적으로 보장할 수 없다) — 접촉을 부드럽게 만들고 솔버가 덜 튀도록
/// 완화할 뿐이다. 실제로 씬에서 확인해가며 rollRadius/PhysicMaterial/solver 값을 조정할 것.
///
/// [토크+마찰 구르기 실험 경로: useTorqueRolling]
/// 위 방식은 물리엔진이 굴리는 게 아니라 스크립트가 결과 각속도를 흉내 내 강제하는 것이라
/// 다면체의 평면 접촉과 근본적으로 상충한다(강제 스핀 vs 모서리 피벗). 그래서 "원인(토크)을
/// 주고 지면 마찰이 실제 구르기로 변환"하는 현실적 방식을 별도 경로로 둔다. useTorqueRolling이
/// 켜지면 접지 중 velocity/각속도를 대입하지 않고 이동 방향과 직교하는 수평축으로 AddTorque만
/// 걸며, 고마찰 그립 PhysicMaterial이 접촉 모서리를 붙잡아 모서리 피벗 텀블링으로 바꾼다.
/// 정사면체에서 먼저 검증한 뒤 정육면체까지 이 방식으로 통일했다 — PlayerObjectMenuItem이
/// 정육면체/정사면체 브랜치에서 true로 세팅한다. 방향 조종성은 SteerAngularVelocity(어긋난
/// 각속도 성분 감쇠)와 RealignHorizontalVelocity(수평 속도 방향 재정렬)로 보강한다.
/// 토글이 false면 기존 각속도 대입 방식이 그대로 유지되어 구(Sphere) 동작은 변하지 않는다.
///
/// [경사에 따른 가감속]
/// 접지 이동은 바닥 법선 평면에 입력을 투영해 비탈을 따라가지만, 그것만으로는 속력이 항상
/// moveSpeed로 일정해 오르막과 내리막이 똑같이 느껴졌다. 그래서 "중력의 비탈 방향 성분"을
/// 진행 방향에 투영해 그 값을 **시간에 따라 속력에 누적**한다 — 목표 속도를 경사각으로 즉시
/// 스케일하는 방식과 달리, 오르막에서는 점점 힘에 부치고 내리막에서는 점점 탄력이 붙는다.
/// 이 누적은 **진행 방향 성분만** 다루고, 그와 직교하는 흘러내림(등고선 방향으로 걸을 때 비탈
/// 아래로 밀리는 성분)은 대입으로 지우지 않고 물리(중력+마찰)에 그대로 맡긴다 — 무입력 분기가
/// 경사면에서 대입을 건너뛰는 것과 같은 이유다.
/// 평지에서는 접선 성분이 정확히 0이라 이 로직 전체가 무효가 되어 기존 동작(지상 속도 =
/// moveSpeed, 도달 규격표의 전제)이 한 톨도 변하지 않는다. 적용 대상은 Kind가 아니라 무게로
/// 가른다(slopeAccelMaxWeight) — 기본값에서 네모만 빠지고, 무중력 버블로 가벼워지면 네모도
/// 경사에 밀린다. 비탈 착지 프레임에는 파고든 속도의 일부가 비탈 아래 속도로 바뀌는 연출이
/// 얹힌다(landingImpactSlideRatio) — 손맛일 뿐 등반 차단 기능이 아니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMover : MonoBehaviour
{
    public float moveSpeed = 5f;

    [Header("공중 이동 제어")]
    [Tooltip("공중에서의 수평 이동 속도 = 지상 속도 × 이 값. 요구사항 기준 0.6 고정. " +
             "도달 규격표(가로 거리 D = 지상속도 × 0.6 × 체공)가 이 값에 직접 의존하므로 " +
             "바꾸면 레벨 배치 수치가 어긋난다.")]
    public float airControlMultiplier = 0.6f;
    [Tooltip("공중에서 벽으로 미는 입력 성분을 제거해, 지면 구르기용 그립 마찰이 몸을 벽에 붙들어 " +
             "안 떨어지는 문제를 막는 벽 감지 거리(Unit). 몸 중심에서 이동 방향으로 이만큼 안에 벽(수직면)이 " +
             "있으면 그 벽으로 미는 성분을 지워, 벽면을 따라만 움직이고 중력으로 미끄러져 내려오게 한다. 0이면 비활성.")]
    public float airWallCheckDistance = 0.7f;

    [Header("구르는 회전 연출 (접지 중에만 적용)")]
    [Tooltip("굴렀을 때의 반경으로 취급할 값(대략 오브젝트 반지름/절반 크기). " +
             "매 프레임 각속도 = Cross(Vector3.up, 이동 velocity) / rollRadius로 하드 설정한다. " +
             "구/정육면체/정사면체 모두 이 공식 하나를 공유한다(자세한 이유는 클래스 상단 주석 참고).")]
    public float rollRadius = 0.5f;

    [Header("토크+마찰 구르기 (정육면체/정사면체)")]
    [Tooltip("켜면 접지 중 velocity/angularVelocity 하드 대입 대신 AddTorque + 고마찰 그립으로 " +
             "물리적으로 굴린다. false면 기존 각속도 대입 방식을 그대로 유지한다(구 무변화). " +
             "PlayerObjectMenuItem이 정육면체/정사면체 생성 시 true로 세팅한다.")]
    public bool useTorqueRolling = false;
    [Tooltip("[useTorqueRolling 전용] 이동 방향과 직교하는 수평축으로 거는 토크 세기. 지면 마찰이 " +
             "이 토크를 실제 구르기로 변환한다. 너무 작으면 다면체가 앞모서리를 못 넘고 제자리에서 떤다.")]
    public float rollTorque = 25f;
    [Tooltip("[useTorqueRolling 전용] 수평 속도 상한. 토크로 계속 가속되므로 이 값에서 클램프한다.")]
    public float maxSpeed = 6f;
    [Tooltip("[useTorqueRolling 전용] 공중에서의 약한 방향 제어 힘.")]
    public float airControlForce = 12f;
    [Tooltip("[useTorqueRolling 전용] 조향 응답성. 원하는 구르기 축과 어긋난 현재 각속도 성분을 " +
             "이 비율(1/s 느낌)로 감쇠시켜 다른 축으로 붙은 각운동량을 풀어준다. 0이면 순수 토크(방향 " +
             "전환 어려움), 높을수록 방향 전환이 빠르지만 구르는 물리감이 옅어진다.")]
    public float steerResponsiveness = 6f;
    [Tooltip("[useTorqueRolling 전용] 접지 중 수평 velocity를 입력 방향으로 재정렬하는 정도(0~1, " +
             "초당 lerp 비율). 조향감을 주되 속력(크기)은 유지한다. 0이면 재정렬 없음.")]
    [Range(0f, 1f)]
    public float turnAssist = 0.35f;
    [Tooltip("입력 방향을 월드 up 축 기준으로 회전시키는 각도(도). 위에서 내려다본 기준 시계방향이 " +
             "양수. 토크 모드/레거시(구) 모두에 공통 적용되어 세 도형의 방향감을 일치시킨다. 입력 방향이 " +
             "의도와 어긋날 때 여기서 보정한다 — 앞키가 실제로 '앞'으로 가도록 90 또는 -90 등으로 맞춘다. " +
             "[mnppi] cameraRelativeInput이 켜져 있으면 이 값 대신 카메라 현재 yaw가 쓰인다.")]
    public float inputYawOffset = 90f;

    // ─── [mnppi 추가] 카메라 상대 이동 (feat/mnppi-orbit-cam #75, 박진수 승인 2026-09-03) ───
    [Tooltip("[mnppi] 팔로우 카메라의 마우스 궤도 회전(enableMouseOrbit)이 켜져 있으면 이 값과 무관하게 " +
             "이동이 카메라 시점 기준으로 자동 전환된다(둘은 한 세트). 이 토글은 '궤도 카메라 없이도' " +
             "카메라 상대 이동을 강제하고 싶을 때만 쓰는 명시적 opt-in. 궤도 카메라도 끄고 이 토글도 " +
             "끄면 기존 동작(inputYawOffset 고정).")]
    public bool cameraRelativeInput = true;
    // ─── [mnppi 추가 끝] ───
    [Tooltip("[useTorqueRolling 전용] ScalingSystem으로 커지거나 납작해지면 관성/무게중심이 커져 " +
             "고정 토크로는 모서리를 넘기 어렵다. 현재 transform 스케일 최댓값의 (이 값)제곱만큼 " +
             "rollTorque를 자동으로 키운다. 0=보정 없음, 1=크기에 비례(선형), 2=크기제곱(관성까지 보정).")]
    public float scaleTorqueCompensation = 1.5f;

    [Header("자체 접지 판정 (PlayerShapeController가 없을 때만 사용)")]
    [Tooltip("바닥 감지 Raycast 거리")]
    public float groundCheckDistance = 0.15f;
    [Tooltip("바닥으로 인정할 레이어 마스크")]
    public LayerMask groundLayer = ~0;

    [Tooltip("입력이 없을 때 수평 속도를 0으로 잡아 '딱 멈추게' 할 바닥의 평평함 기준. 바닥 법선의 " +
             "y가 이 값보다 크면 평지로 보고 멈춘다. 이보다 기울면 경사면으로 보고 대입을 건너뛰어 " +
             "중력에 미끄러지게 둔다. 1에 가까울수록 '경사면'으로 인정하는 각도가 작아진다 " +
             "(0.999≈2.6°, 0.99≈8°, 0.98≈11°). 씬 바닥이 미세하게 안 맞아 평지에서도 미끄러지면 값을 낮춘다.")]
    [Range(0.9f, 1f)]
    public float idleStopMaxNormalY = 0.999f;

    [Header("경사에 따른 가감속 (접지 + 이동 입력 중에만)")]
    [Tooltip("중력의 비탈 방향 성분을 속력에 누적할 때 곱하는 튜닝 계수. 1 = 실제 중력 그대로 " +
             "(g·sinθ). moveSpeed 7, 경사 20° 기준 오르막 7.0 → 0.5s 5.3 → 1.0s 3.6, 내리막 " +
             "7.0 → 0.5s 8.7 → 1.0s 10.4가 나온다. 체감이 밋밋하면 1.2~1.5로 올리고, 경사가 " +
             "너무 사납게 느껴지면 0.7 등으로 내린다. 0이면 가감속 없음(예전 동작).")]
    public float slopeAccelMultiplier = 1f;
    [Tooltip("내리막 속도 상한 = moveSpeed × 이 값. 긴 비탈에서 무한정 빨라져 조작 불능이 되는 " +
             "것을 막는다. 2면 구(7.0)가 최대 14까지 나온다 — 도달 규격표는 '지상 속도'가 " +
             "moveSpeed임을 전제하므로, 이 배수를 키울수록 비탈 끝 점프의 가로 도달이 규격표를 " +
             "넘어선다는 점을 감안할 것. 진행 방향 속력과 옆으로 흘러내리는 속력에 **각각** 걸리는 " +
             "상한이라, 등고선 방향으로 걸으며 최대로 흘러내리는 최악의 경우 총 속력은 " +
             "√(moveSpeed² + (moveSpeed×이 값)²)까지 나올 수 있다.")]
    public float downhillSpeedCap = 2f;
    [Tooltip("오르막 한계각(도). 진행 방향의 오르막 각도가 0 → 이 값으로 갈수록 유지 가능한 속도가 " +
             "moveSpeed → 0으로 선형으로 줄고, 이 값을 넘으면 유지 속도가 음수가 되어 입력을 " +
             "유지해도 밀려 내려온다. PlayerGroundContact가 '바닥'으로 인정하는 한계(법선 y > 0.5, " +
             "약 60°)보다 훨씬 낮게 잡아, 30~60° 구간을 '설 수는 있지만 못 오르는' 벽으로 쓸 수 있게 한다.")]
    public float uphillLimitAngle = 30f;
    [Tooltip("이 무게 이상이면 경사 가감속을 아예 받지 않는다(무겁고 접지력이 좋아 비탈에 안 밀리는 " +
             "쪽). 실제 에셋 질량이 구 1.5 / 세모 1.0 / 네모 3.0이라 기본 2.5면 네모만 제외된다. " +
             "Kind 하드코딩이 아니라 무게(질량 × 실효 중력 배율)로 재므로, 무중력 버블 등으로 " +
             "실효 무게가 내려가면(네모 3.0 → 1.8) 네모도 경사에 밀리기 시작한다 — 의도된 상호작용이다.")]
    public float slopeAccelMaxWeight = 2.5f;

    [Header("착지 충격 → 비탈 아래로 밀림 (연출)")]
    [Tooltip("착지 순간 바닥으로 파고든 속도(법선 방향 성분) 중 이 비율만큼을 비탈 아래 방향 속도로 " +
             "바꾼다. 가파른 비탈에 크게 떨어질수록 세게 밀리는 '쿵' 하는 손맛용이며, 등반을 막는 " +
             "기능이 아니다(작고 빠른 홉은 충격 자체가 작아 거의 안 밀린다 — 등반 제한은 " +
             "uphillLimitAngle이 전담). 0이면 완전히 꺼진다. 0.25는 10 Unit 낙하 기준 약 2 U/s가 " +
             "더해지는 보수적인 값이고, 0.4를 넘기면 급경사 착지에서 조작이 어려워진다.")]
    public float landingImpactSlideRatio = 0.25f;
    [Tooltip("이 속도(m/s) 이하의 충격은 무시하고, 넘는 만큼에만 위 비율을 적용한다(문턱에서 툭 " +
             "튀지 않게 초과분만 쓴다). 기본 3은 약 0.46 Unit 자유낙하에 해당해, 비탈을 오르내리며 " +
             "찍는 잔 홉(착지 충격 3 미만)에서는 아무 일도 일어나지 않는다.")]
    public float landingImpactMinSpeed = 3f;

    [Header("조작권 상실 시 감쇠 (Tab 전환용)")]
    [Tooltip("조작권을 잃은 플레이어가 관성/각속도로 계속 굴러가지 않도록 초당 감쇠하는 비율. " +
             "값이 클수록 빨리 멈춘다(프레임레이트 무관). 수직(중력) 속도는 건드리지 않는다.")]
    public float uncontrolledDamping = 15f;

    /// <summary>PlayerControlSwitcher가 SetControlled()로 세팅. 스위처가 씬에 없으면 기본값 true로
    /// 항상 조작 가능. 다른 컴포넌트(PlayerJump/PlayerAccelReceiver)는 이 값을 읽어 입력 처리 여부를
    /// 결정한다.</summary>
    public bool IsControlled { get; private set; } = true;

    /// <summary>PlayerControlSwitcher가 조작권을 넘길 때 호출한다. 필드를 직접 쓰지 않고 이 메서드를
    /// 거치게 해, 전환 시점 정리 로직을 한 곳에서 관리한다.</summary>
    public void SetControlled(bool controlled)
    {
        IsControlled = controlled;
    }

    /// <summary>외부 시스템이 이 바디의 물리를 직접 소유하는 동안 true로 둔다(예: 실타래 스윙 —
    /// DreamThreadController가 조인트와 접선 힘으로 몸을 굴리는 구간). true인 동안 이 컴포넌트는
    /// 이동 대입도, 비조작 감쇠도 하지 않는다.
    ///
    /// [왜 컴포넌트를 끄는 대신 이 플래그인가]
    /// 스윙을 살리려면 PlayerMover의 velocity 간섭을 멈춰야 한다. 예전에는 `enabled = false`로 껐는데,
    /// 그러면 OnDisable이 PlayerControlSwitcher 로스터에서 이 플레이어를 **빼버려** 부작용이 줄줄이
    /// 생겼다: 조작권이 다른 플레이어로 튀고, Tab 순환에 다시 안 들어와 **매달린 플레이어로 돌아갈
    /// 수 없고**, 그래서 실타래는 Tab을 감지해 매달림을 강제로 해제해야 했다.
    /// 플래그로 간섭만 멈추면 컴포넌트가 켜진 채라 **로스터가 그대로 유지**돼, 매달린 채 Tab으로
    /// 오가는 것이 자연스럽게 성립한다.</summary>
    public bool ExternallyDriven { get; set; }

    private Rigidbody rb;
    private PlayerShapeController shapeController;
    private PlayerGroundContact groundContact;

    // 경사에서 누적된 속력 가감분(m/s). 평지에 서거나(slopeAffected == false) 소유권이 다른
    // 시스템/다른 플레이어에게 넘어갈 때만 0으로 되돌린다. **점프나 입력 뗌으로는 지우지 않는다** —
    // 지우면 그때마다 오르막 페널티가 초기화돼, 한계각을 넘는 비탈도 점프나 입력 연타로 전속력
    // 등반이 가능해진다(2026-08-10 플레이테스트에서 실제로 뚫렸다).
    private float slopeSpeedBonus;

    // 착지 충격 연출용. 공중 프레임마다 그 시점의 속도를 남겨 두고, 접지로 바뀌는 프레임에 한 번 쓴다
    // (착지 프레임의 rb.velocity는 이미 충돌 응답으로 깎여 있어 충격 크기를 못 잰다).
    private bool wasAirborne;
    private Vector3 airVelocity;

    // 직전 스텝에 이 컴포넌트가 대입한 "이동 성분"(수평, 흘러내림 제외). 다음 스텝에 실제 속도에서
    // 이 값을 빼야 물리(중력·마찰)가 만든 몫만 남는다 — 빼지 않으면 우리가 넣은 속도가 흘러내림으로
    // 재해석돼 되먹임 발산한다. 대입하지 않은 스텝(공중·무입력·평지·소유권 이전)에는 0으로 둔다.
    private Vector3 lastTravelVelocity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        shapeController = GetComponent<PlayerShapeController>();
        // Player_Collider(자식)에 붙는다 — 경사면 롤링(레거시 경로)이 이미 필터링된 법선을 읽는 데 쓴다.
        groundContact = GetComponentInChildren<PlayerGroundContact>();

        if (shapeController == null)
            groundLayer &= ~(1 << gameObject.layer);
    }

    void OnEnable()
    {
        PlayerControlSwitcher.RegisterPlayer(this);
    }

    void OnDisable()
    {
        PlayerControlSwitcher.UnregisterPlayer(this);
    }

    void FixedUpdate()
    {
        // 외부 시스템이 몸을 굴리는 동안은 이동도 감쇠도 하지 않는다(둘 다 그 물리를 짓밟는다).
        if (ExternallyDriven)
        {
            slopeSpeedBonus = 0f;
            airVelocity = Vector3.zero;
            lastTravelVelocity = Vector3.zero;
            return;
        }

        if (useTorqueRolling)
        {
            TorqueRollingFixedUpdate();
            return;
        }

        LegacyVelocityFixedUpdate();
    }

    // 기존(기본) 경로: 수평 velocity와 각속도를 직접 대입한다. 구/정육면체가 지금까지
    // 쓰던 방식 그대로이며, useTorqueRolling이 false인 한 동작이 전혀 변하지 않는다.
    private void LegacyVelocityFixedUpdate()
    {
        if (!IsControlled)
        {
            DampWhenUncontrolled();
            return;
        }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = new Vector3(h, 0f, v) * moveSpeed;
        // 입력 방향 보정을 토크 모드와 공유해 세 도형(구/정육면체/정사면체)의 방향감을 일치시킨다.
        // velocity/각속도 대입 로직 자체는 그대로 두고, 입력 방향 벡터에 회전만 추가한다.
        // [mnppi 수정] 기존: inputYawOffset 고정. cameraRelativeInput이면 카메라 현재 yaw로 회전(EffectiveInputYaw).
        float yaw = EffectiveInputYaw();
        if (Mathf.Abs(yaw) > 0.0001f)
            move = Quaternion.AngleAxis(yaw, Vector3.up) * move;

        if (!IsGrounded())
        {
            // 공중 수평 속도 = 지상 속도 × airControlMultiplier(0.6). 도달 규격표의 가로 거리가
            // 이 계수에 맞춰 계산돼 있다. velocity를 "대입"하므로 입력을 유지하면 즉시 0.6배
            // 속도가 되어(가속이 아니라) 체공 내내 그 속도를 유지 → D가 정확히 성립한다.
            // 벽으로 미는 성분은 제거해(벽면 따라만) 그립 마찰이 몸을 벽에 붙들어 안 떨어지는 걸 막는다.
            // [2026-08-10 수정 — 점프로 비탈을 거슬러 오르던 구멍]
            // 예전엔 여기서 slopeSpeedBonus를 0으로 지웠다. 그러면 점프할 때마다 오르막 페널티가
            // 통째로 초기화되고, 착지 후 하한까지 내려가는 데 1초 남짓 걸리니 그 전에 또 뛰면
            // 페널티가 영영 안 쌓여 한계각을 넘는 비탈도 전속력으로 올라갔다. 공중에서는 누적을
            // 얼려 두기만 하고, 착지하면 그 비탈의 floor 클램프가 즉시 다시 잡는다(평지로 돌아가면
            // 접지 분기의 slopeAffected 게이트가 0으로 지운다). 공중 속도 자체는 규격표 고정값 그대로다.
            wasAirborne = true;
            airVelocity = rb.velocity;
            lastTravelVelocity = Vector3.zero; // 공중에서는 이동 성분을 비탈에 대입하지 않았다.
            Vector3 airMove = DeflectAirMoveFromWall(move);
            rb.velocity = new Vector3(airMove.x * airControlMultiplier, rb.velocity.y, airMove.z * airControlMultiplier);
            // 회전은 건드리지 않는다(구는 자유 물리 회전 보존, 다면체는 PlayerVisualRoll이 시각만 처리).
            return;
        }

        // 접지 중 velocity를 매 스텝 하드 대입하는 이 경로는(클래스 상단 주석) 항상 월드 XZ 평면
        // 기준으로 move를 대입했다 — 경사면에서는 그 수평 velocity가 비탈을 파고들거나 뜨는 방향이라,
        // 매 스텝 물리 접촉이 그걸 강제로 되돌리는 충돌을 반복해 "미끄러지듯 씹히는" 느낌이 났다
        // (PlayerObjectMenuItem이 useTorqueRolling을 더 이상 켜지 않으므로 세 도형 모두 이 경로를
        // 탄다 — 각속도 대입만 정육면체/정사면체의 FreezeRotation에 막혀 무효화된다). 바닥 법선 평면에
        // move를 투영해(속력은 보존) X/Z 방향만 비탈을 따라가게 하고, 각속도도 같은 법선 기준으로
        // 계산해 시각 회전축이 실제 이동 방향과 일치하게 한다(평지에선 법선이 Vector3.up이라 기존과
        // 동일하게 동작한다).
        //
        // [2026-08-05 수정 — Y까지 투영값으로 통째로 대입했던 첫 시도의 회귀 버그]
        // 처음엔 `rb.velocity = slopeMove`로 Y까지 통째로 대입했다 — 평지(법선=Up)에서는 slopeMove.y가
        // 항상 0이 되므로, 이동 중(이 분기)에 점프가 걸려도 그 다음 물리 스텝에서(착지 유예 시간 중엔
        // 여전히 이 분기가 실행된다) Y velocity가 즉시 0으로 덮어써져 "이동 중엔 점프가 안 되는" 것처럼
        // 보였다. Y는 항상 `rb.velocity.y`를 그대로 보존하고(중력/점프의 소유물), 투영은 X/Z 방향에만
        // 적용한다 — 원래 코드가 항상 지키던 계약을 그대로 유지한다.
        Vector3 groundNormal = groundContact != null ? groundContact.GroundNormal : Vector3.up;
        // 평지(접선 성분이 0)와 무거운 도형(기본값에서 네모)은 경사 로직 전체에서 빠져 예전 동작
        // 그대로 간다. 가감속·착지 충격·흘러내림 보존이 모두 같은 조건이어야 하므로 한 번만 판정한다.
        bool slopeAffected = groundNormal.y <= idleStopMaxNormalY
                             && PlayerWeight.Of(rb) < slopeAccelMaxWeight;
        if (!slopeAffected) slopeSpeedBonus = 0f;

        if (wasAirborne)
        {
            wasAirborne = false;
            if (slopeAffected) ApplyLandingImpact(groundNormal);
        }

        if (move.sqrMagnitude > 0.0001f)
        {
            // 속력은 더 이상 move.magnitude로 고정이 아니다 — 경사 가감속(SlopeSpeed)이 오르막에서
            // 깎고 내리막에서 더한다. 방향(slopeDir)과 속력을 분리해 곱하므로 각속도도 같은
            // slopeMove로 계산되어 실제 이동 속도와 계속 일치한다(구르는 연출이 안 어긋난다).
            Vector3 slopeDir = Vector3.ProjectOnPlane(move, groundNormal).normalized;
            Vector3 slopeMove = slopeDir * (slopeAffected
                ? SlopeSpeed(move.magnitude, slopeDir, groundNormal)
                : move.magnitude);

            Vector3 target = new Vector3(slopeMove.x, 0f, slopeMove.z);
            if (slopeAffected)
            {
                // [2026-08-10 1차 — 등고선 방향으로 걸으면 흘러내리지 않던 버그]
                // SlopeSpeed는 중력의 비탈 방향 성분을 **진행 방향에 투영한 스칼라**만 다룬다. 등고선
                // (오르내림 0) 방향으로 가면 그 내적이 정확히 0이라 중력의 접선 성분이 통째로 사라지고,
                // XZ 대입이 흘러내림을 매 스텝 지워 비탈 위를 옆으로 활보하게 된다. 그래서 진행 방향
                // 성분만 우리가 정하고 직교 성분은 물리에 맡긴다(무입력 분기가 대입을 건너뛰는 것과
                // 같은 이유). 스칼라 누적기를 하나 더 두지 않은 이유는 slopeDir이 입력에 따라 매
                // 프레임 회전해, 그 축에 저장한 값이 방향을 틀 때 함께 회전해 버리기 때문이다.
                //
                // [2026-08-10 2차 — 방향 전환 펌핑(위 수정이 만든 회귀)]
                // 처음엔 `target += horizontal - Project(horizontal, target)`로 직교 성분을 **통째**
                // 보존했다. 그러면 직전까지의 진행 속도가 방향을 트는 순간 새 진행 방향 기준으로는
                // 직교 성분이 되어 살아남고, 그 위에 새 방향 전속력이 얹힌다(√(s²+v²)). 90° 전환만
                // 반복하면 √2·s에서 멎지만, **비스듬한 방향으로 계속 가면** 우리가 넣은 속도가 다음
                // 스텝에 다시 직교 성분으로 재해석되는 되먹임이 걸려 s/cosα로 발산했다(스텝 시뮬레이션
                // 기준 20° 비탈·45° 진행에서 6초에 18 m/s 이상). 진행 방향 성분에만 걸리는
                // downhillSpeedCap은 이 경로를 전혀 막지 못했다.
                //
                // 보존해야 할 것은 **중력이 만든 흘러내림**이지 플레이어 자신의 관성이 아니므로 둘을
                // 가른다: (1) 흘러내림은 항상 비탈 아래를 향하니 그 축의 성분만 보고(임의 방향 관성은
                // 축에서 벗어나 걸러지고, 오르막 쪽 음수는 버린다), (2) 그 축에서도 **직전 스텝에
                // 우리가 대입한 이동 성분을 빼서** 물리(중력·마찰)가 만든 몫만 남긴다. (2)가 없으면
                // 우리가 넣은 속도가 다음 스텝에 흘러내림으로 재해석돼 s/cosα로 발산한다(비스듬히
                // 걸을 때). 마지막으로 내리막 상한으로 잘라 어떤 경우에도 절대 상한을 준다.
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                downhill = new Vector3(downhill.x, 0f, downhill.z).normalized;
                Vector3 horizontal = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
                float drift = Mathf.Clamp(Vector3.Dot(horizontal - lastTravelVelocity, downhill),
                                          0f, moveSpeed * downhillSpeedCap);
                lastTravelVelocity = target;

                // 진행 방향 성분은 SlopeSpeed가 이미 소유하므로 직교분만 더한다 — 정면 오르막/내리막
                // 에서는 흘러내림이 target과 나란해 더해지는 값이 정확히 0(수치 무변화)이다.
                Vector3 driftVec = downhill * drift;
                target += driftVec - Vector3.Project(driftVec, target);
            }
            else lastTravelVelocity = Vector3.zero;
            rb.velocity = new Vector3(target.x, rb.velocity.y, target.z);
            if (rollRadius > 0.0001f)
                rb.angularVelocity = Vector3.Cross(groundNormal, slopeMove) / rollRadius;
        }
        else
        {
            lastTravelVelocity = Vector3.zero; // 이 스텝엔 이동 성분을 대입하지 않는다(전부 물리의 몫).
            // 여기서도 slopeSpeedBonus를 지우지 않는다(위 공중 분기와 같은 이유) — 지우면 비탈에서
            // 입력을 한 프레임 뗐다 다시 누르는 것만으로 오르막 페널티가 초기화된다. 얼려 두면
            // 다시 누른 순간 그 비탈의 floor 클램프가 잡아 곧바로 그 경사의 유지 속도로 들어간다.
            // 입력이 없을 때 수평 속도를 0으로 대입하는 건 평지에서 "미끄러지지 않고 딱 멈추는"
            // 감각을 위한 것이다. 그런데 경사면에서는 중력이 만드는 비탈 방향 성분이 거의 전부
            // 수평 성분이라, 이 대입이 매 스텝 그걸 지워 도형이 비탈에 붙박이가 된다(경사각을
            // 올려도 상수 0을 대입하므로 증상이 똑같다 — 2026-08-10 플레이테스트로 확인).
            // 경사면에서는 대입을 건너뛰고 물리에 맡긴다. 마찰(PhysicMaterial)이 도형별로
            // 얼마나 미끄러질지를 결정하므로 여기서 따로 도형 분기를 두지 않는다.
            if (groundNormal.y > idleStopMaxNormalY)
                rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        }
    }

    // 착지 프레임 1회. 바닥으로 파고든 속도(법선 방향 성분)의 일부를 비탈 아래 방향 속도로 바꿔
    // "쿵" 하고 밀려나게 하는 연출이다. 등반 차단 기능이 아니다 — 작고 빠른 홉은 충격 자체가 작아
    // 거의 안 밀린다(등반 제한은 uphillLimitAngle이 전담한다). 호출부가 slopeAffected 안에서만
    // 부르므로 평지(비탈 아래 방향이 정의되지 않는다)와 무거운 도형에서는 아예 실행되지 않는다.
    // Y는 건드리지 않는다 — 충격을 수직으로 되돌리면 착지 튕김이 되어 점프/낙하 규격과 충돌한다.
    private void ApplyLandingImpact(Vector3 groundNormal)
    {
        float impact = -Vector3.Dot(airVelocity, groundNormal);
        if (landingImpactSlideRatio <= 0f || impact <= landingImpactMinSpeed) return;

        // 아주 높은 곳에서 떨어지면 충격이 얼마든 커질 수 있으므로 여기서도 내리막 상한으로 자른다
        // (이 컴포넌트가 비탈 아래로 넣는 속도는 전부 같은 상한을 공유한다).
        Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
        float amount = Mathf.Min((impact - landingImpactMinSpeed) * landingImpactSlideRatio,
                                 moveSpeed * downhillSpeedCap);
        Vector3 kick = downhill * amount;
        rb.velocity = new Vector3(rb.velocity.x + kick.x, rb.velocity.y, rb.velocity.z + kick.z);
    }

    // 경사에 따른 가감속. 이번 스텝에 낼 "비탈 방향 속력"을 돌려준다.
    //
    // 모델: 중력의 비탈 방향 성분(크기 g·sinθ)을 진행 방향에 투영한 값이 이 순간의 가감속도이고,
    // 그것을 속력에 시간에 따라 누적한다. 오르막이면 음수라 점점 힘에 부치고, 내리막이면 양수라
    // 점점 탄력이 붙으며, 등고선을 따라 옆으로 가면 정확히 0이다(경사면인데도 무변화가 자연히 성립).
    // 목표 속도를 경사각으로 즉시 스케일하는 방식을 쓰지 않은 이유가 이 "시간에 따른 축적"이다.
    //
    // 하한(floor)이 오르막 한계각을 만든다: 진행 방향 오르막 각도가 uphillLimitAngle에 가까워질수록
    // 유지 가능한 속력이 0으로 줄고, 넘으면 음수가 되어 입력을 유지해도 밀려 내려온다. 누적값 자체를
    // [floor, cap] 범위로 클램프해(출력만 클램프하면) 오래 오른 뒤 방향을 바꿨을 때 쌓인 값이
    // 한꺼번에 터지는 windup을 막는다.
    //
    // 적용 대상 판정(평지 제외 · 무게 임계)은 호출부가 slopeAffected로 이미 걸렀다 — 그 판정이
    // 직교 성분 보존 여부와 같은 조건이라, 두 군데로 갈라지지 않게 호출부 한 곳에 모아 뒀다.
    // 평지에서 누적값을 0으로 지우는 것도 그래서 호출부의 몫이다("지상 속도 = moveSpeed" 보장).
    private float SlopeSpeed(float baseSpeed, Vector3 slopeDir, Vector3 groundNormal)
    {
        // 전역 중력이 아니라 이 바디의 실효 중력을 쓴다 — 무중력 버블 안에서는 비탈이 미는 힘도
        // 같은 배율로 약해져야 앞뒤가 맞는다(PlayerJump가 √(2gH)에 실효 중력을 쓰는 것과 같은 이유).
        // 버블이 런타임에 컴포넌트를 붙이므로 Awake 캐시가 아니라 그때그때 조회한다.
        PlayerGravityOverride gravity = GetComponent<PlayerGravityOverride>();
        float g = gravity != null ? gravity.EffectiveGravityMagnitude : Mathf.Abs(Physics.gravity.y);
        float tangentAccel = Vector3.Dot(Vector3.ProjectOnPlane(Vector3.down * g, groundNormal), slopeDir)
                             * slopeAccelMultiplier;

        float climbAngle = Mathf.Asin(Mathf.Clamp(slopeDir.y, -1f, 1f)) * Mathf.Rad2Deg; // 오르막 +, 내리막 -
        float cap = moveSpeed * downhillSpeedCap;
        float floor = (climbAngle >= 0f && uphillLimitAngle > 0.01f)
            ? Mathf.Max(baseSpeed * (1f - climbAngle / uphillLimitAngle), -cap)
            : -cap;

        slopeSpeedBonus = Mathf.Clamp(slopeSpeedBonus + tangentAccel * Time.fixedDeltaTime,
                                      floor - baseSpeed, Mathf.Max(cap - baseSpeed, 0f));
        return baseSpeed + slopeSpeedBonus;
    }

    // 실험 경로: 접지 중 velocity/각속도를 대입하지 않고 토크만 걸어, 지면 마찰이 그 토크를
    // 실제 구르기(모서리 피벗)로 변환하게 둔다. 선형 전진은 회전의 결과로 자연 발생한다.
    private void TorqueRollingFixedUpdate()
    {
        if (!IsControlled)
        {
            DampWhenUncontrolled();
            return;
        }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 moveDir = new Vector3(h, 0f, v);
        // 입력 방향을 월드 up 축 기준으로 회전 보정(카메라/월드 축과의 어긋남을 씬에서 맞춤).
        // [mnppi 수정] 기존: inputYawOffset 고정. cameraRelativeInput이면 카메라 현재 yaw로 회전(EffectiveInputYaw).
        float yaw = EffectiveInputYaw();
        if (Mathf.Abs(yaw) > 0.0001f)
            moveDir = Quaternion.AngleAxis(yaw, Vector3.up) * moveDir;
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        if (IsGrounded())
        {
            bool hasInput = moveDir.sqrMagnitude > 0.0001f;

            // 이동 방향과 직교하는 수평축으로 토크 → 마찰이 붙잡아 실제 구르기로 변환.
            // 스케일이 커지거나 납작해지면 관성/무게중심이 커져 고정 토크로는 모서리를 못 넘으므로,
            // 현재 스케일 최댓값에 비례(지수 조절 가능)해 토크를 자동으로 키운다.
            Vector3 rollAxis = Vector3.Cross(Vector3.up, moveDir);
            rb.AddTorque(rollAxis * EffectiveRollTorque(), ForceMode.Acceleration);

            if (hasInput)
            {
                SteerAngularVelocity(rollAxis.normalized);
                RealignHorizontalVelocity(moveDir);
            }
        }
        else
        {
            // 공중: 약한 방향 제어만. 회전은 건드리지 않아 통통 튀는 불규칙함을 보존.
            // 벽으로 미는 성분은 제거해(벽면 따라만) 그립 마찰이 몸을 벽에 붙들어 안 떨어지는 걸 막는다.
            Vector3 airDir = DeflectAirMoveFromWall(moveDir);
            rb.AddForce(airDir * airControlForce, ForceMode.Acceleration);
        }

        // 수평 속도 상한 (토크로 무한 가속되는 것 방지).
        Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        if (horiz.magnitude > maxSpeed)
        {
            Vector3 clamped = horiz.normalized * maxSpeed;
            rb.velocity = new Vector3(clamped.x, rb.velocity.y, clamped.z);
        }
    }

    // Tab 전환으로 조작권을 잃은 프레임마다 호출한다. 능동 입력을 끊는 것에 더해, 남은 수평
    // 이동 관성과 각속도(토크 모드로 굴러가던 회전 포함)를 프레임레이트에 무관하게 빠르게
    // 감쇠시켜 다른 오브젝트를 조작하는 동안 이전 플레이어가 계속 굴러가거나 미끄러지지 않게 한다.
    // 수직 속도(중력/낙하)는 유지해 공중에 뜨거나 낙하가 멈추는 부작용을 막는다.
    private void DampWhenUncontrolled()
    {
        // 조작권을 되찾았을 때 잃기 전 경사 누적/착지 충격이 뒤늦게 되살아나지 않게 함께 지운다.
        slopeSpeedBonus = 0f;
        airVelocity = Vector3.zero;
        lastTravelVelocity = Vector3.zero;
        float k = Mathf.Clamp01(uncontrolledDamping * Time.fixedDeltaTime);
        Vector3 vel = rb.velocity;
        rb.velocity = new Vector3(vel.x * (1f - k), vel.y, vel.z * (1f - k));
        rb.angularVelocity *= (1f - k);
    }

    // 스케일 보정: ScalingSystem(PlayerShapeController)이 이 오브젝트의 transform.localScale을
    // 키우면(X/Y 독립) 콜라이더가 커져 관성 텐서와 무게중심-피벗 거리가 함께 커지므로, 같은
    // 토크로는 모서리를 못 넘는다. mass는 그대로이지만 관성/중력 복원 토크가 크기에 따라 커지므로
    // 스케일 최댓값에 비례해(지수는 scaleTorqueCompensation으로 조절) 토크를 키워 보정한다.
    private float EffectiveRollTorque()
    {
        Vector3 s = transform.localScale;
        float scaleMeasure = Mathf.Max(Mathf.Max(s.x, s.y), s.z);
        if (scaleTorqueCompensation <= 0f || scaleMeasure <= 1f)
            return rollTorque;

        return rollTorque * Mathf.Pow(scaleMeasure, scaleTorqueCompensation);
    }

    // 조향 개선 (1): 현재 각속도를 "원하는 구르기 축 성분"과 "그 외 성분"으로 분해해, 어긋난
    // 성분만 감쇠시킨다. 다른 축으로 붙어버린 각운동량(방향 전환을 막던 원인)을 서서히 풀어주되
    // 원하는 방향으로의 구르기 회전은 그대로 유지해 물리감을 살린다.
    private void SteerAngularVelocity(Vector3 desiredAxis)
    {
        if (steerResponsiveness <= 0f) return;

        Vector3 w = rb.angularVelocity;
        Vector3 aligned = Vector3.Project(w, desiredAxis); // 원하는 축 성분(구르기)
        Vector3 stray = w - aligned;                        // 어긋난 성분(조향 방해)

        float k = Mathf.Clamp01(steerResponsiveness * Time.fixedDeltaTime);
        rb.angularVelocity = aligned + stray * (1f - k);
    }

    // 조향 개선 (2): 접지 중 수평 velocity의 "속력(크기)"은 유지한 채 "방향"만 입력 방향으로
    // 부드럽게 재정렬한다. 각운동량 관성과 별개로 조향감을 직접 준다.
    private void RealignHorizontalVelocity(Vector3 moveDir)
    {
        if (turnAssist <= 0f) return;

        Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        float speed = horiz.magnitude;
        if (speed < 0.05f) return;

        float k = Mathf.Clamp01(turnAssist * Time.fixedDeltaTime * 10f);
        Vector3 newDir = Vector3.Slerp(horiz.normalized, moveDir.normalized, k);
        Vector3 newHoriz = newDir * speed;
        rb.velocity = new Vector3(newHoriz.x, rb.velocity.y, newHoriz.z);
    }

    // 공중에서 이동 입력이 벽을 향하면 그 '벽 쪽 성분'을 제거해 벽면을 따라만 미끄러지게 한다.
    // 지면 구르기용 고마찰 그립 PhysicMaterial이 벽 접촉 법선력과 만나면 정지 마찰이 중력을 이겨
    // 몸을 벽에 매달아 둔다(벽에 밀며 점프하면 안 내려오는 문제). 벽으로 미는 힘을 주지 않으면
    // 법선력이 사라져 마찰이 못 잡고 중력이 이긴다. 접지 중엔 적용하지 않는다(정상 이동/구르기 유지).
    // 레이어가 아니라 계층(PlayerMover 보유)으로 자신·타 플레이어를 걸러, 벽이 플레이어와 같은
    // Default 레이어여도 벽만 감지한다.
    private Vector3 DeflectAirMoveFromWall(Vector3 horizontalMove)
    {
        if (airWallCheckDistance <= 0f || horizontalMove.sqrMagnitude < 1e-4f) return horizontalMove;

        Vector3 dir = horizontalMove.normalized;
        RaycastHit[] hits = Physics.RaycastAll(rb.position, dir, airWallCheckDistance, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.PositiveInfinity;
        Vector3 wallNormal = Vector3.zero;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<PlayerMover>() != null) continue; // 자신·타 플레이어 제외
            if (Mathf.Abs(h.normal.y) >= 0.5f) continue;                          // 바닥/천장 제외 — '벽(수직면)'만
            if (h.distance < bestDist) { bestDist = h.distance; wallNormal = h.normal; }
        }
        if (wallNormal == Vector3.zero) return horizontalMove;

        float into = Vector3.Dot(horizontalMove, wallNormal); // 벽으로 미는 성분이면 음수(법선 반대 방향)
        if (into < 0f) horizontalMove -= into * wallNormal;   // 그 성분만 제거 → 벽면에 평행하게
        return horizontalMove;
    }

    private bool IsGrounded()
    {
        if (shapeController != null)
            return shapeController.IsGrounded();

        Vector3 rayOrigin = transform.position + Vector3.up * 0.05f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer, QueryTriggerInteraction.Ignore);
    }

    // ─── [mnppi 추가] 입력 방향을 회전시킬 각도(도) ───
    // 팔로우 카메라의 마우스 궤도 회전이 켜져 있으면(MouseOrbitActive) 이동은 무조건 그 카메라의
    // 현재 시점 yaw(ViewYaw) 기준이다 → WASD가 "지금 보는 방향" 기준(원신/ZZZ식). 궤도 카메라 +
    // 월드축 고정 이동은 방향이 어긋나 못 쓰는 조합이라 자동으로 짝을 맞춘다. cameraRelativeInput은
    // 궤도 카메라 없이도 카메라 상대 이동을 강제하고 싶을 때의 명시적 opt-in. 둘 다 아니면(그리고
    // 씬에 팔로우 카메라가 없으면) 기존 inputYawOffset. ViewYaw는 orbitYaw를 cameraYawOffset(씬
    // 기본 90)에서 시작하므로 inputYawOffset도 90인 기본값에선 전환 시 방향 불연속이 없다.
    private float EffectiveInputYaw()
    {
        float? viewYaw = PlayerFollowCamera.ViewYaw;
        if (viewYaw.HasValue && (PlayerFollowCamera.MouseOrbitActive || cameraRelativeInput))
            return viewYaw.Value;
        return inputYawOffset;
    }
    // ─── [mnppi 추가 끝] ───
}
