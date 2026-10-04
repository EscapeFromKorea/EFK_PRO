#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 섹터2 "격리 구출" 시각·재질·국지 조명(R2 계약 C1-0·C1-6, 지시서 진행/지시서/R2/S2-L.md). S2_Builder.Build의 ④에서
/// `S2_Dress.Apply(generated, refs)`로 부른다(Wire 다음). 콜라이더를 만들거나 바꾸지 않고, GEO의 Transform·Collider·물리 재질도 건드리지 않는다.
///
/// [하는 일]
/// - 어지러운 물건(refs.obstacles 11): S1 복도 가구의 연장선 [확정 S2 설계 §1 "어지러운 모습은 S1의 연장선"]. 쓰러진 선반은 S1 가구 색
///   (팔레트 "Door" 값) 그대로, 책상·캐비닛은 같은 톤 안에서 나무판·도장 철판으로 나눈다(이름 GEO_S2_Obst_Shelf*/Desk*/Cab* — 계약 C2-2).
/// - 벽(refs.walls)·천장(refs.ceilings)·숨은 벽(refs.isoHiddenWall): 이름으로 방 외벽·격리 공간 벽·격리 천장·수납함·레버 칸으로 나눠 톤을 입힌다.
///   숨은 벽은 격리 천장 재질(M4_S2_IsoCeil)로 칠해 천장에 숨긴다 [2차판정 11 — 설계 [확정] "갑자기 등장·안내 없음"].
/// - `VIS_S2_Iso_DropLine`: 숨은 벽 바로 아래, 격리 천장 `GEO_S2_Ceil_Iso` 아랫면(y 4.0)에 붙은 얇은 판 1개
///   (x −14.2~−13.8 · y 3.98~4.0 · z 30.5~53.5), 콜라이더 없음 [판정 2 필수 시각물]. R3 S2-B3가 천장 슬롯을 Ceil_Iso로 메워
///   [2차판정 10 — C3g] 옛 자리(y 4.0~4.3)는 천장 상자 속이 됐다 → 천장 아랫면 바로 밑으로 옮겼다(두께 0.02 [추정], 수정 M3R1 —
///   형태는 컨트롤타워 확정 대기). 발광·경고색·안내 표시 없음 — 천장보다 조금 어두운 무광 이음매색만 [2차판정 11].
/// - 레버 3개(refs.leverRoots): `lever_pivot`·`lever_head`·받침대(`부모 이름 + _Body`) 렌더러의 sharedMaterial만 교체
///   [2차판정 10 "레버 재질 교체(팔레트 통일)는 유지"]. 레버 3개는 S2_Builder가 만든다. 목록이 비면(빌드 실패 등) 오류 없이 건너뛴다.
/// - 국지 조명(generated/VIS_S2_Lights): 레버 칸 3곳 안 약한 보조등만(메시 없음, 버텍스 조명 고정 — 방 바닥 한 장의 픽셀 조명 자리를
///   뺏지 않게, 보고서 "픽셀 조명 한계"). 격리 공간 전용 등은 두지 않는다(격리 기능이 없다) [2차판정 11].
///
/// [하지 않는 일] 전역 조명(Master 방향광·환경광·그림자 설정)·ProjectSettings 불변(판정 1 — 방향광 그림자는 INF1-2).
/// 블록·문·격리 컨트롤러 그룹·마찰 0 조각(같은 GO 렌더러, 초안 :172 — Dress 대상 아님)의 렌더러와 모든 팀 컴포넌트 값 불변. Refs는 읽기만.
/// Map4Palette·Map4_*.mat·팀 .mat 불변.
///
/// 출처 표기: [확정] 설계 사용자 결정 · [판정] 컨트롤타워 판정 · [제안] 초안 제안값 · [팀] 팀 코드 · [계산] 계산값 · [추정] 이 파일이 정한 값(실측 필요).
/// </summary>
public static class S2_Dress
{
    // ───────────────────────── 이름·경로 ─────────────────────────
    private const string LightsGroupName = "VIS_S2_Lights";                         // [계약 C1-6]
    private const string DropLineName = "VIS_S2_Iso_DropLine";                      // [판정 2 · 초안 §2-7 :299]
    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated"; // [계약 C1-6]
    private const string WallPrefix = "GEO_S2_Wall_";                               // [계약 C2-2]
    private const string CeilPrefix = "GEO_S2_Ceil_";                               // [계약 C2-2]
    private const string ObstPrefix = "GEO_S2_Obst_";                               // [계약 C2-2]
    private const string PivotName = "lever_pivot";                                 // [팀 DoorSystemMenuItem.SpawnLever]
    private const string HeadName = "lever_head";                                   // [팀 DoorSystemMenuItem.SpawnLever]
    private const string BodySuffix = "_Body";                                      // [제안 초안 :43·:102 — 부모 이름 + _Body]

