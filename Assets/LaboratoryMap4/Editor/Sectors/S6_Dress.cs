#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// S6 — 밝고 깨끗한 시험실 조명·재질(과제 S6-L → 2차 S6-L3 → 3차 S6-L4 [C14], 계약 진행/지시서/R3/_계약.md K0-3·K6 — 정본. R2S68 계약은 이력).
/// S6_Builder.Build가 Wire 다음 마지막에 `S6_Dress.Apply(generated, refs)`로 부른다.
///
/// 하는 일(렌더러·조명만 — 콜라이더 = 측정 지형 불변):
///  1) GEO_ 상자의 자식 `Visual` 렌더러에 S6 전용 Standard 머티리얼을 입힌다.
///     - refs.nonPortalWalls(벽·천장·우물 벽) → 밝은 무채색 벽·천장(설치 불가 면) [C14 — 어두운 무광 폐기].
///     - 그 밖의 GEO_S6_* → 바닥(윗면 y≈0)은 밝은 회색, 발판·선반·구덩이·구덩이 디딤 등은 더 밝은 회색(디딤 = 발판 재질 [C14]).
///     GEO_ 콜라이더·트랜스폼은 읽기만 한다. 팀 기믹(S6_Gimmicks 아래)의 렌더러·머티리얼은 건드리지 않는다
///     (팀 패널의 기본 색·설치된 포탈의 주황·파랑 그대로).
///  2) generated 바로 아래 그룹 `S6_Dress` 1개에 VIS_S6_… 등·장식을 만든다(콜라이더 0 — 생성 즉시 제거 + 마지막 전수 제거).
///     - 패널 뒤 받침판: 패널 면 뒤 빈 공간(구덩이·벽감) 안쪽 끝면에 밝은 민트 색조 판(설치 가능 자리 표시 — 포탈이
///       설치되면 팀 코드가 패널 메쉬를 끄므로 그때 보이는 것이 이 판이다).
///     - 고정 패널 둘레 얇은 녹색 계열 발광 테두리(PanelTrim, [C14] 유지). 밑에 받칠 면이 없는 변(구덩이 디딤 위 — 0.2 낮음)은 뺀다(16→13 [C14] 추인).
///     밝은 벽 위에서 설치 가능 패널은 밝기 차가 아니라 색조(민트·녹)와 테두리 선으로 읽힌다. 흰 패널 ↔ 어두운 벽 두 톤 대비는 쓰지 않는다 [C14].
///     - 천장 선형 등 6개(발광 판 + 점광원) · 패널 앞 보조등 · 출구 표시 띠·등.
///  팀 패널 오브젝트에는 자식을 달지 않는다 — 팀 PortalSurface.RefreshOccupancy가
///  GetComponentInChildren&lt;MeshRenderer&gt;()로 패널 메쉬를 끄고 켜므로(PortalSurface.cs:71-72) 자식 렌더러를 달면 그 동작이 바뀐다.
///
/// 전역 조명(Master 방향광·환경광·RenderSettings)은 읽지도 바꾸지도 않는다 [계약 K0-3 전역 조명].
/// [1차 판정 A1 · 판정 14] Master 방향광 소프트 그림자는 INF1-2가 켠다(Map4SceneBuilder.cs:486-487, 섹터 코드 불변).
/// 천장(y24~25)이 직사광을 막는다. 환경광 불변(계약 K0-3). 방 안이 얼마나 어두워지는지는 Unity 실측 전까지 (추정)이다.
/// 방향광 회전은 (50,−30,0)(Map4SceneBuilder.cs:478) — 빛 방향 약 (−0.32,−0.77,0.56)이라 M1 시작 면(법선 −X)·
/// 끝 면(+Z)은 그림자와 무관하게 방향광 내적 ≤ 0이다 [계산] → 패널 앞 보조등을 두는 근거는 그대로다.
/// 섹터 안 등(천장 등·보조등·출구 등)이 방 밝기를 맡는다.
///
/// 머티리얼: Assets/LaboratoryMap4/Materials/Generated/M4_S6_&lt;이름&gt;.mat — Standard, 있으면 재사용·없으면 생성
/// (S1_Lighting.cs·S8_Dress 방식). Map4Palette.asset·Map4_*.mat·팀 머티리얼은 수정하지 않는다(새 에셋만 만들고
/// 렌더러의 sharedMaterial 참조만 바꾼다).
///
/// 이름·주석·재질에 원작 게임 고유 표현을 쓰지 않는다(설계 §3-5). 무늬·격자·표지·글자 없이 단색 재질만 쓴다.
/// 형식 검사는 Unity 동봉 csc로만 했다(S6-L3·S6-L4, 산출물 scratchpad). Unity 컴파일·실행은 통합 후 대기열에서 확인.
/// </summary>
public static class S6_Dress
{
    // ════════════════════════════════════════════════════════════════════════════════════
    //  상수 표 — 초안(진행/초안/S6_배치초안.md '2차 반영 수정 1', GEO 46개) 기준. 크기·세기·색은 여기서만 고친다.
    //  좌표는 섹터 로컬(generated 기준): +Z 진행, x=0 중앙, y=0 = 섹터 바닥.
    //  방 경계·패널 자리는 refs(와 W가 놓은 팀 패널)에서, 출구 개구부 윗변은 GEO(인방)에서 읽는다.
    //  B·W·레이아웃 값을 옮겨 적은 결합 상수는 5개다 [계약 K6-0 R3 _계약.md:536] — 하나를 바꾸면 짝도 같이 고친다:
    //   - 항상 씀: NeighborGuardMinZ·NeighborGuardMaxZ(S5 통로 12 · S6 길이 98 + 통로 12 — 레이아웃 결합).
    //   - 읽기 실패 때만 씀(쓰면 LogWarning):
    //       FallbackCeilingY 24 = B S6_Builder.CeilY,
    //       FallbackExitOpeningHeight 4 = B S6_Builder.OpeningH — 계약 K6-0이 'ExitOpeningHeight'라 부르는 상수가 이것이다,
    //       FallbackPanelSize (3.0,3.6,0.2) = W FixedPanelSize·MovablePanelSize·MovableSpecs[1].pivotPointLocal.y(= −반높이)
    //         ↔ B PanelHalfW/H·구덩이·벽감 구멍 [판정 5 — 3.0×3.6 수용].
    //  색은 sRGB로 적는다(프로젝트 색 공간 Linear — ProjectSettings m_ActiveColorSpace 1 — Unity가 변환).
    // ════════════════════════════════════════════════════════════════════════════════════

    // ── 전체 스위치 ─────────────────────────────────────────────────────────────────────
    /// <summary>[C14 유지 — 기능적 표시] 고정 패널 둘레에 얇은 발광 테두리를 둘지(설치 가능 자리 찾기 쉽게). 무늬 없는 단색 선 4개
    /// (받칠 면 없는 변은 뺀다 — 16→13 [C14] 추인).</summary>
    private static readonly bool PanelTrim = true;
    /// <summary>[제안] 벽·천장 외의 GEO(바닥·발판·선반)도 밝게 칠할지. B는 Map4Palette 'Floor'(0.20 어두움)를
    /// 쓰므로(Map4Palette.asset:16-19) 끄면 바닥이 어둡게 남는다.</summary>
    private static readonly bool PaintSurfaces = true;

