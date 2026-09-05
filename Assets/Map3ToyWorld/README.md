# Map 3 ToyWorld — 기존 기믹 재사용 로우폴리 맵

기준 문서: `.claude/plan/Map3_ToyWorld_Unity_Implementation_Brief.md`

추가 기준: 사용자가 pull한 기존 기믹을 배치·연결한다. 기믹 자체는 새로 구현하지 않는다.

2026-09-05 아트 패스: 제공된 두 레퍼런스를 바탕으로 직접 만든 로우폴리 메시·단색 재질·모듈 프리팹을 여섯 구역에 배치했다. 기존 물리/진행 로직은 유지한다. [아트 구성·생성 파일·검증 범위](Validation/Map3_ToyWorld_Art_Report.md).

## 실행

Unity `2022.3.62f3`에서 `Assets/Map3ToyWorld/Scenes/Map3_ToyWorld.unity`를 열고 Play.

- WASD 이동 / 마우스 시점 / Space 점프 / Tab 도형 전환
- F 실타래 연결·해제 / E 블록 결합·분리 / V 스티커 / Q 스티커 종류
- R 기존 체크포인트 리스폰
- F1 로우폴리 테마 HUD의 조작 도움말 표시/숨김
- Backspace 1.5초: 퍼즐 오브젝트 복구. 결합 블록은 기존 결합 해제 후 다음 프레임에 복귀한다.
- 수리 진행도는 퍼즐 리셋·리스폰에 유지되며, Play를 종료하면 초기화된다.

## 기존 시스템 대응

| 배치 기능 | 실제 동작을 담당하는 기존 코드 |
|---|---|
| 플레이어·점프·도형 전환·카메라 | PlayerSystem의 기존 생성 메뉴와 컴포넌트 |
| 블록 조립 | SnapBlock / SnapBlockController |
| 마찰 변경 | FrictionSticker / StickerSurface / FrictionStickerController |
| 시소·가구 경사판·열차 구역 회전 다리 | RotatingPlate / HingeJoint |
| 진입·최종 리프트 | LiftPlatform / LiftPad |
| 열차 구역 왕복 이동판 | CloudTrampoline의 pointA/pointB 왕복 기능 |
| 최종문·설치문·출구문 | doorPhysics |
| 오르골 활성화 조작 | LeverHead; 맵 진행 코드가 양의 20° 도달을 읽음 |
| 선택 굴리기 구간 | Portal / PlayerRollModeReceiver |
| 우회 이동 | JumpPad / AccelPad / DreamThread |
| 체크포인트·낙하 복구 | RespawnController / RespawnZone / OutOfBoundsVolume |

`Portal`은 구·네모·세모 모두에 AddTorque를 넣는 기믹이 아니다. 기존 구현대로 네모·세모의 격자 텀블을 켜고 끄며, 구에는 적용하지 않는다. 필수 리프트 경로 옆의 선택 구간으로 배치했다.

`CloudTrampoline`은 왕복 이동판으로 설정했다(`restMassThreshold=0`, `collapseMassThreshold=100`). 태엽 구동·물리 탈선 기능을 제공하는 레일카로 표현하지 않는다.

Map3 스크립트에는 아이템 수집·설치·최종 완료·HUD·퍼즐 복구 연결이 남아 있다. 기존 기믹 원본 파일은 수정하지 않았다.

## 진행

Toy Box → Toy Plaza → 세 분기(방문 순서 자유) → Broken Music Box → Exit

1. Block Fort에서 Spring, Train Yard에서 Gear, Doll House에서 Cylinder를 획득.
2. 0/3·1/3·2/3에서는 Final Gate가 닫혀 있고 3/3에서 기존 문이 올라간다.
3. 최종방에서 Spring → Gear → Cylinder 순서로 설치.
4. 금색 레버를 닫힘(-45°)에서 양의 20° 이상으로 밀면 오르골 활성화로 인정.
5. 리프트나 우회 루트로 상층 초록 Exit에 접촉하면 완료. 물리적으로 문을 넘어도 수집·설치·활성화 조건을 생략할 수 없다.

## 정상·우회 루트

