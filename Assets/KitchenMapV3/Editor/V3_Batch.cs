#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// KitchenMapV3 — 무인(batchmode) 검증 진입점. 컨트롤타워가 유니티 자체를 직접 열지 않고도
/// 빌드 결과를 확인할 수 있게 하는 것이 목적(2026-09-05 사용자 요청).
///
/// 사용법(명령행, -nographics 없이 — PNG 렌더가 화면 버퍼를 쓴다):
///   Unity.exe -batchmode -projectPath KitchenMapV3 -executeMethod V3Batch.RunAll
///             -logFile &lt;log&gt; -quit [-v3out &lt;출력폴더&gt;]
///
/// 절차: 기존 메뉴(V3Menu)의 Clear → BuildAll → SetupPlay → Audit를 그대로 호출한다(로직 복제
/// 금지 — 공통규칙과 동일한 배치 결과를 보장하기 위함). 각 단계는 개별 try/catch로 감싸 하나가
/// 실패해도 이어지는 단계를 계속 시도하고, 실패 사실은 요약 파일에 남는다.
///
/// 철칙 6(씬 파일 직접 수정 금지) 준수: 이 스크립트는 SaveScene·SaveOpenScenes를 어디서도
/// 호출하지 않는다 — 씬은 저장하지 않고 종료한다.
/// </summary>
public static class V3Batch
{
    private const int ShotWidth = 1600;
    private const int ShotHeight = 1200;

