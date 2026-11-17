# 멀티플레이 기믹 동기화 분류표 (Gimmick Matrix)

> 작성 2026-11-17 · 상위 문서 `Multiplayer_ListenServer.md` §5.4·§11 M4 · 상태: **초안(코드 전수 확인 전)**
> 근거는 폴더별 grep 신호(Rigidbody·트리거·타이머·무작위·입력·싱글턴)와 맵 씬의 스크립트 GUID 집계다. **폴더 코드를 끝까지 읽은 것이 아니다.** 각 폴더는 변환 착수 때 코드를 읽고 이 표의 해당 행을 확정으로 바꾼다.

## 1. 전제 (확정 결정)

- **D1:** 플레이어 몸 = 소유자 권한, 기믹·퍼즐 오브젝트 = 서버 권한.
- 따라서 PRD §5.4 초안의 두 가지를 고친다.
  1. **분류 D(몸에 효과)**: "호스트가 도형 Rigidbody에 적용"은 틀린다. 몸의 물리는 오너 PC가 돌린다. → **호스트가 판정하고, 그 몸의 오너 PC에 RPC로 적용시킨다.** 오너 PC가 로컬로 판정하는 방식(예: 자기 몸이 바람 구역에 들어왔는지)이 지연상 더 나은 기믹은 행마다 적는다.
  2. **분류 B에서 B2 분리**: 몸이 위에 타거나 탑승하는 바디(레일카·이동 발판)는 오너 몸과 서버 바디가 맞물려 일반 B와 해법이 다르다(M1 실측: 서서 타는 발판 최대 오프셋 4.97m, 레일카 하차 위치 덮어쓰기).
- 로컬 전용 기능(`InputGate`·ESC·HUD·카메라)은 동기화하지 않는다(P-05).

## 2. 분류 정의

| 분류 | 방침 |
|---|---|
| **A 상태·트리거** | 호스트가 판정, 상태는 `NetworkVariable`, 연출은 각 클라 로컬. 재접속자는 `NetworkVariable` 현재값으로 복원 |
| **B1 물리 바디** | 서버 시뮬레이션, 클라는 보간만(`NetworkTransform`/`NetworkRigidbody`), 클라 복사본은 kinematic. 충돌 이벤트는 서버에서 감지 |
| **B2 몸이 타는 바디** | B1 + 오너 몸과의 접촉 보정. 기믹별 해법(권위 전환 불가: 자유 승하차) |
| **C 시간·무작위** | 호스트만 스폰·타이머·난수를 돌리고 결과(스폰·위치·상태)를 클라에 전달 |
| **D 몸에 효과** | 호스트가 판정 → 대상 몸의 오너 PC에 RPC로 적용(클라 로컬 임의 적용 금지) |
| **E 로컬 전용** | 동기화 없음 |

## 3. 분류표

맵 열은 씬 집계 기준(§4 참고). `?`는 씬 집계로 확인 불가.

### A 상태·트리거

| 폴더 | 사용 맵 | 비고 |
|---|---|---|
| DoorSystem | 1·2(구)·3·4 | 레버·발판→문. 가장 광범위, **시범 대상 1순위** |
| RainbowBridgeSystem | 1·2(구)·4 | |
| RemoteCheckpointSystem | 시험 씬만 | 맵 미배치. 재접속 복원 시범 대상(상태 작음) |
| ZoneEntrySystem | 시험 씬만 | 맵 미배치 |
| WiringPanelSystem | 4(S7) | M6 이후 |
| ServerRoomPowerSystem | 4(S7) | M6 이후 |
| RoleClueTerminalSystem | 4(S7·S8) | 역할 슬롯별 정보 — 개인 화면 분기 필요 |
| IsolationRescueSystem | 4(S2) | M6 이후 |
| ReagentMixingSystem | 4(S8) | 무작위 포함(rnd) → 호스트 난수 필요 |

### B1 물리 바디

