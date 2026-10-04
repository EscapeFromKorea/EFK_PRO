#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 섹터4 "반중력·보안" 팀 기믹 배치·배선(R2 계약 C1-0·C1-5 S4행·C2-4·C3 S4행, 지시서 진행/지시서/R2/S4-W.md).
/// S4_Builder.Build가 지형을 짓고 Refs 검증을 마친 뒤 ③ 단계에서 `S4_Wiring.Wire(generated, refs)`로 1회 부른다.
/// 좌표 원천은 초안 진행/초안/S4_배치초안.md(2차 반영판 최종본 — §15 &gt; §14 &gt; §13 &gt; 본문) §3-2·§4 표뿐이다.
/// 예외(R3): 버블 조준 3행은 S4-W3 재탐색값이다 [2차판정 15 (a) · 계산: 탐색] — 초안 표 갱신은 초안 소유자 몫(보고 S4-W3 reviewRequests).
///
/// [하는 것]
/// ① 팀 기믹 14개 — generated/S4_Gimmicks(refs.gimmickRoot → generated.Find("S4_Gimmicks") → 새로 만듦 순)
///   - 버블 1 `S4_Bubble`: 빌더 트리거 자리(S4_Builder.BuildBubbleTriggerSlot)가 초안과 같으면 지우지 않고 팀 ZeroGravityBubble만
///     AddComponent(uplift 7·maxRiseHeight 0) [M2R1]. 자리가 없거나 다를 때만 우리 빈 GO → BoxCollider 먼저 → isTrigger 직접 →
///     팀 컴포넌트. 어느 쪽이든 검사(콜라이더 = BoxCollider 1개·isTrigger, Rigidbody 없음) [판정 18 ②·사용자 결정 22:50].
///   - 낙석 3 `S4_Rock_StairA/StairB/Bubble`: 팀 본체 FallingRockMenuItem.CreateCurtain(2, null)(internal static, 같은 에디터
///     어셈블리) → 검사(referenceSlowZone null·레인 2) → 이름·인스펙터 값(hitsBeforeRespawn 1, 버블 despawnFallDistance 12.5)
///     → Lane 자식 위치만 초안 좌표로.
///   - 고정 레이저 4 `S4_FL_*`·조준 레이저 6 `S4_AL_*`: 팀 메뉴 경로를 EditorApplication.ExecuteMenuItem으로 실행(메뉴 메서드가
///     private) → Selection 또는 실행 전후 씬 루트 비교로 루트를 받음 → 검사(팀 루트 이름·LaserBeam·모드 컴포넌트·beam 참조·
///     Emitter_Visual) → 이름·변환(발사점·Euler(X,Y,0), forward = 발사 방향)·인스펙터 값.
///   - Refs는 읽기만 한다(계약 C1-0). refs 목록이 null·0개면 여기서 만들어 **지역 목록**에만 담고, 정확한 개수(1·3·4·6)면 만들지 않고
///     확인·배선만 한다. 그 밖의 개수면 LogError 후 그 종류는 만들지 않는다(있는 것만 확인·배선).
/// ② 복귀 부품 — generated/S4_Respawn(계약 C3 이름 그대로)
///   - `S4_SP_Stairs`(sectionId "S4_STAIRS")·`S4_SP_Bubble`("S4_BUBBLE"): 팀 SectionSafePoint(메뉴 함수 RespawnMenuItem.CreateSectionSafePoint)
///     + 보조점 2개씩(`…_Backup0/1`, 그룹 아래 빈 GO — S5 방식). **Collider 없음 = A안** [판정 13]. counter = 각 위치의 낙석 카운터.
///   - 카운터 4 `S4_Counter_RockStairs`(임계 2)·`_LaserStairs`(1)·`_RockBubble`(2)·`_LaserBubble`(1) [확정 5]: 빈 GO + AddComponent.
///   - `CP_S4_Start`: 팀 RespawnZone(메뉴 함수 RespawnMenuItem.CreateCheckpoint) — 원점 (0,0,3), 박스 폭만 8 [판정 8].
///   - `ADAPT_S4_RespawnBridge`: 우리 Lab_SectionRespawnBridge(카운터 → Master RespawnController를 실행 중에 잇는다).
/// ③ 영구 배선 13 — UnityEventTools.AddPersistentListener(동적 GameObject → counter.RegisterHitEvent), 호출 지점 1곳(AddHitListener).
///   낙석 OnHitThresholdExceeded ×3 → Rock 카운터, 레이저 10의 LaserBeam.OnHazardHit → Laser 카운터(FixedPeriodicLaser·
///   CharacterLockedLaser 모두 이벤트가 없고 `beam` 필드가 가리키는 같은 루트의 LaserBeam이 가진다 — LaserBeam.cs:37).
///   배선 뒤 GetPersistentEventCount·GetPersistentTarget로 카운터별 2·5·1·5를 확인한다.
///
/// [하지 않는 것]
/// - 카운터 OnThresholdReached·OnWarning 영구 배선(0개 — Lab_SectionRespawnBridge.cs:34가 영구 배선 있는 카운터를 건너뛰고,
///   OnWarning을 받을 팀 UI가 없다 §7-15). 팀 배선 메뉴(조사 §0-6)·팀 버블 생성 메뉴·팀 낙석 4줄 프리셋 메뉴 실행.
/// - 장외 판정 볼륨(원통 안은 아래가 허공이 아니다 [확정 8]), 버블 시각물 VIS_S4_BubbleShell(S4-L 몫), 머티리얼 지정.
/// - 팀 코드 변경·동작을 바꾸는 어댑터·private 리플렉션·태그/레이어 변경·Refs 필드 대입·팀 에셋 저장(팀 레이저 메뉴의
///   SaveAssets는 Lab_TeamAssetSaveGuard가 막는다 — 판정 팀머티리얼_재저장).
///
/// [R3 — 지시서 진행/지시서/R3/S4-W3.md, 계약 R3 _계약.md R3-S4] ① 버블 조준 3개 발사점·방향·원뿔·사거리 재탐색 — 출발 자리(남쪽 진입)를 덮게
/// [2차판정 15 (a)·판정 17], uplift 7 유지 [2차판정 15]. ② 안전점 주점·보조점 = 바닥 윗면 +0.1(배치 때만 더함, Refs 대조는 바닥 윗면)
/// [2차판정 15·명령 결정 R3-5]. ③ CreateCurtain 직접 호출 추인 [2차판정 16]. ④ CP_S4_Start 막대를 스폰 슬롯 3점에서 수평 ≥1.0으로 옮김 [2차판정 18].
/// [근거] 판정 2026-09-28_초안S2S3S4_판정.md 13·17·사용자 결정 12=(가)·18=②, 1차 판정 8·11, 계약 R2 _계약.md C1-0·C1-4·C1-5·C2-4·C3,
/// 설계/S4_설계.md 1-2·5·7·8 [확정], 조사 진행/조사/S4_팀API.md §0-5~0-7·§3, 팀 코드(줄 번호는 각 상수 주석).
/// 좌표는 섹터 로컬(+Z 진행, 입구 바닥 y 0). 팀 기믹은 g.TransformPoint/g.rotation *로 놓는다(S5_Builder.Place 방식).
/// 출처: [확정]=설계서 사용자 확정, [팀]=팀 코드, [제안]=초안 제안값, [계산]=계산값(초안 탐색 포함), [추정]=실측 필요,
/// [판정]=컨트롤타워 판정, [계약]=R2 계약·지시서 구현 규약. 예외는 항목별 try로 잡아 LogError만 한다(지형·기믹은 지우지 않는다 —
/// 단 검사에 실패한 이번 생성물은 지우고 그 항목만 건너뛴다).
/// </summary>
public static class S4_Wiring
{
    private const string Tag = "[S4_Wiring]";

    // ── 이름 [계약 C2-4·C3 S4행] ──
    private const string GimmickGroupName = "S4_Gimmicks";
    private const string GroupName = "S4_Respawn";
    private const string BubbleName = "S4_Bubble";
    private const string RockPrefix = "S4_Rock_";
    private const string FixedPrefix = "S4_FL_";
    private const string AimPrefix = "S4_AL_";
    private const string SpStairsName = "S4_SP_Stairs";
    private const string SpBubbleName = "S4_SP_Bubble";
    private const string SectionIdStairs = "S4_STAIRS";          // [계약 C1-5 ASCII] 초안 "S4_계단"
    private const string SectionIdBubble = "S4_BUBBLE";          // [계약 C1-5 ASCII] 초안 "S4_버블"
    private const string CpStartName = "CP_S4_Start";
    private const string BridgeAdapterName = "ADAPT_S4_RespawnBridge"; // [계약 C2-0 ADAPT_ 접두사]

    // ── 팀 생성물 [팀] ──
    private const string FixedLaserMenu = "Tools/SecurityLaserSystem/Create Fixed Periodic Laser (CH5)";   // SecurityLaserMenuItem.cs:28 (private)
    private const string AimLaserMenu = "Tools/SecurityLaserSystem/Create Character Locked Laser (CH4)";   // SecurityLaserMenuItem.cs:15 (private)
    private const string FixedLaserTeamName = "SecurityLaser_FixedPeriodic";   // SecurityLaserMenuItem.cs:31
    private const string AimLaserTeamName = "SecurityLaser_CharacterLocked";   // SecurityLaserMenuItem.cs:18
    private const string EmitterChildName = "Emitter_Visual";                  // SecurityLaserMenuItem.cs:45 (콜라이더 없음 :46)
    private const string CurtainTeamName = "FallingRock_Curtain";              // FallingRockMenuItem.cs:34
    private const int CurtainLaneCount = 2;                                    // [초안 §4-2] 팀 4줄 프리셋(:20-25)은 쓰지 않는다
    private const string PoleChildName = "Pole";                               // RespawnMenuItem.cs:45

    /// <summary>카운터 4개(배선 대상). 순서 = 계약 C1-5 나열 순서.</summary>
    private enum Cnt { RockStairs = 0, LaserStairs = 1, RockBubble = 2, LaserBubble = 3 }
    private static readonly string[] CounterNames = { "S4_Counter_RockStairs", "S4_Counter_LaserStairs", "S4_Counter_RockBubble", "S4_Counter_LaserBubble" };
    /// <summary>[확정 5 설계 :34] 낙석 임계 2·레이저 임계 1(팀 기본 2 — SectionHitCounter.cs:36). hitCooldown은 팀 기본 0.5(:40) 그대로.</summary>
    private static readonly int[] CounterThresholds = { 2, 1, 2, 1 };
    /// <summary>[계약 C1-5] 계단 카운터 → S4_SP_Stairs, 버블 카운터 → S4_SP_Bubble.</summary>
    private static readonly bool[] CounterIsBubble = { false, false, true, true };
    private const float CounterCooldownTeam = 0.5f;  // [팀] SectionHitCounter.cs:40 — 확인 전용

    // ── ① 버블 [초안 §3-2 :68·§4-1 :86, 판정 18 ②] ──
    private static readonly Vector3 BubbleOrigin = new Vector3(0f, 13f, 62f);   // 원점, 회전 0, 스케일 1
    private static readonly Vector3 BubbleBoxCenter = Vector3.zero;
    private static readonly Vector3 BubbleBoxSize = new Vector3(8f, 10f, 8f);   // → (−4,8,58)~(4,18,66): 바닥 8 [제안], 윗면 18 [확정]
    private static readonly Bounds BubbleTriggerDraft = MinMax(new Vector3(-4f, 8f, 58f), new Vector3(4f, 18f, 66f)); // 계약 C1-4 bubbleTrigger
    private const float BubbleUplift = 7f;        // [2차판정 15] 7 유지(판정 17은 (a) 조준 레이저 보강으로 달성) · [확정 설계 1-2 :30 "약 7에서 시작, 실측 조정"]
    private const float BubbleMaxRise = 0f;       // [확정 설계 1-2 "maxRiseHeight 쓰지 않음"] ZeroGravityBubble.cs:105 — 0이면 곧바로 빠진다
    // 팀 기본(대입하지 않고 확인만) [팀 ZeroGravityBubble.cs:15-45]
    private const float BubbleSphereTeam = 0.25f, BubbleTetraTeam = 0.16f, BubbleCubeTeam = 0.60f;
    private const float BubbleJumpMulTeam = 1.5f, BubbleDragMulTeam = 2f, BubbleTransitionTeam = 0.5f, BubbleExitTransitionTeam = 0f;
    private const string PlayerTagTeam = "Player";

