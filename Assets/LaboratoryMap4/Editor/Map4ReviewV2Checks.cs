#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>N04 맵4 후속 PlayMode 검사의 공통 실행기. 공개 API·물리 반응을 자동 점검하며
/// 실제 키보드 조작, 카메라 경험, 사람이 하는 전체 완주를 보증하지 않는다.
/// Run(기본 5,8,flow)은 한 Play 세션을 공유한다. 상태 격리가 필요한 최종 판정에는
/// RunAxe/RunCh8/RunFlow/RunPresentation을 각각 별도 Unity 실행으로 호출한다.</summary>
public static class Map4ReviewV2Checks
{
    private const string Tag = "[Map4ReviewV2Checks]";
    private const string MasterScene = "Assets/LaboratoryMap4/Scenes/Map4_Master.unity";
    private const string DefaultSections = "5,8,flow";
    private const string PendingKey = "Map4ReviewV2_Pending_20261103";
    private const string ActiveKey = "Map4ReviewV2_Active_20261103";
    private const string SelectionKey = "Map4ReviewV2_Selection_20261103";
    private const string DeadlineKey = "Map4ReviewV2_Deadline_20261103";
    private const float WatchdogSeconds = 900f;

    [MenuItem("Tools/Laboratory Map4/Followup/Run N04 Review V2")]
    public static void Run()
    {
        try { StartRun(ParseSelection()); }
        catch (Exception e) { Fallback(DefaultSections, "섹션 선택 실패: " + e); }
    }

    // 각각 별도 -executeMethod로 실행하면 Master와 기믹 상태가 매번 새 Play 세션에서 시작된다.
    public static void RunAxe() => StartRun("5");
    public static void RunCh8() => StartRun("8");
    public static void RunFlow() => StartRun("flow");
    public static void RunPresentation() => StartRun("present");
    public static void RunPressure() => StartRun("pressure");
    public static void RunPortal() => StartRun("portal");
    public static void RunPortalSouth() => StartRun("portalsouth");
    public static void RunPortalCubeBrake() => StartRun("portalcubebrake");
    public static void RunPortalSphere() => StartRun("portalsphere");
    public static void RunPortalSphereBrake() => StartRun("portalspherebrake");
    public static void RunPortalTetra() => StartRun("portaltetra");
    public static void RunCh8Sphere() => StartRun("8sphere");
    public static void RunCh8Cube() => StartRun("8cube");

