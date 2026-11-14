#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// S7 서버실 — 조명·재질(과제 S7-L → 2차 S7-L3 → 3차 S7-L4, 계약 진행/지시서/R3/_계약.md K0-3·K7 — 정본.
/// R2S68/_계약.md·1차 판 _계약_S6-S8.md는 이력).
/// S7_Builder.Build가 Wire 다음 마지막에 `S7_Dress.Apply(generated, refs)`로 부른다(K0-2 ⑤).
/// 좌표 원천: 진행/초안/S7_배치초안.md 2차 반영(§13)·수정 초안1(§14) — 서버 격자 안C(틈 11) [사용자 09-29 나·안C],
/// 입구·출구 문 TEMP 상시 개방(문 없음, refs.entryDoor·exitDoor = null) [판정 17-S7].
/// TEMP 기간 문틀 VIS 없음(인방만) [판정 C10 · 계약 K7-0 :588] — B도 L도 문틀을 만들지 않는다(S7_Builder.cs:18-19).
///
/// 하는 일(렌더러·조명만 — 콜라이더 = 측정 지형 불변):
///  1) GEO_ 상자의 자식 `Visual` 렌더러에 S7 전용 Standard 머티리얼을 입힌다.
///     서버 본체(refs.servers) 어두운 금속 · 유리벽(refs.glassWalls) 투명(Standard Transparent, _Mode 3) ·
///     벽·천장 어둡게 · 책상·콘솔. GEO_ 콜라이더·트랜스폼은 읽기만 한다. 문(GEO_S7_Door_)·바닥(GEO_S7_Floor_)·
///     팀 기믹(S7_Gimmicks 아래)은 건드리지 않는다.
///  2) generated 바로 아래 그룹 `S7_Dress` 1개에 VIS_S7_… 등·장식을 만든다(콜라이더 0 — 생성 즉시 제거 + 마지막 전수 제거).
///     - 서버실(serverRoomBounds — 서버실만, 컴퓨터실과 교집합 0 [판정 15]): 어두운 바닥 덮개(칸 판) · 낮은 세기 통로 등 소수 ·
///       서버마다 작은 LED(발광) · 포트 있는 서버(servers[portServerIndex[i]])는 포트 소켓·윗면 표시판·상태 띠(역할 색 발광) +
///       포트 앞 작은 등 + 포트 ID 글자 [판정 12].
///     - 서버실 서쪽 띠(serverRoomBounds 밖, 컴퓨터실 블록 밖 — 바닥의 14.2%, 초안 §7-7·§14 #5): 어두운 바닥 덮개만(등 없음).
///     - 컴퓨터실(computerRoomBounds — 서버실 쪽 VIS와 겹치면 컴퓨터실 우선, K7-1): 밝은 바닥 덮개·벽 안감 + 등 2 + 책상 스탠드 등.
///     - 유리벽 틀(창살·레일·눈높이 띠 — 벽이 있다는 표시).
///     - 전실 등 2 + W_ENTRY 패널 조명·판·포트 소켓·글자(W가 놓은 팀 WiringPort 위치를 읽기만 한다).
///     - 전력 콘솔 스탠드 등 + 화면 · 책상 위 컴퓨터·가이드북 모형 · 역할 표지 3개(Book·Computer·Power) [판정 12] ·
///       출구 위 표지 등(TEMP 기간에는 TEMP_S7_ExitDoor_Open + GEO_S7_Wall_North_Lintel 아랫면 기준 [판정 17-S7]).
///  3) 배선 선 머티리얼 도우미 `WiringLineMaterial()` — W만 부른다 [판정 11 · 계약 K7-2].
///
/// 전역 조명 [판정 14 · 1차 판정 A1 · 계약 K0-3]: Master 방향광·환경광·RenderSettings는 섹터 코드가 바꾸지 않는다(이 파일은 읽지도 않는다).
/// Master 방향광 소프트 그림자는 INF1-2(Map4SceneBuilder 소유)가 켠다(섹터 코드 불변). 그림자가 켜지면 천장(GEO_S7_Ceiling)이
/// 직사광을 막는다. 환경광은 불변(Skybox·강도 1 — 초안 §7-3). 서버실이 실제로 얼마나 어두워지는지는 Unity 실측 전까지 (추정)이고,
/// 그래서 섹터 안 머티리얼 명도(알베도)와 발광 대비로도 어둡게 만든다.
///
/// 머티리얼: Assets/LaboratoryMap4/Materials/Generated/M4_S7_&lt;이름&gt;.mat — Standard, 있으면 재사용·없으면 생성
/// (S1_Lighting.cs·S8_Dress 방식). Map4Palette.asset·Map4_*.mat·팀 머티리얼은 수정하지 않는다(새 에셋만 만들고
/// 렌더러의 sharedMaterial 참조만 바꾼다). 글자(TextMesh)는 팀 선례(Ch8TestRoomMenuItem.cs:258-267)대로 내장 폰트의
/// 머티리얼을 그대로 쓴다(새 머티리얼 아님).
///
/// 이름으로 찾는 것(refs 필드 밖): GEO_S7_* 접두사(PaintFrameVisuals — 1차 선례), S7_Gimmicks(W 그룹, 읽기만), Visual(Map4Build.cs:450).
/// 이름 규약 경로 2개 [판정 C11 · 계약 K0-3 :502 · K7-0 :593 — Refs 필드 추가 없음]: TEMP 기간 출구 표지 기준점
/// TEMP_S7_ExitDoor_Open 이름 조회(FindByName) · W_ENTRY 입구 포트 스캔(BuildEntryPanel — generated 아래 팀 WiringPort). 둘 다 읽기만.
/// 출구 개구 윗변 GEO_S7_Wall_North_Lintel(초안 §3-1 이름) 조회는 GEO_ 접두사 이름 읽기 선례(PaintFrameVisuals)와 같은 종류다 — 읽기만.
///
/// 형식 검사: Unity 동봉 csc.dll(스테이징, scratchpad 산출물). Unity 실행 검증은 통합 후 대기열.
/// </summary>
public static class S7_Dress
{
    // ════════════════════════════════════════════════════════════════════════════════════
    //  상수 표 — 초안(진행/초안/S7_배치초안.md 2차 반영 §13·수정 초안1 §14, 안C·TEMP) 기준. 좌표·크기·세기·색은 여기서만 고친다.
    //  좌표는 섹터 로컬(generated 기준): +Z 진행, x=0 중앙, y=0 = 섹터 바닥.
    //  방 경계·서버·포트·문·앵커 위치는 상수로 두지 않고 refs에서 읽는다(B 상수를 옮겨 적지 않음).
    //  색은 sRGB로 적는다(프로젝트 색 공간 Linear — ProjectSettings m_ActiveColorSpace 1 — Unity가 변환).
    // ════════════════════════════════════════════════════════════════════════════════════

    // ── 전체 스위치 ─────────────────────────────────────────────────────────────────────
    /// <summary>[판정 C12 채택 · 계약 K7-0 :600] 벽·천장 Visual도 어둡게 칠한다. 근거: 서버실 어둡게는 L 소유(계약 K7-1) +
    /// 환경광 불변(K0-3 :511)이라 방향광 그림자(INF1-2)가 켜져도 벽·천장 알베도가 밝으면 서버실이 덜 어둡다 (추정 — Unity 실측 전).
    /// 벽은 섹터 공용 이름(GEO_S7_Wall*)이라 전실 벽도 어두워지고, 전실은 등 2개로 보완한다.</summary>
    private static readonly bool PaintFrame = true;
    /// <summary>[판정 12 · 계약 K7-0 :596] 포트 ID 글자(A·B·C·1·2·3) — 서버 앞·윗면·뒷면 + W_ENTRY 입구 포트 6. L 소유(W CreatePortLabels = false).
    /// 팀 WiringPort는 게임 화면 표시가 없고(S7_팀API §1-2, Gizmo만) 규칙표(WiringRuleDisplay)는 ID로만 말하므로, 글자가 없으면 어느 서버가 A인지 알 수 없다.
    /// 안C 앞줄은 포트 면이 +Z(북)라 입구 쪽에서는 뒷면(−n)이 보인다 → 뒷면 글자·윗면 표시판은 입구 식별용으로 유지(초안 §4 :168 — 입구 포트면 3/6·윗면 6/6).</summary>
    private static readonly bool ShowPortLabels = true;
    /// <summary>[판정 12 · 계약 K7-0 :596 · K7-1 L 소유] 역할 표지 3개(VIS_S7_RoleLabel_Book/Computer/Power) — L 소유(W CreateRoleLabels = false).
    /// 팀 RoleSlot에는 게임 화면 표지가 없다(팀 선례 Ch8TestRoomMenuItem.cs:258-267은 메뉴 생성물의 자식 Label).</summary>
    private static readonly bool ShowRoleLabels = true;
    /// <summary>[판정 C12 채택 · 계약 K7-0 :600] 책상 위 컴퓨터·가이드북 모형, 콘솔 화면(VIS_, 콜라이더 0). 팀 RoleSlot·BookPanel·QuizTerminal은
    /// 렌더러가 없다(팀 코드 grep). 모형 크기·색 상수(MonitorSize·BookSize·ConsoleScreenSize·PropDarkColor 등)는 [제안] 그대로다.</summary>
    private static readonly bool BuildRoleProps = true;

    // ── 포트 ID·역할 색 ─────────────────────────────────────────────────────────────────
    /// <summary>[계약 K7-1] portAnchors 순서 [0]OUT_A [1]OUT_B [2]OUT_C [3]IN_1 [4]IN_2 [5]IN_3 → 글자.
    /// 정본 한 곳 [판정 C12 부수 · 해석]: 포트 ID·역할의 정본은 W가 앵커 자리에 놓은 팀 WiringPort의 portId·role(public 필드,
    /// WiringPort.cs:19·23 — 값 출처는 S7_Wiring.cs PortIds). 이 상수는 W 포트가 없을 때의 폴백·대조용 사본이고,
    /// 둘이 다르면 BuildServers가 LogWarning을 남기고 W 값을 따른다.</summary>
    private static readonly string[] ContractPortIds = { "A", "B", "C", "1", "2", "3" };
    /// <summary>[제안] 출력 = 호박색(팀 Gizmo 주황=출력, WiringPort.cs:99-103 관례), 입력 = 청록(Gizmo 파랑=입력).</summary>
    private static readonly Color PortOutColor = new Color(1.00f, 0.62f, 0.18f);
    private static readonly Color PortInColor = new Color(0.25f, 0.80f, 1.00f);

    // ── 서버실 바닥 덮개(VIS 칸 판) ─────────────────────────────────────────────────────
    /// <summary>[제안] 칸 한 변. 바닥(GEO_S7_Floor_Main)은 섹터 전체 1장이라 그대로 칠하면 전실까지 어두워지고,
    /// Built-in Forward는 렌더러마다 픽셀 조명 수가 제한되므로(Ultra 4) 큰 판 1장이면 등 몇 개만 웅덩이가 그려진다
    /// → 서버실만 칸 판으로 덮어 칸마다 가까운 등이 픽셀 조명이 되게 한다(S8_Dress 선례).</summary>
    private const float FloorTileSize = 10f;
    /// <summary>[계산] 바닥 윗면 위로 띄우는 높이 — z-fighting 방지(S8_Dress 선례 0.02).</summary>
    private const float FloorLift = 0.02f;
    /// <summary>[계산] 벽 안쪽 면에서 안감을 띄우는 거리.</summary>
    private const float LinerInset = 0.02f;

    // ── 서버실 통로 등(Pendant, Point) — "낮은 세기 등 소수" [확정 §3-9] ──────────────────
    /// <summary>[제안] x0 위 3개(안C 초안 §2-3 :49-53 기준 — 값은 1차와 같고 근거만 안C로 고침):
    /// (0,10,34) = 앞줄 서버(z 28~30)와 뒷줄 서버(z 44~46) 사이 마당(전력 담당이 도는 곳) 위 ·
    /// (0,10,67)·(0,10,97) = 서버 북쪽 빈 어둠(z 46~109.5) 가운데·출구 앞. 세 점 모두 serverRoomBounds 안이고 어느 서버 상자의 XZ 위에도
    /// 있지 않다(수직 겹침 0 — 서버 x·z 범위 밖). 벗어나면 BuildAisleLamps가 경고 후 건너뛴다.</summary>
    private static readonly Vector3[] AisleLampPos = { new Vector3(0f, 10f, 34f), new Vector3(0f, 10f, 67f), new Vector3(0f, 10f, 97f) };
    private const float AisleLampRange = 20f;             // [계산] 높이 10에서 바닥까지 10 → 범위의 절반, 바닥 감쇠 약 0.14(Built-in 근사 1/(1+25(d/r)²)) — 어둡게
    private const float AisleLampIntensity = 1.0f;        // [추정] 실측 조정
    private static readonly Color AisleLampColor = new Color(0.55f, 0.70f, 1.00f); // [제안] 차가운 푸른빛
    private const float AisleLampEmission = 0.5f;         // [추정] 갓 발광 배율(어둡게)

    // ── 포트 앞 작은 등(Point) — 포트 서는 자리 비춤 ──────────────────────────────────────
    // 위치 = 포트 앵커 + forward·1.2 + 위 1.3(refs에서 파생 — 좌표 사본 없음). 안C 값 [계산]:
    //   앞줄(forward +Z) OUT_A (−15,2.5,31.2) · IN_1 (0,2.5,31.2) · OUT_B (15,2.5,31.2)
    //   뒷줄(forward −Z) IN_2 (−7.5,2.5,42.8) · OUT_C (7.5,2.5,42.8) · IN_3 (22.5,2.5,42.8)
    //   포트 트리거(포트 면 앞 0~2, y 0~2 — 초안 §4) 중심(앞줄 z31·뒷줄 z43, y1)까지 거리 √(1.5²+0.2²) ≈ 1.51 — 범위 5 안.
    private const float PortLightForward = 1.2f;          // [제안] 포트 앵커에서 forward(플레이어 쪽)로
    private const float PortLightUp = 1.3f;               // [제안] 포트 앵커 위로(y 1.2 + 1.3 = 2.5)
    private const float PortLightRange = 5f;              // [제안] 포트 트리거(포트 면에서 0~2 앞, 초안 §4)를 덮음
    private const float PortLightIntensity = 1.6f;        // [추정]

