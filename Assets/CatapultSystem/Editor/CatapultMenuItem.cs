// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Catapult 메뉴로 투석기 한 대(받침대+팔+버킷+당김 앵커+조향 손잡이)를 한 번에 생성하고
/// 서로 참조를 연결한다. 기존 표준 패턴(`AccelSystem/Editor/AccelPadMenuItem.cs`,
/// `ScalingSystem/Editor/Shapegimmicksetup.cs`)을 따른다: SceneView 중앙 스폰, Undo 등록,
/// Selection 지정, 렌더 파이프라인 대응 셰이더.
///
/// 개편 이력(라운드별로 무엇을 왜 바꿨는지)은 코드가 아니라 `CatapultSystem/CLAUDE.md`에 기록돼
/// 있다 — 이 요약은 재검산에 실제로 쓰이는 공식/근거와 "지금 코드가 왜 이런가"만 담는다.
///
/// 생성되는 계층:
/// Catapult (CatapultLoadController + CatapultSteerHandle + Rigidbody(중력 적용, 논카인매틱,
///           FreezeRotationX/Z, mass 150) — 조향 회전 대상이자 발사 방향 기준 루트, LineRenderer
///           실 포함)
/// ├─ Catapult_Base            (받침대 상판, 솔리드, 걸을 수 있음)
/// ├─ Catapult_Wheel ×2        (장식용 바퀴, BoxCollider — 아래 "바퀴 콜라이더" 참고)
/// ├─ Catapult_TrestleStrut ×4 (교차 지지대) + Catapult_TieBeam (하부 보강대) + Catapult_Axle
/// ├─ Catapult_ArmPivot        (CatapultArm — 로컬 X 회전으로 당김 연출)
/// │   ├─ Catapult_ArmVisual_Lower/Upper (팔 몸체, 2단 테이퍼)
/// │   ├─ Catapult_Counterweight        (지레 반대편 균형추 장식)
/// │   └─ Catapult_Bucket      (바구니 그룹, armPivot 자식 — 벽 3면(B/L/R)+바닥+내부 트리거. 그룹
/// │                            자체가 추가 회전(-90°X)+축소된 채 재배치된다 — 아래 "버킷 그룹
/// │                            Transform" 참고. 발사 경로를 막던 F 벽만 좌표 계산으로 확정해
/// │                            제거했다 — 아래 "F 벽 제거" 참고)
/// │       ├─ Catapult_BucketFloor      (바닥, 솔리드)
/// │       ├─ Catapult_BucketWall_B/L/R (벽 3면, 솔리드 — 옆에 스치기만 해선 탑승 안 됨. F는 제거됨)
/// │       ├─ Catapult_BucketInner      (CatapultBucket — 내부 트리거)
/// │       └─ Catapult_PullAnchor       (CatapultPullAnchor — 콜라이더 없는 순수 마커, 이 버킷
/// │                                     그룹에 부모화돼 팔이 당겨질 때 함께 움직인다)
/// ├─ Catapult_SteerHandle_Rod   (손잡이 막대 — 시각 전용, 콜라이더 있음)
/// └─ Catapult_SteerHandle_Ring  (N세그먼트 순수 시각 고리, 콜라이더 전혀 없음 — CatapultSteerHandle.
///                                dockAnchor가 이 Transform을 그대로 가리킨다)
/// **Rod와 Ring, `CatapultSteerHandle`은 전부 Catapult 루트의 직계 자식(형제)이다** — 고리가
/// 손잡이(그립)가 아니라 루트 자신에 조향 컴포넌트가 붙는 것은 `CatapultSteerHandle.cs` 상단 주석
/// 참고(물리 충돌을 다루지 않는 순수 상태머신+회전 스크립트이기 때문).
///
/// [버킷 높이 계산 — armVisual/bucket이 armPivot의 로컬 +Y 오프셋인 이유]
/// `CatapultArm.Awake()`는 항상 armPivot에 restAngle 회전을 먼저 적용한다. 그 뒤에 자식으로 붙는
/// armVisual/bucket의 로컬 오프셋은 "이미 회전된 프레임" 안에서 해석되므로, 팔이 "로컬 +Y로 곧게
/// 선 블루프린트"라면 회전 후 버킷 그룹 원점 높이는 `pivot.y + armLength*cos(θ)`로 결정된다(자식
/// 오프셋을 +Z 위주로 두면 회전이 그 오프셋을 아래로 접어 넣어 받침대 밑으로 파묻힌다 — 이 관례를
/// 지킬 것).
///
/// [버킷 그룹 Transform — 왜 그룹 자체가 추가 회전+축소를 갖나]
/// 정육면체(1×1×1, 이 기믹의 `Scale`과 무관하게 고정 크기)를 감싸는 버킷 캐비티가 `Scale` 배율을
/// 그대로 입으면 극단적인 크기 불일치가 생긴다(탑승 시 물리 폭발의 근본 원인이었다). 벽/바닥/내부
/// 트리거 자체의 로컬 치수(`BucketInnerHalf` 등)는 건드리지 않고, 그룹(`Catapult_Bucket`) 자신만
/// `BucketGroupLocalY`(위치)·`BucketGroupLocalRotation`(로컬 X -90°)·`BucketGroupLocalScale`
/// (축소)로 재배치해 정육면체 크기에 맞춘다 — 이 세 상수는 사용자가 씬에서 직접 확정한 값을
/// 코드로 옮긴 것이라 물리적 유도 근거는 없다(씬에서 검증된 값으로 신뢰).
///
/// **파묻힘 검산 공식(다음에 버킷 치수를 조정할 때 반드시 재사용할 것).** armPivot의 회전축(X)과
/// 그룹 자신의 회전축(X)이 같은 축이라 두 회전은 각도가 더해지는 것과 동일하게 합성된다. 그룹 로컬
/// 좌표 (0, Δy, Δz)를 가진 점(Δx는 공통 회전축이라 영향 없음)의 root 좌표 Y는:
/// ```
/// y = ApexY + (BucketGroupLocalY + BucketGroupLocalScale·Δz)·cosθ + BucketGroupLocalScale·Δy·sinθ
/// ```
/// (유도: 그룹 좌표에 스케일 → 그룹 자체 -90° 회전 → 그룹 위치 오프셋(armPivot 회전 θ의 영향을
/// 받음) → armPivot 회전 θ 순으로 합성.) 값을 바꾸면 **바닥 바닥면 모서리**(worst-case, 벽 두께·
/// 바닥 두께까지 포함한 가장 바깥쪽 아래 모서리)를 이 공식에 대입해 받침대 윗면(`BaseTopY`) 아래로
/// 내려가지 않는지 확인할 것.
///
/// **현재 확정값(`restAngle=0°`/`pulledAngle=80°`) 기준 재검산 — worst-case Δy=-2.4, Δz=-2.85(전부
/// ×Scale 반영값), `ApexY=1.8`, `BucketGroupLocalY=5.34`, `BucketGroupLocalScale=0.43`:**
/// ```
/// term1 = 5.34 + 0.43·(-2.85) = 4.1145,  term2 계수 = 0.43·(-2.4) = -1.032
/// rest(0°):   y = 1.8 + 4.1145·cos0° − 1.032·sin0° ≈ 5.9145  (받침대 윗면 0.6 대비 +5.3145)
/// pulled(80°): y = 1.8 + 4.1145·cos80° − 1.032·sin80° ≈ 1.502  (대비 +0.902)
/// ```
/// 여유는 θ에 대해 단조 감소(cosθ가 줄고 sinθ가 늘수록 term1 기여가 사라지고 term2 음수 기여가
/// 커진다)하므로, `pulledAngle`을 더 올릴 경우에만 재검산이 필요하다 — 낮추는 방향은 항상 더
/// 안전하다. `pulledAngle`이 80°인 이유는 파묻힘이 아니라 조향석 도킹 지점과의 물리적 겹침
/// 회피다(`CatapultArm.cs` 상단 주석 참고).
///
/// [F(발사 방향 쪽) 벽만 제거한 이유 — 좌표 유도]
/// 발사(분리)는 항상 팔이 `restAngle`(=0°)에 도달한 순간에 일어나고, 이때 발사 방향을 버킷 그룹의
/// 로컬 좌표로 역변환하면 `(Δx=0, Δy=+cos(launchPitch), Δz=+sin(launchPitch))`가 나온다 — Δx=0이라
/// L/R과 무관하고, Δz가 양수(F 쪽)라 광선이 열린 위쪽으로 빠져나가기 전에 F 벽의 Δz 범위에 먼저
/// 도달한다(B는 Δz 부호가 반대라 애초에 안 닿는다). 그래서 B/L/R 세 벽은 유지하고 F만 제거했다 —
/// F가 사라진 방향의 "가장자리 스침 탑승" 방지는 `CatapultBucket.IsWithinCentralBoardZone`(거리
/// 게이트)이 대신한다(자세한 근거는 `CatapultBucket.cs` 상단 주석 참고). 콜라이더 크기 자체를
/// 줄이지 않은 이유 — `CatapultBucket.ComputeBoardTargetPosition()`이 이 콜라이더의 `box.center`/
/// `box.size`로 바닥 위치를 계산하므로, 줄이면 그 계산이 함께 어긋난다.
///
/// [바퀴 콜라이더 — CapsuleCollider가 아니라 BoxCollider인 이유]
/// 원통 프리미티브의 기본 콜라이더는 `CapsuleCollider`인데, Unity는 캡슐의 "world height"가
/// "2×world radius"보다 작으면 원통 구간 길이를 0으로 clamp한다 — 우리 바퀴는 두께가 지름보다
/// 훨씬 얇아 이 clamp가 발동해 **반지름과 같은 반지름의 완전한 구**가 되고, 그 구가 바퀴 축 방향
/// 으로 반지름만큼 부풀어 받침대/트레슬을 관통했다(`radius`/`height`를 명시적으로 다시 세팅해도
/// 같은 clamp가 재적용돼 피할 수 없다 — 계산으로 확인). 원통 메시의 로컬 바운딩 박스와 정확히
/// 같은 `size=(1,2,1)`의 `BoxCollider`로 교체해 이 clamp 문제 자체를 없앴다(`CreateWheel` 참고).
/// 회전은 콜라이더가 없는 자식(`Catapult_WheelMesh`)에만 `CatapultWheelVisual`을 붙여 처리한다 —
/// 콜라이더를 가진 부모(`Catapult_Wheel`)는 절대 회전하지 않는다(회전축까지 부모에 함께 있으면
/// 사원수 합성상 축 자체가 매 프레임 방향을 바꾸는 팽이 회전이 나온다).
///
/// [조향 손잡이 — 설계는 `CatapultSteerHandle.cs` 참고, SpringJoint를 다시 시도하지 마라]
/// 손잡이(Rod+Ring)는 이제 순수 시각 오브젝트다 — 콜라이더가 있는 Rod는 시각 전용, 콜라이더가
/// 전혀 없는 Ring은 `CatapultSteerHandle.dockAnchor`가 부모로 쓰는 순수 위치 마커다(로컬 스케일이
/// 항상 (1,1,1)이어야 도킹된 구가 전단으로 왜곡되지 않는다). 조향 메커니즘 자체(도킹/회전 상태
/// 머신)와 "SpringJoint/충돌 기반 조향을 12라운드 시도하고 폐기한 이유"는 `CatapultSteerHandle.cs`
/// 상단 주석에 있다 — 이 파일에서 다시 서술하지 않는다.
///
/// [당김 앵커 — 왜 버킷 그룹에 부모화하고 한 축만 납작하게 눌렀나]
/// 앵커가 투석기 루트에 고정 오프셋으로 붙어 있으면 팔이 당겨져도 앵커 자체는 꿈쩍하지 않아
/// "바구니/지레를 직접 뒤로 당기는" 느낌이 안 났다 — `CreateBucket`이 이미 계산해 둔 half/wallT/
/// wallCenterY(B 벽과 같은 그룹 로컬 좌표계)를 그대로 재사용해 B 벽 바로 뒤(그룹 로컬 -Z)에
/// 부모화한다. 형태는 균일 스케일 구에서 로컬 Z축만 1/3로 눌러 납작하게 만들었다 — 임의로 고른
/// 축이 아니다: 그룹 자신의 -90°X 회전과 armPivot의 회전이 같은 축이라 각도가 합산되고(위 파묻힘
/// 공식과 같은 관계), rest 각도(armPivot=0°)에서 이 합성 회전은 정확히 -90°다 — 그 변환에서 그룹
/// 로컬 Z축은 세계(root) Y축(수직)으로 매핑되므로, 로컬 Z를 누르면 rest 각도에서 세계 기준
/// "수직으로 얇은" 원반이 되어 "손수레 손잡이 구멍"(수평으로 눕는 도넛) 모티프와 같은 방향으로
/// 읽힌다. `CatapultLoadController`는 앵커 위치를 연결 판정(최초 C)과 실 시각화에만 읽고 연결
/// 유지에는 거리를 쓰지 않으므로(마우스 휠이 대체), 앵커가 팔 스윙을 따라 움직여도 연결이 끊기지
/// 않는다.
/// </summary>
public static class CatapultMenuItem
{
    private const string SystemFolder = "Assets/CatapultSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    // 투석기 전체 균등 확대 배율. 이 파일의 모든 길이/위치 상수는 "(pre-scale 값) * Scale" 형태로
    // 선언한다(각도는 스케일 대상이 아니다 — CatapultArm의 restAngle/pulledAngle은 이 상수와 무관).
    // internal — 물리/파묻힘 검산에 관여하는 값이라 `SlingCatapultMenuItem`(리스킨 변형)이 같은
    // 숫자를 재사용한다. 여기서 internal로 바뀐 상수·메서드는 전부 같은 이유다: 두 생성기가 같은
    // 숫자를 각자 베끼면 한쪽만 고치고 잊는 드리프트가 난다(이 저장소가 씬-코드 드리프트로 여러
    // 라운드 고생한 이력, `CatapultSystem/CLAUDE.md` 참고).
    internal const float Scale = 3f;

