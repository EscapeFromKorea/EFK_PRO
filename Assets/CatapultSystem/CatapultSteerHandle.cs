using UnityEngine;

/// <summary>
/// 투석기의 조향 손잡이(구 전용) — 22차 개편(2026-08-06)으로 도입된 "도킹 후 직접 조작" 방식.
/// 이 컴포넌트는 손잡이(그립/고리) 오브젝트가 아니라 **투석기 루트 자신**에 붙는다
/// (`CatapultLoadController`/`Rigidbody`와 같은 자리) — 물리 충돌이나 트리거를 하나도 다루지
/// 않고, C키 도킹 상태머신과 조향 회전만 담당하는 순수 스크립트다.
///
/// [22차 개편 — 왜 21차(순수 충돌 기반)를 전면 폐기했나]
/// 9~20차(12라운드)에 걸친 SpringJoint 조향 실패 끝에 21차는 손잡이를 진짜 솔리드 블록으로 바꿔
/// 표준 강체 충돌이 힘을 전달하게 하는 방식으로 전환했다 — 그러나 21차는 씬에서 한 번도
/// 테스트되지 못한 채 사용자가 방향을 완전히 바꿨다: 조준을 "부딪혀서 미는/당기는" 물리 반응이
/// 아니라 **"구가 손잡이에 도킹해 투석기를 직접 조작하는"** 방식으로 하기로 확정했다. 이 방식은
/// 이 저장소가 이미 신뢰하는 세 패턴을 그대로 조합한다 — `ThreadPinPlacer`의 벽 부착
/// (`isKinematic=true`로 완전 고정, 조인트 없음), `CatapultBucket`의 C키 탑승(거리 기반 판정,
/// 물리적 겹침 불필요), 5차 개편의 `Rigidbody.MoveRotation`(논카인매틱 Rigidbody 위에 얹는 스크립트
/// 회전). SpringJoint의 damper/spring 튜닝, `maxDistance` 재검산, `enableCollision`,
/// 트리거 부착/해제 상태 관리, 콜라이더 충돌 임펄스 튜닝이 전부 필요 없어져 코드가 다시 한번
/// 극적으로 단순해졌다 — 9~21차의 실패 이력 자체는 `CatapultSystem/CLAUDE.md`에 그대로 남겨 뒀다
/// (같은 접근을 다시 시도하지 않기 위한 기록).
///
/// [연결/해제 — 거리 기반 C키, 트리거/충돌 아님]
/// `CatapultLoadController.TryConnect()`/`CatapultBucket`의 C키 탑승과 정확히 같은 패턴 —
/// `Vector3.Distance(sphereBody.position, dockAnchor.position) &lt;= dockRange`를 C가 눌린 순간에만
/// 확인한다. `OnTriggerEnter` 자체가 필요 없어, 9~21차가 겪은 모든 물리/충돌 함정을 원천적으로
/// 비껴간다.
///
/// [역할 게이트 — 구 전용]
/// 이 파일이 조향 전반에서 계속 써 온 `Kind == PlayerShapeStats.ShapeKind.Sphere` 게이트를 그대로
/// 쓴다. 도킹 범위 안에서 다른 도형이 C를 눌러도 조용히 반환한다(로그만 남기고 아무 일도 하지
/// 않는다 — `CatapultLoadController`가 정육면체를 거부하는 것과 같은 관례).
///
/// [도킹 시 하는 일]
/// - 구 `Rigidbody.isKinematic = true`(`ThreadPinPlacer`의 벽 부착과 같은 선택 — 조인트도 velocity
///   다툼도 없다).
/// - `dockAnchor`(고리 시각 장식의 중심, 로컬 스케일이 항상 (1,1,1)인 전용 오브젝트)에 부모화해
///   위치/회전을 스냅한다 — 투석기가 조향으로 돌 때 도킹된 구도 그 회전을 그대로 따라가야
///   "손잡이를 쥐고 함께 도는" 느낌이 난다(`CatapultBucket`이 탑승자를 `armPivot`에 부모화해 당김
///   회전을 따라가게 하는 것과 같은 이유). `dockAnchor`의 스케일이 항상 균일(1,1,1)이라
///   `CatapultBucket`이 10차 개편에서 겪은 "비균일 스케일 부모에 부모화하면 전단으로 스케일이
///   왜곡된다"는 함정을 애초에 피한다.
/// - `PlayerMover.ExternallyDriven = true`(`mover.enabled`는 절대 건드리지 않는다 — Tab 로스터를
///   깨는 것은 이 저장소의 하드룰이다, `Assets/CLAUDE.md` 참고).
/// - 구를 "링에 맞게" 확대한다 — `PlayerShapeController.ToggleScale(EScaleState.Grown)`을 재사용한다
///   (`ScalingSystem/Playershapecontroller.cs`, 파일 미수정). 새 스케일 설정 메서드를
///   ScalingSystem에 추가하지 않는다 — 교차 폴더 수정에 해당하고, `growMultiplier`가 이미 public
///   필드라 불필요하다. 호출 직전에 `shapeController.growMultiplier = dockedScaleMultiplier`로
///   확대 배율을 이 컴포넌트가 정하고, 이후 `ToggleScale`을 그대로 호출한다 — 전환 보간
///   (`lerpSpeed`)은 `PlayerShapeController`가 스스로 처리하므로 여기서 새 스무딩 코드가
///   필요 없다.
///
/// [입력 게이트 — 도킹된 플레이어가 조작 대상이 아니면 다른 플레이어의 C를 무시]
/// `DreamThreadController`/`CatapultLoadController`가 이미 쓰는 패턴을 처음부터 반영했다(이
/// 저장소의 2026-07-30 입력 게이트 감사가 지적한 클래스의 버그를 이번엔 처음부터 만들지 않는다).
/// 구가 도킹된 채 Tab으로 다른 플레이어에게 조작권이 넘어간 동안(`dockedMover.IsControlled ==
/// false`), 다른 플레이어가 아무 데서나 C를 눌러도 로그만 남기고 도킹을 풀거나 새로 걸지 않는다.
///
/// [다중 투석기 안전장치 — 가장 가까운 것에만 도킹]
/// `CatapultLoadController.IsNearestCatapult`/`CatapultBucket.IsNearestBucket`과 같은 아이디어를
/// 이 컴포넌트에도 새로 구현했다(공유 헬퍼로 리팩터링하지 않는다 — 이 저장소의 관례상 작은 패턴은
/// 컴포넌트마다 각자 복제한다, `CatapultLoadController.cs` 상단 주석 참고).
///
/// [조향 — 요(Y) 회전만, 스폰 시점 방향 기준 ±steerYawRange]
/// 도킹돼 있는 동안 `Input.GetAxis("Horizontal")`(플레이어가 평소 이동에 쓰는 것과 같은 전역 축 —
/// `ExternallyDriven=true`가 `PlayerMover`의 이 값 처리를 막아 주므로 여기서 재사용해도 안전하다)를
/// 매 프레임 읽어 `Update()`에 캐시하고, 실제 회전 대입은 `FixedUpdate()`에서
/// `Rigidbody.MoveRotation`으로 한다 — 이 파일의 5차 개편이 이미 정립한 이유 그대로다(물리 스텝에
/// 반영되는 값이라 프레임레이트와 무관하게 일관되려면 고정 타임스텝에서 처리해야 한다).
/// `baseYaw`(스폰 시점 요, "링이 있는 방향")는 `Awake()`에서 한 번만 캡처한다. 매 스텝
/// `Mathf.DeltaAngle(baseYaw, candidateYaw)`로 부호 있는 오프셋을 구해 `[-steerYawRange,
/// +steerYawRange]`로 클램프한 뒤 `baseYaw + clampedOffset`을 다시 대입한다 — 원시 오일러 각도를
/// 그대로 `Mathf.Clamp`하면 0°/360° 경계에서 랩어라운드 버그가 나므로 `DeltaAngle`이 반드시
/// 필요하다. 전후 이동은 없다(사용자 확정 — 회전만) — 위치는 기존 루트 Rigidbody 물리(중력/접지)에
/// 완전히 맡긴다.
/// **23차 개편(2026-08-06) — 목표 요는 더 이상 `rootBody.rotation`(물리 시뮬레이션 결과)에서 다시
/// 읽지 않는다.** 22차 코드는 매 스텝 `rootBody.rotation.eulerAngles.y`를 다음 목표의 기준으로 삼았는데,
/// `pendingHorizontal == 0`이어도 이 값은 바퀴 접지 노이즈 등으로 미세하게 계속 드리프트할 수 있고,
/// 스크립트가 그 드리프트를 매 스텝 "새 목표"로 그대로 재확인해 버려 도킹된 채 정지해 있어도 서서히
/// -Y로 계속 회전하는 버그가 났다(드리프트가 교정되지 않고 매 스텝 앞으로 전달됐다). 지금은
/// `dockedYawOffset`(`baseYaw` 기준 오프셋, 항상 `[-steerYawRange, +steerYawRange]`로 클램프된 채
/// 스크립트 자신이 들고 있다)이 유일한 진실이다 — 매 스텝 이 필드에만 입력을 누적하고,
/// `rootBody.rotation`은 다음 계산의 입력으로 두 번 다시 읽지 않는다. `Dock()` 순간에는 그 시점의
/// 실제 요(이전 도킹 세션에서 남은 회전이 있을 수 있다)로 `dockedYawOffset`을 한 번 초기화해 도킹
/// 스냅 때 시각적으로 튀지 않게 한다. 도킹 중에는 매 `FixedUpdate`마다 `rootBody.angularVelocity`도
/// `Vector3.zero`로 강제한다 — `MoveRotation`이 그 프레임의 목표 자세를 정확히 대입해도, 논카인매틱
/// Rigidbody에 남아 있는 시뮬레이션 각속도가 같은 스텝 안에서 다음 프레임의 실제 자세를 살짝 더
/// 밀 수 있기 때문이다(Unity가 `MoveRotation`과 시뮬레이션 적분을 같은 스텝에 함께 적용한다) —
/// 도킹은 회전의 소유권을 스크립트에 완전히 넘기는 것이므로, 물리 엔진이 만든 잔여 토크가 다음
/// 스텝까지 살아남을 여지를 아예 남기지 않는다. `dockedBody != null`일 때만 이 강제 리셋을 하고,
/// 도킹된 사람이 없을 때는 `angularVelocity`를 전혀 건드리지 않는다 — 아무도 조향하지 않는 동안의
/// 기존 물리 거동(충돌·안착 등)은 그대로 유지한다.
///
/// [해제 — 자동 이탈 없음]
/// 도킹된 플레이어가 다시 C를 누르면 링크 시의 모든 단계를 역순으로 되돌린다 —
/// `ToggleScale(Grown)`을 다시 호출해(토글이라 자동으로 Normal로 돌아온다, 새 축소 로직을 추가하지
/// 않는다), `isKinematic = false`, `mover.ExternallyDriven = false`. **자동 "튕겨나가기"는 없다
/// (사용자 명시적 확정)** — 구는 현재(도킹된, 확대된) 위치에서 그대로 다시 조작 가능해질 뿐이고,
/// 걸어/뛰어 나가는 것은 플레이어 몫이다.
///
/// [25차 개편(2026-08-06) — 도킹된 구가 조작 대상이 아닐 때 다른 플레이어의 A/D가 새는 버그 수정]
/// `Update()`가 조향 입력을 `dockedBody != null`일 때만 읽었는데, `Input.GetAxis("Horizontal")`은
/// 전역 축이라 이 조건만으로는 "지금 실제로 도킹된 구를 조작 중인가"를 걸러내지 못했다 — 구가
/// 도킹된 채 Tab으로 다른 플레이어에게 조작권이 넘어가도(`dockedMover.IsControlled == false`)
/// 그 다른 플레이어가 A/D를 누르면 값이 그대로 들어와 투석기가 제멋대로 돌았다(사용자 재현
/// 보고). `HandleDockInput()`의 C키 해제 게이트가 이미 `dockedMover.IsControlled`를 확인하던 것과
/// 똑같은 조건을 조향 입력 읽기에도 추가했다 — 두 입력 경로(C키/좌우 이동)가 이제 정확히 같은
/// "조작 대상 확인" 기준을 공유한다.
/// </summary>
public class CatapultSteerHandle : MonoBehaviour
{
    [Header("도킹 대상 (구 전용)")]
    [Tooltip("도킹 지점 — 고리 시각 장식의 중심(또는 그 안의 빈 자식). CatapultMenuItem이 자동으로 " +
             "연결한다. 로컬 스케일이 항상 (1,1,1)이어야 한다 — 아니면 도킹된 구가 부모화 시 " +
             "전단으로 왜곡된다(CatapultBucket이 10차 개편에서 겪은 함정).")]
    public Transform dockAnchor;

