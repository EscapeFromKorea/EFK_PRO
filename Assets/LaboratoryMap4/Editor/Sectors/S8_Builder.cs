#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 섹터8 "약물 조합(어두운 서고)" 빌더 — 과제 S8-B3(스테이징, 1차 S8-B의 2차 반영). 설계/S8_설계.md(§3 결정표, 09-28 확정)와
/// 진행/초안/S8_배치초안.md(2차 반영·수정 초안1)의 좌표를 섹터 씬 Generated/ 아래에 짓는다 — 같은 폴더의 .json은 1차 판이라 원천 아님.
/// Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry(통합 때 INT-1이 case 8 추가 [판정 24])를 거쳐 부르고,
/// 마커(Manual/Markers)는 공용 코드(BuildConnectorAndMarkers)가 이어서 만든다. S8은 마지막 섹터라 연결 통로가 없다
/// (Map4SceneBuilder.BuildConnectorAndMarkers — next == null).
///
/// [범위 — 계약 K8-1] 지형(바닥·벽·천장·입구·출구 인방), 벽 쪽 책장 줄 A·B + 대기실 가림 줄 + 북쪽 줄, 가운데 작업대 격자,
/// 콘솔 책상·천장 [판정 19], 출구 문(Map4Build.Door → refs.exitDoor, 서고 출구 벽 z131.55~131.95), 탈출 공간(z132~144, 섹터 안,
/// 항상 지음 [판정 18]), 앵커(ANCH_S8_…, 이름 규약 앵커 ANCH_S8_Key 포함 [판정 19]).
/// 팀 기믹(관리자·CCTV·역할 슬롯·혼합·열쇠 문)은 만들지 않는다 — S8_Wiring.Wire(W). 조명·재질은 S8_Dress.Apply(L).
/// 여기서는 Map4Palette 이름만 쓴다(Floor·Wall·Door). 팀 코드·팀 private 필드는 건드리지 않고 팀 메뉴도 부르지 않는다.
///
/// [호출 순서 — 계약 K0-2] 1) 전제 검사 → 2) Map4Build.BeginSection → 3) 지형·문·앵커, refs 채움 → 4) S8_Wiring.Wire →
/// 5) S8_Dress.Apply → 6) true. 전제 불일치면 아무것도 만들지 않고 false. 3)에서 예외가 나면 Generated를 비우고 false
/// (부분 생성 0 [1차 판정 8]). 4)·5)가 예외를 내도 지형은 지우지 않는다.
///
/// [섹터 길이 — 판정 18] 섹터 길이 144(SectorLength)는 전제 검사·탈출 공간 끝·출구 이음새·섹터 경계·Exit 마커 대조에만 쓴다.
/// 서고 끝 z는 ArchiveEndZ(132)로 따로 두며 서고 지형 좌표(z0~132)는 1차와 같다. 통합 전 에셋(길이 132)으로 Generate하면
/// 전제 불일치 → LogError + false(빈 틀)가 정상이다(계약 K0-2).
///
/// [좌표] 섹터 로컬(+Z 진행, x=0 중앙, y=0 = 섹터 바닥 = 세계 34). 모든 좌표·크기·검사 기준은 아래 "상수 표" 한 곳에 있다.
/// 출처 표기: [확정]=설계서 확정 · [팀]=팀 코드 · [제안]=초안이 정한 값 · [계산] · [추정] · [판정 n]=진행/판정/2026-09-28_S6S8_판정.md ·
/// [1차 판정 n]=2026-09-28_1차_판정.md · [계약 Kx]=진행/지시서/R2S68/_계약.md 조항 · [해석] · 초안 항목 이름은
/// S8_배치초안.md §2(상자 #)·§3(앵커) 행.
/// </summary>
public static class S8_Builder
{
    // ═══════════════════════════════ 상수 표 (좌표·크기·검사 기준은 여기 한 곳) ═══════════════════════════════

    // ── 전제 [계약 K0-2] 통합 후 SectorSource·Map4Layout.asset 값 88·144·34·34. 비교는 Mathf.Approximately ──
    private const float SectorWidth = 88f;          // [계약 K0-2] 폭 88
    private const float SectorLength = 144f;        // [판정 18 · 계약 K0-2] 길이 144 — 전제 검사·탈출 공간 끝·출구 이음새·섹터 경계·Exit 마커에만 쓴다
    private const float FloorHeight = 34f;          // [계약 K0-2] 바닥 높이 34(세계)
    private const float ExitHeight = 34f;           // [계약 K0-2] 출구 높이 34(세계) → 로컬 출구 윗면 0

    // ── 틀 (초안 §1-1) ──
    private const float HalfW = SectorWidth / 2f;   // [계산] 44
    private const float ArchiveEndZ = 132f;         // [계약 K8-0] 서고 끝 z(섹터 길이와 분리) — 서고 바닥·옆벽·출구 벽·인방·천장은 이 값을 쓴다(초안 ARCHIVE_END_Z)
    private const float WallT = 0.5f;               // [제안] 벽 두께 — 섹터 안쪽에 둔다(서고 안쪽 x −43.5~43.5, z 0.5~131.5)
    private const float RoomH = 8f;                 // [제안] 벽·천장 높이 8 (초안 constants.ROOM_H)
    private const float CeilT = 0.5f;               // [판정 19] 천장 y 8~8.5 — 어둡게 하는 효과는 (추정), L 실측
    private const float FloorT = 0.3f;              // [제안] 바닥 판 두께(공용 코드 바닥과 같게)
    private const float OpeningHalfW = 4f;          // [계약 K0-2] 입구·출구 개구 x −4~4
    private const float DoorH = 4f;                 // [제안] 출구 문 높이 4 (초안 constants.DOOR_H, 팀 테스트 방 문 높이 4)
    private const float DoorZ0 = 131.55f;           // [제안·수정 1] 서고 출구 벽 안 문 두께 0.4(z 131.55~131.95) — 열린 문이 인방·천장 안에 숨게(C15) [계약 K8-0]
    private const float DoorZ1 = 131.95f;           // [제안·수정 1]
    private const float DoorOpenHeight = 4.5f;      // [팀] Ch8TestRoomMenuItem.cs:135 doorTargetYOffset 4.5 (초안 constants.DOOR_OPEN, 계약 K8-1)
    private const float EscapeLen = 12f;            // [제안 · 판정 18] 탈출 공간 길이 — Build가 ArchiveEndZ + EscapeLen == SectorLength를 대조한다(다르면 예외)