    // ── ① 낙석 3 [초안 §4-2 :99-105, §3-2 :74-76, §15 :609 — 행 순서 = 계약 C1-1 StairA, StairB, Bubble] ──
    private sealed class RockSpec
    {
        public readonly string Name; public readonly Vector3 Lane1, Lane2; public readonly float Despawn; public readonly Cnt Counter;
        public RockSpec(string name, Vector3 l1, Vector3 l2, float despawn, Cnt counter) { Name = name; Lane1 = l1; Lane2 = l2; Despawn = despawn; Counter = counter; }
    }
    private const float RockDespawnTeam = 8f;         // [팀] FallingRockSpawner.cs:114 기본 — 계단 두 생성기는 대입하지 않는다
    private static readonly RockSpec[] RockSpecs =
    {
        // [2차 — 판정 17] 계단 중심선 r 12.5, φ393·407, 디딤 위 7
        new RockSpec("S4_Rock_StairA", new Vector3(10.48f, 10.10f, 68.81f), new Vector3(8.52f, 10.57f, 71.14f), RockDespawnTeam, Cnt.RockStairs),
        // [초안1 2회] B0 r 12.2 φ432·B1 r 14 φ444 — B0 (가)/(나)(§7-30) 작성자 결정 대기 [2차판정 15] — 초안 소유 S4-D, 값 그대로
        new RockSpec("S4_Rock_StairB", new Vector3(3.77f, 11.40f, 73.60f), new Vector3(1.46f, 11.80f, 75.92f), RockDespawnTeam, Cnt.RockStairs),
        // [2차·초안1 2회] 버블 윗면 위 2. 북쪽 레인 z 64.8 = 덮게(§7-26 작성자 결정 대기 [2차판정 15] — 초안 소유 S4-D), 값 그대로. despawnFallDistance 12.5 [제안](돌 윗면 8까지)
        new RockSpec("S4_Rock_Bubble", new Vector3(0f, 20f, 59.5f), new Vector3(0f, 20f, 64.8f), 12.5f, Cnt.RockBubble),
    };
    private const int RockHitsBeforeRespawn = 1;      // [제안 초안 §4-2 / 조사 §0-5] 팀 기본 0 = 꺼짐(FallingRockSpawner.cs:131)
    // 팀 기본(대입하지 않고 확인만) [팀 FallingRockSpawner.cs:78·84·95·165]
    private const float RockSpawnIntervalTeam = 2f, RockSizeTeam = 1f, RockGapTeam = 2f;
    private const float RockLaneMinXZ = 3f;           // [계산] rockSize 1 + gapWidth 2 — 팀 Validate 기준(FallingRockSpawner.cs:305-313)

    // ── ① 고정 레이저 4 [초안 §4-3 :110-113 — 순서 = 계약 C1-1 Stair1, Stair2, Bubble1, Bubble2] ──
    private sealed class FixedSpec
    {
        public readonly string Name; public readonly Vector3 Pos; public readonly float RotX, RotY, Range; public readonly bool SetRange; public readonly Cnt Counter;
        public FixedSpec(string name, Vector3 pos, float rx, float ry, float range, bool setRange, Cnt counter)
        { Name = name; Pos = pos; RotX = rx; RotY = ry; Range = range; SetRange = setRange; Counter = counter; }
    }
    private const float BeamRangeTeam = 20f;          // [팀] LaserBeam.cs:17
    private static readonly FixedSpec[] FixedSpecs =
    {
        new FixedSpec("S4_FL_Stair1", new Vector3(10.05f, 2.75f, 62.00f), 0f, 90f, 5.5f, true, Cnt.LaserStairs),     // L1 참 φ360, forward (1,0,0), range 5.5 [제안]
        new FixedSpec("S4_FL_Stair2", new Vector3(-5.02f, 5.75f, 70.70f), 0f, -30f, 5.5f, true, Cnt.LaserStairs),    // L2 참 φ480, forward (−0.5,0,0.866), range 5.5 [제안]
        new FixedSpec("S4_FL_Bubble1", new Vector3(-4.95f, 11.00f, 58.85f), 0f, 57.5f, BeamRangeTeam, false, Cnt.LaserBubble),  // SW→NE, range 20 [팀 기본, 대입 안 함]
        new FixedSpec("S4_FL_Bubble2", new Vector3(4.95f, 14.50f, 58.85f), 0f, -57.5f, BeamRangeTeam, false, Cnt.LaserBubble),  // SE→NW, range 20 [팀 기본]
    };
    // 팀 기본(대입하지 않고 확인만) [팀 FixedPeriodicLaser.cs:19·23·26·30]
    private const float FixedWarningTeam = 1f, FixedBeamTeam = 0.4f, FixedRestTeam = 2f;

    // ── ① 조준 레이저 6 [초안 §4-3 :114-119 — 순서·이름 = 계약 C1-1(개수 6 고정), 수치 전부 [계산: 탐색] — 계단 3행 = 초안 그대로, 버블 3행 = S4-W3] ──
    private sealed class AimSpec
    {
        public readonly string Name; public readonly PlayerShapeStats.ShapeKind Kind; public readonly Vector3 Pos;
        public readonly float RotX, RotY, MaxAim, Sensor, Range; public readonly Cnt Counter;
        public AimSpec(string name, PlayerShapeStats.ShapeKind kind, Vector3 pos, float rx, float ry, float maxAim, float sensor, float range, Cnt counter)
        { Name = name; Kind = kind; Pos = pos; RotX = rx; RotY = ry; MaxAim = maxAim; Sensor = sensor; Range = range; Counter = counter; }
    }
    private static readonly AimSpec[] AimSpecs =
    {
        new AimSpec("S4_AL_Stair_Sphere", PlayerShapeStats.ShapeKind.Sphere, new Vector3(0f, 5.20f, 78.80f), 55f, 160f, 60f, 20f, 20f, Cnt.LaserStairs),
        new AimSpec("S4_AL_Stair_Cube", PlayerShapeStats.ShapeKind.Cube, new Vector3(0f, 5.80f, 78.80f), 60f, 160f, 60f, 20f, 20f, Cnt.LaserStairs),
        new AimSpec("S4_AL_Stair_Tetra", PlayerShapeStats.ShapeKind.Tetrahedron, new Vector3(0f, 6.40f, 78.80f), 60f, 160f, 60f, 20f, 20f, Cnt.LaserStairs),
        // [2차판정 15 (a)·판정 17 "버블 조준 레이저가 출발 자리를 덮게" — S4-W3 재탐색, 버블 3행 수치 전부 [계산: 탐색]] 초안 §4-3 표(2차)와 다르다(보고 S4-W3).
        //   탐색 = 초안 S4_check.py 모델(현실 진입 entry_path 459경로·§8-4 통과 경로 29·스냅샷 2초 [팀 CharacterLockedLaser.cs:74-77·181-182]).
        //   채택 순서: 남쪽 진입 명중 ≥1 → §8-4 통과 경로 명중(판정 17 척도 §7-21) → 현실 진입 합계 → 이웃 설정 안정성. 금지 표본(다른 구간·다리·입구 통로·
        //   두 안전점 둘레 1.5, 점프 정점 포함) 록온 0 = 원뿔 +3° 밖 또는 사거리 +1 밖, 광선 연장 누출 0. 2차 설정(구 −2/세모 +2/네모 0, y 17.5)은 남쪽 진입 0/0/0.
        //   구: 버블 북쪽 벽 남쪽 면 앞 0.05(z 66 − 0.05), x −3·y 17.5 — 남쪽 15/261·합계 61/459·통과 5/29, 최소 원뿔 여유 4.1°.
        new AimSpec("S4_AL_Bubble_Sphere", PlayerShapeStats.ShapeKind.Sphere, new Vector3(-3f, 17.50f, 65.95f), 75f, 160f, 30f, 12.5f, 12.5f, Cnt.LaserBubble),
        //   네모: 같은 벽면 앞 0.05, x 1·y 13.5(낙석 레인 x ±0.5를 비킴) — 남쪽 27/261·합계 30/459·통과 9/29, 최소 원뿔 여유 26.5°.
        //     (기록 M3R1) 북쪽 벽을 따라 오르는 몸(중심 z 65.5, |x| ≤ 3.5)이 발사구 시각물을 지나갈 수 있다 — 콜라이더 없음이라 물리 영향 없고 시각 관통만(추정).
        new AimSpec("S4_AL_Bubble_Cube", PlayerShapeStats.ShapeKind.Cube, new Vector3(1f, 13.50f, 65.95f), 60f, 170f, 40f, 9f, 9f, Cnt.LaserBubble),
        //   세모: 벽면에서는 어떤 방향·원뿔(20~60°)로도 남쪽 0 → 버블 남쪽 가장자리(출발 자리) 바로 위 공중 y 25.5, 바로 아래(X 90).
        //   받침 없음(팀 발사구 시각물 콜라이더 없음 — SecurityLaserMenuItem.cs:44-47, 밟을 수 없음). y 25.5는 원통 벽 윗면 26 아래 [계산: 탐색].
        //   [M3R1 정정] 세모 몸 윗면 최고 = 초안 §5 9절·§6-1 최악값: 돌 윗면에서 뛰어 들어가기 25.57, 버블 안 점프 24.74 [계산 초안].
        //   → 발사점 중심 대비 여유 −0.07(최악 뛰어 들어가기; 버블 안 점프는 +0.76). 시각물(세로 0.3, 25.35~25.65)은 25.57에서 0.22 겹친다.
        //   콜라이더가 없어 끼임·밟힘은 없고 몸이 시각물을 지나가는 시각 관통만 생긴다. 발사점을 올릴지(벽 26 조건과 충돌)·수용할지는 컨트롤타워 판단(보고 reviewRequests 3).
        //   남쪽 30/261·합계 30/459·통과 0/29, 금지 표본 최소 원뿔 여유 격자 3.6°, 안전점 원 경계 연속 약 3.1°(몸 중심 +0.25 표본; +0.5 이상이면 약 3.3°) — ≥3 충족.
        new AimSpec("S4_AL_Bubble_Tetra", PlayerShapeStats.ShapeKind.Tetrahedron, new Vector3(1.5f, 25.50f, 58.50f), 90f, 0f, 6f, 18f, 18f, Cnt.LaserBubble),
    };
    // 팀 기본(대입하지 않고 확인만) [팀 CharacterLockedLaser.cs:34·37·41·44]
    private const float AimFirstFireTeam = 2f, AimRelockTeam = 1f, AimRefireTeam = 2f, AimBeamDurationTeam = 0.4f;

    // ── ② 안전점 [초안 §3-2 :70-71·§4-4 :138-141 — 계약 C1-4 spStairsLocal/Backups·spBubbleLocal/Backups] ──
    private static readonly Vector3 SpStairsMain = new Vector3(-3.24f, 0f, 49.93f);                  // φ255°, r 12.5, 바닥 y 0
    private static readonly Vector3[] SpStairsBackups = { new Vector3(-4.01f, 0f, 47.03f), new Vector3(-4.79f, 0f, 44.13f) }; // r 15.5·18.5
    private static readonly Vector3 SpBubbleMain = new Vector3(0f, 8f, 54.5f);                        // 버블 남쪽 면에서 3.5, 돌 윗면 y 8
    private static readonly Vector3[] SpBubbleBackups = { new Vector3(-3f, 8f, 54.5f), new Vector3(3f, 8f, 54.5f) };
    /// <summary>[2차판정 15 · 계약 R3-1 8·R3-S4] 안전점 = 바닥 윗면 +0.1(전 섹터 공통). 위 상수·Refs `sp*`는 바닥 윗면 값 그대로 두고
    /// 배치(주점·보조점·카운터 위치)와 배치 확인 때만 한 번 더한다 — 두 번 더하지 않는다 [명령 결정 R3-5].</summary>
    private const float SafePointLift = 0.1f;
    private const float OccupancyRadiusTeam = 0.6f;   // [팀] SectionSafePoint.cs:29 — 바꾸지 않는다(검사용)
    private const float ShapeHeight = 1f;             // [확정 공통 규칙 11] 도형 약 1U — 안전점 위 몸 높이(겹침 검사용)