    [Tooltip("투석기 루트의 Rigidbody. 조향 회전(MoveRotation)의 대상이자 baseYaw(스폰 시점 방향)의 " +
             "기준이다. CatapultArm.aimRoot와 같은 방식으로 CatapultMenuItem이 연결한다.")]
    public Rigidbody rootBody;

    [Header("도킹 판정 [TBD, 임시값]")]
    [Tooltip("구가 C를 눌러 도킹을 시도할 수 있는 dockAnchor 기준 최대 거리.")]
    public float dockRange = 1.2f;

    [Header("도킹 시 크기 확대 [TBD, 임시값]")]
    [Tooltip("도킹한 구를 '링에 맞게' 키우는 배율. PlayerShapeController.growMultiplier에 이 값을 " +
             "대입한 뒤 ToggleScale(Grown)을 호출한다 — growMultiplier는 ScalePad 등 다른 기믹과도 " +
             "공유하는 필드라, 도킹 중 이 값으로 덮어써진다(해제 후에도 원래 값으로 복원하지 " +
             "않는다 — 스펙 확정, 문제가 되면 CatapultSystem/CLAUDE.md TBD 참고).")]
    public float dockedScaleMultiplier = 1.5f;

    [Header("조향 회전 [TBD, 임시값]")]
    [Tooltip("스폰 시점 방향(링이 있는 방향) 기준 좌우 최대 회전각(도). Mathf.DeltaAngle로 계산해 " +
             "0/360도 랩어라운드를 피한다.")]
    public float steerYawRange = 100f;

