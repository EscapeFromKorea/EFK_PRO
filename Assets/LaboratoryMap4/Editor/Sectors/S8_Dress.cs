#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// S8 약물 조합 — 어두운 서고 조명·재질(과제 S8-L → 2차 S8-L3, 계약 진행/지시서/R2S68/_계약.md K0-3·K8).
/// S8_Builder.Build가 Wire 다음 마지막에 `S8_Dress.Apply(generated, refs)`로 부른다 [계약 K0-2·K8-2].
///
/// 하는 일(렌더러·조명만 — 콜라이더 = 측정 지형 불변, 콜라이더 0개 [계약 K0-3]):
///  1) GEO_ 상자의 자식 `Visual` 렌더러에 S8 전용 Standard 머티리얼을 입힌다
///     (책장 어두운 목재 · 작업대 밝은 목재 · 바닥/벽/천장 어둡게 · 콘솔 책상 금속).
///     탈출 공간(z132~144 — 섹터 안 [판정 18])의 GEO_S8_Floor_Escape·Wall_Escape*·Ceil_Escape Visual도 같은 규칙으로
///     어둡게 칠한다(K8-1 L 소유 '탈출 공간 Visual도 어둡게 해도 된다'). 탈출 공간 전용 등은 근거가 없어 만들지 않는다.
///     GEO_ 콜라이더·트랜스폼은 읽기만 한다. 팀 기믹(S8_Gimmicks 아래)·문(GEO_S8_Door_)은 건드리지 않는다.
///  2) generated 바로 아래 그룹 `S8_Dress` 1개에 VIS_S8_… 등·장식을 만든다(콜라이더 0 — 생성 후 제거 + 마지막 전수 제거).
///     - 작업대 구역(refs.workZoneBounds)만 따뜻한 스탠드 조명(Spot, 아래 향함) + 밝은 바닥 덮개 판.
///     - 책·혼합 자리 위 작은 스탠드 등 2개.
///     - 순찰로 약한 조명 [판정 21] — workZoneBounds 밖 순찰로 구간(refs.patrolWaypoints에서 뽑음). 벽 가까운 구간은
///       벽 부착, 나머지는 천장 밀착 기구. 기구는 CCTV 카메라보다 높이 달아 카메라 시선을 가리지 않는다.
///       (1차 판의 책장 통로 A·B 벽등 6개는 이 조명에 흡수했다 — 통로 A·B가 순찰로 구간이다.)
///     - 대기실·콘솔·출구에 약한 등.
///     - CCTV 카메라 자리 뒤에 작은 카메라 몸체 + 빨간 표시등(렌즈 뒤라 화면에 안 잡힘).
///
/// 전역 조명(Master 방향광·환경광·RenderSettings)은 이 코드가 읽지도 바꾸지도 않는다 [계약 K0-3].
/// [1차 판정 A1 · 판정 14] Master 방향광 소프트 그림자는 INF1-2가 Map4SceneBuilder에서 켠다(섹터 코드는 손대지 않음).
/// Master 씬 값(읽기만 — Map4_Master.unity:252-261·:23-27): 방향광 m_Type 1·세기 1, 환경광 Skybox 모드(m_AmbientMode 0)·강도 1.
/// 그림자 m_Shadows.m_Type은 HEAD(09-29 11:47 2차 라운드 Generate 전) 0(없음) → INF1-2 반영(Map4SceneBuilder.cs:486-487) 뒤
/// 2(Soft) — 09-29 12:18 저장본 :261에서 확인(수정 코드1). 이 값은 Generate마다 다시 쓰이므로 여기 적힌 것은 확인 시점 기록이다.
/// 그림자가 켜지면 천장(GEO_S8_Ceil_Main)이 방향광 직사광을 막는다. 환경광은 막지 못하므로 천장 차광 효과가
/// 얼마인지는 Unity 실측 전까지 (추정)이다(초안 §1-1). 그래서 바닥·벽·천장 Visual도 어두운 재질로 칠해 둔다(PaintFrame).
///
/// 머티리얼: Assets/LaboratoryMap4/Materials/Generated/M4_S8_&lt;이름&gt;.mat — Standard, 있으면 재사용·없으면 생성
/// [계약 K0-3] (S1_Lighting.cs 방식). Map4Palette.asset·Map4_*.mat·팀 머티리얼은 수정하지 않는다(새 에셋만 만들고
/// 렌더러의 sharedMaterial 참조만 바꾼다).
///
/// Unity 실행 미검증(스테이징 — 통합 후 Unity 대기열에서 확인). 형식 검사는 Unity 동봉 csc로만 했다(보고 S8-L3).
/// </summary>
public static class S8_Dress
{
    // ════════════════════════════════════════════════════════════════════════════════════
    //  상수 표 — 초안(진행/초안/S8_배치초안.md 2차 반영·수정 초안1)과 판정 17~23 기준. 좌표·크기·세기는 여기서만 고친다.
    //  좌표는 섹터 로컬(generated 기준): +Z 진행, x=0 중앙, y=0 = 섹터 바닥. B 좌표는 옮겨 적지 않고 refs에서 읽는다.
    //  색은 sRGB로 적는다(프로젝트 색 공간 Linear — ProjectSettings m_ActiveColorSpace 1 — Unity가 변환).
    // ════════════════════════════════════════════════════════════════════════════════════

    // ── 전체 스위치 ─────────────────────────────────────────────────────────────────────
    /// <summary>[계약 K8-1 :238 · 판정 19] 바닥·벽·천장·콘솔 책상 Visual(탈출 공간 포함)도 어둡게 칠한다.
    /// 천장·콘솔 책상 GEO는 판정 19로 승인됐고, K8-1 L 소유 범위가 '어두운 서고 · 탈출 공간 Visual도 어둡게'다.
    /// 그림자가 켜져도 환경광은 천장이 못 막으므로(추정) 섹터 안 명도는 이 재질이 맡는다. false면 틀 재질을 건드리지 않는다.</summary>
    private static readonly bool PaintFrame = true;

