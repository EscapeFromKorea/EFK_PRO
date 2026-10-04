#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 섹터5 "외다리 함정(CH5)" 빌더 — 배분 A(상시 주기) — 설계/S5_설계.md(확정 4항목, 09-28)를 섹터 씬 Generated/ 아래에
/// 짓는다. Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry를 거쳐 S5일 때 빈 틀 대신 이것을 부르고, 연결
/// 통로(z 136~148)·마커는 공용 코드(BuildConnectorAndMarkers)가 이어서 만든다. S4 → S5 연결 통로는 S4 씬이 짓는다.
///
/// [10-04 사용자 결정 — 함정 크기 확대 + 숨김 배치 (설계/S5_설계.md §10, 진행/기획보완_2026-10-04/S5_숨은함정_발판발동_검토.md §4·§5-1)]
///   "함정 크기를 키우고, 평소에는 높이 위에 있거나 해서 함정이 있다는 걸 모르게". 이번에 넣은 것은 크기 확대 + 숨김 배치뿐이다.
///   함정은 지금처럼 처음부터 계속 작동한다 — 꺼 두기·SetActive(false)·발판(투명 발판)·Lab_ActivateWhenPlayersReady는 쓰지 않는다
///   (발판 발동은 팀 요청 대기). 팀 컴포넌트는 배치(위치·localScale)와 인스펙터 값(dropDistance·popDistance·속도·반지름)만 바꾼다.
///   · 도끼: localScale (1.5, 2.5, 2), 회전점 1.6 → 3.1 · 망치: 머리 2.2×0.9×2.2, 대기 밑면 2.2 → 12(기본 시점 화면 밖)
///   · 가시: 판 밑 매설 + 발자국 1.6×1.2·높이 1.5(판은 이음새 없는 한 장) · 레이저: 광선 반경 0.15 → 0.3 · 발사구: 탄 반지름 0.3 → 0.4
///   [M1 수정 — 컨트롤타워 판정 (a)] 가시 시각물(팀 정사면체) 꼭짓점까지 판 밑에 묻는다: 피벗 −1.06 → −1.62, popDistance 1.81 → 2.37
///   (수납 시 꼭짓점 −0.321, 돌출 콜라이더 0~1.5 유지).
///
/// [평면 — §2 Z자 확정: 가로 → 대각선 → 가로] 섹터 로컬(+Z 진행, 다리·플랫폼 윗면 y 0 = 세계 18).
///   A 시작 x −30~4 z 0~16 ─ 다리1(x 4→24, z 7~9, 판 고정4·회전4·고정4·회전4·고정4) ─ R1 x 24~30 z 0~20
///   ─ 다리2 (26,20)→(−26,76) 폭 2 대각선(양 끝 플랫폼 안으로 1m) ─ L2 x −30~−22 z 76~92
///   ─ 다리3(x −22→−4, z 83~85) ─ F 도착 x −4~30 z 76~136, 출구 x −4~4 z 136.
///   플랫폼 사이는 허공(깊이 20), 외곽 x ±30 벽 높이 6, 난간 없음.
///
/// [구조물 — §3 배분 A 확정] 다리2: s10 도끼1 · s18 가시1 · s26 망치1 · s36 도끼2 · s44 가시2 · s52 망치2 · s62 도끼3,
///   다리3: s3 고정 레이저1 · s7 측면 발사구1 · s11 레이저2 · s15 발사구2. 모두 팀 생성 메뉴(private이라
///   EditorApplication.ExecuteMenuItem)로 만들고 배치 변환(위치·회전)과 설계서가 준 인스펙터 값(startPhase)만 넣는다.
///   수치는 팀 스크립트 기본값 그대로(§9-3 확정) → 10-04 변경: 위 [10-04 사용자 결정] 크기 값. 팀 코드·팀 private 필드는 건드리지 않는다.
///
/// [복귀 배선] S5_Wiring.Wire(과제 S5-W, 계약 C2)에 함정 11개·회전 다리 2개를 넘긴다 — 마지막 1회.
///
/// [수정 라운드 S5검산1 — 규칙에 맞춘 구현 결정(진행/보고/S5-B.md 끝 절)]
///   ① 다리2 양 끝 1m 삽입: 두께 0.3 본체는 플랫폼 가장자리 바깥(s 0.93 ~ L−0.93)에서 끊고, 1m 삽입부는 두께
///      0.05 얇은 판(GEO_S5_Bridge2_Insert_R1·L2)으로 잇는다 → 관통 깊이 0.05(Map4Audit 0.10 미만), 윗면 0 그대로.
///   ② 가시 0.6 슬롯: 판을 폭 2 전체가 아니라 가시 단면 0.6×0.6만큼만 비운다 — 슬롯 양옆 0.7은 고정 판
///      (GEO_S5_Bridge2_SpikeSide_*)으로 메운다. 열린 0.6×0.7 구멍 0. → 10-04 변경: 가시를 판 밑에 묻어 슬롯·메움판이 사라졌다
///      (다리2 본체는 이음새 없는 한 장 GEO_S5_Bridge2_0).
///   ③ 기둥: 다리3 가장자리에서 면까지 5.0(레이저 z 77.8·발사구 z 90.2, 단면 0.4×0.4) — 설계 z 80·89는 "5m"와
///      함께 맞출 수 없어 "5m"를 따랐다. 레이저1 기둥은 다리2에서도 5.07.
///   ④ 발사구(−Z) 탄(40U)이 다리2까지 가지 않게 탄 받이(GEO_S5_ShotStop_1·2, 모든 다리·플랫폼·기둥에서 5 이상)를 둔다.
///   ⑤ 도끼 startPhase: 팀 단위는 초 → 설계의 위상비(0.5 = 반 주기, 0.25)에 팀 period를 곱해 넣는다.
///
/// 좌표 출처: [확정]=설계서 사용자 확정, [팀]=팀 코드·PRD, [계산]=계산값, [구현 결정]=설계서에 값이 없어 여기서 정함,
/// [추정]=실측 필요. 이름 규칙(S1과 같음): 콜라이더 GEO_S5_…, 시각물 VIS_S5_…, 팀 기믹 S5_….
/// </summary>
public static class S5_Builder
{
    // ── 전제 (Map4Layout / 설계서 근거) ──
    private const float SectorWidth = 60f;     // [확정] Map4Layout S5 폭 60
    private const float SectorLength = 136f;   // [확정] 길이 136
    private const float FloorHeight = 18f;     // [확정] 바닥 높이 18(세계)
    private const float ExitHeight = 18f;      // [확정] 출구 높이 18(평지)
    private const float HalfW = SectorWidth / 2f;

    // ── 플랫폼 (§2) — (xMin, xMax, zMin, zMax), 윗면 y 0 ──
    private static readonly Rect PlatA = Rect.MinMaxRect(-30f, 0f, 4f, 16f);     // [확정] A 시작
    private static readonly Rect PlatR1 = Rect.MinMaxRect(24f, 0f, 30f, 20f);    // [확정] R1 모서리
    private static readonly Rect PlatL2 = Rect.MinMaxRect(-30f, 76f, -22f, 92f); // [확정] L2 모서리
    private static readonly Rect PlatF = Rect.MinMaxRect(-4f, 76f, 30f, 136f);   // [확정] F 도착(출구 x −4~4, z 136)
    private const float VoidDepth = 20f;       // [확정 §2] 허공 깊이 20 — [구현 결정] 플랫폼을 y −20~0 블록으로(S1 절벽 플랫폼과 같은 방식)

    // ── 다리 공통 ──
    private const float BridgeWidth = 2f;      // [확정 §2] 폭 2 = 팀 회전 다리 기본 폭
    private const float PlankThickness = 0.3f; // [구현 결정] 판 두께 0.3 = 팀 회전 다리 지지판 두께(StepRotatingBridgeMenuItem:22)와 같게

