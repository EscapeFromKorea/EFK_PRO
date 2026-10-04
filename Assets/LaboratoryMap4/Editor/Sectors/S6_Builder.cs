#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 섹터6 "포탈(큰 방 하나를 포탈로만 오르기)" 빌더 — 과제 S6-B4(R3 스테이징; S6-B3 = 1차 S6-B의 2차 반영 위에 C15·C16). 설계/S6_설계.md(§3 결정표,
/// 09-28 확정)와 진행/초안/S6_배치초안.md(S6-D **2차 반영(S6-D3) + 2차 수정 1** — §2-7·§3-1·§3-2·§3-4가 좌표 정본)의 좌표를
/// 섹터 씬 Generated/ 아래에 짓는다. S6_배치초안.json은 S6-D4 이관판[C8]으로 md와 차 0이다(검산 S6_R3 S6R3-81·82·87 — 상자 46·앵커 16, 최대 차 0.0000).
/// R3(S6-B4): [C15 재판정 (가)] P4 현재 배치 유지(필요 피치 최대 53.64° ≤55° — 팀 허용 ±0.6 모델, 추정·실측 전)·[C16] 태그 반영.
/// 수정 M3R1: WestInnerX만 −31.2 → −31.6(벽감 패널 뒤 빈 공간 1.0 → 1.4, 스윕 세모 PENETRATING 2건) [제안 M3R1 — 초안 이관 전·판정 대기].
/// Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry(통합 때 K-INT가 case 6 추가)를 거쳐 부르고, 연결 통로(z 98~110,
/// 윗면 16)·마커(Manual/Markers)는 공용 BuildConnectorAndMarkers가 이어서 만든다 — 그 구역에는 Floor를 만들지 않는다.
///
/// [범위 — 계약 K6-1] 지형만: 전실·입구, 방 바닥(패널 구덩이 3곳 + 구덩이 디딤 3 [판정 5]), 외벽·벽감·천장, 발판1(y5)·
/// 발판2(y10 [판정 5])·우물 벽, 출구 층(선반·발사 홈), 출구 문턱·출구 통로(윗면 16), 앵커(ANCH_S6_…).
/// 마지막 단계는 우물 낙하 → P4 → 홈 속 눕힌 M2 → 솟구치기 [사용자 09-29 가](K-AS 실측 성립 전제). **경사로·계단·TEMP_Ramp 없음** — 상승은 포탈로만
/// (Map4Build.Ramp/Stairs 미사용). 출구 문 없음(설계에 결정 없음, S6-W 지시서 "출구 층 도착 후 문 없음").
/// 팀 기믹(포탈 패널·움직이는 패널·레버·에너지볼·복귀 구역)은 만들지 않는다 — S6_Wiring.Wire(W). 패널 자리에는 콜라이더를
/// 두지 않고(계약 K6-1) 패널 뒤·밑 빈 공간만 남긴다. 조명·재질은 S6_Dress.Apply(L). 여기서는 Map4Palette 이름(Floor·Wall)만 쓴다.
///
/// [호출 순서 — 계약 K0-2] 1) 전제 검사 → 2) Map4Build.BeginSection → 3) 지형·앵커, refs 채움 → 4) S6_Wiring.Wire →
/// 5) S6_Dress.Apply → 6) true. 전제 불일치면 아무것도 만들지 않고 false. 3)에서 예외가 나면 Generated를 비우고 false
/// (부분 생성 0). 4)·5)가 예외를 내도 지형은 지우지 않는다.
///
/// [좌표] 섹터 로컬(+Z 진행, x=0 중앙, y=0 = 섹터 바닥 = 세계 18). 모든 좌표·크기는 아래 "상수 표" 한 곳에 있다 —
/// 판정이 바뀌면 여기만 고치면 된다. 출처 표기: [확정]=설계서 확정 · [팀]=팀 코드 · [제안]=초안이 정한 값 · [계산] ·
/// [추정] · [판정 n]=진행/판정/2026-09-28_S6S8_판정.md · [사용자 09-29 가] · [1차 판정 n] · [계약 Kx]=진행/지시서/R3/_계약.md(R3 정본 — K 절은 R2S68 조항 번호·표기를 이어받음, R3-0)
/// 조항 · [C15]·[C16]=진행/판정/2026-09-29_S6S8_2차_판정.md(R3 계약 K6-0 개정) · [C15 재판정]=진행/판정/2026-09-29_C15_재판정.md · [해석]. 상자 이름·앵커 이름은 초안 §3-1(상자 46개)·§3-2(앵커 16개)와 같다.
/// </summary>
public static class S6_Builder
{
    // ═══════════════════════════════ 상수 표 (좌표·크기는 여기 한 곳) ═══════════════════════════════

    // ── 전제 [계약 K0-2] SectorSource·Map4Layout.asset 현재값. 비교는 Mathf.Approximately ──
    private const float SectorWidth = 72f;          // [계약 K0-2] 폭 72
    private const float SectorLength = 98f;         // [계약 K0-2] 길이 98 — 출구 z 98
    private const float FloorHeight = 18f;          // [계약 K0-2] 바닥 높이 18(세계)
    private const float ExitHeight = 34f;           // [계약 K0-2] 출구 높이 34(세계) [확정 설계 §3 절대 34]
    private const float Rise = ExitHeight - FloorHeight; // [계산] 16 — 출구 층·출구 윗면 로컬 y (초안 meta.rise)

    // ── 방 틀 (초안 §2-1) ──
    private const float RoomHalfW = 30f;            // [확정 약 60] 방 안쪽 x −30~30
    private const float OuterHalfW = 32f;           // [제안] 외벽 두께 2(x ±30~±32)
    private const float ShellZ0 = 2f;               // [제안] 입구 벽 바깥 면 z2(= 전실 끝)
    private const float RoomZ0 = 4f;                // [제안] 방 안쪽 남면 — 입구 벽 z2~4
    private const float RoomZ1 = 94f;               // [제안] 방 안쪽 북면 — 출구 벽 z94~96 (방 안 길이 90 [확정 약 90])
    private const float ShellZ1 = 96f;              // [제안] 출구 벽 바깥 면 z96
    private const float CeilY = 24f;                // [확정 약 24] 천장 아랫면
    private const float CeilT = 1f;                 // [제안] 천장 두께 y24~25
    private const float FloorT = 1.5f;              // [제안] 바닥판 두께 1.5 — 바닥 패널 밑 구덩이(1.2)를 파기 위해
    private const float OpeningHalfW = 4f;          // [계약 K0-2] 입구·출구 x −4~4
    private const float OpeningH = 4f;              // [제안] 입구·출구 높이 4
    private const float VestibuleHalfW = 6f;        // [제안] 전실 폭 12(x −6~6, z0~2), 옆벽 x ±4~±6
    private const float WestInnerX = -31.6f;        // [제안 M3R1 — 초안 이관 전] 왼벽 안쪽 판 x −31.6~−30, 뒤판 x −32~−31.6(0.4). 초안 −31.2(패널 뒤 1.0)는 세모(수평 폭 최대 1.208, 요 전체 [계산])가
                                                    // 벽감에 끼어 스윕 PENETRATING 2건(SWEEP_20260929_154941) → 패널 뒤 1.4(≥1.0 권고 S6_팀API §0-3 유지, 세모 여유 0.19)
    private const float ExitPassageT = 1f;          // [제안] 출구 통로 바닥 두께 y15~16
    private const float ExitPassageWallT = 1f;      // [제안] 출구 통로 옆벽 x ±4~±5

    // ── 포탈 패널 (W가 팀 메뉴로 만든다 — 여기서는 자리 비우기·앵커만) ──
    // [계약 K6-0 결합 상수] PanelHalfW/H·OpeningH·CeilY는 W `FixedPanelSize`·`MovablePanelSize`·`MovableSpecs[1].pivotPointLocal.y`(= −PanelHalfH),
    // L `FallbackPanelSize`·`FallbackExitOpeningHeight`(= OpeningH)·`FallbackCeilingY`(= CeilY)와 짝이다 — 하나를 바꾸면 모두 바꾸고 초안 검산을 다시 돌린다.
    private const float PanelHalfW = 1.5f;          // [제안·판정 5 수용] 패널 3.0 × 3.6 (초안 meta.panelSize, 팀 기본 2.4×3.0)
    private const float PanelHalfH = 1.8f;          // [제안·판정 5 수용]
    private const float PanelT = 0.2f;              // [팀] 패널 두께 0.2 (S6_팀API, 초안 meta.panelSize)
    private const float PitClear = 1.0f;            // [제안] 패널 밑면 ↔ 구덩이 바닥 1.0 (S6_팀API §0-3 [추정] 빈 공간 ≥1.0) → 구덩이 바닥 = 면 −1.2
    // [판정 5] 패널 구덩이 디딤(초안 §2-7): 윗면 = 패널 밑면(면 − PanelT) → 구덩이 바닥 → 디딤 턱 PitClear(1.0) → 둘레 윗면 PanelT(0.2).
    // 디딤 깊이 = 디딤 규격 최소값(Map4Build.LedgeMinTreadDepth 1.5, 결정5). P3 디딤만 발판1 북쪽 끝(Pad1Z1)까지 늘린다(남는 조각 1.2 < 1.5).
    private const float PitStepDepth = Map4Build.LedgeMinTreadDepth; // [확정 결정5] 1.5 — 새 수치 아님
    private static readonly Vector3 P1 = new Vector3(-10f, 0f, 24f);    // [제안] ANCH_S6_Panel_1 — 1단계 입구 바닥 패널 면 중심
    private static readonly Vector3 P2 = new Vector3(-30f, 7.8f, 34f);  // [제안] ANCH_S6_Panel_2 — 1단계 출구 왼벽(벽감) 패널 면 중심
    private static readonly Vector3 P3 = new Vector3(-26f, 5f, 41.5f);  // [제안] ANCH_S6_Panel_3 — 2단계 입구 발판1 바닥 패널 면 중심
    private static readonly Vector3 P4 = new Vector3(-20f, 0f, 65f);    // [제안] ANCH_S6_Panel_4 — 3단계 입구 우물 바닥 패널 면 중심
    // [C15 재판정 (가)] 현재 배치 유지 — 팀 CheckWithinEdges 허용 ±0.6 사각형으로 다시 계산하면 P4 조준 자리 972곳 모두 필요 피치 41.76~53.64° ≤55°
    // ([추정] 카메라 모델, 실측 전 — 사람 플레이 확인 항목). 옛 '44.7~59.9° 불수용'은 검산 모델이 허용 범위를 좁게 잡은 값이다.

