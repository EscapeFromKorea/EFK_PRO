#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 섹터3 "점프" 시각·재질·국지 조명(R2 계약 C1-0·C1-6, 지시서 진행/지시서/R2/S3-L.md). S3_Builder.Build의 ④에서
/// `S3_Dress.Apply(generated, refs)`로 부른다(Wire 다음). 콜라이더를 만들거나 바꾸지 않고, GEO의 Transform·Collider도 건드리지 않는다.
///
/// [하는 일]
/// - 앞 점프 구간(refs.frontPath, L1~L6·턴·지름길·시작): S1·S2의 어지러운 실험실을 잇는다 — 금속 격자 발판·철제 선반·
///   나무/플라스틱 상자 톤 [제안 초안 §1-2 :54]. 발판/선반/상자 구분은 초안 §2 좌표표 `종류` 열(id → 종류)로만 한다.
/// - 뒤 실타래 구간(refs.backPath, B1~B7·출구): 짙은 남색 발판, 상자·선반 없음 [제안 초안 §1-4 :78]. 세이브 발판 B1_P1은 초록 [제안 초안 §1-3 :57].
/// - 벽(refs.walls)·기둥(refs.columns): 섹터 로컬 z 중심 &lt; 76.375이면 앞 톤, 아니면 뒤 톤 [초안 §1-3 :59, 지시서 구현 요구 1].
/// - 받침 바닥(refs.catchFloor = GEO_S3_Floor_Catch, 허공 아래 윗면 y −12): 거의 검은 Void 재질 — "어두운 허공" [제안 초안 §1-4 :80]
///   · 받침 바닥에 Void를 입히는 것과 대상을 refs.catchFloor로 받는 것은 [2차판정 13]·[계약 R2-C1-3] (이름 검색 없음, R3 S3-L3).
/// - 줄다리 26개(refs.bridges — 빌더가 ③′ Wire 뒤·Dress 앞에서 채운다 [2차판정 2 (나)], Dress는 읽기만):
///   같은 GO의 LineRenderer.sharedMaterial = M4_S3_BridgeRibbon [제안 초안 §3 :353, 계약 C1-6:161 — 지정 주체 Dress].
///   팀 기믹 렌더러를 바꾸는 곳은 여기 한 곳뿐이다. lineWidth·alignment·회전은 빌더 몫이라 읽지도 바꾸지도 않는다 [판정 9].
/// - 국지 조명: generated/VIS_S3_Lights 아래 주황 점광원 13개 — 탄 받이 기둥(초안 §2 발사구 표 :317-332 '탄 받이' 열의 COL_) 윗면 위
///   [명령 제안 "발사구 기둥 윗면", 좌표 추정]. 발사구가 붙은 기둥 위에 두면 바로 아래 팀 경고 표시(흰 Standard Quad, 경고 때만
///   주황 (1,0.6,0.1) — ProjectileWarning.cs:13-14·33 [팀])를 1.8~3.3 거리에서 주황으로 물들여 평소에도 경고처럼 보인다 [계산] → 레인 건너편 받이 기둥으로 옮겼다.
///   M2R1: 받이 기둥에서도 범위 6 안에 경고 Quad가 드는 등 7개(8쌍, 모두 보이는 면을 비춤)는 범위를 경고 bounds 앞에서 끊는다(LampRangeCap [계산]
///   + 실배치 경고 렌더러 bounds 대조, 읽기만) → (등, 경고) 겹침 0. 렌더 실측 전 예방 조치다(보고서 M2R1 절).
///
/// [하지 않는 일] 전역 조명(Master 방향광·환경광·그림자 설정)·ProjectSettings 불변(판정 1 — 방향광 그림자는 INF1-2).
/// 발사구·경고·체크포인트 막대·깃발·끝 고리 렌더러와 모든 팀 컴포넌트 값 불변. Refs는 읽기만. Map4Palette·Map4_*.mat·팀 .mat 불변.
///
/// [한계 — 보고서 진행/보고/S3-L.md] 천장이 없어 방향광이 뒤 구간도 그대로 비춘다 → "어두운 허공"은 어두운 재질·낮은 smoothness·리본 발광으로만 낸다.
/// Built-in 순방향 렌더링의 오브젝트당 픽셀 조명 수 한계(품질 단계 Ultra 4 · Very High 3 · High 2 · Medium 1 · Low/Very Low 0)는
/// renderMode Auto로 받아들이고, 한 오브젝트에 닿는 등을 최대 2개로 맞췄다 [계산]. 등 메시는 그림자를 드리우지 않는다(우리 렌더러 값).
///
/// 출처 표기: [확정] 설계 사용자 결정 · [판정] 컨트롤타워 판정 · [제안] 초안 제안값 · [팀] 팀 코드 · [계산] 계산값 · [추정] 이 파일이 정한 값(실측 필요).
/// </summary>
public static class S3_Dress
{
    // ───────────────────────── 이름·경로 ─────────────────────────
    private const string LightsGroupName = "VIS_S3_Lights";                       // [계약 C1-6]
    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated"; // [계약 C1-6]
    private const string PathPrefix = "GEO_S3_Path_";                              // [계약 C2-3]
    private const string ColPrefix = "GEO_S3_Col_";                                // [계약 C2-3]
    private const string SavePadName = "GEO_S3_Path_B1_P1";                        // [계약 C2-3 / 초안 §1-3]
    private const string CatchFloorName = "GEO_S3_Floor_Catch";                    // [계약 C2-3·C1-1] 검증용 이름 비교에만 쓴다(검색 안 함)

