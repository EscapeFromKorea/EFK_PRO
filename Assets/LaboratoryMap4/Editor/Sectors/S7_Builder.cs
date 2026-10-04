#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 섹터7 "서버실(CH7)" 빌더 — 과제 S7-B3(스테이징, 1차 S7-B의 2차 반영). 설계/S7_설계.md(§3 결정표, 09-28 확정)와
/// 진행/초안/S7_배치초안.md(S7-D 초안 **2차 반영(S7-D3)**, 2026-09-29 — 빌드 입력용 확정본, 좌표 정본 = md §3)의 좌표를
/// 섹터 씬 Generated/ 아래에 짓는다. 초안 JSON(S7_배치초안.json)의 본 좌표는 아직 안0이라 읽지 않는다(초안 §3-1-b 끝).
/// Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry(통합 때 case 7 추가 [판정 24])를 거쳐 부르고, 연결 통로
/// (z 110~122, x −4~4)·마커(Manual/Markers)는 공용 코드(Map4SceneBuilder.BuildConnectorAndMarkers)가 이어서 만든다 —
/// 이 파일은 그 구역에 Floor를 만들지 않는다.
///
/// [범위 — 계약 K7-1] 지형: 바닥·천장·외벽, 전실(entryHall) 옆벽·칸막이(서버실 남벽 겸함)·인방, 서버실(serverRoom) 북벽·
/// 인방·전력 콘솔 받침·서버 6대 박스(안C [사용자 09-29 나·안C]), 컴퓨터실(computerRoom) 남벽(개구부)·북벽·유리벽(솔리드
/// BoxCollider — Map4Build.Wall)·책상, 앵커 ANCH_S7_… 11개.
/// 문: 되돌림 상태(TempOpenDoors = false)에서만 문 2개(입구 문 = entryDoor, 출구 문 = exitDoor, Map4Build.Door)를 만들고,
/// 그때 문틀 VIS 2곳(z14·z110 — Map4Build.Door가 만드는 VIS_NoCollide_DoorFrame), 개구부 2곳(z0·z19.5)은 틀 없음
/// (WallWithOpenings). **TEMP 기간(TempOpenDoors = true, 지금)에는 Map4Build.Door를 부르지 않으므로 문도 문틀 VIS도
/// 0곳**이다 — 두 문 개구부는 비워 두고(인방은 그대로) 빈 GameObject TEMP_S7_EntryDoor_Open·TEMP_S7_ExitDoor_Open을
/// 문 상자 아랫면 중심에 둔다 [판정 17-S7 · 계약 K7-0]. 문틀 모양이 필요하면 L 몫이다(B는 VIS를 만들지 않는다, K0-3).
/// 팀 기믹(배선 패널·포트·RoleSlot·전력)은 만들지 않는다 — S7_Wiring.Wire(W). 조명·재질은 S7_Dress.Apply(L).
/// 여기서는 Map4Palette 이름만 쓴다(Floor·Wall·Door). 팀 코드·팀 private 필드는 건드리지 않는다. 팀 메뉴를 부르지 않는다.
/// 회로 배정·문 여는 코드는 없다(부트스트랩 금지 [판정 17-S7], HANDOFF §5-0).
///
/// [호출 순서 — 계약 K0-2] 1) 전제 검사 → 2) Map4Build.BeginSection → 3) 지형·(문)·앵커·TEMP 표지, refs 채움 →
/// 4) S7_Wiring.Wire → 5) S7_Dress.Apply → 6) true. 전제 불일치면 아무것도 만들지 않고 false. 3)에서 예외가 나면
/// Generated를 비우고 false(부분 생성 0 [1차 판정 8]). 4)·5)가 예외를 내도 지형은 지우지 않는다.
///
/// [좌표] 섹터 로컬(+Z 진행, x=0 중앙, y=0 = 섹터 바닥 = 세계 34, 평지 — 출구 윗면도 y0). 모든 좌표·크기·검사 기준은
/// 아래 "상수 표" 한 곳에 있다.
/// 출처 표기: [확정]=설계서 확정 · [팀]=팀 코드 · [제안]=초안이 정한 값 · [계산] · [추정] · [해석] ·
/// [계약 Kx]=진행/지시서/R2S68/_계약.md 조항 · [판정 n]/[판정 17-S7]=진행/판정/2026-09-28_S6S8_판정.md ·
/// [사용자 09-29 나·안C]=같은 문서 끝 사용자 결정 · [1차 판정 n]=2026-09-28_1차_판정.md ·
/// "초안 §3-1 이름"=S7_배치초안.md 2차 반영(S7-D3) 좌표표 행.
/// </summary>
public static class S7_Builder
{
    // ═══════════════════════════════ 상수 표 (좌표·크기·검사 기준은 여기 한 곳) ═══════════════════════════════

    // ── TEMP 상시 개방 [판정 17-S7 · 계약 K7-0] ──
    // true(지금): 입구·출구 문을 만들지 않고 개구부를 비운다 + TEMP_S7_*_Open 빈 GO 2개, refs.entryDoor/exitDoor = null.
    // false(되돌림 — W_ENTRY 회로 배정·문답·책 내용이 팀에서 들어온 뒤): 1차 판대로 Map4Build.Door 2개(DoorBoxes).
    private static readonly bool TempOpenDoors = true;   // static readonly — const면 CS0162(도달 불가) 경고(검문 수정 코드1 #3)

    // ── 전제 [계약 K0-2] SectorSource·Map4Layout.asset 현재값. 비교는 Mathf.Approximately ──
    private const float SectorWidth = 84f;          // [계약 K0-2] 폭 84
    private const float SectorLength = 110f;        // [계약 K0-2] 길이 110
    private const float FloorHeight = 34f;          // [계약 K0-2] 바닥 높이 34(세계)
    private const float ExitHeight = 34f;           // [계약 K0-2] 출구 높이 34(세계) → 로컬 출구 윗면 0(평지 [확정 §3-8])

    // ── 틀 (초안 §2-1) ──
    private const float HalfW = SectorWidth / 2f;   // [계산] 42
    private const float WallT = 0.5f;               // [제안] 벽 두께 0.5(S1 OuterWallT 선례) — 외벽은 섹터 경계 안쪽
    private const float InnerX = HalfW - WallT;     // [계산] 41.5 = 외벽 안쪽 면 |x|
    private const float RoomH = 14f;                // [제안] 벽·유리·인방 윗면 = 천장 아랫면 14(점프 시 카메라 최고 13.83 [계산] 위, 초안 §2-1)
    private const float CeilT = 0.5f;               // [제안] 천장 y 14~14.5 (GEO_S7_Ceiling)
    private const float FloorT = 0.3f;              // [팀] 바닥 판 두께 0.3(Map4SceneBuilder.cs:241 빈 틀 Floor y −0.3~0과 같음)
    private const float OpeningHalfW = 4f;          // [계약 K0-2] 입구 z0·출구 z110 개구 x −4~4 = 연결 통로 폭 8(Map4Layout.asset 현재값). 칸막이 입구 개구도 같은 폭

