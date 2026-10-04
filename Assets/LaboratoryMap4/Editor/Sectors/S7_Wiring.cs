#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 섹터7 "서버실(CH7)" 팀 기믹 배치·배선 — 계약 K7-0·K7-1·K7-2(진행/지시서/R3/_계약.md), 지시서 R2S68/S7-W3 → R3/S7-W4(C9·C11·C12).
/// S7_Builder.Build가 지형·문(TEMP 기간엔 문 없음)·앵커를 만든 뒤 Wire → (S7_Dress.Apply) 순서로 부른다 [계약 K0-2].
///
/// [만드는 것 — 전부 generated/S7_Gimmicks 그룹 1개 아래, 씬 루트 잔여 0 [계약 K0-3]]
/// 팀 WiringPanel·RoleClueTerminal·ServerRoomPower에는 생성 메뉴가 없다(진행/조사/S7_팀API.md §0-3, 메뉴는 자가검증·시험 데이터뿐).
/// 그래서 팀 선례(Ch8TestRoomMenuItem.Slot :244-269, ServerRoomPowerSelfTest.NewRig :54-97)와 같이 AddComponent + public 필드로 조립한다.
/// 예외 1개: 수동 R 체크포인트는 팀 생성 함수 RespawnMenuItem.CreateCheckpoint()(RespawnMenuItem.cs:27-28)로 만들고 그룹 아래로 옮긴다 [C12].
///   S7_Gimmicks
///   ├ S7_Activator            (우리 기존 Lab_ActivateWhenPlayersReady + 트리거 — 전실 도착 시 아래 루트를 켠다 [판정 13])
///   ├ S7_Zone_Check           (팀 RespawnZone + 팀 막대 Pole/PoleMesh·Flag — S7 수동 R 체크포인트 1개 [C12 · 계약 K7-0]. 섹터 로컬 (0,0,4),
///   │                          구역 x −4~4 · y 0~3 · z 1~7. S7_GimmickRoot 밖 — 켜진 채 저장한다 [해석 — 명령 결정, S7-W4 §5])
///   └ S7_GimmickRoot          (DeferActivationUntilArrival이면 꺼진 채 저장 [판정 13] — 나머지 팀 기믹은 전부 이 아래)
///      ├ S7_RoleAssignmentManager (RoleAssignmentManager — S7 전용 1개)
///      ├ S7_Slot_Book            (트리거 + RoleSlot "Book" + BookPanel — 표지 없음, 역할 표지는 L VIS_ [판정 12])
///      ├ S7_Slot_Computer        (트리거 + RoleSlot "Computer" + QuizTerminal — 표지 없음 [판정 12])
///      ├ S7_Slot_Power           (트리거 + RoleSlot "Power"(allowMultipleUsers) — 표지 없음 [판정 12])
///      ├ S7_W_ENTRY              (WiringPanel + WiringLineRenderer + WiringRuleDisplay, entryPanelAnchor 포즈 — 동벽 x16 [판정 9])
///      │  └ S7_W_ENTRY_Port_OUT_A..IN_3 (트리거 + WiringPort) — 앵커 기준 벽을 따라 한 줄 (포트 글자는 L 소유 [판정 12])
///      ├ S7_W_POWER              (WiringPanel + WiringLineRenderer + WiringRuleDisplay)
///      │  └ S7_W_POWER_Port_OUT_A..IN_3 (트리거 + WiringPort) — refs.portAnchors[i] 포즈 (포트 글자는 L 소유 [판정 12])
///      └ S7_PowerMaintenance     (PowerMaintenanceController + PowerGauge)
/// W가 만드는 TextMesh는 0개다(CreatePortLabels·CreateRoleLabels = false, 코드는 남김) [판정 12 · 계약 K7-0].
///
/// [기믹 동작은 바꾸지 않는다 — HANDOFF §5-0, 공통 규칙 5]
/// - 전력 수치(M·E0·K·U·d·G·H)는 팀 기본값 그대로 — 대입 코드 자체가 없다(HANDOFF §2-2, PowerMaintenanceController.cs:73-86).
///   감소 속도 d = 팀 값 1 그대로 [판정 16]. 시험 옵션 autoMaintain(private 직렬화)도 건드리지 않는다.
/// - 팀 private 필드는 읽지도 쓰지도 않는다. 인스펙터 값은 public 필드로만 넣는다 [판정 11 · 계약 K0-3].
/// - 영구 UnityEvent 배선은 문이 있을 때 정확히 2개(같은 씬):
///   ① W_ENTRY 패널 OnCircuitCompleted → refs.entryDoor.SetPadPressed(true)   [확정 설계 §3-2]
///   ② QuizTerminal.onAllCleared → refs.exitDoor.SetPadPressed(true)           [확정 §3-7 문구] → [판정 10] onAllCleared
///   전력 패널 OnCircuitCompleted·quiz.onAllCleared·quiz.onActionRequested는 PowerMaintenanceController가 실행 중 스스로
///   AddListener한다(PowerMaintenanceController.cs:141-162) — 영구 배선을 더하지 않는다(이중 호출 방지, S7_팀API.md §3-1).
/// - 문을 여는 팀 public 창구는 doorPhysics.SetPadPressed(bool) 하나(doorPhysics.cs:61). 우리 선례 S1_Builder.cs:234.
///
/// [임시 개방(TEMP) — 판정 17-S7 · 계약 K7-0]
/// - 팀 기능이 들어올 때까지 B가 입구·출구 문을 만들지 않고(TEMP_S7_EntryDoor_Open·TEMP_S7_ExitDoor_Open 빈 GO) refs.entryDoor·exitDoor = null로 넘긴다.
/// - W는 문이 null이면 ①② 배선을 건너뛰고 Debug.Log("[TEMP 판정 17-S7] …")만 남긴다(에러·경고 아님). 배선 코드·신호원 분기는 지우지 않고
///   null 가드로만 막는다 — B가 TempOpenDoors = false로 문을 되돌리면 이 파일을 고치지 않고 영구 배선 2/2가 된다.
/// - 부트스트랩(W_ENTRY 회로 배정을 우리가 하는 코드)은 만들지 않는다 — 기믹 신설이라 HANDOFF §5-0 저촉 [판정 17-S7].
///
/// [팀 코드로 채울 수 없는 것 — 어댑터로 메우지 않고 보고(진행/보고/S7-W3.md mismatch)]
/// - W_ENTRY에 회로를 배정하는 팀 코드가 없다(WiringPanel.AssignCircuit :59를 부르는 곳은 PowerMaintenanceController.cs:418뿐, 인자 2개,
///   currentCircuit는 private·비직렬화 :42). 이 파일은 패널·포트·문 배선까지만 만들고, 배정은 하지 않는다 → 입구 패널은 반응하지 않는다.
///   TEMP 기간에는 PowerMaintenanceController.entranceWiring 연결을 비운다(LinkEntranceWiring = false, TEMP_ 표지) [C9 · 계약 K7-0].
///   팀 코드는 entranceWiring이 null이면 입구 배선 검사를 건너뛴다(PowerMaintenanceController.cs:63 툴팁 "비우면 검사하지 않는다"·:241) →
///   '실험 시작'이 W_ENTRY 잠금을 요구하지 않는다. 인스펙터 값 선택이라 어댑터가 아니다. 팀 회로 배정이 들어오면 true로 되돌린다.
/// - 문답 문항(QuizTerminal.questions)·책 본문(BookPanel.pages)은 저장소에 없다(S7_팀API.md §0-2) → 빈 배열 그대로. 출구는 TEMP 개방 유지 [C9].
///
/// [이름 규약 — 계약 이름, 바꾸지 않는다 [C11 · 계약 K0-3 '이름 규약 앵커' · K7-0]]
/// 다른 파일이 이 파일이 만든 이름·구조로 찾으므로 아래 이름은 계약 이름이다. 이름을 바꾸면 조회하는 쪽이 조용히 비게 된다.
/// - `S7_Gimmicks`(GroupName) — S7_Dress가 이 이름으로 W 그룹을 찾아 자기 검사·칠하기 대상에서 뺀다(S7_Dress.cs:268).
/// - `S7_Gimmicks/S7_GimmickRoot`(RootName) — 지연 활성화 대상 루트 [판정 13]. 꺼진 채 저장되므로 아래를 찾는 쪽은 비활성 자식까지 찾아야 한다.
/// - `S7_W_ENTRY`(EntryPanelName) · `S7_W_ENTRY_Port_<OUT_A|OUT_B|OUT_C|IN_1|IN_2|IN_3>`(EntryPanelName + "_Port_" + PortNames[i]) —
///   W_ENTRY 포트 스캔 대상. 현재 조회 주체는 S7_Dress(S7_Dress.cs:25 — W가 놓은 팀 WiringPort 위치를 읽기만)다.
/// - `TEMP_S7_ExitDoor_Open`·`TEMP_S7_EntryDoor_Open` — B(S7_Builder)가 만드는 이름이다. S7_Dress가 출구 표지를 이름으로 조회한다
///   (S7_Dress.cs:41·:270). 이 파일은 그 이름을 로그 문구로만 쓰고 조회하지 않는다(WireDoor 참고).
/// - `S7_Zone_Check`(CheckpointName) — 수동 R 체크포인트 [C12]. PTF S7 행 경로 `S7_Gimmicks/S7_Zone_Check`.
/// Refs(S7_Refs) 필드는 추가하지 않는다(K7-1 글자 그대로) — 위 이름이 그 대신이다 [C11 · 계약 K7-0].
///
/// 좌표·크기는 아래 상수 표 한 곳에 모았다. 앵커 위치·방향은 B(S7_Builder)의 refs가 정본이고
/// W는 앵커를 옮기거나 지우지 않는다(자기 인스턴스를 앵커 월드 포즈에 맞춘다) [계약 K0-3].
/// 출처: [확정]=설계서 확정, [판정 n]=진행/판정/2026-09-28_S6S8_판정.md, [사용자 09-29 나·안C]=같은 문서 끝 사용자 결정,
/// [계약 Kx]=진행/지시서/R3/_계약.md(R2S68은 이력), [C n]=진행/판정/2026-09-29_S6S8_2차_판정.md, [팀]=팀 코드(파일:행), [제안]=초안(진행/초안/S7_배치초안.md) 또는 이 파일이 정한 값,
/// [해석]=계약·판정 문구의 해석, [계산]=계산값, [추정]=실측 필요.
/// 검증: Unity 동봉 csc 형식 검사(팀 타입 스텁)만 했다 — Unity 컴파일·실행은 통합 후 대기열.
/// </summary>
public static class S7_Wiring
{
    // ═════════════════════════ 상수 표 (확인 뒤 여기만 고친다) ═════════════════════════

