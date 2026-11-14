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

/// <summary>정적 프리뷰에서 null이던 선 렌더러 13개의 재질을 PlayMode 초기화 뒤 재검사한다.
/// 비활성 오브젝트의 렌더러도 읽으며, Awake가 아직 실행되지 않은 비활성 LineRenderer는 PENDING이다.</summary>
public static class Map4RuntimeMaterialChecks
{
    private const string Tag = "[Map4RuntimeMaterialChecks]";
    private const string MasterScene = "Assets/Scenes/Map4_Laboratory/Scenes/Map4_Master.unity";
    private const string PendingKey = "Map4Materials_Pending_20261103";
    private const string ActiveKey = "Map4Materials_Active_20261103";
    private const string DeadlineKey = "Map4Materials_Deadline_20261103";
    private const float WatchdogSeconds = 600f;

    // N03_preview_20261103_182143/render_report.txt의 정적 null 슬롯 13개.
    private static readonly string[] ExpectedRuntimeLines =
    {
        "S1/S1_SlingCatapult",
        "S4/S4_FL_Stair1", "S4/S4_FL_Stair2", "S4/S4_FL_Bubble1", "S4/S4_FL_Bubble2",
        "S4/S4_AL_Stair_Sphere", "S4/S4_AL_Stair_Cube", "S4/S4_AL_Stair_Tetra",
        "S4/S4_AL_Bubble_Sphere", "S4/S4_AL_Bubble_Cube", "S4/S4_AL_Bubble_Tetra",
        "S5/S5_Laser_1", "S5/S5_Laser_2"
    };

    [MenuItem("Tools/Laboratory Map4/Followup/Runtime material audit")]
    public static void Run()
    {
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("이미 플레이 모드이다.");
            if (!Map4SceneGuard.CanReplaceCurrentScenes()) throw new InvalidOperationException("씬 저장/전환이 취소됐다.");
            EditorSceneManager.OpenScene(MasterScene, OpenSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetFloat(DeadlineKey, Time.realtimeSinceStartup + WatchdogSeconds);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            SessionState.SetBool(PendingKey, false);
            Fallback("시작 실패: " + e);
        }
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        EditorApplication.playModeStateChanged -= OnMode;
        EditorApplication.playModeStateChanged += OnMode;
        EditorApplication.update -= EditorWatchdog;
        EditorApplication.update += EditorWatchdog;
    }

