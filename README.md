# EFK_PRO — The Axiom

Unity 2022.3.62f3 기반 샌드박스형 3D 플랫포머 게임 "The Axiom"의 백엔드(기믹/플레이어 시스템)
저장소입니다. 프로젝트 전반의 기획은 `PRD.md`, 작업 규칙은 `Assets/CLAUDE.md`를 참고하세요.

## 이번 변경: PlayerSystem 신설

기존에는 플레이어 이동 코드(`Player_movement.cs`)가 "기믹 테스트를 위해 임시로" DoorSystem
폴더에 얹혀 있었습니다. 이를 정식으로 `Assets/PlayerSystem/` 폴더로 분리하고, 점프·구르는
시각 효과·Tab 키 오브젝트 전환 기능을 추가했습니다.

### 무엇을, 왜 바꿨는지

**`Assets/PlayerSystem/`에 있는 파일**

| 파일 | 역할 |
|---|---|
| `PlayerMover.cs` | WASD 이동을 담당하며, 접지 중에는 매 프레임 `rb.angularVelocity`를 이동 velocity 기반 공식으로 하드 설정해 실제 물리 회전으로 "구르는" 연출까지 함께 낸다(자세한 내용은 아래 계층 구조 섹션 참고). |
| `PlayerJump.cs` | Space("Jump" 입력축, 기존에 이미 정의되어 있었음)로 점프. `JumpPad`가 직접 호출하는 `LaunchFromPad()` 제공. |
| `PlayerGroundContact.cs` | `Player_Collider`에 부착해 실제 충돌 접촉 법선 기반으로 접지 여부를 판단한다. `PlayerShapeController.groundContact`에 연결해 재사용한다. |
| `PlayerControlSwitcher.cs` | Tab 키로 여러 플레이어 오브젝트 중 하나에만 조작권을 부여. |
| `Editor/PlayerObjectMenuItem.cs` | `Tools > PlayerSystem > Create Player > Sphere/Cube/Tetrahedron` 메뉴로 플레이어 오브젝트 생성. |
| `Editor/TetrahedronMeshGenerator.cs` | Unity 기본 Primitive에 없는 정사면체 메쉬를 코드로 생성. |

PlayerSystem 신설 초기에는 이동 거리만큼 시각 전용 자식 오브젝트만 회전시켜 "굴러가는 척"하는
`PlayerRollVisual.cs`를 따로 뒀지만("물리 회전은 고정, 시각만 회전"), 이후 "항상 매끄럽게 실제로
구르는" 연출 요구에 맞춰 Rigidbody 회전 자체를 자유롭게 풀고 `PlayerMover`가 직접 각속도를
제어하는 방식으로 대체되면서 `PlayerRollVisual.cs`는 삭제되었다. 최종 아키텍처는 아래
"플레이어 오브젝트 계층 구조" 섹션을 참고할 것.

**수정한 파일**

| 파일 | 변경 내용 |
|---|---|
| `AccelSystem/PlayerAccelReceiver.cs` | 부스트 중 스티어링 입력(`Input.GetAxis`)을 `mover.IsControlled`일 때만 읽도록 감쌈. Tab으로 전환되어 조작권이 없는 오브젝트는 부스트 중에도 입력에 반응하지 않는다. 부스트 자체(패드에 물리적으로 닿으면 발동)는 조작권과 무관하게 그대로 적용된다. |
| `JumpSystem/JumpPad.cs` | 원래 주석 처리되어 있던 "PlayerJump가 있으면 위임" 로직을 실제로 연결. `PlayerJump`가 없는 일반 Rigidbody 오브젝트에는 기존처럼 직접 `AddForce`. |
| `ScalingSystem/Playershapecontroller.cs` | `PlayerRollVisual` 폐기 및 자유 회전 전환에 맞춰, 회전에 영향받지 않는 `PlayerGroundContact` 기반 접지 판정을 연동했고, 콜라이더 스케일 보정도 "역수 보정으로 히트박스 고정"에서 "Root 스케일을 그대로 물려받아 물리 판정도 함께 커지고 작아짐"으로 바뀌었다. |

**삭제(이동)한 파일**