    // ───────────────────────── 앞/뒤 경계 ─────────────────────────
    /// <summary>[초안 §1-3 :59] 앞/뒤 경계 z — T6_b2 끝 75.75와 B1 시작 77 사이.</summary>
    private const float FrontBackBoundaryZ = 76.375f;

    // ───────────────────────── 개수(계약 확인용 — 다르면 LogError만) ─────────────────────────
    private const int ExpectedBridges = 26;   // [계약 C1-1]
    private const int ExpectedLamps = 13;     // [추정] 탄 받이가 기둥인 발사구 13곳(PL_L1_01은 끝벽 W_end_S_L이 받이 [초안 §2 발사구 표 :312, R3 합침 — 옛 W_end_S_L2] — 외벽 위 금지라 등 없음)

    // ───────────────────────── 종류 표 [초안 §2 좌표표 `종류` 열] ─────────────────────────
    /// <summary>`선반`·`돌출선반` id. 나머지 앞 걷는 면(시작·발판·턴·턴블록·계단)은 격자 발판 재질.</summary>
    private static readonly HashSet<string> ShelfIds = new HashSet<string>
    {
        "L1_00", "L2_03", "L2_09", "L3_03", "L3_07", "L4_08", "L5_01", "L5_04", "SC_ledge",
    };
    /// <summary>`상자` id — 표 순서대로 나무/플라스틱을 번갈아 입힌다(어지러운 더미 [추정]).</summary>
    private static readonly string[] CrateIds =
    {
        "L3_04", "L3_05", "L3_06", "L4_04", "L4_05", "L5_03", "L6_04", "L6_05",
    };
    /// <summary>등을 올릴 기둥 = 탄 받이 기둥 [초안 §2 발사구 표 :317-332 '탄 받이' 열, 표 행 순서 — PL_L1_01(받이 = 끝벽)은 뺌]. "COL_" 접두사는 뗀 id.
    /// 경고 표시까지 거리 ≥4.25(범위 6 안 8쌍, 감쇠 근사 ≤0.074) — 발사구 기둥 위(≥1.79, 16쌍, ≤0.31)보다 경고를 덜 물들인다 [계산, 감쇠식은 추정].
    /// 남은 8쌍은 M2R1에서 LampRangeCap으로 0쌍이 됐다.</summary>
    private static readonly string[] LampColumnIds =
    {
        "L2_04_S", "L2_07_N", "L3_02_S", "L3_08_N", "L4_02_S", "L4_03_N", "L4_06_S",
        "L5_02_N", "L5_05_S", "L5_07_N", "L6_03_S", "L6_07_N", "L6_08_S",
    };

