#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// KitchenMapV3 블록아웃(T0) 공통 헬퍼.
/// 좌표 관례: 문서(X=좌우, Y=앞뒤, Z=높이) → Unity(x, y, z) = doc(X, Z, Y).
/// 1 Unit = 10cm. 모든 수치의 출처는 릴레이 1회차 확정본(RELAY_병합본_1of4~4of4) —
/// [검증] 표기가 없는 배치값은 V3_Layout에 [추정] 주석이 붙어 있으며 DATA JSON 도착 시 교체한다.
/// </summary>
public static class V3
{
    public const string RootName = "KitchenMapV3_Blockout";

    // ── 좌표 변환 ────────────────────────────────────────────────
    public static Vector3 Doc(float x, float y, float z) => new Vector3(x, z, y);

    /// <summary>[R3, r3 재보완 2026-09-14 — 사본 전용] 동명 루트 후보를 비활성 포함 전수 열거(씬 루트 레벨의 RootName).</summary>
    public static List<GameObject> RootCandidates()
    {
        var list = new List<GameObject>();
        foreach (GameObject go in AllSceneObjects(true))
            if (go.name == RootName && go.transform.parent == null) list.Add(go);
        return list;
    }

    /// <summary>[R3] 동명 루트 상태 판정: 0개면 OK(생성 가능), 1개·표식 있음이면 OK, 그 외(표식 없음 또는 복수)는 사유와 함께 거부.</summary>
    public static bool RootStateOk(string op, out GameObject single)
    {
        List<GameObject> c = RootCandidates();
        single = null;
        if (c.Count == 0) return true;
        if (c.Count > 1)
        {
            int owned = 0; foreach (GameObject g in c) if (IsOwned(g)) owned++;
            Debug.LogError($"[KitchenMapV3] {op}: '{RootName}' 동명 루트가 {c.Count}개(표식 {owned}개, 비활성 포함) — 어느 것도 변경하지 않는다. 변경 없음.");
            return false;
        }
        if (!IsOwned(c[0]))
        {
            Debug.LogError($"[KitchenMapV3] {op}: '{RootName}' 동명 루트(활성={c[0].activeSelf})에 V3_Owned 표식이 없다 — 이 도구가 만든 것이 아니므로 인수·변경하지 않는다. 변경 없음.");
            return false;
        }
        single = c[0];
        return true;
    }

    public static GameObject Root()
    {
        // [H05·R3 — 사본 전용] 표식 없는/복수 동명 루트는 인수(표식 부착)하지 않는다(비활성 포함).
        if (!RootStateOk("Root", out GameObject root)) return null;
        if (root == null) { root = new GameObject(RootName); MarkOwned(root, "V3.Root"); }
        return root;
    }

    public static GameObject Group(string name)
    {
        GameObject root = Root();
        if (root == null) return null;   // [H05] 표식 없는 동명 루트 — 호출자는 가드에서 이미 거부됨(방어)
        Transform t = root.transform.Find(name);
        if (t != null) return t.gameObject;
        GameObject g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        return g;
    }

    // ── [K01, 2026-09-12 — Codex 검수 지시] 전용 씬 경계 ──────────────────────────────
    // 근거: 기획작업/부엌맵_플레이검수_2026-09-12/Claude_지시사항.md 5항 + 09-10 K01. 이전엔
    // V3_Play가 전역 이름(GameObject.Find("Player_*"))·Camera.main을 재사용·이동하고 Follow 컴포넌트를
    // 지웠으며, V3_Checkpoints가 씬의 아무 RespawnController나 잡아 dropExtraHeight를 바꿨다 —
    // 팀 씬(SampleScene 등)에서 실수로 메뉴를 누르면 팀 오브젝트가 변한다. 모든 "변경" 진입점은
    // 첫 줄에서 EnsureOwnedScene()을 통과해야 하고, 통과 못 하면 아무것도 바꾸지 않고 return한다.
    //   조건 1: 열린 씬이 정확히 1개(additive로 다른 씬이 함께 열려 있으면 전역 Find가 그 씬을
    //           잡을 수 있어 거부).
    //   조건 2: 활성 씬이 미저장 새 씬(path 없음)이거나 Assets/KitchenMapV3/ 하위에 저장된 씬.
    //           이름에 "V3"가 들어가는 것만으로는 허용하지 않는다(경로 = 소유 표식).
    //   조건 3(checkForeign): V3_Owned 표식이 없는 동명 Player_*·RespawnController·
    //           PlayerControlSwitcher가 있으면 거부 — 이 도구가 만든 것이 아니라 재사용·이동·변경도,
    //           복제(공용 싱글턴 중복)도 하지 않는다. 우리 루트(KitchenMapV3_Blockout) 안만 만지는
    //           토글류는 checkForeign=false로 조건 1·2만 본다.
    public const string OwnedSceneFolder = "Assets/KitchenMapV3/";
    static readonly string[] ReservedNames = { "Player_Sphere", "Player_Cube", "Player_Tetrahedron" };