    // ── ② 체크포인트 [초안 §3-2 :73·§4-4 :146, 판정 8 폭 8] ──
    private static readonly Vector3 CpStartLocal = new Vector3(0f, 0f, 3f);      // 계약 C1-4 checkpointStartLocal
    private const float CpWidthX = 8f;                                            // [제안 — 판정 8 승인] 팀 기본 6(RespawnMenuItem.cs:22)
    private static readonly Vector3 CpSizeExpect = new Vector3(8f, 18f, 6f);      // 높이 18·깊이 6 = 팀 기본(:23·:38)
    private static readonly Vector3 CpCenterExpect = new Vector3(0f, 9f, 0f);     // [팀] :39
    private static readonly Vector3 CpPoleTeam = new Vector3(-2.6f, 0f, 0f);      // [팀] :47 −ZoneWidth/2+0.4(ZoneWidth 6) — 생성 직후 값(확인용)
    /// <summary>[2차판정 18 · 계약 R2-C4 PTC] 막대를 스폰 슬롯에서 수평 ≥1.0 뗀다(옮김 = S4-W). 새 로컬 = 팀 식 −폭/2+0.4를 우리 폭 8에 적용
    /// [계산: RespawnMenuItem.cs:47 식] → (−3.6, 0, 0) = 섹터 (−3.6, 0, 3). 슬롯 0 (−2, 3)과 1.6, 박스 x ±4 안(가장자리 0.4), 입구 벽 안쪽 면 x −4에서 0.4
    /// (막대·깃발 콜라이더 없음 — RespawnMenuItem.cs StripCollider). 깃발은 팀 생성 그대로 막대 +x 0.45·높이 3.4(도형 몸 위).</summary>
    private static readonly Vector3 CpPoleLocal = new Vector3(-CpWidthX * 0.5f + 0.4f, 0f, 0f);
    /// <summary>[PTC-2 보고 :47-58 · 러너 스폰] 섹터 로컬 스폰 슬롯 3점(x, z) — 막대 수평 거리 확인용.</summary>
    private static readonly Vector2[] SpawnSlotsXZ = { new Vector2(-2f, 3f), new Vector2(0f, 3f), new Vector2(2f, 3f) };
    private const float PoleSlotMin = 1.0f;                                       // [2차판정 18]

    private const float PoseTol = 0.001f;   // [계약 지시서 완료 조건] 코드 좌표 = 초안 표 ±0.001
    private const float RotTolDeg = 0.1f;
    private const float RefsTol = 0.01f;    // [계약 지시서 3] refs 값 ↔ 초안 대조 허용

    /// <summary>계약 C1-0. 예외를 밖으로 던지지 않는다(항목별 try — 한 항목 실패가 나머지를 멈추지 않는다).</summary>
    public static void Wire(Transform generated, S4_Refs refs)
    {
        if (generated == null) { Debug.LogError($"{Tag} generated가 null이다 — 기믹·복귀 부품을 만들지 않는다."); return; }
        if (refs == null) { Debug.LogError($"{Tag} refs가 null이다 — 기믹·복귀 부품을 만들지 않는다.", generated); return; }
        if (refs.generated != null && refs.generated != generated)
            Debug.LogError($"{Tag} refs.generated '{refs.generated.name}' ≠ 인자 generated '{generated.name}' — 인자 기준으로 진행한다.", generated);
        Try("축 정렬 확인", () => CheckAxisAligned(generated));
        Try("refs 좌표 대조", () => CheckRefsAgainstDraft(generated, refs));

        // ① 팀 기믹 — 지역 목록(Refs에 대입하지 않는다, 계약 C1-0)
        Transform gimmicks = null;
        ZeroGravityBubble bubble = null;
        List<FallingRockSpawner> rocks = new List<FallingRockSpawner>();
        List<FixedPeriodicLaser> fixedLasers = new List<FixedPeriodicLaser>();
        List<CharacterLockedLaser> aimLasers = new List<CharacterLockedLaser>();
        string bubbleMode = "건너뜀", rockMode = "건너뜀", fixedMode = "건너뜀", aimMode = "건너뜀";
        Try("기믹 그룹", () => gimmicks = ResolveGimmickGroup(generated, refs));
        if (gimmicks != null)
        {
            Try("버블 배치", () => bubbleMode = PrepareBubble(generated, gimmicks, refs, out bubble));
            Try("낙석 배치", () => rockMode = PrepareList(refs.rockSpawners, RockSpecs.Length, "rockSpawners", generated, gimmicks, RockPrefix,
                                                            i => CreateRock(generated, gimmicks, RockSpecs[i]), rocks));
            Try("고정 레이저 배치", () => fixedMode = PrepareList(refs.fixedLasers, FixedSpecs.Length, "fixedLasers", generated, gimmicks, FixedPrefix,
                                                                i => CreateFixedLaser(generated, gimmicks, FixedSpecs[i]), fixedLasers));
            Try("조준 레이저 배치", () => aimMode = PrepareList(refs.aimLasers, AimSpecs.Length, "aimLasers", generated, gimmicks, AimPrefix,
                                                              i => CreateAimLaser(generated, gimmicks, AimSpecs[i]), aimLasers));
        }

        // ② 복귀 부품
        Transform group;
        try { group = NewGroup(generated); }
        catch (System.Exception e) { Debug.LogError($"{Tag} '{GroupName}' 그룹 생성 예외 — 복귀 부품·배선 중단(기믹은 유지): {e}", generated); return; }

        SectionSafePoint spStairs = null, spBubble = null;
        SectionHitCounter[] counters = new SectionHitCounter[CounterNames.Length];
        RespawnZone cp = null;
        Lab_SectionRespawnBridge adapter = null;
        Try(SpStairsName, () => spStairs = BuildSafePoint(group, SpStairsName, SectionIdStairs, SpStairsMain, SpStairsBackups));
        Try(SpBubbleName, () => spBubble = BuildSafePoint(group, SpBubbleName, SectionIdBubble, SpBubbleMain, SpBubbleBackups));
        for (int k = 0; k < CounterNames.Length; k++)
        {
            int idx = k;
            SectionSafePoint dest = CounterIsBubble[idx] ? spBubble : spStairs;
            Vector3 at = Lift(CounterIsBubble[idx] ? SpBubbleMain : SpStairsMain);   // 목적지 주점과 같은 자리(+0.1) [2차판정 15]
            Try(CounterNames[idx], () => counters[idx] = BuildCounter(group, CounterNames[idx], CounterThresholds[idx], dest, at));
        }
        Try("안전점 counter 연결", () =>
        {
            LinkSafePointCounter(spStairs, counters[(int)Cnt.RockStairs]);
            LinkSafePointCounter(spBubble, counters[(int)Cnt.RockBubble]);
        });
        Try(CpStartName, () => cp = BuildCheckpoint(group));
        Try(BridgeAdapterName, () =>
        {
            GameObject go = new GameObject(BridgeAdapterName);
            go.transform.SetParent(group, false);
            adapter = go.AddComponent<Lab_SectionRespawnBridge>();
        });

        // ③ 배선 13 + 확인
        List<WireRecord> wires = new List<WireRecord>();
        Try("낙석 배선", () => WireRocks(rocks, counters, wires));
        Try("고정 레이저 배선", () => WireFixed(fixedLasers, counters, wires));
        Try("조준 레이저 배선", () => WireAim(aimLasers, counters, wires));
        int wiredOk = 0;
        Try("영구 배선 확인", () => wiredOk = VerifyWiring(wires, counters));
        Try("카운터 이벤트 확인", () => VerifyCounterEvents(counters));

        // 확인(값은 쓰지 않음) — 다르면 LogError만.
        int bubbleBad = 0, rockBad = 0, fixedBad = 0, aimBad = 0, respawnBad = 0;
        Try("버블 확인", () => bubbleBad = VerifyBubble(generated, bubble));
        Try("낙석 확인", () => rockBad = VerifyRocks(generated, rocks));
        Try("고정 레이저 확인", () => fixedBad = VerifyFixed(generated, fixedLasers));
        Try("조준 레이저 확인", () => aimBad = VerifyAim(generated, aimLasers));
        Try("복귀 부품 확인", () => respawnBad = VerifyRespawn(generated, group, spStairs, spBubble, counters, cp));
        Try("그룹 확인", () => VerifyGroups(generated, gimmicks, group));
        Try("씬 루트 잔류 확인", () => VerifyNoStrayRoots(generated));

        int expectWires = RockSpecs.Length + FixedSpecs.Length + AimSpecs.Length;
        int madeCounters = 0;
        foreach (SectionHitCounter c in counters) if (c != null) madeCounters++;
        bool gimOk = bubble != null && rocks.Count == RockSpecs.Length && fixedLasers.Count == FixedSpecs.Length && aimLasers.Count == AimSpecs.Length;
        bool resOk = spStairs != null && spBubble != null && madeCounters == CounterNames.Length && cp != null && adapter != null;
        int bad = bubbleBad + rockBad + fixedBad + aimBad + respawnBad;
        string summary = $"{Tag} 기믹 — 버블 {(bubble != null ? 1 : 0)}/1({bubbleMode}), 낙석 {rocks.Count}/{RockSpecs.Length}({rockMode}), " +
                         $"고정 레이저 {fixedLasers.Count}/{FixedSpecs.Length}({fixedMode}), 조준 레이저 {aimLasers.Count}/{AimSpecs.Length}({aimMode}); " +
                         $"복귀 — 안전점 {Ok(spStairs)}/{Ok(spBubble)}, 카운터 {madeCounters}/{CounterNames.Length}(임계 2·1·2·1), CP {Ok(cp)}, 브리지 {Ok(adapter)}; " +
                         $"영구 배선 {wiredOk}/{expectWires}; 확인 불일치 — 버블 {bubbleBad}, 낙석 {rockBad}, 고정 {fixedBad}, 조준 {aimBad}, 복귀 {respawnBad}";
        if (gimOk && resOk && wiredOk == expectWires && bad == 0) Debug.Log(summary, group);
        else Debug.LogError(summary + " — 위 로그 확인.", group);
    }

    private static string Ok(Object o) => o != null ? "O" : "X";

    private static void Try(string label, System.Action a)
    {
        try { a(); }
        catch (System.Exception e) { Debug.LogError($"{Tag} '{label}' 예외 — 이 항목만 건너뛴다: {e}"); }
    }

    // ───────────────────────── 그룹 ─────────────────────────