    // ───────────────────────── 등 ─────────────────────────
    /// <summary>[추정] 주황 계열(초안 §1-2 "주황 계열 조명" [제안]). S1 노란 비상등 (1.0, 0.75, 0.1)보다 붉게 해 구간을 구분한다.</summary>
    private static readonly Color LampColor = new Color(1.0f, 0.55f, 0.15f);
    /// <summary>[추정] 점광원 기본 범위(상한). 경고 표시 가까운 등 7개는 LampRangeCap으로 더 줄인다(M2R1). 6이면 한 오브젝트에 닿는 등이 최대 2개이고(7이면 4개·뒤 B1_P2까지 닿음), 외벽·받침 바닥·뒤 구간·줄다리에는 닿지 않는다 [계산 — 보고서 픽셀 조명 절].</summary>
    private const float LampRange = 6f;
    /// <summary>[추정] 강도 — 천장이 없어 방향광이 그대로 비추므로 S1(1.5)보다 높인다. 실측 조정 대상.</summary>
    private const float LampIntensity = 2.5f;
    /// <summary>[추정] 등 시각물(구) 반지름 — 기둥 윗면(1×1) 안에 들어가는 작은 등.</summary>
    private const float LampRadius = 0.15f;

    // ───────────────────────── 경고 표시 보호(수정 라운드 M2R1 — 설계 §2-7 [확정] 난이도 원천 = 함정 타이밍) ─────────────────────────
    /// <summary>[계산] 등별 범위 상한 — 범위 6 안에 팀 경고 Quad(발사구 피벗 + (0,0.5,0), 0.4×0.4, ProjectileTrapMenuItem.cs:31-38 [팀])가
    /// 드는 등 7개만 줄인다. 값 = 등 구 중심 → 가장 가까운 경고 Quad bounds 거리(초안 §2 :317-332 좌표) − WarningClearance, 0.05 내림.
    /// 8쌍 모두 경고의 보이는 면(법선 = 발사구 −forward, Standard Cull Back)을 비춘다(N·L 0.18~0.83) [계산 — 보고서 M2R1 절]. 나머지 6개는 범위 6 그대로.</summary>
    private static readonly Dictionary<string, float> LampRangeCap = new Dictionary<string, float>
    {
        { "L2_04_S", 5.30f }, // → PL_L1_01 경고 bounds 5.55
        { "L4_02_S", 4.30f }, // → PL_L4_03 4.60
        { "L4_03_N", 3.85f }, // → PL_L5_07 4.10
        { "L5_02_N", 4.95f }, // → PL_L6_07 5.20
        { "L5_05_S", 4.50f }, // → PL_L4_06 4.76
        { "L6_07_N", 4.60f }, // → PL_L6_08 4.89
        { "L6_08_S", 4.60f }, // → PL_L6_07 4.89
    };
    /// <summary>[추정] 광원 범위 구와 경고 Quad bounds 사이 여유. Built-in은 범위 구가 렌더러 bounds에 닿지 않으면 그 오브젝트에 광원을 넣지 않는다(추정 — 실측 항목).</summary>
    private const float WarningClearance = 0.25f;
    /// <summary>[추정] 이보다 작은 범위가 필요하면 등을 만들지 않는다(빛이 기둥 윗면만 비추는 의미 없는 등).</summary>
    private const float MinLampRange = 1.0f;
    private const string WarningVisualName = "Warning_Visual"; // [팀] ProjectileTrapMenuItem.cs:32

