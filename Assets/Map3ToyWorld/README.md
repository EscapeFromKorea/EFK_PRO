# Map 3 ToyWorld 기능형 그레이박스

기준 문서: `.claude/plan/Map3_ToyWorld_Unity_Implementation_Brief.md`

## 실행

1. Unity `2022.3.62f3`에서 `Assets/Map3ToyWorld/Scenes/Map3_ToyWorld.unity`를 연다.
2. Play를 누른다.
3. `WASD` 이동, `Space` 점프, `Tab` 도형 전환으로 진행한다.

추가 입력:

- `F`: 꿈의 실타래 연결/해제
- `E`: SnapBlock 결합/분리
- `V`: 마찰 스티커 부착/회수
- `Q`: 마찰 스티커 종류 전환
- `R`: 마지막 체크포인트로 리스폰
- `Backspace` 1.5초 홀드: 동적 퍼즐 오브젝트 전체 리셋(수리 진행도 유지)

## 진행 루프

`Toy Box → Toy Plaza → 세 분기(순서 자유) → Broken Music Box → Exit`

- Block Fort: `WindUpSpring`
- Train Yard: `PowerGear`
- Doll House: `MelodyCylinder`
- 세 아이템을 전부 획득한 `3/3`에서만 Final Gate가 열린다.
- 최종 방에서 `Spring → Gear → Cylinder` 순서로 설치한다.
- 주황 TorquePortal 통과 후 최종 WindUpAxis를 감아야 회전 계단이 전개된다.
- 설치와 활성화가 끝난 뒤 상층의 초록 ExitTrigger에 도달해야 완료된다.

## 정상/우회 루트

- Toy Box
  - 정상: TorquePortal → WindUpAxis → 선반 리프트
  - 우회: SnapBlock/시소 끝 JumpPad로 선반 상단 통과
- Block Fort
  - 정상: 미리 안정 배치된 SnapBlock 계단을 보강·이용
  - 우회: StickerSurface 경사 또는 시소 JumpPad
- Train Yard
  - 정상: 분기 패드로 실제 레일판 정렬 → WindUpAxis로 Rigidbody 레일카 구동 → 끊어진 레일을 관성으로 통과
  - 우회: Wire 앵커, 가속+점프대, 의도적 탈선 패드 중 선택
- Doll House
  - 정상: 세 개의 FreeRotatingBoard를 임시 경사로로 이용
  - 우회: 모빌 Wire 앵커 또는 하층 JumpPad
- Broken Music Box
  - 정상: 태엽 활성화로 전개되는 6단 회전 계단
  - 우회: SnapBlock, FreeRotatingBoard+Wire, JumpPad. 단, 필수 진행 조건은 건너뛸 수 없다.

## 주요 튜닝값

- TorqueStateController: `torqueAcceleration 24`, `maxAngularSpeed 14`
- WindUpAxis: `maxTurns 1.5`, `windingAcceleration 28`, `releasePerSecond 0.12`, `outputScale 8`
- WindUpLift: `travel 5`, `progressPerPowerSecond 0.18~0.20`, `moveSpeed 4`
- SnapBlockController: `snapDistance 0.45`, `angle 15°`, `maxBlocks 14`
- ToyRailCart: `driveAcceleration 3`, `maxRailSpeed 12`, `derailDistance 2.2`
- RotatingPlate seesaw: `-30°~30°`, 자유 회전판: `-55°~55°`
- MusicBox: `activationChargeRequired 1.75`, 계단 전개 속도 `3.5`
- Respawn: `killY -12`, OutOfBounds 체류 `0.8초`
- Puzzle reset: `Backspace 1.5초`, 자동 복귀 높이 `Y -7~-8`

## 재생성/검증

- 재생성: `Tools > The Axiom > Build Map3 ToyWorld Prototype`
- 구조 검증: `Tools > The Axiom > Validate Map3 ToyWorld Prototype`
- Builder는 `Map3_ToyWorld_Root/Generated`만 다시 만들며 `Manual` 하위는 보존한다.
- 씬의 Collider 오브젝트와 `VisualMesh` 자식은 분리되어 실제 아트 교체 시 Collider를 유지할 수 있다.

현재 아트는 Unity Primitive와 단색 Standard Material만 사용한다. 외부 에셋과 신규 패키지는 없다.