    // ── 그룹·이름 ──
    // [C11] GroupName·RootName·EntryPanelName·PortNames·CheckpointName은 계약 이름이다 — 다른 파일이 이 이름으로 찾으므로 바꾸지 않는다(클래스 주석 '이름 규약').
    private const string GroupName = "S7_Gimmicks";                          // [계약 K0-3] generated 바로 아래 1개 — 계약 이름 [C11]
    private const string RootName = "S7_GimmickRoot";                        // [판정 13 · 계약 K7-0] 지연 활성화 대상 루트 — 계약 이름 [C11]
    private const string ActivatorName = "S7_Activator";                     // [제안] 이름만(활성기 자체는 [판정 13])
    private const string RoleManagerName = "S7_RoleAssignmentManager";       // [제안] (팀 CH8 이름 규칙 CH8_RoleAssignmentManager를 따름)
    private const string SlotNamePrefix = "S7_Slot_";                        // [제안] (팀 CH8_Slot_ :246 규칙)
    private const string EntryPanelName = "S7_W_ENTRY";                      // [팀 PRD WiringPanel.md 인스턴스명 W_ENTRY] — 계약 이름 [C11](포트 = EntryPanelName + "_Port_" + PortNames[i])
    private const string PowerPanelName = "S7_W_POWER";                      // [팀 PRD 인스턴스명 W_POWER]
    private const string PowerName = "S7_PowerMaintenance";                  // [제안]
    private const string LabelName = "Label";                                // [팀 Ch8TestRoomMenuItem.cs:258]
    private const string CheckpointName = "S7_Zone_Check";                   // [명령 결정 — S8 S8_Zone_Check 선례] 팀 이름 "Checkpoint"(RespawnMenuItem.cs:32)를 바꿔 붙인다 — 계약 이름 [C11·C12]
    private const string CheckpointMenuPath = "Tools/Respawn/Create Checkpoint Pole"; // [팀 RespawnMenuItem.cs:27] 로그 표기용(실행은 public static 직접 호출)

    // ── 역할 사물(RoleSlot) ──
    private const string RoleIdBook = "Book";                                // [팀 RoleSlot.cs:33-35 툴팁, ServerRoomPowerSelfTest.cs:65]
    private const string RoleIdComputer = "Computer";                        // [팀 ServerRoomPowerSelfTest.cs:66]
    private const string RoleIdPower = "Power";                              // [팀 ServerRoomPowerSelfTest.cs:67]
    /// <summary>[팀 Ch8TestRoomMenuItem.cs:249-250] 트리거 3.5×2×3.5, 높이 범위 = 섹터 바닥 y 0~2.
    /// 앵커가 바닥(y0)이면 center (0,1,0) = 팀 값 그대로. 앵커가 다른 높이면 중심 y만 1 − 앵커 높이로 맞춘다 [계산].</summary>
    private static readonly Vector3 RoleTriggerSize = new Vector3(3.5f, 2f, 3.5f);
    private const float RoleTriggerY0 = 0f, RoleTriggerY1 = 2f;             // [팀 :249-250] 섹터 로컬 바닥 기준
    /// <summary>역할 슬롯 표지 3개(TextMesh). false — 역할 표지는 L 소유 VIS_(S7_Dress, 콜라이더 0) [판정 12 · 계약 K7-0].
    /// 켜면 L 표지와 두 벌이 겹친다(z-fighting). 코드(BuildLabel)는 남긴다.</summary>
    private static readonly bool CreateRoleLabels = false;                   // [판정 12]
    /// <summary>포트 ID 글자. false — 포트 12곳 글자는 L 소유(S7_Dress ShowPortLabels = true) [판정 12 · 계약 K7-0].
    /// 둘 다 켜면 같은 면 0.03 간격에 글자 두 벌이 겹친다(z-fighting, 검문 S6~S8_코드 :31). 코드는 남긴다.</summary>
    private static readonly bool CreatePortLabels = false;                   // [판정 12]
    private const float RoleLabelHeight = 2.2f;                              // [팀 :258]
    private const float RoleLabelCharacterSize = 0.25f;                      // [팀 :262]
    private const int LabelFontSize = 48;                                    // [팀 :263]
    private static readonly Color LabelColorBook = new Color(0.3f, 0.6f, 1f);      // [팀 CH8 Book :57]
    private static readonly Color LabelColorComputer = new Color(0.9f, 0.9f, 0.9f); // [제안]
    private static readonly Color LabelColorPower = new Color(1f, 0.85f, 0.2f);     // [제안]

    // ── 포트(WiringPort) 공통 ──
    /// <summary>계약 K7-1 portAnchors 순서 [0]OUT_A [1]OUT_B [2]OUT_C [3]IN_1 [4]IN_2 [5]IN_3.
    /// portId는 팀 회로 에셋 outputIds A/B/C · inputIds 1/2/3과 정확히 같아야 한다(WiringPort.cs:21-23, Data/ENTRY·POWER_1~3.asset).</summary>
    private static readonly string[] PortIds = { "A", "B", "C", "1", "2", "3" };
    private static readonly string[] PortNames = { "OUT_A", "OUT_B", "OUT_C", "IN_1", "IN_2", "IN_3" }; // 포트 이름 접미사 — 계약 이름 [C11]
    private static readonly string[] PortLabels = { "OUT A", "OUT B", "OUT C", "IN 1", "IN 2", "IN 3" }; // [제안] ASCII(내장 글꼴 한글 표시 모르겠다)
    private const int OutputCount = 3;                                       // [확정 설계 §3-1] 출력 3 + 입력 3
    private static readonly Color PortColorOutput = new Color(1f, 0.5f, 0.2f);   // [팀 WiringPort.cs:101 Gizmo 색]
    private static readonly Color PortColorInput = new Color(0.2f, 0.6f, 1f);    // [팀 WiringPort.cs:101]
    private const float PortLabelRise = 0.8f;                                // [제안] 포트 피벗 위 0.8
    private const float PortLabelOut = 0.05f;                                // [제안] 면 앞 0.05(면에 묻히지 않게)
    private const float PortLabelCharacterSize = 0.1f;                       // [추정] 글자 폭이 포트 간격 2.2보다 작게 — 실측 필요
    /// <summary>포트 트리거 높이 범위(섹터 로컬). 초안 center y −0.2·size y 2 @ 앵커 y 1.2 → y 0~2 [제안 초안 §4]. 앵커 높이가 바뀌어도 이 범위를 유지한다 [계산].</summary>
    private const float PortTriggerY0 = 0f, PortTriggerY1 = 2f;

    // ── 서버 포트(전력 회로) 트리거 ── [제안 초안 §4 "로컬 center (0, −0.2, 1.0) size (2.5, 2, 2)"] 앞면에서 0~2 앞
    private const float ServerPortTriggerWidth = 2.5f;                       // 앵커 로컬 x
    private const float ServerPortTriggerDepth = 2f;                         // 앵커 로컬 z(forward 쪽으로 0~2)

    // ── W_ENTRY 포트 줄 ── [판정 9] 동벽 x16 본안 확정 · [제안 초안 §4] 앵커 (16,1.2,8) fwd −X 중심, 앵커 오른쪽(rotation*right = +Z) 방향 한 줄,
    // 간격 2.2, 순서 OUT_A..IN_3 → 포트 z 2.5·4.7·6.9·9.1·11.3·13.5 [계산]. 대안 (가)는 [판정 9]로 참고 격하(값 없음).
    private const float EntryPortSpacing = 2.2f;                             // [제안 초안 §4] 이웃 트리거 틈 1.6 > 정사면체 폭 1.414
    private const float EntryPortTriggerWidth = 0.6f;                        // [제안 초안 §4] 앵커 로컬 x
    private const float EntryPortTriggerDepth = 1.5f;                        // [제안 초안 §4] 앵커 로컬 z(forward 쪽으로 0~1.5 → 월드 x 14.5~16)

    // ── 선(WiringLineRenderer) ── lineWidth·lineMaterial 대입 = 팀 public 필드 인스펙터 값 [판정 11 · 계약 K0-3]
    private const float PowerLineWidth = 0.15f;                              // [판정 11 인스펙터 값 · 제안 초안 §4] 팀 기본 0.05(WiringLineRenderer.cs:17)는 큰 어두운 방에서 가늘다
    private const float EntryLineWidth = 0.05f;                              // [팀 WiringLineRenderer.cs:17] 기본값 그대로
    // 선 머티리얼은 W가 만들지 않는다 — L 도우미 S7_Dress.WiringLineMaterial()의 반환값만 넣는다 [판정 11 · 계약 K7-0·K7-2].
    // 팀 코드는 lineMaterial이 null이면 선에 머티리얼을 넣지 않는다(WiringLineRenderer.cs:69) → null이면 경고 1줄.

    // ── 규칙표(WiringRuleDisplay) ──
    /// <summary>[팀 WiringRuleDisplay.cs:19] 기본 (12,40,420,100). 전력 패널은 기본 그대로, 입구 패널은 y 150으로 내려 겹침 방지
    /// [판정 11 인스펙터 값 · 제안 초안 §4].</summary>
    private static readonly Rect EntryRuleArea = new Rect(12f, 150f, 420f, 100f);

