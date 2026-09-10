#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 — 자연화 v2 (계단 자연화 #22의 T0 선행분).
/// 원칙: 콜라이더·높이(z) 검증값은 절대 건드리지 않는다 — 렌더러만 바꾼다.
///  · 수납상자 계단 → "서 있는 서랍장 + 열린 서랍들": 계단마다 몸통 1개(꼭대기) + 단마다 서랍
///    1개(몸통 앞면~그 단 앞면, 아래 단일수록 길게 빠져나옴). 🔒N4 "보이는 결 1개 = 콜라이더 1개"
///    — 서랍·몸통 비주얼 합집합 = 콜라이더 유니온 그대로(2026-09-03 사용자 결정 재해석).
///    [2026-09-06] 열린 서랍마다 양쪽 측판·내부 트레이 칸막이 2개 추가(실물 커트러리 서랍처럼
///    "빠져나온 서랍"으로 읽히게) — 전부 그 단의 실 노출 트레드 안쪽, 상면 위 ≤0.02U만
///    (컨트롤타워 자체 상한, 09-06 — 릴레이·[결정] 근거 없음), 콜라이더 없음. [판정] 바닥
///    그림자 띠는 위에서 볼 때 단당 가로 띠 2개(전면판 상단선+그림자 띠)를 만들어 🔒N6
///    "보이는 결 1개=콜라이더 1개"와 긴장돼 제거 — 측판·트레이(진행축 평행 세로 요소)만 유지.
///  · 후드 전면 버튼 패널 / 스윙 고리 → 천장 전등(전선+갓) / 덕트 중간턱 받침 / 뒷마당 디테일.
///  · 측정 마커는 기본 숨김 — 메뉴 "9. 마커 표시 토글"로 켠다.
/// 모든 추가 오브젝트는 이름이 DRESS_ 로 시작하고 콜라이더가 없다. 재실행 시 지우고 다시 만든다.
/// </summary>
public static class V3Dress
{
    static readonly Color WoodDrawer  = new Color(0.62f, 0.45f, 0.30f);
    static readonly Color WoodFront   = new Color(0.55f, 0.38f, 0.24f);
    static readonly Color WoodCarcass = new Color(0.42f, 0.28f, 0.16f);   // 서랍장 몸통(채움) — 서랍보다 짙게
    static readonly Color WoodCarcassEdge = new Color(0.28f, 0.18f, 0.10f);   // 몸통 테두리(측면판·뒤판) — 채움보다 더 짙게
    static readonly Color ShadowDark  = new Color(0.12f, 0.09f, 0.06f);   // [2026-09-06] 서랍 내부 그림자 띠 — 결(wood) 베이스 유지, 색만 어둡게
    static readonly Color HandleDark  = new Color(0.25f, 0.25f, 0.27f);
    static readonly Color FabricTint  = new Color(0.79f, 0.72f, 0.62f);
    static readonly Color CordDark    = new Color(0.18f, 0.18f, 0.20f);
    static readonly Color ShadeTint   = new Color(0.92f, 0.90f, 0.84f);
    static readonly Color JangdokTint = new Color(0.42f, 0.30f, 0.22f);
    static readonly Color GlassDark   = new Color(0.20f, 0.24f, 0.30f);
    static readonly Color SteelLight  = new Color(0.85f, 0.87f, 0.90f);

    [MenuItem("Tools/KitchenMapV3/7. Naturalize (자연화)", false, 31)]
    public static void Apply()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다 — 먼저 2. Build All."); return; }

