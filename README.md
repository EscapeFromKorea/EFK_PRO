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
| `PlayerMover.cs` | WASD 이동을 담당. **두 경로가 공존한다**: 정육면체/정사면체는 토크+마찰로 실제 굴리는 물리 구르기(`useTorqueRolling=true`), 구(Sphere)는 예전 방식인 velocity/각속도 하드 대입(legacy). 조종성 보정(`steerResponsiveness`/`turnAssist`), 입력 방향 회전(`inputYawOffset`), 스케일 토크 보정(`scaleTorqueCompensation`), 조작권 상실 시 감쇠(`uncontrolledDamping`)까지 담당한다(자세한 "왜"는 아래 전용 섹션 참고). |
| `PlayerJump.cs` | Space("Jump" 입력축, 기존에 이미 정의되어 있었음)로 점프. `JumpPad`가 직접 호출하는 `LaunchFromPad()` 제공. 조작권이 없으면(Tab 전환) 큐된 점프를 비워 뒤늦게 점프하지 않게 한다. |
| `PlayerGroundContact.cs` | `Player_Collider`에 부착해 실제 충돌 접촉 법선 기반으로 접지 여부를 판단한다. `PlayerShapeController.groundContact`에 연결해 재사용한다. |
| `PlayerControlSwitcher.cs` | Tab 키로 여러 플레이어 오브젝트 중 하나에만 조작권을 부여. 순환 순서는 GameObject 이름 오름차순(ordinal)으로 결정적이며, 활성 플레이어가 바뀌면 `PlayerFollowCamera`에 새 타깃을 알린다. |
| `PlayerFollowCamera.cs` | 활성 플레이어의 **위치만** 부드럽게 따라가는 3인칭 카메라. 구르기 회전에 휩쓸려 화면이 도는 멀미를 막기 위해 타깃 회전은 무시한다. 씬의 기존 Main Camera에 부착해 쓴다. |
| `Editor/PlayerObjectMenuItem.cs` | `Tools > PlayerSystem > Create Player > Sphere/Cube/Tetrahedron` 메뉴로 플레이어 오브젝트 생성. 정육면체/정사면체에는 토크 구르기 설정(고마찰 그립 재질 등)을, Main Camera에는 `PlayerFollowCamera`를 자동 세팅한다. |
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

## 토크+마찰 구르기, 조종성, Tab 전환, 팔로우 카메라 (PlayerSystem 후속 개편)

초기 PlayerSystem은 세 도형 모두 접지 중 `rb.angularVelocity`를 이동 velocity로부터 나온 값
(`Cross(up, move) / rollRadius`, v=ωr)으로 매 프레임 하드 설정해 "굴러가는 것처럼" **보이게만**
했습니다. 이는 물리엔진이 굴리는 게 아니라 스크립트가 결과 회전을 흉내 내 강제하는 방식이라,
정육면체/정사면체처럼 평평한 면으로 바닥에 밀착하는 도형에서는 접촉면 양 끝이 서로 반대로
움직이려 해(하나는 파고들고 하나는 뜨려) PhysX 접촉 솔버와 매 스텝 충돌했습니다(강제 스핀 vs
모서리 피벗의 근본 모순). "현실적으로 모서리를 축으로 굴러가게" 해달라는 요구에 맞춰 다면체를
**토크+마찰 기반 물리 구르기**로 다시 설계했습니다. 구(Sphere)는 이 문제가 없어(접촉점이 항상
하나) 기존 방식을 그대로 둡니다.

- **토크로 굴리고 마찰이 붙잡는다.** `useTorqueRolling`이 켜진 도형(정육면체/정사면체)은 접지 중
  velocity/각속도를 직접 대입하지 않고, 이동 방향과 직교하는 수평축으로 `AddTorque`만 겁니다.
  선형 전진은 회전의 결과로 자연 발생합니다. 이때 접촉 모서리가 미끄러지지 않고 **피벗** 역할을
  하려면 마찰이 높아야 하므로, 예전의 저마찰 재질을 걷어내고 **고마찰 그립 `PhysicMaterial`**을
  씌웁니다(정육면체는 넓은 면으로 접지해 더 미끄러지기 쉬워 정사면체보다 강한 그립을 줍니다).