    /// <summary>변경 진입점 공통 가드. false면 호출자는 아무것도 바꾸지 않고 return해야 한다.</summary>
    public static bool EnsureOwnedScene(string op, bool checkForeign = true)
    {
        int open = SceneManager.sceneCount;
        if (open != 1)
        {
            Debug.LogError($"[KitchenMapV3] {op}: 열린 씬이 {open}개(additive) — V3 전용 씬 하나만 열고 실행해라. 변경 없음.");
            return false;
        }
        Scene scene = SceneManager.GetActiveScene();
        string path = (scene.path ?? "").Replace('\\', '/');
        bool untitled = string.IsNullOrEmpty(path);
        bool ownedPath = path.StartsWith(OwnedSceneFolder, StringComparison.OrdinalIgnoreCase);
        if (!untitled && !ownedPath)
        {
            Debug.LogError($"[KitchenMapV3] {op}: 활성 씬 '{path}'은 V3 전용 씬이 아니다(허용: 미저장 새 씬, 또는 '{OwnedSceneFolder}' 하위에 저장한 씬). 팀 씬을 지키기 위해 아무것도 바꾸지 않는다.");
            return false;
        }
        // [H05 최소 수정, r3 후속 2026-09-14 — 사본 전용] 모든 변경 진입점이 건드리는 루트(RootName)와 기본 카메라는
        // checkForeign 여부와 무관하게 표식을 확인한다 — 표식 없는 동명 루트/카메라가 있으면 어떤 부분 변경도 없이 중단.
        if (!RootStateOk(op, out _)) return false;   // [R3] 비활성·복수 동명 루트까지 검사(변경 전)
        if (!checkForeign) return true;

        List<string> foreign = new List<string>();
        foreach (GameObject go in AllSceneObjects(true))
            if (Array.IndexOf(ReservedNames, go.name) >= 0 && !IsOwned(go)) foreign.Add(go.name);
        foreach (RespawnController c in UnityEngine.Object.FindObjectsOfType<RespawnController>(true))
            if (!IsOwned(c.gameObject)) foreign.Add("RespawnController(" + c.name + ")");
        foreach (PlayerControlSwitcher s in UnityEngine.Object.FindObjectsOfType<PlayerControlSwitcher>(true))
            if (!IsOwned(s.gameObject)) foreign.Add("PlayerControlSwitcher(" + s.name + ")");
        if (foreign.Count > 0)
        {
            Debug.LogError($"[KitchenMapV3] {op}: V3_Owned 표식이 없는 동명·동종 오브젝트 {foreign.Count}개 — {string.Join(", ", foreign)}. " +
                           "이 도구가 만든 것이 아니거나 표식 도입(2026-09-12) 이전 산출물이라 재사용·이동·변경하지 않는다(복제도 하지 않음). " +
                           "직접 정리(삭제)한 뒤 재실행해라. 변경 없음.");
            return false;
        }
        return true;
    }

    public static bool IsOwned(GameObject go) => go != null && go.GetComponent<V3_Owned>() != null;