    // ── 작업대 구역 스탠드 조명(Spot, 아래 향함) — 48개 ─────────────────────────────────
    // 등 x·z와 덮개 칸 경계는 상수로 두지 않는다(수정 1) — refs.tables(B의 작업대 격자)에서 Apply 때 뽑는다:
    //  등 x = 작업대 열 x 중심들, 등 z = 행 z 중심을 블록(가로 통로로 나뉜 묶음) 안에서 2개씩 묶은 가운데,
    //  덮개 칸 안쪽 경계 = 이웃한 등 x·z의 가운데. 초안 수정 1 기준 결과(대조용 [계산]):
    //  x {−21,−13,−5,5,13,21} · z {30,40,50,60,72,82,92,102} · 칸 경계 x {−17,−9,0,9,17} · z {35,45,55,66,77,87,97}.
    //  [계약 K8-0 :208] '격자 상수 사본 대조 LogWarning 1줄'은 사본 상수가 없어(파생) 대조할 대상이 없다 — 대신 뽑은 격자를 Log로 남긴다.
    /// <summary>[제안] 행 묶음 크기 — 등 1개가 작업대 행 몇 개를 비추는지.</summary>
    private const int WorkLampRowsPerLamp = 2;
    /// <summary>[제안] 이웃 행 z 중심 간격이 가장 작은 간격의 이 배수보다 크면 다른 블록(가로 통로 건넘)으로 본다.
    /// [계산 — 수정 코드1] 블록 안 행 중심 간격 5.0(B TablePitchZ), 가로 통로를 건너는 간격 7.0(62.5→69.5) → 비율 1.4.
    /// 문턱은 1.0과 1.4 사이여야 한다 — 1.2(문턱 6.0)는 양쪽 여유 1.0씩. (이전 1.5는 문턱 7.5라 16행이 한 블록으로 묶였고,
    /// 두 블록이 모두 8행이라 결과가 우연히 맞았다 — 한 블록이 홀수 행이면 등이 가로 통로 위 z66에 생긴다. scratchpad s8l3_fix1_rows.py)</summary>
    private const float WorkRowBlockGapFactor = 1.2f;
    /// <summary>[제안] 등 갓 중심 높이 — 천장 아랫면 8(초안 §1-1) 아래. 갓 아랫면 4.92.
    /// 도달 근거(수정 1): 작업대 윗면 1.0 + 도형 약 1U + 세모 jumpHeight 2.0 [팀 TetrahedronStats.asset:17] → 약 4.0
    /// (추측 — jumpHeight를 피벗 상승량으로 가정. 네모 1.2·공 1.6은 더 낮다). 4.92 > 4.0이라 닿지 않는다(추측).</summary>
    private const float WorkLampY = 5f;
    /// <summary>[계산] 높이 5에서 바닥 빛 웅덩이 반지름 = 5·tan(48°) ≈ 5.55 → 칸(폭 6.5~9 × 깊이 8.5~11) 대부분.
    /// 안쪽 각(innerSpotAngle)은 쓰지 않는다 — (추측) Built-in에서는 효과 없음, SRP 전용 속성(수정 1).</summary>
    private const float WorkLampSpotAngle = 96f;
    /// <summary>[계산] 웅덩이 가장자리 거리 5/cos48° ≈ 7.47 + 여유 → 9. 바깥 열(x ±21)에서 등 높이 구 반지름 9 →
    /// x ±30, 바닥에서는 √(81−25) ≈ 7.48 → x ±28.5 — 책장 줄(|x| ≥ 30.5, 초안 §1-3)에 닿지 않아 책장은 어둡게 남는다.</summary>
    private const float WorkLampRange = 9f;
    /// <summary>[추정] 세기 — 설계서에 값 없음, 실측 조정 대상.</summary>
    private const float WorkLampIntensity = 3.5f;
    /// <summary>[제안] 따뜻한 스탠드 빛(주황 아님 — 주황은 '조작' 신호로 남겨 둔다).</summary>
    private static readonly Color WorkLampColor = new Color(1.00f, 0.86f, 0.64f);

    // ── 작업대 구역 바닥 덮개 판(VIS, 콜라이더 없음) — 48장 ───────────────────────────────
    // 칸 = 등 1개가 가운데 오는 조각(바깥 경계는 refs.workZoneBounds). 판을 칸으로 나누는 이유: Built-in Forward는
    // 렌더러마다 픽셀 조명 수가 제한되므로(Ultra 4) 큰 판 1장이면 48개 등 중 몇 개만 빛 웅덩이가 그려진다
    // → 칸마다 자기 등이 가장 가까운 픽셀 조명이 되게 나눈다. 칸 경계는 WorkGrid(refs.tables)에서 온다.
    /// <summary>[계산] 바닥 윗면(y0) 위로 띄우는 높이 — 거리 130·근평면 0.3·24비트 깊이 정밀도 약 0.002의 10배(z-fighting 방지).</summary>
    private const float WorkTileLift = 0.02f;

    // ── 책·혼합 자리 스탠드 등(Spot) — 2개 ──────────────────────────────────────────────
    /// <summary>[제안] 역할 앵커(바닥) 위 갓 중심 높이 → 갓 아랫면 4.6 − 0.08 = 4.52. 등이 작업대 R07_C3/C4(윗면 1.0) 바로 위라
    /// 세모가 작업대에 올라 뛰면 닿는지가 기준이다(수정 1 · 계약 K8-0 :208): 도달 = 1.0 + 도형 약 1U + 세모 jumpHeight 2.0
    /// [팀 TetrahedronStats.asset:17] ≈ 4.0(추측 — jumpHeight를 피벗 상승량으로 가정) → 아랫면 ≥ 4.5(추측)로 올렸다.
    /// (이전 3.8·근거 '점프 ≤1.1'은 LD-01 필수 경로 턱 상한을 최대 점프로 잘못 쓴 것.)
    /// [계산 — 판정 20 앵커 x ±4.0, 초안 §3 (∓4.0,0,56.25) forward −Z] 역할 등 = (∓4.0, 4.6, 57.0): workZoneBounds
    /// (x −23.5~23.5 · z 26.5~105.5) 안, 가장 가까운 작업 등 (∓5,5,60)과 수평 3.16 · 3D 3.19 > 갓 반지름 합 0.6(안 겹침).</summary>
    private const float RoleLampHeight = 4.6f;
    /// <summary>[제안] 앵커에서 작업대 쪽(−forward)으로 옮기는 거리 — 작업대 R07 남면(z56.5)과 서는 자리 사이 위.</summary>
    private const float RoleLampTowardTable = 0.75f;
    private const float RoleLampSpotAngle = 70f;          // [추정]
    private const float RoleLampRange = 7f;               // [계산] 높이 4.6에서 웅덩이 반지름 4.6·tan35° ≈ 3.22, 가장자리 거리 4.6/cos35° ≈ 5.62 + 여유
    private const float RoleLampIntensity = 2.5f;         // [추정] 실측 조정
    private static readonly Color RoleLampColor = new Color(1.00f, 0.90f, 0.72f); // [제안]