| 폴더 | 사용 맵 | 비고 |
|---|---|---|
| LiftSystem | 2(구)·3 | |
| SeesawSystem | 2(구) | 몸이 올라타 기울임 → B2 성격 있을 수 있음, 착수 때 판정 |
| RotatingPlateSystem | 3 | 회전 발판 — 위에 타면 B2 성격 |
| WindupAxleSystem | 3·4(S1) | |
| StepRotatingBridgeSystem | 4(S5) | |
| CloudTrampolineSystem | 1 | 튕김 대상이 몸이면 D 성격 겸함 |
| CatapultSystem | 2(구)·4(S1) | 가장 큼(2013줄·입력 있음) |
| SnapBlockSystem · BlockCarrySystem · CubeDockSystem | 2(리모델 보류본)·3(24개)·4(S2 39개) (BlockCarry·CubeDock 단독 씬 집계는 SnapBlock과 따로 안 셈) | 컨트롤러가 `AfterSceneLoad` 자동 생성형(MpGame이 재보장 중). 싱글턴 있음 |
| DestructionSystem | 2(구)·4(S1) (옛 `Map1_old`에도 있었으나 현 맵 1엔 없음) | 무작위(rnd)·파편 → 호스트 난수·스폰 |
| SyncObjectSystem | 데모 씬만 | 맵 미배치 |
| ZeroGravityBubbleSystem | 1·2(리모델 보류본) | 몸에 중력 효과 → D 성격 겸함 |
| DreamThreadSystem | 1·2(구)·3·4(S3 78개) | **가장 어려움**: SpringJoint, 입력 사용, 맵 1~4 전부. 별도 회차 |

### B2 몸이 타는 바디

| 폴더 | 사용 맵 | 비고 |
|---|---|---|
| RailCartSystem | 3·4(S1 11개) | 알려진 미수정: 하차 위치 덮어쓰기. 탑승 방식은 M1에서 검증됨(§5.2.1) |
| 이동 발판 (R12 서서 타기) | 3 외 | 알려진 미수정: 서버 권한 발판 위 오너 몸이 안 실려 감 |

### C 시간·무작위

| 폴더 | 사용 맵 | 비고 |
|---|---|---|
| FallingRockSystem | 2(구)·4(S4) | 난수 사용 |
| PeriodicTrapSystem | 4(S5) | |
| ProjectileTrapSystem | 4(S3·S5) | |
| PathChaserSystem | 4(S1·S8) | |
| SecurityLaserSystem | 4(S4·S5) | |
| ManagerSurveillanceSystem | 4(S8) | |

### D 몸에 효과 (호스트 판정 → 오너 RPC)

| ScalingSystem | 1·2·3 | **A에서 D로 이동(2026-11-17 코드 확인)**: 크기가 바뀌는 건 플레이어 몸이다. 주인 PC가 토글, 크기는 몸의 NetworkTransform이 전달, 질량은 각 PC의 `PlayerShapeController`가 현재 크기로 계산 |
| RespawnSystem | 전 맵 | **M3(c)**: R·낙하 판정을 호스트로. 지금은 각 PC가 따로 판정 |
| WindZoneSystem | 1·2·3 | |
| AccelSystem | 1·2·3 | `PlayerAccelReceiver`가 조작 차단 경로(메모) |
| JumpSystem | 1·2(리모델 보류본)·3 | |
| HourglassSystem | 2(구) | 국소 슬로우(timeScale 미사용) |
| PortalSystem | 3·4(S1) | 순간이동 |
| SpacePortalSystem | 4(S6) | 렌더 카메라는 E(로컬) |

### E 로컬 전용

| 폴더 | 비고 |
|---|---|
| PauseMenuSystem · TelemetrySystem | 동기화 없음 |
| InteractionSystem | 프롬프트·입력은 로컬. 단 `E`로 일어나는 결과는 해당 기믹이 처리 |
| FrictionStickerSystem | 사용자 소유 폴더 아님. 로컬/동기화 여부는 코드 확인 후 결정(**미분류**) |

## 4. 근거와 한계

