#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 섹터1 "01 / BREACH &amp; TRANSIT(추격)" 빌더 — 설계/S1_설계.md(§9 구현 결정 + §11 추격 구간 재설계, 09-28 사용자
/// 확정)를 섹터 씬 Generated/ 아래에 짓는다. Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry를 거쳐 S1일 때
/// 빈 틀 대신 이것을 부르고, 연결 통로·마커는 공용 코드(Map4SceneBuilder.BuildConnectorAndMarkers)가 이어서 만든다.
///
/// [§11 재설계] 옛 "추격 홀 + 격벽 3개"(z 10~54)를 "입구(폭 16) → 곧은 복도(z 10~100, 폭 10, 천장 4) + 쓰러진
/// 선반·책상·의자 장애물 무리 7곳 + 노란 비상등(S1_Lighting)"으로 바꾸고, 섹터 길이를 132 → 188(+56)로 늘렸다.
/// 투석기 구역 이후(투석기·칸막이·파괴벽·승차장·절벽·도착 플랫폼)는 §9 값에 +56만 더했다(§11-1 표와 일치).
///
/// [팀 기능은 팀 생성 코드로 만든다 — 팀 파일 수정 0] 팀 Ch1TestRoomMenuItem(Lab_CH1과 같은 구성을 방
/// 하나에 조립하는 팀 코드)과 같은 경로를 쓴다: 투석기 SlingCatapultMenuItem.BuildSlingCatapult, 카트
/// RailCartMenuItem.CreateRailCartAt, 추격자 일체 PathChaserMenuItem.BuildRig, 체크포인트
/// RespawnMenuItem.CreateCheckpoint, 그리고 생성 함수가 private인 파괴벽·태엽 축·발판·포탈은 팀과 똑같이
/// 메뉴 경로(EditorApplication.ExecuteMenuItem)로 실행한다. 배치 좌표만 이 설계서 값으로 정하고,
/// 배선 방식(카트 HoldPad·latch, 레일 직선화, arrivalPoint·TeamExitZone, 벽 대기 인덱스)은 팀
/// Ch1LevelLayoutMenuItem.LayoutCore/Ch1TestRoomMenuItem.Build와 같게 한다.
///
/// [팀 방식과 다른 점 — 모두 씬 분리 때문]
/// - RespawnController는 Master 씬에 있다. BuildRig는 씬에서 그것을 찾아 영구 배선하므로, 빌드 중에만
///   임시 RespawnController를 두었다가 그 영구 배선을 걷어내고 지운다. 실제 연결은 실행 중
///   Lab_SectionRespawnBridge가 한다.
/// - PathChaserController는 꺼 둔 채 저장하고 Lab_ActivateWhenPlayersReady가 도형 배치 뒤 켠다(섹터
///   비동기 로드 중 시작 지연 2초가 먼저 흘러가지 않게). 같은 이유로 에이전트 컴포넌트도 꺼 둔다(팀
///   컨트롤러 Start가 원래 하는 일을 저장 단계로 당긴 것).
///
/// [기믹은 팀 기본 동작 그대로 — 사용자 09-28 확정 "기믹은 만들거나 고치지 않는다"] 추락 연출·절벽 개인
/// 복귀·1인 통과 조건처럼 팀 기믹 동작을 바꾸는 어댑터는 두지 않는다(요구와 다른 점은 사용자에게 보고).
/// 절벽 낙하는 팀 Lab_CH1과 같이 팀 OutOfBoundsVolume(→ 공용 체크포인트 페이드 복귀)으로, 출구 문은 팀
/// doorPhysics를 팀 OnChapterCleared에 연결만 한다(같은 씬 안이라 영구 배선 가능).
/// 복도 장애물은 팀 기믹이 아니라 우리 맵 지형 배치다(§11-3).
///
/// [10-04 사용자 결정 — 카트 2대 + 도착 쪽 발판을 밟는 동안 열리는 무지개다리(협동), 설계/S1_설계.md §12] 09-28 회의 결정 6(박진수 의견)을
/// 따라 레일카를 3대 → 2대로 줄이고(레일 x −6/0, PathChaserController.carts 2개), 남는 한 도형은 팀 Lab_CH1과 같은 구성의 팀
/// 무지개다리(RainbowBridgeSwitch 협동 모드, 발판은 도착 플랫폼 쪽)로 건넌다. 팀 부품은 배치·인스펙터 값·targetObjects 배선만 한다
/// (새 스크립트·어댑터 없음). 다리는 팀 생성 함수 RainbowBridgeMenuItem.CreateSetup(public static)으로 만든 뒤 위치·조각 폭만 맞춘다.
/// 앞의 "무지개다리는 우회로라 배치하지 않는다(09-28)"는 이 결정으로 바뀌었다.
///
/// [10-04 보정 M-A·L3 — 사용자 결정, 진행/기획보완_2026-10-04/S1_보정안_10-04.md] ① 도착점을 두 레일 가운데 (−3, 176)으로(반경 7 유지) — 두 카트
/// 임계가 같아진다(z ≥ 169.68). ② 태엽 축·발판·포탈 묶음을 오른쪽 (12, 133)으로 — 다리 머리로 곧장 가는 경로가 추격자 종점 옆을 지나 잡히던 것을
/// 없앤다(생성 시 CheckApproachClearance가 새 위치 기준으로 점검). 두 보정 모두 팀 부품의 배치·인스펙터 값만 바꾼다.
///
/// [10-04 사용자 결정 — 다리 한 판] 14조각 다리는 네모·세모가 조각 이음매에서 걸렸다(검사 FUNC_20261004_082156~100428: 네모 z 139.5(이음매 140)·가장 느린 구간 z 149.5(이음매 150),
/// 세모 z 143.36(이음매 144 직전)에서 멈춤, 구는 정상). 그래서 다리를 한 판(28×4×0.2, 이음매는 승차장↔다리·다리↔도착 플랫폼 2곳뿐)으로 만든다 — BuildRainbowBridge.
/// [10-04 도착 구역 밑면] 세모는 바닥에 서면 피벗(transform.position)이 local y −0.21이라, 팀 TeamExitZone.cs:46-50(ClosestPoint(transform.position)==position)은 구역 y 0~4 밖으로
/// 읽어 "전원 도착"이 안 설 수 있다 — BuildExitZone의 구역을 y −1~4로 내렸다(팀이 b15c8f9에서 CH8 시작 구역 밑면을 바닥 −1로 내린 것과 같은 원인).
///
/// [이름 규칙 — 지시서 C4, INF3 감사가 접두사로 판정] 장애물 콜라이더 GEO_S1_Obst_&lt;무리&gt;_&lt;Shelf|Desk|Chair&gt;[_a|_b|_c],
/// 정적 벽 GEO_S1_Wall_…, 천장 GEO_S1_Ceil_…, 바닥 GEO_S1_Floor_…, 시각물은 콜라이더 없는 자식 VIS_….
///
/// 좌표는 섹터 로컬(+Z 진행, 바닥 윗면 y=0). 출처 표기: [확정]=설계서 사용자 확정, [제안]=설계서 제안값,
/// [계산]=계산값, [구현 결정]=설계서에 값이 없어 여기서 정한 것(보고서 목록), [추정]=실측 필요.
/// </summary>
public static class S1_Builder
{
    private const string UndoName = "Build Map4 S1";

    // ── 섹터 전체 (§11-1) ──
    private const float SectorWidth = 60f;          // [제안 §11-1] 폭 60 유지(설계서 §3·§11 제안값 — 사용자 확정은 §11 요소 5가지뿐)
    private const float SectorLength = 188f;        // [제안 §11-1] 132 → 188(+56)(HANDOFF 2-10 "132→188 제안")
    private const float HalfW = SectorWidth / 2f;
    private const float OuterWallT = 0.5f;
    // [§9 구현 결정] 외벽 높이 14 — 투석기 최대 궤적 정점 약 11.2m(설계서 §3 계산)보다 높게. 투석기 구역 이후는 천장을
    // 두지 않는다(설계서 "천장 ≥ 14m"를 천장 없음으로 충족).
    private const float OuterWallH = 14f;

    // ── 입구·스폰 + 복도 (§11-1·§11-2) ──
    private const float EntranceHalfW = 8f;         // [제안 §11-1] 입구 폭 16(x −8~8), z 0~10
    private const float EntranceZ1 = 10f;           // [제안 §11-1] 입구 끝 = 복도 시작
    private const float CorridorHalfW = 5f;         // [제안 §11-2] 복도 폭 10(x −5~5)
    private const float CorridorZ1 = 100f;          // [제안 §11-1] 복도 끝 — 투석기 구역(폭 60)으로 열림
    private const float CorridorH = 4f;             // [제안 §11-2] 천장 높이 4(천장 아랫면 y) — 도형 점프 최고 2.0(세모)보다 높고 지름길 차단
    // [구현 결정] 입구·복도 벽 두께 0.5(외벽과 같게), 천장 판 두께 0.3(바닥 판과 같게) — 설계서에 값 없음.
    private const float InnerWallT = 0.5f;
    private const float CeilT = 0.3f;

    // ── 투석기 구역 ~ 도착 (§9 값 + 56 = §11-1 표) ──
    // [§9 구현 결정] 설계서 §3의 "투석기 z 56 / 벽 중심까지 20m / 칸막이 z 72"가 동시에 성립하지 않아(56+20≠72)
    // 칸막이·파괴벽을 옛 z 72~73, 투석기를 옛 z 53(벽 중심까지 19.5m)에 뒀다. §11-1에서 +56 → 투석기 109, 칸막이 128~129.
    private const float CatapultZ = 109f;           // [§11-1] 옛 53 + 56
    private const float PartitionZ0 = 128f;         // [§11-1] 옛 72 + 56
    private const float PartitionZ1 = 129f;         // [§11-1] 옛 73 + 56
    private const float BreakWallHalfW = 3f;        // [§9] 파괴벽 폭 6
    private const float BreakWallH = 12f;           // [§9] 파괴벽·칸막이 높이 12