    // 팔 블루프린트(회전 전 로컬 +Y 기준) 길이. armVisual/bucket 오프셋과 버킷 높이 계산이 이 값을
    // 공유한다(클래스 상단 "버킷 높이 계산" 참고).
    internal const float ArmLength = 1.4f * Scale;
    internal const float BaseTopY = 0.2f * Scale;   // Catapult_Base 윗면(로컬 y=scale.y/2, scale.y=BaseTopY → top = BaseTopY)
    internal const float ApexY = 0.6f * Scale;      // 트레슬 정점(= armPivot 높이, = Axle 높이)
    internal const float BaseHalfX = 1.2f * Scale;  // Catapult_Base 폭 절반
    internal const float BaseHalfZ = 1.3f * Scale;  // Catapult_Base 깊이 절반 — 조향 손잡이(앞쪽) 시작점 계산용

    // 바구니(벽+바닥) 치수의 "pre-scale" 비율 — CreateBucket(parent, scale, ...)이 실제 캐비티를
    // 지을 때 읽는 **단일 출처**다(2026-08-31 리뷰 반영: 예전엔 CreateBucket 내부에 이 숫자를
    // 리터럴로 다시 적어서, 아래 BucketInnerHalf 등 internal 상수를 고쳐도 실제 생성 결과가 하나도
    // 안 바뀌는 죽은 상수 드리프트가 있었다 — 지금은 CreateBucket도 이 Ratio 상수를 그대로
    // 곱해 쓰므로 여기 값을 고치면 생성 결과도 함께 바뀐다). 정육면체(1×1×1) 기준 확실한 여유로
    // 설계됐다: X 여유 0.4(각 옆면), Y 여유 0.2(위아래 각각), Z 여유 0.35(각 옆면).
    private static readonly Vector3 BucketInnerHalfRatio = new Vector3(0.9f, 0.7f, 0.85f);
    private const float BucketWallThicknessRatio = 0.1f;
    private const float BucketFloorThicknessRatio = 0.1f;
    private const float BucketWallHeightRatio = 1.5f; // 내부 트리거 천장(2×half.y)보다 확실히 높게 —
                                                       // "옆에 스치기만 해도 탑승"되던 버그 재발 방지.
                                                       // B/L/R 3면에만 적용된다(F는 제거).
    private const float BucketGroupLocalYRatio = 1.78f; // ≈5.34(Scale=3 기준) — ArmVisual_Upper 끝
                                                         // (로컬 Y 2.31~4.2, Scale=3 기준) 지나.