    // ── 움직이는 패널 M1 경첩 [판정 2 — 동선분석 제안 D] ──
    // W 상수 MovableSpecs[0].pivotPointLocal (6.5,0,0)과 짝(계약 확인 요청 3 — Pivot 값은 W 상수). B는 앵커만 만든다.
    private static readonly Vector3 M1Start = new Vector3(-4.6f, 12.9f, 51.4f); // [제안] ANCH_S6_MovPanel_1 — 시작 포즈 면 중심(−X를 향함, 불변)
    private static readonly Vector3 M1Hinge = new Vector3(-4.5f, 12.9f, 57.9f); // [판정 2] 경첩(월드 +Y 축) — 수정 2: z56.9
    private const float M1Radius = 6.5f;            // [판정 2] 경첩 ↔ 루트(= 면 − forward×PanelT/2) 거리 — 수정 2: 5.5
    // 끝 포즈 = 경첩에서 −X로 반지름, forward +Z → 면 중심 = (경첩.x − R, 경첩.y, 경첩.z + PanelT/2) = (−11, 12.9, 58.0) [계산 — 팀 Pivot 식]
    private static readonly Vector3 M1End = new Vector3(M1Hinge.x - M1Radius, M1Hinge.y, M1Hinge.z + PanelT / 2f);

    // ── 발판·우물 (초안 §2-3~§2-5) ──
    private const float Pad1Top = 5f;               // [제안] 발판1 윗면 y5 = 절대 23 (설계 '약 23' [제안 설계 §3-3])
    private const float Pad1X0 = -30f, Pad1X1 = -22f, Pad1Z0 = 30f, Pad1Z1 = 46f; // [제안] 발판1 8×16, 왼벽에 붙음(틈 0)
    private const float Pad2Top = 10f;              // [제안·판정 5 수용] 발판2 윗면 y10 = 절대 28 (설계 '약 29', 초안 §9-3)
    private const float Pad2X0 = -16f, Pad2X1 = -2f, Pad2Z0 = 58f, Pad2Z1 = 70f;  // [제안] 발판2 14×12, 바닥까지 채움
    private const float WellX0 = -25f, WellInX0 = -24f; // [제안] 우물 서벽 x −25~−24, 안쪽 x −24~−16(동쪽 = 발판2 서쪽 면)
    private const float WellZ0 = 60f, WellZ1 = 70f; // [제안] 우물 벽 z60~70 — 남벽 z60~61·북벽 z69~70 (초안 §9-4)
    private const float WellWallT = 1f;             // [제안] 우물 벽 두께 1

    // ── 출구 층 (초안 §2-5) — 선반 윗면 = Rise ──
    private const float ShelfHalfW = 12f;           // [제안] 출구 층 x −12~12
    private const float NotchHalfW = 2.5f;          // [제안] 발사 홈 x −2.5~2.5
    private const float ShelfZ0 = 82f;              // [제안] 출구 층 앞면 z82 (뒤는 RoomZ1 = 94)
    private const float NotchBackZ = 88.2f;         // [제안] 홈 뒷벽 z88.2 (수정 1: 88.1→88.2, M2 회전 틈 1.0986)
    private const float NotchFloorY = 4.9f;         // [제안] 홈 바닥 y4.9 (M2 눕힘 밑면 5.9와 틈 1.0)

    // ── 복귀 구역 [판정 4 — 계약 K6-1 (가)] RespawnZone 3개(시작 1 + 체크 2)의 부피 그대로(초안 §3-4·§4).
    //    RespawnZone 루트 = 구역 아랫면 중심 = 앵커(Start·Check_1·Check_2) — 생성 뒤 ±0.02로 대조(CheckZoneAnchors) ──
    private const float CheckZoneH = 3f;            // [제안] 구역 높이 3(시작·체크 공통)
    private static readonly Vector3 StartZoneMin = new Vector3(-6f, 0f, 5f);                     // [제안] 시작 구역 (−6,0,5)~(6,3,11) — 초안 §3-4
    private static readonly Vector3 StartZoneMax = new Vector3(6f, CheckZoneH, 11f);
    private static readonly Vector3 StartZoneSize = StartZoneMax - StartZoneMin;                // [판정 4] refs.startZoneSize = (12,3,6), center = (0, size.y/2, 0)
    private static readonly Vector3 CheckZone1Min = new Vector3(-29f, Pad1Top, 31f);            // [제안] checkpointPadBounds[0] (발판1 남쪽, 가장자리 1.0 들임)
    private static readonly Vector3 CheckZone1Max = new Vector3(-23f, Pad1Top + CheckZoneH, 37f);
    private static readonly Vector3 CheckZone2Min = new Vector3(-15f, Pad2Top, 58.5f);          // [제안] checkpointPadBounds[1] (발판2, 앞 0.5·나머지 1.0 들임)
    private static readonly Vector3 CheckZone2Max = new Vector3(-3f, Pad2Top + CheckZoneH, 69f);

