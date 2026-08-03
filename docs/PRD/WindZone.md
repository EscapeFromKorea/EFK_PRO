# 돌풍 구역 (Wind Zone) — PRD

> 가속 발판(AccelSystem)에서 파생된 새 가속 기믹. 사용자 요청(2026-08-02)으로 착수.
> 이 문서는 기획 의도와 "왜"를 담는다. 파일 경로·클래스 구조 등 코드에서 유도 가능한 내용은
> 최소화하고, 구현 시 판단 근거가 되는 결정 사항과 남은 TBD를 명시한다.
> 상태: **구현 완료(`WindZoneSystem/`). 플레이테스트 피드백 5차 반영 완료(§8, 2026-08-02~04).
> 씬 배치·수치 튜닝만 남음.**

## 1. 기획 의도 / 목적

가속 발판(AccelPad)은 밟는 순간 고정 속도로 **순간 부스트**시키는 기믹이라, 부스트 중에는
조향이 일부만 허용된다(`steerControlWhileBoosting`). 돌풍은 이와 대비되는 두 번째 가속
기믹으로, **구역 안에 머무는 동안 계속** 정해진 방향으로 밀리되 **플레이어의 이동/조향을
전혀 제한하지 않는다** — "발판을 밟아 발사되는" 느낌이 아니라 "강한 바람에 스르륵 밀려가는"
느낌이 목적이다. 플레이어는 여전히 자기 뜻대로 움직이면서, 그 위에 바람이 얹혀 원래라면
못 갈 방향/거리로 밀려간다.

## 2. 동작 시나리오

- 플레이어가 돌풍 구역(트리거 볼륨) 안에 들어가면, 매 물리 스텝마다 구역의 **forward 방향**으로
  `windSpeed`만큼 밀리는 힘이 붙는다. 이 힘은 `rampAccel`(가속도)만큼 부드럽게 목표치까지
  붙는다(즉시 스냅이 아니라 "스르륵" 붙는 연출).
- **공중에 떠 있으면 `airMultiplier`배로 더 강하게 밀린다.** 접지 중에는 기본 세기만 적용된다.
- 플레이어 자신의 이동 입력(걷기·점프·조향)은 이 힘과 무관하게 그대로 동작한다 — 바람은
  더해지는 것이지 조작을 대신 가져가는 것이 아니다.
- 구역을 벗어나면 **다음 물리 스텝에 바람 힘이 즉시 0으로 꺼진다**(서서히 감쇠하지 않음 —
  구역 경계가 명확하고 예측 가능해야 한다는 판단).
- 구역 내부에는 바람이 부는 방향을 보여주는 파티클이 항상 흐른다 — 플레이어가 구역에
  들어가기 전에도 어느 방향으로 밀릴지 미리 알 수 있어야 하기 때문.

## 3. 컴포넌트 / 스크립트 설계

발신자-수신자 분리 패턴(저장소 공통 컨벤션)을 그대로 따른다.

- **`WindZone`** (발신자, `WindZoneSystem/` 루트) — 트리거 볼륨. `windSpeed`, `airMultiplier`를
  인스펙터로 노출한다. `OnTriggerStay`에서 겹친 플레이어의 `PlayerWindReceiver`를 찾아
  `SetWindTarget(forward * windSpeed, airMultiplier)`를 매 스텝 호출한다. `OnTriggerExit`은
  따로 두지 않는다 — 수신자가 매 `FixedUpdate` 자신의 상태를 소비·리셋하므로, 구역을 벗어나
  이번 스텝에 `OnTriggerStay`가 안 불리는 것만으로 다음 스텝에 자연히 꺼진다.
  - **`windVisual`(바람 파티클) 자동 동기화(2026-08-03).** `[ExecuteAlways] LateUpdate`가 매
    프레임 `BoxCollider.size`/`center`를 파티클의 `shape.scale`/`position`에 그대로 복사한다.
    씬에서 구역을 드래그로 리사이즈하면(한쪽 면 핸들만 옮겨도 `center`가 같이 움직인다) 파티클이
    항상 실제 트리거 범위와 일치하게 하기 위함 — §8 참고.
