#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

    public static GameObject Root()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);
        return root;
    }

    public static GameObject Group(string name)
    {
        GameObject root = Root();
        Transform t = root.transform.Find(name);
        if (t != null) return t.gameObject;
        GameObject g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        return g;
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
        Object.DestroyImmediate(go.GetComponent<Collider>());
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