    // ── 순찰로 약한 조명(Point) [판정 21 · 계약 K8-0 :205 · 초안 §1-7 :99-102] ───────────
    // 대상 = 순찰로 구간(refs.patrolWaypoints i→i+1, 마지막→첫 점 [계약 K9])을 refs.workZoneBounds(xz)로 잘라 남은 바깥 조각.
    // 초안 기준 결과 [계산 — scratchpad s8l3_geo.py]: 통로 A(WP1→2) 8 · 북쪽 가로(WP2→3) 7 · 통로 B(WP3→4) 8 ·
    // 남쪽 가로(WP4→5) 4 · 가운데 통로 남쪽 끝(WP5→6 z20~26.5) 1 · 서쪽 안쪽 통로(WP7→8) 4 · 남쪽 가로(WP8→1) 2 = 34개.
    // WP6→7의 바깥 꼬리(x −23.5~−27, 3.5)는 PatrolMinPiece보다 짧아 건너뛴다 — 모퉁이 (−27,66)은 WP7→8 첫 등(z60.25)이 덮는다.
    // 좌표 사본 없음: 위치는 전부 refs(patrolWaypoints·workZoneBounds·archiveBounds·cctvCameraAnchors)에서 뽑는다.
    // 1차 판 책장 통로 벽등(SconceZ 40·66·92 × A·B = 6개)은 이 조명에 흡수했다(통로 A·B = 순찰로 구간, L 재량 — 보고 S8-L3).
    /// <summary>[제안] 한 조각 안 등 간격 상한 — 조각 길이 ÷ 이 값을 올림한 개수로 나눠 각 칸 가운데에 둔다.
    /// 간격 12면 표본 → 가장 가까운 등 수평 최대 6.0, 캡슐 중심(y1)까지 3D 최대 7.72 &lt; 범위 8 [계산].</summary>
    private const float PatrolLightSpacing = 12f;
    /// <summary>[제안] 이보다 짧은 바깥 조각은 등을 두지 않는다(workZoneBounds 경계에 걸친 짧은 꼬리 — 이웃 구간 등이 덮음).</summary>
    private const float PatrolMinPiece = 4f;
    /// <summary>[제안] 조각이 벽(archiveBounds 면)과 평행하고 이 거리 안이면 벽 부착형, 아니면 천장 밀착형.
    /// 초안 기준: 통로 A·B(x ±42, 벽 ±43.5에서 1.5) → 벽 부착. 나머지(벽에서 ≥19.5) → 천장 밀착.</summary>
    private const float PatrolWallMountMaxDist = 2f;
    /// <summary>[제안] 광원 높이(바닥 위). 기구(천장 아래)와 떨어진 광원점이다 — 범위를 작업 등보다 작게(8 &lt; 9) 두면서
    /// 캡슐 중심(y1)까지 닿게 하려고 1차 벽등(SconceY 5)과 같은 높이에 둔다. 광원에는 렌더러·콜라이더가 없다.</summary>
    private const float PatrolLightY = 5f;
    /// <summary>[제안] 벽 부착형일 때 벽 안쪽 면에서 광원까지(1차 SconceLightInset 0.5와 같음).</summary>
    private const float PatrolWallLightInset = 0.5f;
    /// <summary>[제안] 범위 — 작업 등 9보다 작게(초안 :102 '어두운 서고 대비 유지'). 광량 절대값은 근거가 없다.</summary>
    private const float PatrolLightRange = 8f;
    /// <summary>[추정] 세기 — 작업 등 3.5보다 작게. 1차 벽등 0.7보다는 크게(목적이 CCTV 식별). 적정값은 모르겠다 — 실측 조정.</summary>
    private const float PatrolLightIntensity = 1.2f;
    /// <summary>[제안] 약간 차가운 흰빛 — 관리자 상태 색(ManagerStatusIndicator, 비발광)이 덜 틀어지게 1차 벽등보다 채도를 낮췄다.</summary>
    private static readonly Color PatrolLightColor = new Color(0.80f, 0.86f, 1.00f);
    /// <summary>[제안] 천장 밀착 기구 크기(경로 가로 × 두께 × 경로 방향). 윗면 = 천장 아랫면 − PatrolFixtureCeilGap → 아랫면 7.91(초안 천장 8).</summary>
    private static readonly Vector3 PatrolCeilFixtureSize = new Vector3(0.4f, 0.08f, 1.0f);
    /// <summary>[제안] 기구 윗면과 천장 아랫면 사이 틈(면 겹침 방지).</summary>
    private const float PatrolFixtureCeilGap = 0.01f;
    /// <summary>[제안] 벽 부착 기구 크기(벽에서 나온 깊이 × 높이 × 경로 방향). 윗면 = 천장 아랫면 − PatrolWallFixtureDrop → 7.64~7.8.</summary>
    private static readonly Vector3 PatrolWallFixtureSize = new Vector3(0.12f, 0.16f, 1.0f);
    private const float PatrolWallFixtureDrop = 0.2f;     // [제안]
    /// <summary>[계산] 기구 아랫면이 카메라 앵커 최고 높이보다 이만큼 이상 높아야 한다. 카메라 → 순찰로 표적(캡슐 중심 y1
    /// [판정 17 · 초안 :97])의 직선은 카메라 높이에서 내려가기만 하므로, 기구가 카메라보다 높으면 시선을 가릴 수 없다.
    /// 초안 기준: 카메라 최고 y7.5(Cam_3 대안 [판정 21]) ↔ 기구 아랫면 최소 7.64. 어기면 LogWarning(등은 만든다).</summary>
    private const float PatrolFixtureCamClearance = 0.1f;
    /// <summary>[지시서 S8-L3 수치표 21 · 초안 C14 근거] 걷는 바닥 위 VIS 아랫면 하한 — 바닥 도달 0 + 도형 1 + 세모 jumpHeight 2.0 = 3.0,
    /// 책장 4.5는 못 오름(추측). 어기면 LogWarning(등은 만든다).</summary>
    private const float PatrolFixtureMinBottom = 4.5f;

    // ── 대기실·콘솔·출구 약한 등(Point) — 3개 ────────────────────────────────────────────
    private const float FoyerLampHeight = 5f;             // [제안] startAnchor 위
    private const float FoyerLampRange = 9f;              // [제안] 대기실 z0.5~12
    private const float FoyerLampIntensity = 0.8f;        // [추정]
    private const float ConsoleGlowHeight = 1.6f;         // [제안] roleMonitorAnchor 위(화면 빛 흉내 — 메시 없음)
    private const float ConsoleGlowRange = 4f;            // [제안]
    private const float ConsoleGlowIntensity = 0.8f;      // [추정]
    private static readonly Color ConsoleGlowColor = new Color(0.55f, 0.75f, 1.00f); // [제안]
    /// <summary>[제안] exitDoorAnchor 위 → (0,5.5,130) [초안 §3]. 갓 y5.42~5.58·z129.65~130.35, 줄 y5.58~8 — 열린 문
    /// y4.5~8.5·z131.55~131.95와 z 틈 1.20으로 안 겹침 [계산]. 서고 안(z ≤131.5 = archiveBounds [판정 18])이고 탈출 공간 등이 아니다.</summary>
    private const float ExitLampHeight = 5.5f;
    private const float ExitLampRange = 8f;               // [제안]
    private const float ExitLampIntensity = 1.0f;         // [추정]

    // ── 등 갓·줄(시각물) ─────────────────────────────────────────────────────────────────
    private const float ShadeDiameter = 0.7f;             // [제안]
    private const float RoleShadeDiameter = 0.5f;         // [제안]
    private const float ShadeHalfHeight = 0.08f;          // [제안] 원기둥 기본 높이 2 → 스케일 y 0.08 = 높이 0.16
    private const float CordDiameter = 0.03f;             // [제안]
    /// <summary>[초안 §1-1] 천장 아랫면 y — refs.archiveBounds.max.y가 비어 있을 때만 쓰는 대체값.</summary>
    private const float FallbackCeilingY = 8f;

    // ── CCTV 카메라 몸체(선택) ──────────────────────────────────────────────────────────
    // [계산] Cam_3 대안 포즈 (43,7.5,14.5)·fwd (−0.5908,−0.1927,0.7835) [판정 21]에서 몸체+표시등 꼭짓점 최대 x 43.42 < 안쪽 벽 43.5,
    // 최대 y 7.71 < 천장 아랫면 8 — 앵커 기준 파생이라 코드는 바꾸지 않았다(보고 S8-L3 selfChecks).
    private static readonly Vector3 CamHousingSize = new Vector3(0.25f, 0.22f, 0.45f); // [제안]
    /// <summary>[계산] 몸체 중심을 렌즈(앵커) 뒤로 — 앞면이 렌즈 0.075 뒤라 팀 카메라(근평면 기본 0.3) 화면에 안 잡힌다.</summary>
    private const float CamHousingBack = 0.3f;
    private const float CamLedDiameter = 0.06f;           // [제안]

