#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// F1-5 배치 실행 진입점. 각 단계를 개별 -executeMethod 대상으로 독립 호출할 수 있게 나눈다
/// (KitchenMapV3/Assets/KitchenMapV3/Editor/V3_Batch.cs의 "단계별 개별 try/catch + 로그 캡처"
/// 설계 원칙을 재사용 — 로직 복제 아님, 이 파일은 맵4 전용 신규 코드다).
///
/// 사용법(명령행): Unity.exe -batchmode -projectPath &lt;경로&gt; -executeMethod Map4Batch.Generate
///                 -logFile &lt;로그경로&gt; -quit
/// Generate/Audit/Build는 동기 완료라 -quit을 쓸 수 있다 — 플레이 모드 테스트(F1-6 항목2·4·5·6)는
/// 별도 Map4PlayTestRunner.Run이 담당하며 그쪽은 -quit을 쓰지 않는다(비동기 플레이 모드 전환이
/// -quit로 끊기기 때문 — 그 파일 클래스 주석 참고).
/// </summary>
public static class Map4Batch
{
    [MenuItem("Tools/Laboratory Map4/Batch/1. Generate")]
    public static void Generate()
    {
        RunGuarded("generate", () =>
        {
            Map4SceneBuilder.GenerateAll();
            return "Master + 섹터 8개 생성 완료.";
        });
    }

