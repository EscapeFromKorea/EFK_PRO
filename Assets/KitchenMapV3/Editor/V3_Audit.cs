#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 — 물리 감사 + T0 측정 리포트.
/// 🔒M1 전제: 물리 안정성 감사 4종(겹침·끼임·튕김·관통) 0건 + T0 4항 수치 제출.
/// 이 도구는 정적 검사(겹침·부유·질량·성능)와 수치 리포트를 낸다.
/// 튕김·관통은 ▶ 플레이 중 관찰 항목이므로 체크리스트로만 출력한다.
/// </summary>
public static class V3Audit
{
    public static void Run()
    {
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { V3.Warn("블록아웃이 없다 — 먼저 Build All."); return; }
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("══════════ KitchenMapV3 · 물리 감사 + T0 측정 ══════════");

        OverlapAudit(root, sb);
        RigidbodyAudit(root, sb);
        MassAudit(root, sb);
        T0Report(sb);

        sb.AppendLine("── 플레이 중 관찰(정적 검사 불가) ──");
        sb.AppendLine("  · 튕김: 스폰 직후·소품 접촉 시 급가속 없는가");
        sb.AppendLine("  · 관통: 도형이 벽·바닥·계단을 뚫는가 (특히 계단 턱 모서리)");
        sb.AppendLine("  · 끼임: 서랍 계단 틈·싱크볼 스텝·선반↔창턱 사이 (수평 틈 0 규칙)");
        sb.AppendLine("  · P5 차폐물: 밀리는가 (🔒H6 (a) 고정 — 밀리면 위반)");
        sb.AppendLine("  · Bucket_물통: 플레이 시작 시 정지 상태 유지 여부(세탁기 겹침 0.2 보정 후, FROM_코워크 09-06 [결정] 4)");
        Debug.Log(sb.ToString());
    }