    // ── 다리1 (§2·§3) ──
    private const float Bridge1Z0 = 7f, Bridge1Z1 = 9f; // [확정] z 7~9
    // [확정 §3] 판 구성 x 4→24: 고정 4 → 회전 4 → 고정 4 → 회전 4 → 고정 4. true = 회전 판(팀 StepRotatingBridge).
    private static readonly float[] Bridge1Cuts = { 4f, 8f, 12f, 16f, 20f, 24f };
    private static readonly bool[] Bridge1Rotating = { false, true, false, true, false };
    private const float RotBridgeHalfT = 0.15f; // [팀] 지지판 0.3의 절반 — 피벗 = 판 중심 → 피벗 y −0.15면 윗면 0

    // ── 다리2 (§2·§3·§6) ──
    private static readonly Vector2 Bridge2Start = new Vector2(26f, 20f);  // [확정] (x, z)
    private static readonly Vector2 Bridge2End = new Vector2(-26f, 76f);   // [확정] 길이 76.42 [계산]
    private const float Bridge2Insert = 1f;      // [확정 §6] 양 끝을 플랫폼 안으로 1m
    private const float Bridge2Thickness = PlankThickness; // [구현 결정] 본체 두께 = 다른 판과 같은 0.3(플랫폼과 겹치지 않는 구간만)
    // [확정 §6] "겹침은 정적 판정 0.10 미만" → [구현 결정] 플랫폼 속으로 들어가는 1m 삽입부만 두께 0.05 얇은 판.
    // 관통 깊이 = 이 두께(최소 축 y) = 0.05 < Map4Audit 0.10. 윗면은 0 그대로(턱 0).
    private const float Bridge2InsertThickness = 0.05f;
    // [계산] 본체 끝면(폭 2, 다리 방향에 수직)이 플랫폼 가장자리(R1 z 20 / L2 z 76) 바깥에 오는 최소 s:
    // |법선 z성분| / |진행 z성분| = 0.68045 / 0.73279 = 0.92857 → 0.93(끝면 모서리가 가장자리에서 0.001 바깥, 틈은 삽입판이 덮는다).
    private const float Bridge2BodyInset = 0.93f;
    // [10-04 삭제] SpikeSlot 0.6·SpikeHalfWidth 0.3 — 가시를 판 밑에 묻으므로 판을 끊는 슬롯·양옆 메움판(GEO_S5_Bridge2_SpikeSide_*)이 없다.
    // [확정 §3] 다리2 함정 s(시작점 (26,20)부터의 거리)
    private const float AxeS1 = 10f, SpikeS1 = 18f, HammerS1 = 26f, AxeS2 = 36f, SpikeS2 = 44f, HammerS2 = 52f, AxeS3 = 62f;
    // [확정 §3 문구 "반 주기 어긋나게(startPhase 0.5)"·"startPhase 0.25"] 설계의 숫자는 주기에 대한 비율이다. 팀 startPhase는
    // 초(PeriodicTrapBase.cs:26)이므로 넣는 값 = 비율 × 팀 period(AxeTrap 기본 2 [팀]) → 도끼2 1.0초, 도끼3 0.5초 [계산].
    // 10-04: 함정이 처음부터 계속 도는 지금은 이 비율을 그대로 쓴다. 검토서 §5-1 ②의 "전부 0.5초"는 켜질 때 튀지 않게 하는
    // 발판 발동용이라 이번 범위 밖이다(발판 발동이 들어오면 그때 바꾼다).
    private const float Axe2PhaseFraction = 0.5f;
    private const float Axe3PhaseFraction = 0.25f;

    // ═══ [10-04 사용자 결정] 함정 크기 확대 + 숨김 배치 ═══
    // 근거: 진행/기획보완_2026-10-04/S5_숨은함정_발판발동_검토.md §4(크기)·§5-1(규격), 계산 재현 진행/기획보완_2026-10-04/S5_크기확대_검산.py
    //       (S5_숨은함정_계산.py §B~§D와 같은 값, 도끼는 회전 사각형 SAT로 다시 검산). 도형 몸 = 약 1U(공통 규칙 11) [추정: 피벗 = 중심].
    // 팀 기본 콜라이더(PeriodicTrapMenuItem.cs:28-35, AxeTrap·HammerTrap·SpikeTrap의 Reset) — 루트 localScale이 콜라이더와
    // 시각(자식 큐브)을 같이 키운다(검토서 §4-1). 아래 모든 파생값은 이 [팀] 치수 × 배율이다.
    private const float TeamBladeWidthX = 1.0f, TeamBladeCenterY = 1.0f, TeamBladeThickY = 0.2f, TeamBladeThickZ = 0.15f; // 날 1×0.2×0.15, 중심 (0,1,0)
    private const float TeamHandleLenY = 0.8f;                                                       // 손잡이 0.2×0.8×0.2, 중심 (0,0.4,0)
    private const float TeamHeadY = 0.6f;                                                            // 망치 머리 1×0.6×1
    private const float TeamSpikeX = 0.6f, TeamSpikeY = 1.0f, TeamSpikeZ = 0.6f;                    // 가시 0.6×1×0.6
    private const float BodyHeight = 1.0f;       // [확정 공통 규칙 11] 도형 크기 약 1U — 손잡이·망치 통과 판정에 쓴다

    // ── 도끼 (검토서 §4-1·§4-2·§4-3 안1) ──
    // 로컬 X = 날 폭(다리 가로), Y = 팔(날 중심까지 거리·손잡이·날 두께), Z = 다리 방향 두께. 거꾸로 매달려도 로컬 축 기준이다.
    // 날 폭은 1.5 안팎이 한계다(§4-3): 폭 2.5까지 키우면 네모가 날을 지날 안전 창이 필요 시간과 0.05초 차이로 사실상 막힌다.
    private const float AxeScaleX = 1.5f, AxeScaleY = 2.5f, AxeScaleZ = 2.0f;
    // [확정 §9-4 "회전점 1.6"] → 10-04 변경(3.1): 쉴 때 날 중심 0.6(옛 설계 1.6−1.0과 같음)을 지키고 팔만 2.5로 늘리면
    // 회전점 = 0.6 + 팀 날 중심 1.0 × 2.5 = 3.1 [계산]. 날 쉴 때 0.35~0.85, 손잡이 아래끝 3.1 − 0.8×2.5 = 1.1 > 몸 높이 1.0 → B-15 해소.
    private const float AxeBladeRestCenter = 0.6f;
    private const float AxePivotHeight = AxeBladeRestCenter + TeamBladeCenterY * AxeScaleY;             // 3.1
    private const float AxeHandleBottom = AxePivotHeight - TeamHandleLenY * AxeScaleY;                 // 1.1
    private const float AxeBladeRestBottom = AxePivotHeight - TeamBladeCenterY * AxeScaleY - TeamBladeThickY * AxeScaleY / 2f; // 0.35
    // [계산] 통행 폭 2: 날 폭 1.5는 쉴 때 다리 폭 2의 3/4라 양옆 0.25만 남는다 — 날이 중앙을 지나는 동안은 못 지난다. 몸(1×1)이 날과
    // 안 겹치는 가장 긴 연속 창은 0.73초(몸을 다리 가장자리 |x| ≤ 0.5에 얹은 경우, 진자 주기 2초)이고 네모가 날 두께 0.3을 지나는 데
    // 필요한 시간은 (0.3+1)/3.5 = 0.37초라 여유 0.36초다(팀 기본은 창 0.55초·필요 0.33초). 손잡이는 전 주기 최저 1.094로 몸 위로 지난다.