    [MenuItem("Tools/Laboratory Map4/Batch/2. Audit")]
    public static void Audit()
    {
        RunGuarded("audit", () =>
        {
            Map4SceneBuilder.OpenAllSectors();
            Map4Audit.Result r = Map4Audit.RunStaticAudit();
            string verdict = r.overlapCount > 0 ? "❌" : "✅";
            string detail = r.overlapDetails.Count > 0 ? "\n" + string.Join("\n", r.overlapDetails) : "";

            // [INF3-3 · 2차판정 17] 관통 ≥1이면 exit 1 — 위 ❌ 표기와 exit 판정을 맞춘다(기존 약점 보강, 약화 아님).
            // 억제 구간(아래 BeginSuppressErrorCounting) **밖**이라 이 LogError는 errorCount에 들어가 RunGuarded의
            // exitCode = (ok && errorCount == 0) ? 0 : 1 이 1이 된다. expectedSuppressedErrors(4)에는 들어가지 않는다.
            // r은 음성 대조 박스를 만들기 **전**의 실제 맵 감사 결과다 — RunNegativeControlTest의 NC_A/NC_B 겹침은 그 함수
            // 안의 별도 Result라 이 판정에 섞이지 않는다(그쪽은 억제 구간 안, "검출됨"이 통과 조건).
            // S2 레버↔받침대 관통도 감사 제외로 돌리지 않는다(지형을 고친다 [2차판정 10]) — S2-B 반영 전이면 exit 1이 정상.
            if (r.overlapCount >= 1)
                Debug.LogError($"[Map4Batch] 관통 {r.overlapCount}건 — 판정 17: exit 1");

            // [INF3] r에는 끼임 틈 목록(pinchGaps·s1Violations)도 들어 있다 — 보고용, verdict·exitCode 무관.
            // [INF3-2] S2 장애물 규칙 위반 목록(s2Violations)도 같다 — 보고용, verdict·exitCode 무관(S1과 같음).
            // [F1 재작업 판정 R2] 음성 대조 — 감사 로직 자체가 실제로 겹침을 잡아내는지 증명한다.
            // SuppressErrorCounting으로 감싸는 이유: 아래 두 검사(RunNegativeControlTest·
            // Map4BuildUnitTests.RunAll)는 "위반 케이스"에서 Debug.LogError가 찍히는 것 자체가
            // 통과 조건이다(예: Stairs 위반 테스트는 '생성 중단' 에러가 나야 성공). 그 의도된
            // 에러를 이 단계의 exitCode 판정용 errorCount에 그대로 합치면, 전부 통과했는데도
            // 배치가 실패로 종료되는 오탐이 생긴다(실측: 이 수정 전 audit이 errorCount=4로
            // Exit(1)했다 — rework_audit2.log/3 대조).
            BeginSuppressErrorCounting();
            bool caught = Map4Audit.RunNegativeControlTest(out string ncDetail);
            string unitReport = Map4BuildUnitTests.RunAll(out bool unitOk);
            int suppressedCount = EndSuppressErrorCounting();

            if (!caught) Debug.LogError("[Map4Batch] 음성 대조 실패 — 정적 감사가 회귀했다: " + ncDetail);
            if (!unitOk) Debug.LogError("[Map4Batch] 빌더 헬퍼 단위 검사에 실패 항목이 있다.");

            // [2차 반려 C-b] "억제 건수 = 기대 건수" 단언 — 위반 케이스 4개(Door·Stairs·Ramp·
            // CheckGap, 각 1건씩 Debug.LogError)만큼만 억제됐어야 한다(WallWithOpenings 위반은
            // Debug.LogWarning이라 애초에 errorCount 대상이 아님). 이 단언 자체는 위 EndSuppress
            // ErrorCounting() **뒤**(suppressDepth=0으로 돌아간 뒤)에 있어, 어긋나면 이 LogError
            // 자체가 억제되지 않고 정상적으로 exitCode에 반영된다 — 단위 검사 구성이 바뀌었는데
            // 이 상수를 안 고치는 회귀를 잡는다.
            // [INF3] 끼임 틈 음성 대조를 더했지만 이 상수는 4 그대로다 — RunGapNegativeControlTest는
            // Debug.LogError를 내지 않고(판정은 반환값) 억제 구간 **밖**에서 부르므로 이 창의 억제 건수에
            // 들어가지 않는다. 억제 구간 밖이라 그 안에서 뜻밖의 에러가 나면 그대로 exitCode에 반영된다.
            const int expectedSuppressedErrors = 4;
            if (suppressedCount != expectedSuppressedErrors)
                Debug.LogError($"[Map4Batch] 억제된 에러 건수 불일치 — 기대 {expectedSuppressedErrors}건, " +
                                $"실제 {suppressedCount}건. Map4BuildUnitTests 위반 케이스 구성이 바뀌었다면 " +
                                "이 상수도 같이 고쳐야 한다(그렇지 않으면 새 회귀를 조용히 못 잡을 수 있다).");

            // [INF3] 끼임 틈 음성 대조 — 0.5U 틈(축정렬·30° 회전), S1 1.5 위반, 1.2U 비검출을 임시 박스로 확인.
            // [INF3 R1] + 가동체(kinematic)가 메운 0.5U 틈은 보고(꼬리표), 정적 박스가 메운 틈은 제외되는지 확인.
            // 이 두 사례도 LogError를 내지 않으므로 expectedSuppressedErrors(4)는 그대로다.
            // [INF3-2] + ⑦ S2 1.5 위반 검출, ⑧ S2 2.5·맞닿음 0 미검출, ⑨ S1↔S2 접두사 섞임 미검출. 이 세 사례도
            // LogError를 내지 않고(판정은 반환값) 억제 구간 밖에서 부르므로 expectedSuppressedErrors(4)는 그대로다
            // (억제 구간 안의 RunNegativeControlTest·Map4BuildUnitTests.RunAll은 이번에 바꾸지 않았다).
            // 실패하면 LogError → errorCount → exit 1(지시서: "틈 음성 대조 실패는 exit 1").
            // 반대로 실제 맵의 끼임 틈·S1 위반·S2 위반 건수는 보고용이라 exitCode에 넣지 않는다(판정은 관통 기준 그대로).
            // [INF3-3] + ⑩ S3 경로 순수 함수, ⑪ S4 이음매(볼록 메시 포함), ⑫ S8 구조물·섞임. 역시 LogError 없이 반환값 판정,
            // 억제 구간 밖 → expectedSuppressedErrors(4) 그대로. 실제 맵의 S3 경로·S4 이음매·S8 구조물 위반은 보고용(exit 무관 —
            // 판정 17의 exit 대상은 관통뿐 [해석], 컨트롤타워 판정 대기).
            bool gapCaught = Map4Audit.RunGapNegativeControlTest(out string gapNcDetail);
            if (!gapCaught) Debug.LogError("[Map4Batch] 끼임 틈 음성 대조 실패 — 틈 검사가 회귀했다: " + gapNcDetail);

            return $"정적 콜라이더 {r.staticColliderCount}개 · 관통 {r.overlapCount}건 {verdict}{detail}\n\n" +
                   $"음성 대조: {(caught ? "✅" : "❌")} — {ncDetail}\n\n" +
                   $"끼임 틈 검사(보고용 — exit code 무관, 끼임 {r.pinchGaps.Count}건 · " +
                   $"S1 장애물 규칙 위반 {r.s1Violations.Count}건 · S2 장애물 규칙 위반 {r.s2Violations.Count}건 · " +
                   $"가동체 관여 {r.movingInvolvedCount}건):\n" +
                   $"{Map4Audit.FormatGapReport(r)}\n\n" +
                   $"끼임 틈 음성 대조: {(gapCaught ? "✅" : "❌")} — {gapNcDetail}\n\n" +
                   $"빌더 헬퍼 단위 검사: {(unitOk ? "✅ 전부 통과" : "❌ 실패 있음")} " +
                   $"(억제된 의도된 에러 {suppressedCount}건, 기대 {expectedSuppressedErrors}건)\n{unitReport}\n\n" +
                   // [INF3-3] 기존 문구 뒤에 덧붙임 — 관통 exit 판정과 R3 추가 규칙 건수(보고용).
                   $"관통 exit 판정 [2차판정 17]: 관통 {r.overlapCount}건 → {(r.overlapCount >= 1 ? "exit 1" : "관통 사유 없음")} · " +
                   $"R3 추가 규칙(보고용 — exit code 무관): S3 경로 위반 {(r.s3Path.violations != null ? r.s3Path.violations.Count : 0)}건 · " +
                   $"S4 이음매 위반 {(r.s4SeamViolations != null ? r.s4SeamViolations.Count : 0)}건(조각 {r.s4CylPieceCount}개) · " +
                   $"S8 구조물 규칙 위반 {(r.s8Violations != null ? r.s8Violations.Count : 0)}건 · 섹터 {(r.sectors != null ? r.sectors.Count : 0)}개 " +
                   "(상세는 위 끼임 틈 검사 끝 'R3 추가 규칙' 절)";
        });
    }