    // ───────────────────────── 재질 값 [추정 — 초안은 재질 종류만 정했다] ─────────────────────────
    private struct MatSpec
    {
        public string name; public Color albedo; public float smoothness; public float metallic; public Color emission;
        public MatSpec(string n, Color a, float s, float m, Color e) { name = n; albedo = a; smoothness = s; metallic = m; emission = e; }
    }
    private static readonly Color NoEmission = Color.black;
    // 앞(S1·S2 실험실 잇기) [제안 초안 §1-2] — 색·값은 [추정]
    private static readonly MatSpec FrontGrate  = new MatSpec("M4_S3_FrontGrate",  new Color(0.32f, 0.33f, 0.35f), 0.35f, 0.5f, NoEmission);
    private static readonly MatSpec FrontShelf  = new MatSpec("M4_S3_FrontShelf",  new Color(0.40f, 0.34f, 0.28f), 0.30f, 0.6f, NoEmission);
    private static readonly MatSpec CrateWood   = new MatSpec("M4_S3_CrateWood",   new Color(0.47f, 0.33f, 0.19f), 0.12f, 0.0f, NoEmission);
    private static readonly MatSpec CratePlastic= new MatSpec("M4_S3_CratePlastic",new Color(0.20f, 0.36f, 0.52f), 0.45f, 0.0f, NoEmission);
    private static readonly MatSpec FrontWall   = new MatSpec("M4_S3_FrontWall",   new Color(0.44f, 0.43f, 0.40f), 0.18f, 0.0f, NoEmission);
    private static readonly MatSpec FrontPipe   = new MatSpec("M4_S3_Pipe",        new Color(0.36f, 0.40f, 0.36f), 0.45f, 0.6f, NoEmission);
    // 뒤("꿈" — 어두운 허공) [제안 초안 §1-4] — 발판 짙은 남색은 [제안], 수치는 [추정]
    private static readonly MatSpec BackPad     = new MatSpec("M4_S3_BackPad",     new Color(0.07f, 0.09f, 0.24f), 0.10f, 0.0f, NoEmission);
    private static readonly MatSpec BackWall    = new MatSpec("M4_S3_BackWall",    new Color(0.03f, 0.035f, 0.08f), 0.05f, 0.0f, NoEmission);
    private static readonly MatSpec VoidFloor   = new MatSpec("M4_S3_Void",        new Color(0.015f, 0.015f, 0.03f), 0.0f, 0.0f, NoEmission);
    // 세이브 발판 — 초록은 [제안 초안 §1-3 :57], 수치·약한 발광은 [추정](팀 체크포인트 깃발 activeColor (0.25,0.9,0.4)와 같은 계열)
    private static readonly MatSpec SavePad     = new MatSpec("M4_S3_SavePad",     new Color(0.20f, 0.72f, 0.30f), 0.30f, 0.0f, new Color(0.04f, 0.18f, 0.06f));
    // 줄다리 리본 — [제안 초안 §3 :353] Albedo (0.7,0.9,1.0,1), Emission (0.35,0.45,0.5), 나머지 Standard 기본(smoothness 0.5·metallic 0)
    private static readonly MatSpec Ribbon      = new MatSpec("M4_S3_BridgeRibbon",new Color(0.7f, 0.9f, 1.0f, 1f), 0.5f, 0.0f, new Color(0.35f, 0.45f, 0.5f));
    // 등 시각물 — 등 색 그대로 발광 [추정](S1_Lighting 방식)
    private static readonly MatSpec Lamp        = new MatSpec("M4_S3_Lamp",        new Color(1.0f, 0.55f, 0.15f), 0.5f, 0.0f, new Color(1.0f, 0.55f, 0.15f));

