# 무지개 다리 (Rainbow Bridge) — PRD

> 맵1 DayDream 협동 기믹. 출처: `맵1_협동기믹_구현사양.md` §2.
> 이 문서는 기획 의도와 "왜"를 담는다. 파일 경로·클래스 구조 등 코드에서 유도 가능한 내용은
> 최소화하고, 구현 시 판단 근거가 되는 결정 사항과 남은 TBD를 명시한다.
> 상태: **설계 확정, 구현 대기.**

## 1. 기획 의도 / 목적

평소에는 투명해서 존재하지 않는 것처럼 보이는 다리가, 누군가 스위치(발판)를 밟는 동안에만
실체화되어 건널 수 있게 되는 "불안정한 다리" 기믹. 협동에서는 한 명이 스위치를 지키고 다른
한 명이 건너는 역할 분담을, 솔플에서는 "밟고 → 빠르게 건너기"의 시간 압박을 만든다.

핵심 재미는 **누군가는 남고 누군가는 건넌다**는 협동 구조, 그리고 솔플에서 구의 빠른 이동속도로
제한 시간 안에 통과하는 긴박함이다.

## 2. 동작 시나리오

### 협동 (기본 / 정본)
- 한 도형이 활성화 발판 위에 있는 **동안에만** 무지개 다리가 실체화된다(밟는 동안 유지, `held`).
- 발판에서 벗어나면 즉시 다리가 사라진다.
- **Tab 단일조작 전제**: 한 도형을 발판 위에 세워둔 채 Tab으로 다른 도형에 조작권을 넘겨
  건너간다. 정지한 도형이 계속 발판을 누르고 있으므로 "한 명이 밟고 있는 동안"이 자연히 성립한다.
  - (확장 여지) 추후 네트워크 동시조작으로 가더라도, "발판이 눌려 있는 동안 대상이 켜진다"는
    규칙 자체는 그대로 유효하다. 지금은 네트워크 코드를 넣지 않는다.

### 솔플
- 발판을 밟으면 타이머가 시작되고, `activeDurationSec`(기본 3초) 동안만 다리가 유지된 뒤
  자동으로 사라진다.
- 이 시간 안에 구로 전환해 빠르게 건너는 것이 의도된 공략.

## 3. 컴포넌트 / 스크립트 설계

### 발판 스위치 (신규, `RainbowBridgeSystem/` 루트)
- 트리거 발판. 플레이어 감지 시 대상 오브젝트들의 실체화를 토글한다.
- **활성화 = collider + renderer 즉시 토글**(페이드 없음). 켜짐: 둘 다 enabled=true / 꺼짐: false.
- 대상은 **배열로 노출**하여 발판 1개가 다리 여러 세그먼트를 동시에 제어할 수 있게 한다.
  인스펙터에서 대상 오브젝트를 자유롭게 교체·추가 가능(사양 2.5 [확정: public 노출]).
- 모드 분기: `activatorRequiresHold`(true=협동, 밟는 동안 유지 / false=솔플, 타이머).
- 시각 보조로 발판↔대상 연결선을 씬 뷰에 그린다(`OnDrawGizmos`).

### 재사용할 기존 자산 / 패턴
- **overlapCount 트리거 패턴** (`DoorSystem/PadTrigger.cs`에서 관찰): 플레이어가 트리거
  콜라이더(Player_Mesh)와 솔리드 콜라이더(Player_Collider)를 함께 가져 Enter/Exit가 중복
  호출되는 문제를, 겹침 개수 카운터로 해결한다. **DoorSystem 파일은 수정하지 않고 패턴만 이식**한다.
- 플레이어 판별은 `CompareTag("Player")`로 충분하다(도형 무관 기믹). 솔플에서 구로 빠르게
  건너는 것은 PlayerSystem의 도형별 이동속도가 이미 처리하므로 이 기믹은 도형을 구분하지 않는다.

## 4. Tools 메뉴 세팅

- `Assets/RainbowBridgeSystem/Editor/RainbowBridgeMenuItem.cs`, `[MenuItem("Tools/RainbowBridge/...")]`.
- 기존 표준 패턴(`ScalingSystem/Editor/Shapegimmicksetup.cs`, `AccelSystem/Editor/AccelPadMenuItem.cs`)을 따른다:
  - SceneView 중앙(`lastActiveSceneView.pivot`)에 생성, `Undo.RegisterCreatedObjectUndo`, `Selection.activeGameObject`.
  - 발판 1개 + 무지개 다리 판 여러 개(초기 collider/renderer 비활성)를 함께 생성하고,
    발판의 대상 배열에 다리들을 자동 연결한 상태로 배치한다.
  - 머티리얼이 필요하면 `RainbowBridgeSystem/Materials/`에 에셋으로 저장하고, 렌더 파이프라인
    대응은 `Shapegimmicksetup.cs`의 `ResolveShader()` 방식을 재사용한다.

## 5. 기존 시스템 연동 지점

- **PlayerSystem**: 트리거 감지만 사용(태그 기반). PlayerSystem 파일 수정 불필요.
- **PlayerControlSwitcher(Tab 조작권)**: 협동의 "한 명이 밟고 있는 동안" 시나리오가 Tab
  전환에 의존한다. 이 기믹이 Switcher를 직접 참조하진 않지만, 설계 전제로 삼는다.
- 교차 폴더 수정 없음 → `Assets/CLAUDE.md` 하드 룰(교차 폴더 수정 허가) 해당 없음.

## 6. 확정값 / 기본값

| 항목 | 값 | 상태 |
|---|---|---|
| 활성화 방식 | collider + renderer 즉시 토글(페이드 없음) | 확정 |
| 대상 지정 | 배열, 인스펙터 교체/추가 가능 | 확정 |
| 협동 유지 방식 | 발판 눌린 동안 유지(`activatorRequiresHold=true`) | 확정 |
| 솔플 지속 시간 | `activeDurationSec = 3초`(인스펙터 조정 가능) | 확정(기본값) |
| 협동 모델 | Tab 단일조작 기준, 네트워크 확장 여지만 열어둠 | 확정 |

## 7. 남은 TBD

- 없음(현 범위 기준). 무지개 머티리얼/비주얼은 아트 영역이며 백엔드 동작에 영향 없음.
- (확장) 네트워크 동시조작 전환 시 발판 점유 동기화 정책 — 현 범위 밖.