    [Tooltip("조향 회전 속도(도/초).")]
    public float steerRotateSpeed = 90f;

    private float baseYaw;
    private float pendingHorizontal;
    // 23차 개편 — `baseYaw` 기준 오프셋(항상 [-steerYawRange, +steerYawRange] 안). 조향 요 회전의
    // 유일한 진실이다 — `rootBody.rotation`을 다음 계산의 입력으로 다시 읽지 않는다(클래스 상단
    // "23차 개편" 주석 참고, 드리프트 되먹임 방지).
    private float dockedYawOffset;

    private Rigidbody dockedBody;
    private PlayerMover dockedMover;
    private PlayerShapeController dockedShapeController;
    private Transform dockedOriginalParent;

    void Awake()
    {
        if (rootBody != null) baseYaw = rootBody.rotation.eulerAngles.y;
    }

    void Update()
    {
        HandleDockInput();

        // 입력은 여기서 읽고, 실제 회전 대입은 FixedUpdate에서 한다(클래스 상단 "조향" 주석 참고 —
        // 5차 개편이 이미 정립한 이유와 같다). 25차 개편 — `dockedBody != null`만으로는 부족했다.
        // Input.GetAxis("Horizontal")은 전역 축이라, 도킹된 구가 Tab으로 파킹된 동안 다른 플레이어가
        // A/D를 눌러도 그 값이 그대로 여기 들어와 투석기가 제멋대로 돌았다(사용자 재현 보고) —
        // `dockedMover.IsControlled`까지 함께 확인해야 "지금 실제로 이 구를 조작 중"일 때만 반응한다
        // (HandleDockInput의 C키 게이트가 이미 쓰는 것과 같은 조건, 클래스 상단 "입력 게이트" 주석 참고).
        pendingHorizontal = (dockedBody != null && dockedMover != null && dockedMover.IsControlled)
            ? Input.GetAxis("Horizontal") : 0f;
    }

