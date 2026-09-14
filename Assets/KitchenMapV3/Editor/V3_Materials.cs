#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// KitchenMapV3 — 부엌 질감 적용 (v3.2 머티리얼 팔레트 기반).
/// 텍스처: Assets/KitchenMapV3/Textures/kv3_*.png (절차 생성 · 타일링).
/// 팔레트 출처(이어하기_1 §6): Counter #D8D2C4 · Metal #B9C0C7 · Wood #8A5A3B · Ceramic #EFEAE2
///                          · Plastic #D9E3EA · Fabric #C9BBA8 · Accent #F2933A(주황=상호작용).
/// 어포던스 3채널(🔒N6)은 질감으로도 유지된다 — 결(나무·패브릭)=오른다 / 주황=조작한다 / 매끈(메탈·플라스틱)=배경.
/// 되돌리기: Tools>KitchenMapV3>2. Build All 재실행(회색 블록아웃으로 재생성).
/// </summary>
public static class V3Materials
{
    const string TexDir = "Assets/KitchenMapV3/Textures";
    const string MatDir = "Assets/KitchenMapV3/Materials";

    // [R2 복구, 2026-09-10 — map-reviewer 37차 R2 반려 반영] accent(KV3_accent, rule1 —
    // Start_Mat/PlateA/PlateB/PlateC 등이 받는 재질)는 V3_PlateSensor.SetLit의 자기 판 조명(Q2)이
    // 발광으로 밝기를 낸다. MaterialPropertyBlock은 셰이더 프로퍼티 "값"만 오버라이드하고 키워드는
    // 못 켠다(재질 인스턴스의 EnableKeyword와 다른 점) — 그래서 키워드는 여기(재질 쪽)에서 켜 두고,
    // 값(밝기)은 V3_PlateSensor가 MPB로 매 프레임 오버라이드한다.
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // key → (albedo, normal, metallic, smoothness, 타일 크기 U — 이 간격마다 텍스처 1회 반복)
    static readonly (string key, string tex, string normal, float metal, float smooth, float tile)[] Defs =
    {
        ("wood",    "kv3_wood",    "kv3_wood_n",    0.00f, 0.38f, 8f),
        ("counter", "kv3_counter", "kv3_counter_n", 0.05f, 0.60f, 10f),
        ("metal",   "kv3_metal",   "kv3_metal_n",   0.90f, 0.78f, 6f),
        ("paint",   "kv3_paint",   "kv3_paint_n",   0.00f, 0.22f, 16f),
        ("tile",    "kv3_tile",    "kv3_tile_n",    0.00f, 0.55f, 16f),
        ("fabric",  "kv3_fabric",  "kv3_fabric_n",  0.00f, 0.05f, 6f),
        ("ceramic", "kv3_ceramic", null,            0.00f, 0.85f, 8f),
        ("plastic", "kv3_plastic", null,            0.00f, 0.50f, 8f),
        ("accent",  "kv3_accent",  null,            0.00f, 0.45f, 6f),
        ("dirt",    "kv3_dirt",    "kv3_dirt_n",    0.00f, 0.08f, 12f),
    };