    // 전부 위 Ratio 상수 × 기본 `Scale`(3f)의 파생값이다 — 클래스 상단 주석의 파묻힘 검산 예시가
    // 참조하는 "Scale=3 기준" 수치이자, `CreateCatapult()`(원본 손수레 투석기)가 실제로 쓰는 값이다.
    // `SlingCatapultMenuItem`/`MiniCatapultMenuItem`은 이 상수가 아니라 위 Ratio 상수(또는
    // `CreateBucket`에 직접 넘기는 `scale` 매개변수)로 다른 배율을 만든다.
    internal static readonly Vector3 BucketInnerHalf = BucketInnerHalfRatio * Scale;
    internal const float BucketWallThickness = BucketWallThicknessRatio * Scale;
    internal const float BucketFloorThickness = BucketFloorThicknessRatio * Scale;
    internal const float BucketWallHeight = BucketWallHeightRatio * Scale;

    // 버킷 "그룹" 자체의 Transform(사용자가 씬에서 직접 확정한 값, 클래스 상단 "버킷 그룹 Transform"
    // 참고) — 벽/바닥/내부 트리거(BucketInnerHalf 등, 위)의 로컬 치수와는 별개다.
    internal const float BucketGroupLocalY = BucketGroupLocalYRatio * Scale;
    internal static readonly Quaternion BucketGroupLocalRotation = Quaternion.Euler(-90f, 0f, 0f);
    // 씬에 실제로 저장된 값은 로컬 X -84.37°(손 드래그로 생긴 오차)였지만, 사용자 의도는 정확히
    // -90°다 — 이 파일의 다른 정밀 각도(restAngle 등)와 달리 유도 근거가 되는 상수가 없는 임의값이라,
    // 오차를 그대로 베이크하지 않고 의도한 값으로 정리했다.
    internal const float BucketGroupLocalScale = 0.43f;
    // 감각적 판단(씬 확정값) — 사용자의 구두 지시는 "0.5"였으나 실제로 씬에 저장되고 플레이테스트된
    // 값은 0.43이다. 확신이 안 서면 씬을 확인해 그 값을 신뢰한다는 이 프로젝트의 관례에 따라 0.43을
    // 채택했다(0.5로 반올림하지 않는다). Scale(투석기 전체 배율)과는 독립된 별도 계수라, Scale이
    // 나중에 바뀌면 이 값도 함께 재튜닝이 필요할 수 있다(씬 튜닝 전제 [TBD, 임시값]).

    // 조향 손잡이(구 전용) 치수. Catapult 루트 로컬 좌표 기준.
    // Y만 언스케일 처리했다 — 구 콜라이더 반지름은 `PlayerObjectMenuItem.cs:234`에서 고정 0.5(이
    // 기믹의 Scale과 무관 — 구 오브젝트 자체는 카타풀트를 따라 커지지 않는다). 평지에 선 구의
    // 중심은 항상 월드 Y≈0.5(반지름만큼 떠서 접지)이고 도달 가능한 수직 범위는 0~1.0뿐이다. Z(막대
    // 길이)는 여전히 투석기 구조물 자체의 공간적 치수라 Scale을 그대로 곱한다.
    internal const float SteerHandlePivotY = 0.235f; // 언스케일 — Rod/Ring 공유 Y.
    internal static readonly Vector3 SteerHandlePivotLocal = new Vector3(0f, SteerHandlePivotY, 2.0f * Scale);
    // 고리(Ring)는 순수 시각 마커라 콜라이더가 없어 물리 도달 거리를 신경 쓸 필요가 없다(도킹
    // 판정은 `CatapultSteerHandle.dockRange`가 거리로 대신한다, 씬 튜닝 전 [TBD, 임시값]).
    internal const float SteerRingRadius = 0.5f * Scale;
    internal const float SteerRingTubeThickness = 0.12f * Scale;
    // 순수 장식이라 정밀도가 덜 중요해 세그먼트 개수를 적게 잡았다.
    internal const int SteerRingSegmentCount = 12;

    // 위 상수들은 전부 기본 `Scale`(3f) 기준값이고 `CreateCatapult()`가 그대로 쓴다. 아래
    // `XxxFor(scale)` 메서드들은 **같은 공식**을 다른 배율로 재사용하려는 다른 생성기용이다
    // (`SlingCatapultMenuItem`/`MiniCatapultMenuItem`) — 숫자를 다시 베끼지 않고 공식 하나만
    // 공유한다. `BucketGroupLocalRotation`/`BucketGroupLocalScale`/`SteerHandlePivotY`/
    // `SteerRingSegmentCount`는 애초에 scale과 무관해 별도 `For` 버전이 필요 없다.
    internal static float BaseTopYFor(float scale) => 0.2f * scale;
    internal static float ApexYFor(float scale) => 0.6f * scale;
    internal static float ArmLengthFor(float scale) => 1.4f * scale;
    internal static Vector3 SteerHandlePivotLocalFor(float scale) => new Vector3(0f, SteerHandlePivotY, 2.0f * scale);
    internal static float SteerRingRadiusFor(float scale) => 0.5f * scale;
    internal static float SteerRingTubeThicknessFor(float scale) => 0.12f * scale;