    // ── 상자 표 (초안 §3-1, min/max 섹터 로컬) ──
    // 종류: Floor → Map4Build.Floor(걷는 윗면·바닥까지 채운 발판·선반·구덩이 바닥 — 겹침 검사 대상, 팔레트 "Floor")
    //       Wall  → Map4Build.Wall(벽·천장·문턱·우물 벽, 팔레트 "Wall")
    //       WallOpen → Map4Build.WallWithOpenings(구멍 1개, 조각 2개 = 긴 축 낮은 쪽 이름·높은 쪽 이름)
    private static readonly BoxSpec[] Boxes =
    {
        // 전실·바닥 (#1~#8) — 윗면 y0 [확정 섹터 바닥]. P1·P4 구멍은 패널 크기로 자른다(구멍 = 패널 면 ±PanelHalfW/±PanelHalfH).
        F("GEO_S6_Floor_Vestibule", V(-VestibuleHalfW, -FloorT, 0f), V(VestibuleHalfW, 0f, ShellZ0)),                                        // #1 [확정] 입구 z0·x−4~4 윗면 0 · [제안] 전실
        F("GEO_S6_Floor_Room_1", V(-OuterHalfW, -FloorT, ShellZ0), V(P4.x - PanelHalfW, 0f, P4.z - PanelHalfH)),                              // #2 [제안] x −32~−21.5 · [판정 5] z2~63.2(수정 2: ~96, 나머지는 #44·#45)
        F("GEO_S6_Floor_Room_2", V(P4.x - PanelHalfW, -FloorT, ShellZ0), V(P4.x + PanelHalfW, 0f, P4.z - PanelHalfH)),                        // #3 [제안] P4 구멍 남쪽 z2~63.2
        F("GEO_S6_Floor_Room_3", V(P4.x - PanelHalfW, -FloorT, P4.z + PanelHalfH), V(P4.x + PanelHalfW, 0f, ShellZ1)),                        // #4 [제안] P4 구멍 북쪽 z66.8~96
        F("GEO_S6_Floor_Room_4", V(P4.x + PanelHalfW, -FloorT, ShellZ0), V(P1.x - PanelHalfW, 0f, ShellZ1)),                                  // #5 [제안] x −18.5~−11.5
        F("GEO_S6_Floor_Room_5", V(P1.x - PanelHalfW, -FloorT, ShellZ0), V(P1.x + PanelHalfW, 0f, P1.z - PanelHalfH - PitStepDepth)),         // #6 [제안] P1 구멍 남쪽 · [판정 5] z2~20.7(수정 2: ~22.2, P1 디딤 자리 #43)
        F("GEO_S6_Floor_Room_6", V(P1.x - PanelHalfW, -FloorT, P1.z + PanelHalfH), V(P1.x + PanelHalfW, 0f, ShellZ1)),                        // #7 [제안] P1 구멍 북쪽 z25.8~96
        F("GEO_S6_Floor_Room_7", V(P1.x + PanelHalfW, -FloorT, ShellZ0), V(OuterHalfW, 0f, ShellZ1)),                                         // #8 [제안] x −8.5~32
        // 구덩이 바닥 (#9·#10) — 패널 밑면(−0.2)과 틈 1.0 → 윗면 −1.2
        F("GEO_S6_Pit_P1_Bottom", V(P1.x - PanelHalfW, -FloorT, P1.z - PanelHalfH), V(P1.x + PanelHalfW, P1.y - PanelT - PitClear, P1.z + PanelHalfH)), // #9 [제안]
        F("GEO_S6_Pit_P4_Bottom", V(P4.x - PanelHalfW, -FloorT, P4.z - PanelHalfH), V(P4.x + PanelHalfW, P4.y - PanelT - PitClear, P4.z + PanelHalfH)), // #10 [제안]
        // 외벽·벽감 (#11~#16) — 포탈 불가 벽
        W("GEO_S6_Wall_West_Back", V(-OuterHalfW, 0f, ShellZ0), V(WestInnerX, CeilY, ShellZ1)),                                                // #11 [제안] 외벽 두께 2(벽감 뒤 0.4 [제안 M3R1])
        WO("GEO_S6_Wall_West_A", "GEO_S6_Wall_West_B", V(WestInnerX, 0f, ShellZ0), V(-RoomHalfW, CeilY, ShellZ1),
            P2.z - PanelHalfW, P2.z + PanelHalfW),                                                                                             // #12·#13 [제안] 왼벽 안쪽 판, 벽감 z32.5~35.5
        W("GEO_S6_Wall_West_NicheBelow", V(WestInnerX, 0f, P2.z - PanelHalfW), V(-RoomHalfW, P2.y - PanelHalfH, P2.z + PanelHalfW)),           // #14 [제안] 벽감 아래 y0~6
        W("GEO_S6_Wall_West_NicheAbove", V(WestInnerX, P2.y + PanelHalfH, P2.z - PanelHalfW), V(-RoomHalfW, CeilY, P2.z + PanelHalfW)),        // #15 [제안] 벽감 위 y9.6~24
        W("GEO_S6_Wall_East", V(RoomHalfW, 0f, ShellZ0), V(OuterHalfW, CeilY, ShellZ1)),                                                       // #16 [제안] 오른벽 두께 2
        // 입구 벽·전실 옆벽 (#17~#21)
        WO("GEO_S6_Wall_South_W", "GEO_S6_Wall_South_E", V(-RoomHalfW, 0f, ShellZ0), V(RoomHalfW, CeilY, RoomZ0),
            -OpeningHalfW, OpeningHalfW),                                                                                                      // #17·#18 [확정] 입구 x −4~4
        W("GEO_S6_Wall_South_Lintel", V(-OpeningHalfW, OpeningH, ShellZ0), V(OpeningHalfW, CeilY, RoomZ0)),                                    // #19 [제안] 입구 높이 4
        W("GEO_S6_Wall_Vestibule_W", V(-VestibuleHalfW, 0f, 0f), V(-OpeningHalfW, OpeningH, ShellZ0)),                                         // #20 [제안] 전실 옆벽
        W("GEO_S6_Wall_Vestibule_E", V(OpeningHalfW, 0f, 0f), V(VestibuleHalfW, OpeningH, ShellZ0)),                                           // #21 [제안]
        // 출구 벽 (#22~#25)
        WO("GEO_S6_Wall_North_W", "GEO_S6_Wall_North_E", V(-RoomHalfW, 0f, RoomZ1), V(RoomHalfW, CeilY, ShellZ1),
            -OpeningHalfW, OpeningHalfW),                                                                                                      // #22·#23 [확정] 출구 x −4~4
        W("GEO_S6_Wall_North_Sill", V(-OpeningHalfW, 0f, RoomZ1), V(OpeningHalfW, Rise, ShellZ1)),                                             // #24 [확정] 출구 윗면 y16(턱 0) — 문턱 겸 바닥
        W("GEO_S6_Wall_North_Lintel", V(-OpeningHalfW, Rise + OpeningH, RoomZ1), V(OpeningHalfW, CeilY, ShellZ1)),                             // #25 [제안] 출구 높이 4
        // 천장 (#26)
        W("GEO_S6_Ceiling", V(-OuterHalfW, CeilY, ShellZ0), V(OuterHalfW, CeilY + CeilT, ShellZ1)),                                            // #26 [확정 약 24]
        // 출구 통로 (#27~#29) — z96~98, 윗면 16 → z98 공용 연결 통로(윗면 16)와 턱 0·틈 0
        F("GEO_S6_Floor_ExitPassage", V(-OpeningHalfW - ExitPassageWallT, Rise - ExitPassageT, ShellZ1), V(OpeningHalfW + ExitPassageWallT, Rise, SectorLength)), // #27 [확정] 출구 z98 윗면 16
        W("GEO_S6_Wall_ExitPassage_W", V(-OpeningHalfW - ExitPassageWallT, Rise, ShellZ1), V(-OpeningHalfW, Rise + OpeningH, SectorLength)),   // #28 [제안]
        W("GEO_S6_Wall_ExitPassage_E", V(OpeningHalfW, Rise, ShellZ1), V(OpeningHalfW + ExitPassageWallT, Rise + OpeningH, SectorLength)),     // #29 [제안]
        // 발판1 (#30~#34) — 바닥까지 채움, 윗면 5. P3 구멍(패널 크기) 밑은 채움 윗면 3.8 = 구덩이 바닥. 북쪽은 디딤(#34)
        F("GEO_S6_Pad1_Base", V(Pad1X0, 0f, Pad1Z0), V(Pad1X1, P3.y - PanelT - PitClear, Pad1Z1)),                                             // #30 [제안] 윗면 3.8
        F("GEO_S6_Pad1_Top_W", V(Pad1X0, P3.y - PanelT - PitClear, Pad1Z0), V(P3.x - PanelHalfW, Pad1Top, Pad1Z1)),                            // #31 [제안] 발판1 윗면 y5
        F("GEO_S6_Pad1_Top_E", V(P3.x + PanelHalfW, P3.y - PanelT - PitClear, Pad1Z0), V(Pad1X1, Pad1Top, Pad1Z1)),                            // #32 [제안]
        F("GEO_S6_Pad1_Top_S", V(P3.x - PanelHalfW, P3.y - PanelT - PitClear, Pad1Z0), V(P3.x + PanelHalfW, Pad1Top, P3.z - PanelHalfH)),      // #33 [제안]
        F("GEO_S6_Pit_P3_Step", V(P3.x - PanelHalfW, P3.y - PanelT - PitClear, P3.z + PanelHalfH), V(P3.x + PanelHalfW, P3.y - PanelT, Pad1Z1)), // #34 [판정 5] P3 디딤 북쪽 3.0×2.7, 윗면 4.8(옛 Pad1_Top_N 자리 — 삭제 [C15 추인])
        // 발판2 (#35) — 바닥까지 채움, 윗면 10
        F("GEO_S6_Pad2", V(Pad2X0, 0f, Pad2Z0), V(Pad2X1, Pad2Top, Pad2Z1)),                                                                   // #35 [제안·판정 5 수용] 윗면 y10(절대 28)
        // 우물 벽 (#36~#38) — 윗면 = 발판2 윗면, 남·북 벽은 발판2 서쪽 면에 맞닿음(틈 0)
        W("GEO_S6_Well_W", V(WellX0, 0f, WellZ0), V(WellInX0, Pad2Top, WellZ1)),                                                               // #36 [제안]
        W("GEO_S6_Well_S", V(WellInX0, 0f, WellZ0), V(Pad2X0, Pad2Top, WellZ0 + WellWallT)),                                                   // #37 [제안] z60~61
        W("GEO_S6_Well_N", V(WellInX0, 0f, WellZ1 - WellWallT), V(Pad2X0, Pad2Top, WellZ1)),                                                   // #38 [제안] z69~70
        // 출구 층 (#39~#42) — 윗면 16, 가운데 발사 홈(앞이 방 쪽으로 열림)
        F("GEO_S6_Shelf_W", V(-ShelfHalfW, 0f, ShelfZ0), V(-NotchHalfW, Rise, RoomZ1)),                                                        // #39 [확정] 윗면 16 · [제안] x −12~−2.5
        F("GEO_S6_Shelf_E", V(NotchHalfW, 0f, ShelfZ0), V(ShelfHalfW, Rise, RoomZ1)),                                                          // #40 [확정] 윗면 16 · [제안] x 2.5~12
        F("GEO_S6_Shelf_Back", V(-NotchHalfW, 0f, NotchBackZ), V(NotchHalfW, Rise, RoomZ1)),                                                   // #41 [확정] 윗면 16 · [제안] 홈 뒷벽 z88.2
        F("GEO_S6_Notch_Floor", V(-NotchHalfW, 0f, ShelfZ0), V(NotchHalfW, NotchFloorY, NotchBackZ)),                                          // #42 [제안] 홈 바닥 y4.9
        // 구덩이 디딤·바닥판 조각 (#43~#46) [판정 5, 초안 §2-7 · C15 추인 — 옆 디딤 방식] — 디딤 윗면 = 패널 밑면(면 − PanelT), 구덩이 옆면을 막는다
        F("GEO_S6_Pit_P1_Step", V(P1.x - PanelHalfW, -FloorT, P1.z - PanelHalfH - PitStepDepth), V(P1.x + PanelHalfW, P1.y - PanelT, P1.z - PanelHalfH)), // #43 P1 디딤 남쪽 3.0×1.5, 윗면 −0.2
        F("GEO_S6_Floor_Room_8", V(-OuterHalfW, -FloorT, P4.z - PanelHalfH), V(P4.x - PanelHalfW - PitStepDepth, 0f, P4.z + PanelHalfH)),     // #44 P4 디딤 서쪽 바닥판 x −32~−23, z63.2~66.8
        F("GEO_S6_Floor_Room_9", V(-OuterHalfW, -FloorT, P4.z + PanelHalfH), V(P4.x - PanelHalfW, 0f, ShellZ1)),                              // #45 P4 디딤 북쪽 바닥판 x −32~−21.5, z66.8~96
        F("GEO_S6_Pit_P4_Step", V(P4.x - PanelHalfW - PitStepDepth, -FloorT, P4.z - PanelHalfH), V(P4.x - PanelHalfW, P4.y - PanelT, P4.z + PanelHalfH)), // #46 P4 디딤 서쪽 1.5×3.6, 윗면 −0.2
    };

