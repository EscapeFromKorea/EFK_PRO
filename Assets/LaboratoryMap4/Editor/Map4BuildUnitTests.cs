#if UNITY_EDITOR
using System;
using System.Text;
using UnityEngine;

/// <summary>
/// [F1 재작업 판정 C7] Map4Build의 Door·Stairs·Ramp·WallWithOpenings·CheckGap 헬퍼는 이전 회차까지
/// 실제 호출·단위 검사가 0건이었다(섹터 빌드가 Floor/Wall만 썼기 때문). 이 파일은 각 헬퍼마다
/// "정상 1건 + 위반 1건"을 실행해 기대한 대로(성공/거부·경고) 동작하는지 확인한다.
///
/// 모든 생성물은 임시 루트(Map4BuildUnitTests_Root) 아래에 만들고 RunAll() 끝에서 통째로
/// 파괴한다 — 실제 섹터 씬에는 아무 흔적도 남기지 않는다.
/// </summary>
public static class Map4BuildUnitTests
{
    private class Case
    {
        public string name;
        public bool pass;
        public string detail;
    }

    public static string RunAll(out bool allPass)
    {
        GameObject root = new GameObject("Map4BuildUnitTests_Root");
        System.Collections.Generic.List<Case> cases = new System.Collections.Generic.List<Case>();
        try
        {
            Map4Build.BeginSection();

            cases.Add(TestDoorNormal(root.transform));
            cases.Add(TestDoorViolation(root.transform));
            cases.Add(TestStairsNormal(root.transform));
            cases.Add(TestStairsViolation(root.transform));
            cases.Add(TestRampNormal(root.transform));
            cases.Add(TestRampViolation(root.transform));
            cases.Add(TestRampEndHeights(root.transform));
            cases.Add(TestWallWithOpeningsNormal(root.transform));
            cases.Add(TestWallWithOpeningsViolation(root.transform));
            cases.Add(TestCheckGapNormal());
            cases.Add(TestCheckGapViolation());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        StringBuilder sb = new StringBuilder();
        allPass = true;
        foreach (Case c in cases)
        {
            allPass &= c.pass;
            sb.AppendLine($"  [{(c.pass ? "PASS" : "FAIL")}] {c.name} — {c.detail}");
        }
        return sb.ToString();
    }

    /// <summary>action을 실행하는 동안 Debug 로그를 가로채, expectType(Error/Warning)의 로그 중
    /// expectSubstring을 포함하는 것이 있었는지 본다.</summary>
    private static bool CapturedLog(Action action, LogType expectType, string expectSubstring)
    {
        bool found = false;
        Application.LogCallback handler = (condition, stacktrace, type) =>
        {
            if (type == expectType && condition.Contains(expectSubstring)) found = true;
        };
        Application.logMessageReceived += handler;
        try { action(); }
        finally { Application.logMessageReceived -= handler; }
        return found;
    }

    private static Case TestDoorNormal(Transform root)
    {
        doorPhysics dp = null;
        bool errorSeen = CapturedLog(() =>
        {
            dp = Map4Build.Door(root, new Vector3(0, 0, 0), new Vector3(2.5f, 5f, 0.4f), 5f);
        }, LogType.Error, "통로 폭 부족");
        bool pass = dp != null && !errorSeen;
        return new Case { name = "Door 정상(폭 2.5U ≥ 1.0U)", pass = pass,
            detail = pass ? "생성 성공, 통로 폭 경고 없음" : $"실패(dp={(dp != null)}, 폭부족경고={errorSeen})" };
    }

    private static Case TestDoorViolation(Transform root)
    {
        doorPhysics dp = null;
        bool errorSeen = CapturedLog(() =>
        {
            dp = Map4Build.Door(root, new Vector3(10, 0, 0), new Vector3(10.5f, 5f, 0.4f), 5f);
        }, LogType.Error, "통로 폭 부족");
        // 문 자체는 만들어지되(치수는 이미 정해진 대로) 통로 폭 부족 에러가 남아야 한다.
        bool pass = errorSeen;
        return new Case { name = "Door 위반(폭 0.5U < 1.0U)", pass = pass,
            detail = pass ? "예상대로 '통로 폭 부족' 에러 발생" : "에러가 안 남 — CheckPassageWidth 호출 누락 회귀 의심" };
    }

    private static Case TestStairsNormal(Transform root)
    {
        Transform t = null;
        bool errorSeen = CapturedLog(() =>
        {
            t = Map4Build.Stairs(root, new Vector3(0, 0, 20), 2.7f, 6f, 3, 2f);
        }, LogType.Error, "생성 중단");
        bool pass = t != null && !errorSeen;
        return new Case { name = "Stairs 정상(단높이 0.9U·디딤 2.0U)", pass = pass,
            detail = pass ? "생성 성공" : $"실패(t={(t != null)}, 중단에러={errorSeen})" };
    }

    private static Case TestStairsViolation(Transform root)
    {
        Transform t = null;
        bool errorSeen = CapturedLog(() =>
        {
            t = Map4Build.Stairs(root, new Vector3(0, 0, 30), 5f, 4f, 2, 2f); // 단높이 2.5U > 1.1U
        }, LogType.Error, "생성 중단");
        bool pass = t == null && errorSeen;
        return new Case { name = "Stairs 위반(단높이 2.5U > LD-01 1.1U)", pass = pass,
            detail = pass ? "예상대로 생성 중단" : $"실패 — 거부되지 않음(t={(t != null)}, 에러={errorSeen})" };
    }

    private static Case TestRampNormal(Transform root)
    {
        Transform t = null;
        bool errorSeen = CapturedLog(() =>
        {
            t = Map4Build.Ramp(root, new Vector3(0, 0, 40), 4f, 5f, 15f); // atan(5/15)=18.4도 ≤25도
        }, LogType.Error, "생성 중단");
        bool pass = t != null && !errorSeen;
        return new Case { name = "Ramp 정상(경사각 18.4도 ≤ 25도)", pass = pass,
            detail = pass ? "생성 성공" : $"실패(t={(t != null)}, 중단에러={errorSeen})" };
    }

    private static Case TestRampViolation(Transform root)
    {
        Transform t = null;
        bool errorSeen = CapturedLog(() =>
        {
            t = Map4Build.Ramp(root, new Vector3(0, 0, 60), 4f, 10f, 5f); // atan(10/5)=63.4도 > 25도
        }, LogType.Error, "생성 중단");
        bool pass = t == null && errorSeen;
        return new Case { name = "Ramp 위반(경사각 63.4도 > 25도)", pass = pass,
            detail = pass ? "예상대로 생성 중단" : $"실패 — 거부되지 않음(t={(t != null)}, 에러={errorSeen})" };
    }

    /// <summary>[2차 반려 N1 — "단위 검사에 끝 높이 검사 추가"] Ramp 윗면이 시작·끝에서 의도한
    /// 높이와 턱 0(오차 0.02U 이내)으로 맞는지, out 파라미터뿐 아니라 실제 메시 정점 기하를
    /// 독립적으로 계산해 재확인한다(반환값만 믿지 않는다 — 1차 수정 때 반환값 없이 "통행 지장
    /// 없음"이라고 자체 판단했다가 틀렸던 전례가 있어, 이번엔 기하 자체를 직접 잰다).
    /// [N1 재재수정] Ramp()가 회전 BoxCollider에서 오버행 없는 쐐기 MeshCollider로 바뀌어
    /// GetComponent&lt;BoxCollider&gt;() 대신 MeshFilter.sharedMesh의 정점(0=윗면-시작, 2=윗면-끝)을
    /// 직접 읽는다. Z 오버행이 진짜 0인지도 같이 확인한다(감사에서 반복 재현됐던 문제라
    /// 여기서 놓치면 또 같은 반려를 받는다).</summary>
    private static Case TestRampEndHeights(Transform root)
    {
        const float rise = 8f, run = 20f, width = 4f; // atan(8/20)=21.8도 ≤25도.
        Vector3 baseMin = new Vector3(0f, 0f, 80f);
        Transform t = Map4Build.Ramp(root, baseMin, width, rise, run, out float outStartY, out float outEndY);
        if (t == null)
            return new Case { name = "Ramp 끝높이 턱0(N1)", pass = false, detail = "생성 자체가 실패(사전조건 오류)." };

        MeshFilter mf = t.GetComponent<MeshFilter>();
        Vector3[] verts = mf.sharedMesh.vertices;
        // Map4Build.Ramp 정점 순서: 0=윗면-시작-좌, 2=윗면-끝-우 (go의 로컬 좌표계가 parent와
        // 동일하므로 TransformPoint가 그대로 올바른 월드 좌표를 준다).
        Vector3 startTop = t.TransformPoint(verts[0]);
        Vector3 endTop = t.TransformPoint(verts[2]);

        // 오버행(Z 범위가 [baseMin.z, baseMin.z+run]을 벗어나는지) — 회전 박스 시절 감사에서
        // 반복 재현된 겹침의 근본 원인이었다. MeshCollider convex 후 bounds로 확인한다.
        MeshCollider mc = t.GetComponent<MeshCollider>();
        Bounds b = mc.bounds; // 월드 공간.
        float expectedZMin = root.TransformPoint(new Vector3(0f, 0f, baseMin.z)).z;
        float expectedZMax = root.TransformPoint(new Vector3(0f, 0f, baseMin.z + run)).z;
        const float zTol = 0.01f;
        bool noOverhang = b.min.z >= expectedZMin - zTol && b.max.z <= expectedZMax + zTol;

        float expectedStartY = baseMin.y;
        float expectedEndY = baseMin.y + rise;
        const float tol = 0.02f; // 결정11 허용 오차.
        bool startOk = Mathf.Abs(startTop.y - expectedStartY) <= tol;
        bool endOk = Mathf.Abs(endTop.y - expectedEndY) <= tol;
        bool outOk = Mathf.Abs(outStartY - expectedStartY) <= tol && Mathf.Abs(outEndY - expectedEndY) <= tol;
        bool pass = startOk && endOk && outOk && noOverhang;
        return new Case
        {
            name = "Ramp 끝높이 턱0·오버행0(N1, 오차 0.02U)",
            pass = pass,
            detail = pass
                ? $"기하 실측 시작Y={startTop.y:F4}(기대{expectedStartY}) 끝Y={endTop.y:F4}(기대{expectedEndY}) " +
                  $"· Z범위[{b.min.z:F3},{b.max.z:F3}](기대[{expectedZMin:F3},{expectedZMax:F3}])"
                : $"실패 — 기하 실측 시작Y={startTop.y:F4}(기대{expectedStartY}) 끝Y={endTop.y:F4}" +
                  $"(기대{expectedEndY}) · out파라미터 startY={outStartY:F4} endY={outEndY:F4} " +
                  $"· Z범위[{b.min.z:F3},{b.max.z:F3}](기대[{expectedZMin:F3},{expectedZMax:F3}], 오버행={!noOverhang})"
        };
    }

    private static Case TestWallWithOpeningsNormal(Transform root)
    {
        Transform[] pieces = null;
        bool warnSeen = CapturedLog(() =>
        {
            pieces = Map4Build.WallWithOpenings(root, new Vector3(0, 0, 80), new Vector3(10, 3, 80.5f),
                new System.Collections.Generic.List<Vector2>(new[] { new Vector2(4f, 6f) })); // 폭 2U ≥ 1.0U
        }, LogType.Warning, "구멍 폭");
        bool pass = pieces != null && pieces.Length == 2 && !warnSeen;
        return new Case { name = "WallWithOpenings 정상(구멍 폭 2.0U)", pass = pass,
            detail = pass ? $"조각 {pieces?.Length}개, 경고 없음" : $"실패(조각={(pieces?.Length ?? -1)}, 폭경고={warnSeen})" };
    }

    private static Case TestWallWithOpeningsViolation(Transform root)
    {
        Transform[] pieces = null;
        bool warnSeen = CapturedLog(() =>
        {
            pieces = Map4Build.WallWithOpenings(root, new Vector3(0, 0, 90), new Vector3(10, 3, 90.5f),
                new System.Collections.Generic.List<Vector2>(new[] { new Vector2(4f, 4.5f) })); // 폭 0.5U < 1.0U
        }, LogType.Warning, "구멍 폭");
        bool pass = warnSeen; // 만들어지긴 하되(디자인 의도일 수 있어 거부는 아님) 경고는 남아야 한다.
        return new Case { name = "WallWithOpenings 위반(구멍 폭 0.5U < 1.0U)", pass = pass,
            detail = pass ? "예상대로 '구멍 폭' 경고 발생" : "경고가 안 남 — 좁은 구멍 경고 누락 회귀 의심" };
    }

    private static Case TestCheckGapNormal()
    {
        bool errorSeen = CapturedLog(() =>
        {
            Map4Build.CheckGap(Vector3.zero, new Vector3(1, 1, 1), new Vector3(2, 0, 0), new Vector3(3, 1, 1),
                "A", "B"); // 틈 1.0U — 경계값, 위험 구간(0<gap<1.0) 밖.
        }, LogType.Error, "끼임 틈 위험");
        bool pass = !errorSeen;
        return new Case { name = "CheckGap 정상(틈 1.0U, 위험 구간 밖)", pass = pass,
            detail = pass ? "경고 없음" : "예상과 다르게 경고가 남" };
    }

    private static Case TestCheckGapViolation()
    {
        bool errorSeen = CapturedLog(() =>
        {
            Map4Build.CheckGap(Vector3.zero, new Vector3(1, 1, 1), new Vector3(1.5f, 0, 0), new Vector3(2.5f, 1, 1),
                "A", "B"); // 틈 0.5U — 위험 구간(0<gap<1.0) 안.
        }, LogType.Error, "끼임 틈 위험");
        bool pass = errorSeen;
        return new Case { name = "CheckGap 위반(틈 0.5U, 위험 구간 안)", pass = pass,
            detail = pass ? "예상대로 '끼임 틈 위험' 경고 발생" : "경고가 안 남 — 회귀 의심" };
    }
}
#endif
