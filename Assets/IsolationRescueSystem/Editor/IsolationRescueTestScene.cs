using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 바로 열어서 플레이만 누르면 되는 격리 구출 시험 씬을 만든다.
/// `Tools > Isolation Rescue > Create Test Scene` → Assets/Scenes/IsolationRescue_Test_local.unity 저장.
///
/// [무엇이 들어가나]
/// 조명·카메라(플레이어 추적)·RespawnController·체크포인트·플레이어 2명(정육면체=Player_Cube, 구=Player_Sphere,
/// Tab으로 전환)·시험 격리실(Create Test Room과 동일). 기존 메뉴(플레이어 생성·리스폰 생성)를 그대로 불러
/// 만들므로 팀이 쓰는 플레이어 구성과 같다. 씬 이름의 _local 접미사는 기존 A_Playtest_local 관례를 따랐다.
///
/// [검증]
/// `Verify`는 이 씬을 PlayMode로 열어 실제 플레이어 오브젝트(스크립트로 구동)로 처음부터 끝까지 돌린다.
/// 레버는 코드로 돌리지 않고 플레이어가 실제로 막대를 밀어서 조작한다.
/// 배치모드: `-batchmode -nographic -executeMethod IsolationRescueTestScene.CreateFromCommandLine` (-quit 불필요),
/// 검증은 `IsolationRescueTestScene.VerifyFromCommandLine`.
/// </summary>
[InitializeOnLoad]
public static class IsolationRescueTestScene
{
    public const string ScenePath = "Assets/Scenes/IsolationRescue_Test_local.unity";
    private const string VerifyKey = "IsolationRescueTestScene.VerifyPending";

    static IsolationRescueTestScene()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Isolation Rescue/Create Test Scene")]
    public static void CreateFromMenu()
    {
        if (!Create()) return;
        Debug.Log($"[IsolationRescue] 시험 씬 저장: {ScenePath}. 열어서 Play를 누르면 된다.");
    }

    // -executeMethod IsolationRescueTestScene.CreateFromCommandLine
    public static void CreateFromCommandLine()
    {
        EditorApplication.Exit(Create() ? 0 : 1);
    }

    public static bool Create()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var light = new GameObject("Directional Light");
        Light l = light.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // PlayerObjectMenuItem.EnsureFollowCamera는 이미 있는 카메라에 추적 컴포넌트를 붙이므로 카메라를 먼저 둔다.
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(0f, 6f, -16f);
        camGo.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

        if (!Run("Tools/Respawn/Create Respawn Controller")) return false;
        if (!Run("Tools/Respawn/Create Checkpoint Pole")) return false;
        if (!Run("Tools/PlayerSystem/Create Player/Cube")) return false;
        if (!Run("Tools/PlayerSystem/Create Player/Sphere")) return false;

        GameObject checkpoint = GameObject.Find("Checkpoint");
        GameObject cube = GameObject.Find("Player_Cube");
        GameObject sphere = GameObject.Find("Player_Sphere");
        if (checkpoint == null || cube == null || sphere == null)
        {
            Debug.LogError("[IsolationRescue] 체크포인트/플레이어 생성에 실패했다.");
            return false;
        }

        // 두 명 모두 바깥 대기 구역의 체크포인트 구역 안에서 시작한다(R 복귀와 추락 복귀가 동작하도록).
        checkpoint.transform.position = new Vector3(0f, 0f, -10f);
        cube.transform.position = new Vector3(-1.5f, 0.6f, -9f);
        sphere.transform.position = new Vector3(1.5f, 0.6f, -11f);

        // 리스폰 컨트롤러가 이미 있어야 추락 복귀(IsolationRoleReturn)가 연결된다 — 위에서 먼저 만들었다.
        IsolationRescueMenuItem.Result room = IsolationRescueMenuItem.Build(Vector3.zero);
        if (room.roleReturn.respawn == null)
        {
            Debug.LogError("[IsolationRescue] 추락 복귀에 RespawnController가 연결되지 않았다.");
            return false;
        }