    // ── 책장 (초안 §1-2·§1-3·§1-5) — 높이 4.5는 관리자 눈(0.5 [판정 17])·참가자를 가린다. 바닥에서 못 오르는 근거:
    //    최대 점프 세모 jumpHeight 2.0 [팀 TetrahedronStats.asset] + 도형 높이(약 1U) < 4.5 (추측) [수정 1] ──
    private const float ShelfH = 4.5f;              // [제안] (초안 constants.SHELF_H)
    private const float ShelfDepth = 1.5f;          // [제안] 책장 두께
    private const float FoyerShelfZ0 = 12f;         // [제안] 대기실 가림 줄 z 12~13.5, 개구 x −4~4
    private const float ShelfInnerX = 30.5f;        // [제안] 벽 쪽 책장 줄 A(서)·B(동) 안쪽 끝 |x|
    private const float ShelfOuterX = 40.5f;        // [제안] 바깥 끝 |x| — 벽 안쪽 면(43.5)까지 3.0 = 책장 통로 A/B
    private const float ShelfRowZ0 = 22.5f;         // [제안] 첫 책장 z 22.5
    private const float ShelfPitch = 4.5f;          // [제안] 피치 4.5 → 책장 사이 통로 3.0 [계산]
    private const int ShelfRowCount = 20;           // [제안] 줄마다 20개 → 마지막 z 108~109.5
    private const float NorthShelfZ0 = 118f;        // [제안] 북쪽 줄 z 118~119.5
    private const float NorthGapHalfW = 8f;         // [제안] 북쪽 줄 가운데 출구 길 x −8~8

    // ── 작업대 격자 (초안 §1-4) — 5×2·높이 1.0, 열 6 × 행 16, 틈 3.0, 빈터 4칸 ──
    private const float TableW = 5f;                // [제안] x 길이
    private const float TableD = 2f;                // [제안] z 깊이
    private const float TableH = 1f;                // [제안] 높이 1.0 (계약 K8-1 ≥0.75, 초안 constants.TABLE_H)
    private static readonly float[] TableColX0 = { -23.5f, -15.5f, -7.5f, 2.5f, 10.5f, 18.5f }; // [제안] 열 C1..C6 서쪽 끝 x
    private static readonly float[] TableBlockZ0 = { 26.5f, 68.5f }; // [제안] 행 R01..R08 · R09..R16 첫 행 z (사이 z 63.5~68.5 = 가로 통로 폭 5)
    private const int TableRowsPerBlock = 8;        // [제안]
    private const float TablePitchZ = 5f;           // [제안] 행 피치 5 → 행 사이 틈 3.0 [계산]
    // [제안] 빈터: 행 5·6 × 열 3·4(z 46.5~53.5, x −7.5~7.5) — 책·혼합 자리 앞을 연다. (행, 열) 1부터.
    private static readonly Vector2Int[] TableOmit = { new Vector2Int(5, 3), new Vector2Int(5, 4), new Vector2Int(6, 3), new Vector2Int(6, 4) };

    // ── 콘솔 책상 (초안 §1-2) — 대기실 남서 구석, 입구 벽·서쪽 벽에 붙음(틈 0). GEO 승인 [판정 19] ──
    private const float ConsoleDeskW = 1f;          // [제안] x
    private const float ConsoleDeskH = 1f;          // [제안] y
    private const float ConsoleDeskD = 2f;          // [제안] z

    // ── 역할 자리 (초안 §1-4) ──
    // [판정 20] 역할 앵커 x ±4.0(C9d 위반 해소) → 책 (−4,0,56.25)·혼합 (4,0,56.25), 간격 8.0. 간격 8.0은 ±4.0 지정의 결과이고,
    // 판정 20이 '서로 가깝게'로 인정한 값은 7.0이다(8.0이 말이 들리는 거리인지는 사람 플레이 확인, 초안 §9 N8). 설계 "약 2 이상" [확정 §3-9].
    private const float RoleAnchorX = 4.0f;
    private const float RoleAnchorZ = 56.25f;       // [제안] 작업대 R07 남면(z 56.5)에서 0.25 앞, forward −Z

    // ── 순찰로 (초안 §1-6) — 8점, 순서 = 순찰 순서. refs에는 WP_1..WP_8을 한 번씩만 담는다(닫힘용 WP_1 재삽입은 W 몫 [판정 19]) ──
    // [판정 17] y = 관리자 피벗 = 바닥 0. 팀 eyeHeight 0.5(ManagerAgent.cs) → 눈 0.5(설계 §3-8 전제). 모양용 Visual 자식 +1은 W 몫.
    private const float PatrolPivotY = 0f;
    private static readonly Vector2[] PatrolXZ =    // [제안] (x, z)
    {
        new Vector2(-42f, 20f),   // WP_1 = 관리자 시작 — 책장 통로 A 북진
        new Vector2(-42f, 112f),  // WP_2 — 북쪽 가로(동진)
        new Vector2(42f, 112f),   // WP_3 — 책장 통로 B 남진
        new Vector2(42f, 20f),    // WP_4 — 남쪽 가로(서진)
        new Vector2(0f, 20f),     // WP_5 — 가운데 통로 북진
        new Vector2(0f, 66f),     // WP_6 — z66 가로(서진)
        new Vector2(-27f, 66f),   // WP_7 — 서쪽 안쪽 통로 남진
        new Vector2(-27f, 20f),   // WP_8 — 남쪽 가로(서진) → (W가 끝에 WP_1을 재삽입해 닫음 [판정 19])
    };

    // ── L 조명 범위 (초안 §3 Refs 값) ──
    private const float WorkZoneLightH = 3f;        // [제안] workZoneBounds 높이 y 0~3 (초안 refs.workZoneBounds)

    // ── 상자 표 (초안 §2 좌표표, min/max 섹터 로컬) ──
    // 종류: Floor → Map4Build.Floor · Wall/Ceil → Map4Build.Wall · WallOpen → Map4Build.WallWithOpenings(개구 x −4~4, 조각 L·R)
    //       Door → Map4Build.Door · Shelf/Table/Desk → Map4Build.Wall(바닥까지 채운 단순 박스, 계약 K8-1)
    private static readonly BoxSpec[] FrameBoxes =
    {
        B("GEO_S8_Floor_Main", "Floor", -HalfW, -FloorT, 0f, HalfW, 0f, ArchiveEndZ),                                                    // #1 [제안] 서고 바닥 1장(z0~132)
        B("GEO_S8_Wall_Entrance", "WallOpen", -HalfW, 0f, 0f, HalfW, RoomH, WallT),                                                      // #3·#4 [계약 K0-2] 입구 개구 x −4~4(위는 천장까지 열림)
        B("GEO_S8_Wall_SideL", "Wall", -HalfW, 0f, WallT, -HalfW + WallT, RoomH, ArchiveEndZ - WallT),                                   // #5 [제안]
        B("GEO_S8_Wall_SideR", "Wall", HalfW - WallT, 0f, WallT, HalfW, RoomH, ArchiveEndZ - WallT),                                     // #6 [제안]
        B("GEO_S8_Wall_Exit", "WallOpen", -HalfW, 0f, ArchiveEndZ - WallT, HalfW, RoomH, ArchiveEndZ),                                   // #7·#8 [계약 K0-2] 서고 출구 벽 개구 x −4~4
        B("GEO_S8_Wall_ExitLintel", "Wall", -OpeningHalfW, DoorH, ArchiveEndZ - WallT, OpeningHalfW, RoomH, ArchiveEndZ),                // #9 [제안] 문 위 인방
        B("GEO_S8_Door_Exit", "Door", -OpeningHalfW, 0f, DoorZ0, OpeningHalfW, DoorH, DoorZ1),                                           // #10 [계약 K8-1] Map4Build.Door — 서고 출구 벽 안, 뒤는 탈출 공간 [판정 18]
        B("GEO_S8_Ceil_Main", "Ceil", -HalfW, RoomH, 0f, HalfW, RoomH + CeilT, ArchiveEndZ),                                             // #11 [판정 19] 천장
    };

