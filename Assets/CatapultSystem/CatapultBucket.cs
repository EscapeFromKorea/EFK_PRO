using UnityEngine;

/// <summary>
/// 투석기 버킷의 탑승 판정 컴포넌트. 정육면체(Kind == Cube)만 탑승 판정을 받는다 — "핀 박기는 세모
/// 전용"으로 `Kind`를 게이트하는 `ThreadPinPlacer`와 같은 종류의 역할 제한이지, 무게 게이트가
/// 아니다(PRD §5).
///
/// [왜 "옆에 스치기만 해도 탑승"되던 버그를 고쳤나 — 벽+바닥(솔리드) + 내부 트리거로 분리, 2026-08-04
/// → 19차 개편(2026-08-06)으로 발사 경로를 막던 벽 하나(F)만 제거되고 그 방향만 거리 게이트로 보강됐다]
/// 예전엔 버킷 트리거 박스 하나뿐이라 정육면체가 옆면을 살짝 스치기만 해도 `OnTriggerEnter`가
/// 발동했다(플레이테스트 지적). 3~18차 개편은 `CatapultMenuItem.CreateBucket`이 버킷을 두 겹으로
/// 만드는 방식으로 이를 막았다 — 바깥쪽 벽 4면 + 바닥은 솔리드(트리거 아님) 콜라이더라 옆으로는
/// 물리적으로 통과할 수 없고, 이 컴포넌트는 그 벽보다 안쪽·바닥보다 위의 **더 작은 내부 트리거
/// 콜라이더**(`Catapult_BucketInner`) 위에 붙어, 정육면체의 콜라이더가 그 내부 공간과 실제로 겹칠
/// 때만 탑승 판정을 냈다.
/// **19차 개편(2026-08-06, 사용자 확정) — 발사 경로를 실제로 막는 벽은 F 하나뿐이라는 것을 좌표
/// 계산으로 확정해, `Catapult_BucketWall_F`만 제거했다(B/L/R 세 벽은 그대로 유지).** 좌표 유도
/// 근거는 `CatapultMenuItem.cs` 상단 "19차 개편" 주석 참고 — 요약하면, 발사(분리)는 항상 팔이
/// `restAngle`(=0°)에 도달한 순간에 일어나고, 그 순간 발사 방향을 버킷 그룹의 로컬 좌표로
/// 역변환하면 `(Δx=0, Δy=+cos(launchPitch), Δz=+sin(launchPitch))`가 나온다 — Δx=0이라 L/R과는
/// 무관하고, Δz가 양수(F 쪽)라 F 벽의 Δz 범위에 벽 상단보다 먼저 도달한다(B는 Δz 부호가 반대라
/// 애초에 닿지 않는다). B/L/R은 여전히 "옆에 스치기만 해도 탑승"되던 2차 버그를 막는다 — F가
/// 사라진 방향만 이 컴포넌트의 거리 게이트(`IsWithinCentralBoardZone`, 아래)가 대신 걸러낸다.
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
/// [C키 탑승 — 6차 개편, 2026-08-04 신규]
/// 걸어서/뛰어서 들어가는 기존 방식(위 벽+바닥+내부 트리거)과 별개로, 정육면체를 조작 중인 플레이어가
/// `boardApproachRange` 안에서 C를 누르면 자동으로 탑승된다 — 투석기가 3배로 커지며 버킷 위치도
/// 3배 높아져 점프(기본 1.6 Unit)만으로 닿지 않게 된 것을 보완하는 의도된 경로다(PRD §2 단계 A,
/// §6·§7 — "점프로 못 올라간다"는 버그가 아니라 C키 탑승이 그 해법이라는 게 사용자가 명시적으로
/// 받아들인 트레이드오프다).
/// - **판정은 `CatapultPullAnchor.connectRange`와 같은 발상의 거리 게이트다** — 근처에 있으면
///   충분하고, 물리적으로 내부 트리거와 겹칠 필요가 없다(3배 확대 후 지상에서 걸어온 정육면체는
///   대개 안 겹친다는 게 이 기능이 필요해진 이유 그 자체다).
/// - **여러 투석기의 범위가 겹치면 가장 가까운 버킷에만 탑승한다** — `CatapultLoadController.
///   IsNearestCatapult`와 정확히 같은 이유(C는 전역 키라 모든 버킷이 같은 프레임에 독립적으로
///   반응하므로, 겹치는 위치에서 여러 버킷이 동시에 같은 플레이어를 붙잡는 사고를 막는다).
/// - **탑승은 정육면체 전용이다** — 구·정사면체가 C를 누르면 무시한다(기존 역할 게이트,
///   `Kind == Cube`와 동일한 기준 재사용).
/// - **시각 전환 — 6차 개편은 순간이동이 아니라 짧은 글라이드였다(0.4초, `Rigidbody.MovePosition`).
///   9차 개편(2026-08-05)으로 이 글라이드 자체를 삭제하고 즉시 텔레포트로 바꿨다 — 아래 항목 참고.**
///
/// [C키 탑승이 투석기와 함께 "폭발"하던 버그 — 9차 개편(2026-08-05)으로 글라이드 자체를 삭제]
/// 6차 개편의 원래 구현은 C를 누른 자리에서 버킷 내부 트리거 위치(`transform.position`, 매 프레임
/// 다시 읽어 그사이 팔이 당겨져 버킷이 움직여도 따라감)까지 `boardTransitionDuration`(0.4초)에
/// 걸쳐 `Rigidbody.MovePosition`으로 선형 보간하면서, 그동안 `isKinematic = true`(중력/충돌 무시) +
/// `mover.ExternallyDriven = true`(조작 차단)로 플레이어 쪽 간섭을 막았다. **이 "글라이드 도중의
/// 킨네마틱 상태"(움직이면서 동시에 고정된 상태) 자체가 근본 원인이었다** — 킨네마틱 Rigidbody는
/// 물리 시뮬레이션을 무시하고 매 물리 스텝 Transform이 명령한 위치로 강제 이동하는데, 버킷이 팔 끝
/// 높은 곳에 있어 직선 글라이드 경로가 다이나믹(non-kinematic, mass 150) 투석기 루트의 솔리드
/// 지오메트리(팔/받침대/버킷 벽)를 가로지르기 쉬웠다 — 겹친 순간마다 킨네마틱 바디가 그 자리를
/// "정답"이라며 억지로 점유하려 들면서 다이나믹 투석기 쪽을 물리적으로 밀어냈고, 그 반작용이 누적돼
/// 탑승 순간 폭발하듯 튕겨 나갔다.
/// 사용자의 요구사항("C키를 누르면 별도의 애니메이션 없이 정육면체가 고정되지 않은 상태로 버킷에
/// 들어가도록")은 최종 탑승 상태(부모화+킨네마틱, `Board()`가 여전히 그대로 함 — "탑승은 오직
/// Fire로만 해제"라는 불변식의 근거인 클래스 상단 "왜 탑승 중 부모화+isKinematic인가" 참고)를
/// 없애라는 뜻이 아니라, **버그의 원인이었던 "글라이드 도중의" 킨네마틱 상태를 없애라는 뜻으로
/// 해석했다** — "별도의 애니메이션 없이"(보간 제거)와 "고정되지 않은 상태로 들어가도록"(그 이동
/// 과정에서 킨네마틱으로 고정된 채 솔리드를 가로지르지 않도록)가 결국 같은 문제(글라이드)를
/// 가리킨다고 봤다.
/// **수정 — `boardTransitionDuration`/`boarding` 플래그/`MovePosition` 루프를 전부 삭제하고, C를
/// 누른 프레임에 즉시(단일 프레임) `Rigidbody.position`을 버킷 내부 트리거 위치로 텔레포트한 뒤
/// 곧바로 `Board()`(걸어서 탑승하는 경로가 쓰는 것과 동일한 함수)를 호출한다.** 텔레포트는 물리
/// 스텝 사이에 일어나므로 다이나믹 투석기와 겹치는 프레임 자체가 존재하지 않는다 — "움직이는
/// 킨네마틱 바디가 다이나믹 루트를 미는" 버그의 재료 자체가 사라진다. 최종 탑승 상태는 여전히
/// `Board()`가 정하는 대로(부모화+킨네마틱)라 "탑승 중엔 스스로 못 나간다"는 확정 설계와 충돌하지
/// 않는다. 플레이어 Rigidbody가 `RigidbodyInterpolation.Interpolate`를 쓰므로(`PlayerObjectMenuItem`
/// 확인) 텔레포트 순간에도 화면이 끊겨 보이지 않고 한 물리 스텝(기본 0.02초) 안에서 자연스럽게
/// 스냅되는 정도다 — 이건 스크립트가 만드는 "애니메이션"이 아니라 다른 모든 물리 이동에도 똑같이
/// 적용되는 표준 떨림 방지 기능이라 요구사항과 무관하다.
/// **`ExternallyDriven`도 더 이상 세우지 않는다** — 예전엔 0.4초 글라이드 동안 입력을 막아야 했지만,
/// 이제 텔레포트와 `Board()`(isKinematic=true)가 같은 함수 호출 안에서 연속으로 일어나 물리 스텝이
/// 끼어들 틈이 없다. 걸어서 탑승한 경로도 원래 `isKinematic`만으로 조작을 막고 `ExternallyDriven`을
/// 건드리지 않으므로(클래스 상단 "왜 탑승 중 부모화+isKinematic인가" 참고), 여기서도 같은 관례를
/// 따르는 것이 맞다(불필요한 플래그를 늘리지 않는다).
/// **텔레포트 뒤에도 `OnTriggerEnter`가 내부 트리거 겹침을 감지해 다시 호출될 수 있다 — 이건
/// 안전하다.** `Board()`가 이미 `occupantBody`를 채워 뒀으므로, 뒤이어 도착하는 트리거 이벤트는
/// `occupantBody == body` 분기(중복 콜라이더 흡수 경로, 아래 `OnTriggerEnter` 참고)를 타 그냥
/// `overlapCount`만 늘어날 뿐 `Board()`가 두 번 불리지 않는다 — 그래서 6차 개편의 `boarding` 플래그
/// 가드가 더 이상 필요 없다(삭제).
///
/// [10차 개편(2026-08-05) — 탑승 시 정육면체 크기/위치가 이상해지던 버그 — 근본 원인은 부모화
/// 대상의 비균일 스케일]
/// 버킷 위치/크기(`BucketInnerHalf`, `BucketGroupLocalY/Rotation/Scale` 등)는 이번에도 손대지
/// 않았다 — 진짜 원인은 버킷 쪽이 아니라 **탑승 처리(`Board()`)가 부모로 삼는 Transform 자체**였다.
/// `Board()`는 그동안 탑승자를 `transform`(=이 컴포넌트가 붙은 `Catapult_BucketInner`)에
/// `SetParent(..., true)`로 부모화했는데, `Catapult_BucketInner`는 트리거 콜라이더 크기를 겸하려고
/// `localScale = BucketInnerHalf * 2`(예: `(5.4, 4.2, 5.1)`, 세 축이 전부 다른 **비균일** 스케일)로
/// 설정돼 있다. Unity의 `Transform`은 위치·회전·스케일 세 값만 저장할 수 있고 **전단(shear)을
/// 표현할 방법이 없다** — `SetParent(parent, worldPositionStays: true)`는 자식의 월드 변환을
/// 보존하도록 새 로컬 위치/회전/스케일을 역산하는데, 부모 체인에 "회전 + 비균일 스케일"의 조합이
/// 있으면(여기서는 armPivot의 가변 회전 θ와 그룹의 −90° 회전이 `Catapult_BucketInner`의 비균일
/// 스케일과 얽힌다) 그 역산 결과가 진짜로는 전단이 섞인 행렬이라, Unity가 스케일 성분만 억지로
/// 뽑아내며 오차가 생긴다 — 그 결과가 탑승한 정육면체의 눈에 보이는 크기/비율 왜곡이다(사용자가
/// `Catupult_bug2.png`로 제보). `ScalingSystem/PlayerShapeController`가 플레이어 크기를 항상
/// **균일(1:1:1) 배율로만** 바꾼다는 사실(해당 파일 주석 확인, 읽기 전용)도 재확인했다 — 즉 정육면체
/// 자신은 원래 전단이 없는 순수 스케일 상태였는데, 부모화 과정에서 왜곡이 생긴 것이다.
/// **수정 — 부모 대상을 `Catapult_BucketInner`가 아니라 `armPivot`으로 바꿨다.** armPivot은
/// (`CatapultMenuItem`이 `new GameObject`로 만들어) 로컬 스케일이 항상 `(1,1,1)`인 순수 회전 노드다
/// — 균일 스케일은 어떤 회전과 결합해도 절대 전단을 만들지 않으므로(`s·I`는 모든 회전과 교환된다),
/// armPivot에 부모화하면 `SetParent(..., true)`의 스케일 역산이 수학적으로 정확하다(근사가 아니라
/// 엄밀하게 원래 월드 스케일을 그대로 복원한다). armPivot은 이미 `CatapultArm`이 당김 각도(θ)를
/// 회전시키는 바로 그 노드라 "탑승자가 팔 당김에 따라 함께 움직인다"는 요구도 그대로 유지된다 —
/// `GetComponentInParent&lt;CatapultArm&gt;()`로 찾은 뒤(6차 개편의 `Reset()`/생성기 관례상
/// `CatapultArm`이 armPivot 자기 자신에 붙어 있다) 그 `.armPivot` 필드를 부모 대상으로 캐시해
/// 재사용한다(새 필드를 추가하지 않고 기존 참조를 재사용 — YAGNI). 정육면체는 여전히 버킷 그룹의
/// 로컬 회전(−90°)·축소(0.43배)는 상속하지 않는다(7차 개편이 이미 확인한 대로 — armPivot의 회전
/// (θ, 조향과는 별개)만 따라간다는 점은 그대로다).
/// **위치도 함께 정확해졌다 — "버킷 내부에 정확하게 들어가게" 요구.** `Board()`가 부모화 직후
/// `body.transform.position = transform.position`(내부 트리거의 현재 월드 중심)으로 명시적으로
/// 스냅한다 — 걸어서/뛰어서 들어온 경로는 원래 "트리거에 닿은 그 자리 그대로" 고정돼 벽에 반쯤
/// 걸치거나 구석에 치우친 채로 탑승될 수 있었는데, 이 스냅으로 항상 버킷 정중앙에 자리 잡는다.
/// C키 경로는 이미 `HandleBoardInput`에서 같은 위치로 텔레포트한 뒤 `Board()`를 호출하므로 이
/// 스냅은 그 값을 다시 한번 확인해 주는 것과 같다(중복이지만 무해하고, 두 경로가 항상 같은 최종
/// 위치로 수렴한다는 것을 한 곳에서 보장한다 — DRY).
///
/// [12차 개편(2026-08-05) — 정육면체 탑승에 각도 게이트(≥80°) 추가]
/// 사용자 요청: 정사면체가 당김줄로 팔을 어느 정도 당겨 놔야 정육면체가 탑승할 수 있게(협동 조건
/// 강화). `CatapultArm.CurrentAngle`(신규 public 프로퍼티, `currentAngle`을 읽기 전용으로 노출)을
/// `boardMinArmAngle`(기본 80°, `[TBD, 임시값]`)과 비교해 게이트한다 — 걸어서 들어오는 경로
/// (`OnTriggerEnter`)와 C키 경로(`HandleBoardInput`) 둘 다 탑승 직전에 확인한다.
/// **왜 `OnTriggerStay`도 필요한가.** `OnTriggerEnter`는 진입 순간에만 한 번 발동한다 — 정육면체가
/// 각도 미달 상태에서 먼저 버킷 안에 들어와 대기하다가, 정사면체가 나중에 80°를 넘기는 시나리오에서
/// 는 이 이벤트가 다시 오지 않는다. 대신 트리거 안에 머무는 동안 매 프레임 재확인해, 각도가 넘는
/// 순간 자동으로 탑승되게 했다(코디네이터 지시). C키 경로는 이미 `HandleBoardInput` 자체가
/// `Update()`에서 매 프레임 폴링되므로 별도 재시도 로직이 필요 없다.
/// **`arm`(`CatapultArm` 참조)을 `Awake()`에서 캐시한다** — 이미 `mountParent`를 구하려고
/// `GetComponentInParent&lt;CatapultArm&gt;()`를 호출하고 있었으므로, 그 결과 자체를 필드로
/// 남겨 재사용한다(새 조회를 추가하지 않는다 — YAGNI). `arm`을 못 찾으면(수동 조립 등) 게이트를
/// 걸지 않는다 — `mountParent`가 그럴 때 `this.transform`으로 안전하게 폴백하는 것과 같은 방어
/// 원칙이다.
///
/// [13차 개편(2026-08-05) — "탑승 후 팔 각도를 따라가지 않는다" 진단 로그 추가 (코드 수정 없음)]
/// 12차 개편의 정적 코드 검토로도 원인을 찾지 못했다 — 더 이상 블라인드로 코드를 고치지 않고,
/// 탑승자가 있는 동안 실측 데이터를 남기는 쪽으로 바꿨다(코디네이터 지시). `Update()`가 탑승 중
/// (`occupantBody != null`)일 때 `occupantLogInterval`(기본 0.5초)마다 탑승자의 로컬/월드
/// 위치·회전과 `armPivot`의 회전을 `Debug.Log`로 남긴다 — 매 프레임 찍으면 콘솔이 그 자체로
/// 읽기 어려워지므로(성능보다 가독성이 목적) 짧은 간격으로 샘플링한다. 이 로그를 플레이 중
/// 캡처해 오면 "탑승자의 로컬 회전이 armPivot과 실제로 어긋나는지" 또는 "로컬은 맞는데 월드만
/// 이상한지"(부모-자식 관계 자체가 깨진 경우)를 구분할 수 있다.
///
/// [14차 개편(2026-08-05) — 13차 재테스트 결과 "탑승 후 팔 각도를 따라가지 않는다"가 5라운드
/// 연속(9~13차) 재현 확정, `Board()`에서 발견한 회전 미초기화 갭을 확정 수정]
/// 13차 개편으로 추가한 탑승 중 진단 로그를 사용자가 재생성 후 재테스트했지만, 조향 무반응과 별개로
/// 이 증상은 그대로 재현됐다 — 코디네이터가 로그 분석 중 `Board()`를 다시 읽다가 코드로 확인되는
/// 명확한 갭을 발견했다: `Board()`는 탑승 시 `body.transform.position`은 항상 내부 트리거 중심으로
/// 스냅하면서(10차 개편), **`body.transform.rotation`은 한 번도 초기화한 적이 없다.** 정육면체는
/// `FreezeRotation`(월드 회전 고정)이라 발사 순간의 월드 회전을 그대로 유지한 채 날아가고 착지하는데,
/// 그 발사가 팔이 당겨진 각도(pulledAngle 근처)에서 일어났다면 착지한 정육면체는 그 순간의 왜곡된
/// 월드 회전을 그대로 물고 있다. 다음 탑승에서 `SetParent(armPivot, true)`는 **월드 회전을 보존**
/// 하도록 새 로컬 회전을 역산하므로, 이 오염된 월드 회전이 armPivot 기준의 임의의 로컬 오프셋으로
/// 남는다 — 그 뒤 `ApplyAngle()`이 armPivot의 로컬 회전을 계속 바꿔도, 정육면체의 "로컬" 회전
/// 자체가 이미 틀어진 채 고정돼 있어 겉보기엔 "팔 각도를 따라가지 않는" 것처럼 보인다는 것이
/// 코디네이터의 분석이다. **수정 — 부모화 직후 `body.transform.localRotation = Quaternion.identity`를
/// 명시적으로 대입한다**(`Board()` 참고) — 매 탑승마다 armPivot 로컬 기준 "똑바로 선" 상태로 항상
/// 깨끗하게 시작하므로, 이전 발사의 회전이 다음 탑승에 누적/전이될 여지 자체가 사라진다. 12차 개편이
/// 코드 재추적으로도 원인을 못 찾은 이유가 여기서 설명된다 — `PlayerMover`/`ApplyAngle()` 등 다른
/// 파일은 전부 정상이었고, 진짜 결함은 `Board()` 자신의 "위치만 스냅하고 회전은 방치"라는 누락이었다
/// (읽기 전용 정독으로는 "무엇을 안 하고 있는지"가 코드 어디에도 안 보여 놓치기 쉬운 종류의 버그).
///
/// [14차 개편 — 조향(구가 끄는 것)은 13차 재생성 후에도 여전히 무반응, 5라운드 연속(9~13차) 재현
/// 확정 — 진단 로그로만 전환, 코드 로직은 손대지 않았다]
/// `CatapultSteerHandle.cs`/`CatapultMenuItem.cs`의 SpringJoint 설정·트리거 크기·`maxDistance`
/// 재검산 이력을 전부 다시 정독했지만 논리적으로 잘못된 지점을 찾지 못했다(코디네이터 확인) — 9~13차가
/// 이미 각각 "터널링", "물리적 도달 불가", "링 재축소", "마찰", "여유 공간 부족"이라는 서로 다른
/// 근본 원인을 순서대로 찾아 고쳤는데도 같은 증상("구가 밀어도 투석기가 안 움직인다")이 계속
/// 재현된다는 것은, 정적 코드 검토로 6번째 가설을 내는 것 자체가 더 이상 생산적이지 않다는 뜻으로
/// 판단했다. 이번 라운드는 `CatapultSteerHandle`에 관찰용 진단 로그만 추가한다(아래 클래스 참고) —
/// 조인트가 붙는지, 붙었다면 실제로 장력이 걸리는 거리까지 도달하는지, 투석기가 힘을 받고도 왜 안
/// 움직이는지(회전만 하고 병진은 안 하는 것인지)를 다음 플레이테스트 로그로 구분하는 것이 목표다.
///
/// [12차 개편 — "탑승 후 팔 각도를 따라가지 않는다"는 보고 — 코드 검토로는 원인을 찾지 못했다]
/// 코디네이터가 제기한 가설(플레이어 쪽 컴포넌트가 킨네마틱 Rigidbody의 위치/회전을 velocity가
/// 아니라 Transform으로 직접 덮어쓸 수 있다)을 확인하려고 `PlayerSystem/PlayerMover.cs`,
/// `PlayerSystem/PlayerGroundContact.cs`, `PlayerSystem/PlayerJump.cs`,
/// `PlayerSystem/PlayerControlSwitcher.cs`를 **읽기 전용으로** 정독했다(교차 폴더 하드룰 준수 —
/// 코드는 한 글자도 고치지 않았다). 네 파일 전부 `rb.velocity`/`rb.angularVelocity`만 쓰고,
/// `transform.position`/`transform.rotation`을 직접 대입하는 코드는 없었다 — 그리고
/// `Rigidbody.velocity`를 **킨네마틱** 바디에 대입하는 것은 Unity가 정의한 대로 순수 무동작이다
/// (킨네마틱 바디는 velocity 적분 대상이 아니라 Transform이 자세를 정하므로, 이 값은 다른 다이나믹
/// 바디가 이 바디와 충돌할 때의 반응 계산에만 쓰인다 — 몸 자신은 이 값으로 움직이지 않는다).
/// `Board()`의 부모화 대상(`mountParent = arm.armPivot`)과 `CatapultArm.ApplyAngle()`
/// (`armPivot.localRotation = ...`, 순수 Transform 대입, armPivot 자신은 Rigidbody 없음)도 다시
/// 추적했지만 둘 다 정상이었다 — `mountParent`는 씬 직렬화값(`arm.armPivot`, 생성기가 저장한 자기
/// 참조)에서 나오므로 Awake 실행 순서와 무관하게 항상 올바른 값을 가리킨다. 결론적으로 **정적 코드
/// 검토로는 재현 가능한 결함을 찾지 못했다** — Unity의 기본 Transform 계층 규칙대로라면 킨네마틱
/// 자식은 부모(armPivot)의 회전을 그대로 따라가야 한다. 이 항목은 코드 수정 없이 진단만 남긴다
/// (코디네이터 승인 사항) — 이번 라운드에 함께 추가한 각도 게이트(위 항목)로 탑승 시점 자체가
/// 달라지면서 증상이 재현되는지도 실측 대상이다(§7 TBD 참고).
///
/// [15차 개편(2026-08-05) - "잘 따라가다가 어느 순간 멈춘다" 재보고, 진단 로그만 보강 (코드 로직
/// 변경 없음)]
/// 14차 개편의 회전 미초기화 수정(Board()의 localRotation = Quaternion.identity) 이후 armPivot=36도
/// 일 때 occupant local rot≈32.3도로 거의 일치하는 등(수정 전 armPivot=0인데 occupant가 -30도였던
/// 것보단 확실히 나아졌다) 개선은 있었지만, 사용자가 실제 플레이 중 "잘 따라가는 것 같다가 어느
/// 순간 멈춰버려서 이전과 같은 현상(따라가지 않는 것처럼 보임)이 재발한다"고 보고했다. 부모-자식
/// Transform 관계가 정상이라면 자식은 물리적으로 매 프레임 자동으로 부모 회전을 따라가야 하고
/// "따라가다가 멈추는" 것 자체가 불가능하다 - 그래서 라이딩 도중 부모화 자체가 깨지거나(재파싱),
/// isKinematic이 예기치 않게 풀리는 게 아닌가 의심하고 있다. 아직 확실한 후보가 없어 이번 라운드는
/// 코드 수정 없이 LogOccupantTransform()에 occupantBody.transform.parent의 이름, isKinematic 현재
/// 값, 부모가 기대한 arm.armPivot과 실제로 같은 참조인지(bool)를 추가로 로그하는 것으로 그친다 -
/// 다음 캡처에서 "부모가 armPivot이 아닌 다른 것으로 바뀌었는지" 또는 "isKinematic이 false로 풀려
/// 있는지"를 바로 확인할 수 있다.
///
/// [16차 개편(2026-08-05) — "탑승 중 부모-자식 Transform 관계는 정상인데 로컬 회전이 armPivot과
/// 어긋난다"는 15차 재캡처 결과 반영, 탑승 중 Rigidbody.interpolation을 임시로 끄는 가설 기반 수정]
/// 15차 개편이 추가한 진단 로그를 사용자가 다시 캡처했다 — 부모는 정확히 `Catapult_ArmPivot`이고
/// `isKinematic=True`로 둘 다 정상인데도(15차 개편이 의심한 "부모화 자체가 깨지거나 isKinematic이
/// 풀리는" 가설은 기각) occupant의 로컬 회전이 armPivot의 각도와 계속 안 맞는다(예: armPivot=36°인데
/// occupant local rot X=20.7°, 다른 캡처에선 armPivot=0°인데 occupant local rot X≈90°). 부모-자식
/// Transform 관계가 정상이면 자식의 "로컬" 회전은 (Board()가 부모화 직후 identity로 리셋한 뒤로는)
/// 부모가 얼마나 회전하든 항상 그대로 유지돼야 하는데(월드 회전만 부모를 따라 자동으로 바뀐다), 실제로는
/// 로컬값 자체가 계속 달라지고 있었다.
/// **새 용의자 — `PlayerObjectMenuItem.cs:135`가 플레이어 Rigidbody에 설정하는
/// `RigidbodyInterpolation.Interpolate`(읽기 전용으로 확인, PlayerSystem 파일 미수정).** 이 모드는
/// 킨네마틱 바디를 `MovePosition`/`MoveRotation`으로 직접 움직일 때 그 사이를 부드럽게 보간해주는
/// 용도인데, 지금 이 정육면체는 그 방식이 아니라 **부모(armPivot) Transform이 회전해서 간접적으로
/// 끌려가는 방식**이다 — Unity 커뮤니티에 알려진 함정으로, 킨네마틱 Rigidbody가 Interpolate 모드인
/// 상태에서 "부모 Transform 변경으로 인한 간접적인 자세 변화"를 겪으면 보간 버퍼가 그 변화를 제대로
/// 추적하지 못해 `Update()`에서 읽는 렌더링용 Transform 값이 실제 물리 상태와 어긋나거나 지연될 수
/// 있다. `LogOccupantTransform()`이 `Update()`에서 호출돼 정확히 이 보간된(어긋날 수 있는) 값을
/// 읽고 있었다는 것도 이 가설과 일치한다.
/// **수정(확정 수정 시도, 진단이 아니다) — `Board()`에서 탑승 시작 시 `body.interpolation`을
/// `RigidbodyInterpolation.None`으로 바꾸고 원래 값을 저장해 뒀다가, `ConsumeOccupant()`(발사)에서
/// 원래 값으로 되돌린다.** `occupantOriginalParent`를 저장했다가 발사 시 복원하는 기존 패턴과 동일한
/// 구조다(`occupantOriginalInterpolation` 신규 필드). 탑승 중엔 어차피 킨네마틱+부모화로 매 프레임
/// 정확한 Transform이 결정되므로 보간이 필요 없고(오히려 위 가설대로 부작용만 낸다), 발사 후
/// (비킨네마틱, 실제 물리로 날아가는 동안)는 원래대로 보간을 켜야 시각적 떨림이 없다(투석기 루트에도
/// `RigidbodyInterpolation.Interpolate`가 "시각적 떨림 완화" 목적으로 쓰이고 있다 — 이 관례를 깨지
/// 않는다).
/// **이건 가설 기반 수정이다 — 100% 맞다는 보장은 없으며 다음 플레이테스트로 검증이 필요하다.**
/// 13차 개편이 추가한 `LogOccupantTransform()` 진단 로그는 그대로 뒀다 — 이 수정이 실제로 증상을
/// 없애는지(로컬 회전이 armPivot과 계속 일치하는지) 다음 캡처로 확인해야 한다.
///
/// [18차 개편(2026-08-06) — 탑승 위치를 "버킷 정중앙"에서 "바닥보다 살짝 위"로 (팔 버벅거림 완화
/// 시도 1차)]
/// 사용자 보고: "정육면체가 탑승한 상태에서 각도/힘을 조절할 때 팔이 버벅거린다." 원인은 명확히
/// 특정되지 않았다(사용자 자신도 시각적 인터폴레이션 문제(16차가 탑승 중 `interpolation=None`으로
/// 끈 부작용일 가능성)와 물리적 겹침 문제 중 어느 쪽인지 구분하지 못했다). 이번 라운드는 더 저렴하고
/// 안전한 쪽(위치 조정)부터 시도한다. `Board()`가 그동안 `transform.position`(내부 트리거의 기하학적
/// 정중앙 — 바닥에서도 half.y, 천장에서도 half.y만큼 떨어진 진짜 한가운데)을 그대로 대입했는데,
/// 정육면체(반높이 0.5)를 그 자리에 놓으면 위아래로 상당한 여유 공간(half.y − 0.5)이 남아 정육면체가
/// 버킷 안 공중에 붕 떠 있는 셈이었다. **수정 — `ComputeBoardTargetPosition()`이 "바닥면 위
/// occupantFloorClearance만큼 띄운 지점"을 대신 계산한다.** 이 컴포넌트 자신(`Catapult_BucketInner`)이
/// 곧 내부 트리거이므로, `BoxCollider.center/size`로 이 오브젝트 자신의 로컬(비스케일) 좌표계에서
/// 바닥 면을 구하고, 원하는 월드 단위 여유(정육면체 반높이 0.5 + `occupantFloorClearance`)를
/// `transform.lossyScale.y`로 나눠 로컬 오프셋으로 환산한 뒤 `TransformPoint`로 최종 월드 위치를
/// 얻는다 — 버킷 그룹의 회전(-90°X)·축소(0.43배) 등 조상 체인의 변환을 스크립트가 다시 풀어낼
/// 필요 없이 Transform API에 맡긴다.
/// **이 수정만으로 버벅거림이 안 고쳐질 수도 있다** — 그래도 안전하고 사용자가 명시적으로 요청한
/// 방향이라 우선 적용한다. 남으면 다음 유력 후보는 16차 개편이 탑승 중 끈
/// `Rigidbody.interpolation = None`이다(시각적 렌더링 아티팩트일 가능성 — 코디네이터가 사용자 답변을
/// 분석하며 이미 언급한 후보, 아래 TBD 참고).
///
/// [19차 개편(2026-08-06) — 발사 경로를 막는 벽 F만 좌표로 확정해 제거(B/L/R은 유지), 탑승 판정에
/// 중앙 0.8배 거리 게이트 신규 (최초 지시 "벽 완전 제거"는 사용자가 이후 정정 — 실제 요구는 "발사
/// 궤적을 막는 벽 하나만")]
/// 사용자 확정: 발사 시 정육면체가 버킷 벽 윗부분에 걸리는 문제를 해결한다(자세한 배경은 클래스
/// 상단 "왜 옆에 스치기만 해도 탑승되던 버그를 고쳤나" 항목 참고, 좌표 유도는 `CatapultMenuItem.cs`
/// 상단 "19차 개편" 주석 참고) — 걸리는 원인은 F 벽 하나뿐이라는 것을 확인해 F만 제거했다. F가
/// 없어진 방향에서 새로 생기는 "가장자리 스침 탑승" 여지를 막기 위해
/// `IsWithinCentralBoardZone(Vector3 worldPos)`을 새로 추가했다 — 진입 위치를 이 컴포넌트 자신의
/// `BoxCollider` 로컬 좌표로 변환해 `|local.x| ≤ half.x·centralZoneFraction`이고 `|local.z| ≤
/// half.z·centralZoneFraction`(기본 0.8)일 때만 true를 반환한다. Y축은 검사하지 않는다 — 위에서
/// 뛰어들어오는 높이 방향은 가장자리 스침 방지와 무관하다. **이 게이트는 모든 진입점에 걸린다(방향을
/// 가리지 않는다)** — B/L/R 쪽은 벽이 이미 가장자리 스침을 물리적으로 막으므로 이 게이트가 사실상
/// 항상 통과하는 무해한 추가 검사고, F 쪽만 실질적으로 이 게이트가 유일한 방어선이다.
/// **왜 콜라이더 자체를 줄이지 않았나.** `ComputeBoardTargetPosition()`(18차 개편)이 바로 이
/// 컴포넌트의 `BoxCollider.center`/`box.size`를 읽어 바닥 위치를 계산한다 — 콜라이더를 줄이면 이
/// 바닥 계산 자체가 축소된 트리거 기준으로 어긋난다. 대신 콜라이더 크기는 그대로 두고 판정 함수
/// 하나만 추가해, "탑승이 유효한 범위"와 "콜라이더가 반응하는 범위"를 분리했다.
/// **세 진입점 모두에서 재사용한다** — `OnTriggerEnter`(신규 진입), `OnTriggerStay`(각도 게이트로
/// 대기하다 나중에 넘는 경우, 12차 개편이 만든 재시도 경로), `HandleBoardInput`(C키 즉시 텔레포트,
/// 텔레포트 목적지가 항상 정중앙이라 사실상 항상 통과하지만 방어적 일관성 차원에서 넣었다) — 이미
/// 있는 `ArmAngleAllowsBoard()`와 같은 패턴이다.
/// **부작용이자 사용자가 명시적으로 원한 방향 — F 벽이 없으므로 그쪽에서 걸어 들어오는 탑승도 이제
/// 0.8배 구역 안에서는 물리적으로 가능하다.** 별도 방지 장치를 추가하지 않았다 — "탑승이 편해짐"은
/// 부작용이 아니라 사용자가 원한 결과다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CatapultBucket : MonoBehaviour
{
    [Header("발사 후 조작 복구")]
    [Tooltip("착지를 못 잡아도 이 시간(초) 안에 강제로 조작을 복구한다. " +
             "DreamThreadSystem의 launchReenableTimeout과 동일한 6초(PRD 확정).")]
    public float launchReenableTimeout = 6f;

    [Header("C키 탑승 (6차 개편, 신규 — 9차 개편으로 즉시 텔레포트 방식으로 교체)")]
    [Tooltip("정육면체를 조작 중인 플레이어가 이 거리 안에서 C를 누르면 버킷까지 즉시 텔레포트해 " +
             "탑승한다(정육면체 전용). CatapultPullAnchor.connectRange와 같은 발상의 거리 게이트다. " +
             "3배 확대 후 버킷이 지상에서 약 5.7유닛(post-scale) 위에 있어 넉넉하게 잡았다 — " +
             "씬 튜닝 전제 [TBD, 임시값].")]
    public float boardApproachRange = 10f;

    [Header("탑승 각도 게이트 (12차 개편, 신규 [TBD, 임시값])")]
    [Tooltip("팔의 현재 당김 각도(CatapultArm.CurrentAngle)가 이 값 이상일 때만 정육면체가 탑승할 " +
             "수 있다(정사면체가 어느 정도 당겨 놔야 정육면체가 탑승할 수 있는 협동 게이트, 사용자 " +
             "요청). CatapultArm을 찾지 못하면(수동 조립 등) 게이트를 걸지 않는다(과거 동작으로 " +
             "안전 폴백) — armPivot을 못 찾을 때 mountParent가 this.transform으로 폴백하는 것과 " +
             "같은 방어 원칙이다.")]
    public float boardMinArmAngle = 80f;

    [Header("탑승 유효 범위 (19차 개편, 신규 — 벽 제거를 보완하는 거리 게이트)")]
    [Tooltip("내부 트리거 콜라이더 절반 크기(half)의 이 비율 이내로 들어온 경우에만 탑승을 허용한다" +
             "(가장자리를 스치는 것만으로는 탑승되지 않게 하는 목적 — 벽이 하던 일을 대신한다). " +
             "1.0에 가까울수록 가장자리까지 허용, 0에 가까울수록 정중앙만 허용.")]
    [Range(0.1f, 1f)]
    public float centralZoneFraction = 0.8f;

    [Header("진단 로그 (13차 개편, 신규 — '팔 각도를 따라가지 않는다' 실측용)")]
    [Tooltip("탑승 중인 동안 이 간격(초)마다 탑승자 Transform과 armPivot 회전을 Debug.Log로 남긴다. " +
             "0 이하면 로그를 남기지 않는다.")]
    public float occupantLogInterval = 0.5f;

    [Header("탑승 위치 (18차 개편 신규 — 버킷 정중앙 대신 바닥 근처로) [TBD, 임시값]")]
    [Tooltip("탑승 시 정육면체를 내부 트리거 '정중앙'이 아니라 '바닥면 위로 이만큼(월드 유닛) 띄운 " +
             "지점'에 놓는다 — 팔이 버벅거리는 느낌을 완화해 보려는 1차 시도(사용자 요청, 클래스 " +
             "상단 '18차 개편' 주석 참고). 정육면체 반높이(0.5, 고정)에 이 값을 더한 높이가 목표다. " +
             "0에 가까우면 바닥에 파묻히고, 너무 크면 예전(정중앙)과 다시 비슷해진다 — 감각적 기본값.")]
    public float occupantFloorClearance = 0.08f;

    // PlayerObjectMenuItem.cs가 정육면체에 쓰는 BoxCollider.size = Vector3.one(1×1×1, 이 기믹의 Scale과
    // 무관하게 고정) 기준 반높이 — 이 프로젝트에서 바뀔 여지가 거의 없어 하드코딩한다(18차 개편).
    private const float OccupantHalfHeight = 0.5f;

    private float occupantLogTimer;

    private int overlapCount;
    private Rigidbody occupantBody;
    private PlayerMover occupantMover;
    private PlayerShapeController occupantShapeController;
    private Transform occupantOriginalParent;
    // 16차 개편 신규 — 탑승 중 임시로 끄는 보간 모드를 발사 시 되돌리기 위한 원래값 저장.
    // occupantOriginalParent와 정확히 같은 패턴(Board()에서 저장, ConsumeOccupant()에서 복원).
    private RigidbodyInterpolation occupantOriginalInterpolation;

    private bool launching;
    private PlayerMover launchingMover;
    private PlayerShapeController launchingShape;
    private float launchTimer;

    // 10차 개편 — 탑승자를 부모화할 대상. Catapult_BucketInner(this.transform, 비균일 스케일)
    // 대신 armPivot(순수 회전, 스케일 항상 (1,1,1))을 쓴다(클래스 상단 "10차 개편" 주석 참고).
    // null이면(수동 조립 등으로 CatapultArm을 못 찾으면) this.transform으로 대체해 예전 동작으로
    // 안전하게 폴백한다.
    private Transform mountParent;

    // 12차 개편 — 각도 게이트가 읽을 CatapultArm 참조. mountParent와 같은 GetComponentInParent
    // 호출을 두 번 하지 않도록 이 하나를 캐시해 둘 다에 쓴다(YAGNI — 새 조회를 추가하지 않는다).
    private CatapultArm arm;

    /// <summary>지금 탑승자가 있는지(빈 발사 판정 등 외부에서 조회용).</summary>
    public bool HasOccupant => occupantBody != null;

    void Awake()
    {
        arm = GetComponentInParent<CatapultArm>();
        mountParent = arm != null ? arm.armPivot : null;
    }

    // 12차 개편 — CatapultArm을 못 찾았으면(수동 조립 등) 게이트를 걸지 않는다(과거 동작 폴백).
    private bool ArmAngleAllowsBoard() => arm == null || arm.CurrentAngle >= boardMinArmAngle;

    // 19차 개편 신규 — 벽이 사라진 대신 이 거리 게이트가 "가장자리를 스치기만 해도 탑승"을 막는다
    // (클래스 상단 "19차 개편" 주석 참고). world 좌표를 이 트리거(Catapult_BucketInner) 자신의
    // 로컬 좌표로 변환해, 콜라이더 절반 크기(box.size/2)의 centralZoneFraction(0.8)배 이내일 때만
    // true를 반환한다 — Y는 가장자리 스침 방지와 무관해 검사하지 않는다. 콜라이더 크기 자체는
    // 그대로 둔다(ComputeBoardTargetPosition이 같은 box.size를 바닥 계산에 그대로 쓰므로 여기서
    // 축소하면 안 된다) — 판정만 좁힌다.
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
        LogOccupantTransform();

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
    // 관례(기믹 쪽 컴포넌트가 직접 키 입력을 읽는다)를 따른다. 9차 개편 — 즉시 텔레포트 + Board()로
    // 단순화(클래스 상단 "C키 탑승이 투석기와 함께 폭발하던 버그" 참고). FixedUpdate 글라이드 루프
    // 자체가 사라져 이 메서드 하나로 탑승이 완결된다.
    private void HandleBoardInput()
    {
        if (!Input.GetKeyDown(KeyCode.C)) return;
        if (occupantBody != null) return; // 이미 탑승 중

        PlayerMover mover = FindControlledPlayer();
        if (mover == null) return;

        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Cube) return; // 정육면체 전용(역할 게이트)

        // 12차 개편 — 각도 게이트. C키 경로는 매 프레임 입력을 폴링하므로(이 메서드 자체가 Update()에서
        // 매 프레임 불린다) 각도가 나중에 넘어가면 자연히 재시도된다 — 걸어서 들어오는 경로처럼
        // 별도 OnTriggerStay 재확인이 필요 없다.
        if (!ArmAngleAllowsBoard())
        {
            Debug.Log("[Catapult] 아직 팔이 충분히 당겨지지 않아 탑승할 수 없습니다.");
            return;
        }

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        float myDistance = Vector3.Distance(body.position, transform.position);
        if (myDistance > boardApproachRange) return;

        // 범위가 겹치는 다른 투석기 버킷이 여럿이면 가장 가까운 곳에만 탑승한다 —
        // CatapultLoadController.IsNearestCatapult와 같은 이유(C는 전역 키).
        if (!IsNearestBucket(body.position, myDistance)) return;

        // 19차 개편 — 방어적 일관성 차원에서 걸어서/OnTriggerStay 경로와 같은 게이트를 통과한다.
        // 텔레포트 목적지가 항상 이 트리거의 정중앙(transform.position)이라 사실상 항상 통과하지만
        // (0.8배 범위는 정중앙을 항상 포함한다), 세 진입점이 같은 함수를 공유한다는 것을 보장한다.
        if (!IsWithinCentralBoardZone(transform.position)) return;

        // 단일 프레임 텔레포트 — 킨네마틱으로 "움직이는" 구간 자체를 만들지 않는다(클래스 상단 주석
        // 참고). 곧바로 Board()를 호출해 걸어서 들어온 경로와 동일한 최종 상태로 수렴시킨다.
        body.position = transform.position;
        Board(identity, body);

        Debug.Log("[Catapult] 정육면체가 C로 버킷에 즉시 탑승했습니다.");
    }

    // 13차 개편 신규 — "탑승 후 팔 각도를 따라가지 않는다" 실측용 진단 로그(클래스 상단 "13차 개편"
    // 주석 참고). 탑승 중이 아니면 아무 일도 하지 않는다.
    private void LogOccupantTransform()
    {
        if (occupantBody == null || occupantLogInterval <= 0f) return;

        occupantLogTimer -= Time.deltaTime;
        if (occupantLogTimer > 0f) return;
        occupantLogTimer = occupantLogInterval;

        Transform t = occupantBody.transform;
        Vector3 armEuler = arm != null ? arm.armPivot.localEulerAngles : Vector3.zero;
        string currentAngleText = arm != null ? arm.CurrentAngle.ToString("F2") : "n/a";
        // 15차 개편 신규 — "따라가다가 멈춘다"는 재현 불가능해 보이는 증상(부모-자식 Transform 관계가
        // 정상이면 자식은 매 프레임 자동으로 부모 회전을 따라가야 하므로, "따라가다가 멈추는" 것
        // 자체가 논리적으로 불가능하다)의 유력 용의자 — 라이딩 도중 부모화 자체가 깨지거나(재파싱)
        // isKinematic이 예기치 않게 풀리는지를 이 로그로 관찰한다(코드 로직은 변경하지 않는다).
        string parentName = t.parent != null ? t.parent.name : "null";
        Transform expectedParent = arm != null ? arm.armPivot : null;
        bool parentMatchesArmPivot = t.parent == expectedParent;
        Debug.Log($"[Catapult][Diag] occupant local pos={t.localPosition:F3} rot={t.localEulerAngles:F3} | " +
                   $"world pos={t.position:F3} rot={t.eulerAngles:F3} | armPivot local rot={armEuler:F3} " +
                   $"(CurrentAngle={currentAngleText}) | parent={parentName} isKinematic={occupantBody.isKinematic} " +
                   $"parentMatchesArmPivot={parentMatchesArmPivot}");
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
        // 9차 개편 — 즉시 텔레포트 뒤에도 이 이벤트가 뒤이어 도착할 수 있지만, occupantBody가 이미
        // Board()로 채워져 있어 아래 "occupantBody == body" 분기가 안전하게 흡수한다(클래스 상단
        // 주석 참고) — 6차 개편의 boarding 가드는 더 이상 필요 없다.
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

        // 12차 개편 — 각도 게이트. 미달이면 지금은 탑승시키지 않는다(overlapCount도 건드리지 않는다
        // — 탑승 자체가 아직 성립하지 않았으므로). 정육면체가 각도 미달 상태로 먼저 들어와 트리거
        // 안에 머무는 경우는 OnTriggerStay가 매 프레임 재확인해, 각도가 나중에 넘어가는 순간
        // 자동으로 탑승시킨다.
        if (!ArmAngleAllowsBoard()) return;

        // 19차 개편 — 벽이 사라진 대신, 가장자리를 스치기만 한 진입은 걸러낸다(클래스 상단 주석 참고).
        if (!IsWithinCentralBoardZone(body.position)) return;

        overlapCount = 1;
        Board(identity, body);
    }

    // 12차 개편 — 각도 게이트 재시도. OnTriggerEnter는 진입 순간에만 발동해, 정육면체가 각도 미달
    // 상태에서 먼저 들어와 대기하다가 정사면체가 나중에 당김 각도를 넘기는 시나리오를 놓친다 —
    // 트리거 안에 머무는 동안 매 프레임 재확인해 각도를 넘는 순간 자동으로 탑승되게 한다.
    void OnTriggerStay(Collider other)
    {
        if (occupantBody != null) return; // 이미 탑승 중이면 재확인 불필요(제일 싼 체크를 먼저).
        if (!other.CompareTag("Player")) return;
        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Cube) return;
        if (!ArmAngleAllowsBoard()) return;

        Rigidbody body = identity.GetComponent<Rigidbody>();
        if (body == null) return;

        // 19차 개편 — 가장자리에 머무르며 재시도하는 것도 같은 기준으로 걸러낸다.
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

    // 18차 개편 신규 — 탑승 목표 위치를 내부 트리거의 "기하학적 정중앙" 대신 "바닥면 위
    // occupantFloorClearance만큼 띄운 지점"으로 계산한다(클래스 상단 "18차 개편" 주석 참고). 이
    // 컴포넌트가 붙은 GameObject(Catapult_BucketInner) 자신이 곧 내부 트리거이므로, 이 오브젝트
    // 자신의 로컬(비스케일) 좌표계에서 바닥 면(box.center.y - box.size.y/2)을 구한 뒤, 원하는 월드
    // 단위 여유(정육면체 반높이 + clearance)를 lossyScale.y로 나눠 로컬 오프셋으로 환산한다 —
    // TransformPoint가 회전/스케일(그룹의 -90°X 회전+0.43배 축소 등)을 전부 반영해 최종 월드 위치를
    // 계산해 준다.
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
        // 16차 개편 — 탑승 중엔 보간을 끈다(클래스 상단 "16차 개편" 주석 참고). 발사(ConsumeOccupant)
        // 에서 원래 값으로 되돌린다.
        occupantOriginalInterpolation = body.interpolation;
        body.interpolation = RigidbodyInterpolation.None;

        body.isKinematic = true;
        // 10차 개편 — armPivot(순수 회전, 비균일 스케일 없음)에 부모화한다. 이 트리거(transform) 자신에
        // 부모화하면 그 비균일 localScale(BucketInnerHalf*2)이 회전과 얽혀 탑승자의 스케일을 왜곡시켰다
        // (클래스 상단 "10차 개편" 주석 참고).
        body.transform.SetParent(mountParent != null ? mountParent : transform, true);
        // 항상 내부 트리거 안의 정해진 지점으로 스냅한다 — 걸어서 들어온 경우 트리거에 닿은 자리
        // 그대로 고정되던 것(구석/벽 근처일 수 있음)을 방지한다. C키 경로는 이미 비슷한 위치로
        // 텔레포트한 뒤라 무해한 재확인이다. 18차 개편(2026-08-06) — "정중앙"이 아니라 "바닥면 위
        // occupantFloorClearance만큼 띄운 지점"으로 바꿨다(아래 ComputeBoardTargetPosition, 클래스
        // 상단 "18차 개편" 주석 참고).
        body.transform.position = ComputeBoardTargetPosition();
        // 14차 개편(2026-08-05) — 회전도 armPivot 로컬 기준 identity로 강제 리셋한다. 이전 발사
        // (팔이 당겨진 상태에서 velocity만 대입해 날아간 순간의 월드 회전)를 정육면체가 FreezeRotation
        // 때문에 착지해도 계속 물고 있다가, 다음 탑승 때 그 오염된 회전이 armPivot 기준 로컬 오프셋으로
        // 남아 "탑승 후 팔 각도를 따라가지 않는다"(9~13차, 5라운드 연속 재현)는 증상의 원인일 가능성이
        // 높다는 코디네이터 분석에 따른 확정 수정이다 — 위치 스냅과 마찬가지로 매 탑승마다 "똑바로 선"
        // 상태로 깨끗하게 시작하게 만든다.
        body.transform.localRotation = Quaternion.identity;
        occupantLogTimer = 0f; // 탑승 즉시 첫 진단 로그가 바로 찍히게 한다(13차 개편).

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
        // 16차 개편 — 탑승 중 꺼뒀던 보간을 발사 즉시 원래 값으로 되돌린다(발사 후에는 다시
        // 비킨네마틱 실제 물리로 날아가므로 시각적 떨림 방지가 필요하다, 클래스 상단 주석 참고).
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