    // ── 머티리얼(Standard) 색 — sRGB ────────────────────────────────────────────────────
    private static readonly Color ShelfWoodColor = new Color(0.24f, 0.16f, 0.10f);   // [제안] 어두운 목재
    private static readonly Color TableWoodColor = new Color(0.52f, 0.37f, 0.23f);   // [제안] 밝은 목재(스탠드 빛 아래 드러남)
    private static readonly Color FloorColor = new Color(0.13f, 0.13f, 0.14f);       // [제안] 어두운 바닥
    private static readonly Color WallColor = new Color(0.16f, 0.16f, 0.18f);        // [제안] 어두운 벽
    private static readonly Color CeilingColor = new Color(0.08f, 0.08f, 0.09f);     // [제안] 더 어두운 천장
    private static readonly Color ConsoleMetalColor = new Color(0.30f, 0.32f, 0.35f);// [제안] 콘솔 책상 금속
    private static readonly Color WorkFloorColor = new Color(0.50f, 0.43f, 0.33f);   // [제안] 작업대 구역 바닥 덮개(밝은 리놀륨 톤)
    private static readonly Color CordColor = new Color(0.05f, 0.05f, 0.05f);        // [제안]
    private static readonly Color CamHousingColor = new Color(0.18f, 0.19f, 0.20f);  // [제안]
    private static readonly Color CamLedColor = new Color(1.00f, 0.08f, 0.05f);      // [제안]
    private const float LampShadeEmission = 1.5f;         // [추정] 갓 발광 배율
    private const float PatrolFixtureEmission = 0.6f;     // [추정] 순찰로 기구 발광 배율(1차 SconceEmission 값 유지)
    private const float CamLedEmission = 2.0f;            // [추정]

    // ── 이름 ─────────────────────────────────────────────────────────────────────────────
    private const string GroupName = "S8_Dress";          // [계약 K0-3]
    private const string GimmickGroupName = "S8_Gimmicks";// [계약 K0-3] W 그룹 — 이 아래는 건드리지 않는다
    private const string VisualChildName = "Visual";      // Map4Build.CreateSolidBox(:450)가 붙이는 자식 이름
    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated";
    private const string MaterialPrefix = "M4_S8_";       // [계약 K0-3]

    // ════════════════════════════════════════════════════════════════════════════════════

    private sealed class Mats
    {
        public Material shelf, table, floor, wall, ceiling, consoleMetal, workFloor,
                        shade, roleShade, cord, patrolFixture, camHousing, camLed;
    }

    private static int lightCount;
    private static int pixelForcedCount;

    /// <summary>계약 K8-2. generated = S8 Generated 루트(섹터 로컬), refs = S8_Builder가 채운 참조.</summary>
    public static void Apply(Transform generated, S8_Refs refs)
    {
        if (generated == null || refs == null)
        {
            Debug.LogError("[S8_Dress] generated 또는 refs가 null이다 — 호출부(S8_Builder) 확인 필요. 조명·재질을 건너뛴다.");
            return;
        }

        lightCount = 0;
        pixelForcedCount = 0;

        // 멱등: 이전 S8_Dress가 남아 있으면 지우고 새로 만든다(Generated는 보통 매번 비워진다).
        Transform old = generated.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject groupGo = new GameObject(GroupName);
        Transform group = groupGo.transform;
        group.SetParent(generated, false);
        group.localPosition = Vector3.zero;
        group.localRotation = Quaternion.identity;
        group.localScale = Vector3.one;

        Mats m = LoadMaterials();

        // 1) GEO_ Visual 재질 — 콜라이더·트랜스폼 불변.
        int shelfPainted = PaintList(refs.shelves, m.shelf, "shelves");
        int tablePainted = PaintList(refs.tables, m.table, "tables");
        int framePainted = PaintFrame ? PaintFrameVisuals(generated, group, m) : 0;
        CheckListMatchesNames(generated, group, "GEO_S8_Shelf_", shelfPainted, "shelves");
        CheckListMatchesNames(generated, group, "GEO_S8_Table_", tablePainted, "tables");

        float ceilingY = refs.archiveBounds.size.y > 0.01f ? refs.archiveBounds.max.y : FallbackCeilingY;

        // 2) 작업대 구역 — 바닥 덮개 + 스탠드 등.
        //    등 x·z와 칸 경계는 refs.tables(B의 작업대 격자)에서 뽑는다 — B 상수를 L에 옮겨 적지 않는다(수정 1).
        int tiles = 0, workLamps = 0;
        Bounds wz = refs.workZoneBounds;
        WorkGrid grid = WorkGridFromTables(generated, refs.tables);
        if (wz.size.x <= 0.01f || wz.size.z <= 0.01f)
        {
            Debug.LogError("[S8_Dress] refs.workZoneBounds 크기가 0이다 — 작업대 구역 조명·덮개를 건너뛴다(S8_Builder 확인).");
        }
        else if (grid == null)
        {
            Debug.LogError("[S8_Dress] refs.tables에서 작업대 격자를 뽑지 못했다 — 작업대 구역 조명·덮개를 건너뛴다(S8_Builder 확인).");
        }
        else
        {
            tiles = BuildWorkFloorTiles(group, wz, grid, m.workFloor);
            workLamps = BuildWorkLamps(group, wz, grid, ceilingY, m);
        }

        // 3) 책·혼합 자리 스탠드 등.
        int roleLamps = 0;
        roleLamps += BuildRoleLamp(group, generated, refs.roleBookAnchor, "Book", wz, ceilingY, m) ? 1 : 0;
        roleLamps += BuildRoleLamp(group, generated, refs.roleMixerAnchor, "Mixer", wz, ceilingY, m) ? 1 : 0;

        // 4) 순찰로 약한 조명 [판정 21] — workZoneBounds 밖 순찰로 구간, 벽 부착/천장 밀착(1차 책장 통로 벽등 흡수).
        int patrolLights = 0;
        if (refs.archiveBounds.size.x <= 0.01f || refs.archiveBounds.size.z <= 0.01f)
            Debug.LogError("[S8_Dress] refs.archiveBounds 크기가 0이다 — 순찰로 조명을 건너뛴다(S8_Builder 확인).");
        else
            patrolLights = BuildPatrolLights(group, generated, refs, ceilingY, m.patrolFixture);

        // 5) 대기실·콘솔·출구 약한 등.
        int utility = BuildUtilityLights(group, generated, refs, ceilingY, m);

        // 6) CCTV 카메라 몸체(선택, 콜라이더 0).
        int camHousings = BuildCameraHousings(group, refs.cctvCameraAnchors, m);

        // 7) 콜라이더 전수 제거(계약 K0-3 "콜라이더 0개").
        int stripped = StripColliders(group);
        int remaining = group.GetComponentsInChildren<Collider>(true).Length;
        if (remaining != 0)
            Debug.LogError($"[S8_Dress] S8_Dress 아래 콜라이더 {remaining}개가 남았다 — 계약 K0-3 위반.");

        Debug.Log($"[S8_Dress] 완료 — 재질: 책장 {shelfPainted} · 작업대 {tablePainted} · 틀(바닥·벽·천장·콘솔) {framePainted} / " +
                  $"VIS: 바닥 덮개 {tiles} · 작업 등 {workLamps} · 역할 등 {roleLamps} · 순찰로 등 {patrolLights} · 기타 등 {utility} · 카메라 몸체 {camHousings} / " +
                  $"조명 합계 {lightCount}(ForcePixel {pixelForcedCount}, 나머지 Auto) · 제거한 기본 콜라이더 {stripped} · 남은 콜라이더 {remaining}.");
    }

    // ── 1) GEO_ Visual 재질 ─────────────────────────────────────────────────────────────