    // ── 포트 소켓·표시판·띠(VIS, 역할 색 발광) ──────────────────────────────────────────
    private static readonly Vector3 SocketSize = new Vector3(0.7f, 0.7f, 0.06f);    // [제안] 가로·세로·두께(앞면에서 튀어나옴)
    private static readonly Vector3 SocketHoleSize = new Vector3(0.28f, 0.28f, 0.01f); // [제안] 가운데 어두운 구멍
    private const float SocketBack = 0.0f;                // [제안] 소켓 뒷면 = 앵커 면
    private const float PortEmission = 2.0f;              // [추정] 소켓·표시판·띠 발광 배율 — 입구(z15)에서 어둠 속 식별용
    private const float BeaconFrac = 0.7f;                // [제안] 서버 윗면 표시판 = 윗면 가로·세로의 이 비율
    private const float BeaconThick = 0.06f;              // [제안]
    private const float StripHeight = 0.06f;              // [제안] 앞·뒷면 위쪽 상태 띠 높이
    private const float StripDepth = 0.03f;               // [제안]
    private const float StripBelowTop = 0.25f;            // [제안] 서버 윗면에서 띠 중심까지

    // ── 포트 ID 글자(TextMesh — 내장 폰트, 콜라이더 없음) ──────────────────────────────
    private const float LabelAboveSocket = 1.05f;         // [제안] 소켓 중심 위(앵커 y1.2 → 글자 중심 y2.25)
    private const float LabelBelowTop = 0.55f;            // [제안] 서버 윗면에서 글자 중심까지 최소 여유
    private const int LabelFontSize = 64;                 // [제안] 포트 ID 글자(역할 표지는 RoleLabelFontSize)
    private const float LabelCharSize = 0.12f;            // [추정] 글자 높이 ≈ 0.12·64·0.1 ≈ 0.77U(V3_Dress 근사식) — 실측
    private const float TopLabelCharSize = 0.20f;         // [추정] 윗면 글자 ≈ 1.28U — 표시판(1.4U 깊이) 안
    private static readonly Color TopLabelColor = new Color(0.05f, 0.05f, 0.05f); // [제안] 밝은 표시판 위 검은 글자
    /// <summary>[판정 C12 부수 · 초안 §4 :168 입구 식별용 · 계약 K0-2 +Z 진행 · 명령 결정 — 지시서 S7-L4 충돌 1] 윗면 글자(*_Label_T)의 글자 위쪽 축.
    /// 6대 모두 같은 축(포트 면 n과 무관). C13 카메라 기준 yaw 90(보는 방향 +X)을 따르려면 이 한 줄만 Vector3.right로 바꾼다.</summary>
    private static readonly Vector3 TopLabelUp = Vector3.forward;

    // ── 역할 표지(TextMesh — 팀 선례 Ch8TestRoomMenuItem.cs:258-267 값 그대로, 콜라이더 없음) [판정 12] ──────────────
    // 자리 = 역할 앵커 위 RoleLabelHeight(앵커는 바닥 높이 — 초안 §3-2). 앞면은 앵커 forward 쪽(플레이어가 서는 쪽)에서 읽힌다.
    // 글자는 영문 roleId(내장 폰트의 한글 표시는 모르겠다 — 그래서 팀 roleId 그대로).
    // 정본 한 곳 [판정 C12 부수 · 해석]: 역할 ID의 정본은 W가 놓은 팀 RoleSlot의 roleId(값 출처 S7_Wiring.cs RoleId*)다.
    // 아래 RoleId* 세 상수는 표지 글자용 사본이다(같은 팀 근거 ServerRoomPowerSelfTest.cs:65-67). L은 팀 RoleSlot 타입을 새로 참조하지 않으므로
    // 자동 대조는 없다 — 값을 바꿀 때는 W 상수와 함께 고친다.
    private const float RoleLabelHeight = 2.2f;           // [팀 Ch8TestRoomMenuItem.cs:258] Label 로컬 y 2.2
    private const float RoleLabelCharSize = 0.25f;        // [팀 :262]
    private const int RoleLabelFontSize = 48;             // [팀 :263]
    private const string RoleIdBook = "Book";             // [팀 ServerRoomPowerSelfTest.cs:65]
    private const string RoleIdComputer = "Computer";     // [팀 ServerRoomPowerSelfTest.cs:66]
    private const string RoleIdPower = "Power";           // [팀 ServerRoomPowerSelfTest.cs:67]
    private static readonly Color RoleLabelColorBook = new Color(0.3f, 0.6f, 1.0f);      // [팀 Ch8TestRoomMenuItem.cs:57 Book 색]
    private static readonly Color RoleLabelColorComputer = new Color(0.9f, 0.9f, 0.9f);  // [제안] (1차 W 값 — 팀 CH8에 Computer 없음)
    private static readonly Color RoleLabelColorPower = new Color(1.0f, 0.85f, 0.2f);    // [제안] (1차 W 값 — 팀 CH8에 Power 없음)

    // ── 서버 LED(VIS, 발광) ─────────────────────────────────────────────────────────────
    private const int LedRows = 7;                        // [제안]
    private const int LedCols = 3;                        // [제안] 면 왼쪽 가장자리 세로 줄
    private const float LedMarginX = 0.30f;               // [제안] 면 가장자리에서 첫 열
    private const float LedSpacingX = 0.20f;              // [제안]
    private const float LedMarginY = 0.45f;               // [제안] 위·아래 가장자리에서 첫·끝 행
    private static readonly Vector3 LedSize = new Vector3(0.12f, 0.05f, 0.03f); // [제안] 면 방향 두께 0.03
    private const float LedEmbed = 0.01f;                 // [제안] 면 안으로 묻는 깊이(나머지 0.02 튀어나옴)
    private static readonly Color LedGreenColor = new Color(0.25f, 1.00f, 0.45f); // [제안]
    private static readonly Color LedBlueColor = new Color(0.30f, 0.60f, 1.00f);  // [제안]
    private const float LedEmission = 1.6f;               // [추정]

    // ── 전력 콘솔 스탠드 등(Spot, 아래) — 역할 자리 ─────────────────────────────────────
    /// <summary>[제안] rolePowerAnchor(바닥) 위 → 갓 아랫면 4.52. 콘솔 위 한 도형 점프: 윗면 1.0 + 세모 jumpHeight 2.0 + 세모 높이 1.155 ≈ 4.16 (추측 —
    /// jumpHeight를 피벗 상승량으로 가정, S8 근거와 같음) → 닿지 않음. 쌓기(콘솔 1.0 + 정육면체 1 + 세모 점프 2.0 + 1.155 ≈ 5.16, 초안 §2-1 :27 — 쌓기가
    /// 되는지 모르겠다)면 갓을 지날 수 있다 (추측). 갓·줄은 콜라이더 0이라 물리 영향 0 — 화면만.</summary>
    private const float ConsoleLampHeight = 4.6f;
    private const float ConsoleLampSpotAngle = 70f;       // [추정]
    private const float ConsoleLampRange = 7f;            // [계산] 4.6/cos35° ≈ 5.6 + 여유
    private const float ConsoleLampIntensity = 2.2f;      // [추정]
    private static readonly Color ConsoleLampColor = new Color(0.85f, 0.92f, 1.00f); // [제안] 차가운 흰빛

    // ── 컴퓨터실 — "조금 밝게" [확정 §3-9] ─────────────────────────────────────────────
    private const float CrLampY = 9f;                     // [제안] 천장(14) 아래 매단 등
    // [계산 — scratchpad s7l4/leak.py] 빛 새기(사실): AddLight가 shadows = None이라 유리뿐 아니라 불투명 벽도 빛을 막지 않는다.
    // 실제 광원 y 8.9(CrLampY 9 − ShadeHalfHeight 0.08 − 0.02, 등 (−33.75, z 29.33·38.67))에서 바닥에 닿는 수평 반경 13.30 →
    // 유리 동쪽 서버실 x −20.45, 남벽(z 19.5~20) 너머 서버실 서쪽 띠 z 16.0, 북벽(z 48~48.5) 너머 z 52.0까지 바닥에 닿는다.
    // 12로 줄이면 바닥은 방 안(x −25.70, z 21.28·46.72)에 머문다(천장 y14는 12에서도 x −22.89·z 18.47·49.53까지 — 바닥 기준으로만 막힘).
    // 새는 곳의 거리/범위 d/r = 유리 너머 바닥 ≥ 0.76, 남·북 벽 너머 바닥 ≥ 0.83 → 감쇠가 커서 영향은 작을 것(추측, Unity 실측 대기).
    private const float CrLampRange = 16f;                // [판정 C12 채택 · 계약 K7-0 :600] 방 15.5×28 — 등 2개로 덮음. 값 16 유지(빛 새기는 위 주석)
    private const float CrLampIntensity = 1.4f;           // [추정]
    private static readonly Color CrLampColor = new Color(1.00f, 0.95f, 0.85f);    // [제안] 따뜻한 흰빛
    /// <summary>[제안] 책상 윗면(0.75) 위 → 갓 중심 y4.6·아랫면 4.52. 책상 위 세모 점프 0.75 + 2.0 + 1.155 ≈ 3.91 → 닿지 않음 (추측).
    /// 쌓기(책상 + 정육면체 1 + 세모 점프 ≈ 4.91)면 지날 수 있다 (추측 — 쌓기 가능 여부 모르겠다). 콜라이더 0, 화면만.</summary>
    private const float DeskLampAboveTop = 3.85f;
    private const float DeskLampSpotAngle = 90f;          // [제안] 책·컴퓨터(z32~38) 둘 다 — 높이 3.85에서 웅덩이 반지름 3.85
    private const float DeskLampRange = 7f;               // [계산] 3.85/cos45° ≈ 5.4 + 여유
    private const float DeskLampIntensity = 2.0f;         // [추정]
    /// <summary>[초안 §2-4] 컴퓨터실 개구부 폭 4 — 벽 안감을 이 폭만큼 비운다(refs.computerRoomDoorway 중심 기준).</summary>
    private const float DoorwayHalfWidth = 2.0f;

    // ── 유리벽 틀(VIS) ──────────────────────────────────────────────────────────────────
    private const float GlassMullionSpacing = 4f;         // [제안] 세로 창살 최대 간격
    private const float GlassMullionWidth = 0.12f;        // [제안]
    private const float GlassBottomRailH = 0.25f;         // [제안] 바닥 레일(벽이 있다는 표시)
    private const float GlassTopRailH = 0.15f;            // [제안]
    private const float GlassBandY = 1.2f;                // [제안] 눈높이 띠(유리 충돌 방지 표시) 중심
    private const float GlassBandH = 0.08f;               // [제안]
    private static readonly Color GlassColor = new Color(0.62f, 0.80f, 0.88f, 0.16f); // [제안] 알파 0.16
    private const float GlassSmoothness = 0.92f;          // [제안]

    // ── 전실(entryHall) 등 — 초안 §2-2 전실 안쪽 x ±16, z 0.5~14 ─────────────────────────
    private static readonly Vector3[] HallLampPos = { new Vector3(-8f, 9f, 7f), new Vector3(8f, 9f, 7f) }; // [제안]
    private const float HallLampRange = 14f;              // [제안]
    private const float HallLampIntensity = 1.2f;         // [추정]
    // W_ENTRY 패널 벽등(Spot) — entryPanelAnchor 위 벽에 달고 서는 자리를 비춘다.
    private const float EntryPanelLampUp = 2.6f;          // [제안] 앵커(y1.2) 위 → y3.8
    private const float EntryPanelLampOut = 0.4f;         // [제안] 벽에서
    private const float EntryPanelLampAim = 1.5f;         // [제안] 앵커에서 forward로 이만큼 앞 바닥 높이 쪽을 겨눔
    private const float EntryPanelLampSpotAngle = 80f;    // [제안]
    private const float EntryPanelLampRange = 8f;         // [제안]
    private const float EntryPanelLampIntensity = 1.6f;   // [추정]
    /// <summary>[제안] W가 놓은 입구 포트가 entryPanelAnchor에서 이보다 멀면 입구 포트로 보지 않는다(초안 포트 줄 z 2.5~13.5 → 반 5.5).</summary>
    private const float EntryPortMaxDist = 8f;
    /// <summary>[제안] 입구 포트 줄 둘레 판 여유. 판이 솔리드 GEO(예 칸막이 z14)와 겹치면 BuildEntryPanel이 그 쪽 끝을 잘라 낸다
    /// (1차: 판 z 1.55~14.45가 GEO_S7_Wall_Partition_E(z 14~14.5)에 0.45 묻혔음 — 2차에서 GEO 읽기로 잘라 z ~14.0).</summary>
    private const float EntryPlateMargin = 0.6f;
    private const float EntryPlateHeight = 1.9f;          // [제안]
    private const float EntryPlateDepth = 0.02f;          // [제안]
    /// <summary>[계산] 앵커와 같은 자리로 볼 거리 — 전력 포트(앵커 위) 판별.</summary>
    private const float SameSpotTolerance = 0.15f;

    // ── 출구 표지 등 ─────────────────────────────────────────────────────────────────────
    // 기준(개구 윗변·면 위치)은 좌표 상수로 두지 않고 읽는다:
    //  - refs.exitDoor가 있으면(되돌림 TempOpenDoors = false) 문 상자(Visual) 윗면·두께 — 1차 경로 그대로.
    //  - TEMP 기간(refs.exitDoor = null [판정 17-S7 · 계약 K7-0 :588]) 기준점 = TEMP_S7_ExitDoor_Open 위치(초안 (0,0,109.75)),
    //    개구 윗변 = GEO_S7_Wall_North_Lintel 아랫면(초안 y4), 붙는 면 = 인방 두께의 방 쪽 면(초안 z109.5). 둘 다 이름으로 찾고 읽기만 한다
    //    [판정 C11 · 계약 K0-3 :502 · K7-0 :593 — TEMP 표지 이름 조회 / 인방은 GEO_ 접두사 이름 읽기 선례(PaintFrameVisuals) · 초안 §4 :166·§14 #4].
    //    둘 중 하나라도 없으면 경고 후 건너뛴다.
    //  - TEMP 표지 forward(+Z)는 B [제안]·의미 없음(보고 S7-D3 :52) — L은 읽지 않는다. 쓰는 것은 TEMP 위치(벽을 따라가는 축)·
    //    인방 중심(두께 축)·serverRoomBounds 중심(방 쪽)뿐이다 [판정 C12 부수 · 계약 K7-0 :588 위치만].
    //  - TEMP 기간 문틀 VIS는 없다(인방만) [판정 C10 · 계약 K7-0 :588 · S7_Builder.cs:18-19] — L도 문틀을 만들지 않는다.
    //    초안 :166의 'L 문틀 기준점' 문구는 C10으로 무효다(입구 쪽 TEMP 표지는 L이 쓰지 않는다).
    //  초안 값으로 판 중심 (0,5,109.46)·등 (0,5.6,108.5) [계산] — 두 경로가 같은 값을 낸다.
    private const float ExitSignAboveDoor = 1.0f;         // [제안] 개구 윗변(4) 위 → y5(되돌림(문 있음) 때만 Map4Build 문틀 top~top+0.2 위, TEMP 기간에는 인방 아랫면 y4 위)
    private static readonly Vector3 ExitSignSize = new Vector3(1.2f, 0.4f, 0.06f); // [제안]
    private const float ExitLampOut = 1.0f;               // [제안] 문(인방) 면에서 방 쪽
    private const float ExitLampAboveDoor = 1.6f;         // [제안] 개구 윗변 위
    private const float ExitLampRange = 6f;               // [제안]
    private const float ExitLampIntensity = 1.0f;         // [추정]
    private static readonly Color ExitColor = new Color(0.40f, 1.00f, 0.55f); // [제안] 초록(출구)
    private const float ExitSignEmission = 1.5f;          // [추정]