- **`PlayerWindReceiver`** (수신자, `WindZoneSystem/` 루트, 플레이어 오브젝트에 부착) — 구역이
  넘겨준 목표 세기를 `rampAccel`로 부드럽게 따라가며, `PlayerMover`가 그 프레임에 만든
  velocity 위에 얹는다(대체가 아니라 덧셈).
  - **왜 `PlayerMover.useTorqueRolling`에 따라 적용 방식이 갈리는가(핵심 설계 판단):**
    구(Sphere, 레거시 이동 경로)는 `PlayerMover`가 매 `FixedUpdate` 수평 velocity를 입력 기반
    값으로 통째로 다시 대입해, 이전 프레임에 더한 바람을 스스로 지운다 — 그래서 이 스크립트가
    나중에 실행되며(execution order로 보장) 매 프레임 목표 세기 **전체**를 다시 더하면 정확히
    맞아떨어진다. 반면 정육면체/정사면체(토크 구르기 경로)는 velocity를 대입하지 않고
    `AddForce`/`AddTorque`로 유지·누적하므로, 매 프레임 전체를 또 더하면 있는 그대로 계속
    쌓여 폭주한다 — 그래서 "이전에 더한 만큼만 갱신"하는 **차분 방식**으로 순수 오프셋만
    유지한다. 두 경로가 애초에 "매 프레임 velocity를 새로 정하는가, 유지·누적하는가"로
    근본적으로 다르기 때문에 생기는 차이이며, 하나의 공식으로 통일할 수 없었다.
  - **리스폰 중에는 완전히 개입하지 않되, 실타래에 매달린 동안은 막지 않는다(2026-08-02
    도입 → 2026-08-03 정정, §8 참고).** `ExternallyDriven`은 실타래 매달림·리스폰이 공유하는
    신호라 그것만으로는 못 가른다. 실제로 가르는 기준은 `ConfigurableJoint` 컴포넌트 존재
    여부다 — 매달림은 `DreamThreadController`가 몸에 이 조인트를 붙였다 떼는 방식이라, 그
    존재만 읽으면(DreamThreadSystem 파일 수정 없이) "지금 매달려 있는가"를 정확히 알 수 있다.
    - **리스폰(낙하 리스폰의 `ExternallyDriven` 구간, 페이드의 `isKinematic` 구간)** → 완전히
      개입하지 않고 내부 상태(`currentPush`/`lastAppliedPush`)를 0으로 리셋한다. 안 그러면
      그 사이 조용히 쌓인 세기가 조작이 돌아오는 순간 한꺼번에 터진다(§8의 원래 버그).
    - **실타래 매달림(`ConfigurableJoint` 존재)** → 막지 않는다. 바람이 부는 구간 위에서
      그네가 흔들리는 상호작용을 살리기 위함(사용자 요청). 대신 `airMultiplier`는 적용하지
      않는다 — 매달림은 `IsGrounded()`가 항상 false(공중 판정)라, 그대로 두면 배율까지 겹쳐
      과가속되는 것이 원래 신고된 증상이었다.
    - **`airMultiplier` 배율만 빼도 여전히 많이 돌았다(2026-08-04, 후속 플레이테스트).** 진자
      운동은 조향으로 힘을 상쇄할 방법이 없어, 걷거나 구를 때와 같은 세기의 바람에도 훨씬 쉽게
      통제를 잃는다. 그래서 매달린 동안은 **기본 세기 자체**를 `PlayerWindReceiver.
      hangingWindMultiplier`(인스펙터 노출, 기본 0.25 = 25%)만큼 한 번 더 줄인다. `airMultiplier`
      는 그대로 미적용이다(둘은 별개 — 배율 자체를 안 쓰는 것과, 기본값을 줄이는 것).

## 4. Tools 메뉴 세팅

- `Assets/WindZoneSystem/Editor/WindZoneMenuItem.cs`, `Tools/WindZoneSystem/Create Wind Zone`.
- 표준 패턴(`AccelSystem/Editor/AccelPadMenuItem.cs`) 준수: SceneView 중앙 생성, Undo 등록,
  Selection 지정.
- 트리거 박스(`BoxCollider`, 기본 4×3×4)와 `WindZone` 컴포넌트뿐 아니라, 바람 방향으로 흐르는
  `ParticleSystem` 자식(`WindZone_Visual`)까지 한 번에 생성한다 — §2의 "구역 시각화" 요구를
  메뉴 한 번으로 충족시키기 위함. 파티클은 `WindZone.windSpeed`와 독립된 순수 시각 효과라
  실시간으로 값을 따라가지는 않는다(필요해지면 그때 연동).