    // 조향 SpringJoint가 낼 수 있는 최대 견인력이 Unity 기본 마찰(정지마찰계수 0.6)을 이기지 못해
    // "밀어도 안 움직인다"는 문제의 원인이었던 시절의 유산이다(지금은 도킹 방식이라 조향 자체에는
    // 안 쓰이지만, 받침대·바퀴가 바닥과 닿는 콜라이더라 여전히 저마찰 재질을 준다 — "바퀴라 잘
    // 미끄러진다"는 세계관과도 맞는다). 정확한 수치는 감각적 판단 — [TBD, 임시값].
    private const float LowFrictionCoefficient = 0.05f;

    [MenuItem("Tools/Catapult/Create Catapult")]
    private static void CreateCatapult()
    {
        EnsureMaterialFolder();

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        GameObject root = new GameObject("Catapult");
        root.transform.position = origin;
        CatapultLoadController loadController = root.AddComponent<CatapultLoadController>();

        // 받침대·벽·트레슬 등 모든 콜라이더가 이미 루트 자식으로 물려 있으므로, 여기 하나만 붙이면
        // 전부 하나의 컴파운드 콜라이더로 묶여 한 몸처럼 낙하·충돌한다.
        Rigidbody rootBody = ConfigureRootRigidbody(root);

        // 바닥과 닿는 콜라이더(받침대 상판·바퀴)에 적용할 저마찰 재질.
        PhysicMaterial lowFriction = CreateLowFrictionMaterial();

        CreateBase(root.transform, lowFriction);
        CreateWheels(root.transform, lowFriction);
        CreateTrestle(root.transform);
        GameObject armPivot = CreateArmPivot(root.transform, ApexY);
        CreateArmVisual(armPivot.transform);
        CreateCounterweight(armPivot.transform);
        GameObject bucketInner = CreateBucket(armPivot.transform, Scale, out GameObject anchor); // 앵커도 여기서 생성(버킷 그룹에 부모화).
        GameObject steerRing = CreateSteerHandle(root.transform); // 반환값은 고리(Ring) GameObject.

        CatapultArm arm = armPivot.GetComponent<CatapultArm>();
        arm.armPivot = armPivot.transform;
        arm.aimRoot = root.transform; // 조향(루트 요 회전)과 발사 방향을 일치시킨다 — 팔의 장전 회전과 분리.
        arm.bucket = bucketInner.GetComponent<CatapultBucket>();

        loadController.anchor = anchor.GetComponent<CatapultPullAnchor>();
        loadController.arm = arm;

        // CatapultSteerHandle은 손잡이(그립/고리)가 아니라 투석기 루트 자신에 붙는다
        // (CatapultLoadController/Rigidbody와 같은 자리 — 물리에 관여하지 않고 순수 상태머신+조향
        // 회전만 담당하기 때문, CatapultSteerHandle.cs 상단 주석 참고). aimRoot와 같은 방식으로
        // rootBody를 직접 연결한다.
        CatapultSteerHandle steerHandle = root.AddComponent<CatapultSteerHandle>();
        steerHandle.dockAnchor = steerRing.transform;
        steerHandle.rootBody = rootBody;

        Undo.RegisterCreatedObjectUndo(root, "Create Catapult");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = root;
        Debug.Log("[Catapult] 투석기 생성 완료. 정육면체는 위에서 뛰어들거나 근처에서 C를 눌러 " +
                   "버킷에 자동 탑승합니다(점프만으로는 안 닿을 수 있음 — 의도된 동작). Tab으로 " +
                   "구를 조작해 투석기 앞 손잡이 고리 근처에서 C를 누르면(도킹 후 직접 조작) 그 " +
                   "자리에 고정되고 커지며, 좌우 이동 입력으로 투석기를 직접 조향할 수 있습니다" +
                   "(다시 C로 해제). 정사면체는 당김 앵커 근처에서 C로 당김 줄을 연결/해제해 " +
                   "장전·발사합니다.");
    }

    // 투석기 루트 Rigidbody 설정 — internal: `SlingCatapultMenuItem`(리스킨 변형)도 물리적으로
    // 완전히 같은 몸이어야 하므로 값을 복제하지 않고 이 메서드를 그대로 재사용한다.
    internal static Rigidbody ConfigureRootRigidbody(GameObject root)
    {
        Rigidbody rootBody = root.AddComponent<Rigidbody>();
        rootBody.useGravity = true;
        rootBody.isKinematic = false;
        // mass는 부피(27배) 아닌 선형(3배) 기준으로 스케일했다 — "적당히 무겁게"라는 감각적
        // 판단이라 부피 그대로 27배로 키우면 지나치게 무거워진다. 선형 배율은 "커진 만큼 조금 더
        // 무겁게"라는 절충이다 — 씬 튜닝 전제 [TBD, 임시값].
        rootBody.mass = 150f;
        rootBody.drag = 1.2f; // 무력 상태일 때 남은 관성을 엔진 자체가 서서히 죽인다.
        rootBody.angularDrag = 5f; // 각속도 진동/오버슈트를 억제한다.
        rootBody.interpolation = RigidbodyInterpolation.Interpolate; // 시각적 떨림 완화.
        // 회전은 요(Y)만 조향에 쓰므로 X/Z는 얼린다.
        rootBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        // 다른 빠른 동적 바디(플레이어)가 부딪힐 수 있는 상대적으로 느린 큰 구조물 역할에 맞는 CCD
        // 모드다(구처럼 스스로 빠르게 움직이는 발사체가 아니므로 ContinuousDynamic이 아니라
        // Continuous를 쓴다).
        rootBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        return rootBody;
    }