    // ── 앵커 표 (초안 §3-2) — 위치 = 섹터 로컬, forward, up. 패널 앵커 위치 = 면 중심, forward = 면 바깥 법선 ──
    // 바닥을 향한 패널(forward +Y)은 up = +Z(팀 SurfaceRotationFromNormal과 같음 — 초안 §3-2 머리말). 나머지 up = +Y [계약 K0-3].
    private static readonly AnchorSpec[] Anchors =
    {
        A("ANCH_S6_Start", V(0f, 0f, 8f), V(0f, 0f, 1f), V(0f, 1f, 0f)),                       // [제안·판정 4] 시작 RespawnZone 루트 = 구역 아랫면 중심(바닥 윗면 y0) ↔ StartZone
        A("ANCH_S6_Check_1", V(-26f, Pad1Top, 34f), V(0f, 0f, 1f), V(0f, 1f, 0f)),             // [제안·판정 4] 체크1 RespawnZone 루트 = 구역 아랫면 중심(발판1 남쪽 윗면 y5) ↔ CheckZone1
        A("ANCH_S6_Check_2", V(-9f, Pad2Top, 63.75f), V(0f, 0f, 1f), V(0f, 1f, 0f)),           // [제안·판정 4·5] 체크2 RespawnZone 루트 = 구역 아랫면 중심(발판2 윗면 y10 = 절대 28) ↔ CheckZone2
        A("ANCH_S6_ExitLanding", V(0f, Rise, 91f), V(0f, 0f, 1f), V(0f, 1f, 0f)),              // [제안] 출구 층 도착 발판 윗면(홈 뒤) y16
        A("ANCH_S6_Panel_1", P1, V(0f, 1f, 0f), V(0f, 0f, 1f)),                                // [제안] P1 1단계 입구(주황) 바닥 패널
        A("ANCH_S6_Panel_2", P2, V(1f, 0f, 0f), V(0f, 1f, 0f)),                                // [제안] P2 1단계 출구(파랑) 왼벽 벽감 패널
        A("ANCH_S6_Panel_3", P3, V(0f, 1f, 0f), V(0f, 0f, 1f)),                                // [제안] P3 2단계 입구(주황) 발판1 바닥 패널
        A("ANCH_S6_Panel_4", P4, V(0f, 1f, 0f), V(0f, 0f, 1f)),                                // [제안] P4 3단계 입구(주황) 우물 바닥 패널
        A("ANCH_S6_MovPanel_1", M1Start, V(-1f, 0f, 0f), V(0f, 1f, 0f)),                        // [제안] M1 시작 포즈(−X를 향함, 공중) — 불변
        A("ANCH_S6_MovPanelEnd_1", M1End, V(0f, 0f, 1f), V(0f, 1f, 0f)),                        // [계산·판정 2] M1 끝 포즈 (−11,12.9,58)(팀 Pivot 식, +Z) — 끝 패널 z57.8~58.0: 발판2 앞면 z58과 맞닿음, 윗면 10과 세로 1.1 (수정 2: (−10,12.9,57))
        A("ANCH_S6_MovPanel_2", V(0f, 7.8f, 83.4f), V(0f, 0f, -1f), V(0f, 1f, 0f)),            // [제안] M2 시작 포즈(세움, 방 쪽 −Z)
        A("ANCH_S6_MovPanelEnd_2", V(0f, 6.1f, 85.3f), V(0f, 1f, 0f), V(0f, 0f, 1f)),          // [계산] M2 끝 포즈(팀 Pivot 식, 눕힘 +Y)
        A("ANCH_S6_Lever_1", V(-24f, Pad1Top, 38f), V(0f, 0f, 1f), V(0f, 1f, 0f)),             // [제안·판정 3] L1 발판1 윗면(기둥 밑동), P3 남쪽 — 기둥 (−24.2,5,37.8)~(−23.8,6,38.2)는 W가 만든다 (수정 2: z45)
        A("ANCH_S6_Lever_2", V(-4f, Pad2Top, 67f), V(0f, 0f, 1f), V(0f, 1f, 0f)),              // [제안] L2 발판2 윗면(기둥 밑동)
        A("ANCH_S6_Ball_Orange", V(-3f, 1f, 10f), V(0f, 0f, 1f), V(0f, 1f, 0f)),               // [제안] 주황(입구) 에너지볼 중심 — 방 바닥 위 1.0
        A("ANCH_S6_Ball_Blue", V(3f, 1f, 10f), V(0f, 0f, 1f), V(0f, 1f, 0f)),                  // [제안] 파랑(출구) 에너지볼 중심 — 방 바닥 위 1.0
    };

    // ── 계약 K6-1 리스트 (순서 = 진행 단계 순, 초안 §3-4) ──
    private static readonly string[] FixedPanelNames = { "ANCH_S6_Panel_1", "ANCH_S6_Panel_2", "ANCH_S6_Panel_3", "ANCH_S6_Panel_4" };
    private static readonly string[] MovablePanelNames = { "ANCH_S6_MovPanel_1", "ANCH_S6_MovPanel_2" };
    private static readonly string[] MovablePanelEndNames = { "ANCH_S6_MovPanelEnd_1", "ANCH_S6_MovPanelEnd_2" };
    private static readonly string[] LeverNames = { "ANCH_S6_Lever_1", "ANCH_S6_Lever_2" };
    private static readonly string[] CheckpointNames = { "ANCH_S6_Check_1", "ANCH_S6_Check_2" };
    // [제안] 포탈 불가 벽 21개(초안 JSON refs.nonPortalWalls 순서 그대로 — 우물 벽 3개 포함)
    private static readonly string[] NonPortalWallNames =
    {
        "GEO_S6_Wall_West_Back", "GEO_S6_Wall_West_A", "GEO_S6_Wall_West_B", "GEO_S6_Wall_West_NicheBelow", "GEO_S6_Wall_West_NicheAbove",
        "GEO_S6_Wall_East", "GEO_S6_Wall_South_W", "GEO_S6_Wall_South_E", "GEO_S6_Wall_South_Lintel", "GEO_S6_Wall_Vestibule_W",
        "GEO_S6_Wall_Vestibule_E", "GEO_S6_Wall_North_W", "GEO_S6_Wall_North_E", "GEO_S6_Wall_North_Sill", "GEO_S6_Wall_North_Lintel",
        "GEO_S6_Ceiling", "GEO_S6_Wall_ExitPassage_W", "GEO_S6_Wall_ExitPassage_E", "GEO_S6_Well_W", "GEO_S6_Well_S", "GEO_S6_Well_N",
    };

    // ═══════════════════════════════ 여기까지 상수 표 ═══════════════════════════════