    /// <summary>R2 계약 C1-0. 예외는 빌더가 잡지만(C5), 단계마다 따로 잡아 한 단계 실패가 나머지를 막지 않게 한다. 지형은 지우지 않는다.</summary>
    public static void Apply(Transform generated, S3_Refs refs)
    {
        if (generated == null) { Debug.LogError("[S3_Dress] generated가 null이다 — 시각 처리를 하지 않는다."); return; }
        if (refs == null) { Debug.LogError("[S3_Dress] refs가 null이다 — 시각 처리를 하지 않는다."); return; }
        if (refs.generated != null && refs.generated != generated)
            Debug.LogWarning("[S3_Dress] refs.generated가 인자 generated와 다르다 — 인자 쪽을 기준으로 쓴다.", generated);

        int painted = 0, lamps = 0, ribbons = 0;
        try { painted += DressFront(generated, refs.frontPath); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 앞 발판 재질 예외: {e}"); }
        try { painted += DressBack(generated, refs.backPath); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 뒤 발판 재질 예외: {e}"); }
        try { painted += DressByZone(generated, refs.walls, "walls", FrontWall, BackWall); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 벽 재질 예외: {e}"); }
        try { painted += DressByZone(generated, refs.columns, "columns", FrontPipe, BackWall); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 기둥 재질 예외: {e}"); }
        try { painted += DressCatchFloor(refs.catchFloor); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 받침 바닥 재질 예외: {e}"); }
        try { ribbons = DressBridges(refs.bridges); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 줄다리 리본 예외: {e}"); }
        try { lamps = BuildLamps(generated, refs.columns); } catch (System.Exception e) { Debug.LogError($"[S3_Dress] 국지 조명 예외: {e}"); }

        Debug.Log($"[S3_Dress] 완료 — 재질 교체 렌더러 {painted}개, 줄다리 리본 {ribbons}/{ExpectedBridges}, 주황 등 {lamps}/{ExpectedLamps}" +
                  $"(범위 ≤{LampRange}, 경고 가까운 등은 줄임·강도 {LampIntensity}, renderMode Auto·그림자 없음). 전역 조명 불변.");
    }

    // ───────────────────────── 앞·뒤 발판 ─────────────────────────

    private static int DressFront(Transform g, List<Transform> list)
    {
        if (list == null) { Debug.LogError("[S3_Dress] refs.frontPath가 null이다 — 앞 발판 재질을 건너뛴다."); return 0; }
        Material grate = GetOrCreate(FrontGrate), shelf = GetOrCreate(FrontShelf);
        Material wood = GetOrCreate(CrateWood), plastic = GetOrCreate(CratePlastic);
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null) { Debug.LogError($"[S3_Dress] refs.frontPath[{i}]가 null이다 — 건너뛴다."); continue; }
            if (LocalCenter(g, t).z >= FrontBackBoundaryZ)
                Debug.LogWarning($"[S3_Dress] 앞 목록의 '{t.name}' z 중심이 경계 {FrontBackBoundaryZ} 이상이다 — 목록 분류대로 앞 재질을 입힌다.", t);

            string id = StripPrefix(t.name, PathPrefix);
            Material m = grate;
            if (ShelfIds.Contains(id)) m = shelf;
            else
            {
                int k = System.Array.IndexOf(CrateIds, id);
                if (k >= 0) m = (k % 2 == 0) ? wood : plastic;
            }
            n += Paint(t, m);
        }
        return n;
    }

    private static int DressBack(Transform g, List<Transform> list)
    {
        if (list == null) { Debug.LogError("[S3_Dress] refs.backPath가 null이다 — 뒤 발판 재질을 건너뛴다."); return 0; }
        Material pad = GetOrCreate(BackPad), save = GetOrCreate(SavePad);
        int n = 0; bool saveFound = false;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null) { Debug.LogError($"[S3_Dress] refs.backPath[{i}]가 null이다 — 건너뛴다."); continue; }
            if (LocalCenter(g, t).z < FrontBackBoundaryZ)
                Debug.LogWarning($"[S3_Dress] 뒤 목록의 '{t.name}' z 중심이 경계 {FrontBackBoundaryZ} 미만이다 — 목록 분류대로 뒤 재질을 입힌다.", t);

            bool isSave = t.name == SavePadName;
            if (isSave) saveFound = true;
            n += Paint(t, isSave ? save : pad);
        }
        if (!saveFound) Debug.LogError($"[S3_Dress] backPath에 '{SavePadName}'이 없다 — 세이브 발판 초록 재질을 입히지 못했다.");
        return n;
    }

    /// <summary>벽·기둥: 섹터 로컬 z 중심 &lt; 76.375 → 앞 재질, 아니면 뒤 재질 [지시서 구현 요구 1].</summary>
    private static int DressByZone(Transform g, List<Transform> list, string label, MatSpec front, MatSpec back)
    {
        if (list == null) { Debug.LogError($"[S3_Dress] refs.{label}가 null이다 — 건너뛴다."); return 0; }
        Material mf = GetOrCreate(front), mb = GetOrCreate(back);
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null) { Debug.LogError($"[S3_Dress] refs.{label}[{i}]가 null이다 — 건너뛴다."); continue; }
            n += Paint(t, LocalCenter(g, t).z < FrontBackBoundaryZ ? mf : mb);
        }
        return n;
    }

    /// <summary>받침 바닥 = refs.catchFloor [2차판정 13 · 계약 R2-C1-3]. 이름으로 찾지 않는다 — null이면 LogError 후 이 항목만 건너뛴다(C1-0).
    /// 이름이 계약(`GEO_S3_Floor_Catch`, C1-1 개수 검증 1개)과 다르면 경고만 남기고 그대로 입힌다. 시각물 sharedMaterial만 바꾼다(Collider·Transform 불변).</summary>
    private static int DressCatchFloor(Transform catchFloor)
    {
        if (catchFloor == null)
        {
            Debug.LogError("[S3_Dress] refs.catchFloor가 null이다 — 받침 바닥 Void 재질을 건너뛴다(이름 검색으로 대체하지 않는다, 빌더 확인).");
            return 0;
        }
        if (catchFloor.name != CatchFloorName)
            Debug.LogWarning($"[S3_Dress] refs.catchFloor 이름 '{catchFloor.name}' ≠ 계약 '{CatchFloorName}' — Refs가 준 대상에 그대로 입힌다.", catchFloor);
        return Paint(catchFloor, GetOrCreate(VoidFloor));
    }