    // ── 망치 (검토서 §3-2·§4-3·§5-1 ③) ──
    // 머리 1×0.6×1 → 2.2×0.9×2.2. 다리 폭 2를 가로막지만 기본 머리 1×1도 폭 2 다리에서 좌우 0.5뿐이라 옆으로 비킬 수 없는 건 같다.
    // 머리 폭 2.2가 다리 밖으로 0.1씩 나오는 것은 허공 위라 무해하다. 다리 방향 길이 2.2는 함정 간격 8 이상(설계 §3)을 깨지 않는다
    // (망치 ±1.1 · 이웃 가시 ±0.6 · 도끼 날 두께 ±0.15 → 사이 6.3 이상).
    private const float HammerScaleX = 2.2f, HammerScaleY = 1.5f, HammerScaleZ = 2.2f;
    // [설계 §3 "대기 시 머리 밑면은 다리 위 2.2"] → 10-04 변경(12): 기본 시점(pitch 0, FOV 60)에서 화면 윗가장자리가 닿는 높이는
    // 앞 20 m 지점 11.6 m이므로 밑면 12 m의 망치는 앞 21.7 m 안쪽에서 화면 밖이다 [계산 S5_숨은함정_계산.py §A]. 마우스로 올려다보면
    // 보인다(설계 의도 "위를 보면 보이지만").
    private const float HammerHeadBottom = 12f;
    private const float HammerHeadHalfH = TeamHeadY * HammerScaleY / 2f;          // 0.45 — 피벗 = 머리 중심
    private const float HammerLowestBottom = 0.2f;                                // 저점 머리 밑면 0.2(옛 값 유지)
    private const float HammerDropDistance = HammerHeadBottom - HammerLowestBottom; // 11.8 — HammerTrap.dropDistance는 월드 거리(HammerTrap.cs:34)
    // 팀 기본 속도 6/3이면 12 m 한 주기가 8.3초라 늘어진다. 낙하 25·상승 8이면 낙하 0.47초·한 주기 4.35초 [계산].
    // 한 물리 스텝 이동 = 25 × 0.02 = 0.5 m < 머리 두께 0.9 + 몸 1.0이라 이산 충돌에서 몸을 건너뛰지 않는다 [계산, 실측 필요].
    private const float HammerDescendSpeed = 25f, HammerAscendSpeed = 8f;

    // ── 가시 (검토서 §5-1 ④) ──
    // 발자국 1.6(다리 가로)×1.2(다리 방향)·높이 1.5. 다리 폭 2에서 양옆 0.2만 남아 돌출 중에는 막힌다(타이밍으로만 통과; 점프는 세모만 넘는다
    // [계산: 창 네모 0/세모 0.64/구 0.29초, 필요 0.63/0.44/0.31초]).
    private const float SpikeFootAcross = 1.6f, SpikeFootAlong = 1.2f, SpikeTall = 1.5f;
    // [10-04 변경] 옛: 판을 0.6 끊고 수납 윗면 = 다리 윗면 0(피벗 −0.5). 새: 가시 전체를 판 밑에 묻는다. 키네마틱이라 정적 판 속을 지나 솟아오르고
    // (정적-키네마틱은 충돌 쌍이 없다) 플레이어와만 접촉한다.
    // [10-04 M1 수정 — 컨트롤타워 판정 (a), 검토 진행/검문/S5_숨은함정_검토.md] 첫 안(피벗 −1.06 = 콜라이더 윗면만 판 밑면 −0.01 아래)은 틀렸다 —
    // 팀 시각물은 정사면체 메쉬(PeriodicTrapMenuItem.cs:105)라 꼭짓점이 콜라이더 중심축 위 0.866 × 시각 y 배율에 있다(씬 AABB y −0.2887~+0.866,
    // 시각 y 배율 = 자식 TeamSpikeY 1 × 루트 SpikeTall/TeamSpikeY 1.5 = 1.5 → 피벗 위 1.299). 피벗 −1.06이면 꼭짓점이 +0.239에 와 판 위로 붉은
    // 뿔이 늘 보였다. 그래서 피벗을 −1.62로 내린다. 팀이 만든 자식 시각물의 위치는 옮기지 않는다(판정 (b) 불채택 — 배치 변환만).
    //  · 수납 시각 꼭짓점 = −1.62 + 0.866 × 1.5 = −0.321 → 판 윗면(0)보다 0.321 아래(요구 ≥ 0.02), 판 밑면(−0.3)보다도 0.021 아래라 판 속에도 안 보인다 [계산].
    //  · 수납 콜라이더 윗면 = −1.62 + 0.75 = −0.87. popDistance = 콜라이더 반높이 − 피벗 = 0.75 + 1.62 = 2.37 → 돌출 콜라이더 0~1.5(옛 값 유지).
    //  · 돌출 시 시각 꼭짓점 = −1.62 + 2.37 + 1.299 = 2.049로 콜라이더 윗면(1.5)보다 0.549 높고 시각 밑면(−2.053 + 2.37 = 0.317)은 콜라이더 밑(0)보다 0.317 떠 보인다
    //    — 팀 시각물이 콜라이더보다 큰 채라 시각만 다르다(판정 불변).
    //  · 밑면 꼭짓점 하나는 다리 가로 0.7887 × 1.6 = 1.262까지 나가 판 가장자리(1.0) 밖으로 0.262 삐져나온다 — 수납 시 y −2.053 부근, 판 아래 허공이라 위에서는 안 보이고
    //    다리 옆·아래에서 보인다 [계산]. 시각 배치(자식 이동·축소)가 필요하니 이번 판정 범위 밖으로 두고 설계서 §10-4에 남긴다.
    //  · 주기: 수납 대기 2 + 상승 2.37/8 = 0.296 + 돌출 유지 0.4 + 하강 2.37/4 = 0.593 → 3.29초(첫 안 3.08초). 위험(상승 + 돌출 유지) 0.70초.
    private const float SpikeVisualApexRatio = 0.8660254f;                          // 팀 정사면체 꼭짓점 높이 √3/2 (TetrahedronMeshGenerator: 무게중심 원점·한 면이 바닥)
    private const float SpikeApexMargin = 0.02f;                                     // 수납 시 시각 꼭짓점이 판 윗면(0)보다 이만큼 이상 아래여야 한다(컨트롤타워 판정 10-04)
    private const float SpikePivotY = -1.62f;                                        // 콜라이더 중심 = 피벗
    private const float SpikeHalfH = SpikeTall / 2f;                                 // 0.75
    private const float SpikeBuriedTop = SpikePivotY + SpikeHalfH;                   // −0.87 — 수납 콜라이더 윗면
    private const float SpikeApexRetracted = SpikePivotY + SpikeVisualApexRatio * SpikeTall; // −0.321 — 수납 시각 꼭짓점(루트 y 배율 = SpikeTall / TeamSpikeY, 팀 시각물 y 1)
    private const float SpikePopDistance = SpikeHalfH - SpikePivotY;                 // 2.37 — 돌출 콜라이더 0~1.5 (SpikeTrap.popDistance는 월드 거리)