    // ── 전실 entryHall (초안 §2-2) — x −16~16, z 0.5~14 ──
    private const float HallHalfW = 16f;            // [제안] 전실 폭 32 (GEO_S7_Wall_Hall_W/E 안쪽 면 x ±16)
    private const float PartitionZ0 = 14f;          // [제안] 전실/서버실 칸막이 z 14~14.5 (GEO_S7_Wall_Partition_*)
    private const float PartitionZ1 = PartitionZ0 + WallT; // [계산] 14.5
    private const float EntryDoorH = 6f;            // [제안] 입구 개구 높이 6 = 인방 아랫면(인방 y 6~14). 되돌림 때 입구 문 높이 6 (초안 §3-1-b)
    private const float EntryDoorOpen = 6f;         // [제안] 되돌림 때 openHeight 6 → 열린 문(y 6~12)이 인방 안에 숨음 (초안 §3-1-b)
    private const float EntryPanelY = 1.2f;         // [제안] ANCH_S7_EntryPanel 높이 1.2
    private const float EntryPanelZ = 8f;           // [제안] ANCH_S7_EntryPanel z 8(W_ENTRY 포트 z 2.5~13.5 중심). [판정 9] 동벽 x16 확정

    // ── 출구 (초안 §2-3) ──
    private const float ExitWallZ0 = SectorLength - WallT; // [계산] 109.5 — 북벽 z 109.5~110 (GEO_S7_Wall_North_*)
    private const float ExitDoorH = 4f;             // [제안] 출구 개구 높이 4 = 인방 아랫면(인방 y 4~14). 되돌림 때 출구 문 높이 4 (초안 §3-1-b)
    private const float ExitDoorOpen = 4f;          // [제안] 되돌림 때 openHeight 4 → 열린 문(y 4~8)이 인방 안에 숨음 (초안 §3-1-b)

    // ── 컴퓨터실 computerRoom (초안 §2-4) — x −41.5~−26, z 20~48 ──
    private const float GlassX0 = -26f;             // [제안] 유리벽 x −26~−25.5 (GEO_S7_Glass_1), 솔리드 [확정 §3-5]
    private const float GlassX1 = GlassX0 + WallT;  // [계산] −25.5 = 유리 동쪽 면 = serverRoomBounds min x [판정 15]
    private const float CRSouthZ0 = 19.5f;          // [제안] 컴퓨터실 남벽 z 19.5~20 (GEO_S7_Wall_CR_South_*)
    private const float CRSouthZ1 = CRSouthZ0 + WallT; // [계산] 20
    private const float CRNorthZ0 = 48f;            // [제안] 컴퓨터실 북벽 z 48~48.5 (GEO_S7_Wall_CR_North) — OUT_C 가시선 [계산]
    private const float CRNorthZ1 = CRNorthZ0 + WallT; // [계산] 48.5
    private const float CRDoorX0 = -31f;            // [제안] 컴퓨터실 개구부 x −31~−27(폭 4, 문 없음 [제안 §3-5])
    private const float CRDoorX1 = -27f;            // [제안]
    private const float DeskX0 = -29f;              // [제안] 책상 x −29~−28 (GEO_S7_Desk) — 유리와 2.0
    private const float DeskX1 = -28f;              // [제안]
    private const float DeskH = 0.75f;              // [제안] 책상 높이 0.75
    private const float DeskZ0 = 30f;               // [제안] 책상 z 30~40(길이 10)
    private const float DeskZ1 = 40f;               // [제안]
    private const float RoleBookZ = 32f;            // [제안] ANCH_S7_Role_Book z 32
    private const float RoleComputerZ = 38f;        // [제안] ANCH_S7_Role_Computer z 38 — 책과 6.0(트리거 틈 2.5)

    // ── 서버실 serverRoom (초안 §2-3) — x −41.5~41.5, z 14.5~109.5 ──
    private const float ConsoleHalfW = 0.8f;        // [팀] 전력 콘솔 받침 1.6×1×1 (Ch8TestRoomMenuItem.cs:256 Desk) (GEO_S7_PowerConsole)
    private const float ConsoleH = 1f;              // [팀] Ch8TestRoomMenuItem.cs:256 Desk 높이 1
    private const float ConsoleZ0 = 23.5f;          // [제안] z 23.5~24.5(입구 개구에서 10 앞, 가운데). 콘솔 z27 안 폐기(안C와 함께 못 씀, 초안 §11-3)
    private const float ConsoleZ1 = 24.5f;          // [제안]
    private const float ServerW = 4f;               // [추정] 서버 크기 4(x)×3.5(y)×2(z) — 초안 [추정]
    private const float ServerH = 3.5f;             // [추정] 3.5 > LD-01 턱 1.1 → 올라설 수 없음
    private const float ServerD = 2f;               // [추정]
    // [사용자 09-29 나·안C] 서버 6대 중심 (x, z) — 안C(틈 11): 앞줄 z 28~30(x −15·0·15, 포트 면 +Z),
    // 뒷줄 z 44~46(x −7.5·7.5·22.5, 포트 면 −Z). 서버끼리 최소 틈 11, 서버↔유리 8.5, 서버↔외벽 17, 서버↔콘솔 3.5(초안 §2-3).
    // 좌표 [제안] = 초안 §3-1 (S7_동선분석 §9-1). 순서 = refs.servers = GEO_S7_Server_1..6.
    // (안0 폐기, 참고: 2열 x ±12 × 3행 z 40·58·76, 포트 면 −Z, portServerIndex [0,3,4,1,2,5] — 초안 §3-1-b 끝)
    private static readonly Vector2[] ServerCenterXZ =
    {
        new Vector2(-15f, 29f),   // GEO_S7_Server_1 앞줄 [사용자 09-29 나·안C]
        new Vector2(0f, 29f),     // GEO_S7_Server_2 앞줄
        new Vector2(15f, 29f),    // GEO_S7_Server_3 앞줄
        new Vector2(-7.5f, 45f),  // GEO_S7_Server_4 뒷줄
        new Vector2(7.5f, 45f),   // GEO_S7_Server_5 뒷줄
        new Vector2(22.5f, 45f),  // GEO_S7_Server_6 뒷줄
    };
    private const float PortY = 1.2f;               // [제안] 포트 앵커 높이 1.2(선 끝점이 바닥을 기지 않게, S7-R §1-2)
    // [계약 K7-1 순서] [0]OUT_A [1]OUT_B [2]OUT_C [3]IN_1 [4]IN_2 [5]IN_3. server = servers 인덱스(= portServerIndex),
    // face = 포트 면(+1 = 서버 +Z 면 = max z, 앞줄 / −1 = 서버 −Z 면 = min z, 뒷줄). 앵커 = 그 면 중심·높이 PortY,
    // forward = 면 바깥 법선(플레이어가 서는 쪽). 두 줄 포트 면이 z 30~44 마당을 사이에 두고 마주 본다(초안 §2-3).
    private static readonly PortSpec[] Ports =
    {
        P("ANCH_S7_Port_OUT_A", 0, +1),   // Server_1 +Z면 → (−15, 1.2, 30) [사용자 09-29 나·안C]
        P("ANCH_S7_Port_OUT_B", 2, +1),   // Server_3 +Z면 → (15, 1.2, 30)
        P("ANCH_S7_Port_OUT_C", 4, -1),   // Server_5 −Z면 → (7.5, 1.2, 44)
        P("ANCH_S7_Port_IN_1", 1, +1),    // Server_2 +Z면 → (0, 1.2, 30)
        P("ANCH_S7_Port_IN_2", 3, -1),    // Server_4 −Z면 → (−7.5, 1.2, 44)
        P("ANCH_S7_Port_IN_3", 5, -1),    // Server_6 −Z면 → (22.5, 1.2, 44)
    };
    // [사용자 09-29 나·안C] 초안 §3-3 portServerIndex 대조값 — Ports 표가 이 값과 다르면 ValidateRefs가 막는다.
    private static readonly int[] ExpectedPortServerIndex = { 0, 2, 4, 1, 3, 5 };