    private static void OnMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
        {
            SessionState.SetBool(PendingKey, false);
            if (UnityEngine.Object.FindObjectOfType<Driver>() == null)
                new GameObject("Map4RuntimeMaterials_RUNTIME").AddComponent<Driver>();
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ActiveKey, false))
            Fallback("검사 종료 전에 플레이 모드가 끝났다.");
    }

    private static void EditorWatchdog()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        float deadline = SessionState.GetFloat(DeadlineKey, 0f);
        if (deadline > 0f && Time.realtimeSinceStartup >= deadline)
            Fallback("에디터 워치독 600초 초과(Play 진입/러너 미완료).");
    }

    private static void ClearPending()
    {
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(ActiveKey, false);
        SessionState.EraseFloat(DeadlineKey);
    }

    private static void Fallback(string reason)
    {
        ClearPending();
        Report r = new Report();
        r.Add("HARNESS", "(Master)", "Runner", "", "", "FAIL", reason);
        r.Write();
        Debug.LogError(Tag + " " + reason);
        if (Application.isBatchMode) EditorApplication.Exit(1);
        else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    [Serializable]
    private sealed class Item
    {
        public string id;
        public string path;
        public string rendererType;
        public string material;
        public string shader;
        public string status;
        public string detail;
    }

    [Serializable]
    private sealed class Data
    {
        public int sectorCount;
        public int rendererCount;
        public int materialSlots;
        public int passCount;
        public int failCount;
        public int pendingCount;
        public int logErrors;
        public int baselineLinesFound;
        public int baselineLinesPassed;
        public Item[] items;
        public string[] errors;
    }

    private sealed class Report
    {
        public readonly List<Item> Items = new List<Item>();
        public readonly List<string> Errors = new List<string>();
        public int SectorCount;
        public int RendererCount;
        public int BaselineFound;
        public int BaselinePassed;
        private bool written;

        public void Add(string id, string path, string rendererType, string material, string shader, string status, string detail)
        {
            Items.Add(new Item { id = id, path = path, rendererType = rendererType, material = material,
                shader = shader, status = status, detail = detail });
        }
        public void Error(string line) { Errors.Add(line); }
        public int FailCount => Items.Count(i => i.status == "FAIL") + Errors.Count;
        public int PendingCount => Items.Count(i => i.status == "PENDING");
        public bool Success => FailCount == 0 && PendingCount == 0 && SectorCount == 8 && BaselinePassed == ExpectedRuntimeLines.Length;

        public void Write()
        {
            if (written) return;
            written = true;
            string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            string dir = Path.Combine(root, "검증");
            Directory.CreateDirectory(dir);
            string stem = "N03_materials_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string json = Path.Combine(dir, stem + ".json");
            string txt = Path.Combine(dir, stem + ".txt");
            Data data = new Data
            {
                sectorCount = SectorCount, rendererCount = RendererCount,
                materialSlots = Items.Count(i => i.rendererType != "Runner"),
                passCount = Items.Count(i => i.status == "PASS"), failCount = FailCount,
                pendingCount = PendingCount, logErrors = Errors.Count,
                baselineLinesFound = BaselineFound, baselineLinesPassed = BaselinePassed,
                items = Items.ToArray(), errors = Errors.ToArray()
            };
            File.WriteAllText(json, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            StringBuilder s = new StringBuilder();
            s.AppendLine($"sectorCount={SectorCount} rendererCount={RendererCount} materialSlots={data.materialSlots} PASS={data.passCount} FAIL={FailCount} PENDING={PendingCount} logErrors={Errors.Count} baselineLines={BaselinePassed}/{ExpectedRuntimeLines.Length}");
            foreach (Item i in Items) s.AppendLine($"{i.status} {i.id} path={i.path} type={i.rendererType} material={i.material} shader={i.shader} detail={i.detail}");
            foreach (string error in Errors) s.AppendLine("ERROR " + error);
            File.WriteAllText(txt, s.ToString(), new UTF8Encoding(false));
            Debug.Log($"{Tag} 결과 {(Success ? "PASS" : "FAIL")} FAIL={FailCount} PENDING={PendingCount} exit={(Success ? 0 : 1)} file={Path.GetFileName(txt)}");
        }
    }

    private sealed class Driver : MonoBehaviour
    {
        private Report report;
        private float deadline;
        private bool done;

        private void Awake()
        {
            report = new Report();
            deadline = Time.realtimeSinceStartup + WatchdogSeconds;
            Application.logMessageReceived += OnLog;
        }
        private void Start() { StartCoroutine(RunChecks()); }
        private void Update()
        {
            if (done || Time.realtimeSinceStartup < deadline) return;
            report.Add("HARNESS.watchdog", "(runtime)", "Runner", "", "", "FAIL", "실시간 600초 초과");
            Finish();
        }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.Error(message + (string.IsNullOrEmpty(stackTrace) ? "" : "\n" + stackTrace));
        }

        private IEnumerator RunChecks()
        {
            // Work.MoveNext를 직접 감싸 예외가 나도 FAIL 결과와 종료 코드를 남긴다.
            IEnumerator work = Work();
            while (!done)
            {
                bool more;
                object current = null;
                try { more = work.MoveNext(); if (more) current = work.Current; }
                catch (Exception e) { report.Add("HARNESS.exception", "(runtime)", "Runner", "", "", "FAIL", e.ToString()); break; }
                if (!more) break;
                yield return current;
            }
            Finish();
        }

        private IEnumerator Work()
        {
                Map4Director director = UnityEngine.Object.FindObjectOfType<Map4Director>();
                if (director == null) { report.Add("HARNESS.master", "(Master)", "Runner", "", "", "FAIL", "Map4Director 없음"); yield break; }
                float loadDeadline = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < loadDeadline && director.LoadedSectorCount < 8) yield return null;
                if (director.LoadedSectorCount != 8)
                {
                    report.Add("HARNESS.load", "(Master)", "Runner", "", "", "FAIL", $"20초 안에 8섹터 로드 실패: {director.LoadedSectorCount}/8");
                    yield break;
                }
                report.SectorCount = 8;
                float playerDeadline = Time.realtimeSinceStartup + 5f;
                PlayerMover[] players = null;
                while (Time.realtimeSinceStartup < playerDeadline)
                {
                    players = UnityEngine.Object.FindObjectsOfType<PlayerMover>();
                    if (players.Length == 3 && players.All(p => p != null && p.GetComponent<Rigidbody>() != null && p.GetComponent<Rigidbody>().useGravity)) break;
                    yield return null;
                }
                if (players == null || players.Length != 3 || players.Any(p => p == null || p.GetComponent<Rigidbody>() == null || !p.GetComponent<Rigidbody>().useGravity))
                {
                    report.Add("HARNESS.players", "(Master)", "Runner", "", "", "FAIL", "도형 3개 또는 중력 복원 누락");
                    yield break;
                }
                yield return null;
                yield return null;
                Physics.SyncTransforms();

                // 안전한 시작 스폰에만 세 도형을 놓아 섹터 진입 어댑터가 자연 동작하게 한다.
                // 함정 본체의 SetActive·이벤트 Invoke로 초기화를 강제하지 않는다.
                foreach (int id in new[] { 4, 5 })
                {
                    if (!director.TryGetSector(id, out SectorController entry) || entry == null ||
                        entry.spawnSlots == null || entry.spawnSlots.Length < 3 || entry.spawnSlots.Take(3).Any(s => s == null))
                    {
                        report.Add($"HARNESS.S{id}.entry", $"S{id}", "Runner", "", "", "FAIL", "안전 시작 스폰 슬롯 3개 누락");
                        continue;
                    }
                    List<Rigidbody> bodies = players.Select(p => p.GetComponent<Rigidbody>()).ToList();
                    bool placed = director.SpawnPlayersAt(id, bodies, out int overlaps);
                    if (!placed || overlaps != 0)
                        report.Add($"HARNESS.S{id}.entry", $"S{id}", "Runner", "", "", "FAIL", $"시작 스폰 겹침 {overlaps}, 반환 {placed}");
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                }

                HashSet<string> baselineSeen = new HashSet<string>();
                for (int id = 1; id <= 8; id++)
                {
                    if (!director.TryGetSector(id, out SectorController sector) || sector == null)
                    {
                        report.Add($"S{id}.sector", $"S{id}", "Runner", "", "", "FAIL", "SectorController 누락");
                        continue;
                    }
                    foreach (Renderer renderer in sector.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        report.RendererCount++;
                        string key = $"S{id}/{renderer.name}";
                        string path = HierarchyPath(sector.transform, renderer.transform);
                        string kind = renderer.GetType().Name;
                        if (renderer is LineRenderer && ExpectedRuntimeLines.Contains(key)) baselineSeen.Add(key);
                        Material[] materials = renderer.sharedMaterials;
                        if (materials == null || materials.Length == 0)
                        {
                            AddSlot(key + "[0]", path, kind, null, renderer.gameObject.activeInHierarchy);
                            continue;
                        }
                        for (int slot = 0; slot < materials.Length; slot++)
                            AddSlot(key + $"[{slot}]", path, kind, materials[slot], renderer.gameObject.activeInHierarchy);
                    }
                }
                report.BaselineFound = baselineSeen.Count;
                report.BaselinePassed = ExpectedRuntimeLines.Count(key => baselineSeen.Contains(key) &&
                    report.Items.Where(item => item.id.StartsWith(key + "[", StringComparison.Ordinal)).All(item => item.status == "PASS"));
                foreach (string key in ExpectedRuntimeLines)
                    if (!baselineSeen.Contains(key)) report.Add("BASELINE.missing", key, "LineRenderer", "", "", "FAIL", "정적 프리뷰의 null 선 렌더러가 런타임 씬에서 없음");
        }

        private void AddSlot(string id, string path, string rendererType, Material material, bool active)
        {
            string name = material != null ? material.name : "(null)";
            Shader shader = material != null ? material.shader : null;
            string shaderName = shader != null ? shader.name : "(null)";
            string status;
            string detail = active ? "active" : "inactive";
            if (material == null)
                status = rendererType == nameof(LineRenderer) && !active ? "PENDING" : "FAIL";
            else if (shader == null || shaderName == "Hidden/InternalErrorShader") status = "FAIL";
            else status = "PASS";
            if (status == "PENDING") detail += "; Awake 미실행 가능 — 통과로 세지 않음";
            report.Add(id, path, rendererType, name, shaderName, status, detail);
        }

        private static string HierarchyPath(Transform sector, Transform leaf)
        {
            List<string> parts = new List<string>();
            for (Transform t = leaf; t != null; t = t.parent)
            {
                parts.Add(t.name);
                if (t == sector) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
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
            if (Application.isBatchMode) EditorApplication.Exit(report.Success ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
#endif