    public static void MarkOwned(GameObject go, string by)
    {
        if (go == null) return;
        V3_Owned o = go.GetComponent<V3_Owned>();
        if (o == null) o = go.AddComponent<V3_Owned>();
        if (string.IsNullOrEmpty(o.createdBy)) o.createdBy = by + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>활성 씬의 모든 GameObject(비활성 포함 옵션). GameObject.Find는 비활성 오브젝트를 못 찾아
    /// 동명 중복 생성의 구멍이 되므로 이름 탐색은 이 목록으로 한다.</summary>
    public static IEnumerable<GameObject> AllSceneObjects(bool includeInactive)
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive))
                yield return t.gameObject;
    }

    public static GameObject FindInActiveScene(string name, bool includeInactive)
    {
        foreach (GameObject go in AllSceneObjects(includeInactive))
            if (go.name == name) return go;
        return null;
    }

    // ── [K10·K13, 2026-09-12] URP 프로젝트에서 팀 생성기가 붙인 내장 셰이더 재질 교체 ─────────
    // 팀 생성기(PlayerSystem의 세모 CreateDefaultMaterial, DoorSystem의 ExitWeightPlate 판 등)는
    // Shader.Find("Standard")(→Diffuse) 재질을 새로 만들어 붙인다. 이 프로젝트(URP 14)에서는 그 셰이더가
    // 오류 셰이더(마젠타)로 그려진다 — K10(세모 마젠타)과 K12 수정 후 판 위에 드러난 판 A·B2 무게판
    // 마젠타(K13)가 같은 원인이다. 팀 코드는 손대지 않고, root 아래 렌더러 중 그런 재질만 URP Lit
    // 씬 내 재질(.mat 없음)로 참조 교체한다. 원 재질 오브젝트는 파괴하지 않는다. 기존 색(_Color)이
    // 흰색이 아니면 유지하고, 흰색이면 블록아웃 회색(0.62)으로 둔다. V3_PlateSensor의 자기 판 조명(MPB
    // _EmissionColor)이 동작하도록 _EMISSION 키워드를 켜 둔다(값은 검정=꺼짐, V3_Materials accent와 동일).
    // Built-in RP(currentRenderPipeline==null, develop 62f4e29)면 아무것도 하지 않는다.
    public static int ReplaceBuiltinMaterialsForPipeline(GameObject root, string tag)
    {
        if (root == null || GraphicsSettings.currentRenderPipeline == null) return 0;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Warn($"[{tag}] URP 파이프라인인데 'Universal Render Pipeline/Lit' 셰이더가 없어 내장 셰이더 재질을 교체하지 못했다(마젠타로 보일 수 있음).");
            return 0;
        }
        int replaced = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.shader == null) continue;
                string sn = m.shader.name;
                bool builtinOnly = sn == "Standard" || sn == "Diffuse" || sn == "Hidden/InternalErrorShader" || sn.StartsWith("Legacy Shaders/");
                if (!builtinOnly) continue;
                Color c = (m.HasProperty("_Color") && m.GetColor("_Color") != Color.white) ? m.GetColor("_Color") : new Color(0.62f, 0.62f, 0.62f);
                Material fixedMat = new Material(lit) { name = "KV3_URPLit_" + m.name };
                fixedMat.SetColor("_BaseColor", c);
                fixedMat.EnableKeyword("_EMISSION");
                fixedMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                fixedMat.SetColor("_EmissionColor", Color.black);
                mats[i] = fixedMat;
                changed = true;
            }
            if (changed) { r.sharedMaterials = mats; replaced++; }
        }
        if (replaced > 0)
            Log($"[{tag}] 내장 셰이더(Standard/Diffuse) 재질을 쓰던 렌더러 {replaced}개를 URP Lit 씬 내 재질로 교체 — URP에서 오류 셰이더(마젠타)로 그려지던 팀 생성기 재질. 팀 코드 무수정, 참조만 교체(.mat 없음).");
        return replaced;
    }

    // ── 머티리얼 (블록아웃: 회색 3톤 + 주황 = 상호작용, 결 = 줄무늬 대신 톤) ──
    private static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    public static Material Mat(string key)
    {
        if (matCache.TryGetValue(key, out Material cached) && cached != null) return cached;
        Color c;
        switch (key)
        {
            case "Gray":    c = new Color(0.62f, 0.62f, 0.62f); break;      // 정적 구조물
            case "GrayLo":  c = new Color(0.45f, 0.45f, 0.47f); break;      // 바닥·벽
            case "GrayHi":  c = new Color(0.78f, 0.78f, 0.78f); break;      // 상판·발판(머무는 면)
            case "Grain":   c = new Color(0.70f, 0.64f, 0.52f); break;      // 채널① 결(오른다)
            case "Orange":  c = new Color(0.95f, 0.58f, 0.23f); break;      // 채널② 주황(조작한다) #F2933A 근사
            case "Smooth":  c = new Color(0.55f, 0.60f, 0.66f); break;      // 채널③ 매끈(배경/차단)
            case "Rigid":   c = new Color(0.80f, 0.72f, 0.35f); break;      // 가동 소품
            case "Marker":  c = new Color(0.85f, 0.30f, 0.55f); break;      // TBD/측정 마커
            default:        c = Color.magenta; break;
        }
        Material m = null;
        Shader urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp != null) { m = new Material(urp); m.SetColor("_BaseColor", c); }
        else { m = new Material(Shader.Find("Standard")); m.color = c; }
        matCache[key] = m;
        return m;
    }

    // ── 박스 생성 (문서 좌표 min/max) ────────────────────────────
    public static GameObject Box(GameObject parent, string name,
        float x0, float y0, float z0, float x1, float y1, float z1, string mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = Doc((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0), Mathf.Abs(y1 - y0));
        go.GetComponent<Renderer>().sharedMaterial = Mat(mat);
        return go;
    }

    /// <summary>가동 소품 박스. 질량 태그는 릴레이 질량표 값(mass:x.x)을 그대로 쓴다.</summary>
    public static GameObject RigidBox(GameObject parent, string name,
        float x0, float y0, float z0, float x1, float y1, float z1, float mass)
    {
        GameObject go = Box(parent, name, x0, y0, z0, x1, y1, z1, "Rigid");
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        // 소품 안정화(이어하기_1 §7): 건드리기 전 kinematic + depenetration 제한은 PropSettle 몫 —
        // 블록아웃에서는 maxDepenetrationVelocity만 걸어 튕김을 막는다.
        rb.maxDepenetrationVelocity = 1f;
        return go;
    }

    /// <summary>표식(반투명 아님 — 블록아웃은 색으로만 구분). 콜라이더 없는 안내 박스.</summary>
    public static GameObject Marker(GameObject parent, string name,
        float x0, float y0, float z0, float x1, float y1, float z1)
    {
        GameObject go = Box(parent, name, x0, y0, z0, x1, y1, z1, "Marker");
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ── 계단 콜라이더 비탈 (🔒N4: 턱 ≤0.8, 깊이 ≥0.3, 보이는 결 1개 = 콜라이더 1개) ──
    /// <summary>
    /// docFrom(x,y)에서 +방향으로 오르는 계단열 생성. axis: 'x' 또는 'y' 진행.
    /// stepH ≤ 0.8 검사 — 위반이면 에러 로그 후 생성 중단(검증 수치 원칙).
    /// </summary>
    public static void Stairs(GameObject parent, string name, char axis,
        float startX, float startY, float baseZ, float topZ,
        int steps, float stepDepth, float width, string mat = "Grain")
    {
        float stepH = (topZ - baseZ) / steps;
        if (stepH > 0.8f + 1e-4f)
        {
            Debug.LogError($"[V3] {name}: 턱 {stepH:F3} > 상한 0.8 (🔒N4). 생성 중단 — 계단 수를 늘려라.");
            return;
        }
        for (int i = 0; i < steps; i++)
        {
            float z1 = baseZ + stepH * (i + 1);
            float a0 = (axis == 'x') ? startX + stepDepth * i : startX;
            float a1 = (axis == 'x') ? startX + stepDepth * (i + 1) : startX + width;
            float b0 = (axis == 'y') ? startY + stepDepth * i : startY;
            float b1 = (axis == 'y') ? startY + stepDepth * (i + 1) : startY + width;
            Box(parent, $"{name}_Step{i + 1}", a0, b0, baseZ, a1, b1, z1, mat);
        }
    }

    public static void Log(string msg) => Debug.Log("[KitchenMapV3] " + msg);
    public static void Warn(string msg) => Debug.LogWarning("[KitchenMapV3] " + msg);
}
#endif