    // ───────────────────────── 줄다리 리본(팀 렌더러 — 이 한 곳만) ─────────────────────────

    /// <summary>refs.bridges[i]와 같은 GO의 LineRenderer.sharedMaterial만 바꾼다 [제안 초안 §3 :353, 계약 C1-6:161].
    /// 팀 Awake는 sharedMaterial이 비어 있을 때만 Sprites/Default를 만들므로(ThreadBridge.cs:141-145 [팀]) 지정값이 유지된다.
    /// 목록은 빌더 ③′가 채운다 [2차판정 2 (나)] — 여기서는 읽기만 하고, 대입하거나 generated 아래를 다시 뒤져 모으지 않는다.</summary>
    private static int DressBridges(List<ThreadBridge> bridges)
    {
        if (bridges == null) { Debug.LogError("[S3_Dress] refs.bridges가 null이다 — 줄다리 리본 재질을 건너뛴다."); return 0; }
        if (bridges.Count != ExpectedBridges)
            Debug.LogError($"[S3_Dress] refs.bridges {bridges.Count}개 — 계약은 {ExpectedBridges}개. 있는 것만 입힌다.");
        Material ribbon = GetOrCreate(Ribbon);
        if (ribbon == null) return 0;
        int n = 0;
        for (int i = 0; i < bridges.Count; i++)
        {
            ThreadBridge b = bridges[i];
            if (b == null) { Debug.LogError($"[S3_Dress] refs.bridges[{i}]가 null이다 — 건너뛴다."); continue; }
            LineRenderer lr = b.GetComponent<LineRenderer>();
            if (lr == null) { Debug.LogError($"[S3_Dress] 줄다리 '{b.name}'에 LineRenderer가 없다 — 건너뛴다.", b); continue; }
            lr.sharedMaterial = ribbon;
            EditorUtility.SetDirty(lr);
            n++;
        }
        return n;
    }

    // ───────────────────────── 국지 조명 ─────────────────────────