- **조종성 보정.** 순수 토크만으로는 한 축에 각운동량이 붙으면 방향 전환이 어렵습니다.
  `steerResponsiveness`(원하는 구르기 축과 어긋난 각속도 성분만 감쇠)와 `turnAssist`(수평 속도의
  방향만 입력 쪽으로 재정렬, 속력은 유지)로 "구르는 물리감"을 유지하면서 조향감을 줍니다.
- **입력 방향 회전(`inputYawOffset`).** 카메라 시점과 입력 축이 어긋나 있어, 입력 방향을 월드 up
  기준으로 회전 보정합니다. 세 도형(구 포함, 레거시 경로도)에 **공통** 적용해 방향감을 일치시킵니다.
- **스케일 대응(`scaleTorqueCompensation`).** ScalingSystem으로 도형이 커지거나 납작해지면 관성
  텐서와 무게중심-피벗 거리가 함께 커져 고정 토크로는 모서리를 못 넘습니다. 현재 스케일에 비례해
  토크를 자동으로 키워 뒤집힘을 보정합니다(구는 legacy라 해당 없음).

**Tab 전환 개편.** 조작권을 잃은 플레이어가 관성/각속도로 계속 굴러가지 않도록
`uncontrolledDamping`으로 수평 속도·각속도를 프레임레이트 무관하게 빠르게 감쇠합니다(수직=중력은
유지). 순환 순서는 등록 순서(비결정적)가 아니라 **GameObject 이름 오름차순(ordinal)**으로 고정되며
(`Player_Cube → Player_Sphere → Player_Tetrahedron`), 활성 대상은 인덱스가 아닌 **참조**로 추적해
정렬/등록/파괴로 목록이 흔들려도 같은 플레이어를 계속 가리킵니다. 파괴된 참조는 매 시점
정리(prune)합니다. `IsControlled`은 외부에서 함부로 못 바꾸도록 읽기 전용 프로퍼티가 되었고,
전환은 `SetControlled()`를 거칩니다.

**팔로우 카메라.** 활성 플레이어를 따라가되, 토크 모드 도형은 Root가 실제로 회전(구르기)하므로
카메라가 그 회전을 물려받으면 화면이 통째로 돌아 멀미가 납니다. 그래서 `PlayerFollowCamera`는
타깃의 **위치만** 읽고(offset은 월드 공간 고정), 회전은 "카메라→타깃"을 월드 up 기준으로 바라보는
방향으로만 정해 구르기와 분리합니다. `cameraYawOffset`으로 시점 각도를 `inputYawOffset`과 맞추고,
스위처가 Tab 전환 시 카메라 타깃을 갱신합니다.

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
통통 튀듯 자연스럽게 구르는 연출을 얻기 위해서입니다. 굴리는 **방식은 도형에 따라 다릅니다**
(자세한 "왜"는 위의 "토크+마찰 구르기 …" 섹션 참고):

- **정육면체/정사면체(토크 모드, `useTorqueRolling=true`)**: `PlayerMover`가 접지 중 토크만 걸고
  고마찰 그립으로 실제 모서리 피벗 텀블링을 일으킵니다. velocity/각속도를 하드 설정하지 않습니다.
- **구(Sphere, legacy)**: 접지 중 매 프레임 `rb.angularVelocity = Cross(Vector3.up, 이동 velocity)
  / rollRadius`를 하드 설정하는 예전 방식을 그대로 씁니다(구는 접촉점이 하나뿐이라 이 방식으로도
  자연스럽게 굴러갑니다).