- **맵 열**은 `Map*.unity`의 스크립트 GUID를 폴더로 집계한 값이다. 프리팹 안의 스크립트는 세지 않았고, **맵 2 정식 씬(`Map2KitchenPlayable`)은 Scaling·WindZone·Accel만 집계되어 나머지는 런타임 생성 또는 다른 경로일 가능성이 있다.** 표의 "2(구)"는 옛 씬 `Map2_Kitchen.unity` 집계이며, 정식 맵 2에 있는지 **미확인**이다.
- "2(구)"는 옛 `Map2_Kitchen.unity`, "2(리모델 보류본)"은 `(hold)Map2_Kitchen_Remodel.unity` 집계다. 두 씬의 기믹 구성이 달라 어느 쪽이 정식 맵 2에 반영되는지 확인해야 한다.
- **맵 2는 아직 개편 중이라 이후 맵 PR을 받으면 기믹이 추가·교체될 수 있다.** 맵 2 열은 잠정이며, 맵 PR이 머지될 때마다 이 표를 다시 집계한다(집계 스크립트: 씬의 `m_Script` GUID를 폴더별로 센다). 새 기믹이 오면 §2 분류에 맞춰 행을 추가하고, 기존 동기화 패턴(A·D의 `NetworkVariable`/오너 RPC)으로 흡수되는지부터 본다.
- 맵 4는 섹터 로드(M6)가 선행되어야 하므로 M4 범위에서 제외한다. 맵 4 전용 행은 M6에서 다시 연다.

## 5. 변환 순서

1. **A군** — Door·RainbowBridge부터. 이때 재접속 상태 복원(`NetworkVariable` 현재값) 패턴을 확립한다. 코드 전수 확인은 폴더별로 변환할 때 함께 한다.
2. **D군 중 Respawn·WindZone·Accel·Jump** — 맵 1 완주에 필요. M3(c) 포함.
3. **B1 쉬운 것부터** — Lift, Seesaw, CloudTrampoline, RotatingPlate, WindupAxle, Catapult, Destruction, ZeroGravityBubble.
4. **SnapBlock·BlockCarry·CubeDock** — 맵 3 필수.
5. **DreamThread** — 별도 회차.
6. **B2** — RailCart, 이동 발판(알려진 미수정 2건).
7. **C군**, 이후 Portal·Hourglass 등 남은 D군.

각 단계의 완료 기준은 "기믹별 2PC 실측 통과"(§11 M4)다.

## 6. 폴더별 확정 체크리스트 (변환 때 채움)

폴더마다 변환 착수 시 아래를 확인하고 표의 행을 확정으로 갱신한다.

1. 분류가 맞는가 (몸을 건드리는가, 타이머·난수가 있는가)
2. 싱글턴·`FindObjectOfType` 가정이 있는가 → 로컬/호스트/동기화 표기
3. `Input` 직접 읽기가 있는가 → 오너만 읽도록
4. 충돌·트리거 이벤트를 서버에서 감지하도록 바꿀 수 있는가
5. 재접속자에게 필요한 현재 상태는 무엇인가
6. 정식 맵에 실제로 쓰이는가(위 집계의 미확인 해소)

## 7. 공통 동기화 틀 (2026-11-17 확정)

- **씬 배치 기믹은 NetworkObject를 달지 않는다.** 맵 씬은 팀원 파일이고 NGO 씬 동기화를 끈 상태라(M3 결정과 동일한 이유), 공용 `MpSync`(`Assets/MultiplayerSystem/MpSync.cs`) + `MpLobby.SyncVals`(NetworkList)로 동기화한다.
  - 기믹은 `Awake`에서 `MpSync.Register(this)`(ID = 씬 이름+계층 경로+타입 해시)로 등록, 서버는 `MpSync.Publish(id, 채널, 값)`, 참가자는 `IMpSynced.OnMpValue`로 받는다. 늦게 합류·재접속한 PC는 `Register` 때 현재값을 받는다.
  - `MpSync.IsClient`(참가자 PC)면 기믹은 로직을 돌리지 않고 받은 값만 적용한다. 오프라인이면 기존 동작 그대로.
- **몸이 부딪히는 장치(레버 등)**: 서버는 참가자 몸의 복사본(키네마틱)과 충돌 이벤트가 안 나므로 **몸 주인 PC가 감지해 `MpSync.SendEvent`(RPC)로 서버에 보고**하고 서버가 상태를 확정한다.
- **트리거(밟는 판 등)**: 서버가 복사본 몸·운반 물체와의 트리거 겹침으로 판정한다(키네마틱도 트리거 이벤트는 발생).

## 8. 폴더별 확정 기록