    // ───────────────────────── 개수(계약 확인용 — 다르면 로그만) ─────────────────────────
    private const int ExpectedObstacles = 11;  // [계약 C1-1]
    private const int ExpectedWalls = 29;      // [2차판정 10 — R3 S2-B3 #14 GEO_S2_Wall_Housing_In 삭제] 초안 §2-9 :329의 30 − 1
    private const int ExpectedCeilings = 6;    // [초안 §2-9 :330]
    private const int ExpectedLevers = 3;      // [계약 C1-1]

    // ───────────────────────── 선 표시 [판정 2 · 초안 §2-7 :299 · 수정 M3R1] ─────────────────────────
    // x·z = 숨은 벽 발자국(x −14.2~−13.8 · z 30.5~53.5, 초안 §2-1 #17) 그대로. y는 옛 슬롯(4.0~4.3)이 R3 S2-B3에서 Ceil_Iso로 메워져
    // (S2_Builder.cs:183 [2차판정 10]) 천장 아랫면 4.0 바로 밑 얇은 판으로 옮겼다 — 윗면 = 천장 아랫면(맞닿음), 아랫면 3.98.
    // 두께 0.02 [추정] — 아랫면이 천장 아랫면과 같은 평면이 아니게(z-파이팅 방지) 하되 옆면이 거의 안 보이게. 형태는 컨트롤타워 확정 대기.
    private const float IsoCeilBottomY = 4.0f;      // [초안 §2-1 #12 · S2_Builder.cs:183 GEO_S2_Ceil_Iso 아랫면]
    private const float DropLineThickness = 0.02f;  // [추정]
    private const float HiddenWallBottomY = 4.3f;   // [판정 2 · 초안 §2-1 #17 · S2_Builder.cs:188 GEO_S2_Wall_IsoHidden 아랫면] — 대조용(읽기만)
    private const string IsoCeilName = "GEO_S2_Ceil_Iso"; // [계약 C2-2] — 대조용(읽기만)
    private static readonly Vector3 DropLineMin = new Vector3(-14.2f, IsoCeilBottomY - DropLineThickness, 30.5f); // [계산] (−14.2, 3.98, 30.5)
    private static readonly Vector3 DropLineMax = new Vector3(-13.8f, IsoCeilBottomY, 53.5f);                     // [계산] (−13.8, 4.0, 53.5)

    // ───────────────────────── 등 — 칸 보조등만 [2차판정 11] ─────────────────────────
    /// <summary>칸 보조등 색 — 이전 라운드 값 (1.0, 0.75, 0.1) 유지(S1_Lighting.cs:14와 같은 값). 판정 11은 강도만 낮추라 했다 [2차판정 11 · 계약 :733].</summary>
    private static readonly Color BayLightColor = new Color(1.0f, 0.75f, 0.1f);

