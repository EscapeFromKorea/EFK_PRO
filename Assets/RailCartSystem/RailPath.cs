using UnityEngine;

/// <summary>
/// 레일카가 따라가는 경로. 웨이포인트 배열 + 진행 매개변수(t)로 온레일 위치를 코드로 완전히
/// 결정한다(docs/PRD/RailCart.md §3.1·§6). 구간(waypoints[i]→waypoints[i+1])마다 안전 통과
/// 속도를 따로 두어 "속도 임계"와 "분기 탈선"을 하나의 검사로 통합한다.
///
/// [2026-09-05 곡선 지원 추가] 각 구간에 선택적으로 제어점(`curveControlPoints[i]`)을 꽂으면 그
/// 구간이 2차 베지어 곡선으로 휜다 — 제어점을 비워두면(기본값) 기존과 똑같이 직선이다. 분기
/// 표현이 안 맞는다고 기각했던 `AnimationCurve`와는 다른 결정이다 — 여기서 휘는 건 "구간 내부의
/// 모양"일 뿐, 구간을 잇는 순서·탈선 판정(구간별 `maxSafeSpeed`)은 여전히 코드로 완전히 결정되는
/// 웨이포인트 배열 그대로다. 곡선 위 위치/접선/최근접점은 전부 순수 정적 함수로 뽑아 편집기
/// 밖에서도(SelfCheck) 검증 가능하게 했다.
/// </summary>
public class RailPath : MonoBehaviour
{
    [Tooltip("경로를 이루는 지점들. 순서대로 이동한다. 최소 2개 필요.")]
    public Transform[] waypoints;

    [Tooltip("구간별 안전 통과 속도(m/s). waypoints와 길이를 맞춘다 — i번째 값이 " +
             "waypoints[i]→waypoints[i+1] 구간에 적용된다(마지막 원소는 안 쓰인다). 직선 구간은 " +
             "크게, 급커브·분기 구간은 작게 잡는 것만으로 '속도 임계'와 '분기에서 탈선'을 동시에 " +
             "표현한다.")]
    public float[] segmentMaxSafeSpeed;

    [Tooltip("구간별 곡선 제어점(선택). i번째가 waypoints[i]→[i+1] 구간의 2차 베지어 제어점이다 — " +
             "비워두면(null) 그 구간은 직선 그대로다. 제어점을 옆으로 옮기면 레일이 그쪽으로 휜다.")]
    public Transform[] curveControlPoints;

    public int SegmentCount => waypoints != null ? Mathf.Max(0, waypoints.Length - 1) : 0;

    public Vector3 GetPoint(int index)
    {
        index = Mathf.Clamp(index, 0, waypoints.Length - 1);
        Transform wp = waypoints[index];
        // 레벨 편집 중 웨이포인트 하나가 실수로 비면(배열 길이는 그대로, 슬롯만 null) 매 물리
        // 틱 NRE를 내며 카트가 영구히 고장 나는 대신, 경로 자신의 위치로 폴백해 무해하게 넘어간다.
        return wp != null ? wp.position : transform.position;
    }

    public bool HasCurve(int segment) =>
        curveControlPoints != null && segment >= 0 && segment < curveControlPoints.Length &&
        curveControlPoints[segment] != null;

    public Vector3 ControlPoint(int segment) => HasCurve(segment) ? curveControlPoints[segment].position : Vector3.zero;

    public Vector3 Evaluate(int segment, float t)
    {
        segment = Mathf.Clamp(segment, 0, Mathf.Max(0, SegmentCount - 1));
        return EvaluatePoint(GetPoint(segment), GetPoint(segment + 1), ControlPoint(segment), HasCurve(segment), t);
    }

    public Vector3 Tangent(int segment, float t)
    {
        segment = Mathf.Clamp(segment, 0, Mathf.Max(0, SegmentCount - 1));
        return EvaluateTangent(GetPoint(segment), GetPoint(segment + 1), ControlPoint(segment), HasCurve(segment), t);
    }

    /// <summary>이 구간 위에서 월드 좌표 point에 가장 가까운 매개변수 t(0~1)를 샘플링으로 찾는다.
    /// 카트가 물리로 밀려나도(직접 밀기 등) "지금 곡선 위 어디쯤인가"를 실제 위치에서 매 틱 다시
    /// 구해 자연히 보정된다 — 별도의 누적 진행값을 따로 들고 다니지 않는다.</summary>
    public float ClosestT(int segment, Vector3 point, int samples = 16)
    {
        segment = Mathf.Clamp(segment, 0, Mathf.Max(0, SegmentCount - 1));
        return ClosestT(GetPoint(segment), GetPoint(segment + 1), ControlPoint(segment), HasCurve(segment), point, samples);
    }