        AssetDatabase.SaveAssets();
        bool saved = EditorSceneManager.SaveScene(SceneManagerActive(), ScenePath);
        if (!saved) Debug.LogError($"[IsolationRescue] 씬 저장 실패: {ScenePath}");
        return saved;
    }

    private static UnityEngine.SceneManagement.Scene SceneManagerActive() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene();

    private static bool Run(string menu)
    {
        bool ok = EditorApplication.ExecuteMenuItem(menu);
        if (!ok) Debug.LogError($"[IsolationRescue] 메뉴 실행 실패: {menu}");
        return ok;
    }

    // ── 검증 진입점 ───────────────────────────────────────────────────────

    // -executeMethod IsolationRescueTestScene.VerifyFromCommandLine  (-quit를 붙이지 않는다)
    public static void VerifyFromCommandLine()
    {
        SessionState.SetBool(VerifyKey, true);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool(VerifyKey, false)) return;

        SessionState.SetBool(VerifyKey, false);
        new GameObject("IsolationSceneVerifier").AddComponent<IsolationSceneVerifier>();
    }
}

/// <summary>저장된 시험 씬을 실제 플레이어로 끝까지 돌려 보는 검증기(PlayMode 전용, 에디터 어셈블리).</summary>
public class IsolationSceneVerifier : MonoBehaviour
{
    private const float OpenY = 3.936f + 2.8f;
    private const float ClosedY = 3.936f + 0.15f;
    private const float Watchdog = 200f;

    private int passed;
    private int failed;
    private bool finished;
    private bool waitOk;
    private float startedAt;
    private int errorLogs;
    private bool lastPushFired;
    private readonly List<string> errors = new List<string>();