    /// <summary>기믹 그룹: refs.gimmickRoot(generated 아래일 때) → generated.Find("S4_Gimmicks") → 새로 만듦(로컬 원점·무회전).</summary>
    private static Transform ResolveGimmickGroup(Transform generated, S4_Refs refs)
    {
        Transform given = refs.gimmickRoot;
        if (given != null)
        {
            if (given.IsChildOf(generated))
            {
                if (given.name != GimmickGroupName)
                    Debug.LogWarning($"{Tag} refs.gimmickRoot 이름 '{given.name}' ≠ '{GimmickGroupName}'(계약 C3·PTF-2 부모 검사) — 그대로 쓴다.", given);
                return given;
            }
            Debug.LogError($"{Tag} refs.gimmickRoot '{given.name}'가 generated 아래가 아니다 — 쓰지 않고 '{GimmickGroupName}'을 찾거나 만든다.", given);
        }
        Transform found = generated.Find(GimmickGroupName);
        if (found != null) return found;
        GameObject go = new GameObject(GimmickGroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    /// <summary>generated/S4_Respawn 1개. 같은 빌드에서 두 번 불려도 겹치지 않게 기존 그룹(우리 생성물만 있음)을 지운다(S5·S3_Wiring과 같음).</summary>
    private static Transform NewGroup(Transform generated)
    {
        for (int i = generated.childCount - 1; i >= 0; i--)
        {
            Transform c = generated.GetChild(i);
            if (c.name == GroupName) Object.DestroyImmediate(c.gameObject);
        }
        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false);
        return go.transform;
    }

    /// <summary>refs 목록이 비어 이번에 만들 차례일 때, 같은 이름의 우리 잔류물(같은 빌드에서 Wire가 두 번 불린 경우)을 먼저 지운다 — 중복 방지.
    /// 계약 C2-4 우리 이름(접두사 S4_Rock_·S4_FL_·S4_AL_ 또는 S4_Bubble)인 기믹 그룹 직속 자식만 대상이다.</summary>
    private static void RemoveOwnLeftovers(Transform gimmicks, string prefix, bool exact)
    {
        int n = 0;
        for (int i = gimmicks.childCount - 1; i >= 0; i--)
        {
            Transform c = gimmicks.GetChild(i);
            bool hit = exact ? c.name == prefix : c.name.StartsWith(prefix, System.StringComparison.Ordinal);
            if (!hit) continue;
            Object.DestroyImmediate(c.gameObject);
            n++;
        }
        if (n > 0) Debug.LogWarning($"{Tag} '{gimmicks.name}' 아래 '{prefix}{(exact ? "" : "*")}' 잔류 {n}개를 지우고 다시 만든다(refs는 비어 있었다).", gimmicks);
    }

    // ───────────────────────── ① 버블 ─────────────────────────

    /// <summary>refs.bubble가 있으면 만들지 않고 그것을 쓴다(확인만). 없으면 빌더의 트리거 자리(S4_Builder.BuildBubbleTriggerSlot —
    /// S4_Gimmicks/S4_Bubble, BoxCollider isTrigger)를 받아 팀 ZeroGravityBubble만 붙인다 [M2R1 — 이중 생성 경로 제거].
    /// 자리가 없거나 초안과 다를 때만 판정 18 ② 순서로 새로 만든다 — 팀 버블 생성 메뉴는 쓰지 않는다.</summary>
    private static string PrepareBubble(Transform generated, Transform gimmicks, S4_Refs refs, out ZeroGravityBubble bubble)
    {
        bubble = refs.bubble;
        if (bubble != null) return "빌더 것 확인";

        bubble = AdoptBuilderSlot(generated, gimmicks);
        if (bubble != null) return "빌더 트리거 자리에 부착";

        RemoveOwnLeftovers(gimmicks, BubbleName, true);
        GameObject go = new GameObject(BubbleName);                  // ① 빈 GO(S4_Gimmicks 아래)
        try
        {
            go.transform.SetParent(gimmicks, false);
            Place(generated, go.transform, BubbleOrigin, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            BoxCollider box = go.AddComponent<BoxCollider>();         // ② BoxCollider 먼저 — RequireComponent(Collider)는 추상 타입(ZeroGravityBubble.cs:11)
            box.isTrigger = true;                                     // ③ 직접 대입 — Reset(:56-59) 호출 여부와 무관
            box.center = BubbleBoxCenter;
            box.size = BubbleBoxSize;
            ZeroGravityBubble zb = go.AddComponent<ZeroGravityBubble>(); // ④ 팀 컴포넌트. Rigidbody는 붙이지 않는다(초안 §4-1 ⑶)
            zb.upliftAcceleration = BubbleUplift;
            zb.maxRiseHeight = BubbleMaxRise;
            // 배율 0.25/0.16/0.60·jumpHeightMultiplier 1.5·dragMultiplier 2·transitionTime 0.5·exitTransitionTime 0·playerTag는 팀 기본 — 대입하지 않는다.

            // ⑤ 검사: 콜라이더 = BoxCollider 1개·isTrigger, Rigidbody 없음. 아니면 이번 생성물을 지우고 건너뛴다.
            Collider[] cols = go.GetComponents<Collider>();
            List<string> bad = new List<string>();
            if (cols.Length != 1 || !(cols[0] is BoxCollider)) bad.Add($"콜라이더 {cols.Length}개(BoxCollider 1개여야 한다 — 자동 추가 의심)");
            else if (!cols[0].isTrigger) bad.Add("BoxCollider.isTrigger false");
            if (go.GetComponent<Rigidbody>() != null) bad.Add("Rigidbody가 붙어 있다");
            if (bad.Count > 0)
            {
                Debug.LogError($"{Tag} {BubbleName} 검사 실패 [판정 18 ②] — 지우고 건너뛴다: {string.Join(", ", bad)}", gimmicks);
                Object.DestroyImmediate(go);
                return "검사 실패";
            }
            EditorUtility.SetDirty(box);
            EditorUtility.SetDirty(zb);
            EditorUtility.SetDirty(go);
            bubble = zb;
            return "여기서 생성";
        }
        catch
        {
            if (go != null) Object.DestroyImmediate(go);
            throw;
        }
    }

    /// <summary>[M2R1] 기믹 그룹 직속 'S4_Bubble'이 정확히 1개이고 초안과 같으면(원점 (0,13,62)·회전 0·스케일 1, 콜라이더 = isTrigger
    /// BoxCollider 1개, 섹터 로컬 박스 (−4,8,58)~(4,18,66) ±0.01, Rigidbody 없음) 지우지 않고 팀 ZeroGravityBubble만 붙인다
    /// (이미 붙어 있으면 그것을 쓴다 — 같은 빌드에서 Wire가 두 번 불린 경우). 우리 변환·콜라이더 값은 쓰지 않는다(빌더 소유).
    /// 없으면 null(조용히 — 여기서 새로 만든다). 있는데 다르면 LogError 후 null(호출자가 지우고 초안 값으로 다시 만든다).</summary>
    private static ZeroGravityBubble AdoptBuilderSlot(Transform generated, Transform gimmicks)
    {
        List<GameObject> found = new List<GameObject>();
        for (int i = 0; i < gimmicks.childCount; i++)
        {
            Transform c = gimmicks.GetChild(i);
            if (c.name == BubbleName) found.Add(c.gameObject);
        }
        if (found.Count == 0) return null;

        GameObject go = found[0];
        List<string> bad = new List<string>();
        if (found.Count != 1) bad.Add($"같은 이름 {found.Count}개");
        else
        {
            CheckPose(generated, go.transform, BubbleOrigin, Quaternion.identity, bad);
            if ((go.transform.localScale - Vector3.one).sqrMagnitude > PoseTol * PoseTol) bad.Add($"스케일 {V(go.transform.localScale)} ≠ (1, 1, 1)");
            Collider[] cols = go.GetComponents<Collider>();
            BoxCollider box = cols.Length == 1 ? cols[0] as BoxCollider : null;
            if (box == null) bad.Add($"콜라이더 {cols.Length}개(isTrigger BoxCollider 1개여야)");
            else
            {
                if (!box.isTrigger) bad.Add("BoxCollider.isTrigger false");
                Bounds lb = BoxLocalBounds(generated, box);
                if (!BoundsNear(lb, BubbleTriggerDraft, PoseTol * 10f))
                    bad.Add($"트리거 min{V(lb.min)} max{V(lb.max)} ≠ 초안 min{V(BubbleTriggerDraft.min)} max{V(BubbleTriggerDraft.max)}");
            }
            if (go.GetComponent<Rigidbody>() != null) bad.Add("Rigidbody가 붙어 있다");
            if (go.GetComponents<ZeroGravityBubble>().Length > 1) bad.Add("ZeroGravityBubble 2개 이상");
        }
        if (bad.Count > 0)
        {
            Debug.LogError($"{Tag} 빌더 트리거 자리 '{BubbleName}'가 초안 §4-1·판정 18 ②와 다르다 — 받지 않고 지운 뒤 초안 값으로 다시 만든다" +
                           $"(S4-B 확인): {string.Join(", ", bad)}", gimmicks);
            return null;
        }

        ZeroGravityBubble zb = go.GetComponent<ZeroGravityBubble>();
        bool added = false;
        if (zb == null) { zb = go.AddComponent<ZeroGravityBubble>(); added = true; }   // ④ 팀 컴포넌트 — BoxCollider가 이미 있어 RequireComponent(Collider)가 더 붙이지 않는다(검사로 확인)
        zb.upliftAcceleration = BubbleUplift;
        zb.maxRiseHeight = BubbleMaxRise;
        // 배율·jump·drag·transition·exitTransition·playerTag는 팀 기본 — 대입하지 않는다(VerifyBubble이 확인).

        // ⑤ 붙인 뒤 검사: 콜라이더가 여전히 isTrigger BoxCollider 1개. 아니면 이번에 붙인 컴포넌트만 떼고 null(호출자가 다시 만든다).
        Collider[] after = go.GetComponents<Collider>();
        if (after.Length != 1 || !(after[0] is BoxCollider) || !after[0].isTrigger)
        {
            Debug.LogError($"{Tag} '{BubbleName}'에 ZeroGravityBubble을 붙인 뒤 콜라이더 {after.Length}개·트리거 {(after.Length > 0 && after[0].isTrigger)} — " +
                           "받지 않고 다시 만든다(§7-27 자동 추가 의심).", gimmicks);
            if (added) Object.DestroyImmediate(zb);
            return null;
        }
        EditorUtility.SetDirty(zb);
        EditorUtility.SetDirty(go);
        return zb;
    }

    // ───────────────────────── ① 목록 공통 ─────────────────────────

    /// <summary>refs 목록 null·0개 → specCount개를 만든다. specCount개 → 만들지 않고 그것을 쓴다(확인·배선). 그 밖 → LogError, 만들지 않는다
    /// (있는 것만 확인·배선). 결과는 into(지역 목록)에만 담는다. 한 개 생성 실패는 LogError 후 그 항목만 건너뛴다.</summary>
    private static string PrepareList<T>(List<T> given, int specCount, string field, Transform generated, Transform gimmicks, string prefix,
                                         System.Func<int, T> create, List<T> into) where T : Component
    {
        if (given != null && given.Count == specCount) { into.AddRange(given); return "빌더 것 확인"; }
        if (given != null && given.Count != 0)
        {
            Debug.LogError($"{Tag} refs.{field} {given.Count}개 — 0개도 계약 {specCount}개도 아니다. 만들지 않고 있는 것만 확인·배선한다(S4-B·컨트롤타워).", generated);
            into.AddRange(given);
            return "개수 불일치";
        }

        RemoveOwnLeftovers(gimmicks, prefix, false);
        for (int i = 0; i < specCount; i++)
        {
            T made = null;
            try { made = create(i); }
            catch (System.Exception e) { Debug.LogError($"{Tag} refs.{field}[{i}] 생성 예외 — 이 항목만 건너뛴다: {e}", gimmicks); }
            if (made != null) into.Add(made);
        }
        return "여기서 생성";
    }

    // ───────────────────────── ① 낙석 ─────────────────────────

    /// <summary>팀 본체 CreateCurtain(2, null)(FallingRockMenuItem.cs:30) → 검사 → 옮김·이름·인스펙터 값 → Lane 자식 위치.
    /// 직접 호출(internal — 같은 에디터 어셈블리의 팀 편집기 생성 코드) = 메뉴 사용과 같음 [2차판정 16 추인]. 셸 알파·큐·Refs.floors는 이 파일 몫이 아니다(S4-L·S4-B).
    /// 루트 위치 = 두 레인의 중점, 회전 0 [추정 — 초안 미지정. 생성기는 spawnPositions 위치만 쓴다]. Lane 회전은 팀 생성 그대로(로컬 0).</summary>
    private static FallingRockSpawner CreateRock(Transform generated, Transform gimmicks, RockSpec s)
    {
        GameObject root = CaptureNewRoot(() => FallingRockMenuItem.CreateCurtain(CurtainLaneCount, null),
                                         "FallingRockMenuItem.CreateCurtain(2, null)", typeof(FallingRockSpawner));
        if (root == null) return null;
        try
        {
            FallingRockSpawner sp = root.GetComponent<FallingRockSpawner>();
            List<string> bad = new List<string>();
            if (root.name != CurtainTeamName) bad.Add($"루트 이름 '{root.name}' ≠ '{CurtainTeamName}'");
            if (sp == null) bad.Add("FallingRockSpawner 없음");
            else
            {
                // 씬에 SlowZone이 있으면 CreateCurtain이 자동 연결하고 루트를 구역 위로 옮긴다(:42-52) — S4는 SlowZone 없이 쓴다(§7-5).
                if (sp.referenceSlowZone != null) bad.Add($"referenceSlowZone '{sp.referenceSlowZone.name}'이 자동 연결됐다(씬에 SlowZone 있음)");
                if (sp.spawnPositions == null || sp.spawnPositions.Length != CurtainLaneCount)
                    bad.Add($"spawnPositions {(sp.spawnPositions == null ? "null" : sp.spawnPositions.Length.ToString())}개 ≠ {CurtainLaneCount}");
                else
                    for (int i = 0; i < CurtainLaneCount; i++)
                    {
                        Transform lane = sp.spawnPositions[i];
                        if (lane == null || lane.parent != root.transform || lane.name != $"Lane_{i + 1}")
                            bad.Add($"spawnPositions[{i}]가 루트 자식 'Lane_{i + 1}'이 아니다");
                    }
            }
            if (bad.Count > 0)
            {
                Debug.LogError($"{Tag} 낙석 {s.Name} 생성물 검사 실패 [초안 §4-2] — 지우고 건너뛴다: {string.Join(", ", bad)}");
                Object.DestroyImmediate(root);
                return null;
            }

            root.transform.SetParent(gimmicks, true);
            root.name = s.Name;
            Place(generated, root.transform, (s.Lane1 + s.Lane2) * 0.5f, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            sp.spawnPositions[0].position = generated.TransformPoint(s.Lane1);   // 위치만(회전은 팀 생성 그대로)
            sp.spawnPositions[1].position = generated.TransformPoint(s.Lane2);
            sp.hitsBeforeRespawn = RockHitsBeforeRespawn;
            if (!Near(s.Despawn, RockDespawnTeam)) sp.despawnFallDistance = s.Despawn;   // 버블만 12.5 [제안]. 계단은 팀 기본 8 — 대입하지 않는다
            // spawnInterval 2·rockSize 1·gapWidth 2·keepLane true·referenceSlowZone null은 팀 기본 — 대입하지 않는다.
            EditorUtility.SetDirty(sp.spawnPositions[0]);
            EditorUtility.SetDirty(sp.spawnPositions[1]);
            EditorUtility.SetDirty(root.transform);
            EditorUtility.SetDirty(sp);
            return sp;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{Tag} 낙석 {s.Name} 설정 예외 — 지우고 건너뛴다: {e}");
            if (root != null) Object.DestroyImmediate(root);
            return null;
        }
    }

    // ───────────────────────── ① 레이저 ─────────────────────────

    private static FixedPeriodicLaser CreateFixedLaser(Transform generated, Transform gimmicks, FixedSpec s) =>
        CreateLaser<FixedPeriodicLaser>(generated, gimmicks, FixedLaserMenu, FixedLaserTeamName, s.Name, s.Pos, Quaternion.Euler(s.RotX, s.RotY, 0f),
            l => l.beam,
            l =>
            {
                if (s.SetRange) { l.beam.range = s.Range; EditorUtility.SetDirty(l.beam); }   // 계단 5.5 [제안]. 버블은 팀 기본 20 — 대입하지 않는다
                // warning 1·beam 0.4·rest 2·autoActivateOnStart true는 팀 기본 — 대입하지 않는다.
            });

    private static CharacterLockedLaser CreateAimLaser(Transform generated, Transform gimmicks, AimSpec s) =>
        CreateLaser<CharacterLockedLaser>(generated, gimmicks, AimLaserMenu, AimLaserTeamName, s.Name, s.Pos, Quaternion.Euler(s.RotX, s.RotY, 0f),
            l => l.beam,
            l =>
            {
                l.targetKinds = new[] { s.Kind };     // [팀 PRD "센서마다 고유 대상", 초안 §4-3] 도형별 1개
                l.maxAimAngle = s.MaxAim;             // [계산: 탐색]
                l.sensorRange = s.Sensor;             // [계산: 탐색]
                l.beam.range = s.Range;               // [계산: 탐색]
                // firstFire 2·relock 1·refire 2·beamDuration 0.4는 팀 기본 — 대입하지 않는다.
                EditorUtility.SetDirty(l.beam);
            });

    /// <summary>팀 레이저 메뉴(private)를 ExecuteMenuItem으로 실행 → 루트 받기 → 검사(초안 §4-3 ④) → 옮김·이름·변환 → configure(인스펙터 값).
    /// 검사·설정 실패면 이번 생성물을 지우고 null(부분 생성물 잔류 없음).</summary>
    private static T CreateLaser<T>(Transform generated, Transform gimmicks, string menu, string teamName, string newName, Vector3 pos, Quaternion rot,
                                    System.Func<T, LaserBeam> beamOf, System.Action<T> configure) where T : Component
    {
        GameObject root = CaptureNewRoot(() =>
        {
            if (!EditorApplication.ExecuteMenuItem(menu)) throw new System.InvalidOperationException($"ExecuteMenuItem('{menu}')가 false를 돌려줬다");
            return null;
        }, menu, typeof(T));
        if (root == null) return null;
        try
        {
            List<string> bad = new List<string>();
            if (root.name != teamName) bad.Add($"루트 이름 '{root.name}' ≠ '{teamName}'");
            LaserBeam beam = root.GetComponent<LaserBeam>();
            if (beam == null) bad.Add("루트에 LaserBeam 없음");
            T mode = root.GetComponent<T>();
            if (mode == null) bad.Add($"루트에 {typeof(T).Name} 없음");
            else if (beam == null || beamOf(mode) != beam) bad.Add($"{typeof(T).Name}.beam ≠ 같은 루트의 LaserBeam");
            if (root.transform.Find(EmitterChildName) == null) bad.Add($"자식 '{EmitterChildName}' 없음");
            if (bad.Count > 0)
            {
                Debug.LogError($"{Tag} 레이저 {newName} 생성물 검사 실패 [초안 §4-3] — 지우고 건너뛴다: {string.Join(", ", bad)}");
                Object.DestroyImmediate(root);
                return null;
            }

            root.transform.SetParent(gimmicks, true);
            root.name = newName;
            Place(generated, root.transform, pos, rot);
            root.transform.localScale = Vector3.one;
            configure(mode);
            EditorUtility.SetDirty(root.transform);
            EditorUtility.SetDirty(mode);
            return mode;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"{Tag} 레이저 {newName} 설정 예외 — 지우고 건너뛴다: {e}");
            if (root != null) Object.DestroyImmediate(root);
            return null;
        }
    }

    // ───────────────────────── ② 안전점·카운터·체크포인트 ─────────────────────────

    /// <summary>팀 SectionSafePoint — 팀은 레이 없이 이 위치로 옮긴다(SectionSafePoint.cs:8-11). 위치 = 바닥 윗면 +0.1 [2차판정 15]
    /// (main·backupLocal은 바닥 윗면 값, 여기서 Lift로 한 번만 더한다). 보조점은 그룹 아래 빈 GO(S5 방식).
    /// Collider를 붙이지 않는다 = A안 [판정 13]. occupancyRadius는 팀 기본 0.6 그대로.</summary>
    private static SectionSafePoint BuildSafePoint(Transform group, string name, string sectionId, Vector3 main, Vector3[] backupLocal)
    {
        GameObject root = CaptureNewRoot(() => { RespawnMenuItem.CreateSectionSafePoint(); return null; },
                                         "Tools/Respawn/Create Section Safe Point", typeof(SectionSafePoint));
        if (root == null) return null;
        root.transform.SetParent(group, false);
        SectionSafePoint point = root.GetComponent<SectionSafePoint>();
        root.name = name;
        root.transform.localPosition = Lift(main);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        point.sectionId = sectionId;

        Transform[] backups = new Transform[backupLocal.Length];
        for (int i = 0; i < backupLocal.Length; i++)
        {
            GameObject b = new GameObject($"{name}_Backup{i}");
            b.transform.SetParent(group, false);
            b.transform.localPosition = Lift(backupLocal[i]);
            backups[i] = b.transform;
        }
        point.backupPoints = backups;
        if (root.GetComponent<Collider>() != null)
            Debug.LogError($"{Tag} {name}: 팀 안전점에 Collider가 있다 — A안(트리거 없음) [판정 13]과 다르다(지우지 않음, 보고).", root);
        EditorUtility.SetDirty(point);
        EditorUtility.SetDirty(root);
        return point;
    }

    /// <summary>팀 SectionHitCounter(AddComponent — 팀도 메뉴 없이 붙인다, PathChaserMenuItem.cs:147). 위치 = 목적지 주점 [추정 — 초안 미지정,
    /// 콜라이더가 없어 동작과 무관]. OnThresholdReached·OnWarning은 손대지 않는다(영구 배선 0 — 브리지가 실행 중에 붙인다).</summary>
    private static SectionHitCounter BuildCounter(Transform group, string name, int threshold, SectionSafePoint destination, Vector3 at)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(group, false);           // S4_Respawn 직속(PTF-2 :3076)
        go.transform.localPosition = at;
        SectionHitCounter c = go.AddComponent<SectionHitCounter>();
        c.hitsBeforeRespawn = threshold;
        if (destination != null) c.destination = destination;
        else Debug.LogError($"{Tag} {name}: 목적지 안전점이 없다 — destination 비움(피격 시 복귀 안 함).", go);
        EditorUtility.SetDirty(c);
        return c;
    }

    /// <summary>[계약 C1-5·초안 §4-4] 안전점 counter = 같은 위치의 낙석 카운터. A안이라 트리거가 없어 수동 복귀 리셋은 꺼진 채다(SectionSafePoint.cs:18-21).</summary>
    private static void LinkSafePointCounter(SectionSafePoint point, SectionHitCounter rockCounter)
    {
        if (point == null) return;
        if (rockCounter == null) { Debug.LogError($"{Tag} {point.name}.counter에 넣을 낙석 카운터가 없다.", point); return; }
        point.counter = rockCounter;
        EditorUtility.SetDirty(point);
    }

    /// <summary>팀 RespawnZone(공용 체크포인트). 폭만 8 [판정 8], 높이 18·깊이 6·center (0,9,0)은 팀 생성 그대로.
    /// 막대(`Pole` 자식)는 스폰 슬롯에서 수평 ≥1.0이 되게 CpPoleLocal로 옮긴다 [2차판정 18] — S3_Wiring.BuildCheckpoint(:468-469) 방식.</summary>
    private static RespawnZone BuildCheckpoint(Transform group)
    {
        GameObject root = CaptureNewRoot(() => { RespawnMenuItem.CreateCheckpoint(); return null; },
                                         "Tools/Respawn/Create Checkpoint Pole", typeof(RespawnZone));
        if (root == null) return null;
        root.transform.SetParent(group, false);
        root.name = CpStartName;
        root.transform.localPosition = CpStartLocal;
        root.transform.localRotation = Quaternion.identity;   // 팀 전제: 구역은 축 정렬(RespawnZone.cs:185-186)
        root.transform.localScale = Vector3.one;
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null) Debug.LogError($"{Tag} {CpStartName}: 팀 체크포인트에 BoxCollider가 없다.", root);
        else
        {
            box.size = new Vector3(CpWidthX, box.size.y, box.size.z);   // S5_Wiring.BuildCheckpoint 방식
            EditorUtility.SetDirty(box);
        }
        Transform pole = root.transform.Find(PoleChildName);
        if (pole == null) Debug.LogError($"{Tag} {CpStartName}: 팀 막대 자식 '{PoleChildName}'을 찾지 못했다 — 막대를 옮기지 못함 [2차판정 18].", root);
        else
        {
            if ((pole.localPosition - CpPoleTeam).sqrMagnitude > PoseTol * PoseTol)
                Debug.LogWarning($"{Tag} {CpStartName}: 팀 생성 막대 로컬 {V(pole.localPosition)} ≠ 예상 {V(CpPoleTeam)} — 그래도 {V(CpPoleLocal)}로 옮긴다.", root);
            pole.localPosition = CpPoleLocal;   // 위치만(회전·자식 PoleMesh/Flag는 팀 생성 그대로)
            EditorUtility.SetDirty(pole);
        }
        EditorUtility.SetDirty(root);
        return root.GetComponent<RespawnZone>();
    }

