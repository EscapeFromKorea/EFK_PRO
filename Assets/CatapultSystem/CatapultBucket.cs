using UnityEngine;

/// <summary>
/// 투석기 버킷의 탑승 판정 컴포넌트. 정육면체(Kind == Cube)만 탑승 판정을 받는다 — "핀 박기는 세모
/// 전용"으로 `Kind`를 게이트하는 `ThreadPinPlacer`와 같은 종류의 역할 제한이지, 무게 게이트가
/// 아니다(PRD §5).
///
/// 개편 이력(라운드별로 무엇을 왜 바꿨는지)은 코드가 아니라 `CatapultSystem/CLAUDE.md`에 기록돼
/// 있다 — 이 요약은 "지금 코드가 왜 이렇게 동작하는가"만 담는다.
///
/// [왜 "옆에 스치기만 해도 탑승"되던 버그를 벽+바닥(솔리드)+내부 트리거로 분리해 고쳤나]
/// 트리거 박스 하나뿐이면 정육면체가 옆면을 살짝 스치기만 해도 `OnTriggerEnter`가 발동한다.
/// `CatapultMenuItem.CreateBucket`이 버킷을 두 겹으로 만든다 — 바깥쪽 벽(B/L/R 3면)+바닥은
/// 솔리드(트리거 아님) 콜라이더라 옆으로는 물리적으로 통과할 수 없고, 이 컴포넌트는 그 벽보다
/// 안쪽·바닥보다 위의 **더 작은 내부 트리거 콜라이더**(`Catapult_BucketInner`) 위에 붙어, 정육면체의
/// 콜라이더가 그 내부 공간과 실제로 겹칠 때만 탑승 판정을 낸다. **F(발사 방향 쪽) 벽만 좌표 계산으로
/// 확정해 제거했다** — 발사(분리)는 항상 팔이 restAngle(=0°)에 도달한 순간에 일어나고, 그 순간
/// 발사 방향을 버킷 그룹의 로컬 좌표로 역변환하면 `(Δx=0, Δy=+cos(launchPitch), Δz=+sin(launchPitch))`
/// 가 나온다 — Δz가 양수(F 쪽)라 F 벽에 먼저 걸려 발사 궤적이 막혔다(B는 Δz 부호가 반대라 애초에
/// 안 닿는다). B/L/R은 여전히 "옆에 스치기만 해도 탑승"을 막고, F가 사라진 방향은
/// `IsWithinCentralBoardZone`(아래)이 거리 게이트로 대신 걸러낸다.
///
/// [왜 overlapCount 패턴이 필요한가]
/// 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider) 콜라이더를 함께 가져, 트리거 하나에
/// 대해 한 도형당 Enter/Exit가 여러 번 불릴 수 있다(`DoorSystem/PadTrigger.cs`가 이미 겪은 문제 —
/// 파일은 건드리지 않고 그 카운팅 패턴만 이식했다). 여기서는 "탑승자가 누구인지"를 참조(Rigidbody)로
/// 이미 구분하므로 카운트 자체가 판정을 좌우하진 않지만, 같은 탑승자의 두 번째 콜라이더가 먼저
/// Enter/Exit해도 중복 탑승 로그나 상태 흔들림이 나지 않도록 카운트로 흡수한다.
///
/// [왜 탑승 중 부모화 + isKinematic인가 — Exit로 풀지 않고 Fire로만 푼다]
/// `ThreadPinPlacer`가 "벽 부착은 isKinematic"을 택한 것과 같은 이유다: mover.enabled를 끄면
/// PlayerControlSwitcher 로스터에서 빠져 Tab 순환이 깨진다. isKinematic은 mover의 velocity 대입을
/// 전부 무시하므로 mover를 끌 필요가 없다 — 로스터가 그대로 유지된 채 탑승자가 버킷에 완전히
/// 종속된다. 부모화까지 하는 이유는 팔이 당김 애니메이션으로 회전할 때 탑승자가 그 회전을 그대로
/// 따라가야(팔과 함께 움직여야) 하기 때문이다. 이 상태는 오직 `ConsumeOccupant()`(발사)로만 풀린다 —
/// 트리거 Exit는 절대 탑승을 해제하지 않는다("탑승 중에는 버킷에 붙들려 있어 스스로 걸어 나갈 수
/// 없다", PRD §2 단계 A).
///
/// [발사 후 조작 복구 — DreamThreadController의 Launching 패턴 재사용]
/// 발사된 정육면체는 착지할 때까지 조작 불가(`PlayerMover.ExternallyDriven = true`)이며, 착지
/// (`PlayerShapeController.IsGrounded()`) 또는 `launchReenableTimeout` 경과 시 자동으로 조작이
/// 복구된다. 이 감시는 Fire를 호출하는 `CatapultArm`이 아니라 여기(버킷)가 맡는다 — 버킷이 이미
/// 탑승자의 mover/shape 참조를 들고 있어 책임이 자연스럽게 이어진다.
///
/// [C키 탑승 — 즉시 텔레포트, 글라이드 아님]
/// 걸어서/뛰어서 들어가는 기존 방식(위 벽+바닥+내부 트리거)과 별개로, 정육면체를 조작 중인 플레이어가
/// `boardApproachRange` 안에서 C를 누르면 자동으로 탑승된다 — 투석기가 커지며 버킷 위치도 높아져
/// 점프만으로 닿지 않게 된 것을 보완하는 의도된 경로다(PRD §2 단계 A, §6·§7 — 사용자가 명시적으로
/// 받아들인 트레이드오프). 판정은 `CatapultPullAnchor.connectRange`와 같은 발상의 거리 게이트다 —
/// 물리적으로 내부 트리거와 겹칠 필요가 없다. 여러 투석기의 범위가 겹치면 가장 가까운 버킷에만
/// 탑승한다(`IsNearestBucket`, `CatapultLoadController.IsNearestCatapult`와 같은 이유 — C는 전역
/// 키라 겹치는 위치에서 여러 버킷이 동시에 같은 플레이어를 붙잡는 사고를 막는다). 탑승은 정육면체
/// 전용이다(구·정사면체가 C를 누르면 무시).
/// **왜 짧은 글라이드가 아니라 즉시 텔레포트인가.** 예전엔 C를 누른 자리에서 버킷까지 짧게(0.4초)
/// `Rigidbody.MovePosition`으로 보간하며 그동안 `isKinematic=true`로 고정했는데, 이 "움직이면서
/// 동시에 고정된" 상태가 다이나믹 투석기의 솔리드 지오메트리(팔/받침대/버킷 벽)와 물리적으로
/// 겹치기 쉬웠다 — 겹칠 때마다 킨네마틱 바디가 그 자리를 "정답"이라며 억지로 점유하려 들며 다이나믹
/// 투석기를 밀어냈고, 그 반작용이 누적돼 탑승 순간 폭발하듯 튕겨 나갔다. 지금은 C를 누른 프레임에
/// `Rigidbody.position`을 즉시(단일 프레임) 버킷 내부 트리거 위치로 텔레포트한 뒤 곧바로 `Board()`
/// (걸어서 탑승하는 경로와 동일한 함수)를 호출한다 — 텔레포트는 물리 스텝 사이에 일어나므로 다이나믹
/// 투석기와 겹치는 프레임 자체가 존재하지 않는다. 텔레포트 뒤에도 `OnTriggerEnter`가 다시 호출될 수
/// 있지만, `Board()`가 이미 `occupantBody`를 채워 뒀으므로 뒤이어 도착하는 이벤트는 중복 콜라이더
/// 흡수 경로를 타 안전하다(아래 `OnTriggerEnter` 참고).
///
/// [Board() — 탑승자를 armPivot에 부모화하고 위치/회전을 매번 깨끗하게 리셋하는 이유]
/// **부모 대상은 이 컴포넌트 자신(`Catapult_BucketInner`)이 아니라 `armPivot`이다.** 내부 트리거는
/// 트리거 콜라이더 크기를 겸하려고 비균일 스케일(`BucketInnerHalf * 2`, 세 축이 전부 다르다)을
/// 갖는데, Unity `Transform`은 전단(shear)을 표현할 방법이 없어 "회전 + 비균일 스케일" 조합인 부모에
/// `SetParent(..., worldPositionStays: true)`로 부모화하면 스케일 역산이 근사되며 탑승자의 크기/비율이
/// 왜곡된다. armPivot은 로컬 스케일이 항상 `(1,1,1)`인 순수 회전 노드라(균일 스케일은 어떤 회전과
/// 결합해도 전단을 만들지 않는다) 이 왜곡이 수학적으로 원천 차단된다 — armPivot은 이미
/// `CatapultArm`이 당김 각도를 회전시키는 그 노드라 "탑승자가 팔 당김에 따라 함께 움직인다"는 요구도
/// 그대로 유지된다.
/// **부모화 직후 위치는 바닥 근처(`ComputeBoardTargetPosition()`), 회전은 항상 identity로 스냅한다.**
/// 위치를 스냅하지 않으면 트리거에 닿은 자리 그대로(벽 근처·구석) 고정될 수 있어 항상 정해진 지점으로
/// 모은다. 회전을 스냅하지 않으면 더 미묘한 문제가 생긴다 — 정육면체는 `FreezeRotation`이라 발사
/// 순간의(팔이 당겨진 각도의) 월드 회전을 착지 후에도 그대로 물고 있는데, 다음 탑승에서 부모화가
/// 그 오염된 월드 회전을 보존하려 armPivot 기준 임의의 로컬 오프셋으로 역산해 버려 "탑승 후 팔
/// 각도를 따라가지 않는" 것처럼 보인다. 매 탑승마다 `localRotation = Quaternion.identity`로
/// 강제해 이전 발사의 회전이 다음 탑승으로 누적/전이될 여지 자체를 없앤다.
/// **탑승 중엔 `Rigidbody.interpolation`도 `None`으로 끈다(발사 시 원래 값으로 복원).** 킨네마틱
/// Rigidbody가 `Interpolate` 모드인 채로 "부모(armPivot) Transform이 회전해서 간접적으로 끌려가는"
/// 방식으로 움직이면, Unity의 보간 버퍼가 그 변화를 제대로 못 쫓아가 렌더링용 Transform이 실제
/// 물리 자세와 어긋나거나 지연될 수 있다(Unity 커뮤니티에 알려진 함정 — `CatapultSteerHandle`의
/// 도킹된 구도 같은 이유로 같은 처리를 한다). 탑승 중엔 어차피 킨네마틱+부모화로 매 프레임 정확한
/// Transform이 결정되므로 보간이 필요 없고, 발사 후(비킨네마틱, 실제 물리로 날아가는 동안)는 다시
/// 켜야 시각적 떨림이 없다.
/// `occupantFloorClearance`(기본 0.08)는 정육면체를 버킷 정중앙이 아니라 바닥면 위로 이만큼 띄운
/// 지점에 놓는다 — 정중앙에 놓으면 위아래 여유 공간이 커 공중에 붕 떠 있는 것처럼 보이는 문제를
/// 완화하려는 조정이다.
///
/// [탑승 각도 게이트(`boardMinArmAngle`)와 중앙 탑승 구역(`centralZoneFraction`)]
/// `CatapultArm.CurrentAngle`이 `boardMinArmAngle`(기본 80°) 이상일 때만 탑승할 수 있다 —
/// 정사면체가 어느 정도 당겨 놔야 정육면체가 탑승할 수 있는 협동 조건이다. `OnTriggerEnter`뿐
/// 아니라 `OnTriggerStay`도 같은 게이트를 확인한다 — Enter는 진입 순간에만 한 번 발동해, 각도
/// 미달 상태로 먼저 들어와 대기하다가 나중에 각도를 넘기는 시나리오를 놓치기 때문이다(C키 경로는
/// `HandleBoardInput` 자체가 `Update()`에서 매 프레임 폴링돼 별도 재시도가 필요 없다). `arm`을
/// 찾지 못하면(수동 조립 등) 게이트를 걸지 않는다 — 이 파일의 다른 폴백들과 같은 방어 원칙이다.
/// `IsWithinCentralBoardZone`은 F 벽이 사라진 방향에서 "가장자리를 스치기만 해도 탑승"되는 것을
/// 막는 거리 게이트다 — 진입 위치를 이 트리거 자신의 로컬 좌표로 변환해 절반 크기의
/// `centralZoneFraction`(기본 0.8)배 이내일 때만 통과한다(Y는 검사하지 않는다 — 위에서 뛰어드는
/// 높이 방향은 가장자리 스침과 무관). B/L/R 쪽은 벽이 이미 물리적으로 막아 이 게이트가 사실상 항상
/// 통과하는 무해한 추가 검사고, F 쪽만 실질적인 유일한 방어선이다. 콜라이더 크기 자체는 줄이지
/// 않았다 — `ComputeBoardTargetPosition()`이 같은 콜라이더 크기로 바닥 위치를 계산하므로, 판정
/// 함수만 따로 둬 "탑승이 유효한 범위"와 "콜라이더가 반응하는 범위"를 분리했다. 세 진입점
/// (`OnTriggerEnter`/`OnTriggerStay`/`HandleBoardInput`) 모두 같은 함수를 재사용한다.
///
/// [ScalePad 연동 — 커지면 탑승 차단, 작아지면(가벼워지면) 더 멀리]
/// ScalingSystem 파일은 건드리지 않는다 — `PlayerShapeController.ToggleScale`이 매 `FixedUpdate`마다
/// `rb.mass = stats.mass * (localScale.x / initialScale.x)`로 이미 현재 스케일 비율을 `rb.mass`에
/// 반영해 두므로, `rb.mass / identity.stats.mass`를 다시 계산하면 별도 접근자 없이도 "지금
/// 커졌는지/작아졌는지"를 정확히 알 수 있다(1.0=Normal, growMultiplier 기본값 기준 2.0=Grown,
/// shrinkMultiplier 기본값 기준 0.5=Shrunk). 무게(`PlayerWeight.Of`)가 아니라 이 스케일 비율을 직접
/// 쓰는 이유 — 여기서 막으려는 건 "물리적으로 버킷에 안 맞는 크기"이지 무게로 버티는 문제(실·구름
/// 등)가 아니고, `PlayerWeight.Of`는 중력 배율까지 섞어 굳이 필요 없는 변수를 끌어온다.
/// `heavyBoardBlockScaleRatio`(기본 1.2 — Normal 1.0/Shrunk 0.5는 통과, Grown 2.0은 차단) 이상이면
/// 탑승을 거부한다. 발사 속도 배율은 `CatapultArm.Fire()`가 `bucket.OccupantBody`로 탑승자를 미리
/// 들여다봐 계산한다(자세한 근거는 `CatapultArm.cs` 상단 참고) — 탑승 차단 덕분에 실전에서 스케일
/// 비율이 1을 넘는 경우가 없어 항상 "가벼울수록 더 빨리" 방향으로만 작동한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CatapultBucket : MonoBehaviour
{
    [Header("발사 후 조작 복구")]
    [Tooltip("착지를 못 잡아도 이 시간(초) 안에 강제로 조작을 복구한다. " +
             "DreamThreadSystem의 launchReenableTimeout과 동일한 6초(PRD 확정).")]
    public float launchReenableTimeout = 6f;

    [Header("C키 탑승 — 즉시 텔레포트")]
    [Tooltip("정육면체를 조작 중인 플레이어가 이 거리 안에서 C를 누르면 버킷까지 즉시 텔레포트해 " +
             "탑승한다(정육면체 전용). CatapultPullAnchor.connectRange와 같은 발상의 거리 게이트다. " +
             "3배 확대 후 버킷이 지상에서 약 5.7유닛(post-scale) 위에 있어 넉넉하게 잡았다 — " +
             "씬 튜닝 전제 [TBD, 임시값].")]
    public float boardApproachRange = 10f;

    [Header("탑승 각도 게이트 [TBD, 임시값]")]
    [Tooltip("팔의 현재 당김 각도(CatapultArm.CurrentAngle)가 이 값 이상일 때만 정육면체가 탑승할 " +
             "수 있다(정사면체가 어느 정도 당겨 놔야 정육면체가 탑승할 수 있는 협동 게이트, 사용자 " +
             "요청). CatapultArm을 찾지 못하면(수동 조립 등) 게이트를 걸지 않는다(과거 동작으로 " +
             "안전 폴백) — armPivot을 못 찾을 때 mountParent가 this.transform으로 폴백하는 것과 " +
             "같은 방어 원칙이다.")]
    public float boardMinArmAngle = 80f;

    [Header("탑승 유효 범위 — 벽 제거를 보완하는 거리 게이트")]
    [Tooltip("내부 트리거 콜라이더 절반 크기(half)의 이 비율 이내로 들어온 경우에만 탑승을 허용한다" +
             "(가장자리를 스치는 것만으로는 탑승되지 않게 하는 목적 — 벽이 하던 일을 대신한다). " +
             "1.0에 가까울수록 가장자리까지 허용, 0에 가까울수록 정중앙만 허용.")]
    [Range(0.1f, 1f)]
    public float centralZoneFraction = 0.8f;

    [Header("탑승 위치 — 버킷 정중앙 대신 바닥 근처로 [TBD, 임시값]")]
    [Tooltip("탑승 시 정육면체를 내부 트리거 '정중앙'이 아니라 '바닥면 위로 이만큼(월드 유닛) 띄운 " +
             "지점'에 놓는다 — 정중앙에 놓으면 위아래 여유 공간이 커 공중에 붕 떠 있는 것처럼 보이는 " +
             "문제를 완화한다(클래스 상단 'Board()' 주석 참고). 정육면체 반높이(0.5, 고정)에 이 값을 " +
             "더한 높이가 목표다. 0에 가까우면 바닥에 파묻히고, 너무 크면 예전(정중앙)과 다시 " +
             "비슷해진다 — 감각적 기본값.")]
    public float occupantFloorClearance = 0.08f;

    [Header("ScalePad 연동 — 커지면 탑승 차단 [TBD, 임시값]")]
    [Tooltip("탑승 시도 중인 정육면체의 rb.mass / PlayerShapeIdentity.stats.mass 비율이 이 값 이상이면 " +
             "탑승을 거부한다(클래스 상단 'ScalePad 연동' 주석 참고). growMultiplier/shrinkMultiplier " +
             "기본값(2.0/0.5) 기준으로 Normal(1.0)·Shrunk(0.5)는 통과, Grown(2.0)만 차단하도록 사이값 " +
             "1.2로 잡았다 — 감각적 기본값.")]
    public float heavyBoardBlockScaleRatio = 1.2f;

    [Header("미니 투석기 전용 — 축소 상태만 탑승 허용 [TBD, 임시값, 2026-08-31 신규]")]
    [Tooltip("true면 위 스케일 비율이 shrunkBoardMaxScaleRatio 이하(=Shrunk 상태)일 때만 탑승을 " +
             "허용한다 — 버킷 캐비티가 축소 정육면체 기준으로 작게 지어진 미니 투석기에서, 정상 " +
             "크기 정육면체가 캐비티에 물리적으로 파묻혀 탑승하는 것을 막는다(정상 크기는 이미 " +
             "물리 크기만으로도 대개 걸러지지만, 억지로 밀어 넣으면 벽을 뚫고 낄 수 있어 이 코드 " +
             "게이트로 확실히 막는다). 기본값 false — 기존 투석기(손수레형·Sling)는 전혀 영향받지 " +
             "않는다. `MiniCatapultMenuItem`이 미니 투석기의 버킷에서만 true로 세팅한다.")]
    public bool requireShrunkOccupant = false;

    [Tooltip("`requireShrunkOccupant`가 켜졌을 때 '축소 상태'로 인정하는 상한 — shrinkMultiplier " +
             "기본값(0.5)과 Normal(1.0)의 중간값으로 잡았다(위 heavyBoardBlockScaleRatio=1.2가 " +
             "Normal과 Grown 사이에 있는 것과 같은 방식).")]
    public float shrunkBoardMaxScaleRatio = 0.75f;

    // PlayerObjectMenuItem.cs가 정육면체에 쓰는 BoxCollider.size = Vector3.one(1×1×1, 이 기믹의 Scale과
    // 무관하게 고정) 기준 반높이 — 이 프로젝트에서 바뀔 여지가 거의 없어 하드코딩한다.
    private const float OccupantHalfHeight = 0.5f;

    private int overlapCount;
    private Rigidbody occupantBody;
    private PlayerMover occupantMover;
    private PlayerShapeController occupantShapeController;
    private Transform occupantOriginalParent;
    // 탑승 중 임시로 끄는 보간 모드를 발사 시 되돌리기 위한 원래값 저장.
    // occupantOriginalParent와 정확히 같은 패턴(Board()에서 저장, ConsumeOccupant()에서 복원).
    private RigidbodyInterpolation occupantOriginalInterpolation;

    private bool launching;
    private PlayerMover launchingMover;
    private PlayerShapeController launchingShape;
    private float launchTimer;

    // 탑승자를 부모화할 대상. Catapult_BucketInner(this.transform, 비균일 스케일) 대신
    // armPivot(순수 회전, 스케일 항상 (1,1,1))을 쓴다(클래스 상단 "Board()" 주석 참고). null이면
    // (수동 조립 등으로 CatapultArm을 못 찾으면) this.transform으로 대체해 예전 동작으로 안전하게
    // 폴백한다.
    private Transform mountParent;

    // 각도 게이트가 읽을 CatapultArm 참조. mountParent와 같은 GetComponentInParent 호출을 두 번
    // 하지 않도록 이 하나를 캐시해 둘 다에 쓴다(YAGNI — 새 조회를 추가하지 않는다).
    private CatapultArm arm;

    /// <summary>지금 탑승자가 있는지(빈 발사 판정 등 외부에서 조회용).</summary>
    public bool HasOccupant => occupantBody != null;

    /// <summary>지금 탑승 중인 Rigidbody(없으면 null) — ScalePad 연동. `CatapultArm.Fire()`가
    /// `ConsumeOccupant()`로 실제 분리하기 전에 발사 속도 계산용으로 미리 들여다본다(소비하지 않는
    /// 읽기 전용 조회, 클래스 상단 "ScalePad 연동" 주석 참고).</summary>
    public Rigidbody OccupantBody => occupantBody;

    void Awake()
    {
        arm = GetComponentInParent<CatapultArm>();
        mountParent = arm != null ? arm.armPivot : null;
    }

    // CatapultArm을 못 찾았으면(수동 조립 등) 게이트를 걸지 않는다(과거 동작 폴백).
    private bool ArmAngleAllowsBoard() => arm == null || arm.CurrentAngle >= boardMinArmAngle;

    // ScalePad 연동 — 커진 정육면체는 탑승 자체를 거부한다(클래스 상단 "ScalePad 연동" 주석
    // 참고). stats를 못 찾으면(수동 조립 등) 게이트를 걸지 않는다 — 이 파일의 다른 게이트들과 같은
    // 방어적 폴백 원칙.
    private bool IsTooHeavyToBoard(Rigidbody body, PlayerShapeIdentity identity)
    {
        if (identity == null || identity.stats == null || identity.stats.mass <= 0f) return false;
        return (body.mass / identity.stats.mass) >= heavyBoardBlockScaleRatio;
    }

    // 미니 투석기 전용 게이트(2026-08-31 신규) — requireShrunkOccupant가 꺼져 있으면(기존
    // 투석기) 항상 통과한다. stats를 못 찾으면(수동 조립 등) 게이트를 걸지 않는다 — 이 파일의
    // 다른 게이트들과 같은 방어적 폴백 원칙.
    private bool IsNotShrunkEnoughToBoard(Rigidbody body, PlayerShapeIdentity identity)
    {
        if (!requireShrunkOccupant) return false;
        if (identity == null || identity.stats == null || identity.stats.mass <= 0f) return false;
        return (body.mass / identity.stats.mass) > shrunkBoardMaxScaleRatio;
    }

    // 벽이 사라진 대신 이 거리 게이트가 "가장자리를 스치기만 해도 탑승"을 막는다(클래스 상단
    // "탑승 각도 게이트와 중앙 탑승 구역" 주석 참고). world 좌표를 이 트리거(Catapult_BucketInner)
    // 자신의 로컬 좌표로 변환해, 콜라이더 절반 크기(box.size/2)의 centralZoneFraction(0.8)배
    // 이내일 때만 true를 반환한다 — Y는 가장자리 스침 방지와 무관해 검사하지 않는다. 콜라이더 크기
    // 자체는 그대로 둔다(ComputeBoardTargetPosition이 같은 box.size를 바닥 계산에 그대로 쓰므로
    // 여기서 축소하면 안 된다) — 판정만 좁힌다.
    private bool IsWithinCentralBoardZone(Vector3 worldPos)
    {
        BoxCollider box = GetComponent<Collider>() as BoxCollider;
        if (box == null) return true; // 방어적 폴백 — BoxCollider가 아니면 게이트를 걸지 않는다.

        Vector3 local = transform.InverseTransformPoint(worldPos);
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x * centralZoneFraction
            && Mathf.Abs(local.z) <= half.z * centralZoneFraction;
    }

    void Reset()
    {
        Collider c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    void Update()
    {
        HandleBoardInput();

        if (!launching) return;

        launchTimer -= Time.deltaTime;
        bool landed = launchingShape != null && launchingShape.IsGrounded();
        if (landed || launchTimer <= 0f)
        {
            if (launchingMover != null) launchingMover.ExternallyDriven = false;
            launching = false;
            launchingMover = null;
            launchingShape = null;
        }
    }

    // 정육면체 전용 C 탑승 입력 진입점. CatapultLoadController.HandleInput과 같은 전역 키 처리
    // 관례(기믹 쪽 컴포넌트가 직접 키 입력을 읽는다)를 따른다. 즉시 텔레포트 + Board()로 탑승이
    // 이 메서드 하나로 완결된다(클래스 상단 "C키 탑승" 주석 참고).
    private void HandleBoardInput()
    {
        if (!Input.GetKeyDown(KeyCode.C)) return;
        if (occupantBody != null) return; // 이미 탑승 중

        PlayerMover mover = FindControlledPlayer();
        if (mover == null) return;

        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Cube) return; // 정육면체 전용(역할 게이트)

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        // ScalePad 연동 — 커진 상태면 탑승 자체를 거부한다(클래스 상단 "ScalePad 연동" 주석 참고).
        if (IsTooHeavyToBoard(body, identity))
        {
            Debug.Log("[Catapult] 커진 상태로는 버킷에 탑승할 수 없습니다.");
            return;
        }
        if (IsNotShrunkEnoughToBoard(body, identity))
        {
            Debug.Log("[Catapult] 이 투석기는 축소(Shrunk) 상태에서만 탑승할 수 있습니다.");
            return;
        }

        // 각도 게이트. C키 경로는 매 프레임 입력을 폴링하므로(이 메서드 자체가 Update()에서
        // 매 프레임 불린다) 각도가 나중에 넘어가면 자연히 재시도된다 — 걸어서 들어오는 경로처럼
        // 별도 OnTriggerStay 재확인이 필요 없다.
        if (!ArmAngleAllowsBoard())
        {
            Debug.Log("[Catapult] 아직 팔이 충분히 당겨지지 않아 탑승할 수 없습니다.");
            return;
        }

        float myDistance = Vector3.Distance(body.position, transform.position);
        if (myDistance > boardApproachRange) return;

        // 범위가 겹치는 다른 투석기 버킷이 여럿이면 가장 가까운 곳에만 탑승한다 —
        // CatapultLoadController.IsNearestCatapult와 같은 이유(C는 전역 키).
        if (!IsNearestBucket(body.position, myDistance)) return;

        // 방어적 일관성 차원에서 걸어서/OnTriggerStay 경로와 같은 게이트를 통과한다. 텔레포트
        // 목적지가 항상 이 트리거의 정중앙(transform.position)이라 사실상 항상 통과하지만(0.8배
        // 범위는 정중앙을 항상 포함한다), 세 진입점이 같은 함수를 공유한다는 것을 보장한다.
        if (!IsWithinCentralBoardZone(transform.position)) return;

        // 단일 프레임 텔레포트 — 킨네마틱으로 "움직이는" 구간 자체를 만들지 않는다(클래스 상단 주석
        // 참고). 곧바로 Board()를 호출해 걸어서 들어온 경로와 동일한 최종 상태로 수렴시킨다.
        body.position = transform.position;
        Board(identity, body);

        Debug.Log("[Catapult] 정육면체가 C로 버킷에 즉시 탑승했습니다.");
    }

    private bool IsNearestBucket(Vector3 playerPosition, float myDistance)
    {
        foreach (CatapultBucket other in FindObjectsOfType<CatapultBucket>())
        {
            if (other == this) continue;

            float otherDistance = Vector3.Distance(playerPosition, other.transform.position);
            if (otherDistance > other.boardApproachRange) continue;

            if (otherDistance < myDistance) return false;
            if (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID()) return false;
        }
        return true;
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled) return m;
        return null;
    }

    void OnTriggerEnter(Collider other)
    {
        // 즉시 텔레포트 뒤에도 이 이벤트가 뒤이어 도착할 수 있지만, occupantBody가 이미 Board()로
        // 채워져 있어 아래 "occupantBody == body" 분기가 안전하게 흡수한다(클래스 상단 주석 참고).
        if (!other.CompareTag("Player")) return;
        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null) return;

        Rigidbody body = identity.GetComponent<Rigidbody>();
        if (body == null) return;

        if (occupantBody == body)
        {
            overlapCount++; // 같은 탑승자의 이중 콜라이더 — 이미 탑승 처리됨
            return;
        }
        if (occupantBody != null) return; // 버킷 하나에 탑승자는 한 번에 한 명뿐

        // 정육면체만 탑승 판정을 받는다(역할 게이트, PlayerWeight 미사용).
        if (identity.Kind != PlayerShapeStats.ShapeKind.Cube) return;

        // ScalePad 연동 — 커진 상태면 탑승 자체를 거부한다(클래스 상단 "ScalePad 연동" 주석 참고).
        if (IsTooHeavyToBoard(body, identity)) return;
        if (IsNotShrunkEnoughToBoard(body, identity)) return; // 미니 투석기 전용 게이트(위 필드 주석 참고).

        // 각도 게이트. 미달이면 지금은 탑승시키지 않는다(overlapCount도 건드리지 않는다 — 탑승
        // 자체가 아직 성립하지 않았으므로). 정육면체가 각도 미달 상태로 먼저 들어와 트리거 안에
        // 머무는 경우는 OnTriggerStay가 매 프레임 재확인해, 각도가 나중에 넘어가는 순간 자동으로
        // 탑승시킨다.
        if (!ArmAngleAllowsBoard()) return;

        // 벽이 사라진 대신, 가장자리를 스치기만 한 진입은 걸러낸다(클래스 상단 주석 참고).
        if (!IsWithinCentralBoardZone(body.position)) return;

        overlapCount = 1;
        Board(identity, body);
    }

    // 각도 게이트 재시도. OnTriggerEnter는 진입 순간에만 발동해, 정육면체가 각도 미달 상태에서
    // 먼저 들어와 대기하다가 정사면체가 나중에 당김 각도를 넘기는 시나리오를 놓친다 — 트리거 안에
    // 머무는 동안 매 프레임 재확인해 각도를 넘는 순간 자동으로 탑승되게 한다.
    void OnTriggerStay(Collider other)
    {
        if (occupantBody != null) return; // 이미 탑승 중이면 재확인 불필요(제일 싼 체크를 먼저).
        if (!other.CompareTag("Player")) return;
        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Cube) return;

        Rigidbody body = identity.GetComponent<Rigidbody>();
        if (body == null) return;

        // ScalePad 연동 — 커진 상태면 탑승 자체를 거부한다(클래스 상단 "ScalePad 연동" 주석 참고).
        if (IsTooHeavyToBoard(body, identity)) return;
        if (IsNotShrunkEnoughToBoard(body, identity)) return; // 미니 투석기 전용 게이트(위 필드 주석 참고).

        if (!ArmAngleAllowsBoard()) return;

        // 가장자리에 머무르며 재시도하는 것도 같은 기준으로 걸러낸다.
        if (!IsWithinCentralBoardZone(body.position)) return;

        overlapCount = 1;
        Board(identity, body);
    }

    void OnTriggerExit(Collider other)
    {
        if (occupantBody == null) return;
        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null) return;
        if (identity.GetComponent<Rigidbody>() != occupantBody) return;

        overlapCount = Mathf.Max(0, overlapCount - 1);
        // 탑승 중엔 부모화+isKinematic으로 버킷에 종속돼 스스로 나갈 수 없다 — 발사(ConsumeOccupant)
        // 만이 해제한다. overlapCount가 0이 돼도 여기서 강제로 내리지 않는다(의도된 동작, PRD §2).
    }

    // 탑승 목표 위치를 내부 트리거의 "기하학적 정중앙" 대신 "바닥면 위 occupantFloorClearance만큼
    // 띄운 지점"으로 계산한다(클래스 상단 "Board()" 주석 참고). 이 컴포넌트가 붙은 GameObject
    // (Catapult_BucketInner) 자신이 곧 내부 트리거이므로, 이 오브젝트 자신의 로컬(비스케일)
    // 좌표계에서 바닥 면(box.center.y - box.size.y/2)을 구한 뒤, 원하는 월드 단위 여유(정육면체
    // 반높이 + clearance)를 lossyScale.y로 나눠 로컬 오프셋으로 환산한다 — TransformPoint가
    // 회전/스케일(그룹의 -90°X 회전+축소 등)을 전부 반영해 최종 월드 위치를 계산해 준다.
    private Vector3 ComputeBoardTargetPosition()
    {
        BoxCollider box = GetComponent<Collider>() as BoxCollider;
        if (box == null) return transform.position; // 방어적 폴백(과거 동작) — BoxCollider가 아니면 정중앙을 유지한다.

        float floorLocalY = box.center.y - box.size.y * 0.5f;
        float worldClearance = OccupantHalfHeight + occupantFloorClearance;
        float localClearance = worldClearance / Mathf.Max(0.0001f, transform.lossyScale.y);
        float targetLocalY = floorLocalY + localClearance;

        return transform.TransformPoint(new Vector3(box.center.x, targetLocalY, box.center.z));
    }

    private void Board(PlayerShapeIdentity identity, Rigidbody body)
    {
        occupantBody = body;
        occupantMover = identity.GetComponent<PlayerMover>();
        occupantShapeController = identity.GetComponent<PlayerShapeController>();
        occupantOriginalParent = body.transform.parent;
        // 탑승 중엔 보간을 끈다(클래스 상단 "Board()" 주석 참고). 발사(ConsumeOccupant)에서 원래
        // 값으로 되돌린다.
        occupantOriginalInterpolation = body.interpolation;
        body.interpolation = RigidbodyInterpolation.None;

        body.isKinematic = true;
        // armPivot(순수 회전, 비균일 스케일 없음)에 부모화한다. 이 트리거(transform) 자신에
        // 부모화하면 그 비균일 localScale(BucketInnerHalf*2)이 회전과 얽혀 탑승자의 스케일을
        // 왜곡시켰다(클래스 상단 "Board()" 주석 참고).
        body.transform.SetParent(mountParent != null ? mountParent : transform, true);
        // 항상 내부 트리거 안의 정해진 지점으로 스냅한다 — 걸어서 들어온 경우 트리거에 닿은 자리
        // 그대로 고정되던 것(구석/벽 근처일 수 있음)을 방지한다. C키 경로는 이미 비슷한 위치로
        // 텔레포트한 뒤라 무해한 재확인이다. "정중앙"이 아니라 "바닥면 위 occupantFloorClearance만큼
        // 띄운 지점"이다(아래 ComputeBoardTargetPosition, 클래스 상단 "Board()" 주석 참고).
        body.transform.position = ComputeBoardTargetPosition();
        // 회전도 armPivot 로컬 기준 identity로 강제 리셋한다 — 매 탑승마다 "똑바로 선" 상태로 항상
        // 깨끗하게 시작해, 이전 발사의 왜곡된 월드 회전이 다음 탑승에 누적/전이되지 않게 한다(클래스
        // 상단 "Board()" 주석 참고).
        body.transform.localRotation = Quaternion.identity;

        Debug.Log("[Catapult] 정육면체가 버킷에 탑승했습니다.");
    }

    /// <summary>발사 시점에 CatapultArm이 호출한다. 탑승자를 버킷에서 풀어 되돌려주고(부모 해제 +
    /// isKinematic 해제), 착지까지 조작 불가 상태(ExternallyDriven)로 전환한 뒤 그 Rigidbody를
    /// 반환한다. 탑승자가 없으면 null(빈 발사, 허용된 동작).</summary>
    public Rigidbody ConsumeOccupant()
    {
        if (occupantBody == null) return null;

        Rigidbody body = occupantBody;
        PlayerMover mover = occupantMover;
        PlayerShapeController shape = occupantShapeController;

        body.transform.SetParent(occupantOriginalParent, true);
        body.isKinematic = false;
        // 탑승 중 꺼뒀던 보간을 발사 즉시 원래 값으로 되돌린다(발사 후에는 다시 비킨네마틱 실제
        // 물리로 날아가므로 시각적 떨림 방지가 필요하다, 클래스 상단 주석 참고).
        body.interpolation = occupantOriginalInterpolation;
        if (mover != null) mover.ExternallyDriven = true;

        launching = true;
        launchingMover = mover;
        launchingShape = shape;
        launchTimer = launchReenableTimeout;

        occupantBody = null;
        occupantMover = null;
        occupantShapeController = null;
        occupantOriginalParent = null;
        overlapCount = 0;

        return body;
    }
}