    /// <summary>[추정] 레버 칸 보조등 — 칸 안 뚜껑 아랫면(1.8)에서 0.15 아래, 레버 쪽(뒷벽 쪽)으로 치우친 자리.
    /// 칸 입구 바깥면까지 5.0 &gt; 범위 4.5 → 더미·방 쪽으로 새지 않는다. head 쓸림 bbox 먼 모서리까지 3.9 &lt; 4.5 [계산 — 보고서].
    /// 메시 없음(칸 안 걷는 공간·head 위 틈을 가리지 않음).</summary>
    private static readonly Vector3[] BayLightPos =
    {
        new Vector3(-35.5f, 1.65f, 42f),  // Bay1(L1 함정) 안쪽 x −38~−31 · z 37.5~46.5 [초안 §2-1 #23]
        new Vector3(35.5f, 1.65f, 42f),   // Bay2(L2) 안쪽 x 31~38 · z 37.5~46.5 [초안 §2-1 #28]
        new Vector3(20f, 1.65f, 2.5f),    // Bay3(L3) 안쪽 x 15.5~24.5 · z 0~7 [초안 §2-1 #33]
    };
    private static readonly string[] BayLidNames = { "GEO_S2_Ceil_Bay1_Lid", "GEO_S2_Ceil_Bay2_Lid", "GEO_S2_Ceil_Bay3_Lid" }; // [계약 C2-2]
    private const float BayLightRange = 4.5f;      // [추정] 위 주석의 두 조건을 함께 만족
    /// <summary>[2차판정 11 "칸 보조등은 약하게"] 이전 1.2 → 0.6(절반) [추정 — 값은 S2-L 재량, 근거 보고서 S2-L3.md]. 틈새로 칸 안이 주변보다
    /// 밝아 보이지 않게 낮추되 0으로 끄지는 않는다(판정은 "없앤다"가 아니라 "약하게"). 실측 조정 대상.</summary>
    private const float BayLightIntensity = 0.6f;

    // ───────────────────────── 재질 값 [추정 — 초안·설계는 색을 정하지 않았다] ─────────────────────────
    private struct MatSpec
    {
        public string name; public Color albedo; public float smoothness; public float metallic; public Color emission;
        public MatSpec(string n, Color a, float s, float m, Color e) { name = n; albedo = a; smoothness = s; metallic = m; emission = e; }
    }
    private static readonly Color NoEmission = Color.black;
    // 어지러운 물건 — 선반은 S1 가구 색(팔레트 "Door" (0.35,0.36,0.38)·smoothness 0.35, S1_Builder.cs:357) 그대로 [S1 연장선], 나머지 [추정]
    private static readonly MatSpec Shelf     = new MatSpec("M4_S2_ClutterShelf", new Color(0.35f, 0.36f, 0.38f), 0.35f, 0.0f, NoEmission);
    private static readonly MatSpec Desk      = new MatSpec("M4_S2_ClutterDesk",  new Color(0.42f, 0.33f, 0.24f), 0.25f, 0.0f, NoEmission);
    private static readonly MatSpec Cabinet   = new MatSpec("M4_S2_ClutterCab",   new Color(0.30f, 0.36f, 0.34f), 0.40f, 0.5f, NoEmission);
    // 구조물 [추정]
    private static readonly MatSpec Wall      = new MatSpec("M4_S2_Wall",         new Color(0.50f, 0.52f, 0.52f), 0.15f, 0.0f, NoEmission);
    private static readonly MatSpec IsoWall   = new MatSpec("M4_S2_IsoWall",      new Color(0.46f, 0.52f, 0.48f), 0.30f, 0.0f, NoEmission);
    private static readonly MatSpec IsoCeil   = new MatSpec("M4_S2_IsoCeil",      new Color(0.30f, 0.31f, 0.32f), 0.10f, 0.0f, NoEmission);
    private static readonly MatSpec Housing   = new MatSpec("M4_S2_Housing",      new Color(0.25f, 0.26f, 0.28f), 0.35f, 0.5f, NoEmission);
    private static readonly MatSpec Bay       = new MatSpec("M4_S2_Bay",          new Color(0.38f, 0.40f, 0.42f), 0.30f, 0.4f, NoEmission);
    // 선 표시 — 무발광 이음매 [2차판정 11]: 경고 호박색 (1.0, 0.72, 0.05)·발광 (0.45, 0.30, 0.0) 폐기.
    // 격리 천장 IsoCeil (0.30, 0.31, 0.32)보다 조금 어두운 무광 회색 — 천장 판 사이 이음매 정도로만 보이게 [추정].
    private static readonly MatSpec DropLine  = new MatSpec("M4_S2_DropLine",     new Color(0.22f, 0.23f, 0.24f), 0.05f, 0.0f, NoEmission);
    // 레버 — 팀 메뉴 색 그대로(Lever (0.45,0.45,0.5) · LeverHead (0.8,0.35,0.3), DoorSystemMenuItem.SpawnLever [팀]), 광택·금속도 [추정]
    private static readonly MatSpec Lever     = new MatSpec("M4_S2_Lever",        new Color(0.45f, 0.45f, 0.50f), 0.40f, 0.5f, NoEmission);
    private static readonly MatSpec LeverHead = new MatSpec("M4_S2_LeverHead",    new Color(0.80f, 0.35f, 0.30f), 0.45f, 0.0f, NoEmission);
    // (등 구 시각물용 발광 재질 사양은 삭제 — 등 구가 없어졌다 [2차판정 11]. 옛 Generated 에셋은 지우지 않고 그대로 둔다.)