    // ── 인접 섹터로 빛이 새지 않게 — 우리 점광원은 shadows = None(AddLight)이라 벽을 통과한다 ──────
    //    (Master 방향광 그림자를 켜도[1차 판정 A1] 점광원은 그대로 통과. 점광원 그림자는 켜지 않는다 — 비용·Ultra 픽셀 조명 4.)
    /// <summary>[계산] S5 방 끝 z = −12(S5 연결 통로 길이 12, Map4Layout.connectorLengthToNext·원점 Z 누적 :51-58).
    /// 등 구가 이 면을 넘지 않게 범위를 줄인다.</summary>
    private const float NeighborGuardMinZ = -12f;
    /// <summary>[계산] S7 시작 z = S6 길이 98 + 연결 통로 12 = 110(프롬프트 15 현재값).</summary>
    private const float NeighborGuardMaxZ = 110f;
    /// <summary>[제안] 경계 면에서 남기는 여유.</summary>
    private const float NeighborGuardMargin = 0.5f;

    // ── 천장 선형 등(발광 판 + 점광원) — 2열 × 3행 = 6개 ───────────────────────────────
    // 자리는 refs.roomInnerBounds에서 뽑는다: x = 중심 ± 폭/4, z = min.z + 길이 × {0.2, 0.5, 0.8}.
    // 초안 2차 반영 수정 1 기준 결과(대조용 [계산] — 방·천장은 수정 2와 같음): x {−15, 15} · z {22, 49, 76}, 천장 아랫면 y24.
    /// <summary>[제안] 열 위치 = 중심 ± 폭 × 이 비율.</summary>
    private const float CeilColumnFraction = 0.25f;
    /// <summary>[제안] 행 위치 비율(방 길이 기준). 양 끝 행을 안으로 당겨 인접 섹터 경계 여유를 늘린다.</summary>
    private static readonly float[] CeilRowFractions = { 0.2f, 0.5f, 0.8f };
    /// <summary>[제안] 발광 판 크기(x 폭 · y 두께 · z 길이).</summary>
    private static readonly Vector3 CeilLampSize = new Vector3(1.0f, 0.12f, 12f);
    /// <summary>[제안] 판 아랫면에서 빛 위치까지.</summary>
    private const float CeilLightDrop = 0.3f;
    /// <summary>[추정] 원하는 범위 — 바닥(아래 23.6)까지 닿게. 양 끝 행은 인접 섹터 경계로 33.5까지 줄어든다[계산].</summary>
    private const float CeilLightRange = 45f;
    /// <summary>[추정] 세기 — 설계서에 값 없음, 실측 조정 대상.</summary>
    private const float CeilLightIntensity = 2.0f;
    /// <summary>[제안] 차가운 흰빛(주황·파랑 아님 — 포탈 색과 섞이지 않게).</summary>
    private static readonly Color CeilLightColor = new Color(0.95f, 0.97f, 1.00f);
    /// <summary>[초안 §2-1 · 계약 K6-0 결합 상수 — B S6_Builder.CeilY 24를 옮겨 적음] 천장 아랫면 y.
    /// refs.roomInnerBounds 높이가 0일 때만 쓰는 대체값이다(쓰면 LogWarning). 평소에는 refs에서 읽는다.</summary>
    private const float FallbackCeilingY = 24f;

    // ── 패널(고정·움직이는) ─────────────────────────────────────────────────────────────
    /// <summary>[초안 §4 제안 · 판정 5 수용 · 계약 K6-0 결합 상수] 패널 크기(가로 x · 세로 y · 두께 z) — W가 놓은 팀 패널을
    /// 찾지 못할 때만 쓴다. 찾으면 팀 PortalSurface.Box.size(public, PortalSurface.cs:20)를 읽는다(읽기만).
    /// 짝: W FixedPanelSize·MovablePanelSize·MovableSpecs[1].pivotPointLocal.y(= −반높이) ↔ B PanelHalfW/H·구덩이·벽감 구멍.</summary>
    private static readonly Vector3 FallbackPanelSize = new Vector3(3.0f, 3.6f, 0.2f);
    /// <summary>[제안] 팀 패널과 앵커를 같은 것으로 보는 거리(면 중심끼리)·방향 일치(내적) 기준.</summary>
    private const float PanelMatchDistance = 0.35f;
    private const float PanelMatchDot = 0.98f;

    // 패널 뒤 받침판 — 패널 면 뒤 빈 공간(초안: 구덩이·벽감 깊이 1.2)의 안쪽 끝면을 찾아 그 앞에 얇은 판을 둔다.
    /// <summary>[제안] 받침 찾기 범위(패널 면에서 뒤로). 초안 빈 공간 깊이 1.2 [초안 §3-1 #9·#10·#11·#30]를 덮는다.</summary>
    private const float BackingProbeNear = 0.25f;
    private const float BackingProbeFar = 2.0f;
    /// <summary>[제안] 받침 찾기 발자국을 패널 둘레에서 안으로 줄이는 값 — 벽감 문설주·구덩이 옆면을 받침으로 잡지 않게.
    /// [계산 S6-L3 · 판정 5] 구덩이 디딤 3개(Pit_P1/P3/P4_Step)는 패널 발자국과 변에서 맞닿기만(틈 0) 하므로
    /// 이 줄임으로 빠진다 → P1·P4 받침 = Pit_P*_Bottom 윗면 −1.2, P3 받침 = Pad1_Base 윗면 3.8(깊이 모두 1.2).</summary>
    private const float BackingProbeShrink = 0.05f;
    /// <summary>[계산] 판을 받침 면 앞으로 띄우는 값·판 두께 — 거리 30·근평면 0.3·24비트 깊이 정밀도 약 0.0002의 25배 이상.</summary>
    private const float BackingPlateGap = 0.005f;
    private const float BackingPlateThickness = 0.01f;
    /// <summary>[제안] 판 가장자리를 빈 공간 옆면에서 들이는 값(옆면과 같은 평면에 붙지 않게).</summary>
    private const float BackingPlateInset = 0.01f;

    // 고정 패널 테두리(PanelTrim).
    /// <summary>[제안] 테두리 선 폭·두께(면에서 앞으로). 패널 바깥에 둔다 — 포탈 영역(패널 안쪽)을 가리지 않는다.</summary>
    private const float TrimWidth = 0.12f;
    private const float TrimThickness = 0.02f;
    /// <summary>[제안 S6-L3] 테두리 변 하나를 받칠 면 찾기 깊이 — 변 중심에서 패널 면 뒤로 이만큼 들어간 점이 GEO 안이면 둔다.
    /// 판정 5의 구덩이 디딤(윗면 = 패널 밑면, 패널 면보다 0.2 낮음) 위 변은 떠 보이므로 뺀다 [계산: P1 남·P3 북·P4 서 변 3개].</summary>
    private const float TrimSupportProbe = 0.05f;