    private static readonly BoxSpec[] EscapeBoxes = // [판정 18] 탈출 공간 — 섹터 안 z132~144, 항상 짓는다
    {
        B("GEO_S8_Floor_Escape", "Floor", -OpeningHalfW, -FloorT, ArchiveEndZ, OpeningHalfW, 0f, SectorLength),                                  // #2 [제안·판정 18] 문 뒤 탈출 공간 바닥
        B("GEO_S8_Wall_EscapeL", "Wall", -OpeningHalfW - WallT, 0f, ArchiveEndZ, -OpeningHalfW, RoomH, SectorLength),                            // #12 [제안·판정 18]
        B("GEO_S8_Wall_EscapeR", "Wall", OpeningHalfW, 0f, ArchiveEndZ, OpeningHalfW + WallT, RoomH, SectorLength),                              // #13 [제안·판정 18]
        B("GEO_S8_Wall_EscapeEnd", "Wall", -OpeningHalfW - WallT, 0f, SectorLength, OpeningHalfW + WallT, RoomH, SectorLength + WallT),          // #14 [제안·계약 K8-0] 섹터 경계 z144 바깥 두께만(경계 밖 예외 1건 — 확인 요청 4)
        B("GEO_S8_Ceil_Escape", "Ceil", -OpeningHalfW - WallT, RoomH, ArchiveEndZ, OpeningHalfW + WallT, RoomH + CeilT, SectorLength),           // #15 [판정 19·계약 K8-0] 천장 z132~144(섹터 안, 초안 N1)
    };

    private static readonly BoxSpec[] FoyerShelves = // refs.shelves 맨 앞 2개
    {
        B("GEO_S8_Shelf_FoyerL", "Shelf", -HalfW + WallT, 0f, FoyerShelfZ0, -OpeningHalfW, ShelfH, FoyerShelfZ0 + ShelfDepth),  // #16 [제안] 대기실 가림
        B("GEO_S8_Shelf_FoyerR", "Shelf", OpeningHalfW, 0f, FoyerShelfZ0, HalfW - WallT, ShelfH, FoyerShelfZ0 + ShelfDepth),    // #17 [제안]
    };

    private static readonly BoxSpec[] NorthShelves = // refs.shelves 맨 뒤 2개
    {
        B("GEO_S8_Shelf_NorthL", "Shelf", -ShelfOuterX, 0f, NorthShelfZ0, -NorthGapHalfW, ShelfH, NorthShelfZ0 + ShelfDepth),  // #58 [제안] 재우기 전 숨는 줄
        B("GEO_S8_Shelf_NorthR", "Shelf", NorthGapHalfW, 0f, NorthShelfZ0, ShelfOuterX, ShelfH, NorthShelfZ0 + ShelfDepth),    // #59 [제안]
    };

    private static readonly BoxSpec[] ConsoleBoxes = // K8-1 필드 밖 지형(서고 그룹 자식만) [판정 19]
    {
        B("GEO_S8_Desk_Console", "Desk", -HalfW + WallT, 0f, WallT, -HalfW + WallT + ConsoleDeskW, ConsoleDeskH, WallT + ConsoleDeskD), // #152 [판정 19] 콘솔 받침(크기 [제안])
    };

    // ── 앵커 표 (초안 §3) — 위치 = 섹터 로컬, forward(없으면 lookAt 방향) ──
    private static readonly AnchorSpec[] FixedAnchors =
    {
        A("ANCH_S8_Start", 0f, 0f, 5f, 0f, 0f, 1f),                          // [제안] 전원 복귀 중심, forward +Z. 시작점 3개 = 이 점과 right×±2.5(W 파생) [판정 19]
        A("ANCH_S8_Role_Book", -RoleAnchorX, 0f, RoleAnchorZ, 0f, 0f, -1f),  // [판정 20] x −4.0 · 작업대 R07_C3 남면(빈터 쪽) [제안]
        A("ANCH_S8_Role_Mixer", RoleAnchorX, 0f, RoleAnchorZ, 0f, 0f, -1f),  // [판정 20] x +4.0 · 작업대 R07_C4 남면, 책과 8.0
        A("ANCH_S8_Role_Monitor", -42.25f, 0f, 1.5f, 1f, 0f, 0f),            // [판정 20] 대기실 남서 구석 콘솔 책상 앞 = '벽 쪽 구석 책장 줄 뒤' 인정
        A("ANCH_S8_ExitDoor", 0f, 0f, 130f, 0f, 0f, 1f),                     // [제안] 문 안쪽 면(z 131.55)에서 1.55 ≤ useRadius 2.5 [팀 ManagerKeyDoor.cs:23]
        A("ANCH_S8_Key", 0f, 0.8f, 124f, 0f, 0f, 1f),                        // [판정 19] 이름 규약 앵커(Refs 필드 아님, 계약 K0-3·K8-1) — ManagerKeyPoint 자리, 좌표 [제안]. W가 이름으로 찾는다
    };

    private static readonly CameraSpec[] CameraAnchors = // 순서 = refs.cctvCameraAnchors [0]통로 A [1]통로 B [2]작업대 구역 [계약 K8-1]
    {
        Cam("ANCH_S8_Cam_1", -43f, 6.5f, 14.5f, -42f, 1f, 72f),   // [제안] 책장 통로 A — 남쪽 끝에서 북쪽(lookAt (−42,1,72))
        Cam("ANCH_S8_Cam_2", 43f, 6.5f, 117.5f, 42f, 1f, 60f),    // [제안] 책장 통로 B — 북쪽 끝에서 남쪽
        Cam("ANCH_S8_Cam_3", 43f, 7.5f, 14.5f, 20f, 0f, 45f),     // [판정 21] 가운데 작업대 구역 전체 — 대안 포즈(남동 구석 위 → 북서), forward (−0.5908,−0.1927,0.7835) [계산]
    };

    // ── 검사 기준 ──
    private const float SeamTolerance = 0.02f;      // [계약 K0-2] 턱·틈 허용 ±0.02
    private const float PinchSafeGap = 2.5f;        // [확정 §3-8] 구조물끼리·구조물↔벽 틈 0 또는 ≥2.5
    private const float ExitMarkerLift = 0.1f;      // [계약 K0-2] Exit 마커 기본 위치 (0, rise+0.1, length)
    private const float RoleMinSeparation = 2f;     // [확정 §3-9] 책↔혼합 거리 ≥2 (계약 K8-1)
    private const float TableMinHeight = 0.75f;     // [계약 K8-1] 작업대 높이 ≥0.75
    private const int CctvCameraCount = 3;          // [계약 K8-1] cctvCameraAnchors 개수 3
    // [계약 K8-0 · 확인 요청 4] 섹터 경계(x ±HalfW·z 0~length) 밖 예외 — 이름을 적어 Debug.Log로만 남긴다
    private static readonly string[] SectorBoundsExceptions = { "GEO_S8_Wall_EscapeEnd" };
    // [판정 23 · 계약 K8-0] 기대하는 '채워진 틈' 쌍(각 0.05) — 이 밖의 쌍이 채워짐으로 나오면 LogError
    private static readonly string[,] ExpectedFilledPairs =
    {
        { "GEO_S8_Door_Exit", "GEO_S8_Floor_Escape" },
        { "GEO_S8_Door_Exit", "GEO_S8_Wall_EscapeL" },
        { "GEO_S8_Door_Exit", "GEO_S8_Wall_EscapeR" },
    };