    private static string ParseSelection()
    {
        string raw = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], "-reviewSections", StringComparison.OrdinalIgnoreCase)) raw = args[i + 1];
        if (string.IsNullOrWhiteSpace(raw)) raw = DefaultSections;
        List<string> selected = new List<string>();
        foreach (string token in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string id = token.Trim().ToLowerInvariant();
            if (id != "5" && id != "8" && id != "8sphere" && id != "8cube" && id != "flow" && id != "present" && id != "pressure" && id != "portal" && id != "portalsouth" && id != "portalcubebrake" && id != "portalsphere" && id != "portalspherebrake" && id != "portaltetra")
                throw new ArgumentException("지원하지 않는 -reviewSections 값: " + token);
            if (!selected.Contains(id)) selected.Add(id);
        }
        if (selected.Count == 0) throw new ArgumentException("-reviewSections 선택이 비었다.");
        return string.Join(",", selected);
    }

    private static void StartRun(string selection)
    {
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("이미 플레이 모드이다.");
            if (!Map4SceneGuard.CanReplaceCurrentScenes()) throw new InvalidOperationException("씬 저장/전환이 취소됐다.");
            EditorSceneManager.OpenScene(MasterScene, OpenSceneMode.Single);
            SessionState.SetString(SelectionKey, selection);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetFloat(DeadlineKey, Time.realtimeSinceStartup + WatchdogSeconds);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            SessionState.SetBool(PendingKey, false);
            Fallback(selection, "시작 실패: " + e);
        }
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update -= EditorWatchdog;
        EditorApplication.update += EditorWatchdog;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
        {
            SessionState.SetBool(PendingKey, false);
            if (UnityEngine.Object.FindObjectOfType<Driver>() == null)
                new GameObject("Map4ReviewV2_RUNTIME").AddComponent<Driver>();
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ActiveKey, false))
            Fallback(SessionState.GetString(SelectionKey, DefaultSections), "검사가 완료되기 전에 Play 모드가 끝났다.");
    }

    private static void EditorWatchdog()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        float deadline = SessionState.GetFloat(DeadlineKey, 0f);
        if (deadline > 0f && Time.realtimeSinceStartup >= deadline)
            Fallback(SessionState.GetString(SelectionKey, DefaultSections), "에디터 워치독 900초 초과(Play 진입/러너 미완료).");
    }

    private static void ClearPending()
    {
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(ActiveKey, false);
        SessionState.EraseFloat(DeadlineKey);
    }

    private static void Fallback(string selection, string why)
    {
        ClearPending();
        Report report = new Report(selection);
        report.Record("HARNESS.fallback", false, why);
        report.Write();
        Debug.LogError(Tag + " " + why);
        if (Application.isBatchMode) EditorApplication.Exit(1);
        else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    [Serializable]
    private sealed class Item
    {
        public string id;
        public bool passed;
        public string detail;
    }

    [Serializable]
    private sealed class Output
    {
        public string sectionSelection;
        public int failCount;
        public int logErrors;
        public Item[] results;
        public string[] errorMessages;
        public string[] limitations;
    }

    private sealed class Report
    {
        private readonly List<Item> results = new List<Item>();
        private readonly List<string> errors = new List<string>();
        private readonly string selection;
        private bool written;

        public Report(string sections) { selection = sections; }
        public int Count => results.Count;
        public bool Failed => results.Count == 0 || results.Any(r => !r.passed) || errors.Count > 0;
        public void Record(string id, bool passed, string detail)
        {
            results.Add(new Item { id = id ?? "(id 없음)", passed = passed, detail = detail ?? "" });
            Debug.Log($"{Tag} {(passed ? "PASS" : "FAIL")} {id}: {detail}");
        }
        public void LogError(string message) { errors.Add(message); }

        public void Write()
        {
            if (written) return;
            written = true;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string mapRoot = Directory.GetParent(projectRoot).FullName;
            string folder = Path.Combine(mapRoot, "검증");
            Directory.CreateDirectory(folder);
            string stem = "N04_review_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string json = Path.Combine(folder, stem + ".json");
            string txt = Path.Combine(folder, stem + ".txt");
            int fails = results.Count(r => !r.passed) + errors.Count;
            string[] limitations =
            {
                "공개 API와 물리 반응의 자동 점검이다. 실제 키보드 조작·카메라 경험·사람 전체 완주는 별도 검증이 필요하다.",
                selection.Contains(",") ? "여러 섹션을 선택해 같은 Play 세션의 상태를 공유했다. 섹션별 확정 검사는 RunAxe/RunCh8/RunFlow/RunPresentation을 별도 실행한다." : "단일 섹션을 새 Play 세션에서 실행했다."
            };
            Output data = new Output
            {
                sectionSelection = selection, failCount = fails, logErrors = errors.Count,
                results = results.ToArray(), errorMessages = errors.ToArray(), limitations = limitations
            };
            File.WriteAllText(json, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            StringBuilder lines = new StringBuilder();
            lines.AppendLine($"selection={selection} PASS={results.Count(r => r.passed)} FAIL={fails} logErrors={errors.Count}");
            foreach (string limit in limitations) lines.AppendLine("LIMIT " + limit);
            foreach (Item r in results) lines.AppendLine($"{(r.passed ? "PASS" : "FAIL")} {r.id}: {r.detail}");
            foreach (string error in errors) lines.AppendLine("ERROR " + error);
            File.WriteAllText(txt, lines.ToString(), new UTF8Encoding(false));
            Debug.Log($"{Tag} 결과 {(Failed ? "FAIL" : "PASS")} FAIL={fails} exit={(Failed ? 1 : 0)} file={Path.GetFileName(txt)}");
        }
    }

    private sealed class Driver : MonoBehaviour
    {
        private Report report;
        private bool done;
        private float deadline;

        private void Awake()
        {
            report = new Report(SessionState.GetString(SelectionKey, DefaultSections));
            deadline = Time.realtimeSinceStartup + WatchdogSeconds;
            Application.logMessageReceived += OnLog;
        }
        private void Start() { StartCoroutine(Drive()); }
        private void Update()
        {
            if (done || Time.realtimeSinceStartup < deadline) return;
            report.Record("HARNESS.watchdog", false, "실시간 900초 초과");
            Finish();
        }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.LogError(message + (string.IsNullOrEmpty(stackTrace) ? "" : "\n" + stackTrace));
        }

        private IEnumerator Drive()
        {
            // Unity 스케줄러에 nested IEnumerator를 넘기지 않고 모두 이 스택에서 MoveNext한다.
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(Work());
            while (stack.Count > 0 && !done)
            {
                IEnumerator top = stack.Peek();
                bool more;
                object current = null;
                try { more = top.MoveNext(); if (more) current = top.Current; }
                catch (Exception e) { report.Record("HARNESS.exception", false, e.ToString()); break; }
                if (!more)
                {
                    stack.Pop();
                    try { (top as IDisposable)?.Dispose(); }
                    catch (Exception e) { report.Record("HARNESS.dispose", false, e.ToString()); break; }
                    continue;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            if (!done) Finish();
        }

        private IEnumerator Work()
        {
            Map4Director director = UnityEngine.Object.FindObjectOfType<Map4Director>();
            if (director == null) { report.Record("HARNESS.master", false, "Map4Director 없음"); yield break; }
            float loadDeadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < loadDeadline && director.LoadedSectorCount < 8) yield return null;
            SectorController[] sectors = new SectorController[8];
            for (int i = 1; i <= 8; i++)
                if (!director.TryGetSector(i, out sectors[i - 1]) || sectors[i - 1] == null || sectors[i - 1].sectorId != i)
                { report.Record("HARNESS.load", false, $"섹터 {i} 미로드/ID 불일치(전체 {director.LoadedSectorCount}/8)"); yield break; }
            report.Record("HARNESS.load", true, "8섹터 등록 확인");

            float playerDeadline = Time.realtimeSinceStartup + 5f;
            PlayerMover[] players = null;
            while (Time.realtimeSinceStartup < playerDeadline)
            {
                players = OrderedPlayers();
                if (players != null && players.All(p => p.GetComponent<Rigidbody>().useGravity)) break;
                yield return null;
            }
            if (players == null || players.Any(p => !p.GetComponent<Rigidbody>().useGravity))
            { report.Record("HARNESS.players", false, "구/네모/세모 PlayerMover·Rigidbody 각 1개 및 Director 스폰 완료 필요"); yield break; }
            report.Record("HARNESS.players", true, "도형 3개(구·네모·세모), 중력 복원 확인");

            string[] selected = SessionState.GetString(SelectionKey, DefaultSections).Split(',');
            foreach (string section in selected)
            {
                int spawnSector = section == "5" ? 5 : section.StartsWith("8", StringComparison.Ordinal) || section == "pressure" ? 8 : section.StartsWith("portal", StringComparison.Ordinal) ? 6 : 1;
                SectorController scene = sectors[spawnSector - 1];
                if (scene.spawnSlots == null || scene.spawnSlots.Length < 3 || scene.spawnSlots.Take(3).Any(t => t == null))
                { report.Record(section + ".setup", false, "스폰 슬롯 3개 누락"); continue; }
                List<Rigidbody> bodies = players.Select(p => p.GetComponent<Rigidbody>()).ToList();
                bool spawned = director.SpawnPlayersAt(spawnSector, bodies, out int overlaps);
                if (!spawned || overlaps != 0)
                { report.Record(section + ".spawn", false, $"섹터 {spawnSector} 스폰 겹침 {overlaps}, 반환 {spawned}"); continue; }
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                report.Record(section + ".spawn", true, $"섹터 {spawnSector} 3도형 스폰, 겹침 0");
                int before = report.Count;
                Action<string, bool, string> record = (id, ok, detail) => report.Record(section + "/" + id, ok, detail);
                IEnumerator check = section == "5" ? Map4AxeRevealChecks.Run(sectors[4], players, record)
                    : section == "8" ? Map4Ch8FlowChecks.Run(sectors[7], players, record)
                    : section == "8sphere" ? Map4Ch8FlowChecks.RunLead(sectors[7], players, record, 0)
                    : section == "8cube" ? Map4Ch8FlowChecks.RunLead(sectors[7], players, record, 1)
                    : section == "pressure" ? Map4Ch8FlowChecks.RunPressure(sectors[7], players, record)
                    : section == "portal" ? Map4PortalEntryChecks.RunNorth(sectors[5], players, record)
                    : section == "portalsouth" ? Map4PortalEntryChecks.RunSouth(sectors[5], players, record)
                    : section == "portalcubebrake" ? Map4PortalEntryChecks.RunSouthCubeBrake(sectors[5], players, record)
                    : section == "portalsphere" ? Map4PortalEntryChecks.RunSouthSphere(sectors[5], players, record)
                    : section == "portalspherebrake" ? Map4PortalEntryChecks.RunSouthSphereBrake(sectors[5], players, record)
                    : section == "portaltetra" ? Map4PortalEntryChecks.RunSouthTetra(sectors[5], players, record)
                    : section == "present" ? Map4PresentationChecks.Run(sectors, players, record)
                    : Map4ProgressionChecks.Run(sectors, players, record);
                if (check == null) report.Record(section + ".checks", false, "검사 IEnumerator 없음");
                else
                {
                    yield return check;
                    if (report.Count == before) report.Record(section + ".checks", false, "검사가 결과를 기록하지 않았다.");
                }
            }
        }

        private static PlayerMover[] OrderedPlayers()
        {
            PlayerMover[] all = UnityEngine.Object.FindObjectsOfType<PlayerMover>();
            if (all.Length != 3) return null;
            PlayerMover[] ordered = new PlayerMover[3];
            foreach (PlayerMover p in all)
            {
                PlayerShapeIdentity shape = p.GetComponent<PlayerShapeIdentity>();
                if (shape == null) return null;
                int index = (int)shape.Kind; // PlayerShapeStats.ShapeKind: Sphere, Cube, Tetrahedron
                if (index < 0 || index >= 3 || ordered[index] != null) return null;
                ordered[index] = p;
            }
            return ordered.Any(p => p == null || p.GetComponent<Rigidbody>() == null) ? null : ordered;
        }

        private void Finish()
        {
            if (done) return;
            done = true;
            Application.logMessageReceived -= OnLog;
            ClearPending();
            try { report.Write(); }
            catch (Exception e)
            {
                Debug.LogError(Tag + " 결과 쓰기 실패: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else EditorApplication.isPlaying = false;
                return;
            }
            if (Application.isBatchMode) EditorApplication.Exit(report.Failed ? 1 : 0);
            else EditorApplication.isPlaying = false;
        }
    }
}
#endif