별도의 "시각 전용 회전" 스크립트는 더 이상 없으며, `Player_Mesh`/`Player_MeshVisual`/
`Player_Collider`는 모두 로컬 회전이 identity라 Root의 실제 회전을 그대로 물려받습니다.

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
`MeshCollider(convex)`를 그대로 씁니다). 정육면체/정사면체 솔리드 콜라이더에는 **고마찰 그립
`PhysicMaterial`**을 씌웁니다 — 토크로 굴릴 때 접촉 모서리가 미끄러지지 않고 피벗 역할을 해야
실제로 구르기 때문입니다(정육면체 `static 1.0 / dynamic 0.8`, 정사면체 `static 0.7 / dynamic 0.55`,
둘 다 `frictionCombine=Maximum`). 예전에는 각속도를 강제하던 방식의 접촉 충돌을 줄이려 반대로
저마찰을 썼지만, 토크+마찰 구르기로 바뀌면서 정반대 튜닝이 되었습니다. 또한 토크 모드 도형은
Rigidbody `centerOfMass`를 기하 중심 `(0, 0.5, 0)`으로 고정하고 `angularDrag`를 `0.1`로,
`maxAngularVelocity`를 `20`으로 잡아 구르기가 대칭적이고 과하게 죽지 않도록 합니다.

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

### CloudTrampolineSystem — 구름 트램펄린
`CloudTrampoline`이 `OnCollisionEnter`로 **위에서 착지한** 플레이어(접점 법선이 아래로 강하게
향하는 상단 착지)를 감지해, `PlayerJump.LaunchToHeight(baseBounceHeight)`로 목표 높이(기본 6 Unit)
까지 발사합니다. JumpPad와 같은 위임 방식이라 도형 질량과 무관하게 정확히 그 높이에 도달하며,
측면 스침(normal.y가 애매한 접촉)은 발사하지 않아 옆으로 점프해 지나갈 때 오발사가 없습니다.