| 구역 | 정상 루트 | 우회 루트 |
|---|---|---|
| Toy Box | 가벼운 도형을 금색 LiftPad에 남기고 Tab 전환 → 리프트 → 선반 | 시소 옆 보라색 JumpPad로 선반 넘기; 블록 활용 |
| Block Fort | 바닥에 지지된 높이 1~5의 SnapBlock 계단 → 성벽 | 스티커 경사로 또는 시소 옆 JumpPad |
| Train Yard | 기존 왕복 이동판 탑승 → 반대편 | 회전 다리 또는 가속+점프대; 가벼운 도형은 실타래 가능 |
| Doll House | 세 회전 가구판을 경사로로 이용 → 상층 | 층별 JumpPad로 도약하며 반대편 발판 착지; 실타래 보조 |
| Final | 설치 → 레버 밀기 → 기존 협동 리프트 | JumpPad 또는 회전판·블록·실타래 조합 |

리프트 패드는 `PlayerWeight < 2`인 도형을 인식하므로 구/세모를 남겨 두고 다른 도형으로 탑승한다. 혼자 조작하는 한 도형은 우회로를 사용할 수 있다. 실타래는 기존 무게 조건이 유지되므로 모든 도형의 공통 필수 경로로 사용하지 않는다.

## 주요 튜닝값

- LiftPlatform: 시작 리프트 `riseHeight=3.5`, 최종 `7.5`, 속도 `1.4 U/s`
- CloudTrampoline: 왕복 12초, 양 끝 `(36,0.8,18)` / `(49,0.8,18)`
- doorPhysics: 이동 속도 3, 개방 높이 5~6
- LeverHead: 최대 ±45°, Map3 활성화 기준 +20°
- SnapBlockController: 결합 거리 0.45, 각도 15°, 최대 14개
- RotatingPlate: 시소 ±30°, 가구/다리 ±55°, 시작 배치 기준
- JumpPad: 시소 옆 6, 열차 7, 인형집 8/16/16, 최종 10
- AccelPad: 속도 12, 유지 0.8초; 수직 점프대에는 가속판을 중첩하지 않음
- Respawn: killY -12, 장외 체류 0.8초
- 퍼즐 복구: Backspace 1.5초, 자동 복구 Y -7~-8

## 재생성·검증

- `Tools > The Axiom > Build Map3 ToyWorld Prototype`
- `Tools > The Axiom > Validate Map3 ToyWorld Prototype`
- 생성기는 `Map3_ToyWorld_Root/Generated`를 다시 만들고 `Manual`은 보존한다.
- 게임플레이 Collider/Rigidbody와 자식 VisualMesh를 분리했다. 아트 교체는 VisualMesh에서 진행.
- 외형은 직접 만든 베벨/다면체/기어/아치/글자 메시와 단색 Material을 사용한다. 외부 에셋/패키지/텍스처는 추가하지 않았다.
- 아트만 재적용: `Tools > The Axiom > Art > Apply ToyWorld Low Poly Art`. 현재 열린 Map3의 게임플레이 루트와 `Manual`은 유지하고 `Art_Stylized` 하위만 재생성한다.
- 아트 검사: `Tools > The Axiom > Art > Validate ToyWorld Art`.
- 자동 검증 진입점: `ToyWorldPlayModeSmokeRunner.BuildAndRunFromCommandLine` (batchmode, `-quit` 없이 실행; 검증기가 종료).
- 테스트는 실제 플레이어를 접촉 위치에 배치하여 PhysX를 돌린다. 사람의 WASD 입력으로 전 구간을 완주하는 테스트와 구분한다.
- 자세한 결과: [검증 기록](Validation/Map3_ToyWorld_Validation.md).

## 미구현·남은 확인

- 현재 pull된 코드에 없는 태엽 축·에너지 저장, 태엽 구동/탈선 레일카, 레일 분기 기믹은 미구현. 기존 리프트·왕복판·레버로 임시 진행을 구성했다.
- 태엽으로 전개되는 오르골 계단, 아이템 획득 후 전용 귀환 지름길/순간이동은 미구현. 귀환은 기존 경로를 이용한다.
- 로우폴리 환경 아트는 적용했다. 사운드, 저장/로드, 다음 맵 연결은 이번 아트 작업 범위 밖.
- 세 도형 각각의 전체 키보드 완주, 와이어 스윙 타이밍, 회전판/쐐기/스티커 조합의 난이도 튜닝은 수동 QA가 남아 있다.
