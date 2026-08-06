using UnityEngine;

/// <summary>
/// 투석기 팔의 상태(대기/장전 중/장전 완료/발사)를 관리하고, 발사 시점에 버킷 탑승자(정육면체)의
/// Rigidbody에 결정론적 velocity를 대입한다.
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
///
/// [restAngle/pulledAngle 기본값 20/70 — 버킷이 받침대 밑에 파묻히던 버그 수정, 2026-08-04]
/// `armVisual`/`bucket`은 armPivot의 자식으로, 로컬 오프셋이 "armPivot이 이미 restAngle만큼 회전한
/// 프레임" 안에서 해석된다(Awake가 항상 먼저 회전을 적용하므로). 예전 값(rest=70, 자식 오프셋이
/// +Z 위주)은 이 사실을 반영하지 않고 설계돼, 실제로는 버킷이 받침대 아래로 파묻혔다. 지금은
/// `CatapultMenuItem`이 자식 오프셋을 순수 로컬 +Y(팔이 위를 향하는 블루프린트)로 두므로, 회전 후
/// 버킷 높이는 armLength * cos(currentAngle)로 결정된다 — cos는 0°에서 최대이므로 **rest를 0°에
/// 가깝게, pulled를 그보다 90°에 가깝게** 잡아야 "당길수록 버킷이 내려간다"(레퍼런스 사진의
/// 안내 문구)는 방향과 "쉬는 동안은 버킷이 높다"가 동시에 성립한다.
///
/// [pulledAngle을 100°에서 70°로 다시 낮췄다 — 3차 개편(바구니 벽+바닥 추가), 2026-08-04]
/// 100°는 예전의 "트리거 박스 하나짜리" 버킷의 **중심점**만 검증해 정한 값이었다. 이번에 버킷을
/// 벽 4면+바닥이 있는 진짜 바구니로 바꾸면서 바닥의 **네 모서리**까지 좌표로 검산해 보니, 100°에서는
/// sin(100°)≈0.98(거의 최대)이라 바구니의 Z방향 폭이 거의 그대로 수직 낙차로 환산돼 바닥 모서리가
/// 받침대 윗면(y=0.2) 아래로 파고들었다(자세한 계산은 `CatapultMenuItem.cs` 상단 주석과
/// `docs/PRD/Catapult.md` §6 참고). 70°는 같은 바구니 크기에서 모서리 기준으로도 약 0.15 유닛의
/// 여유가 남는 지점이라 다시 올렸다 — 씬 튜닝 전제의 임시값인 건 여전하다(PRD 미확정).
///
/// [pulledAngle을 70°에서 55°로 다시 낮췄다 — 4차 개편(버킷 확대), 2026-08-04]
/// 3차 플레이테스트 지적("버킷을 더 크게")으로 `CatapultMenuItem.BucketInnerHalf`의 Z 폭이
/// 0.4→0.7로 커지면서, 파묻힘 계산의 `−Δz·sinθ` 항이 더 크게 깎게 됐다 — 같은 70°를 유지하면
/// 바닥 모서리가 받침대 윗면보다 약 0.11 유닛 아래로 파고든다(재검산, `CatapultMenuItem.cs` 상단
/// 주석 참고). 55°는 커진 바구니 기준으로도 약 0.15 유닛의 여유가 남는 지점이라 다시 낮췄다.
///
/// [pulledAngle을 55°에서 45°로, restAngle은 20°로 유지 — 6차 개편(버킷 내부 공간 재설계), 2026-08-04]
/// 6차 개편은 "정육면체가 안 들어가진다"는 근본 원인(내부 트리거 전체 높이가 2×0.2=0.4로 정육면체
/// 1×1×1보다 물리적으로 작았던 것)을 고치기 위해 `BucketInnerHalf`를 (0.8,0.2,0.7)→(0.9,0.7,0.85)로
/// 넉넉히 키웠다(§`CatapultMenuItem.cs` 상단 주석). Z 폭(half.z)이 0.7→0.85로 더 커지면서 파묻힘
/// 계산의 `−Δz·sinθ` 항이 또 커졌고, 동시에 `ArmLength`를 1.0→1.4로 늘려(아래 항목) 팔의 유효
/// 길이를 보강했다. 두 변경을 함께 반영해 재검산한 결과 45°가 버킷 바닥 모서리 기준 약 0.15
/// 유닛(스케일 전 기준)의 여유를 남기는 지점이라 55°에서 다시 낮췄다 — 상세 수식은
/// `CatapultMenuItem.cs` 상단 주석과 `docs/PRD/Catapult.md` §6 참고. restAngle(20°)은 이미
/// 충분한 여유(+0.78 안팎)가 있어 그대로 뒀다.
///
/// [ArmLength를 1.0에서 1.4로 늘렸다 — 6차 개편, 2026-08-04]
/// 버킷이 커지며 바닥 바닥면 오프셋(Δy)이 더 깊어졌다(-0.8, 4차 개편 -0.3 대비) — 옛 ArmLength(1.0)를
/// 그대로 두면 `(ArmLength+Δy)` 항이 0.2로 거의 사라져, 파묻히지 않으려면 pulledAngle을 30° 근처까지
/// 낮춰야 했다(rest 20°에서 겨우 10° 차이 — 당겨지는 느낌이 거의 안 남). 지레의 "당겨지는 정도"를
/// 시각적으로 설득력 있게 유지하기 위해 ArmLength를 늘려 `(ArmLength+Δy)` 항을 회복시켰다 — 그
/// 결과가 위 pulledAngle=45°(20°→45°, 25° 스윙)다. 이 변경은 6차 개편의 **버킷 재설계**(2단계)에
/// 속하고, 뒤이은 **3배 균등 확대**(3단계, §6·§7)와는 별개다 — 3배 확대는 모든 길이를 비례
/// 그대로 키우므로 각도(restAngle/pulledAngle)는 전혀 바뀌지 않는다(각도는 스케일과 무관).
///
/// [7차 개편(2026-08-05, 5차 플레이테스트 후) — 버킷 그룹 Transform 재배치로 파묻힘 공식이 바뀌었다]
/// `CatapultMenuItem.CreateBucket`의 "Catapult_Bucket" 그룹 자체가 더 이상 armPivot의 순수 로컬 +Y
/// 오프셋이 아니게 됐다(정육면체 탑승 물리 폭발 수정 — `CatapultMenuItem.cs` 상단 주석 "7차 개편"
/// 참고). 이제 그룹 자신이 로컬 X축으로 -90° 추가 회전하고 0.43배로 축소된 채 armPivot 로컬
/// (0, BucketGroupLocalY=5.34, 0)에 붙는다. armPivot의 회전축(X)과 그룹 자신의 회전축(X)이 같은
/// 축이라 두 회전은 각도가 더해지는 것과 동일하게 합성되고, 그룹 로컬 좌표 (Δy, Δz)를 가진 점의
/// root 좌표는
///   `y = ApexY + (BucketGroupLocalY + 0.43·Δz)·cosθ + 0.43·Δy·sinθ`
/// 로 정리된다(유도 과정은 `CatapultMenuItem.cs` 상단 주석 참고).
/// **재검산 결과 — worst-case(바닥 바닥면 모서리, Δy=-2.4, Δz=-2.85, 전부 이미 ×Scale 반영값):**
///   rest(20°):   y = 1.8 + 4.1145·cos20° − 1.032·sin20° ≈ 5.313 (받침대 윗면 BaseTopY=0.6 대비 +4.713)
///   pulled(45°): y = 1.8 + 4.1145·cos45° − 1.032·sin45° ≈ 3.980 (대비 +3.380, 파묻힘과는 거리가 멀다)
/// 6차 개편까지는 여유가 좁아질 때마다 pulledAngle을 계속 낮춰야 했지만(70°→55°→45°), 이번엔 그룹이
/// 훨씬 높은 위치(BucketGroupLocalY=5.34, 옛 ArmLength=4.2보다 위)에 놓이고 그룹 자신의 회전+축소
/// (0.43배)가 Δy/Δz 항의 실질 크기를 크게 줄여 여유가 오히려 넉넉해졌다 — 역산하면 θ≈92°가 돼야
/// 겨우 파묻히기 시작한다(45°는 그보다 한참 안전하다). 그래서 이번 라운드는 **pulledAngle을 바꾸지
/// 않았다**(45° 유지) — 여유가 부족해서 낮춰야 했던 3~6차와 반대로, 이번엔 여유가 넘쳐서 바꿀
/// 이유가 없었던 경우다.
///
/// [7차 개편(2026-08-05) — 당김/반동을 부드러운 보간에서 노치(딸깍임)+스프링 오버슈트로 교체]
/// 사용자 요청: 당김(장전)과 반동(발사) 둘 다 매끈한 트윈이 아니라 실제 윈치/래칫이 걸쇠를 무는
/// 것처럼 "덜컹거리며" 더 기계적으로 보였으면 한다. **왜 노이즈/흔들림(shake)이 아니라 노치+스프링을
/// 골랐나** — 순수 노이즈는 "떨리는" 느낌은 줘도 "한 단계씩 걸리며 전진한다"는 방향성 있는 기계적
/// 질감을 주지 못한다(래칫은 무작위로 떠는 게 아니라 정해진 단계마다 딱딱 멈춘다). 대신 (1) 당김
/// 비율을 `pullNotchCount`개의 이산 걸쇠로 양자화해(연속 값이 아니라 "다음 걸쇠로 넘어갈 때만" 목표
/// 각도가 바뀐다) 걸쇠 사이에서는 팔이 반응하지 않게 하고(실제 래칫이 걸쇠 사이에서 헛도는 것과
/// 같다), (2) 그 목표가 바뀔 때마다 임계감쇠에 가까운 2차 스프링-댐퍼(`StepSpringToward`, 질량=1
/// 암묵 가정, `notchSpringStiffness`/`notchSpringDamping`)로 각도를 뒤쫓게 했다. 오버슈트 폭을 별도
/// 상수(예: "몇 도 튕긴다")로 못박지 않은 이유는, 진짜 스프링이라면 오버슈트가 강성/감쇠비의 자연
/// 결과여야지 임의로 얹는 애니메이션 값이면 안 되기 때문이다 — 스프링을 흉내만 내는 하드코딩된
/// "튕김" 트윈보다, 실제 2차 감쇠 시스템 하나를 직접 적분하는 쪽이 더 적은 코드로 "노치마다 매번
/// 조금씩 다르게 반응하는" 자연스러움을 얻는다. 발사 반동(`Update()`의 Firing 상태)도 같은 함수를
/// restAngle 목표로 재사용한다 — 예전의 등속 `Mathf.MoveTowards`(`recoilSpeedDegPerSec`)는 삭제했다.
/// 새 노치/스프링 필드는 전부 씬 튜닝 전 감각적 기본값이다(`[TBD, 임시값]`) — 실제 "덜컹거림"의
/// 세기·연출 완성도는 플레이 확인이 필요하다.
///
/// [반동을 노치 스프링과 분리 + 정착 시 "클런크" 잔진동 추가 — 8차 개편(2026-08-05)]
/// 사용자 요청: 발사 후 팔이 rest로 "자연스럽게 감속하며 스윙백"한 다음, 다 멈추고 나서 그냥
/// 매끈하게 서는 대신 작은 "덜컥"(clunk) 잔진동이 한 번 더 있었으면 한다. **먼저 기존 코드가 이미
/// 오버슈트를 내고 있는지부터 계산으로 확인했다** — 7차 개편 값(`notchSpringStiffness=900`,
/// `notchSpringDamping=45`, 질량=1 암묵)의 감쇠비는 `ζ = damping / (2·√(stiffness·mass)) = 45/60 =
/// 0.75`다. 이건 과소감쇠(ζ&lt;1)라 오버슈트가 물리적으로 존재하긴 하지만, 2차 시스템의 스텝
/// 오버슈트 공식 `%OS = exp(-ζπ/√(1-ζ²))·100`에 대입하면 `exp(-0.75π/0.6614)·100 ≈ 2.8%`뿐이다 —
/// **있긴 있지만 눈에 띄지 않을 만큼 작다.** `Update()`의 Firing 분기가 실제로 매 프레임
/// `StepSpringToward(restAngle, dt)`를 호출하는 것도 코드로 재확인했다(7차 개편 요약이 맞았다) —
/// 그러니 "공유 자체가 안 되고 있다"가 아니라 "공유는 되는데 감쇠비가 너무 높아 티가 안 난다"가
/// 진짜 원인이다.
/// **수정 — 반동 전용 스프링 상수(`recoilSpringStiffness`/`recoilSpringDamping`)를 새로 뒀다.**
/// 노치(장전)는 "딸깍, 딸깍" 짧고 톡톡 끊기는 기계식 클릭이 목적이라 과소감쇠를 세게 줄 필요가 없지만,
/// 반동은 "한 번의 연속된 스윙백"이 목적이라 둘을 애초에 하나로 공유한 게 무리수였다 — 감쇠비를
/// 낮춰(목표 ζ≈0.5) `recoilSpringStiffness=500`/`recoilSpringDamping=22`로 잡았다
/// (ζ=22/(2·√500)=22/44.72≈0.492). 이때 오버슈트는 `exp(-0.5π/0.866)·100≈16.3%` — pulledAngle=90°
/// 기준 반동 스윙 폭(70°)의 16.3%인 약 11.4°를 rest 너머로 지나쳤다가 되돌아오는, 눈에 보이는
/// 스윙백이 된다.
/// **"정착 후 클런크"는 스프링 하나만으로는 안 나온다** — 진짜 2차 스프링은 목표에 점근적으로
/// 수렴할 뿐, "다 멈춘 뒤 한 번 더 튕기는" 이산적 2차 이벤트를 저절로 만들지 않는다. 그래서 첫
/// 정착(오버슈트-감쇠 스윙이 `RecoilSettleAngleEpsilon`/`RecoilSettleVelocityEpsilon` 안에 들어오는
/// 순간)을 감지해 그 순간에만 `angleVelocity`에 작은 충격(`recoilSettleJoltVelocity`)을 한 번
/// 더해 준다 — 그 뒤에도 계속 같은 반동 스프링이 이 충격을 뒤쫓아 감쇠시키므로, 결과적으로 "큰
/// 스윙백 한 번 → 잦아듦 → 작은 덜컥 한 번 → 완전 정지"의 2단계 정착이 된다. `recoilJolted` 플래그로
/// 한 발사당 클런크가 정확히 한 번만 나가게 막는다(`Fire()`에서 초기화).
/// 새 필드도 전부 씬 튜닝 전 감각적 기본값이다(`[TBD, 임시값]`).
///
/// [pulledAngle을 45°에서 90°로 — 8차 개편(2026-08-05), "당김이 더 극적으로 보였으면 한다"]
/// 7차 개편이 재검산한 파묻힘 공식(`y = ApexY + (BucketGroupLocalY + 0.43·Δz)·cosθ + 0.43·Δy·sinθ`,
/// worst-case Δy=-2.4/Δz=-2.85, 전부 ×Scale 반영값)에 θ=90°를 그대로 대입해 다시 확인했다(7차 개편의
/// "θ≈92°에서야 겨우 닿기 시작한다"는 결론을 그대로 재사용하지 않고 직접 계산):
///   y(90°) = 1.8 + 4.1145·cos90° − 1.032·sin90° = 1.8 + 0 − 1.032 = 0.768
///   받침대 윗면(BaseTopY=0.6) 대비 **+0.168** — 파묻히지는 않지만 45°(+3.380)보다 여유가 훨씬
///   얇다. θ가 커질수록 cosθ가 줄어 term1(항상 양수) 기여가 사라지고 sinθ가 늘어 term2(-1.032·sinθ,
///   항상 음수 기여)가 커지므로, 여유는 θ에 대해 단조 감소한다 — 역산한 임계각(θ≈92.36°, 아래
///   유도)보다 90°는 여전히 작아(90 < 92.36) 파묻히지 않지만, 마진이 3~7차 개편 때처럼 넉넉하지
///   않다는 점은 그대로 기록해 둔다.
///   **임계각 유도**: `1.8 + 4.1145cosθ − 1.032sinθ = 0.6` → `4.1145cosθ − 1.032sinθ = −1.2`.
///   합성파 형태로 쓰면 `R·cos(θ+φ) = −1.2`, `R=√(4.1145²+1.032²)≈4.242`,
///   `φ=atan(1.032/4.1145)≈14.08°` → `cos(θ+14.08°) = −1.2/4.242 ≈ −0.2829` →
///   `θ+14.08° ≈ 106.44°` → `θ ≈ 92.36°`. 90°가 이 임계각보다 작다는 것을 직접 재확인했다(7차 개편이
///   말만 남긴 "θ≈92°"를 이번에 실제로 다시 유도해 검증한 것).
/// 마진이 얇아진 것 자체는 위험 신호는 아니다 — 여전히 양수이고, 계산이 가정하는 worst-case(바닥
/// 바닥면의 가장 안쪽 모서리)는 실제 정육면체 탑승자가 거의 닿지 않는 극단값이다. 다만 씬 튜닝
/// 오차(예: `BucketGroupLocalScale`이 0.43에서 조금만 커져도 여유가 더 줄어든다)에 민감해진 건
/// 사실이라, 다음 라운드에서 버킷 그룹 치수를 다시 조정할 일이 생기면 이 여백부터 재확인할 것.
///
/// [restAngle을 20°에서 0°로 — 11차 개편(2026-08-05), 사용자 확정("최소 0도/최대 90도")]
/// 같은 파묻힘 공식(`y = ApexY + (BucketGroupLocalY + 0.43·Δz)·cosθ + 0.43·Δy·sinθ`, worst-case
/// Δy=-2.4/Δz=-2.85 → term1=4.1145, term2 계수=-1.032, 전부 ×Scale 반영값)에 θ=0°를 대입해
/// 재검산했다:
///   y(0°) = 1.8 + 4.1145·cos0° − 1.032·sin0° = 1.8 + 4.1145 − 0 = 5.9145
///   받침대 윗면(BaseTopY=0.6) 대비 **+5.3145** — 지금까지 나온 어떤 각도(rest 20°일 때 +4.713,
/// pulled 90°일 때 +0.168)보다도 훨씬 안전하다. 위 임계각 유도 문단이 보여준 대로 여유는 θ에 대해
/// 단조 감소하므로(cosθ가 줄고 sinθ가 늘수록 term1 기여가 사라지고 term2 음수 기여가 커진다), θ가
/// 0°에 가까울수록 여유가 최대가 되는 것은 당연한 결과다 — 별도의 새로운 위험은 없다.
/// `pulledAngle`(90°, 8차 개편 확정)은 이번 라운드에서 바꾸지 않았다 — 사용자가 "pulledAngle은 이미
/// 90으로 확정돼 있으니 그대로"라고 명시했다.
///
/// [13차 개편(2026-08-05) — "발사 시 정육면체가 반대 방향으로 날아간다" 진단 로그 추가 (코드 수정
/// 없음)]
/// `Fire()`의 발사 방향 계산(`dir = Quaternion.AngleAxis(-launchPitch, aimRoot.right) * aimRoot.
/// forward`)은 삼각함수로 재검산해도 맞았다 — 그래서 계산식을 고치는 대신, 발사 순간의 실측값을
/// 남기는 쪽으로 바꿨다(코디네이터 지시, `CatapultBucket`의 탑승 진단 로그와 같은 판단). `dir`,
/// `aimRoot.forward`/`aimRoot.right`/`aimRoot.position`, 최종 `occupant.velocity`를 발사마다
/// `Debug.Log`로 남긴다 — 이 로그를 캡처해 오면 "계산 자체가 틀렸는지" 아니면 "aimRoot가 가리키는
/// Transform 자체가 조향과 어긋나 있는지"(예: 씬에서 `aimRoot`가 잘못 연결됨)를 구분할 수 있다.
///
/// [15차 개편(2026-08-05) - 13차 진단 로그 분석 결과, 발사 방향이 앵커 반대쪽(링 쪽)으로 나가는 것을
/// 확정 수정]
/// 캡처된 로그(dir=(-0.01, 0.766, 0.642), aimRoot.forward=(-0.023, 0, 1.0))를 보면 dir의 Z가
/// forward와 같은 부호(+)다 - 즉 발사는 항상 aimRoot의 +Z(조향 손잡이/링이 있는 쪽)로 나가고
/// 있었다. 사용자가 실제 플레이로 확인한 사실 - 정육면체는 실제로 링 쪽으로 날아가고, 원하는
/// 방향은 그 반대쪽인 당김 앵커 쪽(-Z)이다. 13차 개편이 "삼각함수로 재검산해도 계산식 자체는
/// 맞았다"고 결론 낸 것은 "위로 launchPitch만큼 꺾는다"는 피치 각도 계산에 대해서만 맞았을 뿐,
/// 수평 기준 방향(+Z vs -Z)이 애초에 요구사항과 반대로 정해져 있었다는 걸 놓친 것이었다 - 계산이
/// 틀린 게 아니라 계산의 입력(기준 forward)이 의도와 반대였다.
/// 수정 - aimRoot.forward를 -aimRoot.forward로 뒤집는 동시에 회전 부호도 -launchPitch에서
/// +launchPitch로 함께 뒤집었다. 왜 둘 다 같이 바꿔야 하는지: Quaternion.AngleAxis(angle, axis)는
/// axis를 오른손 법칙으로 감는 회전이라, 방향 벡터 자체를 반대로 뒤집으면(v -> -v) 그 벡터에
/// 적용되는 회전도 부호가 반대여야 "위로 꺾는다"는 기하학적 의미가 그대로 유지된다(성분별로 직접
/// 전개해 확인 - right 축을 기준으로 forward를 +θ만큼 꺾어 위로 올리던 것이, -forward를 그대로
/// +θ만큼 꺾으면 오히려 아래로 꺾인다. 회전 부호까지 -θ -> +θ로 반전해야 다시 위로 꺾인다). 이
/// 둘 중 하나만 바꾸면 위/아래가 뒤집히거나 원하는 결과가 나오지 않는다.
/// TBD - 버킷 위치(+Z)와 발사 방향(-Z)이 반대다. 버킷(정육면체가 타는 자리)은 여전히 물리적으로
/// aimRoot의 +Z 쪽(링과 같은 쪽, CatapultMenuItem의 BucketGroupLocalY/armPivot 회전 기하가 정하는
/// 위치)에 있다 - 방향만 -Z로 뒤집었으므로, 정육면체가 버킷에서 튀어나오자마자 투석기 본체를
/// 가로질러 반대편(-Z, 앵커 쪽)으로 날아가는 것처럼 보일 수 있다. 코드만 봐서는 실제로 시각적으로
/// 이상해 보이는지 판단할 수 없다 - 다음 플레이테스트에서 실측이 필요하다(버킷 위치를 옮기는 건
/// 이번 라운드 범위 밖).
///
/// [17차 개편(2026-08-06) — 발사 시 정육면체가 버킷을 관통하는 버그, "가속 스윙 후 분리"로 재설계]
/// 근본 원인: `Fire()`가 `ConsumeOccupant()`(즉시 unparent+비킨네마틱)와 `velocity` 대입을 같은
/// 프레임에 처리하는 동안, `Update()`의 반동 스프링이 그 즉시 팔/버킷을 restAngle로 빠르게 되감기
/// 시작한다 — 사출된 정육면체와 되감기는 버킷이 몇 물리 스텝 안에서 상대 속도차로 서로를 스쳐
/// 지나며 discrete 충돌 감지가 겹침을 놓친다(터널링과 같은 종류). 사용자 확정 해법 — 순간 velocity
/// 대입을 버리고, 발사를 "팔이 pulledAngle에서 restAngle까지 `launchSwingDuration`(0.8~1초)에 걸쳐
/// 가속하며 스윙하는" 애니메이션으로 바꾼다. 이 스윙 동안 탑승자는 계속 버킷에 부모화+킨네마틱
/// 상태로 남아(=팔과 물리적으로 같은 몸처럼 움직인다) 관통 자체가 원천적으로 불가능해지고, 스윙이
/// 끝나 팔이 restAngle에 도달한 순간에야 비로소 `bucket.ConsumeOccupant()`로 분리하고 그 순간
/// 결정론적 velocity를 대입한다 — 그 시점엔 팔이 이미 정지 각도라 사출 직후의 상대 속도차 문제도
/// 없다.
/// **"결정론적 velocity 대입" 철학은 그대로 유지한다 — 바뀐 건 "언제 대입하는가"뿐이다.** 속도
/// 계산식(`Lerp(minLaunchSpeed, maxLaunchSpeed, ratio01)`)과 방향 계산식은 이전과 동일하되,
/// `ratio01`은 **`Fire()` 호출 시점(발사가 결정되는 순간)에 캡처**해 `pendingLaunchVelocity`로
/// 저장해 둔다 — 스윙이 끝나는 시점에 재계산하면 그 시점엔 이미 발사가 결정된 뒤라 재계산할 근거
/// 자체가 없다(예: 그 사이 조향이 돌아 aimRoot가 바뀌면 발사자가 의도한 방향과 달라진다).
/// **왜 ease-in(가속) 커브인가.** 사용자가 "가속하며"를 명시했다 — 단순 `Mathf.Lerp` 등속이 아니라
/// `t01*t01`(2차 ease-in)로 각도를 보간해, 스윙 초반은 느리게 시작해 끝으로 갈수록 빨라진다(실제
/// 지레가 정지 상태에서 힘을 받아 가속하는 느낌과 일치).
/// **빈 버킷 발사는 램프를 타지 않는다** — 램프는 "탑승자가 버킷과 함께 움직이며 관통을 막기 위한"
/// 장치인데 탑승자가 없으면 관통할 대상 자체가 없다. `Fire()`는 `bucket.HasOccupant`가 false면
/// 예전처럼 즉시 `ArmState.Firing`(반동 애니메이션만)으로 들어간다 — 새 `Launching` 상태로 진입시키지
/// 않는다.
/// **8차 개편의 반동 스프링("스윙백+클런크")은 삭제하지 않고 재사용한다** — 램프가 끝나 `currentAngle`이
/// 이미 restAngle인 채로 `State = ArmState.Firing`으로 넘기면, 다음 프레임부터 기존 `StepSpringToward`가
/// 그대로 이어받는다. 이미 목표 각도 근처에서 시작하므로 오버슈트 폭은 작아지지만(램프가 이미 팔을
/// 정지 각도까지 데려다 놨으므로), 8차 개편이 원하던 "정착 후 살짝 덜컥"이라는 질감 자체는 코드
/// 변경 없이 자연스럽게 남는다 — 두 애니메이션을 합성하는 대신 순서대로 이어 붙이는 쪽이 코드가
/// 가장 단순해 이 방향을 택했다(YAGNI — 별도의 합성 로직을 만들지 않는다).
/// `bucket.ConsumeOccupant()`(즉시 분리하는 함수)의 시그니처/동작은 그대로 두고, `CatapultArm`이
/// "언제 이 함수를 호출하는가"만 늦췄다(Fire() 즉시 → 램프 타이머 종료 시점, `Update()`의
/// `ArmState.Launching` 분기).
///
/// [18차 개편(2026-08-06) — 재테스트 피드백 4건 중 3건이 이 클래스에 해당]
///
/// (1) **재연결이 발사 스윙을 가로채 정육면체가 버킷에 영구히 갇히는 버그 — 확정 원인, `BeginPull`
/// 가드로 수정.** `ArmState.Launching`(17차 개편)이 진행되는 0.8~1초 동안 사용자가 다시 C를 눌러
/// 재연결하면, `CatapultLoadController.TryConnect()`가 즉시 `arm.BeginPull(wheelRatio)`을 부른다.
/// `BeginPull`은 무조건 `State`를 덮어써 왔으므로(`State = ratio01 >= 1f ? Loaded : ...`), 스윙
/// 도중 이게 호출되면 `Update()`의 `Launching` 분기가 다시 실행되지 않아 `bucket.ConsumeOccupant()`가
/// 영원히 불리지 않는다 — 정육면체가 부모화+킨네마틱 상태로 버킷에 영구히 갇힌다("C를 눌렀을 때
/// 기준으로 계산해 정육면체가 버킷 안에 머무는" 사용자 재현 그대로). 수정은 `BeginPull` 맨 앞에
/// `if (State == ArmState.Launching) return;` 가드 하나뿐이다 — 스윙이 끝날 때까지 재연결/재당김
/// 입력을 완전히 무시한다. `CatapultLoadController`는 건드리지 않았다 — 여러 컨트롤러가 같은 `arm`을
/// 호출할 수 있는 구조라, 가드는 이 클래스 한 곳에서 막는 게 맞다(코디네이터 지시).
///
/// (2) **발사 속도를 `ratio01`(입력) 대신 "실제 팔이 도달한 각도"(`currentAngle`) 기준으로 계산 —
/// 사용자 확정.** 사용자 요청: "당김 각도에 따라 발사 직전 벡터 계산할 때 주어지는 힘 크기도
/// 변화되도록(0도=최소, 90도=최대)". `ratio01`(마우스 휠 누적값, 입력)과 실제 팔이 도달한 각도는
/// 노치 양자화+18차의 시간 기반 ease 전환(아래 (3) 참고) 때문에 살짝 어긋날 수 있다 — "얼마나
/// 당겨졌는지"가 힘을 결정해야 물리적으로 더 직관적이라는 게 사용자 판단이다. `Fire()`에서
/// `launchStartAngle`(램프 시작 각도로 이미 캡처하던 필드)을 **속도 계산보다 먼저** 대입하도록
/// 순서를 정리하고, `Mathf.InverseLerp(restAngle, pulledAngle, launchStartAngle)`로 0~1 비율을
/// 역산해 `Lerp(minLaunchSpeed, maxLaunchSpeed, angleRatio)`에 넣는다 — `restAngle`이
/// `pulledAngle`보다 클 가능성은 없다고 가정해도 되지만 `InverseLerp`는 그 경우도 안전하게
/// 처리한다. 방향(`dir`) 계산식은 15차 개편 그대로 손대지 않았다.
///
/// (3) **당김 각도 전환 — 노치(딸깍) 유지, 전환만 시간 기반 ease로(스프링 오버슈트 제거) — 사용자
/// 확정.** "노치 단계는 유지, 전환만 부드럽게" — 목표 노치가 바뀔 때마다 `StepSpringToward`(7차
/// 개편의 임계감쇠 스프링, 오버슈트로 "휙휙" 튀는 느낌)로 뒤쫓던 것을, 17차 개편의 `Launching`
/// 램프와 같은 패턴(고정 시간 동안 ease 보간, 오버슈트 없음)으로 바꿨다 — 새 필드
/// `pullTransitionDuration`(기본 0.2초, 노치 간격 대비 짧게 잡아 휠을 빠르게 돌릴 때 밀리지 않게
/// 했다)를 쓴다. **목표 노치가 다시 바뀌면(사용자가 계속 휠을 돌리는 중) 그 시점의 현재 각도에서
/// 새 목표로 다시 램프를 시작한다** — `Launching` 램프와 달리 당김은 "중간에 방해받으면 안 된다"는
/// 제약이 없다(계속 조정 가능해야 하는 게 원래 의도). ease 함수는 직접 2차식을 새로 쓰는 대신
/// `Mathf.SmoothStep`(표준 라이브러리, S자형 ease-in-out, 오버슈트 없음)을 재사용했다 — 새 수학을
/// 발명할 이유가 없었다. **`notchSpringStiffness`/`notchSpringDamping` 필드와 `StepSpringToward`
/// 함수는 삭제하지 않았다**(코디네이터 지시) — 단, 정확히 말하면 `Update()`의 발사 반동(`Firing`
/// 상태)은 이 두 필드가 아니라 별도의 `recoilSpringStiffness`/`recoilSpringDamping`(8차 개편이
/// 분리)을 쓰므로, 이번 개편 이후 `notchSpringStiffness`/`notchSpringDamping`을 실제로 읽는 코드는
/// 어디에도 남아 있지 않다 — 다음에 노치에 다시 스프링 질감을 넣고 싶어질 경우를 위한 여지로만
/// 필드를 남겨 둔다(정직하게 기록해 둔다 — "여전히 쓰인다"고 착각하지 않도록).
///
/// [25차 개편(2026-08-06) — pulledAngle을 90°에서 80°로, 조향석 도킹과의 겹침 회피]
/// 8차 개편이 90°로 올린 이유(파묻힘 마진 확보)는 여전히 유효하지만, 22차 개편으로 도입된 조향석
/// 도킹(`CatapultSteerHandle`)이 구를 투석기 앞쪽 손잡이 위치에 고정시키면서, 팔이 90°까지
/// 완전히 당겨졌을 때 버킷/팔이 그 도킹된 구와 물리적으로 겹치는 버그가 실측으로 재현됐다(사용자
/// 보고). 파묻힘 여유는 각도가 작아질수록 커지는 단조 관계(8차 개편 재검산 참고)라, 90°보다 낮은
/// 값은 파묻힘 쪽으로는 항상 더 안전하다 — 따로 재검산할 필요 없이 80°로 낮추기만 하면 두 문제
/// (파묻힘·도킹 구와의 겹침) 모두 여유가 생긴다. 정확한 여유 각도(80°가 충분한지, 더 낮춰야
/// 하는지)는 씬 튜닝 대상 — 필드 툴팁 참고.
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
    [Tooltip("[18차 개편(2026-08-06)부터 실제로 쓰는 코드가 없다 — 삭제하지 않고 필드만 남겨 둔다.] " +
             "예전엔 노치가 바뀔 때 목표 각도를 스프링 오버슈트로 뒤쫓는 강성이었으나, 사용자 요청 " +
             "('노치는 유지, 전환만 부드럽게')으로 BeginPull이 시간 기반 ease(pullTransitionDuration) " +
             "로 바뀌었다 — 클래스 상단 '18차 개편 (3)' 주석 참고. 발사 반동은 별도 필드 " +
             "recoilSpringStiffness/Damping을 쓴다(이 필드와 공유하지 않는다, 8차 개편부터).")]
    public float notchSpringStiffness = 900f;
    [Tooltip("[18차 개편부터 실제로 쓰는 코드가 없다 — 위 notchSpringStiffness와 같은 이유.]")]
    public float notchSpringDamping = 45f;

    [Header("장전 전환 부드러움 — 18차 개편 신규(오버슈트 없는 시간 기반 ease) [TBD, 임시값]")]
    [Tooltip("목표 노치가 바뀔 때 그 각도로 부드럽게 전환되는 데 걸리는 시간(초). 노치 자체(걸쇠 단계)는 " +
             "그대로 유지하고 전환 구간만 매끈하게 만든다(사용자 확정 — 클래스 상단 '18차 개편 (3)' " +
             "주석 참고). 너무 길면 마우스 휠을 빠르게 돌릴 때 목표를 따라가지 못해 밀리는 느낌이 " +
             "난다 — 노치 간격보다 확실히 짧게(0.15~0.3초 감각값).")]
    public float pullTransitionDuration = 0.2f;

    [Header("발사 반동 전용 스프링 — 8차 개편 신규, 노치 스프링과 분리 [TBD, 임시값]")]
    [Tooltip("발사 후 팔이 restAngle로 되돌아갈 때 쓰는 전용 강성. 노치 스프링(notchSpringStiffness)과 " +
             "공유하지 않는다 — 장전은 짧고 톡톡 끊기는 클릭, 반동은 하나의 연속된 스윙백이라 서로 " +
             "다른 질감이 필요하다(클래스 상단 주석 '반동을 노치 스프링과 분리' 참고). 감쇠비 " +
             "ζ≈0.5(더 눈에 띄는 오버슈트)가 되도록 notchSpring보다 낮춰 잡았다.")]
    public float recoilSpringStiffness = 500f;
    [Tooltip("위 반동 스프링의 감쇠. notchSpringDamping(ζ≈0.75, 오버슈트 2.8%로 거의 안 보임)보다 " +
             "낮춰(ζ≈0.5, 오버슈트 16.3%) 스윙백이 눈에 띄게 했다. 감각적 기본값.")]
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
             "velocity를 받는다(클래스 상단 '17차 개편' 주석 참고). 0.8~1.0초 사이 감각값 — 씬 튜닝 " +
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
    // "18차 개편 (3)" 주석 참고).
    private float pullTransitionElapsed;
    private float pullTransitionStartAngle;
    private float pullTargetAngle;
    private bool pullTransitionActive;
    // 8차 개편 — 발사 1회당 "정착 후 클런크" 충격이 정확히 한 번만 나가게 막는다(Fire()에서 초기화).
    private bool recoilJolted;

    // 17차 개편 — ArmState.Launching 램프 진행 상태. launchStartAngle은 Fire() 호출 시점의
    // currentAngle(대개 pulledAngle 근처)을 캡처해 램프 시작점으로 쓴다. pendingLaunchVelocity는
    // Fire() 시점에 계산해 둔 결정론적 발사 속도 — 램프가 끝날 때 그대로 대입한다(클래스 상단 "17차
    // 개편" 주석 참고, ratio01을 램프 종료 시점에 재계산하지 않는 이유도 그곳에 있다).
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
        // 버킷에 부모화+킨네마틱 상태로 붙어 있어 팔과 함께 움직인다(클래스 상단 "17차 개편" 주석
        // 참고). 스윙이 끝나야 비로소 분리 + velocity 대입이 일어난다.
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
                    Debug.Log($"[Catapult][Diag] Fire (release) occupant.velocity={occupant.velocity:F3}");
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
        // 개편으로 전용 상수 recoilSpringStiffness/Damping으로 분리 — 클래스 상단 주석 "반동을 노치
        // 스프링과 분리" 참고). 재연결로 BeginPull이 다시 호출되면 그쪽이 즉시 State를 덮어써
        // 자연스럽게 다음 장전으로 넘어간다(별도 잠금 불필요 — 어차피 C로 재연결하려면 그 순간의
        // 거리 비율이 다시 반영되어야 하므로 이 편이 맞다).
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
    /// 바뀌는 전환은 스프링 오버슈트가 아니라 시간 기반 ease로 부드럽게 이어진다(클래스 상단 "18차
    /// 개편 (3)" 주석 참고).</summary>
    public void BeginPull(float ratio01)
    {
        // 18차 개편(1) — 발사 스윙(Launching) 도중 재연결/재당김이 끼어들면 State가 덮어써져
        // ConsumeOccupant()가 영원히 안 불리는 버그의 근본 원인이었다(클래스 상단 "18차 개편 (1)"
        // 주석 참고) — 스윙이 끝날 때까지 이 함수는 아무 일도 하지 않는다.
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
        ratio01 = Mathf.Clamp01(ratio01);
        recoilJolted = false; // 8차 개편 — 이번 발사의 "정착 후 클런크"를 다시 한 번 쓸 수 있게 리셋.

        if (bucket == null || !bucket.HasOccupant)
        {
            State = ArmState.Firing;
            Debug.LogWarning("[Catapult] 버킷이 비어 있어 발사 연출만 재생합니다(빈 발사, 허용된 동작).");
            return;
        }

        // 18차 개편(2) — launchStartAngle을 먼저 캡처하고, 속도는 ratio01(입력)이 아니라 "팔이 실제로
        // 도달한 각도"에서 역산한다(사용자 확정 — 클래스 상단 "18차 개편 (2)" 주석 참고). restAngle
        // (0°)일 때 최소, pulledAngle(90°)일 때 최대가 되도록 InverseLerp로 0~1 비율을 되돌린다.
        launchStartAngle = currentAngle;
        float angleRatio = Mathf.InverseLerp(restAngle, pulledAngle, launchStartAngle);
        float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, angleRatio);
        // 15차 개편 — 앵커 쪽(-Z)으로 발사하도록 기준 방향과 회전 부호를 함께 뒤집었다(클래스 상단
        // "15차 개편" 주석 참고 — 둘 중 하나만 뒤집으면 위/아래가 반대로 나온다).
        Vector3 dir = Quaternion.AngleAxis(launchPitch, aimRoot.right) * (-aimRoot.forward);
        pendingLaunchVelocity = dir.normalized * speed;

        State = ArmState.Launching;
        launchElapsed = 0f;

        // 13차 개편부터 이어온 발사 진단 로그(클래스 상단 "13차 개편" 주석 참고) — 이번엔 스윙 "시작"
        // 시점의 값을 남긴다. 실제 velocity 대입 로그는 Update()의 Launching 분기(스윙 "종료" 시점)에
        // 남는다. 18차 개편 — angleRatio도 함께 남겨 "입력 ratio01과 실제 각도 비율이 얼마나
        // 어긋났는지" 다음 캡처로 확인할 수 있게 했다.
        Debug.Log($"[Catapult][Diag] Fire (ramp start) aimRoot.pos={aimRoot.position:F3} " +
                   $"forward={aimRoot.forward:F3} right={aimRoot.right:F3} | dir={dir.normalized:F3} " +
                   $"launchStartAngle={launchStartAngle:F2} angleRatio={angleRatio:F2} (input ratio01={ratio01:F2}) " +
                   $"speed={speed:F2} pendingVelocity={pendingLaunchVelocity:F3} " +
                   $"duration={launchSwingDuration:F2}");
    }

    // 7차 개편 신규 — 2차 스프링-댐퍼를 직접 적분한다(질량=1 암묵 가정, 세미-임플리시트 오일러).
    // 원래는 노치 전환(BeginPull)과 발사 반동(Update의 Firing) 둘 다 이 함수를 썼지만, 18차 개편
    // (2026-08-06)으로 BeginPull이 시간 기반 ease(SmoothStep)로 바뀌면서 지금은 발사 반동
    // (recoilSpringStiffness/Damping)만 이 함수를 쓴다(클래스 상단 "18차 개편 (3)" 주석 참고) — 함수
    // 자체는 stiffness/damping을 호출부에서 받는 범용 구조 그대로 남겨 뒀다(다른 상태가 다시 이
    // 스프링 질감을 필요로 할 경우를 위해, YAGNI로 삭제하지 않음).
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