### DoorSystem (A군, 2026-11-17 코드 확인·구현, 컴파일 확인, **2PC 실측 전**)
- 5개 스크립트: `doorPhysics`(문 이동), `LeverHead`(레버), `PadTrigger`(발판), `ExitWeightPlate`(무게판), `TeamExitZone`(좌표 질의).
- 문: 서버가 로직 실행, 위치를 진행도(0~1)로 올림. 참가자 PC는 진행도를 따라 이동하고 몸과는 로컬 충돌.
- 발판·무게판: 서버 판정. 발판은 눌림 여부, 무게판은 열림 여부를 올리고 전원이 연출/벽 제거에 반영.
- 레버: 서버가 각도를 올림(채널 0), 주인 PC가 민 방향을 이벤트로 보고(초당 10회 이내).
- `TeamExitZone`: 변경 없음. 호출자(`PathChaserController`, C군)가 서버에서만 호출하면 복사본 몸 위치로 판정된다.
- 미확인: 무게판의 `PlayerWeight.Of`가 몸 복사본에서도 같은 값을 주는가(무중력 버블 안 몸), `P04_Door`/`P04_LeverHead` 이름 분기 동작, 격리구출 쪽 `IsolationEntryDoor`의 `IsBlocked` 사용(A군 IsolationRescue에서 확인).

### D군 재분류 (2026-11-17 코드 확인): D1 "주인 PC 로컬 적용" / D2 "서버 판정"
몸에 닿는 효과는 몸이 주인 PC에서 진짜 물리로 도는 점(D1 확정)을 이용하면, **상태 없는 효과는 주인 PC가 자기 몸에 로컬로 적용**하면 된다(지연 최소, 코드 변경 최소). 남의 몸 복사본에는 효과를 걸지 않는다(복사본은 키네마틱·수신기 비활성이라 자연히 무시되거나, 명시적으로 `MpSync.IsOwnBody`로 거른다).

| 분류 | 기믹 | 처리 |
|---|---|---|
| **D1 주인 로컬** | WindZone, AccelSystem, JumpSystem | **코드 변경 없음** — 복사본에서는 수신기가 꺼져 있거나 충돌 이벤트가 안 난다. 2PC 실측만 필요 |
| **D1 + 복사본 무시** | ScalingSystem(`ScalePad`) | 복사본 트리거를 `MpSync.IsOwnBody`로 무시(이중 토글 방지) |
| **D2 서버 상태 + 주인 실행** | RespawnSystem | 체크포인트(저장·깃발·진행순서)는 서버만 확정해 `RespawnZone` 깃발 상태로 전파, 낙하·R 복귀는 **주인 PC가 자기 몸만** 판정·실행(§8) |
| 미착수 | Hourglass(국소 슬로우 상태), Portal·SpacePortal(순간이동·렌더 카메라) | 폴더별 코드 확인 후 |

### 맵 4 전용 A군(WiringPanel·ServerRoomPower·RoleClueTerminal·IsolationRescue·ReagentMixing) — M6로 이월
코드 4,200줄이고 역할 슬롯별 개인 화면·UI 상호작용이 얽혀 있으며, 맵 4는 섹터 로드(M6) 전에는 시험할 수 없다. M6에서 섹터 로드와 함께 변환한다.

### RespawnSystem (D2, 2026-11-17 구현, 컴파일 확인, **2PC 실측 전**)
- **결정(PRD P-07 변경 — 2026-11-17 사용자 승인, PRD 반영 완료):** PRD는 "복귀를 호스트가 판정"이라 적었으나, D1(몸은 주인 권한)과 지연을 고려해 **복귀 판정·실행은 몸 주인 PC가 자기 몸만** 하도록 구현했다. 서버는 체크포인트 상태만 가진다. 남의 몸 낙하 때 내 화면에 페이드가 뜨는 문제도 이 방식이면 생기지 않는다(자기 몸만 처리).
- `RespawnZone`: 서버만 저장(참가자 PC 트리거 무시), 깃발 상태(Hidden/Visited/Active)를 `MpSync` 채널 0으로 전파. `RespawnZone.Active`(깃발이 Active인 구역)가 현재 체크포인트.
- `RespawnController`: 참가자 PC는 `StoreCheckpoint`·`RestartCheckpoint`를 무시하고 `RespawnZone.Active`에서 복귀 지점을 계산. 낙하·장외 감시 대상 `players`를 내 몸으로 제한. 순간이동 직후 `NetworkTransform.Teleport`로 다른 PC에서 몸이 미끄러지지 않게 함.
- **`RemoteCheckpointGate`·`ZoneEntryDetector`**: 서버에서만 진입을 판정하므로(참가자 PC 무시) 서버의 `TryStoreCheckpoint`가 곧 팀 체크포인트 저장이 된다. `RemoteCheckpointGate.SetUnlocked`는 CH2 성공 신호를 서버가 부를 때만 의미가 있다(맵 4 이월).
- 미처리: `SectionSafePoint`(구간 복귀 안전점 예약)·`SectionHitCounter`(낙석 피격 집계)는 여러 PC가 한 안전점을 예약하는 문제라 서버 중재가 필요 — FallingRock(C군)과 함께. `RespawnCount`(팀 누적 디버그 표시)는 PC별 값. `ZoneEntryDetector`의 "복귀 중 진입 무시"는 남의 복귀 이벤트를 서버가 못 받아 오탐 경고가 날 수 있음(경고만).

