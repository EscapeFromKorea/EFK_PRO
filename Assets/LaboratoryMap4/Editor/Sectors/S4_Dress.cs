#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 섹터4 "반중력·보안" 시각·재질·국지 조명(R2 계약 C1-0·C1-6, 지시서 진행/지시서/R2/S4-L.md). S4_Builder.Build의 ④에서
/// `S4_Dress.Apply(generated, refs)`로 부른다(Wire 다음). 콜라이더를 만들지 않고, GEO의 Transform·Collider도 건드리지 않는다.
///
/// [하는 일]
/// - 지형 재질: 설계는 "원통형·중앙 큰 돌·위로 긴 공간, 별다른 건 없다"뿐이라 색 지정이 없다 [확정 설계 §1] → 색·smoothness는 전부 [추정].
///   걷는 면(계단·돌·다리)은 따뜻한 밝은 계열, 벽은 차가운 회청색, 원통 바닥은 어두운 색으로 나눠 높이감을 살린다 [명령 제안].
///   대상 렌더러 = 각 GEO의 자식 `VIS_…`(이름 바꾸기 전이면 `Visual`) [계약 C2-0]. 자식 시각물이 없고 GEO 자신에 MeshRenderer가 있으면
///   (S4-B가 쐐기·돌·원통 벽 메시를 GEO에 직접 만들 경우) 그 렌더러의 sharedMaterial만 바꾼다 [추정 — 보고서 reviewRequests].
/// - Refs에 없는 바닥 `GEO_S4_Floor_Entry`·`GEO_S4_Floor_Cyl`은 generated 아래에서 이름으로 찾는다 [추정 — 계약 C1-4에 필드 없음].
/// - 버블 시각물 `VIS_S4_BubbleShell`: 트리거와 같은 상자 외곽 (−4, 8, 58)~(4, 18, 66) [판정 18 ②, 초안 §3-2 :69].
///   남(z 58)·동(x 4)·서(x −4)·위(y 18) 4면만(바닥 = 돌 윗면, 북 = 버블 북쪽 벽 남쪽 면이라 만들지 않음) [제안 초안 :69].
///   Standard Fade, 알파 0.25, 그림자 드리우기 끔 [제안 초안 :69]. 면마다 바깥·안쪽을 향한 Quad 두 장(콜라이더 제거)으로
///   만들어 버블 안·밖 어디서 봐도 보인다 — Standard는 뒷면을 그리지 않기 때문 [추정 — 방법 선택].
/// - 국지 조명: generated/VIS_S4_Lights 아래 원통 벽 안쪽 면 점광원 9개(3층 × 3) — 높이 층은 돌 아래·버블 높이·다리 위 띠의
///   가운데(4·13·22) [계산: refs.rockTop·bridgeTop·cylWallTop], 층마다 120° 간격, 층끼리 60° 엇갈림 [추정].
///
/// [하지 않는 일] 전역 조명(Master 방향광·환경광·그림자 설정)·ProjectSettings 불변(판정 1 — 방향광 그림자는 INF1-2).
/// 팀 기믹 렌더러(버블 컴포넌트 GO·낙석·레이저 라인·Emitter_Visual·체크포인트 막대)와 모든 팀 컴포넌트 값·Transform 불변(계약 C1-6 —
/// S4 초안에는 팀 렌더러 머티리얼 교체를 명시한 곳이 없다). Refs는 읽기만. Map4Palette·Map4_*.mat·팀 .mat 불변.
///
/// [한계 — 보고서 진행/보고/S4-L.md] 천장이 없어 방향광이 원통 안을 그대로 비춘다(어둡게 만들 수 없다). Built-in 순방향 렌더링의 오브젝트당
/// 픽셀 조명 수(품질 단계 Ultra 4 · Very High 3 · High 2 · Medium 1 · Low/Very Low 0)는 renderMode Auto로 받아들인다. 원통 벽이
/// 메시 1개(구성 (가) — 지금 S4_Builder 기본값, §7-18 판정 대기)면 그 bounds가 원통 안 전체라 등 9개가 모두 한 오브젝트에 닿는다 [계산]
/// → 한도 밖 등은 버텍스/SH로 떨어진다(ReportCylWallLighting이 구성을 감지해 로그로 남긴다 [M2R1]).
/// 버블 셸과 레이저 라인(Sprites/Default)·낙석의 투명 정렬은 [실측 필요].
///
/// 출처 표기: [확정] 설계 사용자 결정 · [판정] 컨트롤타워 판정 · [제안] 초안 제안값 · [팀] 팀 코드 · [계산] 계산값 · [추정] 이 파일이 정한 값(실측 필요).
/// </summary>
public static class S4_Dress
{
    // ───────────────────────── 이름·경로 ─────────────────────────
    private const string LightsGroupName = "VIS_S4_Lights";                          // [계약 C1-6]
    private const string ShellName = "VIS_S4_BubbleShell";                           // [계약 C1-6 / 초안 :69]
    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated";  // [계약 C1-6]
    private const string FloorEntryName = "GEO_S4_Floor_Entry";                      // [계약 C2-4]
    private const string FloorCylName = "GEO_S4_Floor_Cyl";                          // [계약 C2-4]
    private const string RailTag = "_Rail";                                          // GEO_S4_Wall_RailW/E [계약 C2-4]
    private const string CylWallName = "GEO_S4_Wall_Cyl";                            // (가) 1개 / (나) GEO_S4_Wall_Cyl_{nn} [계약 C2-4] — M2R1 구성 감지