- **파티클 스타일 = Stretched Billboard 바람줄기(2026-08-02, 사용자 선택).** 얇고 긴 입자를
  속도 방향으로 늘려 그려 "빠르게 흐르는 속도선"으로 읽히게 한다(정적인 먼지 느낌 대신). 처음
  버전은 `Velocity over Lifetime`의 z축만 `TwoConstants` 모드로 설정하고 x/y는 기본값
  (`Constant`)으로 남겨 축끼리 모드가 어긋나 "Particle Velocity curves must all be in the same
  mode" 경고가 떴다 — x/y도 `TwoConstants(0,0)`으로 명시해 셋을 같은 모드로 맞춰 해결했다.
  - **구역 절반에만 이펙트가 보이는 문제(2026-08-03).** Stretch 렌더링은 카메라 시선이 파티클
    속도 방향과 거의 평행해질수록 폭이 0에 가깝게 찌그러져 사실상 안 보인다 — 바람 방향을 따라
    보면 카메라도 자연히 그 방향에 가까운 각도가 되므로, 구역 한쪽만 정면에 가깝게 보이는
    배치에서 정확히 이 증상이 난다. 여기에 더해 Automatic 컬링은 스트레치로 실제 화면 범위가
    늘어난 파티클을 반영하지 못해 카메라 프레이밍에 따라 한쪽이 통째로 컬링될 수도 있다. 정확한
    단일 원인을 특정하기 어려워 **둘 다 방어적으로 막았다**: `renderer.minParticleSize`로 화면
    대비 최소 크기를 보장하고, `main.cullingMode = AlwaysSimulate`로 컬링 자체를 껐다(파티클
    수가 적어 비용은 무시할 만하다).

## 5. 기존 시스템 연동 지점

- **PlayerSystem — `PlayerObjectMenuItem.cs` (1줄 수정, 2026-08-02 사용자 허가 후 진행).**
  `Tools/PlayerSystem/Create Player/*`로 새로 만드는 플레이어에 `PlayerWindReceiver`를
  자동 부착한다(`PlayerAccelReceiver`와 같은 방식). **이미 씬에 배치된 기존 플레이어
  오브젝트는 이 변경과 무관하므로 수동으로 컴포넌트를 추가해야 한다.**
- **PlayerMover — `useTorqueRolling`(읽기 전용 참조).** 위 §3의 이유로 어느 도형이 어느
  이동 경로를 쓰는지 판별하는 데만 쓴다. `PlayerMover.cs` 자체는 수정하지 않는다.
- **PlayerShapeController — `IsGrounded()`(읽기 전용 참조).** 접지 여부로 `airMultiplier`
  적용 여부를 가른다.
- AccelSystem과는 코드 의존이 없다(각자 독립적으로 `PlayerMover` 이후에 실행되는 수신자를
  둔다). 같은 순간 가속 발판과 돌풍 구역에 동시에 걸치는 상황은 아직 플레이테스트하지 않았다
  (§7 TBD).

## 6. 확정값 / 기본값

| 항목 | 값 | 상태 |
|---|---|---|
| 바람 방향 | 구역의 forward 축(회전으로 조준) | 확정 |
| `windSpeed`(기본) | 8 m/s | 씬 튜닝 대상 |
| `airMultiplier`(기본) | 2배(공중일 때) | 씬 튜닝 대상 |
| `rampAccel`(기본) | 12 m/s²(목표 세기까지 붙는 속도) | 씬 튜닝 대상 |
| `hangingWindMultiplier`(기본) | 0.007(실타래 매달림 중 기본 세기의 0.7%만 적용, `airMultiplier`는 미적용) | 플레이테스트로 확정(2026-08-04, 최초 0.25는 부족했다) |
| 구역 이탈 시 | 다음 물리 스텝에 즉시 0으로 제거 | 확정 |
| 조작 제한 | 없음 — 이동/조향/점프 모두 그대로 동작, 바람은 덧셈만 | 확정 |
| 시각화 | 구역 내부에 forward 방향으로 흐르는 파티클(항상 표시) | 확정 |
| 트리거 박스 기본 크기 | 4×3×4 Unit(메뉴 생성 시) | 씬 튜닝 대상 |

## 7. 남은 TBD

- **가속 발판과 동시 진입 시 상호작용 미검증.** `PlayerAccelReceiver`(부스트 중 velocity
  전체 대입)와 `PlayerWindReceiver`(덧셈)가 같은 프레임에 같은 도형에 작용할 때의 순서·
  결과는 실제로 플레이테스트하지 않았다. 필요해지면 두 리시버의 execution order를 명시적으로
  조정한다.
- **기존 씬에 배치된 플레이어 오브젝트에 `PlayerWindReceiver` 수동 추가** — 아직 안 함.
- **`windVisual` 필드 이전에 만들어둔 WindZone은 자동 연결이 안 돼 있다.** `WindZone.windVisual`이
  2026-08-03에 추가된 필드라, 그 전에 메뉴로 만든 구역은 인스펙터에서 `WindZone_Visual`의
  `ParticleSystem`을 직접 드래그해 연결해야 리사이즈 동기화가 걸린다.