    // ── 다리3 (§2·§3) ──
    private const float Bridge3X0 = -22f, Bridge3X1 = -4f; // [확정] x −22 → −4
    private const float Bridge3Z0 = 83f, Bridge3Z1 = 85f;  // [확정] z 83~85
    private const float PillarClearance = 5f;   // [확정 §3·§6] 기둥은 다리에서 5m(다리 가장자리 → 기둥 면)
    // [구현 결정] 기둥 단면 0.4×0.4 — 설계서에 값 없음. 0.6이면 레이저1 기둥(x −19)이 다리3·다리2 양쪽에서 동시에 5 이상일
    // 수 없다(반폭 ≤ 0.234 필요 [계산]). 0.4는 레이저 몸통 시각물(0.4×0.3 [팀])을 받친다.
    private const float PillarHalf = 0.2f;
    // [확정 §3] 레이저는 다리 앞쪽에서 +Z, 발사구는 다리 뒤쪽에서 −Z. 설계 좌표 z 80·89는 "5m"와 함께 맞출 수 없어(가장자리까지
    // 3·4) "5m"를 따랐다 [계산]: 레이저 z = 83 − 5 − 0.2 = 77.8, 발사구 z = 85 + 5 + 0.2 = 90.2.
    private const float LaserPillarZ = Bridge3Z0 - PillarClearance - PillarHalf;
    private const float LauncherPillarZ = Bridge3Z1 + PillarClearance + PillarHalf;
    private const float LaserS1 = 3f, LauncherS1 = 7f, LaserS2 = 11f, LauncherS2 = 15f; // [확정 §3] 다리3 s(x −22부터)
    private const float FireHeight = 0.5f;      // [확정 §3] 광선·발사 높이 다리 위 0.5(피벗 = 광선/탄 원점 [팀]) — 10-04: 레이저는 그대로, 발사구는 아래 LauncherFireHeight
    // [10-04 사용자 결정, 검토서 §4-3·§5-1 ⑤⑥] 레이저 광선 반경 0.15 → 0.3, 선 굵기 0.12 → 0.6(판정 지름과 같게 — 팀 activeWidth는 시각 전용).
    // 광선 대역 0.5±0.3 = 0.2~0.8. 점프로 피하려면 몸 중심이 0.8 올라야 하고 그 창은 네모 0.57초·세모 0.99초·구 0.81초로 발사 0.4초보다
    // 길다 [계산]. 반경 0.45는 0.45초(여유 0.05), 0.6은 0.29초라 못 피한다 → 상한 0.3 권장(검토서 §4-3).
    private const float LaserBeamRadius = 0.3f, LaserBeamWidth = 0.6f;
    // 발사구: 탄 반지름 0.3 → 0.4(지름 0.8). 탄은 비트리거 콜라이더에 처음 닿으면 사라지므로(Projectile.cs:52-65) 탄 밑면(높이 − 반지름)은
    // 다리 판(윗면 0)·발사 기둥(윗면 LauncherPillarTop 0.1) 위여야 한다 → 발사 높이를 FireHeight 0.5에서 떼어 0.55로: 밑면 0.15 > 0.1, 윗면 0.95.
    // 점프 창 네모 0.45초·세모 0.93초·구 0.73초 > 탄이 몸을 지나는 시간 (2×0.4+1)/8 = 0.225초 [계산]. 반지름 0.45까지가 한계(발사 높이 ≥ r+0.15).
    private const float LauncherProjectileRadius = 0.4f, LauncherFireHeight = 0.55f;
    private const float PillarBottom = -VoidDepth; // [확정 §3] 기둥은 허공 바닥(y −20)부터
    // [구현 결정] 레이저 기둥 윗면 = 발사체 몸통 밑면(0.5 − 0.4/2 = 0.3) — 몸통이 기둥 위에 얹힌다. 광선 원점(0.5)은 기둥 밖.
    private const float LaserPillarTop = 0.3f;
    // [구현 결정] 발사구 기둥 윗면 0.1 — 탄(반지름 0.3 [팀])이 원점 0.5에서 생기면 밑면이 0.2. 생성 순간 기둥과 닿으면
    // 첫 충돌로 사라질 수 있어(S5-R §ProjectileLauncher 8) 0.1 띄운다. 몸통 시각물 밑면(0.2)과 기둥 사이 0.1 빈다(콜라이더 없음).
    // 10-04: 탄 반지름 0.4·발사 높이 0.55 → 탄 밑면 0.15(기둥 윗면과 0.05 간격). 몸통 시각물(팀 0.6×0.6×0.4, 중심 = 발사 원점) 밑면은 0.25가 돼
    // 기둥과 사이 0.15 뜬다(시각만 — 기둥 윗면을 올리면 탄이 기둥에 걸리므로 올리지 않는다).
    private const float LauncherPillarTop = 0.1f;
    // [구현 결정 — 설계 §3 "다리3 전용"] 탄 받이: 팀 탄(탄속 8 × 수명 5 = 40U [팀])은 다리3을 지나 다리2(x −15 → z 64.15,
    // x −7 → z 55.54)까지 간다. 탄은 비트리거 콜라이더에 처음 닿으면 사라지므로(S5-R §10-2) 발사선 위 허공에 정적 받이를 둔다.
    // 위치 [계산 s5b_r1_search.py]: 모든 다리·플랫폼·기둥에서 면까지 5 이상(점프 4.8로 닿지 않음 → 징검다리 없음).
    //   받이1(x −15): z 73.61~73.73에서만 성립 → 73.67. 받이2(x −7): z 64.99~71.52 → 68.25.
    // 단면 x 0.8(탄 반지름 0.3 + 여유 0.1씩)·z 0.4, 높이 −20~1.0(탄 0.2~0.8을 덮음).
    // 10-04: 탄 반지름 0.4·발사 높이 0.55 → 탄 0.15~0.95, 지름 0.8 = 받이 x 폭 0.8. 발사선(탄 중심)이 받이 중앙을 정면으로 지나
    // 첫 접촉에서 사라진다(높이 1.0 > 0.95). 받이는 옮기지 않는다 — 위 5 이상 이격 조건(좁은 창 z 73.61~73.73)을 그대로 지키려고.
    private const float ShotStopHalfX = 0.4f, ShotStopHalfZ = 0.2f, ShotStopTop = 1.0f;
    private const float ShotStop1Z = 73.67f, ShotStop2Z = 68.25f;

    // ── 외곽 벽 (§2) ──
    private const float OuterWallH = 6f;        // [확정] x ±30 높이 6(탄·레이저 차단), 난간 없음
    private const float OuterWallT = 0.5f;      // [구현 결정] 두께 0.5(S1 외벽·빈 틀 벽과 같게)

    // ── CH5 시작 안전점 (계약 C2) ──
    // [구현 결정] A 플랫폼 위 (−6, 0, 8): 입구 통로(x −4~4)와 스폰 슬롯(z 3) 옆으로 비켜 겹침 없음, 다리1 시작(x 4)에서 10m,
    // 다리1 중심선(z 8)과 같은 줄. y 0 = 윗면(팀 SectionSafePoint는 놓인 자리를 그대로 착지점으로 쓴다).
    private static readonly Vector3 StartLocalPos = new Vector3(-6f, 0f, 8f);

    private static Transform g; // Build 동안만 유효 — Generated 루트
    private static Transform gimmickRoot;

