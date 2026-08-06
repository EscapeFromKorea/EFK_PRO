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
/// 생성되는 계층(참조 연결 포함, 2026-08-04 6차 개편 반영):
/// Catapult (CatapultLoadController + Rigidbody(중력 적용, 논카인매틱, FreezeRotationX/Z, mass 150,
///           drag/angularDrag) — 조향 조인트 대상이자 발사 방향 기준 루트, LineRenderer 실 포함)
/// ├─ Catapult_Base            (받침대 상판, 솔리드, 걸을 수 있음)
/// ├─ Catapult_Wheel ×2        (장식용 바퀴, 콜라이더 없음 — 6차 개편 외형 추가)
/// ├─ Catapult_TrestleStrut ×4 (교차 지지대, 시각 전용) + Catapult_TieBeam (하부 보강대, 6차 개편 신규)
/// ├─ Catapult_Axle            (지지대 상단 가로대, 시각 전용 — 팔이 여기서 피벗한다)
/// ├─ Catapult_ArmPivot        (CatapultArm — 로컬 X 회전으로 당김 연출)
/// │   ├─ Catapult_ArmVisual_Lower/Upper (팔 몸체, 2단 테이퍼 — 6차 개편 외형, 콜라이더 없음)
/// │   ├─ Catapult_Counterweight        (지레 반대편 균형추 장식, 콜라이더 없음 — 6차 개편 신규)
/// │   └─ Catapult_Bucket      (바구니 그룹, armPivot 자식 — 벽 3면(B/L/R)+바닥+내부 트리거. 7차
///                              개편으로 그룹 자체가 추가 회전(-90°X)+축소(0.43배)된 채 재배치됨 —
///                              아래 "7차 개편" 참고. 19차 개편으로 발사 경로를 막던 F 벽 하나만
///                              좌표 계산으로 확정해 제거했다 — 아래 "19차 개편" 항목 참고)
/// │       ├─ Catapult_BucketFloor      (바닥, 솔리드 — 옆/아래로 통과 불가)
/// │       ├─ Catapult_BucketWall_B/L/R (벽 3면, 솔리드 — 옆에 스치기만 해선 탑승 안 됨. F는
///                                       19차 개편으로 제거됨)
/// │       ├─ Catapult_BucketInner      (CatapultBucket — 내부 트리거, 벽 안쪽·바닥 위에만 존재.
///                                       6차 개편으로 정육면체(1×1×1)보다 확실히 넉넉하게 재설계.
///                                       19차 개편부터 콜라이더 크기는 그대로 두고, 탑승 판정
///                                       자체를 중앙 0.8배 구역으로 추가 제한한다(F가 없어진 쪽의
///                                       안전장치) — 아래 "19차 개편" 참고)
/// │       └─ Catapult_PullAnchor       (CatapultPullAnchor — 콜라이더 없는 순수 마커, 납작한 원반.
///                                       23차 개편(2026-08-06)으로 투석기 루트가 아니라 이 버킷
///                                       "그룹"에 부모화됐다 — B 벽 바로 뒤에서 팔이 당겨질 때
///                                       함께 움직인다. 아래 "23차 개편" 참고)
/// ├─ Catapult_SteerHandle_Rod   (손잡이 막대 — 시각 전용, 콜라이더 없음)
/// ├─ Catapult_SteerHandle_Ring  (22차 개편(2026-08-06)으로 그립 블록을 대체 — N세그먼트 순수 시각
///                                고리, 콜라이더 전혀 없음. CatapultSteerHandle.dockAnchor가 이
///                                Transform을 그대로 가리킨다 — 아래 "22차 개편" 항목 참고)
/// └─ CatapultSteerHandle      (22차 개편부터 이 컴포넌트는 Ring이 아니라 Catapult 루트 자신에
///                                붙는다 — 물리 충돌/트리거를 다루지 않고 C키 도킹 상태머신 +
///                                조향 회전만 담당하는 순수 스크립트라, CatapultLoadController와
///                                같은 자리가 자연스럽다)
/// **Rod와 Ring은 부모-자식이 아니라 둘 다 Catapult 루트의 직계 자식(형제)이다** — `CreateSteerHandle`이
/// `rod.transform.SetParent(parent, ...)`와 `ring.transform.SetParent(parent, ...)`를 같은
/// `parent`(투석기 루트)로 호출한다.
///
/// [버킷 높이 계산 — 왜 armVisual/bucket이 armPivot의 로컬 +Y 오프셋인가]
/// `CatapultArm.Awake()`는 항상 armPivot에 restAngle 회전을 먼저 적용한다. 그 뒤에 자식으로 붙는
/// armVisual/bucket의 로컬 오프셋은 "이미 회전된 프레임" 안에서 해석되므로, 자식 오프셋을 +Z 위주로
/// 두면(구버전) 회전이 그 오프셋을 아래로 접어 넣어 받침대 밑으로 파묻힌다(2026-08-04 발견·수정).
/// 지금은 팔이 "로컬 +Y로 곧게 선 블루프린트"이므로, 회전 후 버킷 그룹 원점 높이는
/// pivot.y + armLength*cos(θ)로 결정된다.
///
/// [바구니(벽+바닥) 모서리 파묻힘 검산 — 2026-08-04 3차 개편, 4차·6차 개편으로 재검산]
/// 버킷을 벽+바닥이 있는 진짜 바구니로 바꾸면서, 더 이상 "중심점 하나"가 아니라 **바닥의 네 모서리**가
/// 받침대 위에 있는지 확인해야 한다. 바구니 그룹 로컬 좌표계는 armPivot의 회전 프레임을 그대로
/// 물려받으므로(armVisual/bucket과 같은 이유), 그룹 로컬 오프셋 (Δx, Δy, Δz)를 가진 점의 root 좌표는
/// `(ApexY + (ArmLength+Δy)·cosθ − Δz·sinθ, ...)` 형태로 계산된다(Δx는 회전축이라 영향 없음, y·z만
/// 섞인다).
///
/// [6차 개편(2026-08-04, 5차 플레이테스트 후) — 버킷 내부 공간 재설계 (근본 원인 확인)]
/// 정육면체가 안 들어가지는 진짜 원인은 크기였다: `PlayerObjectMenuItem.cs`의 정육면체
/// `BoxCollider.size = Vector3.one`(1×1×1 월드 유닛)인데, 4차 개편 값(`BucketInnerHalf=(0.8,0.2,0.7)`)은
/// 내부 트리거 전체 **높이**가 `2×0.2=0.4`뿐이라 정육면체가 물리적으로 들어갈 수 없었다(딱 맞추는 게
/// 아니라 애초에 불가능). 각 축에 확실한 여유를 두고 다시 설계했다(pre-scale, ×3 전 기준):
/// `BucketInnerHalf = (0.9, 0.7, 0.85)` → 전체 크기 (1.8, 1.4, 1.7), 정육면체(1×1×1) 대비 여유
/// X 0.4/Z 0.35(각 옆면), Y 0.2(위아래 각각) — "딱 맞을 필요 없이 자연스럽게 들어가 있으면 된다"는
/// 요청에 맞춰 넉넉히 잡았다(정확한 수치의 물리적 근거는 없다 — 감각적 여유값, 씬 튜닝 전제).
/// `BucketWallHeight`도 `2×half.y`(=1.4)보다 확실히 높게 `1.5`로 다시 잡아 "옆에 스치기만 해도
/// 탑승"되던 2차 버그가 재발하지 않게 했다(벽이 거의 버킷 전체 높이를 덮어 실제로 "위에서 뛰어들어야
/// 하는 바구니"에 더 가까워졌다 — 외형 개선(6차 개편 4단계)과도 맞아떨어진다).
///
/// **Z 폭(half.z)이 0.7→0.85로 더 커지면서 파묻힘 계산의 `−Δz·sinθ` 항이 커졌다.** 이를 상쇄하려
/// `ArmLength`도 1.0→1.4(pre-scale)로 늘렸다 — 그러지 않으면 `(ArmLength+Δy)` 항이 너무 작아져
/// `pulledAngle`을 30° 근처까지 낮춰야 했다(rest 20°에서 겨우 10° 스윙 — 당겨지는 느낌이 거의
/// 안 남는다). ArmLength를 늘린 뒤 재검산한 결과 `pulledAngle=45°`가 버킷 확대 이후에도 여유를
/// 남기는 지점이라 55°에서 다시 낮췄다(`CatapultArm.cs` 상단 주석에도 같은 근거 기록).
///
/// [재검산 — pre-scale(3배 확대 전) 기준, ApexY=0.6/ArmLength=1.4/half=(0.9,0.7,0.85)/wallT=0.1/floorT=0.1]
///   floor 바닥면(Δy=-half.y-floorT=-0.8, Δz=half.z+wallT=0.95):
///     rest(20°):   y = 0.6 + (1.4-0.8)·cos20° − 0.95·sin20° = 0.6+0.2·0.9397−0.95·0.3420 ≈ 0.463 (받침대 윗면 0.2 대비 +0.263)
///     pulled(45°): y = 0.6 + 0.2·cos45° − 0.95·sin45° = 0.6+0.2·0.7071−0.95·0.7071 ≈ 0.353 (대비 +0.153, 파묻히지 않음)
///   floor 윗면(Δy=-half.y=-0.7, 밟는 면, rest 기준): y = 0.6+(1.4-0.7)·cos20°−0.95·sin20° ≈ 0.6+0.658−0.325=0.933 (대비 +0.733)
///
/// [6차 개편 3단계 — 투석기 전체 3배 균등 확대(`Scale = 3f`)]
/// 사용자 확정: 모든 형태 치수(위치·크기)를 3배로 키운다. 이 파일의 모든 길이 상수는
/// `(pre-scale 값) * Scale`로 선언돼 있어, 위 pre-scale 재검산 결과가 그대로 비례 유지된다(순수
/// 균등 스케일 — 각도는 스케일과 무관해 restAngle/pulledAngle은 그대로다). 3배 후 재검산(그대로
/// 곱해지는지 직접 재계산해 확인, 반올림 오차 없음 확인):
///   floor 바닥면: rest ≈ 0.6·3+... = 2.517(대비 +0.789×3=+1.917?) — 아래 실측치가 정확하다:
///     rest(20°):   y = 1.8 + 1.8·cos20° − 2.85·sin20° ≈ 1.8+1.6915−0.9747 = 2.5168 (받침대 윗면 0.6 대비 +1.9168)
///     pulled(45°): y = 1.8 + 1.8·cos45° − 2.85·sin45° ≈ 1.8+1.2728−2.0153 = 1.0575 (대비 +0.4575, 파묻히지 않음)
///   floor 윗면(rest): y = 1.8+2.1·cos20°−2.85·sin20° ≈ 1.8+1.9734−0.9747=2.7987 (대비 +2.1987)
///   (모두 pre-scale 여유값(+0.263/+0.153/+0.733)의 정확히 3배 — 균등 스케일이 그대로 보존됨을 확인했다.)
/// 3배 확대로 버킷이 지상에서 약 5.7유닛 위(그룹 원점 기준, `ApexY+ArmLength·cosθ`)에 놓여
/// `PlayerJump.jumpHeight`(1.6) 기본값으로는 더 이상 점프해 오를 수 없다 — **의도된 트레이드오프다.**
/// `CatapultBucket`의 신규 C키 탑승이 그 경로다(사용자가 명시적으로 받아들인 트레이드오프,
/// docs/PRD/Catapult.md §2·§6·§7 참고).
///
/// [6차 개편 — 당김 거리/발사 속도를 다르게 판단한 이유]
/// `CatapultLoadController.minPullDistance`/`maxPullDistance`(공간적 거리)는 3배로 함께 스케일했다
/// (0.5→1.5, 6→18) — 구조물이 커지는데 당김 거리가 그대로면 상대적으로 아주 조금만 당기는 것처럼
/// 보인다. 반면 `CatapultArm.minLaunchSpeed`/`maxLaunchSpeed`/`launchPitch`(탄도 튜닝값)는
/// **스케일하지 않았다** — 이건 투석기 메시 크기가 아니라 "얼마나 빨리/어떤 각도로 던지는가"라는
/// 순수 게임플레이 수치이고, 플레이어 점프 높이·이동 속도(PlayerSystem 소관, 미변경)도 그대로라
/// 착지 지점을 예측 가능하게 유지하려면 이쪽은 건드리지 않는 게 맞다고 판단했다(각 필드 상단 주석에
/// 근거를 남겼다).
///
/// [7차 개편(2026-08-05, 5차 플레이테스트 후) — 버킷 "그룹" 자체 Transform 재배치 (정육면체 탑승
/// 물리 폭발 수정)]
/// 6차 개편의 근본 원인 수정(내부 트리거를 넉넉히 키움)에도 불구하고, 사용자 보고로 실제 탑승 시
/// 투석기와 정육면체가 폭발하듯 튕겨 나가는 문제가 남아 있었다. 코드로 재확인한 진짜 원인은 벽/바닥/
/// 내부 트리거 치수(`BucketInnerHalf` 등, 위)가 아니라 **그룹(`Catapult_Bucket`) 자체가 통째로
/// `Scale=3` 배율을 그대로 입고 있었다는 것**이다 — 캐비티는 `(5.4, 4.2, 5.1)` 월드 유닛까지
/// 부풀었는데, 그 안에 들어오는 정육면체는 이 기믹의 Scale과 무관하게 여전히 고정 `1×1×1`이라(
/// `PlayerObjectMenuItem.cs`, 카타풀트 스케일을 전혀 모르는 별개 오브젝트) 극단적인 크기 불일치가
/// 났다. **벽/바닥/내부 트리거 자신의 로컬 치수(`BucketInnerHalf` 등)는 손대지 않았다** — 대신
/// 사용자가 씬(`Assets/Scenes/SampleScene.unity`)에서 그룹 자신의 Transform(위치·회전·스케일)만
/// 직접 드래그해 "실제 투석기와 비슷하게 ArmVisual_Upper 끝에"(레퍼런스의 슬링 구도) 자리 잡을
/// 때까지 맞췄고, 그 플레이테스트 완료 값을 코드로 그대로 옮겼다(`BucketGroupLocalY`/
/// `BucketGroupLocalRotation`/`BucketGroupLocalScale`, 아래) — 정확한 수치의 물리적 근거는 없다
/// (감각적 판단, 씬에서 검증된 값으로 신뢰).
///
/// [파묻힘 공식이 근본적으로 바뀌었다 — 그룹 자체에 회전+축소가 추가됐기 때문]
/// 6차 개편까지는 그룹이 armPivot의 순수 로컬 +Y 오프셋 하나뿐이라 armPivot의 회전(θ)만 반영하면
/// 됐다. 이제는 그룹 자신도 로컬 X축으로 `BucketGroupLocalRotation`(-90°) 만큼 추가 회전하고
/// `BucketGroupLocalScale`(0.43배)로 축소된다. armPivot의 회전축과 그룹 자신의 회전축이 둘 다 로컬
/// X(같은 축)라서, 두 회전은 순서와 무관하게 각도가 더해지는 것과 동일하게 합성된다(같은 축 회전은
/// 가환) — 이 사실과 좌표 합성을 직접 풀면, 그룹 로컬 좌표 (0, Δy, Δz)를 가진 점(Δx는 여전히 두
/// 회전 모두의 공통 회전축이라 영향 없음)의 root 좌표는
///   `y = ApexY + (BucketGroupLocalY + BucketGroupLocalScale·Δz)·cosθ + BucketGroupLocalScale·Δy·sinθ`
/// 로 정리된다(유도: 그룹 좌표에 스케일→그룹 자체 -90° 회전→그룹 위치 오프셋(이 오프셋 자체도
/// armPivot 회전 θ의 영향을 받음)→armPivot 회전 θ 순으로 합성. Δy/Δz는 이제 "그룹 자신의 로컬
/// 좌표계" 기준이다 — 벽/바닥 로컬 치수는 6차 개편 값 그대로다).
///
/// [재검산 — worst-case(바닥 바닥면 모서리), 전부 이미 ×Scale 반영된 현재 코드 상수 기준]
/// `ApexY=1.8`, `BucketGroupLocalY=5.34`, `BucketGroupLocalScale=0.43`, 바닥 바닥면 Δy=-(half.y+floorT)
/// =-(2.1+0.3)=-2.4, 바닥 옆 모서리 최악값 Δz=-(half.z+wallT)=-(2.55+0.3)=-2.85(부호가 음수일 때
/// term1이 최소가 돼 worst-case):
///   term1 = BucketGroupLocalY + 0.43·(-2.85) = 5.34 - 1.2255 = 4.1145
///   term2 계수 = 0.43·(-2.4) = -1.032
///   rest(20°):   y = 1.8 + 4.1145·cos20° − 1.032·sin20° ≈ 1.8+3.8664−0.3530 = 5.313 (받침대 윗면
///                BaseTopY=0.6 대비 +4.713)
///   pulled(45°): y = 1.8 + 4.1145·cos45° − 1.032·sin45° ≈ 1.8+2.9099−0.7297 = 3.980 (대비 +3.380)
/// 6차 개편까지는 여유가 좁아질 때마다 `pulledAngle`을 계속 낮춰야 했지만(70°→55°→45°), 이번엔
/// 그룹이 훨씬 높은 위치(BucketGroupLocalY=5.34, 옛 armPivot-+Y 오프셋 ArmLength=4.2보다 위)에 놓이고
/// 그룹 자신의 회전+축소(0.43배)가 Δy/Δz 항의 실질 크기를 크게 줄여, 여유가 오히려 넉넉해졌다 —
/// 역산하면 θ≈92°가 돼야 이 모서리가 겨우 받침대 높이에 닿는다(45°는 그보다 한참 안전하다). 그래서
/// **`pulledAngle`은 이번 라운드에서 바꾸지 않았다**(45° 유지 — `CatapultArm.cs` 상단 주석에도 같은
/// 재검산 근거를 남겼다).
///
/// [11차 개편(2026-08-05) — 장식 부품이 전부 플레이어를 투과하던 버그: 콜라이더 복구]
/// `CreateWheel`/`CreateTrestle`의 Axle/`CreateStrut`/`CreateTieBeam`/`CreateArmSegment`/
/// `CreateCounterweight`/`CreateSteerHandle`의 Rod — 이 7곳 전부 `GameObject.CreatePrimitive`가
/// 자동으로 붙이는 기본 콜라이더를 `Object.DestroyImmediate(...)`로 명시적으로 지우고 있었다(6차
/// 개편 외형 추가 당시 "순수 시각"이라고 성급하게 판단한 흔적). **수정은 그 `DestroyImmediate` 호출을
/// 지우는 것뿐이다** — 콜라이더를 새로 추가하지 않는다, 원래 Unity가 만들어 주는 걸 그대로 둘 뿐이다
/// (가장 적은 변경). 이 오브젝트들은 전부 Rigidbody가 없는 static child라, 버킷 벽/바닥이 이미 그렇듯
/// 가장 가까운 조상(투석기 루트 또는 armPivot)의 컴파운드 콜라이더에 자동 편입된다 — 새로운 위험
/// 패턴이 아니다. 회전하는 부품(`ArmVisual_Upper`/`Lower`, `Counterweight` — armPivot 자식)도
/// 포함해서 콜라이더를 살렸다 — 다만 **사용자가 명시적으로 확인한 대로 "물리력은 추가하지 않는다"**:
/// 이 콜라이더들이 플레이어를 능동적으로 밀어내는 새 메커니즘을 만들지 않고, 이미 문서화된 버킷 벽의
/// TBD 위험("회전이 빠른 순간 근처 플레이어가 스윕 없이 밀릴 수 있다", 이 파일 위쪽 "주의" 섹션·
/// PRD §7 참고)과 정확히 같은 성격·같은 수준으로만 취급한다 — 새 완화 장치(스윕 콜라이더 전환 등)는
/// 이번에 추가하지 않았다(YAGNI, 기존 TBD를 그대로 계승할 뿐이라고 명시해 둔다).
/// **바퀴만 추가로 `CatapultWheelVisual`(순수 시각 회전, 콜라이더/Rigidbody 미관여)을 붙인다** —
/// 정육면체/정사면체의 `PlayerVisualRoll`과 같은 성격의 장식이다(투석기 루트 Rigidbody의
/// `velocity.magnitude`를 읽어 로컬 X축을 회전시킨다, `CatapultWheelVisual.cs` 상단 주석 참고).
/// **씬 반영 — 이미 배치된 기존 투석기 인스턴스는 이 콜라이더 복구를 자동으로 받지 않는다.** 콜라이더
/// 유무는 `Create Catapult`가 오브젝트를 생성하는 시점에 결정되는 것이라(씬에 저장된 GameObject
/// 구조 자체의 일부), 코드만 갱신해서는 이미 배치된 장식 오브젝트에 콜라이더가 저절로 생기지 않는다
/// — 기존 씬 인스턴스는 재생성해야 한다(§ 아래 TBD 참고).
///
/// [12차 개편(2026-08-05, 재테스트 피드백 3건) (1) — 구가 밀어도 투석기가 안 움직이는 진짜 원인은
/// 지면 마찰이었다]
/// 11차 개편까지 조향 `SpringJoint`의 `maxDistance`를 물리적 도달 한계 기준으로 재검산했지만
/// (`SteerMaxPhysicalReach≈0.4048`, `SteerHandleMaxDistance≈0.3441`), 실제로는 여전히 구가 밀어도
/// 투석기가 거의 안 움직였다 — 이번엔 좌표가 아니라 **힘**을 계산해 원인을 찾았다.
///   최대 변위 = SteerMaxPhysicalReach − SteerHandleMaxDistance ≈ 0.4048 − 0.3441 = 0.0607
///   최대 견인력 = spring(6000) × 0.0607 ≈ 364(Unity 힘 단위)
///   투석기 무게 = mass(150) × g(9.81) ≈ 1471.5
///   정지마찰을 깨는 데 필요한 힘 = 1471.5 × 정지마찰계수(기본 PhysicMaterial, 0.6) ≈ 883
/// **364 < 883 — 구가 SpringJoint로 낼 수 있는 최대 견인력이 애초에 투석기의 정지마찰을 이길 수
/// 없었다.** 좌표 계산(9~11차 개편)은 "장력이 걸리는가"만 확인했지, "그 장력이 실제로 투석기를
/// 움직일 만큼 큰가"는 검토한 적이 없었다.
/// **수정 — 6차 개편의 설계 철학(당기는 방향으로만 반응하는 게 물리 엔진 자체가 보장해야 한다)을
/// 지키는 선에서, 바닥과 닿는 콜라이더(받침대 상판·바퀴)에 저마찰 `PhysicMaterial`을 적용했다**
/// (`LowFrictionCoefficient=0.05`) — "구를 조향할 때만 투석기 질량을 무시한다"는 식의 특수 스크립트
/// 분기는 만들지 않았다(그러면 물리 엔진이 아니라 스크립트가 "언제 미끄러워지는지"를 판정하는
/// 셈이라 6차 개편이 걷어낸 패턴으로 되돌아간다). 대신 "바퀴가 있어 구름 저항이 낮다"는 이미 있는
/// 설정(6차 개편 외형, 3번 항목의 바퀴 테마)과 자연스럽게 맞아떨어지는 물리적 설명을 골랐다 —
/// 마찰 자체가 항상 낮으므로 조향 중이 아닐 때도 똑같이 낮지만, 이건 "손수레는 원래 잘 미끄러진다"는
/// 세계관과 어긋나지 않는다.
///   재계산 — 필요 힘 = 1471.5 × 0.05 ≈ 73.6. **364 > 73.6(약 4.9배 여유, 필요 힘이 최대 견인력의
/// 약 20%만 차지)** — 이제 최대 견인력이 정지마찰을 확실히 이긴다.
/// `frictionCombine = Minimum`으로 둬 지형 쪽 PhysicMaterial이 무엇이든 결합 마찰이 이 낮은 값을
/// 넘지 않게 했다(바닥에 별도 PhysicMaterial이 없으면 Unity 기본값 0.6과 평균(Average)이 아니라
/// 최소(Minimum)를 적용해, 이 계산이 바닥 설정과 무관하게 항상 보장되게 한다).
/// **왜 mass/spring을 올리는 대신 마찰을 낮췄나.** 마찰은 이번에 처음 추가하는 값이라 다른 곳에
/// 영향이 없다 — `mass=150`은 6차 개편이 3배 확대와 함께 정한 값이고, `spring=6000`은 여러 라운드에
/// 걸쳐 조향 손잡이 반경/두께와 얽혀 재검산된 값이라, 둘 중 하나를 건드리면 이미 확정된 다른 계산
/// (파묻힘 검산, `SteerHandleMaxDistance` 등)까지 연쇄로 다시 검산해야 한다. 마찰은 독립 변수라
/// 가장 적은 파급으로 문제를 풀 수 있었다.
///
/// [13차 개편(2026-08-05, 재테스트 후 사용자 확정) — 조향 손잡이 고리를 4차 개편처럼 다시 트리거
/// 전용으로 (솔리드 벽 제거)]
/// 12차 개편의 저마찰 재질로도 "구가 밀어도 투석기가 안 움직인다"·"구가 링에 갇히면 점프로도 못
/// 나온다"는 두 증상이 재테스트에서 함께 재현됐다. 사용자가 재검산해 확정한 진짜 근본 원인은 좌표도
/// 마찰도 아니라 **여유 공간 자체**였다 — 9~12차 개편이 `maxDistance`를 거듭 좁혀 온 결과, 구가
/// "물리적으로 벽에 막히는 지점"에서 "SpringJoint 장력이 시작되는 지점"까지 실제로 이동할 수 있는
/// 거리가 겨우 `SteerMaxPhysicalReach(0.4048) − SteerHandleMaxDistance(0.3441) ≈ 0.0607`유닛뿐이었다.
/// `spring=6000`이 이 좁은 구간(≈0.06유닛) 안에서 구(질량 1.5)에 순간적으로 거는 가속도는
/// `6000×0.0607/1.5 ≈ 243 m/s²`로 중력(9.81)의 약 20배가 넘는다 — 투석기를 꾸준히 끄는 대신 구
/// 자신이 그 좁은 구간 안에서 튕겨 나가거나(밀어도 안 움직이는 것처럼 보임), 반대로 링 안에 들어간
/// 순간 이 강한 인장력에 갇혀 버렸다(점프로도 못 나옴 — `maxDistance`는 3D 유클리드 거리라 수직
/// 방향으로 벗어나려 해도 똑같이 강하게 되돌아온다).
/// **해법 — 물리적 벽 자체를 없애 이 좁은 여유라는 제약 조건을 지운다(사용자 명시적 확정).**
/// `CreateSteerRing`의 16개 세그먼트에서 콜라이더를 제거해(아래 함수 본문 참고) 다시 순수 시각
/// 마커로 되돌렸다 — 4차 개편이 정확히 같은 종류의 문제("반지름을 구 크기에 맞추면서 솔리드를
/// 유지하면 물리 여유가 너무 좁아 튀는 전형적인 패턴")를 겪고 트리거 전용으로 되돌린 선례를 그대로
/// 재현한다. 벽이 없으므로 이제 "구가 트리거 존 안에서 자유롭게 걸어 다니다가, 고리 중심에서
/// `maxDistance`보다 멀어지면 스프링이 걸린다"는 순수 SpringJoint 편도 구속 모델로 완전히
/// 정리된다 — 물리적 캡이 없으니 `maxDistance`를 "구가 도달 가능한 거리보다 작게" 역산할 필요
/// 자체가 사라졌다(9~12차 개편이 반복한 재검산 사이클의 근본 종료). 이 조치가 6차 개편의 판단
/// ("되먹임 위험 때문에 솔리드로 되돌렸다")을 뒤집는 건 아니다 — 6차 개편이 막으려던 문제(스크립트
/// 직접 이동 + 밀착 콜라이더의 되먹임)와 이번 문제(SpringJoint의 좁은 자유 구간에서의 폭력적 가속)는
/// 서로 다른 문제이고, 6차 개편 이후 이동 방식이 스크립트 대입에서 SpringJoint로 바뀌었다는 전제는
/// 여전히 유효하다.
/// **`CatapultSteerHandle.AttachJoint`/`DetachJoint`의 `enableCollision`/`ContinuousDynamic` 전환은
/// 그대로 남겨 뒀다(삭제하지 않았다) — 판단 근거는 `CatapultSteerHandle.cs` 상단 "13차 개편" 주석
/// 참고.** 요약하면: 고리 턱은 사라졌지만 `enableCollision=true`는 구가 조인트로 붙어 있는 동안
/// 여전히 투석기 전체 컴파운드 콜라이더(받침대·바퀴·트레슬·버킷 벽 등, 전부 여전히 솔리드다)와
/// 정상 충돌하게 해 준다 — 이게 꺼져 있으면 세게 당겨지는 동안 구가 투석기 본체를 그대로 통과할
/// 수 있어, 오히려 새 문제를 만들 위험이 있다. CCD는 그 충돌 판정이 얇은 지오메트리를 건너뛰지
/// 않게 보장하는 짝이라 함께 남겨 뒀다 — 조인트가 붙어 있을 때만 켜지므로 비용도 없다.
/// **씬 반영 — 재생성이 유일한 반영 경로다.** 기존 씬(`Assets/Scenes/SampleScene.unity`)에 배치된
/// 투석기는 16개 세그먼트가 이미 솔리드 콜라이더로 직렬화돼 있다 — 11차 개편의 턱 높이 재설계 때와
/// 정확히 같은 종류의 문제(개별 GameObject 구조/컴포넌트는 생성 시점에 결정되고, 씬 YAML의 fileID
/// 16개를 손으로 고치는 건 위험해 시도하지 않는다). `Tools > Catapult > Create Catapult`로 재생성해야
/// 이번 개편이 반영된다(코디네이터가 씬 파일을 직접 처리 — CatapultSystem/CLAUDE.md TBD 참고).
///
/// [18차 개편(2026-08-06) — 바퀴 콜라이더가 받침대를 침범하는 버그(`Catupult_bug6.png`), 근본 원인
/// 확정 + BoxCollider로 교체]
/// **원인 — CapsuleCollider가 얇은 원판을 표현하지 못하는 물리 엔진 자체의 한계.** `CreateWheel`이
/// `PrimitiveType.Cylinder`를 만들면 Unity가 자동으로 `CapsuleCollider`를 붙인다(원통 전용 콜라이더
/// 타입이 없어 캡슐로 근사하는 Unity의 알려진 관례). 우리 바퀴는 두께(thickness)가 지름
/// (2×radius)보다 훨씬 얇은데, Unity는 캡슐의 "world height"가 "2×world radius"보다 작으면 원통
/// 구간 길이를 0으로 clamp한다 — **결과가 반지름과 같은 반지름의 완전한 구**가 된다(현재 상수
/// 기준: `radius=0.5×Scale=1.5`, `thickness=0.18×Scale=0.54 < 2×1.5=3.0` → clamp 발동, 스크린샷의
/// 초록 와이어프레임 구와 정확히 일치). 이 구가 바퀴 축(로컬 Y→회전 후 세계 X) 방향으로 반지름만큼
/// 양쪽으로 부풀어, 얇은 시각 바퀴보다 훨씬 안쪽까지 파고들어 받침대/트레슬을 관통한다. **`radius`/
/// `height`를 명시적으로 다시 세팅해도 이 clamp 자체는 피할 수 없다** — 우리가 의도한 값(height=
/// thickness, radius=radius)을 그대로 넣어도 물리 엔진이 같은 clamp를 다시 적용해 결과가 바뀌지
/// 않는다(계산으로 확인 — 명시적 세팅이 근본 해결책이 아니었다).
/// **수정 — CapsuleCollider를 지우고 BoxCollider로 교체한다(`CreateWheel` 참고).** 원통 프리미티브
/// 메시 자신의 로컬 바운딩 박스(반지름 방향 X/Z ±0.5, 축 방향 Y ±1)와 정확히 같은 `size=(1,2,1)`을
/// 잡으면, 이미 회전+스케일이 적용된 Transform 위에서 시각 메시와 동일한 세계 좌표 크기(지름×지름×
/// 두께)가 나온다 — clamp 문제 자체가 없는 형태라 구가 아니라 실제 얇은 상자로 남는다. 11차 개편의
/// "장식 콜라이더를 지우지 않는다"는 결정과 충돌하지 않는다 — 콜라이더 자체를 없애는 게 아니라
/// 종류/크기만 정확히 바로잡았을 뿐이다.
/// **바퀴-받침대 배치 상수(`xOffset`)는 조정할 필요가 없었다 — 검산 결과 이미 정확했다.** BoxCollider가
/// 시각 메시와 정확히 같은 두께를 갖게 되면서, 기존 `xOffset = BaseHalfX + thickness·0.5 + 0.05·Scale`
/// 공식이 처음부터 가정했던 전제("콜라이더가 시각 메시만큼만 옆으로 파고든다")가 이제 실제로
/// 성립한다. 재검산(Scale=3 기준): `BaseHalfX=3.6`, `thickness=0.54`, `xOffset=3.6+0.27+0.15=4.02`
/// → 바퀴 콜라이더의 받침대 쪽 안쪽 면 = `xOffset − thickness·0.5 = 3.75`, 받침대 바깥 면
/// (`BaseHalfX=3.6`)보다 **+0.15 여유**(정확히 기존 마진 항 `0.05·Scale`과 일치) — 겹치지 않는다.
/// 사용자가 "바퀴 중심 기준으로 받침대 위치를 조정하는 것을 고려할 만하다"고 제안했지만, 콜라이더
/// 종류를 바로잡은 것만으로 이미 여유가 확보돼 별도 좌표 조정은 불필요했다(축/높이 배치도 함께
/// 검산 — 바퀴 콜라이더의 세계 Y 범위는 `[0, 2·radius]`로 바퀴가 항상 지면에 닿고, 받침대와는
/// 수평(X)으로만 인접하므로 수직 겹침 자체가 없다).
/// **조향 무반응 미스터리와의 연관성은 불확실 — 억지로 연결짓지 않는다.** 이 콜라이더 교체가 조향
/// (구가 투석기를 끄는 문제, 9~17차 여덟 라운드째 재현)에 영향을 줄 수도 있다는 것은 이론상 배제할
/// 수 없다(바퀴 콜라이더가 투석기 루트의 컴파운드 콜라이더 일부이므로, 형태가 완전히 다른 구에서
/// 얇은 상자로 바뀌면 물리 계산이 조금은 달라진다) — 그러나 이번 라운드가 그 가설을 검증한 것은
/// 아니다(미검증, § 아래 TBD 참고). 조향 미스터리 자체를 추가로 파고드는 것은 사용자가 이번엔
/// 요청하지 않아 범위 밖으로 남겨 뒀다.
///
/// [19차 개편(2026-08-06) — 발사 경로를 막는 벽 하나(F)만 좌표로 확정해 제거, 나머지 3면(B/L/R)은
/// 유지 + 탑승 유효 판정을 중앙 0.8배 구역으로 제한(사용자 확정, 최초 지시 정정)]
/// **증상 — 발사 시 정육면체가 버킷 벽 윗부분에 걸려 궤적이 망가진다.** 벽은 원래 2차 개편("옆에
/// 스치기만 해도 탑승되던 버그")을 막으려 도입됐고 6차 개편이 그 방지 목적으로 벽 높이를 다시
/// 올린 적도 있다 — 그 이유 자체는 여전히 유효하다. 처음엔 벽 4면을 전부 제거했었으나, 사용자가
/// "발사될 때 정육면체 궤적을 실제로 막는 벽 딱 하나만" 없애는 것이었다고 정정해 3면(B/L/R)을
/// 되살리고 F 하나만 남겨 제거했다.
/// **어느 벽인지 좌표로 확정 — 직관("당연히 F겠지")이 아니라 계산으로 검증했다.** `CatapultArm.
/// Fire()`의 발사 방향은 `dir = Quaternion.AngleAxis(launchPitch, aimRoot.right) * (-aimRoot.
/// forward)`(15차 개편)이고, 발사(분리)는 항상 팔이 `restAngle`(=0°, 11차 개편 확정)에 도달한
/// 순간에 일어난다(17~18차 개편, `ArmState.Launching` 램프 종료 시점). 아이룻 로컬 프레임에서
/// `-forward = (0,0,-1)`을 `launchPitch`(=50°)만큼 `right` 축으로 회전하면(Unity의 X축 회전
/// 행렬 `Y'=y·cosθ-z·sinθ, Z'=y·sinθ+z·cosθ`, FPS 피치 관례로 부호를 실측 검증) `dir_root =
/// (0, sin50°, -cos50°) ≈ (0, 0.766, -0.643)` — 아이룻 로컬 -Z(앵커 쪽)로 나가며 위로 꺾인다.
/// 이 방향을 버킷 "그룹" 자신의 로컬 좌표계로 역변환해야 한다 — 그룹은 armPivot 회전(θ)과 자신의
/// 추가 회전(`BucketGroupLocalRotation`, 로컬 X -90°)을 함께 입어(7차 개편), 같은 X축이라 두
/// 회전이 각도로 합산된다(θ-90°). θ=restAngle=0°일 때 이 합성 회전은 정확히 -90°이고, 그
/// 역변환(+90° 회전)을 `dir_root`에 적용하면 그룹 로컬 좌표
/// `dir_group = (Δx=0, Δy=+cos(launchPitch), Δz=+sin(launchPitch)) ≈ (0, 0.643, 0.766)`이 나온다
/// (유도: `Y_local=-Z_root, Z_local=Y_root`, `CatapultMenuItem.cs`가 이미 검증해 온 "그룹 로컬
/// (Δy,Δz)→root 좌표" 공식(위 "7차 개편 (2)" 참고)의 역변환과 정확히 일치 — root_Y=(term1)·cosθ+
/// (term2)·sinθ 공식에 θ=0을 대입하면 root_Y=term1, root_Z=term2가 되는 것으로 교차 검산했다).
/// **`dir_group`의 부호를 벽 배치와 대조한다.** `_B`는 그룹 로컬 -Z, `_F`는 +Z, `_L`/`_R`은 ∓X —
/// `dir_group`은 `Δx=0`(L/R과 무관), `Δz=+0.766>0`(F 쪽), `Δy=+0.643>0`(개방된 위쪽, 벽 없음)이다.
/// 탑승자 시작 위치는 바닥 근처(`Δy≈-half.y`, `Δx=Δz≈0`, `ComputeBoardTargetPosition` 기준)이므로,
/// 광선 `(Δy,Δz) = (-half.y, 0) + t·(0.643, 0.766)`를 F 벽 안쪽 면(`Δz=half.z`)까지 진행시키면
/// `t=half.z/0.766≈3.33`, 그때 `Δy≈-half.y+3.33·0.643≈0.04` — 이는 벽 상단(`Δy=wallCenterY+
/// height/2`)에 한참 못 미치는 낮은 높이라, 광선이 열린 위쪽으로 빠져나가기 훨씬 전에 F 벽의
/// Δz 범위에 먼저 진입한다(위 상단으로 빠져나가는 지점은 `t≈7.0`으로 F 진입보다 두 배 이상
/// 늦다). 즉 **F가 발사 경로를 실제로 가로막는 벽이다** — B(`Δz`가 반대 부호라 광선이 닿지 않음)·
/// L/R(`Δx=0`이라 광선이 전혀 접근하지 않음)은 발사 경로와 무관하다.
/// **수정 — `CreateBucket`에서 `CreateBucketWall("Catapult_BucketWall_F", ...)` 호출 하나만
/// 삭제했다.** B/L/R 세 호출과 `CreateBucketWall` 함수 자체는 그대로 남겨 "옆에 스치기만 해도
/// 탑승"되던 2차 버그 방지 역할을 계속한다. 바닥(`Catapult_BucketFloor`)은 원래도 제거 대상이
/// 아니었다.
/// **탑승 유효 범위를 중앙 0.8배로 제한하는 거리 게이트(`IsWithinCentralBoardZone`)는 그대로
/// 유지한다(사용자 확정 — 게이트 자체는 잘못 지시한 게 아니었다, 벽 개수만 정정한다).** F 벽이
/// 사라진 방향에서는 여전히 걸어서/스쳐서 들어올 수 있는 여지가 생기므로, 이 거리 게이트가 그
/// 방향의 안전장치 역할을 한다 — 나머지 B/L/R 방향은 벽이 물리적으로 막고, F 방향은 게이트가
/// 논리적으로 막는 이중 구조다. 콜라이더 크기 자체를 줄이지 않은 이유는 여전히 유효하다 —
/// `ComputeBoardTargetPosition()`이 내부 트리거의 `box.center`/`box.size`를 직접 읽어 바닥
/// 위치를 계산하므로, 콜라이더를 줄이면 그 계산이 함께 어긋난다.
/// **부작용이자 의도된 방향 — F 쪽에서는 이제 걸어 들어오는 탑승도 0.8배 구역 안에서는 물리적으로
/// 가능하다.** 이건 부작용이 아니라 사용자가 명시적으로 받아들인 "탑승이 편해짐"이라는 트레이드
/// 오프다.
/// **발사 궤적 파묻힘 재검산은 필요 없다** — F 벽이 없어져 그 방향에는 충돌할 지오메트리가
/// 없으므로, F 쪽으로 벽에 걸리는 시나리오가 구조적으로 성립하지 않는다. B/L/R은 이번 라운드에
/// 손대지 않아 기존 파묻힘 검산(각 개편 항목 참고)이 그대로 유효하다.
/// **씬 반영 — 재생성이 유일한 반영 경로다(늘 그래왔듯).** F 벽 GameObject는 이미 씬에 배치된
/// 기존 투석기 인스턴스에 그대로 남아 있다 — 코드는 "다음 생성부터 F를 안 만드는" 것만 바꿀 뿐,
/// 기존 씬 파일(`SampleScene.unity`)은 이 라운드에서 손대지 않았다(코디네이터가 직접 처리 —
/// 하드룰).
///
/// [21차 개편(2026-08-06) — 조향 SpringJoint 폐기, 손잡이를 순수 충돌 기반 솔리드 블록으로 재설계]
/// SpringJoint 조향이 9~20차(12라운드) 내내 실패해 사용자가 방식 자체를 폐기했다 — 자세한 실패
/// 이력은 `CatapultSteerHandle.cs` 상단 "21차 개편" 주석/`CatapultSystem/CLAUDE.md` 참고. 이제
/// 손잡이는 조인트도 트리거도 없는 평범한 솔리드 콜라이더 하나다 — 구가 부딪히면 표준 강체 충돌이
/// 알아서 힘/토크를 투석기에 전달한다. **고리(16세그먼트 시각 + 별도 트리거 판정)를 유지할 이유가
/// 사라졌다** — SpringJoint의 "구멍에 넣어야 당겨진다"는 구도 자체가 없어졌으므로, `CreateSteerRing`
/// 함수와 그 전용 상수(`SteerZoneRadius`/`SteerRingTubeRadialThickness`/`SteerRingScale`/
/// `SteerRingTubeHeightScale`/`SteerRingFinalYScale`/`SteerHandleMaxDistance`/
/// `SteerRingTubeTargetWorldHeight`/`SteerRingTubeHeightLocal`/`SteerRingLocalZ`/
/// `SteerRingLocalPosition`)를 전부 삭제하고, 막대(Rod, 11차 개편부터 이미 솔리드) 끝에 손잡이
/// 블록(`Catapult_SteerHandle_Grip`, 단일 `BoxCollider`, 솔리드) 하나만 새로 둔다 — 막대 자신은
/// 그대로 재사용해(시각·콜라이더 모두 변경 없음) "기존 막대+고리 시각 요소를 최대한 재사용하되
/// 콜라이더 구조만 단순화한다"는 요청을 지켰다. 그립 위치는 옛 `SteerHandlePivotLocal`(막대 블루프린트
/// 끝점)을 그대로 재사용했다 — 11차 개편이 링을 막대 끝에서 독립적으로 더 앞으로 옮겼던 조정
/// (`SteerRingLocalPosition`)은 SpringJoint의 물리적 여유 재검산 때문이었는데, 이제 그 재검산 자체가
/// 필요 없어져 다시 막대 끝과 일치시켰다.
/// **`CatapultSteerHandle`의 "구 전용" 역할 게이트도 트리거 판정(`OnTriggerEnter`/`Kind` 검사)에서
/// `Physics.IgnoreCollision`(컴포넌트 `Start()`, 씬의 모든 `PlayerShapeIdentity` 중 Sphere가 아닌
/// 것들의 콜라이더와 이 그립 콜라이더 사이의 충돌을 끈다)로 바뀌었다** — 정육면체/정사면체가 이
/// 손잡이를 물리적으로 투과해야 조준(구 전용)과 장전(정사면체 전용)이 서로 간섭하지 않는다. 새 물리
/// 레이어를 추가하지 않고 스크립트만으로 처리해 프로젝트/씬 세팅을 건드리지 않았다.
/// **"당기면만 반응한다"는 6차 개편 이후의 원칙은 이번엔 일부러 넣지 않았다(사용자 명시적 확정)** —
/// 미는 것도 당기는 것도 둘 다 물리적으로 반응하는 단순한 충돌 응답이 이번 시도의 전체다. 이 방식이
/// 안 되면 다음 대안은 5차 개편의 "거리→비율→velocity+MoveRotation" 스크립트 방식으로 폴백하기로
/// 이미 사용자와 합의됐다(이번 라운드에서 폴백을 미리 구현하지 않는다).
/// `CatapultArm.aimRoot`/발사 방향 계산은 조향 메커니즘이 뭐든 무관하게 항상 성립해 이번 라운드에서
/// 전혀 건드리지 않았다.
/// **씬 반영 — 재생성이 유일한 반영 경로다.** 기존 씬에 배치된 투석기는 여전히 16세그먼트 고리
/// GameObject를 물고 있다 — `Tools > Catapult > Create Catapult`로 재생성해야 이번 개편이 반영된다
/// (코디네이터가 씬 파일을 직접 처리 — 하드룰).
///
/// [22차 개편(2026-08-06) — 조향을 "충돌 기반"에서 "도킹 후 직접 조작"으로 전면 재설계]
/// 21차는 씬에서 한 번도 테스트되지 못한 채 사용자가 방향을 완전히 바꿨다 — 조준을 물리 반응이
/// 아니라 **구가 손잡이 근처에서 C를 눌러 도킹하고, 도킹된 동안 좌우 입력으로 투석기를 직접
/// 회전시키는** 방식으로 확정했다. `CatapultSteerHandle`의 자세한 설계 근거는 그 파일 상단 주석
/// 참고 — 요약하면 `ThreadPinPlacer`의 벽 부착(`isKinematic` 완전 고정)·`CatapultBucket`의 C키
/// 탑승(거리 기반 판정)·5차 개편의 `Rigidbody.MoveRotation`(논카인매틱 Rigidbody 위 스크립트
/// 회전)을 조합한 것이라 이 파일이 이미 신뢰하는 패턴 셋을 그대로 재사용한다.
/// **이 파일에서 바뀐 것 — `CreateSteerHandle`이 만드는 물건과 그것을 붙이는 대상.** 21차의 솔리드
/// 그립 블록(`Catapult_SteerHandle_Grip`, `BoxCollider` 하나)을 완전히 지우고, 대신 콜라이더가
/// 전혀 없는 N세그먼트 순수 시각 고리(`Catapult_SteerHandle_Ring`, `CreateSteerRingVisual`)로
/// 바꿨다 — 6차 개편의 "손수레 손잡이 구멍"(수평 XZ 평면 도넛) 외형을 다시 참고했지만, 이번엔
/// 트리거도 물리 충돌도 없는 순수 장식이라 세그먼트 수를 16→12로 줄였다(정밀도가 덜 중요하다,
/// 감각적 판단 — `[TBD, 임시값]`). `CatapultSteerHandle` 컴포넌트는 더 이상 이 고리(또는 어떤
/// 손잡이 오브젝트)에 붙지 않는다 — 물리 충돌을 하나도 다루지 않는 순수 상태머신+회전 스크립트가
/// 되며 `CatapultLoadController`/`Rigidbody`와 같은 자리(투석기 루트 자신)로 옮겨졌다(위 계층도
/// 참고). `CreateCatapult`가 `root.AddComponent&lt;CatapultSteerHandle&gt;()`로 붙인 뒤
/// `dockAnchor`(고리 Transform)와 `rootBody`(투석기 루트의 Rigidbody, `arm.aimRoot`와 똑같은
/// 방식)를 연결한다.
/// **`SteerHandleGripSize` 상수는 삭제하고 `SteerRingRadius`/`SteerRingTubeThickness`/
/// `SteerRingSegmentCount`로 교체했다** — 물리 도달 거리를 역산해야 했던 9~21차의 여러 재검산
/// 사이클(`SteerHandleMaxDistance`, `SteerMaxPhysicalReach` 등, 이제 전부 죽은 코드)과 달리, 이
/// 상수들은 순수 시각 크기일 뿐이라 별도 파생 계산이 없다 — 도킹 판정 자체는
/// `CatapultSteerHandle.dockRange`가 거리로 담당한다(고리 크기와 독립).
/// **`SteerHandlePivotLocal`(Rod/Ring 공유 좌표)은 그대로 재사용했다** — 위치 자체는 바뀔 이유가
/// 없어(막대 끝에 손잡이가 있다는 구도는 그대로다), 값을 다시 튜닝하지 않았다.
/// **씬 반영 — 재생성이 유일한 반영 경로다.** 기존 씬에 배치된 투석기는 여전히 21차의 솔리드
/// 그립 블록(`Catapult_SteerHandle_Grip`)을 물고 있다 — `Tools > Catapult > Create Catapult`로
/// 재생성해야 이번 개편이 반영된다(코디네이터가 씬 파일을 직접 처리 — 하드룰).
///
/// [23차 개편(2026-08-06) — 22차 재생성 후 재테스트 피드백 3건: 조향 링 외형/막대 관통, 도킹 중
/// 서서히 -Y로 도는 버그, 당김 앵커를 버킷 그룹에 부모화+납작하게]
/// (1) `CreateSteerRingVisual`의 각 세그먼트가 `Quaternion.LookRotation(dir, Vector3.up)` 뒤에
/// 스케일 축을 반대로 줬다 — 호 길이(`segmentLength`)를 반지름 방향(로컬 Z)에, 두께를 접선 방향
/// (로컬 X)에 줘서 중심에서 바깥으로 뻗는 가는 꽃잎 12개처럼 보였다("링이 원 모양을 안 띈다"는
/// 지적의 실체). 두 축을 맞바꿨다(`CreateSteerRingVisual` 루프 안 주석 참고) — 세그먼트 개수·
/// 여유 비율(1.15배)은 손대지 않았다. 막대(Rod)도 예전엔 끝점이 고리 **중심**(`SteerHandlePivotLocal.z`)
/// 까지 뻗어 있어 시각적으로 고리를 관통하는 것처럼 보였다 — 끝점을 고리 테두리 안쪽 면
/// (`SteerHandlePivotLocal.z - SteerRingRadius + SteerRingTubeThickness*0.5`)까지로 줄여 막대가
/// 고리 테두리에 맞닿아 끝나도록 고쳤다(`CreateSteerHandle` 참고, dockAnchor 위치 자체는 불변).
/// (2) `CatapultSteerHandle.FixedUpdate()`가 매 스텝 `rootBody.rotation.eulerAngles.y`(물리
/// 시뮬레이션이 만든 실제 값)를 다음 목표 요의 기준으로 다시 읽고 있었다 — 조향 입력이 0이어도
/// 바퀴 접지 노이즈 등으로 생긴 미세한 드리프트가 그대로 "다음 목표"로 재확인돼, 도킹된 채
/// 정지해 있어도 서서히 -Y로 계속 도는 버그였다. `dockedYawOffset`(baseYaw 기준 오프셋, 스크립트
/// 자신이 유일하게 소유)을 도입해 `rootBody.rotation`을 더 이상 계산 입력으로 읽지 않고, 도킹
/// 중엔 매 스텝 `rootBody.angularVelocity`도 `Vector3.zero`로 강제한다(`MoveRotation`과 같은
/// 스텝에 남은 시뮬레이션 각속도가 다음 프레임 자세를 더 미는 것 방지) — 자세한 근거는
/// `CatapultSteerHandle.cs` 상단 "23차 개편" 주석 참고. 도킹하지 않은 동안은 이 강제 리셋 자체가
/// 실행되지 않아 기존 물리 거동(충돌 후 안착 등)에 영향이 없다.
/// (3) 당김 앵커(`Catapult_PullAnchor`)를 투석기 루트의 고정 오프셋에서 버킷 **그룹**(armPivot의
/// 자식 `Catapult_Bucket`, B 벽과 같은 로컬 좌표계)의 자식으로 옮겼다 — 사용자 요청: 팔이 당겨질
/// 때 앵커도 버킷과 함께 움직여야 "바구니/지레를 직접 뒤로 당기는" 느낌이 난다. `CreateBucket`이
/// 이미 계산해 둔 `half`/`wallT`/`wallCenterY`를 그대로 재사용해 B 벽 바로 뒤(그룹 로컬 -Z)에
/// 배치했다(새 좌표계를 유도하지 않았다) — 이 때문에 `CreateAnchor`의 호출 지점이 `CreateBucket`
/// 안쪽으로 옮겨졌고(그룹 Transform은 그 함수 스코프 안에서만 얻을 수 있다), `CreateBucket`이
/// `out GameObject anchor`로 반환한다. 형태도 균일 스케일 구에서 로컬 Z(그룹의 -90°X 회전 + rest
/// 각도에서 세계 Y로 매핑되는 축, `CreateAnchor` 주석 참고)만 1/3로 눌러 납작한 원반으로 바꿨다 —
/// "당김줄을 거는 고리"로 읽히길 원한다는 요청 반영, 6차 개편의 "손수레 손잡이 구멍" 모티프와 같은
/// 방향(수평으로 눕는 형태)으로 재검산해 골랐다. 콜라이더 없는 순수 위치 마커라는 성질은 그대로다
/// — `CatapultPullAnchor.cs`는 전혀 수정하지 않았다.
/// **이건 순수 미용 변경이 아니다 — 앵커가 이제 팔 스윙을 따라 실제로 움직인다.**
/// `CatapultLoadController`는 `anchor.transform.position`을 연결 판정(최초 C, 거리 게이트)과 실
/// 시각화(매 프레임)에만 읽고 연결 유지에는 더 이상 거리를 쓰지 않는다(9차 개편으로 마우스 휠이
/// 대체) — 그래서 앵커가 연결 도중 움직여도 끊기지 않는다(코드로 확인, `CatapultLoadController.cs`
/// 참고). 다만 `CatapultPullAnchor.connectRange`(9f)는 옛 "루트 기준 고정 오프셋" 위치를 전제로
/// 튜닝된 값이라, 앵커가 버킷 높이(지상에서 상당히 높다)까지 따라 올라간 지금도 여전히 적당한지는
/// 미검증이다 — 아래 TBD 참고.
/// **씬 반영 — 재생성이 유일한 반영 경로다.** 기존 씬에 배치된 투석기는 22차의 링 축 버그·관통
/// 막대·루트 고정 앵커를 그대로 물고 있다 — `Tools > Catapult > Create Catapult`로 재생성해야
/// 이번 개편이 반영된다(코디네이터가 씬 파일을 직접 처리 — 하드룰).
/// </summary>
public static class CatapultMenuItem
{
    private const string SystemFolder = "Assets/CatapultSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    // 6차 개편(2026-08-04) — 투석기 전체 균등 확대 배율. 이 파일의 모든 길이/위치 상수는
    // "(pre-scale 값) * Scale" 형태로 선언한다(각도는 스케일 대상이 아니다 — CatapultArm의
    // restAngle/pulledAngle은 이 상수와 무관).
    private const float Scale = 3f;