무게 협동 축은 **과부하 붕괴**입니다. 구름 위 도형들의 합산 질량(`Rigidbody.mass`, 세트 B: 구1.0/
세모1.5/네모3.0)에 따라 세 가지로 갈립니다 — 가벼우면(`<3.0`) 튕기고, 무거우면(`3.0~3.5`, 네모
단독) 튕기지 않고 눌러앉으며, 임계(`≥3.5`, 네모+다른 도형)를 넘으면 과부하로 구름이 서서히
사라집니다. 알파가 0이 되는 순간 지지 콜라이더가 풀려 위 도형이 낙하하고, 5초 뒤 다시 서서히
나타납니다. 트램펄린은 닿는 즉시 튕겨내므로, 무거운 네모가 눌러앉아 **머물러야** 뒤에 얹히는
도형과 합산돼 붕괴가 성립합니다 — 그래서 네모 단독은 의도적으로 눌러앉습니다(기획 §1.3 "네모는
혼자만 탑승, 구·세모는 함께"의 물리적 실현). 임계값은 인스펙터에서 조정할 수 있습니다.

구름 시각은 콜라이더가 없는 흰 puff 구 여러 개이고(순수 연출), 지지·충돌·도약은 루트의 단일
`BoxCollider` 하나만으로 판정합니다(모든 puff는 이 footprint 안, 윗면은 콜라이더 윗면에 맞춰
평평). 붕괴 페이드를 위해 머티리얼은 Transparent로 두되 **ZWrite는 켜 둡니다(`_ZWrite=1`)** —
겹쳐 놓은 puff들이 카메라를 움직일 때 앞뒤 정렬이 뒤집혀 내부 면이 깜빡이는(팝) 문제를 깊이
기록으로 막기 위함입니다(평평·비겹침 세그먼트라 ZWrite를 끄는 RainbowBridge와 다른 점).

### DreamThreadSystem — 꿈의 실타래 (Phase 1)
`DreamThreadController`(씬에 하나)가 F 입력 시 조작 중인 플레이어(`IsControlled`)를 범위 안 가장
가까운 `ThreadAnchor`에 `ConfigurableJoint` **고정 길이 진자**로 매답니다. 좌우(A/D) 입력으로 실
방향에 수직인 접선 힘을 줘(펌핑) 진폭을 키우고, 다시 F를 누르면 실을 끊어 그 순간의 접선 속도
그대로 포물선으로 날아갑니다(velocity 보존 발사). 마우스 휠로 실 길이를 조절합니다. 매달림
게이트는 질량/태그가 아니라 `PlayerShapeIdentity.Kind`로 판정해 **네모(Cube)는 거부**하고 구·세모만
허용합니다(무거운 도형은 못 매달린다는 협동 축). 캐릭터 점프(약 2 Unit)·구름 트램펄린(6 Unit)으로
못 넘는 넓은 수평 틈을 진자 운동으로 건너기 위한 기믹입니다.

교차 폴더 하드룰을 지키려 **씬 컨트롤러 하나가 전담**합니다 — PlayerSystem 파일은 한 줄도 고치지
않고 `IsControlled`/`Kind`를 읽고 `mover.enabled`를 런타임 토글할 뿐입니다. 주요 설계: (1)
`ConfigurableJoint`의 x/y/z를 같은 `linearLimit`로 둬 "앵커 중심 반지름 L의 구" 구속을 만들면
자유회전하는 구가 몸을 돌려도 길이 구속이 안 깨지고, 평면(옆모습) 구속은 조인트 축이 아니라
Rigidbody `FreezePositionX`로 줍니다. (2) 매달림 중에는 `PlayerMover`가 수평 velocity를 하드 대입해
스윙을 짓밟으므로 `mover.enabled=false`로 끄는데, 이게 `PlayerControlSwitcher` 로스터에서 플레이어를
빼 조작권이 다른 오브젝트로 튀므로(원치 않은 시점 전환·동시 입력) 진입 시 이양 대상을 스냅샷하고
`GrantControl`로 매달린 플레이어를 유일 조작자로 되붙잡습니다(진짜 Tab은 스위처 활성 타깃 변화로
감지해 자동으로 놓음). (3) 놓을 때 mover를 바로 켜면 발사 속도가 입력값으로 덮여 사라지므로
**착지할 때까지** 재활성을 미룹니다. (4) 실 길이를 줄일 때 하드 리밋이 몸을 한 번에 잡아채 뚝뚝
끊기므로, 휠은 목표 길이만 바꾸고 실제 리밋은 `reelSpeed`로 서서히 이동시켜(FixedUpdate) 임펄스를
물리 스텝에 분산해 부드럽게 만듭니다. 스프링 리밋(탄력)은 확정 스펙에서 배제해 쓰지 않습니다.

### PlayerSystem — 플레이어 조작
`PlayerMover`가 WASD 이동을 담당합니다. 접지 중 구르기는 도형에 따라 두 경로로 나뉩니다 —
정육면체/정사면체는 토크+고마찰 그립으로 실제 모서리 피벗 텀블링(`useTorqueRolling=true`), 구는
이동 velocity로부터 각속도를 하드 설정하는 legacy 방식입니다(위의 "토크+마찰 구르기 …" 섹션에
"왜"를 정리해 두었습니다). `PlayerJump`가 Space 점프를 담당하고, 접지 판정은 `PlayerGroundContact`
(실제 충돌 접촉 법선 기반)를 우선 사용하며 없으면 고정 Raycast로 대체합니다. 여러 플레이어
오브젝트가 씬에 있으면 `PlayerControlSwitcher`가 Tab 키로 조작권(`IsControlled`)을 이름 알파벳
순서로 순환시키고, 조작권을 잃은 오브젝트는 `uncontrolledDamping`으로 곧 멈춥니다. 조작권이 없는
오브젝트는 입력에 반응하지 않지만, 다른 기믹(부스트, 점프대, 중력 등)에 의한 물리 이동은 그대로
받습니다. `PlayerFollowCamera`(씬 카메라에 부착)가 현재 조작 중인 플레이어의 위치를 따라가되
구르기 회전에는 휩쓸리지 않습니다.

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

### CloudTrampolineSystem — 구름 트램펄린

#### CloudTrampoline.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `baseBounceHeight` | `6f` | 위에서 착지한 플레이어가 튀어오를 목표 높이(Unit). `PlayerJump.LaunchToHeight`로 위임해 도형 질량과 무관하게 정확히 이 높이까지 오른다. 캐릭터 자체 점프 한계(약 2 Unit)로 못 넘는 구간을 넘기려는 값이라, 넘어야 할 발판/렛지 높이에 맞춰 잡는다(선택 시 씬 뷰에 도달 높이 기즈모가 표시됨). |
| `restMassThreshold` | `3.0f` | 구름 위 합산 질량이 이 값 이상이면 튕기지 않고 눌러앉는다(구름이 버팀). 세트 B에서 네모(3.0) 단독이 눌러앉는 기준. `collapseMassThreshold`와 같게(3.5) 올리면 눌러앉기 밴드가 사라져 네모 단독도 다시 튕기고, 붕괴는 두 도형이 동시에 닿을 때만 성립한다. |
| `collapseMassThreshold` | `3.5f` | 구름 위 합산 질량이 이 값 이상이면 과부하로 붕괴한다. 세트 B에서 네모+다른 도형(≥4.0) 기준. 낮추면 더 가벼운 조합에도 무너지고, 높이면 웬만한 조합을 버틴다. `restMassThreshold`보다 커야 눌러앉기 밴드가 생긴다. |
| `reappearDelaySec` | `5f` | 붕괴로 완전히 사라진 뒤 다시 나타나기까지 숨어 있는 시간(초). 길게 하면 붕괴가 강한 페널티가 되어 타이밍 압박이 커지고, 짧게 하면 실수해도 금방 복구된다. |
| `fadeDuration` | `0.45f` | 사라짐/나타남 알파 페이드 시간(초). `0`이면 즉시 전환(팝). 페이드가 끝나 알파가 0이 되는 순간 지지 콜라이더가 풀리므로, 이 값이 곧 "무너지기 시작한 뒤 발판이 사라지기까지의 유예"이기도 하다. |
| `playerTag` | `"Player"` | 충돌/무게 집계 대상 태그. 커스텀 태그 오브젝트가 아니면 건드릴 필요가 없다. |

### DreamThreadSystem — 꿈의 실타래

#### DreamThreadController.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `maxLength` | `8f` | 실의 최대 길이(Unit). PRD 진자 수치표가 실 8을 기준으로 잡혀 있어, 넘어야 할 틈 폭에 맞춰 조정한다. 휠로 늘릴 수 있는 상한이기도 하다. |
| `minLength` | `1.5f` | 실의 최소 길이(Unit). 휠로 줄일 수 있는 하한 — 너무 작게 잡으면 앵커에 몸이 붙어 스윙이 안 나온다. |
| `wheelSensitivity` | `10f` | 마우스 휠 한 눈금(스크롤 델타 1.0 기준)당 바뀌는 **목표** 길이(Unit). 실제 델타는 보통 0.1이라 눈금당 약 1 Unit씩 조절된다. 위로 굴리면 짧아진다(끌어올림). |
| `reelSpeed` | `4f` | 실 길이가 목표 길이로 따라붙는 속도(Unit/s). 줄일 때 하드 로프 리밋을 한 번에 당기면 임펄스가 뚝뚝 끊기므로 이 속도로 서서히 이동시켜 스냅을 분산한다. 크게 하면 반응이 빠르지만 다시 끊기고, 작게 하면 더 부드럽지만 느리다. |
| `pumpAcceleration` | `6f` | 좌우 입력을 접선 방향으로 주는 가속(m/s², `ForceMode.Acceleration`이라 질량 무관 — 구·세모 동일). 매 FixedUpdate 지속 인가되므로 값이 크면 몇 프레임 만에 진폭이 과하게 커진다(플레이테스트로 12→6 완화). 스윙이 약하면 올리고 과하면 낮춘다. |
| `invertSwing` | `false` | 좌우 입력 방향이 의도와 반대로 느껴지면 켠다(옆모습 카메라 방향에 따라 다름). |
| `launchReenableTimeout` | `6f` | 놓은 뒤 이 시간(초) 안에 착지하지 못해도 강제로 `PlayerMover`를 다시 켜는 안전망(허공 낙하 방지). `PlayerShapeController`가 없어 접지 판정을 못 하는 플레이어의 폴백이기도 하다. |
| `lineWidth` | `0.06f` | 실(LineRenderer) 두께. 순수 시각값. |

#### ThreadAnchor.cs

| 필드 | 기본값 | 설명 |
|---|---|---|
| `connectRange` | `4f` | 이 앵커에 F로 연결할 수 있는 최대 거리(플레이어 Root 중심 기준, Unit). 컨트롤러는 범위 안에 든 앵커들 중 가장 가까운 것을 고른다. 크게 하면 멀리서도 걸리고, 작게 하면 앵커 바로 아래까지 가야 걸린다. 콜라이더 없는 순수 위치 마커라 이 값이 유일한 연결 판정 기준이다. |

### PlayerSystem — 플레이어 조작

#### PlayerControlSwitcher.cs

Inspector에 노출되는 필드가 없다(모든 상태가 `private`). Tab 키 순환, 등록/해제, 죽은 참조
정리(prune)를 자동으로 처리한다. 순환 순서는 GameObject 이름 오름차순(ordinal)으로 결정적이고,
활성 대상은 참조로 추적해 정렬/등록/파괴에도 같은 플레이어를 유지한다. 활성 플레이어가 바뀌면
`PlayerFollowCamera.SetActiveTarget()`으로 카메라 타깃을 갱신한다(카메라가 없으면 무시).

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

`useTorqueRolling`이 이 스크립트의 동작을 둘로 가른다. **legacy(구, `false`)** 전용 필드는
`rollRadius`, **토크 모드(정육면체/정사면체, `true`)** 전용 필드는 아래 `[useTorqueRolling 전용]`
표시가 붙은 것들이다. `inputYawOffset`은 두 경로 공통이다. 생성 메뉴가 정육면체/정사면체에는
`useTorqueRolling=true`를 자동으로 세팅한다.

| 필드 | 기본값 | 설명 |
|---|---|---|
| `moveSpeed` | `5f` | (legacy 경로) 수평 이동 속도. `AccelPad.boostSpeed`(기본 `20f`)와 비교하면 "기본 속도 대비 4배 정도가 부스트 속도"라는 감각으로 맞춰져 있다. 토크 모드에서는 대신 `maxSpeed`가 상한을 정한다. |
| `rollRadius` | `0.5f` | **legacy(구) 전용.** 접지 중 구르는 시각 회전의 반경(`각속도 = Cross(up, 이동 velocity) / rollRadius`). 오브젝트의 실제 반지름/절반 크기와 맞아야 "미끄러지지 않고 구르는" 것처럼 보인다 — 작게 두면 과하게 빨리 도는 것처럼, 크게 두면 거의 안 도는 것처럼 보인다. 토크 모드(정육면체/정사면체)에서는 사용되지 않는다. |
| `useTorqueRolling` | `false` | 켜면 velocity/각속도 하드 대입 대신 토크+고마찰 그립으로 물리적으로 굴린다(정육면체/정사면체). 끄면 legacy 방식 유지(구). 생성 메뉴가 도형별로 세팅하므로 보통 직접 만질 일은 없다. |
| `rollTorque` | `25f` | (토크 모드) 이동 방향과 직교하는 수평축으로 거는 토크 세기. 작으면 다면체가 앞모서리를 못 넘고 제자리에서 떨고, 키우면 잘 뒤집히지만 과하면 튄다. `mass`가 크거나 스케일이 크면 더 필요하다. |
| `maxSpeed` | `6f` | (토크 모드) 수평 속도 상한. 토크로 계속 가속되므로 이 값에서 클램프한다. 스케일이 커지면 한 바퀴 구르는 거리가 늘어 체감 속도가 달라지므로 함께 조정. |
| `airControlForce` | `12f` | (토크 모드) 공중에서의 약한 방향 제어 힘. 크게 하면 점프/발사 중 궤도를 더 많이 틀 수 있다. |
| `steerResponsiveness` | `6f` | (토크 모드) 원하는 구르기 축과 어긋난 각속도 성분을 초당 이 비율로 감쇠해 방향 전환을 돕는다. `0`이면 순수 토크라 한 방향으로 굴러가기 시작하면 틀기 어렵고, 높이면 방향 전환이 빠르지만 구르는 물리감이 옅어진다. |
| `turnAssist` | `0.35f` (`Range(0,1)`) | (토크 모드) 접지 중 수평 velocity의 방향만 입력 쪽으로 재정렬하는 정도(속력은 유지). `0`이면 재정렬 없음, 크면 조향이 즉각적이지만 관성감이 줄어든다. `steerResponsiveness`와 함께 조향감/물리감의 균형을 잡는다. |
| `inputYawOffset` | `90f` | **공통(구/정육면체/정사면체).** 입력 방향을 월드 up 축 기준으로 회전시키는 각도(도, 위에서 본 시계방향이 양수). 카메라 시점/월드 축과 방향키가 어긋날 때 보정한다. 앞키가 실제로 "앞"으로 가도록 `90`/`-90` 중에서 맞추며, `PlayerFollowCamera.cameraYawOffset`과 방향을 일치시킨다. |
| `scaleTorqueCompensation` | `1.5f` | (토크 모드) ScalingSystem으로 커지거나 납작해지면 관성/무게중심이 커져 고정 토크로는 뒤집기 어렵다. 현재 스케일 최댓값의 이 값 제곱만큼 `rollTorque`를 자동 증폭한다. `0`=보정 없음, `1`=크기에 선형 비례, `2`=크기제곱(관성까지 보정). 커진 상태에서도 안 뒤집히면 올리고, 폭주하면 낮춘다. |
| `uncontrolledDamping` | `15f` | (Tab 전환) 조작권을 잃은 플레이어가 관성/각속도로 계속 굴러가지 않도록 초당 감쇠하는 비율(프레임레이트 무관, 수직=중력은 유지). 크게 하면 즉시 정지에 가까워지고, 작게 하면 한동안 미끄러지듯 굴러간다. |
| `groundCheckDistance` | `0.15f` | `PlayerShapeController`가 없을 때만 사용되는 자체 접지 판정용 Raycast 거리. |
| `groundLayer` | `~0`(Everything) | 위와 동일하게 `PlayerShapeController`가 없을 때만 쓰이는 레이어 마스크. |

`IsControlled`은 Inspector에 노출되지 않는 읽기 전용 프로퍼티(기본 `true`)이며, 전환은
`PlayerControlSwitcher`가 `SetControlled()`로만 수행한다.

#### PlayerFollowCamera.cs

씬의 기존 카메라(보통 Main Camera)에 부착한다. 생성 메뉴가 카메라가 있으면 자동으로 붙여준다.

| 필드 | 기본값 | 설명 |
|---|---|---|
| `target` | `null` | 따라갈 대상. 비워두면 `PlayerControlSwitcher`가 활성 플레이어를 자동으로 넣어준다(스위처 없는 단독 씬에서만 직접 지정). Tab 전환 시 스위처가 이 값을 갱신한다. |
| `offset` | `(0, 6, -10)` | 타깃 기준 카메라 위치 오프셋(월드 공간). `cameraYawOffset`만큼 회전해 적용된다. 회전은 `cameraYawOffset`이 전담하므로 이 값은 사실상 "거리감/높이"만 정한다고 보면 된다. 타깃의 회전은 쓰지 않아 플레이어가 굴러도 시점이 휩쓸리지 않는다. |
| `cameraYawOffset` | `90f` | `offset`을 월드 up 축 기준으로 회전시키는 시점 각도(도, 위에서 본 시계방향이 양수). `PlayerMover.inputYawOffset`과 방향을 맞춰 "앞키=화면 앞"이 되게 한다. 시점이 반대로 돌면 `-90`으로 뒤집는다. 타깃 회전과 무관하게 고정 시점 각도만 바꾸므로 멀미 방지 특성은 유지된다. |
| `followSmoothness` | `0.2f` | 위치 추적 부드러움(`SmoothDamp` 시간, 초). 작을수록 즉각적으로 붙고, 크면 부드럽지만 느리게 따라온다. |
| `lookHeightOffset` | `1f` | 시선이 향하는 지점을 타깃 위치에서 이만큼 위로 올린다(발밑이 아니라 몸통을 보게). 타깃이 화면에서 너무 아래/위로 치우치면 이 값이나 `offset` 높이로 조정한다. |

## 검증 방법

Unity 에디터가 없는 환경에서 작업했기 때문에 자동 실행 검증은 하지 못했습니다. Unity
에디터에서 아래 순서로 직접 확인해주세요.

1. **씬 참조 무결성**: 기존 `Assets/Scenes/SampleScene.unity`를 열어 `Player_Root`의
   컴포넌트가 Missing Script로 깨지지 않았는지 확인 (스크립트 이동 시 GUID를 보존했으므로
   깨지지 않아야 정상입니다).
2. **플레이어 생성**: `Tools > PlayerSystem > Create Player > Sphere / Cube / Tetrahedron`로
   3종을 각각 생성.
3. **기본 조작 / 구르기**: Play 모드에서 WASD로 이동, Space로 점프. 정육면체/정사면체가 토크로
   실제 모서리를 넘어 굴러가는지(미끄러지지 않는지), 구는 legacy 방식대로 매끄럽게 굴러가는지
   확인. 방향키 앞이 실제로 "앞"으로 가는지 확인하고, 어긋나면 `PlayerMover.inputYawOffset`
   (세 도형 공통)을 `90`/`-90`으로 맞춘다. 순수 토크라 방향 전환이 굼뜨면 `steerResponsiveness`/
   `turnAssist`를, 앞모서리를 못 넘고 떨면 `rollTorque`를 조정한다.
4. **스케일 뒤집힘**: `ScalePad`로 정육면체/정사면체를 크게/납작하게 만든 뒤에도 굴러 뒤집히는지
   확인. 안 뒤집히면 `PlayerMover.scaleTorqueCompensation`을 올린다(구는 legacy라 해당 없음).
5. **Tab 전환**: 플레이어 오브젝트를 2개 이상 둔 상태로 Tab을 눌러 조작 대상이 이름 알파벳 순
   (`Cube → Sphere → Tetrahedron`)으로 순환하는지, 조작권을 잃은 오브젝트가 곧 멈추는지
   (`uncontrolledDamping`), 전환 직후 이전 오브젝트가 뒤늦게 점프하지 않는지, 씬을 다시 열어도
   순환 순서가 동일한지 확인.
6. **팔로우 카메라**: Main Camera에 `PlayerFollowCamera`가 붙어 있는지(생성 시 자동, 없으면 수동
   부착) 확인. 카메라가 활성 플레이어 위치를 부드럽게 따라가되, 도형이 굴러도 **화면이 회전하지
   않는지**(멀미 방지) 확인. Tab 전환 시 타깃이 새 플레이어로 넘어가는지, 시점 방향이 방향키와
   맞는지(`cameraYawOffset`) 확인.
7. **기믹 호환성**: 새로 만든 플레이어 오브젝트로 기존 `AccelPad`, `JumpPad`, DoorSystem의
   레버/문/발판, `ScalePad` 위를 지나가며 4개 기믹이 이전과 동일하게 동작하는지 확인.
8. **비조작 중 물리**: Tab으로 조작권을 다른 오브젝트로 넘긴 뒤, 방치된 오브젝트가 JumpPad나
   AccelPad 위에 있었다면 입력 없이도 물리적으로는 계속 튕기거나 가속되는지 확인.