    // ───────────────────────── ③ 배선 ─────────────────────────

    private sealed class WireRecord
    {
        public readonly string Name; public readonly UnityEvent<GameObject> Evt; public readonly Cnt Counter; public readonly Object Owner;
        public WireRecord(string name, UnityEvent<GameObject> evt, Cnt counter, Object owner) { Name = name; Evt = evt; Counter = counter; Owner = owner; }
    }

    /// <summary>낙석 OnHitThresholdExceeded(UnityEvent&lt;GameObject&gt;, FallingRockSpawner.cs:139) → 이름으로 찾은 초안 행의 Rock 카운터.</summary>
    private static void WireRocks(List<FallingRockSpawner> rocks, SectionHitCounter[] counters, List<WireRecord> wires)
    {
        foreach (FallingRockSpawner r in rocks)
        {
            if (r == null) { Debug.LogError($"{Tag} 낙석 목록에 null 항목."); continue; }
            RockSpec s = System.Array.Find(RockSpecs, x => x.Name == r.name);
            if (s == null) { Debug.LogError($"{Tag} 낙석 '{r.name}'가 초안 §4-2 이름이 아니다 — 배선 대상 카운터를 정하지 못했다.", r); continue; }
            if (r.OnHitThresholdExceeded == null) r.OnHitThresholdExceeded = new FallingRockSpawner.PlayerHitEvent();   // 이벤트가 비었을 때만(S5 방식)
            Connect(s.Name, r.OnHitThresholdExceeded, s.Counter, r, counters, wires);
        }
    }

    /// <summary>FixedPeriodicLaser에는 이벤트가 없다 → `beam`(같은 루트의 LaserBeam, SecurityLaserMenuItem.cs:32)의 OnHazardHit(LaserBeam.cs:37).</summary>
    private static void WireFixed(List<FixedPeriodicLaser> lasers, SectionHitCounter[] counters, List<WireRecord> wires)
    {
        foreach (FixedPeriodicLaser l in lasers)
        {
            if (l == null) { Debug.LogError($"{Tag} 고정 레이저 목록에 null 항목."); continue; }
            FixedSpec s = System.Array.Find(FixedSpecs, x => x.Name == l.name);
            if (s == null) { Debug.LogError($"{Tag} 고정 레이저 '{l.name}'가 초안 §4-3 이름이 아니다 — 배선하지 않는다.", l); continue; }
            LaserBeam beam = OwnBeam(l, l.beam);
            if (beam == null) continue;
            if (beam.OnHazardHit == null) beam.OnHazardHit = new LaserBeam.PlayerHitEvent();
            Connect(s.Name, beam.OnHazardHit, s.Counter, beam, counters, wires);
        }
    }