    /// <summary>R2 계약 C1-0. 예외는 빌더가 잡지만(C5), 단계마다 따로 잡아 한 단계 실패가 나머지를 막지 않게 한다. 지형은 지우지 않는다.</summary>
    public static void Apply(Transform generated, S2_Refs refs)
    {
        if (generated == null) { Debug.LogError("[S2_Dress] generated가 null이다 — 시각 처리를 하지 않는다."); return; }
        if (refs == null) { Debug.LogError("[S2_Dress] refs가 null이다 — 시각 처리를 하지 않는다."); return; }
        if (refs.generated != null && refs.generated != generated)
            Debug.LogWarning("[S2_Dress] refs.generated가 인자 generated와 다르다 — 인자 쪽을 기준으로 쓴다.", generated);

        int painted = 0, levers = 0, bayLights = 0;
        bool dropLine = false;
        try { painted += DressObstacles(refs.obstacles); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 장애물 재질 예외: {e}"); }
        try { painted += DressWalls(refs.walls, refs.isoHiddenWall); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 벽 재질 예외: {e}"); }
        try { painted += DressCeilings(refs.ceilings); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 천장 재질 예외: {e}"); }
        try { painted += DressHiddenWall(refs.isoHiddenWall); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 숨은 벽 재질 예외: {e}"); }
        try { dropLine = BuildDropLine(generated, refs.isoHiddenWall, refs.ceilings); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 선 표시 예외: {e}"); }
        try { levers = DressLevers(refs.leverRoots); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 레버 재질 예외: {e}"); }
        try { BuildLights(generated, refs.ceilings, out bayLights); } catch (System.Exception e) { Debug.LogError($"[S2_Dress] 국지 조명 예외: {e}"); }

        Debug.Log($"[S2_Dress] 완료 — 재질 교체 렌더러 {painted}개, 선 표시 {(dropLine ? "생성(천장 밑 무발광 판)" : "실패")}, 레버 재질 {levers}/{ExpectedLevers}" +
                  $"{(levers == 0 ? "(레버 없음 — 미적용)" : "")}, 칸 보조등 {bayLights}/{BayLightPos.Length}(버텍스·강도 {BayLightIntensity}), " +
                  "격리 공간 등 0·발광 재질 0 [2차판정 11]. 전역 조명 불변.");
    }

    // ───────────────────────── 어지러운 물건 ─────────────────────────

    /// <summary>이름 GEO_S2_Obst_Shelf*/Desk*/Cab*로만 구분한다(계약 C2-2). 모르는 이름은 선반 재질 + 경고.</summary>
    private static int DressObstacles(List<Transform> list)
    {
        if (list == null) { Debug.LogError("[S2_Dress] refs.obstacles가 null이다 — 장애물 재질을 건너뛴다."); return 0; }
        if (list.Count != ExpectedObstacles)
            Debug.LogError($"[S2_Dress] refs.obstacles {list.Count}개 — 계약은 {ExpectedObstacles}개. 있는 것만 입힌다.");
        Material shelf = GetOrCreate(Shelf), desk = GetOrCreate(Desk), cab = GetOrCreate(Cabinet);
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null) { Debug.LogError($"[S2_Dress] refs.obstacles[{i}]가 null이다 — 건너뛴다."); continue; }
            string id = StripPrefix(t.name, ObstPrefix);
            Material m;
            if (id.StartsWith("Shelf")) m = shelf;
            else if (id.StartsWith("Desk")) m = desk;
            else if (id.StartsWith("Cab")) m = cab;
            else
            {
                Debug.LogWarning($"[S2_Dress] 장애물 '{t.name}' 종류를 이름으로 알 수 없다 — 선반 재질을 입힌다.", t);
                m = shelf;
            }
            n += Paint(t, m);
        }
        return n;
    }

    // ───────────────────────── 벽·천장·숨은 벽 ─────────────────────────

    /// <summary>GEO_S2_Wall_ 뒤 이름으로: IsoHidden → 격리 천장 [2차판정 11] / Housing_* → 수납함 / Iso_* → 격리 벽 / Bay* → 레버 칸 /
    /// 나머지(외벽·인방·전실) → 방 벽. 숨은 벽은 DressHiddenWall이 따로 입히므로 hidden이 있으면 여기서는 건너뛴다(두 번 세지 않게) —
    /// 이름 매핑은 refs.isoHiddenWall이 null일 때의 대비다.</summary>
    private static int DressWalls(List<Transform> list, Transform hidden)
    {
        if (list == null) { Debug.LogError("[S2_Dress] refs.walls가 null이다 — 벽 재질을 건너뛴다."); return 0; }
        if (list.Count != ExpectedWalls)
            Debug.LogWarning($"[S2_Dress] refs.walls {list.Count}개 — 초안 §2-9는 {ExpectedWalls}개. 있는 것만 입힌다.");
        Material wall = GetOrCreate(Wall), iso = GetOrCreate(IsoWall), housing = GetOrCreate(Housing), bay = GetOrCreate(Bay), ceil = GetOrCreate(IsoCeil);
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null) { Debug.LogError($"[S2_Dress] refs.walls[{i}]가 null이다 — 건너뛴다."); continue; }
            if (hidden != null && t == hidden) continue;
            string id = StripPrefix(t.name, WallPrefix);
            Material m = wall;
            if (id == "IsoHidden") m = ceil;                    // [2차판정 11] 숨은 벽 = 천장 재질
            else if (id.StartsWith("Housing_")) m = housing;
            else if (id.StartsWith("Iso_")) m = iso;
            else if (id.StartsWith("Bay")) m = bay;
            n += Paint(t, m);
        }
        return n;
    }

    /// <summary>GEO_S2_Ceil_ 뒤 이름으로: Iso·IsoLip → 격리 천장 / Housing_Top → 수납함 / Bay*_Lid → 레버 칸. 모르는 이름은 격리 천장 + 경고.</summary>
    private static int DressCeilings(List<Transform> list)
    {
        if (list == null) { Debug.LogError("[S2_Dress] refs.ceilings가 null이다 — 천장 재질을 건너뛴다."); return 0; }
        if (list.Count != ExpectedCeilings)
            Debug.LogWarning($"[S2_Dress] refs.ceilings {list.Count}개 — 초안 §2-9는 {ExpectedCeilings}개. 있는 것만 입힌다.");
        Material ceil = GetOrCreate(IsoCeil), housing = GetOrCreate(Housing), bay = GetOrCreate(Bay);
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null) { Debug.LogError($"[S2_Dress] refs.ceilings[{i}]가 null이다 — 건너뛴다."); continue; }
            string id = StripPrefix(t.name, CeilPrefix);
            Material m;
            if (id == "Iso" || id == "IsoLip") m = ceil;
            else if (id.StartsWith("Housing_")) m = housing;
            else if (id.StartsWith("Bay")) m = bay;
            else
            {
                Debug.LogWarning($"[S2_Dress] 천장 '{t.name}' 종류를 이름으로 알 수 없다 — 격리 천장 재질을 입힌다.", t);
                m = ceil;
            }
            n += Paint(t, m);
        }
        return n;
    }