    // ── 회로 데이터(팀 에셋 — 참조만, 수정 금지) ── [팀 ServerRoomPowerTestData.cs:34-40, S7_팀API.md §1-6 · 계약 K7-1]
    // 주의: 이 에셋(ENTRY·POWER_1~3)은 팀 메뉴 'Tools/Server Room Power/Create Test Circuit Data'(ServerRoomPowerTestData.cs:44)의 시험 데이터이고,
    // 정답 쌍은 팀 머리 주석상 '최종 콘텐츠·난도가 아니라 플레이테스트용'(ServerRoomPowerTestData.cs:4-7) — 최종 콘텐츠 미확정.
    private static readonly string[] PowerCircuitPaths =
    {
        "Assets/ServerRoomPowerSystem/Data/POWER_1.asset",
        "Assets/ServerRoomPowerSystem/Data/POWER_2.asset",
        "Assets/ServerRoomPowerSystem/Data/POWER_3.asset",
    };
    private const string EntryCircuitPath = "Assets/ServerRoomPowerSystem/Data/ENTRY.asset"; // 포트 ID 대조용으로만 읽는다(배정 불가 — 위 클래스 주석)

    // ── 출구 신호원 ──
    private enum ExitSignal { QuizAllCleared, PowerCompleted }
    /// <summary>[확정 §3-7 문구] "문답 전체 성공 신호로 열리는 문" → [판정 10] onAllCleared = QuizTerminal.onAllCleared(전체 성공 정확히 1회,
    /// QuizTerminal.cs:82-83) → exitDoor.SetPadPressed(true) [계약 K7-0·K7-1].
    /// PowerCompleted(PowerMaintenanceController.onCompleted :96-97) 분기는 쓰지 않음 [판정 10] — 코드만 남긴다.
    /// 어느 신호원이든 문답을 먼저 풀면 전력 퍼즐을 건너뛸 수 있다(QuizTerminal.cs:97 제출 게이트 기본 열림,
    /// PowerMaintenanceController.cs:28-29·271-277) — 팀 기믹 불일치로 보고만 한다 [판정 10].
    /// TEMP 기간 exitDoor = null → 배선을 건너뛰고 [TEMP 판정 17-S7] 로그만 남긴다.</summary>
    private static readonly ExitSignal ExitSource = ExitSignal.QuizAllCleared;

    /// <summary>W_ENTRY 완료를 '실험 시작' 요건에 거는가(PowerMaintenanceController.entranceWiring :63-64, 팀 PRD 입구 먼저) [계약 K7-1 · 초안 §4].
    /// TEMP_ [C9 · 계약 K7-0] TEMP 기간 false — entranceWiring 인스펙터 연결을 비운다(BuildPower의 `LinkEntranceWiring ? entryPanel : null` 식은 그대로).
    /// 이유: W_ENTRY 회로 배정 팀 코드가 없어 패널이 잠기지 않으므로, 연결하면 '실험 시작'이 영영 거부된다(PowerMaintenanceController.cs:241).
    /// 비우면 팀 코드가 입구 배선 검사를 건너뛴다(:63 툴팁 "비우면 검사하지 않는다") → 전력 퍼즐을 실제로 플레이할 수 있다 [판정 17-S7 "실제로 플레이 가능"].
    /// 인스펙터 값 선택이라 어댑터·부트스트랩이 아니다 [C9]. W_ENTRY 패널·포트·선·규칙표는 그대로 만든다(패널은 반응하지 않음).
    /// 되돌림: 팀 회로 배정(WiringPanel.AssignCircuit 호출 코드)이 W_ENTRY에 들어오면 true로 되돌린다.</summary>
    private static readonly bool LinkEntranceWiring = false;                 // TEMP_ [C9] 팀 회로 배정(WiringPanel.AssignCircuit 호출 코드)이 들어오면 true로 되돌린다

    // ── 지연 활성화(씬 연결 — 기믹 동작 변경 아님) ── [판정 13 · 계약 K7-0]
    /// <summary>모든 섹터가 시작 때 함께 로드되므로(Map4Director.cs:121) S7 기믹의 OnGUI(전력 게이지·규칙표·역할 매니저 메시지)가
    /// S1부터 화면에 뜬다(S7_팀API.md §0-6·§6-4). S7_GimmickRoot를 꺼 둔 채 저장하고, 우리 기존 Lab_ActivateWhenPlayersReady
    /// (S1_Builder.cs:236-245 선례)가 배치 끝난 도형이 전실에 처음 들어올 때 켠다. 켜진 뒤 S8에서도 HUD가 남는 것은 보고만 한다.</summary>
    private static readonly bool DeferActivationUntilArrival = true;         // [판정 13]
    /// <summary>[제안 초안 §4·§14 #6] 판정 구역(섹터 로컬) = 전실 안쪽 x −16~16 · z 0.5~14에 입구 선 z 0 포함, y −1~6(점프 포함).
    /// 입구 마커 (0,0.1,0)·체크포인트 (0,0.1,3)·스폰 (±2/0, 0.6, 3)을 품는다 [계약 K0-2 마커 위치].</summary>
    private static readonly Vector3 ActivationZoneMin = new Vector3(-16f, -1f, 0f);
    private static readonly Vector3 ActivationZoneMax = new Vector3(16f, 6f, 14f);

    // ── 수동 R 체크포인트(팀 RespawnZone 1개) [C12 · 계약 K7-0] — 값은 [제안], Unity 실측(R 눌러 복귀 위치) 전까지 (추정) ──
    /// <summary>[제안 — S8 루트 (0,0,4) 선례 · 지시서 S7-W4 §4] 루트 = 섹터 로컬 (0,0,4) = generated.TransformPoint. S7에는 startAnchor가 없다.
    /// 팀 막대(Pole, 콜라이더 없음 — RespawnMenuItem.cs:54·63 StripCollider)는 루트 로컬 x −2.6(:47)이라 섹터 (−2.6, 0, 4) [계산] —
    /// 스폰 슬롯 (−2, 0.6, 3)과 수평 거리 √(0.6²+1²) ≈ 1.17 [계산](시각만 — 콜라이더 없음).</summary>
    private static readonly Vector3 CheckpointRootLocal = new Vector3(0f, 0f, 4f);
    /// <summary>[제안 — S8 C17 선례] BoxCollider size — 폭 8 = 입구 개구 x −4~4 [계약 K0-2](S8 초안 폭 8), 깊이 6 = 팀 ZoneWidth [팀 RespawnMenuItem.cs:22],
    /// 높이 3 = [C17 · 판정 4] 선례. **팀 기본 높이 18(:23)은 쓰지 않는다** [계산] — 팀 권장 15~20(RespawnMenuItem.cs:23 "권장 15~20 Unit")과 다름:
    /// 천장 14 아래 낙하 시작 y 12.5를 위해 3 [C17 선례] → 킬 라인 낙하 거리가 27.5가 아니라 12.5로 줄어든다(보고 mismatch). 낙하 스폰 = 구역 윗면 − dropSpawnInset 0.5(RespawnZone.cs:41·147)
    /// + dropExtraHeight 10(RespawnController.cs:69·540) → 높이 18이면 y 27.5로 천장(GEO_S7_Ceiling y 14~14.5, S7_Builder.cs:55-56·136) 위에서
    /// 떨어진다(킬 라인 복귀 = 낙하, RespawnController.cs:341). 높이 3이면 y 12.5 < 천장 아랫면 14.
    /// 단 이것은 몸 기준이다. 카메라(추적점 + 4cos p + 10sin p, 초안 §2-1)는 낙하 시작 때 pitch 0·30·60에서 y 16.50·20.96·23.16으로
    /// 천장판 윗면 14.5 위에 있다 [계산 — SmoothDamp 지연 제외 하한] → 그동안 몸이 안 보일 수 있다(추정). 좌표로 풀 수 없다 — 값 불변, 보고서 Unity 실측 항목 8.
    /// 수동 R(페이드)의 바닥 = 구역 중심에서 아래로 쏜 레이의 가장 높은 비트리거 면(RespawnZone.cs:153-184) — 높이 3이면 중심 y 1.5에서 바닥 윗면 y 0.
    /// (참고: S8은 천장 윗면이 중심 아래라 레이가 천장을 잡는 문제였고[C17], S7은 천장 14가 중심 y 9 위라 그 문제는 없다 [계산].)
    /// 구역 = 섹터 로컬 x −4~4 · y 0~3 · z 1~7 — Checkpoint 마커 (0,0.1,3)·스폰 슬롯 (−2/0/2, 0.6, 3)을 품는다 [계약 K0-2].</summary>
    private static readonly Vector3 CheckpointSize = new Vector3(8f, 3f, 6f);
    /// <summary>[팀 RespawnMenuItem.cs:39 규칙] center = (0, 높이/2, 0) — 원점 = 구역 밑면 = 바닥 윗면 y 0. = (0, 1.5, 0).</summary>
    private static readonly Vector3 CheckpointCenter = new Vector3(0f, 1.5f, 0f);
    private const int ExpectedRespawnZones = 1;                              // [C12] S7_Gimmicks 아래 RespawnZone 정확히 1개(PTF S7 행 값)

    // ── 계약 개수·자기 검사 기준(경고만 — 값을 바꾸지 않는다) ──
    private const int ExpectedServers = 6;                                   // [계약 K7-1]
    private const int ExpectedPorts = 6;                                     // [계약 K7-1]
    private const float BookComputerMin = 2f;                                // [제안 설계 §3-6] 책 ↔ 컴퓨터 ≥ 2
    private const float ShapeMaxWidth = 1.414f;                              // [계산 초안 §6] 정사면체 최대 수평 폭 — 이웃 E 트리거 틈 기준
    /// <summary>[제안] 포트 앵커 ↔ 자기 서버 콜라이더 거리 상한. 안C 기대값: 앞줄 포트 z30 = 서버 +Z면, 뒷줄 z44 = 서버 −Z면 → 거리 0
    /// (portServerIndex [0,2,4,1,3,5]) [사용자 09-29 나·안C · 초안 §3-2·§3-3].</summary>
    private const float PortToServerMax = 0.5f;
    private const float CheckEps = 1e-3f;