    // ───────────────────────── 초안 값(대조용) ─────────────────────────
    /// <summary>[판정 18 ② · 초안 :69 · 계약 C1-4 bubbleTrigger] 버블 트리거 = 셸 외곽.</summary>
    private static readonly Vector3 DraftBubbleMin = new Vector3(-4f, 8f, 58f);
    private static readonly Vector3 DraftBubbleMax = new Vector3(4f, 18f, 66f);
    private const float BoundsTolerance = 0.01f;                                     // [지시서 구현 요구 1]
    /// <summary>[제안 초안 §2 :18·:19] 원통 축·안쪽 반지름·벽 높이, 돌 높이, 다리 윗면 — refs 값과 다르면 LogError(값은 refs를 따른다).</summary>
    private static readonly Vector2 DraftAxisXZ = new Vector2(0f, 62f);
    private const float DraftInnerR = 34f, DraftWallTop = 26f, DraftRockTop = 8f, DraftBridgeTop = 18f;

    // ───────────────────────── 버블 셸 ─────────────────────────
    /// <summary>[제안 초안 :69] 알파 0.25(범위 0.2~0.35).</summary>
    private const float ShellAlpha = 0.25f;
    /// <summary>[추정] 투명 큐 3000보다 10 앞(2990). 셸을 팀 레이저 라인(Sprites/Default, 큐 3000 [추정])보다 먼저 그려 광선이 셸에 덮여
    /// 흐려지는 일을 없앤다(광선이 셸 뒤에 있어도 선명하게 보인다 — 버블 구간 플레이에 광선이 더 중요). 2500 초과라 투명 정렬 범위 안.</summary>
    private const int ShellRenderQueue = 2990;

    // ───────────────────────── 등 ─────────────────────────
    /// <summary>[추정] 차가운 흰색 — 팀 레이저 예고 주황 (1, 0.6, 0.1)·발사 빨강 (1, 0.15, 0.1) [팀 S4_팀API :143]과 헷갈리지 않게 한다.</summary>
    private static readonly Color LampColor = new Color(0.80f, 0.90f, 1.0f);
    /// <summary>[추정] 점광원 범위. 8이면 원통 바닥에 닿는 등은 아래층 3개뿐이고(가운데·위층은 높이 13·22 > 8), 계단(r ≤ 15)·돌·버블·다리에는
    /// 닿지 않는다(벽 등에서 가장 가까운 걷는 면 = 다리까지 13.36 > 8) [계산 — 보고서 픽셀 조명 절].</summary>
    private const float LampRange = 8f;
    /// <summary>[추정] 강도 — S1 비상등 1.5 [S1_Lighting.cs]와 같다. 천장이 없어 방향광이 그대로 비추므로 벽 위 밝은 자국 정도다. 실측 조정 대상.</summary>
    private const float LampIntensity = 1.5f;
    /// <summary>[추정] 등 시각물(구) 반지름 — 원통 건너편(최대 약 67U)에서도 점으로 보이게 0.3.</summary>
    private const float LampRadius = 0.3f;
    /// <summary>[추정] 등 중심을 벽 안쪽 면에서 들이는 거리. 34 − 0.5 = 33.5, 구 바깥 33.8 ≤ 33.9 [지시서 구현 요구 4] — S4_Builder 원통 벽은
    /// 96각(3.75°)이고 안쪽 현의 최소 반지름은 34 − 34(1 − cos 1.875°) = 33.982라, 구 바깥 33.8은 그보다 안쪽이어서 벽 메시에 묻히지 않는다 [계산 — M2R1 정정].</summary>
    private const float LampInset = 0.5f;
    /// <summary>[추정] 층마다 등 3개(120° 간격), 층끼리 60° 엇갈림. φ = 축에서 +X → +Z로 잰 각(초안 좌표 규약 :7).
    /// 남쪽 입구(φ 270 ± 7.6°, y 0~6)·북쪽 다리 개구(φ 90 ± 7.6°, y ≥ 17.7)에서 모두 ≥ 30° 떨어진다 [계산].</summary>
    private static readonly float[] TierLowPhi = { 0f, 120f, 240f };
    private static readonly float[] TierMidPhi = { 60f, 180f, 300f };
    private static readonly float[] TierHighPhi = { 0f, 120f, 240f };
    private const int ExpectedLamps = 9;