### B1 물리 바디 공통 틀 (2026-11-17)
- **자세 전파:** `MpBodySync`(`Assets/MultiplayerSystem/MpBodySync.cs`)를 기믹이 `Awake`에서 `MpBodySync.Ensure(this)`로 붙인다(멀티 판에서만). 서버는 매 물리 스텝 자세를 `MpLobby.PoseVals`(NetworkList, 0.2cm·0.2도 미만 변화는 생략)로 올리고, 참가자 PC는 그 바디를 **키네마틱으로 바꿔** `MovePosition`/`MoveRotation`으로 따라간다. 기믹 자체 로직(구동·토크·판정)은 `MpSync.IsClient`로 건너뛴다.
- **라이더 운반:** 서버 PC는 기믹이 하던 `r.position += delta`를 몸 주인이 호스트인 몸에만 적용(`MpSync.IsOwnBody`). 참가자 PC는 `MpBodySync`가 접촉으로 올라탄 내 몸을 같은 방식으로 운반한다. **회전하는 바디(시소·회전판·회전 다리)는 마찰에 맡긴다**(알려진 한계 — 2PC 실측 대상, B2와 같은 계열).
- **서버는 참가자 몸 복사본과 충돌 이벤트가 안 난다**(키네마틱끼리). 이 경우 몸 주인 PC가 이벤트로 보고한다(`MpSync.SendEvent`, 레버·구름). 트리거는 키네마틱도 발생하므로 서버가 직접 판정한다(발판·리프트 패드·회전 다리 감지).
- **남의 몸 복귀:** 서버가 낙하 등을 판정해 `RespawnController.RespawnPlayer(root)`를 부르면 주인 PC로 RPC를 보내 주인이 복귀시킨다(`MpSync.RequestRespawnAtOwner`). 구간 복귀(`SectionSafePoint` 목적지) 변형은 아직 전달하지 않는다(FallingRock·SectionSafePoint 변환 때).

### B1 폴더별 기록 (구현·컴파일 확인, **2PC 실측 전**)
| 폴더 | 처리 |
|---|---|
| LiftSystem | 플랫폼: 서버 구동+자세 전파, 패드: 서버 판정+눌림 연출 동기화 |
| SeesawSystem · RotatingPlateSystem | 서버만 하중 토크·회전 물리, 참가자 PC는 자세만 따라감. **미확인:** 서버에서 몸 복사본(키네마틱)이 판을 누르는 힘은 `SeesawWeightTilt`의 보조 토크(`OnCollisionStay`)로만 반영되고 순수 PhysX 하중은 없다 — 기울기 체감이 싱글과 다를 수 있음 |
| StepRotatingBridge | 서버가 상태 기계(예고→회전→유지→복귀)를 돌림, 자세 전파. 낙하 이벤트는 서버에서 발생해 `RespawnPlayer`가 주인에게 라우팅(구간 복귀 변형 제외) |
| CloudTrampoline | 붕괴 상태(0)·하중(1)은 서버 확정. 튕김은 주인 PC가 로컬에서 즉시(마지막으로 받은 하중 + 내 무게). 올라탐/내려옴을 이벤트로 서버에 보고, 서버가 합산해 붕괴 판정. 왕복 이동은 서버 구동+자세 전파. **미확인:** 연결 끊김 시 남은 라이더 무게 정리 없음 |
| WindupAxle | 충전·방전·수신기 출력은 서버만. 감기 입력(휠·패들)은 주인 PC가 감지(패들은 굴리기 모드 텀블 상태가 주인 PC에만 있음)해 이벤트로 서버에 전달, 서버는 충전량·손잡이 목표각·감은 횟수/방향을 올려 전원이 발광·손잡이·`WindupReleaseTimer` 카운트다운을 같이 봄. `RotatingPlatform`은 서버 구동+자세 전파(회전 바디라 위의 몸은 마찰 운반) |
| 미착수 | Catapult, SnapBlock·BlockCarry·CubeDock, Destruction, SyncObject, ZeroGravityBubble, DreamThread(별도 회차) |