    private const float SeamTolerance = 0.02f;  // [계약 K0-2 · 1차 판정 4(a)] 턱·틈 허용 ±0.02 — 0<g≤0.02는 맞닿음
    private const float PinchGap = Map4Build.ShapeMinPassageWidth; // [확정 00_기반 F1 결정11] 끼임 틈 0<g<1.0 금지
    // [C16 — 0.001로 확정(2026-09-29 S6~S8 2차 판정)] 끼임 위쪽 경계 여유(부동소수 오차 전용). 규칙 0<g<1.0의 위쪽 경계를 실질적으로 바꾸지 않도록 부동소수 오차 수준(1e-3)만 둔다.
    // 딱 1.0 틈(초안 검산 2-E 허용 경계 — 관계로 묶으면 7쌍, 포즈별로 세면 9쌍(M2 시작·끝 분리, 검산 S6R3-22 — M3R1로 Wall_West_Back↔Panel_2가 1.4가 되어 1쌍 빠짐), 모두 상자↔패널 포즈; 상자끼리는 0쌍)은 float32 계산 오차 ≤ 2.4e-7이라 이 안에 든다(S6-B3 수정 코드1 재계산).
    // 실제 판정 범위: SeamTolerance < g < PinchGap − PinchEdgeTol = 0.02 < g < 0.999(상자끼리 CheckBoxPairs·패널 CheckPanelClearance 공통).
    private const float PinchEdgeTol = 0.001f;
    // [판정 7 · 계산] CoplanarTouch로 제외되는 B 상자끼리 0<g<1.0 쌍 수 = 1(Floor_Room_2 ↔ Pad1_Base 0.5, 바닥면 아래 대각 틈).
    // 초안 검산 2-C의 2쌍 중 Pad1_Top_S ↔ L1 기둥 0.3은 W 생성물(레버 기둥)이라 B 상자 쌍 검사 밖이다. 2차 배치(상자 46)로 직접 계산.
    private const int ExpectedCoplanarExcluded = 1;
    private const string LogTag = "[S6_Builder]";

    private struct BoxSpec
    {
        public string name, kind;      // kind: Floor | Wall | WallOpen
        public Vector3 min, max;
        public Vector2 opening;        // WallOpen만 — 긴 축 위 (시작, 끝)
        public string nameHi;          // WallOpen만 — 긴 축 높은 쪽 조각 이름(name = 낮은 쪽)
    }

    private struct AnchorSpec
    {
        public string name;
        public Vector3 pos, fwd, up;
    }

    private struct BuiltBox
    {
        public string name;
        public Vector3 min, max;
    }

    private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    private static BoxSpec F(string name, Vector3 min, Vector3 max) =>
        new BoxSpec { name = name, kind = "Floor", min = min, max = max };

    private static BoxSpec W(string name, Vector3 min, Vector3 max) =>
        new BoxSpec { name = name, kind = "Wall", min = min, max = max };

    private static BoxSpec WO(string nameLo, string nameHi, Vector3 min, Vector3 max, float open0, float open1) =>
        new BoxSpec { name = nameLo, nameHi = nameHi, kind = "WallOpen", min = min, max = max, opening = new Vector2(open0, open1) };

    private static AnchorSpec A(string name, Vector3 pos, Vector3 fwd, Vector3 up) =>
        new AnchorSpec { name = name, pos = pos, fwd = fwd, up = up };

    // Build 동안만 유효
    private static Transform g;            // Generated
    private static Transform roomRoot;     // S6_Room (refs.room)
    private static Transform anchorRoot;   // S6_Anchors
    private static List<BuiltBox> built;
    private static Dictionary<string, Transform> byName;

    /// <summary>S6을 짓는다. 섹터 크기·높이가 전제(72×98, 바닥 18, 출구 34)와 다르면 아무것도 만들지 않고 false —
    /// 호출자가 빈 틀로 되돌아간다. 지형 단계에서 예외가 나면 Generated를 비우고 false(부분 생성 0).</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        // 1) 전제 검사 — 맨 앞, 아무것도 만들기 전 [계약 K0-2]
        if (generated == null || def == null)
        {
            Debug.LogError($"{LogTag} generated 또는 def가 null — S6 구체화를 건너뛴다.");
            return false;
        }
        if (!Mathf.Approximately(def.width, SectorWidth) || !Mathf.Approximately(def.length, SectorLength)
            || !Mathf.Approximately(def.floorHeight, FloorHeight) || !Mathf.Approximately(def.exitHeight, ExitHeight))
        {
            Debug.LogError($"{LogTag} 섹터6 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                           $"(전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}, 계약 K0-2). S6 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        built = new List<BuiltBox>();
        byName = new Dictionary<string, Transform>();
        S6_Refs refs = null;
        try
        {
            // 2) 바닥 겹침 검사 범위를 이 섹터로
            Map4Build.BeginSection();

            // 3) 지형·앵커 + refs
            refs = new S6_Refs();
            roomRoot = NewGroup("S6_Room");
            anchorRoot = NewGroup("S6_Anchors");
            refs.room = roomRoot;

            BuildBoxes();
            BuildAnchors();
            FillRefs(refs);

            CheckSeams(def);
            CheckMarkerSupports(def);
            CheckBoxPairs();
            CheckPanelClearance();
            CheckPitSteps();
            CheckM1Pivot();
            CheckZoneAnchors(refs);
            ValidateRefs(refs);
            ReportStrayRoots();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} 섹터6 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(generated.GetChild(i).gameObject);
            Clear();
            return false;
        }

        // 4) 팀 기믹 배치·배선(S6-W) — 예외가 나도 지형은 지우지 않는다 [계약 K0-2]
        try
        {
            S6_Wiring.Wire(generated, refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} S6_Wiring.Wire 예외 — 지형은 그대로 둔다(계약 K0-2): {e.Message}\n{e.StackTrace}");
        }

        // 5) 조명·재질(S6-L) — 반드시 Wire 뒤, 마지막 [계약 K0-2]
        try
        {
            S6_Dress.Apply(generated, refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} S6_Dress.Apply 예외 — 지형은 그대로 둔다(계약 K0-2): {e.Message}\n{e.StackTrace}");
        }