- **`scalingMode = Hierarchy` 수정 이전에 만든 WindZone도 마찬가지로 소급 적용이 안 된다.**
  기존 구역의 `WindZone_Visual` → Particle System → Main 모듈에서 `Scaling Mode`를 직접
  `Hierarchy`로 바꾸거나, 메뉴로 새로 만들어야 한다.
- **`windSpeed`/`airMultiplier`/`rampAccel` 수치는 전부 가안** — 실제 레벨에 배치해보고
  "밀리는 느낌"이 의도한 세기인지 플레이테스트로 확정해야 한다.
- **다른 기믹과의 조합 레벨 디자인 여지** — 아직 특정 맵/구간에 배치 계획이 없다. 어떤 협동
  동선에 쓸지는 미정.

## 8. 플레이테스트 피드백 수정 이력

### 2026-08-02 (1차, 5건)

| 신고 증상 | 근본 원인 | 조치 |
|---|---|---|
| 실타래(`DreamThreadSystem`)로 매달린 채 구역 안에 있으면 과가속·회전 | `PlayerWindReceiver`가 `PlayerMover.ExternallyDriven`을 안 봐서, 매달림 중 진자 운동(ConfigurableJoint) 위에 raw velocity를 얹어 조인트와 충돌 | §3의 "붙잡힘" 가드 추가 — `ExternallyDriven` 중엔 개입하지 않고 상태 리셋(→ 2026-08-03에 매달림은 예외로 정정, 아래 참고) |
| 페이드 리스폰 도중(암전 아님, 알파 페이드) 구역 안에서 움직임 | 리스폰이 `Rigidbody.isKinematic = true`로 고정하는 동안에도 `currentPush`가 계속 목표치까지 램프업되다가, 페이드 종료로 dynamic 복귀하는 순간 쌓인 세기가 한꺼번에 적용됨 | 같은 가드가 `isKinematic`도 함께 봐서 해결(위와 동일 수정) |
| Scene 뷰에서 구역이 실제보다 작게/눈에 안 띄게 보임 | 기즈모가 `BoxCollider.size`가 아니라 `transform.lossyScale`로 그려져, Zone 오브젝트 스케일이 1이면 실제 트리거(기본 4×3×4)보다 훨씬 작은 큐브가 그려짐 | `BoxCollider.center`/`size`를 직접 읽어 기즈모를 그리도록 수정 + 화살표를 단면 격자(6개)로 늘려 큰 구역에서도 방향이 잘 보이게 함 |
| 콘솔에 `Particle Velocity curves must all be in the same mode` 경고 | `Velocity over Lifetime`의 z축만 `TwoConstants` 모드로 설정하고 x/y는 기본값(`Constant`)으로 남겨 축끼리 모드가 어긋남 | x/y도 `TwoConstants(0,0)`으로 명시해 셋을 같은 모드로 통일 |
| 인게임 시각화가 바람/기류로 안 읽힘 | (버그가 아니라 스타일 요청) 둥근 Billboard 먼지 입자였음 | Stretched Billboard로 교체(사용자 선택: "길게 늘어지는 바람줄기") — §4 참고 |

### 2026-08-03 (2차, 2건 — 1차 조치의 후속 정정)

| 신고 증상 | 근본 원인 | 조치 |
|---|---|---|
| 바람줄기 이펙트가 구역 절반에만 보임(1차 대응 후에도 재현) | Stretch 렌더링의 시야각 의존적 폭 붕괴 + Automatic 컬링(방어적으로 수정)은 근본 원인이 아니었다. **실제 원인은 씬에서 구역을 리사이즈할 때다** — Scene 뷰에서 `BoxCollider`의 한쪽 면 핸들만 드래그하면 `size`뿐 아니라 `center`도 같이 움직이는데, 파티클의 방출 범위(`shape.scale`/`shape.position`)는 메뉴 생성 시점에 한 번 구워둔 값이라 따라가지 않는다 — 그래서 새 콜라이더의 "옛 중심(=한쪽)"에만 파티클이 남고 넓어진 반대쪽엔 안 채워졌다 | `WindZone.windVisual` 필드로 파티클을 연결해두면 `LateUpdate`(에디터에서도 동작, `[ExecuteAlways]`)가 매 프레임 `BoxCollider.size`/`center`를 파티클 `shape`에 그대로 복사한다 — §3/§4 참고 |
| 1차 조치가 실타래 매달림-바람 상호작용을 아예 막아버림(리스폰과 달리 매달림은 막으면 안 됨) | `ExternallyDriven`이 매달림·리스폰 공용 신호라 하나의 플래그로는 둘을 못 가름 | `ConfigurableJoint` 컴포넌트 존재 여부로 "매달림"만 특정해 예외 처리 — 매달림 중엔 바람을 막지 않되 `airMultiplier`만 적용 안 함(§3 참고) |