    // 팔 블루프린트(회전 전 로컬 +Y 기준) 길이. armVisual/bucket 오프셋과 버킷 높이 계산이 이 값을
    // 공유한다. pre-scale 1.0→1.4로 늘렸다(6차 개편, 버킷 확대에 따른 파묻힘 여유 확보 — 클래스
    // 상단 주석 참고).
    private const float ArmLength = 1.4f * Scale;
    private const float BaseTopY = 0.2f * Scale;   // Catapult_Base 윗면(로컬 y=scale.y/2, scale.y=BaseTopY → top = BaseTopY)
    private const float ApexY = 0.6f * Scale;      // 트레슬 정점(= armPivot 높이, = Axle 높이)
    private const float BaseHalfX = 1.2f * Scale;  // Catapult_Base 폭 절반
    private const float BaseHalfZ = 1.3f * Scale;  // Catapult_Base 깊이 절반 — 조향 손잡이(앞쪽) 시작점 계산용

    // 바구니(벽+바닥) 치수. 전부 버킷 "그룹 자신의" 로컬 좌표 기준(그룹 자체의 위치/회전/스케일은
    // 아래 BucketGroupLocalY 등 별도 상수 — 7차 개편으로 armPivot 로컬 (0, ArmLength, 0)이라는 옛
    // 단순 오프셋에서 바뀌었다) — 클래스 상단 주석의 파묻힘 검산이 이 값들을 그대로 쓴다. 값을
    // 바꾸면 그 검산도 다시 해야 한다. 6차 개편(정육면체 1×1×1 기준 확실한 여유로 재설계, pre-scale):
    // X 0.5→0.9(옆면 여유 0.4), Y 0.2→0.7(위아래 여유 각 0.2), Z 0.4→0.85(옆면 여유 0.35) — 클래스
    // 상단 주석 참고.
    private static readonly Vector3 BucketInnerHalf = new Vector3(0.9f, 0.7f, 0.85f) * Scale;
    private const float BucketWallThickness = 0.1f * Scale;
    private const float BucketFloorThickness = 0.1f * Scale;
    private const float BucketWallHeight = 1.5f * Scale; // 내부 트리거 천장(2×half.y)보다 확실히 높게 —
                                                          // "옆에 스치기만 해도 탑승"되던 2차 버그 재발 방지.
                                                          // 19차 개편(2026-08-06)부터 B/L/R 3면에만 적용된다(F는 제거).