        ReportStrayRoots();
        Debug.Log($"{LogTag} 섹터6 구체화 완료 — 상자 {built.Count}개(초안 46), 앵커 {Anchors.Length}개(초안 16: 고정 패널 {refs.fixedPanelAnchors.Count}·" +
                  $"움직이는 패널 {refs.movablePanelAnchors.Count}·끝 포즈 {refs.movablePanelTravelEnd.Count}·레버 {refs.leverAnchors.Count}·" +
                  $"체크 {refs.checkpointPads.Count}), startZoneSize {refs.startZoneSize}, 포탈 불가 벽 {refs.nonPortalWalls.Count}개, " +
                  "구덩이 3 + 디딤 3, 경사로·계단 0(상승은 포탈로만).");
        Clear();
        return true; // 6)
    }

    // ───────────────────────── 지형 ─────────────────────────

    private static void BuildBoxes()
    {
        foreach (BoxSpec s in Boxes)
        {
            switch (s.kind)
            {
                case "Floor":
                    Add(Solid(Map4Build.Floor(roomRoot, s.min, s.max, "Floor"), s.name), s.min, s.max);
                    break;
                case "Wall":
                    Add(Solid(Map4Build.Wall(roomRoot, s.min, s.max, "Wall"), s.name), s.min, s.max);
                    break;
                case "WallOpen":
                    BuildWallWithOpening(s);
                    break;
                default:
                    throw new System.Exception($"{LogTag} 상자 표 종류를 모른다: {s.kind} ({s.name})");
            }
        }
    }

    /// <summary>구멍 1개짜리 벽 — Map4Build.WallWithOpenings(긴 축 = X 또는 Z 중 긴 쪽)로 잘라 조각 2개(틈 0·겹침 0은 헬퍼가 보장).
    /// 조각 순서는 긴 축 낮은 쪽 → 높은 쪽(헬퍼가 구멍을 정렬해 낮은 쪽부터 만든다).</summary>
    private static void BuildWallWithOpening(BoxSpec s)
    {
        List<Vector2> openings = new List<Vector2> { s.opening };
        Transform[] pieces = Map4Build.WallWithOpenings(roomRoot, s.min, s.max, openings, "Wall");
        if (pieces == null || pieces.Length != 2 || pieces[0] == null || pieces[1] == null)
            throw new System.Exception($"{LogTag} 구멍 벽 생성 실패: {s.name}/{s.nameHi} — 조각 2개가 나와야 한다.");

        bool alongX = (s.max.x - s.min.x) >= (s.max.z - s.min.z); // Map4Build.WallWithOpenings와 같은 판정
        Vector3 loMax = s.max, hiMin = s.min;
        if (alongX) { loMax.x = s.opening.x; hiMin.x = s.opening.y; }
        else { loMax.z = s.opening.x; hiMin.z = s.opening.y; }

        Add(Solid(pieces[0], s.name), s.min, loMax);
        Add(Solid(pieces[1], s.nameHi), hiMin, s.max);
    }

    // ───────────────────────── 앵커 ─────────────────────────

    /// <summary>ANCH_S6_… 빈 GameObject(컴포넌트·콜라이더 없음) [계약 K0-3]. 회전 = LookRotation(forward, up).</summary>
    private static void BuildAnchors()
    {
        foreach (AnchorSpec a in Anchors)
        {
            if (byName.ContainsKey(a.name)) throw new System.Exception($"{LogTag} 앵커 이름 중복: {a.name}");
            if (a.fwd.sqrMagnitude < 1e-8f || a.up.sqrMagnitude < 1e-8f || Vector3.Cross(a.fwd, a.up).sqrMagnitude < 1e-8f)
                throw new System.Exception($"{LogTag} 앵커 방향이 0이거나 forward∥up: {a.name}");
            GameObject go = new GameObject(a.name);
            go.transform.SetParent(anchorRoot, false);
            go.transform.localPosition = a.pos;
            go.transform.localRotation = Quaternion.LookRotation(a.fwd.normalized, a.up.normalized);
            byName[a.name] = go.transform;
        }
    }

    private static void FillRefs(S6_Refs refs)
    {
        refs.roomInnerBounds = MinMax(V(-RoomHalfW, 0f, RoomZ0), V(RoomHalfW, CeilY, RoomZ1)); // [제안] 초안 refs.roomInnerBounds
        refs.startAnchor = Need("ANCH_S6_Start");
        refs.startZoneSize = StartZoneSize;                                                      // [판정 4] (12,3,6) — 0 벡터 금지(ValidateRefs)
        refs.exitLanding = Need("ANCH_S6_ExitLanding");
        refs.orangeBallAnchor = Need("ANCH_S6_Ball_Orange");
        refs.blueBallAnchor = Need("ANCH_S6_Ball_Blue");

        refs.checkpointPads = NeedAll(CheckpointNames);
        refs.checkpointPadBounds = new List<Bounds>
        {
            MinMax(CheckZone1Min, CheckZone1Max),   // [제안·판정 4] 낮은 것(발판1) — RespawnZone 부피 그대로(W는 다시 들이지 않음)
            MinMax(CheckZone2Min, CheckZone2Max),   // [제안·판정 4] 높은 것(발판2)
        };
        refs.fixedPanelAnchors = NeedAll(FixedPanelNames);
        refs.movablePanelAnchors = NeedAll(MovablePanelNames);
        refs.movablePanelTravelEnd = NeedAll(MovablePanelEndNames);
        refs.leverAnchors = NeedAll(LeverNames);
        refs.nonPortalWalls = NeedAll(NonPortalWallNames);
    }

    private static Transform Need(string name)
    {
        if (!byName.TryGetValue(name, out Transform t) || t == null)
            throw new System.Exception($"{LogTag} 오브젝트가 없다: {name}");
        return t;
    }

    private static List<Transform> NeedAll(string[] names)
    {
        List<Transform> list = new List<Transform>();
        foreach (string n in names) list.Add(Need(n));
        return list;
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>입구·출구 이음매 [계약 K0-2]: 입구 z0 윗면 0, 출구 z=length 윗면 = exitHeight − floorHeight(16), 턱 0·틈 0(±0.02).
    /// 출구 층(선반) → 문턱 → 출구 통로 → z98이 모두 같은 윗면 16으로 맞닿는지도 본다. 경사로·계단은 없다.</summary>
    private static void CheckSeams(Map4Layout.SectorDef def)
    {
        float rise = def.exitHeight - def.floorHeight;
        BuiltBox vest = FindBuilt("GEO_S6_Floor_Vestibule");
        if (Mathf.Abs(vest.max.y) > SeamTolerance || Mathf.Abs(vest.min.z) > SeamTolerance
            || vest.min.x > -OpeningHalfW + SeamTolerance || vest.max.x < OpeningHalfW - SeamTolerance)
            throw new System.Exception($"{LogTag} 입구 바닥이 z0·윗면 0·x −4~4를 덮지 않는다: {vest.min}~{vest.max}.");

        BuiltBox passage = FindBuilt("GEO_S6_Floor_ExitPassage");
        if (Mathf.Abs(passage.max.y - rise) > SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 윗면 {passage.max.y} ≠ 로컬 출구 높이 {rise} (턱 {passage.max.y - rise:F3}).");
        if (Mathf.Abs(passage.max.z - def.length) > SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 통로 끝 z {passage.max.z} ≠ 섹터 길이 {def.length}(연결 통로와 틈).");
        if (passage.min.x > -OpeningHalfW + SeamTolerance || passage.max.x < OpeningHalfW - SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 통로 바닥이 x −4~4를 덮지 않는다.");

        BuiltBox sill = FindBuilt("GEO_S6_Wall_North_Sill");
        if (Mathf.Abs(sill.max.y - rise) > SeamTolerance || Mathf.Abs(sill.max.z - passage.min.z) > SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 문턱(윗면 {sill.max.y}, 끝 z {sill.max.z})이 출구 통로(윗면 {rise}, 시작 z {passage.min.z})와 턱·틈 0으로 맞지 않는다.");

        foreach (string n in new[] { "GEO_S6_Shelf_W", "GEO_S6_Shelf_E", "GEO_S6_Shelf_Back" })
        {
            BuiltBox b = FindBuilt(n);
            if (Mathf.Abs(b.max.y - rise) > SeamTolerance || Mathf.Abs(b.max.z - sill.min.z) > SeamTolerance)
                throw new System.Exception($"{LogTag} 출구 층 {n}(윗면 {b.max.y}, 끝 z {b.max.z})이 문턱(윗면 {rise}, z {sill.min.z})과 턱·틈 0으로 맞지 않는다.");
        }
    }

    /// <summary>마커 기본 위치 아래에 윗면이 있어야 한다 [계약 K0-2]: Entrance (0,·,0), Checkpoint (0,·,3), Spawn (−2/0/2,·,3) → 윗면 0,
    /// Exit (0,·,length) → 윗면 rise. 가장자리(=)는 덮는 것으로 본다.</summary>
    private static void CheckMarkerSupports(Map4Layout.SectorDef def)
    {
        float rise = def.exitHeight - def.floorHeight;
        (string name, float x, float z, float top)[] markers =
        {
            ("Entrance", 0f, 0f, 0f), ("Checkpoint", 0f, 3f, 0f),
            ("Spawn_0", -2f, 3f, 0f), ("Spawn_1", 0f, 3f, 0f), ("Spawn_2", 2f, 3f, 0f),
            ("Exit", 0f, def.length, rise),
        };
        foreach (var m in markers)
        {
            bool ok = false;
            foreach (BuiltBox b in built)
            {
                if (Mathf.Abs(b.max.y - m.top) > SeamTolerance) continue;
                if (m.x >= b.min.x - 1e-4f && m.x <= b.max.x + 1e-4f && m.z >= b.min.z - 1e-4f && m.z <= b.max.z + 1e-4f) { ok = true; break; }
            }
            if (!ok) throw new System.Exception($"{LogTag} 마커 {m.name} ({m.x}, {m.top}, {m.z}) 아래에 윗면 {m.top}인 지형이 없다(계약 K0-2).");
        }
    }

    /// <summary>상자 쌍 검사 — 겹침 0(관통 &gt; 0.02면 에러), 끼임 틈 0&lt;g&lt;1.0 금지(Map4Build.CheckGap으로 로그). 초안 검산 2-E의
    /// "딱 1.0" 틈은 허용 경계라 부동소수 여유 PinchEdgeTol(1e-3) 안에서 세지 않는다(실제 판정 0.02&lt;g&lt;0.999). 한쪽 밑면 = 다른 쪽 윗면으로 맞닿은 쌍(같은 바닥면 위 물체와
    /// 이웃 바닥 조각 — 틈이 바닥 평면 위로 열려 있음)은 끼임으로 세지 않는다 — 초안 검산기 S6_check.py coplanar_touch와 같은 규칙,
    /// [판정 7] 채택. 위반은 에러 로그만(생성 유지 — 좌표는 초안 판정 대상).</summary>
    private static void CheckBoxPairs()
    {
        int overlaps = 0, pinches = 0, excluded = 0;
        for (int i = 0; i < built.Count; i++)
        {
            for (int j = i + 1; j < built.Count; j++)
            {
                BuiltBox a = built[i], b = built[j];
                Vector3 s = Separation(a.min, a.max, b.min, b.max);
                if (s.x < -SeamTolerance && s.y < -SeamTolerance && s.z < -SeamTolerance)
                {
                    overlaps++;
                    Debug.LogError($"{LogTag} 지형 겹침 — '{a.name}' ↔ '{b.name}' (관통 {-Mathf.Max(s.x, s.y, s.z):F3}U).");
                    continue;
                }
                if (CoplanarTouch(a.min, a.max, b.min, b.max))
                {
                    // [판정 7] 제외 규칙은 XZ 틈 방향을 보지 않으므로 제외된 0<g<1.0 쌍을 정보 로그로 남기고 개수를 기대값과 대조한다.
                    // 2차 배치(상자 46) B 상자끼리는 1쌍: Floor_Room_2 ↔ Pad1_Base 0.5(Room_2가 바닥면 아래라 생기는 대각 틈, 오탐) [계산].
                    // 초안 검산 2-C의 다른 1쌍(Pad1_Top_S ↔ L1 기둥 0.3)은 W 생성물이라 여기 없다.
                    float cg = PositiveGap(s);
                    if (cg > SeamTolerance && cg < PinchGap - PinchEdgeTol)
                    {
                        excluded++;
                        Debug.Log($"{LogTag} 끼임 검사 제외(같은 바닥면 맞닿음) — '{a.name}' ↔ '{b.name}' {cg:F3}U. 좌표가 바뀌었으면 S6_check.py 전체 쌍과 대조.");
                    }
                    continue;
                }
                float gap = PositiveGap(s);
                if (gap > SeamTolerance && gap < PinchGap - PinchEdgeTol)
                {
                    pinches++;
                    Map4Build.CheckGap(a.min, a.max, b.min, b.max, a.name, b.name);
                    Debug.LogError($"{LogTag} 끼임 틈 — '{a.name}' ↔ '{b.name}' {gap:F3}U (0<g<{PinchGap} 금지, 00_기반 F1 결정11).");
                }
            }
        }
        Debug.Log($"{LogTag} 지형 쌍 검사 — 상자 {built.Count}개, 겹침 {overlaps}, 끼임 틈 {pinches}, " +
                  $"제외(같은 바닥면) {excluded}(기대 {ExpectedCoplanarExcluded} — B 상자끼리, 초안 검산 2-C 2쌍 중 L1 기둥 쌍은 W 몫).");
        if (excluded != ExpectedCoplanarExcluded)
            Debug.LogError($"{LogTag} 같은 바닥면 제외 쌍 {excluded}개 ≠ 기대 {ExpectedCoplanarExcluded}개 — 좌표가 바뀌었다. " +
                           "위 '끼임 검사 제외' 목록을 S6_check.py 검산 2-C 전체 쌍과 다시 대조하라 [판정 7].");
    }

    /// <summary>계약 K6-1 "B는 PortalSurface 자리에 콜라이더를 만들지 않는다" — 고정 패널 4장과 움직이는 패널 시작·끝 포즈의 부피(패널 3.0×3.6×0.2,
    /// 루트 = 앵커 − forward×0.1)가 어떤 지형 상자와도 겹치지 않는지, 맞닿지 않은 지형과는 0&lt;g&lt;1.0 틈이 없는지 본다(회전 도중 스윕은 초안 검산 2-S 몫).</summary>
    private static void CheckPanelClearance()
    {
        List<string> poses = new List<string>();
        poses.AddRange(FixedPanelNames);
        poses.AddRange(MovablePanelNames);
        poses.AddRange(MovablePanelEndNames);
        foreach (string n in poses)
        {
            Transform t = Need(n);
            Vector3 fwd = t.localRotation * Vector3.forward, up = t.localRotation * Vector3.up, right = t.localRotation * Vector3.right;
            Vector3 center = t.localPosition - fwd * (PanelT / 2f);
            Vector3 ext = Abs(right) * PanelHalfW + Abs(up) * PanelHalfH + Abs(fwd) * (PanelT / 2f);
            Vector3 pMin = center - ext, pMax = center + ext;
            foreach (BuiltBox b in built)
            {
                Vector3 s = Separation(pMin, pMax, b.min, b.max);
                if (s.x < -SeamTolerance && s.y < -SeamTolerance && s.z < -SeamTolerance)
                {
                    Debug.LogError($"{LogTag} 패널 자리 {n}에 지형 콜라이더 '{b.name}'이 겹친다(계약 K6-1 위반, 관통 {-Mathf.Max(s.x, s.y, s.z):F3}U).");
                    continue;
                }
                if (CoplanarTouch(pMin, pMax, b.min, b.max)) continue;
                float gap = PositiveGap(s);
                if (gap > SeamTolerance && gap < PinchGap - PinchEdgeTol)
                    Debug.LogError($"{LogTag} 패널 {n} ↔ '{b.name}' 틈 {gap:F3}U (0<g<{PinchGap} 금지).");
            }
        }
    }

    /// <summary>[판정 5] 패널 구덩이 디딤 3곳(초안 §2-7): 디딤 윗면 = 패널 밑면(면 − PanelT, 맞닿음 g=0 ±0.02), 구덩이 바닥 → 디딤 턱 ≤1.0
    /// (Map4Build.LedgeRecommendedMaxRise — 네모 점프 1.2로 나옴), 디딤 → 둘레 윗면 턱 = PanelT, 디딤 깊이 ≥1.5(Map4Build.LedgeMinTreadDepth).
    /// 어긋나면 에러 로그만(생성 유지).</summary>
    private static void CheckPitSteps()
    {
        (string step, string pitFloor, Vector3 face)[] pits =
        {
            ("GEO_S6_Pit_P1_Step", "GEO_S6_Pit_P1_Bottom", P1),
            ("GEO_S6_Pit_P3_Step", "GEO_S6_Pad1_Base", P3),      // P3 구덩이 바닥 = 발판1 채움 윗면
            ("GEO_S6_Pit_P4_Step", "GEO_S6_Pit_P4_Bottom", P4),
        };
        int ok = 0;
        foreach (var p in pits)
        {
            BuiltBox s = FindBuilt(p.step), pit = FindBuilt(p.pitFloor);
            float panelBottom = p.face.y - PanelT;
            float riseIn = s.max.y - pit.max.y, riseOut = p.face.y - s.max.y;
            float depth = Mathf.Min(s.max.x - s.min.x, s.max.z - s.min.z);
            bool good = true;
            if (Mathf.Abs(s.max.y - panelBottom) > SeamTolerance)
            { good = false; Debug.LogError($"{LogTag} 디딤 {p.step} 윗면 {s.max.y:F3} ≠ 패널 밑면 {panelBottom:F3}(맞닿음 g=0이어야 한다, [판정 5])."); }
            if (riseIn > Map4Build.LedgeRecommendedMaxRise + SeamTolerance)
            { good = false; Debug.LogError($"{LogTag} 구덩이 → 디딤 {p.step} 턱 {riseIn:F3} > {Map4Build.LedgeRecommendedMaxRise}([판정 5] 턱 ≤1.0)."); }
            if (riseOut > Map4Build.LedgeRecommendedMaxRise + SeamTolerance)
            { good = false; Debug.LogError($"{LogTag} 디딤 {p.step} → 둘레 턱 {riseOut:F3} > {Map4Build.LedgeRecommendedMaxRise}."); }
            if (depth < Map4Build.LedgeMinTreadDepth - SeamTolerance)
            { good = false; Debug.LogError($"{LogTag} 디딤 {p.step} 깊이 {depth:F3} < {Map4Build.LedgeMinTreadDepth}(디딤 규격)."); }
            if (good) ok++;
        }
        Debug.Log($"{LogTag} 구덩이 디딤 검사 — {ok}/{pits.Length} 통과(구덩이 → 디딤 {PitClear} → 둘레 {PanelT}, 디딤 윗면 = 패널 밑면) [판정 5].");
    }

    /// <summary>[판정 2] M1 경첩 대조 — 시작 루트(면 − forward×PanelT/2)와 경첩 거리 = M1Radius, 팀 Pivot 식(+Y 축 +90°)으로 돌린 끝 포즈 =
    /// ANCH_S6_MovPanelEnd_1(위치 ±0.02·forward). W 상수 pivotPointLocal (6.5,0,0)과 짝이다. 어긋나면 에러 로그만.</summary>
    private static void CheckM1Pivot()
    {
        Transform start = Need("ANCH_S6_MovPanel_1"), end = Need("ANCH_S6_MovPanelEnd_1");
        Vector3 startFwd = start.localRotation * Vector3.forward;
        Vector3 startRoot = start.localPosition - startFwd * (PanelT / 2f);
        float r = (M1Hinge - startRoot).magnitude;
        Quaternion turn = Quaternion.AngleAxis(90f, Vector3.up);
        Vector3 endFwd = turn * startFwd;
        Vector3 endFace = M1Hinge + turn * (startRoot - M1Hinge) + endFwd * (PanelT / 2f);
        Vector3 anchorFwd = end.localRotation * Vector3.forward;
        if (Mathf.Abs(r - M1Radius) > SeamTolerance || (endFace - end.localPosition).magnitude > SeamTolerance || Vector3.Angle(endFwd, anchorFwd) > 0.5f)
            Debug.LogError($"{LogTag} M1 경첩 대조 실패 — 반지름 {r:F3}(상수 {M1Radius}), 계산 끝 면 {endFace} forward {endFwd} ↔ 앵커 {end.localPosition} forward {anchorFwd} [판정 2].");
        else
            Debug.Log($"{LogTag} M1 경첩 대조 — 경첩 {M1Hinge}, 반지름 {r:F2}, 끝 포즈 {end.localPosition} 일치 [판정 2].");
    }

    /// <summary>[판정 4 · 계약 K6-1] RespawnZone 루트(앵커) = 구역 AABB 아랫면 중심(±0.02). 시작(startAnchor ↔ StartZone, startZoneSize = 그 size)과
    /// 체크 2(checkpointPads[i] ↔ checkpointPadBounds[i]). 어긋나면 LogError(좌표는 바꾸지 않는다).</summary>
    private static void CheckZoneAnchors(S6_Refs refs)
    {
        List<(string label, Transform anchor, Bounds zone)> zones = new List<(string, Transform, Bounds)>
        {
            ("Start", refs.startAnchor, MinMax(StartZoneMin, StartZoneMax)),
        };
        for (int i = 0; i < refs.checkpointPads.Count && i < refs.checkpointPadBounds.Count; i++)
            zones.Add(($"Check_{i + 1}", refs.checkpointPads[i], refs.checkpointPadBounds[i]));

        int ok = 0;
        foreach (var z in zones)
        {
            Vector3 bottomCenter = new Vector3(z.zone.center.x, z.zone.min.y, z.zone.center.z);
            Vector3 d = z.anchor.localPosition - bottomCenter;
            if (Mathf.Abs(d.x) > SeamTolerance || Mathf.Abs(d.y) > SeamTolerance || Mathf.Abs(d.z) > SeamTolerance)
                Debug.LogError($"{LogTag} RespawnZone {z.label}: 앵커 {z.anchor.localPosition} ≠ 구역 {z.zone.min}~{z.zone.max} 아랫면 중심 {bottomCenter} (±{SeamTolerance}, 계약 K6-1 [판정 4]).");
            else ok++;
        }
        Vector3 ds = refs.startZoneSize - MinMax(StartZoneMin, StartZoneMax).size;
        if (Mathf.Abs(ds.x) > SeamTolerance || Mathf.Abs(ds.y) > SeamTolerance || Mathf.Abs(ds.z) > SeamTolerance)
            Debug.LogError($"{LogTag} startZoneSize {refs.startZoneSize} ≠ 시작 구역 size {MinMax(StartZoneMin, StartZoneMax).size} [판정 4].");
        Debug.Log($"{LogTag} RespawnZone 앵커 = AABB 아랫면 중심 {ok}/{zones.Count} [판정 4], startZoneSize {refs.startZoneSize}.");
    }

    /// <summary>계약 K0-2·K6-1 — 모든 필드 non-null, 리스트 null 요소 0, startZoneSize ≠ 0 [판정 4], 개수: checkpoint 2·2,
    /// 움직이는 패널 = 끝 포즈 = 레버(= 이름 표 2), 고정 패널 = 이름 표(4), 포탈 불가 벽 = 이름 표(21, ≥1).</summary>
    private static void ValidateRefs(S6_Refs r)
    {
        List<string> bad = new List<string>();
        if (r.room == null) bad.Add("room");
        if (r.roomInnerBounds.size == Vector3.zero) bad.Add("roomInnerBounds");
        if (r.startAnchor == null) bad.Add("startAnchor");
        // [판정 4 · 계약 K6-1 :123] 0 벡터 금지 — 성분 하나라도 0 이하면 BoxCollider 부피가 없으므로 함께 거른다([해석])
        if (r.startZoneSize.x <= 0f || r.startZoneSize.y <= 0f || r.startZoneSize.z <= 0f) bad.Add($"startZoneSize {r.startZoneSize}(0 벡터·0 성분 금지)");
        if (r.exitLanding == null) bad.Add("exitLanding");
        if (r.orangeBallAnchor == null) bad.Add("orangeBallAnchor");
        if (r.blueBallAnchor == null) bad.Add("blueBallAnchor");

        CheckList(r.checkpointPads, "checkpointPads", bad);
        if (r.checkpointPadBounds == null) bad.Add("checkpointPadBounds null");
        CheckList(r.fixedPanelAnchors, "fixedPanelAnchors", bad);
        CheckList(r.movablePanelAnchors, "movablePanelAnchors", bad);
        CheckList(r.movablePanelTravelEnd, "movablePanelTravelEnd", bad);
        CheckList(r.leverAnchors, "leverAnchors", bad);
        CheckList(r.nonPortalWalls, "nonPortalWalls", bad);

        if (r.checkpointPads != null && r.checkpointPads.Count != 2) bad.Add($"checkpointPads 개수 {r.checkpointPads.Count}/2");
        if (r.checkpointPadBounds != null && r.checkpointPadBounds.Count != 2) bad.Add($"checkpointPadBounds 개수 {r.checkpointPadBounds.Count}/2");
        if (r.checkpointPadBounds != null && r.checkpointPadBounds.Count == 2 && r.checkpointPadBounds[0].min.y > r.checkpointPadBounds[1].min.y)
            bad.Add("checkpointPadBounds 순서(낮은 것부터) 어긋남");
        if (r.movablePanelAnchors != null && r.movablePanelTravelEnd != null && r.leverAnchors != null
            && (r.movablePanelAnchors.Count != r.movablePanelTravelEnd.Count || r.movablePanelAnchors.Count != r.leverAnchors.Count))
            bad.Add($"움직이는 패널·끝 포즈·레버 개수 불일치 {r.movablePanelAnchors.Count}/{r.movablePanelTravelEnd.Count}/{r.leverAnchors.Count}");
        if (r.checkpointPadBounds != null)
            for (int i = 0; i < r.checkpointPadBounds.Count; i++)
                if (r.checkpointPadBounds[i].size.x <= 0f || r.checkpointPadBounds[i].size.y <= 0f || r.checkpointPadBounds[i].size.z <= 0f)
                    bad.Add($"checkpointPadBounds[{i}] 부피 0");
        if (r.movablePanelAnchors != null && r.movablePanelAnchors.Count != MovablePanelNames.Length)
            bad.Add($"movablePanelAnchors 개수 {r.movablePanelAnchors.Count}/{MovablePanelNames.Length}");
        if (r.fixedPanelAnchors != null && (r.fixedPanelAnchors.Count < 1 || r.fixedPanelAnchors.Count != FixedPanelNames.Length))
            bad.Add($"fixedPanelAnchors 개수 {r.fixedPanelAnchors.Count}/{FixedPanelNames.Length}");
        if (r.nonPortalWalls != null && (r.nonPortalWalls.Count < 1 || r.nonPortalWalls.Count != NonPortalWallNames.Length))
            bad.Add($"nonPortalWalls 개수 {r.nonPortalWalls.Count}/{NonPortalWallNames.Length}");

        if (bad.Count > 0)
            throw new System.Exception($"{LogTag} S6_Refs가 계약과 다르다: {string.Join(", ", bad)}");
    }

    private static void CheckList(List<Transform> list, string field, List<string> bad)
    {
        if (list == null) { bad.Add(field + " null"); return; }
        if (list.Contains(null)) bad.Add(field + " null 요소");
    }

    /// <summary>씬 루트에 남은 오브젝트 확인(S1·S8과 같은 점검) — B는 팀 메뉴를 부르지 않아 원칙적으로 0. Wire·Apply 뒤에도 한 번 더 부른다.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"{LogTag} 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>Map4Build가 돌려준 상자에 S6 이름을 붙인다(Map4Build 수정 없음, 계약 K0-3 — 반환 Transform의 name만 바꿈).
    /// 시각물 자식 "Visual"은 이름을 **바꾸지 않는다** — 계약 K0-3 "L은 GEO_의 자식 `Visual`… 머티리얼만 바꾼다"(S8_Builder와 같은 처리).
    /// 생성이 거부돼 null이면 즉시 중단(부분 지형 방지).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"{LogTag} 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        if (byName.ContainsKey(name)) throw new System.Exception($"{LogTag} 이름 중복: {name}");
        t.name = name;
        byName[name] = t;
        return t;
    }

    private static void Add(Transform t, Vector3 min, Vector3 max)
    {
        built.Add(new BuiltBox { name = t.name, min = min, max = max });
    }

    private static BuiltBox FindBuilt(string name)
    {
        foreach (BuiltBox b in built)
            if (b.name == name) return b;
        throw new System.Exception($"{LogTag} 만든 상자 목록에 없다: {name}");
    }

    /// <summary>축별 분리량(양수 = 떨어짐, 음수 = 겹침 깊이).</summary>
    private static Vector3 Separation(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) => new Vector3(
        Mathf.Max(bMin.x - aMax.x, aMin.x - bMax.x),
        Mathf.Max(bMin.y - aMax.y, aMin.y - bMax.y),
        Mathf.Max(bMin.z - aMax.z, aMin.z - bMax.z));

    /// <summary>떨어진 축만 모은 최단 거리(유클리드) — 맞닿거나 한 축이라도 겹치면 그 축은 0.</summary>
    private static float PositiveGap(Vector3 s) =>
        new Vector3(Mathf.Max(0f, s.x), Mathf.Max(0f, s.y), Mathf.Max(0f, s.z)).magnitude;

    /// <summary>높이 구간이 한 점에서만 맞닿음(한쪽 밑면 = 다른 쪽 윗면) — S6_check.py coplanar_touch와 같은 판정. [판정 7] 채택.
    /// 한계(검문 코드 S6-B 지적): XZ 틈이 어느 쪽으로 열리는지는 보지 않는다 — 좌표가 바뀌면 진짜 끼임도 가려질 수 있으니
    /// CheckBoxPairs가 제외 쌍을 열거하고 개수를 ExpectedCoplanarExcluded와 대조한다(다르면 LogError → S6_check.py 전체 쌍 재대조).</summary>
    private static bool CoplanarTouch(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
        Mathf.Abs(aMin.y - bMax.y) < 1e-4f || Mathf.Abs(bMin.y - aMax.y) < 1e-4f;

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static Transform NewGroup(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(g, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go.transform;
    }

    private static Bounds MinMax(Vector3 min, Vector3 max)
    {
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    private static void Clear()
    {
        g = null;
        roomRoot = null;
        anchorRoot = null;
        built = null;
        byName = null;
    }
}
#endif