    // ═════════════════════════ 진입점 ═════════════════════════

    private static int errorCount;
    private static int warningCount;
    private static int persistentWired;
    private static int tempDoorSkipped;          // [판정 17-S7] refs 문이 null이라 건너뛴 문 배선 수(Log만 — 에러·경고 아님)
    private static int respawnZoneCount;         // [C12] S7_Gimmicks 아래 RespawnZone 수(끝 로그·개수 검사)
    private static bool lineMaterialFetched;
    private static Material lineMaterial;        // S7_Dress.WiringLineMaterial() 반환값을 한 Wire 안에서 한 번만 받는다 [판정 11]

    /// <summary>[계약 K7-2] 예외를 던지지 않는다(로그만) — S7_Builder는 지형을 지우지 않는다. 앵커가 null이면 LogError 후 그 항목만 건너뛴다.
    /// 예외: refs.entryDoor·exitDoor null은 TEMP 기간 정상 상태라 Log만 남긴다 [판정 17-S7 · 계약 K7-0·K7-1].</summary>
    public static void Wire(Transform generated, S7_Refs refs)
    {
        errorCount = 0;
        warningCount = 0;
        persistentWired = 0;
        tempDoorSkipped = 0;
        respawnZoneCount = 0;
        lineMaterialFetched = false;
        lineMaterial = null;
        if (generated == null)
        {
            Debug.LogError("[S7_Wiring] generated가 null이다 — 팀 기믹을 배치하지 않는다.");
            return;
        }
        if (refs == null)
        {
            Debug.LogError("[S7_Wiring] refs(S7_Refs)가 null이다 — 팀 기믹을 배치하지 않는다.", generated);
            return;
        }

        HashSet<GameObject> rootsBefore = SnapshotRoots(generated);
        Transform group = null;
        List<KeyValuePair<string, BoxCollider>> triggers = new List<KeyValuePair<string, BoxCollider>>();
        try
        {
            group = NewGroup(generated);
            Transform root = NewChild(group, RootName, group.position, group.rotation).transform;

            // 1) 역할 매니저 — S7 전용 1개(S8과 따로, S7_팀API.md §2-2). 팀 기본값 그대로(Esc/Return, respawnController 비움 → 실행 중 스스로 찾음 :114).
            RoleAssignmentManager roles = NewChild(root, RoleManagerName, root.position, root.rotation).AddComponent<RoleAssignmentManager>();
            EditorUtility.SetDirty(roles);

            // 2) 역할 사물 3 [팀 ServerRoomPowerSelfTest.cs:65-67, Ch8TestRoomMenuItem.cs:244-269]
            RoleSlot bookSlot = BuildSlot(generated, root, roles, RoleIdBook, false, refs.roleBookAnchor, "roleBookAnchor", LabelColorBook, triggers);
            if (bookSlot != null)
            {
                BookPanel book = bookSlot.gameObject.AddComponent<BookPanel>();
                book.slot = bookSlot;   // pages는 팀 기본 빈 배열 그대로 — 본문 콘텐츠가 저장소에 없다(S7_팀API.md §0-2, 모르겠다)
                EditorUtility.SetDirty(book);
            }

            RoleSlot computerSlot = BuildSlot(generated, root, roles, RoleIdComputer, false, refs.roleComputerAnchor, "roleComputerAnchor", LabelColorComputer, triggers);
            QuizTerminal quiz = null;
            if (computerSlot != null)
            {
                quiz = computerSlot.gameObject.AddComponent<QuizTerminal>(); // 컴퓨터 슬롯과 같은 GO(S7_팀API.md §2-3, CH8 BookPanel 선례)
                quiz.slot = computerSlot;  // questions는 팀 기본 빈 배열 그대로 — 문항 콘텐츠 없음(모르겠다). actionLabel은 전력 장치가 실행 중 채운다(:394)
                EditorUtility.SetDirty(quiz);
            }

            // 전력 담당만 여러 명 공용 [팀 RoleSlot.cs:44-46, ServerRoomPowerSelfTest.cs:67]
            RoleSlot powerSlot = BuildSlot(generated, root, roles, RoleIdPower, true, refs.rolePowerAnchor, "rolePowerAnchor", LabelColorPower, triggers);

            // 3) W_ENTRY 패널 + 포트 6(앵커 벽 한 줄) + 선 + 규칙표 — 완료 → 입구 문 [확정 설계 §3-2]
            //    TEMP 기간 refs.entryDoor = null → WireDoor가 [TEMP 판정 17-S7] Log만 남기고 건너뛴다(배선 코드는 그대로).
            WiringPanel entryPanel = BuildEntryPanel(generated, root, refs.entryPanelAnchor, triggers);
            if (entryPanel != null)
                WireDoor(entryPanel.OnCircuitCompleted, refs.entryDoor, entryPanel, "refs.entryDoor", "W_ENTRY OnCircuitCompleted", "TEMP_S7_EntryDoor_Open");

            // 4) 서버실(전력) 패널 + 포트 6(portAnchors 순서) + 선 + 규칙표
            WiringPanel powerPanel = BuildPowerPanel(generated, root, refs, triggers);

            // 5) 전력 유지 장치 + 게이지(팀 기본 OnGUI 그대로, 서버실 표시물 없음 [확정 설계 §3-4], d 팀 값 [판정 16])
            PowerMaintenanceController power = BuildPower(root, roles, quiz, powerPanel, entryPanel, bookSlot, computerSlot, powerSlot);

            // 6) 출구 문 — [확정 §3-7 문구] → [판정 10] onAllCleared. TEMP 기간 refs.exitDoor = null → Log만 [판정 17-S7]
            WireExit(refs.exitDoor, quiz, power);

            // 6-1) 수동 R 체크포인트 — 팀 RespawnZone 1개 [C12 · 계약 K7-0]. 그룹 바로 아래(S7_GimmickRoot 밖)라
            //      지연 활성화(root.SetActive(false))와 무관하게 켜진 채 저장된다 [해석 — 명령 결정, 지시서 S7-W4 §5].
            BuildCheckpoint(group, generated);

            // 7) 지연 활성화(씬 연결) [판정 13]
            BuildActivator(generated, group, root.gameObject);

            // 8) 자기 검사(경고만 — 아무 값도 바꾸지 않는다)
            SelfCheck(generated, refs, triggers);
        }
        catch (System.Exception e)
        {
            Err($"배선 중 예외 — 지형은 그대로 둔다: {e}");
        }
        finally
        {
            AdoptStrayRoots(generated, rootsBefore, group);
        }

        // 8-1) RespawnZone 개수 [C12] — 씬 루트 잔여를 그룹으로 옮긴 뒤에 센다(정확히 1개가 아니면 Err).
        CheckRespawnZones(generated, group);

        // 끝 로그 — 문 상태를 실제대로 적는다. TEMP면 "영구 배선 0/2(TEMP 판정 17-S7 — 입구·출구 상시 개방)", 문이 있으면 2/2.
        string doorState = tempDoorSkipped > 0
            ? $"영구 UnityEvent 배선 {persistentWired}/2(TEMP 판정 17-S7 — 입구·출구 상시 개방, 문 배선 {tempDoorSkipped}개 건너뜀)"
            : $"영구 UnityEvent 배선 {persistentWired}/2";
        string entranceState = LinkEntranceWiring
            ? "주의: W_ENTRY 회로 배정 코드가 팀에 없어 입구 패널이 반응하지 않고, entranceWiring 연결 때문에 '실험 시작'이 거부된다(PowerMaintenanceController.cs:241). "
            : "TEMP [C9]: entranceWiring 비움 — '실험 시작'이 W_ENTRY 잠금을 요구하지 않는다(PowerMaintenanceController.cs:241 조건 불성립). " +
              "W_ENTRY 패널 자체는 회로 배정 코드가 없어 반응하지 않는다. ";
        if (errorCount > 0)
            Debug.LogError($"[S7_Wiring] 끝 — 에러 {errorCount}개, 경고 {warningCount}개(위 로그 확인). {doorState}. RespawnZone {respawnZoneCount}.", generated);
        else
            Debug.Log($"[S7_Wiring] 완료 — 팀 CH7 기믹을 '{GroupName}' 아래 배치·배선(경고 {warningCount}개). {doorState}. RespawnZone {respawnZoneCount}. " +
                      entranceState + "문답·책 콘텐츠도 비어 있다(보고서 S7-W3·S7-W4).", generated);
    }

    // ═════════════════════════ 그룹 ═════════════════════════