    // ── 1. 겹침: 전수 콜라이더 쌍을 5분류로 나눠 전부 노출 ─────────
    // 판정 대상(❌ 계수, FROM_코워크 2026-09-06 [결정] 6 — 공통규칙 §3 개정):
    //   ① 크로스그룹 정적 솔리드↔솔리드 관통 ≥0.10U
    //   ② 비kinematic Rigidbody ↔ 임의 콜라이더(트리거 상대 제외) 겹침 — 임계 0(공통규칙
    //      §3 개정 문면 "겹침 = 0건", 임계 없음). 시작 순간 PhysX 밀어내기(튕김) 방지가
    //      목적이라 kinematic Rigidbody끼리·kinematic↔정적·트리거 상대는 여기 안 들어간다
    //      (그 쌍은 물리적으로 밀어내지 않는다).
    // 참고 열거(❌ 미계수): 같은 그룹 내부 겹침 · 트리거 볼륨 겹침 · kinematic Rigidbody가
    // 낀 겹침(트리거 상대 포함) — 필터링해서 버리지 않고 전부 노출한다(2026-09-05 map-reviewer
    // 반려표 반영 유지). ①·같은 그룹·트리거·kinematic 분류는 기존 0.10U 임계 그대로.
    // 임계 등호 포함(①등 0.10U 계열); CoffeeMachine×T0RS_04는 float32 MTD 0.0999908로
    // 여전히 미계수(검문 14차 재현, 배치 재실행으로 확정 (추측)) — ①에만 해당, ②는 임계 0.
    static void OverlapAudit(GameObject root, StringBuilder sb)
    {
        // [2026-09-06 R2, map-reviewer 21차 반려 — 이전(#22) 필터를 뒤집는다] 이전엔 "게이트
        // 자식(_Gate) 제외 + 잠긴 부모(T0RS_nn) 포함"이었다. 그런데 부모의 BoxCollider는 순차
        // 잠금 중 실제로 Collider.enabled=false가 된다 — Unity 문서: disabled 콜라이더의
        // Collider.bounds는 빈 바운딩 박스(0 크기, 사실상 transform.position)를 반환한다. 잠긴
        // 부모를 감사 모집단에 남겨 두면 "지금 측정 대상"이 잠금 상태에 따라 크기가 0으로
        // 요동치는, 감사 대상으로 부적절한 콜라이더가 된다(아래 ApproxWorldAABB로 값 자체는
        // 안전하게 계산되지만, 애초에 이 콜라이더가 재는 것은 "지형"이 아니라 "지금 잠겼는가"라는
        // 상태다). 대신 항상 켜져 있는 전용 자식 `T0RS_nn_Gate`(부모와 100% 동일 center/size,
        // Place()가 절대 안 끔)로 대체한다 — 이름이 "T0RS_"로 시작하면서 "_Gate"로 끝나지 않는
        // 것(=부모)은 모집단에서 빼고, "_Gate"로 끝나는 것(=자식)은 그대로 포함한다. 부모·자식은
        // 1대1이라 모집단 총 개수(staticCount)는 이전과 동일하게 유지된다. 출력에 나오는 이름은
        // 그래서 "T0RS_nn_Gate"가 된다 — 아래 ③ 트리거 볼륨 겹침 집계 문구에 이 사실을 명시한다.
        // ComputePenetrationPair는 이제 자식(둘 다 항상 enabled)끼리만 계산하므로 그대로 유효하다.
        List<BoxCollider> all = new List<BoxCollider>(root.GetComponentsInChildren<BoxCollider>())
            .Where(c => !(c.gameObject.name.StartsWith("T0RS_") && !c.gameObject.name.EndsWith("_Gate")))
            .ToList();

        // [R2] a.bounds.Intersects(b.bounds) 선필터도 disabled 콜라이더에 취약하다(위와 같은
        // 이유 — 빈 바운딩 박스는 뭐든 "안 겹침"으로 잘못 판정한다). enabled·activeInHierarchy와
        // 무관하게 항상 유효한 AABB를 transform+center+size로 직접 계산해 둔다(회전 박스는 8개
        // 로컬 꼭짓점을 월드로 변환해 감싸므로 정확한 진짜 AABB다 — 선필터일 뿐이고, 실제 겹침
        // 판정은 회전을 아는 ComputePenetrationPair가 아래에서 담당하므로 이 선필터는 넉넉해도
        // 무방하다). 쌍마다 새로 계산하지 않게 콜라이더당 1회만 미리 구해 둔다.
        Bounds[] approxBounds = all.Select(ApproxWorldAABB).ToArray();

        int crossBad = 0, sameGroupBad = 0, triggerBad = 0, rigidbodyBad = 0, movingBad = 0;
        StringBuilder crossLines = new StringBuilder();
        StringBuilder sameGroupLines = new StringBuilder();
        StringBuilder triggerLines = new StringBuilder();
        StringBuilder rigidbodyLines = new StringBuilder();   // 참고: kinematic 관련·트리거 상대
        StringBuilder movingLines = new StringBuilder();      // ② 판정 대상: 비kinematic RB 겹침

        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                BoxCollider a = all[i], b = all[j];
                if (!approxBounds[i].Intersects(approxBounds[j])) continue;

                Rigidbody aRbComp = a.attachedRigidbody;
                Rigidbody bRbComp = b.attachedRigidbody;
                bool aRb = aRbComp != null;
                bool bRb = bRbComp != null;
                bool aTrig = a.isTrigger;
                bool bTrig = b.isTrigger;

                // Rigidbody 포함(둘 다 Rigidbody인 가동↔가동 쌍도 포함 — 2026-09-05 검문 R2).
                bool rigidbodyPair = aRb || bRb;
                // 트리거 관련(어느 한쪽이라도 트리거).
                bool triggerPair = aTrig || bTrig;
                // ② 판정 대상 조건(2026-09-06 [결정] 6): 어느 한쪽이 "비kinematic" Rigidbody이고,
                // 트리거 상대가 아닌 겹침만 튕김 위험(❌ 계수). kinematic Rigidbody만 낀 쌍은
                // 밀어내기가 없어 참고로 내려간다.
                bool aNonKinematicRb = aRb && !aRbComp.isKinematic;
                bool bNonKinematicRb = bRb && !bRbComp.isKinematic;
                bool movingPair = rigidbodyPair && !triggerPair && (aNonKinematicRb || bNonKinematicRb);

                float dist;
                bool pen = ComputePenetrationPair(a, b, out dist);
                if (!pen) continue;
                // ② 임계 0(문면 0건 — 공통규칙 §3 개정, 컨트롤타워 09-06 판정): 어떤 겹침이든
                // 계수한다(등호 없이 dist>0이면 전부). ①·같은 그룹·트리거·kinematic 분류는
                // 기존 0.10U 임계 유지(부동소수 근접 오차 방지, 이 분류는 무변경).
                if (movingPair) { if (dist <= 0f) continue; }
                else if (dist < 0.10f) continue;

                string line = $"  ⚠️ 겹침 {dist:F2}U: {Path(a)} ↔ {Path(b)}";

                if (movingPair)
                {
                    string rbTag = (aNonKinematicRb && bNonKinematicRb) ? "[가동↔가동]" : "[가동↔콜라이더]";
                    movingLines.AppendLine("  " + rbTag + " " + line.TrimStart());
                    movingBad++;
                }
                else if (rigidbodyPair)
                {
                    // 참고: kinematic Rigidbody끼리·kinematic↔정적, 또는 트리거를 겸하는 쌍.
                    string rbTag = (aRb && bRb) ? "[가동↔가동]" : "[가동↔정적]";
                    string trigSuffix = triggerPair ? " (트리거 동시 해당)" : "";
                    rigidbodyLines.AppendLine("  " + rbTag + " " + line.TrimStart() + trigSuffix);
                    rigidbodyBad++;
                }
                else if (triggerPair)
                {
                    triggerLines.AppendLine("  [트리거] " + line.TrimStart());
                    triggerBad++;
                }
                else if (a.transform.parent == b.transform.parent)
                {
                    sameGroupLines.AppendLine("  [같은 그룹] " + line.TrimStart());
                    sameGroupBad++;
                }
                else
                {
                    crossLines.AppendLine(line);
                    crossBad++;
                }
            }

