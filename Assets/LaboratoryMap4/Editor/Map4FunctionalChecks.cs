#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 과제 PTF — 섹터 기능 검사 러너(S1·S5) + 과제 PTF-2 확장(S2·S3·S4) + 과제 PTF-3 확장(S6·S7·S8). OS 입력 없이 물리(순간이동·속도 대입)·
/// 팀 공개 API·팀 이벤트 관찰만으로 S1_설계 §9-5·§11 / S5_설계 §7 / R2 지시서 PTF-2 / R3 지시서 PTF-3 검사 항목을 판정한다.
///
/// [실행 규약 — 계약 C5·R2-C4(R3 개정)] <c>tools/run_unity.ps1 -Method Map4FunctionalChecks.Run -Tag &lt;tag&gt; [-Extra 'fcSectors=1,2,3,4,5,6,7,8']</c>
/// (-Quit 없이). 인자 <c>-fcSectors 1,2,3,4,5,6,7,8</c>(허용 1~8, 기본 1,2,3,4,5,6,7,8 — R3 R2-C4). Master 씬을 열고 플레이 모드에 들어가 Driver가
/// 검사를 돌린 뒤 <c>검증/FUNC_&lt;yyyyMMdd_HHmmss&gt;.txt</c>(UTF-8 BOM)·<c>.json</c>(UTF-8)을 새 파일로 쓰고,
/// 배치 모드면 <c>EditorApplication.Exit(code)</c>로 스스로 끝낸다(-quit을 주면 플레이 모드 전환 전에
/// 에디터가 닫힌다 — Map4PlayTestRunner 클래스 주석과 같은 이유). 씬은 저장하지 않는다.
///
/// [틀] Map4PlayTestRunner.cs:38-160·613-646 — SessionState pending 키 + [InitializeOnLoadMethod] Bootstrap +
/// playModeStateChanged → EnteredPlayMode에서 Driver 생성, RunSubStep로 하위 단계 예외 흡수, 워치독.
/// 차이: ① 워치독은 Time.realtimeSinceStartup 기준 600초(계약 C5 PTF 10분). PTF-2: 섹터 2·3·4 중 하나라도
/// 돌리면 900초(R2-C4 15분 상한 — 근거는 보고서 PTF-2 §4). PTF-3: 섹터별 예산(<c>SectorBudget</c>)의 합 + 준비 30초를
/// 600~900초로 자른다(섹터 수에 비례, 15분 상한 유지 — 근거 보고서 PTF-3 §4) ② 중첩 IEnumerator를 스택으로
/// 펼쳐 도우미 코루틴(대기·파킹)의 예외도 이 자리에서 잡는다 ③ 플레이 모드 진입 자체가 실패할 때를 대비해
/// 에디터 쪽 예비 워치독(EditorApplication.update, 워치독 + 60초)을 둔다.
///
/// [PTF-2] S2·S3·S4 오브젝트는 계약 R2-C3 이름(<c>진행/지시서/R2/_계약.md</c>)으로 대조한다. 이름을 못 찾으면 그 항목만
/// '검사불가(이름 불일치): 찾은 것/기대 이름'으로 끝내고 러너는 계속 간다. 팀 블록·도형 Transform은 실행 중에만 옮긴다.
/// 도형을 한 자리에 붙잡을 때(Hover)는 ExternallyDriven을 잠깐 세우고, 위험 요소가 맞히기 직전에 반드시 풀어 팀
/// RespawnController의 "붙잡힌 몸 거절"(IsHeld)에 걸리지 않게 한다.
/// 09-29 2회차(초안 2차 S2-D2·S3-D2·S4-D2 반영): S2 마찰 0 면 14(= 둔덕 5 + 경사판 9, 다시 −2 안 함), S3행 둔덕 방 안 자리(정보),
/// S3 BR_B1_1 z 79.5·PL_L1_01 높이 1.4(정보 — 판정은 씬 값), S4 버블 조준 레이저 북쪽 벽 윗부분(판정 17) — S4-1 출발 자리가
/// 조준 발사점을 피하고, S4-2 버블 조준 하위 시도는 버블 안에서 떠오르는 도형으로 한다.
///
/// [PTF-3] S6·S7·S8은 팀 타입 + 씬 Generated 아래 이름(스테이징 W/B 코드가 정본 — 계약 R3 R2-C3)으로만 찾는다. 스테이징 타입
/// (S6_Refs 등)은 참조하지 않는다. 타입·개수로 찾은 뒤 실제 이름을 결과 "이름 대조"에 적는다. S6-3(AntiStuck 실측 [C2])은
/// 팀 공개 창구 <c>AddComponent&lt;SpacePortal&gt;</c> → <c>color</c> → <c>PlaceAt</c>(팀 PlayerEnergyReceiver.ConfirmPlacement와 같은 순서)로
/// 포탈 한 쌍을 실행 중에만 놓고, 검사 뒤 Destroy로 없앤다([2차판정 19] 네 조건). 도형은 ExternallyDriven 없이 AddForce(속도 변화)로
/// 걸어 들어가게 한다(Lab_AntiStuck은 ExternallyDriven 몸에 개입하지 않으므로 — 붙잡으면 실측이 안 된다). S8-1은 관리자가
/// 준비(Inactive) 상태면 팀 공개 API <c>ManagerAgent.Activate</c>로 깨워 이동을 보고 <c>Activate(false)</c>·<c>ResetToStart</c>와
/// <c>PathChaserAgent.speed</c> 원래 값으로 되돌린다([2차판정 19]).
///
/// [판정 규칙] 항목마다 통과/실패/검사불가(사유). 검사불가는 실패로 치지 않되 목록화한다. exit = 실패 0
/// 이고 준비(SETUP) 성공·워치독 미발동이면 0, 아니면 1. 콘솔 마지막 줄은 정확히 한 줄
/// <c>[Map4FunctionalChecks] 결과 PASS=… FAIL=… NA=… SETUP=… WATCHDOG=… SUB_FAIL=… SUB_NA=… SUB_OPEN=… exit=… file=FUNC_….txt</c>.
/// [R1] 하위 결과(SUB_*)는 항목 판정에 합산하지 않는 부수 경로(예: S1-3 투석기 발사)의 상태다 — 판정·exit에 영향 없음,
/// 실패·검사불가가 요약에서 숨지 않게 따로 센다. SUB_OPEN = 실패·검사불가 하위 결과 목록(<c>S1-3.catapult:FAIL</c> 꼴, 없으면 none).
///
/// [오브젝트 찾기] 이름보다 팀 타입으로 찾고(섹터 씬 한정), 계약 이름(_계약.md C2·S1_Builder 이름)과 대조해
/// 결과 파일 "이름 대조"에 남긴다. 같은 타입이 여럿이라 이름으로 골라야 하는데 이름이 안 맞으면 그 항목은
/// '검사불가(이름 불일치)'로 처리하고 러너는 계속 간다.
///
/// [금지 준수] 팀 코드 수정 0 · 팀 private 리플렉션 0(공개 필드/메서드/UnityEvent·C# event만) · 씬 저장 0 ·
/// Time.timeScale/fixedDeltaTime/레이어 행렬 변경 0 · 다른 러너 호출 0. 팀 UnityEvent에 붙이는 리스너는
/// "관찰 전용"(발화 여부 기록)이며 검사가 끝나면 떼어낸다 — 기믹 동작을 바꾸지 않는다.
/// </summary>
public static partial class Map4FunctionalChecks
{
    private const string LogTag = "[Map4FunctionalChecks]";
    private const string PendingKey = "Map4FunctionalChecks_Pending_20260928";
    private const string SectorsKey = "Map4FunctionalChecks_Sectors_20260928";
    private const string SectorsNoteKey = "Map4FunctionalChecks_SectorsNote_20260928";
    private const string DeadlineKey = "Map4FunctionalChecks_Deadline_20260928";
    private const string MasterScenePath = "Assets/LaboratoryMap4/Scenes/Map4_Master.unity";
    private const float WatchdogSeconds = 600f;     // [확정] 계약 C5 — PTF 10분(PTF-3: 하한)
    private const float WatchdogSecondsExtended = 900f; // [계약 R2-C4] 15분 상한(PTF-3: WatchdogFor의 상한 — 근거 보고서 PTF-2 §4·PTF-3 §4)
    private const float EditorWatchdogGrace = 60f;  // [구현 결정] 플레이 진입 실패 대비 예비 워치독 = 워치독+60초
    private const string DefaultSectors = "1,2,3,4,5,6,7,8"; // [계약 R3 R2-C4] 허용 1~8, 기본 1,2,3,4,5,6,7,8
    private const int MaxSector = 8;                          // [계약 R3 R2-C4] -fcSectors 허용 상한
    private const float WatchdogSetupBudget = 30f;            // [추정] 준비(섹터 로드·Director 배치 대기) 20초 + 여유 10초 — PTF-2 §4 "준비 20초"

    /// <summary>PTF-3: 섹터별 최악 예산 [추정]. S1~S5 = 보고서 PTF-2 §4 표(330·60·130·230·160), S6~S8 = 보고서 PTF-3 §4 계산.</summary>
    private static float SectorBudget(int sector)
    {
        switch (sector)
        {
            case 1: return 500f; // T1 K 추가분 +90(KA) [추정] 330→420, 10-04 다리 횡단(S1-4 하위) +40 [추정] 420→460, 10-04 구·세모 횡단 표본 +40 [추정] 460→500
            case 2: return 160f; // T1 K 추가분 +100(KA) [추정] 60→160
            case 3: return 170f; // T1 K 추가분 +40(KA) [추정] 130→170
            case 4: return 330f; // T1 K 추가분 +100(KB) [추정] 230→330
            case 5: return 200f; // T1 K 추가분 +40(KB) [추정] 160→200
            case 6: return 120f; // T1 K 추가분 +45(KC) [추정] 75→120
            case 7: return 65f; // T1 K 추가분 +15(KB) [추정] 50→65
            case 8: return 55f; // T1 K 추가분 +25(KB) [추정] 30→55
            default: return 0f;
        }
    }

    /// <summary>PTF-3: 섹터 수에 비례 — Σ섹터 예산 + 준비 30초를 [600, 900]으로 자른다(R2-C4 15분 상한 유지).
    /// 1·5만이면 520 → 600(1차·PTF-2와 같음), 1~5면 940 → 900(PTF-2와 같음), 1~8이면 1095 → 900(상한 — 보고서 PTF-3 §4).
    /// T1 K 추가분(+455: KA 230·KB 180·KC 45) 반영 뒤: 1·5만이면 650, 1~5면 1310 → 900(상한), 1~8이면 1550 → 900(상한). 1~8·1~5는 상한이라 워치독 불변, 일부 섹터만 돌릴 때만 값이 커진다.</summary>
    private static float WatchdogFor(string sectors)
    {
        float sum = WatchdogSetupBudget;
        foreach (string p in (sectors ?? "").Split(','))
            if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) sum += SectorBudget(n);
        return Mathf.Clamp(sum, WatchdogSeconds, WatchdogSecondsExtended);
    }

    // ───────────────────────── 설계 수치(섹터 로컬, +Z 진행, 바닥 윗면 y0) ─────────────────────────
    // S1 — S1_설계.md §11-1 [확정 재설계 09-28] / 지시서 S1-B 수치표
    private static readonly Vector2 S1SafePreXZ = new Vector2(-26f, 116f); // §11-1 SAFE_PRE (−26,116)
    private const float S1CliffEndZ = 168f;                                 // §11-1 절벽 z140~168(28m) → 도착 z168~
    // [10-04 사용자 결정 · 설계/S1_설계.md §12] 카트 2대(레일 x −6/0) + 팀 무지개다리(협동). 값은 S1_Builder.cs BridgeX·BridgePadZ와 같다.
    private const int S1ExpectedCarts = 2;
    private const float S1BridgeX = 6f;     // 다리 중심 x(폭 4 = x 4~8) — 비게 된 x +6 레인
    private const float S1BridgeZ0 = 140f;  // 다리 시작 = 승차장 바닥 끝
    private const float S1BridgeZ1 = 168f;  // 다리 끝 = 도착 플랫폼 시작(= S1CliffEndZ)
    // [구현 결정] 절벽 낙하 시험점: 절벽 구간 가운데(z154·146), 레일 x −6/0과 카트 받침 구역(±10)·다리(x 4~8, 닫힘 상태라 콜라이더 꺼짐)에서 떨어진 x ±20·24.
    private static readonly Vector3[] S1CliffDropLocal =
    {
        new Vector3(20f, -1.5f, 154f), new Vector3(-20f, -1.5f, 154f), new Vector3(24f, -1.5f, 146f)
    };
    // [제안 — R2 지적] S1-3 투석기 당김 훑기 값(0.3~1.0, 0.1 간격). 팀 기본 노치 10개(0.1 단위)와 맞는다.
    private static readonly float[] CatapultPulls = { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };
    // [확정] S1_설계 §11-1 입구·스폰 바닥 윗면 = 섹터 로컬 y0 / S5_설계 §2 플랫폼 윗면 y0. R2: 복귀 발밑 지지면 기준.
    private const float CheckpointFloorLocalY = 0f;
    private const float FloorTolY = 0.2f;      // [R2 지적] 발밑 지지면 로컬 y 허용 ±0.2
    private const float FootGapMax = 0.3f;     // [R2 지적] 도형 밑면-지지면 간격 허용(발바닥 오프셋) ≤0.3
    // [구현 결정] 도착 플랫폼 파킹점(TeamExitZone을 못 찾을 때만 씀) — 플랫폼 폭 30(x ±15), z168~188 안,
    // 도착점(−3,176)[10-04 M-A 보정]·레일 x −6/0·다리 발판(6,170)에서 8U 이상.
    private static readonly Vector3[] S1ParkFallbackLocal =
    {
        new Vector3(-12f, 0f, 184f), new Vector3(-9f, 0f, 184f), new Vector3(12f, 0f, 184f)
    };

    // S5 — S5_설계.md §2 [확정 09-28]
    private static readonly Rect S5PlatformA = Rect.MinMaxRect(-30f, 0f, 4f, 16f);   // A 시작 x −30~4, z 0~16
    // [구현 결정] 파킹 후보: F 도착 플랫폼(x −4~30, z 76~136) 안, 다리3·레이저·발사구 사선(x −22~−4)에서 12m 이상.
    private static readonly Vector3[] S5ParkCandidatesLocal =
    {
        new Vector3(10f, 0f, 120f), new Vector3(14f, 0f, 120f), new Vector3(18f, 0f, 120f),
        new Vector3(10f, 0f, 126f), new Vector3(14f, 0f, 126f), new Vector3(18f, 0f, 126f),
        new Vector3(-26f, 0f, 3f), new Vector3(-22f, 0f, 3f), new Vector3(-18f, 0f, 3f)   // 폴백: A 먼 구석
    };
    // [구현 결정] 허공 낙하 시험점: 플랫폼·다리 사이 허공(다리2 중심선 (26,20)→(−26,76)에서 수평 15m 이상).
    private static readonly Vector3[] S5VoidDropLocal =
    {
        new Vector3(-15f, 1f, 40f), new Vector3(-20f, 1f, 30f), new Vector3(-10f, 1f, 55f), new Vector3(15f, 1f, 60f)
    };

    // ── PTF-2: S2 격리실 — 초안 S2 §1-2·§1-4·§1-5·§2-2·§2-5 [초안], 판정 1·2 [판정], 지시서 PTF-2 표 ──
    private static readonly Vector3 S2ExitDriveStartLocal = new Vector3(0f, 0f, 78f);  // [지시서] 방 안 (0,바닥,78)
    private const float S2ExitReachZ = 86f;                                             // [지시서] 연결 통로 z ≥ 86
    private static readonly Vector3 S2OpeningMin = new Vector3(-4f, 0.35f, 83.5f);    // [지시서·판정 1] 개구 x −4~4 · y 0.35~3.5 · z 83.5~84.5
    private static readonly Vector3 S2OpeningMax = new Vector3(4f, 3.5f, 84.5f);
    private static readonly Vector3 S2ExitMarkerLocal = new Vector3(0f, 0f, 84f);       // [판정 1·계약 C2-2] TEMP_S2_ExitS3_Open (0,0,84)
    private static readonly Vector3 S2ConnectorProbeLocal = new Vector3(0f, 0f, 88f);   // [구현 결정] 연결 통로(z84~96) 바닥 확인점
    // [제안 초안 S2 2차 :178, 컨트롤타워 확인 대기] S3행 둔덕 GEO_S2_Hump_S3_Room — 방 안 x −6~6 · z 80~84 · 능선 z 82 · 높이 0.3 · 북쪽 발끝 z 84 높이 0.
    // 정보 대조만(판정 아님). S2-1 주행(0,78 → z ≥ 86)은 이 둔덕(8.53°, 마찰 0 — 입력 중에는 걷는다 [초안 §1-5])을 넘어간다.
    private static readonly Bounds S2HumpS3RoomExpect = new Bounds(new Vector3(0f, 0.15f, 82f), new Vector3(12f, 0.3f, 4f));
    private const float S2IsoLineX = -14f;                                              // [초안 §0·§2-2] 숨은 벽 선·둔덕 능선 x −14
    private const float S2HumpHalf = 2f, S2HumpHeight = 0.3f;                          // [초안 §2-2] 능선→발끝 2.0, 높이 0.3
    private const float S2HumpTestOffset = 0.6f;                                        // [지시서] 능선에서 방 쪽 +0.6(정중앙은 불안정 평형이라 안 씀)
    private const float S2HumpTestZ = 42f;                                              // [구현 결정] 둔덕 z 30.5~53.5 가운데
    private const float S2HumpObserve = 5f;                                             // [지시서] 5초 관찰
    private static readonly Vector3 S2IsoDriveStartLocal = new Vector3(-10f, 0f, 42f);  // [지시서] 방 (−10,바닥,42)
    private const float S2IsoReachX = -20f;                                             // [지시서] 격리 공간 x ≤ −20
    private static readonly Bounds S2HiddenWallExpect = new Bounds(new Vector3(-14f, 6.3f, 42f), new Vector3(0.4f, 4f, 23f)); // [초안 §1-2·판정 2] y 4.3~8.3, z 30.5~53.5
    // [초안 §2-5] S2_Block_Scatter_00~14 중심(x,z), y 0.5. 순서 = S2_check.py SCATTER 목록 순서.
    private static readonly Vector2[] S2ScatterXZ =
    {
        new Vector2(-9f, 6f), new Vector2(9f, 4f), new Vector2(-3f, 13f), new Vector2(11f, 13f), new Vector2(-16f, 20f),
        new Vector2(18f, 19f), new Vector2(-2f, 30f), new Vector2(9f, 36f), new Vector2(-5f, 62f), new Vector2(10f, 66f),
        new Vector2(19f, 58f), new Vector2(-13f, 74f), new Vector2(-26f, 35f), new Vector2(-24f, 49f), new Vector2(-20f, 42f)
    };
    // [구현 결정] S2 파킹 후보(방 동쪽 바닥 y0) — 두 주행 경로(x0 z78→86, z42 x−10→−20)에서 15U 이상. 바닥·빈자리를 실행 중에 확인해 3곳을 고른다.
    private static readonly Vector3[] S2ParkLocal =
    {
        new Vector3(22f, 0f, 24f), new Vector3(26f, 0f, 24f), new Vector3(30f, 0f, 24f),
        new Vector3(22f, 0f, 30f), new Vector3(26f, 0f, 30f), new Vector3(30f, 0f, 30f),
        new Vector3(22f, 0f, 52f), new Vector3(26f, 0f, 52f), new Vector3(30f, 0f, 52f)
    };

    // ── PTF-2: S3 점프 — 초안 S3 §1-2·§1-3·§1-5·§2·§3·§5 [초안], 설계 S3 §2-2 [확정] ──
    private const float S3LauncherPeriod = 3.0f, S3LauncherPeriodTol = 0.3f;           // [확정 S3 §2-2] 예고 1 + 휴식 2, 허용 ±0.3 [지시서]
    private const float S3LauncherObserve = 10.5f;                                      // [지시서] ≥ 10초 관찰
    private const float S3SavePadTop = 2.4f;                                            // [초안 §1-3] 세이브 발판 윗면 2.4
    private const float S3RespawnWindow = 12f;                                          // [지시서] ≥ 8초(낙하 0.78~1.12 + 장외 3 [계산 §1-5]) — 여유 포함 12초
    private const float S3BridgeSag = 0.56f, S3BridgeSagTol = 0.3f;                     // [계산 초안 §5] 네모 가운데 처짐 0.56, 허용 0.3 (추정)
    private static readonly Vector3 S3L1_01Expect = new Vector3(-10f, 1.4f, 9.6f);      // [초안 S3 2차 :316] PL_L1_01 피벗(높이 1.4 = 틈 양쪽 높은 윗면 0.8 + 0.6) — 정보 대조만
    // [구현 결정] 앞 구간 허공 낙하 시험점(레인 사이 허공, 세이브 경계 z 76.375 앞). 실행 중에 아래 4U·반경 0.7 빈 곳만 쓴다.
    private static readonly Vector3[] S3VoidDropLocal =
    {
        new Vector3(15f, 1f, 11f), new Vector3(20f, 2f, 22.75f), new Vector3(-20f, 2f, 22.75f), new Vector3(12f, 3f, 35.25f), new Vector3(-12f, 3f, 35.25f)
    };
    // [초안 §1-4] 뒤 구간 B2(윗면 1.6)·B4(0.8) 발판 — 발사구 없음, 세이브 구역(z77~82) 밖. y = 기대 발판 윗면.
    private static readonly Vector3[] S3ParkLocal =
    {
        new Vector3(-7f, 1.6f, 93f), new Vector3(7f, 1.6f, 93f), new Vector3(-21f, 1.6f, 93f), new Vector3(21f, 1.6f, 93f),
        new Vector3(-7f, 0.8f, 120f), new Vector3(7f, 0.8f, 120f), new Vector3(-21f, 0.8f, 120f), new Vector3(21f, 0.8f, 120f)
    };

    // ── PTF-2: S4 반중력·보안 — 설계 S4 §3-1·3-5·3-7·3-8 [확정], 초안 S4 §2·§3·§4·§6-1 [초안], 판정 13·18 ──
    private static readonly Bounds S4BubbleExpect = new Bounds(new Vector3(0f, 13f, 62f), new Vector3(8f, 10f, 8f)); // [판정 18 ②] min(−4,8,58) max(4,18,66)
    private const float S4BubbleReachFootY = 17.5f;                                     // [지시서] 18 부근, 허용 0.5 (추정)
    private const float S4BubbleWindow = 10f;                                           // [지시서] 창 10초
    private const float S4RockTop = 8f;                                                 // [초안 §2 ④] 돌 윗면 8
    private static readonly Vector2 S4CylAxisXZ = new Vector2(0f, 62f);                 // [초안 §2] 원통 축
    private const float S4StairOuterR = 15f;                                            // [초안 §2 ③] 계단 r 10~15
    // [판정 17, 초안 S4 2차 §4-3 :115-117] 버블 조준 레이저 발사점 — 북쪽 벽 남쪽 면 윗부분. 이름 순서(Cube·Sphere·Tetra)와 같게. 정보 대조만(판정은 씬 위치).
    private static readonly string[] S4AimBubbleNames = { "S4_AL_Bubble_Cube", "S4_AL_Bubble_Sphere", "S4_AL_Bubble_Tetra" };
    private static readonly Vector3[] S4AimBubbleExpect = { new Vector3(0f, 17.5f, 65.95f), new Vector3(-2f, 17.5f, 65.95f), new Vector3(2f, 17.5f, 65.95f) };
    // [구현 결정] S4-1 출발 자리 점수에서 버블 조준 레이저 발사점까지 수평 거리를 이만큼 깎아 넣는다 — 판정 17로 조준 레이저가 출발 자리를 덮으므로,
    // 발사점 바로 아래(스냅샷 광선이 상승 기둥과 거의 나란함 → 명중)를 피해 상승 시간 측정이 간섭받지 않게 한다.
    private const float S4AimSpotMargin = 3f;
    // [구현 결정] S4-2 버블 조준 하위 시도: 발사점 바로 아래 버블 안에서 떠오르게 할 자리 조건 — 트리거 안쪽 여유, 낙석 레인·고정 광선까지 수평 여유.
    private const float S4AimRiseInner = 0.6f, S4AimRiseLaneClear = 1.5f, S4AimRiseBeamClear = 1.2f;
    // [구현 결정] S4-4 계단 가장자리 시험 각(φ°, 축 (0,62)에서 +X→+Z). R3(φ495~570, 5.5→8)을 먼저 — 고정 레이저·낙석 레인이 없는 쪽. 마지막 300은 R1.
    private static readonly float[] S4FallPhiDeg = { 520f, 545f, 505f, 300f };
    private const float S4FallObserve = 5f;                                             // [지시서] 5초 동안 순간이동 없음
    // [초안 §2 ①] 입구 통로(x −4~4, z 0~27.5, 바닥 0) — 조준 레이저 록온 0인 대기 장소. CP_S4_Start 구역(z0~6) 밖.
    private static readonly Vector3[] S4ParkLocal =
    {
        new Vector3(-2f, 0f, 12f), new Vector3(2f, 0f, 15f), new Vector3(-2f, 0f, 18f), new Vector3(2f, 0f, 21f), new Vector3(0f, 0f, 24f)
    };

    // [구현 결정] 순간이동 높이 — Map4SceneBuilder 마커 Spawn_0~2의 y 0.6과 같게(바닥 윗면 + 0.6).
    private const float SpawnLift = 0.6f;

    // ── PTF-3: S6 포탈 — 계약 R3 R2-C3 :386-389, 스테이징 S6_Builder.cs:66-69·:190-191, S6_Wiring.cs:11-20 [제안], 판정 C1 (가)·C2 ──
    private static readonly Vector3 S6BallOrangeLocal = new Vector3(-3f, 1f, 10f);     // [제안 S6_Builder.cs:190] ANCH_S6_Ball_Orange — 주황(입구) 볼 중심
    private static readonly Vector3 S6BallBlueLocal = new Vector3(3f, 1f, 10f);        // [제안 S6_Builder.cs:191] ANCH_S6_Ball_Blue — 파랑(출구) 볼 중심
    private const float S6BallTol = 0.05f;                                              // [지시서 PTF-3 §3] ±0.05 (y는 팀 bobHeight 흔들림을 더한다 — EnergyBall.cs:68-69)
    private const int S6FixedPanelMin = 4, S6MovableExpect = 2, S6LeverExpect = 2, S6BallExpect = 2, S6ZoneExpect = 3; // [계약 R3 R2-C3 :386-389 · 판정 4]
    private const float S6WalkStartBeyond = 2.5f;   // [구현 결정] 걸어 들어가기 출발점 = 패널 가장자리 바깥 2.5(구덩이 디딤 1.5 [확정 결정5] + 1.0 — 둘레 바닥 윗면에서 출발)
    private const float S6WalkTimeout = 6f;         // [구현 결정] 출발 → 순간이동 창(런타임 도형 moveSpeed = 도형 프로필 적용값, 네모 실측 3.5U/s [팀 CubeStats.asset:16]; 기본값 5 [팀 PlayerMover.cs:65]는 폴백. 3.5U/s로 2.5U+패널 반폭 ≈ 1초 안팎, 실측 +0.89s)
    private const float S6StandTimeout = 4f;        // [구현 결정] 선 채로 빠지기 창(면 위 0.6에서 낙하 ≈ 0.35초)
    private const float S6AfterTeleport = 1.5f;     // [구현 결정] 순간이동 뒤 더 보는 시간(출구 잠금 0.25s [팀 SpacePortal.cs:65] + 착지)
    private const float S6StandEdgeInset = 0.2f;    // [구현 결정] 선 채로 빠지기 자리 = 포탈 가장자리에서 안쪽 0.2(몸 반폭 0.5 → 트리거와 겹침)
    private const float S6TeleportJump = 2.5f;      // [구현 결정] 한 물리 스텝 위치 변화가 이보다 크면 순간이동으로 본다(걷기 5U/s × 0.02s = 0.1)
    // [구현 결정] S6 파킹 후보(방 동쪽 바닥 y0 — x −8.5~32 바닥 조각 GEO_S6_Floor_Room_7 [제안 S6_Builder.cs:120]) — P1(−10,24)·P3(−26,41.5)·볼(±3,10)에서 20U 이상.
    private static readonly Vector3[] S6ParkLocal =
    {
        new Vector3(24f, 0f, 20f), new Vector3(28f, 0f, 20f), new Vector3(24f, 0f, 26f),
        new Vector3(28f, 0f, 26f), new Vector3(24f, 0f, 32f), new Vector3(28f, 0f, 32f)
    };

    // ── PTF-3: S7 서버실 — 스테이징 S7_Builder.cs:47-71·:105-126·:169-170·:193, S7_Wiring.cs:81-118·:177·:213, 판정 13·17-S7·C9 ──
    private static readonly Vector3 S7EntryMarkerLocal = new Vector3(0f, 0f, 14.25f);   // [제안 S7_Builder.cs:169] TEMP_S7_EntryDoor_Open
    private static readonly Vector3 S7ExitMarkerLocal = new Vector3(0f, 0f, 109.75f);   // [제안 S7_Builder.cs:170] TEMP_S7_ExitDoor_Open
    // 개구 상자(섹터 로컬): x −4~4 [계약 K0-2] · 높이 입구 6·출구 4 [제안 S7_Builder.cs:58·71] · z = 칸막이 14~14.5 / 북벽 109.5~110 [제안 :55-56·:70]
    private static readonly Vector3 S7EntryOpenMin = new Vector3(-4f, 0f, 14f), S7EntryOpenMax = new Vector3(4f, 6f, 14.5f);
    private static readonly Vector3 S7ExitOpenMin = new Vector3(-4f, 0f, 109.5f), S7ExitOpenMax = new Vector3(4f, 4f, 110f);
    private static readonly Vector3 S7EntryDriveStart = new Vector3(0f, 0f, 10f);      // [구현 결정] 전실 안(칸막이 z14 앞 4, 수동 R 구역 z1~7 밖)
    private const float S7EntryReachZ = 18f;                                            // [구현 결정] 칸막이 뒷면 z14.5 + 3.5(전력 콘솔 z23.5 앞)
    private static readonly Vector3 S7ExitDriveStart = new Vector3(0f, 0f, 105f);      // [구현 결정] 북벽 z109.5 앞 4.5
    private const float S7ExitReachZ = 112f;                                            // [구현 결정] 섹터 끝 z110 + 2(공용 연결 통로 z110~122 [Map4SceneBuilder 결정2])
    private const float S7ExitReachNoConnector = 110.6f;                               // [구현 결정] 연결 통로 바닥이 없을 때: 몸(반폭 0.5)이 북벽 바깥 면 z110을 다 지남
    private static readonly Vector3 S7ConnectorProbe = new Vector3(0f, 0f, 114f);      // [구현 결정] 연결 통로 바닥 확인점
    private static readonly string[] S7PortSuffix = { "OUT_A", "OUT_B", "OUT_C", "IN_1", "IN_2", "IN_3" }; // [제안 S7_Wiring.cs:118] 포트 이름 접미사 순서
    private static readonly int[] S7PortServerRef = { 0, 2, 4, 1, 3, 5 };               // [사용자 09-29 나·안C · S7_Builder.cs:126] 정보 대조만
    private const float S7PortFaceTol = 0.5f;                                           // [제안 S7_Wiring.cs:213 PortToServerMax] 포트 ↔ 서버 면 거리 허용
    private const float S7RoleMinDist = 2f;                                             // [제안 §3-6 · S7_Builder.cs:193] 컴퓨터↔책 ≥ 2
    // [구현 결정] S7 파킹 후보(서버실 동쪽 뒤 바닥 y0 — 서버 뒷줄 z44~46·콘솔 z23.5~24.5·주행 경로 x 0에서 20U 이상)
    private static readonly Vector3[] S7ParkLocal =
    {
        new Vector3(30f, 0f, 80f), new Vector3(34f, 0f, 80f), new Vector3(26f, 0f, 80f),
        new Vector3(30f, 0f, 86f), new Vector3(34f, 0f, 86f), new Vector3(26f, 0f, 86f)
    };

    // ── PTF-3: S8 약물 조합 — 스테이징 S8_Wiring.cs:50-72·:362-421·:497-528, S8_Builder.cs:37·:119·:156-157, 판정 18·19 ──
    private const float S8Length = 144f;             // [판정 18 · 계약 R3 :410]
    private const float S8LengthTol = 0.01f;         // [계약 R3 R2-C4 PTC ±0.01]
    private const float S8ExitMarkerY = 0.1f;        // [계약 K0-2] rise 0(바닥·출구 높이 34 [S8_Builder.cs:38-39]) + 0.1 — 정보
    private const float S8PatrolObserve = 8f;        // (추정) 10초 이내 [지시서 PTF-3 §3] — 팀 patrolSpeed 2 [ManagerAgent.cs:55]면 약 16U
    private const float S8PatrolMinMove = 0.5f;      // (추정) [지시서 PTF-3 §3]
    private const float S8DoorObserve = 3f;          // [구현 결정] 시작 뒤 문 닫힘 유지 관찰
    private const float S8KeyTol = 0.05f;            // [구현 결정] ManagerKeyPoint ↔ ANCH_S8_Key(W가 앵커 자리 그대로 둔다 — S8_Wiring.cs:481-497)
    private const float S8ExitDoorZ = 131.75f;       // [제안 S8_Builder.cs:13] 출구 문 z131.55~131.95 가운데 — 출구 doorPhysics 고르기

    private static readonly string[] AllIds =
    {
        "S1-1", "S1-2", "S1-3", "S1-4", "S1-5", "S1-6", "S1-7", "S1-8", "S1-9",
        "S2-1", "S2-2", "S2-3", "S2-4", "S2-5",
        "S3-1", "S3-2", "S3-3", "S3-4", "S3-5",
        "S4-1", "S4-2", "S4-3", "S4-4", "S4-5", "S4-6",
        "S5-1", "S5-2", "S5-3", "S5-4", "S5-5",
        "S6-1", "S6-2", "S6-3", "S6-4",
        "S7-1", "S7-2", "S7-3", "S7-4", "S7-5",
        "S8-1", "S8-2", "S8-3", "S8-4", "S8-5",
        "G-1"   // 섹터 밖 전역 항목(KA) — "S"로 시작하지 않아 -fcSectors 제외 처리에 안 걸리고 항상 돈다
    };

    // ───────────────────────── 진입점 ─────────────────────────

    [MenuItem("Tools/Laboratory Map4/Run Functional Checks (PTF S1~S8)")]
    public static void Run()
    {
        string sectors = DefaultSectors;
        string argNote = "";
        try
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning($"{LogTag} 이미 플레이 모드다 — 먼저 정지해라.");
                if (Application.isBatchMode) WriteFallbackAndExit(sectors, argNote, "이미 플레이 모드", false);
                return;
            }
            sectors = ParseSectorsArg(out argNote);
            if (!Map4SceneGuard.CanReplaceCurrentScenes())
            {
                Debug.LogWarning($"{LogTag} 사용자가 저장을 취소해 검사를 중단한다.");
                return;
            }

            EditorSceneManager.OpenScene(MasterScenePath, OpenSceneMode.Single);
            SessionState.SetString(SectorsKey, sectors);
            SessionState.SetString(SectorsNoteKey, argNote);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetFloat(DeadlineKey, Time.realtimeSinceStartup + WatchdogFor(sectors) + EditorWatchdogGrace);
            Debug.Log($"{LogTag} 시작 — 섹터 {sectors} {argNote} (Master 씬 열고 플레이 모드 진입)");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            SessionState.SetBool(PendingKey, false);
            WriteFallbackAndExit(sectors, argNote, "Run 예외: " + e, false);
        }
    }

    /// <summary>-fcSectors 1,2,3,4,5,6,7,8 (쉼표·세미콜론·공백 구분). 지원 섹터 1~8(R3 R2-C4) — 나머지는 무시하고 메모.</summary>
    private static string ParseSectorsArg(out string note)
    {
        note = "";
        string raw = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "-fcSectors", StringComparison.OrdinalIgnoreCase)) raw = args[i + 1];
        if (string.IsNullOrEmpty(raw)) { note = $"(-fcSectors 없음 → 기본 {DefaultSectors})"; return DefaultSectors; }

        List<int> ok = new List<int>();
        List<string> ignored = new List<string>();
        foreach (string tok in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(tok.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= MaxSector)
            {
                if (!ok.Contains(n)) ok.Add(n);
            }
            else ignored.Add(tok);
        }
        if (ignored.Count > 0) note = $"(-fcSectors '{raw}' 중 지원 안 함: {string.Join(",", ignored)} — 1~{MaxSector}만 지원)";
        if (ok.Count == 0) { note += $" (유효 섹터 없음 → 기본 {DefaultSectors})"; return DefaultSectors; }
        ok.Sort();
        return string.Join(",", ok);
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= EditorWatchdogTick;
        EditorApplication.update += EditorWatchdogTick;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);

        if (Object.FindObjectOfType<Driver>() != null) return;
        Driver d = new GameObject("Map4FunctionalChecks_RUNTIME").AddComponent<Driver>();
        d.Configure(SessionState.GetString(SectorsKey, DefaultSectors), SessionState.GetString(SectorsNoteKey, ""));
        Debug.Log($"{LogTag} 플레이 모드 진입 — 러너 생성.");
    }

    /// <summary>예비 워치독 — Driver가 생기지 못했거나(플레이 진입 실패) Driver 워치독까지 멈춘 경우에도
    /// 결과 파일과 종료 코드가 반드시 남게 한다. Driver가 정상 종료하면 키를 지워 꺼진다.</summary>
    private static void EditorWatchdogTick()
    {
        float deadline = SessionState.GetFloat(DeadlineKey, 0f);
        if (deadline <= 0f || Time.realtimeSinceStartup < deadline) return;
        SessionState.EraseFloat(DeadlineKey);
        WriteFallbackAndExit(SessionState.GetString(SectorsKey, DefaultSectors), SessionState.GetString(SectorsNoteKey, ""),
            "에디터 예비 워치독 — 제한 시간 안에 러너가 결과를 쓰지 못했다(플레이 모드 진입 실패 가능)", true);
    }

    private static void WriteFallbackAndExit(string sectors, string argNote, string why, bool watchdog)
    {
        SessionState.EraseFloat(DeadlineKey);
        Report r = new Report(sectors, argNote) { SetupError = why, Watchdog = watchdog };
        foreach (CheckItem it in r.Items) it.Set(Verdict.NA, "러너가 실행되지 못함: " + why);
        string line = r.WriteAndSummarize();
        Debug.Log(line);
        if (Application.isBatchMode) EditorApplication.Exit(r.ExitCode);
        else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    private static string ResolveOutDir()
    {
        // Map4PlayTestRunner.ResolveOutDir(:642)와 같은 해석 — <맵4_완성>/검증
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string mapRoot = Directory.GetParent(projectRoot).FullName;
        return Path.Combine(mapRoot, "검증");
    }

    // ───────────────────────── 결과 모델 ─────────────────────────

    private enum Verdict { NA, Pass, Fail }

    private sealed class CheckItem
    {
        public readonly string Id;
        public readonly int Sector;
        public readonly string Title;
        public readonly string Basis;
        public Verdict Verdict = Verdict.NA;
        public string Reason = "미실행";
        public bool Decided;
        public readonly List<string> Details = new List<string>();
        /// <summary>항목 판정에 합산하지 않는 하위 경로 결과(R1: 예 — S1-3 투석기 경로). 판정 줄·요약·JSON에 따로 드러낸다.</summary>
        public readonly List<SubResult> Subs = new List<SubResult>();

        public CheckItem(string id, int sector, string title, string basis)
        {
            Id = id; Sector = sector; Title = title; Basis = basis;
        }

        public void Set(Verdict v, string reason)
        {
            if (Decided) Details.Add($"(이전 판정 {Label(Verdict)}: {Reason})");
            Verdict = v; Reason = reason; Decided = true;
        }

        public void SetSub(string key, string name, SubStatus status, string reason)
        {
            SubResult s = Subs.FirstOrDefault(x => x.Key == key);
            if (s == null) { s = new SubResult { Key = key, Name = name }; Subs.Add(s); }
            s.Status = status; s.Reason = reason;
        }

        /// <summary>판정 줄 꼬리표 — 예: " [투석기 경로: 실패 · 대조군: 통과]". 하위 결과가 없으면 빈 문자열.</summary>
        public string SubTag => Subs.Count == 0 ? "" : " [" + string.Join(" · ", Subs.Select(s => $"{s.Name}: {SubLabel(s.Status)}")) + "]";
    }

    private static string Label(Verdict v) => v == Verdict.Pass ? "통과" : v == Verdict.Fail ? "실패" : "검사불가";
    private static string Code(Verdict v) => v == Verdict.Pass ? "PASS" : v == Verdict.Fail ? "FAIL" : "NA";

    /// <summary>하위 경로 상태. Skipped = 앞 경로가 이미 결론을 내서 돌리지 않음.</summary>
    private enum SubStatus { Pass, Fail, NA, Skipped }

    private sealed class SubResult
    {
        public string Key;   // 요약 줄용 ASCII 키(공백 없음)
        public string Name;  // 사람용 이름
        public SubStatus Status = SubStatus.NA;
        public string Reason = "";
    }

    private static string SubLabel(SubStatus s) =>
        s == SubStatus.Pass ? "통과" : s == SubStatus.Fail ? "실패" : s == SubStatus.NA ? "검사불가" : "미실행";
    private static string SubCode(SubStatus s) =>
        s == SubStatus.Pass ? "PASS" : s == SubStatus.Fail ? "FAIL" : s == SubStatus.NA ? "NA" : "SKIP";

    private sealed class NameCheck
    {
        public string What, Expected, Actual;
        public bool Ok;
    }

    private sealed class Report
    {
        public readonly List<CheckItem> Items = new List<CheckItem>();
        public readonly List<string> Notes = new List<string>();
        public readonly List<NameCheck> NameChecks = new List<NameCheck>();
        public readonly string Sectors;
        public readonly string ArgNote;
        public readonly DateTime Started = DateTime.Now;
        public string SetupError;
        public bool Watchdog;

        public Report(string sectors, string argNote)
        {
            Sectors = sectors; ArgNote = argNote ?? "";
            Items.Add(new CheckItem("S1-1", 1, "도형 배치 후 추격자 위치 변화(움직이기 시작)", "S1_설계 §9-5"));
            Items.Add(new CheckItem("S1-2", 1, "추격자 잡기 구역 → 그 도형만 SAFE_PRE(−26,116) 복귀(벽 파괴 전), 다른 도형 위치 유지", "S1_설계 §1·§11-1"));
            // R1: 판정은 "충돌 속도 ≥5로 파괴"(투석기 결과 재현). 투석기 공개 API 발사는 판정에 합산하지 않는 하위 결과로 따로 표기.
            Items.Add(new CheckItem("S1-3", 1, "파괴벽에 네모 충돌 속도 ≥5 → 파괴(투석기 발사 경로는 하위 결과로 따로)", "S1_설계 §2 [팀]"));
            // [10-04 사용자 결정] 카트 3대 → 2대 + 팀 무지개다리. S1-4 판정 = 카트 2대 도착 그리고 다리 개방·네모·구·세모 도보 횡단·발판 해제 시 소멸 모두 통과
            // (하위 결과 5개 — 개방·네모/구/세모 도보 횡단·소멸 — 로도 표기, 하나라도 실패면 실패, 실측 불가면 검사불가: S1_4b_BridgeCross → S1_4_PromoteVerdict).
            Items.Add(new CheckItem("S1-4", 1, "카트 2대가 절벽 28m를 건너 도착(태엽 축 공개 API 구동) 그리고 무지개다리 개방·네모·구·세모 도보 횡단·발판 해제 시 소멸이 모두 통과", "S1_설계 §9-5 · §12(10-04)"));
            Items.Add(new CheckItem("S1-5", 1, "카트 2대 + 전원 도착 → 출구 문 열림", "S1_설계 §9 · §12(10-04)"));
            Items.Add(new CheckItem("S1-6", 1, "절벽 낙하 → 공용 체크포인트 복귀", "S1_설계 §9"));
            // T1 추가 기능 검사 A (팀 abad97e 반영분) — 재검증지시 §2 4단계
            Items.Add(new CheckItem("S1-7", 1, "구가 조향석에 도킹 → 해제 뒤 크기 상태(localScale·CurrentState)가 도킹 전과 같음 [K2]", "재검증지시 K2, CatapultSteerHandle.cs:326-384 [팀 abad97e]"));
            Items.Add(new CheckItem("S1-8", 1, "추격자 잡기 구역 윗면+2에서 놓은 도형도 잡혀 SAFE_PRE 복귀(구역 안 0.25초 재판정), 다른 도형 유지 [K5]", "재검증지시 K5, PathChaserCatchZone.cs:36-52 [팀 e819891]"));
            Items.Add(new CheckItem("S1-9", 1, "투석기 당김 줄: 세모 연결 뒤 앵커 수평 12 밖으로 못 감 + 필요한 자리·최대 장전이 12 안 [K3①]", "재검증지시 K3, CatapultLoadController.cs:96·:167-192 [팀 abad97e]"));
            // PTF-2 (R2 지시서 PTF-2 검사 항목 표)
            Items.Add(new CheckItem("S2-1", 2, "S3행 개구(TEMP, 문 없음)로 걸어 나갈 수 있음", "판정 1, 계약 R2-C2-2·C3"));
            Items.Add(new CheckItem("S2-2", 2, "딱딱블록 흩어짐(39개) + 금지 구역 둔덕 위 블록 1개가 미끄러져 내려옴", "판정 3·4, 설계 S2 §4-13, 초안 §1-5·§2-5"));
            Items.Add(new CheckItem("S2-3", 2, "숨은 벽이 보관 자리(천장 위)에 있고 방 안 통행을 막지 않음", "판정 2, 계약 R2-C2-2"));
            Items.Add(new CheckItem("S2-4", 2, "격리 흐름(갇힘·탈출·출구 열림) — 팀 부품 없음, 존재·배선만 기록", "계약 R2-C3 끝"));
            Items.Add(new CheckItem("S2-5", 2, "S2 레버 3개 각각 당김 → 당김 상태 확인 → 놓은 뒤 복귀 [K10]", "재검증지시 K10, LeverHead.cs [팀 abad97e]"));
            Items.Add(new CheckItem("S3-1", 3, "측면 발사구 14개 주기 발사(간격 3.0±0.3초)", "설계 S3 §2-2 [확정]"));
            Items.Add(new CheckItem("S3-2", 3, "허공 낙하 → 세이브 전 CP_S3_Start / 세이브 뒤 CP_S3_Save(공용 체크포인트 규칙)", "설계 S3 §2 1·11 보완, 초안 §1-5, 사용자 결정 12 (가)"));
            Items.Add(new CheckItem("S3-3", 3, "발사구 탄 피격 → SSP_S3_Start 복귀", "초안 §3, 계약 R2-C1-5"));
            Items.Add(new CheckItem("S3-4", 3, "줄다리 BR_B1_1 위에 네모 → 지지(처짐 0.56 + 허용 0.3)", "판정 9, 초안 §5"));
            Items.Add(new CheckItem("S3-5", 3, "세 도형이 꿈의 실타래에 매달림(Hanging)·매달린 채 위치 변화·해제 [K7①]", "재검증지시 K7, DreamThreadController.cs:151-204 [팀 abad97e]"));
            Items.Add(new CheckItem("S4-1", 4, "버블 트리거에 도형 3종 → 몸 밑면 y ≥ 17.5 도달, 네모가 가장 느림", "설계 S4 §3-1-2·3, 판정 18 ②"));
            Items.Add(new CheckItem("S4-2", 4, "레이저 피격 → 계단은 S4_SP_Stairs, 버블은 S4_SP_Bubble(조준 레이저는 하위 결과)", "설계 S4 §3-5·7"));
            Items.Add(new CheckItem("S4-3", 4, "낙석 2회 → 위치별 안전점(A안: 레이저 복귀 뒤에도 낙석 횟수 유지)", "판정 13 A안, 설계 S4 §3-5"));
            Items.Add(new CheckItem("S4-4", 4, "계단에서 떨어짐 → 복귀 없이 원통 바닥(킬 라인·장외 볼륨 없음)", "설계 S4 §3-8 [확정]"));
            Items.Add(new CheckItem("S4-5", 4, "레이저 16 버퍼 점검(K8) — 레이저별 ①사거리 광선 ②벽까지 빔 굵기 콜라이더 수 표, 16 이상이면 벽 앞/뒤 도형 실측", "팀 develop abad97e LaserBeam.cs:46-47·84·112 (PR #111), 재검증지시 K8"));
            Items.Add(new CheckItem("S4-6", 4, "조준 레이저 6개 전부 목표를 잡음(K9) — 팀 레이저는 플레이어 목록을 Start에서 한 번만 모음", "팀 develop abad97e CharacterLockedLaser.cs:59-67, Map4Director.cs:76-121, 재검증지시 K9"));
            Items.Add(new CheckItem("S5-1", 5, "회전 다리 판에 네모 → 예고 → 회전 → 복귀, 떨어지면 CH5 시작 복귀", "S5_설계 §7-1"));
            Items.Add(new CheckItem("S5-2", 5, "가시·망치·도끼·레이저·발사구 피격 → 그 도형만 CH5 시작, 다른 도형 위치 유지", "S5_설계 §7-2"));
            Items.Add(new CheckItem("S5-3", 5, "허공 낙하 → CH5 시작(공용 체크포인트) 복귀", "S5_설계 §7-3"));
            Items.Add(new CheckItem("S5-4", 5, "함정이 플레이어 없이도 주기 반복(배분 A)", "S5_설계 §3"));
            Items.Add(new CheckItem("S5-5", 5, "고정 레이저 16 버퍼 점검(K8) — 레이저별 ①·② 콜라이더 수 표, 16 이상이면 벽 앞/뒤 도형 실측", "팀 develop abad97e LaserBeam.cs:46-47·84·112 (PR #111), 재검증지시 K8"));
            // PTF-3 (R3 지시서 PTF-3 §3 표 — 이름은 스테이징 W/B 코드 정본, 타입·개수로 찾음)
            Items.Add(new CheckItem("S6-1", 6, "에너지볼 2(시작 자리)·고정 패널 ≥4·움직이는 패널 2·레버 2(각 panel = 짝 패널) 존재", "계약 R3 R2-C3 :386-388, S6_Builder.cs:190-191 [제안]"));
            Items.Add(new CheckItem("S6-2", 6, "RespawnZone 3(시작 1 + 체크 2)", "계약 R3 R2-C3 :389, 판정 4"));
            Items.Add(new CheckItem("S6-3", 6, "P1·P3 포탈 진입(걸어 들어가기·서서 빠지기) → Lab_AntiStuck 개입 0", "판정 C1 (가)·C2, R3 :807"));
            Items.Add(new CheckItem("S6-4", 6, "움직이는 패널 위 라이더가 실려 감(패널 변위 대비 도형 변위 ≥ 0.8) — 팀 킨네마틱 rb.velocity 대입 실측 [팀 보고]", "팀 develop abad97e MovablePortalPanel.cs:242-257, PlayerGroundContact.cs:100·:122, PlayerMover.cs:388·:476"));
            Items.Add(new CheckItem("S7-1", 7, "TEMP 입구·출구 개구(문 없음)를 도형이 걸어 지나감", "판정 17-S7, 계약 R3 R2-C3 :390"));
            Items.Add(new CheckItem("S7-2", 7, "PowerMaintenanceController 1 · entranceWiring 비어 있음(TEMP)", "판정 C9, 계약 R3 K7-0 :592"));
            Items.Add(new CheckItem("S7-3", 7, "전력 포트 6개가 서버 6대 앞면에 하나씩(중복 0)", "사용자 09-29 나·안C, S7_Builder.cs:126"));
            Items.Add(new CheckItem("S7-4", 7, "RoleSlot 3 · 컴퓨터↔책 수평 거리 ≥ 2", "S7_Builder.cs:193 [제안 §3-6]"));
            Items.Add(new CheckItem("S7-5", 7, "역할 사물 컴퓨터·책의 손(E) 탭 승자(K12) — 컴퓨터 앞=컴퓨터, 책 앞=책(조회만, execute 안 함)", "팀 develop abad97e RoleSlot.cs:109-125, InteractionController.cs:127-159, 재검증지시 K12"));
            Items.Add(new CheckItem("S8-1", 8, "관리자 닫힌 순찰(waypoints 끝 = WP_1) · 시간 경과 이동", "판정 19, R3 :678·:690"));
            Items.Add(new CheckItem("S8-2", 8, "CCTV 카메라 3 + ManagerCctvPanel 1", "계약 R3 R2-C3 :393"));
            Items.Add(new CheckItem("S8-3", 8, "열쇠 문 1(door = 출구 문)·열쇠 자리 = ANCH_S8_Key·시작 뒤 문 닫힘 유지", "계약 R3 R2-C3 :394, 판정 19"));
            Items.Add(new CheckItem("S8-4", 8, "출구 Exit 마커·섹터 길이 z = 144", "판정 18, R3 :410"));
            Items.Add(new CheckItem("S8-5", 8, "시작 구역 밑면 = 서고 바닥 윗면 −1(K13②) · 열쇠 줍기·문 열기·완성물 사용 지점이 E 중앙 입력으로 동작(K13③)", "팀 develop abad97e ManagerChapterController.cs:57-61·167, ManagerKeyPoint.cs:82, ManagerKeyDoor.cs:52, ManagerUsePoint.cs:49, 재검증지시 K13"));
            // 섹터 밖 전역 항목
            Items.Add(new CheckItem("G-1", 0, "모든 섹터 검사 뒤 씬 전체의 InteractionController 인스턴스 수 = 정확히 1 [K14]", "재검증지시 K14, InteractionController.cs:52-64 [팀 abad97e]"));
        }

        public CheckItem Get(string id) => Items.First(i => i.Id == id);
        public int Count(Verdict v) => Items.Count(i => i.Verdict == v);
        public int SubCount(SubStatus s) => Items.Sum(i => i.Subs.Count(x => x.Status == s));

        /// <summary>통과·미실행이 아닌 하위 결과(실패·검사불가) — 요약에서 숨지 않게 따로 목록화. 항목 판정·exit에는 합산하지 않는다.</summary>
        public IEnumerable<KeyValuePair<CheckItem, SubResult>> OpenSubs() =>
            Items.SelectMany(i => i.Subs.Where(s => s.Status == SubStatus.Fail || s.Status == SubStatus.NA)
                                        .Select(s => new KeyValuePair<CheckItem, SubResult>(i, s)));

        public int ExitCode => (Count(Verdict.Fail) > 0 || Watchdog || SetupError != null) ? 1 : 0;

        /// <summary>txt·json을 새 파일로 쓰고 콘솔 요약 한 줄을 돌려준다(쓰기 실패해도 요약은 돌려준다).</summary>
        public string WriteAndSummarize()
        {
            string txtName = "(쓰기 실패)";
            try
            {
                string outDir = ResolveOutDir();
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string baseName = "FUNC_" + stamp;
                int n = 1;
                // 검증/ 기존 파일 덮어쓰기 금지(공통 규칙 8) — 같은 초에 이미 있으면 _1, _2…
                while (File.Exists(Path.Combine(outDir, baseName + ".txt")) || File.Exists(Path.Combine(outDir, baseName + ".json")))
                    baseName = $"FUNC_{stamp}_{n++}";
                File.WriteAllText(Path.Combine(outDir, baseName + ".txt"), BuildText(), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(outDir, baseName + ".json"), BuildJson(), new UTF8Encoding(false));
                txtName = baseName + ".txt";
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 결과 파일 쓰기 실패: {e}");
            }
            // R1: 하위 결과(판정 외)의 실패·검사불가를 요약 줄에도 드러낸다. 키=값 형식 유지(공백 없는 ASCII 키), exit·file은 끝.
            List<KeyValuePair<CheckItem, SubResult>> open = OpenSubs().ToList();
            string subOpen = open.Count == 0 ? "none" : string.Join(",", open.Select(kv => $"{kv.Key.Id}.{kv.Value.Key}:{SubCode(kv.Value.Status)}"));
            return $"{LogTag} 결과 PASS={Count(Verdict.Pass)} FAIL={Count(Verdict.Fail)} NA={Count(Verdict.NA)} " +
                   $"SETUP={(SetupError == null ? "OK" : "FAIL")} WATCHDOG={(Watchdog ? 1 : 0)} " +
                   $"SUB_FAIL={SubCount(SubStatus.Fail)} SUB_NA={SubCount(SubStatus.NA)} SUB_OPEN={subOpen} exit={ExitCode} file={txtName}";
        }

        public string BuildText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Map4FunctionalChecks (PTF·PTF-2·PTF-3 섹터 기능 검사) — {Started:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"대상 섹터: {Sectors} {ArgNote}".TrimEnd());
            sb.AppendLine($"배치모드: {Application.isBatchMode} / 워치독: {(Watchdog ? "발동(watchdog)" : "미발동")} / 준비: {(SetupError == null ? "OK" : "실패 — " + SetupError)}");
            sb.AppendLine($"판정: 통과 {Count(Verdict.Pass)} · 실패 {Count(Verdict.Fail)} · 검사불가 {Count(Verdict.NA)} → exit {ExitCode}");
            List<KeyValuePair<CheckItem, SubResult>> open = OpenSubs().ToList();
            sb.AppendLine($"하위 결과(항목 판정·exit에 합산 안 함): 통과 {SubCount(SubStatus.Pass)} · 실패 {SubCount(SubStatus.Fail)} · 검사불가 {SubCount(SubStatus.NA)} · 미실행 {SubCount(SubStatus.Skipped)}" +
                          (open.Count == 0 ? "" : " — 미통과: " + string.Join(" / ", open.Select(kv => $"{kv.Key.Id} {kv.Value.Name} {SubLabel(kv.Value.Status)}"))));
            sb.AppendLine();
            foreach (CheckItem it in Items)
            {
                sb.AppendLine($"[{it.Id}] {Label(it.Verdict)} — {it.Title} ({it.Basis}){it.SubTag}");
                sb.AppendLine($"    사유: {it.Reason}");
                foreach (SubResult s in it.Subs) sb.AppendLine($"    하위 · {s.Name}: {SubLabel(s.Status)} — {s.Reason}");
                foreach (string d in it.Details) sb.AppendLine($"    - {d}");
                sb.AppendLine();
            }
            List<CheckItem> na = Items.Where(i => i.Verdict == Verdict.NA).ToList();
            sb.AppendLine($"검사불가 목록({na.Count}): " + (na.Count == 0 ? "없음" : string.Join(" / ", na.Select(i => $"{i.Id} {i.Reason}"))));
            sb.AppendLine();
            sb.AppendLine($"이름 대조({NameChecks.Count(c => c.Ok)}/{NameChecks.Count} 일치):");
            foreach (NameCheck c in NameChecks)
                sb.AppendLine($"  {(c.Ok ? "일치" : "불일치")} · {c.What}: 기대 '{c.Expected}' / 실제 '{c.Actual}'");
            sb.AppendLine();
            sb.AppendLine("메모:");
            foreach (string note in Notes) sb.AppendLine("  " + note);
            return sb.ToString();
        }

        public string BuildJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"runner\":\"Map4FunctionalChecks\",\"task\":\"PTF\",\"started\":{J(Started.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))},");
            sb.Append($"\"sectors\":{J(Sectors)},\"argNote\":{J(ArgNote)},\"batchMode\":{(Application.isBatchMode ? "true" : "false")},");
            sb.Append($"\"watchdog\":{(Watchdog ? "true" : "false")},\"setupError\":{(SetupError == null ? "null" : J(SetupError))},");
            sb.Append($"\"exitCode\":{ExitCode},");
            sb.Append($"\"summary\":{{\"pass\":{Count(Verdict.Pass)},\"fail\":{Count(Verdict.Fail)},\"na\":{Count(Verdict.NA)}," +
                      $"\"subPass\":{SubCount(SubStatus.Pass)},\"subFail\":{SubCount(SubStatus.Fail)},\"subNa\":{SubCount(SubStatus.NA)},\"subSkip\":{SubCount(SubStatus.Skipped)}," +
                      $"\"subOpen\":[{string.Join(",", OpenSubs().Select(kv => J($"{kv.Key.Id}.{kv.Value.Key}:{SubCode(kv.Value.Status)}")))}]}},");
            sb.Append("\"items\":[");
            for (int i = 0; i < Items.Count; i++)
            {
                CheckItem it = Items[i];
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"id\":{J(it.Id)},\"sector\":{it.Sector},\"title\":{J(it.Title)},\"basis\":{J(it.Basis)},");
                sb.Append($"\"result\":{J(Code(it.Verdict))},\"reason\":{J(it.Reason)},\"details\":[");
                sb.Append(string.Join(",", it.Details.Select(J)));
                sb.Append("],\"subResults\":[");
                sb.Append(string.Join(",", it.Subs.Select(s =>
                    $"{{\"key\":{J(s.Key)},\"name\":{J(s.Name)},\"result\":{J(SubCode(s.Status))},\"reason\":{J(s.Reason)}}}")));
                sb.Append("]}");
            }
            sb.Append("],\"nameChecks\":[");
            sb.Append(string.Join(",", NameChecks.Select(c =>
                $"{{\"what\":{J(c.What)},\"expected\":{J(c.Expected)},\"actual\":{J(c.Actual)},\"ok\":{(c.Ok ? "true" : "false")}}}")));
            sb.Append("],\"notes\":[");
            sb.Append(string.Join(",", Notes.Select(J)));
            sb.Append("]}");
            return sb.ToString();
        }

        private static string J(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    // ───────────────────────── 섹터 해석 결과 ─────────────────────────

    private sealed class S1Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public PathChaserController Chaser;
        public TeamExitZone ExitZone;
        public doorPhysics Door;
        public string DoorProblem;
        public RainbowBridgeSwitch Bridge;   // [10-04] 팀 무지개다리 발판(협동)
        public string BridgeProblem;
        public RespawnZone Checkpoint;
        public string CheckpointProblem;
        public Lab_ActivateWhenPlayersReady Activator;
        public CatapultArm Arm;
        public Vector3[] Park;
    }

    private sealed class S5Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public readonly List<SpikeTrap> Spikes = new List<SpikeTrap>();
        public readonly List<HammerTrap> Hammers = new List<HammerTrap>();
        public readonly List<AxeTrap> Axes = new List<AxeTrap>();
        public readonly List<FixedPeriodicLaser> Lasers = new List<FixedPeriodicLaser>();
        public readonly List<ProjectileLauncher> Launchers = new List<ProjectileLauncher>();
        public readonly List<StepRotatingBridge> Bridges = new List<StepRotatingBridge>();
        public SectionSafePoint Start;
        public string StartProblem;
        public RespawnZone Zone;
        public Vector3[] Park;

        public List<Component> AllHazards()
        {
            List<Component> all = new List<Component>();
            all.AddRange(Axes.Cast<Component>());
            all.AddRange(Hammers.Cast<Component>());
            all.AddRange(Spikes.Cast<Component>());
            all.AddRange(Lasers.Cast<Component>());
            all.AddRange(Launchers.Cast<Component>());
            return all;
        }
    }

    private sealed class HitTrial
    {
        public bool Hit;
        public bool Respawned;
        public bool AtStart;
        public bool Inconclusive; // [S5 10-04 M2] 시험 조건(가시 매설 상태)을 만들지 못함 — 통과도 실패도 아닌 검사불가
        public string Msg = "";
    }

    // ───────────── PTF-2 섹터 해석 결과(이름은 계약 R2-C3) ─────────────

    private sealed class S2Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public Transform ExitMarker;                  // TEMP_S2_ExitS3_Open
        public BoxCollider HiddenWall;                // GEO_S2_Wall_IsoHidden
        public string HiddenWallProblem;
        public Transform HumpIso;                     // GEO_S2_Hump_IsoLine
        public doorPhysics ExitS4Door;                // DOOR_S2_ExitS4
        public string ExitS4Problem;
        public IsolationRescueController IsoController;
        public string IsoProblem;
        public readonly List<SnapBlock> Blocks = new List<SnapBlock>();   // 이름 S2_Block_*
        public readonly List<SnapBlock> Scatter = new List<SnapBlock>();  // S2_Block_Scatter_00~14 (이름 순)
        public int PileCount, AllSnapBlocks;
        public readonly List<string> LeverFound = new List<string>();
        public int LeverTotal;
        public int NoFrictionOk, NoFrictionAll;
        public Vector3[] Park;
    }

    private sealed class S3Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public readonly List<ProjectileLauncher> Launchers = new List<ProjectileLauncher>(); // S3_Launcher_PL_*
        public int AllLaunchers;
        public ThreadBridge BridgeB1;                 // S3_Bridge_BR_B1_1
        public string BridgeProblem;
        public int BridgesNamed, AllBridges;
        public RespawnZone CpStart, CpSave;
        public string CpStartProblem, CpSaveProblem;
        public SectionSafePoint SspStart, SspSave;
        public string SspStartProblem, SspSaveProblem;
        public SectionHitCounter Counter;
        public string CounterProblem;
        public OutOfBoundsVolume Oob;
        public Lab_SectionRespawnBridge Bridge;
        public Vector3[] Park;
    }

    private sealed class S4Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public ZeroGravityBubble Bubble;
        public BoxCollider BubbleBox;
        public string BubbleProblem;
        public FixedPeriodicLaser FlStair1, FlStair2, FlBubble1, FlBubble2;
        public string FlProblem;
        public readonly List<CharacterLockedLaser> AimStair = new List<CharacterLockedLaser>();
        public readonly List<CharacterLockedLaser> AimBubble = new List<CharacterLockedLaser>();
        public int AllAim;
        public FallingRockSpawner RockStairA, RockStairB, RockBubble;
        public string RockProblem;
        public SectionSafePoint SpStairs, SpBubble;
        public string SpProblem;
        public SectionHitCounter CntRockStairs, CntLaserStairs, CntRockBubble, CntLaserBubble;
        public string CntProblem;
        public RespawnZone CpStart;
        public Lab_SectionRespawnBridge Bridge;
        public Vector3[] Park;

        public IEnumerable<FixedPeriodicLaser> FixedLasers()
        {
            foreach (FixedPeriodicLaser f in new[] { FlStair1, FlStair2, FlBubble1, FlBubble2 }) if (f != null) yield return f;
        }

        public IEnumerable<FallingRockSpawner> Rocks()
        {
            foreach (FallingRockSpawner r in new[] { RockStairA, RockStairB, RockBubble }) if (r != null) yield return r;
        }
    }

    // ───────────── PTF-3 섹터 해석 결과(이름은 스테이징 W/B 코드 — 계약 R3 R2-C3) ─────────────

    private sealed class S6Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public Transform Group;                                                     // S6_Gimmicks
        public readonly List<EnergyBall> Balls = new List<EnergyBall>();
        public readonly List<PortalSurface> Surfaces = new List<PortalSurface>();   // 고정 + 움직이는 패널 부착분
        public readonly List<MovablePortalPanel> Movables = new List<MovablePortalPanel>();
        public readonly List<PanelLever> Levers = new List<PanelLever>();
        public readonly List<RespawnZone> Zones = new List<RespawnZone>();
        public Vector3[] Park;
    }

    private sealed class S7Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public Transform Group;                       // S7_Gimmicks
        public Transform GimmickRoot;                 // S7_Gimmicks/S7_GimmickRoot (편집 상태 비활성 [판정 13])
        public bool RootActiveAtResolve;
        public Transform EntryMarker, ExitMarker;     // TEMP_S7_EntryDoor_Open · TEMP_S7_ExitDoor_Open
        public int EntryMarkerCount, ExitMarkerCount;
        public Vector3[] Park;
    }

    private sealed class S8Ctx
    {
        public SectorController Sc;
        public Transform Gen;
        public Scene Scene;
        public Transform Group;                       // S8_Gimmicks
    }

    /// <summary>PTF-3 S6-3: 포탈 진입 1회(걸어 들어가기 또는 서서 빠지기) 결과.</summary>
    private sealed class PortalTrial
    {
        public bool Ran;                  // 시험 자리를 찾아 실제로 돌렸는가
        public bool Teleported;
        public float TeleportAfter = -1f; // 놓은 뒤 순간이동까지(s)
        public Vector3 ExitLocal;         // 순간이동 직후 섹터 로컬
        public int Interventions;         // Lab_AntiStuck.InterventionCount 증가
        public readonly List<string> Logs = new List<string>(); // 그 도형의 [Lab_AntiStuck] 로그
        public Vector3 SideDir;           // 걸어 들어온 쪽(패널 중심 → 출발점, 수평 단위 벡터)
        public string Msg = "";
    }

    /// <summary>PTF-2: 위험 요소 1회 피격 시도 결과(목적지 안전점 대조 포함).</summary>
    private sealed class DestTrial
    {
        public bool Hit, Respawned, AtDest, SupportOk;
        public float HitAfter = -1f;
        public string Msg = "";
        public string Support = "";
    }

    /// <summary>PTF-2: 도형을 한 방향으로 모는(속도 대입) 결과.</summary>
    private sealed class DriveResult
    {
        public bool Reached, Respawned;
        public float Elapsed;
        public Vector3 EndLocal;
        public float Progress;   // 진행 방향 이동량(U)
        public string Note = "";
        // [10-04 L2] 가장 느린 0.1초(5스텝) 구간의 진행 속도(U/s)와 그 구간이 끝난 위치(섹터 로컬). 시작 0.3초는 제외(착지·가속). 구간이 없으면 NaN.
        public float MinWindowSpeed = float.NaN;
        public Vector3 MinWindowAtLocal;
    }

    /// <summary>S5-4 — 함정 하나의 운동을 표본으로 모아 "주기 반복 횟수"를 센다(공개 필드·Transform·Collider·
    /// LineRenderer만 읽는다).</summary>
    private sealed class CycleTracker
    {
        public readonly Component H;
        public readonly string Kind;
        public int Shots;
        private readonly List<float> series = new List<float>();
        private readonly Collider part;
        private readonly Vector3 axisW;
        private Vector3 v0;
        private bool hasV0;
        private readonly LineRenderer line;
        private readonly float activeWidth;
        private bool wasFiring;
        private int fireEdges;

        public CycleTracker(Component h)
        {
            H = h;
            if (h is AxeTrap ax)
            {
                Kind = "도끼";
                part = ax.bladeCollider;
                Vector3 a = ax.transform.rotation * ax.localRotationAxis;
                axisW = a.sqrMagnitude > 1e-6f ? a.normalized : ax.transform.forward;
            }
            else if (h is HammerTrap hm) { Kind = "망치"; part = hm.headCollider; }
            else if (h is SpikeTrap sp) { Kind = "가시"; part = sp.spikeCollider; }
            else if (h is FixedPeriodicLaser lz)
            {
                Kind = "레이저";
                if (lz.beam != null) { line = lz.beam.GetComponent<LineRenderer>(); activeWidth = lz.beam.activeWidth; }
            }
            else if (h is ProjectileLauncher) Kind = "발사구";
            else Kind = h.GetType().Name;
        }

        public void Sample()
        {
            if (Kind == "도끼")
            {
                Vector3 c = part != null ? part.bounds.center : H.transform.position + H.transform.up;
                Vector3 v = c - H.transform.position;
                if (!hasV0) { v0 = v; hasV0 = true; }
                series.Add(Vector3.SignedAngle(v0, v, axisW));
            }
            else if (Kind == "망치" || Kind == "가시")
            {
                series.Add(part != null ? part.bounds.center.y : H.transform.position.y);
            }
            else if (Kind == "레이저")
            {
                bool firing = line != null && line.enabled && line.widthMultiplier >= activeWidth * 0.9f;
                if (firing && !wasFiring) fireEdges++;
                wasFiring = firing;
            }
        }

        /// <summary>관찰된 반복 횟수와 설명. 합격 기준: 2회 이상(도끼는 왕복 반전 3회 이상 + 진폭 20° 이상).</summary>
        public bool Evaluate(out string info)
        {
            if (Kind == "레이저")
            {
                info = line == null ? "LineRenderer 없음(beam 비어 있음)" : $"발사 시작 {fireEdges}회";
                return fireEdges >= 2;
            }
            if (Kind == "발사구") { info = $"탄 생성 {Shots}회"; return Shots >= 2; }
            if (series.Count < 3) { info = "표본 부족"; return false; }

            float min = series.Min(), max = series.Max(), range = max - min;
            if (Kind == "도끼")
            {
                int reversals = 0; int dir = 0; float anchor = series[0];
                foreach (float s in series)
                {
                    float d = s - anchor;
                    if (Mathf.Abs(d) < 1f) continue;
                    int nd = d > 0 ? 1 : -1;
                    if (dir != 0 && nd != dir) reversals++;
                    dir = nd; anchor = s;
                }
                info = $"진폭 {range:F1}° · 왕복 반전 {reversals}회";
                return range >= 20f && reversals >= 3;
            }

            if (range < 0.3f) { info = $"이동 폭 {range:F2}U(0.3 미만 — 정지)"; return false; }
            float mid = min + range * 0.5f;
            int entries = 0; bool inFar = false;
            foreach (float y in series)
            {
                bool far = Kind == "망치" ? y < mid : y > mid; // 망치: 내려옴 / 가시: 튀어나옴
                if (far && !inFar) entries++;
                inFar = far;
            }
            info = $"이동 폭 {range:F2}U · {(Kind == "망치" ? "하강" : "돌출")} {entries}회";
            return entries >= 2;
        }
    }

    // ───────────────────────── 플레이 모드 러너 ─────────────────────────

    private partial class Driver : MonoBehaviour
    {
        private Report report;
        private bool runS1, runS5;
        private bool finished;
        private float realDeadline;

        private Map4Director director;
        private RespawnController respawn;
        private PlayerMover sphere, cube, tetra;
        private readonly List<PlayerMover> players = new List<PlayerMover>();
        private readonly List<KeyValuePair<PlayerMover, float>> respawnLog = new List<KeyValuePair<PlayerMover, float>>();
        private readonly List<Action> cleanups = new List<Action>();
        private readonly HashSet<PlayerMover> drivenByUs = new HashSet<PlayerMover>();

        private S1Ctx s1; private string s1Problem;
        private S5Ctx s5; private string s5Problem;
        private bool s1CartsArrived;
        private float s1ClearedAt = -1f, s1Stage4Start = -1f;   // [10-04 T5 진단] OnChapterCleared가 S1-4 진행 중 발화한 시각(관찰 전용)
        private UnityAction s1ClearListener;

        // PTF-2
        private bool runS2, runS3, runS4;
        private float watchdogSeconds = WatchdogSeconds;
        private S2Ctx s2; private string s2Problem;
        private S3Ctx s3; private string s3Problem;
        private S4Ctx s4; private string s4Problem;
        /// <summary>팀 RespawnController 로그 "[Respawn] 체크포인트 갱신: '이름'"을 관찰해 쌓는다(공개 getter가 없어서 — 관찰 전용).</summary>
        private readonly List<KeyValuePair<string, float>> checkpointLog = new List<KeyValuePair<string, float>>();

        // PTF-3
        private bool runS6, runS7, runS8;
        private S6Ctx s6; private string s6Problem;
        private S7Ctx s7; private string s7Problem;
        private S8Ctx s8; private string s8Problem;
        /// <summary>S6-3이 놓은 포탈 GameObject(검사 뒤 Destroy — [2차판정 19] ③). 예외로 끊겨도 S6_Cleanup·Finish 정리가 없앤다.</summary>
        private readonly List<GameObject> s6Portals = new List<GameObject>();
        /// <summary>"[Lab_AntiStuck] …" 로그(우리 스크립트 Lab_AntiStuck.cs:150·:240·:226의 Debug.Log 문구) — S6-3 동안만 관찰.</summary>
        private readonly List<string> antiStuckLog = new List<string>();
        /// <summary>S8-1이 관리자를 깨웠을 때 되돌리는 동작(되돌린 뒤 null). 예외로 끊겨도 Finish 정리에서 부른다.</summary>
        private Action s8ManagerRestore;

        public void Configure(string sectors, string argNote)
        {
            report = new Report(sectors, argNote);
            string[] parts = sectors.Split(',');
            runS1 = parts.Contains("1");
            runS2 = parts.Contains("2");
            runS3 = parts.Contains("3");
            runS4 = parts.Contains("4");
            runS5 = parts.Contains("5");
            runS6 = parts.Contains("6");
            runS7 = parts.Contains("7");
            runS8 = parts.Contains("8");
            watchdogSeconds = WatchdogFor(sectors);
        }

        void Start()
        {
            if (report == null) Configure(DefaultSectors, $"(Configure 없음 → 기본 {DefaultSectors})");
            realDeadline = Time.realtimeSinceStartup + watchdogSeconds;
            StartCoroutine(RunAll());
        }

        void Update()
        {
            if (!finished && Time.realtimeSinceStartup > realDeadline)
            {
                report.Watchdog = true;
                report.Notes.Add($"watchdog — {watchdogSeconds}초(실시간) 안에 끝나지 않아 강제 종료.");
                Finish();
            }
        }

        void OnDestroy()
        {
            // 인터랙티브에서 사용자가 도중에 플레이를 멈춘 경우 — 예비 워치독이 나중에 엉뚱하게 발동하지 않게 끈다.
            if (!finished && !Application.isBatchMode) SessionState.EraseFloat(DeadlineKey);
        }

        private IEnumerator RunAll()
        {
            yield return RunSubStep(MainSequence(), "MAIN");
            Finish();
        }

        /// <summary>하위 단계를 이 메서드 자신의 try/catch 안에서 직접 MoveNext()한다. 하위 단계가 다시
        /// IEnumerator를 yield하면(도우미 코루틴) Unity에 넘기지 않고 스택에 쌓아 같이 펼친다 — 어느 깊이에서
        /// 예외가 나도 여기서 잡혀 그 항목만 기록되고 전체 순서는 계속된다.</summary>
        private IEnumerator RunSubStep(IEnumerator step, string id)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(step);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                bool moved;
                object current = null;
                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception e)
                {
                    OnStepException(id, e);
                    yield break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        private void OnStepException(string id, Exception e)
        {
            string msg = $"[예외] {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}";
            if (AllIds.Contains(id)) report.Get(id).Set(Verdict.Fail, msg);
            else if (id == "SETUP" || id == "MAIN") report.SetupError = (report.SetupError == null ? "" : report.SetupError + " / ") + id + " " + msg;
            else report.Notes.Add($"{id} {msg}");
            Debug.LogError($"{LogTag} {id} {e}");
        }

        private static string FirstFrame(Exception e)
        {
            string st = e.StackTrace ?? "";
            int nl = st.IndexOf('\n');
            return (nl > 0 ? st.Substring(0, nl) : st).Trim();
        }

        private IEnumerator MainSequence()
        {
            yield return RunSubStep(Setup(), "SETUP");
            if (report.SetupError != null)
            {
                foreach (CheckItem it in report.Items)
                    if (!it.Decided) it.Set(Verdict.NA, "준비 실패로 미실행: " + report.SetupError);
                yield break;
            }

            if (runS1)
            {
                yield return RunSubStep(ResolveS1(), "S1-해석");
                // 순서: 벽 파괴 전 조건(S1-2)을 먼저, 카트·출구(S1-4·S1-5)를 마지막에.
                yield return RunSubStep(S1_1_ChaserStarts(), "S1-1");
                yield return RunSubStep(S1_2_CatchReturnsSafePre(), "S1-2");
                yield return RunSubStep(KA_S1_8_CatchZoneDrop(), "S1-8");
                yield return RunSubStep(S1_3_BreakWall(), "S1-3");
                yield return RunSubStep(S1_6_CliffFall(), "S1-6");
                yield return RunSubStep(S1_4_CartsCross(), "S1-4");
                yield return RunSubStep(S1_4b_BridgeCross(), "S1-4");  // [10-04] 다리 3개 결과를 S1-4 판정에 합산(예외도 S1-4 실패로 기록)
                yield return RunSubStep(S1_5_ExitDoorOpens(), "S1-5");
                yield return RunSubStep(KA_S1_7_SteerDockScale(), "S1-7");
                yield return RunSubStep(KA_S1_9_TetherLimit(), "S1-9");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S1"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            // PTF-2: 섹터 순서(S1 → S2 → S3 → S4 → S5). S5는 S5_Prepare가 자기 체크포인트를 다시 잡으므로 앞 섹터의 체크포인트 변화와 무관하다.
            if (runS2)
            {
                yield return RunSubStep(ResolveS2(), "S2-해석");
                yield return RunSubStep(S2_Prepare(), "S2-준비");
                yield return RunSubStep(S2_2_BlocksAndHump(), "S2-2"); // 블록 개수·자리는 다른 검사가 건드리기 전에
                yield return RunSubStep(S2_3_HiddenWall(), "S2-3");
                yield return RunSubStep(S2_1_ExitOpening(), "S2-1");
                yield return RunSubStep(S2_4_IsolationFlow(), "S2-4");
                yield return RunSubStep(KA_S2_5_Levers(), "S2-5");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S2"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            if (runS3)
            {
                yield return RunSubStep(ResolveS3(), "S3-해석");
                yield return RunSubStep(S3_Prepare(), "S3-준비");
                yield return RunSubStep(S3_1_LaunchersCycle(), "S3-1"); // 도형을 전부 치운 상태에서 먼저 관찰
                yield return RunSubStep(S3_3_ProjectileHit(), "S3-3");  // 세이브 전(체크포인트 = CP_S3_Start)
                yield return RunSubStep(S3_2_VoidFallSaveRule(), "S3-2"); // 순서 고정: (a) 세이브 전 → (b) 세이브 뒤
                yield return RunSubStep(S3_4_BridgeSupport(), "S3-4");
                yield return RunSubStep(KA_S3_5_ThreadHang(), "S3-5");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S3"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            if (runS4)
            {
                yield return RunSubStep(ResolveS4(), "S4-해석");
                yield return RunSubStep(S4_Prepare(), "S4-준비");
                yield return RunSubStep(S4_1_BubbleRise(), "S4-1");
                yield return RunSubStep(S4_2_LaserHits(), "S4-2");
                yield return RunSubStep(S4_3_RockHits(), "S4-3");
                yield return RunSubStep(S4_4_StairFall(), "S4-4");
                yield return RunSubStep(KB_S4_5_BeamBuffer(), "S4-5");
                yield return RunSubStep(KB_S4_6_AimLockAll(), "S4-6");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S4"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            if (runS5)
            {
                yield return RunSubStep(ResolveS5(), "S5-해석");
                yield return RunSubStep(S5_Prepare(), "S5-준비");
                yield return RunSubStep(S5_4_TrapsCycle(), "S5-4"); // 플레이어를 전부 치운 상태에서 먼저 관찰
                yield return RunSubStep(S5_1_RotatingBridge(), "S5-1");
                yield return RunSubStep(S5_2_HazardHits(), "S5-2");
                yield return RunSubStep(S5_3_VoidFall(), "S5-3");
                yield return RunSubStep(KB_S5_5_BeamBuffer(), "S5-5");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S5"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            // PTF-3: S6 → S7 → S8. 섹터마다 자기 파킹부터 한다(앞 섹터 결과와 무관).
            if (runS6)
            {
                yield return RunSubStep(ResolveS6(), "S6-해석");
                yield return RunSubStep(S6_Prepare(), "S6-준비");
                yield return RunSubStep(S6_1_Presence(), "S6-1");
                yield return RunSubStep(S6_2_RespawnZones(), "S6-2");
                yield return RunSubStep(S6_3_AntiStuckPortal(), "S6-3");
                yield return RunSubStep(KC_S6_4_PanelRiders(), "S6-4"); // 재검증 T1K: 움직이는 패널 라이더 실측 [팀 보고]
                yield return RunSubStep(S6_Cleanup(), "S6-정리"); // S6-3이 예외로 끊겨도 포탈·로그 관찰을 여기서 걷는다
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S6"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            if (runS7)
            {
                yield return RunSubStep(ResolveS7(), "S7-해석");
                // 정적 대조(S7-2·3·4)를 먼저 — 주행(S7-1)이 전실 활성기(Lab_ActivateWhenPlayersReady [판정 13])를 켜기 전 상태를 기록한다.
                yield return RunSubStep(S7_2_PowerEntranceWiring(), "S7-2");
                yield return RunSubStep(S7_3_PortsOnServers(), "S7-3");
                yield return RunSubStep(S7_4_RoleSlots(), "S7-4");
                yield return RunSubStep(S7_Prepare(), "S7-준비");
                yield return RunSubStep(S7_1_TempOpenings(), "S7-1");
                yield return RunSubStep(KB_S7_5_RoleSlotPriority(), "S7-5");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S7"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            if (runS8)
            {
                yield return RunSubStep(ResolveS8(), "S8-해석");
                yield return RunSubStep(S8_2_Cctv(), "S8-2");
                yield return RunSubStep(S8_3_KeyDoor(), "S8-3");
                yield return RunSubStep(S8_4_ExitLength(), "S8-4");
                yield return RunSubStep(S8_1_ManagerPatrol(), "S8-1"); // 관리자를 깨우는 검사는 마지막(끝나면 준비 상태로 되돌림)
                yield return RunSubStep(S8_RestoreManager(), "S8-정리");
                yield return RunSubStep(KB_S8_5_StartZoneAndKeyE(), "S8-5");
            }
            else foreach (string id in AllIds.Where(x => x.StartsWith("S8"))) report.Get(id).Set(Verdict.NA, "-fcSectors에서 제외");

            // 재검증 T1K(KA): 섹터 블록 밖 전역 항목 — -fcSectors와 무관하게 항상 돈다(Run 흐름 맨 끝)
            yield return RunSubStep(KA_G1_InteractionControllerCount(), "G-1");
        }

        // ───────────── 준비 ─────────────

        private IEnumerator Setup()
        {
            report.Notes.Add($"시작 {DateTime.Now:HH:mm:ss} · Time.timeScale={Time.timeScale} · fixedDeltaTime={Time.fixedDeltaTime}(변경 안 함)");
            director = Object.FindObjectOfType<Map4Director>();
            if (director == null) { report.SetupError = "Map4Director를 찾지 못함(Master 씬 확인)"; yield break; }

            float t = Time.time;
            while (director.LoadedSectorCount < 8 && Time.time - t < 30f) yield return null;
            report.Notes.Add($"섹터 로드 {director.LoadedSectorCount}/8 ({Time.time - t:F1}s 대기)");

            players.AddRange(Object.FindObjectsOfType<PlayerMover>());
            if (players.Count == 0) { report.SetupError = "PlayerMover 0개"; yield break; }
            if (players.Count != 3) report.Notes.Add($"주의: 플레이어 {players.Count}개(기대 3)");

            // Director가 배치를 끝내면 중력을 되돌린다(Map4Director C4) — 그 신호를 기다린다.
            t = Time.time;
            while (players.Any(p => { Rigidbody rb = p.GetComponent<Rigidbody>(); return rb != null && !rb.useGravity; }) && Time.time - t < 15f)
                yield return null;
            report.Notes.Add($"Director 배치 완료 대기 {Time.time - t:F1}s (중력 켜짐 {players.Count(p => p.GetComponent<Rigidbody>().useGravity)}/{players.Count})");

            foreach (PlayerMover p in players)
            {
                PlayerShapeIdentity idn = p.GetComponent<PlayerShapeIdentity>();
                PlayerShapeStats.ShapeKind kind;
                if (idn != null && idn.stats != null) kind = idn.Kind;
                else if (p.name.Contains("Cube")) kind = PlayerShapeStats.ShapeKind.Cube;
                else if (p.name.Contains("Tetra")) kind = PlayerShapeStats.ShapeKind.Tetrahedron;
                else kind = PlayerShapeStats.ShapeKind.Sphere;
                if (kind == PlayerShapeStats.ShapeKind.Cube && cube == null) cube = p;
                else if (kind == PlayerShapeStats.ShapeKind.Tetrahedron && tetra == null) tetra = p;
                else if (kind == PlayerShapeStats.ShapeKind.Sphere && sphere == null) sphere = p;
            }
            // 식별 실패분은 남은 도형으로 채운다(검사 진행용 — 메모에 남김).
            foreach (PlayerMover p in players)
            {
                if (p == sphere || p == cube || p == tetra) continue;
                if (sphere == null) sphere = p; else if (cube == null) cube = p; else if (tetra == null) tetra = p;
            }
            report.Notes.Add($"도형: 구={Nm(sphere)} 네모={Nm(cube)} 세모={Nm(tetra)}");
            if (sphere == null || cube == null || tetra == null)
            {
                report.SetupError = "세 도형(구·네모·세모)을 모두 식별하지 못함";
                yield break;
            }

            respawn = Object.FindObjectOfType<RespawnController>();
            if (respawn == null) report.Notes.Add("주의: RespawnController 없음 — 복귀 관련 항목은 실패/검사불가가 된다.");
            else
            {
                respawn.PlayerRespawned += OnPlayerRespawned;
                cleanups.Add(() => { if (respawn != null) respawn.PlayerRespawned -= OnPlayerRespawned; });
            }
            // PTF-2: 공용 체크포인트 갱신은 팀 로그로만 드러난다(RespawnController에 공개 getter 없음) — 관찰 전용 로그 구독.
            Application.logMessageReceived += OnLog;
            cleanups.Add(() => { Application.logMessageReceived -= OnLog; });
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>팀 로그 "[Respawn] 체크포인트 갱신: 'X' …"에서 X를 뽑아 둔다(RespawnController.StoreCheckpoint의 Debug.Log 문구).</summary>
        private void OnLog(string condition, string stackTrace, LogType type)
        {
            const string key = "[Respawn] 체크포인트 갱신: '";
            if (condition == null) return;
            int i = condition.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return;
            int s = i + key.Length;
            int e = condition.IndexOf('\'', s);
            if (e > s) checkpointLog.Add(new KeyValuePair<string, float>(condition.Substring(s, e - s), Time.time));
        }

        /// <summary>since 이후 이름이 name인 체크포인트 갱신 로그가 있었는가.</summary>
        private bool CheckpointStoredSince(string name, float since) => checkpointLog.Any(kv => kv.Key == name && kv.Value >= since);

        private string LastCheckpointName() => checkpointLog.Count == 0 ? "(갱신 로그 없음)" : checkpointLog[checkpointLog.Count - 1].Key;

        private void OnPlayerRespawned(GameObject go)
        {
            PlayerMover m = go != null ? go.GetComponentInParent<PlayerMover>() : null;
            if (m != null) respawnLog.Add(new KeyValuePair<PlayerMover, float>(m, Time.time));
        }

        private int RespawnsSince(PlayerMover m, float since) => respawnLog.Count(kv => kv.Key == m && kv.Value >= since);

        // ───────────── S1 해석 ─────────────

        private IEnumerator ResolveS1()
        {
            if (!director.TryGetSector(1, out SectorController sc) || sc == null)
            {
                s1Problem = "섹터 1이 로드되지 않음";
                yield break;
            }
            S1Ctx c = new S1Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;

            List<PathChaserController> chasers = InScene<PathChaserController>(c.Scene);
            if (chasers.Count == 0)
            {
                s1Problem = "PathChaserController 0개 — S1 미구체화(빈 틀) 또는 팀 추격자 누락";
                yield break;
            }
            if (chasers.Count > 1) report.Notes.Add($"S1: PathChaserController {chasers.Count}개 — 첫 번째 '{chasers[0].name}' 사용");
            c.Chaser = chasers[0];

            // 계약 이름 대조(S1_Builder가 붙인 이름) — 팀 기믹 자체 이름은 대조하지 않는다.
            if (c.Chaser.wall != null) AddName("S1 파괴벽(BreakableObject)", "S1_BreakableWall", c.Chaser.wall.name);
            if (c.Chaser.carts != null)
                for (int i = 0; i < c.Chaser.carts.Length; i++)
                    AddName($"S1 카트{i}(RailCart)", $"S1_RailCart_{i}", c.Chaser.carts[i] != null ? c.Chaser.carts[i].name : "(null)");
            if (c.Chaser.arrivalPoint != null) AddName("S1 도착점", "S1_ArrivalPoint", c.Chaser.arrivalPoint.name);
            c.ExitZone = c.Chaser.teamExitZone;
            if (c.ExitZone != null) AddName("S1 TeamExitZone", "S1_TeamExitZone", c.ExitZone.name);

            c.Door = PickByName(InScene<doorPhysics>(c.Scene), "S1_ExitDoor", "S1 출구 문(doorPhysics)", out c.DoorProblem);
            c.Bridge = PickByName(InScene<RainbowBridgeSwitch>(c.Scene), "S1_RainbowBridge_Switch", "S1 무지개다리 발판(RainbowBridgeSwitch)", out c.BridgeProblem);
            // [10-04 사용자 결정 — 다리 한 판] 조각 수 1(옛 14조각 씬이면 이음매 걸림이 되살아나므로 이름 대조에 드러낸다). 소멸 판정(콜라이더 전부 꺼짐)은 조각 수와 무관하게 그대로다.
            if (c.Bridge != null && c.Bridge.targetObjects != null) AddName("S1 다리 조각 수(한 판)", "1", c.Bridge.targetObjects.Length.ToString());

            List<RespawnZone> zones = InScene<RespawnZone>(c.Scene);
            if (zones.Count == 0) c.CheckpointProblem = "S1 섹터 씬에 RespawnZone 0개";
            else if (zones.Count == 1) c.Checkpoint = zones[0];
            else
            {
                Vector3 mk = sc.checkpoint != null ? sc.checkpoint.position : c.Gen.TransformPoint(new Vector3(0f, 0f, 3.5f));
                c.Checkpoint = zones.OrderBy(z => Vector3.Distance(z.transform.position, mk)).First();
                report.Notes.Add($"S1: RespawnZone {zones.Count}개 — 체크포인트 마커에 가장 가까운 '{c.Checkpoint.name}' 사용");
            }

            List<OutOfBoundsVolume> oobs = InScene<OutOfBoundsVolume>(c.Scene);
            if (oobs.Count > 0) PickByName(oobs, "S1_CliffOutOfBounds", "S1 절벽 장외 볼륨(OutOfBoundsVolume)", out _);

            List<Lab_ActivateWhenPlayersReady> acts = InScene<Lab_ActivateWhenPlayersReady>(c.Scene);
            c.Activator = acts.FirstOrDefault();
            List<Lab_SectionRespawnBridge> bridges = InScene<Lab_SectionRespawnBridge>(c.Scene);
            report.Notes.Add($"S1: Lab_SectionRespawnBridge {bridges.Count}개" +
                             (bridges.Count > 0 ? $" (연결한 카운터 {bridges[0].WiredCount}개, 부모 '{Parent(bridges[0].transform)}')" : " — 잡힘 복귀가 RespawnController로 이어지지 않는다"));
            if (bridges.Count > 0) AddName("S1 복귀 다리 오브젝트(Lab_SectionRespawnBridge)", "S1_Adapters", bridges[0].gameObject.name);

            List<CatapultArm> arms = InScene<CatapultArm>(c.Scene);
            c.Arm = arms.FirstOrDefault();
            if (c.Arm != null)
            {
                Transform root = c.Arm.transform;
                while (root.parent != null && root.parent != c.Gen) root = root.parent;
                AddName("S1 투석기 루트", "S1_SlingCatapult", root.name);
            }

            c.Park = S1ParkSpots(c);
            s1 = c;
            report.Notes.Add($"S1 해석: 추격자 '{c.Chaser.name}'(활성={c.Chaser.gameObject.activeInHierarchy}), 파괴벽={Nm(c.Chaser.wall)}, " +
                             $"카트={(c.Chaser.carts == null ? 0 : c.Chaser.carts.Length)}대(기대 {S1ExpectedCarts}), 다리발판={Nm(c.Bridge)}(조각 {(c.Bridge != null && c.Bridge.targetObjects != null ? c.Bridge.targetObjects.Length : 0)}개), " +
                             $"출구문={Nm(c.Door)}, 체크포인트={Nm(c.Checkpoint)}, 투석기={Nm(c.Arm)}");
            yield break;
        }

        private Vector3[] S1ParkSpots(S1Ctx c)
        {
            List<Vector3> spots = new List<Vector3>();
            if (c.ExitZone != null && c.ExitZone.GetComponent<Collider>() != null)
            {
                // TeamExitZone 안(도착 플랫폼) — S1-5 "전원 도착" 조건을 그대로 겸한다. [구현 결정] 구역 폭의 −80%/−60%/+80%,
                // 길이 +60% 지점 — S1-B 보고 기준 x −12/−9/12, z 184: 레일 끝·도착점(−3,176)[10-04 M-A 보정]·카트 레일 x −6/0·다리 발판(6,170)에서 8U 이상.
                Bounds b = c.ExitZone.GetComponent<Collider>().bounds;
                float[] fx = { -0.8f, -0.6f, 0.8f };
                foreach (float f in fx)
                {
                    Vector3 top = new Vector3(b.center.x + b.extents.x * f, b.max.y + 2f, b.center.z + b.extents.z * 0.6f);
                    spots.Add(GroundedSpot(top, b.size.y + 10f, null, new Vector3(top.x, b.min.y, top.z)));
                }
            }
            else
            {
                foreach (Vector3 l in S1ParkFallbackLocal)
                {
                    Vector3 w = c.Gen.TransformPoint(l);
                    spots.Add(GroundedSpot(w + Vector3.up * 5f, 12f, null, w));
                }
                report.Notes.Add("S1: TeamExitZone 없음 — 설계 좌표 파킹점 사용");
            }
            return spots.ToArray();
        }

        private bool Ready1(string id)
        {
            if (s1 != null) return true;
            report.Get(id).Set(Verdict.NA, s1Problem ?? "S1 해석 실패");
            return false;
        }

        // ───────────── S1-1 ─────────────

        private IEnumerator S1_1_ChaserStarts()
        {
            const string id = "S1-1";
            if (!Ready1(id)) yield break;
            PathChaserController ch = s1.Chaser;
            float t0 = Time.time;
            bool assisted = false;
            while (!ch.gameObject.activeInHierarchy && Time.time - t0 < 15f)
            {
                // 5초 안에 안 켜지면 도형 하나를 시작 구역(Lab_ActivateWhenPlayersReady.zone)에 다시 넣어 본다.
                if (!assisted && Time.time - t0 > 5f && s1.Activator != null && s1.Activator.zone != null)
                {
                    assisted = true;
                    Vector3 zc = s1.Activator.zone.bounds.center;
                    yield return WaitIdle(sphere, 3f);
                    Teleport(sphere, GroundedSpot(zc + Vector3.up * 2f, 8f, null, zc));
                    Detail(id, "5초 동안 추격자가 켜지지 않아 구를 시작 구역 중앙에 다시 넣음");
                }
                yield return null;
            }
            if (!ch.gameObject.activeInHierarchy)
            {
                Fail(id, $"도형 배치 후 15초 안에 추격 컨트롤러가 켜지지 않음(Lab_ActivateWhenPlayersReady {(s1.Activator == null ? "없음" : "있음")})");
                yield break;
            }
            float activatedAt = Time.time;
            Detail(id, $"추격 컨트롤러 활성 — 러너 시작 후 {activatedAt - t0:F2}s");
            PathChaserAgent agent = ch.agent;
            if (agent == null) { Fail(id, "chaser.agent 비어 있음"); yield break; }
            Vector3 start = agent.transform.position;

            // 스폰 자리에서 곧바로 잡히지 않게 세 도형을 도착 플랫폼으로 치운다(이후 항목의 기준 위치).
            yield return ParkPlayers(s1.Park, sphere, cube, tetra);

            float movedAt = -1f, maxMove = 0f;
            while (Time.time - activatedAt < ch.startDelay + 6f)
            {
                float d = Vector3.Distance(agent.transform.position, start);
                maxMove = Mathf.Max(maxMove, d);
                if (movedAt < 0f && d > 0.05f) movedAt = Time.time;
                if (d >= 1f) break;
                yield return new WaitForFixedUpdate();
            }
            Detail(id, $"추격자 시작 local{V(L1(start))} → 현재 local{V(L1(agent.transform.position))}, 최대 이동 {maxMove:F2}U, controller.enabled={ch.enabled}");
            if (!ch.enabled) { Fail(id, "추격 컨트롤러가 시작을 거부함(enabled=false — 필수 참조 누락, 콘솔 PathChaserController 오류 참고)"); yield break; }
            if (maxMove >= 1f)
                Pass(id, $"활성 후 {movedAt - activatedAt:F2}s에 움직이기 시작(팀 startDelay {ch.startDelay}s), 이동 {maxMove:F2}U");
            else
                Fail(id, $"활성 후 {ch.startDelay + 6f:F1}s 동안 이동 {maxMove:F2}U(1U 미만)");
        }

        // ───────────── S1-2 ─────────────

        private IEnumerator S1_2_CatchReturnsSafePre()
        {
            const string id = "S1-2";
            if (!Ready1(id)) yield break;
            PathChaserController ch = s1.Chaser;
            if (!ch.gameObject.activeInHierarchy || !ch.enabled || ch.agent == null) { NA(id, "추격 컨트롤러가 동작 중이 아님(S1-1 참고)"); yield break; }
            if (ch.wall == null) { NA(id, "chaser.wall 비어 있음 — 벽 파괴 전/후 구분 불가"); yield break; }
            if (!ch.wall.gameObject.activeInHierarchy) { NA(id, "파괴벽이 이미 없음 — '벽 파괴 전' 조건을 만들 수 없음"); yield break; }
            SectionHitCounter counter = ch.safePreCounter;
            SectionSafePoint dest = counter != null ? counter.destination : null;
            if (dest == null) { Fail(id, "safePreCounter 또는 그 destination(SectionSafePoint)이 비어 있음"); yield break; }

            Vector3 destL = L1(dest.transform.position);
            bool designOk = Mathf.Abs(destL.x - S1SafePreXZ.x) <= 0.5f && Mathf.Abs(destL.z - S1SafePreXZ.y) <= 0.5f;
            Detail(id, $"SAFE_PRE '{dest.name}'(sectionId={dest.sectionId}) local{V(destL)} — 설계 ({S1SafePreXZ.x},{S1SafePreXZ.y}) {(designOk ? "일치" : "불일치")}, " +
                       $"보조 {(dest.backupPoints == null ? 0 : dest.backupPoints.Length)}개, 카운터 임계 {counter.hitsBeforeRespawn}, 쿨다운 {counter.hitCooldown}s");

            PlayerMover victim = sphere;
            yield return WaitIdle(victim, 5f);
            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(victim));
            float since = Time.time;
            float deadline = Time.time + 10f;
            float lastPlace = -10f;
            int placements = 0;
            while (Time.time < deadline && RespawnsSince(victim, since) == 0)
            {
                if (!IsBusy(victim) && Time.time - lastPlace > 1.2f)
                {
                    if (placements > 0)
                    {
                        // 임계가 2 이상이면 트리거 재진입이 필요하다 — 잡기 구역(반경 1) 위로 한 스텝 뺐다가 다시 넣는다.
                        Teleport(victim, ch.agent.transform.position + Vector3.up * 2.2f);
                        yield return new WaitForFixedUpdate();
                    }
                    Teleport(victim, ch.agent.transform.position);
                    placements++;
                    lastPlace = Time.time;
                }
                yield return new WaitForFixedUpdate();
            }
            int resp = RespawnsSince(victim, since);
            if (resp == 0) { Fail(id, $"잡기 구역에 {placements}회 넣었으나 10초 안에 복귀가 일어나지 않음(SectionHitCounter→RespawnController 연결 확인)"); yield break; }
            yield return WaitIdle(victim, 4f);
            yield return new WaitForSeconds(0.3f);

            Vector3 pos = victim.transform.position;
            bool atDest = NearAny(pos, SafePoints(dest), 0.9f, 1.6f, out float bestD);
            string others = CheckUnchanged(snap, since, 0.5f);
            bool wallStill = ch.wall.gameObject.activeInHierarchy;
            Detail(id, $"'{victim.name}' 복귀 후 local{V(L1(pos))}, 가장 가까운 SAFE_PRE 지점까지 수평 {bestD:F2}U, 벽 상태={(wallStill ? "파괴 전" : "파괴됨")}, 넣은 횟수 {placements}");
            Detail(id, "다른 도형: " + (others ?? "위치 유지(0.5U 이내)·복귀 없음"));

            List<string> bad = new List<string>();
            if (!atDest) bad.Add($"복귀 위치가 SAFE_PRE(및 보조) 아님(수평 {bestD:F2}U)");
            if (others != null) bad.Add("다른 도형 영향: " + others);
            if (!designOk) bad.Add($"SAFE_PRE 좌표가 설계와 다름(local x{destL.x:F2} z{destL.z:F2} ≠ −26,116)");
            if (bad.Count == 0) Pass(id, $"잡힌 도형만 SAFE_PRE local({destL.x:F1},{destL.z:F1})로 복귀, 다른 도형 유지");
            else Fail(id, string.Join(" / ", bad));
        }

        // ───────────── S1-3 ─────────────

        private IEnumerator S1_3_BreakWall()
        {
            const string id = "S1-3";
            if (!Ready1(id)) yield break;
            BreakableObject wall = s1.Chaser.wall;
            if (wall == null) { NA(id, "chaser.wall 비어 있음 — 파괴벽 없음"); yield break; }
            if (!wall.gameObject.activeInHierarchy) { NA(id, "검사 전에 이미 파괴됨"); yield break; }
            Rigidbody crb = cube.GetComponent<Rigidbody>();
            Detail(id, $"파괴벽 '{wall.name}' requiredWeight={wall.requiredWeight} breakThreshold={wall.breakThreshold} / 네모 '{cube.name}' PlayerWeight={PlayerWeight.Of(crb):F2}");
            Collider wallCol = wall.GetComponent<Collider>();
            Bounds wb = wallCol != null ? wallCol.bounds : new Bounds(wall.transform.position, wall.transform.lossyScale);
            Vector3 fwd = s1.Gen.forward;
            Vector3 wcL = L1(wb.center);
            // [구현 결정] 벽 앞(투석기 쪽) 면에서 0.3 띄운 자리, 벽 중심에서 x +1.5(파괴벽 폭 6 안), 높이 1.0.
            Vector3 throwStart = s1.Gen.TransformPoint(new Vector3(wcL.x + 1.5f, 1.0f, wcL.z - wb.extents.z - 0.8f));

            // (1) 대조군 — 임계 미만(3U/s)으로 부딪히면 깨지지 않아야 한다. [구현 결정] 3 = 팀 임계 5보다 작게.
            // R1: 충돌 속도는 "첫 벽 접촉 직전 물리 스텝의 전방 속도"로 기록한다(창 끝의 잔류 속도가 아님).
            ImpactProbe slow = new ImpactProbe();
            yield return ThrowCube(throwStart, fwd, 3f, 1.0f, wall, v => slow = v);
            bool brokeBySlow = !wall.gameObject.activeInHierarchy;
            Detail(id, $"대조군: 3U/s로 밀기 — {slow.Text} → {(brokeBySlow ? "파괴됨(임계 미만인데 파괴 — 이상)" : "안 깨짐")}");
            string controlText;
            if (brokeBySlow)
            {
                report.Get(id).SetSub("control", "대조군(임계 미만 안 깨짐)", SubStatus.Fail, $"첫 접촉 직전 전방 {slow.PreContactFwd:F2}U/s에서 파괴됨");
                Fail(id, $"임계(5) 미만 속도(첫 접촉 직전 전방 {slow.PreContactFwd:F2}U/s)에서 파괴됨");
                yield break;
            }
            if (slow.Contact)
            {
                controlText = $"대조군 첫 접촉 직전 {slow.PreContactFwd:F2}U/s는 안 깨짐";
                report.Get(id).SetSub("control", "대조군(임계 미만 안 깨짐)", SubStatus.Pass, $"{slow.Text} → 안 깨짐");
            }
            else
            {
                // 접촉이 확인되지 않으면 "3U/s로는 안 깨진다"를 주장할 근거가 없다 — 대조군만 검사불가, 항목 판정은 그대로.
                controlText = "대조군 벽 접촉 미확인(대조 근거 없음)";
                report.Get(id).SetSub("control", "대조군(임계 미만 안 깨짐)", SubStatus.NA, slow.Text);
            }

            // (2) 투석기 — 팀 공개 API(CatapultArm.BeginPull/Fire). 탑승은 버킷 트리거에 네모를 옮겨 팀 물리 판정으로.
            // R1: 이 경로의 결과는 항목 판정에 합산하지 않고 하위 결과 "투석기 경로"(catapult)로 따로 드러낸다.
            // R2: 당김 1.0 한 가지만 쏘면 "벽을 넘겼다"만 알 뿐 "벽을 맞힐 당김이 있는가"는 모른다 — 당김 0.3~1.0(0.1 간격)을
            //     오름차순으로 쏘고, 발사마다 벽 앞면(섹터 로컬 z) 통과 높이·실제 벽 접촉·접촉 직전 속도 크기를 기록한다.
            //     벽이 깨지면 그 뒤 당김은 쏘지 않는다(팀 BreakableObject에 되살리는 공개 API가 없다). 판정·exit 합산 방식은 R1과 같다.
            string catapult;
            SubStatus catapultStatus;
            CatapultArm arm = s1.Arm;
            float catapultImpact = 0f;
            if (arm == null) { catapult = "CatapultArm 없음(투석기 미배치)"; catapultStatus = SubStatus.NA; }
            else if (arm.bucket == null) { catapult = "CatapultArm.bucket 비어 있음"; catapultStatus = SubStatus.NA; }
            else
            {
                Bounds wl = LocalAabb(wb);
                Detail(id, $"투석기 당김 훑기 — 벽 local x{wl.min.x:F2}~{wl.max.x:F2} y{wl.min.y:F2}~{wl.max.y:F2} z{wl.min.z:F2}~{wl.max.z:F2}, " +
                           $"팀 공개 필드 rest {arm.restAngle}° pulled {arm.pulledAngle}° 노치 {arm.pullNotchCount} 속도 {arm.minLaunchSpeed}~{arm.maxLaunchSpeed} 피치 {arm.launchPitch}°");
                List<string> lines = new List<string>();
                CatapultShot breaker = null, abortShot = null;
                int fired = 0, contacts = 0;
                for (int i = 0; i < CatapultPulls.Length; i++)
                {
                    if (!wall.gameObject.activeInHierarchy)
                    {
                        lines.Add($"당김 {CatapultPulls[i]:F1}~{CatapultPulls[CatapultPulls.Length - 1]:F1}: 벽 파괴 뒤라 미측정");
                        break;
                    }
                    CatapultShot shot = new CatapultShot { Pull = CatapultPulls[i] };
                    yield return FireCatapultOnce(arm, wall, wallCol, wl, shot);
                    Detail(id, "투석기 " + shot.Text(wl));
                    lines.Add(shot.Short(wl));
                    if (shot.Fired) fired++;
                    if (shot.Contact) contacts++;
                    if (shot.Abort != null) { abortShot = shot; break; }
                    if (shot.Broke) { breaker = shot; catapultImpact = shot.ContactSpeed; }
                }
                string sweep = string.Join(" / ", lines);
                if (breaker != null)
                {
                    catapult = $"당김 {breaker.Pull:F1}(팔 {breaker.Angle:F1}°, 발사 {breaker.LaunchSpeed:F2}U/s)에서 벽 적중·파괴 — 접촉 직전 속도 크기 {breaker.ContactSpeed:F2}U/s(≥{wall.breakThreshold}) · 훑기: {sweep}";
                    catapultStatus = SubStatus.Pass;
                }
                else if (abortShot != null && fired == 0)
                {
                    catapult = $"발사 전 중단: {abortShot.Abort} · 훑기: {sweep}";
                    catapultStatus = SubStatus.Fail;
                }
                else
                {
                    catapult = (contacts > 0
                                   ? $"벽 접촉 {contacts}회 있었으나 파괴 없음"
                                   : $"발사 {fired}회 모두 벽 미적중(적중하는 당김 없음 — 설계 경로 '투석기로 벽 파괴' 불성립 의심)")
                               + (abortShot != null ? $", 당김 {abortShot.Pull:F1}에서 중단: {abortShot.Abort}" : "")
                               + $" · 훑기: {sweep}";
                    catapultStatus = SubStatus.Fail;
                }
            }
            Detail(id, "투석기(팀 공개 API): " + catapult);
            if (!wall.gameObject.activeInHierarchy)
            {
                report.Get(id).SetSub("catapult", "투석기 경로(공개 API 발사, 당김 0.3~1.0 훑기)", SubStatus.Pass, catapult);
                report.Get(id).SetSub("direct", "직접 충돌 재현(8U/s)", SubStatus.Skipped, "투석기 경로로 이미 파괴돼 돌리지 않음");
                Pass(id, $"투석기 공개 API 발사로 파괴(접촉 직전 속도 크기 {catapultImpact:F2}U/s ≥ {wall.breakThreshold}), {controlText}");
                yield return AfterWallCleanup();
                yield break;
            }
            report.Get(id).SetSub("catapult", "투석기 경로(공개 API 발사, 당김 0.3~1.0 훑기)", catapultStatus, catapult);

            // (3) 투석기 결과 재현 — 네모를 8U/s로 직접 부딪힌다. [구현 결정] 8 = 팀 임계 5 이상, 투석기 최소 발사 10 미만.
            yield return WaitIdle(cube, 7f); // 투석기 착지까지(버킷이 ExternallyDriven을 최대 6초 잡는다)
            ImpactProbe fast = new ImpactProbe();
            yield return ThrowCube(throwStart, fwd, 8f, 2.0f, wall, v => fast = v);
            bool broken = !wall.gameObject.activeInHierarchy;
            Detail(id, $"직접 충돌 8U/s로 밀기 — {fast.Text} → {(broken ? "파괴" : "안 깨짐")}");
            report.Get(id).SetSub("direct", "직접 충돌 재현(8U/s)", broken ? SubStatus.Pass : SubStatus.Fail, $"{fast.Text} → {(broken ? "파괴" : "안 깨짐")}");
            // 판정 로직은 R1 전과 같다(직접 충돌로 파괴되면 통과). 투석기 경로 상태는 사유 끝과 하위 결과에 따로 표기.
            string catTag = $"투석기 경로: {SubLabel(catapultStatus)} — 하위 결과 참조";
            if (broken) Pass(id, $"직접 충돌 첫 접촉 직전 전방 {fast.PreContactFwd:F2}U/s ≥ {wall.breakThreshold} → 파괴, {controlText} ({catTag})");
            else Fail(id, $"네모(무게 {PlayerWeight.Of(crb):F2}) 직접 충돌({fast.Text})에도 파괴 안 됨 ({catTag})");
            yield return AfterWallCleanup();
        }

        /// <summary>R1: 벽 충돌 속도 측정 결과. PreContactFwd = 첫 벽 접촉이 확인된 물리 스텝의 "직전" 스텝 뒤 전방 속도
        /// (= 그 스텝에 들어갈 때의 접근 속도, 팀 BreakableObject가 쓰는 relativeVelocity 크기에 대응[추정]).</summary>
        private sealed class ImpactProbe
        {
            public bool Contact;
            public string How = "";
            public float PreContactFwd;
            public float PostContactFwd = float.NaN;
            public float MaxFwd;
            public float EndFwd;
            public int Steps;

            public string Text => Contact
                ? $"첫 벽 접촉 직전 전방 {PreContactFwd:F2}U/s → 접촉 스텝 뒤 {(float.IsNaN(PostContactFwd) ? "(측정 없음)" : PostContactFwd.ToString("F2", CultureInfo.InvariantCulture))}U/s, 최대 {MaxFwd:F2}U/s, 창 끝 {EndFwd:F2}U/s ({How}, {Steps}스텝)"
                : $"벽 접촉 미확인 — 최대 전방 {MaxFwd:F2}U/s, 창 끝 {EndFwd:F2}U/s ({Steps}스텝)";
        }

        /// <summary>네모를 ExternallyDriven으로 잠깐 잡아(팀 PlayerMover가 속도를 덮어쓰지 않게) 지정 속도로 민다.
        /// 벽이 사라지거나 제한 시간이 지나면 원래 플래그로 되돌린다.
        /// R1: 창 끝의 잔류 속도가 아니라 "첫 벽 접촉 직전" 전방 속도를 따로 기록한다. 접촉 판정 = 이 스텝 뒤
        /// (a) 벽이 비활성이 됐거나 (b) 네모 콜라이더를 진행 방향으로 0.05U 옮기면 벽 콜라이더와 겹침(Physics.ComputePenetration,
        /// 팀 코드 호출 아님). 판정된 스텝 바로 앞 표본을 충돌 직전 속도로 쓴다.</summary>
        private IEnumerator ThrowCube(Vector3 startW, Vector3 dir, float speed, float window, BreakableObject wall, Action<ImpactProbe> impact)
        {
            yield return WaitIdle(cube, 5f);
            Rigidbody rb = cube.GetComponent<Rigidbody>();
            Collider cubeCol = FirstSolid(cube.gameObject);
            Collider wallCol = wall.GetComponent<Collider>();
            if (wallCol == null || wallCol.isTrigger) wallCol = FirstSolid(wall.gameObject);
            Vector3 d = dir.normalized;
            bool prevDriven = cube.ExternallyDriven;
            ImpactProbe info = new ImpactProbe();
            try
            {
                cube.ExternallyDriven = true;
                drivenByUs.Add(cube);
                Teleport(cube, startW);
                rb.velocity = d * speed;
                float prevFwd = Vector3.Dot(rb.velocity, d); // 첫 스텝에 들어갈 때의 속도
                info.MaxFwd = prevFwd;
                info.EndFwd = prevFwd;
                float t = Time.time;
                while (Time.time - t < window)
                {
                    yield return new WaitForFixedUpdate();
                    info.Steps++;
                    bool gone = !wall.gameObject.activeInHierarchy;
                    float cur = Vector3.Dot(rb.velocity, d);
                    if (!info.Contact)
                    {
                        bool near = !gone && Touching(cubeCol, wallCol, d * 0.05f);
                        if (gone || near)
                        {
                            info.Contact = true;
                            info.PreContactFwd = prevFwd;
                            info.PostContactFwd = cur;
                            info.How = gone ? "이 스텝에 벽 비활성(파괴)" : "진행 방향 0.05U 안에 벽 콜라이더(ComputePenetration)";
                        }
                    }
                    info.MaxFwd = Mathf.Max(info.MaxFwd, cur);
                    info.EndFwd = cur;
                    prevFwd = cur;
                    if (gone) break;
                }
            }
            finally
            {
                cube.ExternallyDriven = prevDriven;
                drivenByUs.Remove(cube);
                impact(info);
            }
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>a를 nudge만큼 옮긴 자세로 b와 겹치는지(유니티 물리 질의, 팀 코드 아님). 콜라이더가 없으면 false.</summary>
        private static bool Touching(Collider a, Collider b, Vector3 nudge)
        {
            if (a == null || b == null) return false;
            return Physics.ComputePenetration(a, a.transform.position + nudge, a.transform.rotation,
                                              b, b.transform.position, b.transform.rotation, out _, out _);
        }

        /// <summary>R2: 투석기 1회 발사 기록(당김 값별). 좌표는 S1 섹터 로컬.</summary>
        private sealed class CatapultShot
        {
            public float Pull;
            public float Angle = float.NaN;       // Fire 직전 팔 각도(팀 공개 CurrentAngle)
            public float ExpectSpeed = float.NaN; // [계산] 팀 공개 필드로 Fire와 같은 식: Lerp(min,max,InverseLerp(rest,pulled,angle)) — 무게 배율 1 가정
            public float LaunchSpeed;             // 분리 직후 실제 속도 크기
            public float MaxSpeed;
            public bool Fired, Crossed, Contact, Broke, Landed;
            public float CrossX, CrossY, CrossSpeed;
            public float ContactSpeed;            // 첫 벽 접촉이 확인된 스텝 "직전" 표본의 속도 크기(팀 판정 relativeVelocity.magnitude 대응[추정])
            public Vector3 ContactL, EndL;
            public string Abort;

            private string Where(Bounds wl)
            {
                // PTF-2(1차 R3 지적, FUNC_215825:21·26·27): 앞면 통과 기록 없이 벽 접촉이 잡힌 발사를 "닿기 전 추적 종료"로 적지 않는다.
                // 문구만 바꾼다 — Crossed·Contact·Broke 측정과 판정은 그대로.
                if (!Crossed && Contact) return Broke ? "접촉으로 파괴 — 앞면 통과 기록 없음" : "접촉(안 깨짐) — 앞면 통과 기록 없음";
                if (!Crossed) return "벽 앞면에 닿기 전 " + (Landed ? "착지" : "추적 종료");
                const float half = 0.5f; // 네모 반폭[확정 규격 11 — 도형 약 1U]
                if (CrossX < wl.min.x - half || CrossX > wl.max.x + half) return $"벽 옆으로 통과(x{CrossX:F2})";
                if (CrossY > wl.max.y + half) return $"벽 위로 {CrossY - wl.max.y:F2}U 넘김(y{CrossY:F2})";
                if (CrossY < wl.min.y - half) return $"벽 밑면 아래(y{CrossY:F2})";
                return $"벽면 안(x{CrossX:F2}, y{CrossY:F2})";
            }

            public string Short(Bounds wl)
            {
                if (Abort != null && !Fired) return $"당김 {Pull:F1}: 중단({Abort})";
                string hit = Broke ? $"적중·파괴 {ContactSpeed:F2}U/s" : Contact ? $"적중·미파괴 {ContactSpeed:F2}U/s" : "미적중";
                return $"당김 {Pull:F1}(팔 {Angle:F1}°·발사 {LaunchSpeed:F2}U/s): {hit}, {Where(wl)}";
            }

            public string Text(Bounds wl)
            {
                if (Abort != null && !Fired) return $"당김 {Pull:F1}: 발사 전 중단 — {Abort}";
                string hit = Contact
                    ? $"벽 접촉 local{V(ContactL)}, 접촉 직전 속도 크기 {ContactSpeed:F2}U/s → {(Broke ? "파괴" : "안 깨짐")}"
                    : "벽 접촉 없음";
                string cross = Crossed ? $"벽 앞면(z{wl.min.z:F2}) 통과 local x{CrossX:F2} y{CrossY:F2} 속도 {CrossSpeed:F2}U/s" : "벽 앞면 통과 기록 없음";
                return $"당김 {Pull:F1}: 팔 {Angle:F1}° · 기대 발사 {ExpectSpeed:F2}U/s[계산] · 실제 분리 직후 {LaunchSpeed:F2}U/s · {cross} → {Where(wl)} · {hit} · 비행 중 최대 {MaxSpeed:F2}U/s · 끝 local{V(EndL)}";
            }
        }

        /// <summary>R2: 팀 공개 API만으로 투석기를 한 번 쏜다 — 탑승 각도까지 BeginPull(1) → 버킷 트리거에 네모를 옮겨 팀 탑승 판정 →
        /// BeginPull(당김)으로 노치를 맞춘 뒤 Fire(당김). 발사 속도는 팀 Fire가 "Fire 순간 팔 각도"로 정한다. 비행은 매 물리 스텝 추적.</summary>
        private IEnumerator FireCatapultOnce(CatapultArm arm, BreakableObject wall, Collider wallCol, Bounds wl, CatapultShot s)
        {
            CatapultBucket bucket = arm.bucket;
            Rigidbody crb = cube.GetComponent<Rigidbody>();
            Collider cubeCol = FirstSolid(cube.gameObject);
            if (wallCol == null || wallCol.isTrigger) wallCol = FirstSolid(wall.gameObject);

            yield return WaitIdle(cube, 7f);
            yield return new WaitForSeconds(0.3f);
            yield return WaitIdle(cube, 3f); // 직전 발사 뒤 팀 복귀 연출이 이어서 시작됐을 수 있다
            float t = Time.time;
            while (arm.State == CatapultArm.ArmState.Launching && Time.time - t < 3f) yield return null;

            t = Time.time;
            while (arm.CurrentAngle < bucket.boardMinArmAngle - 0.01f && Time.time - t < 3f) { arm.BeginPull(1f); yield return null; }
            if (arm.CurrentAngle < bucket.boardMinArmAngle - 0.01f)
            {
                s.Abort = $"BeginPull(1)로 탑승 각도 {bucket.boardMinArmAngle}°에 못 미침(현재 {arm.CurrentAngle:F1}°)";
                yield break;
            }
            if (!bucket.HasOccupant)
            {
                Collider trig = FirstTrigger(bucket.gameObject);
                Teleport(cube, trig != null ? trig.bounds.center : bucket.transform.position);
                t = Time.time;
                while (!bucket.HasOccupant && Time.time - t < 2f) { arm.BeginPull(1f); yield return null; }
                if (!bucket.HasOccupant) { s.Abort = "버킷 트리거로 옮겨도 탑승(Board)되지 않음"; yield break; }
            }

            // 당김 조정 — 팀 BeginPull은 노치 전환을 pullTransitionDuration 동안 보간한다. [구현 결정] 그보다 0.25초 더 부른다.
            float hold = Mathf.Max(0f, arm.pullTransitionDuration) + 0.25f;
            t = Time.time;
            while (Time.time - t < hold) { arm.BeginPull(s.Pull); yield return null; }
            s.Angle = arm.CurrentAngle;
            s.ExpectSpeed = Mathf.Lerp(arm.minLaunchSpeed, arm.maxLaunchSpeed, Mathf.InverseLerp(arm.restAngle, arm.pulledAngle, s.Angle));
            if (!bucket.HasOccupant) { s.Abort = "당김 조정 중 탑승 해제"; yield break; }

            arm.Fire(s.Pull);
            t = Time.time;
            while (bucket.HasOccupant && Time.time - t < 3f) yield return new WaitForFixedUpdate();
            if (bucket.HasOccupant) { s.Abort = "Fire 뒤 3초 안에 분리되지 않음"; yield break; }
            s.Fired = true;

            Vector3 prevL = L1(cube.transform.position);
            float prevSpeed = crb.velocity.magnitude;
            s.LaunchSpeed = prevSpeed;
            s.MaxSpeed = prevSpeed;
            float frontZ = wl.min.z;
            float contactAt = -1f;
            t = Time.time;
            // [구현 결정] 추적 창 6초: 당김 1.0 궤적(최대 18U/s)도 벽 앞면(투석기에서 약 20m)까지 2초 안쪽[계산].
            while (Time.time - t < 6f)
            {
                yield return new WaitForFixedUpdate();
                bool gone = !wall.gameObject.activeInHierarchy;
                Vector3 curL = L1(cube.transform.position);
                float sp = crb.velocity.magnitude;
                s.MaxSpeed = Mathf.Max(s.MaxSpeed, sp);
                if (!s.Crossed && prevL.z < frontZ && curL.z >= frontZ)
                {
                    float k = (frontZ - prevL.z) / Mathf.Max(1e-5f, curL.z - prevL.z);
                    s.Crossed = true;
                    s.CrossX = Mathf.Lerp(prevL.x, curL.x, k);
                    s.CrossY = Mathf.Lerp(prevL.y, curL.y, k);
                    s.CrossSpeed = Mathf.Lerp(prevSpeed, sp, k);
                }
                if (!s.Contact)
                {
                    Vector3 v = crb.velocity;
                    Vector3 nudge = v.sqrMagnitude > 1e-6f ? v.normalized * 0.05f : Vector3.zero;
                    bool near = !gone && Touching(cubeCol, wallCol, nudge);
                    if (gone || near)
                    {
                        s.Contact = true;
                        s.ContactSpeed = prevSpeed;
                        s.ContactL = curL;
                        contactAt = Time.time;
                    }
                }
                if (gone) { s.Broke = true; break; }
                prevL = curL;
                prevSpeed = sp;
                if (s.Contact && Time.time - contactAt > 0.6f) break;     // 튕겨 나옴 — 더 볼 것 없음
                if (curL.y < wl.min.y - 3f) break;                          // 벽 밑면보다 3U 아래로 떨어짐
                if (!s.Contact && !s.Crossed && Time.time - t > 1f && sp < 0.2f) { s.Landed = true; break; } // 벽 앞에서 멈춤
            }
            s.EndL = L1(cube.transform.position);
        }

        /// <summary>R2: 월드 AABB의 8 꼭짓점을 S1 섹터 로컬로 옮겨 감싼 로컬 AABB(섹터가 회전돼 있어도 축이 맞게).</summary>
        private Bounds LocalAabb(Bounds w)
        {
            Vector3 c = w.center, e = w.extents;
            Bounds lb = new Bounds(L1(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                lb.Encapsulate(L1(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return lb;
        }

        private IEnumerator AfterWallCleanup()
        {
            yield return WaitIdle(cube, 7f);
            yield return ParkPlayers(s1.Park, cube);
        }

        // ───────────── S1-6 ─────────────

        private IEnumerator S1_6_CliffFall()
        {
            const string id = "S1-6";
            if (!Ready1(id)) yield break;
            if (s1.Checkpoint == null) { NA(id, "공용 체크포인트(RespawnZone) 없음: " + s1.CheckpointProblem); yield break; }
            PlayerMover victim = tetra;
            yield return WaitIdle(victim, 5f);

            Vector3 drop = Vector3.zero; bool found = false;
            foreach (Vector3 l in S1CliffDropLocal)
            {
                Vector3 w = s1.Gen.TransformPoint(l);
                if (TryGround(w, 4f, null, 0f, out _) || Physics.CheckSphere(w, 0.7f, ~0, QueryTriggerInteraction.Ignore)) continue;
                drop = w; found = true; break;
            }
            if (!found) { NA(id, "절벽 구간(z140~168)에 빈 허공 시험점을 찾지 못함(지형 확인)"); yield break; }
            string below = TryGround(drop, 80f, null, 0f, out Vector3 g) ? $"아래 {drop.y - g.y:F1}U에 바닥 있음(local y{L1(g).y:F1})" : "아래 80U까지 바닥 없음";
            Detail(id, $"낙하 시작 local{V(L1(drop))} — {below} / 체크포인트 '{s1.Checkpoint.name}' local{V(L1(s1.Checkpoint.transform.position))}");

            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(victim));
            float since = Time.time;
            Teleport(victim, drop);
            float t = Time.time;
            while (RespawnsSince(victim, since) == 0 && Time.time - t < 15f) yield return null;
            if (RespawnsSince(victim, since) == 0)
            {
                Fail(id, $"15초 안에 복귀 없음 — 현재 local{V(L1(victim.transform.position))} ({below})");
                yield break;
            }
            yield return WaitIdle(victim, 6f);
            yield return new WaitForSeconds(0.5f);
            Vector3 pos = victim.transform.position;
            Collider zc = s1.Checkpoint.GetComponent<Collider>();
            Bounds zb = zc != null ? zc.bounds : new Bounds(s1.Checkpoint.transform.position, Vector3.one * 2f);
            // R2: 높이는 구역 bounds(높이 18 — 지붕 위까지 받아 줌)로 보지 않는다. 수평은 구역 ±1.5, 높이는 "발밑 첫 지지면 =
            // 입구 바닥 윗면(로컬 y0±0.2), 밑면-지지면 ≤0.3"(CheckSupport)으로 본다. S5-3과 같은 기준.
            bool horizIn = pos.x >= zb.min.x - 1.5f && pos.x <= zb.max.x + 1.5f && pos.z >= zb.min.z - 1.5f && pos.z <= zb.max.z + 1.5f;
            bool onFloor = CheckSupport(victim, s1.Gen, CheckpointFloorLocalY, out string support);
            Detail(id, $"복귀 후 local{V(L1(pos))} ({Time.time - since:F1}s), 체크포인트 구역 local 중심{V(L1(zb.center))} 크기{V(zb.size)} — 수평 {(horizIn ? "구역 안" : "구역 밖")}");
            Detail(id, "발밑: " + support);
            // 진단 전용(판정 미사용): 팀 공개 RespawnZone.FindGroundPoint() — 팀 복귀가 서게 하는 "바닥" 높이.
            Detail(id, $"진단: 팀 RespawnZone.FindGroundPoint() = local{V(L1(s1.Checkpoint.FindGroundPoint()))} (구역 중심에서 아래로 쏜 레이 중 가장 높은 면 — RespawnZone.cs TryFindGroundY)");
            string others = CheckUnchanged(snap, since, 0.5f);
            if (others != null) Detail(id, "다른 도형: " + others);
            if (horizIn && onFloor) Pass(id, $"절벽 낙하 → 공용 체크포인트 '{s1.Checkpoint.name}' 입구 바닥(local y{CheckpointFloorLocalY:F0})으로 복귀({Time.time - since:F1}s)");
            else
            {
                List<string> bad = new List<string>();
                if (!horizIn) bad.Add($"공용 체크포인트 구역 수평 밖(local{V(L1(pos))})");
                if (!onFloor) bad.Add("발밑이 입구 바닥 아님 — " + support);
                Fail(id, "복귀는 됐으나 " + string.Join(" / ", bad));
            }
            yield return ParkPlayers(s1.Park, victim);
        }

        // ───────────── S1-4 ─────────────

        private IEnumerator S1_4_CartsCross()
        {
            const string id = "S1-4";
            if (!Ready1(id)) yield break;
            // [10-04 T5 진단] OnChapterCleared가 S1-4(카트·다리 시험) 도중 일찍 발화했는지 관찰만 한다 — 시험 도형을 TeamExitZone 안에 파킹하면 카트가 도착해 있는 동안
            // 팀 종료 조건(PathChaserController.cs:221-229)이 성립할 수 있다. 리스너는 기믹 동작을 바꾸지 않는다(검사 종료 때 뗌).
            s1ClearedAt = -1f; s1Stage4Start = Time.time;
            if (s1.Chaser.OnChapterCleared != null && s1ClearListener == null)
            {
                s1ClearListener = () => { if (s1ClearedAt < 0f) s1ClearedAt = Time.time; };
                s1.Chaser.OnChapterCleared.AddListener(s1ClearListener);
                PathChaserController chRef = s1.Chaser; UnityAction lis = s1ClearListener;
                cleanups.Add(() => { if (chRef != null && chRef.OnChapterCleared != null) chRef.OnChapterCleared.RemoveListener(lis); });
            }
            Detail(id, "[진단] S1-4 시작 시 챕터 상태: " + ChapterProbe());
            RailCart[] carts = s1.Chaser.carts;
            Transform arrival = s1.Chaser.arrivalPoint;
            if (carts == null || carts.Length != S1ExpectedCarts || carts.Any(x => x == null)) { Fail(id, $"chaser.carts가 {S1ExpectedCarts}대가 아님({(carts == null ? 0 : carts.Count(x => x != null))}대) — 10-04 결정은 카트 2대(씬이 옛 3대 판이면 Generate를 다시 돌려야 함)"); yield break; }
            if (arrival == null) { Fail(id, "chaser.arrivalPoint 비어 있음"); yield break; }
            float radius = s1.Chaser.arrivalRadius;
            WindupAxle axle = carts[0].axle;
            WindupActivationPad pad = carts[0].activationPad;
            if (axle == null) { NA(id, "carts[0].axle 비어 있음 — 태엽 축을 구동할 수 없음"); yield break; }
            Detail(id, "카트: " + string.Join(" / ", carts.Select(x =>
                $"{x.name} local{V(L1(x.transform.position))} mode={x.activationMode} 같은축={(x.axle == axle)} 같은발판={(x.activationPad == pad)} onRail={x.IsOnRail}")));
            Detail(id, $"도착점 local{V(L1(arrival.position))}, arrivalRadius {radius}");

            PlayerMover presser = tetra;
            bool needPad = carts.Any(x => x.activationMode == WindupActivationMode.HoldPad);
            if (needPad)
            {
                if (pad == null) { Fail(id, "HoldPad 모드인데 activationPad 비어 있음"); yield break; }
                yield return WaitIdle(presser, 5f);
                Collider pc = pad.GetComponent<Collider>();
                Vector3 pcen = pc != null ? pc.bounds.center : pad.transform.position;
                Teleport(presser, GroundedSpot(pcen + Vector3.up * 2f, 6f, null, pcen));
                float tp = Time.time;
                while (!pad.IsHeld && Time.time - tp < 3f) yield return new WaitForFixedUpdate();
                Detail(id, $"발판 '{pad.name}' 에 세모 올림 → IsHeld={pad.IsHeld} latchOnFirstPress={pad.latchOnFirstPress}");
                if (!pad.IsHeld) { Fail(id, "도형을 발판에 올려도 IsHeld=false"); yield break; }
            }

            // 태엽 축 구동 — 팀 공개 API WindupAxle.ApplyRotation(축의 유일한 입력 경계). 굴리기 포탈로 막대를 미는
            // 물리 경로 대신이다. [구현 결정] 처음 4회(1회 +1.0 = 네모 1텀블)로 최대 충전, 이후 카트가 모두 멈췄을
            // 때만 추가로 민다(최대 16회).
            float t0 = Time.time;
            int pushes = 0;
            float nextPush = Time.time;
            bool presserParked = !needPad;
            while (Time.time - t0 < 50f)
            {
                if (carts.All(x => Vector3.Distance(x.transform.position, arrival.position) <= radius)) break;
                if (Time.time >= nextPush)
                {
                    bool stalled = carts.All(x => x.Velocity.magnitude < 0.2f);
                    if (pushes < 4 || (stalled && pushes < 16)) { axle.ApplyRotation(1f); pushes++; }
                    nextPush = Time.time + Mathf.Max(1.05f, axle.crankSwingCooldown + 0.05f);
                }
                if (!presserParked && pad != null && pad.latchOnFirstPress && Time.time - t0 > 1f)
                {
                    presserParked = true;
                    yield return ParkPlayers(s1.Park, presser); // 래치 발판 — 내려와도 눌린 상태 유지
                }
                yield return new WaitForFixedUpdate();
            }
            bool all = carts.All(x => Vector3.Distance(x.transform.position, arrival.position) <= radius);
            Detail(id, $"축 밀기 {pushes}회, 경과 {Time.time - t0:F1}s, 축 충전 {axle.CurrentCharge:F2}");
            List<string> crossed = new List<string>();
            foreach (RailCart x in carts)
            {
                Vector3 l = L1(x.transform.position);
                float d = Vector3.Distance(x.transform.position, arrival.position);
                crossed.Add($"{x.name}: local z{l.z:F1}(절벽 끝 {S1CliffEndZ} {(l.z >= S1CliffEndZ ? "넘음" : "못 넘음")}) 도착점 거리 {d:F2} onRail={x.IsOnRail}");
            }
            Detail(id, string.Join(" / ", crossed));
            if (!presserParked) yield return ParkPlayers(s1.Park, presser);
            s1CartsArrived = all;
            if (all) Pass(id, $"카트 {carts.Length}대가 절벽(28m)을 건너 도착점 반경 {radius} 안 도착({Time.time - t0:F1}s, 축 밀기 {pushes}회 — 팀 공개 API ApplyRotation)");
            else Fail(id, $"50초 안에 {carts.Length}대 모두 도착점 반경에 들지 못함 — 위 카트별 위치 참고");
        }

        // ───────────── S1-4 하위: 무지개다리 [10-04 사용자 결정] ─────────────

        /// <summary>[10-04 사용자 결정 · 설계/S1_설계.md §12] 카트 2대에 못 탄 세 번째 도형이 팀 무지개다리(협동 모드)로 건너는지.
        /// S1-4의 하위 결과 5개(하위 결과로 표기하되 S1-4 판정에 합산한다 — 하나라도 실패면 S1-4 실패, 실측 불가(검사불가)면 S1-4 검사불가: S1_4_PromoteVerdict).
        /// [10-04 L2] ②는 네모(기존)에 구·세모 횡단 표본을 더해 세 도형 모두 시험한다(각 횡단은 S1_BridgeCrossTrial — 평균 속도·가장 느린 0.1초 구간 속도 기록).
        ///   ① bridgeOpen — 처음엔 닫힘(조각 콜라이더 전부 꺼짐·가운데 아래 광선에 맞는 것 없음) → 발판 점유(세모를 올림) 뒤 조각 콜라이더 전부 켜지고 윗면 로컬 y 0 지지면이 생김
        ///   ② bridgeCross — 점유가 유지되는 동안 세 번째 도형(네모 — 가장 느려 최악 조건)이 승차장 쪽 다리 머리에서 실제 물리로 걸어(수평 속도 대입 = DriveShape) 도착 플랫폼에 닿음, 추락 복귀 0
        ///   ③ bridgeClose — 발판을 비우면(세모를 치움) 다리 콜라이더가 다시 꺼짐(협동 모드 = 밟는 동안만 유지)
        /// 읽는 것은 팀 공개 필드(targetObjects)·Collider.enabled·Physics 질의뿐 — 팀 코드·리플렉션 0. 카트 도착(S1-4 판정)을 선행 조건으로 두지 않는다 —
        /// 다리 동작은 카트와 독립이고(카트는 API로 구동한 빈 카트), 선행으로 막으면 카트 문제가 다리 결과를 가린다.</summary>
        private IEnumerator S1_4b_BridgeCross()
        {
            yield return S1_4b_BridgeSteps();
            S1_4_PromoteVerdict();
            if (s1 != null)
                Detail("S1-4", $"[진단] S1-4 종료 시 챕터 상태: {ChapterProbe()} · OnChapterCleared S1-4 중 발화 {(s1ClearedAt >= 0f ? $"예(S1-4 시작 +{s1ClearedAt - s1Stage4Start:F1}s)" : "아니오")}");
        }

        /// <summary>[10-04 사용자 결정 — 컨트롤타워 판정] S1-4 판정 = 카트 2대 도착(S1_4_CartsCross가 정한 판정) 그리고 다리 개방·네모·구·세모 횡단·소멸 하위 결과 5개 모두 통과.
        /// 하나라도 실패(또는 카트 실패)면 실패, 실패는 없고 실측 불가(검사불가·결과 없음)가 하나라도 있으면 검사불가, 전부 통과일 때만 통과.
        /// 하위 결과 표기는 그대로 유지한다. s1CartsArrived(S1-5 선행 조건)는 카트 도착만 따르게 그대로 둔다.</summary>
        private void S1_4_PromoteVerdict()
        {
            const string id = "S1-4";
            if (s1 == null) return; // Ready1이 이미 검사불가로 적었다.
            CheckItem item = report.Get(id);
            string[] keys = { "bridgeOpen", "bridgeCross", "bridgeCrossSphere", "bridgeCrossTetra", "bridgeClose" };
            List<string> failParts = new List<string>(), naParts = new List<string>();
            if (item.Verdict == Verdict.Fail) failParts.Add($"카트: {item.Reason}");
            else if (item.Verdict == Verdict.NA) naParts.Add($"카트 검사불가: {item.Reason}");
            foreach (string k in keys)
            {
                SubResult sub = item.Subs.FirstOrDefault(x => x.Key == k);
                if (sub == null) { naParts.Add($"{k} 결과 없음"); continue; }
                if (sub.Status == SubStatus.Pass) continue;
                string part = $"{sub.Name} {SubLabel(sub.Status)}: {sub.Reason}";
                if (sub.Status == SubStatus.Fail) failParts.Add(part); else naParts.Add(part);
            }
            string cartReason = item.Reason;
            if (failParts.Count > 0)
            {
                item.Verdict = Verdict.Fail; item.Decided = true;
                item.Reason = "S1-4 = 카트 2대 도착 그리고 다리 개방·네모/구/세모 횡단·소멸 모두 통과 — 실패: " + string.Join(" / ", failParts)
                              + (naParts.Count > 0 ? " (검사불가: " + string.Join(" / ", naParts) + ")" : "");
            }
            else if (naParts.Count > 0)
            {
                item.Verdict = Verdict.NA; item.Decided = true;
                item.Reason = "S1-4 = 카트 2대 도착 그리고 다리 개방·네모/구/세모 횡단·소멸 모두 통과 — 검사불가: " + string.Join(" / ", naParts);
            }
            else
            {
                item.Verdict = Verdict.Pass; item.Decided = true;
                item.Reason = cartReason + " + 무지개다리 개방·네모·구·세모 도보 횡단·발판 해제 시 소멸 모두 통과(하위 결과 5개)";
            }
        }

        private IEnumerator S1_4b_BridgeSteps()
        {
            const string id = "S1-4";
            const string kOpen = "bridgeOpen", kCross = "bridgeCross", kClose = "bridgeClose";
            const string kCrossS = "bridgeCrossSphere", kCrossT = "bridgeCrossTetra";
            const string nOpen = "다리 개방(발판 점유 시)", nCross = "네모 도보 횡단", nClose = "발판 해제 시 소멸";
            const string nCrossS = "구 도보 횡단", nCrossT = "세모 도보 횡단";
            if (s1 == null) yield break; // S1-4가 이미 사유를 적었다.

            RainbowBridgeSwitch sw = s1.Bridge;
            if (sw == null || sw.targetObjects == null || sw.targetObjects.Length == 0)
            {
                // 10-04 설계는 다리가 필수(카트 2대로는 세 번째 도형이 못 건넌다) — 없으면 검사불가가 아니라 실패.
                string why = $"무지개다리 발판/조각 없음({s1.BridgeProblem ?? "targetObjects 비어 있음"}) — S1_Builder가 다리를 만들지 않았거나 씬이 옛 3대 판(Generate 다시 필요)";
                report.Get(id).SetSub(kOpen, nOpen, SubStatus.Fail, why);
                report.Get(id).SetSub(kCross, nCross, SubStatus.NA, "다리 없음");
                report.Get(id).SetSub(kClose, nClose, SubStatus.NA, "다리 없음");
                yield break;
            }

            List<Collider> cols = new List<Collider>();
            foreach (GameObject t in sw.targetObjects)
                if (t != null) cols.AddRange(t.GetComponentsInChildren<Collider>(true));
            if (cols.Count == 0)
            {
                report.Get(id).SetSub(kOpen, nOpen, SubStatus.Fail, "다리 조각에 Collider 0개");
                report.Get(id).SetSub(kCross, nCross, SubStatus.NA, "조각 콜라이더 없음");
                report.Get(id).SetSub(kClose, nClose, SubStatus.NA, "조각 콜라이더 없음");
                yield break;
            }

            Transform gen = s1.Gen;
            PlayerMover holder = tetra, walker = cube;
            if (holder == null || walker == null || sphere == null)
            {
                report.Get(id).SetSub(kOpen, nOpen, SubStatus.NA, "세모/네모 도형 없음");
                report.Get(id).SetSub(kCross, nCross, SubStatus.NA, "세모/네모 도형 없음");
                report.Get(id).SetSub(kClose, nClose, SubStatus.NA, "세모/네모 도형 없음");
                yield break;
            }

            Collider padCol = sw.GetComponent<Collider>();
            Vector3 padCenter = padCol != null ? padCol.bounds.center : sw.transform.position;
            Vector3 padLocal = L1(padCenter);
            bool padInHoldMode = sw.activatorRequiresHold;
            Vector3 midLocal = new Vector3(S1BridgeX, 2f, (S1BridgeZ0 + S1BridgeZ1) / 2f + 1f); // 다리 한 판 가운데 z 154 옆 z 155(10-04: 조각 이음매는 없어졌다)
            Vector3 midW = gen.TransformPoint(midLocal);
            Detail(id, $"[다리] 발판 '{sw.name}' local{V(padLocal)} 협동(누르는 동안 유지)={padInHoldMode} 조각 {sw.targetObjects.Length}개 콜라이더 {cols.Count}개 fadeDuration {sw.fadeDuration:F2}s");

            // ── ① 처음엔 닫힘 ──
            int onAtStart = cols.Count(c => c != null && c.enabled);
            bool closedHit = Physics.Raycast(midW, -gen.up, out RaycastHit h0, 8f, ~0, QueryTriggerInteraction.Ignore);
            bool startClosed = onAtStart == 0 && !closedHit;
            Detail(id, $"[다리] 처음: 조각 콜라이더 켜짐 {onAtStart}/{cols.Count}, 가운데 local{V(midLocal)} 아래 8U 광선 {(closedHit ? $"'{h0.collider.name}'에 맞음" : "맞는 것 없음")} → {(startClosed ? "닫힘" : "닫혀 있지 않음")}");

            // 발판 점유(세모) — 래치 아님(협동): 올려 둔 동안만 유지.
            yield return WaitIdle(holder, 5f);
            Teleport(holder, GroundedSpot(padCenter + Vector3.up * 2f, 6f, null, padCenter));
            float t0 = Time.time;
            while (cols.Any(c => c != null && !c.enabled) && Time.time - t0 < 3f) yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate(); // 새로 켜진 콜라이더가 물리 씬 질의(Raycast)에 반영될 시간
            yield return new WaitForFixedUpdate();
            int onAfter = cols.Count(c => c != null && c.enabled);
            bool openedAll = onAfter == cols.Count(c => c != null);
            bool supportHit = Physics.Raycast(midW, -gen.up, out RaycastHit h1, 8f, ~0, QueryTriggerInteraction.Ignore);
            float topLocalY = supportHit ? gen.InverseTransformPoint(h1.point).y : float.NaN;
            bool supportOk = supportHit && cols.Contains(h1.collider) && Mathf.Abs(topLocalY) <= 0.05f;
            Detail(id, $"[다리] 세모를 발판에 올림 → {Time.time - t0:F2}s 뒤 조각 콜라이더 켜짐 {onAfter}/{cols.Count}, 가운데 아래 광선 {(supportHit ? $"'{h1.collider.name}' 윗면 local y{topLocalY:F2}" : "맞는 것 없음")}");

            bool openOk = padInHoldMode && startClosed && openedAll && supportOk;
            string openWhy = openOk
                ? $"처음 닫힘(콜라이더 0/{cols.Count}) → 발판 점유 {Time.time - t0:F1}s 안에 {onAfter}/{cols.Count}조각 열림, 윗면 지지(local y{topLocalY:F2})"
                : "실패 사유: " + string.Join(" / ", new[]
                {
                    padInHoldMode ? null : "activatorRequiresHold=false(협동 아님)",
                    startClosed ? null : $"처음부터 열려 있음(켜진 콜라이더 {onAtStart}, 광선 {(closedHit ? "맞음" : "안 맞음")})",
                    openedAll ? null : $"발판을 점유해도 {onAfter}/{cols.Count}조각만 열림(3초)",
                    supportOk ? null : $"열렸는데 윗면 지지면이 로컬 y 0이 아님({(supportHit ? $"'{h1.collider.name}' y{topLocalY:F2}" : "광선이 아무것도 못 맞힘")})",
                }.Where(s => s != null));
            report.Get(id).SetSub(kOpen, nOpen, openOk ? SubStatus.Pass : SubStatus.Fail, openWhy);

            // ── ② 점유가 유지되는 동안 세 도형이 차례로 걷는다 — 네모(가장 느림, 기존)·구·세모 [10-04 L2: 구·세모 표본 추가] ──
            // 세 횡단 모두 하위 결과이고 S1-4 판정에 합산된다(S1_4_PromoteVerdict): 구·세모가 실패하면 S1-4도 실패. 판정 기준(도착 z ≥ 169.5, 추락 복귀 0)은 네모와 같다.
            // 각 횡단은 S1_BridgeCrossTrial — 점유 도형을 발판에 올려 열린 것을 확인한 뒤 DriveShape로 몬다(평균 속도·가장 느린 0.1초 구간 기록). 네모·구는 세모가 점유하고,
            // 세모가 건널 때는 세모를 치우고 네모가 발판을 맡는다(발판을 비운 사이 다리가 닫혔다 다시 열려도 아무도 다리 위에 없다).
            if (!openedAll)
            {
                report.Get(id).SetSub(kCross, nCross, SubStatus.NA, "다리가 열리지 않아 건널 수 없음(①)");
                report.Get(id).SetSub(kCrossS, nCrossS, SubStatus.NA, "다리가 열리지 않아 건널 수 없음(①)");
                report.Get(id).SetSub(kCrossT, nCrossT, SubStatus.NA, "다리가 열리지 않아 건널 수 없음(①)");
            }
            else
            {
                yield return S1_BridgeCrossTrial(gen, cube, tetra, padCenter, cols, kCross, nCross, "네모");
                yield return S1_BridgeCrossTrial(gen, sphere, tetra, padCenter, cols, kCrossS, nCrossS, "구");
                yield return S1_BridgeCrossTrial(gen, tetra, cube, padCenter, cols, kCrossT, nCrossT, "세모");
            }

            // ── ③ 발판을 비우면 사라진다 ──
            // [10-04 S1-4b 수정 — 검사 도구 원인 확정(T3 FUNC_20261004_082156 :29-44)] 횡단한 네모가 끝 local(6.00, 0.00, 169.54)에 멈춰 발판
            // 'S1_RainbowBridge_Switch' local(6.00, 0.05, 170.00) 위에 서 있었다 — 세모만 치우면 발판이 계속 눌려 다리가 유지된다(팀 협동 모드는 정상).
            // 그래서 소멸을 재기 전에 세 도형(세모·네모·구) 모두를 발판 트리거에서 떨어진 파킹 자리로 옮기고, 발판과 겹치는 도형이 0인지 확인해 적는다.
            // 소멸 기준(3초)·횡단 단계의 도착 판정(z ≥ 169.5)은 그대로다.
            yield return WaitIdle(holder, 5f);
            yield return WaitIdle(walker, 5f);
            yield return ParkPlayers(s1.Park, sphere, cube, tetra); // 세 도형 전부 파킹 자리(도착 플랫폼 안, 발판에서 8U 이상)로
            List<string> onPad = new List<string>();
            foreach (PlayerMover p in players)
            {
                if (p == null) continue;
                Bounds pb = SolidBounds(p);
                bool overlaps = padCol != null
                    ? padCol.bounds.Intersects(pb)
                    : HorizDist(p.transform.position, padCenter) < 1.0f && Mathf.Abs(p.transform.position.y - padCenter.y) < 1.5f; // 발판 콜라이더 없음 — 중심 근접으로 대신
                if (overlaps) onPad.Add($"'{p.name}' local{V(L1(p.transform.position))}");
            }
            Detail(id, $"[다리] 소멸 시험 전 세 도형을 발판 local{V(padLocal)}에서 치움 → 발판 트리거와 겹치는 도형 {onPad.Count}개" +
                       (onPad.Count > 0 ? $" [{string.Join(", ", onPad)}] — 발판이 계속 눌려 다리가 유지될 수 있다" : ""));
            float t1 = Time.time;
            while (cols.Any(c => c != null && c.enabled) && Time.time - t1 < 3f) yield return new WaitForFixedUpdate();
            int onEnd = cols.Count(c => c != null && c.enabled);
            bool closedAgain = onEnd == 0;
            Detail(id, $"[다리] 세 도형을 발판에서 치움 → {Time.time - t1:F2}s 뒤 조각 콜라이더 켜짐 {onEnd}/{cols.Count}");
            if (!openedAll) report.Get(id).SetSub(kClose, nClose, SubStatus.NA, "다리가 열리지 않아 소멸을 확인할 수 없음(①)");
            else report.Get(id).SetSub(kClose, nClose, closedAgain ? SubStatus.Pass : SubStatus.Fail,
                closedAgain ? $"발판을 비운 뒤 {Time.time - t1:F1}s 안에 콜라이더 0/{cols.Count}(협동 = 밟는 동안만 유지)"
                            : $"발판을 비운 지 3초가 지나도 콜라이더 {onEnd}/{cols.Count} 켜져 있음");
        }

        /// <summary>[10-04 L2] 다리 횡단 표본 1회 — who가 holderShape가 발판을 밟은 동안 승차장 쪽 다리 머리 앞(z 138.5)에서 +Z로 걸어 도착 플랫폼(z ≥ 169.5)에 닿는지.
        /// 순서: ①나머지 도형·who를 파킹 자리로 치움 → ②holderShape를 발판 위에 순간이동해 모든 조각 콜라이더가 켜질 때까지(3초) 기다림(못 켜지면 실패)
        /// → ③who를 다리 머리 앞으로 순간이동해 DriveShape(속도 = 도형의 moveSpeed)로 몲 → ④판정·기록 → ⑤who를 파킹. 점유 도형을 매번 다시 올려서
        /// 앞 도형이 끝점에서 부딪쳐 점유 도형을 밀어낸 경우나 세모가 건널 때의 점유 교대를 같은 절차로 다룬다.
        /// 기록(하위 결과 key): 통과/실패, 평균 속도(진행 ÷ 시간)와 목표 속도 대비 %, 가장 느린 0.1초 구간 속도와 그 위치(섹터 로컬 z — 10-04 다리 한 판이라 이음매는 승차장↔다리 z 140·다리↔플랫폼 z 168 두 곳뿐, 그 근처에서 느려지는지 보려는 것).
        /// 판정 기준 = 도달(z ≥ 169.5) + 추락 복귀 0 + 끝 y > −0.5. 속도 값은 기록만 한다(기준 없음).</summary>
        private IEnumerator S1_BridgeCrossTrial(Transform gen, PlayerMover who, PlayerMover holderShape, Vector3 padCenter, List<Collider> cols, string key, string name, string label)
        {
            const string id = "S1-4";
            yield return WaitIdle(who, 5f);
            yield return WaitIdle(holderShape, 5f);
            foreach (PlayerMover other in players.Where(p => p != null && p != who && p != holderShape).ToList())
                yield return ParkPlayers(s1.Park, other);
            yield return ParkPlayers(s1.Park, who);
            Teleport(holderShape, GroundedSpot(padCenter + Vector3.up * 2f, 6f, null, padCenter));
            float t0 = Time.time;
            while (cols.Any(c => c != null && !c.enabled) && Time.time - t0 < 3f) yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate(); // 켜진 콜라이더가 물리 질의에 반영될 시간
            yield return new WaitForFixedUpdate();
            int total = cols.Count(c => c != null);
            int on = cols.Count(c => c != null && c.enabled);
            if (on != total)
            {
                Detail(id, $"[다리] {label} 횡단 전 {holderShape.name}을 발판에 올림 → 3초 뒤 조각 콜라이더 켜짐 {on}/{total} — 횡단 못 함");
                report.Get(id).SetSub(key, name, SubStatus.Fail, $"{holderShape.name}을 발판에 올렸는데 3초 안에 조각 콜라이더 {on}/{total}만 켜져 {label}이 건널 수 없음");
                yield break;
            }

            Vector3 headW = gen.TransformPoint(new Vector3(S1BridgeX, 0f, S1BridgeZ0 - 1.5f)); // 승차장 쪽 다리 머리 1.5U 앞
            Teleport(who, GroundedSpot(headW + gen.up * 3f, 6f, null, headW));
            yield return new WaitForSeconds(0.3f);
            // [10-04 T5 진단 — 증거만 남긴다, 판정 로직 불변] 출발 직전 상태: 챕터·횡단 도형·점유 도형.
            Detail(id, $"[진단 {label} 출발 전] 챕터: {ChapterProbe()}");
            Detail(id, $"[진단 {label} 출발 전] 횡단 도형 {DiagShape(who)}");
            Detail(id, $"[진단 {label} 출발 전] 점유 도형 {DiagShape(holderShape)}");
            float since = Time.time;
            DriveResult res = new DriveResult();
            float speed = who.moveSpeed;
            yield return DriveShape(who, gen, gen.forward, speed, 30f, l => l.z >= S1BridgeZ1 + 1.5f, res,
                () => Detail(id, $"[진단 {label} 정체 ≥1.5s — 진행 {res.Progress:F2}U] 횡단 도형 {DiagShape(who)}"));
            bool fell = RespawnsSince(who, since) > 0;
            Vector3 endL = res.EndLocal;
            bool ok = res.Reached && !fell && endL.y > -0.5f;
            float avg = res.Elapsed > 0.01f ? res.Progress / res.Elapsed : 0f;
            string minTxt = float.IsNaN(res.MinWindowSpeed) ? "측정 안 됨" : $"{res.MinWindowSpeed:F2}U/s(local z{res.MinWindowAtLocal.z:F1})";
            string speedTxt = $"평균 {avg:F2}U/s(목표 {speed:F1}의 {avg / Mathf.Max(0.01f, speed) * 100f:F0}%), 가장 느린 0.1초 구간 {minTxt}";
            if (!(res.Reached && !fell && endL.y > -0.5f)) Detail(id, $"[진단 {label} 끝(실패)] 횡단 도형 {DiagShape(who)} · 챕터: {ChapterProbe()}");
            Detail(id, $"[다리] {label}({speed:F1}U/s)를 {holderShape.name} 점유 중 다리 머리 앞에서 +Z로 걸림 → 도달={res.Reached} {res.Elapsed:F1}s 끝 local{V(endL)} 추락 복귀 {(fell ? "있음" : "없음")}, {speedTxt}");
            report.Get(id).SetSub(key, name, ok ? SubStatus.Pass : SubStatus.Fail,
                ok ? $"{label}({speed:F1}U/s)가 {res.Elapsed:F1}s 만에 28m 다리를 걸어 도착 플랫폼(z ≥ {S1BridgeZ1 + 1.5f:F1})에 닿음, 추락 복귀 0, {speedTxt}"
                   : $"도달={res.Reached}, 추락 복귀={(fell ? "있음(다리 위에서 떨어짐)" : "없음")}, 끝 local{V(endL)}, {speedTxt} — 위 [다리] 기록 참고");
            yield return WaitIdle(who, 5f);
            yield return ParkPlayers(s1.Park, who);
        }

        // ───────────── S1-5 ─────────────

        private IEnumerator S1_5_ExitDoorOpens()
        {
            const string id = "S1-5";
            if (!Ready1(id)) yield break;
            PathChaserController ch = s1.Chaser;
            if (s1.Door == null) { NA(id, (s1.DoorProblem != null && s1.DoorProblem.Contains("이름 불일치") ? "검사불가(이름 불일치): " : "출구 문 없음: ") + s1.DoorProblem); yield break; }
            if (s1.ExitZone == null) { NA(id, "chaser.teamExitZone 비어 있음 — 전원 도착 조건을 만들 수 없음"); yield break; }
            if (!s1CartsArrived) { NA(id, "선행 S1-4(카트 2대 도착) 미통과 — 조건을 만들 수 없음"); yield break; }
            if (ch.wall != null && ch.wall.gameObject.activeInHierarchy) { NA(id, "파괴벽이 남아 있음(S1-3 미통과) — 추격자가 종점에 가지 못해 종료 판정이 돌지 않음"); yield break; }
            if (!ch.enabled || !ch.gameObject.activeInHierarchy) { Fail(id, "추격 컨트롤러가 동작하지 않음(시작 거부/비활성) — OnChapterCleared가 발화할 수 없음"); yield break; }

            Detail(id, $"OnChapterCleared 영구 배선: {PersistentSummary(ch.OnChapterCleared)}");
            bool cleared = false;
            float clearedAt = -1f;
            UnityAction onCleared = () => { cleared = true; clearedAt = Time.time; };
            if (ch.OnChapterCleared != null)
            {
                ch.OnChapterCleared.AddListener(onCleared);
                cleanups.Add(() => { if (ch != null && ch.OnChapterCleared != null) ch.OnChapterCleared.RemoveListener(onCleared); });
            }
            // [10-04 T5 진단] S1-5 시작 시점(도형을 파킹하기 전) 챕터 상태 — 종료 신호가 S1-4 중에 이미 발화했다면 아래 doorY0가 이미 열린 문 높이일 수 있다.
            Detail(id, $"[진단 S1-5 시작] OnChapterCleared S1-4 중 발화 {(s1ClearedAt >= 0f ? $"예(S1-4 시작 +{s1ClearedAt - s1Stage4Start:F1}s)" : "아니오")} · {ChapterProbe()} · 문 시작 y(doorY0) local {L1(s1.Door.transform.position).y:F2}");
            float doorY0 = s1.Door.transform.position.y;
            yield return ParkPlayers(s1.Park, sphere, cube, tetra);
            yield return new WaitForSeconds(0.5f);
            Detail(id, $"세 도형을 TeamExitZone 안에 둠(세 번째 도형이 다리로 건너는 흐름은 S1-4 판정에 합산된 하위 결과 — {SubSummary("S1-4")}) → AllPlayersInside={s1.ExitZone.AllPlayersInside()}");
            // [10-04 T5 진단 — 증거만, 판정 로직 불변] 도형별 상태 + TeamExitZone 판정(팀 TeamExitZone.cs 조건대로 도형마다 안/밖과 이유).
            foreach (PlayerMover dm in new[] { sphere, cube, tetra }) Detail(id, "[진단 도형] " + DiagShape(dm));
            Detail(id, "[진단 구역] " + DiagExitZone(s1.ExitZone));
            Detail(id, "[진단 챕터] " + ChapterProbe());

            float t0 = Time.time;
            float nextLog = Time.time + 15f;
            float rise = 0f;
            while (Time.time - t0 < 150f)
            {
                rise = s1.Door.transform.position.y - doorY0;
                if (rise >= 1f) break;
                // 누가 다른 곳으로 복귀됐으면 다시 구역 안으로(추격자 종점 도달을 기다리는 동안).
                if (!s1.ExitZone.AllPlayersInside() && players.All(p => !IsBusy(p)))
                    yield return ParkPlayers(s1.Park, sphere, cube, tetra);
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 15f;
                    Detail(id, $"+{Time.time - t0:F0}s 추격자 local{V(L1(ch.agent != null ? ch.agent.transform.position : ch.transform.position))} " +
                               $"agentActive={(ch.agent != null && ch.agent.gameObject.activeInHierarchy)} cleared={cleared} 문 상승 {rise:F2}");
                }
                yield return new WaitForFixedUpdate();
            }
            Detail(id, $"OnChapterCleared 발화={cleared}{(cleared ? $"(+{clearedAt - t0:F1}s)" : "")}, 문 '{s1.Door.name}' 상승 {rise:F2}U (doorTargetYOffset {s1.Door.doorTargetYOffset})");
            if (rise < 1f)
            {
                Detail(id, "[진단 종료 시점] " + ChapterProbe());
                foreach (PlayerMover dm in new[] { sphere, cube, tetra }) Detail(id, "[진단 종료 시점 도형] " + DiagShape(dm));
                Detail(id, "[진단 종료 시점 구역] " + DiagExitZone(s1.ExitZone));
            }
            if (rise >= 1f) Pass(id, $"카트 {ch.carts.Length}대 도착 + 전원 도착 → 종료 신호 → 출구 문 {rise:F2}U 상승(열림)");
            else if (cleared) Fail(id, "종료 신호(OnChapterCleared)는 발화했으나 문이 올라가지 않음(문 배선/doorPhysics 확인)");
            else Fail(id, "150초 안에 종료 신호 없음 — 추격자 종점 미도달 또는 조건 미충족(위 경과 기록 참고)");
        }

        // ───────────── S5 해석·준비 ─────────────

        private IEnumerator ResolveS5()
        {
            if (!director.TryGetSector(5, out SectorController sc) || sc == null) { s5Problem = "섹터 5가 로드되지 않음"; yield break; }
            S5Ctx c = new S5Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;

            c.Spikes.AddRange(SortByLocal(InScene<SpikeTrap>(c.Scene), c.Gen));
            c.Hammers.AddRange(SortByLocal(InScene<HammerTrap>(c.Scene), c.Gen));
            c.Axes.AddRange(SortByLocal(InScene<AxeTrap>(c.Scene), c.Gen));
            c.Lasers.AddRange(SortByLocal(InScene<FixedPeriodicLaser>(c.Scene), c.Gen));
            c.Launchers.AddRange(SortByLocal(InScene<ProjectileLauncher>(c.Scene), c.Gen));
            c.Bridges.AddRange(InScene<StepRotatingBridge>(c.Scene).OrderBy(b => c.Gen.InverseTransformPoint(b.transform.position).x));
            int hz = c.AllHazards().Count;
            report.Notes.Add($"S5 해석: 도끼 {c.Axes.Count}/3 · 망치 {c.Hammers.Count}/2 · 가시 {c.Spikes.Count}/2 · 레이저 {c.Lasers.Count}/2 · 발사구 {c.Launchers.Count}/2 (합 {hz}/11), 회전 다리 {c.Bridges.Count}/2");
            if (hz == 0 && c.Bridges.Count == 0)
            {
                s5Problem = "S5 섹터 씬에 팀 기믹 0개 — S5 미구체화(S5_Builder 미등록 또는 전제 불일치로 빈 틀)";
                yield break;
            }

            List<SectionSafePoint> sps = InScene<SectionSafePoint>(c.Scene);
            c.Start = sps.FirstOrDefault(p => p.sectionId == "CH5_START" || p.name == "CH5_START")
                   ?? sps.FirstOrDefault(p => (p.sectionId != null && p.sectionId.Contains("CH5_START")) || p.name.Contains("CH5_START"));
            if (c.Start == null)
                c.StartProblem = $"이름 불일치 — SectionSafePoint {sps.Count}개 중 이름/sectionId 'CH5_START' 없음 [{string.Join(", ", sps.Select(p => p.name + "/" + p.sectionId))}]";
            AddName("S5 CH5 시작 안전점", "CH5_START", c.Start != null ? $"{c.Start.name}/{c.Start.sectionId}" : "(없음)", c.Start != null);

            Transform group = c.Gen.Find("S5_Respawn");
            AddName("S5 복귀 그룹", "S5_Respawn", group != null ? group.name : "(Generated 아래 없음)", group != null);
            List<Lab_SectionRespawnBridge> brs = InScene<Lab_SectionRespawnBridge>(c.Scene);
            AddName("S5 복귀 다리(Lab_SectionRespawnBridge)", "ADAPT_S5_RespawnBridge", brs.Count > 0 ? brs[0].name : "(없음)");
            report.Notes.Add($"S5: Lab_SectionRespawnBridge {brs.Count}개" + (brs.Count > 0 ? $"(연결 카운터 {brs[0].WiredCount}개)" : " — 피격 복귀가 RespawnController로 이어지지 않는다"));
            List<SectionHitCounter> counters = InScene<SectionHitCounter>(c.Scene);
            report.Notes.Add($"S5: SectionHitCounter {counters.Count}개 " + string.Join(", ", counters.Select(k => $"'{k.name}' 임계 {k.hitsBeforeRespawn} 목적지 {Nm(k.destination)}")));

            List<RespawnZone> zones = InScene<RespawnZone>(c.Scene);
            Vector3 aCenter = c.Gen.TransformPoint(new Vector3(S5PlatformA.center.x, 0f, S5PlatformA.center.y));
            c.Zone = zones.OrderBy(z => Vector3.Distance(z.transform.position, aCenter)).FirstOrDefault();
            report.Notes.Add($"S5: RespawnZone {zones.Count}개" + (c.Zone != null ? $" — '{c.Zone.name}' local{V(c.Gen.InverseTransformPoint(c.Zone.transform.position))}" : ""));
            List<OutOfBoundsVolume> oobs = InScene<OutOfBoundsVolume>(c.Scene);
            report.Notes.Add($"S5: OutOfBoundsVolume {oobs.Count}개" + string.Join("", oobs.Select(o => { Collider oc = o.GetComponent<Collider>(); return oc == null ? "" : $" [{o.name} local min{V(c.Gen.InverseTransformPoint(oc.bounds.min))} max{V(c.Gen.InverseTransformPoint(oc.bounds.max))}]"; })));

            c.Park = S5ParkSpots(c);
            s5 = c;
            yield break;
        }

        private Vector3[] S5ParkSpots(S5Ctx c)
        {
            List<Vector3> spots = new List<Vector3>();
            List<Vector3> starts = c.Start != null ? SafePoints(c.Start) : new List<Vector3>();
            foreach (Vector3 l in S5ParkCandidatesLocal)
            {
                Vector3 w = c.Gen.TransformPoint(l);
                if (!TryGround(w + Vector3.up * 5f, 8f, null, 0f, out Vector3 g)) continue;
                if (Mathf.Abs(c.Gen.InverseTransformPoint(g).y) > 0.6f) continue;
                if (starts.Any(s => HorizDist(s, g) < 3f)) continue;
                spots.Add(g + Vector3.up * SpawnLift);
                if (spots.Count == 3) break;
            }
            if (spots.Count < 3) report.Notes.Add($"S5: 파킹점 {spots.Count}/3 — 후보 지점 아래에 바닥이 없음(플랫폼 좌표 확인)");
            return spots.ToArray();
        }

        private bool Ready5(string id)
        {
            if (s5 != null && s5.Park != null && s5.Park.Length > 0) return true;
            report.Get(id).Set(Verdict.NA, s5Problem ?? "S5 파킹점(플랫폼 바닥) 없음 — 도형을 치울 곳이 없어 검사 불가");
            return false;
        }

        /// <summary>S5 공용 체크포인트(A의 RespawnZone)를 잡아 두고 세 도형을 F 도착 플랫폼으로 치운다.</summary>
        private IEnumerator S5_Prepare()
        {
            if (s5 == null) yield break;
            if (s5.Zone != null)
            {
                // 팀 공개 메서드 RespawnZone.FindGroundPoint() — 구역 중앙 아래 바닥(자기 계층 제외)을 팀 방식으로 찾는다.
                // (S5-W 보고: 구역 높이 18·center y 9라 임의 레이 길이로는 바닥에 못 닿을 수 있다.)
                Vector3 spot = s5.Zone.FindGroundPoint() + Vector3.up * SpawnLift;
                // 체크포인트는 공용 하나라 한 도형만 들어가도 갱신된다(RespawnController.StoreCheckpoint).
                yield return WaitIdle(sphere, 5f);
                Teleport(sphere, spot);
                yield return new WaitForSeconds(0.6f);
                report.Notes.Add($"S5 준비: 구를 RespawnZone '{s5.Zone.name}'에 넣어 공용 체크포인트를 S5로 갱신(콘솔 '[Respawn] 체크포인트 갱신' 로그로 확인)");
            }
            else report.Notes.Add("S5 준비: RespawnZone 없음 — 공용 체크포인트가 S5로 갱신되지 않음");
            if (s5.Park != null && s5.Park.Length > 0) yield return ParkPlayers(s5.Park, sphere, cube, tetra);
        }

        // ───────────── S5-4 ─────────────

        private IEnumerator S5_4_TrapsCycle()
        {
            const string id = "S5-4";
            if (!Ready5(id)) yield break;
            List<Component> all = s5.AllHazards();
            if (all.Count == 0) { NA(id, "함정(도끼·망치·가시·레이저·발사구) 0개"); yield break; }
            if (all.Count != 11) Detail(id, $"주의: 함정 {all.Count}개(계약 C2 기대 11)");
            yield return ParkPlayers(s5.Park, sphere, cube, tetra);
            float minD = float.MaxValue;
            foreach (Component h in all) foreach (PlayerMover p in players) minD = Mathf.Min(minD, Vector3.Distance(h.transform.position, p.transform.position));
            Detail(id, $"플레이어-함정 최소 거리 {minD:F1}U(플레이어 없음 조건)");

            List<CycleTracker> trackers = all.Select(h => new CycleTracker(h)).ToList();
            List<ProjectileLauncher> launchers = s5.Launchers;
            HashSet<int> seen = new HashSet<int>(Object.FindObjectsOfType<Projectile>().Select(p => p.GetInstanceID()));
            float t0 = Time.time;
            // [구현 결정] 9.5초 — 가장 긴 주기(레이저 1+0.4+2 = 3.4s [팀])의 2회 반 이상.
            // [S5 10-04] 망치가 낙하 11.8U·낙하 25·상승 8U/s로 바뀌어 한 주기가 약 4.35초(대기 2 + 낙하 0.47 + 저점 0.4 + 상승 1.48)다 —
            // 9.5초는 2.2주기뿐이라 가장 긴 주기를 함정 공개 필드로 계산해 그 2.5배(최소 9.5초)로 늘린다. 합격 기준(2회 이상)은 그대로.
            float observe = S5ObserveSeconds(all);
            while (Time.time - t0 < observe)
            {
                yield return new WaitForFixedUpdate();
                foreach (CycleTracker tr in trackers) tr.Sample();
                foreach (Projectile pr in Object.FindObjectsOfType<Projectile>())
                {
                    if (!seen.Add(pr.GetInstanceID())) continue;
                    ProjectileLauncher near = launchers.OrderBy(l => Vector3.Distance(l.transform.position, pr.transform.position)).FirstOrDefault();
                    if (near != null && Vector3.Distance(near.transform.position, pr.transform.position) <= 2f)
                    {
                        CycleTracker tr = trackers.FirstOrDefault(x => x.H == near);
                        if (tr != null) tr.Shots++;
                    }
                }
            }
            List<string> bad = new List<string>();
            foreach (CycleTracker tr in trackers)
            {
                bool ok = tr.Evaluate(out string info);
                Detail(id, $"{tr.Kind} '{tr.H.name}' local{V(s5.Gen.InverseTransformPoint(tr.H.transform.position))}: {info} → {(ok ? "반복" : "반복 안 됨")}");
                if (!ok) bad.Add($"{tr.Kind} '{tr.H.name}'({info})");
            }
            if (bad.Count == 0) Pass(id, $"함정 {trackers.Count}개 모두 플레이어 없이 {observe:F1}초 동안 2회 이상 주기 반복");
            else Fail(id, "주기 반복이 확인되지 않은 함정: " + string.Join(", ", bad));
        }

        /// <summary>[S5 10-04] S5-4 관찰 시간 = max(9.5초, 가장 긴 함정 주기 × 2.5). 주기는 팀 공개 필드로 계산한다
        /// (망치: 상부 대기 + 낙하 거리/낙하 속도 + 저점 대기 + 낙하 거리/상승 속도, 가시도 같은 꼴, 도끼: period,
        /// 레이저: 예고 + 발사 + 휴식, 발사구: 예고 + 휴식). 속도가 0 이하면 0.01로 막아 0 나눗셈을 피한다.</summary>
        private static float S5ObserveSeconds(List<Component> hazards)
        {
            float longest = 0f;
            foreach (Component h in hazards)
            {
                float period = 0f;
                if (h is HammerTrap hm)
                    period = hm.topWaitSeconds + hm.dropDistance / Mathf.Max(0.01f, hm.descendSpeed) + hm.bottomWaitSeconds + hm.dropDistance / Mathf.Max(0.01f, hm.ascendSpeed);
                else if (h is SpikeTrap sp)
                    period = sp.retractedWaitSeconds + sp.popDistance / Mathf.Max(0.01f, sp.risingSpeed) + sp.extendedWaitSeconds + sp.popDistance / Mathf.Max(0.01f, sp.retractingSpeed);
                else if (h is AxeTrap ax) period = ax.period;
                else if (h is FixedPeriodicLaser lz) period = lz.warningSeconds + lz.beamSeconds + lz.restSeconds;
                else if (h is ProjectileLauncher pl) period = pl.warningSeconds + pl.restSeconds;
                longest = Mathf.Max(longest, period);
            }
            return Mathf.Max(9.5f, longest * 2.5f);
        }

        // ───────────── S5-1 ─────────────

        private IEnumerator S5_1_RotatingBridge()
        {
            const string id = "S5-1";
            if (!Ready5(id)) yield break;
            if (s5.Bridges.Count == 0) { NA(id, "StepRotatingBridge 0개"); yield break; }
            if (s5.Bridges.Count != 2) Detail(id, $"주의: 회전 다리 {s5.Bridges.Count}개(계약 C2 기대 2)");
            if (s5.Start == null) { NA(id, "검사불가(이름 불일치): " + s5.StartProblem); yield break; }
            StepRotatingBridge br = s5.Bridges[0];
            PlayerMover victim = cube;
            Detail(id, $"회전 다리 '{br.name}' local{V(s5.Gen.InverseTransformPoint(br.transform.position))} 각도 {br.rotationAngle}° 예고 {br.telegraphDuration}s 유지 {br.holdDuration}s, OnPlayerFell 배선: {PersistentSummary(br.OnPlayerFell)}");

            bool fell = false;
            UnityAction<GameObject> onFell = go => { if (Owner(go) == victim) fell = true; };
            if (br.OnPlayerFell != null)
            {
                br.OnPlayerFell.AddListener(onFell);
                cleanups.Add(() => { if (br != null && br.OnPlayerFell != null) br.OnPlayerFell.RemoveListener(onFell); });
            }

            yield return WaitIdle(victim, 5f);
            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(victim));
            Quaternion rest = br.transform.rotation;
            Transform vis = br.visualRoot;
            Vector3 visRest = vis != null ? vis.localPosition : Vector3.zero;
            Collider sup = br.supportCollider != null ? br.supportCollider : FirstSolid(br.gameObject);
            if (sup == null) { Fail(id, "회전 다리 지지 콜라이더 없음"); yield break; }
            Bounds sb = sup.bounds;
            float since = Time.time;
            Teleport(victim, new Vector3(sb.center.x, sb.max.y + 0.55f, sb.center.z));
            float placedAt = Time.time;
            float telegraphAt = -1f, rotStart = -1f, reachedAt = -1f, returnedAt = -1f, maxAng = 0f;
            float target = Mathf.Abs(br.rotationAngle);
            while (Time.time - placedAt < 15f)
            {
                yield return new WaitForFixedUpdate();
                float ang = Quaternion.Angle(br.transform.rotation, rest);
                maxAng = Mathf.Max(maxAng, ang);
                if (telegraphAt < 0f && rotStart < 0f && vis != null && (vis.localPosition - visRest).sqrMagnitude > 1e-8f) telegraphAt = Time.time;
                if (rotStart < 0f && ang > 0.5f) rotStart = Time.time;
                if (reachedAt < 0f && ang >= target - 1f) reachedAt = Time.time;
                if (reachedAt > 0f && returnedAt < 0f && ang <= 0.5f) returnedAt = Time.time;
                bool respawned = RespawnsSince(victim, since) > 0;
                if (returnedAt > 0f)
                {
                    if (fell || respawned) { if (respawned && !IsBusy(victim)) break; }
                    else if (Time.time - returnedAt > 1.5f) break;
                }
            }
            yield return new WaitForSeconds(0.3f);
            bool resp = RespawnsSince(victim, since) > 0;
            bool telegraphOk = telegraphAt > 0f || (rotStart > 0f && rotStart - placedAt >= br.telegraphDuration * 0.8f);
            Detail(id, $"예고 {(telegraphAt > 0f ? $"떨림 +{telegraphAt - placedAt:F2}s" : "떨림 관찰 안 됨")}, 회전 시작 {(rotStart > 0f ? $"+{rotStart - placedAt:F2}s" : "없음")}, " +
                       $"최대 {maxAng:F1}°/{target}°, 복귀 {(returnedAt > 0f ? $"+{returnedAt - placedAt:F2}s" : "없음")}, OnPlayerFell={fell}, 복귀 이벤트={resp}");

            List<string> bad = new List<string>();
            if (!telegraphOk) bad.Add("예고 없이 회전(또는 회전 없음)");
            if (reachedAt < 0f) bad.Add($"목표 각도 미도달(최대 {maxAng:F1}°)");
            if (returnedAt < 0f) bad.Add("원래 자세로 복귀 안 함");
            string fallNote;
            if (fell || resp)
            {
                bool at = NearAny(victim.transform.position, SafePoints(s5.Start), 0.9f, 1.6f, out float d);
                fallNote = $"떨어짐 → 복귀 위치 CH5 시작(주·보조 중 가장 가까운 점)까지 수평 {d:F2}U({(at ? "CH5 시작" : "CH5 시작 아님")}), 경로={(fell ? "OnPlayerFell→카운터" : "장외/킬라인(OnPlayerFell 미발화)")}";
                if (!fell) bad.Add("판에서 떨어졌는데 OnPlayerFell 미발화");
                if (!resp) bad.Add("OnPlayerFell 발화했으나 복귀 없음(카운터→RespawnController 연결 확인)");
                else if (!at) bad.Add($"복귀 위치가 CH5 시작 아님(수평 {d:F2}U)");
            }
            else fallNote = "네모가 판에서 떨어지지 않음 — '(떨어지면) CH5 시작 복귀'는 이번 실행에서 발생하지 않음";
            Detail(id, fallNote);
            string others = CheckUnchanged(snap, since, 0.5f);
            if (others != null) { bad.Add("다른 도형 영향: " + others); }
            if (bad.Count == 0) Pass(id, $"예고→회전({maxAng:F0}°)→복귀 확인; {fallNote}");
            else Fail(id, string.Join(" / ", bad));
            yield return WaitIdle(victim, 5f);
            yield return ParkPlayers(s5.Park, victim);
        }

        // ───────────── S5-2 ─────────────

        private IEnumerator S5_2_HazardHits()
        {
            const string id = "S5-2";
            if (!Ready5(id)) yield break;
            if (s5.Start == null) { NA(id, "검사불가(이름 불일치): " + s5.StartProblem); yield break; }
            PlayerMover victim = cube;
            yield return WaitIdle(victim, 5f);
            yield return ParkPlayers(s5.Park, sphere, cube, tetra);
            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(victim));
            float since = Time.time;

            // 종류별 첫 번째(섹터 로컬 z·x 순) 1개씩. 11개 전부가 아닌 이유: 워치독 10분 안의 시간 예산.
            string[] kinds = { "가시", "망치", "도끼", "레이저", "발사구" };
            Component[] picks =
            {
                s5.Spikes.FirstOrDefault(), s5.Hammers.FirstOrDefault(), s5.Axes.FirstOrDefault(),
                s5.Lasers.FirstOrDefault(), s5.Launchers.FirstOrDefault()
            };
            List<string> bad = new List<string>(), na = new List<string>();
            int ok = 0;
            for (int k = 0; k < kinds.Length; k++)
            {
                if (picks[k] == null) { bad.Add(kinds[k] + ": 없음"); Detail(id, $"{kinds[k]}: 씬에 없음"); continue; }
                HitTrial r = new HitTrial();
                yield return TryHazard(picks[k], victim, r);
                string line = $"{kinds[k]} '{picks[k].name}': {r.Msg}";
                Detail(id, line);
                if (r.Inconclusive) na.Add(line); // [S5 10-04 M2] 가시 매설 상태를 못 만들었다 — 통과로 치지 않는다(판정 기준은 그대로)
                else if (r.Hit && r.Respawned && r.AtStart) ok++;
                else bad.Add(line);
                yield return ParkPlayers(s5.Park, victim);
            }
            string others = CheckUnchanged(snap, since, 0.5f);
            Detail(id, "다른 도형: " + (others ?? "위치 유지(0.5U 이내)·복귀 없음"));
            if (others != null) bad.Add("다른 도형 영향: " + others);
            if (bad.Count == 0 && na.Count == 0) Pass(id, "5종 모두 피격 → 맞은 네모만 CH5 시작 복귀, 다른 도형 유지");
            else if (bad.Count > 0) Fail(id, $"{ok}/5 종 정상 — " + string.Join(" / ", bad) + (na.Count > 0 ? " (검사불가: " + string.Join(" / ", na) + ")" : ""));
            else NA(id, $"{ok}/5 종 정상, 검사불가 {na.Count}종 — " + string.Join(" / ", na));
        }

        private IEnumerator TryHazard(Component hz, PlayerMover victim, HitTrial r)
        {
            UnityEvent<GameObject> ev = HazardEvent(hz);
            string wiring = ev != null ? PersistentSummary(ev) : "피격 이벤트 없음";
            if (!TryHazardPlacement(hz, out Vector3 place, out string how)) { r.Msg = $"배치점 계산 실패({how}); 배선 {wiring}"; yield break; }
            bool hit = false;
            // [S5 10-04 M2] 가시는 판 밑에 묻힌 상태에서 솟아 맞히는 경로를 시험한다 — 순간이동 전에 가시 콜라이더 윗면이 판 밑면 아래로 내려간 뒤에 도형을 판 위에 세운다.
            // 옛 시험은 가시가 이미 솟는 중(콜라이더 윗면 0.65U)에 도형을 겹쳐 놓아 "묻힌 가시가 판을 지나 솟아 친다"를 시험하지 못했다(검토 M2).
            SpikeTrap spike = hz as SpikeTrap;
            float topAtHit = float.NaN;
            UnityAction<GameObject> cb = go =>
            {
                if (Owner(go) != victim) return;
                hit = true;
                if (spike != null && spike.spikeCollider != null) topAtHit = spike.spikeCollider.bounds.max.y; // 피격 순간의 가시 콜라이더 윗면(월드 y)
            };
            if (ev != null) ev.AddListener(cb);
            // [10-04 L1] 앞 회차가 "시험 조건을 만들었는데 피격 없음"(진짜 실패)으로 끝났으면 그 메시지를 기억한다 — 뒤 회차가 검사불가여도 실패는 유지하고 사유를 이어 붙인다.
            string carriedFail = null;
            try
            {
                for (int attempt = 1; attempt <= 2 && !hit; attempt++)
                {
                    yield return WaitIdle(victim, 5f);
                    // [10-04 L1] 회차 사이에 피해 도형을 치운다 — 판 위에 서 있으면 팀 끼임 정지(PeriodicTrapBase.CanAdvance)가 가시 하강을 막아 매설 상태를 못 만든다.
                    if (attempt > 1) yield return ParkPlayers(s5.Park, victim);
                    string howNow = how;
                    float plankTopY = place.y - 0.55f; // TryHazardPlacement가 판 윗면 + 0.55에 둔다
                    float plankBottomY = float.NaN;
                    if (spike != null)
                    {
                        Collider sc = spike.spikeCollider != null ? spike.spikeCollider : FirstSolid(spike.gameObject);
                        plankBottomY = PlankBottomUnder(new Vector3(place.x, plankTopY, place.z), spike.transform);
                        if (sc == null || float.IsNaN(plankBottomY))
                        {
                            string inconclusive = $"{attempt}회차 검사불가: {(sc == null ? "가시 콜라이더 없음" : "가시 자리 아래 다리 판 밑면을 못 찾음")}; 배선 {wiring}";
                            // 앞 회차가 진짜 실패였으면 실패 유지(Inconclusive 안 켬) + 사유 이어 붙임, 아니면 검사불가.
                            if (carriedFail != null) r.Msg = carriedFail + " / " + inconclusive;
                            else { r.Inconclusive = true; r.Msg = inconclusive; }
                            yield break;
                        }
                        // 한 주기(수납 대기 + 상승 + 돌출 유지 + 하강, 공개 필드로 계산) + 여유 2초 안에 묻히지 않으면 검사불가.
                        float period = spike.retractedWaitSeconds + spike.popDistance / Mathf.Max(0.01f, spike.risingSpeed) + spike.extendedWaitSeconds + spike.popDistance / Mathf.Max(0.01f, spike.retractingSpeed);
                        float limit = period + 2f, w0 = Time.time;
                        while (sc.bounds.max.y > plankBottomY && Time.time - w0 < limit) yield return new WaitForFixedUpdate();
                        float topStart = sc.bounds.max.y;
                        if (topStart > plankBottomY)
                        {
                            string inconclusive2 = $"{attempt}회차 검사불가: {limit:F1}s(주기 {period:F2}s + 2s) 안에 가시 콜라이더 윗면이 판 밑면(판 윗면 대비 {plankBottomY - plankTopY:+0.00;-0.00}U) 아래로 내려가지 않음(지금 판 윗면 대비 {topStart - plankTopY:+0.00;-0.00}U); 배선 {wiring}";
                            // [10-04 L1] 앞 회차가 진짜 실패였으면 실패 유지(Inconclusive 안 켬) + 사유 이어 붙임, 아니면 검사불가.
                            if (carriedFail != null) r.Msg = carriedFail + " / " + inconclusive2;
                            else { r.Inconclusive = true; r.Msg = inconclusive2; }
                            yield break;
                        }
                        howNow = $"가시 자리 위 다리 판 윗면, 도형을 세운 순간 가시 콜라이더 윗면 판 윗면 대비 {topStart - plankTopY:+0.00;-0.00}U(판 밑면 {plankBottomY - plankTopY:+0.00;-0.00}U 아래 = 매설), 돌출 {spike.popDistance:F2}U";
                    }
                    float since = Time.time;
                    Teleport(victim, place);
                    // [구현 결정] 8초 — 가장 긴 주기(레이저 3.4s·망치 약 3.4s [팀])의 2배 이상.
                    // [S5 10-04] 망치는 약 4.35초(낙하 11.8U·25U/s·상승 8U/s)로 길어졌지만 8초는 1.8주기라 낙하가 한 번은 반드시 들어오고,
                    // 시도가 2회(attempt ≤ 2)라 판정 기준은 그대로다.
                    float deadline = Time.time + 8f;
                    while (!hit && Time.time < deadline && RespawnsSince(victim, since) == 0)
                        yield return new WaitForFixedUpdate();
                    if (hit)
                    {
                        float t = Time.time;
                        while (RespawnsSince(victim, since) == 0 && Time.time - t < 3f) yield return null;
                        r.Hit = true;
                        r.Respawned = RespawnsSince(victim, since) > 0;
                        yield return WaitIdle(victim, 4f);
                        yield return new WaitForSeconds(0.2f);
                        r.AtStart = r.Respawned && NearAny(victim.transform.position, SafePoints(s5.Start), 0.9f, 1.6f, out float d);
                        NearAny(victim.transform.position, SafePoints(s5.Start), 0.9f, 1.6f, out float dd);
                        string spikeLabel = "";
                        if (spike != null)
                        {
                            // 라벨은 측정값으로 가른다: 시작이 매설(위에서 확인)이고 피격 순간 가시 윗면이 판 윗면 근처 이상(콜라이더가 판을 지나 올라와 도형 발밑에 닿음)이면
                            // "매설 상태에서 솟아 피격", 아니면 실제 측정 상태를 그대로 적는다.
                            float rel = topAtHit - plankTopY;
                            spikeLabel = (!float.IsNaN(topAtHit) && rel >= -0.15f)
                                ? $" → 매설 상태에서 솟아 피격(피격 순간 가시 윗면 판 윗면 대비 {rel:+0.00;-0.00}U)"
                                : $" → 매설 상태에서 시작했으나 피격 순간 가시 윗면이 판 윗면 대비 {(float.IsNaN(topAtHit) ? "측정 못 함" : $"{rel:+0.00;-0.00}U")} — 솟아 닿은 피격으로 볼 수 없음(실제 상태)";
                        }
                        r.Msg = $"{attempt}회차 피격(+{t - since:F2}s, {howNow}{spikeLabel}) → 복귀 {(r.Respawned ? "있음" : "없음")}, CH5 시작(주·보조 중 가장 가까운 점)까지 수평 {dd:F2}U{(r.AtStart ? "" : " — CH5 시작 아님")}; 배선 {wiring}";
                    }
                    else if (RespawnsSince(victim, since) > 0)
                        r.Msg = $"{attempt}회차: 피격 이벤트 없이 복귀됨(배치 자리에서 추락 가능, {howNow}); 배선 {wiring}";
                    else
                        r.Msg = $"{attempt}회차: 8초 동안 피격 이벤트 없음({howNow}, 배치 local{V(s5.Gen.InverseTransformPoint(place))}); 배선 {wiring}";
                    if (!hit) carriedFail = r.Msg; // [10-04 L1] 시험 조건을 만들었는데 피격이 없었다 = 진짜 실패 — 다음 회차가 검사불가여도 지우지 않는다
                }
            }
            finally
            {
                if (ev != null) ev.RemoveListener(cb);
            }
        }

        private static UnityEvent<GameObject> HazardEvent(Component hz)
        {
            if (hz is PeriodicTrapBase p) return p.OnHazardHit;
            if (hz is ProjectileLauncher l) return l.OnHazardHit;
            if (hz is FixedPeriodicLaser f) return f.beam != null ? f.beam.OnHazardHit : null;
            return null;
        }

        /// <summary>[S5 10-04 M2] 판 윗면 한 점(plankTop) 바로 아래 다리 판의 밑면 월드 y — 위 0.2U에서 아래로 쏜 첫 비트리거 콜라이더(자기 계층·도형·탄 제외)의
        /// bounds.min.y. 판은 Y축 회전만 있어 AABB의 y 범위가 실제 두께와 같다. 못 찾으면 NaN.</summary>
        private static float PlankBottomUnder(Vector3 plankTop, Transform ignore)
        {
            RaycastHit[] hits = Physics.RaycastAll(plankTop + Vector3.up * 0.2f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                Rigidbody arb = h.collider.attachedRigidbody;
                if (arb != null && arb.GetComponent<PlayerMover>() != null) continue;
                if (h.collider.GetComponentInParent<Projectile>() != null) continue;
                return h.collider.bounds.min.y;
            }
            return float.NaN;
        }

        /// <summary>함정 종류별 "맞는 자리". 전부 팀 컴포넌트의 공개 콜라이더·Transform에서 계산한다(설계 좌표 불사용).</summary>
        private bool TryHazardPlacement(Component hz, out Vector3 place, out string how)
        {
            place = default;
            if (hz is SpikeTrap sp)
            {
                Collider c = sp.spikeCollider != null ? sp.spikeCollider : FirstSolid(sp.gameObject);
                if (c == null) { how = "가시 콜라이더 없음"; return false; }
                Bounds b = c.bounds;
                // [S5 10-04] 가시는 다리 판 밑에 묻혀 있다(수납 윗면 = 판 밑면 −0.01) — 옛 방식(가시 윗면 b.max.y + 0.55)은 판 속이 된다.
                // 가시 자리 바로 위 다리 판 윗면(가시 자기 계층은 제외)에 도형을 세운다. 돌출 윗면보다 위(+1)에서 아래로 쏴, 가시가 지금
                // 솟아 있어도 같은 판을 찾는다. 가시가 솟으면 도형 발밑을 친다 — 판정 기준(피격 → 그 도형만 CH5 시작 복귀)은 그대로다.
                Vector3 from = new Vector3(b.center.x, b.max.y + sp.popDistance + 1f, b.center.z);
                if (!TryGround(from, sp.popDistance + 8f, sp.transform, 0f, out Vector3 g)) { how = "가시 자리 위 다리 판 없음"; return false; }
                place = g + Vector3.up * 0.55f;
                // [S5 10-04 M2] 이 시점의 가시 윗면(b.max.y)은 그 순간 값일 뿐이라 "묻힘"을 단정하지 않는다 — 묻힌 상태는 TryHazard가 기다려 측정해 적는다.
                how = $"가시 자리 위 다리 판 윗면(배치 계산 시점 가시 콜라이더 윗면-판 윗면 {b.max.y - g.y:+0.00;-0.00}U, 돌출 {sp.popDistance:F2}U)";
                return true;
            }
            if (hz is HammerTrap hm)
            {
                Collider c = hm.headCollider != null ? hm.headCollider : FirstSolid(hm.gameObject);
                Vector3 from = c != null ? c.bounds.center : hm.transform.position;
                // [S5 10-04] 망치 대기 높이가 12U(머리 밑면)라 머리 중심에서 다리까지 최대 낙하 거리 + 머리 반두께 + 여유 8U를 쏜다(옛 고정 8U는 닿지 않는다).
                float reach = (c != null ? c.bounds.extents.y : 0.5f) + hm.dropDistance + 8f;
                if (!TryGround(from, reach, hm.transform, 0f, out Vector3 g)) { how = "망치 머리 아래 바닥 없음"; return false; }
                place = g + Vector3.up * 0.55f;
                how = $"망치 머리 밑 다리 위(머리 밑면-다리 {(c != null ? c.bounds.min.y - g.y : 0f):F2}U)";
                return true;
            }
            if (hz is AxeTrap ax)
            {
                if (!TryGround(ax.transform.position, 8f, ax.transform, 0f, out Vector3 g)) { how = "도끼 회전점 아래 바닥 없음"; return false; }
                place = g + Vector3.up * 0.55f;
                how = $"도끼 회전점 밑 다리 위(회전점 높이 {ax.transform.position.y - g.y:F2}U)";
                return true;
            }
            if (hz is FixedPeriodicLaser lz)
            {
                float range = lz.beam != null ? lz.beam.range : 20f;
                bool ok = TryBeamPlacement(lz.transform.position, lz.transform.forward, range, lz.transform, out place, out how);
                how = "레이저 광선 " + how;
                return ok;
            }
            if (hz is ProjectileLauncher pl)
            {
                float range = Mathf.Min(pl.projectileSpeed * pl.projectileLifetime, 40f);
                bool ok = TryBeamPlacement(pl.transform.position, pl.transform.forward, range, pl.transform, out place, out how);
                how = "발사구 사선 " + how;
                return ok;
            }
            how = "지원 안 하는 타입 " + hz.GetType().Name;
            return false;
        }

        /// <summary>사선(origin→dir) 아래에 0.1~1.2U 간격으로 얇은 바닥(두께 ≤3 — 기둥·두꺼운 블록 제외)이 이어지는
        /// 첫 구간(발사점에서 1.5U 이후, 샘플 5개 이상)의 가운데에 도형을 둔다.</summary>
        private bool TryBeamPlacement(Vector3 origin, Vector3 dir, float range, Transform ignore, out Vector3 place, out string how)
        {
            place = default;
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            List<Vector3> run = new List<Vector3>();
            float runStart = 0f;
            for (float s = 0.5f; s <= range; s += 0.25f)
            {
                Vector3 p = origin + d * s;
                Vector3 g = Vector3.zero;
                bool ok = HorizDist(p, origin) >= 1.5f && TryGround(p + Vector3.up * 0.3f, 1.6f, ignore, 3f, out g);
                ok = ok && p.y - g.y >= 0.1f && p.y - g.y <= 1.2f;
                if (ok)
                {
                    if (run.Count == 0) runStart = s;
                    run.Add(g);
                    continue;
                }
                if (run.Count >= 5) break;
                run.Clear();
            }
            if (run.Count < 5) { how = $"사거리 {range:F0}U 안에 광선 0.1~1.2U 아래 얇은 바닥 구간 없음"; return false; }
            place = run[run.Count / 2] + Vector3.up * 0.55f;
            how = $"발사점에서 {runStart:F1}~{runStart + (run.Count - 1) * 0.25f:F1}U 바닥 구간 가운데(광선-바닥 {origin.y + d.y * runStart - run[0].y:F2}U)";
            return true;
        }

        // ───────────── S5-3 ─────────────

        private IEnumerator S5_3_VoidFall()
        {
            const string id = "S5-3";
            if (!Ready5(id)) yield break;
            PlayerMover victim = sphere;
            yield return WaitIdle(victim, 5f);
            yield return ParkPlayers(s5.Park, sphere, cube, tetra);
            Vector3 drop = Vector3.zero; bool found = false;
            foreach (Vector3 l in S5VoidDropLocal)
            {
                Vector3 w = s5.Gen.TransformPoint(l);
                if (TryGround(w, 4f, null, 0f, out _) || Physics.CheckSphere(w, 0.7f, ~0, QueryTriggerInteraction.Ignore)) continue;
                drop = w; found = true; break;
            }
            if (!found) { NA(id, "허공 시험점을 찾지 못함(후보 지점 아래 4U 안에 지형 — 배치 확인)"); yield break; }
            string below = TryGround(drop, 80f, null, 0f, out Vector3 g) ? $"아래 {drop.y - g.y:F1}U에 바닥(local y{s5.Gen.InverseTransformPoint(g).y:F1})" : "아래 80U까지 바닥 없음";
            Detail(id, $"낙하 시작 local{V(s5.Gen.InverseTransformPoint(drop))} — {below}");
            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(victim));
            float since = Time.time;
            Teleport(victim, drop);
            float t = Time.time;
            while (RespawnsSince(victim, since) == 0 && Time.time - t < 20f) yield return null;
            if (RespawnsSince(victim, since) == 0)
            {
                Fail(id, $"20초 안에 복귀 없음 — 현재 local{V(s5.Gen.InverseTransformPoint(victim.transform.position))} ({below})");
                yield break;
            }
            yield return WaitIdle(victim, 6f);
            yield return new WaitForSeconds(0.5f);
            Vector3 lp = s5.Gen.InverseTransformPoint(victim.transform.position);
            // R2: 높이는 S1-6과 같은 기준(발밑 첫 지지면 = 플랫폼 윗면 로컬 y0±0.2, 밑면-지지면 ≤0.3)으로 본다(R1까지는 피벗 y −0.5~2.5).
            bool onFloorA = CheckSupport(victim, s5.Gen, CheckpointFloorLocalY, out string supportA);
            bool onA = S5PlatformA.Contains(new Vector2(lp.x, lp.z)) && onFloorA;
            Detail(id, "발밑: " + supportA);
            // R1: "CH5_START까지"는 주 안전점이 아니라 주·보조 중 가장 가까운 점까지였다 — 이름·주 안전점 거리를 따로 적는다.
            // 실제 복귀지(팀 RespawnController의 현재 체크포인트)는 공개 API가 없어 직접 못 읽는다 — S5 RespawnZone 구역 포함 여부로 적는다.
            string startInfo = "";
            if (s5.Start != null)
            {
                Vector3 vp = victim.transform.position;
                NearestSafePoint(s5.Start, vp, out string nearName, out float dNear);
                startInfo = $", CH5_START(주·보조 중 가장 가까운 점 '{nearName}')까지 수평 {dNear:F2}U · 주 안전점 '{s5.Start.name}' local{V(s5.Gen.InverseTransformPoint(s5.Start.transform.position))}까지 수평 {HorizDist(vp, s5.Start.transform.position):F2}U";
            }
            if (s5.Zone != null)
            {
                Collider zc = s5.Zone.GetComponent<Collider>();
                bool inZone = zc != null && zc.bounds.Contains(victim.transform.position);
                startInfo += $", 복귀 기준 RespawnZone '{s5.Zone.name}' local{V(s5.Gen.InverseTransformPoint(s5.Zone.transform.position))}까지 수평 {HorizDist(victim.transform.position, s5.Zone.transform.position):F2}U" +
                             (zc == null ? "(구역 콜라이더 없음)" : inZone ? "(구역 안)" : "(구역 밖)");
            }
            Detail(id, $"복귀 후 local{V(lp)} ({Time.time - since:F1}s){startInfo} — A 시작 플랫폼(x −30~4, z 0~16) {(onA ? "위" : "밖")}");
            string others = CheckUnchanged(snap, since, 0.5f);
            if (others != null) Detail(id, "다른 도형: " + others);
            if (onA) Pass(id, $"허공 낙하 → A 시작 플랫폼(CH5 시작)으로 복귀({Time.time - since:F1}s)");
            else if (!onFloorA && S5PlatformA.Contains(new Vector2(lp.x, lp.z)))
                Fail(id, $"복귀는 됐으나 발밑이 A 플랫폼 윗면 아님(local{V(lp)}) — {supportA}");
            else Fail(id, $"복귀는 됐으나 A 시작 플랫폼 밖(local{V(lp)}) — 공용 체크포인트가 S5로 갱신됐는지 확인");
            yield return ParkPlayers(s5.Park, victim);
        }

        // ═════════════════════════ PTF-2: S2 격리실 ═════════════════════════

        private IEnumerator ResolveS2()
        {
            if (!director.TryGetSector(2, out SectorController sc) || sc == null) { s2Problem = "섹터 2가 로드되지 않음(섹터 씬 없음)"; yield break; }
            S2Ctx c = new S2Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;

            List<SnapBlock> all = InScene<SnapBlock>(c.Scene);
            c.AllSnapBlocks = all.Count;
            c.Blocks.AddRange(all.Where(b => b.name.StartsWith("S2_Block_", StringComparison.Ordinal)));
            c.PileCount = c.Blocks.Count(b => b.name.StartsWith("S2_Block_Pile", StringComparison.Ordinal));
            c.Scatter.AddRange(c.Blocks.Where(b => b.name.StartsWith("S2_Block_Scatter_", StringComparison.Ordinal)).OrderBy(b => b.name, StringComparer.Ordinal));
            int geo = CountNamedPrefix(c.Gen, "GEO_S2_");
            if (geo == 0 && all.Count == 0)
            {
                s2Problem = "S2 미구체화(빈 틀) — Generated 아래 GEO_S2_* 0개·SnapBlock 0개(S2_Builder 결과 없음)";
                yield break;
            }

            c.ExitMarker = FindInScene(c.Scene, "TEMP_S2_ExitS3_Open");
            AddName("S2 S3행 개구 표지(Transform)", "TEMP_S2_ExitS3_Open", c.ExitMarker != null ? PathUnder(c.ExitMarker, c.Gen) : "(없음)" + NearNames(c.Gen, "ExitS3"), c.ExitMarker != null);

            Transform hw = FindInScene(c.Scene, "GEO_S2_Wall_IsoHidden");
            if (hw == null) c.HiddenWallProblem = "이름 불일치 — 'GEO_S2_Wall_IsoHidden' 없음" + NearNames(c.Gen, "Iso");
            else
            {
                c.HiddenWall = hw.GetComponent<BoxCollider>();
                if (c.HiddenWall == null) c.HiddenWallProblem = "'GEO_S2_Wall_IsoHidden'에 BoxCollider 없음(계약 R2-C3 타입 BoxCollider)";
            }
            AddName("S2 숨은 벽(BoxCollider)", "GEO_S2_Wall_IsoHidden", hw != null ? PathUnder(hw, c.Gen) : "(없음)", c.HiddenWall != null);

            c.HumpIso = FindInScene(c.Scene, "GEO_S2_Hump_IsoLine");
            AddName("S2 벽 선 미끄럼 둔덕", "GEO_S2_Hump_IsoLine", c.HumpIso != null ? PathUnder(c.HumpIso, c.Gen) : "(없음)" + NearNames(c.Gen, "Hump"), c.HumpIso != null);

            c.ExitS4Door = PickByName(InScene<doorPhysics>(c.Scene), "DOOR_S2_ExitS4", "S2 S4행 문(doorPhysics)", out c.ExitS4Problem);
            c.IsoController = PickByName(InScene<IsolationRescueController>(c.Scene), "S2_IsoController", "S2 격리 컨트롤러(IsolationRescueController)", out c.IsoProblem);
            if (c.IsoController != null)
                AddName("S2 격리 컨트롤러 경로", "S2_Isolation/S2_IsoController", PathUnder(c.IsoController.transform, c.Gen), Parent(c.IsoController.transform) == "S2_Isolation");

            List<LeverHead> levers = InScene<LeverHead>(c.Scene);
            c.LeverTotal = levers.Count;
            foreach (string pn in new[] { "S2_Lever1_Trap", "S2_Lever2", "S2_Lever3" })
            {
                LeverHead lh = levers.FirstOrDefault(l => HasAncestor(l.transform, pn));
                if (lh != null) c.LeverFound.Add(pn);
                AddName($"S2 레버(LeverHead, 부모 {pn})", pn, lh != null ? PathUnder(lh.transform, c.Gen) : "(없음)", lh != null);
            }

            // 마찰 0 면 — GEO_S2_Hump_*·GEO_S2_Wedge_* 이름의 콜라이더 중 sharedMaterial = PM_Map4_NoFriction
            // 기대 14 = 둔덕 5 + 경사판 9 [초안 S2 2차 §2-2·2-3]. Hump_S3_Gap·_Corr는 초안에서 이미 빠졌으므로 계약 R2-C3 문구 "− 2"를 다시 빼지 않는다 [지시서 PTF-2 09-29 변경분 2, S2-D2 보고 :35]
            List<string> noMat = new List<string>();
            foreach (Transform t in c.Gen.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("GEO_S2_Hump_", StringComparison.Ordinal) && !t.name.StartsWith("GEO_S2_Wedge_", StringComparison.Ordinal)) continue;
                Collider col = t.GetComponent<Collider>();
                if (col == null) continue;
                c.NoFrictionAll++;
                if (col.sharedMaterial != null && col.sharedMaterial.name == "PM_Map4_NoFriction") c.NoFrictionOk++;
                else noMat.Add(t.name);
            }
            AddName("S2 마찰 0 면(PM_Map4_NoFriction) 수", "14", $"{c.NoFrictionOk}/{c.NoFrictionAll}" + (noMat.Count > 0 ? " 재질 다름: " + string.Join(",", noMat.Take(6)) : ""),
                    c.NoFrictionOk == 14 && c.NoFrictionAll == 14);
            AddName("S2 딱딱블록 S2_Block_*", "39 = Pile 24 + Scatter 15", $"{c.Blocks.Count} = Pile {c.PileCount} + Scatter {c.Scatter.Count} (SnapBlock 전체 {c.AllSnapBlocks})",
                    c.Blocks.Count == 39 && c.PileCount == 24 && c.Scatter.Count == 15);

            c.Park = PickParkSpots(c.Gen, S2ParkLocal, "S2");
            s2 = c;
            report.Notes.Add($"S2 해석: GEO_S2_* {geo}개, 블록 {c.Blocks.Count}/39, 숨은 벽={Nm(c.HiddenWall)}, 둔덕={Nm(c.HumpIso)}, S4행 문={Nm(c.ExitS4Door)}, " +
                             $"격리 컨트롤러={Nm(c.IsoController)}, 레버 {c.LeverFound.Count}/3(LeverHead 전체 {c.LeverTotal}), 개구 표지={Nm(c.ExitMarker)}");
            yield break;
        }

        private bool Ready2(string id)
        {
            if (s2 != null) return true;
            report.Get(id).Set(Verdict.NA, s2Problem ?? "S2 해석 실패");
            return false;
        }

        private IEnumerator S2_Prepare()
        {
            if (s2 == null) yield break;
            if (s2.Park != null && s2.Park.Length > 0) yield return ParkPlayers(s2.Park, sphere, cube, tetra);
            else report.Notes.Add("S2 준비: 파킹점 0 — 도형을 옮기지 않았다(다른 도형이 주행 경로에 있을 수 있음)");
        }

        // ───────────── S2-2 ─────────────

        private IEnumerator S2_2_BlocksAndHump()
        {
            const string id = "S2-2";
            if (!Ready2(id)) yield break;
            Transform gen = s2.Gen;

            // (a) 개수·이름·Scatter 중심 [초안 §2-5]
            if (s2.Blocks.Count == 0)
            {
                NA(id, $"검사불가(이름 불일치): SnapBlock {s2.AllSnapBlocks}개 중 이름 'S2_Block_*' 0개" +
                       (s2.AllSnapBlocks > 0 ? $" [{string.Join(", ", InScene<SnapBlock>(s2.Scene).Select(b => b.name).Take(8))}]" : ""));
                yield break;
            }
            List<string> bad = new List<string>();
            bool countOk = s2.Blocks.Count == 39 && s2.PileCount == 24 && s2.Scatter.Count == 15;
            Detail(id, $"(a) SnapBlock 전체 {s2.AllSnapBlocks}개 중 'S2_Block_*' {s2.Blocks.Count}개 = Pile {s2.PileCount} + Scatter {s2.Scatter.Count} (기대 39 = 24 + 15 [초안 §2-5]) → {(countOk ? "일치" : "불일치")}");
            if (!countOk) bad.Add($"블록 수 {s2.Blocks.Count}(Pile {s2.PileCount}·Scatter {s2.Scatter.Count}) ≠ 39(24·15)");
            List<string> off = new List<string>();
            for (int i = 0; i < S2ScatterXZ.Length; i++)
            {
                string nm = $"S2_Block_Scatter_{i:00}";
                SnapBlock b = s2.Scatter.FirstOrDefault(x => x.name == nm);
                if (b == null) { off.Add($"{nm} 없음"); continue; }
                Vector3 l = Lc(gen, b.transform.position);
                if (Mathf.Abs(l.x - S2ScatterXZ[i].x) > 0.1f || Mathf.Abs(l.z - S2ScatterXZ[i].y) > 0.1f || Mathf.Abs(l.y - 0.5f) > 0.1f)
                    off.Add($"{nm} local{V(l)}(기대 ({S2ScatterXZ[i].x:F0},0.5,{S2ScatterXZ[i].y:F0}))");
            }
            Detail(id, off.Count == 0 ? "(a) Scatter 15개 중심 모두 초안 §2-5 값 ±0.1 안(흩어짐)" : "(a) Scatter 중심 ±0.1 밖/없음: " + string.Join(" / ", off));
            if (off.Count > 0) bad.Add($"Scatter 중심 불일치 {off.Count}개");

            // (b) 금지 구역 둔덕 위 블록 1개 미끄러짐 [초안 §1-5: 마찰 0 경사면에 정지 평형 없음, 가속 1.46U/s²]
            string bNa = null;
            bool bPass = false;
            if (s2.HumpIso == null) bNa = "검사불가(이름 불일치): 'GEO_S2_Hump_IsoLine' 없음";
            SnapBlock blk = null;
            if (bNa == null)
            {
                Vector3 testW = gen.TransformPoint(new Vector3(S2IsoLineX + S2HumpTestOffset, 0f, S2HumpTestZ));
                blk = s2.Scatter
                    .Where(x => x != null && Lc(gen, x.transform.position).x > S2IsoLineX + 0.5f && !x.HasConnections)
                    .Where(x => { Rigidbody r = x.GetComponent<Rigidbody>(); return r != null && !r.isKinematic; })
                    .OrderBy(x => HorizDist(x.transform.position, testW)).FirstOrDefault();
                if (blk == null) bNa = "격리 공간 밖·결합 없음·kinematic 아님인 Scatter 블록이 없음(물리가 도는 블록 없음)";
            }
            if (bNa == null)
            {
                Rigidbody rb = blk.GetComponent<Rigidbody>();
                BoxCollider bc = blk.GetComponent<BoxCollider>();
                Vector3 origPos = rb.position;
                Quaternion origRot = rb.rotation;
                // 둔덕 표면: 블록 발자국 양 끝(능선 쪽 x −13.9, 발끝 쪽 −12.9)에서 아래로 쏜 레이가 GEO_S2_Hump_IsoLine에 닿는 높이
                float sHigh, sLow;
                bool okH = HumpSurface(gen, S2IsoLineX + S2HumpTestOffset - 0.5f, out sHigh);
                bool okL = HumpSurface(gen, S2IsoLineX + S2HumpTestOffset + 0.5f, out sLow);
                if (!okH || !okL) bNa = "둔덕 표면을 레이로 찾지 못함(GEO_S2_Hump_IsoLine 콜라이더 확인)";
                else
                {
                    float expH = S2HumpHeight * (1f - (S2HumpTestOffset - 0.5f) / S2HumpHalf), expL = S2HumpHeight * (1f - (S2HumpTestOffset + 0.5f) / S2HumpHalf);
                    Detail(id, $"(b) 둔덕 표면 local y — x{S2IsoLineX + S2HumpTestOffset - 0.5f:F1}: {sHigh:F3}(설계 {expH:F3}) / x{S2IsoLineX + S2HumpTestOffset + 0.5f:F1}: {sLow:F3}(설계 {expL:F3}) [초안 §2-2 높이 0.3·반폭 2.0]");
                    Vector3 placeL = new Vector3(S2IsoLineX + S2HumpTestOffset, Mathf.Max(sHigh, sLow) + 0.5f + 0.03f, S2HumpTestZ);
                    Vector3 placeW = gen.TransformPoint(placeL);
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = placeW;
                    rb.rotation = gen.rotation;
                    blk.transform.SetPositionAndRotation(placeW, gen.rotation);
                    rb.WakeUp(); // 유니티 공개 API(팀 값 변경 아님)
                    float t0 = Time.time;
                    bool slept = false;
                    float maxD = S2HumpTestOffset;
                    while (Time.time - t0 < S2HumpObserve)
                    {
                        yield return new WaitForFixedUpdate();
                        if (blk == null) break;
                        float d = Mathf.Abs(Lc(gen, blk.transform.position).x - S2IsoLineX);
                        maxD = Mathf.Max(maxD, d);
                        if (Time.time - t0 > 0.3f && Time.time - t0 < 0.4f && rb.IsSleeping()) slept = true;
                    }
                    if (blk == null) bNa = "관찰 중 블록이 사라짐";
                    else
                    {
                        Vector3 endL = Lc(gen, blk.transform.position);
                        float dEnd = Mathf.Abs(endL.x - S2IsoLineX);
                        float delta = dEnd - S2HumpTestOffset;
                        Bounds ab = bc != null ? LocalBoxBounds(bc, gen) : new Bounds(endL, Vector3.one);
                        bool overlap = ab.min.x <= S2IsoLineX + 0.2f && ab.max.x >= S2IsoLineX - 0.2f;
                        Detail(id, $"(b) '{blk.name}'를 둔덕 방 쪽 비탈 local{V(placeL)}(능선 +{S2HumpTestOffset})에 올려 {S2HumpObserve:F0}초 관찰 → 끝 local{V(endL)}, " +
                                   $"능선 거리 {S2HumpTestOffset:F2} → {dEnd:F2}(Δd {delta:F2}, 최대 {maxD:F2}), 최종 AABB x{ab.min.x:F2}~{ab.max.x:F2} — 벽 발자국 |x+14|≤0.2와 {(overlap ? "겹침" : "안 겹침")}" +
                                   (slept ? " · 0.3초 뒤 잠든 상태(IsSleeping)" : ""));
                        bPass = delta >= 0.5f && !overlap;
                        if (!bPass) bad.Add(delta < 0.5f ? $"둔덕 위 블록이 미끄러지지 않음(Δd {delta:F2} < 0.5{(slept ? ", 잠듦" : "")})" : "블록 최종 AABB가 벽 발자국과 겹침");
                        // 원래 자리로 되돌림(실행 중 한정 — 씬 저장 없음)
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.position = origPos;
                        rb.rotation = origRot;
                        blk.transform.SetPositionAndRotation(origPos, origRot);
                    }
                }
            }
            if (bNa != null) Detail(id, "(b) " + bNa);

            if (bad.Count > 0) Fail(id, string.Join(" / ", bad));
            else if (bNa != null) NA(id, "(a) 통과, (b) " + bNa);
            else Pass(id, $"블록 39개(Pile 24·Scatter 15) 자리 일치, 둔덕 위 '{blk.name}'가 능선에서 미끄러져 내려와 벽 발자국 밖에 멈춤");
        }

        /// <summary>둔덕 GEO_S2_Hump_IsoLine의 local x(z = S2HumpTestZ) 위치 윗면 높이(섹터 로컬 y).</summary>
        private bool HumpSurface(Transform gen, float localX, out float localY)
        {
            localY = 0f;
            Vector3 from = gen.TransformPoint(new Vector3(localX, 3f, S2HumpTestZ));
            RaycastHit[] hits = Physics.RaycastAll(from, -gen.up, 4f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (!h.collider.transform.IsChildOf(s2.HumpIso) && h.collider.transform != s2.HumpIso) continue;
                localY = Lc(gen, h.point).y;
                return true;
            }
            return false;
        }

        // ───────────── S2-3 ─────────────

        private IEnumerator S2_3_HiddenWall()
        {
            const string id = "S2-3";
            if (!Ready2(id)) yield break;
            if (s2.HiddenWall == null) { NA(id, NameNA(s2.HiddenWallProblem)); yield break; }
            Transform gen = s2.Gen;
            List<string> bad = new List<string>();
            Bounds lb = LocalBoxBounds(s2.HiddenWall, gen);
            Bounds ex = S2HiddenWallExpect;
            bool yOk = Mathf.Abs(lb.min.y - ex.min.y) <= 0.02f && Mathf.Abs(lb.max.y - ex.max.y) <= 0.02f;
            bool xOk = Mathf.Abs(lb.center.x - ex.center.x) <= 0.2f;
            bool zOk = Mathf.Abs(lb.min.z - ex.min.z) <= 0.02f && Mathf.Abs(lb.max.z - ex.max.z) <= 0.02f;
            doorPhysics dp = s2.HiddenWall.GetComponentInParent<doorPhysics>();
            if (dp == null) dp = s2.HiddenWall.GetComponentInChildren<doorPhysics>(true);
            Detail(id, $"'{s2.HiddenWall.name}' BoxCollider 로컬 {BL(lb)} (기대 y 4.30~8.30±0.02 · x 중심 −14±0.2 · z 30.50~53.50±0.02 [초안 §1-2·판정 2]) " +
                       $"→ y {(yOk ? "일치" : "불일치")} · x {(xOk ? "일치" : "불일치")} · z {(zOk ? "일치" : "불일치")}, isTrigger={s2.HiddenWall.isTrigger}, doorPhysics {(dp == null ? "없음" : "있음 '" + dp.name + "'")}");
            if (!yOk) bad.Add($"숨은 벽 높이 y{lb.min.y:F2}~{lb.max.y:F2} ≠ 4.3~8.3");
            if (!xOk) bad.Add($"숨은 벽 x 중심 {lb.center.x:F2} ≠ −14");
            if (!zOk) bad.Add($"숨은 벽 z{lb.min.z:F2}~{lb.max.z:F2} ≠ 30.5~53.5");
            if (dp != null) bad.Add("숨은 벽에 doorPhysics가 있음(판정 2: 움직임 없는 정지 벽)");
            if (s2.HiddenWall.isTrigger) bad.Add("숨은 벽 BoxCollider가 트리거(정지 고체 벽이어야 함)");

            // 통행: 네모를 방 (−10, 바닥, 42)에서 −x로 몰아 격리 공간 x ≤ −20 [지시서]
            PlayerMover v = cube;
            Vector3 startW = gen.TransformPoint(S2IsoDriveStartLocal);
            if (!TryGround(startW + gen.up * 3f, 6f, null, 0f, out Vector3 g) || Mathf.Abs(Lc(gen, g).y) > 0.2f)
            {
                Detail(id, $"주행 시작점 local{V(S2IsoDriveStartLocal)} 아래 바닥이 y0이 아님(첫 면 {(TryGround(startW + gen.up * 3f, 6f, null, 0f, out Vector3 g2) ? $"y{Lc(gen, g2).y:F2}" : "없음")})");
                bad.Add("주행 시작점 바닥 이상");
            }
            else
            {
                SnapBlock b14 = s2.Scatter.FirstOrDefault(x => x.name == "S2_Block_Scatter_14");
                Vector3 b14a = b14 != null ? Lc(gen, b14.transform.position) : Vector3.zero;
                yield return WaitIdle(v, 5f);
                Teleport(v, g + gen.up * SpawnLift);
                yield return new WaitForSeconds(0.3f);
                DriveResult dr = new DriveResult();
                // [구현 결정] 4U/s — 팀 네모 지상 속도 3.5 근처, 10초 창(10U 거리의 4배)
                yield return DriveShape(v, gen, -gen.right, 4f, 10f, lp => lp.x <= S2IsoReachX, dr);
                Detail(id, $"네모 주행 local{V(S2IsoDriveStartLocal)} → −x: 끝 local{V(dr.EndLocal)}, {dr.Elapsed:F1}s, 진행 {dr.Progress:F2}U → x ≤ {S2IsoReachX} {(dr.Reached ? "도달" : "미도달")}" +
                           (b14 != null ? $" · 경로 위 'S2_Block_Scatter_14' local{V(b14a)} → {V(Lc(gen, b14.transform.position))}(밀림 기록)" : ""));
                if (!dr.Reached) bad.Add($"네모가 격리 공간 x ≤ −20에 못 들어감(끝 x{dr.EndLocal.x:F2}) — 방 안 통행 막힘");
                yield return WaitIdle(v, 4f);
                yield return ParkPlayers(s2.Park, v);
            }
            if (bad.Count == 0) Pass(id, "숨은 벽이 천장 위 보관 자리(y 4.3~8.3, 선 x −14, z 30.5~53.5)에 정지 벽으로 있고, 네모가 방에서 격리 공간(x ≤ −20)으로 지나감");
            else Fail(id, string.Join(" / ", bad));
        }

        // ───────────── S2-1 ─────────────

        private IEnumerator S2_1_ExitOpening()
        {
            const string id = "S2-1";
            if (!Ready2(id)) yield break;
            Transform gen = s2.Gen;
            List<string> bad = new List<string>();

            if (s2.ExitMarker != null)
            {
                Vector3 ml = Lc(gen, s2.ExitMarker.position);
                Detail(id, $"표지 'TEMP_S2_ExitS3_Open' local{V(ml)} (기대 {V(S2ExitMarkerLocal)} [판정 1·계약 C2-2]) {(Vector3.Distance(ml, S2ExitMarkerLocal) <= 0.05f ? "일치" : "다름(정보)")}");
            }
            else Detail(id, "표지 'TEMP_S2_ExitS3_Open' 없음 — 이름 대조 참고(판정은 개구 겹침·주행으로)");

            // 개구 고체 겹침(±0.02 안쪽으로 줄인 상자) — GEO_/DOOR_ 이름 고체만 판정, 그 밖은 정보
            Vector3 cL = (S2OpeningMin + S2OpeningMax) * 0.5f;
            Vector3 half = (S2OpeningMax - S2OpeningMin) * 0.5f - Vector3.one * 0.02f;
            List<string> geoHits = new List<string>(), otherHits = new List<string>();
            foreach (Collider col in Physics.OverlapBox(gen.TransformPoint(cL), half, gen.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (col.GetComponentInParent<PlayerMover>() != null || col.GetComponentInParent<Projectile>() != null) continue;
                string gn = GeoOrDoorName(col.transform, gen);
                if (gn == "GEO_S2_Hump_S3_Room") continue; // 둔덕 윗면 0.3 제외 [지시서]
                if (gn != null) geoHits.Add($"{gn}({BL(LocalBoxBounds(col, gen))})");
                else otherHits.Add(col.name);
            }
            Detail(id, $"개구 local x{S2OpeningMin.x}~{S2OpeningMax.x} y{S2OpeningMin.y}~{S2OpeningMax.y} z{S2OpeningMin.z}~{S2OpeningMax.z}(±0.02 안쪽) 고체 겹침: GEO_/DOOR_ {geoHits.Count}개" +
                       (geoHits.Count > 0 ? " [" + string.Join(", ", geoHits) + "]" : "") + (otherHits.Count > 0 ? $" · 기타 고체(정보) [{string.Join(", ", otherHits.Take(6))}]" : ""));
            if (geoHits.Count > 0) bad.Add("개구에 GEO_/DOOR_ 고체가 있음: " + string.Join(", ", geoHits));

            // S3행 둔덕 자리(정보) — 초안 2차에서 방 안으로 옮김 [제안, 컨트롤타워 확인 대기]. 개구 겹침에서는 위치와 무관하게 이름으로 제외했다.
            Transform humpRoom = FindInScene(s2.Scene, "GEO_S2_Hump_S3_Room");
            Collider humpCol = humpRoom != null ? humpRoom.GetComponent<Collider>() : null;
            if (humpCol != null)
            {
                Bounds hb = LocalBoxBounds(humpCol, gen);
                bool hOk = Mathf.Abs(hb.min.x - S2HumpS3RoomExpect.min.x) <= 0.05f && Mathf.Abs(hb.max.x - S2HumpS3RoomExpect.max.x) <= 0.05f &&
                           Mathf.Abs(hb.min.z - S2HumpS3RoomExpect.min.z) <= 0.05f && Mathf.Abs(hb.max.z - S2HumpS3RoomExpect.max.z) <= 0.05f &&
                           Mathf.Abs(hb.max.y - S2HumpS3RoomExpect.max.y) <= 0.05f;
                Detail(id, $"S3행 둔덕 'GEO_S2_Hump_S3_Room' 로컬 {BL(hb)} (초안 2차 x −6~6 · z 80~84 · 윗면 0.3 [제안, 컨트롤타워 확인 대기]) {(hOk ? "일치" : "다름(정보)")}" +
                           $", 재질 {(humpCol.sharedMaterial != null ? humpCol.sharedMaterial.name : "없음")} — 주행 경로 x 0이 능선 z 82를 넘는다");
            }
            else Detail(id, "S3행 둔덕 'GEO_S2_Hump_S3_Room' " + (humpRoom == null ? "없음" : "콜라이더 없음") + "(정보 — 판정은 개구 겹침·주행으로)");

            // 연결 통로(공용 코드 z84~96)
            Vector3 probeW = gen.TransformPoint(S2ConnectorProbeLocal);
            if (!TryGround(probeW + gen.up * 3f, 6f, null, 0f, out Vector3 cg) || Mathf.Abs(Lc(gen, cg).y) > 0.2f)
            {
                if (bad.Count > 0) Fail(id, string.Join(" / ", bad) + " (연결 통로도 확인 안 됨)");
                else NA(id, $"검사불가(공용 통로 없음): local{V(S2ConnectorProbeLocal)} 아래 y0 바닥 없음");
                yield break;
            }

            PlayerMover v = cube;
            Vector3 startW = gen.TransformPoint(S2ExitDriveStartLocal);
            if (!TryGround(startW + gen.up * 3f, 6f, null, 0f, out Vector3 g) || Mathf.Abs(Lc(gen, g).y) > 0.2f)
            {
                bad.Add($"주행 시작점 local{V(S2ExitDriveStartLocal)} 바닥 y0 아님");
            }
            else
            {
                yield return WaitIdle(v, 5f);
                Teleport(v, g + gen.up * SpawnLift);
                yield return new WaitForSeconds(0.3f);
                DriveResult dr = new DriveResult();
                yield return DriveShape(v, gen, gen.forward, 4f, 10f, lp => lp.z >= S2ExitReachZ, dr);
                Detail(id, $"네모 주행 local{V(S2ExitDriveStartLocal)} → +z: 끝 local{V(dr.EndLocal)}, {dr.Elapsed:F1}s, 진행 {dr.Progress:F2}U → z ≥ {S2ExitReachZ} {(dr.Reached ? "도달(연결 통로)" : "미도달")}");
                if (!dr.Reached) bad.Add($"네모가 S3행 개구를 지나 연결 통로(z ≥ 86)에 못 감(끝 z{dr.EndLocal.z:F2})");
                yield return WaitIdle(v, 4f);
                yield return ParkPlayers(s2.Park, v);
            }
            if (bad.Count == 0) Pass(id, "S3행 개구(x −4~4, 높이 3.5)에 GEO_/DOOR_ 고체 없음, 네모가 방 (0,78)에서 S3행 둔덕을 넘어 연결 통로 z ≥ 86까지 지나감");
            else Fail(id, string.Join(" / ", bad));
        }

        // ───────────── S2-4 ─────────────

        private IEnumerator S2_4_IsolationFlow()
        {
            const string id = "S2-4";
            if (!Ready2(id)) yield break;
            List<string> facts = new List<string>();
            int isoN = InScene<IsolationRescueController>(s2.Scene).Count;
            facts.Add($"IsolationRescueController {isoN}개" + (s2.IsoController != null ? $" '{PathUnder(s2.IsoController.transform, s2.Gen)}'(stages {s2.IsoController.StageCount}, 상태 {s2.IsoController.Current})" : $" — {s2.IsoProblem}"));
            if (s2.ExitS4Door != null)
            {
                doorPhysics d = s2.ExitS4Door;
                int toDoor = 0;
                if (s2.IsoController != null && s2.IsoController.onSuccess != null)
                    for (int i = 0; i < s2.IsoController.onSuccess.GetPersistentEventCount(); i++)
                        if (s2.IsoController.onSuccess.GetPersistentTarget(i) == d && s2.IsoController.onSuccess.GetPersistentMethodName(i) == "SetPadPressed") toDoor++;
                facts.Add($"DOOR_S2_ExitS4 local{V(Lc(s2.Gen, d.transform.position))} leverHead={(d.leverHead == null ? "없음(닫힌 채 시작 위치 유지)" : "'" + d.leverHead.name + "'")}");
                facts.Add($"onSuccess → DOOR_S2_ExitS4.SetPadPressed 영구 리스너 {toDoor}개(기대 1)" +
                          (s2.IsoController != null ? $" — onSuccess {PersistentSummary(s2.IsoController.onSuccess)}" : ""));
            }
            else facts.Add("DOOR_S2_ExitS4: " + s2.ExitS4Problem);
            facts.Add($"LeverHead {s2.LeverFound.Count}/3 [{string.Join(", ", s2.LeverFound)}](씬 전체 {s2.LeverTotal})");
            foreach (string f in facts) Detail(id, f);
            NA(id, "검사불가(팀 부품 없음): 팀 격리 시작 부품이 없어 갇힘·탈출·출구 열림 흐름을 만들 수 없음 [계약 R2-C3] — 존재·배선: " + string.Join(" · ", facts));
            yield break;
        }

        // ═════════════════════════ PTF-2: S3 점프 ═════════════════════════

        private IEnumerator ResolveS3()
        {
            if (!director.TryGetSector(3, out SectorController sc) || sc == null) { s3Problem = "섹터 3이 로드되지 않음(섹터 씬 없음)"; yield break; }
            S3Ctx c = new S3Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;

            List<ProjectileLauncher> lau = InScene<ProjectileLauncher>(c.Scene);
            c.AllLaunchers = lau.Count;
            c.Launchers.AddRange(lau.Where(l => l.name.StartsWith("S3_Launcher_PL_", StringComparison.Ordinal)).OrderBy(l => l.name, StringComparer.Ordinal));
            List<ThreadBridge> brs = InScene<ThreadBridge>(c.Scene);
            c.AllBridges = brs.Count;
            c.BridgesNamed = brs.Count(b => b.name.StartsWith("S3_Bridge_BR_", StringComparison.Ordinal));
            int geo = CountNamedPrefix(c.Gen, "GEO_S3_");
            if (geo == 0 && lau.Count == 0 && brs.Count == 0)
            {
                s3Problem = "S3 미구체화(빈 틀) — Generated 아래 GEO_S3_* 0개·발사구 0·줄다리 0(S3_Builder 결과 없음)";
                yield break;
            }
            AddName("S3 발사구 S3_Launcher_PL_*(ProjectileLauncher)", "14", $"{c.Launchers.Count}(전체 {lau.Count})" + (c.Launchers.Count == 0 && lau.Count > 0 ? " [" + string.Join(", ", lau.Select(l => l.name).Take(6)) + "]" : ""), c.Launchers.Count == 14);
            if (c.Launchers.Count > 0) AddName("S3 발사구 그룹", "S3_Gimmicks", Parent(c.Launchers[0].transform), Parent(c.Launchers[0].transform) == "S3_Gimmicks");
            AddName("S3 줄다리 S3_Bridge_BR_*(ThreadBridge)", "26", $"{c.BridgesNamed}(전체 {brs.Count})", c.BridgesNamed == 26);
            c.BridgeB1 = PickStrict(brs, "S3_Bridge_BR_B1_1", "S3 줄다리 BR_B1_1", out c.BridgeProblem);

            List<RespawnZone> zones = InScene<RespawnZone>(c.Scene);
            c.CpStart = PickStrict(zones, "CP_S3_Start", "S3 시작 체크포인트(RespawnZone)", out c.CpStartProblem);
            c.CpSave = PickStrict(zones, "CP_S3_Save", "S3 세이브 체크포인트(RespawnZone)", out c.CpSaveProblem);
            List<SectionSafePoint> sps = InScene<SectionSafePoint>(c.Scene);
            c.SspStart = PickStrict(sps, "SSP_S3_Start", "S3 시작 안전점(SectionSafePoint)", out c.SspStartProblem);
            c.SspSave = PickStrict(sps, "SSP_S3_Save", "S3 세이브 안전점(SectionSafePoint)", out c.SspSaveProblem);
            if (c.SspStart != null) AddName("S3 SSP_S3_Start sectionId", "S3_Start", c.SspStart.sectionId);
            if (c.SspSave != null) AddName("S3 SSP_S3_Save sectionId", "S3_Save", c.SspSave.sectionId);
            c.Counter = PickByName(InScene<SectionHitCounter>(c.Scene), "S3_ProjectileCounter", "S3 탄 카운터(SectionHitCounter)", out c.CounterProblem);
            c.Oob = PickByName(InScene<OutOfBoundsVolume>(c.Scene), "OOB_S3", "S3 장외 볼륨(OutOfBoundsVolume)", out _);
            c.Bridge = PickByName(InScene<Lab_SectionRespawnBridge>(c.Scene), "ADAPT_S3_RespawnBridge", "S3 복귀 다리(Lab_SectionRespawnBridge)", out _);
            foreach (Component k in new Component[] { c.CpStart, c.CpSave, c.SspStart, c.SspSave, c.Counter, c.Oob })
                if (k != null) AddName($"S3 그룹({k.name})", "S3_Respawn", Parent(k.transform), Parent(k.transform) == "S3_Respawn");

            c.Park = PickParkSpots(c.Gen, S3ParkLocal, "S3");
            s3 = c;
            report.Notes.Add($"S3 해석: GEO_S3_* {geo}개, 발사구 {c.Launchers.Count}/14(전체 {lau.Count}), 줄다리 {c.BridgesNamed}/26, CP 시작={Nm(c.CpStart)} 세이브={Nm(c.CpSave)}, " +
                             $"SSP 시작={Nm(c.SspStart)} 세이브={Nm(c.SspSave)}, 카운터={Nm(c.Counter)}" +
                             (c.Counter != null ? $"(임계 {c.Counter.hitsBeforeRespawn}, 목적지 {Nm(c.Counter.destination)})" : "") +
                             $", 장외={Nm(c.Oob)}, 복귀 다리={(c.Bridge != null ? $"'{c.Bridge.name}' 연결 {c.Bridge.WiredCount}개" : "(없음)")}");
            yield break;
        }

        private bool Ready3(string id)
        {
            if (s3 != null && s3.Park != null && s3.Park.Length > 0) return true;
            report.Get(id).Set(Verdict.NA, s3Problem ?? "S3 파킹점(뒤 구간 발판) 없음 — 도형을 치울 곳이 없어 검사 불가");
            return false;
        }

        /// <summary>S3 준비: 공용 체크포인트를 CP_S3_Start로 갱신(세이브 전 상태) → 세 도형을 뒤 구간 발판(발사구 없음)으로 치운다.</summary>
        private IEnumerator S3_Prepare()
        {
            if (s3 == null) yield break;
            if (s3.CpStart != null)
            {
                float since = Time.time;
                yield return WaitIdle(sphere, 5f);
                Teleport(sphere, s3.CpStart.FindGroundPoint() + s3.Gen.up * SpawnLift);
                yield return new WaitForSeconds(0.6f);
                report.Notes.Add($"S3 준비: 구를 'CP_S3_Start'에 넣음 → 체크포인트 갱신 로그 {(CheckpointStoredSince("CP_S3_Start", since) ? "확인" : "없음(이미 잡힌 구역이거나 저장 안 됨)")}, 마지막 갱신 '{LastCheckpointName()}'");
            }
            else report.Notes.Add("S3 준비: CP_S3_Start 없음 — " + s3.CpStartProblem);
            if (s3.Park != null && s3.Park.Length > 0) yield return ParkPlayers(s3.Park, sphere, cube, tetra);
        }

        // ───────────── S3-1 ─────────────

        private IEnumerator S3_1_LaunchersCycle()
        {
            const string id = "S3-1";
            if (!Ready3(id)) yield break;
            if (s3.Launchers.Count == 0)
            {
                NA(id, s3.AllLaunchers > 0 ? $"검사불가(이름 불일치): ProjectileLauncher {s3.AllLaunchers}개 중 'S3_Launcher_PL_*' 0개" : "ProjectileLauncher 0개");
                yield break;
            }
            if (s3.Launchers.Count != 14) Detail(id, $"주의: 발사구 {s3.Launchers.Count}개(계약 기대 14)");
            yield return ParkPlayers(s3.Park, sphere, cube, tetra);
            float minD = float.MaxValue;
            foreach (ProjectileLauncher l in s3.Launchers) foreach (PlayerMover p in players) minD = Mathf.Min(minD, Vector3.Distance(l.transform.position, p.transform.position));
            Detail(id, $"도형-발사구 최소 거리 {minD:F1}U(도형은 뒤 구간 발판 — 탄 경로 밖)");

            Dictionary<ProjectileLauncher, List<float>> shots = s3.Launchers.ToDictionary(l => l, l => new List<float>());
            Dictionary<string, ProjectileLauncher> byName = new Dictionary<string, ProjectileLauncher>();
            foreach (ProjectileLauncher l in s3.Launchers) byName["Projectile_" + l.name] = l;
            HashSet<int> seen = new HashSet<int>(Object.FindObjectsOfType<Projectile>().Select(p => p.GetInstanceID()));
            float t0 = Time.time;
            while (Time.time - t0 < S3LauncherObserve)
            {
                yield return new WaitForFixedUpdate();
                foreach (Projectile pr in Object.FindObjectsOfType<Projectile>())
                {
                    if (!seen.Add(pr.GetInstanceID())) continue;
                    ProjectileLauncher owner;
                    if (!byName.TryGetValue(pr.name, out owner))
                    {
                        owner = s3.Launchers.OrderBy(l => Vector3.Distance(l.transform.position, pr.transform.position)).FirstOrDefault();
                        if (owner == null || Vector3.Distance(owner.transform.position, pr.transform.position) > 2f) continue;
                    }
                    shots[owner].Add(Time.time);
                }
            }
            List<string> bad = new List<string>();
            foreach (ProjectileLauncher l in s3.Launchers)
            {
                List<float> ts = shots[l];
                List<float> gaps = new List<float>();
                for (int i = 1; i < ts.Count; i++) gaps.Add(ts[i] - ts[i - 1]);
                bool ok = ts.Count >= 2 && gaps.All(gp => Mathf.Abs(gp - S3LauncherPeriod) <= S3LauncherPeriodTol);
                Detail(id, $"'{l.name}' local{V(Lc(s3.Gen, l.transform.position))}: 발사 {ts.Count}회, 간격 [{string.Join(", ", gaps.Select(x => x.ToString("F2", CultureInfo.InvariantCulture)))}]s " +
                           $"(팀 필드 예고 {l.warningSeconds}+휴식 {l.restSeconds}={l.warningSeconds + l.restSeconds}s) → {(ok ? "주기 발사" : "기준 밖")}");
                if (!ok) bad.Add($"'{l.name}'(발사 {ts.Count}회{(gaps.Count > 0 ? ", 간격 " + string.Join("/", gaps.Select(x => x.ToString("F2", CultureInfo.InvariantCulture))) : "")})");
            }
            if (bad.Count == 0) Pass(id, $"발사구 {s3.Launchers.Count}개 모두 {S3LauncherObserve:F1}초 동안 2회 이상, 간격 {S3LauncherPeriod}±{S3LauncherPeriodTol}초로 발사");
            else Fail(id, "주기 발사 기준 밖: " + string.Join(", ", bad));
        }

        // ───────────── S3-3 ─────────────

        private IEnumerator S3_3_ProjectileHit()
        {
            const string id = "S3-3";
            if (!Ready3(id)) yield break;
            if (s3.Launchers.Count == 0) { NA(id, "검사불가(이름 불일치): 'S3_Launcher_PL_*' 0개"); yield break; }
            if (s3.SspStart == null) { NA(id, NameNA(s3.SspStartProblem)); yield break; }
            ProjectileLauncher pl = s3.Launchers.FirstOrDefault(l => l.name == "S3_Launcher_PL_L1_01") ?? s3.Launchers[0];
            Detail(id, $"카운터 '{Nm(s3.Counter)}'" + (s3.Counter != null ? $" 임계 {s3.Counter.hitsBeforeRespawn}(기대 1) 목적지 '{Nm(s3.Counter.destination)}'(기대 SSP_S3_Start), 쿨다운 {s3.Counter.hitCooldown}s" : " 없음 — " + s3.CounterProblem) +
                       $" · '{pl.name}' OnHazardHit {PersistentSummary(pl.OnHazardHit)}");
            // 탄 경로는 씬의 발사구 Transform에서 읽는다. 초안 값은 정보 [초안 S3 2차 :316 PL_L1_01 (−10, 1.4, 9.6) yaw 180 — 틈 양쪽 높은 윗면 0.8 + 0.6]
            Vector3 plL = Lc(s3.Gen, pl.transform.position);
            Vector3 plFwdL = s3.Gen.InverseTransformDirection(pl.transform.forward);
            bool plOk = pl.name == "S3_Launcher_PL_L1_01" && Vector3.Distance(plL, S3L1_01Expect) <= 0.05f && plFwdL.z < -0.99f;
            Detail(id, $"'{pl.name}' 발사점 local{V(plL)} 방향 local{V(plFwdL)} — 초안 2차 PL_L1_01 {V(S3L1_01Expect)} −z {(plOk ? "일치" : "다름(정보)")}");
            PlayerMover v = cube;
            yield return ParkPlayers(s3.Park, sphere, cube, tetra);
            Dictionary<PlayerMover, Vector3> snap = Snapshot(Others(v));
            float tStart = Time.time;
            DestTrial r = new DestTrial();
            yield return LauncherHit(pl, v, s3.SspStart, s3.Gen, r);
            Detail(id, r.Msg);
            string others = CheckUnchanged(snap, tStart, 0.5f);
            if (!r.Hit) NA(id, $"탄 피격을 만들지 못함 — {r.Msg}");
            else if (!r.Respawned) Fail(id, "피격됐으나 복귀 없음(카운터 → Lab_SectionRespawnBridge → RespawnController 연결 확인) — " + r.Msg);
            else if (r.AtDest && r.SupportOk) Pass(id, $"'{pl.name}' 탄 피격(+{r.HitAfter:F2}s) → SSP_S3_Start(주·보조 중 하나)로 복귀, 발밑 로컬 y0 지지. 세이브 뒤에도 목적지 필드는 '{Nm(s3.Counter != null ? s3.Counter.destination : null)}'(뒤 구간 발사구 없음 — 정보)");
            else Fail(id, $"복귀 위치가 SSP_S3_Start 아님 또는 발밑 이상 — {r.Msg}");
            if (others != null) Detail(id, "다른 도형(정보): " + others);
            yield return WaitIdle(v, 5f);
            yield return ParkPlayers(s3.Park, v);
        }

        /// <summary>발사구 탄 경로 가운데(발사점~첫 고체의 절반)에 도형을 붙잡아 두고, 새 탄이 도형에 닿기 0.1초 전에 풀어
        /// 팀 물리로 맞게 한다(붙잡힌 몸은 팀 RespawnController가 복귀를 거절하므로). 최대 2회 시도.</summary>
        private IEnumerator LauncherHit(ProjectileLauncher pl, PlayerMover v, SectionSafePoint dest, Transform gen, DestTrial r)
        {
            Vector3 origin = pl.transform.position;
            Vector3 fwd = pl.transform.forward;
            float range = Mathf.Min(pl.projectileSpeed * pl.projectileLifetime, 40f);
            float firstSolid = FirstSolidDistance(origin, fwd, range, pl.transform);
            float s = Mathf.Clamp(firstSolid * 0.5f, 2.0f, firstSolid - 1.0f);
            if (firstSolid < 3f) { r.Msg = $"발사점에서 첫 고체까지 {firstSolid:F2}U — 도형을 둘 자리 없음"; yield break; }
            Vector3 spot = origin + fwd * s;
            if (SolidOverlap(spot, Vector3.one * 0.45f, gen.rotation) != null) { r.Msg = $"탄 경로 {s:F2}U 지점 local{V(Lc(gen, spot))}이 고체와 겹침"; yield break; }
            float speed = Mathf.Max(0.1f, pl.projectileSpeed);
            bool holding = false, hit = false;
            float hitAt = -1f;
            UnityAction<GameObject> cb = go => { if (!holding && !hit && Owner(go) == v) { hit = true; hitAt = Time.time; } };
            if (pl.OnHazardHit != null) pl.OnHazardHit.AddListener(cb);
            string prefix = $"'{pl.name}' 탄 경로 {s:F2}U 지점 local{V(Lc(gen, spot))}(첫 고체 {firstSolid:F2}U)";
            try
            {
                for (int attempt = 1; attempt <= 2 && !hit; attempt++)
                {
                    HashSet<int> known = new HashSet<int>(Object.FindObjectsOfType<Projectile>().Select(p => p.GetInstanceID()));
                    Func<bool> releaseNow = () =>
                    {
                        foreach (Projectile p in Object.FindObjectsOfType<Projectile>())
                        {
                            if (known.Contains(p.GetInstanceID()) || p.name != "Projectile_" + pl.name) continue;
                            float along = Vector3.Dot(v.transform.position - p.transform.position, fwd) - (pl.projectileRadius + 0.55f);
                            if (along / speed <= 0.1f) return true;
                        }
                        return false;
                    };
                    bool released = false;
                    holding = true;
                    float since = Time.time;
                    yield return HoverShape(v, spot, releaseNow, 5f, ok => { released = ok; holding = false; });
                    holding = false;
                    if (!released) { r.Msg += $" {attempt}회차: 5초 안에 새 탄이 오지 않음;"; continue; }
                    float tr = Time.time;
                    while (!hit && Time.time - tr < 1.0f) yield return new WaitForFixedUpdate();
                    if (!hit)
                    {
                        r.Msg += $" {attempt}회차: 풀었으나 탄 피격 이벤트 없음(허공으로 떨어짐 — 장외 복귀 대기);";
                        float tw = Time.time;
                        while (RespawnsSince(v, since) == 0 && Time.time - tw < 7f) yield return null;
                        yield return WaitIdle(v, 5f);
                        continue;
                    }
                    r.Hit = true;
                    r.HitAfter = hitAt - since;
                    float th = Time.time;
                    while (RespawnsSince(v, since) == 0 && Time.time - th < 3f) yield return null;
                    r.Respawned = RespawnsSince(v, since) > 0;
                    yield return WaitIdle(v, 5f);
                    yield return new WaitForSeconds(0.3f);
                    r.Msg += $" {attempt}회차 피격";
                    if (r.Respawned) VerifyDest(v, dest, gen, r);
                }
            }
            finally
            {
                if (pl != null && pl.OnHazardHit != null) pl.OnHazardHit.RemoveListener(cb);
            }
            r.Msg = prefix + ":" + r.Msg;
        }

        // ───────────── S3-2 ─────────────

        private IEnumerator S3_2_VoidFallSaveRule()
        {
            const string id = "S3-2";
            if (!Ready3(id)) yield break;
            if (s3.CpStart == null) { NA(id, NameNA(s3.CpStartProblem)); yield break; }
            Transform gen = s3.Gen;
            PlayerMover v = tetra;
            yield return ParkPlayers(s3.Park, sphere, cube, tetra);

            Vector3 drop = Vector3.zero;
            bool found = false;
            foreach (Vector3 l in S3VoidDropLocal)
            {
                Vector3 w = gen.TransformPoint(l);
                if (TryGround(w, 4f, null, 0f, out _) || Physics.CheckSphere(w, 0.7f, ~0, QueryTriggerInteraction.Ignore)) continue;
                drop = w; found = true; break;
            }
            if (!found) { NA(id, "앞 구간 허공 시험점을 찾지 못함(후보 아래 4U 안에 지형)"); yield break; }
            string below = TryGround(drop, 80f, null, 0f, out Vector3 bg) ? $"아래 {drop.y - bg.y:F1}U에 면(local y{Lc(gen, bg).y:F1})" : "아래 80U까지 면 없음";
            Detail(id, $"낙하 시험점 local{V(Lc(gen, drop))} — {below}, 장외 볼륨 '{Nm(s3.Oob)}' 안 여부는 팀 판정(OutOfBoundsVolume)");
            List<string> bad = new List<string>();
            string naA = null, naB = null;

            // (a) 세이브 전 — 체크포인트 = CP_S3_Start (S3_Prepare에서 갱신)
            Detail(id, $"(a) 시작 전 마지막 체크포인트 갱신 로그: '{LastCheckpointName()}'");
            bool passA = false;
            {
                float since = Time.time;
                yield return WaitIdle(v, 5f);
                Teleport(v, drop);
                float t = Time.time;
                while (RespawnsSince(v, since) == 0 && Time.time - t < S3RespawnWindow) yield return null;
                if (RespawnsSince(v, since) == 0)
                {
                    bad.Add($"(a) {S3RespawnWindow:F0}초 안에 복귀 없음(현재 local{V(Lc(gen, v.transform.position))})");
                    Detail(id, "(a) 복귀 창 끝 진단(관찰 전용): " + S3FallDiag(v, gen));
                }
                else
                {
                    float took = Time.time - since;
                    yield return WaitIdle(v, 6f);
                    yield return new WaitForSeconds(0.5f);
                    passA = ZoneCheck(v, s3.CpStart, gen, 0f, "(a)", took, id, bad);
                }
            }

            // (b) 세이브 뒤 — 다른 도형(구)을 CP_S3_Save에 넣어 저장 → 세모를 다시 앞 구간 허공에서 떨어뜨림
            bool passB = false;
            if (s3.CpSave == null) naB = NameNA(s3.CpSaveProblem);
            else
            {
                float ss = Time.time;
                yield return WaitIdle(sphere, 5f);
                Teleport(sphere, s3.CpSave.FindGroundPoint() + gen.up * SpawnLift);
                yield return new WaitForSeconds(0.6f);
                bool stored = CheckpointStoredSince("CP_S3_Save", ss);
                Detail(id, $"(b) 구를 'CP_S3_Save'에 넣음 → 체크포인트 갱신 로그 {(stored ? "확인" : "없음")}(마지막 '{LastCheckpointName()}') — 공용 체크포인트 규칙(사용자 결정 12 (가))이라 '다른 도형'으로 확인");
                yield return ParkPlayers(s3.Park, sphere);
                float since = Time.time;
                yield return WaitIdle(v, 5f);
                Teleport(v, drop);
                float t = Time.time;
                while (RespawnsSince(v, since) == 0 && Time.time - t < S3RespawnWindow) yield return null;
                if (RespawnsSince(v, since) == 0)
                {
                    bad.Add($"(b) {S3RespawnWindow:F0}초 안에 복귀 없음(현재 local{V(Lc(gen, v.transform.position))})");
                    Detail(id, "(b) 복귀 창 끝 진단(관찰 전용): " + S3FallDiag(v, gen));
                }
                else
                {
                    float took = Time.time - since;
                    yield return WaitIdle(v, 6f);
                    yield return new WaitForSeconds(0.5f);
                    passB = ZoneCheck(v, s3.CpSave, gen, S3SavePadTop, "(b)", took, id, bad);
                    if (!passB && !stored) Detail(id, "(b) 참고: 세이브 갱신 로그가 없었다 — 구역이 도형을 못 받았거나 이미 잡힌 구역");
                }
            }
            yield return WaitIdle(v, 5f);
            yield return ParkPlayers(s3.Park, v);

            if (bad.Count > 0) Fail(id, string.Join(" / ", bad));
            else if (naA != null || naB != null) NA(id, string.Join(" / ", new[] { naA, naB }.Where(x => x != null)) + (passA ? " ((a)는 통과)" : ""));
            else if (passA && passB) Pass(id, "허공 낙하 → 세이브 전 CP_S3_Start 바닥(y0), 다른 도형이 세이브를 밟은 뒤 CP_S3_Save 발판(y2.4)으로 복귀 — 공용 체크포인트 규칙 그대로");
            else Fail(id, "복귀 위치 기준 밖(위 세부 참고)");
        }

        /// <summary>S3-2 복귀 창이 끝났을 때 원인을 가르는 값(관찰 전용 — 판정에 쓰지 않는다, M2R1 지적 4):
        /// 팀 OutOfBoundsVolume.AnyContains(도형 위치), 도형 transform y, 'OOB_S3' 볼륨 콜라이더 bounds min.y·max.y·활성·트리거, RespawnController.killY.</summary>
        private string S3FallDiag(PlayerMover v, Transform gen)
        {
            Vector3 p = v.transform.position;
            bool any = OutOfBoundsVolume.AnyContains(p);
            string vol;
            if (s3 == null || s3.Oob == null) vol = "'OOB_S3' 없음";
            else
            {
                Collider oc = s3.Oob.GetComponent<Collider>();
                if (oc == null) vol = $"'{s3.Oob.name}' 콜라이더 없음";
                else
                {
                    Bounds ob = oc.bounds;
                    vol = $"'{s3.Oob.name}' 월드 min.y {ob.min.y:F2}·max.y {ob.max.y:F2}(로컬 min{V(Lc(gen, ob.min))} max{V(Lc(gen, ob.max))}), " +
                          $"활성 {s3.Oob.isActiveAndEnabled}·콜라이더 enabled {oc.enabled}·isTrigger {oc.isTrigger}, " +
                          $"도형이 bounds 안 {ob.Contains(p)}";
                }
            }
            string kill = respawn != null ? respawn.killY.ToString("F2", CultureInfo.InvariantCulture) : "(RespawnController 없음)";
            return $"AnyContains(도형 위치)={any} · 도형 transform 월드 y {p.y:F2}(local{V(Lc(gen, p))}) · {vol} · killY {kill}" +
                   (respawn != null ? $"(도형 y {(p.y < respawn.killY ? "<" : "≥")} killY)" : "");
        }

        /// <summary>복귀 위치가 체크포인트 구역 수평 ±1.5 안이고 발밑 첫 지지면이 expectLocalY(±0.2, 밑면 간격 ≤0.3)인가 — S1-6과 같은 기준.</summary>
        private bool ZoneCheck(PlayerMover v, RespawnZone zone, Transform gen, float expectLocalY, string tag, float took, string id, List<string> bad)
        {
            Vector3 pos = v.transform.position;
            Collider zc = zone.GetComponent<Collider>();
            Bounds zb = zc != null ? zc.bounds : new Bounds(zone.transform.position, Vector3.one * 2f);
            bool horizIn = pos.x >= zb.min.x - 1.5f && pos.x <= zb.max.x + 1.5f && pos.z >= zb.min.z - 1.5f && pos.z <= zb.max.z + 1.5f;
            bool onFloor = CheckSupport(v, gen, expectLocalY, out string support);
            Detail(id, $"{tag} 복귀 후 local{V(Lc(gen, pos))} ({took:F1}s), '{zone.name}' 구역 local 중심{V(Lc(gen, zb.center))} 크기{V(zb.size)} — 수평 {(horizIn ? "구역 안" : "구역 밖")} / 발밑: {support}");
            if (!horizIn) bad.Add($"{tag} '{zone.name}' 구역 수평 밖(local{V(Lc(gen, pos))})");
            if (!onFloor) bad.Add($"{tag} 발밑이 '{zone.name}' 바닥(local y{expectLocalY:F1}) 아님");
            return horizIn && onFloor;
        }

        // ───────────── S3-4 ─────────────

        private IEnumerator S3_4_BridgeSupport()
        {
            const string id = "S3-4";
            if (!Ready3(id)) yield break;
            if (s3.BridgeB1 == null) { NA(id, NameNA(s3.BridgeProblem)); yield break; }
            ThreadBridge br = s3.BridgeB1;
            if (br.anchorA == null || br.anchorB == null) { Fail(id, "S3_Bridge_BR_B1_1의 anchorA/anchorB가 비어 있음(고정 줄다리 아님)"); yield break; }
            Transform gen = s3.Gen;
            Vector3 a = br.anchorA.transform.position, b = br.anchorB.transform.position;
            Vector3 mid = (a + b) * 0.5f;
            float anchorY = (Lc(gen, a).y + Lc(gen, b).y) * 0.5f;
            float minFoot = anchorY - S3BridgeSag - S3BridgeSagTol;
            Detail(id, $"'{br.name}' 고리 A local{V(Lc(gen, a))} B local{V(Lc(gen, b))}(초안 2차 :287 A (18,2.4,79.5)·B (10,2.4,79.5) — 레인 가운데, 판정은 씬 고리 좌표), 경간 {Vector3.Distance(a, b):F2}, " +
                       $"baseSag {br.baseSag} sagPerWeight {br.sagPerWeight} segmentWidth {br.segmentWidth} maxSpans {br.maxSpans}");
            PlayerMover v = cube;
            yield return ParkPlayers(s3.Park, sphere, cube, tetra);
            yield return WaitIdle(v, 5f);
            float since = Time.time;
            Teleport(v, mid + gen.up * 0.6f);
            bool oob = false;
            float minF = float.MaxValue;
            float t0 = Time.time;
            while (Time.time - t0 < 3f)
            {
                yield return new WaitForFixedUpdate();
                if (OutOfBoundsVolume.AnyContains(v.transform.position)) oob = true;
                Bounds sbx = SolidBounds(v);
                minF = Mathf.Min(minF, Lc(gen, new Vector3(sbx.center.x, sbx.min.y, sbx.center.z)).y);
            }
            int resp = RespawnsSince(v, since);
            Bounds vb = SolidBounds(v);
            float foot = Lc(gen, new Vector3(vb.center.x, vb.min.y, vb.center.z)).y;
            string supName = "(없음)";
            bool onBridge = false;
            float sag = float.NaN;
            RaycastHit[] hits = Physics.RaycastAll(vb.center, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.GetComponentInParent<PlayerMover>() != null) continue;
                supName = PathUnder(h.collider.transform, gen);
                onBridge = h.collider.transform.IsChildOf(br.transform);
                sag = anchorY - Lc(gen, h.point).y;
                break;
            }
            Detail(id, $"네모를 가운데 local{V(Lc(gen, mid))} 위에 놓고 3초: 밑면 local y{foot:F3}(최저 {minF:F3}, 기준 ≥ {minFoot:F2} = {anchorY:F2} − {S3BridgeSag} − {S3BridgeSagTol}), " +
                       $"발밑 첫 면 '{supName}'({(onBridge ? "줄다리 발판" : "줄다리 아님")}), 측정 처짐 {(float.IsNaN(sag) ? "—" : sag.ToString("F3", CultureInfo.InvariantCulture))}U(계산 0.56 [초안 §5]), 복귀 {resp}회, 장외 볼륨 진입 {oob}");
            List<string> bad = new List<string>();
            if (resp > 0) bad.Add("줄다리 위 네모가 복귀됨(떨어짐)");
            if (oob) bad.Add("장외 볼륨에 들어감");
            if (foot < minFoot) bad.Add($"밑면 y{foot:F2} < {minFoot:F2}");
            if (!onBridge) bad.Add($"발밑 첫 면이 줄다리 발판이 아님('{supName}')");
            if (bad.Count == 0) Pass(id, $"줄다리 BR_B1_1 가운데에 네모가 지지됨(밑면 y{foot:F2}, 처짐 {sag:F2}U)");
            else Fail(id, string.Join(" / ", bad));
            yield return WaitIdle(v, 5f);
            yield return ParkPlayers(s3.Park, v);
        }

        // ═════════════════════════ PTF-2: S4 반중력·보안 ═════════════════════════

        private IEnumerator ResolveS4()
        {
            if (!director.TryGetSector(4, out SectorController sc) || sc == null) { s4Problem = "섹터 4가 로드되지 않음(섹터 씬 없음)"; yield break; }
            S4Ctx c = new S4Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;

            List<ZeroGravityBubble> bubbles = InScene<ZeroGravityBubble>(c.Scene);
            List<FixedPeriodicLaser> fls = InScene<FixedPeriodicLaser>(c.Scene);
            List<CharacterLockedLaser> als = InScene<CharacterLockedLaser>(c.Scene);
            List<FallingRockSpawner> rocks = InScene<FallingRockSpawner>(c.Scene);
            int geo = CountNamedPrefix(c.Gen, "GEO_S4_");
            if (geo == 0 && bubbles.Count == 0 && fls.Count == 0 && rocks.Count == 0)
            {
                s4Problem = "S4 미구체화(빈 틀) — Generated 아래 GEO_S4_* 0개·버블·레이저·낙석 0(S4_Builder 결과 없음)";
                yield break;
            }
            c.Bubble = PickByName(bubbles, "S4_Bubble", "S4 반중력 버블(ZeroGravityBubble)", out c.BubbleProblem);
            if (c.Bubble != null)
            {
                c.BubbleBox = c.Bubble.GetComponent<BoxCollider>();
                if (c.BubbleBox == null || !c.BubbleBox.isTrigger) { c.BubbleProblem = "'S4_Bubble'에 isTrigger BoxCollider 없음(판정 18 ②)"; c.BubbleBox = null; }
                AddName("S4 버블 그룹", "S4_Gimmicks", Parent(c.Bubble.transform), Parent(c.Bubble.transform) == "S4_Gimmicks");
            }
            string p1, p2, p3, p4;
            c.FlStair1 = PickStrict(fls, "S4_FL_Stair1", "S4 고정 레이저 계단1", out p1);
            c.FlStair2 = PickStrict(fls, "S4_FL_Stair2", "S4 고정 레이저 계단2", out p2);
            c.FlBubble1 = PickStrict(fls, "S4_FL_Bubble1", "S4 고정 레이저 버블1", out p3);
            c.FlBubble2 = PickStrict(fls, "S4_FL_Bubble2", "S4 고정 레이저 버블2", out p4);
            c.FlProblem = string.Join(" / ", new[] { p1, p2, p3, p4 }.Where(x => x != null));
            if (c.FlProblem.Length == 0) c.FlProblem = null;
            c.AllAim = als.Count;
            c.AimStair.AddRange(als.Where(a => a.name.StartsWith("S4_AL_Stair_", StringComparison.Ordinal)).OrderBy(a => a.name, StringComparer.Ordinal));
            c.AimBubble.AddRange(als.Where(a => a.name.StartsWith("S4_AL_Bubble_", StringComparison.Ordinal)).OrderBy(a => a.name, StringComparer.Ordinal));
            AddName("S4 조준 레이저 S4_AL_Stair_*·S4_AL_Bubble_*(CharacterLockedLaser)", "3 + 3", $"{c.AimStair.Count} + {c.AimBubble.Count}(전체 {als.Count})", c.AimStair.Count == 3 && c.AimBubble.Count == 3);
            c.RockStairA = PickStrict(rocks, "S4_Rock_StairA", "S4 낙석 계단A", out p1);
            c.RockStairB = PickStrict(rocks, "S4_Rock_StairB", "S4 낙석 계단B", out p2);
            c.RockBubble = PickStrict(rocks, "S4_Rock_Bubble", "S4 낙석 버블", out p3);
            c.RockProblem = string.Join(" / ", new[] { p1, p2, p3 }.Where(x => x != null));
            if (c.RockProblem.Length == 0) c.RockProblem = null;
            List<SectionSafePoint> sps = InScene<SectionSafePoint>(c.Scene);
            c.SpStairs = PickStrict(sps, "S4_SP_Stairs", "S4 계단 안전점", out p1);
            c.SpBubble = PickStrict(sps, "S4_SP_Bubble", "S4 버블 안전점", out p2);
            c.SpProblem = string.Join(" / ", new[] { p1, p2 }.Where(x => x != null));
            if (c.SpProblem.Length == 0) c.SpProblem = null;
            if (c.SpStairs != null) AddName("S4 S4_SP_Stairs sectionId", "S4_STAIRS", c.SpStairs.sectionId);
            if (c.SpBubble != null) AddName("S4 S4_SP_Bubble sectionId", "S4_BUBBLE", c.SpBubble.sectionId);
            List<SectionHitCounter> cnts = InScene<SectionHitCounter>(c.Scene);
            c.CntRockStairs = PickStrict(cnts, "S4_Counter_RockStairs", "S4 카운터 낙석 계단", out p1);
            c.CntLaserStairs = PickStrict(cnts, "S4_Counter_LaserStairs", "S4 카운터 레이저 계단", out p2);
            c.CntRockBubble = PickStrict(cnts, "S4_Counter_RockBubble", "S4 카운터 낙석 버블", out p3);
            c.CntLaserBubble = PickStrict(cnts, "S4_Counter_LaserBubble", "S4 카운터 레이저 버블", out p4);
            c.CntProblem = string.Join(" / ", new[] { p1, p2, p3, p4 }.Where(x => x != null));
            if (c.CntProblem.Length == 0) c.CntProblem = null;
            c.CpStart = PickByName(InScene<RespawnZone>(c.Scene), "CP_S4_Start", "S4 시작 체크포인트(RespawnZone)", out _);
            c.Bridge = PickByName(InScene<Lab_SectionRespawnBridge>(c.Scene), "ADAPT_S4_RespawnBridge", "S4 복귀 다리(Lab_SectionRespawnBridge)", out _);
            foreach (Component k in new Component[] { c.SpStairs, c.SpBubble, c.CntRockStairs, c.CntLaserStairs, c.CntRockBubble, c.CntLaserBubble, c.CpStart })
                if (k != null) AddName($"S4 그룹({k.name})", "S4_Respawn", Parent(k.transform), Parent(k.transform) == "S4_Respawn");

            c.Park = PickParkSpots(c.Gen, S4ParkLocal, "S4");
            s4 = c;
            report.Notes.Add($"S4 해석: GEO_S4_* {geo}개, 버블={Nm(c.Bubble)}(트리거 {(c.BubbleBox != null ? BL(LocalBoxBounds(c.BubbleBox, c.Gen)) : "없음")}" +
                             (c.Bubble != null ? $", uplift {c.Bubble.upliftAcceleration}, maxRiseHeight {c.Bubble.maxRiseHeight}" : "") + ")" +
                             $", 고정 레이저 {c.FixedLasers().Count()}/4, 조준 {c.AimStair.Count}+{c.AimBubble.Count}/6, 낙석 {c.Rocks().Count()}/3, " +
                             $"안전점 계단={Nm(c.SpStairs)} 버블={Nm(c.SpBubble)}, 카운터 " +
                             string.Join(" ", new[] { c.CntRockStairs, c.CntLaserStairs, c.CntRockBubble, c.CntLaserBubble }.Select(k => k == null ? "(없음)" : $"{k.name}(임계 {k.hitsBeforeRespawn}→{Nm(k.destination)})")) +
                             $", 복귀 다리={(c.Bridge != null ? $"'{c.Bridge.name}' 연결 {c.Bridge.WiredCount}개" : "(없음)")}, 장외 볼륨 {InScene<OutOfBoundsVolume>(c.Scene).Count}개");
            yield break;
        }

        private bool Ready4(string id)
        {
            if (s4 != null && s4.Park != null && s4.Park.Length > 0) return true;
            report.Get(id).Set(Verdict.NA, s4Problem ?? "S4 파킹점(입구 통로) 없음 — 도형을 치울 곳이 없어 검사 불가");
            return false;
        }

        private IEnumerator S4_Prepare()
        {
            if (s4 == null) yield break;
            if (s4.Park != null && s4.Park.Length > 0) yield return ParkPlayers(s4.Park, sphere, cube, tetra);
        }

        /// <summary>S4 위험 요소 피격 이벤트 전부(고정·조준 레이저 LaserBeam.OnHazardHit, 낙석 OnHitThresholdExceeded) — 간섭 감지용.</summary>
        private List<UnityEvent<GameObject>> HazardEvents4()
        {
            List<UnityEvent<GameObject>> evs = new List<UnityEvent<GameObject>>();
            foreach (FixedPeriodicLaser f in s4.FixedLasers()) if (f.beam != null && f.beam.OnHazardHit != null) evs.Add(f.beam.OnHazardHit);
            foreach (CharacterLockedLaser a in s4.AimStair.Concat(s4.AimBubble)) if (a.beam != null && a.beam.OnHazardHit != null) evs.Add(a.beam.OnHazardHit);
            foreach (FallingRockSpawner r in s4.Rocks()) if (r.OnHitThresholdExceeded != null) evs.Add(r.OnHitThresholdExceeded);
            return evs;
        }

        /// <summary>도형이 버블 안에서 떠오르는 자리 — 트리거 안쪽 1U 격자 중 고정 버블 레이저 광선(수평)·버블 낙석 레인·
        /// 버블 조준 레이저 발사점(수평 거리 − S4AimSpotMargin, 판정 17)에서 가장 먼 점.</summary>
        private Vector3 BubbleSpotLocal(out float clearance)
        {
            List<Vector2> aims = new List<Vector2>();
            foreach (CharacterLockedLaser a in s4.AimBubble) if (a != null) { Vector3 l = Lc(s4.Gen, a.transform.position); aims.Add(new Vector2(l.x, l.z)); }
            Transform gen = s4.Gen;
            Bounds lb = LocalBoxBounds(s4.BubbleBox, gen);
            BubbleHazardsXZ(out List<KeyValuePair<Vector2, Vector2>> lines, out List<Vector2> lanes);
            Vector3 best = new Vector3(lb.center.x, lb.min.y, lb.center.z);
            clearance = -1f;
            for (float x = lb.min.x + 1f; x <= lb.max.x - 1f + 1e-3f; x += 0.5f)
                for (float z = lb.min.z + 1f; z <= lb.max.z - 1f + 1e-3f; z += 0.5f)
                {
                    Vector2 p = new Vector2(x, z);
                    float c = float.MaxValue;
                    foreach (KeyValuePair<Vector2, Vector2> ln in lines) c = Mathf.Min(c, LineDistXZ(p, ln));
                    foreach (Vector2 ln in lanes) c = Mathf.Min(c, Vector2.Distance(p, ln) - 0.5f);
                    foreach (Vector2 am in aims) c = Mathf.Min(c, Vector2.Distance(p, am) - S4AimSpotMargin);
                    if (c > clearance) { clearance = c; best = new Vector3(x, lb.min.y, z); }
                }
            return best;
        }

        /// <summary>버블 위험 요소의 섹터 로컬 XZ — 고정 버블 레이저 광선(원점, 수평 방향 단위벡터)·버블 낙석 레인 중심.</summary>
        private void BubbleHazardsXZ(out List<KeyValuePair<Vector2, Vector2>> lines, out List<Vector2> lanes)
        {
            Transform gen = s4.Gen;
            lines = new List<KeyValuePair<Vector2, Vector2>>();
            foreach (FixedPeriodicLaser f in new[] { s4.FlBubble1, s4.FlBubble2 })
            {
                if (f == null) continue;
                Vector3 o = Lc(gen, f.transform.position), d = gen.InverseTransformDirection(f.transform.forward);
                lines.Add(new KeyValuePair<Vector2, Vector2>(new Vector2(o.x, o.z), new Vector2(d.x, d.z).normalized));
            }
            lanes = new List<Vector2>();
            if (s4.RockBubble != null && s4.RockBubble.spawnPositions != null)
                foreach (Transform t in s4.RockBubble.spawnPositions) if (t != null) { Vector3 l = Lc(gen, t.position); lanes.Add(new Vector2(l.x, l.z)); }
        }

        private static float LineDistXZ(Vector2 p, KeyValuePair<Vector2, Vector2> ln)
        {
            Vector2 w = p - ln.Key;
            return Mathf.Abs(w.x * ln.Value.y - w.y * ln.Value.x);
        }

        /// <summary>S4-2 버블 조준 하위 시도 자리 — 버블 트리거 안(안쪽 S4AimRiseInner) 돌 윗면 가운데, 낙석 레인·고정 광선에서 떨어지고
        /// 시작 조준점이 원뿔(maxAimAngle − 3°)·사거리(sensorRange − 0.5) 안인 점 중 발사점에 수평으로 가장 가까운 곳 [구현 결정].
        /// 설계 의도(판정 17): 버블 조준 레이저의 대상은 떠오르는 도형 — 발사점 바로 아래에서 떠오르면 스냅샷 광선이 상승 기둥과 거의 나란하다.</summary>
        private bool TryAimRiseSpot(CharacterLockedLaser al, out Vector3 ground, out string why)
        {
            ground = Vector3.zero;
            Transform gen = s4.Gen;
            Bounds lb = LocalBoxBounds(s4.BubbleBox, gen);
            Vector3 oL = Lc(gen, al.transform.position);
            BubbleHazardsXZ(out List<KeyValuePair<Vector2, Vector2>> lines, out List<Vector2> lanes);
            List<Vector2> cands = new List<Vector2>();
            for (float x = lb.min.x + S4AimRiseInner; x <= lb.max.x - S4AimRiseInner + 1e-3f; x += 0.25f)
                for (float z = lb.min.z + S4AimRiseInner; z <= lb.max.z - S4AimRiseInner + 1e-3f; z += 0.25f)
                {
                    Vector2 p = new Vector2(x, z);
                    if (lanes.Any(l => Vector2.Distance(p, l) < S4AimRiseLaneClear)) continue;
                    if (lines.Any(ln => LineDistXZ(p, ln) < S4AimRiseBeamClear)) continue;
                    cands.Add(p);
                }
            if (cands.Count == 0) { why = "버블 안 후보 0(낙석 레인·고정 광선 여유 조건)"; return false; }
            int tried = 0, noGround = 0, outCone = 0, blocked = 0;
            foreach (Vector2 p in cands.OrderBy(q => Vector2.Distance(q, new Vector2(oL.x, oL.z))))
            {
                if (++tried > 40) break;
                if (!TryGround2(gen.TransformPoint(new Vector3(p.x, lb.min.y + 3f, p.y)), 6f, null, out Vector3 g, out _) || Mathf.Abs(Lc(gen, g).y - lb.min.y) > 0.3f) { noGround++; continue; }
                Vector3 aim = g + gen.up * SpawnLift;
                if (Vector3.Distance(al.transform.position, aim) > al.sensorRange - 0.5f || Vector3.Angle(al.transform.forward, aim - al.transform.position) > al.maxAimAngle - 3f) { outCone++; continue; }
                if (SolidOverlap(g + gen.up * (SpawnLift + 0.05f), new Vector3(0.55f, 0.5f, 0.55f), gen.rotation) != null) { blocked++; continue; }
                ground = g;
                why = $"버블 안 떠오름 자리 local{V(Lc(gen, g))} — 발사점 local{V(oL)}에서 수평 {Vector2.Distance(p, new Vector2(oL.x, oL.z)):F2}U";
                return true;
            }
            why = $"버블 안 떠오름 자리 없음(가까운 {tried}곳: 돌 윗면 없음 {noGround} · 원뿔/사거리 밖 {outCone} · 고체 겹침 {blocked})";
            return false;
        }

        // ───────────── S4-1 ─────────────

        private IEnumerator S4_1_BubbleRise()
        {
            const string id = "S4-1";
            if (!Ready4(id)) yield break;
            if (s4.Bubble == null || s4.BubbleBox == null) { NA(id, NameNA(s4.BubbleProblem ?? "버블 트리거 없음")); yield break; }
            Transform gen = s4.Gen;
            Bounds lb = LocalBoxBounds(s4.BubbleBox, gen);
            bool boxOk = Vector3.Distance(lb.min, S4BubbleExpect.min) <= 0.05f && Vector3.Distance(lb.max, S4BubbleExpect.max) <= 0.05f;
            Detail(id, $"'{s4.Bubble.name}' 트리거 로컬 {BL(lb)} (판정 18 ② min(−4,8,58) max(4,18,66)) {(boxOk ? "일치" : "다름(정보)")}, uplift {s4.Bubble.upliftAcceleration}(확정 시작값 7), maxRiseHeight {s4.Bubble.maxRiseHeight}(확정 0), " +
                       $"배율 세모 {s4.Bubble.tetrahedronGravityScale}·구 {s4.Bubble.sphereGravityScale}·네모 {s4.Bubble.cubeGravityScale}, enabled={s4.Bubble.enabled}");
            Vector3 spotL = BubbleSpotLocal(out float clr);
            if (!TryGround2(gen.TransformPoint(new Vector3(spotL.x, S4RockTop + 3f, spotL.z)), 6f, null, out Vector3 g, out _))
            {
                Fail(id, $"버블 아래 돌 윗면(local y8)을 찾지 못함(local x{spotL.x:F1} z{spotL.z:F1})");
                yield break;
            }
            Detail(id, $"출발 자리 local({spotL.x:F1},{Lc(gen, g).y:F2},{spotL.z:F1}) — 고정 버블 레이저 선·낙석 레인까지 수평 여유, 조준 레이저 발사점까지 수평 거리 − {S4AimSpotMargin} 중 최소 {clr:F2}U [구현 결정]" +
                       (s4.AimBubble.Count > 0 ? " · 조준 발사점까지 수평 " + string.Join(", ", s4.AimBubble.Select(a => $"'{a.name}' {HorizDist(a.transform.position, gen.TransformPoint(spotL)):F2}U")) : " · 버블 조준 레이저 없음"));
            yield return ParkPlayers(s4.Park, sphere, cube, tetra);

            PlayerMover[] order = { tetra, sphere, cube };
            string[] names = { "세모", "구", "네모" };
            float[] expect = { 2.18f, 2.34f, 4.40f }; // [계산 초안 §6-1] 10U 상승, uplift 7
            float[] times = { -1f, -1f, -1f };
            List<string> bad = new List<string>();
            List<string> na = new List<string>();
            for (int k = 0; k < order.Length; k++)
            {
                PlayerMover v = order[k];
                string line = null;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    yield return WaitIdle(v, 5f);
                    float since = Time.time;
                    Teleport(v, g + gen.up * SpawnLift);
                    float t0 = Time.time, maxFoot = float.MinValue;
                    bool interfered = false;
                    while (Time.time - t0 < S4BubbleWindow)
                    {
                        yield return new WaitForFixedUpdate();
                        if (RespawnsSince(v, since) > 0) { interfered = true; break; }
                        Bounds sb = SolidBounds(v);
                        float foot = Lc(gen, new Vector3(sb.center.x, sb.min.y, sb.center.z)).y;
                        maxFoot = Mathf.Max(maxFoot, foot);
                        if (foot >= S4BubbleReachFootY) { times[k] = Time.time - t0; break; }
                    }
                    if (interfered)
                    {
                        line = $"{names[k]}: {attempt}회차 위험 요소 피격으로 복귀(간섭)";
                        Detail(id, line);
                        yield return WaitIdle(v, 5f);
                        continue;
                    }
                    line = times[k] >= 0f
                        ? $"{names[k]} '{v.name}': 몸 밑면 y{S4BubbleReachFootY} 도달 {times[k]:F2}s (계산 {expect[k]:F2}s — 정보) · {attempt}회차"
                        : $"{names[k]} '{v.name}': {S4BubbleWindow:F0}초 안에 y{S4BubbleReachFootY} 미도달(최고 밑면 y{maxFoot:F2})";
                    break;
                }
                Detail(id, line);
                if (times[k] < 0f)
                {
                    if (line != null && line.Contains("간섭")) na.Add($"{names[k]} 위험 간섭 3회");
                    else bad.Add($"{names[k]} 미도달");
                }
                yield return WaitIdle(v, 5f);
                yield return ParkPlayers(s4.Park, v);
                yield return new WaitForSeconds(0.3f);
                RestoreGravityIfStuck(v, "S4-1");
            }
            if (bad.Count == 0 && na.Count == 0)
            {
                bool cubeSlowest = times[2] > times[0] && times[2] > times[1];
                if (cubeSlowest) Pass(id, $"세 도형 모두 버블로 떠올라 y ≥ {S4BubbleReachFootY} 도달 — 세모 {times[0]:F2}s · 구 {times[1]:F2}s · 네모 {times[2]:F2}s(가장 느림)");
                else Fail(id, $"도달은 했으나 네모가 가장 느리지 않음 — 세모 {times[0]:F2}s · 구 {times[1]:F2}s · 네모 {times[2]:F2}s");
            }
            else if (bad.Count > 0) Fail(id, string.Join(" / ", bad.Concat(na)));
            else NA(id, "검사불가(위험 간섭): " + string.Join(" / ", na));
        }

        /// <summary>버블 밖으로 옮긴 도형이 여전히 버블 중력(useGravity 꺼짐)이면 팀 공개 API PlayerGravityOverride.RestoreDefault(0) —
        /// 팀 RespawnController가 순간이동 때 부르는 것과 같은 호출. 뒤 섹터(S5) 검사에 버블 중력이 남지 않게 한다.</summary>
        private void RestoreGravityIfStuck(PlayerMover v, string tag)
        {
            if (v == null || s4 == null || s4.BubbleBox == null) return;
            Rigidbody rb = v.GetComponent<Rigidbody>();
            PlayerGravityOverride gov = v.GetComponent<PlayerGravityOverride>();
            if (rb == null || gov == null || rb.useGravity || rb.isKinematic) return;
            if (s4.BubbleBox.bounds.Contains(v.transform.position)) return;
            gov.RestoreDefault(0f);
            report.Notes.Add($"{tag}: '{v.name}'를 버블 밖으로 옮긴 뒤에도 버블 중력이 남아 PlayerGravityOverride.RestoreDefault(0) 호출(팀 공개 API)");
        }

        // ───────────── S4-2 ─────────────

        private IEnumerator S4_2_LaserHits()
        {
            const string id = "S4-2";
            if (!Ready4(id)) yield break;
            FixedPeriodicLaser fs = s4.FlStair1 ?? s4.FlStair2;
            FixedPeriodicLaser fb = s4.FlBubble1 ?? s4.FlBubble2;
            if (s4.SpStairs == null || s4.SpBubble == null) { NA(id, NameNA(s4.SpProblem)); yield break; }
            if (fs == null && fb == null) { NA(id, NameNA(s4.FlProblem)); yield break; }
            Transform gen = s4.Gen;
            Detail(id, $"카운터 레이저 계단 '{Nm(s4.CntLaserStairs)}'" + (s4.CntLaserStairs != null ? $"(임계 {s4.CntLaserStairs.hitsBeforeRespawn}, 목적지 {Nm(s4.CntLaserStairs.destination)})" : "") +
                       $" · 버블 '{Nm(s4.CntLaserBubble)}'" + (s4.CntLaserBubble != null ? $"(임계 {s4.CntLaserBubble.hitsBeforeRespawn}, 목적지 {Nm(s4.CntLaserBubble.destination)})" : ""));
            List<string> aimPos = new List<string>();
            for (int i = 0; i < S4AimBubbleNames.Length; i++)
            {
                CharacterLockedLaser ab = s4.AimBubble.FirstOrDefault(x => x.name == S4AimBubbleNames[i]);
                if (ab == null) { aimPos.Add($"'{S4AimBubbleNames[i]}' 없음"); continue; }
                Vector3 abL = Lc(gen, ab.transform.position);
                aimPos.Add($"'{ab.name}' local{V(abL)}(초안 2차 {V(S4AimBubbleExpect[i])} {(Vector3.Distance(abL, S4AimBubbleExpect[i]) <= 0.05f ? "일치" : "다름")}, 원뿔 {ab.maxAimAngle}° 사거리 {ab.sensorRange})");
            }
            Detail(id, "버블 조준 레이저 자리(정보 — 판정 17, 조준 시도는 씬 위치 기준): " + string.Join(" · ", aimPos));
            PlayerMover v = cube;
            List<string> bad = new List<string>();
            List<string> na = new List<string>();

            // (a) 계단 고정 레이저 → S4_SP_Stairs
            if (fs == null) na.Add("(a) 계단 고정 레이저 없음: " + s4.FlProblem);
            else
            {
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                DestTrial r = new DestTrial();
                yield return LaserHit(fs, v, s4.SpStairs, gen, r);
                Detail(id, "(a) " + r.Msg);
                JudgeDest(r, "(a) 계단 레이저 → S4_SP_Stairs", bad, na, requireSupport: false);
                yield return WaitIdle(v, 5f);
                yield return ParkPlayers(s4.Park, v);
            }
            // (b) 버블 고정 레이저 → S4_SP_Bubble
            if (fb == null) na.Add("(b) 버블 고정 레이저 없음: " + s4.FlProblem);
            else
            {
                DestTrial r = new DestTrial();
                yield return LaserHit(fb, v, s4.SpBubble, gen, r, preferHover: true);
                Detail(id, "(b) " + r.Msg);
                JudgeDest(r, "(b) 버블 레이저 → S4_SP_Bubble", bad, na, requireSupport: false);
                yield return WaitIdle(v, 5f);
                yield return ParkPlayers(s4.Park, v);
                yield return new WaitForSeconds(0.3f);
                RestoreGravityIfStuck(v, "S4-2");
            }
            // 조준 레이저 — 종류(계단·버블)별 1개, 하위 결과(판정·exit 합산 안 함, 판정 11 방식)
            foreach (KeyValuePair<string, List<CharacterLockedLaser>> grp in new[]
                     {
                         new KeyValuePair<string, List<CharacterLockedLaser>>("aimStair", s4.AimStair),
                         new KeyValuePair<string, List<CharacterLockedLaser>>("aimBubble", s4.AimBubble)
                     })
            {
                bool stair = grp.Key == "aimStair";
                string nm = stair ? "조준 레이저 계단(→ S4_SP_Stairs)" : "조준 레이저 버블(→ S4_SP_Bubble)";
                CharacterLockedLaser al = grp.Value.FirstOrDefault();
                if (al == null) { report.Get(id).SetSub(grp.Key, nm, SubStatus.NA, $"검사불가(이름 불일치): '{(stair ? "S4_AL_Stair_*" : "S4_AL_Bubble_*")}' 없음(CharacterLockedLaser 전체 {s4.AllAim})"); continue; }
                DestTrial r = new DestTrial();
                yield return AimLaserTrial(al, stair ? s4.SpStairs : s4.SpBubble, r);
                Detail(id, $"하위 {nm}: {r.Msg}");
                SubStatus st = !r.Hit ? SubStatus.NA : (r.Respawned && r.AtDest ? SubStatus.Pass : SubStatus.Fail);
                report.Get(id).SetSub(grp.Key, nm, st, (st == SubStatus.NA ? "안 맞음 — " : "") + r.Msg);
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                yield return new WaitForSeconds(0.3f);
                foreach (PlayerMover p in players) RestoreGravityIfStuck(p, "S4-2 조준");
            }

            if (bad.Count > 0) Fail(id, string.Join(" / ", bad.Concat(na)));
            else if (na.Count > 0) NA(id, string.Join(" / ", na));
            else Pass(id, "계단 고정 레이저 → S4_SP_Stairs, 버블 고정 레이저 → S4_SP_Bubble(임계 1)로 복귀 — 조준 레이저는 하위 결과 참조");
        }

        private static void JudgeDest(DestTrial r, string tag, List<string> bad, List<string> na, bool requireSupport)
        {
            if (!r.Hit) na.Add($"{tag}: 피격을 만들지 못함");
            else if (!r.Respawned) bad.Add($"{tag}: 피격됐으나 복귀 없음(카운터·복귀 다리 연결 확인)");
            else if (!r.AtDest) bad.Add($"{tag}: 복귀 위치가 목적지 안전점 아님");
            else if (requireSupport && !r.SupportOk) bad.Add($"{tag}: 발밑 지지면 이상");
        }

        /// <summary>고정 주기 레이저 1회 피격 — 광선 아래 바닥(얇은 것 → 두께 무관) 위, 없으면 광선 가운데 공중에 도형을 붙잡아 두고,
        /// 예고(얇은 선) 시작을 본 뒤 warningSeconds − 0.06초에 풀어 발사 구간에 팀 물리로 맞게 한다. 최대 2회.
        /// M2R1: 광선 실제 끝점(첫 고체 거리 — 러너 규칙 FirstSolidDistance와 팀 LaserBeam 규칙(Player 태그만 통과, LaserBeam.cs:78-88) 중 짧은 쪽)을
        /// 먼저 구하고, 자리는 그 끝점 − 0.5U 안에서만 고른다. 바닥 자리는 원래 탐색 결과가 끝점 안이면 그대로(계단 (a)·S4-3 원래 동작),
        /// 밖이면 상한을 걸어 다시 찾는다. preferHover(버블 (b))면 광선 위 공중 hover 자리를 먼저 고른다.</summary>
        private IEnumerator LaserHit(FixedPeriodicLaser lz, PlayerMover v, SectionSafePoint dest, Transform gen, DestTrial r, bool preferHover = false)
        {
            LaserBeam beam = lz.beam;
            LineRenderer lr = beam != null ? beam.GetComponent<LineRenderer>() : null;
            if (beam == null || lr == null) { r.Msg = $"'{lz.name}' beam/LineRenderer 없음"; yield break; }
            Vector3 o = lz.transform.position, fw = lz.transform.forward;
            float fsdRunner = FirstSolidDistance(o, fw, beam.range, lz.transform);
            float fsdTeam = TeamBeamEnd(beam, o, fw, out string endName);
            float fsd = Mathf.Min(fsdRunner, fsdTeam);
            float limit = Mathf.Min(beam.range, fsd - 0.5f);
            string endInfo = $"광선 끝점 {fsd:F2}U(러너 규칙 {fsdRunner:F2}·팀 규칙 {fsdTeam:F2}{(endName != null ? $" '{endName}'" : " — 사거리 끝")}) local{V(Lc(gen, o + fw.normalized * fsd))}, 자리 상한 {limit:F2}U";
            if (limit < 1.5f) { r.Msg = $"'{lz.name}' 도형 둘 자리 없음 — {endInfo}(발사점 1.5U 안에서 막힘)"; yield break; }
            Vector3 place = default;
            string how = null;
            bool ok = false;
            if (preferHover) ok = TryBeamHoverSpot(o, fw, limit, gen, out place, out how);
            if (!ok)
            {
                string h1 = null;
                foreach (float th in new[] { 3f, 0f })
                {
                    if (TryBeamPlacementEx(o, fw, beam.range, lz.transform, th, 0f, out place, out how, out float ps) && ps <= limit) { ok = true; break; }
                    if (ps > limit) h1 = $"원래 탐색 자리 {ps:F2}U는 광선 끝점 뒤라 버림";
                    if (TryBeamPlacementEx(o, fw, beam.range, lz.transform, th, limit, out place, out how, out ps)) { ok = true; if (h1 != null) how += $"({h1})"; break; }
                }
            }
            if (!ok && !preferHover) ok = TryBeamHoverSpot(o, fw, limit, gen, out place, out how);
            if (!ok) { r.Msg = $"'{lz.name}' 도형 둘 자리 없음 — {endInfo}; {how}"; yield break; }
            how += " · " + endInfo;
            bool holding = false, hit = false;
            float hitAt = -1f;
            UnityAction<GameObject> cb = go => { if (!holding && !hit && Owner(go) == v) { hit = true; hitAt = Time.time; } };
            if (beam.OnHazardHit != null) beam.OnHazardHit.AddListener(cb);
            string prefix = $"'{lz.name}' 예고 {lz.warningSeconds}·발사 {lz.beamSeconds}·휴식 {lz.restSeconds}s, 자리 {how} local{V(Lc(gen, place))}, 배선 {PersistentSummary(beam.OnHazardHit)}";
            try
            {
                for (int attempt = 1; attempt <= 2 && !hit; attempt++)
                {
                    bool sawIdle = false;
                    float teleAt = -1f;
                    Func<bool> releaseNow = () =>
                    {
                        bool te = lr.enabled && lr.widthMultiplier < beam.activeWidth * 0.9f;
                        if (!te) { if (teleAt < 0f) sawIdle = true; return false; }
                        if (teleAt < 0f && sawIdle) teleAt = Time.time;
                        return teleAt >= 0f && Time.time >= teleAt + lz.warningSeconds - 0.06f;
                    };
                    bool released = false;
                    holding = true;
                    float since = Time.time;
                    yield return HoverShape(v, place, releaseNow, lz.warningSeconds + lz.beamSeconds + lz.restSeconds + 2f, x => { released = x; holding = false; });
                    holding = false;
                    if (!released) { r.Msg += $" {attempt}회차: 예고 시작을 못 봄;"; continue; }
                    float tr = Time.time;
                    while (!hit && Time.time - tr < lz.beamSeconds + 0.6f) yield return new WaitForFixedUpdate();
                    if (!hit) { r.Msg += $" {attempt}회차: 발사 구간에 피격 이벤트 없음;"; yield return WaitIdle(v, 5f); continue; }
                    r.Hit = true;
                    r.HitAfter = hitAt - since;
                    float th = Time.time;
                    while (RespawnsSince(v, since) == 0 && Time.time - th < 3f) yield return null;
                    r.Respawned = RespawnsSince(v, since) > 0;
                    yield return WaitIdle(v, 5f);
                    yield return new WaitForSeconds(0.3f);
                    r.Msg += $" {attempt}회차 피격(+{r.HitAfter:F2}s) 복귀 {(r.Respawned ? "있음" : "없음")}";
                    if (r.Respawned) VerifyDest(v, dest, gen, r);
                }
            }
            finally
            {
                if (beam != null && beam.OnHazardHit != null) beam.OnHazardHit.RemoveListener(cb);
            }
            r.Msg = prefix + ":" + r.Msg;
        }

        /// <summary>조준 레이저 1회 시도(판정 11 방식) — 대상 종류 도형을 원뿔(maxAimAngle − 3°)·사거리(sensorRange − 1) 안 평평한 바닥에
        /// 세워 두고(붙잡지 않음 — 붙잡으면 팀 레이저가 무효 대상으로 본다) 예고 2초 + 발사를 기다린다.
        /// 버블 조준(판정 17)은 발사점 바로 아래 버블 안에 놓아 떠오르게 한다(TryAimRiseSpot) — 못 찾으면 바닥 방식.</summary>
        private IEnumerator AimLaserTrial(CharacterLockedLaser al, SectionSafePoint dest, DestTrial r)
        {
            Transform gen = s4.Gen;
            if (al.beam == null) { r.Msg = $"'{al.name}' beam 없음"; yield break; }
            if (al.targetKinds == null || al.targetKinds.Length == 0) { r.Msg = $"'{al.name}' targetKinds 비어 있음"; yield break; }
            PlayerMover v = ShapeOf(al.targetKinds[0]);
            Vector3 origin = al.transform.position, fwd = al.transform.forward;
            Bounds bub = s4.BubbleBox != null ? s4.BubbleBox.bounds : new Bounds(Vector3.one * 1e6f, Vector3.zero);
            bub.Expand(1.6f);
            List<Vector3> avoid = new List<Vector3>();
            foreach (SectionSafePoint sp in new[] { s4.SpStairs, s4.SpBubble }) if (sp != null) avoid.AddRange(SafePoints(sp));
            List<KeyValuePair<Vector3, Vector3>> beams = s4.FixedLasers().Where(f => f.beam != null)
                .Select(f => new KeyValuePair<Vector3, Vector3>(f.transform.position, f.transform.position + f.transform.forward * f.beam.range)).ToList();
            List<Vector3> lanes = new List<Vector3>();
            foreach (FallingRockSpawner rs in s4.Rocks()) if (rs.spawnPositions != null) foreach (Transform t in rs.spawnPositions) if (t != null) lanes.Add(t.position);
            Vector3 best = Vector3.zero;
            float bestAng = float.MaxValue;
            // 버블 조준(판정 17 — 발사점이 버블 북쪽 벽 윗부분에서 아래를 본다): 버블 밖 평평한 바닥은 원뿔·사거리 밖이거나 벽 뒤라
            // 떠오르는 도형으로 시도한다 [구현 결정]. 자리를 못 찾으면 아래 바닥 탐색으로 넘어간다.
            bool rise = false;
            string riseWhy = null;
            if (s4.AimBubble.Contains(al) && s4.BubbleBox != null && TryAimRiseSpot(al, out Vector3 rg, out riseWhy))
            {
                rise = true;
                best = rg;
                bestAng = Vector3.Angle(fwd, rg + gen.up * SpawnLift - origin);
            }
            if (!rise)
            for (float x = -18f; x <= 18f; x += 1f)
                for (float z = 40f; z <= 84f; z += 1f)
                {
                    if (!TryGround2(gen.TransformPoint(new Vector3(x, 30f, z)), 40f, null, out Vector3 g, out Vector3 n)) continue;
                    if (Vector3.Dot(n, gen.up) < 0.98f) continue;
                    Vector3 aim = g + gen.up * 0.5f;
                    if (bub.Contains(aim)) continue;
                    float dist = Vector3.Distance(origin, aim);
                    if (dist < 2f || dist > al.sensorRange - 1f) continue;
                    float ang = Vector3.Angle(fwd, aim - origin);
                    if (ang > al.maxAimAngle - 3f) continue;
                    if (FirstSolidDistance(origin, aim - origin, dist, al.transform) < dist - 0.6f) continue;
                    if (avoid.Any(p => HorizDist(p, g) < 2.5f)) continue;
                    if (beams.Any(b => SegDist(aim, b.Key, b.Value) < 1.3f)) continue;
                    if (lanes.Any(l => HorizDist(l, g) < 1.5f)) continue;
                    if (SolidOverlap(g + gen.up * (SpawnLift + 0.05f), new Vector3(0.55f, 0.5f, 0.55f), gen.rotation) != null) continue;
                    if (ang < bestAng) { bestAng = ang; best = g; }
                }
            if (bestAng == float.MaxValue) { r.Msg = $"'{al.name}'(대상 {al.targetKinds[0]}) 원뿔 {al.maxAimAngle}°·사거리 {al.sensorRange} 안 평평한 빈 바닥 없음" + (riseWhy != null ? " · " + riseWhy : ""); yield break; }
            bool hit = false;
            float hitAt = -1f;
            UnityAction<GameObject> cb = go => { if (!hit && Owner(go) == v) { hit = true; hitAt = Time.time; } };
            if (al.beam.OnHazardHit != null) al.beam.OnHazardHit.AddListener(cb);
            string prefix = $"'{al.name}' 발사점 local{V(Lc(gen, origin))} 대상 {al.targetKinds[0]} '{Nm(v)}', 자리 local{V(Lc(gen, best))}(정면에서 {bestAng:F1}°, 거리 {Vector3.Distance(origin, best + gen.up * 0.5f):F1}), 예고 {al.firstFireDelay}s" +
                            (rise ? $" · 버블 안에서 떠오르는 도형으로 시도({riseWhy})" : " · 서 있는 도형으로 시도");
            // 떠오름 시도는 첫 발사가 빗나가도 재조준(relock) 뒤 두 번째 발사까지 본다 — 도형이 버블 윗면 근처에 머무는 동안.
            float window = rise ? al.firstFireDelay + al.relockDelay + al.refireDelay + al.beamDuration + 1f : al.firstFireDelay + al.beamDuration + 3f;
            try
            {
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                yield return WaitIdle(v, 5f);
                float since = Time.time;
                Teleport(v, best + gen.up * SpawnLift);
                float t0 = Time.time;
                while (!hit && Time.time - t0 < window && RespawnsSince(v, since) == 0) yield return new WaitForFixedUpdate();
                if (!hit)
                {
                    r.Msg = RespawnsSince(v, since) > 0 ? " 조준 피격 없이 복귀됨(다른 위험 요소)" : $" {window:F1}초 동안 피격 없음(록온·시야 조건 — 도형이 움직였을 수 있음, 끝 local{V(Lc(gen, v.transform.position))})";
                }
                else
                {
                    r.Hit = true;
                    r.HitAfter = hitAt - since;
                    float th = Time.time;
                    while (RespawnsSince(v, since) == 0 && Time.time - th < 3f) yield return null;
                    r.Respawned = RespawnsSince(v, since) > 0;
                    yield return WaitIdle(v, 5f);
                    yield return new WaitForSeconds(0.3f);
                    r.Msg = $" 피격(+{r.HitAfter:F2}s) 복귀 {(r.Respawned ? "있음" : "없음")}";
                    if (r.Respawned) VerifyDest(v, dest, gen, r);
                }
            }
            finally
            {
                if (al.beam != null && al.beam.OnHazardHit != null) al.beam.OnHazardHit.RemoveListener(cb);
            }
            r.Msg = prefix + ":" + r.Msg;
        }

        // ───────────── S4-3 ─────────────

        private sealed class RockTrial
        {
            public bool Hit, HeldHit;
            public string Msg = "";
        }

        private IEnumerator S4_3_RockHits()
        {
            const string id = "S4-3";
            if (!Ready4(id)) yield break;
            if (s4.RockStairA == null && s4.RockStairB == null && s4.RockBubble == null) { NA(id, NameNA(s4.RockProblem)); yield break; }
            if (s4.SpStairs == null || s4.SpBubble == null) { NA(id, NameNA(s4.SpProblem)); yield break; }
            if (s4.CntRockStairs == null || s4.CntRockBubble == null) { NA(id, NameNA(s4.CntProblem)); yield break; }
            Transform gen = s4.Gen;
            PlayerMover v = cube;
            Detail(id, $"안전점 트리거(A안 = 없음): S4_SP_Stairs {(s4.SpStairs.GetComponent<Collider>() == null ? "없음" : "있음")}(counter {Nm(s4.SpStairs.counter)}), " +
                       $"S4_SP_Bubble {(s4.SpBubble.GetComponent<Collider>() == null ? "없음" : "있음")}(counter {Nm(s4.SpBubble.counter)}) · 낙석 카운터 임계 계단 {s4.CntRockStairs.hitsBeforeRespawn}·버블 {s4.CntRockBubble.hitsBeforeRespawn}(확정 2)" +
                       string.Join("", s4.Rocks().Select(rs => $" · '{rs.name}' 주기 {rs.spawnInterval}s·생성기 임계 {rs.hitsBeforeRespawn}·소멸 낙하 {rs.despawnFallDistance}·OnHitThresholdExceeded {PersistentSummary(rs.OnHitThresholdExceeded)}")));

            // 관찰 전용 리스너 — 카운터 경고(1회째)·임계(2회째), 생성기 피격
            int warnS = 0, thrS = 0, warnB = 0, thrB = 0, rockEv = 0;
            UnityAction<GameObject> wS = go => { if (Owner(go) == v) warnS++; };
            UnityAction<GameObject, SectionSafePoint> tS = (go, sp) => { if (Owner(go) == v) thrS++; };
            UnityAction<GameObject> wB = go => { if (Owner(go) == v) warnB++; };
            UnityAction<GameObject, SectionSafePoint> tB = (go, sp) => { if (Owner(go) == v) thrB++; };
            UnityAction<GameObject> rk = go => { if (Owner(go) == v) rockEv++; };
            SectionHitCounter cS = s4.CntRockStairs, cB = s4.CntRockBubble;
            if (cS.OnWarning != null) cS.OnWarning.AddListener(wS);
            if (cS.OnThresholdReached != null) cS.OnThresholdReached.AddListener(tS);
            if (cB.OnWarning != null) cB.OnWarning.AddListener(wB);
            if (cB.OnThresholdReached != null) cB.OnThresholdReached.AddListener(tB);
            foreach (FallingRockSpawner rs in s4.Rocks()) if (rs.OnHitThresholdExceeded != null) rs.OnHitThresholdExceeded.AddListener(rk);
            List<string> bad = new List<string>(), na = new List<string>();
            try
            {
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                // ── 계단: 낙석 1 → 레이저 복귀 → 낙석 1(횟수 유지면 임계 도달)
                FallingRockSpawner rsS = s4.RockStairA ?? s4.RockStairB;
                Transform laneS = FirstLane(rsS);
                FixedPeriodicLaser fl = s4.FlStair1 ?? s4.FlStair2;
                if (rsS == null || laneS == null) na.Add("계단: 낙석 생성기/레인 없음");
                else if (fl == null) na.Add("계단: 계단 고정 레이저 없음 — " + s4.FlProblem);
                else
                {
                    Func<int> hitsS = () => rockEv + warnS + thrS; // 단조 증가 합 — RockHitOnce가 시도마다 전후를 비교한다
                    // 앞 검사(S4-1·S4-2)에서 남았을 수 있는 이 도형의 낙석 횟수를 0으로 — 팀 공개 API ResetFor(안전점 트리거가 부르는 것과 같은 호출)
                    cS.ResetFor(v.gameObject);
                    RockTrial r1 = new RockTrial();
                    yield return RockHitOnce(rsS, laneS, v, gen, hitsS, r1, 9f);
                    Detail(id, $"계단 1) '{rsS.name}' 레인 '{laneS.name}': {r1.Msg} → 경고 {warnS}·임계 {thrS}");
                    if (!r1.Hit) na.Add("계단 1회째 낙석 피격을 만들지 못함");
                    else
                    {
                        if (thrS > 0) bad.Add("계단 낙석 1회째에 임계 도달(임계 2 아님)");
                        else if (warnS == 0) bad.Add("계단 낙석 피격이 카운터 S4_Counter_RockStairs에 집계되지 않음(배선 확인)");
                        int w0 = warnS, t0 = thrS;
                        DestTrial lt = new DestTrial();
                        yield return LaserHit(fl, v, s4.SpStairs, gen, lt);
                        Detail(id, $"계단 2) 레이저 복귀: {lt.Msg} · 낙석 카운터 경고 {warnS}·임계 {thrS}(레이저 전 {w0}·{t0})");
                        if (!lt.Hit) na.Add("계단 레이저 피격을 만들지 못함");
                        else if (!lt.Respawned || !lt.AtDest) bad.Add("계단 레이저 피격 → S4_SP_Stairs 복귀 안 됨");
                        else
                        {
                            float since = Time.time;
                            RockTrial r3 = new RockTrial();
                            yield return RockHitOnce(rsS, laneS, v, gen, hitsS, r3, 9f);
                            float th = Time.time;
                            while (RespawnsSince(v, since) == 0 && Time.time - th < 3f) yield return null;
                            bool resp = RespawnsSince(v, since) > 0;
                            yield return WaitIdle(v, 5f);
                            yield return new WaitForSeconds(0.3f);
                            DestTrial d3 = new DestTrial { Hit = r3.Hit, Respawned = resp };
                            if (resp) VerifyDest(v, s4.SpStairs, gen, d3);
                            Detail(id, $"계단 3) 레이저 복귀 뒤 낙석: {r3.Msg} → 경고 {warnS}·임계 {thrS}, 복귀 {(resp ? "있음" : "없음")}{d3.Msg}");
                            if (!r3.Hit) na.Add("계단 레이저 뒤 낙석 피격을 만들지 못함");
                            else if (r3.HeldHit) na.Add("계단 3회째 낙석이 붙잡힌 동안 맞음(풀기 시점 예측 실패)");
                            else if (thrS == t0) bad.Add("레이저 복귀 뒤 낙석 1회에 임계 미도달 — 레이저 복귀가 낙석 횟수를 0으로 만듦(A안 위반)");
                            else if (!resp || !d3.AtDest) bad.Add("계단 낙석 2회째(횟수 유지) 뒤 S4_SP_Stairs 복귀 안 됨");
                        }
                    }
                }
                yield return WaitIdle(v, 5f);
                yield return ParkPlayers(s4.Park, v);

                // ── 버블: 낙석 2회 → S4_SP_Bubble
                Transform laneB = FirstLane(s4.RockBubble);
                if (s4.RockBubble == null || laneB == null) na.Add("버블: 낙석 생성기/레인 없음");
                else
                {
                    Func<int> hitsB = () => rockEv + warnB + thrB;
                    // S4-1 버블 상승 중 낙석에 맞았다면 남은 횟수가 있다 — 0으로(팀 공개 API ResetFor)
                    cB.ResetFor(v.gameObject);
                    RockTrial b1 = new RockTrial();
                    yield return RockHitOnce(s4.RockBubble, laneB, v, gen, hitsB, b1, 9f);
                    Detail(id, $"버블 1) '{s4.RockBubble.name}' 레인 '{laneB.name}': {b1.Msg} → 경고 {warnB}·임계 {thrB}");
                    if (!b1.Hit) na.Add("버블 1회째 낙석 피격을 만들지 못함");
                    else
                    {
                        if (thrB > 0) bad.Add("버블 낙석 1회째에 임계 도달(임계 2 아님)");
                        float since = Time.time;
                        int tb0 = thrB;
                        RockTrial b2 = new RockTrial();
                        yield return RockHitOnce(s4.RockBubble, laneB, v, gen, hitsB, b2, 9f);
                        float th = Time.time;
                        while (RespawnsSince(v, since) == 0 && Time.time - th < 3f) yield return null;
                        bool resp = RespawnsSince(v, since) > 0;
                        yield return WaitIdle(v, 5f);
                        yield return new WaitForSeconds(0.3f);
                        DestTrial d2 = new DestTrial { Hit = b2.Hit, Respawned = resp };
                        if (resp) VerifyDest(v, s4.SpBubble, gen, d2);
                        Detail(id, $"버블 2) {b2.Msg} → 경고 {warnB}·임계 {thrB}, 복귀 {(resp ? "있음" : "없음")}{d2.Msg}");
                        if (!b2.Hit) na.Add("버블 2회째 낙석 피격을 만들지 못함");
                        else if (b2.HeldHit) na.Add("버블 2회째 낙석이 붙잡힌 동안 맞음(풀기 시점 예측 실패)");
                        else if (thrB == tb0) bad.Add("버블 낙석 2회째에 임계 미도달");
                        else if (!resp || !d2.AtDest) bad.Add("버블 낙석 2회 뒤 S4_SP_Bubble 복귀 안 됨");
                    }
                }
            }
            finally
            {
                if (cS != null && cS.OnWarning != null) cS.OnWarning.RemoveListener(wS);
                if (cS != null && cS.OnThresholdReached != null) cS.OnThresholdReached.RemoveListener(tS);
                if (cB != null && cB.OnWarning != null) cB.OnWarning.RemoveListener(wB);
                if (cB != null && cB.OnThresholdReached != null) cB.OnThresholdReached.RemoveListener(tB);
                foreach (FallingRockSpawner rs in s4.Rocks()) if (rs != null && rs.OnHitThresholdExceeded != null) rs.OnHitThresholdExceeded.RemoveListener(rk);
            }
            yield return WaitIdle(v, 5f);
            yield return ParkPlayers(s4.Park, v);
            yield return new WaitForSeconds(0.3f);
            RestoreGravityIfStuck(v, "S4-3");

            if (bad.Count > 0) Fail(id, string.Join(" / ", bad.Concat(na)));
            else if (na.Count > 0) NA(id, "검사불가(사유): " + string.Join(" / ", na));
            else Pass(id, "계단: 낙석 1(경고) → 레이저 복귀 S4_SP_Stairs → 낙석 1로 임계(횟수 유지, A안) → S4_SP_Stairs / 버블: 낙석 2회 → S4_SP_Bubble");
        }

        private static Transform FirstLane(FallingRockSpawner sp)
        {
            if (sp == null || sp.spawnPositions == null) return null;
            return sp.spawnPositions.FirstOrDefault(t => t != null);
        }

        /// <summary>낙석 1회 피격 — 레인 바로 아래 바닥에 도형을 붙잡아 두고, 그 생성기 낙석이 도형 윗면에 닿기 0.12초 전에 푼다.
        /// hits()가 늘면 피격. 붙잡힌 동안 늘었으면 HeldHit(팀이 복귀를 거절했을 수 있음).</summary>
        private IEnumerator RockHitOnce(FallingRockSpawner sp, Transform lane, PlayerMover v, Transform gen, Func<int> hits, RockTrial rt, float timeout)
        {
            if (!TryGround2(lane.position + Vector3.down * 0.6f, 40f, sp.transform, out Vector3 g, out _)) { rt.Msg = "레인 아래 바닥 없음"; yield break; }
            Vector3 spot = g + gen.up * SpawnLift;
            float gAcc = Mathf.Max(0.1f, -Physics.gravity.y);
            Func<bool> releaseNow = () =>
            {
                Bounds vb = SolidBounds(v);
                foreach (FallingRock rock in Object.FindObjectsOfType<FallingRock>())
                {
                    if (rock == null || rock.owner != sp) continue;
                    if (HorizDist(rock.transform.position, spot) > 0.8f) continue;
                    Collider rc = rock.GetComponent<Collider>();
                    float bottom = rc != null ? rc.bounds.min.y : rock.transform.position.y - 0.5f;
                    float h = bottom - vb.max.y;
                    if (h < -0.3f) continue;
                    Rigidbody rrb = rock.GetComponent<Rigidbody>();
                    float vy = rrb != null ? Mathf.Max(0f, -rrb.velocity.y) : 0f;
                    float t = (-vy + Mathf.Sqrt(vy * vy + 2f * gAcc * Mathf.Max(0f, h))) / gAcc;
                    if (t <= 0.12f) return true;
                }
                return false;
            };
            float t0 = Time.time;
            int attempt = 0;
            while (!rt.Hit && Time.time - t0 < timeout)
            {
                attempt++;
                int before = hits();
                bool released = false;
                yield return HoverShape(v, spot, releaseNow, Mathf.Max(0.5f, timeout - (Time.time - t0)), x => released = x);
                if (hits() > before) { rt.Hit = true; rt.HeldHit = true; rt.Msg += $" {attempt}회차: 붙잡힌 동안 맞음;"; break; }
                if (!released) { rt.Msg += $" {attempt}회차: 창 안에 낙석이 오지 않음;"; break; }
                float tr = Time.time;
                while (hits() == before && Time.time - tr < 1.0f) yield return new WaitForFixedUpdate();
                if (hits() > before) { rt.Hit = true; rt.Msg += $" {attempt}회차 피격(자리 local{V(Lc(gen, spot))});"; }
                else rt.Msg += $" {attempt}회차 빗나감;";
            }
        }

        // ───────────── S4-4 ─────────────

        private IEnumerator S4_4_StairFall()
        {
            const string id = "S4-4";
            if (!Ready4(id)) yield break;
            Transform gen = s4.Gen;
            PlayerMover v = cube;
            float floorW = gen.TransformPoint(Vector3.zero).y;
            int oobN = InScene<OutOfBoundsVolume>(s4.Scene).Count;
            Detail(id, $"킬 라인 killY {(respawn != null ? respawn.killY.ToString("F1", CultureInfo.InvariantCulture) : "(RespawnController 없음)")} · 원통 바닥 월드 y {floorW:F1}(섹터 로컬 0) · S4 씬 장외 볼륨 {oobN}개(기대 0 [확정 S4 §3-8])");
            bool hazard = false;
            UnityAction<GameObject> hz = go => { if (Owner(go) == v) hazard = true; };
            List<UnityEvent<GameObject>> evs = HazardEvents4();
            foreach (UnityEvent<GameObject> e in evs) e.AddListener(hz);
            bool decided = false;
            int tries = 0;
            try
            {
                yield return ParkPlayers(s4.Park, sphere, cube, tetra);
                foreach (float phi in S4FallPhiDeg)
                {
                    if (tries >= 3 || decided) break;
                    float rad = phi * Mathf.Deg2Rad;
                    Vector2 d2 = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    Vector3 edgeL = new Vector3(S4CylAxisXZ.x + d2.x * (S4StairOuterR - 0.7f), 30f, S4CylAxisXZ.y + d2.y * (S4StairOuterR - 0.7f));
                    if (!TryGround2(gen.TransformPoint(edgeL), 40f, null, out Vector3 g, out _)) { Detail(id, $"φ{phi}: 바닥 없음 — 건너뜀"); continue; }
                    float stairY = Lc(gen, g).y;
                    if (stairY < 0.5f || stairY > S4RockTop + 0.2f) { Detail(id, $"φ{phi}: 첫 면 local y{stairY:F2} — 계단 윗면 아님, 건너뜀"); continue; }
                    tries++;
                    hazard = false;
                    yield return WaitIdle(v, 5f);
                    Teleport(v, g + gen.up * SpawnLift);
                    yield return new WaitForSeconds(0.3f);
                    float since = Time.time;
                    Vector3 outW = gen.TransformDirection(new Vector3(d2.x, 0f, d2.y));
                    DriveResult dr = new DriveResult();
                    yield return DriveShape(v, gen, outW, 3f, 3f, lp => lp.y < stairY - 0.6f, dr);
                    // 착지 대기(최대 4초)
                    float tl = Time.time;
                    Rigidbody rb = v.GetComponent<Rigidbody>();
                    while (Time.time - tl < 4f)
                    {
                        yield return new WaitForFixedUpdate();
                        if (rb != null && Mathf.Abs(rb.velocity.y) < 0.05f && Lc(gen, SolidBounds(v).center).y < 1.5f) break;
                    }
                    yield return new WaitForSeconds(0.3f);
                    Vector3 landL = Lc(gen, v.transform.position);
                    bool onFloor = CheckSupport(v, gen, 0f, out string sup);
                    // 5초 관찰 — 순간이동(한 스텝 1.0U 넘는 이동)·복귀 이벤트·장외 볼륨
                    Vector3 prev = v.transform.position;
                    float maxStep = 0f;
                    bool oob = OutOfBoundsVolume.AnyContains(prev);
                    float to = Time.time;
                    while (Time.time - to < S4FallObserve)
                    {
                        yield return new WaitForFixedUpdate();
                        Vector3 p = v.transform.position;
                        maxStep = Mathf.Max(maxStep, Vector3.Distance(p, prev));
                        prev = p;
                        if (OutOfBoundsVolume.AnyContains(p)) oob = true;
                    }
                    int resp = RespawnsSince(v, since);
                    Detail(id, $"φ{phi}: 계단 가장자리 local y{stairY:F2}에서 바깥으로 밀기(3U/s, {dr.Elapsed:F1}s, 가장자리 벗어남 {(dr.Reached ? "예" : "아니오")}) → 착지 local{V(landL)}, 발밑: {sup} · " +
                               $"5초 관찰 최대 스텝 이동 {maxStep:F2}U, 복귀 {resp}회, 장외 볼륨 {oob}, 위험 요소 피격 {hazard}");
                    if (resp > 0 && hazard) { Detail(id, $"φ{phi}: 위험 요소 피격으로 복귀(간섭) — 다른 자리로 다시"); yield return WaitIdle(v, 5f); continue; }
                    if (!dr.Reached) { Detail(id, $"φ{phi}: 가장자리에서 떨어지지 않음 — 다른 자리로 다시"); continue; }
                    List<string> bad = new List<string>();
                    if (!onFloor) bad.Add("착지 발밑이 원통 바닥(local y0) 아님 — " + sup);
                    if (resp > 0) bad.Add($"떨어진 뒤 복귀 {resp}회(복귀 없어야 함)");
                    if (maxStep > 1.0f) bad.Add($"순간이동 의심(한 스텝 {maxStep:F2}U)");
                    if (oob) bad.Add("장외 볼륨 안에 들어감(장외 볼륨 없어야 함)");
                    decided = true;
                    if (bad.Count == 0) Pass(id, $"계단(φ{phi}, 높이 {stairY:F2})에서 떨어져 원통 바닥(y0)에 섰고 5초 동안 복귀·순간이동 없음 — 킬 라인·장외 볼륨 미개입");
                    else Fail(id, string.Join(" / ", bad));
                }
            }
            finally
            {
                foreach (UnityEvent<GameObject> e in evs) if (e != null) e.RemoveListener(hz);
            }
            if (!decided) NA(id, tries == 0 ? "계단 가장자리 시험 자리를 찾지 못함(계단 윗면 레이 실패)" : $"검사불가(위험 간섭 또는 밀어도 안 떨어짐): {tries}회 시도");
            yield return WaitIdle(v, 5f);
            yield return ParkPlayers(s4.Park, v);
        }

        // ═════════════════════════ PTF-3: S6 포탈 ═════════════════════════

        private IEnumerator ResolveS6()
        {
            if (!director.TryGetSector(6, out SectorController sc) || sc == null) { s6Problem = "섹터 6이 로드되지 않음(섹터 씬 없음)"; yield break; }
            S6Ctx c = new S6Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;

            c.Balls.AddRange(InScene<EnergyBall>(c.Scene).OrderBy(x => x.name, StringComparer.Ordinal));
            c.Surfaces.AddRange(InScene<PortalSurface>(c.Scene).OrderBy(x => x.name, StringComparer.Ordinal));
            c.Movables.AddRange(InScene<MovablePortalPanel>(c.Scene).OrderBy(x => x.name, StringComparer.Ordinal));
            c.Levers.AddRange(InScene<PanelLever>(c.Scene).OrderBy(x => x.name, StringComparer.Ordinal));
            c.Zones.AddRange(InScene<RespawnZone>(c.Scene).OrderBy(x => x.name, StringComparer.Ordinal));
            int geo = CountNamedPrefix(c.Gen, "GEO_S6_");
            if (geo == 0 && c.Surfaces.Count == 0 && c.Balls.Count == 0)
            {
                s6Problem = "S6 미구체화(빈 틀) — Generated 아래 GEO_S6_* 0개·PortalSurface 0개·EnergyBall 0개(S6 빌더·배선 결과 없음 — INT-2 반영 전일 수 있음)";
                yield break;
            }
            c.Group = FindDeep(c.Gen, "S6_Gimmicks");
            AddName("S6 기믹 그룹", "S6_Gimmicks", c.Group != null ? PathUnder(c.Group, c.Gen) : "(없음)" + NearNames(c.Gen, "Gimmick"), c.Group != null);
            c.Park = PickParkSpots(c.Gen, S6ParkLocal, "S6");
            s6 = c;
            int movSurf = c.Surfaces.Count(x => x.GetComponent<MovablePortalPanel>() != null);
            report.Notes.Add($"S6 해석: GEO_S6_* {geo}개, EnergyBall {c.Balls.Count}, PortalSurface {c.Surfaces.Count}(고정 {c.Surfaces.Count - movSurf} + 움직이는 패널 부착 {movSurf}), " +
                             $"MovablePortalPanel {c.Movables.Count}, PanelLever {c.Levers.Count}, RespawnZone {c.Zones.Count}");
            yield break;
        }

        private bool Ready6(string id)
        {
            if (s6 != null) return true;
            report.Get(id).Set(Verdict.NA, s6Problem ?? "S6 해석 실패");
            return false;
        }

        private IEnumerator S6_Prepare()
        {
            if (s6 == null) yield break;
            if (s6.Park != null && s6.Park.Length > 0) yield return ParkPlayers(s6.Park, sphere, cube, tetra);
            else report.Notes.Add("S6 준비: 파킹점 0 — 도형을 옮기지 않았다(포탈 시험 경로에 다른 도형이 있을 수 있음)");
        }

        // ───────────── S6-1 ─────────────

        private IEnumerator S6_1_Presence()
        {
            const string id = "S6-1";
            if (!Ready6(id)) yield break;
            Transform gen = s6.Gen;
            List<string> bad = new List<string>();

            // ① 에너지볼 2 — 짝은 팀 필드 color로 고르고, 이름은 대조만 한다 [계약 R3 R2-C3 "타입·개수로 찾은 뒤 실제 이름"].
            if (s6.Balls.Count != S6BallExpect) bad.Add($"EnergyBall {s6.Balls.Count}개(기대 {S6BallExpect})");
            CheckS6Ball(id, EnergyColor.Orange, "S6_Ball_Orange", S6BallOrangeLocal, "ANCH_S6_Ball_Orange", bad);
            CheckS6Ball(id, EnergyColor.Blue, "S6_Ball_Blue", S6BallBlueLocal, "ANCH_S6_Ball_Blue", bad);

            // ② 고정 패널 = PortalSurface 중 MovablePortalPanel이 없는 것 ≥ 4. 움직이는 패널에도 팀 생성 함수가 PortalSurface를 붙인다
            //    (SpacePortalMenuItem.BuildMovablePortalPanel → BuildPortalSurface, SpacePortalMenuItem.cs:201-204).
            List<PortalSurface> fixedS = s6.Surfaces.Where(x => x.GetComponent<MovablePortalPanel>() == null).ToList();
            List<PortalSurface> movS = s6.Surfaces.Where(x => x.GetComponent<MovablePortalPanel>() != null).ToList();
            Detail(id, $"PortalSurface {s6.Surfaces.Count}개 = 고정 {fixedS.Count} [" +
                       string.Join(", ", fixedS.Select(x => $"{x.name} 면 local{V(Lc(gen, FaceCenter(x)))} 법선{V(gen.InverseTransformDirection(x.transform.forward))}")) +
                       $"] + 움직이는 패널 부착 {movS.Count} [{string.Join(", ", movS.Select(x => x.name))}]");
            if (fixedS.Count < S6FixedPanelMin) bad.Add($"고정 PortalSurface {fixedS.Count}개(기대 ≥ {S6FixedPanelMin})");
            for (int k = 1; k <= S6FixedPanelMin; k++)
            {
                string nm = "S6_Panel_" + k;
                PortalSurface hit = fixedS.FirstOrDefault(x => x.name == nm);
                Transform anc = FindInScene(s6.Scene, "ANCH_S6_Panel_" + k);
                string pose = hit != null && anc != null ? $" · 면↔'ANCH_S6_Panel_{k}' {Vector3.Distance(FaceCenter(hit), anc.position):F3}U" : "";
                AddName($"S6 고정 패널 {k}(PortalSurface)", nm, hit != null ? PathUnder(hit.transform, gen) + pose : "(없음)", hit != null);
            }

            // ③ 움직이는 패널 2
            if (s6.Movables.Count != S6MovableExpect) bad.Add($"MovablePortalPanel {s6.Movables.Count}개(기대 {S6MovableExpect})");
            foreach (MovablePortalPanel mp in s6.Movables)
                Detail(id, $"MovablePortalPanel '{PathUnder(mp.transform, gen)}' mode {mp.mode} · 진행도 {mp.Progress:F2} · PortalSurface {(mp.GetComponent<PortalSurface>() != null ? "있음" : "없음")} · 루트 local{V(Lc(gen, mp.transform.position))}");
            for (int k = 1; k <= S6MovableExpect; k++)
            {
                string nm = "S6_MovPanel_" + k;
                MovablePortalPanel hit = s6.Movables.FirstOrDefault(x => x.name == nm);
                AddName($"S6 움직이는 패널 {k}(MovablePortalPanel)", nm, hit != null ? PathUnder(hit.transform, gen) : "(없음)", hit != null);
            }

            // ④ 레버 2 — 각 panel이 S6 움직이는 패널이고 서로 다른 패널(1:1)
            if (s6.Levers.Count != S6LeverExpect) bad.Add($"PanelLever {s6.Levers.Count}개(기대 {S6LeverExpect})");
            HashSet<MovablePortalPanel> driven = new HashSet<MovablePortalPanel>();
            foreach (PanelLever lv in s6.Levers)
            {
                bool okPanel = lv.panel != null && s6.Movables.Contains(lv.panel);
                if (!okPanel) bad.Add($"레버 '{lv.name}'.panel = {Nm(lv.panel)} — S6 움직이는 패널이 아님");
                else if (!driven.Add(lv.panel)) bad.Add($"레버 '{lv.name}'.panel '{lv.panel.name}'을 다른 레버도 가리킴(짝 중복)");
                Detail(id, $"PanelLever '{PathUnder(lv.transform, gen)}' → panel '{Nm(lv.panel)}' · invertDirection {lv.invertDirection} · 루트 local{V(Lc(gen, lv.transform.position))}");
            }
            for (int k = 1; k <= S6LeverExpect; k++)
            {
                string nm = "S6_Lever_" + k;
                PanelLever hit = s6.Levers.FirstOrDefault(x => x.name == nm);
                AddName($"S6 레버 {k}(PanelLever → 짝 패널)", $"{nm} → S6_MovPanel_{k}", hit != null ? $"{nm} → {Nm(hit.panel)}" : "(없음)",
                        hit != null && hit.panel != null && hit.panel.name == "S6_MovPanel_" + k);
            }
            if (s6.Levers.Count == S6LeverExpect && s6.Movables.Count == S6MovableExpect && driven.Count != S6MovableExpect)
                bad.Add($"레버가 움직이는 패널 {S6MovableExpect}개 중 {driven.Count}개만 가리킴");

            if (s6.Group != null)
            {
                int outside = s6.Balls.Count(x => !x.transform.IsChildOf(s6.Group)) + s6.Surfaces.Count(x => !x.transform.IsChildOf(s6.Group)) +
                              s6.Levers.Count(x => !x.transform.IsChildOf(s6.Group));
                Detail(id, $"S6_Gimmicks 밖 팀 기믹(볼·패널·레버) {outside}개(계약 K0-3 '그룹 1개 아래' — 정보)");
            }
            if (bad.Count == 0)
                Pass(id, $"에너지볼 2(주황·파랑 시작 자리 x·z ±{S6BallTol}), 고정 PortalSurface {fixedS.Count}(≥{S6FixedPanelMin}) + 움직이는 패널 부착 {movS.Count}, " +
                         $"MovablePortalPanel {s6.Movables.Count}, PanelLever {s6.Levers.Count}(짝 패널 1:1) — 이름은 '이름 대조' 참고");
            else Fail(id, string.Join(" / ", bad));
            yield break;
        }

        /// <summary>팀 EnergyBall.color로 볼을 고르고 시작 자리를 대조한다. y는 팀 흔들림(bobHeight, EnergyBall.cs:68-69)만큼 더 허용한다.</summary>
        private void CheckS6Ball(string id, EnergyColor color, string expectName, Vector3 expectLocal, string anchorName, List<string> bad)
        {
            Transform gen = s6.Gen;
            List<EnergyBall> same = s6.Balls.Where(b => b.color == color).ToList();
            if (same.Count != 1)
            {
                bad.Add($"{color} EnergyBall {same.Count}개(기대 1)");
                AddName($"S6 에너지볼 {color}", expectName, same.Count == 0 ? "(없음)" : string.Join("|", same.Select(b => b.name)), false);
                return;
            }
            EnergyBall ball = same[0];
            AddName($"S6 에너지볼 {color}", expectName, ball.name == expectName ? ball.name : PathUnder(ball.transform, gen), ball.name == expectName);
            Vector3 l = Lc(gen, ball.transform.position);
            float yTol = Mathf.Abs(ball.bobHeight) + S6BallTol;
            bool ok = Mathf.Abs(l.x - expectLocal.x) <= S6BallTol && Mathf.Abs(l.z - expectLocal.z) <= S6BallTol && Mathf.Abs(l.y - expectLocal.y) <= yTol;
            Transform anc = FindInScene(s6.Scene, anchorName);
            string ancTxt = anc != null ? $", 앵커 '{anchorName}' local{V(Lc(gen, anc.position))}" : $", 앵커 '{anchorName}' 없음";
            Detail(id, $"{color} 볼 '{ball.name}' 상태 {ball.State} local{V(l)} (기대 {V(expectLocal)} [제안] x·z ±{S6BallTol}, y ±{yTol:F2} = 팀 bobHeight {ball.bobHeight} + {S6BallTol}){ancTxt} → {(ok ? "일치" : "다름")}");
            if (!ok) bad.Add($"{color} 볼 자리 local{V(l)} ≠ {V(expectLocal)}");
            if (ball.State != EnergyBall.BallState.Ground) bad.Add($"{color} 볼 상태 {ball.State}(기대 Ground — 시작 자리)");
        }

        // ───────────── S6-2 ─────────────

        private IEnumerator S6_2_RespawnZones()
        {
            const string id = "S6-2";
            if (!Ready6(id)) yield break;
            Transform gen = s6.Gen;
            string[] names = { "S6_Zone_Start", "S6_Zone_Check_1", "S6_Zone_Check_2" };   // [제안 S6_Wiring.cs:17-19]
            string[] anchors = { "ANCH_S6_Start", "ANCH_S6_Check_1", "ANCH_S6_Check_2" };  // [제안 S6_Builder.cs:176-178] 구역 루트 = 앵커 ±0.02 [계약 K6-1]
            foreach (RespawnZone z in s6.Zones)
            {
                BoxCollider bc = z.GetComponent<BoxCollider>();
                Detail(id, $"RespawnZone '{PathUnder(z.transform, gen)}' 루트 local{V(Lc(gen, z.transform.position))}" +
                           (bc != null ? $" · 구역 {BL(LocalBoxBounds(bc, gen))} · isTrigger {bc.isTrigger}" : " · BoxCollider 없음") +
                           (s6.Group != null && !z.transform.IsChildOf(s6.Group) ? " · S6_Gimmicks 밖" : ""));
            }
            for (int i = 0; i < names.Length; i++)
            {
                RespawnZone hit = s6.Zones.FirstOrDefault(z => z.name == names[i]);
                Transform anc = FindInScene(s6.Scene, anchors[i]);
                string pose = hit != null && anc != null ? $" · 루트↔'{anchors[i]}' {Vector3.Distance(hit.transform.position, anc.position):F3}U(계약 K6-1 ±0.02)" : "";
                AddName($"S6 복귀 구역 {i + 1}(RespawnZone)", names[i], hit != null ? PathUnder(hit.transform, gen) + pose : "(없음)", hit != null);
            }
            if (s6.Zones.Count == S6ZoneExpect) Pass(id, $"RespawnZone {s6.Zones.Count}개(시작 1 + 체크 2 [판정 4]) — 이름·앵커 거리는 '이름 대조' 참고");
            else Fail(id, $"RespawnZone {s6.Zones.Count}개(기대 {S6ZoneExpect} = 시작 1 + 체크 2 [판정 4]) [{string.Join(", ", s6.Zones.Select(z => z.name))}]");
            yield break;
        }

        // ───────────── S6-3 ─────────────

        /// <summary>[C1 (가)·C2] P1·P3 입구 포탈에 도형을 넣어 Lab_AntiStuck 개입이 0인지 잰다. 포탈 한 쌍을 팀 공개 창구로 놓고
        /// (주황 = 입구 바닥 패널, 파랑 = 짝 패널), 입구마다 ① 걸어 들어가기(네모, AddForce 속도 변화) ② 선 채로 가장자리에서 빠지기(구)를 한 번씩 한다.
        /// 우물 낙하(③)·착지 여유 0.86 실측은 Unity 실행 담당 대기열 몫이다(R3 :807) — 여기서 하지 않는다.</summary>
        private IEnumerator S6_3_AntiStuckPortal()
        {
            const string id = "S6-3";
            if (!Ready6(id)) yield break;
            Transform gen = s6.Gen;
            SpacePortal[] existing = Object.FindObjectsOfType<SpacePortal>();
            if (SpacePortal.Orange != null || SpacePortal.Blue != null || existing.Length > 0)
            {
                NA(id, $"검사불가(기존 포탈 있음): SpacePortal {existing.Length}개(Orange={Nm(SpacePortal.Orange)}, Blue={Nm(SpacePortal.Blue)}) — 러너는 남이 놓은 포탈을 옮기지 않는다");
                yield break;
            }
            if (cube.GetComponent<Lab_AntiStuck>() == null || sphere.GetComponent<Lab_AntiStuck>() == null)
            {
                NA(id, $"검사불가: 도형에 Lab_AntiStuck 없음(네모 {(cube.GetComponent<Lab_AntiStuck>() != null ? "있음" : "없음")} · 구 {(sphere.GetComponent<Lab_AntiStuck>() != null ? "있음" : "없음")})");
                yield break;
            }

            // 태그, 입구 앵커, 입구 이름(앵커에 맞는 면이 없을 때만), 출구 앵커, 출구 이름, 출구 설명
            string[][] entries =
            {
                new[] { "P1", "ANCH_S6_Panel_1", "S6_Panel_1", "ANCH_S6_Panel_2", "S6_Panel_2", "P2(왼벽 벽감, 초안 동선 ① P1→P2)" },
                new[] { "P3", "ANCH_S6_Panel_3", "S6_Panel_3", "ANCH_S6_MovPanel_1", "S6_MovPanel_1", "M1 시작 포즈(초안 S6_배치초안.md:47 '주황 P1→P3, 파랑 P2→M1')" },
            };
            Detail(id, "포탈 설치 = 팀 공개 창구 AddComponent<SpacePortal> → color 대입 → PlaceAt(면 중심, 패널 forward, 패널 up, 패널)" +
                       "(팀 PlayerEnergyReceiver.cs:333-342 ConfirmPlacement 순서, 조준 hit.point = 면 위 점 :236·:243). 실행 중에만 놓고 검사 뒤 Destroy [2차판정 19]");
            Detail(id, "도형 구동 = ExternallyDriven 없이 Rigidbody.AddForce(속도 변화) — Lab_AntiStuck은 ExternallyDriven·kinematic 몸에 개입하지 않으므로(Lab_AntiStuck.cs:36-38) 붙잡지 않는다");
            Application.logMessageReceived -= OnAntiStuckLog;
            Application.logMessageReceived += OnAntiStuckLog;

            List<string> bad = new List<string>();
            List<string> okTags = new List<string>();
            List<string> naTags = new List<string>();
            int ran = 0;
            foreach (string[] e in entries)
            {
                string tag = e[0];
                PortalSurface inS = FindS6Panel(e[1], e[2], out string inHow);
                PortalSurface outS = FindS6Panel(e[3], e[4], out string outHow);
                Detail(id, $"{tag}: 입구 {inHow} / 출구 {e[5]}: {outHow}");
                if (inS == null || outS == null || inS == outS)
                {
                    naTags.Add($"{tag} 패널 못 찾음");
                    report.Get(id).SetSub(tag + "walk", tag + " 걸어 들어가기", SubStatus.NA, "패널 못 찾음");
                    report.Get(id).SetSub(tag + "stand", tag + " 서서 빠지기", SubStatus.NA, "패널 못 찾음");
                    continue;
                }
                if (Vector3.Dot(inS.transform.forward, Vector3.up) < 0.9f) Detail(id, $"{tag}: 입구 패널 법선 {V(inS.transform.forward)} — 바닥 패널(+Y)이 아님(정보)");
                SpacePortal orange = PlaceS6Portal(EnergyColor.Orange, inS);
                SpacePortal blue = PlaceS6Portal(EnergyColor.Blue, outS);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Detail(id, $"{tag}: 주황 '{orange.name}' local{V(Lc(gen, orange.transform.position))} {orange.width}×{orange.height}·트리거 두께 {orange.triggerDepth} → 파랑 local{V(Lc(gen, blue.transform.position))} " +
                           $"법선{V(gen.InverseTransformDirection(blue.transform.forward))} · 짝 {(orange.Partner == blue && blue.Partner == orange ? "맞음" : "안 맞음")}");

                PortalTrial walk = new PortalTrial();
                yield return S6WalkIn(inS, cube, walk);
                yield return WaitIdle(cube, 4f);
                yield return ParkPlayers(s6.Park, cube);
                PortalTrial stand = new PortalTrial();
                yield return S6StandDrop(inS, orange, sphere, walk.Ran ? walk.SideDir : Vector3.zero, stand);
                yield return WaitIdle(sphere, 4f);
                yield return ParkPlayers(s6.Park, sphere);

                bool entryOk = false;
                foreach (KeyValuePair<string, PortalTrial> kv in new[] { new KeyValuePair<string, PortalTrial>("walk", walk), new KeyValuePair<string, PortalTrial>("stand", stand) })
                {
                    PortalTrial r = kv.Value;
                    string nm = tag + (kv.Key == "walk" ? " 걸어 들어가기" : " 서서 빠지기");
                    Detail(id, $"{nm}: {r.Msg}");
                    foreach (string lg in r.Logs.Take(4)) Detail(id, $"{nm} 로그: {lg}");
                    if (!r.Ran) { report.Get(id).SetSub(tag + kv.Key, nm, SubStatus.NA, r.Msg); continue; }
                    ran++;
                    if (r.Interventions > 0 || r.Logs.Count > 0)
                    {
                        bad.Add($"{nm}: AntiStuck 개입 {r.Interventions}회·로그 {r.Logs.Count}줄" + (r.Logs.Count > 0 ? " — " + r.Logs[0] : ""));
                        report.Get(id).SetSub(tag + kv.Key, nm, SubStatus.Fail, $"AntiStuck 개입 {r.Interventions}회");
                    }
                    else if (!r.Teleported) report.Get(id).SetSub(tag + kv.Key, nm, SubStatus.NA, "순간이동 안 됨(개입 0)");
                    else
                    {
                        entryOk = true;
                        report.Get(id).SetSub(tag + kv.Key, nm, SubStatus.Pass, $"순간이동 +{r.TeleportAfter:F2}s, 개입 0");
                    }
                }
                if (entryOk) okTags.Add(tag); else naTags.Add($"{tag} 순간이동한 시도 0");

                DestroyS6Portals();
                yield return null;
                yield return new WaitForFixedUpdate();
                Detail(id, $"{tag}: 포탈 제거 뒤 SpacePortal.Orange={Nm(SpacePortal.Orange)} · Blue={Nm(SpacePortal.Blue)} · 패널 시각 메쉬 켜짐 {VisualOn(inS)}/{VisualOn(outS)}" +
                           "(팀 SpacePortal.OnDestroy → PortalSurface.RefreshOccupancy가 충돌 무시를 푼다 — SpacePortal.cs:128-137, PortalSurface.cs:43-72)");
            }
            Application.logMessageReceived -= OnAntiStuckLog;

            if (bad.Count > 0) Fail(id, string.Join(" / ", bad));
            else if (okTags.Count == entries.Length)
                Pass(id, $"P1·P3 포탈 진입에서 Lab_AntiStuck 개입 0(InterventionCount 증가 0·'[Lab_AntiStuck]' 로그 0) — 시도 {ran}회, 시도별 결과는 하위 결과 [C1 (가)·C2]");
            else NA(id, $"검사불가(진입 성립 안 함): {string.Join(" / ", naTags)} — 돌린 시도 {ran}회의 AntiStuck 개입은 0");
        }

        private void OnAntiStuckLog(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.StartsWith("[Lab_AntiStuck]", StringComparison.Ordinal)) antiStuckLog.Add(condition);
        }

        /// <summary>PortalSurface 바깥 면 중심(로컬 +Z 면, PortalSurface.cs:10-14 규약).</summary>
        private static Vector3 FaceCenter(PortalSurface s)
        {
            BoxCollider b = s.Box;
            return b == null ? s.transform.position : s.transform.TransformPoint(b.center + new Vector3(0f, 0f, b.size.z * 0.5f));
        }

        private static bool VisualOn(PortalSurface s)
        {
            MeshRenderer mr = s != null ? s.GetComponentInChildren<MeshRenderer>() : null;
            return mr != null && mr.enabled;
        }

        /// <summary>앵커(씬 Transform — C15로 바뀔 수 있어 상수를 쓰지 않는다)와 면 중심이 0.1 안에 있는 PortalSurface. 없으면 이름으로.</summary>
        private PortalSurface FindS6Panel(string anchorName, string fallbackName, out string how)
        {
            Transform anc = FindInScene(s6.Scene, anchorName);
            if (anc != null)
            {
                PortalSurface best = null;
                float bd = float.MaxValue;
                foreach (PortalSurface ps in s6.Surfaces)
                {
                    float d = Vector3.Distance(FaceCenter(ps), anc.position);
                    if (d < bd) { bd = d; best = ps; }
                }
                if (best != null && bd <= 0.1f)
                {
                    how = $"앵커 '{anchorName}' local{V(Lc(s6.Gen, anc.position))}에 면이 맞는 '{best.name}'({bd:F3}U)";
                    return best;
                }
                how = $"앵커 '{anchorName}' local{V(Lc(s6.Gen, anc.position))}에 면이 맞는 패널 없음(가장 가까운 '{Nm(best)}' {(best != null ? bd.ToString("F2", CultureInfo.InvariantCulture) : "-")}U)";
            }
            else how = $"앵커 '{anchorName}' 없음";
            PortalSurface byName = s6.Surfaces.FirstOrDefault(x => x.name == fallbackName);
            how += byName != null ? $" → 이름 '{fallbackName}'으로 고름" : $" → 이름 '{fallbackName}'도 없음";
            return byName;
        }

        /// <summary>팀 PlayerEnergyReceiver.ConfirmPlacement(:333-342)와 같은 순서 — new GameObject → AddComponent&lt;SpacePortal&gt; → color → PlaceAt.
        /// 팀 이름 SpacePortal_{color}(:338)에 _FUNC를 붙여 러너 생성물임을 드러낸다. 검사 뒤 DestroyS6Portals로 없앤다 [2차판정 19].</summary>
        private SpacePortal PlaceS6Portal(EnergyColor color, PortalSurface surf)
        {
            GameObject go = new GameObject($"SpacePortal_{color}_FUNC");
            s6Portals.Add(go);
            SpacePortal p = go.AddComponent<SpacePortal>();
            p.color = color;
            p.PlaceAt(FaceCenter(surf), surf.transform.forward, surf.transform.up, surf.transform);
            return p;
        }

        private void DestroyS6Portals()
        {
            foreach (GameObject g in s6Portals) if (g != null) Object.Destroy(g);
            s6Portals.Clear();
        }

        /// <summary>팀 PlayerMover.FixedUpdate가 다음 스텝에 수평 속도를 어떻게 바꿀지(공개 값만으로) 예측한다. AddForce(속도 변화)는 그 대입 뒤
        /// 물리 스텝에서 더해지므로(추정 — Unity 실행으로 확인), 목표 − 예측을 넣으면 그 스텝 수평 속도 ≈ 목표다.
        /// ExternallyDriven이면 그대로(:310-316), 조작권 없음·입력 잠금이면 감쇠(:331-335·:592-601), 조작 중·입력 없음이면 0(평지 접지 :476, 공중 :363).</summary>
        private static Vector3 PredictMoverHoriz(PlayerMover m, Vector3 horiz)
        {
            if (m.ExternallyDriven) return horiz;
            if (!m.IsControlled || m.InputLocked) return horiz * (1f - Mathf.Clamp01(m.uncontrolledDamping * Time.fixedDeltaTime));
            return Vector3.zero;
        }

        /// <summary>① 걸어 들어가기: 입구 패널 둘레 바닥(패널 가장자리 바깥 2.5)에 세우고, 패널 중심 쪽으로 팀 걷기 속도(moveSpeed)로 민다.
        /// 출발 쪽은 패널 −up, +up, −right, +right 순서로 첫 번째 가능한 쪽(출발점 = 면 높이 ±0.05, 경로 = 면 −0.25~+0.05 바닥·고체 겹침 0 — 구덩이 디딤 허용).</summary>
        private IEnumerator S6WalkIn(PortalSurface inS, PlayerMover m, PortalTrial r)
        {
            Transform gen = s6.Gen;
            Vector3 face = FaceCenter(inS);
            Vector2 half = inS.HalfExtents();
            Vector3[] axes = { -inS.transform.up, inS.transform.up, -inS.transform.right, inS.transform.right };
            float[] halves = { half.y, half.y, half.x, half.x };
            List<string> why = new List<string>();
            Vector3 startG = default, side = default;
            bool found = false;
            for (int i = 0; i < axes.Length && !found; i++)
            {
                Vector3 d = new Vector3(axes[i].x, 0f, axes[i].z);
                if (d.sqrMagnitude < 0.25f) { why.Add($"축{i} 수평 아님"); continue; }
                d.Normalize();
                bool ok = true;
                Vector3 g0 = default;
                for (int s = 0; s <= 5 && ok; s++)
                {
                    float t = s * 0.5f; // 출발점(t 0) → 패널 가장자리(t 2.5)
                    Vector3 q = face + d * (halves[i] + S6WalkStartBeyond - t);
                    if (!TryGround2(q + Vector3.up * 3f, 6f, null, out Vector3 g, out _)) { ok = false; why.Add($"local{V(Lc(gen, q))} 바닥 없음"); break; }
                    float dy = g.y - face.y;
                    if (s == 0 ? Mathf.Abs(dy) > 0.05f : (dy < -0.25f || dy > 0.05f)) { ok = false; why.Add($"local{V(Lc(gen, q))} 바닥 높이 면 {dy:+0.00;-0.00}"); break; }
                    Collider blk = SolidOverlap(g + Vector3.up * 0.6f, new Vector3(0.45f, 0.45f, 0.45f), Quaternion.LookRotation(d, Vector3.up));
                    if (blk != null) { ok = false; why.Add($"local{V(Lc(gen, q))} '{blk.name}'와 겹침"); break; }
                    if (s == 0) g0 = g;
                }
                if (!ok) continue;
                startG = g0;
                side = d;
                found = true;
            }
            if (!found) { r.Msg = "걸어 들어갈 출발 자리 없음: " + string.Join(", ", why.Take(6)); yield break; }

            Lab_AntiStuck ast = m.GetComponent<Lab_AntiStuck>();
            Rigidbody rb = m.GetComponent<Rigidbody>();
            if (ast == null || rb == null) { r.Msg = "Lab_AntiStuck·Rigidbody 없음"; yield break; }
            r.SideDir = side;
            yield return WaitIdle(m, 5f);
            Teleport(m, startG + Vector3.up * SpawnLift);
            yield return new WaitForSeconds(0.6f); // 착지·안정 — 선 상태에서 출발
            r.Ran = true;
            Vector3 dir = -side;
            float speed = m.moveSpeed > 0.1f ? m.moveSpeed : 5f;
            int c0 = ast.InterventionCount, l0 = antiStuckLog.Count;
            Vector3 start = rb.position, prev = rb.position, lastPre = rb.position;
            float t0 = Time.time, tTel = -1f;
            bool pushing = true;
            while (Time.time - t0 < S6WalkTimeout)
            {
                if (pushing && !rb.isKinematic && !m.ExternallyDriven)
                {
                    Vector3 v = rb.velocity;
                    rb.AddForce(dir * speed - PredictMoverHoriz(m, new Vector3(v.x, 0f, v.z)), ForceMode.VelocityChange);
                }
                yield return new WaitForFixedUpdate();
                if (m == null || rb == null) break;
                Vector3 p = rb.position;
                if (tTel < 0f && (rb.isKinematic || (p - prev).magnitude > S6TeleportJump))
                {
                    tTel = Time.time;
                    pushing = false;
                    r.Teleported = true;
                    r.TeleportAfter = tTel - t0;
                    r.ExitLocal = Lc(gen, p);
                }
                if (tTel < 0f) lastPre = p;
                // 패널 중심을 지났거나 구덩이로 떨어졌는데 순간이동이 없으면 더 밀지 않는다 — 구덩이 벽에 몸을 밀어붙여
                // 러너가 만든 관통으로 AntiStuck이 개입하는 것을 막는다(관찰은 창 끝까지 계속).
                if (pushing && tTel < 0f && (Vector3.Dot(p - face, dir) >= 0f || p.y < face.y - 0.6f)) pushing = false;
                prev = p;
                if (tTel >= 0f && Time.time - tTel >= S6AfterTeleport) break;
            }
            float walked = Vector3.Dot(lastPre - start, dir);
            float walkTime = (r.Teleported ? r.TeleportAfter : Time.time - t0);
            r.Interventions = ast.InterventionCount - c0;
            r.Logs.AddRange(antiStuckLog.Skip(l0).Where(s => s.Contains("'" + m.name + "'")));
            r.Msg = $"{Nm(m)} 출발 local{V(Lc(gen, startG))}(패널 가장자리 바깥 {S6WalkStartBeyond}U) 방향 local{V(gen.InverseTransformDirection(dir))} · 목표 {speed:F1}U/s(팀 moveSpeed) · " +
                    (r.Teleported ? $"순간이동 +{r.TeleportAfter:F2}s(그 전 진행 {walked:F2}U, 평균 {(walkTime > 0.01f ? walked / walkTime : 0f):F2}U/s) → local{V(r.ExitLocal)}"
                                  : $"순간이동 없음({S6WalkTimeout}s, 진행 {walked:F2}U, 끝 local{V(Lc(gen, rb.position))})") +
                    $" · AntiStuck InterventionCount {c0}→{ast.InterventionCount}(증가 {r.Interventions}) · 로그 {r.Logs.Count}줄";
        }

        /// <summary>② 선 채로 가장자리에서 빠지기: 걸어 들어온 쪽(없으면 패널 −up)의 포탈 가장자리 안쪽 0.2, 면 위 0.6에 수평 속도 0으로 놓는다
        /// (포탈이 놓인 패널은 충돌이 무시되므로 선 몸이 그대로 빠진다 — PortalSurface.cs:43-49).</summary>
        private IEnumerator S6StandDrop(PortalSurface inS, SpacePortal portal, PlayerMover m, Vector3 sideHint, PortalTrial r)
        {
            Transform gen = s6.Gen;
            Vector3 face = FaceCenter(inS);
            Vector3 n = inS.transform.forward;
            Vector3 side = sideHint.sqrMagnitude > 0.5f ? sideHint : Vector3.ProjectOnPlane(-inS.transform.up, n).normalized;
            float alongUp = Mathf.Abs(Vector3.Dot(side, portal.transform.up)), alongRight = Mathf.Abs(Vector3.Dot(side, portal.transform.right));
            float edge = alongUp >= alongRight ? portal.height * 0.5f : portal.width * 0.5f;
            Vector3 pos = face + side * Mathf.Max(0f, edge - S6StandEdgeInset) + n * SpawnLift;
            Collider blk = SolidOverlap(pos, new Vector3(0.45f, 0.45f, 0.45f), Quaternion.identity);
            if (blk != null) { r.Msg = $"선 자리 local{V(Lc(gen, pos))}가 '{blk.name}'와 겹침"; yield break; }
            Lab_AntiStuck ast = m.GetComponent<Lab_AntiStuck>();
            Rigidbody rb = m.GetComponent<Rigidbody>();
            if (ast == null || rb == null) { r.Msg = "Lab_AntiStuck·Rigidbody 없음"; yield break; }
            r.SideDir = side;
            yield return WaitIdle(m, 5f);
            r.Ran = true;
            int c0 = ast.InterventionCount, l0 = antiStuckLog.Count;
            Teleport(m, pos);
            Vector3 prev = rb.position;
            float t0 = Time.time, tTel = -1f;
            while (Time.time - t0 < S6StandTimeout)
            {
                yield return new WaitForFixedUpdate();
                if (m == null || rb == null) break;
                Vector3 p = rb.position;
                if (tTel < 0f && (rb.isKinematic || (p - prev).magnitude > S6TeleportJump))
                {
                    tTel = Time.time;
                    r.Teleported = true;
                    r.TeleportAfter = tTel - t0;
                    r.ExitLocal = Lc(gen, p);
                }
                prev = p;
                if (tTel >= 0f && Time.time - tTel >= S6AfterTeleport) break;
            }
            r.Interventions = ast.InterventionCount - c0;
            r.Logs.AddRange(antiStuckLog.Skip(l0).Where(s => s.Contains("'" + m.name + "'")));
            r.Msg = $"{Nm(m)} 선 자리 local{V(Lc(gen, pos))}(포탈 가장자리 안쪽 {S6StandEdgeInset}, 면 위 {SpawnLift}, 수평 속도 0) · " +
                    (r.Teleported ? $"순간이동 +{r.TeleportAfter:F2}s → local{V(r.ExitLocal)}" : $"순간이동 없음({S6StandTimeout}s, 끝 local{V(Lc(gen, rb.position))})") +
                    $" · AntiStuck InterventionCount {c0}→{ast.InterventionCount}(증가 {r.Interventions}) · 로그 {r.Logs.Count}줄";
        }

        /// <summary>S6-3이 예외로 끊겨도 포탈·로그 관찰을 걷는다(포탈이 남아 있으면 도형을 먼저 파킹점으로 옮긴다 — 충돌이 되살아날 때 패널 밑에 끼지 않게).</summary>
        private IEnumerator S6_Cleanup()
        {
            Application.logMessageReceived -= OnAntiStuckLog;
            int left = s6Portals.Count(g => g != null);
            if (left == 0) yield break;
            if (s6 != null && s6.Park != null && s6.Park.Length > 0) yield return ParkPlayers(s6.Park, sphere, cube, tetra);
            DestroyS6Portals();
            report.Notes.Add($"S6 정리: 남은 검사 포탈 {left}개 제거(S6-3이 중간에 끊김)");
            yield return null;
        }

        // ═════════════════════════ PTF-3: S7 서버실 ═════════════════════════

        private IEnumerator ResolveS7()
        {
            if (!director.TryGetSector(7, out SectorController sc) || sc == null) { s7Problem = "섹터 7이 로드되지 않음(섹터 씬 없음)"; yield break; }
            S7Ctx c = new S7Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;
            int geo = CountNamedPrefix(c.Gen, "GEO_S7_");
            List<PowerMaintenanceController> pmc = InScene<PowerMaintenanceController>(c.Scene);
            if (geo == 0 && pmc.Count == 0)
            {
                s7Problem = "S7 미구체화(빈 틀) — Generated 아래 GEO_S7_* 0개·PowerMaintenanceController 0개(S7 빌더·배선 결과 없음 — INT-2 반영 전일 수 있음)";
                yield break;
            }
            c.Group = FindDeep(c.Gen, "S7_Gimmicks");
            AddName("S7 기믹 그룹", "S7_Gimmicks", c.Group != null ? PathUnder(c.Group, c.Gen) : "(없음)" + NearNames(c.Gen, "Gimmick"), c.Group != null);
            c.GimmickRoot = FindDeep(c.Group != null ? c.Group : c.Gen, "S7_GimmickRoot");
            AddName("S7 기믹 루트(지연 활성화 [판정 13])", "S7_Gimmicks/S7_GimmickRoot", c.GimmickRoot != null ? PathUnder(c.GimmickRoot, c.Gen) : "(없음)",
                    c.GimmickRoot != null && Parent(c.GimmickRoot) == "S7_Gimmicks");
            c.RootActiveAtResolve = c.GimmickRoot != null && c.GimmickRoot.gameObject.activeSelf;

            List<Transform> all = c.Gen.GetComponentsInChildren<Transform>(true).ToList();
            List<Transform> en = all.Where(t => t.name == "TEMP_S7_EntryDoor_Open").ToList();
            List<Transform> ex = all.Where(t => t.name == "TEMP_S7_ExitDoor_Open").ToList();
            c.EntryMarkerCount = en.Count; c.ExitMarkerCount = ex.Count;
            c.EntryMarker = en.FirstOrDefault(); c.ExitMarker = ex.FirstOrDefault();
            AddName("S7 TEMP 입구 개구 표지", "TEMP_S7_EntryDoor_Open ×1", en.Count == 0 ? "(없음)" + NearNames(c.Gen, "TEMP") : $"{PathUnder(en[0], c.Gen)} ×{en.Count}", en.Count == 1);
            AddName("S7 TEMP 출구 개구 표지", "TEMP_S7_ExitDoor_Open ×1", ex.Count == 0 ? "(없음)" + NearNames(c.Gen, "TEMP") : $"{PathUnder(ex[0], c.Gen)} ×{ex.Count}", ex.Count == 1);

            List<RespawnZone> zones = InScene<RespawnZone>(c.Scene);
            AddName("S7 수동 R 체크포인트(RespawnZone) [C12]", "S7_Gimmicks/S7_Zone_Check ×1",
                    zones.Count == 0 ? "(없음)" : string.Join("|", zones.Select(z => PathUnder(z.transform, c.Gen))), zones.Count == 1 && zones[0].name == "S7_Zone_Check");
            c.Park = PickParkSpots(c.Gen, S7ParkLocal, "S7");
            s7 = c;
            report.Notes.Add($"S7 해석: GEO_S7_* {geo}개, PowerMaintenanceController {pmc.Count}, QuizTerminal {InScene<QuizTerminal>(c.Scene).Count}, RoleSlot {InScene<RoleSlot>(c.Scene).Count}, " +
                             $"WiringPort {InScene<WiringPort>(c.Scene).Count}, RespawnZone {zones.Count}, doorPhysics {InScene<doorPhysics>(c.Scene).Count}, " +
                             $"기믹 루트 activeSelf {c.RootActiveAtResolve}(저장 상태 비활성 [판정 13] — 해석 시점 실행 값)");
            yield break;
        }

        private bool Ready7(string id)
        {
            if (s7 != null) return true;
            report.Get(id).Set(Verdict.NA, s7Problem ?? "S7 해석 실패");
            return false;
        }

        private IEnumerator S7_Prepare()
        {
            if (s7 == null) yield break;
            if (s7.Park != null && s7.Park.Length > 0) yield return ParkPlayers(s7.Park, sphere, cube, tetra);
            else report.Notes.Add("S7 준비: 파킹점 0 — 도형을 옮기지 않았다");
        }

        // ───────────── S7-2 ─────────────

        private IEnumerator S7_2_PowerEntranceWiring()
        {
            const string id = "S7-2";
            if (!Ready7(id)) yield break;
            Transform gen = s7.Gen;
            List<PowerMaintenanceController> list = InScene<PowerMaintenanceController>(s7.Scene);
            PowerMaintenanceController pm = PickByName(list, "S7_PowerMaintenance", "S7 전력 유지(PowerMaintenanceController)", out string prob);
            Detail(id, $"PowerMaintenanceController {list.Count}개 [{string.Join(", ", list.Select(x => PathUnder(x.transform, gen)))}] · QuizTerminal {InScene<QuizTerminal>(s7.Scene).Count}개 · RoleSlot {InScene<RoleSlot>(s7.Scene).Count}개(계약 R3 R2-C3 :392 기대 1·1·3)");
            report.Get(id).SetSub("flow", "회로 배정·문답 흐름", SubStatus.NA,
                "검사불가(팀 콘텐츠 없음) — W_ENTRY 회로 배정 팀 코드 없음(AssignCircuit 호출처 PowerMaintenanceController.cs:418 하나)·문답/책 콘텐츠 빈 배열 [C9 · 계약 R3 :397]");
            if (list.Count != 1 || pm == null)
            {
                Fail(id, $"PowerMaintenanceController {list.Count}개(기대 1)" + (prob != null ? " — " + prob : ""));
                yield break;
            }
            Detail(id, $"'{pm.name}' 활성 {pm.gameObject.activeInHierarchy}: wiringPanel {Nm(pm.wiringPanel)} · quiz {Nm(pm.quiz)} · bookSlot {Nm(pm.bookSlot)} · computerSlot {Nm(pm.computerSlot)} · " +
                       $"powerSlot {Nm(pm.powerSlot)} · circuits {(pm.circuits == null ? 0 : pm.circuits.Length)}개 · entranceWiring {Nm(pm.entranceWiring)}");
            if (pm.entranceWiring == null)
                Pass(id, "PowerMaintenanceController 1개, entranceWiring = null(TEMP — 팀 코드가 입구 배선 검사를 건너뜀, PowerMaintenanceController.cs:63 툴팁) [C9]");
            else
                Fail(id, $"entranceWiring = '{pm.entranceWiring.name}' — TEMP 기간 비어 있어야 함 [C9](스테이징 S7_Wiring LinkEntranceWiring 반영 확인)");
        }

        // ───────────── S7-3 ─────────────

        private IEnumerator S7_3_PortsOnServers()
        {
            const string id = "S7-3";
            if (!Ready7(id)) yield break;
            Transform gen = s7.Gen;
            const string prefix = "S7_W_POWER_Port_";
            List<WiringPort> allPorts = InScene<WiringPort>(s7.Scene);
            List<WiringPort> ports = allPorts.Where(p => p.name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(p => { int k = Array.IndexOf(S7PortSuffix, p.name.Substring(prefix.Length)); return k < 0 ? 99 : k; })
                .ThenBy(p => p.name, StringComparer.Ordinal).ToList();
            AddName("S7 전력 포트(WiringPort) S7_W_POWER_Port_*", "6 [OUT_A, OUT_B, OUT_C, IN_1, IN_2, IN_3]",
                    $"{ports.Count} [{string.Join(", ", ports.Select(p => p.name.Substring(prefix.Length)))}] (WiringPort 전체 {allPorts.Count})",
                    ports.Count == 6 && ports.Select(p => p.name.Substring(prefix.Length)).SequenceEqual(S7PortSuffix));

            List<Bounds> servers = new List<Bounds>();
            List<string> missing = new List<string>();
            for (int k = 1; k <= 6; k++)
            {
                Transform t = FindInScene(s7.Scene, "GEO_S7_Server_" + k);
                Collider col = t != null ? t.GetComponent<Collider>() : null;
                if (col == null && t != null) col = t.GetComponentsInChildren<Collider>(true).FirstOrDefault(x => !x.isTrigger);
                if (col == null) { missing.Add("GEO_S7_Server_" + k); servers.Add(default); continue; }
                servers.Add(LocalBoxBounds(col, gen));
            }
            if (missing.Count > 0) { NA(id, "검사불가(이름 불일치): " + string.Join(", ", missing) + " 없음(또는 콜라이더 없음)" + NearNames(gen, "Server")); yield break; }

            List<string> bad = new List<string>();
            if (ports.Count != 6) bad.Add($"전력 포트 {ports.Count}개(기대 6)");
            int[] idx = new int[ports.Count];
            for (int i = 0; i < ports.Count; i++)
            {
                Vector3 pl = Lc(gen, ports[i].transform.position);
                int best = -1;
                float bd = float.MaxValue;
                for (int s = 0; s < servers.Count; s++)
                {
                    Bounds b = servers[s];
                    float dx = Mathf.Max(0f, Mathf.Max(b.min.x - pl.x, pl.x - b.max.x));
                    float dz = Mathf.Max(0f, Mathf.Max(b.min.z - pl.z, pl.z - b.max.z));
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < bd) { bd = d; best = s; }
                }
                idx[i] = best;
                Bounds sb = servers[best];
                float dMax = Mathf.Abs(pl.z - sb.max.z), dMin = Mathf.Abs(pl.z - sb.min.z);
                float faceDz = Mathf.Min(dMax, dMin);
                bool inX = pl.x >= sb.min.x - 0.05f && pl.x <= sb.max.x + 0.05f;
                bool inY = pl.y >= sb.min.y - 0.05f && pl.y <= sb.max.y + 0.05f;
                bool notInside = pl.z <= sb.min.z + 0.05f || pl.z >= sb.max.z - 0.05f;
                bool onFace = inX && inY && notInside && faceDz <= S7PortFaceTol;
                Detail(id, $"'{ports[i].name}'(portId '{ports[i].portId}', panel {Nm(ports[i].panel)}) local{V(pl)} → 가장 가까운 GEO_S7_Server_{best + 1} {BL(sb)} " +
                           $"{(dMax <= dMin ? "+Z" : "−Z")}면까지 {faceDz:F2}U{(onFace ? "" : " — 앞면 위 아님")}");
                if (!onFace) bad.Add($"'{ports[i].name}' 서버 앞면 위 아님(가장 가까운 Server_{best + 1}, 면까지 {faceDz:F2}U, x 안 {inX}, y 안 {inY})");
            }
            List<int> dup = idx.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key + 1).ToList();
            if (dup.Count > 0) bad.Add($"서버 중복: Server_{string.Join(",", dup)}에 포트 2개 이상");
            if (ports.Count == 6)
                Detail(id, $"포트 순서(OUT_A…IN_3) → 서버 인덱스 [{string.Join(",", idx)}] / 참고 [{string.Join(",", S7PortServerRef)}] [사용자 09-29 나·안C · S7_Builder.cs:126] " +
                           (idx.SequenceEqual(S7PortServerRef) ? "같음" : "다름(정보)"));
            if (bad.Count == 0) Pass(id, $"전력 포트 6개가 서버 6대 앞면(면까지 ≤ {S7PortFaceTol})에 하나씩, 서버 중복 0");
            else Fail(id, string.Join(" / ", bad));
        }

        // ───────────── S7-4 ─────────────

        private IEnumerator S7_4_RoleSlots()
        {
            const string id = "S7-4";
            if (!Ready7(id)) yield break;
            Transform gen = s7.Gen;
            List<RoleSlot> slots = InScene<RoleSlot>(s7.Scene);
            foreach (string nm in new[] { "S7_Slot_Book", "S7_Slot_Computer", "S7_Slot_Power" })
            {
                RoleSlot hit = slots.FirstOrDefault(x => x.name == nm);
                AddName($"S7 역할 사물(RoleSlot) {nm.Substring("S7_Slot_".Length)}", nm, hit != null ? $"{PathUnder(hit.transform, gen)} roleId '{hit.roleId}'" : "(없음)", hit != null);
            }
            Detail(id, $"RoleSlot {slots.Count}개 [{string.Join(", ", slots.Select(x => $"{x.name}/'{x.roleId}' local{V(Lc(gen, x.transform.position))}"))}]");
            RoleSlot book = slots.FirstOrDefault(x => x.roleId == "Book");
            RoleSlot comp = slots.FirstOrDefault(x => x.roleId == "Computer");
            if (book == null) book = slots.FirstOrDefault(x => x.name == "S7_Slot_Book");
            if (comp == null) comp = slots.FirstOrDefault(x => x.name == "S7_Slot_Computer");
            List<string> bad = new List<string>();
            if (slots.Count != 3) bad.Add($"RoleSlot {slots.Count}개(기대 3)");
            if (book == null || comp == null)
            {
                NA(id, "검사불가(이름 불일치): 책/컴퓨터 RoleSlot(roleId 'Book'·'Computer' 또는 이름 S7_Slot_Book·_Computer)을 못 찾음" + (bad.Count > 0 ? " / " + string.Join(" / ", bad) : ""));
                yield break;
            }
            float d = HorizDist(book.transform.position, comp.transform.position);
            Detail(id, $"컴퓨터 '{comp.name}' ↔ 책 '{book.name}' 수평 {d:F2}U(기대 ≥ {S7RoleMinDist} [제안 §3-6]; 초안 값 6.0 [제안 S7_Builder.cs:88-89])");
            if (d < S7RoleMinDist) bad.Add($"컴퓨터↔책 수평 {d:F2}U < {S7RoleMinDist}");
            if (bad.Count == 0) Pass(id, $"RoleSlot 3개, 컴퓨터↔책 수평 {d:F2}U ≥ {S7RoleMinDist}");
            else Fail(id, string.Join(" / ", bad));
        }

        // ───────────── S7-1 ─────────────

        private IEnumerator S7_1_TempOpenings()
        {
            const string id = "S7-1";
            if (!Ready7(id)) yield break;
            Transform gen = s7.Gen;
            List<string> bad = new List<string>();
            if (s7.EntryMarkerCount != 1) bad.Add($"'TEMP_S7_EntryDoor_Open' {s7.EntryMarkerCount}개(기대 1)");
            if (s7.ExitMarkerCount != 1) bad.Add($"'TEMP_S7_ExitDoor_Open' {s7.ExitMarkerCount}개(기대 1)");
            foreach (KeyValuePair<Transform, Vector3> kv in new[] { new KeyValuePair<Transform, Vector3>(s7.EntryMarker, S7EntryMarkerLocal), new KeyValuePair<Transform, Vector3>(s7.ExitMarker, S7ExitMarkerLocal) })
            {
                if (kv.Key == null) continue;
                Vector3 ml = Lc(gen, kv.Key.position);
                Detail(id, $"표지 '{kv.Key.name}' local{V(ml)} (기대 ≈{V(kv.Value)} [제안 S7_Builder.cs:169-170]) {(Vector3.Distance(ml, kv.Value) <= 0.05f ? "일치" : "다름(정보)")}");
            }
            List<doorPhysics> doors = InScene<doorPhysics>(s7.Scene);
            Detail(id, $"S7 doorPhysics {doors.Count}개(TEMP 기간 0 기대 — 판정 17-S7){(doors.Count > 0 ? " [" + string.Join(", ", doors.Select(x => $"{x.name} local{V(Lc(gen, x.transform.position))}")) + "]" : "")}");
            S7OpeningSolids(gen, S7EntryOpenMin, S7EntryOpenMax, "입구", bad);
            S7OpeningSolids(gen, S7ExitOpenMin, S7ExitOpenMax, "출구", bad);

            PlayerMover v = cube;
            yield return S7Drive(v, S7EntryDriveStart, S7EntryReachZ, "입구", bad);
            bool conn = TryGround(gen.TransformPoint(S7ConnectorProbe) + gen.up * 3f, 6f, null, 0f, out Vector3 cg) && Mathf.Abs(Lc(gen, cg).y) <= 0.2f;
            float reach = conn ? S7ExitReachZ : S7ExitReachNoConnector;
            Detail(id, conn ? $"연결 통로 바닥 local{V(Lc(gen, cg))} 확인 → 출구 목표 z ≥ {reach}"
                            : $"연결 통로 바닥 없음(local{V(S7ConnectorProbe)} 아래) → 출구 목표 z ≥ {reach}(몸이 북벽 바깥 면 z110을 다 지남)");
            yield return S7Drive(v, S7ExitDriveStart, reach, "출구", bad);
            if (s7.Park != null && s7.Park.Length > 0) yield return ParkPlayers(s7.Park, v);
            Detail(id, $"기믹 루트 activeSelf: 해석 때 {s7.RootActiveAtResolve} → 주행 뒤 {(s7.GimmickRoot != null && s7.GimmickRoot.gameObject.activeSelf)}" +
                       "(전실 활성기 Lab_ActivateWhenPlayersReady가 도형 진입으로 켬 [판정 13] — 정보)");
            if (bad.Count == 0) Pass(id, "TEMP 표지 각 1개, 입구(x −4~4·높이 6)·출구(x −4~4·높이 4) 개구에 GEO_/DOOR_ 고체 없음, 네모가 두 개구를 걸어 지나감");
            else Fail(id, string.Join(" / ", bad));
        }

        /// <summary>개구 상자(±0.02 안쪽)와 겹치는 고체 — 이름(자신·조상)이 GEO_/DOOR_면 판정, 그 밖은 정보(S2-1과 같은 규칙).</summary>
        private void S7OpeningSolids(Transform gen, Vector3 mn, Vector3 mx, string tag, List<string> bad)
        {
            Vector3 cL = (mn + mx) * 0.5f;
            Vector3 half = (mx - mn) * 0.5f - Vector3.one * 0.02f;
            List<string> geoHits = new List<string>(), otherHits = new List<string>();
            foreach (Collider col in Physics.OverlapBox(gen.TransformPoint(cL), half, gen.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (col.GetComponentInParent<PlayerMover>() != null || col.GetComponentInParent<Projectile>() != null) continue;
                string gn = GeoOrDoorName(col.transform, gen);
                if (gn != null) geoHits.Add($"{gn}({BL(LocalBoxBounds(col, gen))})");
                else otherHits.Add(col.name);
            }
            Detail("S7-1", $"{tag} 개구 local x{mn.x}~{mx.x} y{mn.y}~{mx.y} z{mn.z}~{mx.z}(±0.02 안쪽) 고체: GEO_/DOOR_ {geoHits.Count}개" +
                           (geoHits.Count > 0 ? " [" + string.Join(", ", geoHits) + "]" : "") + (otherHits.Count > 0 ? $" · 기타 고체(정보) [{string.Join(", ", otherHits.Take(6))}]" : ""));
            if (geoHits.Count > 0) bad.Add($"{tag} 개구에 GEO_/DOOR_ 고체: " + string.Join(", ", geoHits));
        }

        /// <summary>S2-1과 같은 주행(ExternallyDriven 속도 대입 수평 4U/s, 수직은 물리) — 섹터 로컬 +z로 reachZ까지.</summary>
        private IEnumerator S7Drive(PlayerMover v, Vector3 startLocal, float reachZ, string tag, List<string> bad)
        {
            Transform gen = s7.Gen;
            Vector3 startW = gen.TransformPoint(startLocal);
            if (!TryGround(startW + gen.up * 3f, 6f, null, 0f, out Vector3 g) || Mathf.Abs(Lc(gen, g).y) > 0.2f)
            {
                bad.Add($"{tag} 주행 시작점 local{V(startLocal)} 아래 바닥 y0 없음");
                yield break;
            }
            yield return WaitIdle(v, 5f);
            Teleport(v, g + gen.up * SpawnLift);
            yield return new WaitForSeconds(0.3f);
            DriveResult dr = new DriveResult();
            yield return DriveShape(v, gen, gen.forward, 4f, 10f, lp => lp.z >= reachZ, dr);
            Detail("S7-1", $"{tag}: 네모 local{V(startLocal)} → +z 4U/s: 끝 local{V(dr.EndLocal)}, {dr.Elapsed:F1}s, 진행 {dr.Progress:F2}U → z ≥ {reachZ} {(dr.Reached ? "도달" : "미도달")}");
            if (!dr.Reached) bad.Add($"{tag} 개구를 지나 z ≥ {reachZ}에 못 감(끝 z{dr.EndLocal.z:F2})");
            yield return WaitIdle(v, 4f);
        }

        // ═════════════════════════ PTF-3: S8 약물 조합 ═════════════════════════

        private IEnumerator ResolveS8()
        {
            if (!director.TryGetSector(8, out SectorController sc) || sc == null) { s8Problem = "섹터 8이 로드되지 않음(섹터 씬 없음)"; yield break; }
            S8Ctx c = new S8Ctx { Sc = sc, Scene = sc.gameObject.scene };
            c.Gen = sc.transform.Find("Generated");
            if (c.Gen == null) c.Gen = sc.transform;
            int geo = CountNamedPrefix(c.Gen, "GEO_S8_");
            int mgr = InScene<ManagerAgent>(c.Scene).Count;
            if (geo == 0 && mgr == 0)
            {
                s8Problem = "S8 미구체화(빈 틀) — Generated 아래 GEO_S8_* 0개·ManagerAgent 0개(S8 빌더·배선 결과 없음 — INT-2 반영 전일 수 있음)";
                yield break;
            }
            c.Group = FindDeep(c.Gen, "S8_Gimmicks");
            AddName("S8 기믹 그룹", "S8_Gimmicks", c.Group != null ? PathUnder(c.Group, c.Gen) : "(없음)" + NearNames(c.Gen, "Gimmick"), c.Group != null);
            // 계약 R3 R2-C3 :393-395 표의 나머지(존재·개수 대조만 — 잡힘·열쇠·조합 흐름은 존재·배선 확인까지 [R3 :397])
            List<RoleSlot> roles = InScene<RoleSlot>(c.Scene);
            AddName("S8 RoleSlot(책·혼합·Monitor)", "3 [CH8_Slot_Book, CH8_Slot_Mixer, CH8_Slot_Monitor]",
                    $"{roles.Count} [{string.Join(", ", roles.Select(r => r.name + "/'" + r.roleId + "'"))}]", roles.Count == 3);
            List<MixingStation> mix = InScene<MixingStation>(c.Scene);
            AddName("S8 조합(계약 표기 'ReagentMixing' — 팀 타입 MixingStation, ReagentMixingSystem/MixingStation.cs:37)", "1",
                    $"{mix.Count} [{string.Join(", ", mix.Select(x => x.name))}]", mix.Count == 1);
            List<RespawnZone> zones = InScene<RespawnZone>(c.Scene);
            AddName("S8 수동 R 체크포인트(RespawnZone) [판정 19]", "S8_Zone_Check ×1",
                    zones.Count == 0 ? "(없음)" : string.Join("|", zones.Select(z => PathUnder(z.transform, c.Gen))), zones.Count == 1 && zones[0].name == "S8_Zone_Check");
            s8 = c;
            report.Notes.Add($"S8 해석: GEO_S8_* {geo}개, ManagerAgent {mgr}, ManagerCctvPanel {InScene<ManagerCctvPanel>(c.Scene).Count}, ManagerKeyDoor {InScene<ManagerKeyDoor>(c.Scene).Count}, " +
                             $"ManagerKeyPoint {InScene<ManagerKeyPoint>(c.Scene).Count}, RoleSlot {roles.Count}, MixingStation {mix.Count}, RespawnZone {zones.Count}, " +
                             $"도형 S8 로컬 [{string.Join(", ", players.Select(p => $"{p.name} {V(Lc(c.Gen, p.transform.position))}"))}]");
            yield break;
        }

        private bool Ready8(string id)
        {
            if (s8 != null) return true;
            report.Get(id).Set(Verdict.NA, s8Problem ?? "S8 해석 실패");
            return false;
        }

        // ───────────── S8-2 ─────────────

        private IEnumerator S8_2_Cctv()
        {
            const string id = "S8-2";
            if (!Ready8(id)) yield break;
            Transform gen = s8.Gen;
            List<string> bad = new List<string>();
            List<Camera> cams = InScene<Camera>(s8.Scene).Where(x => x.name.StartsWith("CCTV_", StringComparison.Ordinal)).OrderBy(x => x.name, StringComparer.Ordinal).ToList();
            for (int k = 1; k <= 3; k++)
            {
                Camera hit = cams.FirstOrDefault(x => x.name == "CCTV_" + k);
                AddName($"S8 CCTV 카메라 {k}(Camera)", "CCTV_" + k, hit != null ? PathUnder(hit.transform, gen) : "(없음)", hit != null);
            }
            Detail(id, $"CCTV_ 카메라 {cams.Count}개 [{string.Join(", ", cams.Select(x => $"{x.name} local{V(Lc(gen, x.transform.position))} 시선{V(gen.InverseTransformDirection(x.transform.forward))} enabled {x.enabled}"))}]");
            if (cams.Count != 3) bad.Add($"CCTV_ 카메라 {cams.Count}개(기대 3)");
            List<ManagerCctvPanel> panels = InScene<ManagerCctvPanel>(s8.Scene);
            if (panels.Count != 1)
            {
                bad.Add($"ManagerCctvPanel {panels.Count}개(기대 1)");
                AddName("S8 CCTV 패널(ManagerCctvPanel)", "CH8_Slot_Monitor", panels.Count == 0 ? "(없음)" : string.Join("|", panels.Select(x => x.name)), false);
            }
            else
            {
                ManagerCctvPanel p = panels[0];
                AddName("S8 CCTV 패널(ManagerCctvPanel)", "CH8_Slot_Monitor", PathUnder(p.transform, gen), p.name == "CH8_Slot_Monitor");
                Camera[] pc = p.cameras ?? new Camera[0];
                Detail(id, $"'{p.name}' cameras {pc.Length}개 [{string.Join(", ", pc.Select(x => x == null ? "(null)" : x.name))}] · slot {Nm(p.slot)}");
                if (pc.Length != 3) bad.Add($"ManagerCctvPanel.cameras {pc.Length}개(기대 3)");
                if (pc.Any(x => x == null)) bad.Add("ManagerCctvPanel.cameras에 빈 칸");
                if (pc.Any(x => x != null && !cams.Contains(x))) bad.Add("ManagerCctvPanel.cameras에 CCTV_ 카메라가 아닌 것");
                if (pc.Where(x => x != null).Distinct().Count() != pc.Count(x => x != null)) bad.Add("ManagerCctvPanel.cameras 중복");
            }
            if (bad.Count == 0) Pass(id, "CCTV_ 카메라 3개 + ManagerCctvPanel 1개(cameras = 그 3대)");
            else Fail(id, string.Join(" / ", bad));
            yield break;
        }

        // ───────────── S8-3 ─────────────

        private IEnumerator S8_3_KeyDoor()
        {
            const string id = "S8-3";
            if (!Ready8(id)) yield break;
            Transform gen = s8.Gen;
            List<string> bad = new List<string>();
            List<ManagerKeyDoor> kds = InScene<ManagerKeyDoor>(s8.Scene);
            List<ManagerKeyPoint> kps = InScene<ManagerKeyPoint>(s8.Scene);
            AddName("S8 열쇠 문(ManagerKeyDoor)", "C8_KEY_DOOR ×1", kds.Count == 0 ? "(없음)" : string.Join("|", kds.Select(x => PathUnder(x.transform, gen))), kds.Count == 1 && kds[0].name == "C8_KEY_DOOR");
            AddName("S8 열쇠 자리(ManagerKeyPoint)", "C8_KEY_POINT ×1", kps.Count == 0 ? "(없음)" : string.Join("|", kps.Select(x => PathUnder(x.transform, gen))), kps.Count == 1 && kps[0].name == "C8_KEY_POINT");
            if (kds.Count != 1) bad.Add($"ManagerKeyDoor {kds.Count}개(기대 1)");
            if (kps.Count != 1) bad.Add($"ManagerKeyPoint {kps.Count}개(기대 1)");

            List<doorPhysics> doors = InScene<doorPhysics>(s8.Scene);
            doorPhysics exitDoor = doors.OrderBy(d => Mathf.Abs(Lc(gen, d.transform.position).z - S8ExitDoorZ)).FirstOrDefault();
            Detail(id, $"S8 doorPhysics {doors.Count}개 [{string.Join(", ", doors.Select(d => $"{d.name} local{V(Lc(gen, d.transform.position))}"))}] → 출구 문 = z {S8ExitDoorZ}에 가장 가까운 '{Nm(exitDoor)}'");
            AddName("S8 출구 문(doorPhysics)", "GEO_S8_Door_Exit", Nm(exitDoor));
            ManagerKeyDoor kd = kds.Count == 1 ? kds[0] : null;
            ManagerKeyPoint kp = kps.Count == 1 ? kps[0] : null;
            if (kd != null)
            {
                Detail(id, $"'{kd.name}' local{V(Lc(gen, kd.transform.position))} · door '{Nm(kd.door)}' · keyPoint '{Nm(kd.keyPoint)}' · manager {Nm(kd.manager)} · useRadius {kd.useRadius} · IsOpen {kd.IsOpen} · onOpened {PersistentSummary(kd.onOpened)}");
                if (kd.door == null) bad.Add("ManagerKeyDoor.door 비어 있음");
                else if (kd.door != exitDoor) bad.Add($"ManagerKeyDoor.door '{kd.door.name}' ≠ 출구 문 '{Nm(exitDoor)}'");
                if (kp != null && kd.keyPoint != kp) Detail(id, $"ManagerKeyDoor.keyPoint '{Nm(kd.keyPoint)}' ≠ ManagerKeyPoint '{kp.name}'(정보)");
            }
            if (kp != null)
            {
                Transform anc = FindInScene(s8.Scene, "ANCH_S8_Key");
                if (anc == null) bad.Add("이름 규약 앵커 'ANCH_S8_Key' 없음 [판정 19]");
                else
                {
                    float d = Vector3.Distance(kp.transform.position, anc.position);
                    Detail(id, $"'{kp.name}' local{V(Lc(gen, kp.transform.position))} ↔ 'ANCH_S8_Key' local{V(Lc(gen, anc.position))} 거리 {d:F3}U(허용 {S8KeyTol}) · HasKey {kp.HasKey} · Revealed {kp.Revealed}");
                    if (d > S8KeyTol) bad.Add($"열쇠 자리가 ANCH_S8_Key에서 {d:F3}U 떨어짐");
                }
            }
            // 시작 뒤 문 닫힘 유지 — 팀 doorPhysics는 눌림(SetPadPressed)·레버가 없으면 시작 자리를 지킨다(doorPhysics.cs:40-58).
            if (kd != null && kd.door != null)
            {
                Transform dt = kd.door.transform;
                Vector3 p0 = dt.position;
                bool open0 = kd.IsOpen;
                yield return new WaitForSeconds(S8DoorObserve);
                if (kd == null || dt == null) { bad.Add("관찰 중 열쇠 문/출구 문이 사라짐"); }
                else
                {
                    float moved = Vector3.Distance(dt.position, p0);
                    Collider dc = FirstSolid(kd.door.gameObject);
                    Detail(id, $"{S8DoorObserve}s 관찰: 출구 문 이동 {moved:F3}U · IsOpen {open0}→{kd.IsOpen} · leverHead {Nm(kd.door.leverHead)} · 문 고체 {(dc != null ? BL(LocalBoxBounds(dc, gen)) : "(콜라이더 없음)")}");
                    if (open0 || kd.IsOpen) bad.Add("ManagerKeyDoor.IsOpen = true(시작 뒤 닫혀 있어야 함)");
                    if (moved > 0.01f) bad.Add($"출구 문이 {moved:F3}U 움직임(닫힘 유지 아님)");
                }
            }
            if (bad.Count == 0) Pass(id, $"ManagerKeyDoor 1(door = 출구 문 '{Nm(exitDoor)}'), ManagerKeyPoint 1(ANCH_S8_Key ±{S8KeyTol}), {S8DoorObserve}s 동안 문 닫힘 유지 — 열쇠 흐름은 존재·배선까지 [R3 :397]");
            else Fail(id, string.Join(" / ", bad));
        }

        // ───────────── S8-4 ─────────────

        private IEnumerator S8_4_ExitLength()
        {
            const string id = "S8-4";
            if (!Ready8(id)) yield break;
            Transform gen = s8.Gen;
            List<string> bad = new List<string>();
            float len = s8.Sc.length;
            Detail(id, $"SectorController.length {len} (기대 {S8Length}±{S8LengthTol} [판정 18])");
            if (Mathf.Abs(len - S8Length) > S8LengthTol) bad.Add($"섹터 길이 {len} ≠ {S8Length}");
            Transform ex = s8.Sc.exit;
            if (ex == null) bad.Add("SectorController.exit(Exit 마커) 없음");
            else
            {
                Vector3 el = Lc(gen, ex.position);
                bool zOk = Mathf.Abs(el.z - S8Length) <= S8LengthTol;
                Detail(id, $"Exit 마커 '{ex.name}' local{V(el)} — z {(zOk ? "일치" : "다름")}(기대 {S8Length}±{S8LengthTol}) · |x| ≤ 4 {(Mathf.Abs(el.x) <= 4f ? "예" : "아니오")} · " +
                           $"y {el.y:F2}(기대 rise 0 + {S8ExitMarkerY} — 정보)" +
                           (Mathf.Abs(el.z - 132f) <= S8LengthTol ? " — 옛 기본 자리 z132: Map4MarkerTools.MoveS8ExitMarker(R3-INT-3) 미실행으로 보임" : ""));
                if (!zOk) bad.Add($"Exit 마커 z {el.z:F2} ≠ {S8Length}");
            }
            Transform endWall = FindInScene(s8.Scene, "GEO_S8_Wall_EscapeEnd");
            Collider ec = endWall != null ? endWall.GetComponent<Collider>() : null;
            Detail(id, ec != null ? $"끝벽 'GEO_S8_Wall_EscapeEnd' 로컬 {BL(LocalBoxBounds(ec, gen))}(안쪽 면 z {S8Length} [제안 S8_Builder.cs:128] — 정보)"
                                  : "끝벽 'GEO_S8_Wall_EscapeEnd' 없음 또는 콜라이더 없음(정보)");
            if (bad.Count == 0) Pass(id, $"섹터 길이 {len} · Exit 마커 z {S8Length}(±{S8LengthTol})");
            else Fail(id, string.Join(" / ", bad));
            yield break;
        }

        // ───────────── S8-1 ─────────────

        private IEnumerator S8_1_ManagerPatrol()
        {
            const string id = "S8-1";
            if (!Ready8(id)) yield break;
            Transform gen = s8.Gen;
            List<ManagerAgent> list = InScene<ManagerAgent>(s8.Scene);
            ManagerAgent ma = PickByName(list, "C8_MANAGER", "S8 관리자(ManagerAgent)", out string prob);
            if (ma == null) { NA(id, NameNA(prob)); yield break; }
            PathChaserAgent ag = ma.agent;
            if (ag == null) { Fail(id, $"'{ma.name}'.agent(PathChaserAgent) 비어 있음 — 팀 컨트롤러가 CH8 시작을 거부한다(ManagerChapterController.cs:110-113)"); yield break; }
            List<string> bad = new List<string>();

            Transform[] wps = ag.waypoints ?? new Transform[0];
            Transform first = wps.Length > 0 ? wps[0] : null, last = wps.Length > 0 ? wps[wps.Length - 1] : null;
            Detail(id, $"waypoints {wps.Length}개 [{string.Join(", ", wps.Select(w => w == null ? "(null)" : $"{w.name} local{V(Lc(gen, w.position))}"))}]");
            AddName("S8 순찰 첫 점(waypoints[0])", "ANCH_S8_WP_1", first != null ? first.name : "(없음)");
            bool closed = wps.Length >= 3 && first != null && last != null && (last == first || Vector3.Distance(last.position, first.position) <= 0.01f);
            if (!closed) bad.Add($"닫힌 순찰 아님: 끝 점 '{Nm(last)}' ≠ 첫 점 '{Nm(first)}'(판정 19 — W가 끝에 WP_1 재삽입, 개수 {wps.Length})");

            if (!ma.gameObject.activeInHierarchy || !ma.enabled)
            {
                Detail(id, $"관리자 비활성(activeInHierarchy {ma.gameObject.activeInHierarchy}, enabled {ma.enabled}) — 이동 관찰 안 함");
                if (bad.Count > 0) Fail(id, string.Join(" / ", bad));
                else NA(id, "검사불가(관리자 비활성): 닫힌 순찰은 통과, 이동은 관찰 못 함");
                yield break;
            }
            ManagerAgent.State st0 = ma.Current;
            Vector3 p0 = ag.transform.position;
            Detail(id, $"시작 상태 {st0} · 감시 {ma.Surveillance} · 에이전트 enabled {ag.enabled} · speed {ag.speed} · patrolSpeed {ma.patrolSpeed} · 위치 local{V(Lc(gen, p0))}");
            if (st0 == ManagerAgent.State.Asleep)
            {
                if (bad.Count > 0) Fail(id, string.Join(" / ", bad));
                else NA(id, "검사불가(관리자 수면 상태 — 새 CH8 재시작 전까지 깨지 않음, ManagerAgent.cs:151-158): 닫힌 순찰은 통과");
                yield break;
            }
            if (st0 == ManagerAgent.State.Inactive)
            {
                float speed0 = ag.speed;
                ManagerAgent maC = ma;
                PathChaserAgent agC = ag;
                s8ManagerRestore = () =>
                {
                    if (maC == null) return;
                    maC.Activate(false);                 // 감시 플래그를 원래 false로(에이전트는 꺼진다 — ManagerAgent.cs:135-149·:197-200)
                    maC.ResetToStart();                  // 준비(Inactive) + 경로 첫 점 + 시작 방향(:171-189)
                    if (agC != null) agC.speed = speed0; // Activate가 patrolSpeed로 덮은 PathChaserAgent.speed(:145) 원래 값
                };
                ma.Activate(true); // [2차판정 19] 팀 공개 API — 검사 코드 안·플레이 중·끝나면 되돌림·저장 안 함
                Detail(id, "관리자가 준비(Inactive) → 팀 공개 API ManagerAgent.Activate(true)로 깨움(팀 ManagerChapterController.RequestStart가 부르는 것과 같은 호출, :186) [2차판정 19]");
            }
            float maxD = 0f, pathLen = 0f;
            Vector3 prev = p0;
            float t0 = Time.time;
            while (Time.time - t0 < S8PatrolObserve)
            {
                yield return new WaitForFixedUpdate();
                if (ma == null || ag == null) break;
                Vector3 p = ag.transform.position;
                pathLen += HorizDist(p, prev);
                prev = p;
                maxD = Mathf.Max(maxD, HorizDist(p, p0));
            }
            string stNow = ma != null ? ma.Current.ToString() : "(없음)";
            Detail(id, $"{S8PatrolObserve}s 관찰: 시작점에서 최대 수평 {maxD:F2}U · 이동 경로 {pathLen:F2}U · 상태 {stNow}" + (ag != null ? $" · 끝 local{V(Lc(gen, ag.transform.position))}" : ""));
            RestoreS8Manager("S8-1");
            if (ma != null && ag != null)
                Detail(id, $"되돌린 뒤: 상태 {ma.Current} · 감시 {ma.Surveillance} · 에이전트 enabled {ag.enabled} · speed {ag.speed} · 시작점과 {Vector3.Distance(ag.transform.position, p0):F3}U");
            if (maxD <= S8PatrolMinMove) bad.Add($"{S8PatrolObserve}s 동안 최대 이동 {maxD:F2}U ≤ {S8PatrolMinMove}(순찰 이동 없음)");
            if (bad.Count == 0) Pass(id, $"닫힌 순찰(waypoints {wps.Length}개, 끝 = '{Nm(first)}'), {S8PatrolObserve}s 동안 최대 {maxD:F2}U 이동(> {S8PatrolMinMove})");
            else Fail(id, string.Join(" / ", bad));
        }

        private void RestoreS8Manager(string where)
        {
            Action a = s8ManagerRestore;
            if (a == null) return;
            s8ManagerRestore = null;
            a();
            report.Notes.Add($"S8 관리자 되돌림({where}): Activate(false) → ResetToStart → PathChaserAgent.speed 원래 값 [2차판정 19 ③]");
        }

        private IEnumerator S8_RestoreManager()
        {
            RestoreS8Manager("S8-정리");
            yield break;
        }

        // ═════════════════════════ PTF-2 공용 도우미 ═════════════════════════

        private static Vector3 Lc(Transform gen, Vector3 w) => gen.InverseTransformPoint(w);

        private static string NameNA(string problem) =>
            (problem != null && problem.StartsWith("이름 불일치", StringComparison.Ordinal) ? "검사불가(이름 불일치): " : "검사불가: ") + (problem ?? "(사유 없음)");

        private static int CountNamedPrefix(Transform root, string prefix) =>
            root == null ? 0 : root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static Transform FindInScene(Scene scene, string name)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform t = FindDeep(root.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        private static bool HasAncestor(Transform t, string name)
        {
            for (Transform p = t; p != null; p = p.parent) if (p.name == name) return true;
            return false;
        }

        /// <summary>stop(Generated) 아래 경로 "그룹/이름". stop 밖이면 루트까지.</summary>
        private static string PathUnder(Transform t, Transform stop)
        {
            if (t == null) return "(없음)";
            List<string> parts = new List<string>();
            for (Transform p = t; p != null && p != stop; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>이름에 key가 든 Transform 이름(최대 6개) — 이름 불일치 사유에 "찾은 것"으로 붙인다.</summary>
        private static string NearNames(Transform root, string key)
        {
            if (root == null) return "";
            List<string> n = root.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(s => s.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0).Distinct().Take(6).ToList();
            return n.Count == 0 ? "" : $" [찾은 것: {string.Join(", ", n)}]";
        }

        /// <summary>콜라이더 자신 또는 stop 아래 조상 중 이름이 GEO_/DOOR_로 시작하는 첫 이름(없으면 null).</summary>
        private static string GeoOrDoorName(Transform t, Transform stop)
        {
            for (Transform p = t; p != null && p != stop; p = p.parent)
                if (p.name.StartsWith("GEO_", StringComparison.Ordinal) || p.name.StartsWith("DOOR_", StringComparison.Ordinal)) return p.name;
            return null;
        }

        /// <summary>계약 이름으로만 고른다(같은 타입이 여럿인 대상용 — 하나뿐이어도 이름이 다르면 쓰지 않는다).</summary>
        private T PickStrict<T>(List<T> list, string expected, string what, out string problem) where T : Component
        {
            problem = null;
            T hit = list.FirstOrDefault(x => x != null && x.name == expected);
            if (hit != null) { AddName(what, expected, hit.name); return hit; }
            string found = string.Join(", ", list.Where(x => x != null).Select(x => x.name).Take(10)) + (list.Count > 10 ? ", …" : "");
            problem = list.Count == 0 ? $"{typeof(T).Name} 0개(기대 '{expected}')" : $"이름 불일치 — {typeof(T).Name} {list.Count}개 중 '{expected}' 없음 [찾은 것: {found}]";
            AddName(what, expected, list.Count == 0 ? "(없음)" : found, false);
            return null;
        }

        /// <summary>콜라이더의 섹터 로컬 AABB — BoxCollider는 8 꼭짓점을 정확히 옮기고, 그 밖은 월드 bounds 꼭짓점을 옮긴다.</summary>
        private static Bounds LocalBoxBounds(Collider c, Transform gen)
        {
            Vector3[] cs = new Vector3[8];
            BoxCollider bc = c as BoxCollider;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sgn = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                if (bc != null) cs[i] = gen.InverseTransformPoint(bc.transform.TransformPoint(bc.center + Vector3.Scale(bc.size * 0.5f, sgn)));
                else cs[i] = gen.InverseTransformPoint(c.bounds.center + Vector3.Scale(c.bounds.extents, sgn));
            }
            Bounds b = new Bounds(cs[0], Vector3.zero);
            for (int i = 1; i < 8; i++) b.Encapsulate(cs[i]);
            return b;
        }

        private static string BL(Bounds b) => $"min{V(b.min)} max{V(b.max)}";

        /// <summary>도형 솔리드 콜라이더 전부의 합 bounds(세모처럼 여러 개인 도형 포함). 없으면 피벗 1U 상자.</summary>
        private static Bounds SolidBounds(PlayerMover m)
        {
            Bounds b = new Bounds(m.transform.position, Vector3.one);
            bool any = false;
            foreach (Collider c in m.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            return b;
        }

        private PlayerMover ShapeOf(PlayerShapeStats.ShapeKind k) =>
            k == PlayerShapeStats.ShapeKind.Cube ? cube : k == PlayerShapeStats.ShapeKind.Tetrahedron ? tetra : sphere;

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>도형·탄·낙석·트리거를 뺀 겹침 고체 하나(없으면 null).</summary>
        private static Collider SolidOverlap(Vector3 center, Vector3 half, Quaternion rot)
        {
            foreach (Collider c in Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null) continue;
                if (c.GetComponentInParent<PlayerMover>() != null || c.GetComponentInParent<Projectile>() != null || c.GetComponentInParent<FallingRock>() != null) continue;
                return c;
            }
            return null;
        }

        /// <summary>origin에서 dir로 첫 고체까지 거리(도형·탄·낙석·트리거·ignore 계층 제외). 없으면 maxDist.</summary>
        private static float FirstSolidDistance(Vector3 origin, Vector3 dir, float maxDist, Transform ignore)
        {
            if (dir.sqrMagnitude < 1e-8f) return maxDist;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir.normalized, maxDist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.collider.GetComponentInParent<PlayerMover>() != null || h.collider.GetComponentInParent<Projectile>() != null || h.collider.GetComponentInParent<FallingRock>() != null) continue;
                return h.distance;
            }
            return maxDist;
        }

        /// <summary>팀 LaserBeam.Tick과 같은 규칙으로 광선 끝을 구한다(LaserBeam.cs:78-88 — RaycastAll(wallMask, 트리거 무시)에서
        /// playerTag가 아닌 첫 충돌). 공개 필드만 읽는다(관찰 전용). 없으면 range, hitName = null.</summary>
        private static float TeamBeamEnd(LaserBeam beam, Vector3 origin, Vector3 dir, out string hitName)
        {
            hitName = null;
            if (beam == null) return 0f;
            Vector3 d = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
            RaycastHit[] hits = Physics.RaycastAll(origin, d, beam.range, beam.wallMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.CompareTag(beam.playerTag)) continue;
                hitName = h.collider.name;
                return h.distance;
            }
            return beam.range;
        }

        /// <summary>M2R1: 고정 레이저 광선 위 공중 hover 자리 — 발사점에서 수평 1.5U 이상, 광선 거리 ≤ maxS(= 끝점 − 0.5),
        /// 0.45U 반폭 상자가 고체와 겹치지 않는 표본(0.25U 간격) 중 낙석 레인까지 수평 여유(2.5U에서 포화)가 큰 곳, 같으면 끝점 절반에 가까운 곳 [구현 결정].</summary>
        private bool TryBeamHoverSpot(Vector3 origin, Vector3 dir, float maxS, Transform gen, out Vector3 place, out string how)
        {
            place = default;
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            List<Vector3> lanes = new List<Vector3>();
            if (s4 != null)
                foreach (FallingRockSpawner rs in s4.Rocks())
                    if (rs != null && rs.spawnPositions != null)
                        foreach (Transform t in rs.spawnPositions) if (t != null) lanes.Add(t.position);
            float target = Mathf.Max(1.5f, (maxS + 0.5f) * 0.5f);
            float bestScore = -1f, bestOff = float.MaxValue, bestS = -1f, bestClear = 0f;
            int overlapped = 0;
            for (float s = 1.5f; s <= maxS + 1e-4f; s += 0.25f)
            {
                Vector3 p = origin + d * s;
                if (HorizDist(p, origin) < 1.5f) continue;
                if (SolidOverlap(p, Vector3.one * 0.45f, gen.rotation) != null) { overlapped++; continue; }
                float clear = lanes.Count > 0 ? lanes.Min(l => HorizDist(l, p)) : 99f;
                float score = Mathf.Round(Mathf.Min(clear, 2.5f) * 100f) / 100f;
                float off = Mathf.Abs(s - target);
                if (score > bestScore || (Mathf.Approximately(score, bestScore) && off < bestOff))
                {
                    bestScore = score; bestOff = off; bestS = s; bestClear = clear; place = p;
                }
            }
            if (bestS < 0f) { how = $"광선 위 공중 hover 자리 없음(1.5~{maxS:F2}U, 고체 겹침 표본 {overlapped})"; return false; }
            how = $"광선 위 공중 hover {bestS:F2}U(끝점 절반 {target:F2}U 기준, 낙석 레인 수평 여유 {(lanes.Count > 0 ? bestClear.ToString("F2", CultureInfo.InvariantCulture) + "U" : "레인 없음")})";
            return true;
        }

        /// <summary>TryGround + 면 법선. 도형·탄·낙석(파편 포함)·트리거·ignore 계층은 건너뛴다.</summary>
        private static bool TryGround2(Vector3 from, float maxDist, Transform ignore, out Vector3 point, out Vector3 normal)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, maxDist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.collider.GetComponentInParent<PlayerMover>() != null || h.collider.GetComponentInParent<Projectile>() != null) continue;
                if (h.collider.GetComponentInParent<FallingRock>() != null || h.collider.GetComponentInParent<FallingRockShard>() != null) continue;
                point = h.point;
                normal = h.normal;
                return true;
            }
            point = default;
            normal = Vector3.up;
            return false;
        }

        /// <summary>TryBeamPlacement와 같은 규칙에 바닥 두께 상한만 인자로 뺐다(0 = 상한 없음). S5가 쓰는 원본은 그대로 둔다.
        /// M2R1: maxS(≤ 0이면 range) — 탐색 상한. placeS = 고른 자리의 광선 위 거리(구간 가운데 표본).</summary>
        private bool TryBeamPlacementEx(Vector3 origin, Vector3 dir, float range, Transform ignore, float maxThickness, float maxS, out Vector3 place, out string how, out float placeS)
        {
            place = default;
            placeS = -1f;
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            List<Vector3> run = new List<Vector3>();
            float runStart = 0f;
            float lim = maxS > 0f ? Mathf.Min(range, maxS) : range;
            for (float s = 0.5f; s <= lim; s += 0.25f)
            {
                Vector3 p = origin + d * s;
                Vector3 g = Vector3.zero;
                bool ok = HorizDist(p, origin) >= 1.5f && TryGround(p + Vector3.up * 0.3f, 1.6f, ignore, maxThickness, out g);
                ok = ok && p.y - g.y >= 0.1f && p.y - g.y <= 1.2f;
                if (ok)
                {
                    if (run.Count == 0) runStart = s;
                    run.Add(g);
                    continue;
                }
                if (run.Count >= 5) break;
                run.Clear();
            }
            if (run.Count < 5) { how = $"{(lim < range ? $"탐색 상한 {lim:F2}U(사거리 {range:F1}U)" : $"사거리 {range:F1}U")} 안에 광선 0.1~1.2U 아래 바닥 구간 없음(두께 상한 {(maxThickness > 0f ? maxThickness.ToString("F0", CultureInfo.InvariantCulture) : "없음")})"; return false; }
            place = run[run.Count / 2] + Vector3.up * 0.55f;
            placeS = runStart + (run.Count / 2) * 0.25f;
            how = $"광선 아래 바닥(두께 상한 {(maxThickness > 0f ? maxThickness.ToString("F0", CultureInfo.InvariantCulture) : "없음")}) {runStart:F1}~{runStart + (run.Count - 1) * 0.25f:F1}U 구간 가운데";
            return true;
        }

        /// <summary>후보(섹터 로컬, y = 기대 바닥 윗면)마다 위에서 레이를 쏴 실제 바닥이 기대 높이 ±0.3이고 1.2U 상자가 빈 곳만 고른다(최대 3곳, 서로 2.5U 이상).</summary>
        private Vector3[] PickParkSpots(Transform gen, Vector3[] candLocal, string tag)
        {
            List<Vector3> spots = new List<Vector3>();
            List<string> skipped = new List<string>();
            foreach (Vector3 l in candLocal)
            {
                Vector3 top = gen.TransformPoint(new Vector3(l.x, l.y + 4f, l.z));
                if (!TryGround2(top, 8f, null, out Vector3 g, out _)) { skipped.Add($"{V(l)} 바닥 없음"); continue; }
                float gy = Lc(gen, g).y;
                if (Mathf.Abs(gy - l.y) > 0.3f) { skipped.Add($"{V(l)} 첫 면 y{gy:F2}"); continue; }
                Vector3 c = g + gen.up * (SpawnLift + 0.05f);
                Collider blocker = SolidOverlap(c, new Vector3(0.6f, 0.5f, 0.6f), gen.rotation);
                if (blocker != null) { skipped.Add($"{V(l)} '{blocker.name}'와 겹침"); continue; }
                if (spots.Any(s => HorizDist(s, g) < 2.5f)) continue;
                spots.Add(g + gen.up * SpawnLift);
                if (spots.Count == 3) break;
            }
            report.Notes.Add($"{tag}: 파킹점 {spots.Count}/3 [{string.Join(", ", spots.Select(s => V(Lc(gen, s))))}]" + (skipped.Count > 0 ? " — 뺀 후보: " + string.Join(", ", skipped) : ""));
            return spots.ToArray();
        }

        /// <summary>도형을 ExternallyDriven으로 잠깐 잡고 매 물리 스텝 수평 속도를 dirW·speed로 대입해 민다(수직 속도는 물리 그대로 —
        /// 둔덕을 타고 넘고, 가장자리에서는 떨어진다). done(로컬 위치)이 참이거나 timeout이면 멈추고 플래그를 되돌린다.</summary>
        private IEnumerator DriveShape(PlayerMover m, Transform gen, Vector3 dirW, float speed, float timeout, Func<Vector3, bool> done, DriveResult res, Action stallProbe = null)
        {
            yield return WaitIdle(m, 5f);
            Rigidbody rb = m.GetComponent<Rigidbody>();
            Vector3 d = new Vector3(dirW.x, 0f, dirW.z);
            d = d.sqrMagnitude > 1e-6f ? d.normalized : gen.forward;
            Vector3 start = m.transform.position;
            bool prev = m.ExternallyDriven;
            float t0 = Time.time;
            List<float> sampleT = new List<float>(), sampleP = new List<float>(); // [10-04 L2] 속도 표본(시각·진행량)
            float bestProg = float.NegativeInfinity, bestAt = t0; bool stallFired = false; // [10-04 T5 진단] 최대 진행이 1.5초 정체되면 stallProbe를 한 번 부른다(관찰 전용)
            try
            {
                m.ExternallyDriven = true;
                drivenByUs.Add(m);
                while (Time.time - t0 < timeout)
                {
                    if (rb != null && !rb.isKinematic) rb.velocity = d * speed + Vector3.up * rb.velocity.y;
                    yield return new WaitForFixedUpdate();
                    res.Progress = Vector3.Dot(m.transform.position - start, d);
                    sampleT.Add(Time.time); sampleP.Add(res.Progress);
                    if (res.Progress > bestProg + 0.05f) { bestProg = res.Progress; bestAt = Time.time; }
                    else if (stallProbe != null && !stallFired && Time.time - bestAt >= 1.5f && Time.time - t0 >= 0.5f) { stallFired = true; stallProbe(); }
                    if (Time.time - t0 >= 0.3f)
                    {
                        int k = sampleT.Count - 1;
                        while (k > 0 && sampleT[sampleT.Count - 1] - sampleT[k] < 0.1f) k--;
                        float dt = sampleT[sampleT.Count - 1] - sampleT[k];
                        if (dt >= 0.1f)
                        {
                            float sp = (sampleP[sampleP.Count - 1] - sampleP[k]) / dt;
                            if (float.IsNaN(res.MinWindowSpeed) || sp < res.MinWindowSpeed) { res.MinWindowSpeed = sp; res.MinWindowAtLocal = Lc(gen, m.transform.position); }
                        }
                    }
                    if (done(Lc(gen, m.transform.position))) { res.Reached = true; break; }
                }
            }
            finally
            {
                res.Elapsed = Time.time - t0;
                if (m != null)
                {
                    m.ExternallyDriven = prev;
                    drivenByUs.Remove(m);
                    if (rb != null && !rb.isKinematic) rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
                    res.EndLocal = Lc(gen, m.transform.position);
                }
            }
        }

        /// <summary>도형을 pos에 붙잡아 둔다(ExternallyDriven + 매 스텝 위치·속도 0). release()가 참이 되는 스텝에 플래그를 되돌리고 끝낸다.
        /// 팀 RespawnController는 붙잡힌 몸(IsHeld)의 복귀를 거절하므로, 위험 요소가 맞히기 전에 반드시 풀려야 한다.</summary>
        private IEnumerator HoverShape(PlayerMover m, Vector3 pos, Func<bool> release, float timeout, Action<bool> released)
        {
            yield return WaitIdle(m, 5f);
            Rigidbody rb = m.GetComponent<Rigidbody>();
            bool prev = m.ExternallyDriven;
            bool ok = false;
            try
            {
                m.ExternallyDriven = true;
                drivenByUs.Add(m);
                Teleport(m, pos);
                float t0 = Time.time;
                while (Time.time - t0 < timeout)
                {
                    yield return new WaitForFixedUpdate();
                    if (release()) { ok = true; break; }
                    if (rb != null)
                    {
                        if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                        rb.position = pos;
                    }
                    m.transform.position = pos;
                }
            }
            finally
            {
                if (m != null) { m.ExternallyDriven = prev; drivenByUs.Remove(m); }
                released(ok);
            }
        }

        /// <summary>복귀 뒤 위치가 목적지 안전점(주·보조)에서 수평 0.9·수직 1.6 안인지, 발밑 지지면이 그 점의 로컬 높이인지.</summary>
        private void VerifyDest(PlayerMover v, SectionSafePoint dest, Transform gen, DestTrial r)
        {
            List<Vector3> pts = SafePoints(dest);
            r.AtDest = NearAny(v.transform.position, pts, 0.9f, 1.6f, out _);
            NearestSafePoint(dest, v.transform.position, out string nm, out float dd);
            Vector3 np = pts.OrderBy(p => HorizDist(p, v.transform.position)).First();
            r.SupportOk = CheckSupport(v, gen, Lc(gen, np).y, out r.Support);
            r.Msg += $" → 복귀 local{V(Lc(gen, v.transform.position))}, '{dest.name}'(주·보조 중 가장 가까운 '{nm}')까지 수평 {dd:F2}U{(r.AtDest ? "" : " — 목적지 아님")}; 발밑: {r.Support}";
        }

        // ───────────── 공용 도우미 ─────────────

        private void Pass(string id, string reason) => report.Get(id).Set(Verdict.Pass, reason);
        private void Fail(string id, string reason) => report.Get(id).Set(Verdict.Fail, reason);
        private void NA(string id, string reason) => report.Get(id).Set(Verdict.NA, reason);
        private void Detail(string id, string line) => report.Get(id).Details.Add(line);

        private void AddName(string what, string expected, string actual) => AddName(what, expected, actual, actual == expected);

        private void AddName(string what, string expected, string actual, bool ok) =>
            report.NameChecks.Add(new NameCheck { What = what, Expected = expected, Actual = actual, Ok = ok });

        /// <summary>같은 타입이 하나면 그것(이름은 대조만), 여럿이면 기대 이름으로 고른다 — 못 고르면 null + '이름 불일치'.</summary>
        private T PickByName<T>(List<T> list, string expected, string what, out string problem) where T : Component
        {
            problem = null;
            if (list.Count == 0) { problem = $"{typeof(T).Name} 0개"; AddName(what, expected, "(없음)", false); return null; }
            if (list.Count == 1) { AddName(what, expected, list[0].name); return list[0]; }
            T hit = list.FirstOrDefault(x => x.name == expected);
            if (hit != null) { AddName(what, expected, hit.name); return hit; }
            problem = $"이름 불일치 — {typeof(T).Name} {list.Count}개 중 '{expected}' 없음 [{string.Join(", ", list.Select(x => x.name))}]";
            AddName(what, expected, string.Join("|", list.Select(x => x.name)), false);
            return null;
        }

        private static List<T> InScene<T>(Scene scene) where T : Component
        {
            List<T> list = new List<T>();
            if (!scene.IsValid() || !scene.isLoaded) return list;
            foreach (GameObject root in scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        private static IEnumerable<T> SortByLocal<T>(List<T> list, Transform gen) where T : Component =>
            list.OrderBy(x => gen.InverseTransformPoint(x.transform.position).z).ThenBy(x => gen.InverseTransformPoint(x.transform.position).x);

        private Vector3 L1(Vector3 w) => s1 != null ? s1.Gen.InverseTransformPoint(w) : w;

        private static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
        private static string Nm(Object o) => o == null ? "(없음)" : o.name;
        private static string Parent(Transform t) => t.parent != null ? t.parent.name : "(루트)";
        private static float HorizDist(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private static PlayerMover Owner(GameObject go) => go != null ? go.GetComponentInParent<PlayerMover>() : null;

        private static Collider FirstTrigger(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) if (c.isTrigger) return c;
            return null;
        }

        private static Collider FirstSolid(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) if (!c.isTrigger) return c;
            return null;
        }

        /// <summary>아래로 레이를 쏴 가장 가까운 "바닥"을 찾는다. 트리거·도형·탄·ignore 계층은 건너뛰고,
        /// maxThickness>0이면 높이가 그보다 큰 콜라이더(기둥 등)도 건너뛴다.</summary>
        private static bool TryGround(Vector3 from, float maxDist, Transform ignore, float maxThickness, out Vector3 point)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, maxDist, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                Rigidbody arb = h.collider.attachedRigidbody;
                if (arb != null && arb.GetComponent<PlayerMover>() != null) continue;
                if (h.collider.GetComponentInParent<Projectile>() != null) continue;
                if (maxThickness > 0f && h.collider.bounds.size.y > maxThickness) continue;
                point = h.point;
                return true;
            }
            point = default;
            return false;
        }

        /// <summary>R2: 복귀 뒤 "발밑 지지면" 판정. 도형 솔리드 콜라이더 중심에서 아래로 레이를 쏴(자기 계층·다른 도형·탄·트리거 제외)
        /// 첫 면을 지지면으로 본다. 합격 = ① 그 면의 섹터 로컬 y가 expectLocalY±FloorTolY ② 도형 밑면(bounds.min.y)과 그 면 사이
        /// −0.1~+FootGapMax. 첫 면이 기대 바닥이 아니면(예: 천장 지붕 윗면) 그 아래 기대 바닥까지 사이에 낀 면을 이름과 함께 적는다
        /// — 즉 "도형과 바닥 사이에 솔리드 면이 없음"도 ①로 함께 확인된다. 피벗 오프셋(세모 −0.21 등)에 영향받지 않게 bounds를 쓴다.</summary>
        private static bool CheckSupport(PlayerMover m, Transform gen, float expectLocalY, out string text)
        {
            Collider col = m != null ? FirstSolid(m.gameObject) : null;
            if (col == null) { text = "도형 솔리드 콜라이더 없음 — 발밑 판정 불가"; return false; }
            Bounds b = col.bounds;
            RaycastHit[] hits = Physics.RaycastAll(b.center, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, c) => a.distance.CompareTo(c.distance));
            List<RaycastHit> solid = new List<RaycastHit>();
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(m.transform)) continue;
                if (h.collider.GetComponentInParent<PlayerMover>() != null) continue;
                if (h.collider.GetComponentInParent<Projectile>() != null) continue;
                solid.Add(h);
            }
            float footLocal = gen.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z)).y;
            if (solid.Count == 0) { text = $"발밑 60U 안에 지지면 없음(도형 밑면 local y{footLocal:F2})"; return false; }
            RaycastHit first = solid[0];
            float supLocal = gen.InverseTransformPoint(first.point).y;
            float gap = b.min.y - first.point.y;
            bool floorOk = Mathf.Abs(supLocal - expectLocalY) <= FloorTolY;
            bool gapOk = gap >= -0.1f && gap <= FootGapMax;
            text = $"발밑 첫 지지면 '{first.collider.name}' 윗면 local y{supLocal:F2}(기대 {expectLocalY:F1}±{FloorTolY}) · 도형 밑면 local y{footLocal:F2} · 밑면-지지면 {gap:F2}U(허용 −0.1~{FootGapMax})";
            if (!floorOk)
            {
                int k = solid.FindIndex(h => Mathf.Abs(gen.InverseTransformPoint(h.point).y - expectLocalY) <= FloorTolY);
                if (k > 0)
                {
                    IEnumerable<string> between = solid.Take(k).Select(h => $"'{h.collider.name}'(local y{gen.InverseTransformPoint(h.point).y:F2})");
                    text += $" — 그 아래 기대 바닥 '{solid[k].collider.name}'(local y{gen.InverseTransformPoint(solid[k].point).y:F2})까지 사이에 솔리드 면 {string.Join(", ", between)}: 도형이 바닥이 아닌 면 위(플레이 공간 밖 의심)";
                }
                else text += " — 발밑 60U 안에 기대 바닥 높이의 면 없음";
            }
            return floorOk && gapOk;
        }

        private static Vector3 GroundedSpot(Vector3 from, float maxDown, Transform ignore, Vector3 fallback)
        {
            if (TryGround(from, maxDown, ignore, 0f, out Vector3 g)) return g + Vector3.up * SpawnLift;
            return fallback + Vector3.up * SpawnLift;
        }

        private static List<Vector3> SafePoints(SectionSafePoint sp)
        {
            List<Vector3> pts = new List<Vector3> { sp.transform.position };
            if (sp.backupPoints != null) foreach (Transform b in sp.backupPoints) if (b != null) pts.Add(b.position);
            return pts;
        }

        /// <summary>R1: 안전점(주 + backupPoints) 중 수평으로 가장 가까운 점의 이름과 거리.</summary>
        private static void NearestSafePoint(SectionSafePoint sp, Vector3 pos, out string name, out float best)
        {
            name = sp.name + "(주)";
            best = HorizDist(pos, sp.transform.position);
            if (sp.backupPoints == null) return;
            foreach (Transform b in sp.backupPoints)
            {
                if (b == null) continue;
                float d = HorizDist(pos, b.position);
                if (d < best) { best = d; name = b.name + "(보조)"; }
            }
        }

        private static bool NearAny(Vector3 pos, List<Vector3> pts, float horiz, float vert, out float best)
        {
            best = float.MaxValue;
            bool ok = false;
            foreach (Vector3 p in pts)
            {
                float d = HorizDist(pos, p);
                best = Mathf.Min(best, d);
                if (d <= horiz && Mathf.Abs(pos.y - p.y) <= vert) ok = true;
            }
            return ok;
        }

        private static string PersistentSummary(UnityEventBase ev)
        {
            if (ev == null) return "(이벤트 null)";
            int n = ev.GetPersistentEventCount();
            if (n == 0) return "영구 배선 0개";
            List<string> parts = new List<string>();
            for (int i = 0; i < n; i++)
            {
                Object target = ev.GetPersistentTarget(i);
                parts.Add($"{(target == null ? "(null)" : target.GetType().Name + "'" + target.name + "'")}.{ev.GetPersistentMethodName(i)}");
            }
            return $"영구 배선 {n}개 [{string.Join(", ", parts)}]";
        }

        // ───────────── [10-04 T5 진단] 도형·TeamExitZone·챕터 상태 증거 — 읽기만 한다(공개 필드·Unity 물리 질의), 판정 로직은 바꾸지 않는다 ─────────────

        private static string PathOf(Transform t)
        {
            string chain = "";
            for (Transform q = t != null ? t.parent : null; q != null; q = q.parent) chain += (chain.Length > 0 ? " < " : "") + q.name;
            return chain.Length == 0 ? "(없음 — 씬 루트)" : chain;
        }

        /// <summary>도형 한 개가 "왜 안 움직이나"를 가르는 증거를 한 줄로: PlayerMover 플래그(ExternallyDriven·InputLocked·IsControlled·IsGripped·IsBusy·검사가 모는 중인지),
        /// Rigidbody(isKinematic·constraints·속도·잠듦·충돌 검출), 부모 사슬(카트·버킷 등에 붙었는지), 붙은 Joint, 자기 콜라이더 높이, 발밑 광선 첫 콜라이더,
        /// +Z 1.5U 박스캐스트 첫 콜라이더(막는 것), 몸 상자(+0.05 여유) OverlapBox 겹침 상위 8(트리거 포함).</summary>
        private string DiagShape(PlayerMover m)
        {
            if (m == null) return "(도형 null)";
            Vector3 pos = m.transform.position;
            Rigidbody rb = m.GetComponent<Rigidbody>();
            StringBuilder sb = new StringBuilder();
            sb.Append($"'{m.name}' local{V(L1(pos))}, 레이어 {LayerMask.LayerToName(m.gameObject.layer)}, 부모 {PathOf(m.transform)}");
            sb.Append($" | ExternallyDriven={m.ExternallyDriven} InputLocked={m.InputLocked} IsControlled={m.IsControlled} IsGripped={InteractionController.IsGripped(m)} IsBusy={IsBusy(m)} 검사가 모는 중={drivenByUs.Contains(m)}");
            if (rb != null)
                sb.Append($" | rb: isKinematic={rb.isKinematic} useGravity={rb.useGravity} constraints={rb.constraints} velocity{V(rb.velocity)} 각속도{V(rb.angularVelocity)} 잠듦={rb.IsSleeping()} detectCollisions={rb.detectCollisions} 충돌검출={rb.collisionDetectionMode} 보간={rb.interpolation} 질량={rb.mass:F2} (rb.position−transform {Vector3.Distance(rb.position, pos):F3})");
            else sb.Append(" | Rigidbody 없음");

            List<string> joints = new List<string>();
            foreach (Joint j in m.GetComponentsInChildren<Joint>(true))
                joints.Add($"{j.GetType().Name}@{j.gameObject.name}→{(j.connectedBody != null ? j.connectedBody.name : "(월드)")}");
            foreach (Joint j in Object.FindObjectsOfType<Joint>())
                if (rb != null && j.connectedBody == rb && !j.transform.IsChildOf(m.transform))
                    joints.Add($"{j.GetType().Name}@{j.gameObject.name}→(이 몸에 연결됨)");
            sb.Append($" | Joint {(joints.Count == 0 ? "없음" : string.Join(", ", joints))}");

            List<string> own = new List<string>();
            foreach (Collider c in m.GetComponentsInChildren<Collider>(true))
            {
                Bounds cb = c.bounds;
                own.Add($"{c.name}:{c.GetType().Name}{(c.isTrigger ? "/트리거" : "")}{(c.enabled ? "" : "/꺼짐")} y{L1(cb.min).y:F2}~{L1(cb.max).y:F2}");
            }
            sb.Append($" | 자기 콜라이더 [{string.Join("; ", own)}]");

            Bounds sbnd = SolidBounds(m);
            string ground = "광선 3U 안 지지면 없음";
            RaycastHit[] gh = Physics.RaycastAll(sbnd.center, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(gh, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in gh)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(m.transform)) continue;
                ground = $"'{h.collider.name}' 윗면 local y{L1(h.point).y:F3} (도형 밑면 local y{L1(new Vector3(sbnd.center.x, sbnd.min.y, sbnd.center.z)).y:F3}, 밑면−지지면 {sbnd.min.y - h.point.y:F3})";
                break;
            }
            sb.Append($" | 발밑 {ground}");

            Vector3 fwd = s1 != null ? s1.Gen.forward : Vector3.forward;
            string front = "+Z 1.5U 안 막는 것 없음";
            RaycastHit[] fh = Physics.BoxCastAll(sbnd.center, sbnd.extents * 0.95f, fwd, Quaternion.identity, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(fh, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in fh)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(m.transform)) continue;
                front = $"'{h.collider.name}'(거리 {h.distance:F3}, 법선{V(h.normal)})";
                break;
            }
            sb.Append($" | 앞(+Z) 첫 콜라이더 {front}");

            Collider[] ov = Physics.OverlapBox(sbnd.center, sbnd.extents + Vector3.one * 0.05f, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            List<string> ovl = ov.Where(c => c != null && !c.transform.IsChildOf(m.transform))
                .OrderBy(c => Vector3.Distance(c.bounds.center, sbnd.center)).Take(8)
                .Select(c => $"{c.name}({c.GetType().Name}{(c.isTrigger ? "/트리거" : "/솔리드")}, 레이어 {LayerMask.LayerToName(c.gameObject.layer)})").ToList();
            sb.Append($" | 몸 상자(+0.05) 겹침 상위 8 [{(ovl.Count == 0 ? "없음" : string.Join("; ", ovl))}]");
            return sb.ToString();
        }

        /// <summary>TeamExitZone의 공개 판정(AllPlayersInside)과, 팀 조건(TeamExitZone.cs:36-50)대로 도형마다 "안(센다)/밖(안 센다)"와 그 이유.
        /// 팀 조건: PlayerMover가 하나도 없으면 false(:39), 하나라도 구역 밖이면 false(:41-42), 안 판정 = zoneCollider.ClosestPoint(mover.transform.position) == position(:46-50) —
        /// 도형의 transform.position 한 점으로 판정한다(콜라이더가 아니라 피벗이라 세모처럼 피벗이 몸 아래쪽이면 구역 아래 경계에 걸릴 수 있다).</summary>
        private string DiagExitZone(TeamExitZone zone)
        {
            if (zone == null) return "TeamExitZone 없음";
            Collider zc = zone.GetComponent<Collider>();
            PlayerMover[] movers = Object.FindObjectsOfType<PlayerMover>(); // 팀 AllPlayersInside와 같은 집합(TeamExitZone.cs:38, 비활성 오브젝트 제외)
            StringBuilder sb = new StringBuilder();
            sb.Append($"AllPlayersInside()={zone.AllPlayersInside()} · 구역 오브젝트 활성={zone.gameObject.activeInHierarchy}, collider {(zc == null ? "없음" : $"enabled={zc.enabled} isTrigger={zc.isTrigger}")}, 씬의 PlayerMover {movers.Length}개 " +
                      "· 팀 조건: 0개면 false(TeamExitZone.cs:39), 하나라도 구역 밖이면 false(:41-42), 안 = ClosestPoint(transform.position)==position(:46-50)");
            if (zc != null)
            {
                Bounds zb = zc.bounds;
                Vector3 lo = L1(zb.min), hi = L1(zb.max);
                sb.Append($" · 구역 local x{lo.x:F1}~{hi.x:F1} y{lo.y:F2}~{hi.y:F2} z{lo.z:F1}~{hi.z:F1}");
                foreach (PlayerMover mv in movers)
                {
                    Vector3 pos = mv.transform.position;
                    Vector3 cp = zc.ClosestPoint(pos);
                    bool inside = cp == pos;
                    Vector3 pl = L1(pos);
                    string why = inside ? "" : $" — 밖: 구역 로컬 초과량 x{Mathf.Max(lo.x - pl.x, 0f, pl.x - hi.x):F2} y{Mathf.Max(lo.y - pl.y, 0f, pl.y - hi.y):F2} z{Mathf.Max(lo.z - pl.z, 0f, pl.z - hi.z):F2}, ClosestPoint 거리 {Vector3.Distance(cp, pos):F3}";
                    bool known = mv == sphere || mv == cube || mv == tetra;
                    sb.Append($" || '{mv.name}' transform local{V(pl)} → {(inside ? "안(센다)" : "밖(안 센다)")}{why}{(known ? "" : " [검사 도형 아님 — 씬의 다른 PlayerMover]")}");
                }
            }
            return sb.ToString();
        }

        /// <summary>챕터 종료 상태: 추격 컨트롤러·추격자 활성/위치, 출구 문 높이(local y), 카트 도착 수, AllPlayersInside — 종료 신호가 일찍 발화했는지 가르는 증거.</summary>
        private string ChapterProbe()
        {
            if (s1 == null || s1.Chaser == null) return "S1 컨텍스트 없음";
            PathChaserController ch = s1.Chaser;
            int arrived = ch.carts == null || ch.arrivalPoint == null ? -1
                : ch.carts.Count(c => c != null && Vector3.Distance(c.transform.position, ch.arrivalPoint.position) <= ch.arrivalRadius);
            Vector3 ap = ch.agent != null ? L1(ch.agent.transform.position) : Vector3.zero;
            string doorY = s1.Door != null ? L1(s1.Door.transform.position).y.ToString("F2") : "(없음)";
            string inside = s1.ExitZone != null ? s1.ExitZone.AllPlayersInside().ToString() : "(구역 없음)";
            return $"추격 컨트롤러 enabled={ch.enabled} 활성={ch.gameObject.activeInHierarchy}, 추격자 활성={(ch.agent != null && ch.agent.gameObject.activeInHierarchy)} local{V(ap)}, " +
                   $"출구 문 local y{doorY}, 카트 도착 {arrived}/{(ch.carts == null ? 0 : ch.carts.Length)}, AllPlayersInside={inside}";
        }

        /// <summary>항목의 하위 결과를 "이름 통과/실패/검사불가 · …"로 한 줄 요약(없으면 "하위 결과 없음") — S1-5가 S1-4 다리 하위 결과를 가리킬 때.</summary>
        private string SubSummary(string id)
        {
            CheckItem it = report.Get(id);
            if (it == null || it.Subs.Count == 0) return "하위 결과 없음";
            return string.Join(" · ", it.Subs.Select(s => $"{s.Name} {SubLabel(s.Status)}"));
        }

        private bool IsBusy(PlayerMover m)
        {
            if (m == null) return false;
            Rigidbody rb = m.GetComponent<Rigidbody>();
            return m.ExternallyDriven || (rb != null && rb.isKinematic);
        }

        /// <summary>복귀 연출(키네마틱·ExternallyDriven) 중인 도형을 옮기면 RespawnController와 다툰다 — 끝날 때까지 기다린다.</summary>
        private IEnumerator WaitIdle(PlayerMover m, float timeout)
        {
            float t = Time.time;
            while (m != null && IsBusy(m) && Time.time - t < timeout) yield return null;
        }

        private static void Teleport(PlayerMover m, Vector3 pos)
        {
            if (m == null) return;
            Rigidbody rb = m.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.position = pos;
            }
            m.transform.position = pos;
        }

        private IEnumerator ParkPlayers(Vector3[] spots, params PlayerMover[] who)
        {
            if (spots == null || spots.Length == 0) yield break;
            foreach (PlayerMover p in who)
            {
                if (p == null) continue;
                int idx = p == sphere ? 0 : p == cube ? 1 : 2; // 도형마다 고정 자리(서로 겹치지 않게)
                yield return WaitIdle(p, 6f);
                Teleport(p, spots[idx % spots.Length]);
            }
            yield return new WaitForSeconds(0.4f);
        }

        private IEnumerable<PlayerMover> Others(PlayerMover victim) => players.Where(p => p != victim);

        private static Dictionary<PlayerMover, Vector3> Snapshot(IEnumerable<PlayerMover> who) =>
            who.Where(p => p != null).ToDictionary(p => p, p => p.transform.position);

        /// <summary>다른 도형이 since 이후 복귀되지 않았고 tol 이상 움직이지 않았으면 null.</summary>
        private string CheckUnchanged(Dictionary<PlayerMover, Vector3> snap, float since, float tol)
        {
            List<string> bad = new List<string>();
            foreach (KeyValuePair<PlayerMover, Vector3> kv in snap)
            {
                if (kv.Key == null) continue;
                float d = Vector3.Distance(kv.Key.transform.position, kv.Value);
                int r = RespawnsSince(kv.Key, since);
                if (d > tol || r > 0) bad.Add($"'{kv.Key.name}' 이동 {d:F2}U 복귀 {r}회");
            }
            return bad.Count == 0 ? null : string.Join(", ", bad);
        }

        // ───────────── 종료 ─────────────

        private void Finish()
        {
            if (finished) return;
            finished = true;
            StopAllCoroutines();

            // PTF-3: 검사 중 끊겼을 때도 [2차판정 19] ③ 원복 — S8 관리자 되돌리기, S6 포탈 제거, AntiStuck 로그 관찰 해제.
            try { RestoreS8Manager("Finish"); } catch (Exception e) { report.Notes.Add("S8 관리자 되돌리기 중 예외: " + e.Message); }
            try { DestroyS6Portals(); } catch (Exception e) { report.Notes.Add("S6 포탈 제거 중 예외: " + e.Message); }
            Application.logMessageReceived -= OnAntiStuckLog;

            foreach (Action a in cleanups)
            {
                try { a(); } catch (Exception e) { report.Notes.Add("정리 중 예외: " + e.Message); }
            }
            foreach (PlayerMover m in drivenByUs) if (m != null) m.ExternallyDriven = false;
            drivenByUs.Clear();

            foreach (CheckItem it in report.Items)
                if (!it.Decided) it.Set(Verdict.NA, report.Watchdog ? "watchdog로 중단 — 미실행" : "미실행");

            SessionState.EraseFloat(DeadlineKey);
            string line = report.WriteAndSummarize();
            Debug.Log($"{LogTag} 상세 —\n{report.BuildText()}");
            Debug.Log(line); // 콘솔 마지막 요약 정확히 한 줄(계약 C5)

            if (Application.isBatchMode)
                EditorApplication.Exit(report.ExitCode);
            else
                EditorApplication.isPlaying = false;
        }
    }
}
#endif