    // 패널 앞 보조등(Point) — 고정 패널 전부 + 움직이는 패널 시작 포즈(조준하는 포즈, 초안 §2-4·§2-5).
    /// <summary>[제안] 패널 면에서 앞(법선)으로 떨어진 거리.</summary>
    private const float PanelLightDistance = 2.5f;
    /// <summary>[제안] 범위 — 패널(최대 대각 약 4.7)과 둘레 바닥·벽까지.</summary>
    private const float PanelLightRange = 7f;
    /// <summary>[추정] 세기 — 실측 조정.</summary>
    private const float PanelLightIntensity = 1.2f;
    private static readonly Color PanelLightColor = new Color(1.00f, 1.00f, 1.00f); // [제안] 중성 흰빛
    /// <summary>[제안] 움직이는 패널의 이동 경로(시작→끝 포즈 사이 전체) 검사 여유. 보조등이 경로 상자 + 이 여유 안이면
    /// 패널 위(up)·아래·옆 순으로 '반치수 + 2 × 여유'만큼 옮긴다(수정 1 — 두 포즈만 보던 것을 경로 전체로).
    /// [판정 2 · 계산 S6-L3] 2차 M1(경첩 (−4.5,12.9,57.9)·반지름 6.5, 수평 반경 5.0~8.0): 기본 자리 (−7.1,12.9,51.4)는
    /// 경첩 수평 거리 7.0이라 경로 안 → 위로 2.8 옮겨 (−7.1,15.7,51.4). 시작 패널 중심까지 3.82 < 범위 7.</summary>
    private const float PanelLightSweepClearance = 0.5f;
    /// <summary>[제안] 경로 표본 간격 — 회전은 각도(°), 밀기는 거리.</summary>
    private const float SweepStepDeg = 0.5f;
    private const float SweepStepDist = 0.25f;

    // ── 출구 표시(출구 개구부 위 벽 면의 발광 띠 + 점광원) ─────────────────────────────────
    // 출구 개구부 윗변(인방 아랫면)은 GEO에서 찾는다(수정 1): exitLanding (x) 위, 출구 벽 안쪽 면(roomInnerBounds.max.z)
    // 바로 뒤 ExitProbeBehindWall 지점을 덮는 GEO 중 exitLanding보다 높은 가장 낮은 밑면. 초안 결과 [계산]:
    // GEO_S6_Wall_North_Lintel 밑면 y20(천장 24보다 낮음). 못 찾을 때만 아래 대체값을 쓴다(LogWarning).
    /// <summary>[제안] 출구 벽 안쪽 면에서 벽 속으로 들어가 찾는 깊이(초안 출구 벽 두께 2 [초안 §3-1 #22~#25] 안).</summary>
    private const float ExitProbeBehindWall = 0.05f;
    /// <summary>[초안 §3-1 #25 제안 · 계약 K6-0 결합 상수 — B S6_Builder.OpeningH 4를 옮겨 적음] 출구 개구부 높이.
    /// 계약 K6-0(R3 _계약.md:536)의 'ExitOpeningHeight'가 이 상수다(수정 1에서 대체값으로 내리며 이름을 바꿈).
    /// GEO에서 인방을 찾지 못할 때만 쓰는 대체값이다(쓰면 LogWarning).</summary>
    private const float FallbackExitOpeningHeight = 4f;
    /// <summary>[제안] 띠 크기(x 폭 · y 높이 · z 두께) · 인방 아랫면에서 띠 중심까지.</summary>
    private static readonly Vector3 ExitStripSize = new Vector3(6f, 0.3f, 0.06f);
    private const float ExitStripAboveOpening = 0.4f;
    /// <summary>[제안] 점광원: refs.exitLanding 위 높이 · 범위 · 세기[추정].</summary>
    private const float ExitLightHeight = 3.5f;
    private const float ExitLightRange = 8f;
    private const float ExitLightIntensity = 1.0f;

    // ── 머티리얼(Standard) 색 — sRGB ────────────────────────────────────────────────────
    /// <summary>[제안 · C14] 설치 불가 면(nonPortalWalls: 벽·천장·우물 벽) — 밝은 무채색(깨끗하고 밝은 시험실 [확정 §3-5]).
    /// 어두운 무광(0.34,0.36,0.38)은 폐기 [C14]. 조건: sRGB 채널 ≥ 0.75·채널 차 ≤ 0.04·광택 0.1~0.35·금속 0·발광 0 [명령 결정].
    /// 팀 패널 기본색(0.4,0.9,0.6)·광택 0.5(PortalSurface.mat:69·81)와는 밝기가 아니라 색조(채도)·광택으로 갈린다 [계산 S6-L4 대비 표].</summary>
    private static readonly Color WallLightColor = new Color(0.84f, 0.85f, 0.86f);
    private const float WallLightSmoothness = 0.20f;
    /// <summary>[제안] 방 바닥(윗면 y≈0) — 밝은 회색(깨끗한 시험실).</summary>
    private static readonly Color FloorLightColor = new Color(0.80f, 0.81f, 0.83f);
    private const float FloorLightSmoothness = 0.35f;
    /// <summary>[제안] 발판·선반·출구 통로 바닥·구덩이 — 바닥보다 조금 더 밝게(높이 단계가 읽히게).
    /// 구덩이 디딤(Pit_P1/P4_Step 윗면 −0.2, Pit_P3_Step 4.8)도 여기에 든다 — 디딤 = 발판 재질 [C14 확정].</summary>
    private static readonly Color PlatformLightColor = new Color(0.90f, 0.90f, 0.91f);
    private const float PlatformLightSmoothness = 0.30f;
    /// <summary>[제안 · C14 색조로 구분] 패널 뒤 받침판 — 옅은 민트(팀 패널 기본색 계열, 설치 가능 자리와 한 식구로 보이게).
    /// 순백 아님. 밝은 벽(무채색)과 색조가 보이게 채도를 조금 올림(0.86,0.96,0.91 → 아래) [계산 S6-L4: HSV S 0.10 → 0.16].</summary>
    private static readonly Color PanelBackingColor = new Color(0.80f, 0.95f, 0.87f);
    private const float PanelBackingEmission = 0.15f;     // [추정] 빈 공간 안은 빛을 덜 받아 조금 스스로 밝힘
    /// <summary>[제안 · C14 · 팀 _Color (0.4,0.9,0.6) 방향] 테두리 — 흰색(0.95,0.97,1.00)은 밝은 벽 위에서 안 보여 녹색 계열로 바꿈.
    /// 팀 패널보다 조금 짙게 해 패널 가장자리 선으로 읽히게. 색상 H 약 150° — 주황(30°)·파랑(216°)과 겹치지 않음 [계산].</summary>
    private static readonly Color PanelTrimColor = new Color(0.30f, 0.80f, 0.55f);
    private const float PanelTrimEmission = 0.6f;         // [추정]
    private static readonly Color CeilLampColor = new Color(0.96f, 0.98f, 1.00f);  // [제안]
    private const float CeilLampEmission = 1.5f;          // [추정]
    private static readonly Color ExitGlowColor = new Color(0.96f, 0.98f, 1.00f);  // [제안]
    private const float ExitGlowEmission = 1.2f;          // [추정]

    // ── 이름 ─────────────────────────────────────────────────────────────────────────────
    private const string GroupName = "S6_Dress";          // [계약 K0-3]
    private const string GimmickGroupName = "S6_Gimmicks";// [계약 K0-3] W 그룹 — 이 아래는 칠하지 않는다
    private const string GeoPrefix = "GEO_S6_";           // [계약 K0-3]
    private const string VisualChildName = "Visual";      // Map4Build.CreateSolidBox(:450)가 붙이는 자식 이름
    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated";
    private const string MaterialPrefix = "M4_S6_";       // [계약 K0-3]
    /// <summary>[제안] 윗면 y가 이 값 안이면 '방 바닥'으로 본다.</summary>
    private const float FloorTopTolerance = 0.05f;

    // ════════════════════════════════════════════════════════════════════════════════════