        // 개수 표시는 기존과 동일하게 "정적 박스"(Rigidbody 없는 콜라이더)만 센다 — 순회
        // 대상(all)에는 Rigidbody 콜라이더도 섞여 있어 따로 다시 센다.
        int staticCount = all.Count(c => c.attachedRigidbody == null);
        // staticCount 중 트리거(isTrigger)는 ①의 크로스그룹 솔리드↔솔리드 판정에서 실제로는
        // 제외되므로(위 if/else에서 rigidbodyPair·triggerPair가 먼저 걸러짐), 헤더에
        // "① 모집단 = staticCount − staticTriggerCount"를 병기해 혼동을 없앤다.
        int staticTriggerCount = all.Count(c => c.attachedRigidbody == null && c.isTrigger);
        int judgedBad = crossBad + movingBad;
        sb.Append(crossLines);
        sb.Append(movingLines);
        sb.AppendLine($"── 1. 겹침 감사: 정적 박스 {staticCount}개(트리거 {staticTriggerCount}개 포함, ① 모집단 {staticCount - staticTriggerCount}개) · ① 정적 관통 {crossBad}건 · ② 가동체 겹침 {movingBad}건 → {(judgedBad == 0 ? "✅" : "❌")}");

        // 참고: 같은 그룹 내 솔리드↔솔리드(의도된 맞닿음이 섞여 있을 수 있음) — 판정 제외.
        sb.Append(sameGroupLines);
        sb.AppendLine($"     같은 그룹 내 겹침 {sameGroupBad}건(참고)");

