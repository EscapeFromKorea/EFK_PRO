#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// S8 Exit 마커 한 번 이동 도구 — 소유 INT-2 [계약 R3-INT-3 · 판정 C5 · 1차 판정 6 연장].
///
/// Map4_S8 씬의 &lt;섹터 루트(= Generated의 부모)&gt;/Manual/Markers/Exit Transform 하나를,
/// 로컬 위치가 옛 기본 위치 (0, 0.1, 132)에 정확히(성분별 허용 오차 0.001 [계약 확인 요청 8]) 있을 때만
/// 새 위치 (0, 0.1, 144)로 옮기고 그 씬만 저장한다 [C5]. 사람이 옮긴 마커는 건드리지 않는다
/// (Manual 불가침 [1차 판정 B6·결정3]).
///
/// - 인자 없음. Unity 실행 담당이 명시적으로 한 번 실행한다
///   (`tools/run_unity.ps1 -Method Map4MarkerTools.MoveS8ExitMarker -Tag &lt;새 태그&gt;`, -Quit 없이).
/// - Generate·GenerateAll·빌더·러너 어디에서도 부르지 않는다. 메뉴 항목은 이 메서드 자체다.
/// - 자동 이동 스위치 Map4SceneBuilder.AutoRelocateStaleDefaultExit(false)는 바꾸지 않는다 [C5].
/// - 전제: Data/Map4Layout.asset S8 length = 144(±0.01) [판정 18]. 아니면 ERROR.
///
/// | 현재 localPosition | 동작 | 결과 | exit |
/// | (0,0.1,132) | (0,0.1,144)로 옮긴다(회전·스케일·이름·부모·SectorController 참조 불변) | MOVED | 0 |
/// | (0,0.1,144) | 없음 | ALREADY | 0 |
/// | 그 밖 | 건드리지 않음, LogWarning | SKIP_MANUAL | 3 |
/// | 씬·경로·전제 오류 | 없음, LogError | ERROR | 1 |
///
/// 저장은 MOVED일 때만: Undo.RecordObject → 대입 → EditorSceneManager.MarkSceneDirty →
/// EditorSceneManager.SaveScene(S8 씬만). AssetDatabase.SaveAssets는 부르지 않는다.
/// 기록: 검증/MARKER_&lt;yyyyMMdd_HHmmss&gt;.txt 새 파일(UTF-8 BOM, 같은 이름이 있으면 쓰지 않는다) +
/// 콘솔 마지막 한 줄 `[Map4MarkerTools] 결과 &lt;…&gt; before=(x,y,z) after=(x,y,z) exit=&lt;code&gt;`.
/// 배치 모드면 EditorApplication.Exit(code).
/// </summary>
public static class Map4MarkerTools
{
    private const string LayoutPath = "Assets/LaboratoryMap4/Data/Map4Layout.asset";          // [해석] Map4SceneBuilder.LayoutPath와 같은 값
    private const string S8ScenePath = "Assets/LaboratoryMap4/Scenes/Sectors/Map4_S8.unity";  // [계약 R3-INT-3]
    private const int SectorId = 8;
    private const float ExpectedLength = 144f;   // [판정 18] S8 length
    private const float LengthTol = 0.01f;       // [계약 R3-INT-3] ±0.01
    private const float PosTol = 0.001f;         // [계약 확인 요청 8] 성분별 허용 오차
    private static readonly Vector3 OldDefault = new Vector3(0f, 0.1f, 132f);  // [C5] 09-22 기준 길이 132의 옛 기본 위치
    private static readonly Vector3 NewDefault = new Vector3(0f, 0.1f, 144f);  // [C5] S8 rise 0(floor 34 = exit 34) → y 0.1

    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitSkipManual = 3;