    private sealed class Mats
    {
        public Material wallLight, floorLight, platformLight, panelBacking, panelTrim, ceilLamp, exitGlow;
    }

    /// <summary>패널 한 장의 면 포즈(월드)와 크기. 팀 패널을 찾았으면 그 값, 아니면 앵커 + 대체 크기.</summary>
    private struct PanelPose
    {
        public Vector3 face;      // 면 중심(월드)
        public Vector3 right, up, fwd;
        public Vector3 size;      // 가로·세로·두께
        public bool fromTeam;
    }

    private static int lightCount;
    private static int rangeClampedCount;

    /// <summary>계약 K6-2. generated = S6 Generated 루트(섹터 로컬), refs = S6_Builder가 채운 참조.</summary>
    public static void Apply(Transform generated, S6_Refs refs)
    {
        if (generated == null || refs == null)
        {
            Debug.LogError("[S6_Dress] generated 또는 refs가 null이다 — 호출부(S6_Builder) 확인 필요. 조명·재질을 건너뛴다.");
            return;
        }

        lightCount = 0;
        rangeClampedCount = 0;

        // 멱등: 이전 S6_Dress가 남아 있으면 지우고 새로 만든다(Generated는 보통 매번 비워진다).
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
        List<Transform> geos = CollectGeo(generated, gimmicks, group);

        // 1) GEO_ Visual 재질 — 콜라이더·트랜스폼 불변.
        HashSet<Transform> wallSet;
        int wallPainted = PaintNonPortalWalls(refs.nonPortalWalls, m.wallLight, out wallSet);
        int floorPainted = 0, platformPainted = 0;
        if (PaintSurfaces) PaintOtherGeo(generated, geos, wallSet, m, out floorPainted, out platformPainted);

        // 2) 패널 — 받침판·테두리·보조등. 팀 패널(W가 Wire에서 놓음)은 읽기만 한다.
        PortalSurface[] teamPanels = generated.GetComponentsInChildren<PortalSurface>(true);
        int plates = 0, trims = 0, trimSegments = 0, panelLights = 0;
        GameObject panelsGo = NewNode("VIS_S6_Panels", group, Vector3.zero);

        if (refs.fixedPanelAnchors == null)
            Debug.LogError("[S6_Dress] refs.fixedPanelAnchors가 null이다 — 고정 패널 받침·테두리·보조등을 건너뛴다.");
        else
        {
            for (int k = 0; k < refs.fixedPanelAnchors.Count; k++)
            {
                Transform a = refs.fixedPanelAnchors[k];
                if (a == null) { Debug.LogWarning($"[S6_Dress] fixedPanelAnchors[{k}]가 null — 건너뛴다."); continue; }
                PanelPose p = ResolvePanel(a, teamPanels, null, $"고정 {k + 1}");
                string tag = $"Fixed_{k + 1}";
                if (BuildBackingPlate(panelsGo.transform, generated, geos, p, tag, m.panelBacking)) plates++;
                if (PanelTrim) { trimSegments += BuildTrim(panelsGo.transform, geos, p, tag, m.panelTrim); trims++; }
                BuildPanelLight(panelsGo.transform, generated, p, tag, null); panelLights++;
            }
        }

        if (refs.movablePanelAnchors == null)
            Debug.LogError("[S6_Dress] refs.movablePanelAnchors가 null이다 — 움직이는 패널 받침·보조등을 건너뛴다.");
        else
        {
            int endCount = refs.movablePanelTravelEnd != null ? refs.movablePanelTravelEnd.Count : 0;
            if (endCount != refs.movablePanelAnchors.Count)
                Debug.LogWarning($"[S6_Dress] movablePanelTravelEnd {endCount}개 ≠ movablePanelAnchors {refs.movablePanelAnchors.Count}개(계약 K6-1) — 끝 포즈 받침은 있는 것만.");
            for (int k = 0; k < refs.movablePanelAnchors.Count; k++)
            {
                Transform a = refs.movablePanelAnchors[k];
                if (a == null) { Debug.LogWarning($"[S6_Dress] movablePanelAnchors[{k}]가 null — 건너뛴다."); continue; }
                // 시작 포즈: 편집 시점의 팀 패널이 여기 있다(Wire 직후). 조준하는 포즈라 보조등을 둔다.
                PanelPose start = ResolvePanel(a, teamPanels, null, $"움직이는 {k + 1} 시작");
                string tag = $"Mov_{k + 1}";
                if (BuildBackingPlate(panelsGo.transform, generated, geos, start, tag + "_Start", m.panelBacking)) plates++;

                // 끝 포즈: 팀 패널은 시작 포즈에 있으므로 크기만 시작 포즈에서 빌린다. 테두리·보조등 없음(움직이는 패널).
                PanelPose? end = null;
                if (k < endCount && refs.movablePanelTravelEnd[k] != null)
                {
                    PanelPose e = ResolvePanel(refs.movablePanelTravelEnd[k], null, start, $"움직이는 {k + 1} 끝");
                    end = e;
                    if (BuildBackingPlate(panelsGo.transform, generated, geos, e, tag + "_End", m.panelBacking)) plates++;
                }
                // 보조등은 시작 포즈 앞에 두되, 시작→끝 이동 경로 전체를 피한다(수정 1).
                BuildPanelLight(panelsGo.transform, generated, start, tag, end); panelLights++;
            }
        }

        // 3) 천장 선형 등.
        int ceilLamps = BuildCeilingLamps(group, refs.roomInnerBounds, m.ceilLamp);

        // 4) 출구 표시.
        int exitParts = BuildExitMarker(group, generated, refs, geos, m.exitGlow);

        // 5) 콜라이더 전수 제거(계약 K0-3 "콜라이더 0개").
        int stripped = StripColliders(group);
        int remaining = group.GetComponentsInChildren<Collider>(true).Length;
        if (remaining != 0)
            Debug.LogError($"[S6_Dress] S6_Dress 아래 콜라이더 {remaining}개가 남았다 — 계약 K0-3 위반.");

        Debug.Log($"[S6_Dress] 완료 — 재질: 밝은 벽·천장(설치 불가 면) {wallPainted} · 바닥 {floorPainted} · 발판·선반 등 {platformPainted} / " +
                  $"VIS: 받침판 {plates} · 테두리 {trims}(변 {trimSegments}) · 패널 보조등 {panelLights} · 천장 등 {ceilLamps} · 출구 {exitParts} / " +
                  $"점광원 합계 {lightCount}(전부 Auto·점광원 그림자 없음, 인접 섹터 경계로 범위 줄인 등 {rangeClampedCount}) · " +
                  $"팀 패널 {teamPanels.Length}개 읽음 · 제거한 기본 콜라이더 {stripped} · 남은 콜라이더 {remaining}.");
    }

    // ── 1) GEO_ Visual 재질 ─────────────────────────────────────────────────────────────

    /// <summary>generated 아래 GEO_S6_* 중 자식 Visual MeshRenderer가 있는 것(팀 기믹·S6_Dress 아래 제외).</summary>
    private static List<Transform> CollectGeo(Transform generated, Transform gimmicks, Transform dressGroup)
    {
        var list = new List<Transform>();
        foreach (Transform t in generated.GetComponentsInChildren<Transform>(true))
        {
            if (IsUnder(t, gimmicks) || IsUnder(t, dressGroup)) continue;
            if (!t.name.StartsWith(GeoPrefix, System.StringComparison.Ordinal)) continue;
            if (VisualRenderer(t) == null) continue;
            list.Add(t);
        }
        return list;
    }