    /// <summary>숨은 벽(정지 벽, 판정 2): 자식 VIS 렌더러 재질만 격리 천장 재질(M4_S2_IsoCeil)로 — 천장에 숨긴다 [2차판정 11].
    /// 위치·콜라이더는 손대지 않는다.</summary>
    private static int DressHiddenWall(Transform hidden)
    {
        if (hidden == null) { Debug.LogError("[S2_Dress] refs.isoHiddenWall이 null이다 — 숨은 벽 재질을 건너뛴다."); return 0; }
        return Paint(hidden, GetOrCreate(IsoCeil));
    }

    // ───────────────────────── 선 표시 ─────────────────────────

    /// <summary>`VIS_S2_Iso_DropLine` — 격리 천장 아랫면 바로 밑 얇은 판 1개(섹터 로컬, 위 상수), 생성 직후 Collider 제거.
    /// 대조(읽기만, 다르면 경고만 — 좌표는 상수를 쓴다): 숨은 벽 콜라이더의 x 범위 = 선 x 범위, 아랫면 = 4.3 /
    /// 격리 천장 `GEO_S2_Ceil_Iso` 콜라이더 아랫면 = 선 윗면이고 x 범위가 선을 덮는다(슬롯이 다시 열리면 경고 — S2-B3 검토 요청 7).</summary>
    private static bool BuildDropLine(Transform g, Transform hidden, List<Transform> ceilings)
    {
        Transform old = g.Find(DropLineName);
        if (old != null) Object.DestroyImmediate(old.gameObject); // 같은 빌드에서 두 번 불려도 겹치지 않게(우리 VIS_만).

        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = DropLineName;
        Object.DestroyImmediate(line.GetComponent<Collider>()); // 콜라이더 0 — 시각 전용 [계약 C1-6].
        line.transform.SetParent(g, false);
        line.transform.localPosition = (DropLineMin + DropLineMax) * 0.5f;   // [계산] (−14.0, 3.99, 42.0)
        line.transform.localRotation = Quaternion.identity;
        line.transform.localScale = DropLineMax - DropLineMin;               // [계산] (0.4, 0.02, 23.0)
        MeshRenderer mr = line.GetComponent<MeshRenderer>();
        Material m = GetOrCreate(DropLine);
        if (mr != null && m != null) mr.sharedMaterial = m;

        // 숨은 벽 대조 — x 범위 = 선 x 범위, 아랫면 = 4.3.
        if (hidden != null && ReadBox(g, hidden, out Vector3 hMin, out Vector3 hMax))
        {
            if (Mathf.Abs(hMin.y - HiddenWallBottomY) > 0.001f ||
                Mathf.Abs(hMin.x - DropLineMin.x) > 0.001f || Mathf.Abs(hMax.x - DropLineMax.x) > 0.001f)
                Debug.LogWarning($"[S2_Dress] 숨은 벽 콜라이더(x {hMin.x:0.###}~{hMax.x:0.###}, 아랫면 {hMin.y:0.###})가 " +
                                 $"선 표시(x {DropLineMin.x}~{DropLineMax.x})·숨은 벽 아랫면 {HiddenWallBottomY}와 어긋난다 — 초안 §2-1 #17·§2-7 확인.", hidden);
        }

        // 격리 천장 대조 — 아랫면 = 선 윗면(맞닿음), x 범위가 선 x 범위를 덮는다(슬롯이 메워진 상태).
        Transform ceilIso = null;
        if (ceilings != null)
            foreach (Transform c in ceilings)
                if (c != null && c.name == IsoCeilName) { ceilIso = c; break; }
        if (ceilIso == null)
            Debug.LogWarning($"[S2_Dress] refs.ceilings에 '{IsoCeilName}'이 없다 — 선 표시가 천장 아랫면에 붙었는지 대조하지 못했다.");
        else if (ReadBox(g, ceilIso, out Vector3 cMin, out Vector3 cMax))
        {
            if (Mathf.Abs(cMin.y - DropLineMax.y) > 0.001f || cMin.x > DropLineMin.x + 0.001f || cMax.x < DropLineMax.x - 0.001f)
                Debug.LogWarning($"[S2_Dress] 격리 천장 콜라이더(x {cMin.x:0.###}~{cMax.x:0.###}, 아랫면 {cMin.y:0.###})가 " +
                                 $"선 표시(x {DropLineMin.x}~{DropLineMax.x}, 윗면 {DropLineMax.y})를 덮지 않는다 — 슬롯이 다시 열렸는지 S2_Builder #12 확인.", ceilIso);
        }
        return true;
    }