### 2026-08-03 (3차, 1건 — 리사이즈 방식이 다른 경우의 후속)

사용자가 스크린샷으로 재현 조건을 특정: **`BoxCollider.size`가 아니라 `WindZone` 오브젝트의
Transform Scale 자체를 비균일(예: X=1, Y=1.3153, Z=5.475)하게 늘린 경우**, 2차 조치
(`windVisual` 동기화)로도 여전히 재현됐다.

| 신고 증상 | 근본 원인 | 조치 |
|---|---|---|
| Transform Scale로 구역을 비균일하게 늘리면 파티클이 여전히 한쪽에만 보임 | 새 `ParticleSystem`의 기본 `main.scalingMode`는 `Local`이다 — **이 오브젝트 자신의 로컬 스케일만** 반영하고 **부모(WindZone)의 스케일은 무시한다.** `WindZone_Visual`(파티클) 자신의 로컬 스케일은 항상 (1,1,1)이라, 부모만 늘려도 파티클 방출 범위는 그대로다. 반면 `BoxCollider` 바운드는 항상 전체 계층 스케일을 반영해 커진다 — 그래서 커진 트리거 안에 파티클은 원래 크기 그대로 한쪽에만 남는다 | `main.scalingMode = ParticleSystemScalingMode.Hierarchy`로 변경(→ 4차에서 `Shape`로 재수정, 아래 참고) |

### 2026-08-03 (4차, 2건 — 3차 조치의 후속 정정)

3차 조치(`Hierarchy`)를 스크린샷으로 재검증한 결과, 이번엔 **방출 범위는 맞는데 파티클
자체가 구역 크기에 비례해 두꺼운 덩어리로 부풀어**(구역 안에서 플레이어가 안 보일 정도) 재현됐다.

| 신고 증상 | 근본 원인 | 조치 |
|---|---|---|
| 파티클 작용 범위가 너무 과함(두꺼운 덩어리가 돼 구역 안 플레이어를 가림) | `scalingMode = Hierarchy`는 Shape(방출 범위)뿐 아니라 **Start Size/Speed 등 파티클 자체의 크기까지** 부모 스케일만큼 부풀린다 — 구역을 몇 배로 늘리면 얇은 줄기도 몇 배로 두꺼워진다 | `ParticleSystemScalingMode.Shape`로 변경 — Shape 모듈(방출 범위)에만 부모 스케일을 반영하고 Start Size/Speed는 그대로 유지, "범위는 넓어지되 굵기는 안 변함" |
| 파티클이 막대기(가는 줄기)보다 두꺼움 | `main.startSize`가 0.06으로 상대적으로 굵음 | 0.02로 축소 |

이 수정 이전에 만든 구역은 소급 적용되지 않는다 — 기존 `WindZone_Visual`의 Particle System
Main 모듈에서 `Scaling Mode`를 `Shape`로, `Start Size`를 `0.02` 근처로 직접 바꾸거나, 메뉴로
다시 만들어야 한다.

### 2026-08-04 (5차, 1건 — 매달림 상호작용 추가 완화)

| 신고 증상 | 근본 원인 | 조치 |
|---|---|---|
| `airMultiplier` 미적용으로 정정한 뒤에도 실타래 매달림 중 여전히 많이 돎 | 진자 운동(ConfigurableJoint)은 조향으로 힘을 상쇄할 방법이 없어, 걷거나 구를 때와 같은 세기의 바람에도 훨씬 쉽게 통제를 잃는다 — `airMultiplier`만 빼는 것으로는 부족했다 | `PlayerWindReceiver.hangingWindMultiplier`(인스펙터) 추가 — 매달린 동안은 기본 세기 자체를 줄인다. `airMultiplier`는 여전히 미적용(둘은 별개 노브) |
| 5차 조치의 기본값 0.25로도 여전히 많이 돎(같은 날 재테스트) | 진자가 힘에 반응하는 민감도가 직관보다 훨씬 커서, 25%까지 줄여도 부족했다 | 실측으로 **0.007**(0.7%)까지 낮춰야 원하는 느낌이 확인됨 — 스크립트 기본값과 씬에 이미 배치된 플레이어 3개의 직렬화 값을 모두 0.007로 갱신 |