    private static int PaintNonPortalWalls(List<Transform> walls, Material mat, out HashSet<Transform> set)
    {
        set = new HashSet<Transform>();
        if (walls == null)
        {
            Debug.LogError("[S6_Dress] refs.nonPortalWalls가 null이다 — 설치 불가 면 재질을 건너뛴다.");
            return 0;
        }
        if (walls.Count == 0)
            Debug.LogWarning("[S6_Dress] refs.nonPortalWalls가 비었다(계약 K6-1 '1개 이상').");
        int ok = 0, fail = 0;
        foreach (Transform t in walls)
        {
            if (t != null) set.Add(t);
            if (PaintVisual(t, mat)) ok++;
            else fail++;
        }
        if (fail > 0)
            Debug.LogWarning($"[S6_Dress] refs.nonPortalWalls {fail}개는 null이거나 '{VisualChildName}' 자식 MeshRenderer가 없어 재질을 못 입혔다.");
        return ok;
    }

    /// <summary>nonPortalWalls가 아닌 GEO_S6_*: 윗면 y≈0 → 바닥 재질, 나머지(발판·선반·구덩이·출구 통로) → 발판 재질.
    /// [계산 S6-L3 · 초안 2차 GEO 46] 벽 21(nonPortalWalls) · 바닥 10(Floor_Vestibule, Floor_Room_1~9) · 발판 15.
    /// 판정 5 새 GEO: Floor_Room_8·_9(윗면 0) → 바닥, Pit_P1_Step·Pit_P4_Step(−0.2)·Pit_P3_Step(4.8) → 발판 [C14 확정].
    /// 구덩이 디딤 재질 = 발판 재질 [C14 — 초안 :688 '바닥 재질로 추정'은 기각, 검토 요청 종결].</summary>
    private static void PaintOtherGeo(Transform generated, List<Transform> geos, HashSet<Transform> wallSet, Mats m,
                                      out int floors, out int platforms)
    {
        floors = 0;
        platforms = 0;
        foreach (Transform t in geos)
        {
            if (wallSet.Contains(t)) continue;
            MeshRenderer mr = VisualRenderer(t);
            Bounds lb = ToLocalBounds(generated, mr.bounds);
            bool isFloor = Mathf.Abs(lb.max.y) <= FloorTopTolerance;
            mr.sharedMaterial = isFloor ? m.floorLight : m.platformLight;
            if (isFloor) floors++;
            else platforms++;
        }
    }

    /// <summary>GEO_ 상자의 자식 Visual 렌더러 재질만 바꾼다(콜라이더·트랜스폼 불변).</summary>
    private static bool PaintVisual(Transform geo, Material mat)
    {
        if (mat == null) return false;
        MeshRenderer mr = VisualRenderer(geo);
        if (mr == null) return false;
        mr.sharedMaterial = mat;
        return true;
    }

    private static MeshRenderer VisualRenderer(Transform geo)
    {
        if (geo == null) return null;
        Transform vis = geo.Find(VisualChildName);
        return vis != null ? vis.GetComponent<MeshRenderer>() : null;
    }

    private static bool IsUnder(Transform t, Transform root) => root != null && t.IsChildOf(root);

    // ── 2) 패널 ─────────────────────────────────────────────────────────────────────────

    /// <summary>앵커(면 중심, forward = 바깥 법선 [계약 K6-1])에 맞는 팀 패널을 찾아 포즈·크기를 정한다.
    /// sizeDonor가 있으면(움직이는 패널 끝 포즈) 그 크기를 쓴다. 팀 패널의 public 멤버(Box·transform)만 읽는다.</summary>
    private static PanelPose ResolvePanel(Transform anchor, PortalSurface[] teamPanels, PanelPose? sizeDonor, string label)
    {
        var p = new PanelPose
        {
            face = anchor.position,
            right = anchor.right,
            up = anchor.up,
            fwd = anchor.forward,
            size = FallbackPanelSize,
            fromTeam = false,
        };
        if (sizeDonor.HasValue)
        {
            p.size = sizeDonor.Value.size;
            p.fromTeam = sizeDonor.Value.fromTeam;
            return p;
        }

        PortalSurface best = null;
        float bestDist = PanelMatchDistance;
        if (teamPanels != null)
        {
            foreach (PortalSurface ps in teamPanels)
            {
                if (ps == null) continue;
                BoxCollider box = ps.Box;
                if (box == null) continue;
                Transform pt = ps.transform;
                if (Vector3.Dot(pt.forward, anchor.forward) < PanelMatchDot) continue;
                // 팀 규약: 바깥 법선 = 로컬 +Z, 면 = 중심에서 +Z로 size.z/2(PortalSurface.cs:10-14, S6_팀API §2-1).
                Vector3 face = pt.TransformPoint(box.center + new Vector3(0f, 0f, box.size.z * 0.5f));
                float d = Vector3.Distance(face, anchor.position);
                if (d < bestDist) { bestDist = d; best = ps; }
            }
        }

        if (best == null)
        {
            Debug.LogWarning($"[S6_Dress] 패널 {label}: 앵커 자리에서 팀 패널을 찾지 못했다(W 확인) — 대체 크기 {FallbackPanelSize}로 받침·테두리를 만든다.");
            return p;
        }

        Transform bt = best.transform;
        Vector3 s = best.Box.size;
        p.face = bt.TransformPoint(best.Box.center + new Vector3(0f, 0f, s.z * 0.5f));
        p.right = bt.right;
        p.up = bt.up;
        p.fwd = bt.forward;
        p.size = s;
        p.fromTeam = true;
        if (Mathf.Abs(s.x - FallbackPanelSize.x) > 0.01f || Mathf.Abs(s.y - FallbackPanelSize.y) > 0.01f)
            Debug.LogWarning($"[S6_Dress] 패널 {label}: 팀 패널 크기 {s} ≠ 초안 {FallbackPanelSize}[초안 §4] — B의 빈 공간(구덩이·벽감)과 크기가 어긋나는지 확인.");
        return p;
    }