    void FixedUpdate()
    {
        if (rootBody == null || dockedBody == null) return;

        // 23차 개편 — `rootBody.rotation`을 다시 읽지 않는다. 이번 스텝의 델타를 스크립트가 이미
        // 들고 있는 `dockedYawOffset`(baseYaw 기준, 클램프됨)에만 누적한다 — 물리 시뮬레이션이 만든
        // 드리프트가 다음 목표로 되먹임되는 경로 자체를 없앤다(클래스 상단 "23차 개편" 주석 참고).
        dockedYawOffset = Mathf.Clamp(
            dockedYawOffset + pendingHorizontal * steerRotateSpeed * Time.fixedDeltaTime,
            -steerYawRange, steerYawRange);
        rootBody.MoveRotation(Quaternion.Euler(0f, baseYaw + dockedYawOffset, 0f));

        // 도킹 중엔 회전을 스크립트가 전적으로 소유한다 — 논카인매틱 Rigidbody에 남은 시뮬레이션
        // 각속도(바퀴 접지 노이즈 등)가 이번 MoveRotation과 같은 스텝에 함께 적분돼 다음 프레임의
        // 자세를 살짝 더 미는 것을 막는다(위 "23차 개편" 주석 참고). 아무도 도킹하지 않았을 때는
        // 이 줄 자체가 실행되지 않으므로(위 조기 반환) 기존 물리 거동을 그대로 둔다.
        rootBody.angularVelocity = Vector3.zero;
    }

