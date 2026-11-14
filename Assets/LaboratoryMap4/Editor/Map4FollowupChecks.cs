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

/// <summary>11-03 맵4 후속 PlayMode 검사의 공통 실행기. Master에서 8섹터를 로드한 뒤
/// 선택한 섹터의 공개 검사를 순차 실행한다. -quit은 비동기 Play 진입 전에 종료될 수 있어 사용하지 않는다.</summary>
public static class Map4FollowupChecks
{
    private const string Tag = "[Map4FollowupChecks]";
    private const string MasterScene = "Assets/LaboratoryMap4/Scenes/Map4_Master.unity";
    private const string DefaultSectors = "2,5,7,8";
    private const string PendingKey = "Map4Followup_Pending_20261103";
    private const string ActiveKey = "Map4Followup_Active_20261103";
    private const string SectorsKey = "Map4Followup_Sectors_20261103";
    private const string DeadlineKey = "Map4Followup_Deadline_20261103";
    private const float WatchdogSeconds = 600f;
    private const float LoadSeconds = 20f;

    [MenuItem("Tools/Laboratory Map4/Run Followup Checks (2,5,7,8)")]
    public static void Run()
    {
        string selection = DefaultSectors;
        try
        {
            selection = ParseSelection();
            if (EditorApplication.isPlaying) throw new InvalidOperationException("이미 플레이 모드이다.");
            if (!Map4SceneGuard.CanReplaceCurrentScenes()) throw new InvalidOperationException("씬 저장/전환이 취소됐다.");
            EditorSceneManager.OpenScene(MasterScene, OpenSceneMode.Single);
            SessionState.SetString(SectorsKey, selection);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetFloat(DeadlineKey, Time.realtimeSinceStartup + WatchdogSeconds);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            SessionState.SetBool(PendingKey, false);
            FinishFallback(selection, "시작 실패: " + e);
        }
    }

