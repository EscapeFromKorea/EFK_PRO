# Map 3 ToyWorld 검증 기록 — 기존 기믹 재사용 교체 후

검증일: 2026-09-05

Unity: 2022.3.62f3

기준 커밋: `d69eadb`에서 중단된 교체 작업을 이어서 수정.

원본 Unity Editor 세션을 유지하고, 동일한 Assets/Packages/ProjectSettings 복제본에서 검증했다. 검증된 씬을 원본 프로젝트에 반영했다.

## 결과

| 검사 | 결과 |
|---|---|
| 런타임/Editor C# 컴파일 | PASS |
| 씬 생성기 재실행 | PASS |
| 필수 구역·기존 기믹 참조·Collider | PASS |
| Missing Script / 끊어진 직렬화 참조 | 0 / 0 |
| Collider와 Renderer의 동일 오브젝트 혼합 | 0 |
| 여섯 가지 수집 순서·중복 획득·설치 순서 상태 검증 | PASS |
| Play Mode 물리/진행 통합 | RUNTIME_SMOKE_PASS |
| 전체 맵·Toy Box·Doll House 렌더 배치 확인 | PASS |

## 실제 Play Mode에서 확인한 동작

- 실제 Sphere 플레이어와 3개 아이템의 Trigger 접촉 → 1/3, 2/3, 3/3; 외형 숨김·트리거 비활성화.
- 2/3에서는 doorPhysics Rigidbody 위치가 유지되고 3/3 이후 실제 문이 상승.
- 잘못된 설치 순서 거부, Spring → Gear → Cylinder 순서의 실제 소켓 접촉.
- 설치 후 초기 레버 각도 -45°만으로 활성화되지 않음.
- 실제 Sphere 충돌로 기존 LeverHead를 올바른 방향으로 밀어 +20° 이상 도달 → 오르골 활성화.
- Toy Box / Final의 기존 LiftPad 접촉·이탈 → LiftPlatform 상승·원위치 복귀.
- 기존 CloudTrampoline 왕복판 이동 및 실제 Sphere 탑승 운반.
- 기존 Portal Enable/Disable 실제 접촉 → Cube의 PlayerRollModeReceiver 상태 전환.
- SnapBlock 실제 결합 → 복구 시 기존 DetachAll 호출 → 조인트 삭제 후 위치 복귀, 양쪽 연결 정보 해제.
- 우회 JumpPad 8개 × 기존 도형 3종 = 24개 조합에서 실제 충돌 발사 확인.
- Block Fort 경사면의 기존 스티커 Slip/Velcro 마찰값 교체 및 회수 후 원본 PhysicMaterial 복구.
- 기존 RotatingPlate 다리의 물리 회전과 원위치 복구.
- 기존 체크포인트 접촉 후 RespawnController 리스폰 및 수집/활성화 진행도 보존.
- 모든 조건 충족 후 실제 Exit Trigger 접촉 → MAP 3 COMPLETE.

이 테스트는 플레이어를 각 검사 위치에 배치하고 PhysX를 실행한다. 점프 후 조향·착지, 정상/우회 경로 전체의 WASD 완주를 자동으로 증명하지 않는다.

## 이번에 수정한 문제

1. 삭제된 인터페이스를 참조하던 태엽/토크/레일카 코드와 미완성 생성기 호출 때문에 발생한 컴파일 불일치.
2. 레버 각도의 절댓값을 사용해 초기 -45°가 활성화 조건을 만족하던 오류. 양의 +20° 기준으로 수정.
3. 공중에서 시작하고 서로 겹치던 Block Fort 블록 계단. 바닥에 지지되는 높이 1~5 계단으로 배치.
4. 결합 블록 복구 직후 살아 있는 조인트가 복구를 되돌리던 오류. 기존 해제 API 호출 후 다음 프레임에 복구.
5. 보간된 Transform 대신 실제 Rigidbody 위치/회전을 복구 기준으로 저장.
6. 시소 Rigidbody의 자식에 붙은 JumpPad가 충돌 콜백을 받지 못하던 배치. 시소 옆의 독립 Collider로 배치하여 3종 도형 발사 확인.
7. Doll House 진입 통로를 막던 벽, 층 플랫폼과 회전판 끝의 교차, 가벼운 도형의 등반 한계를 넘던 일부 경사 배치 조정.
8. 다른 층까지 걸치던 체크포인트 높이를 4로 줄이고 층별 기준 높이에 배치.
9. 수직 JumpPad에 겹쳐 있던 수평 AccelPad 제거.
10. 이전 구성만 설명하던 README/검증 기록 갱신.

