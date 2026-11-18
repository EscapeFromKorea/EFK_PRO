#if DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 개발 빌드 전용 자동 점검. 싱글: -spsmoke / 멀티: -mpsmoke host|client -slot N -map N -codefile 경로.
/// 결과는 로그의 [Smoke] 줄. 기믹 조작감은 확인하지 못한다 — 진입·복귀·일시정지·컨트롤러·이동·스폰만 본다.
/// </summary>
public class MpSmoke : MonoBehaviour
{
    static string[] args;
    static string Arg(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        args = Environment.GetCommandLineArgs();
        if (!args.Contains("-spsmoke") && Arg("-mpsmoke") == null) return;
        var go = new GameObject("MpSmoke");
        DontDestroyOnLoad(go);
        go.AddComponent<MpSmoke>();
    }

    int fails;
    readonly HashSet<string> errors = new HashSet<string>();

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;
    void OnLog(string m, string st, LogType t)
    {
        if ((t == LogType.Error || t == LogType.Exception) && !m.StartsWith("[Smoke]")) errors.Add(t + ": " + m.Split('\n')[0]);
    }

    void Check(bool ok, string what)
    {
        if (!ok) fails++;
        Debug.Log("[Smoke] " + (ok ? "PASS " : "FAIL ") + what);
    }

    void FlushErrors(string where)
    {
        foreach (var e in errors) Debug.Log($"[Smoke] LOGERR ({where}) {e}");
        errors.Clear();
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return Arg("-mpsmoke") != null ? Multi() : Single();
        Debug.Log($"[Smoke] DONE fails={fails}");
        Application.Quit(fails == 0 ? 0 : 1);
    }