    // 7차 개편(2026-08-05, 5차 플레이테스트 지적 — "버킷 탑승 시 투석기+정육면체가 폭발하듯 튕겨
    // 나간다") — 버킷 "그룹" 자체의 Transform. 벽/바닥/내부 트리거 치수(BucketInnerHalf 등, 위)는
    // 전혀 건드리지 않았다 — 근본 원인은 그룹이 정육면체(이 기믹의 Scale과 무관하게 고정 1×1×1)보다
    // Scale=3배 커진 캐비티를 그대로 감싼 채 배치돼 있던 크기 불일치였다. 사용자가 씬에서 직접
    // 드래그해 "실제 투석기와 비슷하게 ArmVisual_Upper 끝에"(레퍼런스 사진의 슬링처럼) 자리 잡을
    // 때까지 맞춘 값을 그대로 코드에 반영했다(SampleScene.unity 확인, 2026-08-05) — 정확한 Y·회전·
    // 축소 비율의 물리적 근거는 없다(감각적 판단, 씬에서 검증된 값으로 신뢰. 클래스 상단 주석 "7차
    // 개편" 참고).
    private const float BucketGroupLocalY = 1.78f * Scale; // ≈5.34 — ArmVisual_Upper 끝(로컬 Y 2.31~4.2) 지나.
    private static readonly Quaternion BucketGroupLocalRotation = Quaternion.Euler(-90f, 0f, 0f);
    // 씬에 실제로 저장된 값은 로컬 X -84.37°(손 드래그로 생긴 오차)였지만, 사용자 의도는 정확히
    // -90°다 — 이 파일의 다른 정밀 각도(restAngle 등)와 달리 유도 근거가 되는 상수가 없는 임의값이라,
    // 오차를 그대로 베이크하지 않고 의도한 값으로 정리했다.
    private const float BucketGroupLocalScale = 0.43f;
    // 감각적 판단(씬 확정값) — 사용자의 구두 지시는 "0.5"였으나 실제로 씬에 저장되고 플레이테스트된
    // 값은 0.43이다. 확신이 안 서면 씬을 확인해 그 값을 신뢰한다는 이 프로젝트의 관례에 따라 0.43을
    // 채택했다(0.5로 반올림하지 않는다). Scale(투석기 전체 배율)과는 독립된 별도 계수라, Scale이
    // 나중에 바뀌면 이 값도 함께 재튜닝이 필요할 수 있다(씬 튜닝 전제 [TBD, 임시값]).