    // ═══════════════════════════════ 여기까지 상수 표 ═══════════════════════════════

    private const string LogTag = "[S8_Builder]";

    private struct BoxSpec
    {
        public string name, kind;
        public Vector3 min, max;
    }

    private struct AnchorSpec
    {
        public string name;
        public Vector3 pos, fwd;
    }

    private struct CameraSpec
    {
        public string name;
        public Vector3 pos, lookAt;
    }

    private struct BuiltBox
    {
        public string name;
        public Vector3 min, max;
    }

    private static BoxSpec B(string name, string kind, float x0, float y0, float z0, float x1, float y1, float z1) =>
        new BoxSpec { name = name, kind = kind, min = new Vector3(x0, y0, z0), max = new Vector3(x1, y1, z1) };

    private static AnchorSpec A(string name, float x, float y, float z, float fx, float fy, float fz) =>
        new AnchorSpec { name = name, pos = new Vector3(x, y, z), fwd = new Vector3(fx, fy, fz) };

    private static CameraSpec Cam(string name, float x, float y, float z, float lx, float ly, float lz) =>
        new CameraSpec { name = name, pos = new Vector3(x, y, z), lookAt = new Vector3(lx, ly, lz) };

    // Build 동안만 유효
    private static Transform g;            // Generated
    private static Transform archiveRoot;  // S8_ArchiveRoom (refs.archiveRoom)
    private static Transform anchorRoot;   // S8_Anchors
    private static List<BuiltBox> built;
    private static Dictionary<string, Transform> byName;