    private static int PaintList(List<Transform> list, Material mat, string label)
    {
        if (list == null)
        {
            Debug.LogError($"[S8_Dress] refs.{label}가 null이다 — 재질을 건너뛴다.");
            return 0;
        }
        int ok = 0, fail = 0;
        foreach (Transform t in list)
        {
            if (PaintVisual(t, mat)) ok++;
            else fail++;
        }
        if (fail > 0)
            Debug.LogWarning($"[S8_Dress] refs.{label} {fail}개는 null이거나 '{VisualChildName}' 자식 MeshRenderer가 없어 재질을 못 입혔다.");
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

    /// <summary>바닥·벽·천장·콘솔 책상(GEO_S8_Floor_/Wall_/Ceil_/Desk_) Visual을 어둡게. 문(GEO_S8_Door_)·
    /// 팀 기믹(S8_Gimmicks 아래)·S8_Dress 아래는 대상이 아니다.</summary>
    private static int PaintFrameVisuals(Transform generated, Transform dressGroup, Mats m)
    {
        Transform gimmicks = generated.Find(GimmickGroupName);
        int n = 0;
        foreach (Transform t in generated.GetComponentsInChildren<Transform>(true))
        {
            if (IsUnder(t, gimmicks) || IsUnder(t, dressGroup)) continue;
            string name = t.name;
            Material mat = null;
            if (name.StartsWith("GEO_S8_Floor_", System.StringComparison.Ordinal)) mat = m.floor;
            else if (name.StartsWith("GEO_S8_Wall_", System.StringComparison.Ordinal)) mat = m.wall;
            else if (name.StartsWith("GEO_S8_Ceil_", System.StringComparison.Ordinal)) mat = m.ceiling;
            else if (name.StartsWith("GEO_S8_Desk_", System.StringComparison.Ordinal)) mat = m.consoleMetal;
            if (mat != null && PaintVisual(t, mat)) n++;
        }
        return n;
    }

    /// <summary>진단만: refs 리스트 개수와 이름 접두사로 찾은 개수가 다르면 경고(재질은 리스트 기준).</summary>
    private static void CheckListMatchesNames(Transform generated, Transform dressGroup, string prefix, int painted, string label)
    {
        Transform gimmicks = generated.Find(GimmickGroupName);
        int named = 0;
        foreach (Transform t in generated.GetComponentsInChildren<Transform>(true))
        {
            if (IsUnder(t, gimmicks) || IsUnder(t, dressGroup)) continue;
            if (t.name.StartsWith(prefix, System.StringComparison.Ordinal)) named++;
        }
        if (named != painted)
            Debug.LogWarning($"[S8_Dress] refs.{label}로 칠한 {painted}개 ≠ 이름 '{prefix}*' {named}개 — 리스트 누락/이름 규칙 확인(재질은 리스트 기준).");
    }

    private static bool IsUnder(Transform t, Transform root) => root != null && t.IsChildOf(root);

    // ── 2) 작업대 구역 ──────────────────────────────────────────────────────────────────

    /// <summary>작업대 격자에서 뽑은 등 위치(x 열 · z 행 묶음)와 덮개 칸 안쪽 경계(이웃 등 사이 가운데).</summary>
    private sealed class WorkGrid
    {
        public float[] lampX, lampZ, tileInnerX, tileInnerZ;
    }

    /// <summary>refs.tables 중심(generated 로컬 — Map4Build.CreateSolidBox가 GEO 위치를 상자 중심에 둔다)에서
    /// 열 x 중심·행 z 중심을 모아(0.01 반올림 중복 제거) 등 격자를 만든다. 빈터(빠진 칸)가 있어도 같은 열·행의
    /// 다른 작업대가 있으면 격자 칸은 유지된다(초안 수정 1 결과 48칸). 뽑지 못하면 null.</summary>
    private static WorkGrid WorkGridFromTables(Transform generated, List<Transform> tables)
    {
        if (tables == null || tables.Count == 0) return null;
        var xs = new SortedSet<float>();
        var zs = new SortedSet<float>();
        foreach (Transform t in tables)
        {
            if (t == null) continue;
            Vector3 c = generated.InverseTransformPoint(t.position);
            xs.Add(Mathf.Round(c.x * 100f) / 100f);
            zs.Add(Mathf.Round(c.z * 100f) / 100f);
        }
        if (xs.Count == 0 || zs.Count == 0) return null;

        // 행 z 중심 → 블록(가로 통로로 나뉨) → 블록 안에서 WorkLampRowsPerLamp개씩 묶은 가운데.
        var rows = new List<float>(zs);
        float minStep = float.MaxValue;
        for (int i = 1; i < rows.Count; i++) minStep = Mathf.Min(minStep, rows[i] - rows[i - 1]);
        var lampZ = new List<float>();
        int blockStart = 0;
        for (int i = 1; i <= rows.Count; i++)
        {
            bool blockEnds = i == rows.Count || rows[i] - rows[i - 1] > minStep * WorkRowBlockGapFactor;
            if (!blockEnds) continue;
            for (int k = blockStart; k < i; k += WorkLampRowsPerLamp)
            {
                int last = Mathf.Min(k + WorkLampRowsPerLamp, i) - 1;
                lampZ.Add((rows[k] + rows[last]) * 0.5f);
            }
            blockStart = i;
        }

        var g = new WorkGrid { lampX = new List<float>(xs).ToArray(), lampZ = lampZ.ToArray() };
        g.tileInnerX = Midpoints(g.lampX);
        g.tileInnerZ = Midpoints(g.lampZ);
        Debug.Log($"[S8_Dress] 작업대 {tables.Count}개에서 뽑은 격자 — 등 x {{{string.Join(",", g.lampX)}}} · " +
                  $"z {{{string.Join(",", g.lampZ)}}} · 칸 경계 x {{{string.Join(",", g.tileInnerX)}}} · z {{{string.Join(",", g.tileInnerZ)}}}.");
        return g;
    }

    private static float[] Midpoints(float[] v)
    {
        var r = new float[Mathf.Max(0, v.Length - 1)];
        for (int i = 0; i < r.Length; i++) r[i] = (v[i] + v[i + 1]) * 0.5f;
        return r;
    }

    private static List<float> Edges(float min, float max, float[] inner)
    {
        var e = new List<float> { min };
        foreach (float v in inner)
            if (v > e[e.Count - 1] + 0.01f && v < max - 0.01f) e.Add(v);
        e.Add(max);
        return e;
    }

    private static int BuildWorkFloorTiles(Transform group, Bounds wz, WorkGrid grid, Material mat)
    {
        List<float> xs = Edges(wz.min.x, wz.max.x, grid.tileInnerX);
        List<float> zs = Edges(wz.min.z, wz.max.z, grid.tileInnerZ);
        GameObject tilesGo = NewNode("VIS_S8_WorkFloor", group, Vector3.zero);
        float y = wz.min.y + WorkTileLift;
        int n = 0;
        for (int r = 0; r < zs.Count - 1; r++)
        {
            for (int c = 0; c < xs.Count - 1; c++)
            {
                float sx = xs[c + 1] - xs[c];
                float sz = zs[r + 1] - zs[r];
                Vector3 center = new Vector3((xs[c] + xs[c + 1]) * 0.5f, y, (zs[r] + zs[r + 1]) * 0.5f);
                // Quad(XY 평면, 앞면 −Z)를 X축 +90° 돌리면 앞면이 +Y — 로컬 Y 스케일이 월드 Z 크기가 된다.
                Prim(PrimitiveType.Quad, $"VIS_S8_WorkFloor_{r + 1:00}_{c + 1}", tilesGo.transform, center,
                     Quaternion.Euler(90f, 0f, 0f), new Vector3(sx, sz, 1f), mat);
                n++;
            }
        }
        return n;
    }

    private static int BuildWorkLamps(Transform group, Bounds wz, WorkGrid grid, float ceilingY, Mats m)
    {
        GameObject lampsGo = NewNode("VIS_S8_WorkLamps", group, Vector3.zero);
        int n = 0;
        for (int r = 0; r < grid.lampZ.Length; r++)
        {
            for (int c = 0; c < grid.lampX.Length; c++)
            {
                float x = grid.lampX[c], z = grid.lampZ[r];
                if (x < wz.min.x - 0.01f || x > wz.max.x + 0.01f || z < wz.min.z - 0.01f || z > wz.max.z + 0.01f)
                {
                    Debug.LogWarning($"[S8_Dress] 작업 등 ({x},{z})가 workZoneBounds 밖이라 건너뛴다 [확정 §3-11 '작업대 구역만 밝게'].");
                    continue;
                }
                Pendant(lampsGo.transform, $"VIS_S8_WorkLamp_{r + 1:00}_{c + 1}", new Vector3(x, WorkLampY, z),
                        ShadeDiameter, ceilingY, m.shade, m.cord,
                        LightType.Spot, WorkLampColor, WorkLampIntensity, WorkLampRange,
                        WorkLampSpotAngle, LightRenderMode.Auto);
                n++;
            }
        }
        return n;
    }

    // ── 3) 책·혼합 자리 등 ──────────────────────────────────────────────────────────────

    private static bool BuildRoleLamp(Transform group, Transform generated, Transform anchor, string label,
                                      Bounds wz, float ceilingY, Mats m)
    {
        if (anchor == null)
        {
            Debug.LogWarning($"[S8_Dress] 역할 앵커 {label}가 null이다 — 그 자리 스탠드 등을 건너뛴다.");
            return false;
        }
        Vector3 p = generated.InverseTransformPoint(anchor.position);
        Vector3 fwd = generated.InverseTransformDirection(anchor.forward);
        fwd.y = 0f;
        Vector3 towardTable = fwd.sqrMagnitude > 1e-6f ? -fwd.normalized : Vector3.zero;
        Vector3 pos = p + towardTable * RoleLampTowardTable + Vector3.up * RoleLampHeight;
        if (wz.size.x > 0.01f && (pos.x < wz.min.x - 0.5f || pos.x > wz.max.x + 0.5f || pos.z < wz.min.z - 0.5f || pos.z > wz.max.z + 0.5f))
            Debug.LogWarning($"[S8_Dress] 역할 등 {label} {pos}가 workZoneBounds 밖이다 — 앵커 위치 확인(등은 만든다).");
        Pendant(group, $"VIS_S8_RoleLamp_{label}", pos, RoleShadeDiameter, ceilingY, m.roleShade, m.cord,
                LightType.Spot, RoleLampColor, RoleLampIntensity, RoleLampRange,
                RoleLampSpotAngle, LightRenderMode.ForcePixel);
        return true;
    }

    // ── 4) 순찰로 약한 조명 [판정 21] ───────────────────────────────────────────────────

    /// <summary>refs.patrolWaypoints 구간(i→i+1, 마지막→첫 점 [계약 K9])을 workZoneBounds(xz) 밖 조각으로 잘라, 조각마다
    /// PatrolLightSpacing 이하 간격으로 약한 Point 등을 둔다. 벽 가까운 평행 조각은 벽 부착, 나머지는 천장 밀착 기구.
    /// 기구는 카메라보다 높이 달아 CCTV 시선(카메라 → 캡슐 중심 y1)을 가리지 않는다. archiveBounds(z ≤131.5, 탈출 공간 제외
    /// [판정 18]) 밖 등은 만들지 않는다. 콜라이더 없음(Prim이 즉시 제거).</summary>
    private static int BuildPatrolLights(Transform group, Transform generated, S8_Refs refs, float ceilingY, Material mat)
    {
        List<Transform> wps = refs.patrolWaypoints;
        if (wps == null || wps.Count < 2)
        {
            Debug.LogError("[S8_Dress] refs.patrolWaypoints가 null이거나 2개 미만이다 — 순찰로 조명을 건너뛴다(S8_Builder 확인).");
            return 0;
        }
        Bounds archive = refs.archiveBounds;
        Bounds wz = refs.workZoneBounds;
        bool haveWz = wz.size.x > 0.01f && wz.size.z > 0.01f;
        if (!haveWz)
            Debug.LogWarning("[S8_Dress] refs.workZoneBounds 크기가 0이다 — 순찰로 전 구간을 순찰로 조명 대상으로 한다.");

        // 카메라 앵커 최고 높이(generated 로컬). 기구 아랫면이 이보다 높으면 카메라 → 표적(아래) 직선에 걸리지 않는다.
        float camMaxY = float.NegativeInfinity;
        if (refs.cctvCameraAnchors != null)
            foreach (Transform c in refs.cctvCameraAnchors)
                if (c != null) camMaxY = Mathf.Max(camMaxY, generated.InverseTransformPoint(c.position).y);

        GameObject go = NewNode("VIS_S8_PatrolLights", group, Vector3.zero);
        var ranges = new List<Vector2>();
        int n = 0, wallCount = 0, skippedShort = 0, skippedOutside = 0, lowFixtures = 0;
        float minFixtureBottom = float.PositiveInfinity;
        for (int i = 0; i < wps.Count; i++)
        {
            Transform ta = wps[i], tb = wps[(i + 1) % wps.Count];   // 마지막 → 첫 점으로 닫는다 [계약 K9 · 판정 19]
            if (ta == null || tb == null)
            {
                Debug.LogWarning($"[S8_Dress] refs.patrolWaypoints[{i}] 또는 다음 점이 null이다 — 구간 {i + 1} 순찰로 조명을 건너뛴다.");
                continue;
            }
            Vector3 a3 = generated.InverseTransformPoint(ta.position);
            Vector3 b3 = generated.InverseTransformPoint(tb.position);
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z);
            float segLen = Vector2.Distance(a, b);
            if (segLen < 0.01f) continue;
            Vector2 dir = (b - a) / segLen;
            Vector3 along = new Vector3(dir.x, 0f, dir.y);
            Quaternion fixRot = Quaternion.LookRotation(along, Vector3.up); // 기구 로컬 z = 경로 방향, 로컬 x = 경로 가로(수평)

            ranges.Clear();
            if (haveWz) OutsideRangesXZ(a, b, wz, ranges);
            else ranges.Add(new Vector2(0f, 1f));

            int seq = 0;   // 구간 안 등 번호(조각이 둘이어도 이름이 겹치지 않게)
            foreach (Vector2 r in ranges)
            {
                Vector2 pa = Vector2.Lerp(a, b, r.x), pb = Vector2.Lerp(a, b, r.y);
                float pieceLen = (r.y - r.x) * segLen;
                if (pieceLen < PatrolMinPiece) { skippedShort++; continue; }
                int count = Mathf.Max(1, Mathf.CeilToInt(pieceLen / PatrolLightSpacing - 1e-4f));

                // 벽 부착 판정 — 조각이 x축 또는 z축과 평행하고 archiveBounds 안쪽 면이 PatrolWallMountMaxDist 안.
                bool wallMount = false;
                bool wallIsX = false;
                float wallCoord = 0f;
                Vector3 inward = Vector3.zero;   // 벽 면에서 방 안쪽 방향
                if (Mathf.Abs(dir.y) > 0.99f)            // z 방향으로 달림 → 옆벽(x 면)
                {
                    float dMin = pa.x - archive.min.x, dMax = archive.max.x - pa.x;
                    if (dMin <= PatrolWallMountMaxDist && dMin <= dMax) { wallMount = true; wallIsX = true; wallCoord = archive.min.x; inward = Vector3.right; }
                    else if (dMax <= PatrolWallMountMaxDist) { wallMount = true; wallIsX = true; wallCoord = archive.max.x; inward = Vector3.left; }
                }
                else if (Mathf.Abs(dir.x) > 0.99f)       // x 방향으로 달림 → 앞뒤 벽(z 면)
                {
                    float dMin = pa.y - archive.min.z, dMax = archive.max.z - pa.y;
                    if (dMin <= PatrolWallMountMaxDist && dMin <= dMax) { wallMount = true; wallCoord = archive.min.z; inward = Vector3.forward; }
                    else if (dMax <= PatrolWallMountMaxDist) { wallMount = true; wallCoord = archive.max.z; inward = Vector3.back; }
                }

                for (int k = 0; k < count; k++)
                {
                    Vector2 p = Vector2.Lerp(pa, pb, (k + 0.5f) / count);   // 칸 가운데 — 모퉁이에 두 구간 등이 겹치지 않는다
                    Vector3 lightPos, fixCenter, fixSize;
                    if (wallMount)
                    {
                        Vector3 onWall = wallIsX ? new Vector3(wallCoord, archive.min.y, p.y) : new Vector3(p.x, archive.min.y, wallCoord);
                        lightPos = onWall + inward * PatrolWallLightInset + Vector3.up * PatrolLightY;
                        fixSize = PatrolWallFixtureSize;
                        float top = ceilingY - PatrolWallFixtureDrop;
                        Vector3 onWallTop = new Vector3(onWall.x, top - fixSize.y * 0.5f, onWall.z);
                        fixCenter = onWallTop + inward * (fixSize.x * 0.5f);   // 뒷면이 벽 면에 닿는다
                    }
                    else
                    {
                        lightPos = new Vector3(p.x, archive.min.y + PatrolLightY, p.y);
                        fixSize = PatrolCeilFixtureSize;
                        float top = ceilingY - PatrolFixtureCeilGap;
                        fixCenter = new Vector3(p.x, top - fixSize.y * 0.5f, p.y);
                    }

                    if (!InsideBounds(archive, lightPos, 0.01f) || !InsideBounds(archive, fixCenter, 0.01f))
                    {
                        Debug.LogWarning($"[S8_Dress] 순찰로 등 {lightPos}가 archiveBounds 밖이라 건너뛴다(탈출 공간·벽 밖 제외 [판정 18]).");
                        skippedOutside++;
                        continue;
                    }
                    float fixBottom = fixCenter.y - fixSize.y * 0.5f;
                    minFixtureBottom = Mathf.Min(minFixtureBottom, fixBottom);
                    if (fixBottom < archive.min.y + PatrolFixtureMinBottom)
                        Debug.LogWarning($"[S8_Dress] 순찰로 등 기구 아랫면 {fixBottom:F2} < {PatrolFixtureMinBottom} — 뛰어서 닿을 수 있다(추측, 천장 높이 확인).");
                    if (!float.IsNegativeInfinity(camMaxY) && fixBottom < camMaxY + PatrolFixtureCamClearance)
                    {
                        lowFixtures++;
                        Debug.LogWarning($"[S8_Dress] 순찰로 등 기구 아랫면 {fixBottom:F2}가 카메라 최고 높이 {camMaxY:F2}+{PatrolFixtureCamClearance} 아래다 " +
                                         "— CCTV 시선을 가릴 수 있다(등은 만든다, 천장·카메라 높이 확인).");
                    }

                    seq++;
                    string name = $"VIS_S8_PatrolLight_{i + 1}_{seq:00}";
                    GameObject root = NewNode(name, go.transform, lightPos);
                    Prim(PrimitiveType.Cube, name + "_Fixture", root.transform, fixCenter - lightPos, fixRot, fixSize, mat);
                    AddLight(root, LightType.Point, PatrolLightColor, PatrolLightIntensity, PatrolLightRange, 0f, LightRenderMode.Auto);
                    n++;
                    if (wallMount) wallCount++;
                }
            }
        }
        Debug.Log($"[S8_Dress] 순찰로 조명 {n}개(벽 부착 {wallCount} · 천장 밀착 {n - wallCount}) — 짧은 조각 건너뜀 {skippedShort} · " +
                  $"archiveBounds 밖 건너뜀 {skippedOutside} · 기구 아랫면 최소 {(n > 0 ? minFixtureBottom.ToString("F2") : "-")} · " +
                  $"카메라 최고 y {(float.IsNegativeInfinity(camMaxY) ? "-" : camMaxY.ToString("F2"))} · 카메라보다 낮은 기구 {lowFixtures}.");
        return n;
    }