    // ── 상자 표 (초안 §3-1 좌표표 26개 중 서버 6대를 뺀 20개, min/max 섹터 로컬) ──
    // 종류: Floor → Map4Build.Floor · Wall/Ceil/Glass → Map4Build.Wall · WallOpen → Map4Build.WallWithOpenings(조각 _W·_E)
    //       Desk/Console → Map4Build.Wall(바닥까지 채운 단순 박스). 서버 6대는 ServerCenterXZ로 따로. 문은 DoorBoxes.
    // 그룹: Shell(계약 밖 보조 그룹 S7_Shell — 초안 groups.shell, B 재량) · EntryHall · ServerRoom · ComputerRoom.
    private static readonly BoxSpec[] Boxes =
    {
        // Shell
        B("GEO_S7_Floor_Main", "Floor", "Shell", -HalfW, -FloorT, 0f, HalfW, 0f, SectorLength),                           // [계약 K0-2] 84×110 평지 윗면 y0 [확정 §3-8]
        B("GEO_S7_Ceiling", "Ceil", "Shell", -HalfW, RoomH, 0f, HalfW, RoomH + CeilT, SectorLength),                      // [제안] y 14~14.5
        BO("GEO_S7_Wall_South", "Shell", -HalfW, 0f, 0f, HalfW, RoomH, WallT, -OpeningHalfW, OpeningHalfW),               // [계약 K0-2] 입구 개구 x −4~4 → _W·_E
        B("GEO_S7_Wall_West", "Wall", "Shell", -HalfW, 0f, WallT, -InnerX, RoomH, SectorLength - WallT),                  // [제안]
        B("GEO_S7_Wall_East", "Wall", "Shell", InnerX, 0f, WallT, HalfW, RoomH, SectorLength - WallT),                    // [제안]
        // ServerRoom — 출구
        BO("GEO_S7_Wall_North", "ServerRoom", -HalfW, 0f, ExitWallZ0, HalfW, RoomH, SectorLength, -OpeningHalfW, OpeningHalfW), // [확정 :32] 위치 · [계약 K0-2] 개구 x −4~4 → _W·_E
        B("GEO_S7_Wall_North_Lintel", "Wall", "ServerRoom", -OpeningHalfW, ExitDoorH, ExitWallZ0, OpeningHalfW, RoomH, SectorLength), // [제안] 출구 개구 위 인방 y 4~14(TEMP 기간에도 짓는다)
        // EntryHall
        B("GEO_S7_Wall_Hall_W", "Wall", "EntryHall", -HallHalfW - WallT, 0f, WallT, -HallHalfW, RoomH, PartitionZ0),     // [제안]
        B("GEO_S7_Wall_Hall_E", "Wall", "EntryHall", HallHalfW, 0f, WallT, HallHalfW + WallT, RoomH, PartitionZ0),       // [제안] 안쪽 면 x16 = W_ENTRY 벽 [판정 9]
        BO("GEO_S7_Wall_Partition", "EntryHall", -InnerX, 0f, PartitionZ0, InnerX, RoomH, PartitionZ1, -OpeningHalfW, OpeningHalfW), // [제안] 칸막이, 입구 개구 x −4~4 → _W·_E
        B("GEO_S7_Wall_Partition_Lintel", "Wall", "EntryHall", -OpeningHalfW, EntryDoorH, PartitionZ0, OpeningHalfW, RoomH, PartitionZ1), // [제안] 입구 개구 위 인방 y 6~14(TEMP 기간에도 짓는다)
        // ComputerRoom
        BO("GEO_S7_Wall_CR_South", "ComputerRoom", -InnerX, 0f, CRSouthZ0, GlassX0, RoomH, CRSouthZ1, CRDoorX0, CRDoorX1), // [제안] 개구부 x −31~−27 → _W·_E
        B("GEO_S7_Wall_CR_North", "Wall", "ComputerRoom", -InnerX, 0f, CRNorthZ0, GlassX0, RoomH, CRNorthZ1),            // [제안] 불투명
        B("GEO_S7_Glass_1", "Glass", "ComputerRoom", GlassX0, 0f, CRSouthZ0, GlassX1, RoomH, CRNorthZ1),                  // [확정 §3-5] 솔리드 유리벽, 위치 [제안] = refs.glassWalls[0]
        B("GEO_S7_Desk", "Desk", "ComputerRoom", DeskX0, 0f, DeskZ0, DeskX1, DeskH, DeskZ1),                              // [확정 §3-6] 책상 1개, 크기·위치 [제안]
        // ServerRoom — 콘솔
        B("GEO_S7_PowerConsole", "Console", "ServerRoom", -ConsoleHalfW, 0f, ConsoleZ0, ConsoleHalfW, ConsoleH, ConsoleZ1), // [팀] 크기 = Ch8TestRoomMenuItem.cs:256 Desk. 전력 역할 사물 받침
    };

    // ── 문 상자 (초안 §3-1-b 되돌림용 2개) — TempOpenDoors = false일 때만 Map4Build.Door로 짓는다 [판정 17-S7] ──
    // TEMP 기간에는 이 상자 부피가 빈 개구부이고, 아랫면 중심이 TEMP 표지 자리다(TempDoorMarkers).
    private static readonly BoxSpec[] DoorBoxes =
    {
        BD("GEO_S7_Door_Entry", "EntryHall", -OpeningHalfW, 0f, PartitionZ0, OpeningHalfW, EntryDoorH, PartitionZ1, EntryDoorOpen), // [확정 §3-2] 서버실 입구 문 = refs.entryDoor(되돌림)
        BD("GEO_S7_Door_Exit", "ServerRoom", -OpeningHalfW, 0f, ExitWallZ0, OpeningHalfW, ExitDoorH, SectorLength, ExitDoorOpen),     // [확정 §3-7] 출구 문 = refs.exitDoor(되돌림)
    };

    // ── TEMP 표지 (초안 §3-2 끝 2행) — 빈 GO, 위치 = 대응 DoorBoxes 아랫면 중심, forward +Z [제안](진행 방향) ──
    // TempOpenDoors = true일 때만 만든다. 부모 = S7_Anchors [해석 — B 재량]. built(구조물 목록)에는 넣지 않는다.
    private static readonly TempSpec[] TempDoorMarkers =
    {
        T("TEMP_S7_EntryDoor_Open", "GEO_S7_Door_Entry"),   // → (0, 0, 14.25) [판정 17-S7]
        T("TEMP_S7_ExitDoor_Open", "GEO_S7_Door_Exit"),     // → (0, 0, 109.75) [판정 17-S7]
    };