    // ── 매단 등 갓·줄(시각물) ────────────────────────────────────────────────────────────
    private const float ShadeDiameter = 0.7f;             // [제안]
    private const float RoleShadeDiameter = 0.5f;         // [제안]
    private const float ShadeHalfHeight = 0.08f;          // [제안] 원기둥 기본 높이 2 → 스케일 y 0.08 = 높이 0.16
    private const float CordDiameter = 0.03f;             // [제안]
    /// <summary>[초안 §2-1] 천장 아랫면 y14 — refs.serverRoomBounds.max.y가 비어 있을 때만 쓰는 대체값.</summary>
    private const float FallbackCeilingY = 14f;
    private const float LampShadeEmission = 1.2f;         // [추정] 따뜻한 갓 발광 배율

    // ── 역할 모형(VIS) ──────────────────────────────────────────────────────────────────
    /// <summary>[초안 §3-1] 책상 윗면 0.75·콘솔 윗면 1.0 — 앵커 아래 GEO 윗면을 찾지 못할 때만 쓰는 대체값.</summary>
    private const float FallbackDeskTop = 0.75f;
    private const float FallbackConsoleTop = 1.0f;
    private static readonly Vector3 MonitorSize = new Vector3(1.0f, 0.62f, 0.06f); // [제안] 가로·세로·두께(화면 = forward 쪽)
    private const float MonitorLift = 0.30f;              // [제안] 받침 높이(윗면 → 화면 아래 끝)
    private const float MonitorBack = 0.20f;              // [제안] 앵커에서 −forward(책상 안쪽)로
    private static readonly Vector3 KeyboardSize = new Vector3(0.8f, 0.04f, 0.28f); // [제안]
    private const float KeyboardFront = 0.22f;            // [제안] 앵커에서 forward로
    private static readonly Vector3 BookSize = new Vector3(0.55f, 0.08f, 0.75f);  // [제안] 덮은 책
    private const float BookYaw = 12f;                    // [제안] 살짝 비스듬히
    private static readonly Vector3 ConsoleScreenSize = new Vector3(1.0f, 0.5f, 0.04f); // [제안]
    private const float ConsoleScreenTilt = 25f;          // [제안] 플레이어 쪽으로 기울임
    private const float ScreenEmission = 1.2f;            // [추정]

    // ── 머티리얼(Standard) 색 — sRGB ────────────────────────────────────────────────────
    private static readonly Color ServerBodyColor = new Color(0.09f, 0.10f, 0.11f);  // [제안] 어두운 금속
    private static readonly Color ServerFloorColor = new Color(0.07f, 0.075f, 0.085f); // [제안] 서버실 바닥 덮개
    private static readonly Color WallColor = new Color(0.13f, 0.14f, 0.16f);        // [제안] 어두운 벽
    private static readonly Color CeilingColor = new Color(0.06f, 0.06f, 0.07f);     // [제안]
    private static readonly Color CrFloorColor = new Color(0.42f, 0.43f, 0.45f);     // [제안] 컴퓨터실 바닥(조금 밝게)
    private static readonly Color CrWallColor = new Color(0.55f, 0.56f, 0.58f);      // [제안] 컴퓨터실 벽 안감
    private static readonly Color DeskColor = new Color(0.46f, 0.40f, 0.33f);        // [제안] 책상(밝은 목재 톤)
    private static readonly Color ConsoleColor = new Color(0.20f, 0.22f, 0.25f);     // [제안] 콘솔 금속
    private static readonly Color GlassFrameColor = new Color(0.22f, 0.23f, 0.25f);  // [제안] 유리 틀
    private static readonly Color GlassBandColor = new Color(0.75f, 0.78f, 0.80f);   // [제안] 눈높이 띠
    private static readonly Color SocketHoleColor = new Color(0.02f, 0.02f, 0.02f);  // [제안]
    private static readonly Color PropDarkColor = new Color(0.08f, 0.08f, 0.09f);    // [제안] 모니터 몸체·키보드
    private static readonly Color ScreenColor = new Color(0.55f, 0.78f, 1.00f);      // [제안] 화면 빛
    private static readonly Color BookColor = new Color(0.45f, 0.12f, 0.10f);        // [제안] 책 표지
    private static readonly Color CordColor = new Color(0.05f, 0.05f, 0.05f);        // [제안]
    private static readonly Color EntryPlateColor = new Color(0.16f, 0.17f, 0.19f);  // [제안]

    // ── 이름 ─────────────────────────────────────────────────────────────────────────────
    private const string GroupName = "S7_Dress";          // [계약 K0-3]
    private const string GimmickGroupName = "S7_Gimmicks";// [계약 K0-3] W 그룹 — 이 아래는 읽기만
    private const string VisualChildName = "Visual";      // Map4Build.CreateSolidBox(:450 `visual.name = "Visual"`)가 붙이는 자식 이름
    private const string TempExitDoorName = "TEMP_S7_ExitDoor_Open";   // [판정 17-S7 · 판정 C11 · 계약 K0-3 :502 · K7-0 :588·:593] TEMP 기간 출구 문 대신 빈 GO(B) — 이름 규약 조회, 읽기만
    private const string ExitLintelName = "GEO_S7_Wall_North_Lintel";  // [초안 §3-1 :85] 출구 개구 위 인방(B) — GEO_ 접두사 이름 읽기 선례(PaintFrameVisuals), GeoBox로 읽기만
    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated";
    private const string MaterialPrefix = "M4_S7_";       // [계약 K0-3]
    private const string LogTag = "[S7_Dress]";

    // ════════════════════════════════════════════════════════════════════════════════════

    private sealed class Mats
    {
        public Material serverBody, serverFloor, wall, ceiling, crFloor, crWall, desk, console,
                        glass, glassFrame, glassBand, portOut, portIn, socketHole, ledGreen, ledBlue,
                        aisleShade, warmShade, cord, exitSign, propDark, screen, book, entryPlate;
    }

    private static int lightCount;
    private static int pixelForcedCount;

    /// <summary>계약 K7-2. generated = S7 Generated 루트(섹터 로컬), refs = S7_Builder가 채운 참조.</summary>
    public static void Apply(Transform generated, S7_Refs refs)
    {
        if (generated == null || refs == null)
        {
            Debug.LogError($"{LogTag} generated 또는 refs가 null이다 — 호출부(S7_Builder) 확인 필요. 조명·재질을 건너뛴다.");
            return;
        }
        if (Shader.Find("Standard") == null)
        {
            Debug.LogError($"{LogTag} Standard 셰이더를 찾지 못했다(Built-in 전용 프로젝트여야 한다) — 조명·재질을 건너뛴다.");
            return;
        }

        lightCount = 0;
        pixelForcedCount = 0;

        // 멱등: 이전 S7_Dress가 남아 있으면 지우고 새로 만든다(Generated는 보통 매번 비워진다).
        Transform old = generated.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject groupGo = new GameObject(GroupName);
        Transform group = groupGo.transform;
        group.SetParent(generated, false);
        group.localPosition = Vector3.zero;
        group.localRotation = Quaternion.identity;
        group.localScale = Vector3.one;

        Mats m = LoadMaterials();
        Transform gimmicks = generated.Find(GimmickGroupName);

        float ceilingY = refs.serverRoomBounds.size.y > 0.01f ? refs.serverRoomBounds.max.y : FallbackCeilingY;
        bool hasSr = refs.serverRoomBounds.size.x > 0.01f && refs.serverRoomBounds.size.z > 0.01f;
        bool hasCr = refs.computerRoomBounds.size.x > 0.01f && refs.computerRoomBounds.size.z > 0.01f;
        if (!hasSr) Debug.LogError($"{LogTag} refs.serverRoomBounds 크기가 0이다 — 서버실 바닥 덮개·통로 등을 건너뛴다(S7_Builder 확인).");
        if (!hasCr) Debug.LogError($"{LogTag} refs.computerRoomBounds 크기가 0이다 — 컴퓨터실 조명·안감을 건너뛴다(S7_Builder 확인).");

        // 컴퓨터실 블록 = computerRoomBounds ∪ 유리벽(XZ). 규칙 [판정 15 · 계약 K7-1 :179]: 서버실 쪽 VIS(바닥 덮개·통로 등)는
        // 이 블록을 뺀다 = "두 Bounds가 겹치면 컴퓨터실 우선". 2차 serverRoomBounds (−25.5,0,14.5)~(41.5,14,109.5)는 서버실만이라
        // 블록과 교집합이 0 → 규칙은 자동 통과한다(초안 §3-3 :141·§7-7). 아래 빼기 코드는 B가 AABB를 다시 넓혀도 맞게 방어용으로 남긴다.
        Bounds crBlock = refs.computerRoomBounds;
        if (refs.glassWalls != null)
            foreach (Transform gw in refs.glassWalls)
                if (GeoBox(generated, gw, out Vector3 gc, out Vector3 gs)) crBlock.Encapsulate(new Bounds(gc, gs));

        // 1) GEO_ Visual 재질 — 콜라이더·트랜스폼 불변.
        var skip = new HashSet<Transform>();
        int serversPainted = PaintList(refs.servers, m.serverBody, "servers", skip);
        int glassPainted = PaintList(refs.glassWalls, m.glass, "glassWalls", skip);
        int wallsPainted = 0, ceilPainted = 0, deskPainted = 0, consolePainted = 0;
        PaintFrameVisuals(generated, group, gimmicks, skip, m, ref wallsPainted, ref ceilPainted, ref deskPainted, ref consolePainted);

        // 2) 서버실 바닥 덮개(컴퓨터실 블록 제외).
        int srTiles = hasSr ? BuildServerFloorTiles(group, refs.serverRoomBounds, hasCr ? crBlock : new Bounds(), hasCr, m.serverFloor) : 0;

        // 2-b) 서버실 서쪽 띠(serverRoomBounds 밖 · 컴퓨터실 블록 밖) — 어두운 바닥 덮개만, 등 없음(초안 §7-7·§14 #5).
        int westTiles = hasSr && hasCr ? BuildWestStripTiles(group, refs.serverRoomBounds, refs.computerRoomBounds, crBlock, m.serverFloor) : 0;

        // 3) 서버실 통로 등(낮은 세기, 소수).
        int aisleLamps = hasSr ? BuildAisleLamps(group, refs.serverRoomBounds, hasCr ? crBlock : new Bounds(), hasCr, ceilingY, m) : 0;

        // 4) 서버 LED + 포트 서버 표시(소켓·표시판·띠·글자·포트 앞 등).
        Dictionary<Transform, WiringPort> portByAnchor = FindPowerPorts(generated, refs.portAnchors);
        int leds = 0, portMarks = 0, portLights = 0, labels = 0;
        BuildServers(group, generated, refs, portByAnchor, m, ref leds, ref portMarks, ref portLights, ref labels);

        // 5) 컴퓨터실 — 바닥 덮개·벽 안감·등 2·책상 등.
        int crTiles = 0, crLiners = 0, crLamps = 0;
        if (hasCr) BuildComputerRoom(group, generated, refs, ceilingY, m, ref crTiles, ref crLiners, ref crLamps);

        // 6) 유리벽 틀.
        int glassFrames = BuildGlassFrames(group, generated, refs.glassWalls, m);

        // 7) 전실 등 + W_ENTRY 패널(판·소켓·글자·벽등).
        int hallLamps = BuildHallLamps(group, ceilingY, m);
        int entryPorts = BuildEntryPanel(group, generated, refs, portByAnchor, gimmicks, m, ref labels);

        // 8) 전력 콘솔 등 + 역할 모형.
        int roleLamps = 0, props = 0;
        BuildRoleAreas(group, generated, refs, gimmicks, ceilingY, m, ref roleLamps, ref props);

        // 8-b) 역할 표지 3개(글자) [판정 12].
        int roleLabels = BuildRoleLabels(group, generated, refs);

        // 9) 출구 표지 등(TEMP 기간 이름 기준점 [판정 17-S7]).
        int exitMarks = BuildExitSign(group, generated, refs, gimmicks, m);

        // 10) 콜라이더 전수 제거(계약 K0-3 "콜라이더 0개").
        int stripped = StripColliders(group);
        int remaining = group.GetComponentsInChildren<Collider>(true).Length;
        if (remaining != 0)
            Debug.LogError($"{LogTag} S7_Dress 아래 콜라이더 {remaining}개가 남았다 — 계약 K0-3 위반.");

        Debug.Log($"{LogTag} 완료 — 재질: 서버 {serversPainted} · 유리 {glassPainted} · 벽 {wallsPainted} · 천장 {ceilPainted} · 책상 {deskPainted} · 콘솔 {consolePainted} / " +
                  $"VIS: 서버실 바닥 칸 {srTiles} · 서쪽 띠 칸 {westTiles} · 통로 등 {aisleLamps} · LED {leds} · 포트 표시 {portMarks} · 포트 등 {portLights} · 포트 글자 {labels} · " +
                  $"컴퓨터실 바닥 {crTiles}·안감 {crLiners}·등 {crLamps} · 유리 틀 {glassFrames} · 전실 등 {hallLamps} · 입구 포트 {entryPorts} · " +
                  $"역할 등 {roleLamps} · 모형 {props} · 역할 표지 {roleLabels} · 출구 표지 {exitMarks} / 조명 합계 {lightCount}(ForcePixel {pixelForcedCount}, 나머지 Auto) · " +
                  $"제거한 기본 콜라이더 {stripped} · 남은 콜라이더 {remaining}.");
    }