    private void HandleDockInput()
    {
        if (!Input.GetKeyDown(KeyCode.C)) return;

        if (dockedBody != null)
        {
            // 입력 게이트(DreamThreadController/CatapultLoadController와 같은 패턴, 클래스 상단
            // "입력 게이트" 주석 참고) — 도킹된 플레이어가 지금 조작 대상이 아니면(Tab으로 파킹)
            // 다른 플레이어의 C가 새어 들어와도 무시한다. TryDock으로 흘려보내면 파킹된 플레이어의
            // 참조가 새 플레이어로 덮여 영구히 추적에서 사라진다.
            if (dockedMover != null && !dockedMover.IsControlled)
            {
                Debug.Log("[Catapult] 다른 플레이어가 조향석에 도킹되어 있습니다 — Tab으로 그 " +
                          "플레이어를 조작해 C로 해제하세요(동시 1인 도킹).");
                return;
            }
            Undock();
            return;
        }

        TryDock();
    }

    private void TryDock()
    {
        if (dockAnchor == null || rootBody == null) return;

        PlayerMover mover = FindControlledPlayer();
        if (mover == null)
        {
            Debug.Log("[Catapult] 조작 중인 플레이어를 찾지 못했습니다.");
            return;
        }

        // 조향석은 구 전용 — 나머지 도형은 조용히 무시한다(로그만, CatapultLoadController의
        // 정육면체 거부와 같은 관례).
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Sphere)
        {
            Debug.Log("[Catapult] 조향석은 구 전용입니다.");
            return;
        }

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        float myDistance = Vector3.Distance(body.position, dockAnchor.position);
        if (myDistance > dockRange)
        {
            Debug.Log("[Catapult] 조향석 범위 밖입니다.");
            return;
        }

        // 범위가 겹치는 투석기가 여럿이면 가장 가까운 것에만 도킹한다 — CatapultLoadController.
        // IsNearestCatapult와 같은 이유(C는 전역 키라 모든 인스턴스가 같은 프레임에 독립적으로 반응).
        if (!IsNearestSteerHandle(body.position, myDistance)) return;