        // 참고: 트리거 볼륨 관련 — 물리 관통 아님(🔒M3 항목, 예: T0RS RespawnZone 6×H×6).
        // [2026-09-06 R2] T0RS 볼륨 = 자식 _Gate 기준(부모는 잠금 시 콜라이더 OFF라 감사에서
        // 제외) — 위 필터가 부모(T0RS_nn)를 빼고 자식(T0RS_nn_Gate)만 모집단에 넣으므로, 여기
        // 나오는 T0RS 관련 겹침 줄의 이름은 전부 "T0RS_nn_Gate"다. 부모·자식 자기중복 집계
        // 문제(이전 필터 방향의 우려)는 애초에 부모가 모집단에 없으므로 발생하지 않는다.
        sb.Append(triggerLines);
        sb.AppendLine($"     트리거 볼륨 겹침 {triggerBad}건(🔒M3 항목, 물리 관통 아님, T0RS 볼륨 = 자식 _Gate 기준)");

        // 참고: kinematic Rigidbody가 낀 겹침(트리거 상대 포함) — 밀어내기 없음, 판정 제외.
        sb.Append(rigidbodyLines);
        sb.AppendLine($"     kinematic 가동 프롭 겹침 {rigidbodyBad}건(참고, 물리 차단 없음)");
    }

    /// <summary>[R2, map-reviewer 21차] Collider.bounds는 콜라이더가 disabled이거나 GameObject가
    /// inactive면 빈 바운딩 박스(0 크기, 사실상 transform.position)를 반환한다(Unity 문서 확정
    /// 동작) — 이 감사가 매번 정적으로 순회하는 콜라이더 중 일부(잠긴 T0RS_nn 부모는 이제
    /// 모집단에서 빠졌지만, 앞으로도 disabled 콜라이더가 섞일 가능성 자체를 선필터가 취약하지
    /// 않게 막아 둔다)가 있어도 안전하도록 enabled 여부와 무관하게 항상 유효한 진짜 월드 AABB를
    /// 직접 계산한다. 로컬 8개 꼭짓점(center ± size/2)을 transform으로 월드 변환해 감싸므로
    /// 회전된 박스(예: T0RS_10, Y180)에도 정확하다 — 근사가 아니라 정확한 AABB이며, 선필터
    /// 용도이므로 이후 ComputePenetrationPair(회전 인식, 정확한 겹침 판정)가 최종 판정을
    /// 담당한다.</summary>
    static Bounds ApproxWorldAABB(BoxCollider c)
    {
        Vector3 half = c.size * 0.5f;
        Bounds b = new Bounds(c.transform.TransformPoint(c.center), Vector3.zero);
        for (int xi = -1; xi <= 1; xi += 2)
            for (int yi = -1; yi <= 1; yi += 2)
                for (int zi = -1; zi <= 1; zi += 2)
                {
                    Vector3 localCorner = c.center + Vector3.Scale(half, new Vector3(xi, yi, zi));
                    b.Encapsulate(c.transform.TransformPoint(localCorner));
                }
        return b;
    }

    /// <summary>Physics.ComputePenetration은 트리거 콜라이더에도 그대로 동작한다(콜라이더의
    /// isTrigger 여부와 무관하게 두 형상의 기하 교차만 계산 — 근거: 맵2_V3_릴레이설계/
    /// 무인검증/무인검증_보고_2026-09-05.md §3(15:04 콘솔 보존본)에서 트리거 15건이 실제
    /// 계산된 실출력으로 확인됨). 폴백 없음 — try/catch로
    /// 감싸지 않고 그대로 호출한다(R6: 이름에서 "Safe"를 빼 폴백이 있다는 암시를 없앤다).</summary>
    static bool ComputePenetrationPair(BoxCollider a, BoxCollider b, out float dist)
    {
        return Physics.ComputePenetration(
            a, a.transform.position, a.transform.rotation,
            b, b.transform.position, b.transform.rotation,
            out _, out dist);
    }

    // ── 2. 성능: 구역별 Rigidbody 수 (상한 40) ───────────────────
    static void RigidbodyAudit(GameObject root, StringBuilder sb)
    {
        Dictionary<string, int> bySector = new Dictionary<string, int>();
        Rigidbody[] all = Object.FindObjectsOfType<Rigidbody>();
        foreach (Rigidbody rb in all)
        {
            Transform t = rb.transform;
            while (t.parent != null && t.parent.gameObject != root) t = t.parent;
            string sector = (t.parent == null) ? "(씬 루트)" : t.name;
            bySector.TryGetValue(sector, out int n);
            bySector[sector] = n + 1;
        }
        sb.AppendLine($"── 2. 성능(Rigidbody): 총 {all.Length}개 (구역 상한 40)");
        foreach (var kv in bySector)
            sb.AppendLine($"     {kv.Key}: {kv.Value}{(kv.Value > 40 ? "  ❌ 상한 초과" : "")}");
    }

    // ── 3. 질량 감사: 가동 프롭 2.70 초과 (검산 N-15 규칙) ────────
    static void MassAudit(GameObject root, StringBuilder sb)
    {
        int bad = 0;
        foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>())
        {
            if (rb.isKinematic) continue;
            if (rb.mass > 2.70f && !rb.name.Contains("Trolley"))
            {
                sb.AppendLine($"  ⚠️ 질량 {rb.mass:F2} > 2.70 가동 프롭: {rb.name}");
                bad++;
            }
        }
        sb.AppendLine($"── 3. 질량 감사(가동 2.70 초과): {bad}건 {(bad == 0 ? "✅" : "❌")} (Trolley는 관문 장치라 예외)");
    }

    // ── 4. T0 측정 리포트 ────────────────────────────────────────
    static void T0Report(StringBuilder sb)
    {
        sb.AppendLine("── 4. T0 측정 4항 (🔒M1 · 감독 §⑥-3) ──");
        // ㉠ T0-1a/1b — 기하 수치는 레이아웃에서, 실효값은 플레이 실측으로.
        float planeW = 52.2f - 45.8f;
        sb.AppendLine($"  ㉠ P1: 갭 4.80U [검증] · 착지 평면부 폭 {planeW:F1}U (도킹 폭) — 조건① ≥3.0 {(planeW >= 3f ? "✅" : "❌")}");
        sb.AppendLine("       T0-1a: ▶에서 구 단독 도약 → 평면 밖 몰딩에 서지지는지 (조준 궤도와 분리 측정 — 감독 §⑦-3)");
        sb.AppendLine("       T0-1b: Trolley isKinematic 해제·도킹 후 실효 착지 폭 ≥3.0 확인 (1a 단독 통과 계수 금지 — 🔒M6-b)");
        sb.AppendLine("  ㉡ V1 5분 예산: ▶ 스톱워치 — 매트→러그 존→아일랜드行 수납상자 계단(Crate_Step 10단)→아일랜드→카트→상판 (초과 시 놀이터 삭감 순서표)");
        sb.AppendLine("  ㉢ P4 반복 체감: 스윙 고리 z24 · 상부장 22.2→냉장고 20 — CP6 반복 재도전 시 피로 확인");
        sb.AppendLine("  ㉣ S1 성능: 위 2번 리포트의 S1_* 합계 (예산 32/40 · 초과 시 P-9 삭감 1→3순위)");
        // 검증 상수 대조표
        sb.AppendLine("── 좌표 검증 상수 대조 (빌드 결과가 이 값과 다르면 버그) ──");
        sb.AppendLine("  수납상자 계단 0.96×10 (조리대行 x10.4~34.4 · 아일랜드行 x38~46) | 싱크 바닥 7.4 스텝 8.3/9.3+턱2");
        sb.AppendLine("  냉장고 20 | 덕트계단 21/22/23+중간턱 0.5 | 패드·덕트 24 | 능선 22.2 | 후드 19 | 팬트리 선반 4.8/9.6/14.4/19.2");
        sb.AppendLine("  창턱 12.0(실좌표 11.5~12) | IN/OUT 선반 10.2(OUT x18~29.8 — N-19·20) | 복귀 3단 0.6 | 게이트 h12 · 판 B/C 배치안");
    }

    static string Path(Component c)
    {
        return (c.transform.parent != null ? c.transform.parent.name + "/" : "") + c.name;
    }
}
#endif