    static IEnumerator Until(Func<bool> c, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!c() && Time.realtimeSinceStartup < end) yield return null;
    }

    static string Active => SceneManager.GetActiveScene().path;
    static IEnumerable<PlayerMover> Movers => FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
    static bool Map4Busy => FindFirstObjectByType<Map4Director>() != null && Movers.Any(m => m.TryGetComponent(out Rigidbody r) && !r.useGravity);

    void CheckControllers(string map)
    {
        Check(FindFirstObjectByType<InteractionController>() != null && FindFirstObjectByType<BlockCarryController>() != null
            && FindFirstObjectByType<CubeDockController>() != null && FindFirstObjectByType<FrictionStickerController>() != null
            && FindFirstObjectByType<SnapBlockController>() != null, map + " 자동 생성 컨트롤러 5종 존재");
    }

    IEnumerator WaitMap(int i)
    {
        string path = MpLobby.MapScenes[i];
        yield return Until(() => Active == path, 90f);
        Check(Active == path, $"맵{i + 1} 씬 진입 ({Active})");
        yield return null;
        yield return Until(() => !Map4Busy, 60f);
        yield return new WaitForSecondsRealtime(3f);
    }

    IEnumerator CheckPlayable(int i)
    {
        string n = "맵" + (i + 1);
        var startZone = RespawnZone.Active; // 이동 시험 전에 — 시작 지점에서 잡힌 체크포인트
        var all = Movers.ToArray();
        var ctl = all.Where(m => m.IsControlled).ToArray();
        Check(all.Length == 3, $"{n} 도형 3개 (실제 {all.Length})");
        Check(ctl.Length == 1, $"{n} 조작 도형 1개 (실제 {ctl.Length})");
        Check(FindFirstObjectByType<PlayerControlSwitcher>() != null, n + " Tab 전환기 존재");
        Check(!MpSync.Active && !(NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening), n + " 네트워크 비활성");
        CheckControllers(n);
        if (i == 3) Check(FindFirstObjectByType<Map4Director>().LoadedSectorCount == 8, "맵4 섹터 8개 로드");
        if (ctl.Length == 1)
        {
            var m = ctl[0]; var start = m.transform.position;
            m.UseRemoteInput = true; m.RemoteAxes = new Vector2(0, 1); m.RemoteYaw = m.transform.eulerAngles.y;
            yield return new WaitForSecondsRealtime(1f);
            m.RemoteAxes = Vector2.zero; m.UseRemoteInput = false;
            var d = m.transform.position - start; d.y = 0;
            Check(d.magnitude > 0.05f, $"{n} 조작 도형 이동 {d.magnitude:F2}m");
            Check(m.transform.position.y > start.y - 3f, $"{n} 추락 없음 (y {start.y:F1}→{m.transform.position.y:F1})");
        }
        // ESC 메뉴: 싱글은 시간이 멈추고, 닫으면 돌아온다. 메인화면으로 나가기가 켜져 있다.
        var pause = FindFirstObjectByType<PauseMenuController>();
        var t = typeof(PauseMenuController); var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetMethod("Open", flags).Invoke(pause, null);
        Check(Time.timeScale == 0f && InputGate.Blocked, n + " ESC 일시정지 시간 정지");
        Check(PauseMenuController.OnLeaveToMain != null, n + " 메인화면으로 나가기 활성");
        t.GetMethod("Close", flags).Invoke(pause, null);
        Check(Time.timeScale == 1f && !InputGate.Blocked, n + " ESC 해제 후 복원");
        yield return null;
        if (i != 1 && ctl.Length == 1) yield return CheckStartCheckpoint(n, i, ctl[0], startZone);
        if (ctl.Length == 1) yield return CheckScaleReset(n, ctl[0]);
    }

    // 시작 직후 체크포인트가 있는지, 있으면 다음 체크포인트 전에 떨어져도 거기로 돌아오는지 본다
    IEnumerator CheckStartCheckpoint(string n, int i, PlayerMover m, RespawnZone zone)
    {
        Debug.Log($"[Smoke] INFO {n} 시작 체크포인트: {(zone != null ? zone.name : "없음")}");
        if (i == 0) Check(zone != null && zone.name == "Checkpoint_P00", "맵1 시작 체크포인트");
        if (zone == null) yield break;
        var rb = m.GetComponent<Rigidbody>();
        var fall = new Vector3(m.transform.position.x, -40f, m.transform.position.z);
        rb.linearVelocity = Vector3.zero;
        rb.position = fall;
        m.transform.position = fall;
        yield return Until(() => m.transform.position.y > -5f, 20f);
        yield return Until(() => !m.ExternallyDriven, 15f);
        yield return new WaitForSecondsRealtime(1f);
        var p = m.transform.position;
        var c = zone.GetComponent<Collider>().bounds;
        Check(c.Contains(new Vector3(p.x, c.center.y, p.z)) && p.y > c.min.y - 2f, $"{n} 낙하 후 시작 체크포인트 복귀 {p:F2}");
    }

    static bool StoreFirstCheckpoint()
    {
        var zone = FindFirstObjectByType<RespawnZone>();
        var rc = FindFirstObjectByType<RespawnController>();
        if (zone == null || rc == null) return false;
        typeof(RespawnController).GetMethod("StoreCheckpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(rc, new object[] { zone });
        return true;
    }

    // fade=true: R·피격과 같은 페이드 복귀, false: 낙하 복귀
    static bool Respawn(PlayerMover m, bool fade) => (bool)typeof(RespawnController)
        .GetMethod("TryRespawn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        .Invoke(FindFirstObjectByType<RespawnController>(), new object[] { m, fade, "smoke", !fade, null });

    IEnumerator CheckScaleReset(string n, PlayerMover m)
    {
        if (!StoreFirstCheckpoint()) { Debug.Log($"[Smoke] SKIP {n} 체크포인트 없음 — 크기 초기화 미확인"); yield break; }
        var shape = m.GetComponent<PlayerShapeController>();
        var rb = m.GetComponent<Rigidbody>();
        float s0 = m.transform.localScale.x, mass0 = rb.mass;
        foreach (bool fade in new[] { false, true })
        {
            string how = fade ? "R·피격(페이드)" : "낙하";
            shape.ToggleScale(PlayerShapeController.EScaleState.Grown);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(m.transform.localScale.x > s0 * 1.2f, $"{n} 커짐 ({s0:F2}→{m.transform.localScale.x:F2})");
            Check(Respawn(m, fade), $"{n} {how} 복귀 시작");
            yield return Until(() => !m.ExternallyDriven, 12f);
            float y = m.transform.position.y;
            yield return new WaitForSecondsRealtime(1f);
            Check(shape.CurrentState == PlayerShapeController.EScaleState.Normal && Mathf.Abs(m.transform.localScale.x - s0) < 0.001f, $"{n} {how} 후 기본 크기 ({m.transform.localScale.x:F2})");
            Check(Mathf.Abs(rb.mass - mass0) < 0.001f, $"{n} {how} 후 기본 질량 ({rb.mass:F2})");
            Check(m.transform.position.y > y - 1f, $"{n} {how} 후 추락 없음 (y {y:F2}→{m.transform.position.y:F2})");
        }
    }

    // 멀티: 슬롯 1의 주인이 커졌다가 R로 돌아오는 것을 다른 PC도 본다
    IEnumerator CheckScaleResetMp(MpLobby L, int slot, bool host, int map)
    {
        if (map == 0)
        {
            yield return Until(() => RespawnZone.Active != null, 15f);
            Check(RespawnZone.Active != null && RespawnZone.Active.name == "Checkpoint_P00", "멀티 맵1 시작 체크포인트 공유");
        }
        else if (host && !StoreFirstCheckpoint()) // 맵 1은 시작 체크포인트를 그대로 쓴다 { Debug.Log("[Smoke] SKIP 멀티 체크포인트 없음"); yield break; }
        yield return Until(() => RespawnZone.Active != null, 15f);
        Check(RespawnZone.Active != null, "멀티 체크포인트 공유");
        var body = FindObjectsByType<NetProtoPlayer>(FindObjectsSortMode.None).First(b => b.OwnerClientId == L.Owners[1]).transform;
        float s0 = body.localScale.x;
        if (slot == 1)
        {
            var m = body.GetComponent<PlayerMover>();
            var shape = body.GetComponent<PlayerShapeController>();
            shape.ToggleScale(PlayerShapeController.EScaleState.Grown);
            yield return new WaitForSecondsRealtime(4f);
            Check(Respawn(m, true), "멀티 R 경로 리스폰 시작");
            yield return Until(() => !m.ExternallyDriven, 10f);
            Check(Mathf.Abs(body.localScale.x - s0) < 0.001f, $"멀티 R 후 내 도형 기본 크기 ({body.localScale.x:F2})");
            yield return new WaitForSecondsRealtime(4f);
        }
        else
        {
            yield return Until(() => body.localScale.x > s0 * 1.2f, 20f);
            Check(body.localScale.x > s0 * 1.2f, $"멀티 다른 PC에서 커짐 확인 ({body.localScale.x:F2})");
            yield return Until(() => Mathf.Abs(body.localScale.x - s0) < 0.02f, 25f);
            Check(Mathf.Abs(body.localScale.x - s0) < 0.02f, $"멀티 다른 PC에서 기본 크기 복귀 ({body.localScale.x:F2})");
        }
    }

    IEnumerator BackHome(string how)
    {
        yield return Until(() => SceneManager.GetActiveScene().name == MpNet.HomeScene, 30f);
        yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name == MpNet.HomeScene && FindFirstObjectByType<MpMenu>() != null, how + " → 메인 화면");
        Check(Time.timeScale == 1f && !InputGate.Blocked && Cursor.lockState == CursorLockMode.None, how + " 후 시간·입력·커서 정상");
        Check(PauseMenuController.OnLeaveToMain == null, how + " 후 나가기 훅 해제");
    }

    IEnumerator Single()
    {
        yield return Until(() => FindFirstObjectByType<MpMenu>() != null, 20f);
        Check(SceneManager.GetActiveScene().name == MpNet.HomeScene, "첫 화면이 메인 화면");
        yield return MenuViews();
        yield return LeaveDuringMap4Load();
        for (int i = 0; i < 4; i++)
        {
            FindFirstObjectByType<MpMenu>().StartSingle(i);
            yield return WaitMap(i);
            yield return CheckPlayable(i);
            if (i % 2 == 0) { LevelManager.Instance.CompleteLevel(); Check(LevelManager.Instance.IsCleared, $"맵{i + 1} 클리어 화면"); LevelManager.Instance.GoHome(); yield return BackHome("클리어 Home"); }
            else { PauseMenuController.OnLeaveToMain(); yield return BackHome("ESC 나가기"); }
            FlushErrors("맵" + (i + 1));
        }
        // 맵 1부터 Next Stage로 끝까지(컨트롤러가 다음 맵에서도 살아 있는지)
        FindFirstObjectByType<MpMenu>().StartSingle(0);
        yield return WaitMap(0);
        for (int i = 1; i < 4; i++)
        {
            LevelManager.Instance.CompleteLevel();
            LevelManager.Instance.GoNext();
            yield return WaitMap(i);
            Check(!LevelManager.Instance.IsCleared && Time.timeScale == 1f, $"Next Stage → 맵{i + 1} 시간 정상");
            CheckControllers($"Next Stage 맵{i + 1}");
            FlushErrors("Next 맵" + (i + 1));
        }
    }

    IEnumerator Multi()
    {
        bool host = Arg("-mpsmoke") == "host";
        int slot = int.Parse(Arg("-slot")), map = int.Parse(Arg("-map") ?? "0");
        string file = Arg("-codefile");
        yield return Until(() => FindFirstObjectByType<MpMenu>() != null, 20f);
        var mp = MpNet.Ensure();
        if (host)
        {
            var c = mp.CreateRoom(); yield return Until(() => c.IsCompleted, 40f);
            Check(c.IsCompleted && c.Result == null, "방 생성 " + (c.IsCompleted ? c.Result : "시간 초과"));
            File.WriteAllText(file, mp.Session.Code);
        }
        else
        {
            yield return Until(() => File.Exists(file) && new FileInfo(file).Length > 0, 60f);
            var c = mp.JoinRoom(File.ReadAllText(file)); yield return Until(() => c.IsCompleted, 40f);
            Check(c.IsCompleted && c.Result == null, "방 입장 " + (c.IsCompleted ? c.Result : "시간 초과"));
        }
        yield return Until(() => MpLobby.I != null && MpLobby.I.IsSpawned && MpLobby.I.Owners.Count == 3, 20f);
        var L = MpLobby.I;
        L.ClaimSlotRpc(slot);
        yield return Until(() => L.Owners[slot] == mp.NM.LocalClientId, 10f);
        L.SetReadyRpc(true);
        if (host)
        {
            L.SetMapRpc(map);
            yield return Until(() => L.CanStart && L.Map.Value == map, 90f);
            Check(L.CanStart, "3명 준비 완료");
            L.StartRpc();
        }
        string path = MpLobby.MapScenes[map];
        yield return Until(() => Active == path, 90f);
        yield return Until(() => NetProtoRunner.LocalPlayer != null, 90f);
        Check(NetProtoRunner.LocalPlayer != null, $"맵{map + 1} 내 도형 스폰");
        yield return new WaitForSecondsRealtime(6f);
        var me = NetProtoRunner.LocalPlayer;
        float y1 = me.position.y;
        var bodies = FindObjectsByType<NetProtoPlayer>(FindObjectsSortMode.None);
        Check(bodies.Length == 3, "네트워크 도형 3개");
        float minGap = float.MaxValue;
        for (int a = 0; a < bodies.Length; a++)
            for (int b = a + 1; b < bodies.Length; b++)
            {
                var d = bodies[a].transform.position - bodies[b].transform.position; d.y = 0;
                minGap = Mathf.Min(minGap, d.magnitude);
            }
        Check(minGap > 1f, $"멀티 맵{map + 1} 도형끼리 겹치지 않음 (최소 간격 {minGap:F2}m)");
        CheckControllers($"멀티 맵{map + 1}");
        if (map == 3) Check(FindFirstObjectByType<Map4Director>().LoadedSectorCount == 8, "맵4 섹터 8개 로드");
        var pause = FindFirstObjectByType<PauseMenuController>();
        var t = typeof(PauseMenuController); var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetMethod("Open", flags).Invoke(pause, null);
        Check(Time.timeScale == 1f, "멀티 ESC는 시간을 멈추지 않음");
        t.GetMethod("Close", flags).Invoke(pause, null);
        yield return new WaitForSecondsRealtime(6f);
        Debug.Log($"[Smoke] 내 도형 위치 {me.position:F2}");
        Check(Mathf.Abs(me.position.y - y1) < 1f, $"멀티 맵{map + 1} 바닥 위 유지 (y {y1:F2}→{me.position.y:F2})");
        yield return CheckScaleResetMp(L, slot, host, map);
        FlushErrors("멀티 맵" + (map + 1));
        yield return new WaitForSecondsRealtime(host ? 8f : 2f);
        var leave = mp.Leave(null);
        yield return Until(() => leave.IsCompleted, 20f);
        yield return BackHome("멀티 나가기");
        // 멀티를 하고 나온 뒤의 싱글: 네트워크 도형·상태가 남지 않아야 한다
        FindFirstObjectByType<MpMenu>().StartSingle(0);
        yield return WaitMap(0);
        yield return CheckPlayable(0);
        FlushErrors("멀티 뒤 싱글");
    }

    // 메뉴 화면을 차례로 띄워 IMGUI 오류를 잡고 스크린샷을 남긴다(버튼 클릭 자체는 사람이 확인)
    IEnumerator MenuViews()
    {
        var menu = FindFirstObjectByType<MpMenu>();
        var field = typeof(MpMenu).GetField("v", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        string dir = Arg("-shots");
        foreach (string name in new[] { "Home", "Single", "Multi", "Join" })
        {
            field.SetValue(menu, Enum.Parse(field.FieldType, name));
            for (int i = 0; i < 3; i++) yield return null;
            if (dir != null) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "menu_" + name + ".png"));
            for (int i = 0; i < 3; i++) yield return null;
        }
        var pause = FindFirstObjectByType<PauseMenuController>();
        pause.OpenSettings();
        field.SetValue(menu, Enum.Parse(field.FieldType, "Settings"));
        for (int i = 0; i < 3; i++) yield return null;
        if (dir != null) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "menu_Settings.png"));
        for (int i = 0; i < 3; i++) yield return null;
        Check(pause.SettingsOpen && Time.timeScale == 1f && !InputGate.Blocked, "메인 화면 설정 열기(게임 상태 변화 없음)");
        typeof(PauseMenuController).GetMethod("LeaveSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(pause, null);
        for (int i = 0; i < 3; i++) yield return null;
        Check(!pause.SettingsOpen && field.GetValue(menu).ToString() == "Home", "설정 닫으면 메인 화면 복귀");
        FlushErrors("메뉴 화면");
    }

    // 맵 4 섹터가 로드되는 도중 나가도 섹터가 메인 화면에 남지 않아야 한다
    IEnumerator LeaveDuringMap4Load()
    {
        FindFirstObjectByType<MpMenu>().StartSingle(3);
        yield return Until(() => Active == MpLobby.MapScenes[3], 90f);
        PauseMenuController.OnLeaveToMain();
        yield return BackHome("맵4 로딩 중 나가기");
        yield return new WaitForSecondsRealtime(8f);
        Check(SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().name == MpNet.HomeScene, $"맵4 로딩 중 나가기 후 남은 씬 없음 (씬 {SceneManager.sceneCount}개)");
        FlushErrors("맵4 로딩 중 나가기");
    }
}
#endif