    /// <summary>선분 a→b(xz)에서 bounds의 xz 사각형 밖에 있는 조각들의 매개변수 구간(x = 시작 t, y = 끝 t)을 result에 넣는다.
    /// 사각형과 만나지 않으면 (0,1) 하나. 경계선 위를 지나는 선분은 안으로 본다.</summary>
    private static void OutsideRangesXZ(Vector2 a, Vector2 b, Bounds bounds, List<Vector2> result)
    {
        float t0 = 0f, t1 = 1f;
        bool miss = false;
        for (int axis = 0; axis < 2 && !miss; axis++)
        {
            float p = axis == 0 ? a.x : a.y;
            float d = axis == 0 ? b.x - a.x : b.y - a.y;
            float lo = axis == 0 ? bounds.min.x : bounds.min.z;
            float hi = axis == 0 ? bounds.max.x : bounds.max.z;
            if (Mathf.Abs(d) < 1e-6f)
            {
                if (p < lo || p > hi) miss = true;
                continue;
            }
            float ta = (lo - p) / d, tb = (hi - p) / d;
            if (ta > tb) { float s = ta; ta = tb; tb = s; }
            t0 = Mathf.Max(t0, ta);
            t1 = Mathf.Min(t1, tb);
        }
        if (miss || t0 >= t1) { result.Add(new Vector2(0f, 1f)); return; }
        if (t0 > 1e-4f) result.Add(new Vector2(0f, t0));
        if (t1 < 1f - 1e-4f) result.Add(new Vector2(t1, 1f));
    }