        // 재실행 대비: 기존 DRESS_ 전부 제거
        var olds = new List<GameObject>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("DRESS_")) olds.Add(t.gameObject);
        foreach (GameObject o in olds) Object.DestroyImmediate(o);

        // R7 그룹 컨테이너(검문 §E-2 texture-artist 배정): [판정, 2026-09-06 재갱신] 서랍
        // 140개(계단당 몸통1+테두리4+서랍9+전면판10+손잡이10=34, 열린 서랍 9단×[측판2+트레이2]=36
        // → 계단당 70, 2계단)를 한 부모로 묶어 — 바닥 그림자 띠(DRESS_dwShadow)는 🔒N6 판정으로
        // 제거(계단당 79→70).
        // 표시 토글이 O(1)(SetActive 1회)이 되게 한다. 루트 가이드는 기존에 이미
        // DRESS_RouteGuide 단일 부모라 O(1) — 별도 그룹 불필요.
        GameObject drawerGroupGO = new GameObject("DRESS_DrawerGroup");
        drawerGroupGO.transform.SetParent(root.transform, false);
        Transform drawerGroup = drawerGroupGO.transform;

        int lamps = 0;
        var boxSteps = new List<Transform>();
        var crateSteps = new List<Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>())
        {
            if (t.name.StartsWith("Box_Step_")) boxSteps.Add(t);
            else if (t.name.StartsWith("Crate_Step_")) crateSteps.Add(t);
            else if (t.name.StartsWith("SwingRing_")) { DressLamp(t); lamps++; }
            else if (t.name.StartsWith("Jangdok_") && !t.name.Contains("Fold")) DressJangdok(t);
        }
        // 사용자 결정(2026-09-03 #4): 서랍 계단 재해석 — 계단마다 서랍장 몸통 1개(꼭대기, 마지막 단
        // 콜라이더 안에 埋め込む) + 단마다 몸통 앞면~그 단 자신의 앞면(riser)까지 이어지는 서랍 1개.
        // 아래 단일수록(오름축 좌표가 작을수록) 앞면까지 거리가 멀어 더 길게 빠져나온 모습이 된다.
        int drawers = DressDrawerStaircase(boxSteps, drawerGroup, StairAxis.X)
                    + DressDrawerStaircase(crateSteps, drawerGroup, StairAxis.Y);
        // 사용자 결정(2026-09-03 — 1번): S1 식탁은 조형물로만 남기고 루트에서 뺀다. 앞치마 계단
        // (Apron_앞치마)은 V3_Build.cs에서 이미 삭제됐으므로 DressApron() 호출도 제거한다(허공
        // 데칼 방지) — 함수 자체도 아래에서 삭제.
        DressHoodButtons(root);
        DressDuctBracket(root);
        DressYard(root);
        DressGuide(root);
        // R7: Build All 직후 기본 ON(T0 기간). "10/11/12" 메뉴로 OFF 가능 — T1 이후 기본 OFF는 후속 작업.
        V3.Log($"자연화 완료 — 서랍 계단 {drawers}단 · 전등 {lamps}개 · 루트 발자국 점선 + 갈림길 화살표. (콜라이더 불변) " +
               "표시 끄기: Tools/KitchenMapV3/10·11·12.");
    }

    /// <summary>마커(분홍) 표시 토글 — Build All 후 기본 숨김. 측정할 때 켠다.</summary>
    [MenuItem("Tools/KitchenMapV3/9. 마커 표시 토글", false, 33)]
    public static void ToggleMarkers()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다."); return; }
        bool? turnOn = null; int n = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsMarker(r)) continue;
            if (turnOn == null) turnOn = !r.enabled;
            r.enabled = turnOn.Value; n++;
        }
        V3.Log($"마커 {n}개 {(turnOn == true ? "표시" : "숨김")}.");
    }

    public static void SetMarkers(bool on)
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) return;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            if (IsMarker(r)) r.enabled = on;
    }

    static bool IsMarker(Renderer r)
    {
        Material m = r.sharedMaterial;
        if (m == null) return false;
        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                : (m.HasProperty("_Color") ? m.color : Color.clear);
        // V3.Mat("Marker") = (0.85, 0.30, 0.55) 근사 판별
        return Mathf.Abs(c.r - 0.85f) < 0.08f && Mathf.Abs(c.g - 0.30f) < 0.08f && Mathf.Abs(c.b - 0.55f) < 0.08f;
    }

    // ── R7 (검문 §E-1 L4·§E-2): T0 측정 보조물(가이드·서랍) 표시 토글. 기본 ON, 메뉴로 OFF 가능.
    //    각각 그룹 오브젝트 SetActive 1회 = O(1). "전체"는 DRESS_ 최상위 노드만 순회(중첩 자손은
    //    상위 SetActive에 딸려 꺼지므로 건너뛴다) — 개별 오브젝트 600여 개를 매번 훑지 않는다.
    [MenuItem("Tools/KitchenMapV3/10. 가이드 표시 토글", false, 34)]
    public static void ToggleGuide()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다."); return; }
        Transform g = root.transform.Find("DRESS_RouteGuide");
        if (g == null) { V3.Warn("DRESS_RouteGuide 없음 — 먼저 7. Naturalize."); return; }
        bool next = !g.gameObject.activeSelf;
        g.gameObject.SetActive(next);
        V3.Log($"루트 가이드 {(next ? "표시" : "숨김")}.");
    }

    [MenuItem("Tools/KitchenMapV3/11. 서랍 표시 토글", false, 35)]
    public static void ToggleDrawers()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다."); return; }
        Transform g = root.transform.Find("DRESS_DrawerGroup");
        if (g == null) { V3.Warn("DRESS_DrawerGroup 없음 — 먼저 7. Naturalize."); return; }
        bool next = !g.gameObject.activeSelf;
        g.gameObject.SetActive(next);
        SetStepBaseRenderers(root, next);   // [O5] DRESS 서랍이 꺼지면 실지형(Box/Crate_Step) 렌더러를 되살린다
        V3.Log($"서랍 자연화 {(next ? "표시" : "숨김")}.");
    }

    [MenuItem("Tools/KitchenMapV3/12. DRESS_ 전체 토글", false, 36)]
    public static void ToggleAllDress()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다."); return; }
        bool? turnOn = null; int n = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("DRESS_")) continue;
            if (t.parent != null && t.parent.name.StartsWith("DRESS_")) continue; // 중첩 — 상위가 이미 처리
            if (turnOn == null) turnOn = !t.gameObject.activeSelf;
            t.gameObject.SetActive(turnOn.Value);
            n++;
        }
        if (turnOn != null) SetStepBaseRenderers(root, turnOn.Value);   // [O5] 전체 토글도 서랍과 같은 규칙
        V3.Log($"DRESS_ 전체(최상위 {n}개) {(turnOn == true ? "표시" : "숨김")}.");
    }

    /// <summary>[O5, map-reviewer 검문] DressDrawerStaircase가 Box_Step_/Crate_Step_의 실지형
    /// 렌더러를 꺼서 DRESS 서랍 비주얼로 덮는다(DressDrawerStaircase 본문 baseR.enabled=false 참고).
    /// DRESS 서랍이 꺼지면(dressOn=false) 그 실지형이 화면에서 통째로 사라지므로, SetMarkers()와
    /// 같은 패턴(Renderer 이름/그룹 훑어 enabled 토글)으로 여기서 되살린다 — DRESS 서랍을 다시
    /// 켜면(dressOn=true) 다시 숨긴다.</summary>
    static void SetStepBaseRenderers(GameObject root, bool dressOn)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Box_Step_") && !t.name.StartsWith("Crate_Step_")) continue;
            Renderer r = t.GetComponent<Renderer>();
            if (r != null) r.enabled = !dressOn;
        }
    }

    // ── 수납상자 계단 10단 → "서 있는 서랍장 + 열린 서랍들" 재해석 ──
    // 사용자 결정(2026-09-03 #4, 원문 발췌): "서랍장 하나가 서 있고 서랍들이 열려서 계단이 된
    // 모습 — 아래 서랍일수록 많이 빠져나온 형태." 콜라이더·높이값 불변, DRESS_ 렌더러만 교체.
    // 계단 꼭대기 쪽(마지막 단, 몸통이 이어지는 가구 쪽)에 DRESS_ 서랍장 몸통 1개, 단마다 몸통
    // 앞면에서 그 단 자신의 앞면(riser, 실좌표)까지 이어지는 DRESS_ 서랍 1개.
    //
    // 컨트롤타워 반려(2026-09-03, "몸통이 서 있는 가구로 안 읽힌다"): 몸통을 마지막 단(Step_10)
    // 실 콜라이더 기둥의 "전체 발자국"으로 키웠다(이전 0.4U 얇은 등판 → 등치). 서 있는 가구
    // 실루엣을 만들기 위해 몸통을 속이 빈 상자로 보이게 측면판 2장 + 뒤판 + 천판(전부 몸통 AABB
    // 안, 채움보다 짙은 WoodCarcassEdge/WoodFront)을 얹는다. 마지막 단은 몸통이 이미 전체를
    // 덮으므로 별도 서랍 몸체를 만들지 않고 "닫힌 서랍"으로 취급 — 전면판·손잡이만 몸통 앞면에
    // flush로 남긴다(돌출 규칙 동일).
    //
    // 기하 증명(자가검증 스크립트로 재확인): 단 i(오름축 오름차순, i=0..n-1)의 실 콜라이더는
    // [오름축: frontEdge_i~AscendMax_i] × [폭축: widthMin~widthMax] × [높이: 0~top_i] 전체가
    // "열(column)"이다(바닥부터 자기 상면까지 꽉 찬 기둥). i<n-1 서랍_i를 [frontEdge_i~bodyFront] ×
    // [widthMin~widthMax] × [prevTop~top_i]로 만들면(bodyFront = 마지막 단 자신의 앞면 = 몸통이
    // 시작되는 지점), i=0..k의 서랍을 모두 합쳤을 때(오름축이 작을수록 frontEdge가 작아 폭이 넓다
    // — "아래 서랍일수록 길게 빠져나옴") 정확히 단 k의 실 콜라이더 전체(0~top_k)를 덮는다:
    // prevTop~top_i 밴드들이 z0~top_k를 분할 없이 이어 채우고, frontEdge_i ≤ frontEdge_k(i≤k)라
    // 그 아래 서랍이 항상 위 단의 폭까지 감싼다. 마지막 단(i=n-1) 자체는 몸통 채움이 그 밴드
    // (top_{n-2}~top_{n-1})에서 [bodyFront,bodyBack] 전체와 정확히 일치해 이미 덮여 있으므로
    // 별도 서랍 몸체가 필요 없다(존재해도 폭 0인 퇴화 박스). [결정 2026-09-03] 사용자 결정 4번
    // (전면판·손잡이 포함 서랍 재해석)이 R6(2026-09-02, 손잡이 0.15U→0.03U)를 대체 — 전면판
    // (±0.04U, 두께 0.08U ≤ 상한 0.1U)·손잡이(riser 기준 총 돌출 ≤0.12U)만 실 콜라이더 밖으로
    // 살짝 나가며, 둘 다 노출 상한 0.15U 미달로 가짜 발판 아님. 몸통 테두리(측면판·뒤판·천판)는
    // 전부 몸통 채움 AABB 안(두께 0.12U 인셋)이라 콜라이더 포함성에 영향 없음.
    enum StairAxis { X, Y }

    static int DressDrawerStaircase(List<Transform> steps, Transform group, StairAxis axis)
    {
        if (steps.Count == 0) return 0;
        steps.Sort((a, b) => TopHeight(a).CompareTo(TopHeight(b)));   // 낮은 단 → 몸통 쪽(높은 단) 순
        int n = steps.Count;

        // 폭(width) 축 범위 — 계단 전체 스텝의 합집합(설계상 전 단 동일 폭, 안전하게 유니온 사용)
        float widthMin = float.MaxValue, widthMax = float.MinValue;
        foreach (Transform s in steps)
        {
            float wc = WidthCenter(s, axis), wh = WidthHalf(s, axis);
            widthMin = Mathf.Min(widthMin, wc - wh);
            widthMax = Mathf.Max(widthMax, wc + wh);
        }

        Transform last = steps[n - 1];
        float bodyBack = AscendMax(last, axis);           // 몸통 뒤판(벽 쪽) — 마지막 단 콜라이더 먼 끝
        float bodyFront = AscendMin(last, axis);          // 몸통 앞면 — 마지막 단 '자신'의 앞면(riser)
        float lastTop = TopHeight(last);                  // 마지막 단 상면 = 몸통 상면(천판)

        // 몸통 채움 — 마지막 단 실 콜라이더 기둥과 정확히 일치(부분집합이 아니라 등치)
        MakeAxisBox($"DRESS_dwBody_{last.name}", group, WoodCarcass, axis,
            bodyFront, bodyBack, widthMin, widthMax, 0f, lastTop);

        // 몸통을 "서 있는 속 빈 상자"로 읽히게: 측면판 2장 + 뒤판 + 천판 — 전부 몸통 채움 AABB
        // 안(두께 0.12U 인셋)이라 콜라이더 포함성엔 전혀 영향 없다(장식 오버레이).
        const float edgeT = 0.12f;
        MakeAxisBox($"DRESS_dwSideA_{last.name}", group, WoodCarcassEdge, axis,
            bodyFront, bodyBack, widthMin, widthMin + edgeT, 0f, lastTop);
        MakeAxisBox($"DRESS_dwSideB_{last.name}", group, WoodCarcassEdge, axis,
            bodyFront, bodyBack, widthMax - edgeT, widthMax, 0f, lastTop);
        MakeAxisBox($"DRESS_dwBack_{last.name}", group, WoodCarcassEdge, axis,
            bodyBack - edgeT, bodyBack, widthMin, widthMax, 0f, lastTop);
        MakeAxisBox($"DRESS_dwTop_{last.name}", group, WoodFront, axis,
            bodyFront, bodyBack, widthMin, widthMax, lastTop - edgeT, lastTop);

        float prevTop = 0f;
        for (int i = 0; i < n; i++)
        {
            Transform step = steps[i];
            Renderer baseR = step.GetComponent<Renderer>();
            if (baseR != null) baseR.enabled = false;

            float top = TopHeight(step);
            float frontEdge = AscendMin(step, axis);       // 이 단 '자신'의 앞면(riser) — 실좌표

            // 서랍 몸체 — [frontEdge, bodyFront] × 폭 × [prevTop, top]. 마지막 단(frontEdge==
            // bodyFront)은 몸통이 이미 그 자리를 전부 덮으므로 만들지 않는다(닫힌 서랍).
            bool isOpenDrawer = bodyFront - frontEdge > 1e-4f;
            if (isOpenDrawer)
                MakeAxisBox($"DRESS_dw_{step.name}", group, WoodDrawer, axis,
                    frontEdge, bodyFront, widthMin, widthMax, prevTop, top);

            // [2026-09-06 사용자 요청] "계단이 실제로 빠져나온 서랍으로 읽혀야" — 열린 서랍마다
            // 측판·내부 트레이 칸막이 추가(바닥 그림자 띠는 [판정, 09-06]으로 제거 — 아래 참고).
            // 전부 이 단의 "실 노출 트레드"(frontEdge~다음 단 frontEdge, 실 콜라이더 상면 그
            // 자체) *안쪽*에만 두고, 상면 위로 ≤0.02U만 올라간다(컨트롤타워 자체 상한(09-06) —
            // 릴레이·[결정] 근거 없는 이 세션의 자체 판단, 측판도 동일 캡 적용). 콜라이더는
            // 만들지 않는다(Vis()가 PrimitiveType.Cube에서 Collider를 즉시 제거) — 통행·발판
            // 깊이(콜라이더)는 무변화.
            if (isOpenDrawer)
            {
                float nextFront = (i + 1 < n) ? AscendMin(steps[i + 1], axis) : bodyBack;
                float treadDepth = nextFront - frontEdge;      // 이 단의 실제 노출 트레드 깊이(실좌표, 콜라이더 그대로)
                float frontGap = treadDepth * 0.06f;
                float backGap  = treadDepth * 0.18f;           // 뒤쪽을 더 비워 "발판 깊이를 시각적으로만" 좁힌다
                float inA = frontEdge + frontGap;
                float inB = frontEdge + treadDepth - backGap;
                if (inB - inA > 0.05f)                          // 너무 얕은 단 방어(현재 2.4U/1.0U 트레드 둘 다 안전하게 통과)
                {
                    const float wallInset = 0.08f, wallThick = 0.05f, rimUp = 0.02f, trayT = 0.02f;
                    float wL0 = widthMin + wallInset, wL1 = wL0 + wallThick;
                    float wR1 = widthMax - wallInset, wR0 = wR1 - wallThick;

                    // 양쪽 측판 — 트레드 상면 "안쪽"(폭 양 끝에서 0.08U 물러난 자리), 상면 위 0.02U만
                    MakeAxisBox($"DRESS_dwWallL_{step.name}", group, WoodCarcassEdge, axis,
                        inA, inB, wL0, wL1, top, top + rimUp);
                    MakeAxisBox($"DRESS_dwWallR_{step.name}", group, WoodCarcassEdge, axis,
                        inA, inB, wR0, wR1, top, top + rimUp);

                    // 내부 트레이 칸막이 2개 — 측판 안쪽 폭을 3칸으로 분할. 두께 0.02U(요구 상한
                    // 그대로) · 상면 위 0.02U만. 결(wood) 베이스 색만 다르게 — 매끈 재질 아님(N6).
                    float innerL = wL1, innerR = wR0, innerW = innerR - innerL;
                    if (innerW > trayT * 4f)
                    {
                        float c1 = innerL + innerW / 3f, c2 = innerL + innerW * 2f / 3f;
                        MakeAxisBox($"DRESS_dwTray1_{step.name}", group, WoodCarcassEdge, axis,
                            inA, inB, c1 - trayT * 0.5f, c1 + trayT * 0.5f, top, top + trayT);
                        MakeAxisBox($"DRESS_dwTray2_{step.name}", group, WoodCarcassEdge, axis,
                            inA, inB, c2 - trayT * 0.5f, c2 + trayT * 0.5f, top, top + trayT);
                    }
                    // [판정, 2026-09-06] 바닥 안쪽 그림자 띠(구 DRESS_dwShadow, 0.25×폭×0.012)는
                    // 위에서 볼 때 단당 가로 띠를 2개(전면판 상단선 + 그림자 띠)로 만들어 🔒N6
                    // "보이는 결 1개 = 계단 콜라이더 1개(1:1)"(RELAY_3of4:527)와 긴장 — 제거.
                    // 측판 L/R·트레이 칸막이 2(진행축 평행 세로 요소)만 유지.
                }
            }

            // 전면판 — riser(닫힌 서랍은 몸통 앞면과 동일) 중심으로 안 0.04U/밖 0.04U(두께
            // 0.08U ≤ 상한 0.1U)
            MakeAxisBox($"DRESS_dwF_{step.name}", group, WoodFront, axis,
                frontEdge - 0.04f, frontEdge + 0.04f, widthMin, widthMax, prevTop, top);

            // 손잡이 — 전면판 바깥으로 0.08U 추가. [결정 2026-09-03] 사용자 결정 4번이 R6를
            // 대체: 손잡이 총 돌출 ≤0.12U·전면판 riser 밖 ≤0.04U(현행 유지, 손대지 않음)
            float mid = (prevTop + top) * 0.5f;
            float handW = Mathf.Min((widthMax - widthMin) * 0.4f, 1.4f);
            float wc = (widthMin + widthMax) * 0.5f;
            MakeAxisBox($"DRESS_dwH_{step.name}", group, HandleDark, axis,
                frontEdge - 0.12f, frontEdge - 0.04f, wc - handW * 0.5f, wc + handW * 0.5f,
                mid - 0.05f, mid + 0.05f);

            prevTop = top;
        }
        return n;
    }

    static float TopHeight(Transform t) => t.position.y + t.lossyScale.y * 0.5f;
    static float AscendMin(Transform t, StairAxis axis) =>
        axis == StairAxis.X ? t.position.x - t.lossyScale.x * 0.5f : t.position.z - t.lossyScale.z * 0.5f;
    static float AscendMax(Transform t, StairAxis axis) =>
        axis == StairAxis.X ? t.position.x + t.lossyScale.x * 0.5f : t.position.z + t.lossyScale.z * 0.5f;
    static float WidthCenter(Transform t, StairAxis axis) => axis == StairAxis.X ? t.position.z : t.position.x;
    static float WidthHalf(Transform t, StairAxis axis) =>
        axis == StairAxis.X ? t.lossyScale.z * 0.5f : t.lossyScale.x * 0.5f;

    /// <summary>오름축(axis)에 맞춰 문서 x/y에 배치하는 콜라이더 없는 시각 박스. ascendA&lt;ascendB, widthA&lt;widthB 전제.</summary>
    static void MakeAxisBox(string name, Transform parent, Color tint, StairAxis axis,
        float ascendA, float ascendB, float widthA, float widthB, float z0, float z1)
    {
        float x0, x1, y0, y1;
        if (axis == StairAxis.X) { x0 = ascendA; x1 = ascendB; y0 = widthA; y1 = widthB; }
        else { y0 = ascendA; y1 = ascendB; x0 = widthA; x1 = widthB; }
        GameObject go = Vis(name, parent, tint);
        go.transform.position = V3.Doc((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0), Mathf.Abs(y1 - y0));
    }

    // ── 스윙 고리 → 천장 전등 (전선 + 갓) — 고리는 주황 조작 채널 그대로 둔다 ──
    static void DressLamp(Transform ring)
    {
        Vector3 c = ring.position, s = ring.lossyScale;
        float top = c.y + s.y * 0.5f;                      // 고리 상단 (z24.6)
        GameObject cord = Vis($"DRESS_cord_{ring.name}", ring.parent, CordDark);
        cord.transform.position = new Vector3(c.x, (top + 30f) * 0.5f, c.z);
        cord.transform.localScale = new Vector3(0.25f, 30f - top, 0.25f);
        GameObject shade = Vis($"DRESS_shade_{ring.name}", ring.parent, ShadeTint);
        shade.transform.position = new Vector3(c.x, top + 0.35f, c.z);
        shade.transform.localScale = new Vector3(s.x + 1.0f, 0.7f, s.z + 1.0f);
    }

    // ── 후드 전면 버튼 패널 (콜라이더 없음 — 시각 전용, 절차 생성이라 저작권 무관) ──
    static void DressHoodButtons(GameObject root)
    {
        Transform g = FindGroup(root, "S3_RangeHood_P2");
        // 후드 캐노피 남면 y76.5 · 하단 17 — 패널 스트립 + 버튼 4 + 전원(주황) + 표시창
        MakeDoc(g, "DRESS_hood_패널", 60.0f, 76.30f, 17.35f, 68.6f, 76.52f, 18.45f, HandleDark);
        for (int i = 0; i < 4; i++)
            MakeDoc(g, $"DRESS_hood_버튼{i + 1}", 60.6f + i * 1.1f, 76.18f, 17.65f, 61.3f + i * 1.1f, 76.32f, 18.15f, SteelLight);
        MakeDoc(g, "DRESS_hood_전원버튼", 65.4f, 76.18f, 17.65f, 66.2f, 76.32f, 18.15f, new Color(0.95f, 0.58f, 0.23f));
        MakeDoc(g, "DRESS_hood_표시창", 66.7f, 76.20f, 17.70f, 68.3f, 76.31f, 18.10f, new Color(0.35f, 0.75f, 0.85f));
    }

    // ── 덕트 중간턱 3(공중 부양분) 받침 ──
    static void DressDuctBracket(GameObject root)
    {
        Transform g = FindGroup(root, "S4_FridgeWall");
        MakeDoc(g, "DRESS_duct_받침", 92.5f, 74.9f, 22.2f, 94.5f, 75.9f, 23.0f, WoodFront);
    }

    // ── 장독: 짙은 옹기색 + 뚜껑 테 ──
    static void DressJangdok(Transform jar)
    {
        Renderer r = jar.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = Tinted(JangdokTint, "ceramic");
        Vector3 c = jar.position, s = jar.lossyScale;
        GameObject lid = Vis($"DRESS_lid_{jar.name}", jar.parent, new Color(0.32f, 0.22f, 0.16f));
        lid.transform.position = new Vector3(c.x, c.y + s.y * 0.5f - 0.2f, c.z);
        lid.transform.localScale = new Vector3(s.x + 0.3f, 0.5f, s.z + 0.3f);
    }

    // ── 뒷마당·가전 디테일 ──
    static void DressYard(GameObject root)
    {
        Transform g = FindGroup(root, "S8_Backyard_P5");
        // 세탁기: 원형 문(근사) + 조작부
        MakeDoc(g, "DRESS_세탁기_문", 9.5f, 87.90f, 2.5f, 14.5f, 88.02f, 7.0f, GlassDark);
        MakeDoc(g, "DRESS_세탁기_조작부", 14.9f, 87.90f, 7.6f, 17.4f, 88.02f, 8.5f, SteelLight);
        // 실외기: 팬 그릴 2
        MakeDoc(g, "DRESS_실외기_그릴1", 82f, 87.90f, 1.2f, 86.5f, 88.02f, 5.8f, HandleDark);
        MakeDoc(g, "DRESS_실외기_그릴2", 87.5f, 87.90f, 1.2f, 92f, 88.02f, 5.8f, HandleDark);
        // 물탱크: 파란 몸통 틴트 대신 뚜껑만 (몸통은 재질 규칙 유지)
        MakeDoc(g, "DRESS_물탱크_뚜껑", 42f, 88.6f, 8.0f, 45f, 91.6f, 8.5f, new Color(0.35f, 0.50f, 0.70f));
        // 창고: 문 + 문손잡이
        MakeDoc(g, "DRESS_창고_문", 87.5f, 97.90f, 0.05f, 91.0f, 98.02f, 7.0f, WoodFront);
        MakeDoc(g, "DRESS_창고_손잡이", 90.4f, 97.86f, 3.3f, 90.7f, 98.02f, 4.0f, HandleDark);
        // 장독 사이 나무 판 받침 기둥 2 (기믹 판이 공중에 떠 보이지 않게)
        MakeDoc(g, "DRESS_판받침_1", 44.7f, 104.0f, 0f, 45.2f, 104.5f, 4.6f, WoodFront);
        MakeDoc(g, "DRESS_판받침_2", 68.0f, 104.0f, 0f, 68.5f, 104.5f, 4.6f, WoodFront);
    }

    // ── 루트 가이드: 청록 발자국 점선 + 갈림길 화살표 + 순번 숫자 + 지선 마커 (콜라이더 없음) ──
    // 🔒 컨트롤타워 판정(L5, 검문_Editor3파일_반려표_2026-09-02.md §E-1): 가이드는 어포던스 3채널의
    // 4번째 채널이 아니다 — T0 측정 전용 오버레이. 주황(조작)·자홍(마커)과 색거리 ≥0.5 확보.
    // N3(map-reviewer 재검, 2026-09-03): Dot·Arw를 Orange·Marker·Smooth·GrayLo 4색 전부 ≥0.5
    // 만족하도록 재조정(최소 Dot-Smooth 0.578·Arw-GrayLo 0.581) — 이하 그대로 유지.
    //
    // 2026-09-03 재배선(컨트롤타워, 사용자 결정 1·2번) 전면 개정:
    //  1) S1 식탁은 조형물로만 남고 루트에서 빠졌다(구 T0RS_02_식탁 삭제) — 식탁을 경유하던 구
    //     seg(좌면→앞치마→식탁 CP)와 의자 점프 표시(JumpCue)를 통째로 삭제한다. Apron_앞치마
    //     계단도 V3_Build.cs에서 삭제됐으므로 DressApron()도 삭제(허공 데칼 방지).
    //  2) 조리대 위에서 서쪽(IN선반)·동쪽(코너)으로 갈라지던 T자 갈림을 해소: 동쪽만 점선(관문
    //     진입부에서 끊음), 서쪽 복귀는 화살표+순번만 두고 점선은 스토브 서쪽 끝(x≤56)에서
    //     새로 시작해 전진선(y≈79)과 안 겹치는 y≈82.5~83.5로 서행한다. 점선은 CP 번호 순으로
    //     끊김 없이 이어진다(세그 이름에 진행 순서를 반영, 화력 징검다리·코너 클라임·팬트리
    //     체인·P4 스윙·덕트 낙하는 기존대로 "관문 미표시" — 점선 없이 화살표+다음 번호만).
    // 새 T0RS_01~12 볼륨(중심 ±3, 07만 +0.6 — V3_Checkpoints.cs Defs와 동일 좌표, map-builder
    // 재배열분)을 전부 순서대로 통과하도록 웨이포인트를 다시 짰다(예외: 07 — 06→07 팬트리 체인과
    // 07→08 스윙이 모두 "가이드 없음" 구간이라 점선이 07 볼륨 자체를 지나지 않는다. 07은 화살표
    // 큐 2개(팬트리 진입·스윙)로만 안내되며, 이는 지시 D "06→07·07→08·09→10: 점선 없음, 화살표+
    // 다음 순번"의 명시적 결과다). 파이썬 전수검산(255개 실 콜라이더, 점 199·화살표 8·지선 6)
    // 결과는 캐시·보고서 참고 — 침범 0·공중부양 0·체크포인트 접촉순서 역전 0.
    static readonly Color GuideDot = new Color(0.05f, 0.82f, 0.85f);   // 밝은 청록 — 주 경로 점선
    static readonly Color GuideArw = new Color(0.00f, 0.18f, 0.22f);   // 짙은 청록 — 화살표·순번 숫자
    // 지선(선택 경로) 전용 색 — 노랑연두. Orange·Marker·Smooth·GrayLo·GuideDot·GuideArw 6색 전부와
    // [O6 정정, map-reviewer 실측] 팔레트 전체 최소는 Branch–Rigid(0.80,0.72,0.35) 색거리 0.515,
    // Branch–GuideArw 0.908 — 청록·주황 계열은 확실히 회피했고 나머지 팔레트와도 구분된다.
    static readonly Color GuideBranch = new Color(0.45f, 0.95f, 0.05f);

    static void DressGuide(GameObject root)
    {
        GameObject g = new GameObject("DRESS_RouteGuide");
        g.transform.SetParent(root.transform, false);
        // 구간별 웨이포인트 (문서 x, y, 점이 놓일 표면 z) — 사이를 ~2U 간격으로 보간.
        // 세그 이름 접미사(A~L)는 CP01→CP12 진행 순서를 그대로 따른다.
        float[][][] segs = new float[][][]
        {
            // A: 스폰(매트)→CP01 관통→식탁 동쪽 우회(식탁 밑 통과 금지 유지)→CP02 방향
            // R3'-④ 계승: 매트 구간(63,6)→(63,9)은 매트 상면(z0.3) 유지, (63,10)에서 바닥(z0)으로.
            // 신규: (63,13)에 CP01(x60~66·y10~16) 중심을 정확히 찍고 (65,15.5)로 빠져나간 뒤
            // 동쪽 아크(옛 seg0 후반부, Table_Top x30~70·y18~34 밑을 지나지 않도록 x72부터 우회)로
            // 합류한다.
            Seg(new float[,]{{63,6,0.3f},{63,9,0.3f},{63,10,0f},{63,13,0f},{65,15.5f,0f},
                              {72,17,0f},{78,20,0f},{79,22,0f},{79,30,0f},{76,37,0f},
                              {68,41,0f},{58,42.5f,0f},{49,41,0f},{46.5f,37.5f,0f}}),
            // B: 바닥→계단 발치 (구 seg2, C의 검산: 직결선 최소여유 1.569U — coord-auditor 확인).
            // (44.5,42.5)·(42,46) 모두 CP02(x39~45·y42~48) 안.
            Seg(new float[,]{{47,39,0f},{44.5f,42.5f,0f},{42,46,0f}}),
            // C: Crate_Step 10단 계단 오르기 (구 seg3, 무변경).
            Seg(new float[,]{{42,48.5f,1.0f},{42,51,3.85f},{42,53.5f,5.81f},{42,56,8.64f},{42.5f,58.5f,9.65f}}),
            // [O1 반영, map-reviewer 검문] C끝(42.5,58.5)→D시작(44,64) 5.70U가 점선 없이 끊겨
            // 있었다 — 관문이 아니라 Island_Top 위 평지(z9.65 그대로)라 메울 수 있다. 직선(2점)
            // 으로 처음 채웠더니 x43·y60.33 보간점이 IS_Board_도마(x36~43·y60~66.6)를 스쳤다 —
            // 중계점(44.5,59)을 끼워 도마 y시작(60) 아래에서 먼저 x44.5(도마 밖)로 빠진 뒤
            // 북상하도록 꺾었다(자가검증: 실물 침범 0·공중부양 0, 아래 검산표 참고).
            Seg(new float[,]{{42.5f,58.5f,9.65f},{44.5f,59,9.65f},{44,64,9.65f}}),
            // D: 아일랜드 위→CP03 관통(x36.5~42.5·y65~71)→카트→조리대 착지.
            // IS_Board_도마(x36~43·y60~66.6·z9.6~10.1)와 Molding_W(북쪽 가장자리, y≥68.25 매끈
            // 비탈)를 모두 피해야 한다 — 안전대는 y67.1~67.75(폭 0.65) 뿐이라 y67.2~67.5로 관통.
            // x44 이서(도마 x범위 밖)에서 접근·이탈해 도마를 우회한다.
            Seg(new float[,]{{44,64,9.65f},{44,67.5f,9.65f},{41,67.4f,9.65f},{39.5f,67.4f,9.65f},
                              {41,67.3f,9.65f},{45,67.2f,9.65f},{47,66.5f,9.65f},
                              {49,69.5f,9.65f},{49,72.4f,8.65f},{49,74.6f,8.65f},{49,76,9.65f},{49,79,9.65f}}),
            // E: 조리대→화력 징검다리 진입부(여기서 끊음, 구 seg 무변경). 이후 P2 화력 징검다리·
            // 코너 클라임(PlateStack_C∪Tray_Corner∪CoffeeMachine, x77.6~83.6 병목 0.60U — 같은
            // 높이 우회 불가, coord-auditor 확정)은 관문 미표시 원칙대로 점선을 긋지 않는다.
            Seg(new float[,]{{49,79,9.65f},{55,79.2f,9.65f}}),
            // F: 관문 통과 후 CP04(코너 상판, x83.5~89.5·y78~84) 안에서 점선 재개(구 seg 무변경).
            Seg(new float[,]{{84.5f,81,9.65f},{86.5f,81,9.65f}}),
            // G(신규): 사용자 결정 2번 — T자 갈림 해소. CP04에서 온 길을 되짚어 코너 클러스터·
            // 스토브를 다시 지나는 점선은 긋지 않는다(F/E와 같은 관문 재표시 금지). 서쪽 복귀는
            // CP04 옆 화살표+"05" 숫자로만 안내(아래 arrows)하고, 점선은 Stove_Frame 서쪽 끝
            // (x56) 밖에서 새로 시작한다. 동행선(y≈79.0~79.2)과 겹치지 않도록 y82.5~83.5(Counter_E
            // 상면 위, Stove_Frame과 y74.8~84.95 겹치지만 z9.6 상면이라 안전)로 서행해 (43,80.5)
            // 에서 H(구 seg5)에 합류한다.
            // [O4 반영, map-reviewer 검문] 첫 점 55.5→55.2 — 마름모 동단(55.5+0.495=55.995)이
            // Stove_Frame 서면(x56)과 여유 0.005U뿐이었다. 55.2+0.495=55.695로 여유 0.305U 확보.
            Seg(new float[,]{{55.2f,83,9.65f},{50,83,9.65f},{45,81.5f,9.65f},{43,80.5f,9.65f}}),
            // H: 서행→CP05(x28~34·y77~83) 관통→IN선반 앞턱 (구 seg5, 무변경 — (35,80.5)→(27,77.2)
            // 보간 구간이 이미 (33,79.68) 부근에서 CP05 안을 지난다, 좌표 계산으로 확인).
            Seg(new float[,]{{43,80.5f,9.65f},{35,80.5f,9.65f},{27,77.2f,9.65f},{20,77.2f,9.65f},{15,77.2f,9.65f}}),
            // I(신규): IN선반 앞턱→CP06(x11.5~17.5·y79.6~85.6) 관통. 싱크볼 개구부(x14~26·
            // y77.5~83.5)를 피해 x13.5(Counter_W_L 쪽, 개구부 밖)로 접근한 뒤 카운터 상면(9.6)에서
            // Shelf_IN 상면(10.2)으로 0.6U 단차 상승(≤1.0 기준 통과) — 상승 지점(14,82)은 Pot_A·
            // Cup_A 사이 빈 자리(y82, 각각 y83.1↑·y80.4↓)로 계산해서 잡았다.
            Seg(new float[,]{{15,77.2f,9.65f},{13.5f,79,9.65f},{13.5f,81,9.65f},{14,82,10.25f},{14.5f,82.6f,10.25f}}),
            // ── 여기부터 CP07까지 "가이드 없음"(사용자 결정 2번+지시 D): 팬트리 G0~G5 체인은
            // 점선 없이 진입 화살표(pantry_entry, "07")만 둔다. CP07(상부장 능선, h0.6)은 06→07·
            // 07→08 두 무점선 구간 사이에 있어 점선이 볼륨 자체를 지나지 않는다 — 의도된 예외.
            // J: 냉장고→덕트 계단→동측 패드 (구 seg8, 무변경). 시작점이 CP08(x90.5~96.5·y52~58)
            // 안, 끝점이 CP09(x89~95·y77~83·z24~27) 안.
            Seg(new float[,]{{93.5f,57.5f,20.05f},{94,61.5f,20.05f},{94,62.3f,20.55f},{94,64.7f,21.05f},
                              {94,66.7f,21.55f},{94,68.8f,22.05f},{94,73,23.05f},{93,75.5f,23.55f},
                              {92.5f,78,24.0f},{92,79.5f,24.05f}}),
            // [O1 반영, map-reviewer 검문] J끝(92,79.5)→K시작(85,82) 7.43U가 점선 없이 끊겨
            // 있었다 — 관문이 아니라 Duct_East_Pad/Duct_Floor 위 평지(z24.05 그대로)라 메운다.
            Seg(new float[,]{{92,79.5f,24.05f},{85,82,24.05f}}),
            // K: 덕트 서행→출구 패드 (구 seg9, 무변경). 출구 이후 뒷마당 낙하는 편도(🔒L2)라
            // 점선을 잇지 않고 화살표+"10"만 둔다(아래 arrows).
            Seg(new float[,]{{85,82,24.05f},{70,82,24.05f},{55,82,24.05f},{40,82,24.05f},{25,82,24.05f},
                              {12,82,24.05f},{5.5f,82.5f,24.05f},{5,85,24.05f}}),
            // L: 마당(통로 안 6.5,87.5 시작 — 기존 N6 판정 유지)→CP10→CP11→CP12(x54~60·y99~105)
            // 관통. (41,102.5) 이전은 구 seg10과 완전히 동일(PinchA/B 유지). 그 뒤가 신규 — 옛
            // 종점(PlateA 직행)을 폐기하고 CP12로 우회한다: Jangdok_1(x33~41) 밖(x44.5)에서 살짝
            // 남하해 Bench_평상(y103~·x~44까지) 아래를 피하고, Jangdok_2(x46~54) 위는 y103.2로
            // 넘어(여유 1.2U), FarSide_LeverPad(x52.1~55.9·y103.28~) 밑을 지나는 마지막 구간만
            // PinchD(아래)로 축소한다. CP12에서 점선을 멈추고 PlateA(49,104)를 가리키는 화살표로
            // 마무리한다(지시 D의 "또는 12에서 판A를 가리키는 최종 화살표" 채택 — 되짚어가는
            // 왕복 점선을 피하기 위해).
            Seg(new float[,]{{6.5f,87.5f,0.0f},{18.5f,87.5f,0.0f},{22,93,0.0f},{25,93,0.0f},{28.5f,93.5f,0.0f},
                              {30,99,0.0f},{32.5f,102.3f,0.0f},{33,102.5f,0.0f},{41,102.5f,0.0f},
                              {44.5f,102.2f,0.0f},{48,103.2f,0.0f},{52,102.64f,0.0f},{57,102.3f,0.0f}}),
        };
        // 축소존(pinch): 기본 점(0.7U, 대각반경0.495)이 좁은 통로(폭 1.0U 안팎)에서 양쪽 실재물에
        // 동시에 닿는 구간만 줄인다. PinchA·B·C는 이전 라운드 그대로 유지(🔒 규칙 유지 지시).
        //  · PinchA = 벽(Wall_N_B1)~세탁기 서측 코너 일대 → 0.5U.
        //  · PinchB = 장독열 상단~평상 하단(x30~42) → 0.5U.
        //  · PinchC = Apron Step2 잔재 좌표(seg L 이전 구간엔 더 이상 안 쓰이지만, 다른 좌표와
        //    안 겹치므로 유지 — 회귀 방지) → 0.2U.
        //  · PinchD(신규, L 구간): Jangdok_2 상단(y102)↔FarSide_LeverPad 하단(y103.28) 사이
        //    1.28U 통로, x50~56 — 0.5U(대각0.354)로 확보 마진 0.286U 양쪽.
        (float x0, float x1, float y0, float y1) pinchA = (2f, 19f, 86f, 90f);
        (float x0, float x1, float y0, float y1) pinchB = (30f, 42f, 102f, 103f);
        (float x0, float x1, float y0, float y1) pinchC = (41f, 42f, 34.9f, 35.35f);
        (float x0, float x1, float y0, float y1) pinchD = (50f, 56f, 102f, 103.3f);
        bool InPinch((float x0, float x1, float y0, float y1) r, float x, float y) =>
            x >= r.x0 && x <= r.x1 && y >= r.y0 && y <= r.y1;

        int dots = 0;
        foreach (var seg in segs)
            foreach (var p in seg)
            {
                GameObject d = Vis($"DRESS_guide_dot_{dots++}", g.transform, GuideDot);
                d.transform.position = V3.Doc(p[0], p[1], p[2] + 0.04f);
                float sz = InPinch(pinchC, p[0], p[1]) ? 0.2f
                         : (InPinch(pinchA, p[0], p[1]) || InPinch(pinchB, p[0], p[1]) || InPinch(pinchD, p[0], p[1])) ? 0.5f : 0.7f;
                d.transform.localScale = new Vector3(sz, 0.06f, sz);
                d.transform.rotation = Quaternion.Euler(0, 45f, 0);   // 마름모꼴 점
            }

        // 갈림/끊김 화살표(큰 마름모) — 순번 숫자가 붙는 것과 순수 장식(계속 직진)인 것을 구분.
        // 좌표 뒤 문자열이 null이면 "다음 깃발 번호" 표시가 없는 장식/최종 큐다(지시 E: 번호는
        // 갈림점·끊김점에만). decor_crate·decor_cart_gap은 구간 내내 점선이 이어지는 지점이라
        // 번호 없이 유지(구 화살표#2·#3, 무변경). 화살표#1(46.5,36.5 — 구 "의자 점프" 방향 큐)과
        // 화살표#6(44.3,100.5 — 구 PlateA 직행 큐)은 각각 삭제된 식탁 경로/직행 경로 전용이라
        // 제거한다.
        // sz 열: 기본 1.6U 마름모. pantry_entry_cue만 [O2 반영] Shelf_IN_전면부(y81.2~83.0,
        // 깊이 1.8U)가 기본 크기(대각 2.263U)보다 얕아 전체 발자국이 안 들어갔다(구 위치 기준
        // 17.1%가 그 아래 Counter_W_L 상면 9.6 위 0.65U 공중) — 크기를 1.1U(대각 1.556U)로
        // 줄이고 중심을 그 얕은 선반 안(y82.1)으로 옮겨 발자국 전체가 선반 상면(10.2) 위에만
        // 오도록 했다(마진 y0.12U 양쪽, x는 원래도 넉넉).
        (string label, float x, float y, float z, string num, float sz)[] arrows =
        {
            ("decor_crate",      42f,   46.5f, 0.05f,  null,  1.6f),
            ("decor_cart_gap",   49f,   72f,   8.65f,  null,  1.6f),
            // 게이트 큐(구 화살표#7, 무변경 좌표) — P2 진입 직전. 다음 번호 "04"(코너 상판).
            ("gate_cue",         54.5f, 79.3f, 9.65f,  "04",  1.6f),
            // 신규: CP04 옆 서향 복귀 큐 — CoffeeMachine(x79.8~83.6) 밖 CP04 동쪽 여유 지점.
            ("west_return_cue",  87f,   81f,   9.65f,  "05",  1.6f),
            // 신규: IN선반 위에서 팬트리 방향(서·남)을 가리키는 진입 큐. [O2] y81.7→82.1·크기
            // 1.6→1.1(위 sz 열 설명 참고).
            ("pantry_entry_cue", 13f,   82.1f, 10.25f, "07",  1.1f),
            // 스윙 큐(구 화살표#4=57,81,22.28, 무변경 좌표) — 다음 번호 "08"(냉장고 위 착지).
            ("swing_cue",        57f,   81f,   22.28f, "08",  1.6f),
            // 덕트 낙하 큐(구 화살표#5=5,84,24.08, 무변경 좌표) — 다음 번호 "10"(뒷마당 착지).
            ("duct_drop_cue",    5f,    84f,   24.08f, "10",  1.6f),
            // 신규: CP12에서 PlateA(49,104) 방향 최종 큐 — 다음 CP가 없어 번호 없음(골 지시자).
            ("final_goal_cue",   56.5f, 101f,  0.05f,  null,  1.6f),
        };
        foreach (var a in arrows)
        {
            GameObject go = Vis($"DRESS_guide_arrow_{a.label}", g.transform, GuideArw);
            go.transform.position = V3.Doc(a.x, a.y, a.z);
            go.transform.localScale = new Vector3(a.sz, 0.08f, a.sz);
            go.transform.rotation = Quaternion.Euler(0, 45f, 0);
            if (a.num != null)
                MakeNumber(g.transform, a.x + 0.9f, a.y - 0.4f, a.z + 0.02f, a.num);
        }

        // 지선(선택 경로) 마커 — 점선 없이 GuideBranch 색 마름모 1~2개만(지시 F). 낙하받이는
        // 넓은 선반이라 0.6U, 계단 3종은 발판 폭(0.6U)보다 좁은 0.3U로 이웃 단 침범을 피한다.
        (string name, float x, float y, float z, float sz)[] branches =
        {
            ("낙하받이",       24.0f,  87.7f, 10.2f, 0.6f),   // Shelf_OUT_낙하받이 중앙
            ("복귀계단_기슭",  22.25f, 87.6f, 10.8f, 0.3f),   // Shelf_OUT_복귀계단 Step1 상면
            ("복귀계단_창턱",  22.25f, 86.4f, 12.0f, 0.3f),   // Shelf_OUT_복귀계단 Step3 상면(창턱)
            ("IN창턱계단_기슭", 25.25f, 82.7f, 10.8f, 0.3f),  // Shelf_IN_창턱계단 Step1 상면
            ("IN창턱계단_창턱", 25.25f, 83.9f, 12.0f, 0.3f),  // Shelf_IN_창턱계단 Step3 상면(창턱)
            // [7번 반영, map-reviewer 검문] 이름·주석 명확화(좌표 불변): 🔒L1-b 우회·수집 존
            // 바닥 입구(V1·V2 접근) — 상부장行 팬트리 체인(pantry_entry_cue, 06→07 화살표)과는
            // 별개다(그쪽은 주 경로의 무점선 구간, 이 마커는 그 우회 존으로 들어가는 지선 입구).
            ("팬트리_우회존_바닥입구", 7.0f, 29.0f, 0.05f, 0.6f),   // G0 climb 진입부 앞 바닥
        };
        foreach (var b in branches)
        {
            GameObject go = Vis($"DRESS_guide_branch_{b.name}", g.transform, GuideBranch);
            go.transform.position = V3.Doc(b.x, b.y, b.z + 0.04f);
            go.transform.localScale = new Vector3(b.sz, 0.06f, b.sz);
            go.transform.rotation = Quaternion.Euler(0, 45f, 0);
        }
    }

    /// <summary>순번 숫자 — Unity 내장 TextMesh(콜라이더 없음). 바닥에 눕혀 위에서 읽히게 한다.
    /// [K2 반영] 폰트·머티리얼을 명시 지정하지 않으면 TextMesh가 빈 사각형(머티리얼 없음)으로
    /// 렌더될 수 있어 내장 Arial + 그 폰트의 머티리얼을 직접 건다.
    /// 예상 폭(근사): Unity 문서 예시("Character Size 1 + Font Size 13 ≈ 글자 높이 1.3U")를
    /// 기준으로 환산하면 글자 높이 ≈ characterSize×fontSize×0.1 = 0.15×48×0.1 = 0.72U, 숫자
    /// 글리프 폭은 대략 그 절반(≈0.36U/자) — 2자리(예: "05")면 자간 포함 총 폭 ≈0.8~0.9U로
    /// 상한 1.5U에 여유. 근사식이라 정확한 폭은 유니티 실행 확인이 필요하다(자가 검증 한계,
    /// 검토 요청 참고 — 이전 라운드부터 남아 있는 항목).</summary>
    static void MakeNumber(Transform parent, float x, float y, float z, string label)
    {
        GameObject go = new GameObject($"DRESS_Num_{label}_{x:F0}_{y:F0}");
        go.transform.SetParent(parent, false);
        go.transform.position = V3.Doc(x, y, z);
        go.transform.rotation = Quaternion.Euler(90f, 0, 0);   // 텍스트 평면을 수평으로 눕힌다
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = label;
        tm.characterSize = 0.15f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        // [L1 반영, map-reviewer 2차 검문] Unity 2022.3.62f3에서 "Arial.ttf" 내장 리소스가 이미
        // 제거됐을 수 있다(2020.2+ 추측) — 그러면 GetBuiltinResource가 null을 반환하고 그 결과에
        // .material로 접근하면 NRE가 터져 BuildAll 전체가 죽는다. "LegacyRuntime.ttf"(대체 내장
        // 폰트)를 먼저 시도하고 실패하면 "Arial.ttf"로 폴백, 둘 다 없으면(또는 GetBuiltinResource가
        // 예외를 던지는 경우까지) try/catch로 감싸 숫자만 안 보이고 빌드는 계속되게 한다.
        if (go.GetComponent<MeshRenderer>() == null) go.AddComponent<MeshRenderer>();
        Font f = null;
        try
        {
            f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        catch (System.Exception e) { V3.Warn($"MakeNumber({label}): 내장 폰트 로드 실패({e.Message}) — 숫자 렌더 생략."); }
        if (f != null) { tm.font = f; go.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
        else V3.Warn($"MakeNumber({label}): LegacyRuntime.ttf·Arial.ttf 둘 다 없음 — 숫자 텍스트가 안 보일 수 있다(빌드는 계속).");
    }

    /// <summary>웨이포인트 사이를 ~2U 간격으로 보간해 점 좌표 배열을 만든다.</summary>
    static float[][] Seg(float[,] wp)
    {
        var pts = new List<float[]>();
        for (int i = 0; i < wp.GetLength(0) - 1; i++)
        {
            float dx = wp[i + 1, 0] - wp[i, 0], dy = wp[i + 1, 1] - wp[i, 1], dz = wp[i + 1, 2] - wp[i, 2];
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2f));
            for (int k = 0; k < n; k++)
            {
                float t = (float)k / n;
                pts.Add(new float[] { wp[i, 0] + dx * t, wp[i, 1] + dy * t, wp[i, 2] + dz * t });
            }
        }
        pts.Add(new float[] { wp[wp.GetLength(0) - 1, 0], wp[wp.GetLength(0) - 1, 1], wp[wp.GetLength(0) - 1, 2] });
        return pts.ToArray();
    }

    // ── 헬퍼 ────────────────────────────────────────────────────
    static Transform FindGroup(GameObject root, string name)
    {
        Transform t = root.transform.Find(name);
        return t != null ? t : root.transform;
    }

    static void MakeDoc(Transform parent, string name, float x0, float y0, float z0, float x1, float y1, float z1, Color tint)
    {
        GameObject go = Vis(name, parent, tint);
        go.transform.position = V3.Doc((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(x1 - x0, z1 - z0, y1 - y0);
    }

    static GameObject Vis(string name, Transform parent, Color tint)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = Tinted(tint, "wood");
        return go;
    }

    static readonly Dictionary<Color, Material> tintCache = new Dictionary<Color, Material>();

    static Material Tinted(Color c, string baseKey)
    {
        if (tintCache.TryGetValue(c, out Material cached) && cached != null) return cached;
        Material src = AssetDatabase.LoadAssetAtPath<Material>($"Assets/KitchenMapV3/Materials/KV3_{baseKey}.mat");
        Material m;
        if (src != null)
        {
            m = new Material(src) { name = $"KV3_dress_{ColorUtility.ToHtmlStringRGB(c)}" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        }
        else
        {
            Shader urp = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(urp != null ? urp : Shader.Find("Standard"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        }
        tintCache[c] = m;
        return m;
    }
}
#endif