- `DoorSystem/Player_movement.cs` → `PlayerSystem/PlayerMover.cs`로 이동(당시 Unity 스크립트 GUID 보존을 위해 `.cs`+`.cs.meta`를 함께 이동).
- `PlayerSystem/PlayerRollVisual.cs` → 이후 작업에서 완전히 삭제(역할은 `PlayerMover.cs`에 흡수).

**손대지 않은 파일** — `DoorSystem`의 `doorPhysics.cs`/`LeverHead.cs`/`PadTrigger.cs`는 Rigidbody
물리와 `"Player"` 태그 검사만으로 동작하므로 이번 작업과 무관해 수정하지 않았습니다.

## 플레이어 오브젝트 계층 구조

`Tools > PlayerSystem > Create Player > Sphere/Cube/Tetrahedron`(`PlayerObjectMenuItem.cs`)가
생성하는 최종 구조는 다음과 같습니다:

```
Player_<Shape>                (Root, Rigidbody 회전 자유(RigidbodyConstraints.None), tag: Player)
├─ PlayerMover / PlayerJump / PlayerAccelReceiver / PlayerShapeController
├─ Player_Mesh                (트리거 콜라이더, tag: Player) — AccelPad/ScalePad/PadTrigger가 감지
│  └─ Player_MeshVisual       (MeshFilter+MeshRenderer만) — Root와 함께 그대로 회전하는 시각 메쉬
└─ Player_Collider            (솔리드 콜라이더 + PlayerGroundContact, tag: Player) — 지면/JumpPad/
                                 DoorSystem이 충돌
```