    /// <summary>S5를 짓는다. 섹터 크기·높이가 설계서 전제(폭 60, 길이 136, 바닥 18, 출구 18)와 다르면 아무것도 짓지 않고
    /// false — 호출자가 빈 틀로 되돌아간다. 짓는 도중 예외가 나면 Generated를 비우고 이번 Build가 만든 씬 루트 잔류물도 지운 뒤 false(부분 생성 금지).</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        if (Mathf.Abs(def.width - SectorWidth) > 0.01f || Mathf.Abs(def.length - SectorLength) > 0.01f
            || Mathf.Abs(def.floorHeight - FloorHeight) > 0.01f || Mathf.Abs(def.exitHeight - ExitHeight) > 0.01f)
        {
            Debug.LogError($"[S5_Builder] 섹터5 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                           $"(설계서 전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}). S5 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        HashSet<GameObject> before = new HashSet<GameObject>(generated.gameObject.scene.GetRootGameObjects());
        List<Component> bridges, hazards;
        try
        {
            Map4Build.BeginSection();
            ValidateTrapSizes(); // [10-04] 함정 크기 상수 점검(에러 로그만)
            BuildPlatforms();
            BuildBridge1Fixed();
            BuildBridge2Planks();
            BuildBridge3();
            BuildOuterWalls();
            BuildPillars();
            BuildShotStops();

            gimmickRoot = NewChild("S5_Gimmicks", Vector3.zero).transform;
            bridges = BuildRotatingBridges();
            hazards = BuildBridge2Traps();
            hazards.AddRange(BuildBridge3Hazards());

            // 계약 C2: hazards 11개(배치 순서, null 없음) · bridges 2개(x 오름차순)
            if (hazards.Count != 11 || hazards.Contains(null) || bridges.Count != 2 || bridges.Contains(null))
                throw new System.Exception($"[S5_Builder] 팀 기믹 목록이 계약과 다르다 — hazards {hazards.Count}/11, bridges {bridges.Count}/2.");

            ReportStrayRoots();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S5_Builder] 섹터5 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(generated.GetChild(i).gameObject);
            foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
                if (!before.Contains(r)) Object.DestroyImmediate(r);   // 이번 Build가 팀 메뉴로 만든 루트 잔류물
            g = null;
            gimmickRoot = null;
            return false;
        }

        try
        {
            // 복귀 배선(S5-W) — 모든 지형·기믹 생성 후 마지막 1회. Wire가 실패해도 지형은 지우지 않는다(계약 C2).
            S5_Wiring.Wire(g, StartLocalPos, hazards, bridges);
            ReportStrayRoots();
            Debug.Log("[S5_Builder] 섹터5 구체화 완료(Z자 외다리 3개 + 배분 A: 회전 다리 2 · 도끼 3 · 가시 2 · 망치 2 · 레이저 2 · 발사구 2).");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S5_Builder] S5_Wiring.Wire 예외 — 지형·기믹은 그대로 둔다(계약 C2): {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            g = null;
            gimmickRoot = null;
        }
        return true;
    }

    // ───────────────────────── 지형 ─────────────────────────

    /// <summary>플랫폼 4개 — 윗면 y 0, 허공 깊이 20까지 채운 블록. 서로 16m 이상 떨어져 면이 닿지 않는다.</summary>
    private static void BuildPlatforms()
    {
        Solid(Map4Build.Floor(g, Min(PlatA, -VoidDepth), Max(PlatA, 0f)), "GEO_S5_Floor_A");
        Solid(Map4Build.Floor(g, Min(PlatR1, -VoidDepth), Max(PlatR1, 0f)), "GEO_S5_Floor_R1");
        Solid(Map4Build.Floor(g, Min(PlatL2, -VoidDepth), Max(PlatL2, 0f)), "GEO_S5_Floor_L2");
        Solid(Map4Build.Floor(g, Min(PlatF, -VoidDepth), Max(PlatF, 0f)), "GEO_S5_Floor_F");
    }

    /// <summary>다리1 고정 판 3개(x 4~8·12~16·20~24). 회전 판 자리(8~12·16~20)는 팀 회전 다리가 채운다(판끼리 면 맞닿음, 틈 0).
    /// A(x ≤ 4)·R1(x ≥ 24)과도 면만 맞닿는다.</summary>
    private static void BuildBridge1Fixed()
    {
        int k = 0;
        for (int i = 0; i < Bridge1Rotating.Length; i++)
        {
            if (Bridge1Rotating[i]) continue;
            Solid(Map4Build.Floor(g, new Vector3(Bridge1Cuts[i], -PlankThickness, Bridge1Z0),
                                     new Vector3(Bridge1Cuts[i + 1], 0f, Bridge1Z1)), $"GEO_S5_Bridge1_Fixed_{k++}");
        }
    }

    /// <summary>다리2 — 대각선이라 Floor(AABB)로 만들 수 없어 Map4Build.Wall 상자를 만든 뒤 중심 기준 Y회전(C6). 윗면은 모두 y 0.
    ///  · 본체(두께 0.3): s ∈ [0.93, L−0.93] 한 장 GEO_S5_Bridge2_0 — [10-04 변경] 가시를 판 밑에 묻으므로 슬롯으로 끊지 않는다
    ///    (옛: 가시 슬롯 s 18·44 ± 0.3으로 3조각 + 메움판 4개). 틈·구멍 없음. 양 끝면이 플랫폼 가장자리 바깥이라 R1·L2와
    ///    겹치지 않는다(맞닿음 틈 0.001 [계산]).
    ///  · 삽입판(두께 0.05): s ∈ [−1, 0.93]·[L−0.93, L+1] — GEO_S5_Bridge2_Insert_R1·L2. 플랫폼과 0.05만 겹친다.</summary>
    private static void BuildBridge2Planks()
    {
        float len = Bridge2Length();
        Bridge2Box("GEO_S5_Bridge2_0", Bridge2BodyInset, len - Bridge2BodyInset, -BridgeWidth / 2f, BridgeWidth / 2f, Bridge2Thickness);
        Bridge2Box("GEO_S5_Bridge2_Insert_R1", -Bridge2Insert, Bridge2BodyInset, -BridgeWidth / 2f, BridgeWidth / 2f, Bridge2InsertThickness);
        Bridge2Box("GEO_S5_Bridge2_Insert_L2", len - Bridge2BodyInset, len + Bridge2Insert, -BridgeWidth / 2f, BridgeWidth / 2f, Bridge2InsertThickness);
    }

    /// <summary>다리2 좌표계(s = 길이, w = 폭 방향 로컬 X, 윗면 y 0)의 상자 하나. w는 다리 로컬 +X(Bridge2Yaw 적용 후 방향).</summary>
    private static void Bridge2Box(string name, float s0, float s1, float w0, float w1, float thickness)
    {
        Quaternion yaw = Bridge2Yaw();
        Vector2 c = Bridge2Point((s0 + s1) / 2f);
        Vector3 wOffset = yaw * new Vector3((w0 + w1) / 2f, 0f, 0f); // 폭 방향 중심 이동(로컬 X → 섹터 XZ)
        Vector3 center = new Vector3(c.x, -thickness / 2f, c.y) + wOffset;
        Vector3 half = new Vector3((w1 - w0) / 2f, thickness / 2f, (s1 - s0) / 2f); // 로컬 X = 폭, Z = 길이
        Transform t = Solid(Map4Build.Wall(g, center - half, center + half, "Floor"), name);
        t.localRotation = yaw; // CreateSolidBox의 localPosition = 상자 중심 → 중심 기준 회전
    }

    private static void BuildBridge3()
    {
        Solid(Map4Build.Floor(g, new Vector3(Bridge3X0, -PlankThickness, Bridge3Z0), new Vector3(Bridge3X1, 0f, Bridge3Z1)), "GEO_S5_Bridge3");
    }

    /// <summary>외곽 벽 x ±30, 높이 6 — [구현 결정] 허공 바닥(y −20)부터 세워 허공 구간도 한 장으로 막는다. 플랫폼 바깥 면에
    /// 붙인다(겹침 0). 섹터 z 0~136 전체.</summary>
    private static void BuildOuterWalls()
    {
        Solid(Map4Build.Wall(g, new Vector3(-HalfW - OuterWallT, -VoidDepth, 0f), new Vector3(-HalfW, OuterWallH, SectorLength)), "GEO_S5_Wall_OuterL");
        Solid(Map4Build.Wall(g, new Vector3(HalfW, -VoidDepth, 0f), new Vector3(HalfW + OuterWallT, OuterWallH, SectorLength)), "GEO_S5_Wall_OuterR");
    }

    /// <summary>다리3 레이저·발사구 기둥 4개(허공 바닥 y −20부터). x는 §3 s 그대로, z는 다리3 가장자리에서 면까지 5(③).</summary>
    private static void BuildPillars()
    {
        Pillar("GEO_S5_Pillar_Laser1", Bridge3X0 + LaserS1, LaserPillarZ, LaserPillarTop);
        Pillar("GEO_S5_Pillar_Launcher1", Bridge3X0 + LauncherS1, LauncherPillarZ, LauncherPillarTop);
        Pillar("GEO_S5_Pillar_Laser2", Bridge3X0 + LaserS2, LaserPillarZ, LaserPillarTop);
        Pillar("GEO_S5_Pillar_Launcher2", Bridge3X0 + LauncherS2, LauncherPillarZ, LauncherPillarTop);
    }

    private static void Pillar(string name, float x, float z, float top)
    {
        Solid(Map4Build.Wall(g, new Vector3(x - PillarHalf, PillarBottom, z - PillarHalf), new Vector3(x + PillarHalf, top, z + PillarHalf)), name);
    }

    /// <summary>발사구 탄 받이 2개(④) — 각 발사구의 발사선(x −15·−7, −Z) 위, 다리3을 지난 허공. 허공 바닥 y −20 ~ 1.0.</summary>
    private static void BuildShotStops()
    {
        ShotStop("GEO_S5_ShotStop_1", Bridge3X0 + LauncherS1, ShotStop1Z);
        ShotStop("GEO_S5_ShotStop_2", Bridge3X0 + LauncherS2, ShotStop2Z);
    }

    private static void ShotStop(string name, float x, float z)
    {
        Solid(Map4Build.Wall(g, new Vector3(x - ShotStopHalfX, PillarBottom, z - ShotStopHalfZ),
                                new Vector3(x + ShotStopHalfX, ShotStopTop, z + ShotStopHalfZ)), name);
    }

    // ───────────────────────── 팀 기믹 ─────────────────────────

    /// <summary>다리1 회전 판 2개(x 8~12·16~20) — 팀 StepRotatingBridge. 판 2×0.3×4, 길이 = 로컬 Z, 회전축 localRotationAxis
    /// 기본값(로컬 Z) 그대로. 루트를 Y축 90° 돌려 로컬 Z를 섹터 +X(다리 길이 방향)로 — 배치 변환만(S5-R §StepRotatingBridge 8).</summary>
    private static List<Component> BuildRotatingBridges()
    {
        List<Component> list = new List<Component>();
        int k = 0;
        for (int i = 0; i < Bridge1Rotating.Length; i++) // x 오름차순
        {
            if (!Bridge1Rotating[i]) continue;
            StepRotatingBridge b = MenuCreate<StepRotatingBridge>("Tools/StepRotatingBridgeSystem/Create Step Rotating Bridge");
            if (b == null) throw new System.Exception("[S5_Builder] 회전 다리 생성 실패.");
            b.name = $"S5_RotBridge_{++k}";
            float cx = (Bridge1Cuts[i] + Bridge1Cuts[i + 1]) / 2f;
            Place(b.transform, new Vector3(cx, -RotBridgeHalfT, (Bridge1Z0 + Bridge1Z1) / 2f), Quaternion.Euler(0f, 90f, 0f));
            list.Add(b);
        }
        return list;
    }

    /// <summary>다리2 함정 7개 — 배치 순서 s 10·18·26·36·44·52·62(계약 C2 순서).</summary>
    private static List<Component> BuildBridge2Traps()
    {
        return new List<Component>
        {
            Axe(1, AxeS1, 0f),
            Spike(1, SpikeS1),
            Hammer(1, HammerS1),
            Axe(2, AxeS2, Axe2PhaseFraction),
            Spike(2, SpikeS2),
            Hammer(2, HammerS2),
            Axe(3, AxeS3, Axe3PhaseFraction),
        };
    }

    /// <summary>다리3 구조물 4개 — s 3·7·11·15(계약 C2 순서). 레이저는 z 77.8 기둥 위에서 +Z, 발사구는 z 90.2 기둥 위에서 −Z(③).
    /// 둘 다 발사 방향 = 루트 transform.forward [팀] — 방향 필드 없이 회전으로만 조준.</summary>
    private static List<Component> BuildBridge3Hazards()
    {
        return new List<Component>
        {
            Laser(1, LaserS1),
            Launcher(1, LauncherS1),
            Laser(2, LaserS2),
            Launcher(2, LauncherS2),
        };
    }

    /// <summary>도끼 — 팀 AxeTrap. 피벗 = 회전점, 날·손잡이는 로컬 +Y. [확정 §9-4] 거꾸로 매달기: 로컬 Z축 180°(팀 SampleScene과
    /// 같은 방법)로 날이 아래, 회전점은 다리 위 1.6 → 10-04 변경: 3.1(AxePivotHeight). 회전축 localRotationAxis 기본값(로컬 Z)을 다리 길이 방향에
    /// 맞춰 날이 다리를 가로질러 흔들린다. 10-04: 루트 localScale (1.5, 2.5, 2) — 로컬 X = 날 폭(다리 가로), Z축 180° 회전은 Z를 보존하므로
    /// 로컬 Z = 다리 방향 두께. 쉴 때 날 중심 0.6(0.35~0.85)·손잡이 아래끝 1.1 [계산, 옛: 날 0.6·손잡이 끝 0.8].
    /// phaseFraction: 설계 §3의 위상(주기에 대한 비율). 팀 startPhase는 초이므로 비율 × 팀 period(공개 필드, 기본 2)를 넣는다.</summary>
    private static Component Axe(int index, float s, float phaseFraction)
    {
        AxeTrap axe = MenuCreate<AxeTrap>("Tools/PeriodicTrapSystem/Create Axe Trap");
        if (axe == null) throw new System.Exception($"[S5_Builder] 도끼 {index} 생성 실패.");
        axe.name = $"S5_Axe_{index}";
        Vector2 p = Bridge2Point(s);
        Place(axe.transform, new Vector3(p.x, AxePivotHeight, p.y), Bridge2Yaw() * Quaternion.Euler(0f, 0f, 180f));
        axe.transform.localScale = new Vector3(AxeScaleX, AxeScaleY, AxeScaleZ); // [10-04] 판정(루트 콜라이더 2개)·시각(자식 큐브)이 같이 커진다
        // 인스펙터 값(설계서 §3). 비율 0이면 팀 기본값(0초) 그대로 — 대입하지 않는다.
        if (phaseFraction > 0f) axe.startPhase = phaseFraction * axe.period;
        return axe;
    }

    /// <summary>가시 — 팀 SpikeTrap. 피벗 = 돌출부 콜라이더(0.6×1×0.6) 중심. [10-04 변경] 옛: 판을 끊은 0.6×0.6 자리에 수축 윗면 = 다리 윗면 0(피벗 −0.5).
    /// 새: 루트 localScale (1.6/0.6, 1.5, 1.2/0.6) = 발자국 1.6(다리 가로)×1.2(다리 방향)·높이 1.5, 피벗 y −1.62(M1 수정: 수납 콜라이더 윗면 −0.87,
    /// 시각 꼭짓점 −0.321 = 판 밑면 아래), popDistance 2.37 = 돌출 콜라이더 0~1.5. 다리 판은 이음새 없는 한 장이다. 판 위로는 안 보인다(수납 꼭짓점 판 아래 0.321).
    /// 다리 방향에서 벗어난 시점에서는 판 아래 매달린 가시 몸통이 보인다(벗어난 각 10°/20°/45° → 9%/28%/78%, 검토 진행/검문/S1S5_최종_검토.md M-B 계산)
    /// — 사용자 10-04 '지금대로 둠'.
    /// 만든 직후 시각물 꼭짓점을 점검한다(CheckSpikeVisualApex). 기부 콜라이더 없음 [팀].</summary>
    private static Component Spike(int index, float s)
    {
        SpikeTrap spike = MenuCreate<SpikeTrap>("Tools/PeriodicTrapSystem/Create Spike Trap");
        if (spike == null) throw new System.Exception($"[S5_Builder] 가시 {index} 생성 실패.");
        spike.name = $"S5_Spike_{index}";
        Vector2 p = Bridge2Point(s);
        Place(spike.transform, new Vector3(p.x, SpikePivotY, p.y), Bridge2Yaw());
        spike.transform.localScale = new Vector3(SpikeFootAcross / TeamSpikeX, SpikeTall / TeamSpikeY, SpikeFootAlong / TeamSpikeZ);
        spike.popDistance = SpikePopDistance; // 월드 거리라 스케일과 별개로 직접(SpikeTrap.cs:24·58)
        EditorUtility.SetDirty(spike);
        CheckSpikeVisualApex(spike);
        return spike;
    }

    /// <summary>[10-04 M1] 수납 시 시각물 꼭짓점 점검 — 가시 아래 렌더러 전부의 최고점(월드 y)을 섹터 로컬 y로 바꿔 판 윗면(0)보다 SpikeApexMargin(0.02) 이상 아래인지 본다.
    /// 값은 Renderer.bounds.max.y와 같다(메쉬 AABB × localToWorld; 루트가 Y회전만이라 y 범위가 정확). 편집 중 Renderer.bounds 갱신이 한 박자 늦을 수 있어
    /// 갱신 시점에 안 흔들리게 메쉬 AABB를 변환 행렬로 직접 옮긴다(메쉬가 없는 렌더러만 Renderer.bounds). 어기면 에러 로그(생성은 막지 않는다).</summary>
    private static void CheckSpikeVisualApex(SpikeTrap spike)
    {
        float topWorld = float.NegativeInfinity;
        int n = 0;
        foreach (Renderer rd in spike.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter mf = rd.GetComponent<MeshFilter>();
            float t = mf != null && mf.sharedMesh != null
                ? MeshTopWorldY(mf.sharedMesh.bounds, rd.transform.localToWorldMatrix)
                : rd.bounds.max.y;
            topWorld = Mathf.Max(topWorld, t);
            n++;
        }
        if (n == 0)
        {
            Debug.LogError($"[S5_Builder] '{spike.name}'에 렌더러가 없어 수납 시각 꼭짓점을 점검하지 못했다.", spike);
            return;
        }
        float apexLocal = g.InverseTransformPoint(new Vector3(g.position.x, topWorld, g.position.z)).y;
        if (apexLocal > -SpikeApexMargin)
            Debug.LogError($"[S5_Builder] '{spike.name}' 수납 시 시각 꼭짓점 섹터 로컬 y {apexLocal:0.###}이 판 윗면(0) 아래 {SpikeApexMargin} 이내·위다 — 판 위로 가시 뿔이 보인다(M1).", spike);
    }

    private static float MeshTopWorldY(Bounds meshBounds, Matrix4x4 localToWorld)
    {
        float top = float.NegativeInfinity;
        for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                {
                    Vector3 c = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(ix, iy, iz));
                    top = Mathf.Max(top, localToWorld.MultiplyPoint3x4(c).y);
                }
        return top;
    }

    /// <summary>망치 — 팀 HammerTrap. 피벗 = 머리 중심, 상부 위치 = Awake 때 위치, 낙하 dropDistance(월드 −Y) [팀 기본 2].
    /// [10-04 변경] 루트 localScale (2.2, 1.5, 2.2) → 머리 2.2×0.9×2.2, 대기 머리 밑면 다리 위 12 → 피벗 y 12.45, dropDistance 11.8 =
    /// 저점 머리 밑면 0.2(옛 값 유지) [계산], 낙하 25·상승 8. 회전은 다리 방향에 맞춘 Y회전만(낙하 방향 무관).</summary>
    private static Component Hammer(int index, float s)
    {
        HammerTrap hammer = MenuCreate<HammerTrap>("Tools/PeriodicTrapSystem/Create Hammer Trap");
        if (hammer == null) throw new System.Exception($"[S5_Builder] 망치 {index} 생성 실패.");
        hammer.name = $"S5_Hammer_{index}";
        Vector2 p = Bridge2Point(s);
        Place(hammer.transform, new Vector3(p.x, HammerHeadBottom + HammerHeadHalfH, p.y), Bridge2Yaw());
        hammer.transform.localScale = new Vector3(HammerScaleX, HammerScaleY, HammerScaleZ);
        hammer.dropDistance = HammerDropDistance;       // 월드 거리라 스케일과 별개로 직접(HammerTrap.cs:34·70)
        hammer.descendSpeed = HammerDescendSpeed;
        hammer.ascendSpeed = HammerAscendSpeed;
        EditorUtility.SetDirty(hammer);
        return hammer;
    }

    /// <summary>고정 레이저 — 팀 FixedPeriodicLaser(+LaserBeam). 피벗 = 광선 원점, 방향 = 로컬 +Z. z 77.8 기둥 위, 광선 높이 0.5, +Z.
    /// 계약 C2: 루트 컴포넌트(FixedPeriodicLaser)를 넘기고, OnHazardHit는 같은 오브젝트의 LaserBeam에 있다(S5-W가 찾는다).
    /// [10-04] 광선 반경 0.3(판정 SphereCast, LaserBeam.cs:112)·선 굵기 0.6(시각, 판정과 같은 지름) — 몸체(0.4×0.4×0.3 시각물)는 안 키운다.</summary>
    private static Component Laser(int index, float s)
    {
        FixedPeriodicLaser laser = MenuCreate<FixedPeriodicLaser>("Tools/SecurityLaserSystem/Create Fixed Periodic Laser (CH5)");
        if (laser == null) throw new System.Exception($"[S5_Builder] 고정 레이저 {index} 생성 실패.");
        laser.name = $"S5_Laser_{index}";
        Place(laser.transform, new Vector3(Bridge3X0 + s, FireHeight, LaserPillarZ), Quaternion.identity);
        if (laser.beam == null) throw new System.Exception($"[S5_Builder] 고정 레이저 {index}의 LaserBeam(beam)이 비어 있다 — 광선 반경을 넣을 수 없다.");
        laser.beam.beamRadius = LaserBeamRadius;
        laser.beam.activeWidth = LaserBeamWidth;
        EditorUtility.SetDirty(laser.beam);
        return laser;
    }

    /// <summary>측면 발사구 — 팀 ProjectileLauncher. 피벗 = 발사 원점, 방향 = 로컬 +Z → Y 180°로 −Z. z 90.2 기둥 위.
    /// [10-04] 탄 반지름 0.4(탄 크기는 발사구 루트 스케일과 무관 — ProjectileLauncher.cs:128), 발사 높이 0.55(옛 FireHeight 0.5와 분리).</summary>
    private static Component Launcher(int index, float s)
    {
        ProjectileLauncher launcher = MenuCreate<ProjectileLauncher>("Tools/ProjectileTrapSystem/Create Projectile Launcher");
        if (launcher == null) throw new System.Exception($"[S5_Builder] 발사구 {index} 생성 실패.");
        launcher.name = $"S5_Launcher_{index}";
        Place(launcher.transform, new Vector3(Bridge3X0 + s, LauncherFireHeight, LauncherPillarZ), Quaternion.Euler(0f, 180f, 0f));
        launcher.projectileRadius = LauncherProjectileRadius;
        EditorUtility.SetDirty(launcher);
        return launcher;
    }

    /// <summary>[10-04] 함정 크기 상수의 파생값이 검토서 §4·§5-1 조건을 깨면 에러 로그(상수를 나중에 바꿀 때의 안전망 — 생성은 막지 않는다).
    /// 모든 기준은 위 상수 주석의 [계산]과 같다.</summary>
    private static void ValidateTrapSizes()
    {
        // 상수를 지역 변수로 한 번 받는다 — const끼리 바로 비교하면 컴파일러가 분기를 상수로 접어 CS0162(도달 불가) 경고를 낸다.
        float handleBottom = AxeHandleBottom, bladeBottom = AxeBladeRestBottom, bladeWidth = TeamBladeWidthX * AxeScaleX;
        float lowest = HammerLowestBottom, drop = HammerDropDistance;
        float step = HammerDescendSpeed * Time.fixedDeltaTime, headPlusBody = TeamHeadY * HammerScaleY + BodyHeight;
        float buriedTop = SpikeBuriedTop, plank = PlankThickness, foot = SpikeFootAcross, bridgeW = BridgeWidth;
        float apexRetracted = SpikeApexRetracted, apexMargin = SpikeApexMargin;
        float ballBottom = LauncherFireHeight - LauncherProjectileRadius, pillarTop = LauncherPillarTop;
        float ballR = LauncherProjectileRadius, beamR = LaserBeamRadius;

        if (g.lossyScale != Vector3.one)
            Debug.LogError($"[S5_Builder] Generated 스케일이 1이 아니다({g.lossyScale}) — 함정 localScale을 월드 크기로 쓰는 전제 위반.");
        if (handleBottom < BodyHeight + 0.05f)
            Debug.LogError($"[S5_Builder] 도끼 손잡이 아래끝 {handleBottom:0.##} < 몸 높이 {BodyHeight} + 0.05 — 손잡이가 통행을 막는다(B-15).");
        if (bladeBottom < 0.2f)
            Debug.LogError($"[S5_Builder] 도끼 날 쉴 때 밑면 {bladeBottom:0.##} < 0.2 — 날이 다리 판에 닿는다.");
        if (bladeWidth > 1.5f + 0.001f)
            Debug.LogError($"[S5_Builder] 도끼 날 폭 {bladeWidth:0.##} > 1.5 — 검토서 §4-3 한계(날 폭 1.5 안팎)를 넘어 네모 통과 창이 필요 시간에 못 미친다.");
        if (lowest < 0f || drop <= 0f)
            Debug.LogError($"[S5_Builder] 망치 저점 밑면 {lowest}·낙하 {drop} — 머리가 다리 판을 뚫는다.");
        if (step >= headPlusBody)
            Debug.LogError($"[S5_Builder] 망치 한 스텝 이동 {step:0.##} ≥ 머리 두께 + 몸 높이 {headPlusBody:0.##} — 몸을 건너뛴다.");
        if (buriedTop > -plank)
            Debug.LogError($"[S5_Builder] 가시 수납 콜라이더 윗면 {buriedTop}이 판 밑면 {-plank}보다 위다 — 가시가 보이거나 판과 겹친다.");
        if (apexRetracted > -apexMargin)
            Debug.LogError($"[S5_Builder] 가시 수납 시 시각 꼭짓점 {apexRetracted:0.###}이 판 윗면(0) 아래 {apexMargin} 이내·위다 — 판 위로 뿔이 보인다(M1). 생성 후 렌더러 점검은 CheckSpikeVisualApex.");
        if (foot > bridgeW)
            Debug.LogError($"[S5_Builder] 가시 발자국 {foot} > 다리 폭 {bridgeW}.");
        if (ballBottom < pillarTop + 0.04f)
            Debug.LogError($"[S5_Builder] 발사구 탄 밑면 {ballBottom:0.##}이 기둥 윗면 {pillarTop} + 0.04보다 낮다 — 탄이 생성 즉시 사라질 수 있다.");
        if (ballR > 0.45f + 0.001f || beamR > 0.3f + 0.001f)
            Debug.LogError($"[S5_Builder] 탄 반지름 {ballR}(상한 0.45)·광선 반경 {beamR}(상한 0.3) — 점프 회피 한계(검토서 §4-3) 초과.");
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>팀 생성 메뉴는 씬 루트에 오브젝트를 만든다. 전부 Generated 아래로 옮겼는지 확인(S1_Builder와 같은 점검) —
    /// 루트에 남으면 재생성 때 지워지지 않아 Generate마다 중복된다.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"[S5_Builder] 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static float Bridge2Length() => (Bridge2End - Bridge2Start).magnitude;

    /// <summary>다리2 중심선 위 s 지점(x, z). s = 시작점 (26, 20)부터의 거리.</summary>
    private static Vector2 Bridge2Point(float s) => Bridge2Start + (Bridge2End - Bridge2Start).normalized * s;

    /// <summary>로컬 +Z를 다리2 진행 방향으로 돌리는 Y회전(약 −42.88° [계산]).</summary>
    private static Quaternion Bridge2Yaw()
    {
        Vector2 d = (Bridge2End - Bridge2Start).normalized;
        return Quaternion.Euler(0f, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0f);
    }

    private static Vector3 Min(Rect r, float y) => new Vector3(r.xMin, y, r.yMin);
    private static Vector3 Max(Rect r, float y) => new Vector3(r.xMax, y, r.yMax);

    /// <summary>Map4Build.Floor/Wall이 돌려준 상자에 S5 이름을 붙이고 시각물 자식 "Visual"을 VIS_ 접두사로 바꾼다(Map4Build 수정 없음).
    /// 생성이 거부돼 null이면 즉시 중단(부분 지형 방지 — Build가 Generated를 비운다).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"[S5_Builder] 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        t.name = name;
        Transform vis = t.Find("Visual");
        if (vis != null) vis.name = "VIS_" + name.Substring("GEO_".Length);
        return t;
    }

    private static GameObject NewChild(string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(g, false);
        go.transform.localPosition = localPos;
        return go;
    }

    /// <summary>섹터 로컬 위치·회전으로 놓는다(Generated의 세계 변환을 곱한다).</summary>
    private static void Place(Transform t, Vector3 local, Quaternion localRot) =>
        t.SetPositionAndRotation(g.TransformPoint(local), g.rotation * localRot);

    /// <summary>팀 생성 메뉴(private) 실행 — S1_Builder.MenuCreate와 같은 방식(생성물이 선택된다). 배치 모드에서 Selection이 비는
    /// 경우(S5-R §0-2 "모르겠다")에 대비해, 실행 전후 활성 씬 루트를 비교해 새로 생긴 T 오브젝트를 찾는 대안을 둔다.
    /// 찾은 오브젝트는 곧바로 S5_Gimmicks 아래로 옮긴다(씬 루트에 남기지 않음).</summary>
    private static T MenuCreate<T>(string path) where T : Component
    {
        Scene active = SceneManager.GetActiveScene();
        HashSet<GameObject> before = new HashSet<GameObject>(active.GetRootGameObjects());
        Selection.activeGameObject = null;
        if (!EditorApplication.ExecuteMenuItem(path))
        {
            Debug.LogError($"[S5_Builder] 메뉴 실행 실패: {path}");
            return null;
        }

        GameObject go = Selection.activeGameObject;
        if (go == null || go.GetComponent<T>() == null)
        {
            go = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!before.Contains(root) && root.GetComponent<T>() != null) { go = root; break; }
        }
        if (go == null)
        {
            Debug.LogError($"[S5_Builder] '{path}'가 만든 {typeof(T).Name} 오브젝트를 찾지 못했다.");
            return null;
        }
        go.transform.SetParent(gimmickRoot, true);
        return go.GetComponent<T>();
    }
}
#endif