    /// <summary>CharacterLockedLaser에도 이벤트가 없다(공개 필드 CharacterLockedLaser.cs:18-44) → `beam`(SecurityLaserMenuItem.cs:19-20)의 OnHazardHit.</summary>
    private static void WireAim(List<CharacterLockedLaser> lasers, SectionHitCounter[] counters, List<WireRecord> wires)
    {
        foreach (CharacterLockedLaser l in lasers)
        {
            if (l == null) { Debug.LogError($"{Tag} 조준 레이저 목록에 null 항목."); continue; }
            AimSpec s = System.Array.Find(AimSpecs, x => x.Name == l.name);
            if (s == null) { Debug.LogError($"{Tag} 조준 레이저 '{l.name}'가 초안 §4-3 이름이 아니다 — 배선하지 않는다.", l); continue; }
            LaserBeam beam = OwnBeam(l, l.beam);
            if (beam == null) continue;
            if (beam.OnHazardHit == null) beam.OnHazardHit = new LaserBeam.PlayerHitEvent();
            Connect(s.Name, beam.OnHazardHit, s.Counter, beam, counters, wires);
        }
    }

    /// <summary>모드 컴포넌트의 beam이 같은 GameObject의 LaserBeam인지 확인(팀 메뉴 구조). 아니면 LogError·null(배선 안 함).</summary>
    private static LaserBeam OwnBeam(Component mode, LaserBeam beam)
    {
        if (beam == null) { Debug.LogError($"{Tag} '{mode.name}'.beam이 비어 있다 — 피격 이벤트 소유자가 없어 배선하지 않는다.", mode); return null; }
        if (beam.gameObject != mode.gameObject)
        {
            Debug.LogError($"{Tag} '{mode.name}'.beam이 다른 오브젝트 '{beam.name}'의 LaserBeam이다 — 팀 메뉴 구조와 달라 배선하지 않는다.", mode);
            return null;
        }
        return beam;
    }

    private static void Connect(string name, UnityEvent<GameObject> evt, Cnt which, Object owner, SectionHitCounter[] counters, List<WireRecord> wires)
    {
        SectionHitCounter counter = counters[(int)which];
        if (counter == null) { Debug.LogError($"{Tag} '{name}' → {CounterNames[(int)which]}: 카운터가 없어 배선하지 않는다.", owner); return; }
        if (((Component)owner).gameObject.scene != counter.gameObject.scene)
        {
            Debug.LogError($"{Tag} '{name}'가 카운터와 다른 씬이다 — 영구 배선 불가.", owner);
            return;
        }
        AddHitListener(evt, counter, owner);
        wires.Add(new WireRecord(name, evt, which, owner));
    }