    private static Transform NewGroup(Transform generated)
    {
        // 같은 빌드 안에서 두 번 불려도 겹치지 않게 기존 그룹을 지운다(우리 생성물만 있는 그룹 — S5_Wiring과 같은 방식).
        Transform old = generated.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    // ═════════════════════════ 역할 사물 ═════════════════════════

    /// <summary>팀 Ch8TestRoomMenuItem.Slot(:244-269)과 같은 구성: 슬롯 GO에 트리거 BoxCollider + RoleSlot(roleId, manager).
    /// 자식 Label은 CreateRoleLabels = false라 만들지 않는다(역할 표지 = L VIS_ [판정 12]).
    /// 팀 Desk(솔리드 책상)는 만들지 않는다 — 받침은 B의 GEO_S7_Desk·GEO_S7_PowerConsole(초안 §4, S7-C W2).</summary>
    private static RoleSlot BuildSlot(Transform generated, Transform root, RoleAssignmentManager roles, string roleId, bool shared,
        Transform anchor, string anchorField, Color labelColor, List<KeyValuePair<string, BoxCollider>> triggers)
    {
        if (anchor == null)
        {
            Err($"refs.{anchorField}가 null — 역할 사물 '{roleId}'를 건너뛴다.");
            return null;
        }

        GameObject go = NewChild(root, SlotNamePrefix + roleId, anchor.position, FlatRotation(anchor.forward, root));
        float h = LocalHeight(generated, anchor.position);
        if (Mathf.Abs(h) > 0.02f)
            Warn($"refs.{anchorField} 높이 {h:0.###} ≠ 0 — 슬롯 루트는 바닥 높이(팀 CH8 선례, 초안 §3-2)라 트리거 중심만 y {RoleTriggerY0}~{RoleTriggerY1}에 맞춘다.");

        BoxCollider trigger = go.AddComponent<BoxCollider>(); // RoleSlot GO에 트리거(RoleSlot.cs:82-104)
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, (RoleTriggerY0 + RoleTriggerY1) * 0.5f - h, 0f);
        trigger.size = RoleTriggerSize;
        triggers.Add(new KeyValuePair<string, BoxCollider>(go.name, trigger));

        RoleSlot slot = go.AddComponent<RoleSlot>();
        slot.roleId = roleId;
        slot.manager = roles;
        slot.allowMultipleUsers = shared; // 팀 기본 false, 전력만 true [팀 RoleSlot.cs:44-46]
        // interactKey E는 팀 기본 그대로.
        EditorUtility.SetDirty(slot);

        if (CreateRoleLabels)
            BuildLabel(go.transform, roleId, labelColor, new Vector3(0f, RoleLabelHeight - h, 0f), RoleLabelCharacterSize);
        return slot;
    }

    // ═════════════════════════ 배선 패널 ═════════════════════════

    /// <summary>W_ENTRY: 패널 GO = entryPanelAnchor 포즈. 포트 6개를 앵커 오른쪽 방향 한 줄(간격 EntryPortSpacing, 중심 = 앵커)에
    /// OUT_A..IN_3 순으로 둔다(초안 본안: 앵커 (16,1.2,8) forward −X → z 2.5·4.7·…·13.5). 트리거는 앵커 앞(forward) 0~EntryPortTriggerDepth.</summary>
    private static WiringPanel BuildEntryPanel(Transform generated, Transform root, Transform anchor, List<KeyValuePair<string, BoxCollider>> triggers)
    {
        if (anchor == null)
        {
            Err("refs.entryPanelAnchor가 null — W_ENTRY 패널·포트·입구 문 배선을 건너뛴다.");
            return null;
        }

        Quaternion rot = FlatRotation(anchor.forward, root);
        GameObject go = NewChild(root, EntryPanelName, anchor.position, rot);
        WiringPanel panel = go.AddComponent<WiringPanel>();
        if (panel.OnCircuitCompleted == null) panel.OnCircuitCompleted = new WiringPanel.CompletionEvent(); // 초기화자 없음(WiringPanel.cs:40, 팀 null 방어 PowerMaintenanceController.cs:148)

        WiringPort[] outs = new WiringPort[OutputCount];
        WiringPort[] ins = new WiringPort[PortIds.Length - OutputCount];
        Vector3 right = rot * Vector3.right;
        for (int i = 0; i < PortIds.Length; i++)
        {
            float offset = (i - (PortIds.Length - 1) * 0.5f) * EntryPortSpacing; // [계산] −5.5 … +5.5
            Vector3 pos = anchor.position + right * offset;
            WiringPort port = BuildPort(generated, go.transform, EntryPanelName + "_Port_" + PortNames[i], pos, rot, i, panel,
                EntryPortTriggerWidth, EntryPortTriggerDepth, triggers);
            if (i < OutputCount) outs[i] = port; else ins[i - OutputCount] = port;
        }
        panel.outputPorts = outs;
        panel.inputPorts = ins;
        EditorUtility.SetDirty(panel);

        AddLinesAndRules(go, panel, EntryLineWidth, true);
        CheckCircuitIds(EntryCircuitPath, "W_ENTRY");
        // 의도된 경고 1건 [판정 17-S7 · 계약 K7-0] — 부트스트랩(회로 배정 코드)은 만들지 않는다.
        Warn("W_ENTRY에 회로를 배정하는 팀 코드가 없다(WiringPanel.AssignCircuit은 PowerMaintenanceController.cs:418만 부름, 회로 필드는 비공개·비직렬화) — " +
             "포트 입력이 무시되어(WiringPanel.cs:86,101) 패널이 완료되지 않는다(입구 문은 TEMP 상시 개방 [판정 17-S7]). " +
             (LinkEntranceWiring
                 ? "entranceWiring 연결 때문에 '실험 시작'도 거부된다(PowerMaintenanceController.cs:241). "
                 : "TEMP [C9]: entranceWiring을 비워 '실험 시작'은 W_ENTRY 잠금을 요구하지 않는다(PowerMaintenanceController.cs:241 조건 불성립). ") +
             "부트스트랩으로 메우지 않는다(HANDOFF §5-0) — 팀 요청 대상.");
        return panel;
    }

    /// <summary>서버실(전력) 패널: 패널 GO는 위치 무관(초안 §4) — 루트 원점. 포트 = refs.portAnchors[i] 포즈 그대로(OUT_A..IN_3 계약 순서).</summary>
    private static WiringPanel BuildPowerPanel(Transform generated, Transform root, S7_Refs refs, List<KeyValuePair<string, BoxCollider>> triggers)
    {
        GameObject go = NewChild(root, PowerPanelName, root.position, root.rotation);
        WiringPanel panel = go.AddComponent<WiringPanel>();
        if (panel.OnCircuitCompleted == null) panel.OnCircuitCompleted = new WiringPanel.CompletionEvent(); // 전력 장치가 실행 중 AddListener(:148-149)

        List<WiringPort> outs = new List<WiringPort>();
        List<WiringPort> ins = new List<WiringPort>();
        List<Transform> anchors = refs.portAnchors;
        if (anchors == null) Err("refs.portAnchors가 null — 전력 회로 포트 0개(전력 퍼즐 불가).");
        else
        {
            if (anchors.Count != ExpectedPorts) Err($"refs.portAnchors 개수 {anchors.Count} ≠ 계약 {ExpectedPorts}.");
            int n = Mathf.Min(anchors.Count, PortIds.Length);
            for (int i = 0; i < n; i++)
            {
                Transform a = anchors[i];
                if (a == null)
                {
                    Err($"refs.portAnchors[{i}]({PortNames[i]})가 null — 그 포트를 건너뛴다(그 회로 쌍은 완성 불가).");
                    continue;
                }
                WiringPort port = BuildPort(generated, go.transform, PowerPanelName + "_Port_" + PortNames[i], a.position,
                    FlatRotation(a.forward, root), i, panel, ServerPortTriggerWidth, ServerPortTriggerDepth, triggers);
                if (i < OutputCount) outs.Add(port); else ins.Add(port);
            }
        }
        panel.outputPorts = outs.ToArray();   // null 요소 없음(계약 K0-3)
        panel.inputPorts = ins.ToArray();
        EditorUtility.SetDirty(panel);

        AddLinesAndRules(go, panel, PowerLineWidth, false);
        foreach (string p in PowerCircuitPaths) CheckCircuitIds(p, "W_POWER");
        return panel;
    }

    /// <summary>WiringPort 1개 [팀 WiringPort.cs:16-30]: role·portId·panel만 넣고 interactKey E는 팀 기본. 스크립트가 콜라이더를 요구하지 않으므로
    /// 트리거 BoxCollider를 같은 GO에 붙인다(S7_팀API.md §1-2). 트리거 = 앵커 로컬 x ±width/2, z 0~depth(forward = 플레이어 쪽), 높이 섹터 y 0~2.</summary>
    private static WiringPort BuildPort(Transform generated, Transform parent, string name, Vector3 worldPos, Quaternion worldRot, int index,
        WiringPanel panel, float width, float depth, List<KeyValuePair<string, BoxCollider>> triggers)
    {
        GameObject go = NewChild(parent, name, worldPos, worldRot);
        float h = LocalHeight(generated, worldPos);

        BoxCollider trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, (PortTriggerY0 + PortTriggerY1) * 0.5f - h, depth * 0.5f); // [계산] 앵커 y 1.2 → (0, −0.2, depth/2) = 초안 값
        trigger.size = new Vector3(width, PortTriggerY1 - PortTriggerY0, depth);
        triggers.Add(new KeyValuePair<string, BoxCollider>(name, trigger));

        WiringPort port = go.AddComponent<WiringPort>();
        port.role = index < OutputCount ? WiringPort.Role.Output : WiringPort.Role.Input;
        port.portId = PortIds[index];
        port.panel = panel;
        EditorUtility.SetDirty(port);