    // 이름 규칙 → 머티리얼 키 (위에서부터 첫 일치 적용 — 실좌표판 오브젝트명 기준 · 2026-09-01)
    static readonly (string[] keywords, string key)[] Rules =
    {
        // [2026-09-06 컨트롤타워 채택] rule0에 6개 키워드 추가 — 이름에 "마커"/"Marker"가 없는
        // V3.Marker()/NoColl() 산출 측정용 placeholder 6종(SwingRing_1~6 포함 12개 오브젝트)이
        // 실제 텍스처를 입던 문제 수정. 구조적 판별(콜라이더 없음→SKIP)은 Clothesline_빨랫줄
        // (NoColl, fabric 정상 매칭 의도)까지 함께 걸려 폐기 — 이름 키워드 추가로 대체.
        (new[]{"Marker", "마커", "DRESS_", "U3b_배수구마개", "CP60", "CP62", "Duct_그릴_래치",
               "V4폭_신규확인", "Catapult", "SwingRing"},                     "SKIP"),
        // "SwingRing"은 rule0(SKIP)이 이미 선점 — 여기 다시 넣으면 죽은 코드(도달 불가)라 제거.
        (new[]{"Start_Mat", "PlateA", "PlateB", "PlateC", "Burner", "LeverPad", "Brake", "그릴"}, "accent"),
        // [2026-09-06 신규 오매칭 방지] "Pot_B"(rule2, wood)를 "Pot_Big"(냄비, metal)보다 뒤에 두면
        // Contains 부분 문자열 특성상 "Pot_Big_큰냄비"가 먼저 "Pot_B"에 걸려 metal→wood로 잘못
        // 떨어진다(에이전트 지침 §5 예시 그대로 재현·시뮬레이션으로 발견). rule2보다 먼저 두어
        // "Pot_Big"부터 확정시키고, 정확히 "Pot_A"/"Pot_B"(그 접두만 있고 "ig"가 안 붙는 것)만
        // rule2로 떨어지게 한다.
        (new[]{"Pot_Big", "Pan_Fry"},                                        "metal"),
        // [2026-09-06] 19개 이동/신규 — "용도"(계단·발판·수납상자류)가 뒤쪽 사물-재질 규칙(구
        // rule3/4/7)에 먼저 걸려 결(Grain, 오른다)이 매끈(metal/ceramic/plastic, 배경·차단)으로
        // 떨어지던 🔒N6 위반 41건 해소(코워크 09-06#3 §3 판정: "실제 재질 대신 결로 칠하는 것이
        // 이 맵의 규칙"). G0_Water는 신규 스코프 키워드 — "Water" 전체를 옮기면 WaterTank_물탱크·
        // WaterCup 마커가 회귀하므로 팬트리 G0 전용 접두로 좁혔다. [자체 발견, 승인 목록 밖 —
        // 지시문 목록(18개+G0_Water)에는 없었지만 캐시 원 41건 목록(§4 #1)엔 있던 "G1_Jars"가
        // 재시뮬레이션에서 유일한 잔여 오매칭(ceramic)으로 재확인돼 같은 근거로 동봉 추가.]
        // map-reviewer/컨트롤타워 재확인 요청 — 회신 참조.
        (new[]{"중간턱", "턱_", "Duct_Step", "YardStep", "Cab_", "Box_Step", "Crate_Step", "세제통",
               "HoodBox", "PlateStack", "Pot_A", "Pot_B", "Box1", "CanBox", "Cereal", "TinStack",
               "PastaBox", "SnackBox", "Blender", "PotSet", "Cooker", "BigBox", "TopBox", "G0_Water",
               "Jars"},                                                      "wood"),
        (new[]{"Jangdok", "SmallPot", "YardPlant", "Plant2", "BigPlant", "Cup_"}, "ceramic"),
        (new[]{"Rug", "수세미", "Basket", "Clothesline", "Rice", "FlourBag", "Curtain"}, "fabric"),
        (new[]{"Trolley", "Hood_", "Duct", "SinkBowl", "Faucet", "Fridge", "Kimchi", "GOAL_Gate", "Stove_Frame",
               "CoffeeMachine", "Microwave", "Wagon", "Stool", "Rail", "DryRack", "Line_Pole", "WashingMachine",
               "AirCon", "Bucket", "Caster", "난간", "Oven"},                 "metal"),
        // [2026-09-06 부가 발견 (a)] Bench_평상·Storage_Shed·Shed_Roof — V3_Build.cs 색 태그
        // "Smooth"+주석 "채널③ 매끈"(🔒H6(a) 고정)인데 키워드 미매칭으로 wood 폴백(역방향 오류).
        // 같은 뒷마당 차폐물 묶음의 형제(YardBox_*·WaterTank→plastic)와 동일 채널로 맞춤.
        (new[]{"Molding", "Humidifier", "Recycle", "YardBox", "WaterTank",
               "유리문", "Ridge_Box", "Water", "Bench_평상", "Storage_Shed", "Shed_Roof"}, "plastic"),
        (new[]{"Floor_Yard"},                                                "dirt"),
        (new[]{"Floor_Indoor"},                                              "tile"),
        (new[]{"Wall_", "Fence", "Ceiling"},                                 "paint"),
        (new[]{"Counter", "Island_Top", "WindowSill"},                       "counter"),
        // 그 외(가구·수납·선반·계단류)는 기본 wood 로 떨어진다 — Apply()의 폴백 참조
    };

    [MenuItem("Tools/KitchenMapV3/6. Apply Kitchen Materials (질감)", false, 30)]
    public static void Apply()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다 — 먼저 2. Build All."); return; }

        FixImporters();
        Dictionary<string, Material> mats = BuildMaterials();
        if (mats == null) return;