        Dock(mover, body, identity);
    }

    private void Dock(PlayerMover mover, Rigidbody body, PlayerShapeIdentity identity)
    {
        dockedMover = mover;
        dockedBody = body;
        dockedShapeController = identity.GetComponent<PlayerShapeController>();
        dockedOriginalParent = body.transform.parent;

        // 23차 개편 — 도킹 순간의 실제 요(이전 도킹 세션에서 남은 회전이 있을 수 있다)로
        // dockedYawOffset을 초기화한다. 그래야 회전 소유권을 스크립트로 넘기는 순간 투석기가
        // 시각적으로 튀지 않는다(클래스 상단 "23차 개편" 주석 참고).
        dockedYawOffset = Mathf.Clamp(
            Mathf.DeltaAngle(baseYaw, rootBody.rotation.eulerAngles.y), -steerYawRange, steerYawRange);

        body.isKinematic = true; // ThreadPinPlacer의 벽 부착과 같은 선택 — 조인트도 velocity 다툼도 없다.

        // dockAnchor에 부모화해 위치/회전을 스냅한다 — 조향으로 투석기 루트가 돌 때 도킹된 구가
        // 그 회전을 그대로 따라가야 "손잡이를 쥐고 함께 도는" 느낌이 난다(클래스 상단 "도킹 시
        // 하는 일" 주석 참고).
        body.transform.SetParent(dockAnchor, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;

        mover.ExternallyDriven = true; // mover.enabled는 절대 건드리지 않는다(Tab 로스터 보존, 하드룰).

        // ScalingSystem 파일은 건드리지 않는다 — growMultiplier가 이미 public 필드라 이 값만
        // 투석기가 원하는 배율로 바꿔 두고 기존 ToggleScale을 그대로 호출한다. lerpSpeed 보간은
        // PlayerShapeController가 이미 스스로 처리하므로 추가 스무딩 코드가 필요 없다.
        if (dockedShapeController != null)
        {
            dockedShapeController.growMultiplier = dockedScaleMultiplier;
            dockedShapeController.ToggleScale(PlayerShapeController.EScaleState.Grown);
        }

        Debug.Log("[Catapult] 구가 조향석에 도킹했습니다. 좌우 이동 입력으로 투석기를 조향하세요(다시 C로 해제).");
    }

    private void Undock()
    {
        if (dockedShapeController != null)
        {
            // 토글이라 Grown 상태에서 다시 호출하면 Normal로 되돌아간다 — 새 축소 로직 불필요.
            dockedShapeController.ToggleScale(PlayerShapeController.EScaleState.Grown);
        }

        if (dockedBody != null)
        {
            // 현재(도킹된, 확대된) 위치를 그대로 유지한 채 부모만 되돌린다 — 자동 이탈 없음(사용자
            // 명시적 확정, 클래스 상단 "해제" 주석 참고).
            dockedBody.transform.SetParent(dockedOriginalParent, true);
            dockedBody.isKinematic = false;
        }

        if (dockedMover != null) dockedMover.ExternallyDriven = false;

        Debug.Log("[Catapult] 구가 조향석에서 해제됐습니다.");

        dockedBody = null;
        dockedMover = null;
        dockedShapeController = null;
        dockedOriginalParent = null;
        pendingHorizontal = 0f;
    }

    // 범위 안에 있는 다른 조향 손잡이 중 이 손잡이보다 가까운 것(또는 정확히 같은 거리라면
    // InstanceID가 더 작은 것)이 있으면 false를 반환해 도킹을 양보한다.
    private bool IsNearestSteerHandle(Vector3 playerPosition, float myDistance)
    {
        foreach (CatapultSteerHandle other in FindObjectsOfType<CatapultSteerHandle>())
        {
            if (other == this || other.dockAnchor == null) continue;

            float otherDistance = Vector3.Distance(playerPosition, other.dockAnchor.position);
            if (otherDistance > other.dockRange) continue;

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
}