    /// <summary>[판정 11 · 계약 K7-0 :597 · K7-2] 배선 선 머티리얼 도우미 — L 소유, W만 부른다. W는 반환값을 팀 public 필드
    /// `WiringLineRenderer.lineMaterial`에 인스펙터 값으로 넣는다(L은 팀 렌더러에 직접 넣지 않는다 — K0-3). 팀 코드는 lineMaterial이 null이면
    /// 선에 머티리얼을 넣지 않는다(WiringLineRenderer.cs:69, S7_팀API §1-3) → 선이 안 보일 수 있다(추측, Unity 실측 대기).
    /// `M4_S7_WiringLine.mat`(Standard 불투명 + 발광)을 있으면 재사용·없으면 생성해 돌려준다. Standard 셰이더가 없으면 null(LogError).
    /// refs·Apply 상태(static 카운터·Mats)에 기대지 않는다 — Wire 안에서 Apply보다 먼저 불려도 동작한다(아래는 상수와
    /// EnsureDir/GetOrCreateMaterial만 쓴다). 색·발광 값은 [제안].</summary>
    public static Material WiringLineMaterial()
    {
        EnsureDir(MaterialDir);
        Color c = new Color(1.00f, 0.92f, 0.55f); // [제안] 밝은 노랑빛 선
        return GetOrCreateMaterial("WiringLine", c, 0.3f, 0f, c * 2.0f); // [제안] 발광 ×2.0
    }

    // ── 1) GEO_ Visual 재질 ─────────────────────────────────────────────────────────────

    private static int PaintList(List<Transform> list, Material mat, string label, HashSet<Transform> done)
    {
        if (list == null)
        {
            Debug.LogError($"{LogTag} refs.{label}가 null이다 — 재질을 건너뛴다.");
            return 0;
        }
        int ok = 0, fail = 0;
        foreach (Transform t in list)
        {
            if (t != null) done.Add(t);
            if (PaintVisual(t, mat)) ok++;
            else fail++;
        }
        if (fail > 0)
            Debug.LogWarning($"{LogTag} refs.{label} {fail}개는 null이거나 '{VisualChildName}' 자식 MeshRenderer가 없어 재질을 못 입혔다.");
        return ok;
    }

    /// <summary>GEO_ 상자의 자식 Visual 렌더러 재질만 바꾼다(콜라이더·트랜스폼 불변).</summary>
    private static bool PaintVisual(Transform geo, Material mat)
    {
        if (geo == null || mat == null) return false;
        Transform vis = geo.Find(VisualChildName);
        if (vis == null) return false;
        MeshRenderer mr = vis.GetComponent<MeshRenderer>();
        if (mr == null) return false;
        mr.sharedMaterial = mat;
        return true;
    }

    /// <summary>벽·천장·책상·콘솔(GEO_S7_Wall_/Ceil/Desk/PowerConsole) Visual. 바닥(GEO_S7_Floor_ — 전실까지 한 장이라
    /// 칠하지 않고 서버실만 덮개로 어둡게)·문(GEO_S7_Door_)·서버·유리(리스트로 칠함)·팀 기믹·S7_Dress 아래는 대상이 아니다.
    /// 문 GEO는 되돌림(TempOpenDoors = false)에서만 있다 — TEMP 기간에는 문·문틀 VIS 0곳이라 이 분기에 걸리는 것이 없다 [판정 17-S7 · C10].
    /// 인방(GEO_S7_Wall_*_Lintel)은 GEO_S7_Wall 접두사라 벽 재질로 칠한다(PaintFrame).</summary>
    private static void PaintFrameVisuals(Transform generated, Transform dressGroup, Transform gimmicks, HashSet<Transform> done,
                                          Mats m, ref int walls, ref int ceils, ref int desks, ref int consoles)
    {
        var unknown = new List<string>();
        foreach (Transform t in generated.GetComponentsInChildren<Transform>(true))
        {
            if (IsUnder(t, gimmicks) || IsUnder(t, dressGroup) || done.Contains(t)) continue;
            string name = t.name;
            if (!name.StartsWith("GEO_S7_", System.StringComparison.Ordinal)) continue;
            if (name.StartsWith("GEO_S7_Wall", System.StringComparison.Ordinal))
            {
                if (PaintFrame && PaintVisual(t, m.wall)) walls++;
            }
            else if (name.StartsWith("GEO_S7_Ceil", System.StringComparison.Ordinal))
            {
                if (PaintFrame && PaintVisual(t, m.ceiling)) ceils++;
            }
            else if (name.StartsWith("GEO_S7_Desk", System.StringComparison.Ordinal))
            {
                if (PaintVisual(t, m.desk)) desks++;
            }
            else if (name.StartsWith("GEO_S7_PowerConsole", System.StringComparison.Ordinal))
            {
                if (PaintVisual(t, m.console)) consoles++;
            }
            else if (name.StartsWith("GEO_S7_Floor", System.StringComparison.Ordinal) ||
                     name.StartsWith("GEO_S7_Door", System.StringComparison.Ordinal) ||
                     name.StartsWith("GEO_S7_Server", System.StringComparison.Ordinal) ||
                     name.StartsWith("GEO_S7_Glass", System.StringComparison.Ordinal))
            {
                // 의도적으로 칠하지 않음(바닥·문) 또는 리스트로 이미 칠함(서버·유리 — 리스트에 없으면 아래 진단).
                if ((name.StartsWith("GEO_S7_Server", System.StringComparison.Ordinal) ||
                     name.StartsWith("GEO_S7_Glass", System.StringComparison.Ordinal)) && t.Find(VisualChildName) != null)
                    unknown.Add(name + "(리스트 밖)");
            }
            else if (t.Find(VisualChildName) != null)
            {
                unknown.Add(name);
            }
        }
        if (unknown.Count > 0)
            Debug.LogWarning($"{LogTag} 분류하지 못한 GEO_S7_ {unknown.Count}개(재질 그대로 둠): {string.Join(", ", unknown)}");
    }

    private static bool IsUnder(Transform t, Transform root) => root != null && t.IsChildOf(root);

    // ── 2) 서버실 바닥 덮개 ─────────────────────────────────────────────────────────────

    private static int BuildServerFloorTiles(Transform group, Bounds sr, Bounds crBlock, bool hasCr, Material mat)
    {
        var extraX = new List<float>();
        var extraZ = new List<float>();
        if (hasCr)
        {
            extraX.Add(crBlock.min.x); extraX.Add(crBlock.max.x);
            extraZ.Add(crBlock.min.z); extraZ.Add(crBlock.max.z);
        }
        List<float> xs = GridEdges(sr.min.x, sr.max.x, FloorTileSize, extraX);
        List<float> zs = GridEdges(sr.min.z, sr.max.z, FloorTileSize, extraZ);
        GameObject root = NewNode("VIS_S7_ServerFloor", group, Vector3.zero);
        float y = sr.min.y + FloorLift;
        int n = 0;
        for (int r = 0; r < zs.Count - 1; r++)
        {
            for (int c = 0; c < xs.Count - 1; c++)
            {
                Vector3 center = new Vector3((xs[c] + xs[c + 1]) * 0.5f, y, (zs[r] + zs[r + 1]) * 0.5f);
                if (hasCr && InsideXZ(crBlock, center)) continue; // 컴퓨터실 우선(초안 §7-7)
                FloorQuad(root.transform, $"VIS_S7_ServerFloor_{r + 1:00}_{c + 1:00}", center,
                          xs[c + 1] - xs[c], zs[r + 1] - zs[r], mat);
                n++;
            }
        }
        return n;
    }

    /// <summary>서버실 서쪽 띠 — serverRoomBounds(서버실만, [판정 15])가 L자 서버실을 다 못 덮어 빠진 곳(초안 §7-7: x −41.5~−25.5 ×
    /// z 14.5~19.5·48.5~109.5, 바닥의 14.2%)에 어두운 바닥 덮개만 깐다(등 없음 — 더 어둡게 남는 것은 의도, 초안 §14 #5).
    /// 범위는 refs에서만 파생: x = computerRoomBounds.min.x ~ serverRoomBounds.min.x, z = serverRoomBounds.min.z ~ max.z에서
    /// 컴퓨터실 블록(computerRoomBounds ∪ 유리) z 범위를 뺀 곳. 전실(z &lt; serverRoomBounds.min.z)로는 넘어가지 않는다.
    /// 띠 폭이 0 이하(B가 AABB를 서쪽 끝까지 넓힌 경우 등)면 만들지 않는다.</summary>
    private static int BuildWestStripTiles(Transform group, Bounds sr, Bounds cr, Bounds crBlock, Material mat)
    {
        float x0 = cr.min.x, x1 = sr.min.x;
        if (x1 - x0 <= 0.05f) return 0;
        var zSpans = new List<Vector2>();
        float zLo = sr.min.z, zHi = sr.max.z;
        float cutLo = Mathf.Clamp(crBlock.min.z, zLo, zHi), cutHi = Mathf.Clamp(crBlock.max.z, zLo, zHi);
        if (crBlock.max.x <= x0 + 0.05f || crBlock.min.x >= x1 - 0.05f) { cutLo = zHi; cutHi = zHi; } // 블록이 띠와 x로 안 겹치면 빼지 않음
        if (cutLo - zLo > 0.05f) zSpans.Add(new Vector2(zLo, cutLo));
        if (zHi - cutHi > 0.05f) zSpans.Add(new Vector2(cutHi, zHi));
        if (zSpans.Count == 0) return 0;

        GameObject root = NewNode("VIS_S7_ServerFloorWest", group, Vector3.zero);
        float y = sr.min.y + FloorLift;
        List<float> xs = GridEdges(x0, x1, FloorTileSize, new List<float>());
        int n = 0;
        for (int s = 0; s < zSpans.Count; s++)
        {
            List<float> zs = GridEdges(zSpans[s].x, zSpans[s].y, FloorTileSize, new List<float>());
            for (int r = 0; r < zs.Count - 1; r++)
            {
                for (int c = 0; c < xs.Count - 1; c++)
                {
                    Vector3 center = new Vector3((xs[c] + xs[c + 1]) * 0.5f, y, (zs[r] + zs[r + 1]) * 0.5f);
                    if (InsideXZ(crBlock, center)) continue; // 방어 — 컴퓨터실 우선
                    FloorQuad(root.transform, $"VIS_S7_ServerFloorWest_{s + 1}_{r + 1:00}_{c + 1:00}", center,
                              xs[c + 1] - xs[c], zs[r + 1] - zs[r], mat);
                    n++;
                }
            }
        }
        return n;
    }

    /// <summary>[min, max]를 step 간격으로 자르고 extra 경계를 끼운다(0.05 안쪽 중복 제거). 정수 개수로 계산해 float 누적 오차를 피한다.</summary>
    private static List<float> GridEdges(float min, float max, float step, List<float> extra)
    {
        var set = new SortedSet<float> { Round2(min), Round2(max) };
        int count = Mathf.CeilToInt((max - min) / step - 0.001f);
        for (int k = 1; k < count; k++) set.Add(Round2(min + k * step));
        foreach (float e in extra)
            if (e > min + 0.05f && e < max - 0.05f) set.Add(Round2(e));
        var result = new List<float>();
        foreach (float v in set)
        {
            if (result.Count > 0 && v - result[result.Count - 1] < 0.05f)
            {
                // 가까운 두 경계 — max·extra 쪽(뒤 값)을 남긴다.
                result[result.Count - 1] = v;
                continue;
            }
            result.Add(v);
        }
        if (result.Count > 0) result[0] = min;
        if (result.Count > 1) result[result.Count - 1] = max;
        return result;
    }

    private static float Round2(float v) => Mathf.Round(v * 100f) / 100f;

    private static bool InsideXZ(Bounds b, Vector3 p) =>
        p.x > b.min.x - 0.001f && p.x < b.max.x + 0.001f && p.z > b.min.z - 0.001f && p.z < b.max.z + 0.001f;

    /// <summary>바닥 덮개 판 — Quad(XY 평면, 앞면 −Z)를 X축 +90° 돌리면 앞면이 +Y. 로컬 Y 스케일이 월드 Z 크기.</summary>
    private static void FloorQuad(Transform parent, string name, Vector3 center, float sx, float sz, Material mat)
    {
        Prim(PrimitiveType.Quad, name, parent, center, Quaternion.Euler(90f, 0f, 0f), new Vector3(sx, sz, 1f), mat);
    }

    // ── 3) 서버실 통로 등 ───────────────────────────────────────────────────────────────

    private static int BuildAisleLamps(Transform group, Bounds sr, Bounds crBlock, bool hasCr, float ceilingY, Mats m)
    {
        GameObject root = NewNode("VIS_S7_AisleLamps", group, Vector3.zero);
        int n = 0;
        for (int i = 0; i < AisleLampPos.Length; i++)
        {
            Vector3 p = AisleLampPos[i];
            if (!InsideXZ(sr, p) || (hasCr && InsideXZ(crBlock, p)))
            {
                Debug.LogWarning($"{LogTag} 통로 등 {p}가 서버실 밖이거나 컴퓨터실 블록 안이라 건너뛴다(상수 표 AisleLampPos 확인).");
                continue;
            }
            Pendant(root.transform, $"VIS_S7_AisleLamp_{i + 1}", p, ShadeDiameter, ceilingY, m.aisleShade, m.cord,
                    LightType.Point, AisleLampColor, AisleLampIntensity, AisleLampRange, 0f, LightRenderMode.Auto);
            n++;
        }
        return n;
    }

    // ── 4) 서버 LED + 포트 서버 표시 ────────────────────────────────────────────────────

    /// <summary>W가 portAnchors 자리에 놓은 팀 WiringPort(읽기만 — portId·role public 필드). 없으면 빈 사전(계약 순서로 대체).</summary>
    private static Dictionary<Transform, WiringPort> FindPowerPorts(Transform generated, List<Transform> anchors)
    {
        var map = new Dictionary<Transform, WiringPort>();
        if (anchors == null) return map;
        WiringPort[] ports = generated.GetComponentsInChildren<WiringPort>(true);
        foreach (Transform a in anchors)
        {
            if (a == null) continue;
            foreach (WiringPort p in ports)
            {
                if (p == null) continue;
                if ((p.transform.position - a.position).sqrMagnitude <= SameSpotTolerance * SameSpotTolerance)
                {
                    map[a] = p;
                    break;
                }
            }
        }
        return map;
    }