    // ───────────────────────── 재질 값 [추정 — 설계·초안에 색 지정 없음] ─────────────────────────
    private struct MatSpec
    {
        public string name; public Color albedo; public float smoothness; public float metallic; public Color emission; public bool fade;
        public MatSpec(string n, Color a, float s, float m, Color e, bool f)
        { name = n; albedo = a; smoothness = s; metallic = m; emission = e; fade = f; }
    }
    private static readonly Color NoEmission = Color.black;
    // 벽 — 차가운 회청 콘크리트(원통 벽·입구/출구 벽·버블 북쪽 벽) [추정]
    private static readonly MatSpec Wall   = new MatSpec("M4_S4_Wall",   new Color(0.46f, 0.50f, 0.56f), 0.15f, 0.0f, NoEmission, false);
    // 원통·입구 바닥 — 벽보다 어두운 흙회색(떨어진 자리) [추정]
    private static readonly MatSpec Floor  = new MatSpec("M4_S4_Floor",  new Color(0.27f, 0.26f, 0.24f), 0.08f, 0.0f, NoEmission, false);
    // 나선 계단 — 걷는 길, 밝은 모래색(벽·바닥·돌과 구분) [추정]
    private static readonly MatSpec Stair  = new MatSpec("M4_S4_Stair",  new Color(0.66f, 0.58f, 0.43f), 0.20f, 0.0f, NoEmission, false);
    // 큰 돌 — 갈회색 돌(윗면 = 버블 입구) [추정]
    private static readonly MatSpec Rock   = new MatSpec("M4_S4_Rock",   new Color(0.44f, 0.39f, 0.34f), 0.10f, 0.0f, NoEmission, false);
    // 레이저 기둥·계단 조준 받침대 — 어두운 금속 [추정]
    private static readonly MatSpec Mast   = new MatSpec("M4_S4_Mast",   new Color(0.20f, 0.22f, 0.25f), 0.45f, 0.6f, NoEmission, false);
    // 출구 다리 — 걷는 길, 따뜻한 금속판(계단과 같은 계열) [추정]
    private static readonly MatSpec Bridge = new MatSpec("M4_S4_Bridge", new Color(0.60f, 0.54f, 0.42f), 0.35f, 0.3f, NoEmission, false);
    // 다리 난간 — 밝은 금속(벽과 구분) [추정]
    private static readonly MatSpec Rail   = new MatSpec("M4_S4_Rail",   new Color(0.70f, 0.72f, 0.74f), 0.50f, 0.7f, NoEmission, false);
    // 버블 셸 — 하늘색 반투명(레이저 주황·빨강과 구분), 알파 0.25 [제안 초안 :69], 색·약한 발광은 [추정]
    private static readonly MatSpec Shell  = new MatSpec("M4_S4_BubbleShell", new Color(0.35f, 0.78f, 1.0f, ShellAlpha), 0.85f, 0.0f, new Color(0.03f, 0.09f, 0.14f), true);
    // 등 시각물 — 등 색 그대로 발광 [추정](S1_Lighting 방식)
    private static readonly MatSpec Lamp   = new MatSpec("M4_S4_Lamp",   new Color(0.80f, 0.90f, 1.0f), 0.5f, 0.0f, new Color(0.80f, 0.90f, 1.0f), false);