    /// <summary>패널 면 뒤(−법선) BackingProbeNear~Far 안에서 가장 가까운 GEO 면을 찾아 그 앞에 받침판을 둔다.
    /// 찾지 못하면(공중 패널 등) 만들지 않는다.</summary>
    private static bool BuildBackingPlate(Transform parent, Transform generated, List<Transform> geos, PanelPose p,
                                          string tag, Material mat)
    {
        float hw = p.size.x * 0.5f - BackingProbeShrink;
        float hh = p.size.y * 0.5f - BackingProbeShrink;
        if (hw <= 0f || hh <= 0f) return false;

        // 받침 찾기 상자(월드 AABB — 앵커가 축에 맞춰져 있으면 정확).
        Vector3 probeCenter = p.face - p.fwd * ((BackingProbeNear + BackingProbeFar) * 0.5f);
        Vector3 probeHalf = new Vector3(hw, hh, (BackingProbeFar - BackingProbeNear) * 0.5f);
        Bounds probe = OrientedToWorldBounds(probeCenter, p.right, p.up, p.fwd, probeHalf);

        float bestDepth = float.MaxValue;
        Transform bestGeo = null;
        foreach (Transform g in geos)
        {
            MeshRenderer mr = VisualRenderer(g);
            if (mr == null) continue;
            Bounds b = mr.bounds;
            if (!StrictIntersects(b, probe)) continue;
            // 가까운 면 깊이 = 상자 모서리 8개의 (면 − 모서리)·법선 최솟값.
            float depth = NearDepth(b, p.face, p.fwd);
            if (depth < BackingProbeNear - 0.01f) continue; // 면에 걸친 상자(문설주 등)는 받침이 아니다
            if (depth < bestDepth) { bestDepth = depth; bestGeo = g; }
        }
        if (bestGeo == null) return false;

        float plateDepth = bestDepth - BackingPlateGap - BackingPlateThickness * 0.5f;
        GameObject root = new GameObject($"VIS_S6_PanelBacking_{tag}");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(p.face - p.fwd * plateDepth, Quaternion.LookRotation(p.fwd, p.up));
        Prim(PrimitiveType.Cube, $"VIS_S6_PanelBacking_{tag}_Plate", root.transform, Vector3.zero, Quaternion.identity,
             new Vector3(p.size.x - 2f * BackingPlateInset, p.size.y - 2f * BackingPlateInset, BackingPlateThickness), mat);
        Vector3 lp = generated.InverseTransformPoint(root.transform.position);
        Debug.Log($"[S6_Dress] 받침판 {tag}: '{bestGeo.name}' 면 앞(깊이 {bestDepth:F2}) 로컬 {lp}.");
        return true;
    }

    /// <summary>고정 패널 둘레 테두리 — 패널 바깥 가장자리에 선 최대 4개(면에서 앞으로 TrimThickness).
    /// 변 중심에서 면 뒤 TrimSupportProbe 점이 어떤 GEO Visual 안에도 없으면(받칠 면 없음 — 구덩이 디딤 위 등) 그 변은 뺀다(S6-L3).
    /// 만든 변 개수를 돌려준다.</summary>
    private static int BuildTrim(Transform parent, List<Transform> geos, PanelPose p, string tag, Material mat)
    {
        GameObject root = new GameObject($"VIS_S6_PanelTrim_{tag}");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(p.face, Quaternion.LookRotation(p.fwd, p.up));
        float w = p.size.x, h = p.size.y, z = TrimThickness * 0.5f;
        float ox = w * 0.5f + TrimWidth * 0.5f, oy = h * 0.5f + TrimWidth * 0.5f;
        Vector3 horiz = new Vector3(w + 2f * TrimWidth, TrimWidth, TrimThickness);
        Vector3 vert = new Vector3(TrimWidth, h, TrimThickness);
        string[] sides = { "Top", "Bottom", "Left", "Right" };
        Vector2[] offsets = { new Vector2(0f, oy), new Vector2(0f, -oy), new Vector2(-ox, 0f), new Vector2(ox, 0f) };
        Vector3[] scales = { horiz, horiz, vert, vert };
        int made = 0;
        for (int i = 0; i < sides.Length; i++)
        {
            Vector3 probe = root.transform.TransformPoint(new Vector3(offsets[i].x, offsets[i].y, -TrimSupportProbe));
            if (!PointInAnyGeo(geos, probe))
            {
                Debug.Log($"[S6_Dress] 테두리 {tag}_{sides[i]}: 밑에 받칠 GEO 면이 없어(구덩이 디딤 위 등) 뺐다 — 검사점 {probe}.");
                continue;
            }
            Prim(PrimitiveType.Cube, $"VIS_S6_PanelTrim_{tag}_{sides[i]}", root.transform,
                 new Vector3(offsets[i].x, offsets[i].y, z), Quaternion.identity, scales[i], mat);
            made++;
        }
        return made;
    }

    /// <summary>월드 점이 GEO Visual 렌더러 AABB 중 하나 안(경계 포함)인지.</summary>
    private static bool PointInAnyGeo(List<Transform> geos, Vector3 world)
    {
        foreach (Transform g in geos)
        {
            MeshRenderer mr = VisualRenderer(g);
            if (mr != null && mr.bounds.Contains(world)) return true;
        }
        return false;
    }

    /// <summary>패널 면 앞 PanelLightDistance에 보조 점광원(메쉬 없음).
    /// travelEnd가 있으면(움직이는 패널) 시작→끝 이동 경로 전체와 겹치는지 보고, 겹치면 위·아래·옆으로 옮긴다(수정 1).
    /// 점광원이라 콜라이더 영향은 0이다 — 패널이 등을 뚫고 지나가는 화면 어색함만 막는다.</summary>
    private static void BuildPanelLight(Transform parent, Transform generated, PanelPose p, string tag, PanelPose? travelEnd)
    {
        Vector3 world = p.face + p.fwd * PanelLightDistance;
        if (travelEnd.HasValue && InPanelTravel(world, p, travelEnd.Value, PanelLightSweepClearance, tag))
        {
            float lift = p.size.y * 0.5f + 2f * PanelLightSweepClearance;
            float side = p.size.x * 0.5f + 2f * PanelLightSweepClearance;
            Vector3[] tries = { world + p.up * lift, world - p.up * lift, world + p.right * side, world - p.right * side };
            bool moved = false;
            foreach (Vector3 c in tries)
            {
                if (InPanelTravel(c, p, travelEnd.Value, PanelLightSweepClearance, tag)) continue;
                Debug.Log($"[S6_Dress] 보조등 {tag}: 패널 이동 경로 안이라 {generated.InverseTransformPoint(world)} → " +
                          $"{generated.InverseTransformPoint(c)}(로컬)로 옮겼다.");
                world = c;
                moved = true;
                break;
            }
            if (!moved)
                Debug.LogWarning($"[S6_Dress] 보조등 {tag}: 패널 이동 경로를 피할 자리를 못 찾았다 — 원래 자리에 둔다(화면에서 패널이 등을 지나감).");
        }
        Vector3 local = generated.InverseTransformPoint(world);
        GameObject go = NewNode($"VIS_S6_PanelLight_{tag}", parent, Vector3.zero);
        go.transform.position = world;
        AddLight(go, local, PanelLightColor, PanelLightIntensity, PanelLightRange);
    }