    private IsolationRescueController c;
    private PlayerMover cube, sphere;
    private Rigidbody cubeRb, sphereRb;
    private Vector3 cubeVel, sphereVel;

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        errorLogs++;
        if (errors.Count < 8) errors.Add($"{type}: {message}");
    }

    private void Start()
    {
        Application.runInBackground = true;
        startedAt = Time.realtimeSinceStartup;
        StartCoroutine(Main());
    }

    private void Update()
    {
        if (!finished && Time.realtimeSinceStartup - startedAt > Watchdog)
        {
            Check("워치독: 시간 안에 시나리오가 끝남", false);
            Finish();
        }
    }

    private void FixedUpdate()
    {
        Drive(cubeRb, cubeVel);
        Drive(sphereRb, sphereVel);
    }

    private static void Drive(Rigidbody rb, Vector3 v)
    {
        if (rb == null) return;
        Vector3 cur = rb.velocity;
        rb.velocity = new Vector3(v.x, cur.y, v.z);
    }

    private IEnumerator Main()
    {
        // ── 씬 구성 점검 ────────────────────────────────────────────────
        c = FindObjectOfType<IsolationRescueController>();
        var entry = FindObjectOfType<IsolationEntryDoor>();
        var levers = FindObjectsOfType<SequenceLever>();
        var ready = FindObjectsOfType<IsolationReadyButton>();
        var release = FindObjectOfType<ReleaseButton>();
        var power = FindObjectOfType<PowerHoldSwitch>();
        var roleReturn = FindObjectOfType<IsolationRoleReturn>();
        PlayerMover[] movers = FindObjectsOfType<PlayerMover>();
        foreach (PlayerMover m in movers)
        {
            if (m.name == "Player_Cube") cube = m;
            if (m.name == "Player_Sphere") sphere = m;
        }

        Check("씬: 컨트롤러·진입문·레버 3·준비 버튼 2·해제 버튼·전원 판이 있음",
            c != null && entry != null && levers.Length == 3 && ready.Length == 2 && release != null && power != null);
        Check("씬: 플레이어 2명(Cube·Sphere)", cube != null && sphere != null);
        Check("씬: 카메라 추적·조작 전환·리스폰 컨트롤러·추락 복귀 연결",
            FindObjectOfType<PlayerFollowCamera>() != null && FindObjectOfType<PlayerControlSwitcher>() != null &&
            FindObjectOfType<RespawnController>() != null && roleReturn != null && roleReturn.respawn != null);
        if (c == null || cube == null || sphere == null) { Finish(); yield break; }

        cubeRb = cube.GetComponent<Rigidbody>();
        sphereRb = sphere.GetComponent<Rigidbody>();
        System.Array.Sort(levers, (a, b) => a.leverNumber.CompareTo(b.leverNumber));

        // 플레이어를 스크립트로 구동한다(키 입력 대신). 붙잡힘 플래그가 PlayerMover의 속도 대입을 끈다.
        cube.ExternallyDriven = true;
        sphere.ExternallyDriven = true;

        yield return new WaitForSeconds(3f);
        Debug.Log($"[Verify] 시작 위치: Cube {cube.transform.position}, Sphere {sphere.transform.position}");
        // 정육면체는 피벗이 발바닥이라 root y=0이 정상이다 — 솔리드 콜라이더의 바닥 높이로 본다.
        Check("시작: 두 플레이어가 바닥 위에 안착(콜라이더 바닥이 바닥면 근처)",
            SolidBottomY(cube) > -0.1f && SolidBottomY(cube) < 0.3f &&
            SolidBottomY(sphere) > -0.1f && SolidBottomY(sphere) < 0.3f);
        Check("시작: 진입문 열림·격리 대기", entry.door.transform.position.y > OpenY && c.Current == IsolationRescueController.State.Idle);

        // ── 큐브(질량 3, 느림)도 레버를 밀 수 있는가: 대기 상태에서 밀어 레버 무장 해제 여부만 본다 ─────
        Teleport(cubeRb, new Vector3(0f, 0.6f, -4f));
        yield return PushLeverWith(levers[0], cubeRb, v => cubeVel = v, new Vector3(-1.5f, 0.6f, -9f));
        Check("레버: 정육면체도 실제로 밀어서 입력을 만들 수 있다(대기 상태라 컨트롤러는 무시)",
            levers[0].Armed && lastPushFired);

        // ── 진입 → 격리 확정 → 문 닫힘 ──────────────────────────────────
        Teleport(cubeRb, new Vector3(0f, 0.6f, -6f));
        yield return new WaitForSeconds(0.5f);
        yield return Walk(cubeRb, v => cubeVel = v, new Vector3(0f, 0f, 6f), 3.5f, 10f);
        yield return WaitFor(() => c.Current == IsolationRescueController.State.Preparing, 3f);
        Check("진입: 실제 정육면체가 통과하면 격리 확정", waitOk && c.InsidePlayer == cube.gameObject);
        yield return WaitFor(() => entry.Current == IsolationEntryDoor.Phase.Closed, 6f);
        Check("진입: 문이 닫히고 안전 확인", waitOk);

        // 준비 확인 — 두 버튼을 E 대신 Press()로
        foreach (IsolationReadyButton b in ready) b.Press();
        yield return null;
        Check("준비: 양쪽 확인으로 1단계 시작", c.Current == IsolationRescueController.State.InProgress);

        // ── 레버를 실제로 민다 ───────────────────────────────────────────
        LeverHead head1 = levers[0].lever;
        Check("레버: 막대가 플레이어 몸 높이에 있음(바닥 위 0.2~1.2)",
            head1.transform.position.y > 0.2f && head1.transform.position.y < 1.2f);

        // 오답 한 번(가장 먼저 눌러야 할 레버가 아닌 것)을 실제로 밀어 본다.
        int first = c.CurrentOrder[0];
        int wrong = first == 3 ? 2 : 3;
        yield return PushLever(levers[wrong - 1]);
        Check("레버: 실제로 밀면 입력이 들어간다(오답 → 정답 순서 순환)", c.QuestionVersion == 1);

        // 3단계를 전부 실제 푸시로 푼다 — 매번 현재 정답 순서의 다음 레버를 민다.
        for (int stage = 0; stage < 3; stage++)
        {
            for (int i = 0; i < IsolationRescueController.LeverCount; i++)
                yield return PushLever(levers[c.CurrentOrder[c.InputCount] - 1]);

            if (stage < 2)
            {
                Check($"단계 {stage + 1}: 실제 푸시로 완료 → 다음 단계", c.StageIndex == stage + 1);
                yield return new WaitForSeconds(2.8f);
                Check($"단계 {stage + 1}: 사이 문 {stage + 1} 개방", r_passDoor(stage) > OpenY);
            }
        }
        Check("단계 3: 실제 푸시로 완료 → 최종 해제 대기", c.Current == IsolationRescueController.State.AwaitFinalRelease);

        // 전원 판을 구가 밟고 있는 동안 정육면체가 해제 버튼까지 걸어간다.
        Teleport(sphereRb, new Vector3(0f, 0.6f, -18f));
        yield return Walk(sphereRb, v => sphereVel = v, new Vector3(0f, 0f, -22f), 3f, 10f);
        yield return WaitFor(() => c.PowerOn, 3f);
        Check("전원: 구가 판을 밟으면 켜짐", waitOk);

        yield return Walk(cubeRb, v => cubeVel = v, new Vector3(-5f, 0f, 45f), 3.5f, 25f);
        Check("해제: 정육면체가 마지막 방의 해제 버튼 위치에 도착",
            Vector3.Distance(new Vector3(cube.transform.position.x, 0f, cube.transform.position.z),
                             new Vector3(-5f, 0f, 45f)) < 1.5f);
        release.Press();
        Check("해제: 전원 ON에서 해제 성공", c.Current == IsolationRescueController.State.Success);
        yield return new WaitForSeconds(3f);
        Check("해제: 해제문 개방", FindObjectOfType<doorPhysics>() != null && DoorYByName("C2_RELEASE_DOOR") > OpenY);

        Finish();
    }

    private float r_passDoor(int index) => DoorYByName($"C2_PASS_{index + 1}");

    private static float SolidBottomY(PlayerMover m)
    {
        foreach (Collider col in m.GetComponentsInChildren<Collider>())
            if (!col.isTrigger) return col.bounds.min.y;
        return float.NaN;
    }

    private static float DoorYByName(string name)
    {
        GameObject go = GameObject.Find(name);
        return go != null ? go.transform.position.y : -999f;
    }

    // 막대의 '밀리는 쪽 면'(막대 길이에 수직)에 구를 놓고 그 방향으로 민다. 입력이 들어가면(SequenceLever가 무장 해제)
    // 몸을 치우고 막대가 돌아올 때까지 기다린다. 입력이 안 들어가면 실패로 이어지도록 시간 초과만 한다.
    private IEnumerator PushLever(SequenceLever lever)
    {
        yield return PushLeverWith(lever, sphereRb, v => sphereVel = v, new Vector3(-4f, 0.6f, -3f));
    }

    private IEnumerator PushLeverWith(SequenceLever lever, Rigidbody pusher, System.Action<Vector3> setVel,
                                      Vector3 parkAt)
    {
        LeverHead head = lever.lever;
        Vector3 center = head.transform.position;
        Vector3 side = Vector3.Cross(Vector3.up, head.transform.right).normalized; // 막대 길이에 수직
        Vector3 push = -side;                                                       // 실험으로 확인한 밀리는 방향

        Teleport(pusher, new Vector3(center.x + side.x * 2.2f, 0.6f, center.z + side.z * 2.2f));
        yield return new WaitForSeconds(0.25f);

        float t = 0f;
        float best = -999f;
        Debug.Log($"[Verify] 푸시 시작: {pusher.name} pos {pusher.transform.position} → 막대 중심 {center}, 방향 {push}, 막대 right {head.transform.right}");
        while (t < 3f && lever.Armed)
        {
            if (Mathf.Approximately(t % 0.5f, 0f) || t < 0.03f)
                Debug.Log($"[Verify]   t={t:0.00} pos {pusher.transform.position} vel {pusher.velocity} 각도 {head.GetCurrentAngle():0.0}");
            setVel(push * 3f);
            best = Mathf.Max(best, head.GetCurrentAngle());
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        Debug.Log($"[Verify]   끝 pos {pusher.transform.position}");
        lastPushFired = !lever.Armed;
        setVel(Vector3.zero);
        Debug.Log($"[Verify] 레버 {lever.leverNumber} 푸시({pusher.name}): {(lever.Armed ? "입력 안 됨" : "입력됨")}, 최대 각도 {best:0.0}°, {t:0.00}초");

        Teleport(pusher, parkAt); // 막대에서 비킨다
        float w = 0f;
        while (w < 5f && !lever.Armed)
        {
            w += Time.deltaTime;
            yield return null;
        }
    }

    // ── 도구 ──────────────────────────────────────────────────────────────

    private static void Teleport(Rigidbody rb, Vector3 p)
    {
        rb.velocity = Vector3.zero;
        rb.position = p;
        rb.transform.position = p;
    }

    private IEnumerator Walk(Rigidbody rb, System.Action<Vector3> setVel, Vector3 target, float speed, float timeout)
    {
        float t = 0f;
        while (t < timeout)
        {
            Vector3 d = target - rb.transform.position;
            d.y = 0f;
            if (d.magnitude < 0.6f) break;
            setVel(d.normalized * speed);
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        setVel(Vector3.zero);
        yield return new WaitForFixedUpdate();
    }

    private IEnumerator WaitFor(System.Func<bool> cond, float timeout)
    {
        waitOk = false;
        float t = 0f;
        while (t < timeout)
        {
            if (cond()) { waitOk = true; yield break; }
            t += Time.deltaTime;
            yield return null;
        }
        waitOk = cond();
    }

    private void Check(string name, bool ok)
    {
        if (ok) passed++; else failed++;
        Debug.Log($"[Verify] {(ok ? "PASS" : "FAIL")}  {name}");
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        foreach (string e in errors) Debug.Log($"[Verify] 실행 중 로그 에러: {e}");
        Check("실행 중 에러/예외 로그 0건", errorLogs == 0);
        Debug.Log($"[Verify] 결과: PASS {passed} / FAIL {failed}");
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