    // 조향 손잡이(구 전용) 치수. Catapult 루트 로컬 좌표 기준.
    // 7차 개편(2026-08-05, 5차 플레이테스트 지적 — "고리가 너무 높아서 구가 못 닿는다") — Y만
    // 언스케일 처리했다. 구 콜라이더 반지름은 `PlayerObjectMenuItem.cs:234`에서 고정 0.5(이 기믹의
    // Scale과 무관 — 구 오브젝트 자체는 카타풀트를 따라 커지지 않는다). 평지에 선 구의 중심은 항상
    // 월드 Y≈0.5(반지름만큼 떠서 접지)이고 도달 가능한 수직 범위는 0~1.0뿐이다. Z(막대 길이)는
    // 여전히 투석기 구조물 자체의 공간적 치수라 Scale을 그대로 곱한다.
    // 11차 개편(2026-08-05, 사용자가 씬에서 다시 손으로 조정) — Y를 0.5f에서 0.235f로 낮췄다.
    // 21차 개편(2026-08-06) — 고리(Ring)가 그립(Grip) 블록으로 대체되면서, 그립도 다시 이 값을
    // Rod와 공유한다(SpringJoint 재검산 때문에 11차 개편이 링을 막대 끝에서 독립적으로 더 앞으로
    // 옮겼던 조정은 그 재검산 자체가 필요 없어져 되돌렸다 — 아래 "21차 개편" 항목 참고).
    private const float SteerHandlePivotY = 0.235f; // 언스케일 — Rod/Ring 공유 Y.
    private static readonly Vector3 SteerHandlePivotLocal = new Vector3(0f, SteerHandlePivotY, 2.0f * Scale);
    // 22차 개편(2026-08-06) — 조향이 "도킹 후 직접 조작" 방식으로 바뀌며 그립 블록(솔리드
    // BoxCollider)이 사라지고 순수 시각 고리(Ring)로 대체됐다(클래스 상단 "22차 개편" 주석 참고).
    // 옛 `SteerHandleGripSize`(0.7*Scale, 손으로 쥐는 블록 크기)와 비슷한 감으로 반지름을 잡았다 —
    // 콜라이더가 전혀 없어 물리 도달 거리를 신경 쓸 필요가 없다(도킹 판정은
    // `CatapultSteerHandle.dockRange`가 거리로 대신한다, 씬 튜닝 전 [TBD, 임시값]).
    private const float SteerRingRadius = 0.5f * Scale;
    private const float SteerRingTubeThickness = 0.12f * Scale;
    // 6차 개편의 16세그먼트 도넛 근사와 같은 발상 — 순수 장식이라 정밀도가 덜 중요해 개수만 줄였다.
    private const int SteerRingSegmentCount = 12;