    private const float CartStartZ = 132f;          // [§11-1] 카트 z 132 = 옛 76 + 56
    // [10-04 사용자 결정] 카트 2대 — 레일 x −6/0(옛 3대 −6/0/6에서 x +6 레인을 다리에 내줌). 카트 간격 6(설계서 §6) 유지.
    private static readonly float[] RailX = { -6f, 0f };
    // [§9 구현 결정] 추격자가 승차장·절벽 구간에서 다니는 x — 가운데 레일(0)과 왼쪽 레일(−6) 사이. 종점과 벽 구멍 통과점이 같이 쓴다.
    private const float ChaserLaneX = -3f;
    private const float EdgeZ = 140f;               // [§11-1] 절벽 시작 = 옛 84 + 56
    private const float PlatformNearZ = 168f;       // [§11-1] 절벽 끝 = 도착 플랫폼 시작(절벽 28m) = 옛 112 + 56
    private const float PlatformHalfW = 15f;        // [§3] 폭 30
    private const float CliffDepth = 20f;           // [§3] 깊이 ≥ 20
    private const float RailEndZ = 176f;            // 옛 120 + 56(도착점 = 플랫폼 시작 + 8)
    // [§9 구현 결정] 도착 반경 7 — 설계서는 5였으나 레일 간격을 6으로 넓혀 바깥 카트 중심이 도착점에서 6m였다(3대 안).
    // [10-04 M-A 보정 — 측정값대로] 2대 안의 옛 도착점 (0,176)에서 x −6 카트 임계는 z ≥ 172.39(=176−√(7²−6²))였고, T2~T4 검사 로그의 z 172.5·거리 6.95는
    // 정지 위치가 아니라 판정 루프가 끊긴 순간(그때 충전 7.60 — 에너지 안 다함)의 값이다. 레일 끝(176) 정지 시 여유는 1.0(x −6)이었고, 감은 양이 적으면
    // (충전 < 7.52) 반경 밖에서 선다. 그래서 도착점을 두 레일 가운데 (ArrivalX = RailX 평균 = −3, 176)으로 옮겼다 — 두 카트 모두 임계 z ≥ 176−√(7²−3²) = 169.68
    // (= 카트 길이 3의 뒷면이 절벽 끝 168 위 = 몸통 전체가 플랫폼 위, 169.5 이상), 레일 끝 정지 시 거리 3.00(여유 4.00), 필요 충전 7.52 → 7.26.
    // x 0 카트의 임계는 169.00 → 169.68로 오히려 엄격해지고 x −6은 172.39 → 169.68로 관대해진다(반경 7은 그대로 — 반경만 키우면 x 0 카트가 허공에서 통과).
    private const float ArrivalRadius = 7f;
    private static readonly float ArrivalX = RailX.Average(); // = −3, 두 레일 가운데(RailX 뒤에 선언해야 초기화 순서가 맞다)

    // ── 무지개다리 [10-04 사용자 결정 · 진행/기획보완_2026-10-04/S1_카트2대_변경안.md §4 배치 초안 · 사용자 결정(가)] ──
    private const float BridgeX = 6f;               // [제안] 비게 된 x +6 레인 — 추격자 종점(x −3, z 139.4)에서 멀리 둔다(감지 반경 6 밖)
    private const float BridgeWidth = 4f;           // [제안] 폭 4(x 4~8) — 팀 생성 메뉴 기본 조각 폭 2를 맵 배치값으로 넓힘(추락 시 섹터 시작 복귀가 무거워서)
    private const float BridgeSegThickness = 0.2f;  // 팀 CreateSetup 조각 두께
    private const float BridgeSegLength = 2f;       // 팀 CreateSetup 조각 길이·간격(로컬 z = 2 + 2i, RainbowBridgeMenuItem.cs:61-62) — 한 판이면 첫 조각 로컬 z 2 위치만 쓴다
    private const float BridgeLength = PlatformNearZ - EdgeZ; // [10-04 사용자 결정] 다리 한 판 길이 = 절벽 28(z 140~168)
    private const float BridgePadZ = 170f;          // [제안] 발판 중심 z — 도착 플랫폼(z 168~188) 안 = TeamExitZone 안(Lab_CH1도 판이 도착 플랫폼 쪽)
    private const float BridgePadHalfSize = 0.75f;  // 팀 CreateSetup 발판 1.5×0.1×1.5
    private const float BridgeMinGapToRail = 2.5f;  // [제안] 카트 몸통(반폭 1) 가장자리 ↔ 다리 가장자리 ≥ 2.5(끼임 틈 규칙과 같은 값)
    private const float BridgeApproachMargin = 0.5f; // 접근 경로 ↔ 추격자 종점 거리가 팀 감지 반경 + 이 값(도형 반경) 이상이어야 한다

    private const float ExitDoorHalfW = 4f;  // 연결 통로 폭 8과 같게
    private const float ExitWallH = 8f;      // [§9] 도착 난간·출구 벽 8
    private const float ExitWallT = 0.5f;

    // 팀 Lab_CH1에서 사용자가 축 기준으로 놓은 포탈·발판 상대 위치(Ch1TestRoomMenuItem.PortalEnterXZ 등에서
    // 축(-9,-62)을 뺀 값). 축은 회전 없이 놓이므로 상대 위치를 그대로 옮기면 사용자 배치가 보존된다.
    private static readonly Vector2 PortalEnterFromAxle = new Vector2(1.04f, 3.91f);
    private static readonly Vector2 PortalExitFromAxle = new Vector2(-1.74f, 3.76f);
    private static readonly Vector2 PadFromAxle = new Vector2(-3.86f, 1.38f);
    // [10-04 L3 보정 — 사용자 결정] 축·발판·포탈 묶음(위 세 상대 위치가 그대로 따라간다)을 옛 (−12,133)에서 오른쪽 (12,133)으로 옮겼다.
    // 이유 [계산, 보정안 §2]: 옛 자리에서 다리 머리로 곧장 가면 추격자 종점(−3,139.4) 옆 1.1~2.5m를 지나(축→머리 2.10~2.45, 감는 자리→머리 1.83~2.09, 발판→머리 1.11~1.37)
    // 큐브·세모·구가 시뮬에서 잡혔다(감지 6, 종점 대기 중에도 추격 PathChaserController.cs:109-110). 새 자리는 다리와 SAFE_POST(24,134)와 같은 오른쪽이다.
    // 새 자리 계산 [씬 직독의 상대 위치 +24]: 축 (12,133) · 발판 (8.14,134.38) 면 x 7.14~9.14·z 133.38~135.38 · 포탈 Enter (13.04,136.91)·Exit (10.26,136.76) 트리거 2.4×2×0.4.
    //  · 지형 겹침 0: 묶음의 콜라이더는 전부 트리거(포탈 박스 2.4×2×0.4, 축 막대 2.6×0.3×0.3, 발판 2×0.15×2)이고 기둥·상인방·축대는 콜라이더가 없다 → 솔리드와 겹칠 것이 없고
    //    감사(Map4Audit, 비트리거만 — :252)·끼임 스윕 대상이 아니다. 가장 가까운 솔리드: 다리(한 판, x 4~8, z ≥ 140)까지 발판 4.62·Exit 포탈 트리거 모서리 3.22, 칸막이 벽(z 129)까지
    //    축 막대 3.85·발판 4.38, 승차장 끝(절벽 z 140)까지 Enter 포탈 2.89, 카트 x 0 몸통(x ±1)까지 발판 6.14, SAFE_POST 보조점까지 10 이상 → 공통 규칙의 금지 틈 0.02<g<1.0 없음.
    //  · 통행: 축·발판 묶음과 다리 머리 사이 폭 3.2~4.6, 벽 구멍(x −3~3)에서 축·발판까지 10 이상 열려 있다.
    //  · 접근 경로: 축·발판·감는 자리·SAFE_POST → 다리 머리 직선이 추격자 종점에서 ≥ 7.58(머리 왼쪽 걷는 끝 (4.5,140.5) 기준, 머리 중심은 9.07) > 감지 6 + 0.5.
    private static readonly Vector2 AxleXZ = new Vector2(12f, 133f);

    // [§11-1] 안전점 — 옛 SAFE_PRE (−26, 60)·SAFE_POST (24, 78)에 +56.
    private static readonly Vector2 SafePreXZ = new Vector2(-26f, 116f);  // 투석기 구역 왼쪽 벽감(추격 경로 반대편)
    private static readonly Vector2 SafePostXZ = new Vector2(24f, 134f);  // 승차장 오른쪽

    // ── 복도 장애물 (§11-3) ── 충돌 크기 = 가로(x) × 높이(y) × 깊이(z), 박스 콜라이더는 바닥(y 0)까지 채운다.
    private const float ShelfH = 1.5f;   // [제안 §11-3] 쓰러진 선반 높이 — 네모 점프 1.2 < 1.5라 넘을 수 없다
    private const float ShelfD = 0.8f;   // [제안 §11-3] 쓰러진 선반 깊이(길이는 무리마다 4~6)
    private const float DeskW = 1.6f, DeskH = 0.75f, DeskD = 0.8f;   // [제안 §11-3] 책상
    private const float ChairW = 0.5f, ChairH = 0.9f, ChairD = 0.5f; // [제안 §11-3] 의자(2~3개 붙여서)
    private const float MaxBlockWidth = CorridorHalfW * 2f * 0.6f;   // [제안 §11-3] 무리가 막는 폭 ≤ 복도 폭의 60%(=6)
    private const float MinPassageWidth = 2.5f;                      // [제안 §11-3] 남는 통로 ≥ 2.5

    // [계산 — 자기 검사 s1b_check.py로 산출·검증] 추격자 복도 경로(§11-2): 무리마다 통로 가운데 x로 꺾어
    // (무리 z 범위 앞뒤 1.5) 선반·책상·의자 콜라이더 안을 지나지 않게 한다. 좌·우 번갈아.
    private static readonly Vector2[] CorridorChaserRoute =
    {
        new Vector2( 2.96f, 17.10f), new Vector2( 2.96f, 22.90f),  // 무리1 z20 — 통로 오른쪽
        new Vector2(-2.77f, 28.89f), new Vector2(-2.77f, 34.56f),  // 무리2 z32 — 통로 왼쪽
        new Vector2( 2.61f, 40.90f), new Vector2( 2.61f, 47.43f),  // 무리3 z44 — 통로 오른쪽
        new Vector2(-2.96f, 53.10f), new Vector2(-2.96f, 58.90f),  // 무리4 z56 — 통로 왼쪽
        new Vector2( 2.71f, 64.62f), new Vector2( 2.71f, 70.71f),  // 무리5 z68 — 통로 오른쪽
        new Vector2(-2.61f, 76.90f), new Vector2(-2.61f, 83.43f),  // 무리6 z80 — 통로 왼쪽
        new Vector2( 2.96f, 89.10f), new Vector2( 2.96f, 94.90f),  // 무리7 z92 — 통로 오른쪽
    };
    // [구현 결정] 복도를 나온 뒤 투석기(z 109, x 0)를 오른쪽으로 비켜 벽 앞 대기점으로 — 옛 경로는 격벽 통로(x +26)에서
    // 비스듬히 와서 투석기 위를 지나지 않았는데, 복도(x 0)에서 곧장 가면 투석기 한가운데를 지나간다. 팀 슬링 투석기(배율 3)의
    // 조향 고리는 중심에서 로컬 z 6(반지름 1.5)에 있고 조향 ±100°로 돌므로 쓸고 가는 반경 ≈ 7.7 [계산: SlingCatapultMenuItem
    // SteerHandlePivotLocalFor·SteerRingRadiusFor]. 경로는 투석기 중심에서 ≥ 8.5, 복도 입구 모서리에서 ≥ 1(추격자 모습 반지름).
    private static readonly Vector2[] CatapultBypassRoute =
    {
        new Vector2(4f, 101.5f), new Vector2(12f, 104f), new Vector2(12f, 120f),
    };