        int applied = 0, skipped = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            string key = Match(r.name);
            if (key == "SKIP") { skipped++; continue; }
            Material src = mats[key ?? "wood"];   // 폴백: 가구·수납·계단류 = 나무
            // 오브젝트 크기에 맞춘 타일링 — 인스턴스 머티리얼(에디터 전용이라 배칭 무관)
            float tileSize = TileOf(key ?? "paint");
            Vector3 s = r.transform.lossyScale;
            float u = Mathf.Max(s.x, 0.5f) / tileSize;
            float v = Mathf.Max(Mathf.Max(s.y, s.z), 0.5f) / tileSize;
            Material inst = new Material(src) { name = src.name + "_inst" };
            SetTiling(inst, new Vector2(Mathf.Max(u, 0.25f), Mathf.Max(v, 0.25f)));
            r.sharedMaterial = inst;
            applied++;
        }
        V3.Log($"질감 적용 완료 — {applied}개 (마커 {skipped}개 제외). 되돌리기: 2. Build All 재실행.");
    }

    static float TileOf(string key)
    {
        foreach (var d in Defs) if (d.key == key) return d.tile;
        return 8f;
    }

    static string Match(string name)
    {
        foreach (var rule in Rules)
            foreach (string k in rule.keywords)
                if (name.Contains(k)) return rule.key;
        return null;
    }

    static void FixImporters()
    {
        foreach (var d in Defs)
        {
            Fix($"{TexDir}/{d.tex}.png", false);
            if (d.normal != null) Fix($"{TexDir}/{d.normal}.png", true);
        }
        AssetDatabase.Refresh();
    }

    static void Fix(string path, bool isNormal)
    {
        TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { V3.Warn("텍스처 없음: " + path); return; }
        bool dirty = false;
        if (isNormal && imp.textureType != TextureImporterType.NormalMap)
        { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (imp.wrapMode != TextureWrapMode.Repeat)
        { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty) imp.SaveAndReimport();
    }

    /// <summary>[정정 2026-09-10, 컨트롤타워 지시 R5] 현재 렌더 파이프라인에 맞는 셰이더를
    /// 고른다. 예전엔 Shader.Find("Universal Render Pipeline/Lit")의 성패(=URP 패키지가 이
    /// 프로젝트에 임포트돼 있는가)로 갈랐는데, origin/develop 62f4e29에는 URP 패키지 자체가
    /// 없다(36차 R5 실측 — manifest.json에 com.unity.render-pipelines.universal 0건,
    /// GraphicsSettings.asset m_CustomRenderPipeline: {fileID: 0}). 그 상태에서도 이 프로젝트
    /// 클론에 URP 패키지만 남아 있으면 Shader.Find가 성공해 URP 셰이더로 잘못 만들 수 있었다
    /// (실제로 커밋된 KV3_*.mat 10개가 전부 URP guid였던 것이 그 결과). Catapult
    /// System(Assets/CatapultSystem/Editor/CatapultMenuItem.ResolveShader())과 같은
    /// 패턴으로, GraphicsSettings.currentRenderPipeline이 실제로 설정돼 있는가만으로 먼저
    /// 파이프라인을 정한다 — 패키지가 남아 있어도 프로젝트가 그 파이프라인을 쓰지 않으면
    /// Standard로 만든다. isUrp를 함께 돌려줘 호출부가 텍스처 프로퍼티 이름을 고르게 한다.</summary>
    static Shader ResolveShader(out bool isUrp)
    {
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s != null) { isUrp = true; return s; }
            V3.Warn("현재 렌더 파이프라인이 URP인데 'Universal Render Pipeline/Lit' 셰이더를 찾지 못해 Standard로 대체한다.");
        }
        isUrp = false;
        Shader std = Shader.Find("Standard");
        if (std != null) return std;
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }

    static Dictionary<string, Material> BuildMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/KitchenMapV3", "Materials");

        bool isUrp;
        Shader shader = ResolveShader(out isUrp);
        if (shader == null) { V3.Warn("셰이더를 찾지 못했다."); return null; }

        var mats = new Dictionary<string, Material>();
        foreach (var d in Defs)
        {
            string matPath = $"{MatDir}/KV3_{d.key}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, matPath);
            }
            else if (m.shader != shader)
            {
                // [R5] .mat은 이미 있지만(예: 업로드로 딸려온 KV3_*.mat) 셰이더가 현재
                // 파이프라인과 다르면 재대입한다 — 여러 번 눌러도 이후로는 항상 같은 결과가
                // 되는 멱등 동작. 원본 파괴가 아니라 같은 에셋의 shader 필드만 바꾼다.
                m.shader = shader;
            }
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{d.tex}.png");
            Texture2D normal = d.normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{d.normal}.png") : null;
            if (isUrp)
            {
                m.SetTexture("_BaseMap", albedo);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", d.metal);
                m.SetFloat("_Smoothness", d.smooth);
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            }
            else
            {
                m.SetTexture("_MainTex", albedo);
                m.SetColor("_Color", Color.white);
                m.SetFloat("_Metallic", d.metal);
                m.SetFloat("_Glossiness", d.smooth);
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            }
            if (d.key == "accent")
            {
                // [R2 복구] 37차 R2 실측: 기존 KV3_accent.mat은 m_ValidKeywords:[] ·
                // m_LightmapFlags:4(EmissiveIsBlack)라 MPB로 _EmissionColor를 넣어도 발광 0이었다.
                // 여기서 키워드를 켜고 EmissiveIsBlack을 걷어낸다(RealtimeEmissive로 갱신 — 이
                // 프로젝트는 실시간 GI 베이크를 쓰지 않으므로 그 의미보다는 EmissiveIsBlack 해제가
                // 목적). 기본 _EmissionColor는 검정(꺼짐) — 실제 밝기는 V3_PlateSensor.SetLit이
                // MPB로 매 프레임 올린다. 프로퍼티명 _EmissionColor는 Standard·URP Lit 공용(확인).
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty(EmissionColorId)) m.SetColor(EmissionColorId, Color.black);
            }
            EditorUtility.SetDirty(m);
            mats[d.key] = m;
        }
        AssetDatabase.SaveAssets();
        return mats;
    }

    static void SetTiling(Material m, Vector2 t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", t);
        if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", t);
        if (m.HasProperty("_BumpMap")) m.SetTextureScale("_BumpMap", t);
    }
}
#endif