    private static string ParseSelection()
    {
        string raw = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "-followupSectors", StringComparison.OrdinalIgnoreCase)) raw = args[i + 1];
        if (string.IsNullOrWhiteSpace(raw)) raw = DefaultSectors;
        List<int> ids = new List<int>();
        foreach (string token in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(token, out int id) || (id != 2 && id != 5 && id != 7 && id != 8))
                throw new ArgumentException("지원하지 않는 -followupSectors 값: " + token);
            if (!ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count == 0) throw new ArgumentException("-followupSectors 선택이 비었다.");
        ids.Sort();
        return string.Join(",", ids);
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.update -= EditorWatchdog;
        EditorApplication.update += EditorWatchdog;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
        {
            SessionState.SetBool(PendingKey, false);
            if (UnityEngine.Object.FindObjectOfType<Driver>() == null)
                new GameObject("Map4Followup_RUNTIME").AddComponent<Driver>();
        }
        else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ActiveKey, false))
        {
            FinishFallback(SessionState.GetString(SectorsKey, DefaultSectors), "검사가 완료되기 전에 플레이 모드가 끝났다.");
        }
    }

    private static void EditorWatchdog()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        float deadline = SessionState.GetFloat(DeadlineKey, 0f);
        if (deadline > 0f && Time.realtimeSinceStartup >= deadline)
            FinishFallback(SessionState.GetString(SectorsKey, DefaultSectors), "에디터 워치독 600초 초과(Play 진입/러너 미완료).");
    }

    private static void FinishFallback(string selection, string reason)
    {
        ClearPending();
        Report report = new Report(selection);
        report.Record("HARNESS.fallback", false, reason);
        report.Write();
        Debug.LogError(Tag + " " + reason);
        if (Application.isBatchMode) EditorApplication.Exit(1);
        else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    private static void ClearPending()
    {
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(ActiveKey, false);
        SessionState.EraseFloat(DeadlineKey);
    }

    [Serializable]
    private sealed class Result
    {
        public string id;
        public bool passed;
        public string detail;
    }

    [Serializable]
    private sealed class ReportData
    {
        public string sectorSelection;
        public int failCount;
        public int logErrors;
        public Result[] results;
        public string[] errorMessages;
    }

    private sealed class Report
    {
        private readonly List<Result> results = new List<Result>();
        private readonly List<string> errors = new List<string>();
        private readonly string selection;
        private bool written;

        public Report(string sectorSelection) { selection = sectorSelection; }
        public void Record(string id, bool passed, string detail)
        {
            results.Add(new Result { id = id ?? "(id 없음)", passed = passed, detail = detail ?? "" });
            Debug.Log($"{Tag} {(passed ? "PASS" : "FAIL")} {id}: {detail}");
        }
        public void LogError(string message) { errors.Add(message); }

        public void Write()
        {
            if (written) return;
            written = true;
            int failCount = results.Count(r => !r.passed) + errors.Count;
            ReportData data = new ReportData
            {
                sectorSelection = selection, failCount = failCount, logErrors = errors.Count,
                results = results.ToArray(), errorMessages = errors.ToArray()
            };
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string mapRoot = Directory.GetParent(projectRoot).FullName;
            string output = Path.Combine(mapRoot, "검증");
            Directory.CreateDirectory(output);
            string stem = "N03_followup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string json = Path.Combine(output, stem + ".json");
            string txt = Path.Combine(output, stem + ".txt");
            File.WriteAllText(json, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            StringBuilder summary = new StringBuilder();
            summary.AppendLine($"sectorSelection={selection} failCount={failCount} logErrors={errors.Count}");
            foreach (Result r in results) summary.AppendLine($"{(r.passed ? "PASS" : "FAIL")} {r.id}: {r.detail}");
            foreach (string e in errors) summary.AppendLine("ERROR " + e);
            File.WriteAllText(txt, summary.ToString(), new UTF8Encoding(false));
            Debug.Log($"{Tag} 결과 {(failCount == 0 ? "PASS" : "FAIL")} FAIL={failCount} exit={(failCount == 0 ? 0 : 1)} file={Path.GetFileName(txt)}");
        }

        public bool Failed => results.Any(r => !r.passed) || errors.Count > 0 || results.Count == 0;
        public int Count => results.Count;
    }

    private sealed class Driver : MonoBehaviour
    {
        private Report report;
        private bool done;
        private float deadline;

        private void Awake()
        {
            report = new Report(SessionState.GetString(SectorsKey, DefaultSectors));
            deadline = Time.realtimeSinceStartup + WatchdogSeconds;
            Application.logMessageReceived += OnLog;
        }

        private void Start() { StartCoroutine(Drive()); }
        private void Update()
        {
            if (!done && Time.realtimeSinceStartup >= deadline)
            {
                report.Record("HARNESS.watchdog", false, "실시간 600초 초과");
                Finish();
            }
        }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.LogError(message + (string.IsNullOrEmpty(stackTrace) ? "" : "\n" + stackTrace));
        }

        private IEnumerator Drive()
        {
            IEnumerator work = Work();
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(work);
            while (stack.Count > 0 && !done)
            {
                IEnumerator top = stack.Peek();
                bool more;
                object current = null;
                try { more = top.MoveNext(); if (more) current = top.Current; }
                catch (Exception e)
                {
                    report.Record("HARNESS.exception", false, e.ToString());
                    break;
                }
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
            float loadDeadline = Time.realtimeSinceStartup + LoadSeconds;
            while (Time.realtimeSinceStartup < loadDeadline && director.LoadedSectorCount < 8)
                yield return null;
            if (director.LoadedSectorCount != 8)
            {
                report.Record("HARNESS.load", false, $"20초 안에 8섹터 로드 실패: {director.LoadedSectorCount}/8");
                yield break;
            }
            report.Record("HARNESS.load", true, "8섹터 로드 확인");

            // Director의 RunStartup이 스폰·중력 복원까지 끝낼 시간을 로드 제한과 별도로 준다.
            float playerDeadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < playerDeadline && !ThreePlayersReady()) yield return null;
            PlayerMover[] players = OrderedPlayers();
            if (players == null) { report.Record("HARNESS.players", false, "구/네모/세모 PlayerMover·Rigidbody 각 1개 필요"); yield break; }
            report.Record("HARNESS.players", true, "구/네모/세모 3개, 중력 복원 확인");

            string selection = SessionState.GetString(SectorsKey, DefaultSectors);
            foreach (string token in selection.Split(','))
            {
                int id = int.Parse(token);
                if (!director.TryGetSector(id, out SectorController sector) || sector == null || sector.sectorId != id ||
                    sector.spawnSlots == null || sector.spawnSlots.Length < 3 || sector.spawnSlots.Take(3).Any(s => s == null))
                {
                    report.Record($"S{id}.setup", false, "SectorController 또는 3개 스폰 슬롯 누락");
                    continue;
                }
                List<Rigidbody> bodies = players.Select(p => p.GetComponent<Rigidbody>()).ToList();
                if (bodies.Any(b => b == null)) { report.Record($"S{id}.setup", false, "플레이어 Rigidbody 누락"); continue; }
                // CH7 검사 자체가 최초 진입을 관찰한다. 그 전에 CH7 안에 스폰하면
                // 0.25초 감시 시점에 따라 이미 활성화되어 초기상태 검사가 흔들린다.
                int fixtureSector = id == 7 ? 6 : id;
                bool spawned = director.SpawnPlayersAt(fixtureSector, bodies, out int overlaps);
                if (!spawned || overlaps != 0)
                {
                    report.Record($"S{id}.spawn", false, $"스폰 겹침 {overlaps}, 반환 {spawned}");
                    continue;
                }
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                report.Record($"S{id}.spawn", true, id == 7
                    ? "CH7 최초 진입 관찰 전 CH6 시작 슬롯에 3도형 배치, 겹침 0; CH7 진입은 하위 검사가 수행"
                    : "3도형 개별 슬롯 스폰, 겹침 0");
                IEnumerator checks = id == 2 ? Map4FollowupS2Checks.Run(sector, players, report.Record)
                    : id == 7 ? Map4FollowupS7Checks.Run(sector, players, report.Record)
                    : Map4FollowupS5S8Checks.Run(sector, players, report.Record);
                if (checks == null) report.Record($"S{id}.checks", false, "검사 IEnumerator 없음");
                else
                {
                    int before = report.Count;
                    yield return checks;
                    if (report.Count == before) report.Record($"S{id}.checks", false, "검사가 결과를 기록하지 않았다.");
                }
            }
        }

        private static bool ThreePlayersReady()
        {
            PlayerMover[] players = OrderedPlayers();
            return players != null && players.All(p => p.GetComponent<Rigidbody>().useGravity);
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
                int i = (int)shape.Kind;
                if (i < 0 || i >= ordered.Length || ordered[i] != null) return null;
                ordered[i] = p;
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