    private struct ObstacleSpec
    {
        public int group;       // 무리 번호 1..7(z 20→1, 32→2, …, 92→7)
        public string kind;     // Shelf | Desk | Chair
        public string suffix;   // "" | a | b | c
        public Vector2 centerXZ;
        public Vector3 size;    // 회전 전 가로(x)·높이(y)·깊이(z)
        public float yawDeg;    // Y축 회전(선반만 비스듬히)
        public string Name => $"GEO_S1_Obst_{group}_{kind}" + (string.IsNullOrEmpty(suffix) ? "" : "_" + suffix);
    }

    private static Transform g; // Build 동안만 유효 — Generated 루트

    /// <summary>S1을 짓는다. 섹터 크기가 설계서 전제(폭 ≥60, 길이 188, 평지 출구)와 다르면 아무것도 짓지 않고 false —
    /// 호출자가 빈 틀로 되돌아간다.</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        if (def.width < SectorWidth - 0.01f || Mathf.Abs(def.length - SectorLength) > 0.01f
            || Mathf.Abs(def.exitHeight - def.floorHeight) > 0.001f)
        {
            Debug.LogError($"[S1_Builder] 섹터1 크기({def.width}×{def.length}, 출구 높이차 {def.exitHeight - def.floorHeight})가 " +
                           $"설계서 전제(폭 ≥{SectorWidth}, 길이 {SectorLength}, 평지)와 달라 S1 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        // [판정 8 · R2-C5] 예외 시 이 Build가 새로 만든 씬 루트(팀 메뉴 잔류물·TEMP_RespawnForS1Build)만 지우기 위한 시작 전 집합.
        var before = new HashSet<GameObject>(generated.gameObject.scene.GetRootGameObjects());
        RespawnController tempRespawn = null;
        try
        {
            Map4Build.BeginSection();
            BuildGeometry();
            BuildCorridorObstacles();

            // §11-4 노란 비상등 11개(S1-L 계약 C1) — 인자 = 복도 z 10(EntranceZ1)~100(CorridorZ1), 천장 아랫면 y 4(CorridorH).
            // [판정 A1] 전역 조명: 판정 A1로 Master 방향광 소프트 그림자 켬(INF1-2), 환경광 불변.
            S1_Lighting.Build(g, 10f, 100f, 4f);

            // ── 팀 기믹 ──
            BreakableObject wall = BuildBreakableWall();
            BuildCatapult();
            WindupAxle axle = MenuCreate<WindupAxle>("Tools/WindupAxleSystem/Create Windup Axle");
            WindupActivationPad pad = MenuCreate<WindupActivationPad>("Tools/WindupAxleSystem/Create Activation Pad");
            Portal portalEnter = MenuCreate<Portal>("Tools/PortalSystem/Create Portal (Enter — 굴리기 켬)");
            Portal portalExit = MenuCreate<Portal>("Tools/PortalSystem/Create Portal (Exit — 원래 이동으로 복귀)");
            if (wall == null || axle == null || pad == null || portalEnter == null || portalExit == null)
                throw new System.Exception("[S1_Builder] 팀 생성 메뉴 중 실패한 것이 있다 — 위 로그 확인.");

            PlaceOnGround(axle.gameObject, AxleXZ.x, AxleXZ.y, 0.02f);
            Place(portalEnter.transform, AxleXZ + PortalEnterFromAxle, 0f); // 포탈 피벗은 문 바닥
            Place(portalExit.transform, AxleXZ + PortalExitFromAxle, 0f);
            Place(pad.transform, AxleXZ + PadFromAxle, pad.transform.localScale.y * 0.5f);
            pad.latchOnFirstPress = true; // 팀 Lab_CH1과 동일 — 한 번 밟으면 카트 2대가 끝까지 간다

            RailCart[] carts = BuildCarts(axle, pad);
            BuildSupportZone();

            // [수정 라운드 R2] 생성이 조용히 실패하면 직전 MenuCreate가 남긴 선택(포탈 Exit 등)을 잘못 가져가지 않게 선택을 비우고,
            // 생성물이 팀 RespawnZone인지 확인한 뒤에만 옮긴다.
            Selection.activeGameObject = null;
            RespawnMenuItem.CreateCheckpoint();
            GameObject checkpoint = Selection.activeGameObject;
            if (checkpoint == null || checkpoint.GetComponent<RespawnZone>() == null)
                throw new System.Exception("[S1_Builder] 체크포인트 생성 실패(선택된 생성물에 RespawnZone 없음).");
            Adopt(checkpoint);
            // 스폰 슬롯(z 3~4)을 품는 폭 6 구역(입구 폭 16 안). 이 구역 위(x −3~3, z 0.5~6.5)에는 고체가 없어야 팀 페이드·낙하 복귀가
            // 입구 바닥(y 0)에 선다(BuildGeometry 입구 주석 — 수정 라운드 R2).
            checkpoint.transform.position = L(0f, 0f, 3.5f);

            Transform arrival = NewChild("S1_ArrivalPoint", L(ArrivalX, 0f, RailEndZ)).transform; // [10-04 M-A] 두 레일 가운데 (−3, 176)
            TeamExitZone exitZone = BuildExitZone();
            doorPhysics door = BuildExitDoor();

            // ── 추격자 일체(팀 BuildRig) ── 시작 z 1(스폰 뒤, §11-1)
            tempRespawn = new GameObject("TEMP_RespawnForS1Build").AddComponent<RespawnController>();
            PathChaserController chaser = PathChaserMenuItem.BuildRig(L(0f, 1f, 1f), tempRespawn);
            StripPersistentRespawnWiring(chaser.safePreCounter);
            StripPersistentRespawnWiring(chaser.safePostCounter);
            Object.DestroyImmediate(tempRespawn.gameObject);
            tempRespawn = null;

            LayoutChaser(chaser);
            PlaceSafePoint(chaser.safePreCounter, SafePreXZ);
            PlaceSafePoint(chaser.safePostCounter, SafePostXZ);

            chaser.wall = wall;
            chaser.carts = carts;
            chaser.arrivalPoint = arrival;
            chaser.teamExitZone = exitZone;
            chaser.arrivalRadius = ArrivalRadius;

            // ── 무지개다리 [10-04] — 추격자 종점과의 거리 검사에 chaser가 필요해 추격자 배치 뒤에 만든다 ──
            RainbowBridgeSwitch bridge = BuildRainbowBridge(chaser);
            CheckApproachClearance(chaser, axle); // [10-04 L3] 축·발판·감는 자리·SAFE_POST → 다리 머리 경로의 추격자 종점 거리(새 위치 기준)

            // ── 씬 연결(기믹 동작은 바꾸지 않음) ──
            GameObject adapters = NewChild("S1_Adapters", L(0f, 0f, 0f));
            adapters.AddComponent<Lab_SectionRespawnBridge>();

            BuildCliffOutOfBounds();

            // 출구 문 — 팀 OnChapterCleared(카트 2대 + 전원 도착)에 영구 배선. 같은 씬이라 저장된다.
            if (chaser.OnChapterCleared == null) chaser.OnChapterCleared = new UnityEvent();
            UnityEventTools.AddBoolPersistentListener(chaser.OnChapterCleared, door.SetPadPressed, true);

            // 추격은 도형이 S1 시작 구역에 실제로 배치된 뒤 시작. [구현 결정] 판정 구역 = 입구 전체(폭 16 × z 0~10).
            GameObject startZone = NewChild("S1_StartZone", L(0f, 2f, EntranceZ1 / 2f));
            BoxCollider startCol = startZone.AddComponent<BoxCollider>();
            startCol.isTrigger = true;
            startCol.size = new Vector3(EntranceHalfW * 2f, 4f, EntranceZ1);
            Lab_ActivateWhenPlayersReady activator = adapters.AddComponent<Lab_ActivateWhenPlayersReady>();
            activator.zone = startCol;
            activator.targets = new[] { chaser.gameObject };
            chaser.agent.enabled = false;
            chaser.gameObject.SetActive(false);

            ReportMissing(chaser, pad);
            ReportBridgeMissing(bridge, chaser);
            ReportStrayRoots();
            Debug.Log("[S1_Builder] 섹터1 구체화 완료(§11 복도·장애물 7무리·비상등 + 팀 기믹: 추격자·투석기·파괴벽·카트 2대·태엽 축·포탈 쌍·발판·무지개다리(협동)·체크포인트).");
            return true;
        }
        catch (System.Exception e)
        {
            // [판정 8 · R2-C5] 부분 생성 금지 — 지형·조명·팀 기믹 어느 단계의 예외든 Generated를 비우고 씬 루트를 Build 전 집합으로 되돌린 뒤 false.
            Debug.LogError($"[S1_Builder] 섹터1 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--) Object.DestroyImmediate(generated.GetChild(i).gameObject);
            foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
                if (!before.Contains(r)) Object.DestroyImmediate(r);   // 이번 Build가 팀 메뉴로 만든 루트 잔류물
            g = null; return false;
        }
        finally
        {
            // 모든 경로(성공·예외)에서 임시 RespawnController 정리와 정적 필드 초기화를 보장한다. 예외 경로에서는 위 루트 삭제로
            // 이미 지워졌으면 Unity 파괴 객체 비교(tempRespawn == null)가 참이라 여기서 다시 지우지 않는다.
            if (tempRespawn != null) Object.DestroyImmediate(tempRespawn.gameObject);
            g = null;
        }
    }

    // ───────────────────────── 지형 ─────────────────────────

    private static void BuildGeometry()
    {
        float T = InnerWallT, H = CorridorH;

        // 바닥: 입구(폭 16) / 복도(폭 10) / 투석기 구역(폭 60, 얇은 판) / 승차장(절벽 면까지 깊은 블록) / 도착 플랫폼(깊은 블록).
        // 윗면 모두 y=0이고 서로 면만 맞닿는다 → 턱 0·틈 0, Floor 겹침 0.
        Solid(Map4Build.Floor(g, new Vector3(-EntranceHalfW, -0.3f, 0f), new Vector3(EntranceHalfW, 0f, EntranceZ1)), "GEO_S1_Floor_Entrance");
        Solid(Map4Build.Floor(g, new Vector3(-CorridorHalfW, -0.3f, EntranceZ1), new Vector3(CorridorHalfW, 0f, CorridorZ1)), "GEO_S1_Floor_Corridor");
        Solid(Map4Build.Floor(g, new Vector3(-HalfW, -0.3f, CorridorZ1), new Vector3(HalfW, 0f, PartitionZ0)), "GEO_S1_Floor_Catapult");
        Solid(Map4Build.Floor(g, new Vector3(-HalfW, -CliffDepth, PartitionZ0), new Vector3(HalfW, 0f, EdgeZ)), "GEO_S1_Floor_Station");
        Solid(Map4Build.Floor(g, new Vector3(-PlatformHalfW, -CliffDepth, PlatformNearZ), new Vector3(PlatformHalfW, 0f, SectorLength)), "GEO_S1_Floor_Platform");

        // 입구(z 0~10, x −8~8): 뒤·좌·우 벽. [구현 결정 — 수정 라운드 R2] 입구는 천장 없이 두고 벽을 외벽 높이 14로 올린다.
        // 옛 구현(벽 4 + 입구 천장 y 4~4.3)은 팀 체크포인트(6×18×6, 중심 y 9) 위 칸을 덮어, 팀 RespawnZone.TryFindGroundY(중심에서
        // 아래로 쏜 레이 중 가장 높은 면)가 천장 윗면 y 4.3을 페이드 복귀 바닥으로 잡았다(R2_func.log "페이드 바닥 (0.00, 4.30, 3.50)").
        // 낙하 복귀(시작 y 27.5)도 같은 지붕에 떨어져 경기 구역으로 돌아올 길이 없었다(소프트락). 그래서 체크포인트 칸(x −3~3, z 0.5~6.5)
        // 위에는 고체를 두지 않는다. 지름길 차단은 높이로 한다 — 입구 벽·전환 벽·복도 입구 상인방을 14로 올려 입구에서 복도 지붕
        // (윗면 y 4.3)으로 넘어갈 길을 막는다(점프 최고 2.0 [계산]). 벽은 바닥 바깥에 붙인다(겹침 0, 틈 0).
        float EH = OuterWallH;
        Solid(Map4Build.Wall(g, new Vector3(-EntranceHalfW - T, 0f, -T), new Vector3(EntranceHalfW + T, EH, 0f)), "GEO_S1_Wall_EntranceBack");
        Solid(Map4Build.Wall(g, new Vector3(-EntranceHalfW - T, 0f, 0f), new Vector3(-EntranceHalfW, EH, EntranceZ1)), "GEO_S1_Wall_EntranceL");
        Solid(Map4Build.Wall(g, new Vector3(EntranceHalfW, 0f, 0f), new Vector3(EntranceHalfW + T, EH, EntranceZ1)), "GEO_S1_Wall_EntranceR");
        // 입구(폭 16) → 복도(폭 10) 전환 벽: z 10 면, x −8~−5 · 5~8(모서리 x ±(5~5.5)는 복도 벽이 채운다). 높이 14.
        Solid(Map4Build.Wall(g, new Vector3(-EntranceHalfW - T, 0f, EntranceZ1), new Vector3(-CorridorHalfW - T, EH, EntranceZ1 + T)), "GEO_S1_Wall_TransitionL");
        Solid(Map4Build.Wall(g, new Vector3(CorridorHalfW + T, 0f, EntranceZ1), new Vector3(EntranceHalfW + T, EH, EntranceZ1 + T)), "GEO_S1_Wall_TransitionR");
        // 복도 입구(폭 10 × 높이 4) 위(y 4~14)를 막는 상인방 — z 100 쪽 FrontLintel과 같은 역할. 옛 입구 천장이 채우던
        // z 10~10.5 칸(복도 천장 z 10.5 시작 앞)도 이것이 막는다.
        Solid(Map4Build.Wall(g, new Vector3(-CorridorHalfW - T, H, EntranceZ1), new Vector3(CorridorHalfW + T, EH, EntranceZ1 + T)), "GEO_S1_Wall_EntranceLintel");

        // 복도(z 10~100, x −5~5): 좌우 벽 높이 4 + 천장(아랫면 y 4) — §11-2 "천장 4, 장애물 위로 넘어가는 지름길 차단".
        Solid(Map4Build.Wall(g, new Vector3(-CorridorHalfW - T, 0f, EntranceZ1), new Vector3(-CorridorHalfW, H, CorridorZ1)), "GEO_S1_Wall_CorridorL");
        Solid(Map4Build.Wall(g, new Vector3(CorridorHalfW, 0f, EntranceZ1), new Vector3(CorridorHalfW + T, H, CorridorZ1)), "GEO_S1_Wall_CorridorR");
        Solid(Map4Build.Wall(g, new Vector3(-CorridorHalfW - T, H, EntranceZ1 + T), new Vector3(CorridorHalfW + T, H + CeilT, CorridorZ1 - T)), "GEO_S1_Ceil_Corridor");

        // 복도 끝 z 100 → 투석기 구역(폭 60)으로 열림. 전면벽 x −30~−5 · 5~30(면 z 100, 복도 벽 바깥 모서리부터).
        // [구현 결정] 전면벽 높이 = 외벽 14(투석기 구역을 외벽 높이로 두른다). 복도 입구(폭 10 × 높이 4) 위(y 4~14)는
        // 상인방 벽으로 막는다 — 투석기 구역 쪽에서 복도 천장 위로 올라가는 틈 차단.
        Solid(Map4Build.Wall(g, new Vector3(-HalfW - OuterWallT, 0f, CorridorZ1 - T), new Vector3(-CorridorHalfW - T, OuterWallH, CorridorZ1)), "GEO_S1_Wall_FrontL");
        Solid(Map4Build.Wall(g, new Vector3(CorridorHalfW + T, 0f, CorridorZ1 - T), new Vector3(HalfW + OuterWallT, OuterWallH, CorridorZ1)), "GEO_S1_Wall_FrontR");
        Solid(Map4Build.Wall(g, new Vector3(-CorridorHalfW - T, H, CorridorZ1 - T), new Vector3(CorridorHalfW + T, OuterWallH, CorridorZ1)), "GEO_S1_Wall_FrontLintel");

        // 외벽: 좌·우(투석기 구역 ~ 절벽 시작, 높이 14, 천장 없음 — §9).
        Solid(Map4Build.Wall(g, new Vector3(-HalfW - OuterWallT, 0f, CorridorZ1), new Vector3(-HalfW, OuterWallH, EdgeZ)), "GEO_S1_Wall_OuterL");
        Solid(Map4Build.Wall(g, new Vector3(HalfW, 0f, CorridorZ1), new Vector3(HalfW + OuterWallT, OuterWallH, EdgeZ)), "GEO_S1_Wall_OuterR");

        // 칸막이: 파괴벽(가운데 폭 6) 양옆을 외벽까지 막아 벽을 부숴야만 지나가게(팀 CH1_Barrier_L/R와 같은 역할).
        Solid(Map4Build.Wall(g, new Vector3(-HalfW, 0f, PartitionZ0), new Vector3(-BreakWallHalfW, BreakWallH, PartitionZ1)), "GEO_S1_Wall_PartitionL");
        Solid(Map4Build.Wall(g, new Vector3(BreakWallHalfW, 0f, PartitionZ0), new Vector3(HalfW, BreakWallH, PartitionZ1)), "GEO_S1_Wall_PartitionR");

        // 도착 플랫폼: 좌우 난간벽 + 출구 벽(가운데 폭 8은 문).
        Solid(Map4Build.Wall(g, new Vector3(-PlatformHalfW - OuterWallT, 0f, PlatformNearZ), new Vector3(-PlatformHalfW, ExitWallH, SectorLength)), "GEO_S1_Wall_PlatformRailL");
        Solid(Map4Build.Wall(g, new Vector3(PlatformHalfW, 0f, PlatformNearZ), new Vector3(PlatformHalfW + OuterWallT, ExitWallH, SectorLength)), "GEO_S1_Wall_PlatformRailR");
        Solid(Map4Build.Wall(g, new Vector3(-PlatformHalfW, 0f, SectorLength - ExitWallT), new Vector3(-ExitDoorHalfW, ExitWallH, SectorLength)), "GEO_S1_Wall_ExitL");
        Solid(Map4Build.Wall(g, new Vector3(ExitDoorHalfW, 0f, SectorLength - ExitWallT), new Vector3(PlatformHalfW, ExitWallH, SectorLength)), "GEO_S1_Wall_ExitR");
    }

    // ───────────────────────── 복도 장애물 (§11-3) ─────────────────────────

    /// <summary>쓰러진 선반·책상·의자 무리 7곳(z 20·32·44·56·68·80·92). 무리마다 한쪽 벽에 붙여 복도 폭의 60% 이하만 막고
    /// 반대쪽 통로(≥ 2.5)를 남기며 통로 쪽을 좌·우 번갈아 둔다(무리1 통로 오른쪽 → 무리2 왼쪽 → …).
    ///
    /// 끼임 방지 — 틈 규칙은 **쌍마다**(INF3 감사 계약 C4: Obst↔Obst·Obst↔Wall, 0 &lt; g &lt; 2.5 위반) 적용된다. 그래서
    /// "벽 → 책상 → 책상 → 의자"처럼 셋 이상을 줄지어 붙이면 사이가 채워져 있어도 양 끝 쌍(예: 벽↔둘째 책상 1.6)이 위반이 된다.
    /// [구현 결정] 그래서 무리마다 "벽에 한쪽 끝 모서리를 댄 쓰러진 선반 1개"를 뼈대로 두고, 책상·의자는 선반의 **벽 반대쪽(자유) 끝**에
    /// 같은 각도로 면을 맞대어 붙인다 — 붙은 조각끼리는 모두 서로 맞닿고(틈 0), 벽과는 ≥ 2.5 떨어진다(자기 검사 확인).
    /// 책상(높이 0.75)은 점프로 넘을 수 있고 선반(1.5)은 네모가 못 넘는다(§11-3 역할 그대로). 의자는 2개를 선반 끝면에 나란히(§11-3 "2~3개").
    /// 비스듬한 선반은 벽 쪽 끝 모서리를 벽 면에 정확히 대어(틈 0) 선반 끝과 벽 사이 삼각 틈을 없앤다.
    /// 충돌체는 바닥(y 0)부터 채운 단순 박스, 모두 정적. 다리 모양은 콜라이더 없는 VIS_ 자식으로만.</summary>
    private static void BuildCorridorObstacles()
    {
        List<ObstacleSpec> specs = new List<ObstacleSpec>();

        // [제안 §11-3 + 구현 결정: 길이·각도·붙임] AddShelf(목록, 무리, 붙일 벽 −1=왼쪽/+1=오른쪽, 선반 중심 z, 길이 4~6, Y회전°, 자유 끝에 붙일 것)
        AddShelf(specs, 1, -1, 20f, 6f, 20f, null);      // 막는 폭 5.91 · 통로 오른쪽 4.09
        AddShelf(specs, 2, +1, 32f, 4f, -20f, "Desk");   // 막는 폭 5.54 · 통로 왼쪽 4.46
        AddShelf(specs, 3, -1, 44f, 5f, -30f, "Chair");  // 막는 폭 5.21 · 통로 오른쪽 4.79
        AddShelf(specs, 4, +1, 56f, 6f, 20f, null);      // 막는 폭 5.91 · 통로 왼쪽 4.09
        AddShelf(specs, 5, -1, 68f, 4f, 25f, "Desk");    // 막는 폭 5.41 · 통로 오른쪽 4.59
        AddShelf(specs, 6, +1, 80f, 5f, 30f, "Chair");   // 막는 폭 5.21 · 통로 왼쪽 4.79
        AddShelf(specs, 7, -1, 92f, 6f, -20f, null);     // 막는 폭 5.91 · 통로 오른쪽 4.09

        GameObject groupRoot = new GameObject("S1_CorridorObstacles");
        groupRoot.transform.SetParent(g, false);
        Material mat = Map4Build.GetMaterial("Door"); // [구현 결정] 가구 색 = 팔레트 "Door"(어두운 회색) — 팔레트에 가구 항목 없음

        foreach (ObstacleSpec s in specs)
        {
            Vector3 c = new Vector3(s.centerXZ.x, s.size.y / 2f, s.centerXZ.y);
            // Map4Build.Wall = 박스 콜라이더 1개 + 콜라이더 없는 시각물 자식. 부모를 Generated로 두어 좌표를 섹터 로컬로 유지하고,
            // 비스듬한 선반은 만든 뒤 중심 기준 Y회전(CreateSolidBox의 localPosition = 박스 중심).
            Transform t = Map4Build.Wall(g, c - s.size / 2f, c + s.size / 2f, "Door");
            if (t == null) throw new System.Exception($"[S1_Builder] 장애물 생성 실패: {s.Name}");
            t.name = s.Name;
            t.localRotation = Quaternion.Euler(0f, s.yawDeg, 0f);
            t.SetParent(groupRoot.transform, true);
            DecorateObstacle(t, s, mat);
        }

        CheckObstacleGroups(specs);
    }

    /// <summary>비스듬히 누운 선반. 회전 후 x 방향 반폭 hx = L/2·|cos θ| + D/2·|sin θ|, 중심 x = side·(W − hx) →
    /// 벽 쪽 끝 모서리가 벽 면(x = ±W)에 정확히 닿는다(틈 0, 관통 0).</summary>
    private static ObstacleSpec Shelf(int group, int side, float centerZ, float length, float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        float hx = length / 2f * Mathf.Abs(Mathf.Cos(rad)) + ShelfD / 2f * Mathf.Abs(Mathf.Sin(rad));
        return new ObstacleSpec
        {
            group = group, kind = "Shelf", suffix = "",
            centerXZ = new Vector2(side * (CorridorHalfW - hx), centerZ),
            size = new Vector3(length, ShelfH, ShelfD),
            yawDeg = yawDeg,
        };
    }

    /// <summary>선반 1개 + (선택) 자유 끝에 붙인 책상 1개 또는 의자 2개를 목록에 넣는다.
    /// 선반 로컬 축(회전 후): ax = Euler(0,θ,0)·(1,0,0) = (cos θ, −sin θ), az = Euler(0,θ,0)·(0,0,1) = (sin θ, cos θ) [x,z].
    /// 벽 반대쪽 끝 방향 = −side·ax. 책상: 중심 = 선반 중심 + (L/2 + 책상 가로/2)·(−side·ax), 같은 각도 — 책상 깊이 0.8 = 선반 깊이라
    /// 끝면끼리 딱 맞는다. 의자 2개: 중심 = 선반 중심 + (L/2 + 의자 가로/2)·(−side·ax) ∓ (의자 깊이/2)·az — 두 의자가 서로 맞닿고
    /// 둘 다 선반 끝면(깊이 0.8 구간)에 닿는다(양옆으로 0.1씩 튀어나옴 — 틈이 아니라 턱 0.1 모서리).</summary>
    private static void AddShelf(List<ObstacleSpec> specs, int group, int side, float centerZ, float length, float yawDeg, string endKind)
    {
        ObstacleSpec shelf = Shelf(group, side, centerZ, length, yawDeg);
        specs.Add(shelf);
        if (string.IsNullOrEmpty(endKind)) return;

        Quaternion q = Quaternion.Euler(0f, yawDeg, 0f);
        Vector3 ax3 = q * Vector3.right, az3 = q * Vector3.forward;
        Vector2 toFree = new Vector2(ax3.x, ax3.z) * -side;
        Vector2 az = new Vector2(az3.x, az3.z);
        if (endKind == "Desk")
        {
            specs.Add(new ObstacleSpec
            {
                group = group, kind = "Desk", suffix = "",
                centerXZ = shelf.centerXZ + toFree * (length / 2f + DeskW / 2f),
                size = new Vector3(DeskW, DeskH, DeskD),
                yawDeg = yawDeg,
            });
        }
        else // Chair ×2
        {
            Vector2 baseXZ = shelf.centerXZ + toFree * (length / 2f + ChairW / 2f);
            specs.Add(new ObstacleSpec
            {
                group = group, kind = "Chair", suffix = "a",
                centerXZ = baseXZ - az * (ChairD / 2f),
                size = new Vector3(ChairW, ChairH, ChairD),
                yawDeg = yawDeg,
            });
            specs.Add(new ObstacleSpec
            {
                group = group, kind = "Chair", suffix = "b",
                centerXZ = baseXZ + az * (ChairD / 2f),
                size = new Vector3(ChairW, ChairH, ChairD),
                yawDeg = yawDeg,
            });
        }
    }

    /// <summary>시각물 — 선반은 박스 그대로(누운 선반), 책상·의자는 박스 시각물을 지우고 상판·좌판·등받이·다리로 바꾼다.
    /// 모두 콜라이더 없음. 좌표는 장애물 박스 중심 기준 로컬(부모 스케일 1).</summary>
    private static void DecorateObstacle(Transform t, ObstacleSpec s, Material mat)
    {
        Transform baseVis = t.Find("Visual");
        string tag = s.Name.Substring("GEO_".Length);
        if (s.kind == "Shelf")
        {
            if (baseVis != null) baseVis.name = "VIS_" + tag;
            return;
        }
        if (baseVis != null) Object.DestroyImmediate(baseVis.gameObject);

        Vector3 sz = s.size;
        float bottom = -sz.y / 2f;
        const float leg = 0.06f;
        if (s.kind == "Desk")
        {
            const float topT = 0.06f;
            Vis(t, $"VIS_{tag}_Top", new Vector3(0f, sz.y / 2f - topT / 2f, 0f), new Vector3(sz.x, topT, sz.z), mat);
            float legH = sz.y - topT;
            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * (sz.x / 2f - leg / 2f);
                float lz = (i < 2 ? -1f : 1f) * (sz.z / 2f - leg / 2f);
                Vis(t, $"VIS_{tag}_Leg{i}", new Vector3(lx, bottom + legH / 2f, lz), new Vector3(leg, legH, leg), mat);
            }
        }
        else // Chair
        {
            const float seatY = 0.45f, seatT = 0.05f, backT = 0.05f; // [구현 결정] 좌판 높이 0.45 — 겉모습만
            Vis(t, $"VIS_{tag}_Seat", new Vector3(0f, bottom + seatY - seatT / 2f, 0f), new Vector3(sz.x, seatT, sz.z), mat);
            Vis(t, $"VIS_{tag}_Back", new Vector3(0f, bottom + (seatY + sz.y) / 2f, sz.z / 2f - backT / 2f), new Vector3(sz.x, sz.y - seatY, backT), mat);
            float legH = seatY - seatT;
            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * (sz.x / 2f - leg / 2f);
                float lz = (i < 2 ? -1f : 1f) * (sz.z / 2f - leg / 2f);
                Vis(t, $"VIS_{tag}_Leg{i}", new Vector3(lx, bottom + legH / 2f, lz), new Vector3(leg, legH, leg), mat);
            }
        }
    }

    private static void Vis(Transform parent, string name, Vector3 localCenter, Vector3 size, Material mat)
    {
        GameObject v = GameObject.CreatePrimitive(PrimitiveType.Cube);
        v.name = name;
        Object.DestroyImmediate(v.GetComponent<Collider>());
        v.transform.SetParent(parent, false);
        v.transform.localPosition = localCenter;
        v.transform.localRotation = Quaternion.identity;
        v.transform.localScale = size;
        v.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>무리별 막는 폭 ≤ 6(60%)·남는 통로 ≥ 2.5를 생성 시점에도 확인한다(회전 박스 꼭짓점의 x 범위). 미달이면 에러.
    /// 틈 0/≥2.5·통과 경로·추격 경로 교차는 과제 자기 검사(s1b_check.py)와 INF3 감사가 본다.</summary>
    private static void CheckObstacleGroups(List<ObstacleSpec> specs)
    {
        foreach (IGrouping<int, ObstacleSpec> grp in specs.GroupBy(s => s.group))
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (ObstacleSpec s in grp)
            {
                float rad = s.yawDeg * Mathf.Deg2Rad;
                float hx = s.size.x / 2f * Mathf.Abs(Mathf.Cos(rad)) + s.size.z / 2f * Mathf.Abs(Mathf.Sin(rad));
                minX = Mathf.Min(minX, s.centerXZ.x - hx);
                maxX = Mathf.Max(maxX, s.centerXZ.x + hx);
            }
            float block = maxX - minX;
            float passage = CorridorHalfW * 2f - block;
            if (block > MaxBlockWidth + 0.01f)
                Debug.LogError($"[S1_Builder] 장애물 무리 {grp.Key}가 복도 폭의 60%를 넘게 막는다: {block:F2}U > {MaxBlockWidth:F2}U.");
            Map4Build.CheckPassageWidth(passage, MinPassageWidth, $"S1 복도 장애물 무리 {grp.Key}");
        }
    }

    // ───────────────────────── 팀 기믹 배치 ─────────────────────────

    private static BreakableObject BuildBreakableWall()
    {
        BreakableObject wall = MenuCreate<BreakableObject>("Tools/DestructionSystem/Create Breakable/Cube");
        if (wall == null) return null;
        wall.name = "S1_BreakableWall";
        wall.transform.SetPositionAndRotation(L(0f, BreakWallH / 2f, (PartitionZ0 + PartitionZ1) / 2f), Quaternion.identity);
        wall.transform.localScale = new Vector3(BreakWallHalfW * 2f, BreakWallH, PartitionZ1 - PartitionZ0);
        // requiredWeight 3·breakThreshold 5는 팀 기본값 그대로(네모 3.0, 충돌 속도 ≥5 — 설계서 §2).
        return wall;
    }

    private static void BuildCatapult()
    {
        GameObject catapult = Adopt(SlingCatapultMenuItem.BuildSlingCatapult(L(0f, 0f, CatapultZ), CatapultMenuItem.Scale, "S1_SlingCatapult"));
        if (catapult == null) throw new System.Exception("[S1_Builder] 투석기 생성 실패.");
        // 발사 방향은 -root.forward(CatapultArm.cs 387) — 파괴벽 중앙을 향하게 돌린다(팀 LayoutCore와 같은 식).
        Vector3 toWall = L(0f, 0f, PartitionZ0) - L(0f, 0f, CatapultZ);
        catapult.transform.rotation = Quaternion.LookRotation(-toWall.normalized, g.up);
        PlaceOnGround(catapult, 0f, CatapultZ, 0.02f);
    }

    private static RailCart[] BuildCarts(WindupAxle axle, WindupActivationPad pad)
    {
        RailCart[] carts = new RailCart[RailX.Length];
        for (int i = 0; i < carts.Length; i++)
        {
            GameObject cartGo = Adopt(RailCartMenuItem.CreateRailCartAt(L(RailX[i], 0f, CartStartZ)));
            RailCart cart = cartGo.GetComponent<RailCart>();
            cart.name = $"S1_RailCart_{i}";
            RailPath path = cart.path;
            if (path == null || path.waypoints == null || path.waypoints.Length < 3)
                throw new System.Exception("[S1_Builder] 팀 카트 생성기가 웨이포인트 3개짜리 RailPath를 만들지 않았다 — 팀 코드 변경 확인.");
            Adopt(path.gameObject);

            // 승차장 → 절벽 끝 → 플랫폼, 직선(팀 LayoutCore와 같은 처리: 곡선 제어점 해제).
            Vector3 a = L(RailX[i], 0f, CartStartZ);
            Vector3 mid = L(RailX[i], 0f, EdgeZ);
            Vector3 b = L(RailX[i], 0f, RailEndZ);
            path.transform.position = a;
            path.waypoints[0].position = a;
            path.waypoints[1].position = mid;
            path.waypoints[2].position = b;
            path.curveControlPoints = new Transform[0];
            Transform curve = path.transform.Find("CurvePoint_AMid");
            if (curve != null) Object.DestroyImmediate(curve.gameObject);

            cart.transform.SetPositionAndRotation(a, Quaternion.LookRotation(mid - a, g.up));
            cart.axle = axle;
            cart.activationMode = WindupActivationMode.HoldPad;
            cart.activationPad = pad;
            path.GetComponent<RailTrackVisual>()?.ForceRebuild();
            carts[i] = cart;
        }
        return carts;
    }

    /// <summary>[10-04 사용자 결정] 팀 무지개다리 — 팀 생성 함수(RainbowBridgeMenuItem.CreateSetup, 팀 Lab_CH1과 같은 구성: 발판 1 + 조각 N,
    /// 조각 콜라이더·렌더러 꺼진 채 저장, targetObjects 배선, 협동 모드)를 부른 뒤 위치·조각 폭만 맞춘다. 새 스크립트 없음.
    /// [10-04 사용자 결정 — 다리 한 판] 조각 수 1(CreateSetup(1)) — 한 판(28×4×0.2)이 절벽 z 140~168을 덮는다(옛 14조각은 이음매 걸림, 헤더 참고).
    /// 남는 이음매 2곳 수치: ① 승차장↔다리(z 140): 승차장 바닥 윗면 y 0(Map4Build.Floor 상자 윗면) ↔ 다리 윗면 = 루트 y −0.1f + 판 반두께 0.1f = 0 → 높이차 0(부동소수 오차 ≤ 1e−7),
    /// 틈 0(바닥 끝 EdgeZ = 140과 판 시작 154 − 14 = 140이 둘 다 정수라 float로 정확). ② 다리↔도착 플랫폼(z 168): 판 끝 154 + 14 = 168 ↔ 플랫폼 시작 PlatformNearZ = 168, 윗면 모두 y 0 → 같은 값
    /// (높이차 0·틈 0). 물리 접촉 오프셋 0.01(DynamicsManager)보다 5자리 작아 단차로 걸리지 않는다. 조각 내부 이음매 13곳은 없어졌다.
    /// 발판은 도착 플랫폼 안(BridgePadZ) — 먼저 도착한 도형이 밟고 있는 동안(activatorRequiresHold)만 열린다.
    /// 팀 생성 함수는 팀 머티리얼 2개(RainbowBridge_*_Mat)를 다시 저장하려 들지만 Lab_TeamAssetSaveGuard가 디스크 쓰기를 막는다.</summary>
    private static RainbowBridgeSwitch BuildRainbowBridge(PathChaserController chaser)
    {
        const int count = 1; // [10-04 사용자 결정] 다리 한 판 — 팀 CreateSetup의 조각 수를 1로

        Selection.activeGameObject = null;
        RainbowBridgeMenuItem.CreateSetup(count);
        GameObject padGo = Selection.activeGameObject;
        RainbowBridgeSwitch sw = padGo != null ? padGo.GetComponent<RainbowBridgeSwitch>() : null;
        if (sw == null || sw.targetObjects == null || sw.targetObjects.Length != count || padGo.transform.parent == null)
            throw new System.Exception("[S1_Builder] 팀 무지개다리 생성(RainbowBridgeMenuItem.CreateSetup) 실패 — 선택된 생성물에 RainbowBridgeSwitch(조각 " + count + "개)가 없다.");

        Transform root = padGo.transform.parent;
        Adopt(root.gameObject);
        root.name = "S1_RainbowBridge";
        // 팀 조각 로컬 중심 = (0, 0, 2 + 2i) → 한 판(i = 0) 중심 로컬 z 2가 절벽 가운데 EdgeZ + 14 = 154에 오려면 루트 z = 154 − 2 = 152. 조각 두께 0.2 → 윗면 y 0이려면 루트 y = −0.1.
        root.SetPositionAndRotation(L(BridgeX, -BridgeSegThickness / 2f, EdgeZ + BridgeLength / 2f - BridgeSegLength), g.rotation);

        GameObject deck = sw.targetObjects[0];
        deck.name = "S1_RainbowBridge_Deck";
        deck.transform.localScale = new Vector3(BridgeWidth, BridgeSegThickness, BridgeLength); // 한 판: 폭 4(팀 기본 2에서), 두께 0.2(팀 기본), 길이 28

        padGo.name = "S1_RainbowBridge_Switch";
        // 발판은 팀 Lab_CH1처럼 도착 플랫폼 쪽(이동 방향 끝). 발판 윗면 높이 = 두께 0.1(바닥 y 0 위에 얹음).
        padGo.transform.SetPositionAndRotation(L(BridgeX, padGo.transform.localScale.y * 0.5f, BridgePadZ), g.rotation);
        sw.activatorRequiresHold = true; // 협동 — 밟고 있는 동안만 유지(팀 Lab_CH1과 같음, 팀 기본값)

        // ── 배치 검사(실패하면 로그 에러 — 팀 컨트롤러·씬은 이미 만들어진 뒤라 생성은 계속) ──
        float half = BridgeWidth / 2f;
        // 추격자 종점(LayoutChaser: x ChaserLaneX, z EdgeZ − 0.6)에서 다리 직사각형까지 수평 거리 > 팀 감지 반경 + 도형 반경 여유.
        Vector2 chaserEnd = new Vector2(ChaserLaneX, EdgeZ - 0.6f);
        Vector2 nearest = new Vector2(Mathf.Clamp(chaserEnd.x, BridgeX - half, BridgeX + half), Mathf.Clamp(chaserEnd.y, EdgeZ, PlatformNearZ));
        float gap = Vector2.Distance(chaserEnd, nearest);
        if (gap < chaser.detectRadius + 0.5f)
            Debug.LogError($"[S1_Builder] 다리 가장자리가 추격자 종점에서 {gap:F2}m — 팀 감지 반경 {chaser.detectRadius} + 도형 반경 0.5 안이다. 종점에서 대기 중인 추격자가 다리를 건너는 도형을 쫓는다.");
        // 카트 레일(몸통 반폭 1)과 다리 가장자리 간격
        foreach (float rx in RailX)
        {
            float edgeGap = Mathf.Abs(rx - BridgeX) - 1f - half;
            if (edgeGap < BridgeMinGapToRail)
                Debug.LogError($"[S1_Builder] 레일 x {rx} 카트와 다리 가장자리 간격 {edgeGap:F2}m < {BridgeMinGapToRail}.");
        }
        return sw;
    }

    /// <summary>[10-04 L3 보정] 후발 도형이 승차장에서 다리 머리로 가는 직선 경로가 종점 대기 추격자의 감지권(팀 detectRadius + 도형 반경 여유)을 피하는지 점검한다.
    /// 출발점 = 태엽 축·활성 발판·감는 자리 중 추격자에 가장 가까운 점(크랭크에서 휠 감기 거리 wheelRange 안 — 최악)·SAFE_POST(잡힌 뒤 복귀 자리),
    /// 도착점 = 다리 머리 중심과 왼쪽 걷는 끝(도형 반경 0.5 안쪽). 모든 쌍에서 선분 ↔ 종점 최소 거리가 필요값 미만이면 에러(생성은 계속).
    /// 보정 전 (−12,133) 자리는 이 점검이 1.1~2.5m로 에러를 냈을 것이다.</summary>
    private static void CheckApproachClearance(PathChaserController chaser, WindupAxle axle)
    {
        Vector2 chaserEnd = new Vector2(ChaserLaneX, EdgeZ - 0.6f);
        float need = chaser.detectRadius + BridgeApproachMargin;
        float half = BridgeWidth / 2f;
        Vector2 padXZ = AxleXZ + PadFromAxle;
        Vector2 toChaser = chaserEnd - AxleXZ;
        float reach = axle != null ? Mathf.Min(axle.wheelRange, toChaser.magnitude) : 0f;
        Vector2 windSpot = AxleXZ + (toChaser.sqrMagnitude > 1e-6f ? toChaser.normalized : Vector2.zero) * reach;
        (string name, Vector2 point)[] starts =
        {
            ("태엽 축", AxleXZ), ("활성 발판", padXZ), ("감는 자리(추격자에 가장 가까운 점)", windSpot), ("SAFE_POST", SafePostXZ),
        };
        (string name, Vector2 point)[] heads =
        {
            ("다리 머리 중심", new Vector2(BridgeX, EdgeZ + 0.5f)),
            ("다리 머리 왼쪽 걷는 끝", new Vector2(BridgeX - half + 0.5f, EdgeZ + 0.5f)),
        };
        float worst = float.MaxValue;
        string worstName = "";
        foreach (var s in starts)
            foreach (var h in heads)
            {
                float d = DistancePointToSegment(chaserEnd, s.point, h.point);
                if (d < worst) { worst = d; worstName = $"{s.name} → {h.name}"; }
                if (d < need)
                    Debug.LogError($"[S1_Builder] 접근 경로 '{s.name} → {h.name}'가 추격자 종점에서 {d:F2}m — 팀 감지 반경 {chaser.detectRadius} + 도형 반경 {BridgeApproachMargin} = {need:F1} 안이다. " +
                                   "곧장 다리로 가는 후발 도형이 종점에서 대기 중인 추격자에게 잡힌다(잡히면 SAFE_POST 복귀).");
            }
        Debug.Log($"[S1_Builder] 다리 접근 경로 ↔ 추격자 종점 최소 거리 {worst:F2}m(필요 ≥ {need:F1}, {worstName}).");
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>다리 배선 점검 — 팀 컴포넌트가 비어 있으면 플레이 시 다리가 안 열린다. 발판은 팀 TeamExitZone 안이어야 한다(발판을 밟은 도형도 "전원 도착"에 든다).</summary>
    private static void ReportBridgeMissing(RainbowBridgeSwitch sw, PathChaserController chaser)
    {
        List<string> missing = new List<string>();
        if (sw == null) { Debug.LogError("[S1_Builder] 무지개다리 발판이 없다."); return; }
        if (sw.targetObjects == null || sw.targetObjects.Length == 0 || sw.targetObjects.Any(t => t == null)) missing.Add("targetObjects");
        else if (sw.targetObjects.Any(t => t.GetComponent<Collider>() == null || t.GetComponent<Collider>().enabled)) missing.Add("조각 콜라이더가 꺼져 있어야 함(실체화 전)");
        if (!sw.activatorRequiresHold) missing.Add("activatorRequiresHold(협동=true)");
        // [10-04] 다리는 한 판(조각 1개) — 위치·크기도 점검한다(Collider.bounds는 편집 모드에서 낡을 수 있어 Transform 값으로).
        if (sw.targetObjects != null && sw.targetObjects.Length != 1) missing.Add($"다리 조각 수 {sw.targetObjects.Length}(한 판 = 1)");
        else if (sw.targetObjects != null && sw.targetObjects[0] != null)
        {
            Transform d = sw.targetObjects[0].transform;
            Vector3 lc = g.InverseTransformPoint(d.position);
            Vector3 ls = d.lossyScale;
            bool sizeOk = Mathf.Abs(ls.x - BridgeWidth) < 0.01f && Mathf.Abs(ls.y - BridgeSegThickness) < 0.01f && Mathf.Abs(ls.z - BridgeLength) < 0.01f;
            bool placeOk = Mathf.Abs(lc.x - BridgeX) < 0.01f && Mathf.Abs(lc.z - (EdgeZ + BridgeLength / 2f)) < 0.01f && Mathf.Abs(lc.y + ls.y / 2f) < 0.01f; // 윗면 = 중심 y + 반두께 = 0
            if (!sizeOk || !placeOk)
                missing.Add($"다리 한 판 크기·위치(기대 x {BridgeX}·z {EdgeZ + BridgeLength / 2f} 중심, {BridgeWidth}×{BridgeSegThickness}×{BridgeLength}, 윗면 y 0 / 실제 중심 {lc}, 크기 {ls})");
        }
        // TeamExitZone(BuildExitZone: x ±PlatformHalfW, z PlatformNearZ~SectorLength)은 편집 모드에서 Collider.bounds가 낡을 수 있어 상수로 확인한다.
        bool padInZone = chaser.teamExitZone != null
            && Mathf.Abs(BridgeX) + BridgePadHalfSize <= PlatformHalfW
            && BridgePadZ - BridgePadHalfSize >= PlatformNearZ
            && BridgePadZ + BridgePadHalfSize <= SectorLength;
        if (!padInZone) missing.Add("발판이 TeamExitZone 안에 없음");
        if (missing.Count > 0)
            Debug.LogError("[S1_Builder] 무지개다리 점검 실패: " + string.Join(", ", missing));
    }

    private static void BuildSupportZone()
    {
        // 카트 전용 트리거 교량 — 절벽 구간에서만 카트 중력 상쇄(팀 CH1_RailCartSupportZone과 같은 구성,
        // 폭만 레일 ±6에 맞춰 20으로).
        GameObject support = NewChild("S1_RailCartSupportZone", L(0f, 1f, (EdgeZ + PlatformNearZ) / 2f));
        BoxCollider col = support.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(20f, 4f, PlatformNearZ - EdgeZ + 1f);
        support.AddComponent<RailCartSupportZone>();
    }

    /// <summary>[10-04 도착 구역 밑면 −1 — 우리 맵 결함 수정] 구역 = x ±15 · z 168~188 · <b>y −1~4</b>(옛 y 0~4). 팀 TeamExitZone은 도형의 transform.position 한 점이 구역 안인지로 본다
    /// (TeamExitZone.cs:46-50 ClosestPoint(position)==position, 콜라이더 아님). 세모는 바닥(y 0)에 서면 피벗이 local y −0.21이라 옛 구역(y ≥ 0) 밖으로 읽혀 "전원 도착"이 안 설 수
    /// 있었다(진단 S1-5). 팀도 b15c8f9에서 CH8 시작 구역 밑면을 바닥 −1로 내렸다. 잘못 세어질 우려 점검 [계산]: 구역 아래 1(y −1~0)은 도착 플랫폼 솔리드 블록(y −20~0, x ±15, z 168~188)
    /// 안이라 도형이 들어갈 수 없다(피벗 오프셋 −0.21만 닿음). 떨어진 도형은 절벽(z 140~168, 구역 z 밖 — 구역은 z ≥ 168)이나 플랫폼 바깥 허공으로 가므로 구역 밖이다. 절벽 장외 볼륨
    /// (y −7~−1, z 140~168)과는 z 168 면·y −1 선에서 닿기만 한다. 구역 높이 4(y 4까지)는 그대로라 위로는 같다. 발판(y 0~0.1)·파킹점·도착점은 구역 안이다.</summary>
    private static TeamExitZone BuildExitZone()
    {
        float centerZ = (PlatformNearZ + SectorLength) / 2f;
        GameObject go = NewChild("S1_TeamExitZone", L(0f, 1.5f, centerZ)); // 중심 y 1.5 · 높이 5 → y −1~4
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(PlatformHalfW * 2f, 5f, SectorLength - PlatformNearZ);
        return go.AddComponent<TeamExitZone>();
    }

    /// <summary>③ 출구 문 — 팀 doorPhysics(열림 = SetPadPressed(true), 위로 doorTargetYOffset만큼 이동).
    /// doorPhysics는 Rigidbody가 없으면 동작하지 않는다(09-22 그레이박스 "문 7개 안 열림" 원인) —
    /// kinematic Rigidbody를 반드시 붙인다. 문 높이 = 출구 벽 높이라 위로 문 높이+0.5 올라가면 통로가 완전히 열린다.</summary>
    private static doorPhysics BuildExitDoor()
    {
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "S1_ExitDoor";
        door.transform.SetParent(g, false);
        door.transform.position = L(0f, ExitWallH / 2f, SectorLength - ExitWallT / 2f);
        door.transform.localScale = new Vector3(ExitDoorHalfW * 2f, ExitWallH, ExitWallT);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/LaboratoryMap4/Materials/Generated/Map4_Door.mat");
        if (mat != null) door.GetComponent<Renderer>().sharedMaterial = mat;

        Rigidbody rb = door.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        doorPhysics dp = door.AddComponent<doorPhysics>();
        dp.doorTargetYOffset = ExitWallH + 0.5f;
        dp.doorSpeed = 2f;
        return dp;
    }

    /// <summary>절벽 낙하 — 팀 Lab_CH1과 같은 팀 OutOfBoundsVolume(Tools/Respawn/Create Respawn Scale).
    /// 킬 라인(y -30)까지 기다리지 않고 공용 체크포인트로 페이드 복귀시키는 팀 기본 동작 그대로다.
    /// 배치 규칙도 팀 Ch1LevelLayoutMenuItem.LayoutCore 6번과 같다(절벽 끝 높이 −4 중심, 높이 6, 바닥 폭 +4).</summary>
    private static void BuildCliffOutOfBounds()
    {
        // [수정 라운드 R2] 선택을 비운 뒤 생성하고, OutOfBoundsVolume이 있는 것만 가져온다(직전 선택을 잘못 옮기지 않게).
        Selection.activeGameObject = null;
        RespawnMenuItem.CreateRespawnScale();
        GameObject oob = Selection.activeGameObject;
        if (oob == null || oob.GetComponent<OutOfBoundsVolume>() == null)
            throw new System.Exception("[S1_Builder] 팀 장외 판정 볼륨 생성 실패.");
        Adopt(oob);
        oob.name = "S1_CliffOutOfBounds";
        oob.transform.SetPositionAndRotation(L(0f, -4f, (EdgeZ + PlatformNearZ) / 2f), g.rotation);
        oob.transform.localScale = new Vector3(SectorWidth + 4f, 6f, PlatformNearZ - EdgeZ);
        BoxCollider col = oob.GetComponent<BoxCollider>();
        col.center = Vector3.zero;
        col.size = Vector3.one;
    }

    // ───────────────────────── 추격자 ─────────────────────────

    /// <summary>경로(§11-2): 시작(스폰 뒤 z 1) → 복도 장애물 무리 7곳의 통로 가운데로 좌·우 꺾으며 전진(팀 추격자는 솔리드
    /// 콜라이더가 없어 장애물을 뚫고 지나가므로, 뚫는 모습이 보이지 않게 웨이포인트 선분이 장애물·벽 콜라이더 안을 지나지
    /// 않게 했다) → 복도 출구에서 투석기를 오른쪽으로 비켜 → 파괴벽 앞 대기(z 124 = 옛 68 + 56) → 벽 구멍 → 절벽 끝 종점.
    /// 팀 BuildRig가 만든 웨이포인트 3개(Start/WallStop/End)를 재사용하고 사이 지점만 더한다.
    /// 높이는 팀과 같이 바닥 윗면 + 1. [§9 구현 결정] 종점은 x −3(가운데 레일 0과 왼쪽 레일 −6 사이) —
    /// 팀 Lab_CH1은 가운데 레일 위였는데, 그러면 가운데 카트가 추격자를 반드시 관통한다.</summary>
    private static Transform LayoutChaser(PathChaserController chaser)
    {
        PathChaserAgent agent = chaser.agent;
        Transform start = agent.waypoints[0], wallStop = agent.waypoints[1], end = agent.waypoints[2];

        List<Vector2> route = new List<Vector2> { new Vector2(0f, 1f) }; // 0 시작(§11-1 z 1)
        route.AddRange(CorridorChaserRoute);                              // 복도 장애물 통로
        route.AddRange(CatapultBypassRoute);                              // 투석기 비켜 가기
        route.Add(new Vector2(0f, PartitionZ0 - 4f));                     // 파괴벽 앞 대기(§11-1 z 124)
        // [구현 결정 — 수정 라운드 S1검산1] 벽 구멍 통과점은 옛 (0, 76)+56 = (0, 132)였으나 가운데 카트 출발 위치(RailX 0,
        // CartStartZ 132)와 정확히 겹쳤다. 종점과 같은 x −3(가운데 레일 0과 왼쪽 레일 −6 사이)으로 옮긴다. [계산] 팀 카트 몸통은
        // 피벗 중심 폭 2.0 × 길이 3.0(RailCartMenuItem BodyWidth 0.8·BodyLength 1.2 × Scale 2.5, 벽판이 로컬 x·z 0 대칭)이라
        // 경로가 카트 몸통에서 ≥ 1.3, 파괴벽 구멍 모서리(x ±3, z 128~129)에서 ≥ 1.05 떨어진다(추격자 모습 반지름 1).
        route.Add(new Vector2(ChaserLaneX, PartitionZ1 + 3f));            // 벽 구멍 통과(옛 76 + 56, x만 −3)
        route.Add(new Vector2(ChaserLaneX, EdgeZ - 0.6f));                // 절벽 끝 종점(옛 83.4 + 56)
        int wallStopIndex = route.Count - 3;

        Transform[] wps = new Transform[route.Count];
        for (int i = 0; i < route.Count; i++)
        {
            Transform t = i == 0 ? start : i == wallStopIndex ? wallStop : i == route.Count - 1 ? end
                : new GameObject($"PathChaser_WP_S1_{i}").transform;
            Adopt(t.gameObject);
            t.position = L(route[i].x, 1f, route[i].y);
            wps[i] = t;
        }
        agent.waypoints = wps;
        agent.transform.position = wps[0].position;
        chaser.wallStopWaypointIndex = wallStopIndex;

        Adopt(chaser.gameObject);
        Adopt(agent.gameObject);
        Adopt(chaser.safePreCounter.gameObject);
        Adopt(chaser.safePostCounter.gameObject);
        EnsureChaserVisual(agent.transform);
        return end;
    }

    /// <summary>팀 Ch1LevelLayoutMenuItem.EnsureChaserVisual(private)과 같은 모습 — 붉은 구(지름 2, 콜라이더 없음).
    /// 팀 머티리얼 에셋이 있으면 읽기만 하고, 없으면 에셋을 만들지 않고 씬 내장 머티리얼을 쓴다(팀 폴더에 쓰지 않기 위해).</summary>
    private static void EnsureChaserVisual(Transform agent)
    {
        if (agent.Find("PathChaser_Visual") != null) return;
        GameObject v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        v.name = "PathChaser_Visual";
        Object.DestroyImmediate(v.GetComponent<Collider>());
        v.transform.SetParent(agent, false);
        v.transform.localScale = Vector3.one * 2f;
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/PathChaserSystem/PathChaserRed.mat");
        if (mat == null) mat = new Material(Shader.Find("Standard")) { color = new Color(0.85f, 0.15f, 0.15f) };
        v.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>안전점(팀 SectionSafePoint) 위치 + 보조 지점 2개(설계서 §6 "백업 지점 2개씩", §9 z ±2.5). 안전점은 "놓인 자리 = 착지 바닥"이라 y=0.</summary>
    private static void PlaceSafePoint(SectionHitCounter counter, Vector2 xz)
    {
        SectionSafePoint point = counter.destination;
        point.transform.position = L(xz.x, 0f, xz.y);
        Transform b0 = NewChild($"{point.name}_Backup0", L(xz.x, 0f, xz.y - 2.5f)).transform;
        Transform b1 = NewChild($"{point.name}_Backup1", L(xz.x, 0f, xz.y + 2.5f)).transform;
        point.backupPoints = new[] { b0, b1 };
    }

    /// <summary>BuildRig가 임시 RespawnController에 건 영구 배선을 걷어낸다 — 실제 연결은 실행 중
    /// Lab_SectionRespawnBridge가 Master의 RespawnController에 건다.</summary>
    private static void StripPersistentRespawnWiring(SectionHitCounter counter)
    {
        if (counter == null || counter.OnThresholdReached == null) return;
        for (int i = counter.OnThresholdReached.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(counter.OnThresholdReached, i);
    }

    /// <summary>팀 Ch1TestRoomMenuItem.ReportMissing과 같은 점검 — 비면 팀 컨트롤러가 플레이 시작을 거부한다.</summary>
    private static void ReportMissing(PathChaserController c, WindupActivationPad pad)
    {
        List<string> missing = new List<string>();
        if (c.agent == null || c.agent.waypoints == null || c.agent.waypoints.Any(w => w == null)) missing.Add("agent/waypoints");
        if (c.wall == null) missing.Add("wall");
        if (c.carts == null || c.carts.Length != RailX.Length || c.carts.Any(x => x == null || x.axle == null || x.path == null || x.activationPad != pad))
            missing.Add($"carts({RailX.Length}대, axle/path/activationPad)");
        if (c.arrivalPoint == null) missing.Add("arrivalPoint");
        if (c.teamExitZone == null) missing.Add("teamExitZone");
        if (c.safePreCounter == null || c.safePreCounter.destination == null) missing.Add("safePreCounter");
        if (c.safePostCounter == null || c.safePostCounter.destination == null) missing.Add("safePostCounter");
        if (missing.Count > 0)
            Debug.LogError("[S1_Builder] 추격 컨트롤러 참조가 비었다 — 플레이 시 시작 거부된다: " + string.Join(", ", missing));
    }

    /// <summary>팀 생성 함수는 씬 루트에 오브젝트를 만든다. 전부 Generated 아래로 옮겼는지 확인한다 — 루트에
    /// 남으면 Generated 재생성 때 지워지지 않아 다음 Generate마다 중복된다(재생성 멱등성 위반).</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"[S1_Builder] 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static Vector3 L(float x, float y, float z) => g.TransformPoint(new Vector3(x, y, z));

    /// <summary>Map4Build.Floor/Wall이 돌려준 상자에 S1 이름을 붙인다(지시서 C4 — Map4Build 수정 없이 반환 Transform의
    /// name만 바꾼다). 시각물 자식 "Visual"도 VIS_ 접두사로 바꾼다. 생성이 거부돼 null이면 즉시 중단(부분 지형 방지).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"[S1_Builder] 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        t.name = name;
        Transform vis = t.Find("Visual");
        if (vis != null) vis.name = "VIS_" + name.Substring("GEO_".Length);
        return t;
    }

    private static GameObject Adopt(GameObject go)
    {
        if (go != null) go.transform.SetParent(g, true);
        return go;
    }

    private static GameObject NewChild(string name, Vector3 worldPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(g, false);
        go.transform.position = worldPos;
        return go;
    }

    // 팀 생성 메뉴 중 private인 것은 팀 Ch1TestRoomMenuItem.Menu&lt;T&gt;와 같이 메뉴 경로로 실행한다(생성물이 선택된다).
    private static T MenuCreate<T>(string path) where T : Component
    {
        Selection.activeGameObject = null;
        if (!EditorApplication.ExecuteMenuItem(path) || Selection.activeGameObject == null)
        {
            Debug.LogError($"[S1_Builder] 메뉴 실행 실패: {path}");
            return null;
        }
        GameObject go = Adopt(Selection.activeGameObject);
        T c = go.GetComponent<T>();
        if (c == null) Debug.LogError($"[S1_Builder] '{path}'가 만든 오브젝트에 {typeof(T).Name}이 없다.");
        return c;
    }

    private static void Place(Transform t, Vector2 xz, float y) =>
        t.SetPositionAndRotation(L(xz.x, y, xz.y), g.rotation);

    /// <summary>팀 Ch1LevelLayoutMenuItem.PlaceOnGround와 같은 규칙 — 보이는 메시의 가장 낮은 점이 바닥 윗면(y=0) +
    /// clearance에 오게 한다. 바닥이 평평해 레이 대신 y=0을 쓴다.</summary>
    private static void PlaceOnGround(GameObject go, float x, float z, float clearance)
    {
        go.transform.position = L(x, 0f, z);
        Renderer[] rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        float groundY = L(x, 0f, z).y;
        go.transform.position += Vector3.up * (groundY + clearance - b.min.y);
    }
}
#endif