    /// <summary>구간별 안전 통과 속도. 배열 범위 밖(설정 안 함)이면 사실상 무제한.</summary>
    public float MaxSafeSpeed(int segment)
    {
        if (segmentMaxSafeSpeed == null || segment < 0 || segment >= segmentMaxSafeSpeed.Length)
            return float.PositiveInfinity;
        return segmentMaxSafeSpeed[segment];
    }

    // ── 순수 함수 (저장소 관례 — 곡선 수학을 편집기/플레이 상태 없이 검증 가능하게 뽑아 둔다.
    //    Tools/RailCartSystem/Self-Check 참고) ──────────────────────────────────────────────

    public static Vector3 EvaluatePoint(Vector3 p0, Vector3 p1, Vector3 control, bool curved, float t)
    {
        t = Mathf.Clamp01(t);
        if (!curved) return Vector3.Lerp(p0, p1, t);
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * control + t * t * p1;
    }

    public static Vector3 EvaluateTangent(Vector3 p0, Vector3 p1, Vector3 control, bool curved, float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 d = curved
            ? 2f * (1f - t) * (control - p0) + 2f * t * (p1 - control)
            : (p1 - p0);
        return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
    }

    public static float ClosestT(Vector3 p0, Vector3 p1, Vector3 control, bool curved, Vector3 point, int samples)
    {
        float bestT = 0f;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i <= samples; i++)
        {
            float t = samples > 0 ? (float)i / samples : 0f;
            float distSq = (EvaluatePoint(p0, p1, control, curved, t) - point).sqrMagnitude;
            if (distSq < bestDistSq) { bestDistSq = distSq; bestT = t; }
        }
        return bestT;
    }

    public static string SelfCheck()
    {
        var failures = new System.Collections.Generic.List<string>();

        Vector3 p0 = Vector3.zero;
        Vector3 p1 = new Vector3(10f, 0f, 0f);
        Vector3 control = new Vector3(5f, 4f, 0f);

        // 직선(curved=false)은 순수 Lerp와 같아야 한다.
        Vector3 straightMid = EvaluatePoint(p0, p1, control, false, 0.5f);
        if (Vector3.Distance(straightMid, new Vector3(5f, 0f, 0f)) > 0.001f)
            failures.Add($"직선 구간 중간 지점이 어긋났다 ({straightMid})");

        // 곡선 양 끝(t=0,1)은 제어점과 무관하게 시작점/끝점과 정확히 일치해야 한다.
        if (Vector3.Distance(EvaluatePoint(p0, p1, control, true, 0f), p0) > 0.001f)
            failures.Add("곡선 t=0이 시작점과 다르다");
        if (Vector3.Distance(EvaluatePoint(p0, p1, control, true, 1f), p1) > 0.001f)
            failures.Add("곡선 t=1이 끝점과 다르다");

        // 곡선 중간은 제어점 쪽으로 부풀어야 한다(직선 중점보다 제어점에 더 가까워짐).
        Vector3 curveMid = EvaluatePoint(p0, p1, control, true, 0.5f);
        if (Vector3.Distance(curveMid, control) >= Vector3.Distance(straightMid, control))
            failures.Add($"곡선이 제어점 쪽으로 휘지 않았다 (curveMid={curveMid})");

        // 직선 접선은 시작점→끝점 방향과 일치해야 한다.
        Vector3 straightTangent = EvaluateTangent(p0, p1, control, false, 0.5f);
        if (Vector3.Distance(straightTangent, Vector3.right) > 0.001f)
            failures.Add($"직선 접선이 방향과 다르다 ({straightTangent})");

        // 선분 위 최근접점 — 밖의 점은 끝점으로 clamp, 안의 점은 투영된 지점.
        float tMid = ClosestT(p0, p1, control, false, new Vector3(5f, 3f, 0f), 32);
        if (Mathf.Abs(tMid - 0.5f) > 0.05f)
            failures.Add($"직선 최근접점 t가 예상과 다르다 (t={tMid})");
        float tBeyond = ClosestT(p0, p1, control, false, new Vector3(50f, 0f, 0f), 32);
        if (Mathf.Abs(tBeyond - 1f) > 0.001f)
            failures.Add($"선분 밖 점이 끝점(t=1)으로 clamp되지 않았다 (t={tBeyond})");

        return failures.Count == 0 ? "OK" : string.Join("\n", failures);
    }

    void OnDrawGizmos()
    {
        if (waypoints == null) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < SegmentCount; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            Vector3 prev = GetPoint(i);
            const int samples = 16;
            for (int s = 1; s <= samples; s++)
            {
                Vector3 next = Evaluate(i, (float)s / samples);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
        foreach (Transform wp in waypoints)
            if (wp != null) Gizmos.DrawSphere(wp.position, 0.15f);
    }
}
