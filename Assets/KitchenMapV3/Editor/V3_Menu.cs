#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 메뉴 — T0 블록아웃 절차 (릴레이 1회차 확정 설계 · 2026-08-31).
/// 순서: 1 Clear → 2 Build All → 3 Setup Play → ▶ → 4 Audit & T0 Measure.
/// </summary>
public static class V3Menu
{
    [MenuItem("Tools/KitchenMapV3/1. Clear Blockout", false, 0)]
    public static void Clear() => V3Build.Clear();

    [MenuItem("Tools/KitchenMapV3/2. Build All (T0 블록아웃+질감+자연화)", false, 1)]
    public static void BuildAll() => V3Build.BuildAll();

    [MenuItem("Tools/KitchenMapV3/3. Setup Play (도형 3종 + 카메라)", false, 2)]
    public static void SetupPlay() => V3Play.SetupPlay();

    [MenuItem("Tools/KitchenMapV3/4. Audit + T0 Measure (콘솔 리포트)", false, 3)]
    public static void Audit() => V3Audit.Run();

    [MenuItem("Tools/KitchenMapV3/5. Trolley 도킹 토글", false, 20)]
    public static void ToggleTrolley()
    {
        GameObject t = GameObject.Find("Trolley_카트");
        if (t == null) { V3.Warn("Trolley 없음 — Build All 먼저."); return; }
        // 도킹 = 갭 중앙으로 이동(정적) / 비도킹 = 아일랜드 안쪽 대기
        Rigidbody rb = t.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        bool docked = t.transform.position.z > 70f;   // Unity z = 문서 y
        // 도킹: 갭(문서 y70~74.8, 실좌표)을 메움 / 비도킹: 동쪽 바닥 대기(문서 x86.5 · y40)
        t.transform.position = docked
            ? new Vector3(86.5f, t.transform.position.y, 40f)
            : new Vector3(49f, t.transform.position.y, 72.35f);
        V3.Log(docked ? "Trolley 비도킹(동쪽 바닥 대기) — T0-1a 상태." : "Trolley 도킹(갭 메움) — T0-1b 상태.");
    }

    /// <summary>[후보 구현 — P5 뒷마당 카메라 제한] 근거: 맵2_V3_릴레이설계/
    /// 하18_P5카메라_산출보고서_2026-09-05.md §5(구현 스펙 초안). 정식 채택 아님 — 릴레이 질의
    /// (마)-2 [수단 인정 여부] 판정 전까지의 T0 체감 측정용 후보. 기본값 ON(true)을 끄면 뒷마당
    /// 구간 거리 상한(8U)·충돌 당김(0.3U)이 비활성화되고 실내와 동일한 원래 동작(거리 3~30)으로
    /// 돌아간다.</summary>
    [MenuItem("Tools/KitchenMapV3/8d. Toggle P5 Camera Limit (뒷마당 거리상한·충돌당김)", false, 39)]
    public static void ToggleYardCameraLimit()
    {
        V3ThirdPersonCamera cam = Object.FindObjectOfType<V3ThirdPersonCamera>();
        if (cam == null)
        {
            V3.Warn("V3ThirdPersonCamera를 찾지 못했다 — 먼저 Setup Play를 실행해라.");
            return;
        }
        cam.yardCameraLimit = !cam.yardCameraLimit;
        EditorUtility.SetDirty(cam);   // 검문 C3: 8c(ToggleSequentialLock)와 동일 처리 — 씬 저장 보장 여부는 유니티 실행 후 확정 (추측)
        V3.Log($"P5 뒷마당 카메라 제한 {(cam.yardCameraLimit ? "켜짐" : "꺼짐")} — 후보 구현, (마)-2 판정 대기(하18 보고서 §5).");
    }
}
#endif