    private static GameObject CreateBase(Transform parent, PhysicMaterial groundMaterial)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObj.name = "Catapult_Base";
        baseObj.transform.SetParent(parent, false);
        baseObj.transform.localPosition = new Vector3(0f, BaseTopY * 0.5f, 0f);
        baseObj.transform.localScale = new Vector3(BaseHalfX * 2f, BaseTopY, BaseHalfZ * 2f); // top = BaseTopY.
        baseObj.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Base", new Color(0.55f, 0.4f, 0.22f));
        // 바닥과 실제로 닿는 상판에 저마찰 재질을 준다.
        baseObj.GetComponent<Collider>().material = groundMaterial;
        return baseObj;
    }

    // 장식용 바퀴 — "손수레" 인상을 강화한다(조향 메커니즘이 이미 손수레 비유를 쓰고 있어 시각적
    // 으로도 뒷받침). 실제 회전은 CatapultWheelVisual이 순수 시각으로 담당한다(아래 CreateWheel
    // 참고).
    private static void CreateWheels(Transform parent, PhysicMaterial groundMaterial)
    {
        Material mat = LoadOrCreateMaterial("Wheel", new Color(0.3f, 0.22f, 0.12f));
        Material strutMat = LoadOrCreateMaterial("Base", new Color(0.55f, 0.4f, 0.22f));
        float radius = 0.5f * Scale;
        float thickness = 0.18f * Scale;
        float xOffset = BaseHalfX + thickness * 0.5f + 0.05f * Scale;

        CreateWheel(parent, new Vector3(-xOffset, radius, 0f), radius, thickness, mat, groundMaterial);
        CreateWheel(parent, new Vector3(xOffset, radius, 0f), radius, thickness, mat, groundMaterial);
        CreateWheelStrut(parent, -1f, xOffset, radius, strutMat);
        CreateWheelStrut(parent, 1f, xOffset, radius, strutMat);
    }

    // 바퀴와 받침대 사이를 잇는 축(순수 시각 장식). Catapult_TieBeam/Catapult_TrestleStrut와 같은
    // 생성 패턴을 재사용한다(로컬 X축이 이미 길이 방향이라 회전 없이 scale.x만 늘린다). 콜라이더는
    // 굳이 필요하지 않다고 판단했다(YAGNI) — 받침대 옆면보다 낮은 위치(바퀴 축 높이)라 플레이어가
    // 걸어 다니다 부딪힐 일이 거의 없고, 부딪혀도 걷는 표면이 아니라 옆으로 스치는 정도라 위험이 낮다.
    private static void CreateWheelStrut(Transform parent, float xSign, float xOffset, float wheelRadius, Material mat)
    {
        GameObject strut = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strut.name = "Catapult_WheelStrut";
        Object.DestroyImmediate(strut.GetComponent<Collider>());
        strut.transform.SetParent(parent, false);
        float length = xOffset - BaseHalfX; // 받침대 옆면에서 바퀴 중심까지.
        strut.transform.localPosition = new Vector3(xSign * (BaseHalfX + length * 0.5f), wheelRadius, 0f);
        strut.transform.localScale = new Vector3(length, 0.1f * Scale, 0.1f * Scale);
        strut.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // 콜라이더(고정)와 회전 메시(자식)를 분리한다 — 콜라이더와 회전 스크립트가 같은 Transform에
    // 있으면 회전마다 컴파운드 콜라이더 형상까지 함께 돌아 "순수 시각 연출"이 깨진다. "Catapult_
    // Wheel"(부모, 콜라이더 전용, 절대 회전하지 않음)과 "Catapult_WheelMesh"(자식, 메시+회전
    // 스크립트 전용)로 나눈다 — 자식은 부모의 고정 회전(R0)을 상속만 하고 자신의 로컬 회전은
    // identity에서 시작하므로, 자식 자신의 로컬 Y축(메시의 원래 높이/축 방향)을 돌리면 축 방향이
    // 고정된 채(World = R0 * Ry(θ)) 올바르게 굴러간다(로컬 X를 돌리면 사원수 합성상 축 자체가 매
    // 프레임 방향을 바꾸는 팽이 회전이 나온다 — `CatapultWheelVisual`도 이 축을 로컬 Y로 맞췄다).
    private static void CreateWheel(Transform parent, Vector3 localPos, float radius, float thickness, Material mat, PhysicMaterial groundMaterial)
    {
        GameObject wheel = new GameObject("Catapult_Wheel");
        wheel.transform.SetParent(parent, false);
        wheel.transform.localPosition = localPos;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // 원통의 높이축을 로컬 X(옆면)로 돌려 바퀴처럼 보이게 한다.
        // 기본 Cylinder는 scale=1일 때 반지름 0.5·높이 2 — 따라서 반지름은 *2f, 두께(높이)는 *0.5f로 환산.
        wheel.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);

        // CapsuleCollider는 두께가 지름보다 얇으면 Unity가 원통 구간을 0으로 clamp해 완전한 구가
        // 되므로 쓸 수 없다(클래스 상단 "바퀴 콜라이더" 참고). BoxCollider는 이 부모(회전하지
        // 않음)에 고정된다.
        BoxCollider wheelCollider = wheel.AddComponent<BoxCollider>();
        wheelCollider.size = new Vector3(1f, 2f, 1f);
        wheelCollider.material = groundMaterial; // 바퀴도 바닥과 닿을 수 있는 콜라이더라 저마찰 재질을 적용한다.

        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mesh.name = "Catapult_WheelMesh";
        Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.transform.SetParent(wheel.transform, false);
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localRotation = Quaternion.identity; // 부모의 R0을 그대로 상속만 한다 — 정지 시 외형은 이전과 동일.
        mesh.transform.localScale = Vector3.one; // 부모 스케일을 그대로 상속한다.
        mesh.GetComponent<Renderer>().sharedMaterial = mat;
        mesh.AddComponent<CatapultWheelVisual>().wheelRadius = radius; // 순수 시각 회전, 콜라이더 없는 자식(§ 위 주석 참고).
    }

    // 레퍼런스 사진처럼 좌/우에 교차 지지대(X자 목재 트레슬)를 세우고, 그 위를 가로대(Axle)로 연결한다.
    // 걸어다니는 건 Catapult_Base 상판이지만 이 장식들도 콜라이더를 유지한다 — Rigidbody 없는
    // static child라 투석기 루트의 컴파운드 콜라이더에 자동 편입된다.
    private static void CreateTrestle(Transform parent)
    {
        Material woodMat = LoadOrCreateMaterial("Base", new Color(0.55f, 0.4f, 0.22f));
        const float crossX = 0.6f * Scale;
        CreateTrestleCross(parent, -crossX, woodMat);
        CreateTrestleCross(parent, crossX, woodMat);
        CreateTieBeam(parent, crossX, woodMat);

        GameObject axle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        axle.name = "Catapult_Axle";
        axle.transform.SetParent(parent, false);
        axle.transform.localPosition = new Vector3(0f, ApexY, 0f);
        axle.transform.localScale = new Vector3(1.3f * Scale, 0.12f * Scale, 0.12f * Scale);
        axle.GetComponent<Renderer>().sharedMaterial = woodMat;
    }

    // 한 쪽(x 위치)에 X자로 교차하는 지지대 2개를 만든다. 반대쪽은 호출부에서 x 부호만 뒤집어 재사용.
    private static void CreateTrestleCross(Transform parent, float x, Material mat)
    {
        float height = ApexY - BaseTopY;
        float centerY = (BaseTopY + ApexY) / 2f;
        CreateStrut(parent, new Vector3(x, centerY, 0f), 28f, height, mat);
        CreateStrut(parent, new Vector3(x, centerY, 0f), -28f, height, mat);
    }

    private static void CreateStrut(Transform parent, Vector3 localPos, float rotZDeg, float height, Material mat)
    {
        GameObject strut = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strut.name = "Catapult_TrestleStrut";
        strut.transform.SetParent(parent, false);
        strut.transform.localPosition = localPos;
        strut.transform.localRotation = Quaternion.Euler(0f, 0f, rotZDeg);
        strut.transform.localScale = new Vector3(0.12f * Scale, height / Mathf.Cos(rotZDeg * Mathf.Deg2Rad), 0.12f * Scale);
        strut.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // 좌/우 X자 지지대 하단을 잇는 보강대 — 레퍼런스의 목재 프레임 느낌을 보강한다. 파묻힘 계산과는
    // 무관(버킷 그룹 콜라이더만 그 계산의 대상)하지만 콜라이더 자체는 유지한다.
    private static void CreateTieBeam(Transform parent, float crossX, Material mat)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Catapult_TieBeam";
        beam.transform.SetParent(parent, false);
        beam.transform.localPosition = new Vector3(0f, BaseTopY + 0.15f * Scale, 0f);
        beam.transform.localScale = new Vector3(crossX * 2f + 0.3f * Scale, 0.12f * Scale, 0.12f * Scale);
        beam.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // internal — `SlingCatapultMenuItem`/`MiniCatapultMenuItem`도 같은 armPivot(CatapultArm 부착)을
    // 그대로 재사용한다(팔 회전 각도 자체는 armPivot이 아니라 CatapultArm 필드가 결정하므로 이
    // 메서드는 리스킨·크기와 무관하게 항상 같아야 한다). `apexY`는 호출부가 `ApexY`(기본) 또는
    // `ApexYFor(scale)`(다른 배율)를 넘긴다.
    internal static GameObject CreateArmPivot(Transform parent, float apexY)
    {
        GameObject pivot = new GameObject("Catapult_ArmPivot");
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = new Vector3(0f, apexY, 0f); // 트레슬 정점(Axle)에서 피벗.
        pivot.AddComponent<CatapultArm>();
        return pivot;
    }

    // armPivot 로컬 +Y로 곧게 선 블루프린트(회전 전 기준) — 클래스 상단 주석의 "버킷 높이 계산"
    // 참고. 균일 두께 박스 하나 대신, 피벗 쪽이 두껍고 버킷 쪽이 얇은 2단 테이퍼로 지레 느낌을
    // 준다. 파묻힘 계산에는 관여하지 않지만(버킷 그룹 콜라이더만 그 계산의 대상), 콜라이더 자체는
    // 유지한다(armPivot 자식이라 armPivot 회전을 따라간다. 물리력을 추가하지 않는다는 사용자
    // 확인과 함께).
    private static void CreateArmVisual(Transform parent)
    {
        Material mat = LoadOrCreateMaterial("Arm", new Color(0.5f, 0.32f, 0.15f));

        float lowerHeight = ArmLength * 0.55f;
        float upperHeight = ArmLength - lowerHeight;
        float lowerThick = 0.26f * Scale;
        float upperThick = 0.16f * Scale;

        CreateArmSegment(parent, "Catapult_ArmVisual_Lower", lowerHeight * 0.5f, lowerHeight, lowerThick, mat);
        CreateArmSegment(parent, "Catapult_ArmVisual_Upper", lowerHeight + upperHeight * 0.5f, upperHeight, upperThick, mat);
    }

    private static void CreateArmSegment(Transform parent, string name, float centerY, float height, float thickness, Material mat)
    {
        GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        seg.name = name;
        seg.transform.SetParent(parent, false);
        seg.transform.localPosition = new Vector3(0f, centerY, 0f);
        seg.transform.localScale = new Vector3(thickness, height, thickness);
        seg.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // 지레 반대편의 균형추 장식 — armPivot의 -Y쪽(팔/버킷과 반대 방향)에 붙어 팔이 회전할 때 함께
    // 돌아 "무게 중심을 맞추는 반대쪽 추"처럼 보인다. 순수 장식(질량 없음 — 실제 물리 균형에는
    // 관여하지 않는다, 관여시키려면 Rigidbody 질량 분포까지 다시 설계해야 해 범위 밖으로 판단했다,
    // YAGNI). 콜라이더는 유지한다 — 질량이 없다는 것과 콜라이더가 있다는 것은 별개다.
    private static void CreateCounterweight(Transform parent)
    {
        Material mat = LoadOrCreateMaterial("Counterweight", new Color(0.25f, 0.22f, 0.2f));
        GameObject cw = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cw.name = "Catapult_Counterweight";
        cw.transform.SetParent(parent, false);
        float cwHalf = 0.3f * Scale;
        cw.transform.localPosition = new Vector3(0f, -(cwHalf + 0.05f * Scale), 0f);
        cw.transform.localScale = Vector3.one * (cwHalf * 2f);
        cw.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // 바구니 그룹: 벽 3면(B/L/R, 솔리드) + 바닥(솔리드) + 그보다 안쪽·위의 내부 트리거
    // (CatapultBucket, 실제 탑승 판정) — "옆에 스치기만 해도 탑승"되던 버그 수정. F 벽만 좌표
    // 계산으로 확정해 제거됐다(클래스 상단 "F 벽 제거" 참고) — B/L/R은 여전히 그 버그를 막는다.
    // 반환값은 CatapultBucket이 붙은 내부 트리거 GameObject다(arm.bucket이 이걸 참조한다). 치수/
    // 파묻힘 검산은 클래스 상단 주석 참고.
    // 당김 앵커(`CreateAnchor`)를 이 함수 안에서 만들어 `out anchor`로 반환한다 — 앵커가 투석기
    // 루트가 아니라 이 버킷 "그룹"(group.transform, B 벽과 같은 로컬 좌표계)에 부모화돼야 팔이
    // 당겨질 때 앵커도 버킷과 함께 움직이기 때문이다(클래스 상단 "당김 앵커" 참고) — 그룹 Transform
    // 은 이 함수 스코프 밖에서 얻을 방법이 없어 호출을 여기로 뒀다.
    // internal — 버킷 캐비티(치수/파묻힘 검산)는 리스킨과 무관하게 항상 같은 공식을 써야 하므로
    // `SlingCatapultMenuItem`/`MiniCatapultMenuItem`도 이 메서드를 `scale`만 다르게 넘겨 그대로
    // 호출한다(`scale`이 다르면 캐비티 절대 크기도 그 배율만큼 정확히 줄어든다 — `BucketGroupLocalRotation`/
    // `BucketGroupLocalScale`은 scale과 무관한 별도 계수라 그대로 재사용된다, 아래 본문 참고).
    // `wallMaterial`은 벽/바닥 색만 바꿀 수 있게 연 선택적 시각 파라미터다(콜라이더 치수에는 영향
    // 없음) — 기본값(null)이면 기존 재질을 그대로 쓴다.
    internal static GameObject CreateBucket(Transform parent, float scale, out GameObject anchor, Material wallMaterial = null)
    {
        Material woodMat = wallMaterial != null ? wallMaterial : LoadOrCreateMaterial("Bucket", new Color(0.2f, 0.2f, 0.22f));

        GameObject group = new GameObject("Catapult_Bucket");
        group.transform.SetParent(parent, false);
        // 그룹 자체의 위치/회전/스케일(사용자가 씬에서 직접 확정한 값, 클래스 상단 주석 "버킷 그룹
        // Transform" 참고). 안쪽 벽/바닥/트리거(BucketInnerHalf 등, 아래)의 로컬 치수는 그대로다 —
        // 그룹만 재배치·회전·축소해 정육면체(고정 1×1×1, 또는 그 균일 배율) 크기에 맞춘다.
        // `BucketGroupLocalRotation`/`BucketGroupLocalScale`은 scale과 무관해 그대로 재사용한다.
        // 나머지는 전부 클래스 상단의 Ratio 상수 × scale로 구한다(리터럴을 다시 적지 않는다 —
        // 2026-08-31 리뷰 반영, 클래스 상단 "바구니 치수" 주석 참고).
        float groupLocalY = BucketGroupLocalYRatio * scale;
        group.transform.localPosition = new Vector3(0f, groupLocalY, 0f);
        group.transform.localRotation = BucketGroupLocalRotation;
        group.transform.localScale = Vector3.one * BucketGroupLocalScale;

        Vector3 half = BucketInnerHalfRatio * scale;
        float wallT = BucketWallThicknessRatio * scale;
        float floorT = BucketFloorThicknessRatio * scale;
        float wallHeight = BucketWallHeightRatio * scale;

        // 바닥: 내부 트리거 바닥(-half.y) 바로 아래, 벽 두께만큼 옆으로 넉넉히.
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Catapult_BucketFloor";
        floor.transform.SetParent(group.transform, false);
        floor.transform.localPosition = new Vector3(0f, -half.y - floorT * 0.5f, 0f);
        floor.transform.localScale = new Vector3((half.x + wallT) * 2f, floorT, (half.z + wallT) * 2f);
        floor.GetComponent<Renderer>().sharedMaterial = woodMat;

        // 벽 3면(B/L/R): 바닥 윗면(-half.y)부터 위로 wallHeight만큼. F(그룹 로컬 +Z)만 제거됐다
        // (클래스 상단 "F 벽 제거" 주석의 좌표 유도 참고).
        float wallCenterY = -half.y + wallHeight * 0.5f;
        CreateBucketWall(group.transform, "Catapult_BucketWall_L", new Vector3(-(half.x + wallT * 0.5f), wallCenterY, 0f),
            new Vector3(wallT, wallHeight, (half.z + wallT) * 2f), woodMat);
        CreateBucketWall(group.transform, "Catapult_BucketWall_R", new Vector3(half.x + wallT * 0.5f, wallCenterY, 0f),
            new Vector3(wallT, wallHeight, (half.z + wallT) * 2f), woodMat);
        CreateBucketWall(group.transform, "Catapult_BucketWall_B", new Vector3(0f, wallCenterY, -(half.z + wallT * 0.5f)),
            new Vector3(half.x * 2f, wallHeight, wallT), woodMat);

        // 내부 트리거: 벽보다 안쪽, 바닥보다 위 — 정육면체 콜라이더가 여기 겹칠 때만 탑승 판정
        // (또는 CatapultBucket의 C키 텔레포트가 이 지점을 목표로 삼는다). F 쪽은 벽이 없어졌으므로
        // CatapultBucket의 중앙 0.8배 거리 게이트가 그쪽 가장자리 스침을 대신 걸러낸다.
        GameObject inner = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inner.name = "Catapult_BucketInner";
        inner.transform.SetParent(group.transform, false);
        inner.transform.localPosition = Vector3.zero;
        inner.transform.localScale = half * 2f;
        Renderer innerRenderer = inner.GetComponent<Renderer>();
        Object.DestroyImmediate(innerRenderer); // 내부 트리거는 보이지 않아도 된다(벽이 바구니 형태를 이미 보여준다).

        BoxCollider innerCol = inner.GetComponent<BoxCollider>();
        innerCol.isTrigger = true;

        inner.AddComponent<CatapultBucket>();

        // 앵커를 그룹 자신에 부모화해 B 벽 바로 뒤(그룹 로컬 -Z)에 둔다. half/wallT/wallCenterY는
        // 이 함수가 이미 계산해 둔 값을 그대로 재사용한다(새 좌표계를 다시 유도하지 않는다).
        anchor = CreateAnchor(group.transform, half, wallT, wallCenterY, scale);

        return inner;
    }

    // F(발사 방향 쪽) 호출만 없다 — B/L/R은 여전히 이 함수를 쓴다.
    private static void CreateBucketWall(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = size;
        wall.GetComponent<Renderer>().sharedMaterial = mat;
        // BoxCollider는 기본으로 트리거가 아니다 — 솔리드로 그대로 둬서 옆에서 물리적으로 막는다.
    }

    // 앵커를 투석기 루트의 고정 오프셋이 아니라 버킷 "그룹" 자신에 부모화한다(왜인지는 클래스 상단
    // "당김 앵커" 주석 참고). 위치는 `CreateBucket`이 이미 계산해 둔 half/wallT/wallCenterY(B 벽과
    // 같은 그룹 로컬 좌표계)를 그대로 재사용해 B 벽 바로 뒤(그룹 로컬 -Z)에 둔다. 형태도 솔리드
    // 구에서 한 축을 눌러 납작하게(디스크/고리처럼) 바꿨다 — "당김줄을 거는 고리"로 읽히길 원한다는
    // 요청 반영. 콜라이더 없는 순수 위치 마커라는 성질(ThreadAnchor와 동일 이유)은 그대로다.
    private static GameObject CreateAnchor(Transform bucketGroup, Vector3 half, float wallT, float wallCenterY, float scale)
    {
        GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        anchor.name = "Catapult_PullAnchor";
        Object.DestroyImmediate(anchor.GetComponent<Collider>()); // 순수 위치 마커 — 물리 접촉 없음(ThreadAnchor와 동일 이유).
        anchor.transform.SetParent(bucketGroup, false);

        // B 벽 바깥 면(-(half.z+wallT))보다 더 뒤로 살짝 띄운다 — "벽에 매달린 고리"처럼 보이게
        // 하는 여유값(감각적 판단, [TBD, 임시값]).
        float margin = 0.25f * scale;
        anchor.transform.localPosition = new Vector3(0f, wallCenterY, -(half.z + wallT + margin));

        // 한 축(로컬 Z, "그룹 안으로 파고드는" 깊이 방향)만 1/3로 눌러 납작한 디스크처럼 보이게
        // 한다 — 나머지 두 축은 살짝 키워 눌린 만큼의 시각적 존재감을 보존했다. 왜 로컬 Z를
        // 눌렀는지는 클래스 상단 "당김 앵커" 주석 참고(임의로 고른 축이 아니라 파묻힘 공식과 같은
        // 변환식으로 재검산해 고른 축이다).
        float anchorDiameter = 0.5f * scale;
        float anchorFlattenedDiameter = anchorDiameter / 3f;
        anchor.transform.localScale = new Vector3(anchorDiameter, anchorDiameter, anchorFlattenedDiameter);

        anchor.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Anchor", new Color(1f, 0.6f, 0.1f));
        anchor.AddComponent<CatapultPullAnchor>();
        return anchor;
    }

    // 손잡이 — 막대(Rod, 시각 전용+콜라이더) + 고리(Ring, 순수 시각 도킹 마커, 콜라이더 없음).
    // 반환값은 고리(Ring) GameObject다 — `CatapultSteerHandle.dockAnchor`가 이 Transform을 그대로
    // 가리킨다(고리 자신의 로컬 스케일이 항상 (1,1,1)이라, 비균일 스케일 부모에 도킹 대상을
    // 부모화하면 전단으로 왜곡되는 함정을 애초에 피한다 — 세그먼트마다 개별 스케일을 주고 링
    // 자신은 스케일하지 않기 때문).
    private static GameObject CreateSteerHandle(Transform parent)
    {
        Material rodMat = LoadOrCreateMaterial("Arm", new Color(0.5f, 0.32f, 0.15f));
        Material ringMat = LoadOrCreateMaterial("Ring", new Color(0.35f, 0.35f, 0.38f)); // 철제 손잡이 느낌(회색조).

        // 막대: 받침대 앞(BaseHalfZ)에서 고리 **테두리 안쪽 면**까지(고리 중심까지 뻗으면 시각적
        // 으로 고리를 꿰뚫고 지나가는 것처럼 보인다) — `SteerHandlePivotLocal`(Rod/Ring 공유 좌표,
        // dockAnchor 위치)은 건드리지 않는다.
        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rod.name = "Catapult_SteerHandle_Rod";
        rod.transform.SetParent(parent, false);
        float ringNearEdgeZ = SteerHandlePivotLocal.z - SteerRingRadius + SteerRingTubeThickness * 0.5f;
        float rodLength = ringNearEdgeZ - BaseHalfZ;
        rod.transform.localPosition = new Vector3(0f, SteerHandlePivotLocal.y, BaseHalfZ + rodLength * 0.5f);
        rod.transform.localScale = new Vector3(0.15f * Scale, 0.15f * Scale, rodLength);
        rod.GetComponent<Renderer>().sharedMaterial = rodMat;

        return CreateSteerRingVisual(parent, SteerHandlePivotLocal, SteerRingRadius, SteerRingTubeThickness, SteerRingSegmentCount, ringMat);
    }

    // N세그먼트 도넛 근사(수평 XZ 평면, "손수레 손잡이 구멍" 구도)를 순수 시각 전용으로 만든다 —
    // 콜라이더가 하나도 없다("장식 오브젝트는 콜라이더를 아예 없앤다"는 `CreateAnchor`와 같은
    // 관례). 반환하는 링 GameObject 자신은 스케일을 건드리지 않는다(항상 (1,1,1)) —
    // CatapultSteerHandle.dockAnchor가 이 Transform을 그대로 부모로 쓰므로, 여기에 비균일 스케일을
    // 주면 도킹된 구가 왜곡된다(위 CreateSteerHandle 주석 참고).
    // internal — 조향 도킹 지점(위치·반지름) 자체는 리스킨과 무관하게 항상 같은 공식을 써야 하므로
    // `SlingCatapultMenuItem`/`MiniCatapultMenuItem`도 이 메서드를 `ringRadius`/`tubeThickness`만
    // 다르게 넘겨 그대로 호출한다(재질도 다르게 넘긴다).
    internal static GameObject CreateSteerRingVisual(Transform parent, Vector3 localCenter, float ringRadius, float tubeThickness, int segmentCount, Material mat)
    {
        GameObject ring = new GameObject("Catapult_SteerHandle_Ring");
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = localCenter;

        float segmentAngleDeg = 360f / segmentCount;
        float segmentLength = (2f * Mathf.PI * ringRadius / segmentCount) * 1.15f; // 세그먼트 사이 틈 방지 여유.

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * segmentAngleDeg, 0f) * Vector3.forward;

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "Catapult_SteerHandle_RingSegment";
            Object.DestroyImmediate(seg.GetComponent<Collider>()); // 순수 시각 — 물리에 전혀 관여하지 않는다.
            seg.transform.SetParent(ring.transform, false);
            seg.transform.localPosition = dir * ringRadius;
            seg.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            // LookRotation(dir, Vector3.up)은 세그먼트의 로컬 Z를 반지름 방향(dir)에, 로컬 X를
            // 접선 방향에 놓는다 — 그래서 접선(X)에 호 길이(segmentLength), 반지름(Z)에 두께
            // (tubeThickness)를 줘야 세그먼트들이 원둘레를 따라 이어 붙어 실제 닫힌 원으로 보인다
            // (반대로 주면 중심에서 바깥으로 뻗는 꽃잎/바큇살처럼 보인다).
            seg.transform.localScale = new Vector3(segmentLength, tubeThickness, tubeThickness);
            seg.GetComponent<Renderer>().sharedMaterial = mat;
        }

        return ring;
    }

    // `PlayerShapeIdentity.Start()`가 이미 쓰는 패턴(런타임에 `new PhysicMaterial(...)` 인스턴스
    // 생성, 별도 에셋으로 저장하지 않음)을 그대로 따른다. 이 기믹은 정육면체/정사면체와 달리
    // 도형별 스탯 에셋이 없어 인스턴스를 도형 이름별로 나눌 필요가 없으므로, 투석기 한 대당 하나만
    // 만들어 바닥과 닿는 콜라이더(상판·바퀴)끼리 공유한다. `frictionCombine = Minimum`으로 둬,
    // 지형 쪽 PhysicMaterial이 무엇이든(또는 없든) 접촉 마찰이 항상 이 낮은 값 이하로 정해지게
    // 한다.
    // internal — `SlingCatapultMenuItem`도 같은 저마찰 재질을 재사용한다(계수를 따로 복제하지 않는다).
    internal static PhysicMaterial CreateLowFrictionMaterial()
    {
        return new PhysicMaterial("Catapult_LowFriction")
        {
            staticFriction = LowFrictionCoefficient,
            dynamicFriction = LowFrictionCoefficient,
            frictionCombine = PhysicMaterialCombine.Minimum,
        };
    }

    // internal — `SlingCatapultMenuItem`도 같은 `CatapultSystem/Materials` 폴더에 재질을 저장하므로
    // 이 메서드를 재사용한다(경로를 따로 복제하지 않는다).
    internal static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(SystemFolder))
            AssetDatabase.CreateFolder("Assets", "CatapultSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");
    }

    // internal — `SlingCatapultMenuItem`도 이 메서드로 자기 재질을 만든다(경로/셰이더 해석 로직을
    // 복제하지 않는다).
    internal static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialSavePath}/Catapult_{name}_Mat.mat";
        Shader shader = ResolveShader();

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
        {
            mat.shader = shader;
            mat.color = color;
        }
        else
        {
            mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
        }
        EditorUtility.SetDirty(mat);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    /// <summary>현재 렌더 파이프라인에 맞는 셰이더를 반환한다(ShapeGimmickSetup/DreamThreadMenuItem과
    /// 같은 방식 — Built-in/URP/HDRP 대응).</summary>
    private static Shader ResolveShader()
    {
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        if (pipeline == null)
        {
            Shader s = Shader.Find("Standard");
            if (s != null) return s;
        }
        else
        {
            string n = pipeline.GetType().Name;
            if (n.Contains("Universal") || n.Contains("URP"))
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) return s;
            }
            else if (n.Contains("HighDefinition") || n.Contains("HDRP"))
            {
                Shader s = Shader.Find("HDRP/Lit");
                if (s != null) return s;
            }
        }

        Debug.LogWarning("[Catapult] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}