    private static bool InsideBounds(Bounds b, Vector3 p, float tol) =>
        p.x >= b.min.x - tol && p.x <= b.max.x + tol &&
        p.y >= b.min.y - tol && p.y <= b.max.y + tol &&
        p.z >= b.min.z - tol && p.z <= b.max.z + tol;

    // ── 5) 대기실·콘솔·출구 ─────────────────────────────────────────────────────────────

    private static int BuildUtilityLights(Transform group, Transform generated, S8_Refs refs, float ceilingY, Mats m)
    {
        int n = 0;
        if (refs.startAnchor != null)
        {
            Vector3 p = generated.InverseTransformPoint(refs.startAnchor.position);
            Pendant(group, "VIS_S8_FoyerLamp", new Vector3(p.x, p.y + FoyerLampHeight, p.z), ShadeDiameter, ceilingY,
                    m.shade, m.cord, LightType.Point, WorkLampColor, FoyerLampIntensity, FoyerLampRange, 0f,
                    LightRenderMode.Auto);
            n++;
        }
        else Debug.LogWarning("[S8_Dress] refs.startAnchor가 null — 대기실 등을 건너뛴다.");

        if (refs.roleMonitorAnchor != null)
        {
            Vector3 p = generated.InverseTransformPoint(refs.roleMonitorAnchor.position);
            GameObject glow = NewNode("VIS_S8_ConsoleGlow", group, new Vector3(p.x, p.y + ConsoleGlowHeight, p.z));
            AddLight(glow, LightType.Point, ConsoleGlowColor, ConsoleGlowIntensity, ConsoleGlowRange, 0f, LightRenderMode.Auto);
            n++;
        }
        else Debug.LogWarning("[S8_Dress] refs.roleMonitorAnchor가 null — 콘솔 빛을 건너뛴다.");

        if (refs.exitDoorAnchor != null)
        {
            Vector3 p = generated.InverseTransformPoint(refs.exitDoorAnchor.position);
            Pendant(group, "VIS_S8_ExitLamp", new Vector3(p.x, p.y + ExitLampHeight, p.z), ShadeDiameter, ceilingY,
                    m.shade, m.cord, LightType.Point, WorkLampColor, ExitLampIntensity, ExitLampRange, 0f,
                    LightRenderMode.Auto);
            n++;
        }
        else Debug.LogWarning("[S8_Dress] refs.exitDoorAnchor가 null — 출구 등을 건너뛴다.");
        return n;
    }