    // ── 앵커 표 (초안 §3-2) — 위치 = 섹터 로컬, forward(up = 월드 +Y). 포트 6개는 Ports에서 ──
    private static readonly AnchorSpec[] FixedAnchors =
    {
        A("ANCH_S7_EntryPanel", HallHalfW, EntryPanelY, EntryPanelZ, -1f, 0f, 0f),                                  // [판정 9] 전실 동벽 안쪽 면 x16 (16,1.2,8), forward −X
        A("ANCH_S7_Role_Book", (DeskX0 + DeskX1) / 2f, 0f, RoleBookZ, -1f, 0f, 0f),                                  // [제안] 책상 가운데 x −28.5, 루트 y0(팀 CH8 선례)
        A("ANCH_S7_Role_Computer", (DeskX0 + DeskX1) / 2f, 0f, RoleComputerZ, -1f, 0f, 0f),                          // [제안] 같은 책상, 책과 6.0
        A("ANCH_S7_Role_Power", 0f, 0f, (ConsoleZ0 + ConsoleZ1) / 2f, 0f, 0f, -1f),                                  // [제안] 콘솔 중심 바닥 높이, forward −Z
        A("ANCH_S7_CRDoorway", (CRDoorX0 + CRDoorX1) / 2f, 0f, (CRSouthZ0 + CRSouthZ1) / 2f, 0f, 0f, 1f),           // [제안] 개구부 중심 (−29, 0, 19.75)
    };