### 코드 검토 결과 (2026-11-17, A·D·B1 변환분)
- **수정함:** `PlayerShapeController.Update`가 남의 몸 복사본의 크기를 초기 크기로 매 프레임 되돌려 NetworkTransform 크기 동기화와 싸우던 문제 → 복사본은 크기 보간을 건너뜀. (이게 없으면 다른 PC에서 커진/작아진 몸이 안 보이고 복사본 질량도 안 변한다.)
- **실측으로 먼저 확인할 가정(틀리면 여러 기믹이 같이 안 됨):** (1) 서버에서 참가자 몸 복사본(키네마틱, `NetworkRigidbody`가 비권한 쪽을 키네마틱으로 만든다는 전제)이 **트리거 이벤트를 발생시키는가** — 발판·무게판·무지개다리·ZoneEntry·RespawnZone·리프트 패드·회전 다리·활성 발판이 전부 이 전제에 의존. (2) `NetworkTransform.Teleport`가 주인 권한 `NetProtoTransform`에서 의도대로 보간을 끊는가. (3) 예고 없이 NetworkList를 50Hz로 갱신하는 부하(움직이는 바디가 늘면 대역폭).
- **알려진 한계(미수정):** 레버 — 주인 PC 접속이 끊겨 "떨어짐" 이벤트가 안 오면 레버가 밀린 채로 남음. 구름 — 끊긴 사람의 라이더 무게가 남음. 회전 다리 — 예고 진동은 서버에서만 보임(상태 동기화 안 함). 회전하는 바디 위 참가자 몸은 마찰 운반만.

### 1차 3창 실측 (2026-11-17, 맵 1, Loki `mp_*` 로그)
- **가정 1 확인(부분):** 모든 몸 복사본이 `kin=True`. 호스트에서 참가자 몸 복사본이 `RespawnZone`(P01~P12), `PadTrigger`(P04_HoldPad), `doorPhysics` 끼임 트리거(P06_TeamLiftGate), `ExitWeightPlate`(P08), `RainbowBridgeSwitch`를 **발생시킴** → 키네마틱 복사본의 트리거 전제 성립. 아직 안 밟힌 것: ZoneEntry·StepRotatingBridge·WindupActivationPad(맵 1에 없음). **LiftPad는 이후 맵 3 시험에서 밟힘**(호스트가 참가자 몸 복사본의 트리거를 받음).
- **가정 2 확인:** 복귀 13건 전부 관찰자 PC에서 **한 프레임에 거리 전체(d=이동거리)가 점프**, 중간 프레임 없음(미끄러짐 없음), 지연 약 60~120ms. 클라이언트 복귀 지점이 P01→P12로 진행 → 체크포인트 서버 저장·깃발 동기화도 동작.
- **가정 3 미결:** 맵 1엔 `MpBodySync` 바디가 없어 `poses=0`. 값 채널은 키 39개, 서버 갱신 평균 2.5/s·최대 23/s, 참가자 수신 최대 24/s로 부하 미미. 자세 전파 부하는 맵 2·3(리프트·시소·회전판·태엽)에서 재측정 필요.
- **미발생:** `mp_lever`·`mp_cloud` 이벤트 0건 — 레버·구름 이벤트 경로는 아직 실측 못 함.
- 분류 못 한 것: 비복귀 위치 점프 224건(0.5m 이상 한 프레임 이동, 5m 이상 66건) — 낙하·발사 등 정상 이동일 수 있으나 시각으로 확인 안 함.