    [MenuItem("Tools/Laboratory Map4/Batch/3. Build Windows EXE")]
    public static void Build()
    {
        RunGuarded("build", () =>
        {
            string outDir = ResolveMapRoot() + "/빌드"; // "빌드"
            Directory.CreateDirectory(outDir);
            string exePath = outDir + "/LaboratoryMap4.exe";

            EditorBuildSettingsScene[] settingsScenes = EditorBuildSettings.scenes;
            string[] scenes = new string[settingsScenes.Length];
            for (int i = 0; i < scenes.Length; i++) scenes[i] = settingsScenes[i].path;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            return $"빌드 결과: {report.summary.result} · 에러 {report.summary.totalErrors} · " +
                   $"경고 {report.summary.totalWarnings} · 크기 {report.summary.totalSize} bytes · 경로 {exePath}";
        });
    }

    /// <summary>0보다 크면 이 구간의 Error/Exception 로그는 콘솔 전문에는 그대로 남되 exitCode
    /// 판정용 errorCount에는 더해지지 않는다 — "위반 케이스가 에러를 내는 것 자체가 성공 조건"인
    /// 자가 검사(음성 대조·빌더 헬퍼 단위 검사) 전용. 중첩 호출을 대비해 카운터로 둔다.
    /// [2차 반려 C-b] EndSuppressErrorCounting()이 "이번 구간에서 억제된 건수"를 돌려줘 호출자가
    /// 기대 건수와 비교(단언)할 수 있게 한다.</summary>
    private static int suppressDepth;
    private static int windowSuppressedCount;
    private static void BeginSuppressErrorCounting()
    {
        suppressDepth++;
        windowSuppressedCount = 0;
    }
    private static int EndSuppressErrorCounting()
    {
        suppressDepth = Mathf.Max(0, suppressDepth - 1);
        return windowSuppressedCount;
    }

    private static void RunGuarded(string stepName, Func<string> action)
    {
        StringBuilder consoleLog = new StringBuilder();
        int errorCount = 0;
        int suppressedErrorCount = 0;
        Application.LogCallback handler = (condition, stacktrace, type) =>
        {
            bool isErrorType = type == LogType.Error || type == LogType.Exception;
            string tag = (isErrorType && suppressDepth > 0) ? "[의도된 검사]" : "";
            consoleLog.AppendLine($"[{type}]{tag} {condition}");
            if (!isErrorType) return;
            if (suppressDepth > 0) { suppressedErrorCount++; windowSuppressedCount++; }
            else errorCount++;
        };
        Application.logMessageReceived += handler;

        string resultLine;
        bool ok = true;
        try
        {
            resultLine = action();
        }
        catch (Exception e)
        {
            ok = false;
            resultLine = "[실패] " + e;
        }
        finally
        {
            Application.logMessageReceived -= handler;
            suppressDepth = 0; // action이 예외로 끝나 End를 못 불렀을 경우를 대비한 안전망.
        }

        string outDir = ResolveMapRoot() + "/검증"; // "검증"
        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, $"F1_{stepName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        string content = $"단계: {stepName}\n실행: {DateTime.Now}\n결과: {resultLine}\n" +
                          $"에러 로그(판정 대상) {errorCount}건 · 의도된 검사 에러(판정 제외) " +
                          $"{suppressedErrorCount}건\n\n── 콘솔 전문 ──\n{consoleLog}";
        File.WriteAllText(path, content, new UTF8Encoding(true));
        Debug.Log($"[Map4Batch] {stepName} 완료 — {path}");

        int exitCode = (ok && errorCount == 0) ? 0 : 1;
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        else Debug.LogWarning($"[Map4Batch] 대화형 실행 — 종료하지 않음(exitCode={exitCode}).");
    }

    private static string ResolveMapRoot()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName; // .../UnityProject
        return Directory.GetParent(projectRoot).FullName;                        // .../맵4_완성
    }
}
#endif
