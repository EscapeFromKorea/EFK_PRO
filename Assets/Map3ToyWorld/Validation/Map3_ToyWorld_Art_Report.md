# ToyWorld 로우폴리 아트 구현 기록

작업일: 2026-09-05 / Unity 2022.3.62f3 / Built-in Render Pipeline.

## 적용 내용

사용자가 제공한 전체 맵 및 모듈 키트 레퍼런스의 색상·실루엣을 단순화해 실제 Unity 3D 메시로 제작했다. 이미지 평면을 배경으로 붙이거나 외부 에셋을 다운로드하지 않았다. 레퍼런스의 2/3 수집 표기는 사용하지 않고, 기존의 세 아이템 필수 규칙을 유지했다.

| 구역 | 적용한 외형 |
|---|---|
| Toy Box | 나무 패널·금속 모서리·장난감 아치·베벨 스냅 블록·금장 시소 |
| Toy Plaza | 석재 타일·원형 바닥 인레이·별 문양·진행 표시 받침·구역 안내 표지 |
| Block Fort | 석조 벽체·성가퀴·성탑·깃발·청록색 조립 블록·경사판 |
| Clockwork Yard | 침목·금속 레일·바퀴 달린 왕복판·가장자리 경고 표시·케이블·상자 |
| Doll House | 나무 바닥·장밋빛 벽·창틀·책장·서랍장·이불 무늬 경사판·별 모빌 |
| Broken Music Box | 금색 기어 메달·장난감 열쇠 장식·금속 문·파이프·설치 받침·출구 아치 |

장식 기어/열쇠/케이블은 시각 요소다. 새 태엽 구동이나 탈선, 레일 스위치 기능을 구현한 것이 아니다. 왕복판·리프트·레버·문은 기존 컴포넌트 그대로 동작한다.

## 파일

아래는 모두 `Assets/Map3ToyWorld/` 내부다. 이전 재사용 교체 작업의 변경분도 현재 Git 작업 트리에 남아 있으며, 이번 아트 작업에서 기존 기믹 원본을 변경하지 않았다.

- 신규 `Editor/ToyWorldArtKit.cs`: 베벨 블록, 다면체 실린더, 기어, 링, 아치, 별, 3D 글자 및 프리팹 제작.
- 신규 `Editor/ToyWorldArtDirector.cs`: 기존 Collider 크기와 구역에 맞춘 외형 교체, 장식 배치, 조명 설정.
- 신규 `Editor/ToyWorldArtValidation.cs`: 아트/재적용 검사 및 여덟 시점 중 일곱 편집기 프리뷰 캡처.
- 생성 `Art/Materials/*.mat`: 단색 팔레트 22종. Standard 셰이더, 텍스처 불필요.
- 생성 `Art/Meshes/*.asset`: 기본 메시 7종과 키트 프리팹용 병합 메시.
- 생성 `Art/Prefabs/*.prefab`: 시각 전용 프리팹 11종.
- 생성 `Art/Baked/*.asset`: 구역/오브젝트별 장식 병합 메시. 원본 기믹의 자식으로 부착.
- 생성 `Art/Lettering/*.asset`: 깊이 검사를 따르는 실제 글자 메시. 런타임 폰트 아틀라스 의존성 없음.
- 수정 `Editor/ToyWorldPrototypeBuilder.cs`: 기존 맵 생성 후 아트 패스 자동 실행.
- 수정 `Scripts/ToyWorldDebugHUD.cs`: 테마 진행 표시, 다음 목표, F1 도움말. 기존 진행 상태를 읽는다.
- 수정 `Editor/ToyWorldPlayModeSmokeRunner.cs`, `Scripts/ToyWorldPlayModeSmokeProbe.cs`: 아트 씬의 실제 카메라 캡처를 동반한 물리 테스트 진입점.
- 수정 `Scenes/Map3_ToyWorld.unity`: 검증된 아트 적용 씬.
- 생성 `Validation/ArtPreviews/*.png`: 전체·6개 구역 렌더 및 Play Mode 실제 팔로우 카메라 뷰. 마지막 카메라 캡처에는 IMGUI HUD가 포함되지 않는다.
- 수정 `README.md`, 신규 본 기록과 각 Unity meta.

프리팹: `CastleTurret`, `PortalFrame`, `ToyKey`, `ClockworkMedallion`, `Bookcase`, `DollDresser`, `ToyBanner`, `RailSleeper`, `MobileStar`, `CorePedestal`, `ToyCrate`.

## 실행·재생성