    /// <summary>GEO의 BoxCollider를 섹터 로컬 경계 상자로 읽는다(읽기만). 회전 0 전제(S2 지형 상자는 모두 축 정렬 — 초안 §2-1).</summary>
    private static bool ReadBox(Transform g, Transform geo, out Vector3 min, out Vector3 max)
    {
        min = max = Vector3.zero;
        BoxCollider box = geo.GetComponent<BoxCollider>();
        if (box == null) return false;
        Vector3 c = g.InverseTransformPoint(geo.TransformPoint(box.center));
        Vector3 h = Vector3.Scale(box.size, geo.lossyScale) * 0.5f;
        min = c - h; max = c + h;
        return true;
    }

    // ───────────────────────── 레버(팀 렌더러 — 이 한 곳만) ─────────────────────────

    /// <summary>refs.leverRoots[i] 아래 `lever_pivot`·`lever_head`·`&lt;부모 이름&gt;_Body`의 MeshRenderer.sharedMaterial만 바꾼다
    /// [2차판정 10 "레버 재질 교체(팔레트 통일)는 유지" · 계약 C1-6]. Transform·Collider·LeverHead 값·팀 .mat은 건드리지 않는다.
    /// 레버 3개는 S2_Builder가 만든다(F1 Generate 로그 "레버 재질 3/3"). 빈 목록(빌드 실패 등)이면 오류 없이 0을 돌려준다.</summary>
    private static int DressLevers(List<Transform> roots)
    {
        if (roots == null) { Debug.LogError("[S2_Dress] refs.leverRoots가 null이다 — 레버 재질을 건너뛴다."); return 0; }
        if (roots.Count == 0)
        {
            Debug.Log("[S2_Dress] refs.leverRoots가 비어 있다 — 레버 재질 교체 미적용(레버 없음).");
            return 0;
        }
        if (roots.Count != ExpectedLevers)
            Debug.LogError($"[S2_Dress] refs.leverRoots {roots.Count}개 — 계약은 {ExpectedLevers}개. 있는 것만 입힌다.");
        Material lever = GetOrCreate(Lever), head = GetOrCreate(LeverHead);
        int n = 0;
        for (int i = 0; i < roots.Count; i++)
        {
            Transform root = roots[i];
            if (root == null) { Debug.LogError($"[S2_Dress] refs.leverRoots[{i}]가 null이다 — 건너뛴다."); continue; }
            int done = 0;
            done += SetTeamRenderer(FindDeep(root, PivotName), lever, root, PivotName);
            done += SetTeamRenderer(FindDeep(root, HeadName), head, root, HeadName);
            string bodyName = root.name + BodySuffix;
            done += SetTeamRenderer(FindDeep(root, bodyName), lever, root, bodyName);
            if (done == 3) n++;
            else Debug.LogError($"[S2_Dress] 레버 '{root.name}' — 3개 중 {done}개만 재질을 바꿨다(위 로그 확인).", root);
        }
        return n;
    }