    private static void BuildServers(Transform group, Transform generated, S7_Refs refs, Dictionary<Transform, WiringPort> portByAnchor,
                                     Mats m, ref int leds, ref int marks, ref int portLights, ref int labels)
    {
        if (refs.servers == null || refs.servers.Count == 0)
        {
            Debug.LogError($"{LogTag} refs.servers가 비었다 — 서버 LED·포트 표시를 건너뛴다.");
            return;
        }

        // 서버 인덱스 → 포트 인덱스(계약: portServerIndex 서로 다름, 서버 1대에 포트 1개 [확정 §3-1]).
        var portOfServer = new Dictionary<int, int>();
        int portCount = refs.portAnchors != null ? refs.portAnchors.Count : 0;
        if (refs.portServerIndex == null || refs.portServerIndex.Count != portCount)
            Debug.LogError($"{LogTag} portServerIndex 개수({(refs.portServerIndex == null ? -1 : refs.portServerIndex.Count)}) ≠ portAnchors 개수({portCount}) — 포트 표시를 건너뛴다.");
        else
        {
            for (int i = 0; i < portCount; i++)
            {
                int si = refs.portServerIndex[i];
                if (si < 0 || si >= refs.servers.Count || refs.portAnchors[i] == null)
                {
                    Debug.LogError($"{LogTag} 포트 {i}의 서버 인덱스 {si}가 범위 밖이거나 앵커가 null — 이 포트 표시를 건너뛴다.");
                    continue;
                }
                if (portOfServer.ContainsKey(si))
                {
                    Debug.LogError($"{LogTag} 서버 {si}에 포트가 둘 이상(포트 {portOfServer[si]}·{i}) — 계약 K7-1 위반, 뒤 포트 표시를 건너뛴다.");
                    continue;
                }
                portOfServer[si] = i;
            }
        }
        if (portOfServer.Count != 6)
            Debug.LogWarning($"{LogTag} 표시할 포트 서버 {portOfServer.Count}개 ≠ 계약 6개.");

        GameObject root = NewNode("VIS_S7_Servers", group, Vector3.zero);
        for (int s = 0; s < refs.servers.Count; s++)
        {
            Transform sv = refs.servers[s];
            if (!GeoBox(generated, sv, out Vector3 c, out Vector3 size))
            {
                Debug.LogWarning($"{LogTag} 서버 {s}의 상자를 읽지 못했다(null 또는 Visual 없음) — 이 서버 장식을 건너뛴다.");
                continue;
            }
            string sName = $"VIS_S7_Server_{s + 1}";
            GameObject sGo = NewNode(sName, root.transform, Vector3.zero);

            bool hasPort = portOfServer.TryGetValue(s, out int pi);
            Transform anchor = hasPort ? refs.portAnchors[pi] : null;
            // n = 포트 면 바깥 법선 = 앵커 forward(축 정렬). 안C: 앞줄 +Z(면 = 서버 max z 30), 뒷줄 −Z(면 = min z 44) — 소켓·LED·띠·글자가 이 면에 붙는다.
            // 포트 없는 서버의 기본 n (0,0,−1)은 안C에서 6대 모두 포트가 있어 쓰이지 않는다(방어용).
            Vector3 n = hasPort ? AxisSnapH(generated.InverseTransformDirection(anchor.forward)) : new Vector3(0f, 0f, -1f);
            Vector3 anchorPos = hasPort ? generated.InverseTransformPoint(anchor.position) : Vector3.zero;

            // LED — 앞(n)·뒤(−n) 두 면, 포트 소켓 자리 피함.
            leds += BuildLeds(sGo.transform, sName + "_LedF", c, size, n, s, 0, hasPort ? anchorPos : (Vector3?)null, m);
            leds += BuildLeds(sGo.transform, sName + "_LedB", c, size, -n, s, 1, null, m);

            if (!hasPort) continue;

            // 포트 ID·역할 — W의 팀 WiringPort가 앵커 자리에 있으면 그 값, 없으면 계약 순서.
            string id = pi < ContractPortIds.Length ? ContractPortIds[pi] : pi.ToString();
            bool isOut = pi < 3;
            if (portByAnchor.TryGetValue(anchor, out WiringPort wp) && wp != null)
            {
                string wid = wp.portId ?? "";
                bool wOut = wp.role == WiringPort.Role.Output;
                if (wid != id || wOut != isOut)
                    Debug.LogWarning($"{LogTag} {anchor.name}: W의 WiringPort(portId '{wid}', {wp.role}) ≠ 계약 순서('{id}', {(isOut ? "Output" : "Input")}) — 표시는 W 값을 따른다.");
                if (wid.Length > 0) id = wid;
                isOut = wOut;
            }
            Material roleMat = isOut ? m.portOut : m.portIn;
            Color roleColor = isOut ? PortOutColor : PortInColor;

            float ext = Mathf.Abs(Vector3.Dot(size * 0.5f, Abs(n)));
            Vector3 t = Vector3.Cross(Vector3.up, n).normalized;
            float w = Mathf.Abs(Vector3.Dot(size, Abs(t)));
            float top = c.y + size.y * 0.5f;

            // 소켓(앵커 = 서버 앞면 위, 초안 §2-3).
            marks += BuildSocket(sGo.transform, $"{sName}_Port_{id}", anchorPos, n, roleMat, m.socketHole);

            // 윗면 표시판(어느 방향에서든 보임, 초안 S7_동선분석 §4 "윗면에도 두면").
            Prim(PrimitiveType.Cube, $"{sName}_Beacon", sGo.transform,
                 new Vector3(c.x, top + BeaconThick * 0.5f, c.z), Quaternion.LookRotation(n, Vector3.up),
                 new Vector3(w * BeaconFrac, BeaconThick, ext * 2f * BeaconFrac), roleMat);
            marks++;

            // 앞·뒷면 상태 띠.
            for (int f = 0; f < 2; f++)
            {
                Vector3 fn = f == 0 ? n : -n;
                Vector3 sp = new Vector3(c.x, top - StripBelowTop, c.z) + fn * (ext + StripDepth * 0.5f - 0.01f);
                Prim(PrimitiveType.Cube, $"{sName}_Strip_{(f == 0 ? "F" : "B")}", sGo.transform, sp,
                     Quaternion.LookRotation(fn, Vector3.up), new Vector3(w - 0.4f, StripHeight, StripDepth), roleMat);
                marks++;
            }

            // 포트 앞 작은 등.
            GameObject pl = NewNode($"{sName}_PortLight", sGo.transform,
                                    anchorPos + n * PortLightForward + Vector3.up * PortLightUp);
            AddLight(pl, LightType.Point, roleColor, PortLightIntensity, PortLightRange, 0f, LightRenderMode.Auto);
            portLights++;

            // 글자 — 앞면(소켓 위)·윗면(표시판 위, 누워서)·뒷면.
            if (ShowPortLabels)
            {
                float ly = Mathf.Min(anchorPos.y + LabelAboveSocket, top - LabelBelowTop);
                Vector3 frontPos = new Vector3(anchorPos.x, ly, anchorPos.z) + n * 0.02f;
                if (MakeLabel(sGo.transform, $"{sName}_Label_F", frontPos, Quaternion.LookRotation(-n, Vector3.up), id, roleColor, LabelCharSize)) labels++;
                // 윗면 글자 — 6대 모두 n과 무관한 고정 up 축 TopLabelUp(+Z 진행 [계약 K0-2])으로 눕힌다 [판정 C12 부수 · 초안 :168 입구 식별용].
                // 앞면(로컬 −Z)이 위(+Y)를 보고, 글자 위쪽 = +Z(북)·글자 오른쪽 = +X → 입구·콘솔(남)에서 북쪽을 보면 바로 읽힌다.
                // (S7-L3까지는 up = −n이라 앞줄(n +Z)·뒷줄(n −Z)이 서로 반대로 뒤집혀 있었다.)
                Vector3 topPos = new Vector3(c.x, top + BeaconThick + 0.01f, c.z);
                if (MakeLabel(sGo.transform, $"{sName}_Label_T", topPos, Quaternion.LookRotation(Vector3.down, TopLabelUp), id, TopLabelColor, TopLabelCharSize)) labels++;
                Vector3 backPos = new Vector3(c.x, ly, c.z) - n * (ext + 0.02f);
                if (MakeLabel(sGo.transform, $"{sName}_Label_B", backPos, Quaternion.LookRotation(n, Vector3.up), id, roleColor, LabelCharSize)) labels++;
            }
        }
    }

    /// <summary>한 면(바깥 법선 n)의 왼쪽 가장자리 LED 격자. 켜짐/꺼짐·색은 서버·면·행·열로 정한 고정 무늬(재생성해도 같음).</summary>
    private static int BuildLeds(Transform parent, string baseName, Vector3 c, Vector3 size, Vector3 n, int serverIdx, int faceIdx,
                                 Vector3? avoid, Mats m)
    {
        Vector3 t = Vector3.Cross(Vector3.up, n).normalized;
        float ext = Mathf.Abs(Vector3.Dot(size * 0.5f, Abs(n)));
        float w = Mathf.Abs(Vector3.Dot(size, Abs(t)));
        float h = size.y;
        if (w < LedMarginX * 2f + LedSpacingX * LedCols || h < LedMarginY * 2f + 0.1f) return 0;
        Vector3 faceCenter = c + n * ext;
        Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
        float stepY = LedRows > 1 ? (h - 2f * LedMarginY) / (LedRows - 1) : 0f;
        int count = 0;
        for (int r = 0; r < LedRows; r++)
        {
            float v = -h * 0.5f + LedMarginY + r * stepY;
            for (int k = 0; k < LedCols; k++)
            {
                int hash = (serverIdx * 97 + faceIdx * 53 + r * 31 + k * 17) % 11;
                if (hash < 3) continue;                                  // 꺼진 LED
                float u = -w * 0.5f + LedMarginX + k * LedSpacingX;
                Vector3 pos = faceCenter + t * u + Vector3.up * v + n * (LedSize.z * 0.5f - LedEmbed);
                if (avoid.HasValue)
                {
                    Vector3 d = pos - avoid.Value;
                    if (Mathf.Abs(Vector3.Dot(d, t)) < SocketSize.x * 0.5f + 0.25f &&
                        Mathf.Abs(d.y) < SocketSize.y * 0.5f + 0.25f) continue; // 소켓 자리
                }
                Prim(PrimitiveType.Cube, $"{baseName}_{r + 1}_{k + 1}", parent, pos, rot, LedSize,
                     hash < 8 ? m.ledGreen : m.ledBlue);
                count++;
            }
        }
        return count;
    }

    /// <summary>포트 소켓 — 역할 색 발광 판 + 가운데 어두운 구멍. pos = 붙는 면 위 점, n = 면 바깥 법선. 반환 = 만든 표시 수(1).</summary>
    private static int BuildSocket(Transform parent, string name, Vector3 pos, Vector3 n, Material roleMat, Material holeMat)
    {
        Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
        Prim(PrimitiveType.Cube, name, parent, pos + n * (SocketBack + SocketSize.z * 0.5f), rot, SocketSize, roleMat);
        Prim(PrimitiveType.Cube, name + "_Hole", parent, pos + n * (SocketBack + SocketSize.z + SocketHoleSize.z * 0.5f),
             rot, SocketHoleSize, holeMat);
        return 1;
    }

    /// <summary>글자(포트 ID·역할 표지) — Unity 내장 TextMesh(콜라이더 없음). 팀 선례 Ch8TestRoomMenuItem.cs:258-267(LegacyRuntime.ttf +
    /// font.material)를 따르고, 폰트를 못 얻으면 V3_Dress.cs:585-599처럼 경고 후 건너뛴다(빌드 계속).
    /// TextMesh 앞면은 로컬 −Z 쪽에서 보인다 → rot의 forward = 보는 사람에서 멀어지는 쪽.</summary>
    private static bool MakeLabel(Transform parent, string name, Vector3 localPos, Quaternion localRot, string text, Color color, float charSize)
    {
        return MakeLabel(parent, name, localPos, localRot, text, color, charSize, LabelFontSize);
    }