1. Unity에서 `Scenes/Map3_ToyWorld.unity`를 다시 열고 Play.
2. WASD/마우스/Space/Tab 등 기존 조작을 사용한다. F1으로 도움말을 켠다.
3. 전체 재생성: `Tools > The Axiom > Build Map3 ToyWorld Prototype` — 이름은 기존 메뉴 호환성을 위해 유지한다. 이제 아트까지 적용한다.
4. 아트만 재생성: `Tools > The Axiom > Art > Apply ToyWorld Low Poly Art`.
5. 검사: `Tools > The Axiom > Art > Validate ToyWorld Art`.

전체 생성은 `Generated`를 교체하고 `Manual`을 보존한다. 아트 재적용은 생성된 `Art_Stylized`를 교체한다. 수동 장식은 `Manual`에 둔다. 시각 프리팹에는 Collider/기믹 스크립트가 없으므로 새 플레이 공간을 만들 때는 별도의 게임플레이 루트에 Collider를 설정해야 한다.

## 주요 아트 튜닝 지점

- 팔레트: `ToyWorldArtKit.Prepare()`의 HEX 값. 청록 가동 부품, 금색 기계 장식, 보라색 점프대, 따뜻한 석재/나무가 기준이다.
- 바닥 타일/벽 패널 밀도: `TileTop`, `WallSurface`.
- 구역 소품 위치: `DressLandmarks`. 기존 통로와 착지점을 피해 벽면·상단·외곽 위주로 배치한다.
- 동적 기믹 외형: `DressSolid`, `DressMechanisms`. 기존 물리 루트의 자식이다.
- 조명/배경: `DressLighting`. 다른 맵의 프로젝트 품질 설정은 수정하지 않는다.
- 프리뷰 카메라: `ToyWorldArtValidation.Capture`. 실제 게임 카메라 설정과 별개다.

## 검증 범위

최종 결과: `art-build5.log`의 `ART_VALIDATION_PASS`, `ART_REAPPLY_PASS`, `ART_CAPTURE_PASS` 및 `art-runtime2.log`의 `RUNTIME_SMOKE_PASS` 확인. 컴파일 오류, Missing Script/Reference, 유효하지 않은 메시/재질/셰이더는 검출되지 않았다.

씬 집계: 아트 루트 142개, 키트 프리팹 11종, 활성화된 MeshRenderer 380개, 해당 메시의 삼각형 합계 211,096개. 이는 씬 집계이며 실제 프레임별 드로콜/프레임레이트 측정값은 아니다.

- 아트 적용 전후 Collider/Rigidbody/Joint 전체 직렬화 값, 위치, 회전, 스케일의 지문 일치 검사.
- 아트 하위와 프리팹에 Collider/Rigidbody/기믹 코드가 없는지 검사.
- 기존 Missing Script/직렬화 참조, 모든 시각 Mesh/Material/Shader 유효성 검사.
- 아트 연속 재적용 후 오브젝트 수 일치 검사.
- 별도 프로젝트의 Unity Play Mode에서 기존 수집·문·설치·레버·리프트·왕복판·포탈·블록 복구·24개 점프 조합·스티커·회전판·리스폰·최종 완료 테스트.
- 렌더 이미지로 글자 크기, 재질, 배치와 시야 확인.

이는 수동 키보드 완주를 대체하지 않는다. 테스트는 실제 플레이어를 접촉 위치에 옮겨 물리/진행을 검사한다. 모든 우회 경로의 조향·착지, 자유 회전 카메라의 모든 각도, HUD의 다양한 해상도, 프레임레이트 실측은 수동 QA가 남아 있다. 장식이 추가됐다고 기존에 없던 태엽/탈선/전용 귀환 기능이 생긴 것은 아니다.

## 로그·복구

검증 로그/변경 전 전체 Map3 백업: `C:/Users/Public/Documents/ESTsoft/CreatorTemp/Map3Art_1a1e685ee61944ff9577468b0fe959e5/`.

`BeforeArt/`에는 이번 아트 작업 직전의 씬과 Map3 코드가 보관돼 있다. 열린 원본 Unity 에디터는 종료하지 않았으며, 분리된 검증 프로젝트에서 생성·테스트한 에셋과 씬만 원본으로 반영한다.

검수 이미지: [전체 맵](ArtPreviews/01_WholeMap.png), [Toy Box](ArtPreviews/02_ToyBox.png), [Block Fort](ArtPreviews/03_BlockFort.png), [Train Yard](ArtPreviews/04_TrainYard.png), [Doll House](ArtPreviews/05_DollHouse.png), [Music Box](ArtPreviews/06_MusicBox.png), [Plaza](ArtPreviews/07_Plaza.png), [실제 플레이 카메라](ArtPreviews/08_PlayCamera.png).