    /// <summary>같은 이벤트에 남은 RegisterHitEvent 배선(재실행·사라진 옛 카운터)을 먼저 걷어 중복 발화를 막고 1개를 건다(S5·S3_Wiring과 같음).
    /// 이 파일의 AddPersistentListener 호출 지점은 여기 1곳이다(대상 = 초안 배선표 13행의 이벤트뿐).</summary>
    private static void AddHitListener(UnityEvent<GameObject> evt, SectionHitCounter counter, Object owner)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            Object target = evt.GetPersistentTarget(i);
            if (evt.GetPersistentMethodName(i) == nameof(SectionHitCounter.RegisterHitEvent) && (target == null || target is SectionHitCounter))
                UnityEventTools.RemovePersistentListener(evt, i);
        }
        UnityEventTools.AddPersistentListener(evt, new UnityAction<GameObject>(counter.RegisterHitEvent));
        EditorUtility.SetDirty(owner);
    }

    /// <summary>배선 뒤 확인: 이벤트마다 RegisterHitEvent 영구 배선이 정확히 1개이고 대상 = 초안 행의 카운터. 카운터별 개수 = 2·5·1·5.
    /// 반환 = 확인에 통과한 배선 수.</summary>
    private static int VerifyWiring(List<WireRecord> wires, SectionHitCounter[] counters)
    {
        int ok = 0;
        int[] perCounter = new int[CounterNames.Length];
        foreach (WireRecord w in wires)
        {
            SectionHitCounter expect = counters[(int)w.Counter];
            int hits = 0; bool rightTarget = true;
            for (int i = 0; i < w.Evt.GetPersistentEventCount(); i++)
            {
                if (w.Evt.GetPersistentMethodName(i) != nameof(SectionHitCounter.RegisterHitEvent)) continue;
                hits++;
                if (w.Evt.GetPersistentTarget(i) != expect) rightTarget = false;
            }
            if (hits == 1 && rightTarget) { ok++; perCounter[(int)w.Counter]++; }
            else Debug.LogError($"{Tag} '{w.Name}' 영구 배선 확인 실패 — RegisterHitEvent {hits}개, 대상 일치 {rightTarget}(기대 {CounterNames[(int)w.Counter]}).", w.Owner);
        }
        int[] expectPer = new int[CounterNames.Length];
        foreach (RockSpec s in RockSpecs) expectPer[(int)s.Counter]++;
        foreach (FixedSpec s in FixedSpecs) expectPer[(int)s.Counter]++;
        foreach (AimSpec s in AimSpecs) expectPer[(int)s.Counter]++;
        for (int k = 0; k < CounterNames.Length; k++)
            if (perCounter[k] != expectPer[k])
                Debug.LogError($"{Tag} {CounterNames[k]} 영구 배선 {perCounter[k]}개 — 초안 배선표는 {expectPer[k]}개.", counters[k]);
        return ok;
    }

    /// <summary>카운터 OnThresholdReached·OnWarning 영구 배선 0(브리지 :34 건너뜀 방지, §7-15).</summary>
    private static void VerifyCounterEvents(SectionHitCounter[] counters)
    {
        foreach (SectionHitCounter c in counters)
        {
            if (c == null) continue;
            int t = c.OnThresholdReached != null ? c.OnThresholdReached.GetPersistentEventCount() : 0;
            int w = c.OnWarning != null ? c.OnWarning.GetPersistentEventCount() : 0;
            if (t > 0) Debug.LogError($"{Tag} {c.name}.OnThresholdReached 영구 배선 {t}개 — Lab_SectionRespawnBridge가 이 카운터를 건너뛴다(계약: 0).", c);
            if (w > 0) Debug.LogError($"{Tag} {c.name}.OnWarning 영구 배선 {w}개 — 초안 §7-15는 배선하지 않는다.", c);
        }
    }

    // ───────────────────────── 확인(값은 쓰지 않는다) ─────────────────────────

    private static int VerifyBubble(Transform generated, ZeroGravityBubble b)
    {
        if (b == null) { Debug.LogError($"{Tag} {BubbleName} 없음(계약 C3).", generated); return 1; }
        List<string> d = new List<string>();
        if (b.name != BubbleName) d.Add($"이름 ≠ {BubbleName}");
        if (b.transform.parent == null || b.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}(PTF-2 :3041)");
        CheckPose(generated, b.transform, BubbleOrigin, Quaternion.identity, d);
        Collider[] cols = b.GetComponents<Collider>();
        BoxCollider box = b.GetComponent<BoxCollider>();
        if (cols.Length != 1 || box == null) d.Add($"콜라이더 {cols.Length}개(BoxCollider 1개여야)");
        else
        {
            if (!box.isTrigger) d.Add("isTrigger false");
            Bounds lb = BoxLocalBounds(generated, box);
            if (!BoundsNear(lb, BubbleTriggerDraft, PoseTol * 10f)) d.Add($"트리거 min{V(lb.min)} max{V(lb.max)} ≠ 초안 min{V(BubbleTriggerDraft.min)} max{V(BubbleTriggerDraft.max)}");
        }
        if (b.GetComponent<Rigidbody>() != null) d.Add("Rigidbody 있음");
        if (!Near(b.upliftAcceleration, BubbleUplift)) d.Add($"upliftAcceleration {b.upliftAcceleration}≠{BubbleUplift}");
        if (!Near(b.maxRiseHeight, BubbleMaxRise)) d.Add($"maxRiseHeight {b.maxRiseHeight}≠{BubbleMaxRise}");
        if (!Near(b.sphereGravityScale, BubbleSphereTeam) || !Near(b.tetrahedronGravityScale, BubbleTetraTeam) || !Near(b.cubeGravityScale, BubbleCubeTeam))
            d.Add($"도형 배율 {b.sphereGravityScale}/{b.tetrahedronGravityScale}/{b.cubeGravityScale} ≠ 팀 기본 0.25/0.16/0.6");
        if (!Near(b.jumpHeightMultiplier, BubbleJumpMulTeam)) d.Add($"jumpHeightMultiplier {b.jumpHeightMultiplier}≠{BubbleJumpMulTeam}");
        if (!Near(b.dragMultiplier, BubbleDragMulTeam)) d.Add($"dragMultiplier {b.dragMultiplier}≠{BubbleDragMulTeam}");
        if (!Near(b.transitionTime, BubbleTransitionTeam)) d.Add($"transitionTime {b.transitionTime}≠{BubbleTransitionTeam}");
        if (!Near(b.exitTransitionTime, BubbleExitTransitionTeam)) d.Add($"exitTransitionTime {b.exitTransitionTime}≠{BubbleExitTransitionTeam}");
        if (b.playerTag != PlayerTagTeam) d.Add($"playerTag '{b.playerTag}'≠'{PlayerTagTeam}'");
        return Report(d, BubbleName, "판정 18 ②·초안 §4-1", b);
    }

    private static int VerifyRocks(Transform generated, List<FallingRockSpawner> rocks)
    {
        int bad = 0;
        if (rocks.Count != RockSpecs.Length) { bad++; Debug.LogError($"{Tag} 낙석 {rocks.Count}개 — 계약 C1-1은 {RockSpecs.Length}개.", generated); }
        List<Vector3> lanesXZ = new List<Vector3>();
        for (int i = 0; i < rocks.Count; i++)
        {
            FallingRockSpawner r = rocks[i];
            if (r == null) { bad++; continue; }
            List<string> d = new List<string>();
            if (rocks.Count == RockSpecs.Length && r.name != RockSpecs[i].Name) d.Add($"순서[{i}] 이름 ≠ {RockSpecs[i].Name}(C1-1)");
            RockSpec s = System.Array.Find(RockSpecs, x => x.Name == r.name);
            if (s == null) d.Add("초안 §4-2 이름 아님");
            if (r.transform.parent == null || r.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}");
            if (r.spawnPositions == null || r.spawnPositions.Length != CurtainLaneCount) d.Add("spawnPositions ≠ 2");
            else if (s != null)
            {
                Vector3[] want = { s.Lane1, s.Lane2 };
                for (int k = 0; k < CurtainLaneCount; k++)
                {
                    if (r.spawnPositions[k] == null) { d.Add($"레인[{k}] null"); continue; }
                    Vector3 lp = generated.InverseTransformPoint(r.spawnPositions[k].position);
                    if ((lp - want[k]).sqrMagnitude > PoseTol * PoseTol) d.Add($"레인[{k}] {V(lp)} ≠ 초안 {V(want[k])}");
                    lanesXZ.Add(new Vector3(lp.x, 0f, lp.z));
                }
                CheckPose(generated, r.transform, (s.Lane1 + s.Lane2) * 0.5f, Quaternion.identity, d);
                if (!Near(r.despawnFallDistance, s.Despawn)) d.Add($"despawnFallDistance {r.despawnFallDistance}≠{s.Despawn}");
            }
            if (r.hitsBeforeRespawn != RockHitsBeforeRespawn) d.Add($"hitsBeforeRespawn {r.hitsBeforeRespawn}≠{RockHitsBeforeRespawn}");
            if (r.referenceSlowZone != null) d.Add("referenceSlowZone ≠ null");
            if (!Near(r.spawnInterval, RockSpawnIntervalTeam)) d.Add($"spawnInterval {r.spawnInterval}≠{RockSpawnIntervalTeam}");
            if (!Near(r.rockSize, RockSizeTeam)) d.Add($"rockSize {r.rockSize}≠{RockSizeTeam}");
            if (!Near(r.gapWidth, RockGapTeam)) d.Add($"gapWidth {r.gapWidth}≠{RockGapTeam}");
            if (!r.keepLane) d.Add("keepLane false≠true");
            bad += Report(d, r.name, "초안 §4-2", r);
        }
        // [계산] 모든 레인 쌍 XZ 간격 ≥ 3(팀 Validate는 인접 쌍만 본다 §7-22)
        for (int i = 0; i < lanesXZ.Count; i++)
            for (int j = i + 1; j < lanesXZ.Count; j++)
                if (Vector3.Distance(lanesXZ[i], lanesXZ[j]) < RockLaneMinXZ - 0.01f)
                { bad++; Debug.LogError($"{Tag} 낙석 레인 XZ 간격 {Vector3.Distance(lanesXZ[i], lanesXZ[j]):0.###} < {RockLaneMinXZ} ({V(lanesXZ[i])}·{V(lanesXZ[j])}).", generated); }
        return bad;
    }

    private static int VerifyFixed(Transform generated, List<FixedPeriodicLaser> lasers)
    {
        int bad = 0;
        if (lasers.Count != FixedSpecs.Length) { bad++; Debug.LogError($"{Tag} 고정 레이저 {lasers.Count}개 — 계약 C1-1은 {FixedSpecs.Length}개.", generated); }
        for (int i = 0; i < lasers.Count; i++)
        {
            FixedPeriodicLaser l = lasers[i];
            if (l == null) { bad++; continue; }
            List<string> d = new List<string>();
            if (lasers.Count == FixedSpecs.Length && l.name != FixedSpecs[i].Name) d.Add($"순서[{i}] 이름 ≠ {FixedSpecs[i].Name}(C1-1)");
            FixedSpec s = System.Array.Find(FixedSpecs, x => x.Name == l.name);
            if (s == null) d.Add("초안 §4-3 이름 아님");
            else CheckPose(generated, l.transform, s.Pos, Quaternion.Euler(s.RotX, s.RotY, 0f), d);
            if (l.transform.parent == null || l.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}");
            if (l.beam == null || l.beam.gameObject != l.gameObject) d.Add("beam ≠ 같은 루트의 LaserBeam");
            else if (s != null && !Near(l.beam.range, s.Range)) d.Add($"beam.range {l.beam.range}≠{s.Range}");
            if (!Near(l.warningSeconds, FixedWarningTeam)) d.Add($"warningSeconds {l.warningSeconds}≠{FixedWarningTeam}");
            if (!Near(l.beamSeconds, FixedBeamTeam)) d.Add($"beamSeconds {l.beamSeconds}≠{FixedBeamTeam}");
            if (!Near(l.restSeconds, FixedRestTeam)) d.Add($"restSeconds {l.restSeconds}≠{FixedRestTeam}");
            if (!l.autoActivateOnStart) d.Add("autoActivateOnStart false≠true");
            bad += Report(d, l.name, "초안 §4-3", l);
        }
        return bad;
    }

    private static int VerifyAim(Transform generated, List<CharacterLockedLaser> lasers)
    {
        int bad = 0;
        if (lasers.Count != AimSpecs.Length) { bad++; Debug.LogError($"{Tag} 조준 레이저 {lasers.Count}개 — 계약 C1-1은 {AimSpecs.Length}개.", generated); }
        for (int i = 0; i < lasers.Count; i++)
        {
            CharacterLockedLaser l = lasers[i];
            if (l == null) { bad++; continue; }
            List<string> d = new List<string>();
            if (lasers.Count == AimSpecs.Length && l.name != AimSpecs[i].Name) d.Add($"순서[{i}] 이름 ≠ {AimSpecs[i].Name}(C1-1)");
            AimSpec s = System.Array.Find(AimSpecs, x => x.Name == l.name);
            if (s == null) d.Add("초안 §4-3 이름 아님");
            else
            {
                CheckPose(generated, l.transform, s.Pos, Quaternion.Euler(s.RotX, s.RotY, 0f), d);
                if (l.targetKinds == null || l.targetKinds.Length != 1 || l.targetKinds[0] != s.Kind) d.Add($"targetKinds ≠ {{{s.Kind}}}");
                if (!Near(l.maxAimAngle, s.MaxAim)) d.Add($"maxAimAngle {l.maxAimAngle}≠{s.MaxAim}");
                if (!Near(l.sensorRange, s.Sensor)) d.Add($"sensorRange {l.sensorRange}≠{s.Sensor}");
                if (l.beam != null && !Near(l.beam.range, s.Range)) d.Add($"beam.range {l.beam.range}≠{s.Range}");
            }
            if (l.transform.parent == null || l.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}");
            if (l.beam == null || l.beam.gameObject != l.gameObject) d.Add("beam ≠ 같은 루트의 LaserBeam");
            if (!Near(l.firstFireDelay, AimFirstFireTeam)) d.Add($"firstFireDelay {l.firstFireDelay}≠{AimFirstFireTeam}");
            if (!Near(l.relockDelay, AimRelockTeam)) d.Add($"relockDelay {l.relockDelay}≠{AimRelockTeam}");
            if (!Near(l.refireDelay, AimRefireTeam)) d.Add($"refireDelay {l.refireDelay}≠{AimRefireTeam}");
            if (!Near(l.beamDuration, AimBeamDurationTeam)) d.Add($"beamDuration {l.beamDuration}≠{AimBeamDurationTeam}");
            bad += Report(d, l.name, "초안 §4-3", l);
        }
        return bad;
    }

    /// <summary>안전점 sectionId·위치·보조 2·counter·Collider 없음, 점 간격 ≥ 반경×2, 6점이 버블 트리거·CP 박스 밖(초안 §13 #2),
    /// 카운터 임계·목적지·쿨다운, CP 원점·박스·막대.</summary>
    private static int VerifyRespawn(Transform generated, Transform group, SectionSafePoint spStairs, SectionSafePoint spBubble, SectionHitCounter[] counters, RespawnZone cp)
    {
        int bad = 0;
        Bounds cpBox = MinMax(new Vector3(CpStartLocal.x - CpSizeExpect.x * 0.5f, CpStartLocal.y + CpCenterExpect.y - CpSizeExpect.y * 0.5f, CpStartLocal.z - CpSizeExpect.z * 0.5f),
                              new Vector3(CpStartLocal.x + CpSizeExpect.x * 0.5f, CpStartLocal.y + CpCenterExpect.y + CpSizeExpect.y * 0.5f, CpStartLocal.z + CpSizeExpect.z * 0.5f));
        bad += VerifySafePoint(generated, spStairs, SpStairsName, SectionIdStairs, SpStairsMain, SpStairsBackups, counters[(int)Cnt.RockStairs], cpBox);
        bad += VerifySafePoint(generated, spBubble, SpBubbleName, SectionIdBubble, SpBubbleMain, SpBubbleBackups, counters[(int)Cnt.RockBubble], cpBox);

        for (int k = 0; k < CounterNames.Length; k++)
        {
            SectionHitCounter c = counters[k];
            if (c == null) { bad++; Debug.LogError($"{Tag} {CounterNames[k]} 없음(계약 C3).", group); continue; }
            List<string> d = new List<string>();
            if (c.name != CounterNames[k]) d.Add($"이름 ≠ {CounterNames[k]}");
            if (c.transform.parent != group) d.Add($"부모 ≠ {GroupName}(PTF-2 :3076)");
            if (c.hitsBeforeRespawn != CounterThresholds[k]) d.Add($"hitsBeforeRespawn {c.hitsBeforeRespawn}≠{CounterThresholds[k]}");
            if (!Near(c.hitCooldown, CounterCooldownTeam)) d.Add($"hitCooldown {c.hitCooldown}≠{CounterCooldownTeam}");
            SectionSafePoint want = CounterIsBubble[k] ? spBubble : spStairs;
            if (c.destination == null || c.destination != want) d.Add($"destination ≠ {(CounterIsBubble[k] ? SpBubbleName : SpStairsName)}");
            bad += Report(d, CounterNames[k], "계약 C1-5·확정 5", c);
        }

        if (cp == null) { bad++; Debug.LogError($"{Tag} {CpStartName} 없음(계약 C3).", group); }
        else
        {
            List<string> d = new List<string>();
            if (cp.transform.parent != group) d.Add($"부모 ≠ {GroupName}");
            CheckPose(generated, cp.transform, CpStartLocal, Quaternion.identity, d);
            BoxCollider box = cp.GetComponent<BoxCollider>();
            if (box == null) d.Add("BoxCollider 없음");
            else
            {
                if (!box.isTrigger) d.Add("isTrigger false");
                if ((box.size - CpSizeExpect).sqrMagnitude > PoseTol * PoseTol) d.Add($"size {V(box.size)} ≠ {V(CpSizeExpect)}");
                if ((box.center - CpCenterExpect).sqrMagnitude > PoseTol * PoseTol) d.Add($"center {V(box.center)} ≠ {V(CpCenterExpect)}");
            }
            Transform pole = cp.transform.Find(PoleChildName);
            if (pole == null) d.Add($"막대 '{PoleChildName}' 없음");
            else
            {
                if ((pole.localPosition - CpPoleLocal).sqrMagnitude > PoseTol * PoseTol) d.Add($"막대 로컬 {V(pole.localPosition)} ≠ {V(CpPoleLocal)}(2차판정 18)");
                // 막대 발(섹터 로컬 XZ) ↔ 스폰 슬롯 3점 수평 최솟값 ≥ 1.0, 막대가 CP 박스 발자국 안
                Vector3 foot = generated.InverseTransformPoint(pole.position);
                float minSlot = float.MaxValue;
                foreach (Vector2 s in SpawnSlotsXZ) minSlot = Mathf.Min(minSlot, Vector2.Distance(new Vector2(foot.x, foot.z), s));
                if (minSlot < PoleSlotMin - 1e-4f) d.Add($"막대 발 {V(foot)} ↔ 스폰 슬롯 수평 최소 {minSlot:0.###} < {PoleSlotMin}(2차판정 18)");
                if (Mathf.Abs(foot.x - CpStartLocal.x) > CpSizeExpect.x * 0.5f || Mathf.Abs(foot.z - CpStartLocal.z) > CpSizeExpect.z * 0.5f)
                    d.Add($"막대 발 {V(foot)}가 {CpStartName} 박스 발자국 밖");
            }
            bad += Report(d, CpStartName, "초안 §4-4·판정 8·2차판정 18", cp);
        }
        return bad;
    }

    private static int VerifySafePoint(Transform generated, SectionSafePoint p, string name, string sectionId, Vector3 main, Vector3[] backups, SectionHitCounter rockCounter, Bounds cpBox)
    {
        if (p == null) { Debug.LogError($"{Tag} {name} 없음(계약 C3).", generated); return 1; }
        List<string> d = new List<string>();
        if (p.name != name) d.Add($"이름 ≠ {name}");
        if (p.transform.parent == null || p.transform.parent.name != GroupName) d.Add($"부모 ≠ {GroupName}(PTF-2 :3076)");
        if (p.sectionId != sectionId) d.Add($"sectionId '{p.sectionId}' ≠ '{sectionId}'");
        if (!Near(p.occupancyRadius, OccupancyRadiusTeam)) d.Add($"occupancyRadius {p.occupancyRadius}≠{OccupancyRadiusTeam}");
        if (p.GetComponent<Collider>() != null) d.Add("Collider 있음(A안 위반)");
        if (p.counter != rockCounter || p.counter == null) d.Add("counter ≠ 같은 위치의 낙석 카운터");
        List<Vector3> pts = new List<Vector3>();
        Vector3 mp = generated.InverseTransformPoint(p.transform.position);
        if ((mp - Lift(main)).sqrMagnitude > PoseTol * PoseTol) d.Add($"주점 {V(mp)} ≠ 바닥 윗면 +{SafePointLift} {V(Lift(main))}(2차판정 15)");
        pts.Add(mp);
        if (p.backupPoints == null || p.backupPoints.Length != backups.Length) d.Add($"보조점 {(p.backupPoints == null ? 0 : p.backupPoints.Length)}개 ≠ {backups.Length}");
        else
            for (int i = 0; i < backups.Length; i++)
            {
                Transform b = p.backupPoints[i];
                if (b == null) { d.Add($"보조점[{i}] null"); continue; }
                if (b.name != $"{name}_Backup{i}") d.Add($"보조점[{i}] 이름 '{b.name}'");
                Vector3 bp = generated.InverseTransformPoint(b.position);
                if ((bp - Lift(backups[i])).sqrMagnitude > PoseTol * PoseTol) d.Add($"보조점[{i}] {V(bp)} ≠ 바닥 윗면 +{SafePointLift} {V(Lift(backups[i]))}(2차판정 15)");
                pts.Add(bp);
            }
        for (int i = 0; i < pts.Count; i++)
        {
            for (int j = i + 1; j < pts.Count; j++)
                if (Vector3.Distance(pts[i], pts[j]) < OccupancyRadiusTeam * 2f - 1e-4f)
                    d.Add($"점 간격 {Vector3.Distance(pts[i], pts[j]):0.###} < {OccupancyRadiusTeam * 2f}");
            // [초안 §13 #2·§4-4] 복귀점(점유 반경 × 몸 높이 1)이 어떤 트리거(버블·CP)에도 걸치지 않는다.
            if (Overlaps(pts[i], BubbleTriggerDraft)) d.Add($"점 {V(pts[i])}가 버블 트리거에 걸친다");
            if (Overlaps(pts[i], cpBox)) d.Add($"점 {V(pts[i])}가 {CpStartName} 박스에 걸친다");
        }
        return Report(d, name, "초안 §3-2·§4-4·판정 13", p);
    }

    /// <summary>배치점(바닥 윗면 +0.1)에서 반경 0.6 기둥이 박스와 겹치는가. 세로는 바닥 윗면(점 −0.1)부터 점 + 몸 높이 1까지
    /// (+0.1 틈까지 포함해 보수적), XZ는 박스를 반경만큼 부풀려 본다.</summary>
    private static bool Overlaps(Vector3 placedPoint, Bounds box)
    {
        float r = OccupancyRadiusTeam;
        return placedPoint.x > box.min.x - r && placedPoint.x < box.max.x + r &&
               placedPoint.z > box.min.z - r && placedPoint.z < box.max.z + r &&
               placedPoint.y + ShapeHeight > box.min.y && placedPoint.y - SafePointLift < box.max.y;
    }

    /// <summary>바닥 윗면 값 → 안전점 배치 값(+0.1) [2차판정 15]. 한 번만 부른다(Refs·초안 상수는 바닥 윗면 그대로).</summary>
    private static Vector3 Lift(Vector3 floorTop) => new Vector3(floorTop.x, floorTop.y + SafePointLift, floorTop.z);

    /// <summary>계약 C3·지시서 완료 조건: generated 아래 S4_Gimmicks·S4_Respawn 각 1개, 기믹 이름·개수(1·3·4·6), 복귀 C3 이름 8개.</summary>
    private static void VerifyGroups(Transform generated, Transform gimmicks, Transform group)
    {
        int gim = 0, res = 0;
        for (int i = 0; i < generated.childCount; i++)
        {
            string n = generated.GetChild(i).name;
            if (n == GimmickGroupName) gim++;
            else if (n == GroupName) res++;
        }
        if (res != 1) Debug.LogError($"{Tag} generated 아래 '{GroupName}' {res}개 — 1개여야 한다.", generated);
        if (gimmicks != null && gimmicks.parent == generated && gim != 1) Debug.LogError($"{Tag} generated 아래 '{GimmickGroupName}' {gim}개 — 1개여야 한다.", generated);
        if (gimmicks != null)
        {
            List<string> missing = new List<string>();
            if (!HasChild<ZeroGravityBubble>(gimmicks, BubbleName)) missing.Add(BubbleName);
            foreach (RockSpec s in RockSpecs) if (!HasChild<FallingRockSpawner>(gimmicks, s.Name)) missing.Add(s.Name);
            foreach (FixedSpec s in FixedSpecs) if (!HasChild<FixedPeriodicLaser>(gimmicks, s.Name)) missing.Add(s.Name);
            foreach (AimSpec s in AimSpecs) if (!HasChild<CharacterLockedLaser>(gimmicks, s.Name)) missing.Add(s.Name);
            if (missing.Count > 0) Debug.LogError($"{Tag} '{gimmicks.name}' 아래 없음(계약 C3): {string.Join(", ", missing)}", gimmicks);
        }
        List<string> missingR = new List<string>();
        if (!HasChild<SectionSafePoint>(group, SpStairsName)) missingR.Add(SpStairsName);
        if (!HasChild<SectionSafePoint>(group, SpBubbleName)) missingR.Add(SpBubbleName);
        foreach (string n in CounterNames) if (!HasChild<SectionHitCounter>(group, n)) missingR.Add(n);
        if (!HasChild<RespawnZone>(group, CpStartName)) missingR.Add(CpStartName);
        if (!HasChild<Lab_SectionRespawnBridge>(group, BridgeAdapterName)) missingR.Add(BridgeAdapterName);
        if (missingR.Count > 0) Debug.LogError($"{Tag} '{GroupName}' 아래 없음(계약 C3): {string.Join(", ", missingR)}", group);
    }

    private static bool HasChild<T>(Transform parent, string name) where T : Component
    {
        int n = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name && c.GetComponent<T>() != null) n++;
        }
        if (n > 1) Debug.LogError($"{Tag} '{parent.name}/{name}'가 {n}개 — 1개여야 한다.", parent);
        return n == 1;
    }

    /// <summary>1차 C3: 섹터 씬 루트는 섹터 루트 1개뿐이어야 한다(팀 메뉴 생성물 잔류 0). 확인만 — 지우는 것은 CaptureNewRoot가 한다.</summary>
    private static void VerifyNoStrayRoots(Transform generated)
    {
        GameObject sectorRoot = generated.root.gameObject;
        foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
            if (r != sectorRoot) Debug.LogError($"{Tag} 씬 루트에 남은 오브젝트 '{r.name}'(씬 루트 잔류 0 위반).", r);
    }

    private static int Report(List<string> d, string name, string basis, Object ctx)
    {
        if (d.Count == 0) return 0;
        Debug.LogError($"{Tag} '{name}' 불일치 [{basis}] — 고치지 않음: {string.Join(", ", d)}", ctx);
        return 1;
    }

    /// <summary>[계약 지시서 3] refs 값이 기본값이 아닌데 초안과 0.01 넘게 다르면 LogError만(대입 금지 — 좌표는 초안 상수로 쓴다).</summary>
    private static void CheckRefsAgainstDraft(Transform generated, S4_Refs refs)
    {
        CheckRefVec("spStairsLocal", refs.spStairsLocal, SpStairsMain, generated);
        CheckRefVec("spBubbleLocal", refs.spBubbleLocal, SpBubbleMain, generated);
        CheckRefArr("spStairsBackups", refs.spStairsBackups, SpStairsBackups, generated);
        CheckRefArr("spBubbleBackups", refs.spBubbleBackups, SpBubbleBackups, generated);
        CheckRefVec("checkpointStartLocal", refs.checkpointStartLocal, CpStartLocal, generated);
        Bounds bt = refs.bubbleTrigger;
        if (bt.size.sqrMagnitude > 1e-8f && !BoundsNear(bt, BubbleTriggerDraft, RefsTol))
            Debug.LogError($"{Tag} refs.bubbleTrigger min{V(bt.min)} max{V(bt.max)} ≠ 초안 min{V(BubbleTriggerDraft.min)} max{V(BubbleTriggerDraft.max)} — 초안 값으로 만든다(S4-B·초안 확인).", generated);
    }

    private static void CheckRefVec(string field, Vector3 v, Vector3 draft, Transform ctx)
    {
        if (v == Vector3.zero) return;   // 기본값(빌더 미기입) — 대조하지 않는다
        if ((v - draft).sqrMagnitude > RefsTol * RefsTol)
            Debug.LogError($"{Tag} refs.{field} {V(v)} ≠ 초안 {V(draft)} — 초안 값으로 만든다(S4-B·초안 확인).", ctx);
    }

    private static void CheckRefArr(string field, Vector3[] arr, Vector3[] draft, Transform ctx)
    {
        if (arr == null) return;
        if (arr.Length != draft.Length) { Debug.LogError($"{Tag} refs.{field} 길이 {arr.Length} ≠ {draft.Length}.", ctx); return; }
        for (int i = 0; i < arr.Length; i++) CheckRefVec($"{field}[{i}]", arr[i], draft[i], ctx);
    }

    // ───────────────────────── 팀 생성 함수·메뉴 ─────────────────────────

    /// <summary>팀 생성 함수(public/internal static은 직접, private 메뉴는 ExecuteMenuItem)를 실행하고 새 씬 루트를 받는다.
    /// S5_Builder.MenuCreate 방식: 반환값 → Selection → 실행 전후 루트 비교(mustHave 컴포넌트) 순. 이번 호출로 생긴 루트 중 받은 것 말고는
    /// 지운다(씬 루트 잔류 0, 1차 C3). 받은 루트는 호출자가 곧바로 그룹 아래로 옮기거나, 검사 실패면 지운다.</summary>
    private static GameObject CaptureNewRoot(System.Func<GameObject> create, string label, System.Type mustHave)
    {
        HashSet<GameObject> before = AllRoots();
        Selection.activeGameObject = null;
        GameObject returned = null, sel = null;
        bool threw = false;
        try
        {
            returned = create();
            sel = Selection.activeGameObject;
        }
        catch (System.Exception e)
        {
            threw = true;
            Debug.LogError($"{Tag} 팀 생성 '{label}' 실행 실패: {e.Message}");
        }

        List<GameObject> fresh = new List<GameObject>();
        foreach (GameObject r in AllRoots()) if (!before.Contains(r)) fresh.Add(r);
        GameObject made = null;
        if (!threw)
        {
            if (returned != null && fresh.Contains(returned)) made = returned;
            else if (sel != null && fresh.Contains(sel)) made = sel;
            else
                foreach (GameObject r in fresh)
                    if (mustHave == null || r.GetComponent(mustHave) != null) { made = r; break; }
        }
        foreach (GameObject r in fresh)
        {
            if (r == null || r == made) continue;
            Debug.LogError($"{Tag} 팀 생성 '{label}' 뒤 남은 씬 루트 '{r.name}' — 지운다(씬 루트 잔류 0).");
            Object.DestroyImmediate(r);
        }
        if (made == null && !threw) Debug.LogError($"{Tag} 팀 생성 '{label}' 생성물({(mustHave != null ? mustHave.Name : "?")})을 찾지 못했다.");
        return made;
    }

    private static HashSet<GameObject> AllRoots()
    {
        HashSet<GameObject> set = new HashSet<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) foreach (GameObject r in s.GetRootGameObjects()) set.Add(r);
        }
        return set;
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>섹터 로컬 위치·회전으로 놓는다(Generated의 세계 변환을 곱한다 — S5_Builder.Place와 같음).</summary>
    private static void Place(Transform generated, Transform t, Vector3 local, Quaternion localRot) =>
        t.SetPositionAndRotation(generated.TransformPoint(local), generated.rotation * localRot);

    /// <summary>섹터 로컬 위치(±0.001)·회전(±0.1°) 대조 — 다르면 d에 적는다.</summary>
    private static void CheckPose(Transform generated, Transform t, Vector3 local, Quaternion localRot, List<string> d)
    {
        Vector3 lp = generated.InverseTransformPoint(t.position);
        if ((lp - local).sqrMagnitude > PoseTol * PoseTol) d.Add($"위치(섹터 로컬) {V(lp)} ≠ 초안 {V(local)}");
        Quaternion lr = Quaternion.Inverse(generated.rotation) * t.rotation;
        float ang = Quaternion.Angle(lr, localRot);
        if (ang > RotTolDeg) d.Add($"회전(섹터 로컬) {V(lr.eulerAngles)} ≠ {V(localRot.eulerAngles)} [{ang:0.##}°]");
    }

    /// <summary>팀 RespawnZone은 축 정렬 배치를 전제한다(RespawnZone.cs:185-186) — Generated가 y축 90° 배수 회전이 아니면 경고.</summary>
    private static void CheckAxisAligned(Transform generated)
    {
        Vector3 e = generated.rotation.eulerAngles;
        bool ok = Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) < 0.01f &&
                  Mathf.Abs(Mathf.DeltaAngle(e.y, Mathf.Round(e.y / 90f) * 90f)) < 0.01f;
        if (!ok) Debug.LogWarning($"{Tag} Generated 회전 {e}가 축 정렬이 아니다 — 팀 RespawnZone·버블 박스가 기울어진다.", generated);
    }

    /// <summary>BoxCollider 박스(center·size, 자기 변환) → generated 로컬 AABB. Collider.bounds(물리 동기화 필요)를 쓰지 않고 꼭짓점 8개를 옮긴다.</summary>
    private static Bounds BoxLocalBounds(Transform generated, BoxCollider box)
    {
        Transform t = box.transform;
        Vector3 h = box.size * 0.5f;
        Bounds b = new Bounds(generated.InverseTransformPoint(t.TransformPoint(box.center)), Vector3.zero);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Encapsulate(generated.InverseTransformPoint(t.TransformPoint(box.center + new Vector3(sx * h.x, sy * h.y, sz * h.z))));
        return b;
    }

    private static bool BoundsNear(Bounds a, Bounds b, float tol) =>
        (a.min - b.min).sqrMagnitude <= tol * tol && (a.max - b.max).sqrMagnitude <= tol * tol;
    private static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
    private static Bounds MinMax(Vector3 min, Vector3 max) { Bounds b = new Bounds(); b.SetMinMax(min, max); return b; }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-4f;
}
#endif