    // 12차 개편(2026-08-05) — 조향 시 투석기가 안 움직이는 문제의 근본 원인은 지면 마찰이었다(정지
    // 상태를 깨는 데 필요한 힘이 조인트가 낼 수 있는 최대 견인력보다 컸다 — 클래스 상단 "12차 개편
    // (1)" 주석의 계산 참고). 목표는 "정지마찰을 깨는 데 필요한 힘 < 조인트 최대 견인력"이 확실히
    // 성립하는 마찰계수다. 정확한 수치는 감각적 판단 — [TBD, 임시값].
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
        Rigidbody rootBody = root.AddComponent<Rigidbody>();
        rootBody.useGravity = true;
        rootBody.isKinematic = false;
        // 6차 개편: 50→150(mass는 부피(27배) 아닌 선형(3배) 기준으로 스케일했다 — 옛 값도 "적당히
        // 무겁게"라는 감각적 판단이라 부피 그대로 27배(1350)로 키우면 SpringJoint 스프링 상수를
        // 처음부터 다시 튜닝해야 할 만큼 극단적으로 무거워진다. 선형 배율은 "커진 만큼 조금 더
        // 무겁게"라는 절충이다 — 씬 튜닝 전제 [TBD, 임시값], 문서(§6·§7)에도 근거를 남겼다.
        rootBody.mass = 150f;
        // 6차 개편 신규 — 조향 SpringJoint가 무력 상태(줄이 느슨)일 때 남은 관성을 엔진 자체가
        // 서서히 죽인다(옛 CatapultSteerHandle.releaseDamping 스크립트 감쇠를 대체).
        rootBody.drag = 1.2f;
        // 6차 개편 신규 — SpringJoint가 고리(pivotAnchor, 무게중심에서 앞으로 떨어진 지점)에 거는
        // 인장력은 오프셋 때문에 자연히 토크(요 회전)를 만든다(CatapultSteerHandle.cs 상단 "회전"
        // 주석 참고) — 각속도 진동/오버슈트를 억제하려 각항력을 기본치보다 높였다.
        rootBody.angularDrag = 5f;
        rootBody.interpolation = RigidbodyInterpolation.Interpolate; // 시각적 떨림 완화.
        // 회전은 요(Y)만 조향에 쓰므로 X/Z는 얼린다.
        rootBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        // 9차 개편(2026-08-05) 신규 — 조향 손잡이 고리 턱 터널링의 근본 원인 중 하나였다
        // (CatapultSteerHandle.cs 상단 "9차 개편" 주석 참고). 이 루트는 고리 턱을 포함한 투석기
        // 전체 컴파운드 콜라이더의 소유자이자 다이나믹(논카인매틱) Rigidbody인데, 기본값
        // (Discrete)으로 두면 조인트로 붙은 구 쪽을 ContinuousDynamic으로 올려도 Unity가 이 루트를
        // "스윕 대상"으로 취급하지 않아 CCD 자체가 무력하다(Unity 규칙 — ContinuousDynamic 바디는
        // 정적 콜라이더/Continuous/ContinuousDynamic 상대에만 스윕 검사를 한다). Continuous로
        // 두면 "다른 빠른 동적 바디가 부딪힐 수 있는 상대적으로 느린 큰 구조물" 역할에 맞다(구처럼
        // 스스로 빠르게 움직이는 발사체가 아니므로 ContinuousDynamic이 아니라 Continuous를 썼다).
        rootBody.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // 12차 개편(2026-08-05) — 바닥과 닿는 콜라이더(받침대 상판·바퀴)에 적용할 저마찰 재질.
        // 조향 SpringJoint가 낼 수 있는 최대 견인력이 기본 마찰(정지마찰계수 0.6)을 이기지 못해
        // "밀어도 안 움직인다"는 문제의 근본 원인이었다 — 클래스 상단 "12차 개편 (1)" 주석 참고.
        PhysicMaterial lowFriction = CreateLowFrictionMaterial();