    /// <summary>R2 계약 C1-0. 예외는 빌더가 잡지만(C5), 단계마다 따로 잡아 한 단계 실패가 나머지를 막지 않게 한다. 지형은 지우지 않는다.</summary>
    public static void Apply(Transform generated, S4_Refs refs)
    {
        if (generated == null) { Debug.LogError("[S4_Dress] generated가 null이다 — 시각 처리를 하지 않는다."); return; }
        if (refs == null) { Debug.LogError("[S4_Dress] refs가 null이다 — 시각 처리를 하지 않는다."); return; }
        if (refs.generated != null && refs.generated != generated)
            Debug.LogWarning("[S4_Dress] refs.generated가 인자 generated와 다르다 — 인자 쪽을 기준으로 쓴다.", generated);
        CheckDims(refs);

        // [M2R1] refs.stairs에는 계단 그룹 6개와 그 직속 쐐기 88개가 함께 들어 있어, 그룹을 칠할 때(직속 GEO_ 자식까지) 쐐기가 한 번,
        // 목록의 쐐기로 또 한 번 잡힌다. 렌더러를 HashSet으로 중복 제거해 한 번만 칠하고, 로그에는 고유 개수와 건너뛴 중복 수를 따로 낸다.
        s_painted.Clear(); s_duplicates = 0;
        int shellFaces = 0, lamps = 0;
        try { PaintList(refs.stairs, "stairs", GetOrCreate(Stair)); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 계단 재질 예외: {e}"); }
        try { DressWalls(refs.walls); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 벽 재질 예외: {e}"); }
        try { PaintList(refs.masts, "masts", GetOrCreate(Mast)); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 기둥 재질 예외: {e}"); }
        try { PaintOne(refs.rock, "rock", GetOrCreate(Rock)); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 큰 돌 재질 예외: {e}"); }
        try { PaintOne(refs.exitBridge, "exitBridge", GetOrCreate(Bridge)); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 다리 재질 예외: {e}"); }
        try { DressFloors(generated); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 바닥 재질 예외: {e}"); }
        try { shellFaces = BuildBubbleShell(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 버블 셸 예외: {e}"); }
        try { lamps = BuildLamps(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S4_Dress] 국지 조명 예외: {e}"); }

        int painted = s_painted.Count, duplicates = s_duplicates;
        s_painted.Clear();   // 씬 오브젝트 참조를 정적 필드에 남기지 않는다
        Debug.Log($"[S4_Dress] 완료 — 재질 교체 렌더러 {painted}개(고유, 중복 지정 {duplicates}건 제외), 버블 셸 Quad {shellFaces}/8(4면 × 양쪽), " +
                  $"벽 등 {lamps}/{ExpectedLamps}(범위 {LampRange}·강도 {LampIntensity}, renderMode Auto·그림자 없음). 전역 조명 불변.");
    }

    /// <summary>[M2R1] 이번 Apply에서 재질을 바꾼 고유 렌더러(중복 제거용). Apply 시작·끝에서 비운다.</summary>
    private static readonly HashSet<Renderer> s_painted = new HashSet<Renderer>();
    /// <summary>[M2R1] 이미 칠한 렌더러를 다시 만난 횟수(로그용).</summary>
    private static int s_duplicates;

    /// <summary>refs 치수가 초안 값과 같은지 본다(±0.01). 다르면 LogError만 — 등 자리는 refs 값을 따른다.</summary>
    private static void CheckDims(S4_Refs refs)
    {
        if (Vector2.Distance(refs.cylAxisXZ, DraftAxisXZ) > BoundsTolerance ||
            Mathf.Abs(refs.cylInnerR - DraftInnerR) > BoundsTolerance ||
            Mathf.Abs(refs.cylWallTop - DraftWallTop) > BoundsTolerance ||
            Mathf.Abs(refs.rockTop - DraftRockTop) > BoundsTolerance ||
            Mathf.Abs(refs.bridgeTop - DraftBridgeTop) > BoundsTolerance)
            Debug.LogError($"[S4_Dress] refs 치수가 초안과 다르다 — 축 {refs.cylAxisXZ}·r {refs.cylInnerR}·벽 {refs.cylWallTop}·돌 {refs.rockTop}·다리 {refs.bridgeTop} " +
                           $"(초안 {DraftAxisXZ}·{DraftInnerR}·{DraftWallTop}·{DraftRockTop}·{DraftBridgeTop}). 등 자리는 refs 값으로 계산한다.");
    }

    // ───────────────────────── 지형 재질 ─────────────────────────

    /// <summary>벽: 난간(`_Rail`)은 M4_S4_Rail, 나머지(원통·입구·출구·버블 북쪽 벽)는 M4_S4_Wall.</summary>
    private static int DressWalls(List<Transform> walls)
    {
        if (walls == null) { Debug.LogError("[S4_Dress] refs.walls가 null이다 — 벽 재질을 건너뛴다."); return 0; }
        Material wall = GetOrCreate(Wall), rail = GetOrCreate(Rail);
        int n = 0;
        for (int i = 0; i < walls.Count; i++)
        {
            Transform t = walls[i];
            if (t == null) { Debug.LogError($"[S4_Dress] refs.walls[{i}]가 null이다 — 건너뛴다."); continue; }
            n += Paint(t, t.name.Contains(RailTag) ? rail : wall);
        }
        return n;
    }

    private static int PaintList(List<Transform> list, string label, Material m)
    {
        if (list == null) { Debug.LogError($"[S4_Dress] refs.{label}가 null이다 — 건너뛴다."); return 0; }
        if (list.Count == 0) Debug.LogWarning($"[S4_Dress] refs.{label}가 비었다 — 입힐 대상이 없다.");
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null) { Debug.LogError($"[S4_Dress] refs.{label}[{i}]가 null이다 — 건너뛴다."); continue; }
            n += Paint(list[i], m);
        }
        return n;
    }

    private static int PaintOne(Transform t, string label, Material m)
    {
        if (t == null) { Debug.LogError($"[S4_Dress] refs.{label}가 null이다 — 건너뛴다."); return 0; }
        return Paint(t, m);
    }

    /// <summary>Refs에 없는 바닥 2개를 generated 아래 이름으로 찾는다(첫 1개) [추정 — 계약 C1-4에 필드 없음]. 없으면 경고만.</summary>
    private static int DressFloors(Transform g)
    {
        Material floor = GetOrCreate(Floor);
        int n = 0;
        foreach (string name in new[] { FloorEntryName, FloorCylName })
        {
            Transform found = null;
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                if (t.name == name) { found = t; break; }
            if (found == null) { Debug.LogWarning($"[S4_Dress] '{name}'을 찾지 못했다 — 기본 팔레트 재질 그대로다."); continue; }
            n += Paint(found, floor);
        }
        return n;
    }

    /// <summary>GEO의 시각 렌더러 sharedMaterial만 바꾼다. 찾는 순서: 직속 `VIS_…`/`Visual` 자식 → (없으면) GEO 자신의 MeshRenderer [추정] →
    /// 직속 `GEO_` 자식(계단 그룹의 쐐기 `…_W{nn}` 등)을 같은 규칙으로 한 단계 더. GEO의 Transform·Collider는 건드리지 않는다.</summary>
    private static int Paint(Transform geo, Material m)
    {
        if (m == null) return 0;
        List<Renderer> rs = new List<Renderer>();
        bool selfUsed = false;
        CollectVisuals(geo, rs, ref selfUsed, 0);
        if (rs.Count == 0)
        {
            Debug.LogWarning($"[S4_Dress] '{geo.name}' 아래 시각물(VIS_/Visual/자기 MeshRenderer)을 찾지 못했다 — 재질을 입히지 않는다.", geo);
            return 0;
        }
        if (selfUsed)
            Debug.Log($"[S4_Dress] '{geo.name}'(또는 그 GEO_ 자식)은 VIS_ 자식이 없어 GEO 자신의 MeshRenderer 재질을 바꿨다 [추정 — 보고서 reviewRequests].", geo);
        int added = 0;
        foreach (Renderer r in rs)
        {
            if (!s_painted.Add(r)) { s_duplicates++; continue; }   // [M2R1] 이미 칠한 렌더러 — 다시 칠하지도 세지도 않는다
            r.sharedMaterial = m;
            EditorUtility.SetDirty(r);
            added++;
        }
        return added;
    }

    private static void CollectVisuals(Transform t, List<Renderer> into, ref bool selfUsed, int depth)
    {
        bool found = false;
        foreach (Transform child in t)
        {
            if (!(child.name.StartsWith("VIS_") || child.name == "Visual")) continue;
            Renderer r = child.GetComponent<MeshRenderer>();
            if (r != null) { into.Add(r); found = true; }
        }
        if (!found)
        {
            Renderer self = t.GetComponent<MeshRenderer>();
            if (self != null) { into.Add(self); selfUsed = true; }
        }
        if (depth >= 1) return;
        foreach (Transform child in t)
            if (child.name.StartsWith("GEO_")) CollectVisuals(child, into, ref selfUsed, depth + 1);
    }

    // ───────────────────────── 버블 셸 ─────────────────────────

    /// <summary>generated/VIS_S4_BubbleShell 아래 4면 × 양쪽 = Quad 8장. 외곽은 refs.bubbleTrigger(섹터 로컬)에서 읽고 초안 값과 ±0.01로 대조 —
    /// 다르면 LogError 후 초안 값으로 만든다. 콜라이더 0, 그림자 드리우기·받기 끔.</summary>
    private static int BuildBubbleShell(Transform g, S4_Refs refs)
    {
        Transform old = g.Find(ShellName);
        if (old != null) Object.DestroyImmediate(old.gameObject); // 우리 시각물만 다시 만든다(지형 아님).

        Vector3 min = refs.bubbleTrigger.min, max = refs.bubbleTrigger.max;
        if (!Approx(min, DraftBubbleMin) || !Approx(max, DraftBubbleMax))
        {
            Debug.LogError($"[S4_Dress] refs.bubbleTrigger {min}~{max}가 초안 {DraftBubbleMin}~{DraftBubbleMax}와 다르다(±{BoundsTolerance}) — " +
                           "초안 값으로 셸을 만든다(보고서 mismatchReports).");
            min = DraftBubbleMin; max = DraftBubbleMax;
        }

        GameObject root = new GameObject(ShellName);
        root.transform.SetParent(g, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        Material mat = GetOrCreate(Shell);
        Vector3 c = (min + max) * 0.5f;
        int n = 0;
        // 면 = (이름, 바깥 법선, 면 중심). 바닥(−Y)·북(+Z)은 만들지 않는다 [제안 초안 :69].
        n += BuildFace(root.transform, "S", Vector3.back,    new Vector3(c.x, c.y, min.z), min, max, mat);
        n += BuildFace(root.transform, "E", Vector3.right,   new Vector3(max.x, c.y, c.z), min, max, mat);
        n += BuildFace(root.transform, "W", Vector3.left,    new Vector3(min.x, c.y, c.z), min, max, mat);
        n += BuildFace(root.transform, "Top", Vector3.up,    new Vector3(c.x, max.y, c.z), min, max, mat);
        if (n != 8) Debug.LogError($"[S4_Dress] 버블 셸 Quad {n}/8만 만들었다 — 위 로그 확인.");
        return n;
    }

    /// <summary>한 면 = 바깥을 보는 Quad + 안쪽을 보는 Quad. 두 장은 서로 반대를 보므로 뒷면 컬링으로 어느 쪽에서든 한 장만 그려진다
    /// (겹쳐 깜박임 없음). Quad가 어느 쪽을 앞면으로 삼든 두 장이 양쪽을 덮으므로 방향 착오에도 안전하다 [추정 — 방법 선택].</summary>
    private static int BuildFace(Transform parent, string tag, Vector3 outward, Vector3 center, Vector3 min, Vector3 max, Material mat)
    {
        Vector3 size = max - min;
        int n = 0;
        n += BuildQuad(parent, $"{ShellName}_{tag}_Out", outward, center, size, mat) ? 1 : 0;
        n += BuildQuad(parent, $"{ShellName}_{tag}_In", -outward, center, size, mat) ? 1 : 0;
        return n;
    }

    private static bool BuildQuad(Transform parent, string name, Vector3 facing, Vector3 center, Vector3 size, Material mat)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Collider col = q.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col); // 콜라이더 0 — 시각 전용 [계약 C1-6].
        q.transform.SetParent(parent, false);
        // Unity Quad는 로컬 XY 평면, 앞면 법선 = 로컬 −Z. 앞면이 facing을 보게: 로컬 +Z = −facing.
        Vector3 up = Mathf.Abs(facing.y) > 0.5f ? Vector3.forward : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(-facing, up);
        q.transform.localRotation = rot;
        q.transform.localPosition = center;
        Vector3 ax = rot * Vector3.right, ay = rot * Vector3.up;
        q.transform.localScale = new Vector3(Mathf.Abs(Vector3.Dot(ax, size)), Mathf.Abs(Vector3.Dot(ay, size)), 1f);

        MeshRenderer mr = q.GetComponent<MeshRenderer>();
        if (mr == null) { Debug.LogError($"[S4_Dress] '{name}'에 MeshRenderer가 없다."); return false; }
        if (mat != null) mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // [제안 초안 :69] 그림자 드리우기 끔
        mr.receiveShadows = false;                                          // [추정] Fade는 그림자를 받지 않는다 — 명시만
        return true;
    }

    private static bool Approx(Vector3 a, Vector3 b) =>
        Mathf.Abs(a.x - b.x) <= BoundsTolerance && Mathf.Abs(a.y - b.y) <= BoundsTolerance && Mathf.Abs(a.z - b.z) <= BoundsTolerance;

    // ───────────────────────── 국지 조명 ─────────────────────────

    /// <summary>generated/VIS_S4_Lights 아래 원통 벽 안쪽 면 등 9개. 층 높이 = 돌 아래 띠(0~rockTop)·버블 띠(rockTop~bridgeTop)·다리 위 띠
    /// (bridgeTop~cylWallTop)의 가운데 [계산]. 걷는 면 위(입구 통로·계단 r 10~15·돌 윗면·다리 위)·버블 상자 안·광선·낙석 레인·발사점과
    /// 겹치지 않는다(등은 r 33.5 — 고정 광선까지 ≥ 17.99, 낙석 레인까지 수평 ≥ 21.48, 조준 발사점까지 ≥ 20.76 > 사거리 20) [계산 — 보고서].
    /// 전역 조명은 건드리지 않는다.</summary>
    private static int BuildLamps(Transform g, S4_Refs refs)
    {
        Transform old = g.Find(LightsGroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject); // 우리 그룹만 다시 만든다(지형 아님).

        GameObject group = new GameObject(LightsGroupName);
        group.transform.SetParent(g, false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;

        float r = refs.cylInnerR - LampInset;
        if (r <= 0f) { Debug.LogError($"[S4_Dress] refs.cylInnerR {refs.cylInnerR}가 너무 작다 — 등을 만들지 않는다."); return 0; }
        float yLow = refs.rockTop * 0.5f;
        float yMid = (refs.rockTop + refs.bridgeTop) * 0.5f;
        float yHigh = (refs.bridgeTop + refs.cylWallTop) * 0.5f;

        Material lampMat = GetOrCreate(Lamp);
        int n = 0;
        n += BuildTier(group.transform, "Low", TierLowPhi, yLow, r, refs.cylAxisXZ, lampMat);
        n += BuildTier(group.transform, "Mid", TierMidPhi, yMid, r, refs.cylAxisXZ, lampMat);
        n += BuildTier(group.transform, "High", TierHighPhi, yHigh, r, refs.cylAxisXZ, lampMat);
        if (n != ExpectedLamps) Debug.LogError($"[S4_Dress] 벽 등 {n}/{ExpectedLamps}개만 만들었다 — 위 로그 확인.");
        ReportCylWallLighting(refs, n);
        return n;
    }

    /// <summary>[M2R1] 원통 벽 구성((가) 비볼록 1개 / (나) 조각)을 refs.walls 이름으로 알아내 픽셀 조명 한도 상태를 로그로 남긴다. 값은 바꾸지 않는다 —
    /// 등 배치 조정은 §7-18 판정 뒤(보고서 reviewRequests). (가)면 등 전부가 렌더러 1개의 bounds 안이라 어느 품질 단계에서도 한도(최대 4)를 넘는다 [계산].</summary>
    private static void ReportCylWallLighting(S4_Refs refs, int lampCount)
    {
        if (refs.walls == null || lampCount <= 0) return;
        int single = 0, pieces = 0;
        foreach (Transform t in refs.walls)
        {
            if (t == null) continue;
            if (t.name == CylWallName) single++;
            else if (t.name.StartsWith(CylWallName + "_", System.StringComparison.Ordinal)) pieces++;
        }
        if (single > 0)
            Debug.LogWarning($"[S4_Dress] 원통 벽 구성 (가) 비볼록 1개 — 벽 등 {lampCount}개가 모두 렌더러 1개에 닿아 오브젝트당 픽셀 조명 한도" +
                             $"(Ultra 4 · Very High 3 · High 2 · Medium 1 · Low 0 [팀 QualitySettings])를 모든 단계에서 넘는다. 넘친 등은 버텍스/SH로 떨어지고" +
                             $"(벽 꼭짓점이 y 0·26에만 있어 버텍스 불빛은 거의 0 [계산]) 높이 신호는 발광 구만 남는다 — §7-18 판정 뒤 등 배치 재조정(보고서 S4-L).");
        else if (pieces > 0)
            Debug.Log($"[S4_Dress] 원통 벽 구성 (나) 조각 {pieces}개 — 조각당 닿는 등 최대 2개 [계산](High 이상 한도 안).");
        else
            Debug.LogWarning($"[S4_Dress] refs.walls에 '{CylWallName}'·'{CylWallName}_nn'이 없다 — 원통 벽 구성을 모르겠다.");
    }

    private static int BuildTier(Transform group, string tier, float[] phis, float y, float r, Vector2 axis, Material mat)
    {
        int n = 0;
        foreach (float phi in phis)
        {
            float rad = phi * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(axis.x + r * Mathf.Cos(rad), y, axis.y + r * Mathf.Sin(rad));
            BuildOneLamp(group, $"VIS_S4_Lamp_{tier}_{Mathf.RoundToInt(phi):000}", pos, mat);
            n++;
        }
        return n;
    }

    private static void BuildOneLamp(Transform group, string name, Vector3 localPos, Material mat)
    {
        GameObject lampGo = new GameObject(name);
        lampGo.transform.SetParent(group, false);
        lampGo.transform.localPosition = localPos;
        lampGo.transform.localRotation = Quaternion.identity;

        Light light = lampGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = LampColor;
        light.range = LampRange;
        light.intensity = LampIntensity;
        // 픽셀 조명 수 한계 대응(ProjectSettings 불변, 오브젝트 설정만): Auto면 오브젝트마다 영향이 큰 등부터 픽셀 조명이 되고
        // 나머지는 버텍스/SH로 내려간다 [S1-L 선례, 추정].
        light.renderMode = LightRenderMode.Auto;
        light.shadows = LightShadows.None; // 점광원 9개 그림자는 비용이 커서 끈다(연출 전용) [추정].

        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mesh.name = name + "_Mesh";
        Collider col = mesh.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col); // 콜라이더 0 — 시각 전용 [계약 C1-6].
        mesh.transform.SetParent(lampGo.transform, false);
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localRotation = Quaternion.identity;
        mesh.transform.localScale = Vector3.one * (LampRadius * 2f);
        MeshRenderer mr = mesh.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 등 구가 방향광(INF1-2) 그림자를 드리우지 않게 [추정].
        }
    }

    // ───────────────────────── 머티리얼 ─────────────────────────

    /// <summary>Standard 머티리얼을 Materials/Generated/M4_S4_&lt;용도&gt;.mat로 만든다 — 있으면 재사용하고 값을 다시 맞춘다(S1_Lighting·S3_Dress 방식).
    /// fade면 Standard 인스펙터의 Fade 모드와 같은 상태(렌더 모드·블렌드·ZWrite·키워드·렌더 큐)를 코드로 넣는다. Map4Palette·Map4_*.mat·팀 .mat 불변.</summary>
    private static Material GetOrCreate(MatSpec spec)
    {
        EnsureDir(MaterialDir);
        string path = $"{MaterialDir}/{spec.name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null) { Debug.LogError($"[S4_Dress] Standard 셰이더를 찾지 못했다 — '{spec.name}'을 만들지 않는다."); return null; }
            mat = new Material(standard);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = spec.albedo;
        mat.SetFloat("_Glossiness", spec.smoothness);
        mat.SetFloat("_Metallic", spec.metallic);
        if (spec.emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", spec.emission);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.SetColor("_EmissionColor", Color.black);
        }
        if (spec.fade) SetupFade(mat); else SetupOpaque(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Standard "Fade" = _Mode 2, SrcAlpha / OneMinusSrcAlpha, ZWrite 0, _ALPHABLEND_ON만 켬, RenderType Transparent.
    /// 큐는 3000 대신 2990 [추정 — ShellRenderQueue 주석].</summary>
    private static void SetupFade(Material mat)
    {
        mat.SetFloat("_Mode", 2f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = ShellRenderQueue;
    }

    /// <summary>재사용한 에셋이 투명 상태로 남아 있지 않게 불투명 기본값으로 되돌린다(Standard "Opaque").</summary>
    private static void SetupOpaque(Material mat)
    {
        mat.SetFloat("_Mode", 0f);
        mat.SetOverrideTag("RenderType", "");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1;
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