    private static bool MakeLabel(Transform parent, string name, Vector3 localPos, Quaternion localRot, string text, Color color, float charSize,
                                  int fontSize)
    {
        Font font = null;
        try
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"{LogTag} 내장 폰트 로드 실패({e.Message}) — 글자 '{text}'를 건너뛴다.");
        }
        if (font == null)
        {
            Debug.LogWarning($"{LogTag} LegacyRuntime.ttf·Arial.ttf 둘 다 없음 — 글자 '{text}'를 건너뛴다.");
            return false;
        }
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = Vector3.one;
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = fontSize;
        tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = font.material; // 내장 폰트 머티리얼을 참조만 한다(수정하지 않음).
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return true;
    }

    // ── 5) 컴퓨터실 ─────────────────────────────────────────────────────────────────────

    private static void BuildComputerRoom(Transform group, Transform generated, S7_Refs refs, float ceilingY, Mats m,
                                          ref int tiles, ref int liners, ref int lamps)
    {
        Bounds cr = refs.computerRoomBounds;
        GameObject root = NewNode("VIS_S7_ComputerRoom", group, Vector3.zero);

        // 바닥 덮개 1장(등 3개 ≤ Ultra 픽셀 4 — 칸으로 나눌 필요 없음).
        FloorQuad(root.transform, "VIS_S7_CR_Floor", new Vector3(cr.center.x, cr.min.y + FloorLift, cr.center.z),
                  cr.size.x, cr.size.z, m.crFloor);
        tiles++;

        // 벽 안감 — 네 면 중 유리 면은 빼고, 개구부가 있는 면은 개구부 폭만큼 비운다.
        int glassSide = -1;
        if (refs.glassWalls != null)
        {
            foreach (Transform gw in refs.glassWalls)
            {
                if (!GeoBox(generated, gw, out Vector3 gc, out Vector3 gs)) continue;
                glassSide = NearestSide(cr, gc, out float gd);
                if (gd > 1.0f) glassSide = -1; // 유리가 컴퓨터실 면에 붙어 있지 않다
                break;
            }
        }
        if (glassSide < 0) Debug.LogWarning($"{LogTag} 유리벽이 컴퓨터실 면에 붙어 있지 않다 — 안감을 네 면 모두에 붙인다(확인 필요).");

        int doorSide = -1;
        float doorCoord = 0f;
        if (refs.computerRoomDoorway != null)
        {
            Vector3 dp = generated.InverseTransformPoint(refs.computerRoomDoorway.position);
            doorSide = NearestSide(cr, dp, out float dd);
            if (dd > 1.0f) { doorSide = -1; Debug.LogWarning($"{LogTag} computerRoomDoorway {dp}가 컴퓨터실 면에서 {dd:F2} 떨어져 있다 — 안감에 개구부를 비우지 않는다(확인 필요)."); }
            doorCoord = (doorSide == 0 || doorSide == 1) ? dp.z : dp.x;
        }
        else Debug.LogWarning($"{LogTag} refs.computerRoomDoorway가 null — 안감에 개구부를 비우지 않는다(확인 필요).");

        for (int side = 0; side < 4; side++)
        {
            if (side == glassSide) continue;
            // side: 0 = 서(−x 면, 안쪽 법선 +X) 1 = 동(+x, −X) 2 = 남(−z, +Z) 3 = 북(+z, −Z)
            bool alongZ = side <= 1;
            float lo = alongZ ? cr.min.z : cr.min.x;
            float hi = alongZ ? cr.max.z : cr.max.x;
            var spans = new List<Vector2>();
            if (side == doorSide)
            {
                float a = Mathf.Clamp(doorCoord - DoorwayHalfWidth, lo, hi);
                float b = Mathf.Clamp(doorCoord + DoorwayHalfWidth, lo, hi);
                if (a - lo > 0.05f) spans.Add(new Vector2(lo, a));
                if (hi - b > 0.05f) spans.Add(new Vector2(b, hi));
            }
            else spans.Add(new Vector2(lo, hi));

            Vector3 inward = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
            float plane = side == 0 ? cr.min.x : side == 1 ? cr.max.x : side == 2 ? cr.min.z : cr.max.z;
            // Quad 앞면(−Z 로컬)이 inward를 보게: LookRotation(−inward). 로컬 X = Cross(up, −inward) = 벽을 따라가는 축.
            Quaternion rot = Quaternion.LookRotation(-inward, Vector3.up);
            for (int k = 0; k < spans.Count; k++)
            {
                float mid = (spans[k].x + spans[k].y) * 0.5f;
                float len = spans[k].y - spans[k].x;
                Vector3 center = alongZ
                    ? new Vector3(plane, cr.center.y, mid) + inward * LinerInset
                    : new Vector3(mid, cr.center.y, plane) + inward * LinerInset;
                Prim(PrimitiveType.Quad, $"VIS_S7_CR_Liner_{side}_{k + 1}", root.transform, center, rot,
                     new Vector3(len, cr.size.y, 1f), m.crWall);
                liners++;
            }
        }

        // 천장 매단 등 2개 — 방 긴 축을 3등분한 자리.
        bool longZ = cr.size.z >= cr.size.x;
        for (int i = 0; i < 2; i++)
        {
            float f = (i + 1) / 3f;
            Vector3 p = longZ
                ? new Vector3(cr.center.x, CrLampY, cr.min.z + cr.size.z * f)
                : new Vector3(cr.min.x + cr.size.x * f, CrLampY, cr.center.z);
            Pendant(root.transform, $"VIS_S7_CR_Lamp_{i + 1}", p, ShadeDiameter, ceilingY, m.warmShade, m.cord,
                    LightType.Point, CrLampColor, CrLampIntensity, CrLampRange, 0f, LightRenderMode.Auto);
            lamps++;
        }
    }

    /// <summary>점 p에 가장 가까운 Bounds 옆면(0 서 −x, 1 동 +x, 2 남 −z, 3 북 +z)과 그 평면까지 수평 거리.</summary>
    private static int NearestSide(Bounds b, Vector3 p, out float dist)
    {
        float[] d = { Mathf.Abs(p.x - b.min.x), Mathf.Abs(p.x - b.max.x), Mathf.Abs(p.z - b.min.z), Mathf.Abs(p.z - b.max.z) };
        int best = 0;
        for (int i = 1; i < 4; i++) if (d[i] < d[best]) best = i;
        dist = d[best];
        return best;
    }

    // ── 6) 유리벽 틀 ────────────────────────────────────────────────────────────────────

    private static int BuildGlassFrames(Transform group, Transform generated, List<Transform> glassWalls, Mats m)
    {
        if (glassWalls == null) return 0;
        GameObject root = NewNode("VIS_S7_GlassFrames", group, Vector3.zero);
        int n = 0;
        for (int g = 0; g < glassWalls.Count; g++)
        {
            if (!GeoBox(generated, glassWalls[g], out Vector3 c, out Vector3 size)) continue;
            bool thinX = size.x <= size.z;
            float thin = thinX ? size.x : size.z;
            float span = thinX ? size.z : size.x;
            float spanMin = (thinX ? c.z : c.x) - span * 0.5f;
            float bottom = c.y - size.y * 0.5f;
            float top = c.y + size.y * 0.5f;
            string gName = $"VIS_S7_Glass_{g + 1}";

            // 세로 창살 — 양 끝 포함, 간격 ≤ GlassMullionSpacing.
            int segs = Mathf.Max(1, Mathf.CeilToInt(span / GlassMullionSpacing - 0.001f));
            for (int k = 0; k <= segs; k++)
            {
                float s = spanMin + GlassMullionWidth * 0.5f + (span - GlassMullionWidth) * k / segs;
                Vector3 p = thinX ? new Vector3(c.x, c.y, s) : new Vector3(s, c.y, c.z);
                Prim(PrimitiveType.Cube, $"{gName}_Mullion_{k + 1:00}", root.transform, p, Quaternion.identity,
                     AxisSize(thinX, thin + 0.02f, size.y, GlassMullionWidth), m.glassFrame);
                n++;
            }
            // 바닥·윗 레일(창살보다 조금 두껍게 — 교차면 z-fighting 방지), 눈높이 띠(조금 얇게).
            Prim(PrimitiveType.Cube, $"{gName}_RailBottom", root.transform, new Vector3(c.x, bottom + GlassBottomRailH * 0.5f, c.z),
                 Quaternion.identity, AxisSize(thinX, thin + 0.024f, GlassBottomRailH, span), m.glassFrame);
            Prim(PrimitiveType.Cube, $"{gName}_RailTop", root.transform, new Vector3(c.x, top - GlassTopRailH * 0.5f, c.z),
                 Quaternion.identity, AxisSize(thinX, thin + 0.024f, GlassTopRailH, span), m.glassFrame);
            Prim(PrimitiveType.Cube, $"{gName}_Band", root.transform, new Vector3(c.x, bottom + GlassBandY, c.z),
                 Quaternion.identity, AxisSize(thinX, thin + 0.016f, GlassBandH, span), m.glassBand);
            n += 3;
        }
        return n;
    }

    /// <summary>얇은 축이 X면 (thin, h, span), 아니면 (span, h, thin).</summary>
    private static Vector3 AxisSize(bool thinX, float thin, float h, float span) =>
        thinX ? new Vector3(thin, h, span) : new Vector3(span, h, thin);

    // ── 7) 전실 등 + W_ENTRY 패널 ───────────────────────────────────────────────────────

    private static int BuildHallLamps(Transform group, float ceilingY, Mats m)
    {
        GameObject root = NewNode("VIS_S7_HallLamps", group, Vector3.zero);
        for (int i = 0; i < HallLampPos.Length; i++)
            Pendant(root.transform, $"VIS_S7_HallLamp_{i + 1}", HallLampPos[i], ShadeDiameter, ceilingY, m.warmShade, m.cord,
                    LightType.Point, CrLampColor, HallLampIntensity, HallLampRange, 0f, LightRenderMode.Auto);
        return HallLampPos.Length;
    }

    /// <summary>W_ENTRY — 앵커 위 벽등 + (W가 놓은 입구 WiringPort가 있으면) 포트 줄 둘레 판·소켓·글자.
    /// 입구 포트 = generated 아래 팀 WiringPort 중 전력 포트(portAnchors 자리)가 아닌 것, 앵커에서 EntryPortMaxDist 안.</summary>
    private static int BuildEntryPanel(Transform group, Transform generated, S7_Refs refs, Dictionary<Transform, WiringPort> powerPorts,
                                      Transform gimmicks, Mats m, ref int labels)
    {
        if (refs.entryPanelAnchor == null)
        {
            Debug.LogWarning($"{LogTag} refs.entryPanelAnchor가 null — W_ENTRY 조명·표시를 건너뛴다.");
            return 0;
        }
        GameObject root = NewNode("VIS_S7_EntryPanel", group, Vector3.zero);
        Vector3 ap = generated.InverseTransformPoint(refs.entryPanelAnchor.position);
        Vector3 n = AxisSnapH(generated.InverseTransformDirection(refs.entryPanelAnchor.forward));
        Vector3 t = Vector3.Cross(Vector3.up, n).normalized;

        // 벽등(Spot) — 앵커 위 벽에서 서는 자리를 겨눔 + 작은 발광 갓.
        Vector3 lp = ap + Vector3.up * EntryPanelLampUp + n * EntryPanelLampOut;
        Vector3 aim = new Vector3(ap.x, 0f, ap.z) + n * EntryPanelLampAim;
        GameObject lampGo = NewNode("VIS_S7_EntryPanel_Lamp", root.transform, lp);
        Prim(PrimitiveType.Cube, "VIS_S7_EntryPanel_Lamp_Mesh", lampGo.transform, -n * (EntryPanelLampOut * 0.5f),
             Quaternion.LookRotation(n, Vector3.up), new Vector3(0.6f, 0.12f, EntryPanelLampOut), m.warmShade);
        GameObject spot = NewNode("VIS_S7_EntryPanel_Lamp_Light", lampGo.transform, Vector3.zero);
        spot.transform.localRotation = Quaternion.LookRotation(aim - lp, Vector3.up);
        AddLight(spot, LightType.Spot, CrLampColor, EntryPanelLampIntensity, EntryPanelLampRange, EntryPanelLampSpotAngle, LightRenderMode.Auto);

        // 입구 포트 찾기(읽기만).
        var powerSet = new HashSet<WiringPort>(powerPorts.Values);
        var entry = new List<WiringPort>();
        foreach (WiringPort p in generated.GetComponentsInChildren<WiringPort>(true))
        {
            if (p == null || powerSet.Contains(p)) continue;
            bool onPowerAnchor = false;
            if (refs.portAnchors != null)
                foreach (Transform a in refs.portAnchors)
                    if (a != null && (p.transform.position - a.position).sqrMagnitude <= SameSpotTolerance * SameSpotTolerance) onPowerAnchor = true;
            if (onPowerAnchor) continue;
            if ((generated.InverseTransformPoint(p.transform.position) - ap).magnitude > EntryPortMaxDist)
            {
                Debug.LogWarning($"{LogTag} WiringPort '{p.name}'가 W_ENTRY 앵커에서 {EntryPortMaxDist}보다 멀다 — 입구 포트 표시에서 뺀다.");
                continue;
            }
            entry.Add(p);
        }
        if (entry.Count == 0)
        {
            Debug.LogWarning($"{LogTag} W_ENTRY 앵커 근처에 팀 WiringPort가 없다(S7_Wiring 결과 확인) — 벽등만 만든다.");
            return 0;
        }
        if (entry.Count != 6)
            Debug.LogWarning($"{LogTag} 입구 포트 {entry.Count}개 ≠ 초안 6개.");

        // 포트 줄 둘레 판(벽 면 위, 포트보다 뒤) — "포트가 한곳에 모인 패널" 표시(초안 §7-9 참고).
        float uMin = float.MaxValue, uMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        foreach (WiringPort p in entry)
        {
            Vector3 lpP = generated.InverseTransformPoint(p.transform.position);
            float u = Vector3.Dot(lpP - ap, t);
            uMin = Mathf.Min(uMin, u); uMax = Mathf.Max(uMax, u);
            yMin = Mathf.Min(yMin, lpP.y); yMax = Mathf.Max(yMax, lpP.y);
        }
        float plateH = Mathf.Max(EntryPlateHeight, (yMax - yMin) + SocketSize.y + EntryPlateMargin * 2f);
        float plateCy = (yMin + yMax) * 0.5f + 0.35f;
        float u0 = uMin - SocketSize.x * 0.5f - EntryPlateMargin, u1 = uMax + SocketSize.x * 0.5f + EntryPlateMargin;
        // 판이 솔리드 GEO(칸막이 등)에 묻히지 않게 벽을 따라가는 축(t)에서 겹치는 쪽 끝을 잘라 낸다(읽기만 — 콜라이더·트랜스폼 불변).
        int clipped = ClipSpanAgainstSolids(generated, gimmicks, group, ap, t, n, 0f, EntryPlateDepth,
                                            plateCy - plateH * 0.5f, plateCy + plateH * 0.5f, ref u0, ref u1);
        if (clipped > 0)
            Debug.Log($"{LogTag} W_ENTRY 판이 솔리드 {clipped}개와 겹쳐 끝을 잘랐다 — 판 범위 u {u0:0.##}~{u1:0.##}(앵커 기준).");
        if (u1 - u0 > 0.1f)
        {
            Vector3 plateCenter = ap + t * ((u0 + u1) * 0.5f) + Vector3.up * (plateCy - ap.y) + n * (EntryPlateDepth * 0.5f);
            Prim(PrimitiveType.Cube, "VIS_S7_EntryPanel_Plate", root.transform, plateCenter, Quaternion.LookRotation(n, Vector3.up),
                 new Vector3(u1 - u0, plateH, EntryPlateDepth), m.entryPlate);
        }
        else Debug.LogWarning($"{LogTag} W_ENTRY 판 자리가 솔리드에 막혀 판을 만들지 않는다(소켓·글자는 만든다).");

        int count = 0;
        foreach (WiringPort p in entry)
        {
            Vector3 pp = generated.InverseTransformPoint(p.transform.position);
            // 소켓은 판 앞(판 두께만큼 띄움). 포트가 벽 면(앵커 면)보다 앞에 있으면 그 자리 그대로.
            float outFromWall = Vector3.Dot(pp - ap, n);
            Vector3 sockBase = pp + n * Mathf.Max(0f, EntryPlateDepth - outFromWall);
            bool isOut = p.role == WiringPort.Role.Output;
            string id = string.IsNullOrEmpty(p.portId) ? "?" : p.portId;
            BuildSocket(root.transform, $"VIS_S7_EntryPort_{id}", sockBase, n, isOut ? m.portOut : m.portIn, m.socketHole);
            if (ShowPortLabels &&
                MakeLabel(root.transform, $"VIS_S7_EntryPort_{id}_Label", sockBase + Vector3.up * 0.75f + n * 0.03f,
                          Quaternion.LookRotation(-n, Vector3.up), id, isOut ? PortOutColor : PortInColor, LabelCharSize * 0.7f))
                labels++;
            count++;
        }
        return count;
    }

    // ── 8) 전력 콘솔 등 + 역할 모형 ─────────────────────────────────────────────────────

    private static void BuildRoleAreas(Transform group, Transform generated, S7_Refs refs, Transform gimmicks, float ceilingY, Mats m,
                                       ref int roleLamps, ref int props)
    {
        GameObject root = NewNode("VIS_S7_RoleAreas", group, Vector3.zero);
        Transform dress = group;

        // 전력 콘솔 — 스탠드 등(ForcePixel) + 화면.
        if (refs.rolePowerAnchor != null)
        {
            Vector3 p = generated.InverseTransformPoint(refs.rolePowerAnchor.position);
            Vector3 f = AxisSnapH(generated.InverseTransformDirection(refs.rolePowerAnchor.forward));
            Pendant(root.transform, "VIS_S7_ConsoleLamp", p + Vector3.up * ConsoleLampHeight, RoleShadeDiameter, ceilingY,
                    m.warmShade, m.cord, LightType.Spot, ConsoleLampColor, ConsoleLampIntensity, ConsoleLampRange,
                    ConsoleLampSpotAngle, LightRenderMode.ForcePixel);
            roleLamps++;
            if (BuildRoleProps)
            {
                float topY = TopUnder(generated, gimmicks, dress, p, FallbackConsoleTop);
                // 화면: 윗면 위에 플레이어 쪽(f)으로 기울여 세움. 앞면 = f.
                Quaternion rot = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(-ConsoleScreenTilt, 0f, 0f);
                Vector3 sc = new Vector3(p.x, topY + ConsoleScreenSize.y * 0.5f * Mathf.Cos(ConsoleScreenTilt * Mathf.Deg2Rad) + 0.02f, p.z);
                Prim(PrimitiveType.Cube, "VIS_S7_ConsoleScreen_Body", root.transform, sc, rot,
                     ConsoleScreenSize, m.propDark);
                Prim(PrimitiveType.Quad, "VIS_S7_ConsoleScreen", root.transform,
                     sc + rot * new Vector3(0f, 0f, ConsoleScreenSize.z * 0.5f + 0.005f),
                     rot * Quaternion.Euler(0f, 180f, 0f), // Quad 앞면(−Z)을 f 쪽으로
                     new Vector3(ConsoleScreenSize.x * 0.9f, ConsoleScreenSize.y * 0.85f, 1f), m.screen);
                props += 2;
            }
        }
        else Debug.LogWarning($"{LogTag} refs.rolePowerAnchor가 null — 콘솔 등·화면을 건너뛴다.");

        // 책상 — 스탠드 등(ForcePixel, 책·컴퓨터 가운데 위) + 컴퓨터·가이드북 모형.
        Transform book = refs.roleBookAnchor, comp = refs.roleComputerAnchor;
        if (book != null || comp != null)
        {
            Vector3 bp = book != null ? generated.InverseTransformPoint(book.position) : generated.InverseTransformPoint(comp.position);
            Vector3 cp = comp != null ? generated.InverseTransformPoint(comp.position) : bp;
            Vector3 mid = (bp + cp) * 0.5f;
            float deskTop = TopUnder(generated, gimmicks, dress, mid, FallbackDeskTop);
            Pendant(root.transform, "VIS_S7_DeskLamp", new Vector3(mid.x, deskTop + DeskLampAboveTop, mid.z), RoleShadeDiameter, ceilingY,
                    m.warmShade, m.cord, LightType.Spot, CrLampColor, DeskLampIntensity, DeskLampRange,
                    DeskLampSpotAngle, LightRenderMode.ForcePixel);
            roleLamps++;
        }
        else Debug.LogWarning($"{LogTag} roleBookAnchor·roleComputerAnchor가 모두 null — 책상 등을 건너뛴다.");

        if (!BuildRoleProps) return;

        if (comp != null)
        {
            Vector3 p = generated.InverseTransformPoint(comp.position);
            Vector3 f = AxisSnapH(generated.InverseTransformDirection(comp.forward)); // 플레이어 쪽
            float topY = TopUnder(generated, gimmicks, dress, p, FallbackDeskTop);
            Quaternion rot = Quaternion.LookRotation(f, Vector3.up);
            Vector3 monC = p - f * MonitorBack + Vector3.up * (topY - p.y + MonitorLift + MonitorSize.y * 0.5f);
            Prim(PrimitiveType.Cube, "VIS_S7_Computer_Monitor", root.transform, monC, rot, MonitorSize, m.propDark);
            Prim(PrimitiveType.Quad, "VIS_S7_Computer_Screen", root.transform, monC + f * (MonitorSize.z * 0.5f + 0.005f),
                 Quaternion.LookRotation(-f, Vector3.up), new Vector3(MonitorSize.x * 0.9f, MonitorSize.y * 0.85f, 1f), m.screen);
            Prim(PrimitiveType.Cube, "VIS_S7_Computer_Stand", root.transform,
                 p - f * MonitorBack + Vector3.up * (topY - p.y + MonitorLift * 0.5f), rot,
                 new Vector3(0.12f, MonitorLift, 0.12f), m.propDark);
            Prim(PrimitiveType.Cube, "VIS_S7_Computer_Keyboard", root.transform,
                 p + f * KeyboardFront + Vector3.up * (topY - p.y + KeyboardSize.y * 0.5f), rot, KeyboardSize, m.propDark);
            props += 4;
        }
        if (book != null)
        {
            Vector3 p = generated.InverseTransformPoint(book.position);
            Vector3 f = AxisSnapH(generated.InverseTransformDirection(book.forward));
            float topY = TopUnder(generated, gimmicks, dress, p, FallbackDeskTop);
            Quaternion rot = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(0f, BookYaw, 0f);
            Prim(PrimitiveType.Cube, "VIS_S7_GuideBook", root.transform, p + Vector3.up * (topY - p.y + BookSize.y * 0.5f),
                 rot, BookSize, m.book);
            props++;
        }
    }

    /// <summary>벽 면 판(면 위 기준점 origin, 벽을 따라가는 축 t, 바깥 법선 n — 모두 축 정렬)의 t 방향 범위 [u0, u1]을 솔리드(비트리거) BoxCollider와
    /// 겹치지 않게 줄인다. 판 부피 = t [u0,u1] × n [d0,d1] × y [y0,y1](섹터 로컬). 겹치는 상자마다 판 가운데보다 먼 쪽 끝을 상자 면까지 당긴다.
    /// 팀 기믹(S7_Gimmicks)·S7_Dress 아래는 보지 않는다. 반환 = 자른 상자 수. 읽기만 한다.</summary>
    private static int ClipSpanAgainstSolids(Transform generated, Transform gimmicks, Transform dress, Vector3 origin, Vector3 t, Vector3 n,
                                             float d0, float d1, float y0, float y1, ref float u0, ref float u1)
    {
        const float eps = 0.001f;
        int clipped = 0;
        foreach (BoxCollider bc in generated.GetComponentsInChildren<BoxCollider>(true))
        {
            if (bc == null || bc.isTrigger) continue;
            Transform tr = bc.transform;
            if (IsUnder(tr, gimmicks) || IsUnder(tr, dress)) continue;
            Vector3 c = generated.InverseTransformPoint(tr.TransformPoint(bc.center));
            Vector3 half = Abs(Vector3.Scale(bc.size, tr.lossyScale)) * 0.5f;
            float bu = Vector3.Dot(c - origin, t), bd = Vector3.Dot(c - origin, n);
            float hu = Mathf.Abs(Vector3.Dot(half, Abs(t))), hd = Mathf.Abs(Vector3.Dot(half, Abs(n)));
            bool overlapD = Mathf.Min(d1, bd + hd) - Mathf.Max(d0, bd - hd) > eps;
            bool overlapY = Mathf.Min(y1, c.y + half.y) - Mathf.Max(y0, c.y - half.y) > eps;
            bool overlapU = Mathf.Min(u1, bu + hu) - Mathf.Max(u0, bu - hu) > eps;
            if (!overlapD || !overlapY || !overlapU) continue;
            if (bu >= (u0 + u1) * 0.5f) u1 = Mathf.Min(u1, bu - hu);
            else u0 = Mathf.Max(u0, bu + hu);
            clipped++;
        }
        return clipped;
    }

    /// <summary>섹터 로컬 점 p(XZ) 바로 아래·p.y+2.5 이하에서 가장 높은 솔리드(비트리거) BoxCollider 윗면. 팀 기믹·S7_Dress 아래 제외.
    /// Map4Build 상자는 회전·스케일 없음(CreateSolidBox)이라 로컬 AABB로 계산한다. 못 찾으면 fallback.</summary>
    private static float TopUnder(Transform generated, Transform gimmicks, Transform dress, Vector3 p, float fallback)
    {
        float best = float.NegativeInfinity;
        foreach (BoxCollider bc in generated.GetComponentsInChildren<BoxCollider>(true))
        {
            if (bc == null || bc.isTrigger) continue;
            Transform t = bc.transform;
            if (IsUnder(t, gimmicks) || IsUnder(t, dress)) continue;
            Vector3 c = generated.InverseTransformPoint(t.TransformPoint(bc.center));
            Vector3 half = Abs(Vector3.Scale(bc.size, t.lossyScale)) * 0.5f;
            if (p.x < c.x - half.x || p.x > c.x + half.x || p.z < c.z - half.z || p.z > c.z + half.z) continue;
            float top = c.y + half.y;
            if (top > p.y + 2.5f) continue;   // 천장 등 위쪽 상자 제외
            if (top > best) best = top;
        }
        if (float.IsNegativeInfinity(best) || best < p.y + 0.05f)
        {
            // 바닥(윗면 = p.y)만 찾았거나 아무것도 없으면 대체값 — 책상·콘솔이 앵커 아래에 없다는 뜻이라 경고.
            Debug.LogWarning($"{LogTag} {p} 아래 받침 윗면을 못 찾아 대체값 {fallback}을 쓴다(초안 §3-1 확인).");
            return fallback;
        }
        return best;
    }

    // ── 8-b) 역할 표지 ──────────────────────────────────────────────────────────────────

    /// <summary>역할 표지 3개 [판정 12 · 계약 K7-0 :596] — 역할 앵커 위 RoleLabelHeight에 영문 roleId(팀 Ch8 선례 값).
    /// 앵커(refs.roleBookAnchor·roleComputerAnchor·rolePowerAnchor)는 읽기만 하고 자식을 달지 않는다(표지는 S7_Dress 아래).</summary>
    private static int BuildRoleLabels(Transform group, Transform generated, S7_Refs refs)
    {
        if (!ShowRoleLabels) return 0;
        GameObject root = NewNode("VIS_S7_RoleLabels", group, Vector3.zero);
        int n = 0;
        n += RoleLabel(root.transform, generated, refs.roleBookAnchor, "roleBookAnchor", RoleIdBook, RoleLabelColorBook);
        n += RoleLabel(root.transform, generated, refs.roleComputerAnchor, "roleComputerAnchor", RoleIdComputer, RoleLabelColorComputer);
        n += RoleLabel(root.transform, generated, refs.rolePowerAnchor, "rolePowerAnchor", RoleIdPower, RoleLabelColorPower);
        if (n != 3) Debug.LogWarning($"{LogTag} 역할 표지 {n}개 ≠ 계약 3개(K7-0 :596).");
        return n;
    }

    private static int RoleLabel(Transform parent, Transform generated, Transform anchor, string field, string roleId, Color color)
    {
        if (anchor == null)
        {
            Debug.LogWarning($"{LogTag} refs.{field}가 null — 역할 표지 '{roleId}'를 건너뛴다.");
            return 0;
        }
        Vector3 p = generated.InverseTransformPoint(anchor.position);
        Vector3 f = AxisSnapH(generated.InverseTransformDirection(anchor.forward)); // 플레이어가 서는 쪽
        // 앞면이 f 쪽 사람에게 보이게: rot forward = 보는 사람에서 멀어지는 쪽 = −f (MakeLabel 규약).
        return MakeLabel(parent, $"VIS_S7_RoleLabel_{roleId}", p + Vector3.up * RoleLabelHeight, Quaternion.LookRotation(-f, Vector3.up),
                         roleId, color, RoleLabelCharSize, RoleLabelFontSize) ? 1 : 0;
    }

    // ── 9) 출구 표지 ────────────────────────────────────────────────────────────────────

    /// <summary>출구 위 초록 발광 판 + 초록 점광원. 기준은 문 상자(refs.exitDoor, 되돌림) 또는 TEMP 기간 이름 기준점(상수 표 '출구 표지 등' 주석).</summary>
    private static int BuildExitSign(Transform group, Transform generated, S7_Refs refs, Transform gimmicks, Mats m)
    {
        // 개구 기준: 가로 위치(cx, cz) · 개구 윗변(doorTop) · 붙는 벽 두께의 얇은 축(thinX)과 반두께(half).
        float cx, cz, doorTop, half;
        bool thinX;
        if (refs.exitDoor != null)
        {
            if (!GeoBox(generated, refs.exitDoor.transform, out Vector3 c, out Vector3 size))
            {
                Debug.LogWarning($"{LogTag} 출구 문 상자를 읽지 못했다 — 출구 표지를 건너뛴다.");
                return 0;
            }
            thinX = size.x <= size.z;
            half = (thinX ? size.x : size.z) * 0.5f;
            doorTop = c.y + size.y * 0.5f;
            cx = c.x;
            cz = c.z;
        }
        else
        {
            // TEMP 기간 [판정 17-S7 · 계약 K7-0 :588-589] — 문이 없다. 이름으로 찾고 읽기만 한다(옮기지도, 자식을 달지도 않는다).
            Transform temp = FindByName(generated, gimmicks, group, TempExitDoorName);
            Transform lintel = FindByName(generated, gimmicks, group, ExitLintelName);
            if (temp == null || lintel == null)
            {
                Debug.LogWarning($"{LogTag} refs.exitDoor = null(TEMP)인데 {(temp == null ? TempExitDoorName : "")}{(temp == null && lintel == null ? "·" : "")}" +
                                 $"{(lintel == null ? ExitLintelName : "")}을(를) 찾지 못했다 — 출구 표지를 건너뛴다(S7_Builder 이름 확인).");
                return 0;
            }
            if (!GeoBox(generated, lintel, out Vector3 lc, out Vector3 ls))
            {
                Debug.LogWarning($"{LogTag} {ExitLintelName} 상자를 읽지 못했다(Visual·BoxCollider 없음) — 출구 표지를 건너뛴다.");
                return 0;
            }
            Vector3 tp = generated.InverseTransformPoint(temp.position);
            thinX = ls.x <= ls.z;
            half = (thinX ? ls.x : ls.z) * 0.5f;
            doorTop = lc.y - ls.y * 0.5f;           // 인방 아랫면 = 개구 윗변(초안 y4)
            // 벽을 따라가는 축 = TEMP 기준점, 두께 축 = 인방 중심(판이 인방 면에 붙고 묻히지 않게). 초안 값에서는 둘이 같다(z 109.75).
            cx = thinX ? lc.x : tp.x;
            cz = thinX ? tp.z : lc.z;
            float off = thinX ? Mathf.Abs(tp.x - lc.x) : Mathf.Abs(tp.z - lc.z);
            float along = thinX ? Mathf.Abs(tp.z - lc.z) : Mathf.Abs(tp.x - lc.x);
            float alongHalf = (thinX ? ls.z : ls.x) * 0.5f;
            if (off > half + 0.05f || along > alongHalf + 0.05f)
                Debug.LogWarning($"{LogTag} {TempExitDoorName} {tp}가 {ExitLintelName} 아래(두께·폭 범위)에 있지 않다 — 표지가 개구와 어긋날 수 있다(확인 필요).");
            Debug.Log($"{LogTag} [TEMP 판정 17-S7] refs.exitDoor = null — 출구 표지 기준점 = {TempExitDoorName} {tp} · 개구 윗변 = {ExitLintelName} 아랫면 y{doorTop:0.##}.");
        }

        // 방 쪽 = 서버실 중심 방향(얇은 축 위).
        Vector3 roomDir;
        if (refs.serverRoomBounds.size.sqrMagnitude > 0.01f)
        {
            float d = thinX ? refs.serverRoomBounds.center.x - cx : refs.serverRoomBounds.center.z - cz;
            roomDir = thinX ? new Vector3(Mathf.Sign(d), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d));
        }
        else roomDir = new Vector3(0f, 0f, -1f);
        Vector3 c0 = new Vector3(cx, 0f, cz);

        GameObject root = NewNode("VIS_S7_ExitSign", group, Vector3.zero);
        Vector3 signPos = new Vector3(c0.x, doorTop + ExitSignAboveDoor, c0.z) + roomDir * (half + ExitSignSize.z * 0.5f + 0.01f);
        Prim(PrimitiveType.Cube, "VIS_S7_ExitSign_Plate", root.transform, signPos, Quaternion.LookRotation(roomDir, Vector3.up),
             ExitSignSize, m.exitSign);
        GameObject lg = NewNode("VIS_S7_ExitSign_Light", root.transform,
                                new Vector3(c0.x, doorTop + ExitLampAboveDoor, c0.z) + roomDir * (half + ExitLampOut));
        AddLight(lg, LightType.Point, ExitColor, ExitLampIntensity, ExitLampRange, 0f, LightRenderMode.Auto);
        return 1;
    }

    // ── 공용: 상자 읽기 · 매달린 등 · 조명 · 도형 ─────────────────────────────────────

    /// <summary>GEO_ 상자(Map4Build.CreateSolidBox — GEO 위치 = 상자 중심, 자식 Visual 스케일 = 크기, 회전 없음)의
    /// generated 로컬 중심·크기. Visual이 없으면 BoxCollider로. 읽기만 한다.</summary>
    private static bool GeoBox(Transform generated, Transform geo, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;
        if (geo == null) return false;
        Transform vis = geo.Find(VisualChildName);
        if (vis != null)
        {
            center = generated.InverseTransformPoint(vis.position);
            size = Abs(Vector3.Scale(vis.localScale, geo.lossyScale));
            return size.x > 0f && size.y > 0f && size.z > 0f;
        }
        BoxCollider bc = geo.GetComponent<BoxCollider>();
        if (bc == null) return false;
        center = generated.InverseTransformPoint(geo.TransformPoint(bc.center));
        size = Abs(Vector3.Scale(bc.size, geo.lossyScale));
        return size.x > 0f && size.y > 0f && size.z > 0f;
    }

    /// <summary>generated 아래에서 이름이 정확히 같은 Transform(팀 기믹 S7_Gimmicks·S7_Dress 아래 제외). 없으면 null, 둘 이상이면 경고 후 첫 것.
    /// 읽기만 한다(옮기거나 자식을 달지 않는다).</summary>
    private static Transform FindByName(Transform generated, Transform gimmicks, Transform dress, string exactName)
    {
        Transform found = null;
        int count = 0;
        foreach (Transform t in generated.GetComponentsInChildren<Transform>(true))
        {
            if (t == generated || IsUnder(t, gimmicks) || IsUnder(t, dress)) continue;
            if (!string.Equals(t.name, exactName, System.StringComparison.Ordinal)) continue;
            if (found == null) found = t;
            count++;
        }
        if (count > 1) Debug.LogWarning($"{LogTag} '{exactName}'가 {count}개다 — 첫 번째({(found.parent != null ? found.parent.name : "")}/{found.name})를 쓴다(S7_Builder 확인).");
        return found;
    }

    /// <summary>수평 방향을 가장 가까운 X/Z 축으로(서버·벽 면이 축 정렬이라). 0이면 −Z.</summary>
    private static Vector3 AxisSnapH(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 1e-6f) return new Vector3(0f, 0f, -1f);
        return Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>천장에서 줄로 매단 등(S8_Dress 선례). 루트(회전 없음) 아래 갓(원기둥)·줄(원기둥)·빛(자식, Spot이면 아래 향함).</summary>
    private static void Pendant(Transform parent, string name, Vector3 shadeCenter, float shadeDia, float ceilingY,
                                Material shadeMat, Material cordMat, LightType type, Color color, float intensity,
                                float range, float spotAngle, LightRenderMode mode)
    {
        GameObject root = NewNode(name, parent, shadeCenter);
        Prim(PrimitiveType.Cylinder, name + "_Shade", root.transform, Vector3.zero, Quaternion.identity,
             new Vector3(shadeDia, ShadeHalfHeight, shadeDia), shadeMat);

        float cordBottom = shadeCenter.y + ShadeHalfHeight;
        float cordLen = ceilingY - cordBottom;
        if (cordLen > 0.05f)
        {
            // 원기둥 기본 높이 2 → 스케일 y = 길이/2, 중심 = 갓 윗면과 천장 사이 가운데.
            Prim(PrimitiveType.Cylinder, name + "_Cord", root.transform,
                 new Vector3(0f, ShadeHalfHeight + cordLen * 0.5f, 0f), Quaternion.identity,
                 new Vector3(CordDiameter, cordLen * 0.5f, CordDiameter), cordMat);
        }

        GameObject lightGo = NewNode(name + "_Light", root.transform, new Vector3(0f, -ShadeHalfHeight - 0.02f, 0f));
        if (type == LightType.Spot)
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Spot 기본 +Z → X축 +90° = −Y(아래)
        AddLight(lightGo, type, color, intensity, range, spotAngle, mode);
    }

    private static Light AddLight(GameObject go, LightType type, Color color, float intensity, float range,
                                  float spotAngle, LightRenderMode mode)
    {
        Light light = go.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        // innerSpotAngle은 넣지 않는다 — (추측) Built-in에서는 효과 없음, SRP 전용 속성(S8_Dress 수정 1과 같음).
        if (type == LightType.Spot) light.spotAngle = spotAngle;
        // Built-in Forward 픽셀 조명 수 한계(QualitySettings Ultra 4 / Very High 3 / High 2 / Medium 1 / Low 0, 현재 Ultra) —
        // ProjectSettings는 바꾸지 않고 오브젝트 설정으로만 대응:
        //  - 통로·포트·컴퓨터실·전실·출구 등 = Auto: 렌더러마다 가장 중요한(가깝고 센) 조명부터 픽셀, 나머지는 버텍스/SH로 자동 강등.
        //    서버실 바닥은 칸 판(10U)으로 나눠 칸마다 가까운 등이 픽셀 조명이 되게 했다.
        //  - 콘솔·책상 스탠드 등 2개만 ForcePixel(품질이 낮아도 역할 자리는 밝게 — 2개라 비용 작음).
        light.renderMode = mode;
        light.shadows = LightShadows.None;                 // [추정] 연출 전용 — 그림자 비용을 피한다. 대가: 벽·유리가 빛을 막지 않아 범위 안이면 벽 너머도 밝힌다(CrLampRange 주석).
        light.lightmapBakeType = LightmapBakeType.Realtime; // 굽지 않음(씬 GI 설정 불변).
        lightCount++;
        if (mode == LightRenderMode.ForcePixel) pixelForcedCount++;
        return light;
    }

    private static GameObject NewNode(string name, Transform parent, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>시각 전용 도형 — 기본 콜라이더를 즉시 제거하고 그림자를 끈다. 좌표는 parent 로컬(S7_Dress 아래는 모두 generated와 같은 축).</summary>
    private static MeshRenderer Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos,
                                     Quaternion localRot, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col); // 콜라이더 0개(계약 K0-3 — 시각 전용).
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return mr;
    }

    private static int StripColliders(Transform group)
    {
        Collider[] cols = group.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in cols) Object.DestroyImmediate(c);
        return cols.Length;
    }

    // ── 머티리얼(Standard, Materials/Generated/M4_S7_*.mat) ─────────────────────────────

    private static Mats LoadMaterials()
    {
        EnsureDir(MaterialDir);
        return new Mats
        {
            serverBody = GetOrCreateMaterial("ServerBody", ServerBodyColor, 0.45f, 0.55f, Color.black),
            serverFloor = GetOrCreateMaterial("ServerFloor", ServerFloorColor, 0.30f, 0f, Color.black),
            wall = GetOrCreateMaterial("Wall", WallColor, 0.10f, 0f, Color.black),
            ceiling = GetOrCreateMaterial("Ceiling", CeilingColor, 0.05f, 0f, Color.black),
            crFloor = GetOrCreateMaterial("CRFloor", CrFloorColor, 0.25f, 0f, Color.black),
            crWall = GetOrCreateMaterial("CRWall", CrWallColor, 0.15f, 0f, Color.black),
            desk = GetOrCreateMaterial("Desk", DeskColor, 0.35f, 0f, Color.black),
            console = GetOrCreateMaterial("Console", ConsoleColor, 0.50f, 0.60f, Color.black),
            glass = GetOrCreateTransparentMaterial("Glass", GlassColor, GlassSmoothness),
            glassFrame = GetOrCreateMaterial("GlassFrame", GlassFrameColor, 0.50f, 0.60f, Color.black),
            glassBand = GetOrCreateMaterial("GlassBand", GlassBandColor, 0.30f, 0f, Color.black),
            portOut = GetOrCreateMaterial("PortOut", PortOutColor, 0.40f, 0f, PortOutColor * PortEmission),
            portIn = GetOrCreateMaterial("PortIn", PortInColor, 0.40f, 0f, PortInColor * PortEmission),
            socketHole = GetOrCreateMaterial("SocketHole", SocketHoleColor, 0.20f, 0f, Color.black),
            ledGreen = GetOrCreateMaterial("LedGreen", LedGreenColor, 0.50f, 0f, LedGreenColor * LedEmission),
            ledBlue = GetOrCreateMaterial("LedBlue", LedBlueColor, 0.50f, 0f, LedBlueColor * LedEmission),
            aisleShade = GetOrCreateMaterial("AisleShade", AisleLampColor, 0.30f, 0f, AisleLampColor * AisleLampEmission),
            warmShade = GetOrCreateMaterial("LampShade", CrLampColor, 0.30f, 0f, CrLampColor * LampShadeEmission),
            cord = GetOrCreateMaterial("LampCord", CordColor, 0.20f, 0f, Color.black),
            exitSign = GetOrCreateMaterial("ExitSign", ExitColor, 0.40f, 0f, ExitColor * ExitSignEmission),
            propDark = GetOrCreateMaterial("PropDark", PropDarkColor, 0.40f, 0.30f, Color.black),
            screen = GetOrCreateMaterial("Screen", ScreenColor, 0.60f, 0f, ScreenColor * ScreenEmission),
            book = GetOrCreateMaterial("GuideBook", BookColor, 0.25f, 0f, Color.black),
            entryPlate = GetOrCreateMaterial("EntryPlate", EntryPlateColor, 0.45f, 0.50f, Color.black),
        };
    }

    /// <summary>M4_S7_&lt;name&gt;.mat — 있으면 재사용, 없으면 Standard로 생성(불투명). 매번 값을 다시 써서 상수 표와 일치시킨다.
    /// emission이 검정이면 _EMISSION을 끈다.</summary>
    private static Material GetOrCreateMaterial(string name, Color albedo, float smoothness, float metallic, Color emission)
    {
        Material mat = LoadOrCreate(name);
        if (mat == null) return null;
        SetOpaque(mat);
        mat.color = albedo;
        mat.SetFloat("_Glossiness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        bool emissive = emission.maxColorComponent > 0.0001f;
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>유리 — Standard Rendering Mode "Transparent"(_Mode 3). 인스펙터(StandardShaderGUI)가 하는 블렌드·키워드·렌더큐 설정을
    /// 코드로 똑같이 한다: SrcBlend One · DstBlend OneMinusSrcAlpha · ZWrite 0 · _ALPHAPREMULTIPLY_ON · RenderType Transparent · 큐 3000.</summary>
    private static Material GetOrCreateTransparentMaterial(string name, Color albedoWithAlpha, float smoothness)
    {
        Material mat = LoadOrCreate(name);
        if (mat == null) return null;
        mat.SetFloat("_Mode", 3f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)BlendMode.One);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.color = albedoWithAlpha;
        mat.SetFloat("_Glossiness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        mat.DisableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        mat.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>불투명(_Mode 0) 설정 — 같은 이름 에셋이 예전에 다른 모드로 저장돼 있어도 상수 표대로 되돌린다.</summary>
    private static void SetOpaque(Material mat)
    {
        mat.SetFloat("_Mode", 0f);
        mat.SetOverrideTag("RenderType", "");
        mat.SetInt("_SrcBlend", (int)BlendMode.One);
        mat.SetInt("_DstBlend", (int)BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1; // 셰이더 기본(Geometry)
    }

    private static Material LoadOrCreate(string name)
    {
        string path = $"{MaterialDir}/{MaterialPrefix}{name}.mat";
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogError($"{LogTag} Standard 셰이더를 찾지 못했다(Built-in 전용 프로젝트여야 한다) — {path} 건너뜀.");
            return null;
        }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(standard);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != standard)
        {
            mat.shader = standard; // 우리 에셋(M4_S7_)만 — 팀·팔레트 머티리얼은 여기 오지 않는다.
        }
        return mat;
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