    [MenuItem("Tools/Laboratory Map4/Markers/Move S8 Exit Marker (C5)")]
    public static void MoveS8ExitMarker()
    {
        string result;
        int code;
        string before = "(-)";
        string after = "(-)";
        List<string> notes = new List<string>();

        try
        {
            // 1) 전제 — 에셋 S8 length = 144(±0.01).
            Map4Layout layout = AssetDatabase.LoadAssetAtPath<Map4Layout>(LayoutPath);
            Map4Layout.SectorDef def = layout != null ? layout.GetSector(SectorId) : null;
            if (layout == null || def == null)
            {
                Finish("ERROR", ExitError, before, after,
                       $"전제 오류 — {LayoutPath} 또는 섹터 {SectorId} 정의를 찾지 못했다.", notes);
                return;
            }
            notes.Add($"에셋 S{SectorId} length = {F(def.length)}");
            if (Mathf.Abs(def.length - ExpectedLength) > LengthTol)
            {
                Finish("ERROR", ExitError, before, after,
                       $"전제 오류 — 에셋 S{SectorId} length {F(def.length)} ≠ {F(ExpectedLength)}(±{F(LengthTol)}). INT-2 에셋 변경 뒤에만 실행한다.", notes);
                return;
            }

            // 2) 씬 — 열려 있으면 그대로, 아니면 OpenScene.
            if (!File.Exists(S8ScenePath))
            {
                Finish("ERROR", ExitError, before, after, $"씬 오류 — {S8ScenePath} 파일이 없다.", notes);
                return;
            }
            Scene scene = SceneManager.GetSceneByPath(S8ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // 인터랙티브면 수정된 씬 저장 여부를 Unity 표준 대화상자로 묻는다(Map4SceneGuard). 취소 = ERROR.
                if (!Map4SceneGuard.CanReplaceCurrentScenes())
                {
                    Finish("ERROR", ExitError, before, after, "씬 오류 — 사용자가 현재 씬 교체를 취소했다.", notes);
                    return;
                }
                scene = EditorSceneManager.OpenScene(S8ScenePath, OpenSceneMode.Single);
                notes.Add($"씬 열기(Single): {S8ScenePath}");
            }
            else
            {
                notes.Add($"씬 이미 열려 있음: {S8ScenePath}");
            }
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Finish("ERROR", ExitError, before, after, $"씬 오류 — {S8ScenePath}를 열지 못했다.", notes);
                return;
            }

            // 3) 경로 — <섹터 루트(= Generated의 부모)>/Manual/Markers/Exit가 정확히 1개.
            List<Transform> exits = new List<Transform>();
            int sectorRoots = 0;
            foreach (GameObject rootGo in scene.GetRootGameObjects())
            {
                if (rootGo.transform.Find("Generated") == null) continue;
                sectorRoots++;
                foreach (Transform manual in ChildrenNamed(rootGo.transform, "Manual"))
                    foreach (Transform markers in ChildrenNamed(manual, "Markers"))
                        exits.AddRange(ChildrenNamed(markers, "Exit"));
            }
            notes.Add($"섹터 루트(Generated 부모) {sectorRoots}개 · Manual/Markers/Exit {exits.Count}개");
            if (exits.Count != 1)
            {
                Finish("ERROR", ExitError, before, after,
                       $"경로 오류 — <섹터 루트>/Manual/Markers/Exit가 {exits.Count}개다(정확히 1개여야 한다). 변경하지 않았다.", notes);
                return;
            }

            Transform exit = exits[0];
            Vector3 cur = exit.localPosition;
            before = V(cur);
            notes.Add($"대상: {PathOf(exit)}");

            // 4) 조건과 동작.
            if (Near(cur, OldDefault))
            {
                Undo.RecordObject(exit, "Map4MarkerTools.MoveS8ExitMarker");
                exit.localPosition = NewDefault;
                after = V(exit.localPosition);
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                if (!saved)
                {
                    Finish("ERROR", ExitError, before, after,
                           $"저장 오류 — 메모리에서는 옮겼으나 {S8ScenePath} 저장에 실패했다.", notes);
                    return;
                }
                result = "MOVED";
                code = ExitOk;
                notes.Add($"옛 기본 위치 {V(OldDefault)} → {V(NewDefault)}로 옮기고 {S8ScenePath}만 저장했다.");
            }
            else if (Near(cur, NewDefault))
            {
                after = before;
                result = "ALREADY";
                code = ExitOk;
                notes.Add("이미 새 위치 — 아무것도 하지 않았다(저장 안 함).");
            }
            else
            {
                after = before;
                result = "SKIP_MANUAL";
                code = ExitSkipManual;
                notes.Add("옛 기본 위치도 새 위치도 아니다 — 사람이 옮긴 마커로 보고 건드리지 않았다(Manual 불가침, 저장 안 함).");
            }
        }
        catch (System.Exception e)
        {
            Finish("ERROR", ExitError, before, after, "예외 — " + e, notes);
            return;
        }

        Finish(result, code, before, after, null, notes);
    }

    // ───────────────────────── 보조 ─────────────────────────

    private static void Finish(string result, int code, string before, string after, string error, List<string> notes)
    {
        string summary = $"[Map4MarkerTools] 결과 {result} before={before} after={after} exit={code}";

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Map4MarkerTools.MoveS8ExitMarker — S8 Exit 마커 이동 기록 [계약 R3-INT-3 · 판정 C5]");
        sb.AppendLine("실행: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine($"씬: {S8ScenePath}");
        sb.AppendLine($"조건: 옛 기본 {V(OldDefault)} → 새 {V(NewDefault)}, 성분별 허용 오차 {F(PosTol)}");
        if (error != null) sb.AppendLine("오류: " + error);
        foreach (string n in notes) sb.AppendLine("- " + n);
        sb.AppendLine(summary);

        string recordPath = null;
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName; // .../UnityProject
            string mapRoot = Directory.GetParent(projectRoot).FullName;               // .../맵4_완성
            string outDir = Path.Combine(mapRoot, "검증");
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, $"MARKER_{System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.txt");
            if (File.Exists(path))
            {
                Debug.LogWarning($"[Map4MarkerTools] 기록 파일 {path}가 이미 있어 쓰지 않았다(검증/ 덮어쓰기 금지).");
            }
            else
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                recordPath = path;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Map4MarkerTools] 기록 파일 쓰기 실패 — " + e.Message);
        }
        if (recordPath != null) Debug.Log("[Map4MarkerTools] 기록: " + recordPath);
        if (error != null) Debug.Log("[Map4MarkerTools] 오류 상세: " + error);

        // 콘솔 마지막 요약 한 줄.
        if (result == "ERROR") Debug.LogError(summary);
        else if (result == "SKIP_MANUAL") Debug.LogWarning(summary);
        else Debug.Log(summary);

        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    private static IEnumerable<Transform> ChildrenNamed(Transform parent, string name)
    {
        List<Transform> list = new List<Transform>();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == name) list.Add(c);
        }
        return list;
    }

    private static bool Near(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) <= PosTol && Mathf.Abs(a.y - b.y) <= PosTol && Mathf.Abs(a.z - b.z) <= PosTol;
    }

    private static string F(float v)
    {
        return v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string V(Vector3 v)
    {
        return $"({F(v.x)},{F(v.y)},{F(v.z)})";
    }

    private static string PathOf(Transform t)
    {
        string p = t.name;
        for (Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
        return p;
    }
}
#endif