    /// <summary>generated/VIS_S3_Lights 아래 탄 받이 기둥 윗면 위에 주황 점광원 + 작은 발광 구. 기둥 윗면(1×1) 안이라 레인 걷는 면 위·외벽 위로
    /// 올라오지 않는다 [계산 — 보고서]. 전역 조명은 건드리지 않는다.</summary>
    private static int BuildLamps(Transform g, List<Transform> columns)
    {
        // 같은 빌드에서 두 번 불려도 겹치지 않게 우리 그룹만 지운다(지형 아님).
        Transform old = g.Find(LightsGroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject group = new GameObject(LightsGroupName);
        group.transform.SetParent(g, false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;

        if (columns == null) { Debug.LogError("[S3_Dress] refs.columns가 null이다 — 등을 만들지 않는다(빈 그룹만 남는다)."); return 0; }

        Dictionary<string, Transform> byId = new Dictionary<string, Transform>();
        for (int i = 0; i < columns.Count; i++)
        {
            Transform c = columns[i];
            if (c == null) { Debug.LogError($"[S3_Dress] refs.columns[{i}]가 null이다 — 등 후보에서 뺀다."); continue; }
            string id = StripPrefix(StripPrefix(c.name, ColPrefix), "COL_");
            if (!byId.ContainsKey(id)) byId.Add(id, c);
        }

        Material lampMat = GetOrCreate(Lamp);
        List<Renderer> warnings = FindWarningRenderers(g);
        int n = 0, capped = 0;
        foreach (string id in LampColumnIds)
        {
            if (!byId.TryGetValue(id, out Transform col))
            {
                Debug.LogError($"[S3_Dress] 등 자리 기둥 '{ColPrefix}{id}'(또는 {ColPrefix}COL_{id})를 refs.columns에서 찾지 못했다 — 이 등은 건너뛴다.");
                continue;
            }
            if (!TryColumnTopLocal(g, col, out Vector3 top))
            {
                Debug.LogError($"[S3_Dress] 기둥 '{col.name}'의 윗면을 구하지 못했다(BoxCollider·렌더러 없음) — 이 등은 건너뛴다.", col);
                continue;
            }
            if (top.z >= FrontBackBoundaryZ)
                Debug.LogWarning($"[S3_Dress] 기둥 '{col.name}'이 뒤 구간(z {top.z:0.##})에 있다 — 앞 구간 등이 뒤로 새어 든다.", col);
            Vector3 lampLocal = top + new Vector3(0f, LampRadius, 0f);
            float range = LampRangeFor(id, g.TransformPoint(lampLocal), warnings, out string nearest);
            if (range < MinLampRange)
            {
                Debug.LogError($"[S3_Dress] 등 '{id}' 가까이 팀 경고 표시 '{nearest}'가 있어 범위가 {range:0.##} < {MinLampRange} — 경고 가독성을 위해 이 등은 만들지 않는다.", col);
                continue;
            }
            if (range < LampRange) capped++;
            BuildOneLamp(group.transform, id, lampLocal, lampMat, range);
            n++;
        }
        if (n != ExpectedLamps) Debug.LogError($"[S3_Dress] 주황 등 {n}/{ExpectedLamps}개만 만들었다 — 위 로그 확인.");
        Debug.Log($"[S3_Dress] 경고 표시 보호: 팀 경고 렌더러 {warnings.Count}개 확인, 범위를 줄인 등 {capped}개(표 상한 {LampRangeCap.Count} + 실배치 bounds 대조, 여유 {WarningClearance}).");
        return n;
    }

    /// <summary>등 범위 = min(LampRange, 표 상한 [계산], 실제 경고 렌더러 bounds까지 거리 − 여유). 팀 렌더러는 bounds만 읽는다.</summary>
    private static float LampRangeFor(string id, Vector3 lampWorld, List<Renderer> warnings, out string nearest)
    {
        float range = LampRange;
        nearest = "-";
        if (LampRangeCap.TryGetValue(id, out float cap) && cap < range) { range = cap; nearest = "(초안 표)"; }
        foreach (Renderer r in warnings)
        {
            float lim = Mathf.Sqrt(r.bounds.SqrDistance(lampWorld)) - WarningClearance;
            if (lim < range) { range = lim; nearest = r.transform.parent != null ? r.transform.parent.name : r.name; }
        }
        return range;
    }

    /// <summary>generated 아래 팀 발사구의 경고 렌더러(읽기만): ProjectileLauncher.warning.target(공개 필드 [팀] ProjectileLauncher.cs:56·ProjectileWarning.cs:11),
    /// 비어 있으면 자식 `Warning_Visual`의 Renderer. 발사구가 아직 없으면 빈 목록 — 그때는 표 상한만 쓴다.</summary>
    private static List<Renderer> FindWarningRenderers(Transform g)
    {
        List<Renderer> list = new List<Renderer>();
        foreach (ProjectileLauncher l in g.GetComponentsInChildren<ProjectileLauncher>(true))
        {
            Renderer r = (l.warning != null) ? l.warning.target : null;
            if (r == null)
            {
                Transform w = l.transform.Find(WarningVisualName);
                if (w != null) r = w.GetComponent<Renderer>();
            }
            if (r != null && !list.Contains(r)) list.Add(r);
        }
        if (list.Count == 0) Debug.LogWarning("[S3_Dress] 팀 경고 렌더러를 찾지 못했다(발사구 미배치?) — 등 범위는 초안 좌표 기준 표 상한만 적용한다.");
        return list;
    }

    private static void BuildOneLamp(Transform group, string id, Vector3 localPos, Material mat, float range)
    {
        GameObject lampGo = new GameObject($"VIS_S3_Lamp_{id}");
        lampGo.transform.SetParent(group, false);
        lampGo.transform.localPosition = localPos;
        lampGo.transform.localRotation = Quaternion.identity;

        Light light = lampGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = LampColor;
        light.range = range;
        light.intensity = LampIntensity;
        // 픽셀 조명 수 한계 대응(ProjectSettings 불변, 오브젝트 설정만): Auto면 오브젝트마다 영향이 큰 등부터 픽셀 조명이 되고
        // 나머지는 버텍스/SH로 내려간다. Important로 고정하면 한도를 넘는 오브젝트에서 오히려 비용만 는다 [S1-L 선례, 추정].
        light.renderMode = LightRenderMode.Auto;
        light.shadows = LightShadows.None; // 점광원 13개 그림자는 비용이 커서 끈다(연출 전용) [추정].

        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mesh.name = $"VIS_S3_Lamp_{id}_Mesh";
        Object.DestroyImmediate(mesh.GetComponent<Collider>()); // 콜라이더 0 — 시각 전용 [계약 C1-6].
        mesh.transform.SetParent(lampGo.transform, false);
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localRotation = Quaternion.identity;
        mesh.transform.localScale = Vector3.one * (LampRadius * 2f);
        MeshRenderer mr = mesh.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            if (mat != null) mr.sharedMaterial = mat;
            // 우리 등 구(팀 렌더러 아님): 광원 자체가 방향광(INF1-2 Soft) 그림자를 드리우지 않게 한다 [추정 — 연출].
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>기둥 윗면 중앙(섹터 로컬). BoxCollider를 우선 쓰고(읽기만), 없으면 VIS_ 자식 렌더러 bounds로 대신한다.</summary>
    private static bool TryColumnTopLocal(Transform g, Transform col, out Vector3 top)
    {
        BoxCollider box = col.GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 worldTop = col.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, 0f));
            top = g.InverseTransformPoint(worldTop);
            return true;
        }
        foreach (Renderer r in VisualRenderers(col))
        {
            Bounds b = r.bounds;
            top = g.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            return true;
        }
        top = Vector3.zero;
        return false;
    }