    /// <summary>S8을 짓는다. 섹터 크기·높이가 전제(88×144, 바닥 34, 출구 34 [판정 18 · 계약 K0-2])와 다르면 아무것도 만들지 않고
    /// false — 호출자가 빈 틀로 되돌아간다. 지형 단계에서 예외가 나면 Generated를 비우고 false(부분 생성 0 [1차 판정 8]).</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        // 1) 전제 검사 — 맨 앞, 아무것도 만들기 전 [계약 K0-2]
        if (generated == null || def == null)
        {
            Debug.LogError($"{LogTag} generated 또는 def가 null — S8 구체화를 건너뛴다.");
            return false;
        }
        if (!Mathf.Approximately(def.width, SectorWidth) || !Mathf.Approximately(def.length, SectorLength)
            || !Mathf.Approximately(def.floorHeight, FloorHeight) || !Mathf.Approximately(def.exitHeight, ExitHeight))
        {
            Debug.LogError($"{LogTag} 섹터8 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                           $"(전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}, 계약 K0-2 · 판정 18). S8 구체화를 건너뛰고 빈 틀로 짓는다" +
                           " — 통합(INT-1) 전 에셋 길이 132면 이것이 정상이다.");
            return false;
        }

        g = generated;
        built = new List<BuiltBox>();
        byName = new Dictionary<string, Transform>();
        S8_Refs refs = null;
        try
        {
            // 상수 표 자체 대조 [계약 K8-0] — 서고 끝 + 탈출 공간 길이 = 섹터 길이. 아무것도 만들기 전에 본다.
            if (!Mathf.Approximately(ArchiveEndZ + EscapeLen, SectorLength))
                throw new System.Exception($"{LogTag} 상수 표 불일치 — ArchiveEndZ {ArchiveEndZ} + EscapeLen {EscapeLen} ≠ SectorLength {SectorLength} (계약 K8-0).");

            // 2) 바닥 겹침 검사 범위를 이 섹터로
            Map4Build.BeginSection();

            // 3) 지형·문·앵커 + refs
            refs = new S8_Refs();
            archiveRoot = NewGroup("S8_ArchiveRoom");
            anchorRoot = NewGroup("S8_Anchors");
            refs.archiveRoom = archiveRoot;

            BuildBoxes(FrameBoxes, null, refs);
            BuildBoxes(EscapeBoxes, null, refs); // [판정 18] 항상 짓는다

            refs.shelves = new List<Transform>();
            BuildBoxes(FoyerShelves, refs.shelves, refs);
            BuildShelfRow("A", -1f, refs.shelves);
            BuildShelfRow("B", +1f, refs.shelves);
            BuildBoxes(NorthShelves, refs.shelves, refs);

            refs.tables = new List<Transform>();
            BuildTables(refs.tables);

            BuildBoxes(ConsoleBoxes, null, refs);

            // [판정 18 · 계약 K8-1] 서고 안쪽만(z ≤ ArchiveEndZ − WallT = 131.5) — 탈출 공간 제외. SectorLength를 쓰면 안 된다.
            refs.archiveBounds = MinMax(new Vector3(-HalfW + WallT, 0f, WallT), new Vector3(HalfW - WallT, RoomH, ArchiveEndZ - WallT));
            refs.workZoneBounds = WorkZoneBounds();

            BuildAnchors(refs);

            CheckExitSeam(def);
            CheckSectorBounds(def);
            CheckExitMarker(def);
            CheckPassages();
            CheckStructureGaps();
            ValidateRefs(refs);
            ReportStrayRoots();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} 섹터8 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(generated.GetChild(i).gameObject);
            Clear();
            return false;
        }

        // 4) 팀 기믹 배선(S8-W) — 예외가 나도 지형은 지우지 않는다 [계약 K0-2]
        try
        {
            S8_Wiring.Wire(generated, refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} S8_Wiring.Wire 예외 — 지형은 그대로 둔다(계약 K0-2): {e.Message}\n{e.StackTrace}");
        }

        // 5) 조명·재질(S8-L) — 반드시 Wire 뒤, 마지막 [계약 K0-2]
        try
        {
            S8_Dress.Apply(generated, refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{LogTag} S8_Dress.Apply 예외 — 지형은 그대로 둔다(계약 K0-2): {e.Message}\n{e.StackTrace}");
        }

        ReportStrayRoots();
        Debug.Log($"{LogTag} 섹터8 구체화 완료 — 상자 {built.Count}개(책장 {refs.shelves.Count}·작업대 {refs.tables.Count}), " +
                  $"순찰점 {refs.patrolWaypoints.Count}·카메라 {refs.cctvCameraAnchors.Count}, 서고 z0~{ArchiveEndZ}·탈출 공간 z{ArchiveEndZ}~{SectorLength}(섹터 안).");
        Clear();
        return true; // 6)
    }

    // ───────────────────────── 지형 ─────────────────────────

    /// <summary>상자 표 한 묶음을 짓는다. list가 있으면 만든 순서대로 넣는다(Shelf·Table).</summary>
    private static void BuildBoxes(BoxSpec[] specs, List<Transform> list, S8_Refs refs)
    {
        foreach (BoxSpec s in specs)
        {
            switch (s.kind)
            {
                case "Floor":
                    Add(Solid(Map4Build.Floor(archiveRoot, s.min, s.max, "Floor"), s.name), s.min, s.max, list);
                    break;
                case "Wall":
                case "Ceil":
                    Add(Solid(Map4Build.Wall(archiveRoot, s.min, s.max, "Wall"), s.name), s.min, s.max, list);
                    break;
                case "Shelf":
                case "Table":
                case "Desk":
                    // [제안] 가구 색 = 팔레트 "Door"(어두운 회색, S1 장애물과 같음). 재질은 L이 Visual에 다시 입힌다.
                    Add(Solid(Map4Build.Wall(archiveRoot, s.min, s.max, "Door"), s.name), s.min, s.max, list);
                    break;
                case "WallOpen":
                    BuildWallWithOpening(s);
                    break;
                case "Door":
                    refs.exitDoor = BuildDoor(s);
                    break;
                default:
                    throw new System.Exception($"{LogTag} 상자 표 종류를 모른다: {s.kind} ({s.name})");
            }
        }
    }

    /// <summary>입구·출구 벽 — 개구 x −4~4 [계약 K0-2]로 잘라 조각 L·R 두 개(틈 0·겹침 0은 WallWithOpenings가 보장).</summary>
    private static void BuildWallWithOpening(BoxSpec s)
    {
        List<Vector2> openings = new List<Vector2> { new Vector2(-OpeningHalfW, OpeningHalfW) };
        Transform[] pieces = Map4Build.WallWithOpenings(archiveRoot, s.min, s.max, openings, "Wall");
        if (pieces == null || pieces.Length != 2 || pieces[0] == null || pieces[1] == null)
            throw new System.Exception($"{LogTag} 개구 벽 생성 실패: {s.name} — 조각 2개(L·R)가 나와야 한다.");
        Solid(pieces[0], s.name + "L");
        Solid(pieces[1], s.name + "R");
        Add(pieces[0], s.min, new Vector3(-OpeningHalfW, s.max.y, s.max.z), null);
        Add(pieces[1], new Vector3(OpeningHalfW, s.min.y, s.min.z), s.max, null);
    }

    /// <summary>출구 문 — Map4Build.Door(팀 doorPhysics + kinematic RB + 트리거 + 문틀 VIS, 문틀은 archiveRoot 자식).
    /// 서고 출구 벽 z131.55~131.95에 있고 뒤는 탈출 공간이다 [판정 18 · 계약 K8-0]. 여는 것은 W가 붙이는 팀 ManagerKeyDoor(door = refs.exitDoor).</summary>
    private static doorPhysics BuildDoor(BoxSpec s)
    {
        doorPhysics dp = Map4Build.Door(archiveRoot, s.min, s.max, DoorOpenHeight, "Door");
        if (dp == null) throw new System.Exception($"{LogTag} 출구 문 생성 실패: {s.name} — 위 Map4Build 로그 확인.");
        Add(Solid(dp.transform, s.name), s.min, s.max, null);
        return dp;
    }

    /// <summary>벽 쪽 책장 줄 A(서, side −1)·B(동, side +1) — ShelfRowCount개, z = ShelfRowZ0 + k·ShelfPitch [제안].
    /// 책장 사이 통로 3.0, 벽↔책장 끝 3.0 = 책장 통로(순찰로 x ±42) [계산].</summary>
    private static void BuildShelfRow(string row, float side, List<Transform> list)
    {
        float xLo = side < 0f ? -ShelfOuterX : ShelfInnerX;
        float xHi = side < 0f ? -ShelfInnerX : ShelfOuterX;
        for (int k = 0; k < ShelfRowCount; k++)
        {
            float z0 = ShelfRowZ0 + k * ShelfPitch;
            Vector3 min = new Vector3(xLo, 0f, z0);
            Vector3 max = new Vector3(xHi, ShelfH, z0 + ShelfDepth);
            string name = $"GEO_S8_Shelf_{row}_{k + 1:00}";
            Add(Solid(Map4Build.Wall(archiveRoot, min, max, "Door"), name), min, max, list);
        }
    }

    /// <summary>작업대 격자 — 행 우선(R01_C1 … R16_C6), 빈터 4칸 제외 [제안]. refs.tables 순서 = 만든 순서.</summary>
    private static void BuildTables(List<Transform> list)
    {
        for (int b = 0; b < TableBlockZ0.Length; b++)
        {
            for (int r = 0; r < TableRowsPerBlock; r++)
            {
                int row = b * TableRowsPerBlock + r + 1;
                float z0 = TableBlockZ0[b] + r * TablePitchZ;
                for (int c = 0; c < TableColX0.Length; c++)
                {
                    int col = c + 1;
                    if (IsOmitted(row, col)) continue;
                    Vector3 min = new Vector3(TableColX0[c], 0f, z0);
                    Vector3 max = new Vector3(TableColX0[c] + TableW, TableH, z0 + TableD);
                    string name = $"GEO_S8_Table_R{row:00}_C{col}";
                    Add(Solid(Map4Build.Wall(archiveRoot, min, max, "Door"), name), min, max, list);
                }
            }
        }
    }

    private static bool IsOmitted(int row, int col)
    {
        foreach (Vector2Int o in TableOmit)
            if (o.x == row && o.y == col) return true;
        return false;
    }

    private static Bounds WorkZoneBounds()
    {
        // [제안] 작업대 격자 외곽(x −23.5~23.5, z 26.5~105.5) × y 0~3 — 초안 refs.workZoneBounds와 같은 값 [계산]
        float xMin = float.MaxValue, xMax = float.MinValue;
        foreach (float x0 in TableColX0) { xMin = Mathf.Min(xMin, x0); xMax = Mathf.Max(xMax, x0 + TableW); }
        float zMin = TableBlockZ0[0];
        float zMax = TableBlockZ0[TableBlockZ0.Length - 1] + (TableRowsPerBlock - 1) * TablePitchZ + TableD;
        return MinMax(new Vector3(xMin, 0f, zMin), new Vector3(xMax, WorkZoneLightH, zMax));
    }

    // ───────────────────────── 앵커 ─────────────────────────

    /// <summary>ANCH_S8_… 빈 GameObject(컴포넌트·콜라이더 없음) [계약 K0-3]. forward = 기믹이 바라보는 쪽, up = 월드 +Y.
    /// ANCH_S8_Key는 Refs 필드 없이 S8_Anchors 아래 이름으로만 넘긴다(이름 규약 앵커 [판정 19]).</summary>
    private static void BuildAnchors(S8_Refs refs)
    {
        foreach (AnchorSpec a in FixedAnchors) NewAnchor(a.name, a.pos, a.fwd);

        refs.startAnchor = Need("ANCH_S8_Start");
        refs.roleBookAnchor = Need("ANCH_S8_Role_Book");
        refs.roleMixerAnchor = Need("ANCH_S8_Role_Mixer");
        refs.roleMonitorAnchor = Need("ANCH_S8_Role_Monitor");
        refs.exitDoorAnchor = Need("ANCH_S8_ExitDoor");
        Need("ANCH_S8_Key"); // 이름 규약 앵커가 실제로 생겼는지만 확인(필드 없음)

        // 순찰점 — 각 1회(WP_1 재삽입 없음 [판정 19]). forward = 다음 점 방향(마지막은 첫 점 방향). y = 피벗 = 바닥 [판정 17].
        refs.patrolWaypoints = new List<Transform>();
        for (int i = 0; i < PatrolXZ.Length; i++)
        {
            Vector2 p = PatrolXZ[i], q = PatrolXZ[(i + 1) % PatrolXZ.Length];
            Vector3 pos = new Vector3(p.x, PatrolPivotY, p.y);
            Vector3 fwd = new Vector3(q.x - p.x, 0f, q.y - p.y);
            refs.patrolWaypoints.Add(NewAnchor($"ANCH_S8_WP_{i + 1}", pos, fwd));
        }

        // 관리자 시작 = WP_1 자리(−42,0,20), 첫 구간 방향(+Z) [판정 17 · 계약 K8-1]
        Transform wp1 = refs.patrolWaypoints[0];
        refs.managerSpawnAnchor = NewAnchor("ANCH_S8_Manager", wp1.localPosition, wp1.localRotation * Vector3.forward);

        refs.cctvCameraAnchors = new List<Transform>();
        foreach (CameraSpec c in CameraAnchors)
            refs.cctvCameraAnchors.Add(NewAnchor(c.name, c.pos, c.lookAt - c.pos));
    }

    private static Transform NewAnchor(string name, Vector3 localPos, Vector3 localFwd)
    {
        if (byName.ContainsKey(name)) throw new System.Exception($"{LogTag} 앵커 이름 중복: {name}");
        if (localFwd.sqrMagnitude < 1e-8f) throw new System.Exception($"{LogTag} 앵커 방향이 0: {name}");
        GameObject go = new GameObject(name);
        go.transform.SetParent(anchorRoot, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.LookRotation(localFwd.normalized, Vector3.up);
        byName[name] = go.transform;
        return go.transform;
    }

    private static Transform Need(string name)
    {
        if (!byName.TryGetValue(name, out Transform t) || t == null)
            throw new System.Exception($"{LogTag} 앵커가 없다: {name}");
        return t;
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>출구·입구 이음새 [계약 K0-2 · K8-0], 허용 ±0.02. 평지 섹터라 경사로·계단 없음(Map4Build.Ramp/Stairs 미사용).
    /// ① 서고 바닥 max z = ArchiveEndZ이고 탈출 공간 바닥 min z = ArchiveEndZ(틈 0·턱 0)
    /// ② 탈출 공간 바닥 max z = def.length(144), 윗면 = rise(0), x −4~4
    /// ③ 입구: 서고 바닥 min z = 0, 윗면 0.</summary>
    private static void CheckExitSeam(Map4Layout.SectorDef def)
    {
        float rise = def.exitHeight - def.floorHeight;
        BuiltBox floor = FindBuilt("GEO_S8_Floor_Main");
        BuiltBox esc = FindBuilt("GEO_S8_Floor_Escape");

        // ① 서고 끝 ↔ 탈출 공간
        if (Mathf.Abs(floor.max.z - ArchiveEndZ) > SeamTolerance || Mathf.Abs(esc.min.z - ArchiveEndZ) > SeamTolerance)
            throw new System.Exception($"{LogTag} 서고 바닥 끝 z{floor.max.z}·탈출 공간 바닥 시작 z{esc.min.z}가 서고 끝 {ArchiveEndZ}와 맞지 않는다(틈).");
        if (Mathf.Abs(esc.max.y - floor.max.y) > SeamTolerance)
            throw new System.Exception($"{LogTag} 탈출 공간 바닥 윗면 {esc.max.y}와 서고 바닥 윗면 {floor.max.y}의 턱 {esc.max.y - floor.max.y:F3}.");

        // ② 출구 = 탈출 공간 바닥 z=length 끝
        if (Mathf.Abs(esc.max.z - def.length) > SeamTolerance)
            throw new System.Exception($"{LogTag} 탈출 공간 바닥 끝 z{esc.max.z} ≠ 섹터 길이 {def.length}(출구 틈).");
        if (Mathf.Abs(esc.max.y - rise) > SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 윗면 {esc.max.y} ≠ 로컬 출구 높이 {rise} (턱 {esc.max.y - rise:F3}).");
        if (esc.min.x > -OpeningHalfW + SeamTolerance || esc.max.x < OpeningHalfW - SeamTolerance)
            throw new System.Exception($"{LogTag} 출구 바닥 x {esc.min.x}~{esc.max.x}가 개구 x −{OpeningHalfW}~{OpeningHalfW}를 덮지 않는다.");

        // ③ 입구 z0, 윗면 0
        if (Mathf.Abs(floor.min.z) > SeamTolerance || Mathf.Abs(floor.max.y) > SeamTolerance)
            throw new System.Exception($"{LogTag} 입구 바닥 시작 z{floor.min.z}·윗면 {floor.max.y}가 z0·윗면 0과 맞지 않는다.");
    }

    /// <summary>섹터 경계 [판정 18 · 초안 C18]: 모든 GEO가 x −HalfW~HalfW·z 0~def.length 안(±0.02).
    /// 예외(SectorBoundsExceptions, 현재 Wall_EscapeEnd 1건)는 이름을 적어 Debug.Log, 그 밖의 위반은 LogError.</summary>
    private static void CheckSectorBounds(Map4Layout.SectorDef def)
    {
        int excepted = 0, violations = 0;
        foreach (BuiltBox b in built)
        {
            bool outside = b.min.x < -HalfW - SeamTolerance || b.max.x > HalfW + SeamTolerance
                        || b.min.z < -SeamTolerance || b.max.z > def.length + SeamTolerance;
            if (!outside) continue;
            if (System.Array.IndexOf(SectorBoundsExceptions, b.name) >= 0)
            {
                excepted++;
                Debug.Log($"{LogTag} 참고 — 섹터 경계 밖 예외 [계약 K8-0 · 확인 요청 4]: '{b.name}' x {b.min.x}~{b.max.x}·z {b.min.z}~{b.max.z}" +
                          $"(섹터 z 0~{def.length} 바깥 두께만).");
                continue;
            }
            violations++;
            Debug.LogError($"{LogTag} 섹터 경계 위반 [판정 18]: '{b.name}' x {b.min.x}~{b.max.x}·z {b.min.z}~{b.max.z}" +
                           $"(허용 x ±{HalfW}·z 0~{def.length}).");
        }
        Debug.Log($"{LogTag} 섹터 경계 검사 — 상자 {built.Count}개, 경계 밖 예외 {excepted}(기대 {SectorBoundsExceptions.Length}), 위반 {violations}.");
    }

    /// <summary>Exit 마커 기본 위치 (0, rise+0.1, def.length) [계약 K0-2 · 초안 C4] 대조 — 마커는 옮기지 않는다(INT-1 몫).
    /// ① 그 점 아래에 윗면(= rise ±0.02, xz 발자국 안)이 있어야 하고 ② 어떤 상자의 엄격 내부(경계면 제외)에도 들면 안 된다. 위반은 LogError.</summary>
    private static void CheckExitMarker(Map4Layout.SectorDef def)
    {
        float rise = def.exitHeight - def.floorHeight;
        Vector3 p = new Vector3(0f, rise + ExitMarkerLift, def.length);
        bool supported = false;
        List<string> inside = new List<string>();
        foreach (BuiltBox b in built)
        {
            bool inXZ = p.x >= b.min.x - SeamTolerance && p.x <= b.max.x + SeamTolerance
                     && p.z >= b.min.z - SeamTolerance && p.z <= b.max.z + SeamTolerance;
            if (inXZ && Mathf.Abs(b.max.y - rise) <= SeamTolerance) supported = true;
            if (p.x > b.min.x && p.x < b.max.x && p.y > b.min.y && p.y < b.max.y && p.z > b.min.z && p.z < b.max.z)
                inside.Add(b.name);
        }
        if (!supported)
            Debug.LogError($"{LogTag} Exit 마커 {p} 아래에 윗면 y{rise}가 없다(계약 K0-2).");
        if (inside.Count > 0)
            Debug.LogError($"{LogTag} Exit 마커 {p}가 솔리드 내부에 있다: {string.Join(", ", inside)} (계약 K0-2).");
        if (supported && inside.Count == 0)
            Debug.Log($"{LogTag} Exit 마커 대조 통과 — {p}: 아래 윗면 y{rise}, 솔리드 내부 0.");
    }

    /// <summary>통로 폭 [계산] — 책장 사이·벽↔책장·작업대 사이가 도형 통과 규격 이상인지(F1-3).</summary>
    private static void CheckPassages()
    {
        float req = Map4Build.ShapeMinPassageWidth;
        Map4Build.CheckPassageWidth(ShelfPitch - ShelfDepth, req, "S8 책장 사이 통로");
        Map4Build.CheckPassageWidth((HalfW - WallT) - ShelfOuterX, req, "S8 책장 통로 A/B(벽↔책장)");
        Map4Build.CheckPassageWidth(TablePitchZ - TableD, req, "S8 작업대 행 사이");
        for (int c = 1; c < TableColX0.Length; c++)
            Map4Build.CheckPassageWidth(TableColX0[c] - (TableColX0[c - 1] + TableW), req, $"S8 작업대 열 C{c}↔C{c + 1} 사이");
        Map4Build.CheckPassageWidth(OpeningHalfW * 2f, req, "S8 입구·출구 개구");
    }

    /// <summary>구조물 쌍 검사(바닥 포함) — 겹침 0, 틈은 0(±0.02) 또는 ≥2.5 [확정 §3-8 · 계약 K8-1]. 틈 사이 공간이 다른 상자로 꽉 차
    /// 있으면(문 ↔ 탈출 공간 바닥·옆벽 0.05, 사이 = 서고 바닥·출구 벽 몸통) 끼임으로 세지 않되 건너뛰지 않고 쌍마다
    /// Debug.Log로 열거한다 [판정 23]. 기대 쌍(ExpectedFilledPairs) 밖의 '채워진 틈'은 LogError. 빈 틈이면 Map4Build.CheckGap(F1-3
    /// 0&lt;g&lt;1.0)도 부른다. 위반은 에러 로그만(생성은 유지 — 수치는 초안 검산 S8_check.py C2·C3와 같은 기준).</summary>
    private static void CheckStructureGaps()
    {
        int overlaps = 0, pinches = 0, filled = 0, unexpectedFilled = 0;
        bool[] expectedSeen = new bool[ExpectedFilledPairs.GetLength(0)];
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
                float gap = new Vector3(Mathf.Max(0f, s.x), Mathf.Max(0f, s.y), Mathf.Max(0f, s.z)).magnitude;
                if (gap <= SeamTolerance || gap >= PinchSafeGap) continue;
                if (GapIsFilled(a, b, i, j))
                {
                    filled++;
                    int k = ExpectedFilledIndex(a.name, b.name);
                    if (k >= 0)
                    {
                        expectedSeen[k] = true;
                        Debug.Log($"{LogTag} 참고 — 채워진 틈 제외 [판정 23]: '{a.name}' ↔ '{b.name}' g={gap:F3}U");
                    }
                    else
                    {
                        unexpectedFilled++;
                        Debug.LogError($"{LogTag} 기대 밖 '채워진 틈' [판정 23]: '{a.name}' ↔ '{b.name}' g={gap:F3}U — 기대 쌍은 " +
                                       "Door_Exit ↔ Floor_Escape·Wall_EscapeL·Wall_EscapeR 3쌍뿐이다. 상수 표를 확인한다.");
                    }
                    continue;
                }
                pinches++;
                Map4Build.CheckGap(a.min, a.max, b.min, b.max, a.name, b.name);
                Debug.LogError($"{LogTag} 끼임 규칙 위반 — '{a.name}' ↔ '{b.name}' 틈 {gap:F3}U (0 또는 ≥{PinchSafeGap} [확정 §3-8]).");
            }
        }
        for (int k = 0; k < expectedSeen.Length; k++)
            if (!expectedSeen[k])
                Debug.LogWarning($"{LogTag} 기대한 채워진 틈이 나오지 않았다 [판정 23]: '{ExpectedFilledPairs[k, 0]}' ↔ '{ExpectedFilledPairs[k, 1]}' " +
                                 "— 좌표가 바뀌었으면 기대 쌍 표를 함께 고친다.");
        Debug.Log($"{LogTag} 구조물 쌍 검사 — 상자 {built.Count}개, 겹침 {overlaps}, 끼임 틈 {pinches}, " +
                  $"채워진 틈 제외 {filled}(기대 {ExpectedFilledPairs.GetLength(0)}쌍, 기대 밖 {unexpectedFilled}) [판정 23].");
    }

    /// <summary>ExpectedFilledPairs에서 (a, b) 순서 무관 쌍의 행 번호, 없으면 −1.</summary>
    private static int ExpectedFilledIndex(string a, string b)
    {
        for (int k = 0; k < ExpectedFilledPairs.GetLength(0); k++)
        {
            string p = ExpectedFilledPairs[k, 0], q = ExpectedFilledPairs[k, 1];
            if ((p == a && q == b) || (p == b && q == a)) return k;
        }
        return -1;
    }

    /// <summary>축별 분리량(양수 = 떨어짐, 음수 = 겹침 깊이).</summary>
    private static Vector3 Separation(BuiltBox a, BuiltBox b) => new Vector3(
        Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x),
        Mathf.Max(b.min.y - a.max.y, a.min.y - b.max.y),
        Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z));

    /// <summary>a·b 사이 틈 공간(축마다 겹치면 겹친 구간, 떨어지면 떨어진 구간)의 표본 27점이 모두 다른 상자 안이면 true.</summary>
    private static bool GapIsFilled(BuiltBox a, BuiltBox b, int ia, int ib)
    {
        Vector3 lo = Vector3.zero, hi = Vector3.zero;
        for (int k = 0; k < 3; k++)
        {
            float aMin = a.min[k], aMax = a.max[k], bMin = b.min[k], bMax = b.max[k];
            if (aMax <= bMin) { lo[k] = aMax; hi[k] = bMin; }
            else if (bMax <= aMin) { lo[k] = bMax; hi[k] = aMin; }
            else { lo[k] = Mathf.Max(aMin, bMin); hi[k] = Mathf.Min(aMax, bMax); }
        }
        float[] f = { 0.25f, 0.5f, 0.75f };
        foreach (float fx in f)
        foreach (float fy in f)
        foreach (float fz in f)
        {
            Vector3 p = new Vector3(Mathf.Lerp(lo.x, hi.x, fx), Mathf.Lerp(lo.y, hi.y, fy), Mathf.Lerp(lo.z, hi.z, fz));
            bool inside = false;
            for (int k = 0; k < built.Count && !inside; k++)
            {
                if (k == ia || k == ib) continue;
                BuiltBox c = built[k];
                inside = p.x >= c.min.x - 1e-4f && p.x <= c.max.x + 1e-4f
                      && p.y >= c.min.y - 1e-4f && p.y <= c.max.y + 1e-4f
                      && p.z >= c.min.z - 1e-4f && p.z <= c.max.z + 1e-4f;
            }
            if (!inside) return false;
        }
        return true;
    }

    /// <summary>계약 K0-2·K8-1 — 모든 필드 non-null, 리스트 null 요소 0, 개수: 책장 44·작업대 92(초안 §2)·순찰점 8(중복 0 [판정 19])·
    /// 카메라 3(계약), 책↔혼합 ≥2, 작업대 높이 ≥0.75, archiveBounds z max = ArchiveEndZ − WallT(131.5) [판정 18].</summary>
    private static void ValidateRefs(S8_Refs r)
    {
        List<string> bad = new List<string>();
        if (r.archiveRoom == null) bad.Add("archiveRoom");
        if (r.startAnchor == null) bad.Add("startAnchor");
        if (r.managerSpawnAnchor == null) bad.Add("managerSpawnAnchor");
        if (r.roleBookAnchor == null) bad.Add("roleBookAnchor");
        if (r.roleMixerAnchor == null) bad.Add("roleMixerAnchor");
        if (r.roleMonitorAnchor == null) bad.Add("roleMonitorAnchor");
        if (r.exitDoor == null) bad.Add("exitDoor");
        if (r.exitDoorAnchor == null) bad.Add("exitDoorAnchor");
        if (r.archiveBounds.size == Vector3.zero) bad.Add("archiveBounds");
        if (r.workZoneBounds.size == Vector3.zero) bad.Add("workZoneBounds");
        if (Mathf.Abs(r.archiveBounds.max.z - (ArchiveEndZ - WallT)) > SeamTolerance)
            bad.Add($"archiveBounds max z {r.archiveBounds.max.z} ≠ {ArchiveEndZ - WallT}(탈출 공간 제외 [판정 18])");

        int expectShelves = FoyerShelves.Length + 2 * ShelfRowCount + NorthShelves.Length;                          // 44
        int expectTables = TableBlockZ0.Length * TableRowsPerBlock * TableColX0.Length - TableOmit.Length;           // 92
        CheckList(r.shelves, expectShelves, "shelves", bad);
        CheckList(r.tables, expectTables, "tables", bad);
        CheckList(r.patrolWaypoints, PatrolXZ.Length, "patrolWaypoints", bad);                                     // 8
        CheckList(r.cctvCameraAnchors, CctvCameraCount, "cctvCameraAnchors", bad);                                 // 3

        // 순찰점 중복 0 [판정 19 · 계약 K8-1] — 같은 Transform 재삽입(WP_1 닫힘은 W 몫)과 같은 자리 모두 금지
        if (r.patrolWaypoints != null)
        {
            for (int i = 0; i < r.patrolWaypoints.Count; i++)
                for (int j = i + 1; j < r.patrolWaypoints.Count; j++)
                {
                    Transform a = r.patrolWaypoints[i], b = r.patrolWaypoints[j];
                    if (a == null || b == null) continue;
                    if (a == b || (a.localPosition - b.localPosition).sqrMagnitude <= SeamTolerance * SeamTolerance)
                        bad.Add($"patrolWaypoints 중복 [{i}]↔[{j}] ({a.name}·{b.name})");
                }
        }

        if (r.roleBookAnchor != null && r.roleMixerAnchor != null
            && Vector3.Distance(r.roleBookAnchor.localPosition, r.roleMixerAnchor.localPosition) < RoleMinSeparation)
            bad.Add($"roleBook↔roleMixer 거리 < {RoleMinSeparation} [확정 §3-9]");
        if (r.tables != null)
            foreach (Transform t in r.tables)
            {
                if (t == null) continue;
                BuiltBox tb = FindBuilt(t.name);
                if (tb.max.y - tb.min.y < TableMinHeight) { bad.Add($"{t.name} 높이 < {TableMinHeight}"); break; }
            }

        if (bad.Count > 0)
            throw new System.Exception($"{LogTag} S8_Refs가 계약과 다르다: {string.Join(", ", bad)}");
    }

    private static void CheckList(List<Transform> list, int expect, string field, List<string> bad)
    {
        if (list == null) { bad.Add(field + " null"); return; }
        if (list.Count != expect) bad.Add($"{field} 개수 {list.Count}/{expect}");
        if (list.Contains(null)) bad.Add(field + " null 요소");
    }

    /// <summary>씬 루트에 남은 오브젝트 확인(S1·S5와 같은 점검) — B는 팀 메뉴를 부르지 않아 원칙적으로 0. Wire 뒤에도 한 번 더 부른다.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"{LogTag} 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>Map4Build가 돌려준 상자에 S8 이름을 붙인다(Map4Build 수정 없음, 계약 K0-3 — 반환 Transform의 name만 바꿈).
    /// 시각물 자식 "Visual"은 이름을 **바꾸지 않는다** — 계약 K0-3 "자식 `Visual` 이름은 바꾸지 않는다", S8_Dress가
    /// Find("Visual")로 찾는다. 생성이 거부돼 null이면 즉시 중단(부분 지형 방지).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"{LogTag} 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        if (byName.ContainsKey(name)) throw new System.Exception($"{LogTag} 이름 중복: {name}");
        t.name = name;
        byName[name] = t;
        return t;
    }

    private static void Add(Transform t, Vector3 min, Vector3 max, List<Transform> list)
    {
        built.Add(new BuiltBox { name = t.name, min = min, max = max });
        if (list != null) list.Add(t);
    }

    private static BuiltBox FindBuilt(string name)
    {
        foreach (BuiltBox b in built)
            if (b.name == name) return b;
        throw new System.Exception($"{LogTag} 만든 상자 목록에 없다: {name}");
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
        archiveRoot = null;
        anchorRoot = null;
        built = null;
        byName = null;
    }
}
#endif