    /// <summary>점 pt가 움직이는 패널의 시작→끝 이동 경로(패널 상자 + margin)에 들어가는지 — 표본 검사.
    /// 두 포즈 사이를 '고정 축 하나 둘레 회전'(팀 Pivot) 또는 '평행 이동'(팀 Slide)으로 본다 [계산 — 강체 두 포즈의 상대 회전].
    /// 회전은 짧은 쪽(≤180°)으로 돈다고 본다(초안 M1·M2는 90° — 팀 Pivot minAngle 0 → maxAngle 90 전제, 180° 넘게 도는 설정이면
    /// 검사가 모자란다). 경첩은 두 면 중심의 수직 이등분선에서 푼다 — 2차 M1 두 포즈 (−4.6,12.9,51.4)·(−11,12.9,58)에서
    /// (−4.5,12.9,57.9)가 나온다 [계산 S6-L3, 판정 2].</summary>
    private static bool InPanelTravel(Vector3 pt, PanelPose start, PanelPose end, float margin, string tag)
    {
        Vector3 half = start.size * 0.5f;
        Vector3 c0 = start.face - start.fwd * half.z; // 상자 중심(면 = 중심 + fwd × 두께/2)
        Quaternion r0 = Quaternion.LookRotation(start.fwd, start.up);
        Quaternion r1 = Quaternion.LookRotation(end.fwd, end.up);
        Quaternion q = r1 * Quaternion.Inverse(r0);
        q.ToAngleAxis(out float angle, out Vector3 axis);
        bool finiteAxis = !(float.IsNaN(axis.x) || float.IsNaN(axis.y) || float.IsNaN(axis.z) ||
                            float.IsInfinity(axis.x) || float.IsInfinity(axis.y) || float.IsInfinity(axis.z));

        if (angle < SweepStepDeg || !finiteAxis || axis.sqrMagnitude < 0.25f)
        {
            // 평행 이동(Slide) — 면 중심을 직선으로 옮긴다.
            Vector3 d = end.face - start.face;
            int steps = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / SweepStepDist));
            for (int i = 0; i <= steps; i++)
                if (InBox(pt, c0 + d * ((float)i / steps), start.right, start.up, start.fwd, half, margin)) return true;
            return false;
        }

        axis.Normalize();
        if (angle > 180f) { angle = 360f - angle; axis = -axis; }
        Vector3 ab = end.face - start.face;
        Vector3 abPerp = ab - axis * Vector3.Dot(ab, axis);
        Vector3 hinge = start.face; // 면 중심이 축 위에 있는 경우(abPerp ≈ 0)
        if (abPerp.magnitude > 0.0001f)
        {
            Vector3 mid = start.face + ab * 0.5f;
            float t = Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad);
            hinge = mid + Vector3.Cross(axis, abPerp).normalized * (abPerp.magnitude * 0.5f / t);
        }
        Quaternion qa = Quaternion.AngleAxis(angle, axis);
        Vector3 check = hinge + qa * (start.face - hinge);
        if (Vector3.Distance(check, end.face) > 0.05f)
            Debug.LogWarning($"[S6_Dress] 패널 {tag}: 시작·끝 포즈가 축 하나 둘레 회전으로 맞지 않는다(어긋남 {Vector3.Distance(check, end.face):F3}) — 경로 검사는 근사다.");

        int n = Mathf.Max(1, Mathf.CeilToInt(angle / SweepStepDeg));
        for (int i = 0; i <= n; i++)
        {
            Quaternion qi = Quaternion.AngleAxis(angle * i / n, axis);
            if (InBox(pt, hinge + qi * (c0 - hinge), qi * start.right, qi * start.up, qi * start.fwd, half, margin)) return true;
        }
        return false;
    }

    /// <summary>점 pt가 중심 c·축(r,u,f)·반치수 half 상자를 margin만큼 키운 부피 안인지.</summary>
    private static bool InBox(Vector3 pt, Vector3 c, Vector3 r, Vector3 u, Vector3 f, Vector3 half, float margin)
    {
        Vector3 d = pt - c;
        return Mathf.Abs(Vector3.Dot(d, r)) <= half.x + margin &&
               Mathf.Abs(Vector3.Dot(d, u)) <= half.y + margin &&
               Mathf.Abs(Vector3.Dot(d, f)) <= half.z + margin;
    }

    // ── 3) 천장 선형 등 ─────────────────────────────────────────────────────────────────

    private static int BuildCeilingLamps(Transform group, Bounds room, Material glow)
    {
        if (room.size.x <= 0.01f || room.size.z <= 0.01f)
        {
            Debug.LogError("[S6_Dress] refs.roomInnerBounds 크기가 0이다 — 천장 등을 건너뛴다(S6_Builder 확인).");
            return 0;
        }
        float ceilY = room.max.y;
        if (room.size.y <= 0.01f)
        {
            ceilY = FallbackCeilingY;
            Debug.LogWarning($"[S6_Dress] refs.roomInnerBounds 높이가 0 — 대체 천장 y {FallbackCeilingY}(B 결합 상수)에 등을 단다.");
        }
        GameObject lampsGo = NewNode("VIS_S6_CeilLamps", group, Vector3.zero);
        float[] xs = { room.center.x - room.size.x * CeilColumnFraction, room.center.x + room.size.x * CeilColumnFraction };
        int n = 0;
        for (int r = 0; r < CeilRowFractions.Length; r++)
        {
            float z = room.min.z + room.size.z * CeilRowFractions[r];
            for (int c = 0; c < xs.Length; c++)
            {
                string name = $"VIS_S6_CeilLamp_{r + 1}_{c + 1}";
                // 판 윗면 = 천장 아랫면(붙임). 판 중심 = 천장 − 두께/2.
                GameObject root = NewNode(name, lampsGo.transform, new Vector3(xs[c], ceilY - CeilLampSize.y * 0.5f, z));
                Prim(PrimitiveType.Cube, name + "_Panel", root.transform, Vector3.zero, Quaternion.identity, CeilLampSize, glow);
                Vector3 lightLocal = new Vector3(xs[c], ceilY - CeilLampSize.y - CeilLightDrop, z);
                GameObject lightGo = NewNode(name + "_Light", lampsGo.transform, lightLocal);
                AddLight(lightGo, lightLocal, CeilLightColor, CeilLightIntensity, CeilLightRange);
                n++;
            }
        }
        return n;
    }

    // ── 4) 출구 표시 ────────────────────────────────────────────────────────────────────

    private static int BuildExitMarker(Transform group, Transform generated, S6_Refs refs, List<Transform> geos, Material glow)
    {
        if (refs.exitLanding == null)
        {
            Debug.LogWarning("[S6_Dress] refs.exitLanding이 null — 출구 표시를 건너뛴다.");
            return 0;
        }
        Vector3 land = generated.InverseTransformPoint(refs.exitLanding.position);
        int n = 0;
        Bounds room = refs.roomInnerBounds;
        if (room.size.z > 0.01f)
        {
            // 출구 벽 안쪽 면(z = room.max.z)의 인방 아랫면 위에 붙인 발광 띠(방 쪽으로 두께만큼 나옴).
            // 인방 아랫면은 GEO에서 찾는다(수정 1 — B의 출구 높이가 바뀌어도 띠가 인방에 붙어 있게).
            string lintel;
            float openingTop = FindExitOpeningTop(generated, geos, land, room.max.z + ExitProbeBehindWall, out lintel);
            if (lintel == null)
            {
                openingTop = land.y + FallbackExitOpeningHeight;
                Debug.LogWarning($"[S6_Dress] 출구 인방 GEO를 찾지 못했다(x {land.x:F2}, z {room.max.z + ExitProbeBehindWall:F2}, y > {land.y:F2}) — " +
                                 $"대체 개구부 높이 {FallbackExitOpeningHeight}(B 결합 상수)로 띠를 둔다.");
            }
            else
                Debug.Log($"[S6_Dress] 출구 개구부 윗변 y {openingTop:F2}('{lintel}' 밑면, 개구부 높이 {openingTop - land.y:F2}).");
            float y = openingTop + ExitStripAboveOpening;
            Vector3 c = new Vector3(0f, y, room.max.z - ExitStripSize.z * 0.5f);
            Prim(PrimitiveType.Cube, "VIS_S6_ExitStrip", group, c, Quaternion.identity, ExitStripSize, glow);
            n++;
        }
        else Debug.LogWarning("[S6_Dress] refs.roomInnerBounds 크기가 0 — 출구 띠를 건너뛴다(등은 만든다).");

        Vector3 lightLocal = new Vector3(land.x, land.y + ExitLightHeight, land.z);
        GameObject go = NewNode("VIS_S6_ExitLight", group, lightLocal);
        AddLight(go, lightLocal, CeilLightColor, ExitLightIntensity, ExitLightRange);
        n++;
        return n;
    }

    /// <summary>출구 개구부 윗변 y(generated 로컬): 점 (land.x, ·, probeZ)의 세로줄을 덮는 GEO 중 밑면이 land.y보다 높은
    /// 가장 낮은 밑면. 초안 [계산]: GEO_S6_Wall_North_Lintel(−4~4 × y20~24 × z94~96) → 20. 없으면 source = null.</summary>
    private static float FindExitOpeningTop(Transform generated, List<Transform> geos, Vector3 land, float probeZ, out string source)
    {
        float best = float.MaxValue;
        source = null;
        foreach (Transform g in geos)
        {
            MeshRenderer mr = VisualRenderer(g);
            if (mr == null) continue;
            Bounds lb = ToLocalBounds(generated, mr.bounds);
            if (land.x <= lb.min.x || land.x >= lb.max.x) continue;
            if (probeZ <= lb.min.z || probeZ >= lb.max.z) continue;
            if (lb.min.y <= land.y + 0.01f) continue; // 문턱·바닥(출구 층 밑)은 제외
            if (lb.min.y < best) { best = lb.min.y; source = g.name; }
        }
        return best;
    }

    // ── 공용: 조명 · 도형 · 경계 ───────────────────────────────────────────────────────

    /// <summary>점광원. localPos(섹터 로컬)로 인접 섹터 경계까지 거리를 재 범위를 줄인다
    /// (우리 점광원은 아래 shadows = None이라 벽을 통과한다 — Master 방향광 그림자[1차 판정 A1]와 무관).</summary>
    private static Light AddLight(GameObject go, Vector3 localPos, Color color, float intensity, float range)
    {
        float guard = Mathf.Min(localPos.z - NeighborGuardMinZ, NeighborGuardMaxZ - localPos.z) - NeighborGuardMargin;
        if (guard < range)
        {
            if (guard <= 0.5f)
                Debug.LogWarning($"[S6_Dress] 등 '{go.name}' {localPos}가 인접 섹터 경계에 너무 가깝다(여유 {guard:F2}) — 범위 0.5로 둔다.");
            range = Mathf.Max(0.5f, guard);
            rangeClampedCount++;
        }

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        // Built-in Forward 픽셀 조명 수 한계(QualitySettings Ultra 4 / Very High 3 / High 2 / Medium 1 / Low 0,
        // 가장 밝은 방향광 1개 포함) — ProjectSettings는 바꾸지 않고 오브젝트 설정으로만 대응:
        //  - 전부 Auto: 렌더러마다 가장 중요한(가깝고 센) 조명부터 픽셀, 나머지는 버텍스/SH로 자동 강등(S1_Lighting 선례).
        //  - 패널 보조등은 패널 바로 앞(2.5)이라 작은 팀 패널 렌더러에서 1순위가 된다(조준 대상이 밝게).
        //  - 천장 등은 6개로 줄이고 판을 길게 해 '밝은 방'은 발광 판·밝은 알베도로 읽히게 했다.
        light.renderMode = LightRenderMode.Auto;
        light.shadows = LightShadows.None;                  // [추정] 연출 전용 — 점광원 그림자 비용을 쓰지 않는다(방향광 그림자는 INF1-2 몫, 계약 K0-3).
        light.lightmapBakeType = LightmapBakeType.Realtime; // 굽지 않음(씬 GI 설정 불변).
        lightCount++;
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

    /// <summary>축(right·up·fwd)에 맞춘 상자(중심·반치수)를 감싸는 월드 AABB.</summary>
    private static Bounds OrientedToWorldBounds(Vector3 center, Vector3 right, Vector3 up, Vector3 fwd, Vector3 half)
    {
        Vector3 ext = new Vector3(
            Mathf.Abs(right.x) * half.x + Mathf.Abs(up.x) * half.y + Mathf.Abs(fwd.x) * half.z,
            Mathf.Abs(right.y) * half.x + Mathf.Abs(up.y) * half.y + Mathf.Abs(fwd.y) * half.z,
            Mathf.Abs(right.z) * half.x + Mathf.Abs(up.z) * half.y + Mathf.Abs(fwd.z) * half.z);
        return new Bounds(center, ext * 2f);
    }

    /// <summary>면이 닿기만 하는 경우는 빼고 부피가 겹칠 때만 참(여유 0.001).</summary>
    private static bool StrictIntersects(Bounds a, Bounds b)
    {
        const float e = 0.001f;
        return a.min.x < b.max.x - e && a.max.x > b.min.x + e &&
               a.min.y < b.max.y - e && a.max.y > b.min.y + e &&
               a.min.z < b.max.z - e && a.max.z > b.min.z + e;
    }

    /// <summary>상자 모서리 8개 중 패널 면에 가장 가까운 것의 깊이((면 − 모서리)·법선). 면 뒤가 양수.</summary>
    private static float NearDepth(Bounds b, Vector3 face, Vector3 fwd)
    {
        float best = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                         (i & 2) == 0 ? b.min.y : b.max.y,
                                         (i & 4) == 0 ? b.min.z : b.max.z);
            best = Mathf.Min(best, Vector3.Dot(face - corner, fwd));
        }
        return best;
    }

    /// <summary>월드 AABB → generated 로컬 AABB(모서리 8개 변환).</summary>
    private static Bounds ToLocalBounds(Transform generated, Bounds world)
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? world.min.x : world.max.x,
                                         (i & 2) == 0 ? world.min.y : world.max.y,
                                         (i & 4) == 0 ? world.min.z : world.max.z);
            Vector3 l = generated.InverseTransformPoint(corner);
            min = Vector3.Min(min, l);
            max = Vector3.Max(max, l);
        }
        var r = new Bounds();
        r.SetMinMax(min, max);
        return r;
    }

    // ── 머티리얼(Standard, Materials/Generated/M4_S6_*.mat) ─────────────────────────────

    private static Mats LoadMaterials()
    {
        EnsureDir(MaterialDir);
        return new Mats
        {
            wallLight = GetOrCreateMaterial("WallLight", WallLightColor, WallLightSmoothness, 0f, Color.black),
            floorLight = GetOrCreateMaterial("FloorLight", FloorLightColor, FloorLightSmoothness, 0f, Color.black),
            platformLight = GetOrCreateMaterial("PlatformLight", PlatformLightColor, PlatformLightSmoothness, 0f, Color.black),
            panelBacking = GetOrCreateMaterial("PanelBacking", PanelBackingColor, 0.20f, 0f, PanelBackingColor * PanelBackingEmission),
            panelTrim = GetOrCreateMaterial("PanelTrim", PanelTrimColor, 0.30f, 0f, PanelTrimColor * PanelTrimEmission),
            ceilLamp = GetOrCreateMaterial("CeilLampGlow", CeilLampColor, 0.30f, 0f, CeilLampColor * CeilLampEmission),
            exitGlow = GetOrCreateMaterial("ExitGlow", ExitGlowColor, 0.30f, 0f, ExitGlowColor * ExitGlowEmission),
        };
    }

    /// <summary>M4_S6_&lt;name&gt;.mat — 있으면 재사용, 없으면 Standard로 생성. 매번 값을 다시 써서 상수 표와 일치시킨다.
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
                Debug.LogError("[S6_Dress] Standard 셰이더를 찾지 못했다(Built-in 전용 프로젝트여야 한다).");
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