    // ───────────────────────── 공용 ─────────────────────────

    /// <summary>GEO의 시각물 자식(C2-0: `VIS_…`, 이름 바꾸기 전이면 `Visual`)의 sharedMaterial만 바꾼다. 직속 시각물이 없으면
    /// (계단 그룹 등) 직속 `GEO_` 자식 한 단계 아래에서 찾는다. GEO 자체의 Transform·Collider는 건드리지 않는다.</summary>
    private static int Paint(Transform geo, Material m)
    {
        if (m == null) return 0;
        List<Renderer> rs = VisualRenderers(geo);
        if (rs.Count == 0)
        {
            foreach (Transform child in geo)
                if (child.name.StartsWith("GEO_")) rs.AddRange(VisualRenderers(child));
        }
        if (rs.Count == 0)
        {
            Debug.LogWarning($"[S3_Dress] '{geo.name}' 아래 시각물(VIS_/Visual)을 찾지 못했다 — 재질을 입히지 않는다.", geo);
            return 0;
        }
        foreach (Renderer r in rs)
        {
            r.sharedMaterial = m;
            EditorUtility.SetDirty(r);
        }
        return rs.Count;
    }

    private static List<Renderer> VisualRenderers(Transform geo)
    {
        List<Renderer> list = new List<Renderer>();
        foreach (Transform child in geo)
        {
            if (!(child.name.StartsWith("VIS_") || child.name == "Visual")) continue;
            Renderer r = child.GetComponent<MeshRenderer>();
            if (r != null) list.Add(r);
        }
        return list;
    }

    /// <summary>섹터 로컬 중심. BoxCollider 중심(읽기만) → 시각물 bounds 중심 → Transform 위치 순.</summary>
    private static Vector3 LocalCenter(Transform g, Transform t)
    {
        BoxCollider box = t.GetComponent<BoxCollider>();
        if (box != null) return g.InverseTransformPoint(t.TransformPoint(box.center));
        List<Renderer> rs = VisualRenderers(t);
        if (rs.Count > 0) return g.InverseTransformPoint(rs[0].bounds.center);
        return g.InverseTransformPoint(t.position);
    }

    private static string StripPrefix(string s, string prefix) => s.StartsWith(prefix) ? s.Substring(prefix.Length) : s;

    /// <summary>Standard 머티리얼을 Materials/Generated/M4_S3_&lt;용도&gt;.mat로 만든다 — 있으면 재사용하고 값을 다시 맞춘다(S1_Lighting·Map4Build.GetMaterial 방식).
    /// Map4Palette·Map4_*.mat·팀 .mat은 건드리지 않는다.</summary>
    private static Material GetOrCreate(MatSpec spec)
    {
        EnsureDir(MaterialDir);
        string path = $"{MaterialDir}/{spec.name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null) { Debug.LogError($"[S3_Dress] Standard 셰이더를 찾지 못했다 — '{spec.name}'을 만들지 않는다."); return null; }
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