## 생성·수정 파일

이번 재개 작업에서 신규 프로젝트 파일은 추가하지 않았다. 아래 경로는 모두 `Assets/Map3ToyWorld/` 기준이다.

- `Scenes/Map3_ToyWorld.unity`: 기존 기믹으로 씬 재생성 및 배치 수정.
- `Editor/ToyWorldPrototypeBuilder.cs`: 기존 컴포넌트 배치·연결 및 정상/우회 지형 수정.
- `Editor/ToyWorldPrototypeValidator.cs`: 기존 기믹 참조, Collider 분리, 진행 순서 검사.
- `Editor/ToyWorldPlayModeSmokeRunner.cs`: 씬 생성부터 Play Mode 검증까지의 실행 진입점.
- `Scripts/MusicBoxRepairController.cs`: 레버 초기 각도 오작동 수정.
- `Scripts/PuzzleResettable.cs`: Rigidbody 기준 복구 및 결합 해제 타이밍 수정.
- `Scripts/ToyWorldPlayModeSmokeProbe.cs`: 실제 충돌·트리거·진행 통합 테스트 확장.
- `README.md`: 실행, 기존 시스템 대응, 정상/우회 루트와 튜닝값 갱신.
- `Validation/Map3_ToyWorld_Validation.md`: 검증 결과와 남은 범위 기록.

## 범위와 남은 확인

- 기믹 원본 파일 변경은 없다. 변경 범위는 `Assets/Map3ToyWorld/`이다.
- 삭제한 별도 기믹: TorqueStateController, TorquePortal, WindUpAxis, WindUpLift, ToyRailCart, ToyRailSwitch, ToyRailSwitchPad, ToyRailDerailPad, ToyWorldGate, ToyWorldReturnPortal와 각 meta.
- 맵 진행/리셋/HUD 코드와 기존 기믹을 배치하는 Editor 생성기·검증기만 유지한다.
- 현재 pull에 없는 태엽 축·태엽 동력·탈선 레일카·분기 레일은 구현하지 않았다. 기존 리프트·왕복판·레버로 임시 진행을 구성했다.
- 태엽 계단, 수집 후 전용 귀환 지름길은 미구현. 일반 경로로 귀환한다.
- 세 도형 각각의 전체 수동 완주, 회전판/쐐기 균형과 와이어 타이밍, 최종 아트 교체 후 충돌 정합은 수동 QA가 남아 있다.

## 재현

Unity batchmode에서 `ToyWorldPlayModeSmokeRunner.BuildAndRunFromCommandLine`을 실행한다. Play Mode 진입 후 검사기가 종료 코드를 반환하므로 `-quit`은 지정하지 않는다.

최종 통합 로그: `C:/Users/Public/Documents/ESTsoft/CreatorTemp/Map3Reuse_5cdc65f394494fb3ae4613c843672629/runtime5.log`

렌더 확인 로그: `C:/Users/Public/Documents/ESTsoft/CreatorTemp/Map3Reuse_5cdc65f394494fb3ae4613c843672629/preview.log`

변경 전 백업: `C:/Users/Public/Documents/ESTsoft/CreatorTemp/Map3Reuse_5cdc65f394494fb3ae4613c843672629/BeforeCorrection`

삭제 파일은 백업 또는 기준 커밋 `d69eadb`에서 복구할 수 있다.