    public static void RunAll()
    {
        StringBuilder consoleLog = new StringBuilder();
        int errorCount = 0;

        // 1) 로그 캡처 시작 — 이후 모든 단계의 Log/Warning/Error/Exception을 모은다.
        Application.LogCallback handler = (condition, stacktrace, type) =>
        {
            consoleLog.AppendLine($"[{type}] {condition}");
            if (!string.IsNullOrEmpty(stacktrace) && (type == LogType.Error || type == LogType.Exception))
                consoleLog.AppendLine(stacktrace);
            if (type == LogType.Error || type == LogType.Exception)
                errorCount++;
        };
        Application.logMessageReceived += handler;

        StringBuilder summary = new StringBuilder();
        summary.AppendLine("══════════ KitchenMapV3 무인 검증 요약 (V3Batch.RunAll) ══════════");
        summary.AppendLine("실행 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        // 2) 출력 폴더 — 전체를 try로 감싼다(A3: 인자 파싱·경로 정규화 중 예외가 나도
        // 기본 폴더로 폴백해 RunAll 전체가 죽지 않게 한다).
        string outDirSource, outDirWarning;
        string outDir;
        try
        {
            outDir = ResolveOutDir(out outDirSource, out outDirWarning);
        }
        catch (Exception e)
        {
            outDirSource = "기본값(ResolveOutDir 예외 폴백)";
            outDirWarning = "[경고] ResolveOutDir 예외 — 기본 폴더로 폴백: " + e;
            // [정정 2026-09-10, R4] 예외 폴백도 정식 기본값과 동일하게 프로젝트 루트 하위
            // V3_Reports를 쓴다(위 ResolveOutDir의 defaultDir과 같은 경로 계산).
            outDir = Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "V3_Reports"));
        }
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception e)
        {
            summary.AppendLine("[치명] 출력 폴더 생성 실패(" + outDir + "): " + e);
        }
        summary.AppendLine($"출력 폴더: {outDir} (출처: {outDirSource})");
        if (!string.IsNullOrEmpty(outDirWarning)) summary.AppendLine(outDirWarning);

        // 3) 기존 메뉴 정적 메서드를 순서대로 호출 — 로직 복제 금지.
        // "1. Clear"는 여기서 1회 호출하지만 "2. BuildAll" 내부에서도 자체적으로 Clear를 다시
        // 호출한다(V3Menu.BuildAll 구현) — 콘솔에 "삭제 완료"가 2회 찍히는 것은 버그가 아니라
        // 정상 동작이다(A4).
        summary.AppendLine();
        summary.AppendLine("── 단계별 실행 ──");
        int stepFailCount = 0;
        if (!RunStep(summary, "1. Clear", () => V3Menu.Clear())) stepFailCount++;
        if (!RunStep(summary, "2. BuildAll", () => V3Menu.BuildAll())) stepFailCount++;
        if (!RunStep(summary, "3. SetupPlay", () => V3Menu.SetupPlay())) stepFailCount++;
        if (!RunStep(summary, "3b. Physics.SyncTransforms", () => Physics.SyncTransforms())) stepFailCount++;
        if (!RunStep(summary, "4. Audit", () => V3Menu.Audit())) stepFailCount++;

        // 3c) Audit 판정 파싱 — V3Audit.Run()의 Debug.Log가 이미 consoleLog에 잡혀 있다.
        // 2026-09-06 [결정] 6(공통규칙 §3 개정) 반영: 판정 대상이 ①(크로스그룹 정적 관통)
        // 하나에서 ①+②(비kinematic Rigidbody 겹침)로 늘었다 — "겹침 감사" 줄에서 두 건수를
        // 각각 Regex로 읽어 합산한 값으로 실패 여부를 정한다(줄의 "❌" 문자와도 이중 대조).
        // R9(2026-09-05 검문) 유지: 줄 자체를 못 찾으면(auditFound==false) ParseAuditVerdict
        // 내부에서 failed=true로 잡아 실패로 계수한다 — exitCode에 +1(아래 참고).
        int crossCount, movingCount; bool auditFound, auditFailed;
        ParseAuditVerdict(consoleLog.ToString(), out crossCount, out movingCount, out auditFound, out auditFailed);

        // 컨트롤타워 판정(2026-09-05 검문 반영, 09-06 라벨 갱신): 종료코드는 판정 대상
        // (①+②)만 반영하되, 판정 줄에는 같은 그룹·트리거·kinematic 가동 프롭 건수도
        // 병기한다 — V3_Audit.cs OverlapAudit의 참고 집계 줄을 Regex로 읽는다. 이 셋은
        // 참고용 — exitCode에는 영향 없음.
        string logSnapshot = consoleLog.ToString();
        int sameGroupCount, triggerCount, rigidbodyCount;
        bool sameGroupFound = ParseCategoryCount(logSnapshot, "같은 그룹 내 겹침", out sameGroupCount);
        bool triggerFound = ParseCategoryCount(logSnapshot, "트리거 볼륨 겹침", out triggerCount);
        bool rigidbodyFound = ParseCategoryCount(logSnapshot, "kinematic 가동 프롭 겹침", out rigidbodyCount);

        // 4) 요약 수집
        // 부수 실패 카운터(2026-09-05 17차 재검 반영) — CollectSceneSummary·CaptureShots
        // 전체 실패·CaptureOne 개별 PNG 실패를 하나로 합산해 exitCode·판정 줄에 반영한다.
        // stepFailCount(단계 실패)와는 별개 — 이쪽은 "부수적"(요약/PNG) 실패다.
        int auxFailCount = 0;
        try
        {
            CollectSceneSummary(summary);
        }
        catch (Exception e)
        {
            summary.AppendLine("[요약 수집 단계 실패] " + e);
            auxFailCount++;
        }

        // 5) PNG 캡처 4장 — 실패해도 계속(각 샷 자체 try/catch, CaptureShots도 한 번 더 감쌈).
        try
        {
            CaptureShots(outDir, summary, ref auxFailCount);
        }
        catch (Exception e)
        {
            summary.AppendLine("[PNG 캡처 단계 전체 실패] " + e);
            auxFailCount++;
        }

        summary.AppendLine();
        summary.AppendLine($"Error/Exception 로그 건수: {errorCount}");

        // topLine(감사 판정 줄) — R1(2026-09-05 검문): 지금 위치(파일 저장 전)를 유지하되,
        // 저장 성패에 의존하는 값은 담지 않는다(저장 성패는 이 줄이 파일에 쓰인 뒤에야 알 수
        // 있다 — 자기 자신의 저장 결과를 자기 내용에 미리 담을 수 없다). 저장 실패 수는 아래
        // 6)에서 파일 맨 끝에 별도 항목 "저장 실패 n"으로 덧붙인다.
        string topLine;
        if (!auditFound)
        {
            topLine = $"감사 판정: Audit 판정 줄 없음 ❌ · 단계 실패 {stepFailCount} · 에러 로그 {errorCount} · 부수 실패 {auxFailCount}";
        }
        else
        {
            string sameGroupText = sameGroupFound ? sameGroupCount.ToString() : "?";
            string triggerText = triggerFound ? triggerCount.ToString() : "?";
            string rigidbodyText = rigidbodyFound ? rigidbodyCount.ToString() : "?";
            // 2026-09-06 [결정] 6: 판정 대상 두 건수를 각각 병기(①+② 모두 ❌ 계수 대상).
            topLine = $"감사 판정: 정적 관통 {crossCount} · 가동체 {movingCount} {(auditFailed ? "❌" : "✅")} · 같은 그룹 {sameGroupText} · 트리거 {triggerText} · kinematic 가동 프롭 {rigidbodyText} · 단계 실패 {stepFailCount} · 에러 로그 {errorCount} · 부수 실패 {auxFailCount}";
        }
        string finalSummaryText = topLine + Environment.NewLine + summary.ToString();

        // 6) 결과 파일 저장. 저장 자체에서 예외가 나면 saveErrors에 모아 요약 파일에도
        // 이어붙인다(A2 — "파일 저장 예외는 요약에 기록").
        string summaryPath = Path.Combine(outDir, "무인검증_요약.txt");
        string consoleLogPath = Path.Combine(outDir, "콘솔_전문.txt");
        List<string> saveErrors = new List<string>();
        try
        {
            File.WriteAllText(summaryPath, finalSummaryText, new UTF8Encoding(true));
        }
        catch (Exception e)
        {
            string msg = "[V3Batch] 요약 파일 저장 실패: " + e;
            saveErrors.Add(msg);
            Debug.LogError(msg);
        }
        try
        {
            File.WriteAllText(consoleLogPath, consoleLog.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e)
        {
            string msg = "[V3Batch] 콘솔 전문 저장 실패: " + e;
            saveErrors.Add(msg);
            Debug.LogError(msg);
        }
        // R1(2026-09-05 검문): 저장 실패 수를 요약 "끝"에 별도 항목으로 항상 덧붙인다(0건이어도
        // 항목 자체는 남겨 확인 가능하게 함). 요약 파일이 이미 쓰였으므로 append로만 가능하다 —
        // 최선 노력(출력 폴더 자체가 불능이면 이 재시도도 실패할 수 있고, 그때는 위의
        // Debug.LogError만 콘솔_전문.txt에 남는다).
        try
        {
            string tail = Environment.NewLine + $"저장 실패 {saveErrors.Count}";
            if (saveErrors.Count > 0)
            {
                tail += Environment.NewLine + "[파일 저장 예외]" + Environment.NewLine + string.Join(Environment.NewLine, saveErrors);
            }
            File.AppendAllText(summaryPath, tail, new UTF8Encoding(true));
        }
        catch { /* 출력 폴더 자체가 불능 — 더 손쓸 수 없다. */ }

        // R1(2026-09-05 검문): 종료 코드는 파일 저장 결과(saveErrors)까지 합산한 뒤 — 즉 여기,
        // 파일 저장이 모두 끝난 뒤 — 계산한다(기존엔 저장 전에 계산해 saveErrors가 종료코드에
        // 전혀 반영되지 않았다). 17차 재검 반영: 부수 실패(auxFailCount — 씬 요약 수집·PNG
        // 캡처 실패)도 합산한다. = (에러/예외 로그 수 + 단계 실패 수 + Audit ❌ 여부 + 저장
        // 실패 수 + 부수 실패 수) > 0 이면 1.
        int exitCode = (errorCount + stepFailCount + (auditFailed ? 1 : 0) + saveErrors.Count + auxFailCount) > 0 ? 1 : 0;

        // 7) 로그 캡처 종료 — 결과 파일·PNG 저장이 모두 끝난 뒤에 해제한다(A2: 요약 저장 실패는
        // 콘솔 전문에 담긴다(콘솔 파일 자체 저장 실패는 원리상 불가)).
        Application.logMessageReceived -= handler;

        // 8) 종료 — 철칙 6: 씬 저장 호출 없음(SaveScene·SaveOpenScenes 미사용).
        //    -quit와 충돌하지 않도록 Exit는 가장 마지막에 호출한다.
        // [정정 2026-09-10, 컨트롤타워 지시 A7] Editor/V3_PhysLab.cs가 2026-09-09에 이미 겪은 것과
        // 같은 위험 — RunAll은 [MenuItem]이 없어 정상 경로는 항상 -batchmode -executeMethod지만,
        // 이 메서드는 여전히 public static이라 팀원이 -batchmode 없이 -executeMethod V3Batch.RunAll만
        // 걸거나 커스텀 에디터 버튼에서 직접 호출하면 대화형 에디터가 예고 없이 통째로 꺼진다
        // (V3_PhysLab.cs의 "2026-09-09 게이트 추가" 주석·12:15 실측 참고, 같은 사고 유형). 배치모드가
        // 아니면 종료하지 않고 로그만 남긴다.
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
        else
        {
            Debug.LogWarning($"[V3Batch] 대화형 실행(비-배치모드) — 에디터를 끄지 않습니다. exitCode={exitCode} · 출력 폴더: {outDir}");
        }
    }

    /// <summary>캡처된 콘솔 로그 전문에서 "겹침 감사" 줄을 찾아 판정한다(V3_Audit.cs
    /// OverlapAudit의 출력 형식에 결합 — 문자열 검색만, 별도 API 호출 없음). 2026-09-06
    /// [결정] 6(공통규칙 §3 개정)으로 판정 대상이 ①(정적 관통)·②(가동체 겹침) 둘로 늘어
    /// 각각 Regex로 읽어 crossCount·movingCount에 담고, 그 합으로 실패 여부를 판정한다
    /// (줄의 "❌" 문자와 이중 대조 — 어느 한쪽이라도 실패를 가리키면 failed=true).</summary>
    private static void ParseAuditVerdict(string logText, out int crossCount, out int movingCount, out bool found, out bool failed)
    {
        found = false;
        failed = false;
        crossCount = 0;
        movingCount = 0;
        int idx = logText.IndexOf("겹침 감사", StringComparison.Ordinal);
        if (idx < 0)
        {
            // R9(2026-09-05 검문): "겹침 감사" 줄 자체를 못 찾으면 실패로 계수한다
            // (failed=true → RunAll의 exitCode에 +1). 이전엔 found=false여도 failed=false로
            // 남아 이 항목만으로는 종료코드에 기여하지 않았다.
            failed = true;
            return;
        }
        found = true;

        int lineStart = logText.LastIndexOf('\n', idx);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = logText.IndexOf('\n', idx);
        if (lineEnd < 0) lineEnd = logText.Length;
        string line = logText.Substring(lineStart, lineEnd - lineStart);

        Match mCross = Regex.Match(line, "①\\s*정적\\s*관통\\s*(\\d+)건");
        if (mCross.Success) int.TryParse(mCross.Groups[1].Value, out crossCount);
        Match mMoving = Regex.Match(line, "②\\s*가동체\\s*겹침\\s*(\\d+)건");
        if (mMoving.Success) int.TryParse(mMoving.Groups[1].Value, out movingCount);
        // 합산(①+②)이 exit code 판정의 1차 근거 — 줄의 "❌" 문자는 V3_Audit.cs가 같은
        // 합산으로 이미 계산해 찍은 것이므로 정상 동작 시 항상 일치하지만, 파싱이 어떤
        // 이유로든 빗나가는 경우를 대비해 OR로 이중 안전장치를 둔다.
        failed = (crossCount + movingCount) > 0 || line.Contains("❌");
    }

    /// <summary>컨트롤타워 판정(2026-09-05 검문): Audit 출력의 ②③④ 집계 줄("같은 그룹 내
    /// 겹침 a건" 등)에서 건수만 Regex로 읽는다 — 참고용, exitCode에는 반영하지 않는다. 줄을
    /// 못 찾으면 found=false를 반환한다(RunAll이 topLine에 "?"로 표시).</summary>
    private static bool ParseCategoryCount(string logText, string label, out int count)
    {
        count = 0;
        Match m = Regex.Match(logText, Regex.Escape(label) + @"\s*(\d+)건");
        if (!m.Success) return false;
        int.TryParse(m.Groups[1].Value, out count);
        return true;
    }

    // ── 출력 폴더 결정 ───────────────────────────────────────────
    /// <summary>[정정 2026-09-10, 컨트롤타워 지시 R4] 기본값을 axiom 루트 밖(클론마다 존재
    /// 여부가 다른 "맵2_V3_릴레이설계" 폴더) 대신 이 유니티 프로젝트 안 "&lt;프로젝트 루트&gt;/
    /// V3_Reports"로 바꿨다(Assets 밖이라 임포트되지 않는다 — 36차 R4: 팀원 클론엔 axiom
    /// 형제 폴더가 없을 수 있다). V3_REPORT_DIR 환경변수가 있으면 -v3out보다도 우선한다
    /// (컨트롤타워가 무인검증 폴더를 지정하는 용도 — Editor/V3_PhysLab.cs·
    /// Scripts/V3_GateTestRunner.cs와 동일 규칙; 환경변수는 axiom 루트 제한을 걸지 않는다).
    /// 그 다음 -v3out 인자가 있으면 그 경로(절대경로 정규화 후, axiom 루트 하위이고
    /// "efk_pro"·"develop_snapshot"을 포함하지 않을 때만 채택 — A3: 컨트롤타워가 실수로
    /// 원본 저장소나 스냅샷 폴더를 가리켜도 그리로 쓰지 않는다) 이 기존대로 유지된다. 셋 다
    /// 없으면 기본값을 쓰고 warning에 사유를 남긴다.</summary>
    private static string ResolveOutDir(out string source, out string warning)
    {
        warning = null;

        // Application.dataPath = <projectPath>/Assets → 1단계 상위 = <projectPath>(KitchenMapV3) =
        // 프로젝트 루트(신규 기본값의 기준), 2단계 상위 = axiom 루트(-v3out 안전검사 전용).
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string axiomRoot = Path.GetFullPath(Directory.GetParent(projectRoot).FullName);
        string defaultDir = Path.GetFullPath(Path.Combine(projectRoot, "V3_Reports"));

        string envOverride = Environment.GetEnvironmentVariable("V3_REPORT_DIR");
        if (!string.IsNullOrEmpty(envOverride))
        {
            source = "V3_REPORT_DIR 환경변수";
            return Path.GetFullPath(envOverride);
        }

        string axiomRootWithSep = axiomRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "-v3out") continue;

            if (i + 1 >= args.Length)
            {
                // R8(2026-09-05 검문): "-v3out"가 마지막 인자(값 없음) — 이전엔 루프 상한이
                // args.Length-1이라 이 경우 자체를 검사하지 않고 그냥 지나쳐 "-v3out 미지정"과
                // 구분 없이 기본 폴더로 갔다. 이제 명시적으로 감지해 경고를 남긴다.
                warning = "[경고] -v3out 뒤에 경로 값이 없음(마지막 인자) — 기본 폴더 사용";
                source = "기본값(-v3out 값 없음)";
                return defaultDir;
            }

            string candidate = Path.GetFullPath(args[i + 1]);
            bool underAxiomRoot = candidate.StartsWith(axiomRootWithSep, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                  axiomRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                  StringComparison.OrdinalIgnoreCase);
            bool forbidden = candidate.IndexOf("efk_pro", StringComparison.OrdinalIgnoreCase) >= 0
                || candidate.IndexOf("develop_snapshot", StringComparison.OrdinalIgnoreCase) >= 0;

            if (underAxiomRoot && !forbidden)
            {
                source = "-v3out 인자";
                return candidate;
            }

            warning = $"[경고] -v3out 경로 거부(axiom 루트 밖이거나 금지 폴더 포함): {candidate} → 기본 폴더 사용";
            source = "기본값(-v3out 거부됨)";
            return defaultDir;
        }

        source = "기본값(-v3out 미지정)";
        return defaultDir;
    }

    private static bool RunStep(StringBuilder summary, string label, Action action)
    {
        try
        {
            action();
            summary.AppendLine($"  [성공] {label}");
            return true;
        }
        catch (Exception e)
        {
            summary.AppendLine($"  [실패] {label}: {e}");
            return false;
        }
    }

    // ── 요약 수집 ────────────────────────────────────────────────
    private static void CollectSceneSummary(StringBuilder summary)
    {
        summary.AppendLine();
        summary.AppendLine("── 씬 요약 ──");

        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        summary.AppendLine($"씬 루트 오브젝트 수: {roots.Length}");

        GameObject[] allGos = UnityEngine.Object.FindObjectsOfType<GameObject>();
        summary.AppendLine($"전체 GameObject 수: {allGos.Length}");

        Collider[] colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
        summary.AppendLine($"Collider 수: {colliders.Length}");

        Rigidbody[] rigidbodies = UnityEngine.Object.FindObjectsOfType<Rigidbody>();
        summary.AppendLine($"Rigidbody 수: {rigidbodies.Length}");

        summary.AppendLine();
        summary.AppendLine("── T0RS_ 오브젝트 (이름·위치) ──");
        var t0rs = allGos.Where(g => g.name.StartsWith("T0RS_")).OrderBy(g => g.name).ToList();
        foreach (GameObject g in t0rs)
            summary.AppendLine($"  {g.name} @ {g.transform.position:F2}");
        summary.AppendLine($"  (총 {t0rs.Count}개)");

        summary.AppendLine();
        summary.AppendLine("── \"실게이트3.0_마커\" 포함 오브젝트 (위치·스케일) ──");
        var gates = allGos.Where(g => g.name.Contains("실게이트3.0_마커")).OrderBy(g => g.name).ToList();
        foreach (GameObject g in gates)
            summary.AppendLine($"  {g.name} @ pos={g.transform.position:F2} scale={g.transform.localScale:F2}");
        summary.AppendLine($"  (총 {gates.Count}개)");

        summary.AppendLine();
        int dressCount = allGos.Count(g => g.name.StartsWith("DRESS_"));
        summary.AppendLine($"DRESS_ 접두 오브젝트 수: {dressCount}");

        summary.AppendLine();
        summary.AppendLine("── V3ThirdPersonCamera 인스턴스 ──");
        V3ThirdPersonCamera cam = UnityEngine.Object.FindObjectOfType<V3ThirdPersonCamera>();
        if (cam == null)
        {
            summary.AppendLine("  없음(SetupPlay 실패 가능성 — 위 단계별 실행 로그 확인).");
        }
        else
        {
            summary.AppendLine($"  yardCameraLimit = {cam.yardCameraLimit}");
            summary.AppendLine($"  yardMaxDistance = {GetFloatField(cam, "yardMaxDistance")}");
            summary.AppendLine($"  collisionPull   = {GetFloatField(cam, "collisionPull")}");
        }
    }

    /// <summary>비공개 필드도 포함해 읽는다 — yardMaxDistance·collisionPull은 private
    /// [SerializeField]라 리플렉션이 필요하다(V3ThirdPersonCamera.cs 참고).</summary>
    private static string GetFloatField(object obj, string fieldName)
    {
        FieldInfo fi = obj.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fi == null) return "(필드를 찾지 못함: " + fieldName + ")";
        object value = fi.GetValue(obj);
        return value == null ? "(null)" : value.ToString();
    }

    // ── PNG 캡처 ─────────────────────────────────────────────────
    private static void CaptureShots(string outDir, StringBuilder summary, ref int auxFailCount)
    {
        summary.AppendLine();
        summary.AppendLine("── PNG 캡처 (4장) ──");

        // a·b(TextMesh 순번 표시용 상단뷰)가 라이팅 각도에 좌우되지 않도록 임시로 flat ambient를
        // 올린다 — 지시 5의 "Unlit 기본 머티리얼이면 그대로, 아니면 ambient만" 처리. 캡처가 끝나면
        // 원래 씬의 ambient 설정으로 되돌린다(부작용 없이 렌더만 보조).
        AmbientMode prevAmbientMode = RenderSettings.ambientMode;
        Color prevAmbientColor = RenderSettings.ambientLight;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
        try
        {
            // a. 전체 평면도 — 지시 좌표 그대로: 맵 중심 위 수직 하향, 직교 60.
            CaptureOne(outDir, summary, "shot_a_전체평면.png",
                V3.Doc(49f, 55f, 220f), Quaternion.Euler(90f, 0f, 0f),
                ortho: true, orthoSize: 60f, fov: 60f, auxFailCount: ref auxFailCount);

            // b. 뒷마당 P5 평면도 — 지시 좌표 그대로: 수직 하향, 직교 15.
            CaptureOne(outDir, summary, "shot_b_P5뒷마당평면.png",
                V3.Doc(49f, 98f, 80f), Quaternion.Euler(90f, 0f, 0f),
                ortho: true, orthoSize: 15f, fov: 60f, auxFailCount: ref auxFailCount);

            // c. 서랍장 계단 측면 — 지시 원안(X22,Y-30,Z8)은 계단이 원점 근처라는 가정으로 보이나,
            // 실좌표는 S2_BoxSteps(V3_Build.cs L155~161): x10.4~34.4·y70.5~74.4·z0~9.6(10단×0.96)다.
            // 원안 그대로면 계단(y70.5 부근)까지 약 100U 떨어져 FOV60에서 점처럼 작게 나온다.
            // "+Y 방향으로 계단을 옆에서 본다"는 의도는 유지하고 위치만 실좌표 남쪽으로 당겼다:
            // X=22.4(계단 x범위 10.4~34.4의 중앙), Y=47.45(계단 시작 y70.5보다 25U 남쪽 — FOV60·
            // 종횡비4:3에서 폭 24U+여유를 담는 최소거리 근사(약 19.5U)에 여유를 더한 값),
            // Z=4.8(전체 높이 9.6의 중앙). 모든 Box_Step이 같은 y70.5~74.4를 공유하므로 이 축을
            // 정면으로 보면 계단들이 겹치지 않는 평면 실루엣으로 잡힌다(원근 카메라, FOV60 유지).
            Vector3 stairEye = V3.Doc(22.4f, 47.45f, 4.8f);
            Vector3 stairLook = V3.Doc(22.4f, 72.45f, 4.8f);
            CaptureOne(outDir, summary, "shot_c_서랍장계단측면.png",
                stairEye, Quaternion.LookRotation((stairLook - stairEye).normalized, Vector3.up),
                ortho: false, orthoSize: 0f, fov: 60f, auxFailCount: ref auxFailCount);

            // d. 3인칭 시점 예시 — 지시 좌표 그대로.
            Vector3 tpEye = V3.Doc(82f, 89f, 4.9f);
            Vector3 tpLook = V3.Doc(82f, 97.25f, 2.0f);
            CaptureOne(outDir, summary, "shot_d_3인칭예시.png",
                tpEye, Quaternion.LookRotation((tpLook - tpEye).normalized, Vector3.up),
                ortho: false, orthoSize: 0f, fov: 65f, auxFailCount: ref auxFailCount);
        }
        finally
        {
            RenderSettings.ambientMode = prevAmbientMode;
            RenderSettings.ambientLight = prevAmbientColor;
        }
    }

    /// <summary>임시 카메라 1개를 만들어 RenderTexture로 렌더 → PNG 저장 → 카메라·텍스처 파괴.
    /// 실패해도 예외를 삼키고 요약에 기록만 한다(지시 5: "실패해도 계속").</summary>
    private static void CaptureOne(string outDir, StringBuilder summary, string fileName,
        Vector3 position, Quaternion rotation, bool ortho, float orthoSize, float fov, ref int auxFailCount)
    {
        GameObject camGo = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        try
        {
            camGo = new GameObject("V3Batch_TempCam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.transform.position = position;
            cam.transform.rotation = rotation;
            cam.orthographic = ortho;
            if (ortho) cam.orthographicSize = orthoSize;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.17f);

            rt = new RenderTexture(ShotWidth, ShotHeight, 24);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            byte[] png = tex.EncodeToPNG();
            string path = Path.Combine(outDir, fileName);
            File.WriteAllBytes(path, png);
            summary.AppendLine($"  [성공] {fileName}");
        }
        catch (Exception e)
        {
            summary.AppendLine($"  [실패] {fileName}: {e.Message}");
            auxFailCount++;
        }
        finally
        {
            if (camGo != null)
            {
                Camera cam = camGo.GetComponent<Camera>();
                if (cam != null) cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(camGo);
            }
            if (rt != null)
            {
                RenderTexture.active = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
#endif