Rigidbody 회전을 더 이상 고정하지 않는 이유: 정육면체/정사면체가 실제 물리로 모서리에 걸리며
통통 튀듯 자연스럽게 구르는 연출을 얻기 위해서입니다. 대신 `PlayerMover`가 접지 중 매 프레임
`rb.angularVelocity = Cross(Vector3.up, 이동 velocity) / rollRadius`를 하드 설정해, 구/정육면체/
정사면체 세 도형 모두 같은 공식 하나로 매끄럽게 굴러가는 것처럼 보이게 만듭니다(별도의 "시각
전용 회전" 스크립트는 더 이상 없습니다). `Player_Mesh`/`Player_MeshVisual`/`Player_Collider`는
모두 로컬 회전이 identity라 Root의 실제 회전을 그대로 물려받습니다.

회전이 자유로워진 대신, Root 원점 기준 고정 방향 Raycast로는 접지(바닥에 닿았는지)를 신뢰할 수
없습니다 — 물체가 회전하면 Root와 실제 지면 사이의 상대 방향이 매 프레임 달라지기 때문입니다.
그래서 `Player_Collider`에 `PlayerGroundContact`를 붙여 실제 충돌 접촉 법선(월드 스페이스라 회전과
무관)으로 접지를 판단하고, 이를 `PlayerShapeController.groundContact`에 연결해 `PlayerMover`/
`PlayerJump`도 함께 재사용합니다(연결하지 않으면 예전의 고정 Raycast 방식으로 폴백하는데, 회전하는
오브젝트에는 부정확합니다).

트리거 콜라이더(`Player_Mesh`)와 솔리드 콜라이더(`Player_Collider`)를 분리해두는 이유는 회전과는
무관합니다 — Unity의 Collider 컴포넌트 하나는 트리거와 솔리드를 동시에 겸할 수 없어서,
`OnTriggerEnter`로 감지하는 기믹(AccelPad/ScalePad/PadTrigger)과 `OnCollisionEnter`로 감지하는
기믹(지면/JumpPad/DoorSystem)을 각각 전담하는 콜라이더를 별도 자식 오브젝트에 나눠 둔 것입니다.

콜라이더 형태는 도형마다 다릅니다: 구는 `SphereCollider`, 정육면체는 `BoxCollider`, 정사면체는
Unity에 대응하는 기본 Primitive Collider가 없어 꼭짓점 4개에 작은 `SphereCollider`를 배치한
컴파운드로 근사합니다(단, 트리거 쪽은 침투 방지가 필요 없어 실제 사면체 모양의
`MeshCollider(convex)`를 그대로 씁니다). 정육면체/정사면체 솔리드 콜라이더에는 저마찰
`PhysicMaterial`을 씌워, 평평한 면으로 접지한 채 각속도를 강제할 때 생기는 접촉 솔버 충돌을
완화합니다.

## 각 폴더 시스템 동작 방식

### AccelSystem — 가속 발판
`AccelPad`가 트리거로 플레이어를 감지하면 `PlayerAccelReceiver.ApplyBoost()`를 호출합니다.
`PlayerAccelReceiver`는 램프업(RampUp) → 유지(Hold) → 감속(Decel) 3단계 상태머신으로 velocity를
직접 제어하며, `DefaultExecutionOrder(100)`로 `PlayerMover`보다 항상 나중에 실행되어 부스트
중에는 최종 velocity 결정권을 가집니다. 부스트 중에도 조작 중인 오브젝트라면 좌우 스티어링이
일부 반영됩니다(`steerControlWhileBoosting`).

### DoorSystem — 레버로 여닫는 문
`LeverHead`는 플레이어와의 충돌 노멀 방향에 따라 레버를 회전시키고, `doorPhysics`는 레버 각도
또는 `PadTrigger`(발판)의 눌림 상태를 보고 문을 목표 위치로 이동시킵니다. 플레이어가 문 사이에
끼어 있으면(`OnTriggerEnter/Exit` 카운팅) 문이 멈춰서 끼임을 방지합니다.

### JumpSystem — 점프대
`JumpPad`는 `OnCollisionEnter`로 플레이어(솔리드 콜라이더)와의 충돌을 감지하고, 위에서 떨어지는
충돌(윗면 노멀)은 무시합니다. 플레이어에게 `PlayerJump`가 있으면 `LaunchFromPad()`로 위임하고,
없으면 직접 `Rigidbody.AddForce`로 발사합니다.

### ScalingSystem — 크기/형태 변형 발판
`ScalePad`를 밟고 있는 동안 `PlayerShapeController.SetAction()`으로 스케일 변경 방향(상/하,
좌/우, 리셋)을 지정하면, `PlayerShapeController`가 매 프레임 Root의 `localScale`을 보간합니다.
`Player_Collider`의 로컬 스케일은 `(1,1,1)`로 고정해 Root의 실시간 스케일을 그대로 물려받게
해서, 물리적 히트박스 크기도 시각 크기와 함께 커지고 작아집니다(단, `SphereCollider`를 쓰는
구 플레이어는 `useAverageColliderScale`로 X/Y 평균을 강제해 비균일 스케일에서 생기는 반지름
왜곡만 보정합니다). 접지 판정은 `PlayerGroundContact`가 연결되어 있으면 그 값을 그대로 재사용
합니다.

### PlayerSystem — 플레이어 조작
`PlayerMover`가 WASD 이동을 담당하며, 접지 중에는 이동 velocity로부터 계산한 각속도를 Rigidbody에
직접 설정해 구/정육면체/정사면체 모두 실제 물리로 굴러가게 만듭니다. `PlayerJump`가 Space 점프를
담당하고, 접지 판정은 `PlayerGroundContact`(실제 충돌 접촉 법선 기반)를 우선 사용하며 없으면
고정 Raycast로 대체합니다. 여러 플레이어 오브젝트가 씬에 있으면 `PlayerControlSwitcher`가 Tab
키로 조작권(`IsControlled`)을 하나씩 순환시킵니다. 조작권이 없는 오브젝트는 입력에 반응하지
않지만, 다른 기믹(부스트, 점프대, 중력 등)에 의한 물리 이동은 그대로 받습니다.

## Inspector 설정값

아래는 각 스크립트가 Inspector에 노출하는 `public` 필드(및 `[Tooltip]`이 달린 필드)를
현재 코드 기준으로 정리한 것입니다. 단순 필드 나열이 아니라, 값을 올리거나 내렸을 때 실제
게임플레이가 어떻게 달라지는지와 다른 필드/다른 스크립트 값과 어떻게 맞물리는지를 함께
적었습니다. `PlayerControlSwitcher.cs`, `Playersetup.cs`(`PlayerSetup`)처럼 Inspector에
노출되는 필드가 아예 없는 스크립트는 그 사실만 짧게 언급합니다.

### AccelSystem — 가속 발판

#### AccelPad.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `boostSpeed` | `20f` | 패드를 밟는 순간 부여되는 고정 수평 속도(m/s). `PlayerMover.moveSpeed`(기본 5)의 몇 배가 되어야 "가속감"이 실제로 체감된다 — 5~6 근처로 낮추면 밟아도 별 차이를 못 느끼고, 너무 높이면(40 이상) 좁은 통로에서 벽에 바로 부딪히거나 다음 지형을 그대로 넘어가버릴 수 있다. |
| `holdDuration` | `1f` | `boostSpeed`가 감속 없이 그대로 유지되는 시간(초). 길게 잡을수록 부스트 구간을 넓게 잡아도 속도가 일정하게 유지되어, 긴 직선 도약 구간에 적합하다. |
| `decelDuration` | `1.5f` | 유지 시간이 끝난 뒤 0으로 줄어드는 데 걸리는 시간(초). 짧게 하면 부스트가 끝나는 순간 급정거하듯 느껴지고, 길게 하면 서서히 원래 속도로 돌아온다. `PlayerAccelReceiver.rampDuration`(들어갈 때)과 대칭을 이루는 "빠져나갈 때" 곡선이다. |

#### PlayerAccelReceiver.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `rampDuration` | `0.15f` | 부스트가 시작될 때 기존 속도에서 `boostVelocity`까지 보간되는 시간(초). 0에 가까울수록 패드를 밟는 순간 훅 밀리는 느낌이 강해지고, 크게 하면 부드럽게 가속된다. |
| `steerControlWhileBoosting` | `0.3f` (`Range(0,1)`) | 부스트 진행 중 플레이어 입력이 진행 방향에 수직인 성분에 반영되는 비율. `0`이면 부스트 방향으로만 강제 이동(레일을 타는 느낌), `1`이면 부스트 중에도 사실상 자유 조작이 가능해져 부스트의 "밀려나감" 의미가 옅어진다. 부스트 방향과 나란한 성분(전진/후진)에는 이 값이 적용되지 않고 그대로 더해진다는 점에 유의. |

### DoorSystem — 레버로 여닫는 문

#### doorPhysics.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `doorTargetYOffset` | `3f` | 문이 완전히 열렸을 때 시작 위치 대비 올라가는 높이. 실제 문 모델의 높이/통과 가능 여백에 맞춰야 하며, 너무 작으면 플레이어가 못 지나가고 너무 크면 문이 다 열리기까지 체감상 오래 걸린다. |
| `doorSpeed` | `2f` | 문이 목표 위치(레버 각도 또는 패드 눌림 상태로 정해짐)를 향해 이동하는 속도. `LeverHead.rotateSpeed`와 별개 값이라, 레버는 빨리 도는데 문은 느리게 따라가는(또는 반대) 어긋난 느낌을 줄 수 있어 함께 맞춰보는 것이 좋다. |
| `leverHead` | `null` | 문이 각도를 읽어올 `LeverHead` 참조. 비워두면 항상 레버 각도를 닫힘 위치(각도 하한)로 취급해, 사실상 `PadTrigger`(발판)의 눌림 여부만으로 문을 여닫게 된다. |

내부적으로 레버 각도 진행률을 `leverHead.maxAngle`(연결되어 있지 않으면 기본값 `40f`)을 기준으로 `Mathf.InverseLerp(-maxAngle, maxAngle, leverAngle)`로 계산한다. `LeverHead.maxAngle`을 바꾸면 이 진행률 계산도 항상 함께 따라가므로, 더 이상 `doorPhysics.cs`와 `LeverHead.cs` 사이에 값을 수동으로 맞출 필요가 없다.

#### LeverHead.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `leverPivot` | `null` | 실제로 회전하는 Transform. 반드시 연결해야 하며, 비어있으면 `Start()`에서 경고만 뜨고 레버가 전혀 움직이지 않는다. |
| `rotateSpeed` | `3f` | 플레이어가 미는 동안 목표 각도까지 회전하는 속도 계수(코드 내부에서 `*20`이 곱해져 적용됨). 크게 하면 살짝만 밀어도 레버가 즉각 끝까지 돌아가고, 작게 하면 계속 밀고 있어야 서서히 돌아가는 "힘겨루기" 느낌을 낼 수 있다. |
| `maxAngle` | `45f` | 레버가 밀릴 수 있는 최대 각도(양방향). `doorPhysics.cs`가 이 값을 그대로 참조해 문 열림 진행률을 계산하므로, 여기서 값을 바꾸면 별도 조정 없이 문 쪽 계산에도 바로 반영된다. |
| `normalSmoothSpeed` | `5f` | 플레이어가 레버를 미는 동안 충돌 노멀 방향을 얼마나 빠르게 보간할지. 크게 하면 방향을 바꿔 밀 때 반응이 즉각적이지만 떨림이 생기기 쉽고, 작게 하면 부드럽지만 반응이 둔해진다. |
| `returnDelay` | `5f` | 플레이어가 레버에서 떨어진 뒤 원위치로 복귀를 시작하기까지 대기하는 시간(초). 크게 하면 문이 열린 채로 오래 유지되는 여유를 줘 퍼즐 난이도를 낮출 수 있다. |
| `returnSpeed` | `1.5f` | 복귀 시 `-45f`(닫힘 기준각)로 돌아가는 속도. `rotateSpeed`와 분리되어 있어 "밀 때는 빠르게, 복귀는 천천히" 같은 비대칭 연출이 가능하다. |
| `pushExitGrace` | `0.3f` | 충돌이 끊긴 뒤 즉시 복귀 로직으로 넘어가지 않고 유예를 두는 시간(초). 플레이어가 모서리에 걸려 접촉이 매 프레임 끊겼다 이어졌다 할 때 레버가 되돌아가려다 다시 밀리는 떨림을 막아준다. 너무 작으면 접촉이 살짝만 끊겨도 민감하게 반응하고, 너무 크면 손을 뗀 후에도 한참 레버가 그대로 멈춰 있는 것처럼 보인다. |

#### PadTrigger.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `padPressDepth` | `0.1f` | 패드가 눌렸을 때 아래로 내려가는 깊이. 순수 시각적 피드백(단순 `Transform` 이동)이며 물리 판정에는 영향이 없다. 너무 깊게 잡으면 바닥 지형과 시각적으로 겹쳐 보일 수 있다. |
| `padSpeed` | `5f` | 패드가 눌린 위치/원위치로 이동하는 속도. |
| `doorPhysicsScript` | `null` | 이 패드가 눌렸을 때 `SetPadPressed()`를 호출할 대상 문. 비워두면 패드는 시각적으로만 눌리고 어떤 문에도 영향을 주지 않는다. |

### JumpSystem — 점프대

#### JumpPad.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `jumpForce` | `20f` | `AddForce(ForceMode.Impulse)`로 위로 가해지는 힘. `PlayerJump.jumpForce`(기본 `7f`)보다 훨씬 크게 잡혀 있는데, 이는 "평범한 점프"와 "발판을 밟았을 때의 특수 발사"를 값으로 구분 짓기 위한 의도로 보인다. 너무 크게 올리면 위층 구조물을 뚫고 지나가거나 착지 지점을 예측하기 어려워진다. |
| `playerTag` | `"Player"` | 충돌 판정 대상 태그. 커스텀 태그를 쓰는 특수 오브젝트가 아니면 건드릴 필요가 없다. |

### ScalingSystem — 크기/형태 변형 발판

#### Playersetup.cs (`PlayerSetup`)

Inspector에 노출되는 필드가 없다. `Awake()`에서 태그/Rigidbody/`PlayerShapeController` 존재
여부만 자동으로 점검하고, `PlayerShapeController`가 없으면 자동으로 추가해주는 안전망 역할만
한다.

#### Playershapecontroller.cs (`PlayerShapeController`)

| 필드 | 기본값 | 설명 |
|---|---|---|
| `verticalScaleSpeed` | `0.5f` | `ScalePad`(상/하 조절 패드)를 밟고 있는 동안 초당 Y축 스케일 변화량. `ScalePad`와 짝을 이루는 핵심 튜닝값 — 너무 빠르면 발판에 잠깐만 닿아도 순식간에 `minScale`/`maxScale`에 도달해 "밟고 있는 시간"이라는 조작 여유가 사라지고, 너무 느리면 답답하게 느껴진다. |
| `horizontalScaleSpeed` | `0.5f` | 좌/우(X축) 버전. 의미는 `verticalScaleSpeed`와 동일. |
| `minScale` | `0.2f` | 스케일 클램프 최솟값. 너무 작게 잡으면(0.1 이하) 좁은 틈을 통과하는 퍼즐 요소로 쓸 수는 있지만, 조작이 예민해지고 물리 정밀도 문제로 콜라이더가 떨릴 수 있다. |
| `maxScale` | `5.0f` | 스케일 클램프 최댓값. 코드 주석에도 명시되어 있듯, 콜라이더가 커지는 도중 주변 지형과 겹치면 물리적으로 튕겨나갈 수 있으므로 값을 키울수록 `verticalScaleSpeed`/`horizontalScaleSpeed`는 오히려 낮춰서 급격한 변화를 피하는 편이 안전하다. |
| `resetSpeed` | `2f` | `Reset` 패드를 밟았을 때 초기 스케일로 돌아가는 속도(`MoveTowards` 기반 절대 변화량). `verticalScaleSpeed`/`horizontalScaleSpeed`와 달리 min/max 스케일과 무관하게 항상 일정한 속도로 복귀한다. |
| `lerpSpeed` | `8f` | `targetScale`을 실제 `localScale`이 뒤따라가는 보간 속도. `0`으로 두면 보간 없이 즉시 적용되어 스케일이 매 프레임 계단식으로 튀므로 디버깅 용도가 아니면 권장하지 않는다. |
| `lerpSnapEpsilon` | `0.001f` | 목표 스케일에 이 값 이하로 가까워지면 정확히 스냅하는 임계값. `Lerp`가 수학적으로 무한히 근접만 하고 정확히 도달하지 못하는 문제를 방지한다. 너무 크게 잡으면 스케일이 목표치에 닿기 전에 멈춘 것처럼 보이는 오차가 생긴다. |
| `meshTransform` | `null` | `Player_Mesh` 참조. 비워두면 경고만 뜨고 Y 위치 보정이 동작하지 않는다. |
| `colliderTransform` | `null` | `Player_Collider` 참조. 비워두면 경고만 뜨고 콜라이더 크기/위치 보정이 동작하지 않아, 스케일은 바뀌는데 히트박스는 그대로인 상태가 된다. |
| `useAverageColliderScale` | `false` | `SphereCollider`(구 플레이어)처럼 X/Y를 독립 반영하지 못하는 콜라이더에서는 `true`로, `BoxCollider`(정육면체/정사면체)에서는 `false`로 둔다. 구 플레이어인데 `false`로 두면 좌우/상하 비율이 달라질 때 콜라이더가 시각 메쉬와 어긋난다. |
| `groundContact` | `null` | `Player_Collider`에 붙은 `PlayerGroundContact` 참조. 비워두면 예전 방식인 고정 방향 Raycast로 대체되는데, 코드 주석대로 "Root가 회전하지 않는 오브젝트에서만 신뢰 가능"하다. 현재 정육면체/정사면체가 실제로 굴러가며 자유 회전하므로, 사실상 항상 연결해두는 것을 전제로 한다. |
| `groundCheckDistance` | `0.15f` | `groundContact`가 비어있을 때만 쓰이는 대체 경로용 Raycast 거리. `Player_Mesh` 콜라이더의 절반 높이보다 살짝 크게 잡는다. |
| `groundLayer` | `~0`(Everything) | `groundContact`가 비어있을 때만 쓰이는 대체 경로용 레이어 마스크. |

#### Scalepad.cs (`ScalePad`)

| 필드 | 기본값 | 설명 |
|---|---|---|
| `padType` | `IncreaseVertical` | 이 패드가 어떤 축을 어느 방향으로 바꿀지 정하는 값. 미세 튜닝값이라기보다, 패드 하나를 배치할 때 반드시 정해야 하는 정체성에 가깝다. |
| `playerTag` | `"Player"` | 트리거 판정 대상 태그. |
| `activateColor` | `Color.yellow` | 패드를 밟고 있는 동안의 색. 순수 시각 피드백이며 게임플레이에는 영향 없음. `MeshRenderer`가 없으면 무시된다. |
| `defaultColor` | `Color.white` | 평상시 색. |

### PlayerSystem — 플레이어 조작

#### PlayerControlSwitcher.cs

Inspector에 노출되는 필드가 없다(모든 필드가 `private`). Tab 키 순환 로직과 등록/해제만
자동으로 처리한다.

#### PlayerGroundContact.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `groundNormalThreshold` | `0.5f` | 충돌 접촉 노멀의 Y 성분이 이 값보다 커야 "바닥"으로 인정한다(완전한 수평면은 `1`). 값을 낮추면 제법 가파른 경사면까지 바닥으로 인정하는 범위가 넓어지고(가파른 벽에서도 점프 가능해질 수 있음), `1`에 가깝게 높이면 살짝만 기울어진 발판에서도 공중 취급되어 점프가 안 먹힐 수 있다. |

#### PlayerJump.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `jumpForce` | `7f` | 일반 점프 시 위로 가해지는 힘. `JumpPad.jumpForce`(기본 `20f`)와 의도적으로 차이를 둬, 평범한 점프와 점프대의 "발사" 느낌을 구분하는 것으로 보인다. |
| `groundCheckDistance` | `0.15f` | `PlayerShapeController`가 없을 때만 사용되는 자체 접지 판정용 Raycast 거리. `PlayerShapeController`가 붙어 있으면 이 값은 아예 무시되고 그쪽의 접지 판정을 그대로 재사용한다. |
| `groundLayer` | `~0`(Everything) | 위와 동일하게 `PlayerShapeController`가 없을 때만 쓰이는 레이어 마스크. |

#### PlayerMover.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `moveSpeed` | `5f` | 수평 이동 속도. `AccelPad.boostSpeed`(기본 `20f`)와 비교하면 "기본 속도 대비 4배 정도가 부스트 속도"라는 감각으로 맞춰져 있다. |
| `rollRadius` | `0.5f` | 접지 중 구르는 시각 회전의 반경으로 취급되는 값(`각속도 = Cross(Vector3.up, 이동 velocity) / rollRadius`). 코드 주석대로 오브젝트의 실제 반지름/절반 크기와 맞아야 "미끄러지지 않고 구르는" 것처럼 보인다 — 실제 크기보다 작게 두면 이동 거리에 비해 과하게 빨리 도는 것처럼 보이고, 크게 두면 거의 안 도는 것처럼 보인다. `ScalingSystem`으로 크기가 실시간으로 변하는 오브젝트라면, 이 값은 스케일 변화에 따라 자동으로 조정되지 않으므로(고정값) 크기를 크게 키운 상태에서는 실제 반지름과 어긋나 회전이 부자연스러워질 수 있다. |
| `groundCheckDistance` | `0.15f` | `PlayerShapeController`가 없을 때만 사용되는 자체 접지 판정용 Raycast 거리. |
| `groundLayer` | `~0`(Everything) | 위와 동일하게 `PlayerShapeController`가 없을 때만 쓰이는 레이어 마스크. |

## 검증 방법

Unity 에디터가 없는 환경에서 작업했기 때문에 자동 실행 검증은 하지 못했습니다. Unity
에디터에서 아래 순서로 직접 확인해주세요.

1. **씬 참조 무결성**: 기존 `Assets/Scenes/SampleScene.unity`를 열어 `Player_Root`의
   컴포넌트가 Missing Script로 깨지지 않았는지 확인 (스크립트 이동 시 GUID를 보존했으므로
   깨지지 않아야 정상입니다).
2. **플레이어 생성**: `Tools > PlayerSystem > Create Player > Sphere / Cube / Tetrahedron`로
   3종을 각각 생성.
3. **기본 조작**: Play 모드에서 WASD로 이동, Space로 점프, 이동 중 시각 메쉬가 굴러가듯
   회전하는지 확인.
4. **Tab 전환**: 플레이어 오브젝트를 2개 이상 씬에 둔 상태로 Tab을 눌러 조작 대상이
   바뀌는지, 조작 중이 아닌 오브젝트는 WASD/Space에 반응하지 않는지 확인.
5. **기믹 호환성**: 새로 만든 플레이어 오브젝트로 기존 `AccelPad`, `JumpPad`, DoorSystem의
   레버/문/발판, `ScalePad` 위를 지나가며 4개 기믹이 이전과 동일하게 동작하는지 확인.
6. **비조작 중 물리**: Tab으로 조작권을 다른 오브젝트로 넘긴 뒤, 방치된 오브젝트가 JumpPad나
   AccelPad 위에 있었다면 입력 없이도 물리적으로는 계속 튕기거나 가속되는지 확인.