        if (CreatePortLabels)
            BuildLabel(go.transform, PortLabels[index], index < OutputCount ? PortColorOutput : PortColorInput,
                new Vector3(0f, PortLabelRise, PortLabelOut), PortLabelCharacterSize);
        return port;
    }

    /// <summary>W_LINES(WiringLineRenderer :14-20)·W_RULE(WiringRuleDisplay :16-19)를 패널 GO에 붙인다. W_CANCEL은 두지 않는다
    /// (설계 요구 없음, 출력 재선택으로 해제 — S7_팀API.md §1-5). lineWidth·lineMaterial·displayArea = 인스펙터 값 [판정 11].</summary>
    private static void AddLinesAndRules(GameObject panelGo, WiringPanel panel, float lineWidth, bool entry)
    {
        WiringLineRenderer lines = panelGo.AddComponent<WiringLineRenderer>();
        lines.panel = panel;
        lines.lineWidth = lineWidth;
        Material mat = LineMaterial();
        if (mat != null) lines.lineMaterial = mat;
        else Warn($"S7_Dress.WiringLineMaterial()이 null — {panelGo.name} 선 머티리얼이 비어 선이 안 보일 수 있다(WiringLineRenderer.cs:69, 모르겠다).");
        EditorUtility.SetDirty(lines);

        WiringRuleDisplay rule = panelGo.AddComponent<WiringRuleDisplay>();
        rule.panel = panel;
        if (entry) rule.displayArea = EntryRuleArea; // 전력 패널은 팀 기본 Rect 그대로
        EditorUtility.SetDirty(rule);
    }

    /// <summary>선 머티리얼 = L 도우미 한 곳 [판정 11 · 계약 K7-2]. W는 머티리얼을 만들지 않고 반환값만 쓴다.
    /// 계약상 Apply 전에 불려도 동작한다(refs·Apply 상태에 기대지 않음). 한 Wire 안에서 한 번만 부르고 두 패널이 같은 참조를 쓴다.
    /// 도우미가 예외를 던지면 에러 1줄 후 null(선 머티리얼 없이 나머지 배선은 계속).</summary>
    private static Material LineMaterial()
    {
        if (lineMaterialFetched) return lineMaterial;
        lineMaterialFetched = true;
        try
        {
            lineMaterial = S7_Dress.WiringLineMaterial();
        }
        catch (System.Exception e)
        {
            Err($"S7_Dress.WiringLineMaterial() 예외 — 선 머티리얼 없이 계속한다: {e.Message}");
            lineMaterial = null;
        }
        return lineMaterial;
    }

    /// <summary>팀 회로 에셋을 읽기만 해 포트 ID가 맞는지 대조한다(수정 없음).</summary>
    private static WiringCircuitData CheckCircuitIds(string path, string who)
    {
        WiringCircuitData data = AssetDatabase.LoadAssetAtPath<WiringCircuitData>(path);
        if (data == null)
        {
            Err($"{who}: 팀 회로 에셋 '{path}'가 없다(팀 메뉴 'Tools/Server Room Power/Create Test Circuit Data' 시험 데이터 생성물 — 플레이테스트용, ServerRoomPowerTestData.cs:44).");
            return null;
        }
        if (!data.Validate(out string error)) Err($"{who}: 팀 회로 '{path}' 검증 실패 — {error}");
        for (int i = 0; i < PortIds.Length; i++)
        {
            string[] ids = i < OutputCount ? data.outputIds : data.inputIds;
            if (ids == null || System.Array.IndexOf(ids, PortIds[i]) < 0)
                Err($"{who}: 포트 ID '{PortIds[i]}'가 회로 '{data.circuitId}'의 {(i < OutputCount ? "outputIds" : "inputIds")}에 없다.");
        }
        return data;
    }

    // ═════════════════════════ 전력 ═════════════════════════

    /// <summary>[팀 ServerRoomPowerSelfTest.cs:77-84 연결 필드만] 수치 필드는 대입하지 않는다(팀 기본값 = HANDOFF §2-2와 일치, S7_팀API.md §3-1).</summary>
    private static PowerMaintenanceController BuildPower(Transform root, RoleAssignmentManager roles, QuizTerminal quiz, WiringPanel powerPanel,
        WiringPanel entryPanel, RoleSlot bookSlot, RoleSlot computerSlot, RoleSlot powerSlot)
    {
        GameObject go = NewChild(root, PowerName, root.position, root.rotation);
        PowerMaintenanceController ctrl = go.AddComponent<PowerMaintenanceController>();
        ctrl.manager = roles;
        ctrl.quiz = quiz;
        ctrl.wiringPanel = powerPanel;
        ctrl.bookSlot = bookSlot;
        ctrl.computerSlot = computerSlot;
        ctrl.powerSlot = powerSlot;
        ctrl.entranceWiring = LinkEntranceWiring ? entryPanel : null;

        List<WiringCircuitData> circuits = new List<WiringCircuitData>();
        foreach (string p in PowerCircuitPaths)
        {
            WiringCircuitData d = AssetDatabase.LoadAssetAtPath<WiringCircuitData>(p);
            if (d != null) circuits.Add(d); // 없으면 CheckCircuitIds가 이미 Err
        }
        ctrl.circuits = circuits.ToArray(); // POWER_1 → 2 → 3 순서(:416)
        EditorUtility.SetDirty(ctrl);

        if (quiz == null) Err("QuizTerminal이 없다 — 전력 장치 quiz 비어 '실험 시작'·제출 게이트·완료가 동작하지 않는다.");
        if (bookSlot == null || computerSlot == null || powerSlot == null)
            Warn("역할 슬롯 일부가 없다 — 비어 있는 슬롯은 팀 시작 요건 검사에서 빠진다(PowerMaintenanceController.cs:443-449).");
        if (circuits.Count != PowerCircuitPaths.Length) Err($"전력 회로 {circuits.Count}/{PowerCircuitPaths.Length}개만 찾았다.");

        PowerGauge gauge = go.AddComponent<PowerGauge>();
        gauge.controller = ctrl; // size·topMargin 팀 기본 그대로(PowerGauge.cs:14-15)
        EditorUtility.SetDirty(gauge);
        return ctrl;
    }

    // ═════════════════════════ 문 배선 ═════════════════════════

    /// <summary>[확정 §3-7 문구] → [판정 10] onAllCleared. PowerCompleted 분기는 쓰지 않음 [판정 10](ExitSource 상수 참고, 코드만 남김).</summary>
    private static void WireExit(doorPhysics exitDoor, QuizTerminal quiz, PowerMaintenanceController power)
    {
        if (ExitSource == ExitSignal.QuizAllCleared)
        {
            if (quiz == null) { Err("QuizTerminal이 없어 출구 문 배선(onAllCleared)을 건너뛴다."); return; }
            WireDoor(quiz.onAllCleared, exitDoor, quiz, "refs.exitDoor", "QuizTerminal.onAllCleared", "TEMP_S7_ExitDoor_Open");
        }
        else
        {
            // 쓰지 않음 [판정 10] — ExitSource가 QuizAllCleared로 고정이라 이 분기는 실행되지 않는다.
            if (power == null) { Err("전력 장치가 없어 출구 문 배선(onCompleted)을 건너뛴다."); return; }
            WireDoor(power.onCompleted, exitDoor, power, "refs.exitDoor", "PowerMaintenanceController.onCompleted", "TEMP_S7_ExitDoor_Open");
        }
    }

    /// <summary>doorPhysics.SetPadPressed(true) bool 영구 리스너(RuntimeOnly) — S1_Builder.cs:234와 같은 방식. 한 번 true면 닫는 호출이 없어
    /// 계속 열림(CH7엔 재시작 흐름 없음, S7_팀API.md §4-1). 같은 문에 PadTrigger는 두지 않는다(떨림, ExitWeightPlate.cs:42).
    /// door == null은 TEMP 기간의 정상 상태다 [판정 17-S7 · 계약 K7-0·K7-1] — Debug.Log만 남기고 건너뛴다(에러·경고 수 불변).
    /// null을 TEMP로 넘길지는 B ValidateRefs(TempOpenDoors)가 정한다 — W는 이름으로 TEMP 표지를 찾지 않는다(tempMarker는 로그 문구용 이름뿐).</summary>
    private static void WireDoor(UnityEngine.Events.UnityEventBase evt, doorPhysics door, Object owner, string doorField, string evtName, string tempMarker)
    {
        if (evt == null) { Err($"{evtName}가 null — 문 배선을 건너뛴다."); return; }
        if (door == null)
        {
            tempDoorSkipped++;
            Debug.Log($"[TEMP 판정 17-S7] {doorField} = null — 문 대신 {tempMarker}(상시 개방)라 {evtName} → SetPadPressed(true) 영구 배선을 건너뛴다. " +
                      "문이 돌아오면(B TempOpenDoors = false) 이 파일 수정 없이 배선된다.");
            return;
        }
        Component oc = owner as Component;
        if (oc != null && oc.gameObject.scene != door.gameObject.scene)
        {
            Err($"{evtName}와 {doorField}가 다른 씬에 있다 — 영구 배선 불가(씬 사이 참조).");
            return;
        }

        // 재실행 잔여 방지: 같은 문으로 가는 SetPadPressed 영구 배선을 먼저 걷는다(새 GO라 보통 0개).
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            if (evt.GetPersistentMethodName(i) == nameof(doorPhysics.SetPadPressed) && evt.GetPersistentTarget(i) == door)
                UnityEventTools.RemovePersistentListener(evt, i);

        int before = evt.GetPersistentEventCount();
        UnityEventTools.AddBoolPersistentListener(evt, door.SetPadPressed, true);
        if (owner != null) EditorUtility.SetDirty(owner);

        bool ok = evt.GetPersistentEventCount() == before + 1 && evt.GetPersistentTarget(before) == door;
        if (ok) persistentWired++;
        else Err($"{evtName} → {doorField}.SetPadPressed(true) 영구 배선 확인 실패.");
    }

    // ═════════════════════════ 수동 R 체크포인트 ═════════════════════════

    /// <summary>[C12 · 계약 K7-0] 팀 RespawnZone 1개 — S7 전실 입구. 없으면 S7 안에서 R이 S6 체크포인트로 보낸다
    /// (RespawnController.cs:181-206 저장·:252-256 수동 R — 마지막으로 밟은 구역으로 페이드 복귀). S8_Wiring.BuildCheckpoint 구조를 따른다:
    /// 팀 생성 함수 RespawnMenuItem.CreateCheckpoint()(public static, RespawnMenuItem.cs:27-28)를 직접 부르고, 선택물에 RespawnZone이
    /// 있는지 확인한 뒤(S1_Builder 선례) 없으면 호출 전후 목록 비교로 찾고(S6_Wiring 선례), S7_Gimmicks 아래로 옮긴다(씬 루트 잔여 0 [계약 K0-3]).
    /// 그다음 배치 변환과 BoxCollider 인스펙터 값(size·center)만 넣는다 [해석 K0-3 · 판정 11 — 인스펙터 값]. isTrigger·막대·깃발·RespawnZone 필드는 팀 값 그대로.
    /// 루트 = generated.TransformPoint(0,0,4), 회전 identity(축 정렬 전제, RespawnZone.cs:186-187 — 섹터가 돌아 있으면 경고).
    /// S7_GimmickRoot 아래에 두지 않는다 — RespawnZone은 OnGUI가 없고, 꺼진 채 저장되면 첫 진입 때 체크포인트를 저장하지 못할 수 있다(추정)
    /// [해석 — 명령 결정]. 값은 [제안] (추정 — Unity 실측 전).</summary>
    private static RespawnZone BuildCheckpoint(Transform group, Transform generated)
    {
        Vector3 root = generated.TransformPoint(CheckpointRootLocal);
        if (Quaternion.Angle(generated.rotation, Quaternion.identity) > 0.01f)
            Warn($"섹터가 월드 축과 어긋나 있다(회전 {generated.rotation.eulerAngles}) — RespawnZone은 월드 AABB를 쓰므로(RespawnZone.cs:186-195) 구역을 월드 축 정렬로 둔다.");

        // 팀 생성 함수 호출 → 선택물 확인(S1 선례) → 없으면 전후 목록 비교(S6 선례)
        HashSet<RespawnZone> before = new HashSet<RespawnZone>(Object.FindObjectsOfType<RespawnZone>(true));
        Selection.activeGameObject = null;
        RespawnMenuItem.CreateCheckpoint();
        GameObject sel = Selection.activeGameObject;
        RespawnZone zone = sel != null ? sel.GetComponent<RespawnZone>() : null;
        if (zone != null && before.Contains(zone)) zone = null;
        if (zone == null)
        {
            foreach (RespawnZone z in Object.FindObjectsOfType<RespawnZone>(true))
                if (!before.Contains(z)) { zone = z; break; }
            if (zone != null)
                Warn($"팀 '{CheckpointMenuPath}' 생성물을 선택물로 받지 못해 호출 전후 목록 비교로 찾았다('{zone.name}').");
        }
        if (zone == null)
        {
            Err($"팀 '{CheckpointMenuPath}'(RespawnMenuItem.CreateCheckpoint) 생성물에서 RespawnZone을 찾지 못했다 — 수동 R 체크포인트 없음. S7 안에서 R이 S6 체크포인트로 간다.");
            return null;
        }

        GameObject go = zone.gameObject;
        go.transform.SetParent(group, true);  // Adopt — 씬 루트 잔여 0 [계약 K0-3]. 부모 = S7_Gimmicks(GimmickRoot 아님)
        go.name = CheckpointName;
        go.transform.SetPositionAndRotation(root, Quaternion.identity); // 축 정렬 [팀 RespawnZone.cs:186-187]

        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null)
        {
            Err($"'{CheckpointName}'에 BoxCollider가 없다 — 팀 생성 구조가 바뀌었다(RespawnMenuItem.cs:36-39).");
            return zone;
        }
        // isTrigger true는 팀이 넣은 값 그대로(RespawnMenuItem.cs:37). size·center만 [제안] 값으로(팀 높이 18 대신 3 — 상수 주석 [계산]).
        box.center = CheckpointCenter;
        box.size = CheckpointSize;
        if (!box.isTrigger) Warn($"'{CheckpointName}' BoxCollider가 트리거가 아니다 — 팀 값(RespawnMenuItem.cs:37)이 바뀌었다(값은 건드리지 않음).");
        EditorUtility.SetDirty(box);
        EditorUtility.SetDirty(go);
        return zone;
    }

    /// <summary>[C12] S7_Gimmicks 아래 RespawnZone이 정확히 ExpectedRespawnZones(1)개인지 센다 — 다르면 Err. 있으면 구역이
    /// Checkpoint 마커 (0,0.1,3)·스폰 슬롯 (−2/0/2, 0.6, 3)을 품는지 확인한다(경고만) [계약 K0-2]. 아무 값도 바꾸지 않는다.</summary>
    private static void CheckRespawnZones(Transform generated, Transform group)
    {
        if (generated == null) return;
        if (group == null)
        {
            Err($"{GroupName} 그룹이 없어 RespawnZone 개수를 셀 수 없다(0개로 본다).");
            return;
        }
        RespawnZone[] zones = group.GetComponentsInChildren<RespawnZone>(true);
        respawnZoneCount = zones.Length;
        if (zones.Length != ExpectedRespawnZones)
        {
            Err($"{GroupName} 아래 RespawnZone {zones.Length}개 ≠ {ExpectedRespawnZones}개 [C12] — 수동 R 체크포인트 확인.");
            return;
        }
        BoxCollider box = zones[0].GetComponent<BoxCollider>();
        if (box == null) return; // BuildCheckpoint가 이미 Err
        Bounds w = WorldBoundsOf(box);
        Vector3[] marks = { new Vector3(0f, 0.1f, 3f), new Vector3(-2f, 0.6f, 3f), new Vector3(0f, 0.6f, 3f), new Vector3(2f, 0.6f, 3f) };
        foreach (Vector3 m in marks)
            if (!w.Contains(generated.TransformPoint(m)))
                Warn($"'{zones[0].name}' 구역이 마커 {m}(섹터 로컬)를 품지 않는다 — 입구에서 체크포인트가 저장되지 않을 수 있다.");
    }

    // ═════════════════════════ 지연 활성화 ═════════════════════════

    /// <summary>[판정 13 · 계약 K7-0] 우리 기존 런타임 스크립트 Lab_ActivateWhenPlayersReady(zone :20·targets :23, Scripts/Lab_ActivateWhenPlayersReady.cs:17-23)만
    /// 쓴다 — 새 스크립트 없음(K0-1). 켜진 채 저장되는 것은 활성기(그룹 바로 아래) 자신뿐이고, 기믹 루트(S7_GimmickRoot)는 꺼진 채 저장한다.
    /// 켜질 때 RoleSlot.OnEnable 등록·전력/문답 Bind가 차례로 일어난다(S7_팀API.md §6-4). 기믹 동작은 불변(씬 연결).</summary>
    private static void BuildActivator(Transform generated, Transform group, GameObject root)
    {
        if (!DeferActivationUntilArrival) return;

        Vector3 center = (ActivationZoneMin + ActivationZoneMax) * 0.5f;
        GameObject go = NewChild(group, ActivatorName, generated.TransformPoint(center), generated.rotation);
        BoxCollider zone = go.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.center = Vector3.zero;
        zone.size = ActivationZoneMax - ActivationZoneMin;

        Lab_ActivateWhenPlayersReady activator = go.AddComponent<Lab_ActivateWhenPlayersReady>();
        activator.zone = zone;
        activator.targets = new[] { root };
        EditorUtility.SetDirty(activator);

        root.SetActive(false);
    }

    // ═════════════════════════ 표지 ═════════════════════════

    /// <summary>[팀 Ch8TestRoomMenuItem.cs:258-267] TextMesh 표지(콜라이더 없음). 참가자가 forward 쪽에서 −forward를 보며 읽도록 글자 앞면을 돌린다
    /// [계산 — TextMesh는 자기 +Z 방향으로 볼 때 바로 읽힌다, S8_Wiring 같은 처리].</summary>
    private static void BuildLabel(Transform parent, string text, Color color, Vector3 localPos, float characterSize)
    {
        GameObject label = new GameObject(LabelName);
        label.transform.SetParent(parent, false);
        label.transform.localPosition = localPos;
        Vector3 back = Flat(-parent.forward);
        if (back.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(back.normalized, Vector3.up);

        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.characterSize = characterSize;
        tm.fontSize = LabelFontSize;
        tm.color = color;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // [팀 :265]
        if (font != null)
        {
            tm.font = font;
            MeshRenderer mr = label.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = font.material; // [팀 :267] 내장 글꼴 머티리얼 — 새 에셋 아님
        }
        else Warn("내장 글꼴 LegacyRuntime.ttf를 찾지 못했다 — 표지 글자가 보이지 않을 수 있다.");
    }

    // ═════════════════════════ 자기 검사 ═════════════════════════

    private static void SelfCheck(Transform generated, S7_Refs refs, List<KeyValuePair<string, BoxCollider>> triggers)
    {
        // 계약 개수·인덱스
        if (refs.servers == null || refs.servers.Count != ExpectedServers)
            Err($"refs.servers 개수 {(refs.servers == null ? "null" : refs.servers.Count.ToString())} ≠ 계약 {ExpectedServers}.");
        List<int> idx = refs.portServerIndex;
        if (idx == null || idx.Count != ExpectedPorts)
            Err($"refs.portServerIndex 개수 {(idx == null ? "null" : idx.Count.ToString())} ≠ 계약 {ExpectedPorts}.");
        else
        {
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < idx.Count; i++)
            {
                int s = idx[i];
                if (s < 0 || s >= ExpectedServers || !seen.Add(s)) { Err($"refs.portServerIndex[{i}] = {s} — 0..5 범위·서로 다름 위반(서버 1대에 포트 1개 [확정 §3-1])."); continue; }
                if (refs.servers == null || s >= refs.servers.Count || refs.servers[s] == null) continue;
                if (refs.portAnchors == null || i >= refs.portAnchors.Count || refs.portAnchors[i] == null) continue;
                BoxCollider sb = SolidBoxOf(refs.servers[s].gameObject);
                if (sb == null) { Warn($"servers[{s}] '{refs.servers[s].name}'에 솔리드 BoxCollider가 없다 — 포트↔서버 거리 검사 생략."); continue; }
                float d = Mathf.Sqrt(WorldBoundsOf(sb).SqrDistance(refs.portAnchors[i].position));
                if (d > PortToServerMax) Warn($"portAnchors[{i}]({PortNames[i]})가 자기 서버 servers[{s}]에서 {d:0.##} 떨어져 있다(> {PortToServerMax}).");
            }
        }

        // 책 ↔ 컴퓨터 거리 [제안 설계 §3-6]
        if (refs.roleBookAnchor != null && refs.roleComputerAnchor != null)
        {
            float d = Vector3.Distance(Flat(refs.roleBookAnchor.position), Flat(refs.roleComputerAnchor.position));
            if (d < BookComputerMin - CheckEps) Warn($"책 ↔ 컴퓨터 {d:0.##} < {BookComputerMin}(설계 §3-6).");
        }

        // 역할 사물 자리: 책·컴퓨터 트리거는 컴퓨터실 안(유리 너머 서버실 쪽에서 E가 닿지 않게, 초안 §2-4), 전력은 서버실 안·컴퓨터실 밖.
        CheckRoleInside(generated, triggers, SlotNamePrefix + RoleIdBook, refs.computerRoomBounds, true, "computerRoomBounds");
        CheckRoleInside(generated, triggers, SlotNamePrefix + RoleIdComputer, refs.computerRoomBounds, true, "computerRoomBounds");
        CheckRoleInside(generated, triggers, SlotNamePrefix + RoleIdPower, refs.serverRoomBounds, false, "serverRoomBounds");
        if (refs.rolePowerAnchor != null && refs.computerRoomBounds.size.sqrMagnitude > 0f &&
            refs.computerRoomBounds.Contains(generated.InverseTransformPoint(refs.rolePowerAnchor.position)))
            Warn("rolePowerAnchor가 computerRoomBounds 안이다 — 전력 역할 사물은 서버실 [설계 §2 :19].");

        // E 트리거끼리: 부피 겹침 = 에러(두 컴포넌트가 같은 E에 반응, S7_팀API.md §6-1), 수평 틈 < 정사면체 폭 = 경고.
        for (int a = 0; a < triggers.Count; a++)
        {
            Bounds ba = WorldBoundsOf(triggers[a].Value);
            for (int b = a + 1; b < triggers.Count; b++)
            {
                Bounds bb = WorldBoundsOf(triggers[b].Value);
                float dx = Mathf.Max(0f, Mathf.Max(ba.min.x - bb.max.x, bb.min.x - ba.max.x));
                float dy = Mathf.Max(0f, Mathf.Max(ba.min.y - bb.max.y, bb.min.y - ba.max.y));
                float dz = Mathf.Max(0f, Mathf.Max(ba.min.z - bb.max.z, bb.min.z - ba.max.z));
                if (dy > 0f) continue; // 높이가 안 겹치면 한 도형이 동시에 들어갈 수 없다
                float gap = Mathf.Sqrt(dx * dx + dz * dz);
                if (gap <= CheckEps) Err($"E 트리거 겹침: '{triggers[a].Key}' ↔ '{triggers[b].Key}' — 한 자리에서 두 기믹이 같은 E에 반응한다.");
                else if (gap < ShapeMaxWidth) Warn($"E 트리거 틈 {gap:0.###} < 정사면체 폭 {ShapeMaxWidth}: '{triggers[a].Key}' ↔ '{triggers[b].Key}'.");
            }
        }

        // 포트 트리거가 서버 본체 밖으로 나와 있는가(서버 안에만 있으면 도형이 못 들어감, S7_팀API.md §6-3)
        if (refs.servers != null)
            foreach (KeyValuePair<string, BoxCollider> t in triggers)
            {
                if (!t.Key.StartsWith(PowerPanelName)) continue;
                Bounds tb = WorldBoundsOf(t.Value);
                foreach (Transform s in refs.servers)
                {
                    if (s == null) continue;
                    BoxCollider sb = SolidBoxOf(s.gameObject);
                    if (sb == null) continue;
                    Bounds sw = WorldBoundsOf(sb);
                    if (sw.Contains(tb.min + Vector3.one * CheckEps) && sw.Contains(tb.max - Vector3.one * CheckEps))
                        Err($"포트 트리거 '{t.Key}'가 서버 '{s.name}' 안에 완전히 묻혔다 — 도형이 닿을 수 없다.");
                }
            }

        // 지연 활성화 구역이 입구 마커(0, 0.1, 0)·체크포인트(0, 0.1, 3)·스폰 슬롯(±2/0, 0.6, 3)을 품는가 [계약 K0-2 마커 위치 · 초안 §14 #6]
        if (DeferActivationUntilArrival)
        {
            Bounds zone = new Bounds((ActivationZoneMin + ActivationZoneMax) * 0.5f, ActivationZoneMax - ActivationZoneMin);
            Vector3[] marks = { new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.1f, 3f), new Vector3(-2f, 0.6f, 3f), new Vector3(0f, 0.6f, 3f), new Vector3(2f, 0.6f, 3f) };
            foreach (Vector3 m in marks)
                if (!zone.Contains(m)) Warn($"활성화 구역이 마커 {m}를 품지 않는다 — S7 기믹이 켜지지 않을 수 있다.");
        }
    }

    private static void CheckRoleInside(Transform generated, List<KeyValuePair<string, BoxCollider>> triggers, string name, Bounds room, bool mustContainXZ, string roomField)
    {
        if (room.size.sqrMagnitude <= 0f) { Warn($"refs.{roomField}가 비었다 — '{name}' 자리 검사 생략."); return; }
        foreach (KeyValuePair<string, BoxCollider> t in triggers)
        {
            if (t.Key != name) continue;
            Bounds w = WorldBoundsOf(t.Value);
            Vector3 lo = generated.InverseTransformPoint(w.min), hi = generated.InverseTransformPoint(w.max);
            Vector3 mn = Vector3.Min(lo, hi), mx = Vector3.Max(lo, hi);
            bool inside = mn.x >= room.min.x - CheckEps && mx.x <= room.max.x + CheckEps && mn.z >= room.min.z - CheckEps && mx.z <= room.max.z + CheckEps;
            bool centerIn = room.Contains(new Vector3((mn.x + mx.x) * 0.5f, room.center.y, (mn.z + mx.z) * 0.5f));
            if (mustContainXZ && !inside) Warn($"'{name}' 트리거(x {mn.x:0.##}~{mx.x:0.##}, z {mn.z:0.##}~{mx.z:0.##})가 {roomField} 밖으로 나간다 — 유리 너머에서 E가 닿을 수 있다(초안 §2-4).");
            if (!mustContainXZ && !centerIn) Warn($"'{name}' 트리거 중심이 {roomField} 밖이다.");
        }
    }

    // ═════════════════════════ 헬퍼 ═════════════════════════

    private static GameObject NewChild(Transform parent, string name, Vector3 worldPos, Quaternion worldRot)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        return go;
    }

    /// <summary>섹터 로컬 높이(generated 로컬 y — y 0 = 섹터 바닥, 계약 K0-2).</summary>
    private static float LocalHeight(Transform generated, Vector3 world) => generated.InverseTransformPoint(world).y;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>수평 forward로 도는 회전(up = 월드 +Y, 계약 K0-3). forward가 수직·0이면 fallback 회전.</summary>
    private static Quaternion FlatRotation(Vector3 forward, Transform fallback)
    {
        Vector3 f = Flat(forward);
        return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : fallback.rotation;
    }

    private static BoxCollider SolidBoxOf(GameObject go)
    {
        foreach (BoxCollider b in go.GetComponents<BoxCollider>())
            if (b != null && !b.isTrigger) return b;
        return null;
    }

    /// <summary>BoxCollider의 월드 AABB — 물리 동기화 없이 변환만으로 계산(편집 중 새로 만든 콜라이더의 bounds에 기대지 않는다).</summary>
    private static Bounds WorldBoundsOf(BoxCollider b)
    {
        Transform t = b.transform;
        Vector3 e = b.size * 0.5f;
        Bounds r = new Bounds(t.TransformPoint(b.center), Vector3.zero);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    r.Encapsulate(t.TransformPoint(b.center + new Vector3(sx * e.x, sy * e.y, sz * e.z)));
        return r;
    }

    // ── 씬 루트 잔여 0 (S1_Builder.ReportStrayRoots/Adopt 방식) ──

    private static HashSet<GameObject> SnapshotRoots(Transform generated)
    {
        HashSet<GameObject> set = new HashSet<GameObject>();
        foreach (Scene s in ScenesOf(generated))
            foreach (GameObject r in s.GetRootGameObjects()) set.Add(r);
        return set;
    }

    private static IEnumerable<Scene> ScenesOf(Transform generated)
    {
        Scene own = generated.gameObject.scene;
        yield return own;
        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid() && active != own) yield return active;
    }

    /// <summary>팀 생성 함수는 BuildCheckpoint 한 곳에서만 부르고 그 자리에서 그룹 아래로 옮기므로 원칙적으로 새 루트는 0이다 [C12].
    /// 그래도 생기면 그룹 아래로 옮기고(Adopt) 경고한다 —
    /// 루트에 남으면 Generated 재생성 때 지워지지 않아 중복된다.</summary>
    private static void AdoptStrayRoots(Transform generated, HashSet<GameObject> before, Transform group)
    {
        if (generated == null) return;
        List<GameObject> strays = new List<GameObject>();
        foreach (Scene s in ScenesOf(generated))
            foreach (GameObject r in s.GetRootGameObjects())
                if (!before.Contains(r)) strays.Add(r);
        foreach (GameObject r in strays)
        {
            if (group != null)
            {
                r.transform.SetParent(group, true);
                Warn($"씬 루트에 새로 생긴 '{r.name}'을 {GroupName} 아래로 옮겼다.");
            }
            else Err($"씬 루트에 남은 오브젝트 '{r.name}' — 그룹이 없어 옮기지 못했다(재생성 시 중복).");
        }
    }

    private static void Err(string msg)
    {
        errorCount++;
        Debug.LogError("[S7_Wiring] " + msg);
    }

    private static void Warn(string msg)
    {
        warningCount++;
        Debug.LogWarning("[S7_Wiring] " + msg);
    }
}
#endif