    private static int SetTeamRenderer(Transform t, Material m, Transform root, string label)
    {
        if (t == null) { Debug.LogError($"[S2_Dress] 레버 '{root.name}' 아래 '{label}'을 찾지 못했다 — 건너뛴다.", root); return 0; }
        MeshRenderer r = t.GetComponent<MeshRenderer>();
        if (r == null) { Debug.LogError($"[S2_Dress] '{t.name}'에 MeshRenderer가 없다 — 건너뛴다.", t); return 0; }
        if (m == null) return 0;
        r.sharedMaterial = m;
        EditorUtility.SetDirty(r);
        return 1;
    }

    /// <summary>이름으로 자손 1개를 찾는다(자기 자신 제외, 깊이 우선 첫 1개).</summary>
    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            Transform deeper = FindDeep(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // ───────────────────────── 국지 조명 ─────────────────────────

    /// <summary>generated/VIS_S2_Lights 아래: 레버 칸 보조등 3(점광원 ForceVertex·그림자 없음·메시 없음)만. 격리 공간 전용 등은 없다 [2차판정 11].
    /// 칸 보조등은 뚜껑(refs.ceilings의 GEO_S2_Ceil_Bay{k}_Lid)이 있을 때만 만든다. 뚜껑이 없어 0개여도 그룹은 만든다(빈 그룹 — 다시 만들 때
    /// 옛 그룹을 지우는 방식은 그대로라 이전 라운드 등 GO가 남지 않는다). 전역 조명은 건드리지 않는다.</summary>
    private static void BuildLights(Transform g, List<Transform> ceilings, out int bayLights)
    {
        bayLights = 0;
        Transform old = g.Find(LightsGroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject); // 같은 빌드에서 두 번 불려도 겹치지 않게(우리 그룹만, 지형 아님).

        GameObject group = new GameObject(LightsGroupName);
        group.transform.SetParent(g, false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;

        HashSet<string> lids = new HashSet<string>();
        if (ceilings == null) Debug.LogError("[S2_Dress] refs.ceilings가 null이다 — 레버 칸 보조등을 만들지 않는다.");
        else
            foreach (Transform c in ceilings)
                if (c != null) lids.Add(c.name);

        for (int k = 0; k < BayLightPos.Length; k++)
        {
            if (!lids.Contains(BayLidNames[k]))
            {
                if (ceilings != null)
                    Debug.LogWarning($"[S2_Dress] refs.ceilings에 '{BayLidNames[k]}'이 없다 — Bay{k + 1} 보조등을 만들지 않는다.");
                continue;
            }
            BuildBayLight(group.transform, k + 1, BayLightPos[k]);
            bayLights++;
        }
    }

    private static void BuildBayLight(Transform group, int bay, Vector3 localPos)
    {
        GameObject go = new GameObject($"VIS_S2_BayLight_{bay}");
        go.transform.SetParent(group, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = BayLightColor;
        light.range = BayLightRange;
        light.intensity = BayLightIntensity;
        // [추정 — 보고서 "픽셀 조명 한계"] 방 바닥은 76×84 한 장(GEO_S2_Floor_Room)이라 이 등도 바닥의 픽셀 조명 자리를 다툰다. 칸 안 바닥은
        // 마찰 0 조각이 덮어 바닥에서 이 등은 보이지 않으므로, 버텍스 조명으로 고정해 바닥의 픽셀 조명 자리를 쓰지 않는다.
        // 레버·칸 벽처럼 작은 상자는 버텍스 조명으로도 윤곽이 드러난다(실측 필요).
        light.renderMode = LightRenderMode.ForceVertex;
        light.shadows = LightShadows.None;
    }

    // ───────────────────────── 공용 ─────────────────────────

    /// <summary>GEO의 시각물 자식(C2-0: `VIS_…`, 이름 바꾸기 전이면 `Visual`)의 sharedMaterial만 바꾼다. GEO 자체의 Transform·Collider는 건드리지 않는다.</summary>
    private static int Paint(Transform geo, Material m)
    {
        if (m == null) return 0;
        List<Renderer> rs = new List<Renderer>();
        foreach (Transform child in geo)
        {
            if (!(child.name.StartsWith("VIS_") || child.name == "Visual")) continue;
            Renderer r = child.GetComponent<MeshRenderer>();
            if (r != null) rs.Add(r);
        }
        if (rs.Count == 0)
        {
            Debug.LogWarning($"[S2_Dress] '{geo.name}' 아래 시각물(VIS_/Visual)을 찾지 못했다 — 재질을 입히지 않는다.", geo);
            return 0;
        }
        foreach (Renderer r in rs)
        {
            r.sharedMaterial = m;
            EditorUtility.SetDirty(r);
        }
        return rs.Count;
    }

    private static string StripPrefix(string s, string prefix) => s.StartsWith(prefix) ? s.Substring(prefix.Length) : s;

    /// <summary>Standard 머티리얼을 Materials/Generated/M4_S2_&lt;용도&gt;.mat로 만든다 — 있으면 재사용하고 값을 다시 맞춘다(S1_Lighting·Map4Build.GetMaterial 방식).
    /// Map4Palette·Map4_*.mat·팀 .mat은 건드리지 않는다.</summary>
    private static Material GetOrCreate(MatSpec spec)
    {
        EnsureDir(MaterialDir);
        string path = $"{MaterialDir}/{spec.name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null) { Debug.LogError($"[S2_Dress] Standard 셰이더를 찾지 못했다 — '{spec.name}'을 만들지 않는다."); return null; }
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