        CreateBase(root.transform, lowFriction);
        CreateWheels(root.transform, lowFriction);
        CreateTrestle(root.transform);
        GameObject armPivot = CreateArmPivot(root.transform);
        CreateArmVisual(armPivot.transform);
        CreateCounterweight(armPivot.transform);
        GameObject bucketInner = CreateBucket(armPivot.transform, out GameObject anchor); // 23차 개편 — 앵커도 여기서 생성(버킷 그룹에 부모화).
        GameObject steerRing = CreateSteerHandle(root.transform); // 22차 개편 — 반환값은 고리(Ring) GameObject.

        CatapultArm arm = armPivot.GetComponent<CatapultArm>();
        arm.armPivot = armPivot.transform;
        arm.aimRoot = root.transform; // 조향(루트 요 회전)과 발사 방향을 일치시킨다 — 팔의 장전 회전과 분리.
        arm.bucket = bucketInner.GetComponent<CatapultBucket>();

        loadController.anchor = anchor.GetComponent<CatapultPullAnchor>();
        loadController.arm = arm;

        // 22차 개편 — CatapultSteerHandle은 이제 손잡이(그립/고리)가 아니라 투석기 루트 자신에
        // 붙는다(CatapultLoadController/Rigidbody와 같은 자리 — 물리에 관여하지 않고 순수 상태
        // 머신+조향 회전만 담당하기 때문). aimRoot와 같은 방식으로 rootBody를 직접 연결한다.
        CatapultSteerHandle steerHandle = root.AddComponent<CatapultSteerHandle>();
        steerHandle.dockAnchor = steerRing.transform;
        steerHandle.rootBody = rootBody;