    // ── 마커 기본 위치 [계약 K0-2] — Entrance (0,0.1,0), Checkpoint (0,0.1,3), Spawn_0~2 (−2/0/2, 0.6, 3). Exit는 (0, rise+ExitMarkerLift, length) ──
    private static readonly Vector3[] MarkerDefaults =
    {
        new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.1f, 3f),
        new Vector3(-2f, 0.6f, 3f), new Vector3(0f, 0.6f, 3f), new Vector3(2f, 0.6f, 3f),
    };
    private const float ExitMarkerLift = 0.1f;      // [계약 K0-2] Exit 마커 y = rise + 0.1

    // ── 검사 기준 ──
    private const float SeamTolerance = 0.02f;      // [계약 K0-2] 턱·틈 허용 ±0.02
    private const float MinRoleDistance = 2f;       // [제안 §3-6] 책↔컴퓨터 거리 ≥ 2 (계약 K7-1 주석)

    // ═══════════════════════════════ 여기까지 상수 표 ═══════════════════════════════

    private const string LogTag = "[S7_Builder]";

    private struct BoxSpec
    {
        public string name, kind, group;
        public Vector3 min, max;
        public float open0, open1;   // WallOpen: 긴 축 개구 범위
        public float openHeight;     // Door: doorTargetYOffset
    }

    private struct AnchorSpec
    {
        public string name;
        public Vector3 pos, fwd;
    }

    private struct PortSpec
    {
        public string name;
        public int server;
        public int face;
    }

    private struct TempSpec
    {
        public string name;
        public string doorBox;       // DoorBoxes의 이름 — 위치는 그 상자 아랫면 중심에서 파생
    }

    private struct BuiltBox
    {
        public string name;
        public Vector3 min, max;
    }

    private static BoxSpec B(string name, string kind, string group, float x0, float y0, float z0, float x1, float y1, float z1) =>
        new BoxSpec { name = name, kind = kind, group = group, min = new Vector3(x0, y0, z0), max = new Vector3(x1, y1, z1) };

    private static BoxSpec BO(string name, string group, float x0, float y0, float z0, float x1, float y1, float z1, float o0, float o1) =>
        new BoxSpec { name = name, kind = "WallOpen", group = group, min = new Vector3(x0, y0, z0), max = new Vector3(x1, y1, z1), open0 = o0, open1 = o1 };

    private static BoxSpec BD(string name, string group, float x0, float y0, float z0, float x1, float y1, float z1, float openHeight) =>
        new BoxSpec { name = name, kind = "Door", group = group, min = new Vector3(x0, y0, z0), max = new Vector3(x1, y1, z1), openHeight = openHeight };

    private static AnchorSpec A(string name, float x, float y, float z, float fx, float fy, float fz) =>
        new AnchorSpec { name = name, pos = new Vector3(x, y, z), fwd = new Vector3(fx, fy, fz) };

    private static PortSpec P(string name, int server, int face) =>
        new PortSpec { name = name, server = server, face = face };

    private static TempSpec T(string name, string doorBox) =>
        new TempSpec { name = name, doorBox = doorBox };

    // Build 동안만 유효
    private static Transform g;                                   // Generated
    private static Dictionary<string, Transform> groups;          // Shell·EntryHall·ServerRoom·ComputerRoom·Anchors
    private static List<BuiltBox> built;
    private static Dictionary<string, Transform> byName;
    private static Dictionary<string, doorPhysics> doors;

    /// <summary>S7을 짓는다. 섹터 크기·높이가 전제(84×110, 바닥 34, 출구 34)와 다르면 아무것도 만들지 않고 false —
    /// 호출자가 빈 틀로 되돌아간다. 지형 단계에서 예외가 나면 Generated를 비우고 false(부분 생성 0 [1차 판정 8]).</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        // 1) 전제 검사 — 맨 앞, 아무것도 만들기 전 [계약 K0-2]
        if (generated == null || def == null)
        {
            Debug.LogError($"{LogTag} generated 또는 def가 null — S7 구체화를 건너뛴다.");
            return false;
        }
        if (!Mathf.Approximately(def.width, SectorWidth) || !Mathf.Approximately(def.length, SectorLength)
            || !Mathf.Approximately(def.floorHeight, FloorHeight) || !Mathf.Approximately(def.exitHeight, ExitHeight))
        {
            Debug.LogError($"{LogTag} 섹터7 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                           $"(전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}, 계약 K0-2). S7 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        groups = new Dictionary<string, Transform>();
        built = new List<BuiltBox>();
        byName = new Dictionary<string, Transform>();
        doors = new Dictionary<string, doorPhysics>();
        S7_Refs refs = null;
        try
        {
            // 2) 바닥 겹침 검사 범위를 이 섹터로
            Map4Build.BeginSection();

            // 3) 지형·(문)·앵커·TEMP 표지 + refs
            refs = new S7_Refs();
            groups["Shell"] = NewGroup("S7_Shell");
            groups["EntryHall"] = NewGroup("S7_EntryHall");
            groups["ServerRoom"] = NewGroup("S7_ServerRoom");
            groups["ComputerRoom"] = NewGroup("S7_ComputerRoom");
            groups["Anchors"] = NewGroup("S7_Anchors");
            refs.entryHall = groups["EntryHall"];
            refs.serverRoom = groups["ServerRoom"];
            refs.computerRoom = groups["ComputerRoom"];

            BuildBoxes();
            if (TempOpenDoors)
            {
                // [판정 17-S7 · 계약 K7-0] 문을 만들지 않는다 — 개구부는 비우고 refs 문 = null(K0-2 non-null 규칙의 유일한 예외)
                refs.entryDoor = null;
                refs.exitDoor = null;
            }
            else
            {
                BuildDoors();
                refs.entryDoor = NeedDoor("GEO_S7_Door_Entry");
                refs.exitDoor = NeedDoor("GEO_S7_Door_Exit");
            }
            refs.glassWalls = new List<Transform> { Need("GEO_S7_Glass_1") };

            refs.servers = new List<Transform>();
            BuildServers(refs.servers);

            // [판정 15 · 계약 K7-1] serverRoomBounds = 서버실만(컴퓨터실·유리 제외): min x = 유리 동쪽 면 GlassX1(−25.5),
            // 외벽·칸막이·북벽 안쪽 면, y 0~14 → (−25.5,0,14.5)~(41.5,14,109.5) (초안 §3-3).
            // 서버실이 L자라 AABB 하나로 전체를 덮지 못해 서쪽 띠(x −41.5~−25.5 × z 14.5~19.5·48.5~109.5)가 빠진다 —
            // 초안 §7-7, 보고서 reviewRequests. computerRoomBounds = 벽·유리 안쪽 면 (−41.5,0,20)~(−26,14,48).
            refs.serverRoomBounds = MinMax(new Vector3(GlassX1, 0f, PartitionZ1), new Vector3(InnerX, RoomH, ExitWallZ0));
            refs.computerRoomBounds = MinMax(new Vector3(-InnerX, 0f, CRSouthZ1), new Vector3(GlassX0, RoomH, CRNorthZ0));
            LogServerRoomStrip();

            BuildAnchors(refs);
            if (TempOpenDoors) BuildTempDoorMarkers();

            CheckSeamsAndMarkers(def);
            CheckPassages(def);
            CheckStructureGaps();
            ValidateRefs(refs);
            ReportStrayRoots();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} 섹터7 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(generated.GetChild(i).gameObject);
            Clear();
            return false;
        }

        // 4) 팀 기믹 배선(S7-W) — 예외가 나도 지형은 지우지 않는다 [계약 K0-2]
        try
        {
            S7_Wiring.Wire(generated, refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} S7_Wiring.Wire 예외 — 지형은 그대로 둔다(계약 K0-2): {e.Message}\n{e.StackTrace}");
        }

        // 5) 조명·재질(S7-L) — 반드시 Wire 뒤, 마지막 [계약 K0-2]
        try
        {
            S7_Dress.Apply(generated, refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} S7_Dress.Apply 예외 — 지형은 그대로 둔다(계약 K0-2): {e.Message}\n{e.StackTrace}");
        }

        ReportStrayRoots();
        string doorText = TempOpenDoors
            ? $"문 0(TEMP 상시 개방 [판정 17-S7] — TEMP 표지 {TempDoorMarkers.Length})"
            : $"문 2(입구 높이 {EntryDoorH}·출구 높이 {ExitDoorH})";
        Debug.Log($"{LogTag} 섹터7 구체화 완료 — 상자 {built.Count}개(서버 {refs.servers.Count}·유리 {refs.glassWalls.Count}), " +
                  $"{doorText}, 포트 앵커 {refs.portAnchors.Count}.");
        Clear();
        return true; // 6)
    }

    // ───────────────────────── 지형 ─────────────────────────

    /// <summary>상자 표를 순서대로 짓는다(문 제외 — 문은 BuildDoors).</summary>
    private static void BuildBoxes()
    {
        foreach (BoxSpec s in Boxes)
        {
            Transform parent = Group(s.group);
            switch (s.kind)
            {
                case "Floor":
                    Add(Solid(Map4Build.Floor(parent, s.min, s.max, "Floor"), s.name), s.min, s.max);
                    break;
                case "Wall":
                case "Ceil":
                case "Glass":
                    // 유리 = 솔리드 박스(Map4Build.Wall) [계약 K7-1]. 투명 재질은 L이 Visual에 입힌다.
                    Add(Solid(Map4Build.Wall(parent, s.min, s.max, "Wall"), s.name), s.min, s.max);
                    break;
                case "Desk":
                case "Console":
                    // [제안] 가구 색 = 팔레트 "Door"(어두운 회색, S1 장애물·S8 가구와 같음). 재질은 L이 Visual에 다시 입힌다.
                    Add(Solid(Map4Build.Wall(parent, s.min, s.max, "Door"), s.name), s.min, s.max);
                    break;
                case "WallOpen":
                    BuildWallWithOpening(parent, s);
                    break;
                default:
                    throw new System.Exception($"{LogTag} 상자 표 종류를 모른다: {s.kind} ({s.name})");
            }
        }
    }

    /// <summary>되돌림(TempOpenDoors = false) 때만 — 문 상자 2개를 Map4Build.Door로 짓는다(1차 판).</summary>
    private static void BuildDoors()
    {
        foreach (BoxSpec s in DoorBoxes)
        {
            if (s.kind != "Door") throw new System.Exception($"{LogTag} 문 표 종류가 Door가 아니다: {s.kind} ({s.name})");
            BuildDoor(Group(s.group), s);
        }
    }

    /// <summary>개구 벽 — 긴 축 개구(open0~open1)로 잘라 조각 _W(작은 x)·_E(큰 x) 두 개(틈 0·겹침 0은 WallWithOpenings가 보장).</summary>
    private static void BuildWallWithOpening(Transform parent, BoxSpec s)
    {
        bool alongX = (s.max.x - s.min.x) >= (s.max.z - s.min.z);
        if (!alongX) throw new System.Exception($"{LogTag} 개구 벽은 X축 방향만 쓴다: {s.name}");
        List<Vector2> openings = new List<Vector2> { new Vector2(s.open0, s.open1) };
        Transform[] pieces = Map4Build.WallWithOpenings(parent, s.min, s.max, openings, "Wall");
        if (pieces == null || pieces.Length != 2 || pieces[0] == null || pieces[1] == null)
            throw new System.Exception($"{LogTag} 개구 벽 생성 실패: {s.name} — 조각 2개(_W·_E)가 나와야 한다.");
        Solid(pieces[0], s.name + "_W");
        Solid(pieces[1], s.name + "_E");
        Add(pieces[0], s.min, new Vector3(s.open0, s.max.y, s.max.z));
        Add(pieces[1], new Vector3(s.open1, s.min.y, s.min.z), s.max);
    }

    /// <summary>문 — Map4Build.Door(팀 doorPhysics + kinematic RB + 트리거 + 문틀 VIS, 문틀은 parent 자식). 여는 것은 W가
    /// doorPhysics public API(SetPadPressed)로 배선한다 [계약 K7-0]. 문틀 이름은 문 구분을 위해 접미사만 붙인다(접두사 유지).
    /// 되돌림(TempOpenDoors = false) 때만 불린다.</summary>
    private static void BuildDoor(Transform parent, BoxSpec s)
    {
        doorPhysics dp = Map4Build.Door(parent, s.min, s.max, s.openHeight, "Door");
        if (dp == null) throw new System.Exception($"{LogTag} 문 생성 실패: {s.name} — 위 Map4Build 로그 확인.");
        Add(Solid(dp.transform, s.name), s.min, s.max);
        doors[s.name] = dp;

        Transform frame = parent.Find("VIS_NoCollide_DoorFrame");
        if (frame != null) frame.name = "VIS_NoCollide_DoorFrame_" + s.name.Substring("GEO_S7_Door_".Length);
    }

    /// <summary>서버 6대 — 바닥까지 채운 단순 박스(콜라이더 1개), 이름 GEO_S7_Server_1..6, refs.servers 순서 = ServerCenterXZ 순서.</summary>
    private static void BuildServers(List<Transform> list)
    {
        Transform parent = Group("ServerRoom");
        for (int i = 0; i < ServerCenterXZ.Length; i++)
        {
            Vector2 c = ServerCenterXZ[i];
            Vector3 min = new Vector3(c.x - ServerW / 2f, 0f, c.y - ServerD / 2f);
            Vector3 max = new Vector3(c.x + ServerW / 2f, ServerH, c.y + ServerD / 2f);
            string name = $"GEO_S7_Server_{i + 1}";
            // [제안] 서버 색 = 팔레트 "Door"(어두운 회색). LED·발광은 L(S7_Dress)이 Visual·VIS_로 입힌다.
            Transform t = Solid(Map4Build.Wall(parent, min, max, "Door"), name);
            Add(t, min, max);
            list.Add(t);
        }
    }

    // ───────────────────────── 앵커 ─────────────────────────

    /// <summary>ANCH_S7_… 빈 GameObject(컴포넌트·콜라이더 없음) [계약 K0-3]. forward = 기믹이 바라보는 쪽, up = 월드 +Y.</summary>
    private static void BuildAnchors(S7_Refs refs)
    {
        foreach (AnchorSpec a in FixedAnchors) NewAnchor(a.name, a.pos, a.fwd);

        refs.entryPanelAnchor = Need("ANCH_S7_EntryPanel");
        refs.roleBookAnchor = Need("ANCH_S7_Role_Book");
        refs.roleComputerAnchor = Need("ANCH_S7_Role_Computer");
        refs.rolePowerAnchor = Need("ANCH_S7_Role_Power");
        refs.computerRoomDoorway = Need("ANCH_S7_CRDoorway");

        // 포트 6개 — 서버 포트 면 중심, 높이 PortY, forward = 면 바깥(플레이어 쪽) [계약 K7-1]
        refs.portAnchors = new List<Transform>();
        refs.portServerIndex = new List<int>();
        foreach (PortSpec p in Ports)
        {
            if (p.server < 0 || p.server >= ServerCenterXZ.Length)
                throw new System.Exception($"{LogTag} 포트 {p.name}의 서버 인덱스 {p.server}가 범위 밖이다.");
            if (p.face != -1 && p.face != 1)
                throw new System.Exception($"{LogTag} 포트 {p.name}의 면 {p.face}는 −1 또는 +1이어야 한다.");
            Vector2 c = ServerCenterXZ[p.server];
            Vector3 pos = new Vector3(c.x, PortY, c.y + p.face * ServerD / 2f);
            refs.portAnchors.Add(NewAnchor(p.name, pos, new Vector3(0f, 0f, p.face)));
            refs.portServerIndex.Add(p.server);
        }
    }

    /// <summary>[판정 17-S7 · 계약 K0-3·K7-0] TEMP 표지 — 빈 GameObject(컴포넌트·콜라이더 0), 위치 = 대응 문 상자 아랫면 중심,
    /// forward +Z. 부모 S7_Anchors. 구조물 목록(built)에는 넣지 않는다.</summary>
    private static void BuildTempDoorMarkers()
    {
        foreach (TempSpec t in TempDoorMarkers)
            NewAnchor(t.name, TempMarkerPos(t), Vector3.forward);
    }

    /// <summary>TEMP 표지 위치 = DoorBoxes 중 같은 이름 상자의 아랫면 중심((min.x+max.x)/2, min.y, (min.z+max.z)/2).</summary>
    private static Vector3 TempMarkerPos(TempSpec t)
    {
        BoxSpec d = FindDoorBox(t.doorBox);
        return new Vector3((d.min.x + d.max.x) / 2f, d.min.y, (d.min.z + d.max.z) / 2f);
    }

    private static BoxSpec FindDoorBox(string name)
    {
        foreach (BoxSpec d in DoorBoxes)
            if (d.name == name) return d;
        throw new System.Exception($"{LogTag} 문 상자 표에 없다: {name}");
    }

    private static Transform NewAnchor(string name, Vector3 localPos, Vector3 localFwd)
    {
        if (byName.ContainsKey(name)) throw new System.Exception($"{LogTag} 앵커 이름 중복: {name}");
        if (localFwd.sqrMagnitude < 1e-8f) throw new System.Exception($"{LogTag} 앵커 방향이 0: {name}");
        GameObject go = new GameObject(name);
        go.transform.SetParent(Group("Anchors"), false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.LookRotation(localFwd.normalized, Vector3.up);
        byName[name] = go.transform;
        return go.transform;
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>입구 z0·출구 z=length 윗면 = 0 / exitHeight − floorHeight(로컬 0), 턱 0·틈 0(±0.02) [계약 K0-2].
    /// 평지 섹터라 경사로·계단 없음(Map4Build.Ramp/Stairs 미사용). 마커 기본 위치 아래 윗면이 있고, 마커가 고체 안에 없는지 확인.</summary>
    private static void CheckSeamsAndMarkers(Map4Layout.SectorDef def)
    {
        float rise = def.exitHeight - def.floorHeight;
        BuiltBox floor = FindBuilt("GEO_S7_Floor_Main");
        if (Mathf.Abs(floor.max.y - rise) > SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 윗면 {floor.max.y} ≠ 로컬 출구 높이 {rise} (턱 {floor.max.y - rise:F3}).");
        if (Mathf.Abs(floor.max.y) > SeamTolerance)
            throw new System.Exception($"{LogTag} 입구 윗면 {floor.max.y} ≠ 0 (턱).");
        if (Mathf.Abs(floor.max.z - def.length) > SeamTolerance || Mathf.Abs(floor.min.z) > SeamTolerance)
            throw new System.Exception($"{LogTag} 바닥 z 범위 {floor.min.z}~{floor.max.z}가 입구 0·출구 {def.length}와 맞지 않는다(틈).");
        if (floor.min.x > -OpeningHalfW || floor.max.x < OpeningHalfW)
            throw new System.Exception($"{LogTag} 바닥 x 범위 {floor.min.x}~{floor.max.x}가 개구 x ±{OpeningHalfW}를 덮지 않는다.");

        // [계약 K0-2] 마커 기본 위치 — MarkerDefaults 5개 + Exit (0, rise + 0.1, length)
        List<Vector3> markers = new List<Vector3>(MarkerDefaults) { new Vector3(0f, rise + ExitMarkerLift, def.length) };
        foreach (Vector3 m in markers)
        {
            bool onFloor = m.x >= floor.min.x && m.x <= floor.max.x && m.z >= floor.min.z - SeamTolerance && m.z <= floor.max.z + SeamTolerance
                           && m.y >= floor.max.y - SeamTolerance;
            if (!onFloor) throw new System.Exception($"{LogTag} 마커 기본 위치 {m} 아래에 바닥 윗면이 없다.");
            foreach (BuiltBox b in built)
            {
                if (b.name == floor.name) continue;
                if (m.x > b.min.x && m.x < b.max.x && m.y > b.min.y && m.y < b.max.y && m.z > b.min.z && m.z < b.max.z)
                    throw new System.Exception($"{LogTag} 마커 기본 위치 {m}가 고체 '{b.name}' 안에 있다.");
            }
        }
    }

    /// <summary>통로 폭 [계산] — 개구·책상↔유리·책상↔서벽이 도형 통과 규격(Map4Build.ShapeMinPassageWidth) 이상인지(F1-3).
    /// TEMP 기간에는 두 문 개구부(칸막이 z14·북벽 z110, x −4~4)도 검사하고, 그 부피 안에 구조물이 없는지(비어 있는지) 확인한다
    /// [계약 K7-0]. 되돌림 때 문 열린 폭은 Map4Build.Door가 스스로 검사한다.</summary>
    private static void CheckPassages(Map4Layout.SectorDef def)
    {
        float req = Map4Build.ShapeMinPassageWidth;
        Map4Build.CheckPassageWidth(OpeningHalfW * 2f, req, "S7 입구 z0·출구 z110 개구");
        Map4Build.CheckPassageWidth(CRDoorX1 - CRDoorX0, req, "S7 컴퓨터실 개구부");
        Map4Build.CheckPassageWidth(GlassX0 - DeskX1, req, "S7 책상↔유리벽");
        Map4Build.CheckPassageWidth(DeskX0 - (-InnerX), req, "S7 책상↔컴퓨터실 서벽");
        if (TempOpenDoors)
        {
            foreach (BoxSpec d in DoorBoxes)
            {
                // 개구 폭 = 문 상자 수평 넓은 축(Map4Build.Door의 열린 폭 검사와 같은 기준)
                Map4Build.CheckPassageWidth(Mathf.Max(d.max.x - d.min.x, d.max.z - d.min.z), req, $"S7 TEMP 개구({d.name} 자리, 문 없음 [판정 17-S7])");
                BuiltBox hole = new BuiltBox { name = d.name, min = d.min, max = d.max };
                foreach (BuiltBox b in built)
                {
                    Vector3 s = Separation(hole, b);
                    if (s.x < -SeamTolerance && s.y < -SeamTolerance && s.z < -SeamTolerance)
                        throw new System.Exception($"{LogTag} TEMP 개구({d.name} 자리)에 구조물 '{b.name}'이 있다 — 개구부가 비어 있어야 한다 [판정 17-S7].");
                }
            }
        }
        if (!Mathf.Approximately(def.connectorWidthToNext, OpeningHalfW * 2f))
            Debug.LogWarning($"{LogTag} 연결 통로 폭 {def.connectorWidthToNext} ≠ 출구 개구 폭 {OpeningHalfW * 2f} — 출구 이음매 확인 필요.");
    }

    /// <summary>구조물 쌍 검사 — 겹침 0, 끼임 틈 0 &lt; g &lt; 1.0 없음(규격 11; 되돌림 때는 문 닫힘 상태). 위반은 에러 로그만(생성 유지 —
    /// 수치는 초안 검산 S7_check.py와 같은 기준). Map4Build.CheckGap(F1-3)도 쌍마다 부른다.</summary>
    private static void CheckStructureGaps()
    {
        int overlaps = 0, pinches = 0;
        for (int i = 0; i < built.Count; i++)
        {
            for (int j = i + 1; j < built.Count; j++)
            {
                BuiltBox a = built[i], b = built[j];
                Vector3 s = Separation(a, b);
                if (s.x < -SeamTolerance && s.y < -SeamTolerance && s.z < -SeamTolerance)
                {
                    overlaps++;
                    Debug.LogError($"{LogTag} 구조물 겹침 — '{a.name}' ↔ '{b.name}' (관통 {-Mathf.Max(s.x, s.y, s.z):F3}U).");
                    continue;
                }
                Map4Build.CheckGap(a.min, a.max, b.min, b.max, a.name, b.name);
                float gap = new Vector3(Mathf.Max(0f, s.x), Mathf.Max(0f, s.y), Mathf.Max(0f, s.z)).magnitude;
                if (gap > SeamTolerance && gap < Map4Build.ShapeMinPassageWidth)
                {
                    pinches++;
                    Debug.LogError($"{LogTag} 끼임 틈 — '{a.name}' ↔ '{b.name}' {gap:F3}U (0 또는 ≥{Map4Build.ShapeMinPassageWidth}).");
                }
            }
        }
        Debug.Log($"{LogTag} 구조물 쌍 검사 — 상자 {built.Count}개, 겹침 {overlaps}, 끼임 틈 {pinches}.");
    }

    /// <summary>축별 분리량(양수 = 떨어짐, 음수 = 겹침 깊이).</summary>
    private static Vector3 Separation(BuiltBox a, BuiltBox b) => new Vector3(
        Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x),
        Mathf.Max(b.min.y - a.max.y, a.min.y - b.max.y),
        Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z));

    /// <summary>[판정 15 · 초안 §7-7] serverRoomBounds가 빠뜨리는 서버실 서쪽 띠 면적을 참고로 남긴다(상수에서 계산 —
    /// 초안 값 1056 / 7421 = 14.2%). 서버실 바닥 = 외벽·칸막이·북벽 안쪽 면 사각형 − 컴퓨터실 블록(남벽~북벽, 서벽~유리).</summary>
    private static void LogServerRoomStrip()
    {
        float westW = GlassX1 - (-InnerX);                               // 16
        float roomD = ExitWallZ0 - PartitionZ1;                          // 95
        float crD = CRNorthZ1 - CRSouthZ0;                               // 29
        float total = 2f * InnerX * roomD - westW * crD;                 // 7885 − 464 = 7421
        float strip = westW * (roomD - crD);                             // 1056
        Debug.Log($"{LogTag} 참고 — serverRoomBounds(서버실만 [판정 15])가 서쪽 띠 x {-InnerX}~{GlassX1} × z {PartitionZ1}~{CRSouthZ0}·{CRNorthZ1}~{ExitWallZ0}" +
                  $"(면적 {strip:F0} / 서버실 바닥 {total:F0} = {100f * strip / total:F1}%)를 덮지 않는다 — L 처리 규칙 필요(초안 §7-7, 보고 S7-B3 검토 요청).");
    }

    /// <summary>계약 K0-2·K7-0·K7-1 — 모든 필드 non-null(예외: TempOpenDoors일 때 entryDoor·exitDoor는 null이어야 하고 대신 TEMP 표지 2개
    /// 실존), 리스트 null 요소 0, 개수: servers 6·portAnchors 6·portServerIndex 6(서로 다름, 0..5, 초안 §3-3 값)·glassWalls ≥1,
    /// 책↔컴퓨터 ≥2, 포트 앵커가 해당 서버 포트 면(+Z = max z, −Z = min z) 위, Bounds 크기 0 아님,
    /// serverRoomBounds ∩ computerRoomBounds 부피 0, 서버·콘솔이 serverRoomBounds 안.</summary>
    private static void ValidateRefs(S7_Refs r)
    {
        List<string> bad = new List<string>();
        if (r.entryHall == null) bad.Add("entryHall");
        if (r.serverRoom == null) bad.Add("serverRoom");
        if (r.computerRoom == null) bad.Add("computerRoom");
        if (TempOpenDoors)
        {
            // [판정 17-S7 · 계약 K7-0] TEMP 기간: 두 문 필드만 null 통과(오히려 null이어야 한다) + TEMP 표지 2개 실존·빈 GO·위치
            if (r.entryDoor != null) bad.Add("entryDoor(TEMP 기간 null이어야 한다 [판정 17-S7])");
            if (r.exitDoor != null) bad.Add("exitDoor(TEMP 기간 null이어야 한다 [판정 17-S7])");
            foreach (TempSpec t in TempDoorMarkers) CheckTempMarker(t, bad);
        }
        else
        {
            if (r.entryDoor == null) bad.Add("entryDoor");
            if (r.exitDoor == null) bad.Add("exitDoor");
        }
        if (r.entryPanelAnchor == null) bad.Add("entryPanelAnchor");
        if (r.roleBookAnchor == null) bad.Add("roleBookAnchor");
        if (r.roleComputerAnchor == null) bad.Add("roleComputerAnchor");
        if (r.rolePowerAnchor == null) bad.Add("rolePowerAnchor");
        if (r.computerRoomDoorway == null) bad.Add("computerRoomDoorway");
        if (r.serverRoomBounds.size == Vector3.zero) bad.Add("serverRoomBounds");
        if (r.computerRoomBounds.size == Vector3.zero) bad.Add("computerRoomBounds");

        // [판정 15] 서버실 Bounds는 컴퓨터실을 포함하지 않는다 — 교집합 부피 0
        float inter = OverlapVolume(r.serverRoomBounds, r.computerRoomBounds);
        if (inter > 0f) bad.Add($"serverRoomBounds ∩ computerRoomBounds 부피 {inter:F2} ≠ 0 [판정 15]");

        CheckList(r.servers, 6, "servers", bad);                   // [계약 K7-1] 개수 6
        CheckList(r.portAnchors, 6, "portAnchors", bad);           // [계약 K7-1] 개수 6
        if (r.glassWalls == null || r.glassWalls.Count < 1 || r.glassWalls.Contains(null)) bad.Add("glassWalls(1개 이상, null 없음)");

        if (r.portServerIndex == null || r.portServerIndex.Count != 6) bad.Add("portServerIndex 개수 6");
        else
        {
            HashSet<int> seen = new HashSet<int>();
            foreach (int k in r.portServerIndex)
                if (k < 0 || k > 5 || !seen.Add(k)) { bad.Add($"portServerIndex {k}(범위 0..5·서로 다름 [확정 §3-1])"); break; }
            if (r.portServerIndex.Count == ExpectedPortServerIndex.Length)
                for (int i = 0; i < ExpectedPortServerIndex.Length; i++)
                    if (r.portServerIndex[i] != ExpectedPortServerIndex[i])
                    {
                        bad.Add($"portServerIndex[{i}] = {r.portServerIndex[i]} ≠ 초안 §3-3 {ExpectedPortServerIndex[i]} [사용자 09-29 나·안C]");
                        break;
                    }
        }

        // 포트 앵커가 해당 서버 포트 면 중심 위(x = 서버 중심, z = 면(+Z = max z / −Z = min z), forward = 면 바깥)
        if (r.portAnchors != null && r.servers != null && r.portServerIndex != null && r.portAnchors.Count == 6 && r.portServerIndex.Count == 6)
        {
            for (int i = 0; i < 6; i++)
            {
                int k = r.portServerIndex[i];
                if (k < 0 || k >= r.servers.Count || r.portAnchors[i] == null || r.servers[k] == null) continue;
                BuiltBox sv = FindBuilt(r.servers[k].name);
                Vector3 p = r.portAnchors[i].localPosition;
                Vector3 f = r.portAnchors[i].localRotation * Vector3.forward;
                bool onFace = Mathf.Abs(p.x - (sv.min.x + sv.max.x) / 2f) <= SeamTolerance
                              && (Mathf.Abs(p.z - sv.min.z) <= SeamTolerance && f.z < -0.99f
                                  || Mathf.Abs(p.z - sv.max.z) <= SeamTolerance && f.z > 0.99f)
                              && p.y > sv.min.y && p.y < sv.max.y;
                if (!onFace) bad.Add($"{r.portAnchors[i].name}가 {sv.name} 포트 면 위가 아니다");
            }
        }

        // 서버 6대·콘솔은 serverRoomBounds 안(초안 §3-3 "서버 6대·콘솔은 모두 안")
        if (r.servers != null)
            foreach (Transform s in r.servers)
                if (s != null) CheckInside(FindBuilt(s.name), r.serverRoomBounds, "serverRoomBounds", bad);
        CheckInside(FindBuilt("GEO_S7_PowerConsole"), r.serverRoomBounds, "serverRoomBounds", bad);

        if (r.roleBookAnchor != null && r.roleComputerAnchor != null
            && Vector3.Distance(r.roleBookAnchor.localPosition, r.roleComputerAnchor.localPosition) < MinRoleDistance)
            bad.Add($"roleBook↔roleComputer 거리 < {MinRoleDistance} [제안 §3-6]");

        if (bad.Count > 0)
            throw new System.Exception($"{LogTag} S7_Refs가 계약과 다르다: {string.Join(", ", bad)}");
    }

    /// <summary>TEMP 표지 — 있음, 빈 GameObject(Transform 외 컴포넌트 0, 자식 0), 위치 = 문 상자 아랫면 중심(±0.02) [계약 K0-3].</summary>
    private static void CheckTempMarker(TempSpec t, List<string> bad)
    {
        if (!byName.TryGetValue(t.name, out Transform tr) || tr == null) { bad.Add($"{t.name} 없음 [판정 17-S7]"); return; }
        if (tr.GetComponents<Component>().Length != 1 || tr.childCount != 0) bad.Add($"{t.name}가 빈 GameObject가 아니다 [계약 K0-3]");
        if ((tr.localPosition - TempMarkerPos(t)).magnitude > SeamTolerance) bad.Add($"{t.name} 위치 {tr.localPosition} ≠ {TempMarkerPos(t)}");
    }

    private static void CheckInside(BuiltBox b, Bounds bounds, string field, List<string> bad)
    {
        Vector3 lo = bounds.min, hi = bounds.max;
        if (b.min.x < lo.x - SeamTolerance || b.min.y < lo.y - SeamTolerance || b.min.z < lo.z - SeamTolerance
            || b.max.x > hi.x + SeamTolerance || b.max.y > hi.y + SeamTolerance || b.max.z > hi.z + SeamTolerance)
            bad.Add($"{b.name}가 {field} 밖");
    }

    /// <summary>두 AABB 교집합 부피(맞닿기만 하면 0).</summary>
    private static float OverlapVolume(Bounds a, Bounds b)
    {
        float dx = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
        float dy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
        float dz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
        return (dx > 0f && dy > 0f && dz > 0f) ? dx * dy * dz : 0f;
    }

    private static void CheckList(List<Transform> list, int expect, string field, List<string> bad)
    {
        if (list == null) { bad.Add(field + " null"); return; }
        if (list.Count != expect) bad.Add($"{field} 개수 {list.Count}/{expect}");
        if (list.Contains(null)) bad.Add(field + " null 요소");
    }

    /// <summary>씬 루트에 남은 오브젝트 확인(S1·S5·S8과 같은 점검) — B는 팀 메뉴를 부르지 않아 원칙적으로 0. Wire·Apply 뒤에도 한 번 더.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"{LogTag} 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>Map4Build가 돌려준 상자에 S7 이름을 붙인다(Map4Build 수정 없음, 계약 K0-3 — 반환 Transform의 name만 바꿈).
    /// 시각물 자식 "Visual"은 이름을 바꾸지 않는다 — 계약 K0-3 "자식 `Visual` 이름은 바꾸지 않는다"(S8-B와 같음).
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

    private static Transform Need(string name)
    {
        if (!byName.TryGetValue(name, out Transform t) || t == null)
            throw new System.Exception($"{LogTag} 오브젝트가 없다: {name}");
        return t;
    }

    private static doorPhysics NeedDoor(string name)
    {
        if (!doors.TryGetValue(name, out doorPhysics d) || d == null)
            throw new System.Exception($"{LogTag} 문이 없다: {name}");
        return d;
    }

    private static Transform Group(string key)
    {
        if (!groups.TryGetValue(key, out Transform t) || t == null)
            throw new System.Exception($"{LogTag} 그룹이 없다: {key}");
        return t;
    }

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
        groups = null;
        built = null;
        byName = null;
        doors = null;
    }
}
#endif