    // ── 6) CCTV 카메라 몸체 ─────────────────────────────────────────────────────────────

    private static int BuildCameraHousings(Transform group, List<Transform> anchors, Mats m)
    {
        if (anchors == null)
        {
            Debug.LogWarning("[S8_Dress] refs.cctvCameraAnchors가 null — 카메라 몸체를 건너뛴다.");
            return 0;
        }
        int n = 0;
        for (int i = 0; i < anchors.Count; i++)
        {
            Transform a = anchors[i];
            if (a == null) continue;
            Vector3 fwd = a.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(fwd, up);

            // 앵커(빈 GO, 계약 K0-3)에는 자식을 달지 않는다 — S8_Dress 아래에 월드 포즈로 둔다.
            GameObject root = new GameObject($"VIS_S8_CamHousing_{i + 1}");
            root.transform.SetParent(group, false);
            root.transform.SetPositionAndRotation(a.position - fwd * CamHousingBack, rot);

            Prim(PrimitiveType.Cube, $"VIS_S8_CamHousing_{i + 1}_Body", root.transform, Vector3.zero,
                 Quaternion.identity, CamHousingSize, m.camHousing);
            // 표시등: 몸체 윗면 앞쪽(렌즈보다 뒤 — 화면에 안 잡힘).
            Vector3 ledLocal = new Vector3(0f, CamHousingSize.y * 0.5f + CamLedDiameter * 0.3f,
                                           CamHousingSize.z * 0.5f - CamLedDiameter);
            Prim(PrimitiveType.Sphere, $"VIS_S8_CamHousing_{i + 1}_Led", root.transform, ledLocal,
                 Quaternion.identity, Vector3.one * CamLedDiameter, m.camLed);
            n++;
        }
        if (n != 3)
            Debug.LogWarning($"[S8_Dress] 카메라 몸체 {n}개 ≠ 계약 3개(cctvCameraAnchors 확인).");
        return n;
    }

    // ── 공용: 매달린 등 · 조명 · 도형 ───────────────────────────────────────────────────

    /// <summary>천장에서 줄로 매단 등. 루트(회전 없음) 아래 갓(원기둥)·줄(원기둥)·빛(자식, Spot이면 아래 향함).</summary>
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
        // innerSpotAngle은 넣지 않는다 — (추측) Built-in에서는 효과 없음, SRP 전용 속성(수정 1).
        if (type == LightType.Spot) light.spotAngle = spotAngle;
        // Built-in Forward 픽셀 조명 수 한계(QualitySettings Ultra 4 / Very High 3 / High 2 / Medium 1 / Low 0) —
        // ProjectSettings는 바꾸지 않고 오브젝트 설정으로만 대응:
        //  - 작업 등·순찰로 등·기타 = Auto: 렌더러마다 가장 중요한(가깝고 센) 조명부터 픽셀, 나머지는 버텍스/SH로 자동 강등.
        //    바닥 덮개를 등 1개당 1칸으로 나눠 칸마다 자기 등이 1순위 픽셀 조명이 되게 했다.
        //    순찰로 등은 바닥 덮개가 없다 — 큰 바닥 Visual 1장(GEO_S8_Floor_Main)에서는 몇 개만 픽셀로 그려지고(추정), 작은
        //    렌더러(관리자 캡슐·도형)는 가까운 순찰로 등이 픽셀 조명이 된다(추정). 목적(CCTV 식별)은 캡슐 쪽이다.
        //  - 책·혼합 자리 등 2개만 ForcePixel(품질이 낮아도 역할 자리는 밝게 — 2개라 비용 작음).
        light.renderMode = mode;
        light.shadows = LightShadows.None;                 // [추정] 등 87개(초안 기준) 그림자는 비용이 커서 끈다(연출 전용).
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

    /// <summary>시각 전용 도형 — 기본 콜라이더를 즉시 제거하고 그림자를 끈다.</summary>
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

    // ── 머티리얼(Standard, Materials/Generated/M4_S8_*.mat) ─────────────────────────────

    private static Mats LoadMaterials()
    {
        EnsureDir(MaterialDir);
        return new Mats
        {
            shelf = GetOrCreateMaterial("ShelfWood", ShelfWoodColor, 0.20f, 0f, Color.black),
            table = GetOrCreateMaterial("TableWood", TableWoodColor, 0.35f, 0f, Color.black),
            floor = GetOrCreateMaterial("ArchiveFloor", FloorColor, 0.15f, 0f, Color.black),
            wall = GetOrCreateMaterial("ArchiveWall", WallColor, 0.10f, 0f, Color.black),
            ceiling = GetOrCreateMaterial("ArchiveCeiling", CeilingColor, 0.05f, 0f, Color.black),
            consoleMetal = GetOrCreateMaterial("ConsoleMetal", ConsoleMetalColor, 0.50f, 0.60f, Color.black),
            workFloor = GetOrCreateMaterial("WorkFloor", WorkFloorColor, 0.25f, 0f, Color.black),
            shade = GetOrCreateMaterial("LampShade", WorkLampColor, 0.30f, 0f, WorkLampColor * LampShadeEmission),
            roleShade = GetOrCreateMaterial("RoleLampShade", RoleLampColor, 0.30f, 0f, RoleLampColor * LampShadeEmission),
            cord = GetOrCreateMaterial("LampCord", CordColor, 0.20f, 0f, Color.black),
            patrolFixture = GetOrCreateMaterial("PatrolLamp", PatrolLightColor, 0.30f, 0f, PatrolLightColor * PatrolFixtureEmission),
            camHousing = GetOrCreateMaterial("CamHousing", CamHousingColor, 0.45f, 0.50f, Color.black),
            camLed = GetOrCreateMaterial("CamLed", CamLedColor, 0.50f, 0f, CamLedColor * CamLedEmission),
        };
    }

    /// <summary>M4_S8_&lt;name&gt;.mat — 있으면 재사용, 없으면 Standard로 생성. 매번 값을 다시 써서 상수 표와 일치시킨다.
    /// emission이 검정이면 _EMISSION을 끈다.</summary>
    private static Material GetOrCreateMaterial(string name, Color albedo, float smoothness, float metallic, Color emission)
    {
        string path = $"{MaterialDir}/{MaterialPrefix}{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                Debug.LogError("[S8_Dress] Standard 셰이더를 찾지 못했다(Built-in 전용 프로젝트여야 한다).");
                return null;
            }
            mat = new Material(standard);
            AssetDatabase.CreateAsset(mat, path);
        }
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