        Undo.RegisterCreatedObjectUndo(root, "Create Catapult");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = root;
        Debug.Log("[Catapult] 투석기 생성 완료(6차 개편 — 3배 확대). 정육면체는 위에서 뛰어들거나 " +
                   "근처에서 C를 눌러 버킷에 자동 탑승합니다(3배 확대로 점프만으로는 안 닿을 수 있음 — " +
                   "의도된 동작). Tab으로 구를 조작해 투석기 앞 손잡이 고리 근처에서 C를 누르면(22차 " +
                   "개편 — 도킹 후 직접 조작) 그 자리에 고정되고 커지며, 좌우 이동 입력으로 투석기를 " +
                   "직접 조향할 수 있습니다(다시 C로 해제). 정사면체는 당김 앵커 근처에서 C로 당김 " +
                   "줄을 연결/해제해 장전·발사합니다.");
    }

    private static GameObject CreateBase(Transform parent, PhysicMaterial groundMaterial)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObj.name = "Catapult_Base";
        baseObj.transform.SetParent(parent, false);
        baseObj.transform.localPosition = new Vector3(0f, BaseTopY * 0.5f, 0f);
        baseObj.transform.localScale = new Vector3(BaseHalfX * 2f, BaseTopY, BaseHalfZ * 2f); // top = BaseTopY.
        baseObj.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Base", new Color(0.55f, 0.4f, 0.22f));
        // 12차 개편 — 바닥과 실제로 닿는 상판에 저마찰 재질을 준다(클래스 상단 "12차 개편 (1)" 참고).
        baseObj.GetComponent<Collider>().material = groundMaterial;
        return baseObj;
    }

    // 장식용 바퀴(6차 개편 외형 추가) — "손수레" 인상을 강화한다(조향 메커니즘이 이미 손수레
    // 비유를 쓰고 있어 시각적으로도 뒷받침). 11차 개편(2026-08-05) — 실제 회전은 CatapultWheelVisual이
    // 순수 시각으로 담당하고(아래 CreateWheel 참고), 콜라이더는 6차 개편부터 잘못 지워지고 있었다
    // (아래 "11차 개편 — 장식 콜라이더 복구" 참고). 12차 개편 — 바퀴와 받침대를 잇는 축(장식) 신규 추가,
    // 바퀴 콜라이더에도 저마찰 재질 적용("바퀴라 마찰이 낮다"는 설정과 자연스럽게 맞아떨어진다).
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

    // 12차 개편(2026-08-05, 사용자 요청) — 바퀴와 받침대 사이를 잇는 축(순수 시각 장식). Catapult_
    // TieBeam/Catapult_TrestleStrut와 같은 생성 패턴을 재사용한다(로컬 X축이 이미 길이 방향이라
    // 회전 없이 scale.x만 늘린다 — CreateTieBeam과 동일한 관례). 콜라이더는 굳이 필요하지 않다고
    // 판단했다(YAGNI) — 받침대 옆면보다 낮은 위치(바퀴 축 높이)라 플레이어가 걸어 다니다 부딪힐
    // 일이 거의 없고, 부딪혀도 걷는 표면이 아니라 옆으로 스치는 정도라 위험이 낮다.
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

    // PR #54 코드검토 반영(2026-08-06, P1) — 콜라이더(고정)와 회전 메시(자식)를 분리한다. 예전엔
    // CatapultWheelVisual과 BoxCollider가 같은 Transform("Catapult_Wheel" 자신)에 있어, 시각 스크립트가
    // 매 프레임 그 Transform을 돌리면 컴파운드 콜라이더 형상까지 함께 돌았다 — "순수 시각 연출"이라는
    // 주석과 실제 동작이 어긋났다. 게다가 그 회전축(로컬 X, Space.Self)도 수학적으로 틀렸었다 —
    // Quaternion.Euler(0,0,90)으로 이미 기울여진 객체에 Space.Self로 로컬 X를 돌리면(사원수 합성을
    // 직접 전개해 확인: R0*Rx(θ)) 축(axle) 자체가 매 프레임 방향을 바꾸며 팽이처럼 도는 동작이 나온다
    // (θ=0에서 축이 -X를 향하다 θ=90°에서 +Z를 향함 — 굴러가는 게 아니라 넘어지듯 돈다). 지금은
    // "Catapult_Wheel"(부모, 콜라이더 전용, 절대 회전하지 않음)과 "Catapult_WheelMesh"(자식, 메시+
    // 회전 스크립트 전용)로 나눈다 — 자식은 부모의 고정 회전(R0)을 그대로 상속만 하고 자신의 로컬
    // 회전은 identity에서 시작하므로, 자식 자신의 로컬 Y축(메시의 원래 높이/축 방향)을 그대로 돌리면
    // 축 방향이 고정된 채(World = R0 * Ry(θ)) 올바르게 굴러간다 — 아래
    // `CatapultWheelVisual`도 이 축을 로컬 X에서 로컬 Y로 함께 바꿨다.
    private static void CreateWheel(Transform parent, Vector3 localPos, float radius, float thickness, Material mat, PhysicMaterial groundMaterial)
    {
        GameObject wheel = new GameObject("Catapult_Wheel");
        wheel.transform.SetParent(parent, false);
        wheel.transform.localPosition = localPos;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // 원통의 높이축을 로컬 X(옆면)로 돌려 바퀴처럼 보이게 한다.
        // 기본 Cylinder는 scale=1일 때 반지름 0.5·높이 2 — 따라서 반지름은 *2f, 두께(높이)는 *0.5f로 환산.
        wheel.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);

        // 18차 개편(2026-08-06)의 BoxCollider 근거는 그대로 유효하다 — CapsuleCollider는 두께가
        // 지름보다 얇으면 Unity가 원통 구간을 0으로 clamp해 완전한 구가 되므로 쓸 수 없다
        // (`Catupult_bug6.png`로 확인). BoxCollider는 이제 이 부모(회전하지 않음)에 고정된다.
        BoxCollider wheelCollider = wheel.AddComponent<BoxCollider>();
        wheelCollider.size = new Vector3(1f, 2f, 1f);
        // 12차 개편 — 바퀴도 바닥과 닿을 수 있는 콜라이더라 저마찰 재질을 적용한다(클래스 상단
        // "12차 개편 (1)" 참고).
        wheelCollider.material = groundMaterial;

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
    // 걸어다니는 건 Catapult_Base 상판이지만, 11차 개편(2026-08-05)부터 이 장식들도 콜라이더를
    // 유지한다(아래 "11차 개편 — 장식 콜라이더 복구" 참고) — Rigidbody 없는 static child라 투석기
    // 루트의 컴파운드 콜라이더에 자동 편입된다.
    private static void CreateTrestle(Transform parent)
    {
        Material woodMat = LoadOrCreateMaterial("Base", new Color(0.55f, 0.4f, 0.22f));
        const float crossX = 0.6f * Scale;
        CreateTrestleCross(parent, -crossX, woodMat);
        CreateTrestleCross(parent, crossX, woodMat);
        CreateTieBeam(parent, crossX, woodMat); // 6차 개편 외형 추가 — 하부 보강대.

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

    // 좌/우 X자 지지대 하단을 잇는 보강대(6차 개편 외형 추가) — 레퍼런스의 목재 프레임 느낌을
    // 보강한다. 파묻힘 계산과는 무관(버킷 그룹 콜라이더만 그 계산의 대상)하지만, 11차 개편부터
    // 콜라이더 자체는 유지한다(아래 "11차 개편 — 장식 콜라이더 복구" 참고).
    private static void CreateTieBeam(Transform parent, float crossX, Material mat)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Catapult_TieBeam";
        beam.transform.SetParent(parent, false);
        beam.transform.localPosition = new Vector3(0f, BaseTopY + 0.15f * Scale, 0f);
        beam.transform.localScale = new Vector3(crossX * 2f + 0.3f * Scale, 0.12f * Scale, 0.12f * Scale);
        beam.GetComponent<Renderer>().sharedMaterial = mat;
    }

    private static GameObject CreateArmPivot(Transform parent)
    {
        GameObject pivot = new GameObject("Catapult_ArmPivot");
        pivot.transform.SetParent(parent, false);
        pivot.transform.localPosition = new Vector3(0f, ApexY, 0f); // 트레슬 정점(Axle)에서 피벗.
        pivot.AddComponent<CatapultArm>();
        return pivot;
    }

    // armPivot 로컬 +Y로 곧게 선 블루프린트(회전 전 기준) — 클래스 상단 주석의 "버킷 높이 계산" 참고.
    // 6차 개편(외형 개선) — 균일 두께 박스 하나 대신, 피벗 쪽이 두껍고 버킷 쪽이 얇은 2단 테이퍼로
    // 지레 느낌을 준다. 파묻힘 계산에는 관여하지 않는다(버킷 그룹 콜라이더만 그 계산의 대상) — 다만
    // 11차 개편부터 콜라이더 자체는 유지한다(armPivot 자식이라 armPivot 회전을 따라간다. 아래
    // "11차 개편 — 장식 콜라이더 복구" 참고, 물리력을 추가하지 않는다는 사용자 확인과 함께).
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

    // 지레 반대편의 균형추 장식(6차 개편 외형 추가) — armPivot의 -Y쪽(팔/버킷과 반대 방향)에 붙어
    // 팔이 회전할 때 함께 돌아 "무게 중심을 맞추는 반대쪽 추"처럼 보인다. 순수 장식(질량 없음 —
    // 실제 물리 균형에는 관여하지 않는다, 관여시키려면 Rigidbody 질량 분포까지 다시 설계해야 해
    // 범위 밖으로 판단했다, YAGNI). 11차 개편부터 콜라이더는 유지한다(아래 "11차 개편 — 장식
    // 콜라이더 복구" 참고) — 질량이 없다는 것과 콜라이더가 있다는 것은 별개다.
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
    // (CatapultBucket, 실제 탑승 판정) — "옆에 스치기만 해도 탑승"되던 버그 수정(2026-08-04).
    // 19차 개편(2026-08-06)으로 발사 경로를 막던 F 벽 하나만 제거됐다(클래스 상단 "19차 개편"
    // 참고, 좌표 계산으로 F가 발사 경로를 막는 벽임을 확정했다) — B/L/R은 여전히 "옆에 스치기만
    // 해도 탑승"되던 2차 버그를 계속 막는다. 반환값은 CatapultBucket이 붙은 내부 트리거
    // GameObject다(arm.bucket이 이걸 참조한다). 치수/파묻힘 검산은 클래스 상단 주석 참고.
    // 23차 개편(2026-08-06) — 당김 앵커(`CreateAnchor`)를 이 함수 안에서 만들어 `out anchor`로
    // 반환한다. 앵커가 투석기 루트가 아니라 이 버킷 "그룹"(group.transform, B 벽과 같은 로컬
    // 좌표계)에 부모화돼야 팔이 당겨질 때 앵커도 버킷과 함께 움직이기 때문이다(클래스 상단
    // "23차 개편" 주석 참고) — 그룹 Transform은 이 함수 스코프 밖에서 얻을 방법이 없어(예전엔
    // CreateAnchor가 루트 아래 독립적으로 생성됐다), 호출을 여기로 옮겼다.
    private static GameObject CreateBucket(Transform parent, out GameObject anchor)
    {
        Material woodMat = LoadOrCreateMaterial("Bucket", new Color(0.2f, 0.2f, 0.22f));

        GameObject group = new GameObject("Catapult_Bucket");
        group.transform.SetParent(parent, false);
        // 7차 개편 — 그룹 자체의 위치/회전/스케일(사용자가 씬에서 직접 확정한 값, 클래스 상단 주석
        // "7차 개편" 참고). 안쪽 벽/바닥/트리거(BucketInnerHalf 등, 아래)의 로컬 치수는 그대로다 —
        // 그룹만 재배치·회전·축소해 정육면체(고정 1×1×1) 크기에 맞춘다.
        group.transform.localPosition = new Vector3(0f, BucketGroupLocalY, 0f);
        group.transform.localRotation = BucketGroupLocalRotation;
        group.transform.localScale = Vector3.one * BucketGroupLocalScale;

        Vector3 half = BucketInnerHalf;
        float wallT = BucketWallThickness;

        // 바닥: 내부 트리거 바닥(-half.y) 바로 아래, 벽 두께만큼 옆으로 넉넉히.
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Catapult_BucketFloor";
        floor.transform.SetParent(group.transform, false);
        floor.transform.localPosition = new Vector3(0f, -half.y - BucketFloorThickness * 0.5f, 0f);
        floor.transform.localScale = new Vector3((half.x + wallT) * 2f, BucketFloorThickness, (half.z + wallT) * 2f);
        floor.GetComponent<Renderer>().sharedMaterial = woodMat;

        // 벽 3면(B/L/R): 바닥 윗면(-half.y)부터 위로 BucketWallHeight만큼. 19차 개편(2026-08-06) —
        // F(그룹 로컬 +Z)만 제거했다(클래스 상단 "19차 개편" 주석의 좌표 유도 참고) — 발사 방향이
        // 그룹 로컬 좌표에서 (Δx=0, Δy=+cos(launchPitch), Δz=+sin(launchPitch))로 F 쪽을 향해
        // 뚫고 나가려 하기 때문이다. B/L/R은 발사 경로와 무관하므로 그대로 남겨 "옆에 스치기만
        // 해도 탑승"되던 2차 버그를 계속 막는다.
        float wallCenterY = -half.y + BucketWallHeight * 0.5f;
        CreateBucketWall(group.transform, "Catapult_BucketWall_L", new Vector3(-(half.x + wallT * 0.5f), wallCenterY, 0f),
            new Vector3(wallT, BucketWallHeight, (half.z + wallT) * 2f), woodMat);
        CreateBucketWall(group.transform, "Catapult_BucketWall_R", new Vector3(half.x + wallT * 0.5f, wallCenterY, 0f),
            new Vector3(wallT, BucketWallHeight, (half.z + wallT) * 2f), woodMat);
        CreateBucketWall(group.transform, "Catapult_BucketWall_B", new Vector3(0f, wallCenterY, -(half.z + wallT * 0.5f)),
            new Vector3(half.x * 2f, BucketWallHeight, wallT), woodMat);

        // 내부 트리거: 벽보다 안쪽, 바닥보다 위 — 정육면체 콜라이더가 여기 겹칠 때만 탑승 판정
        // (또는 CatapultBucket의 C키 글라이드가 이 지점을 목표로 삼는다). F 쪽은 벽이 없어졌으므로
        // CatapultBucket의 중앙 0.8배 거리 게이트가 그쪽 가장자리 스침을 대신 걸러낸다(19차 개편,
        // 클래스 상단 주석 참고).
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

        // 23차 개편 — 앵커를 그룹 자신에 부모화해 B 벽 바로 뒤(그룹 로컬 -Z)에 둔다. half/wallT/
        // wallCenterY는 이 함수가 이미 계산해 둔 값을 그대로 재사용한다(새 좌표계를 다시 유도하지
        // 않는다).
        anchor = CreateAnchor(group.transform, half, wallT, wallCenterY);

        return inner;
    }

    // 19차 개편(2026-08-06) — F(발사 방향 쪽) 호출만 삭제됐다. B/L/R은 여전히 이 함수를 쓴다.
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

    // 23차 개편(2026-08-06, 사용자 요청) — 앵커를 투석기 루트의 고정 오프셋이 아니라 버킷 "그룹"
    // 자신에 부모화한다 — 팔이 당겨지며 그룹이 움직일 때 앵커도 함께 따라가야 "바구니/지레를 직접
    // 뒤로 당기는" 느낌이 나기 때문이다(예전엔 루트에 고정돼 팔이 아무리 당겨져도 앵커 자체는
    // 꿈쩍하지 않았다). 위치는 `CreateBucket`이 이미 계산해 둔 half/wallT/wallCenterY(B 벽과 같은
    // 그룹 로컬 좌표계)를 그대로 재사용해 B 벽 바로 뒤(그룹 로컬 -Z)에 둔다 — 좌표계를 새로 유도하지
    // 않는다. 형태도 솔리드 구에서 한 축을 눌러 납작하게(디스크/고리처럼) 바꿨다 — "당김줄을 거는
    // 고리"로 읽히길 원한다는 요청 반영. 콜라이더 없는 순수 위치 마커라는 성질(ThreadAnchor와 동일
    // 이유)은 그대로다.
    private static GameObject CreateAnchor(Transform bucketGroup, Vector3 half, float wallT, float wallCenterY)
    {
        GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        anchor.name = "Catapult_PullAnchor";
        Object.DestroyImmediate(anchor.GetComponent<Collider>()); // 순수 위치 마커 — 물리 접촉 없음(ThreadAnchor와 동일 이유, 변경 없음).
        anchor.transform.SetParent(bucketGroup, false);

        // B 벽 바깥 면(-(half.z+wallT))보다 더 뒤로 살짝 띄운다 — "벽에 매달린 고리"처럼 보이게
        // 하는 여유값(감각적 판단, [TBD, 임시값]).
        const float margin = 0.25f * Scale;
        anchor.transform.localPosition = new Vector3(0f, wallCenterY, -(half.z + wallT + margin));

        // 옛 스케일(균일 0.35*Scale)에서 한 축(로컬 Z, "그룹 안으로 파고드는" 깊이 방향)만 1/3로
        // 눌러 납작한 디스크처럼 보이게 한다 — 나머지 두 축은 살짝 키워(0.35→0.5) 눌린 만큼의 시각적
        // 존재감을 보존했다. **왜 로컬 Z를 눌렀는가:** 그룹 자신이 로컬 X로 -90° 회전하고(
        // `BucketGroupLocalRotation`) armPivot도 로컬 X로 회전하므로(같은 축이라 각도가 합산된다,
        // 클래스 상단 "7차 개편" 공식), rest 각도(armPivot=0°)에서 이 합성 회전은 정확히 -90°다 —
        // 그 변환에서 그룹 로컬 Z축은 세계(root) Y축(수직)으로 매핑된다(19차 개편이 유도한
        // `Y_local=-Z_root, Z_local=Y_root`의 역변환과 일치, 클래스 상단 "19차 개편" 참고). 즉 로컬
        // Z를 누르면 rest 각도에서 세계 기준 "수직으로 얇은" 원반이 되어, 6차 개편이 이미 확립한
        // "손수레 손잡이 구멍"(수평으로 눕는 도넛) 모티프와 같은 방향으로 읽힌다 — 임의로 고른 축이
        // 아니라 기존 변환식으로 재검산해 고른 축이다.
        float anchorDiameter = 0.5f * Scale;
        float anchorFlattenedDiameter = anchorDiameter / 3f;
        anchor.transform.localScale = new Vector3(anchorDiameter, anchorDiameter, anchorFlattenedDiameter);

        anchor.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Anchor", new Color(1f, 0.6f, 0.1f));
        anchor.AddComponent<CatapultPullAnchor>();
        return anchor;
    }

    // 손잡이 — 막대(Rod, 시각 전용) + 고리(Ring, 순수 시각 도킹 마커). 22차 개편(2026-08-06)으로
    // 조향이 "부딪히면 미는/당기는" 충돌 기반에서 "C로 도킹 후 직접 조작"으로 전면 교체되며,
    // 손잡이는 더 이상 물리에 관여하지 않는다 — 콜라이더가 하나도 없다(클래스 상단 "22차 개편"
    // 주석 참고). 반환값은 고리(Ring) GameObject다 — `CatapultSteerHandle.dockAnchor`가 이
    // Transform을 그대로 가리킨다(고리 자신의 로컬 스케일이 항상 (1,1,1)이라, `CatapultBucket`이
    // 10차 개편에서 겪은 "비균일 스케일 부모에 도킹 대상을 부모화하면 전단으로 왜곡된다"는 함정을
    // 애초에 피한다 — 세그먼트마다 개별 스케일을 주고 링 자신은 스케일하지 않기 때문).
    private static GameObject CreateSteerHandle(Transform parent)
    {
        Material rodMat = LoadOrCreateMaterial("Arm", new Color(0.5f, 0.32f, 0.15f));
        Material ringMat = LoadOrCreateMaterial("Ring", new Color(0.35f, 0.35f, 0.38f)); // 철제 손잡이 느낌(회색조, 옛 그립/고리 머티리얼 재사용).

        // 막대: 받침대 앞(BaseHalfZ)에서 고리 **테두리**까지. 11차 개편부터 콜라이더를 유지한다(아래
        // "11차 개편 — 장식 콜라이더 복구" 참고) — 22차 개편도 Rod 자체는 손대지 않았다.
        // 23차 개편(2026-08-06) — 막대 끝점을 고리 중심(`SteerHandlePivotLocal.z`)이 아니라 고리
        // 테두리 안쪽 면(`SteerHandlePivotLocal.z - SteerRingRadius + SteerRingTubeThickness*0.5`)
        // 까지로 줄였다 — 예전엔 막대가 고리 중심까지 뻗어 있어 시각적으로 고리를 꿰뚫고 지나가는
        // 것처럼 보였다("막대가 고리 중심을 관통한다"는 지적의 실체). 순전히 시각적 변경이라
        // `SteerHandlePivotLocal`(Rod/Ring 공유 좌표, dockAnchor 위치)은 전혀 건드리지 않았다.
        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rod.name = "Catapult_SteerHandle_Rod";
        rod.transform.SetParent(parent, false);
        float ringNearEdgeZ = SteerHandlePivotLocal.z - SteerRingRadius + SteerRingTubeThickness * 0.5f;
        float rodLength = ringNearEdgeZ - BaseHalfZ;
        rod.transform.localPosition = new Vector3(0f, SteerHandlePivotLocal.y, BaseHalfZ + rodLength * 0.5f);
        rod.transform.localScale = new Vector3(0.15f * Scale, 0.15f * Scale, rodLength);
        rod.GetComponent<Renderer>().sharedMaterial = rodMat;

        return CreateSteerRingVisual(parent, SteerHandlePivotLocal, ringMat);
    }

    // 6차 개편의 16세그먼트 도넛 근사(수평 XZ 평면, "손수레 손잡이 구멍" 구도)를 순수 시각 전용으로
    // 다시 만든다 — 22차 개편(2026-08-06)부터 이 고리는 콜라이더가 하나도 없다(`CreateAnchor`가
    // 이미 쓰는 "장식 오브젝트는 콜라이더를 아예 없앤다" 관례). `CreatePrimitive`가 자동으로 붙이는
    // 콜라이더를 각 세그먼트에서 명시적으로 지운다. 반환하는 링 GameObject 자신은 스케일을 건드리지
    // 않는다(항상 (1,1,1)) — CatapultSteerHandle.dockAnchor가 이 Transform을 그대로 부모로 쓰므로,
    // 여기에 비균일 스케일을 주면 도킹된 구가 왜곡된다(위 CreateSteerHandle 주석 참고).
    // 23차 개편(2026-08-06) — 세그먼트 스케일의 축 배정이 뒤바뀌어 있던 버그를 고쳤다(루프 안
    // 주석 참고) — 12개 세그먼트가 이제 실제로 원둘레를 따라 이어 붙는다. `segmentLength`의
    // 1.15배 여유(세그먼트 사이 틈 방지)는 축만 바뀌었을 뿐 값 자체는 그대로 재사용해도 인접
    // 세그먼트가 겹치는 방향으로 충분히 겹친다(반지름 대비 폭이 넓어져 오히려 여유가 커졌다) —
    // 세그먼트 개수·여유 비율은 조정하지 않았다.
    private static GameObject CreateSteerRingVisual(Transform parent, Vector3 localCenter, Material mat)
    {
        GameObject ring = new GameObject("Catapult_SteerHandle_Ring");
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = localCenter;

        float segmentAngleDeg = 360f / SteerRingSegmentCount;
        float segmentLength = (2f * Mathf.PI * SteerRingRadius / SteerRingSegmentCount) * 1.15f; // 세그먼트 사이 틈 방지(6차 개편과 같은 여유 비율).

        for (int i = 0; i < SteerRingSegmentCount; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * segmentAngleDeg, 0f) * Vector3.forward;

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "Catapult_SteerHandle_RingSegment";
            Object.DestroyImmediate(seg.GetComponent<Collider>()); // 순수 시각 — 물리에 전혀 관여하지 않는다.
            seg.transform.SetParent(ring.transform, false);
            seg.transform.localPosition = dir * SteerRingRadius;
            seg.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            // 23차 개편(2026-08-06) — `LookRotation(dir, Vector3.up)`은 세그먼트의 로컬 Z를 반지름
            // 방향(dir)에, 로컬 X를 접선 방향에 놓는다. 이전 코드는 `segmentLength`(원둘레를 따라
            // 이어 붙는 호 길이)를 반지름 방향(Z)에, 두께(`SteerRingTubeThickness`)를 접선 방향(X)에
            // 줬다 — 그래서 세그먼트가 중심에서 바깥으로 뻗는 12개의 가는 꽃잎/바큇살처럼 보였다("링이
            // 확실히 원 모양을 안 띈다"는 지적의 실체). 여기서 두 축을 바로잡는다 — 접선(X)에 호 길이,
            // 반지름(Z)에 두께를 줘야 세그먼트들이 원둘레를 따라 이어 붙어 실제 닫힌 원으로 보인다.
            seg.transform.localScale = new Vector3(segmentLength, SteerRingTubeThickness, SteerRingTubeThickness);
            seg.GetComponent<Renderer>().sharedMaterial = mat;
        }

        return ring;
    }

    // 12차 개편(2026-08-05) 신규 — `PlayerShapeIdentity.Start()`가 이미 쓰는 패턴(런타임에
    // `new PhysicMaterial(...)` 인스턴스 생성, 별도 에셋으로 저장하지 않음)을 그대로 따른다. 이
    // 기믹은 정육면체/정사면체와 달리 도형별 스탯 에셋이 없어 인스턴스를 도형 이름별로 나눌 필요가
    // 없으므로, 투석기 한 대당 하나만 만들어 바닥과 닿는 콜라이더(상판·바퀴)끼리 공유한다.
    // `frictionCombine = Minimum`으로 둬, 지형 쪽 PhysicMaterial이 무엇이든(또는 없든) 접촉 마찰이
    // 항상 이 낮은 값 이하로 정해지게 한다 — 그래야 위 계산(요구 힘 < 최대 견인력)이 지형 설정과
    // 무관하게 항상 보장된다.
    private static PhysicMaterial CreateLowFrictionMaterial()
    {
        return new PhysicMaterial("Catapult_LowFriction")
        {
            staticFriction = LowFrictionCoefficient,
            dynamicFriction = LowFrictionCoefficient,
            frictionCombine = PhysicMaterialCombine.Minimum,
        };
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(SystemFolder))
            AssetDatabase.CreateFolder("Assets", "CatapultSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");
    }

    private static Material LoadOrCreateMaterial(string name, Color color)
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
