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

    static void Put(Transform me, Vector3 p) => Map2KitchenDraft.KitchenDraftController.MoveActor(me, p);

    // 맵 4 퍼즐: 배선 → 격리 → 에너지볼·포탈 → 혼합(역할) → 매니저 수면 → 열쇠·문 → 최종 탈출(팀 이동) → 클리어
    // 세 창이 같은 단계에 도착할 때까지 기다린다(참가 코드별 표시 파일)
    IEnumerator Barrier(string step, int slot)
    {
        string prefix = Arg("-codefile") + "." + MpNet.I.Session.Code + "." + step + ".";
        File.WriteAllText(prefix + slot, "1");
        yield return Until(() => File.Exists(prefix + 0) && File.Exists(prefix + 1) && File.Exists(prefix + 2), 90f);
    }

    IEnumerator CheckLabPuzzlesMp(MpLobby L, int slot, bool host)
    {
        var me = NetProtoRunner.LocalPlayer;
        var mover = me.GetComponent<PlayerMover>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        yield return Barrier("start", slot);

        // 서버실은 전실에 도착해야 켜진다: 슬롯 0이 들어가면 전원의 장치가 켜진다
        var activator = FindObjectsByType<Lab_ActivateWhenPlayersReady>(FindObjectsSortMode.None).First(a => a.name.StartsWith("S7"));
        if (slot == 0) Put(me, activator.zone.bounds.center);

        // 배선: 슬롯 1이 맞는 쌍 하나를 연결하면 전원이 본다
        WiringPanel panel = null;
        yield return Until(() => (panel = FindObjectsByType<WiringPanel>(FindObjectsSortMode.None).Where(p => p.CurrentCircuit != null).OrderBy(p => p.name).FirstOrDefault()) != null, 10f);
        Check(panel != null, "맵4 배선 회로 공유");
        if (panel != null)
        {
            if (slot == 1)
            {
                var pair = panel.CurrentCircuit.pairs[0];
                panel.SelectOutput(panel.outputPorts.First(p => p.portId == pair.outputId));
                yield return Until(() => panel.SelectedOutput != null, 8f);
                panel.TrySelectInput(panel.inputPorts.First(p => p.portId == pair.inputId));
            }
            yield return Until(() => panel.Connections.Count > 0, 12f);
            Check(panel.Connections.Count > 0, "맵4 배선 연결 공유");
        }
        yield return Barrier("wiring", slot);

        // 격리 구출: 방장이 슬롯 1을 격리, 바깥 준비는 슬롯 2의 버튼
        var iso = FirstByName<IsolationRescueController>();
        var cube = MpSync.MoverById(MpSync.BodyId(FindObjectsByType<NetProtoPlayer>(FindObjectsSortMode.None).First(b => b.OwnerClientId == L.Owners[1])));
        if (host) { iso.RequestCapture(cube.gameObject); iso.ReportDoorClosedSafe(); iso.SetReady(true); }
        yield return Until(() => iso.InsidePlayer == cube.gameObject, 12f);
        Check(iso.InsidePlayer == cube.gameObject, "맵4 격리 대상 공유");
        if (slot == 2)
        {
            var button = FindObjectsByType<IsolationReadyButton>(FindObjectsSortMode.None).First(b => !b.inside && b.controller == iso);
            typeof(IsolationInteractZone).GetMethod("PressOrRequest", flags).Invoke(button, null);
        }
        yield return Until(() => iso.Current == IsolationRescueController.State.InProgress, 12f);
        Check(iso.Current == IsolationRescueController.State.InProgress && iso.Timer.Remaining > 0f && iso.CurrentOrder[0] > 0, $"맵4 격리 시작·타이머·문제 공유 (남은 {iso.Timer.Remaining:F0}초)");
        yield return new WaitForSecondsRealtime(1f);
        yield return Barrier("iso", slot);
        if (host) iso.FullRestart();
        yield return Until(() => iso.Current == IsolationRescueController.State.Idle, 10f);

        // 에너지볼: 슬롯 2가 줍고 포탈을 설치한다
        var ball = FirstByName<EnergyBall>();
        var surface = FirstByName<PortalSurface>();
        if (slot == 2)
        {
            var receiver = me.GetComponent<PlayerEnergyReceiver>();
            if (receiver == null) receiver = me.gameObject.AddComponent<PlayerEnergyReceiver>();
            receiver.TryPickup(ball);
        }
        yield return Until(() => ball.State == EnergyBall.BallState.Carried, 12f);
        Check(ball.State == EnergyBall.BallState.Carried && ball.Owner != null, "맵4 에너지볼 줍기 공유");
        yield return Barrier("ball", slot);
        if (slot == 2)
        {
            surface.MpRequestPlace(ball.color, surface.Box.bounds.center);
            ball.MpNotifyInstalled(me.GetComponent<PlayerEnergyReceiver>());
        }
        System.Func<SpacePortal> portal = () => ball.color == EnergyColor.Orange ? SpacePortal.Orange : SpacePortal.Blue;
        yield return Until(() => portal() != null && portal().transform.parent == surface.transform, 12f);
        Check(portal() != null && portal().transform.parent == surface.transform, "맵4 포탈 설치 공유");
        yield return Barrier("portal", slot);

        // 혼합대: 슬롯 1이 역할을 맡고 서버의 조합식대로 넣는다(조합식이 다르면 완성되지 않는다)
        var chapter = FirstByName<ManagerChapterController>();
        var station = chapter.station;

        // 매니저에게 잡히면 팀 실패: 전원이 CH8 시작으로 돌아간다(각 몸의 주인 PC가 옮긴다)
        if (host) chapter.RequestStart(mover);
        yield return Until(() => chapter.Current == ManagerChapterController.State.Running, 12f);
        yield return Barrier("catch-start", slot);
        Transform managerBody = chapter.managerAgent.agent.transform;
        float catchEnd = Time.realtimeSinceStartup + 4f;
        while (slot == 2 && Time.realtimeSinceStartup < catchEnd)
        {
            Put(me, managerBody.position + Vector3.up * 0.3f);
            yield return new WaitForSecondsRealtime(0.15f);
            if (mover.ExternallyDriven) break; // 복귀가 시작됐다
        }
        yield return Until(() => chapter.InStartZone(me.position) && !mover.ExternallyDriven, 20f);
        Check(chapter.InStartZone(me.position), $"맵4 잡힘: 내 몸이 시작 구역으로 복귀 {me.position:F1}");
        if (host) Check(chapter.FailureCount > 0, $"맵4 잡힘: 서버 팀 실패 처리 ({chapter.FailureCount}회)");
        yield return Barrier("caught", slot);
        // 이후 단계는 감시를 끄고 진행한다(사용 지점이 매니저 바로 옆이다)
        if (host)
        {
            typeof(ManagerChapterController).GetField("skipSurveillance", flags).SetValue(chapter, true);
            chapter.managerAgent.Deactivate();
            chapter.ResetAll();
        }
        yield return Barrier("skip", slot);
        if (slot == 1)
        {
            yield return Until(() => station.Recipe.Count > 0, 10f);
            station.slot.HandleInteract(mover);
            yield return Until(() => station.slot.HasUser(mover), 10f);
            Check(station.slot.CanShowTo(mover), "맵4 역할 화면이 담당자에게 열림");
            for (int i = 0; i < station.Recipe.Count; i++)
            {
                int index = System.Array.FindIndex(station.reagents, r => r.id == station.Recipe[i]);
                int expected = i + 1;
                station.Select(index, mover);
                yield return Until(() => station.SelectedIndex == index, 6f);
                station.Submit(mover);
                yield return Until(() => station.Buffer.Count >= expected || station.IsComplete, 6f);
            }
        }
        yield return Until(() => station.slot.Users.Count > 0, 12f);
        Check(station.slot.Users.Count > 0, "맵4 역할 담당자 공유");
        if (slot != 1) Check(!station.slot.CanShowTo(mover), "맵4 역할 화면은 다른 사람에게 열리지 않음");
        yield return Until(() => station.IsComplete && station.HasToken, 20f);
        Check(station.IsComplete && station.HasToken, "맵4 혼합 완성 공유");
        Check(station.book == null || station.book.PageCount > 0, "맵4 가이드북에 조합식 표시");
        yield return Barrier("mix", slot);

        // 매니저: 방장이 시작, 슬롯 2가 완성물을 쓰면 잠든다
        if (host) chapter.RequestStart(mover);
        yield return Until(() => chapter.Current == ManagerChapterController.State.Running, 12f);
        Check(chapter.Current == ManagerChapterController.State.Running && chapter.managerAgent.IsAwake, "맵4 매니저 시작 공유");
        yield return Barrier("manager", slot);
        if (slot == 2)
        {
            Put(me, chapter.usePoint.transform.position + Vector3.up * 0.6f);
            yield return new WaitForSecondsRealtime(1.2f);
            Put(me, chapter.usePoint.transform.position + Vector3.up * 0.6f);
            yield return new WaitForSecondsRealtime(0.5f);
            chapter.usePoint.RequestUse(mover);
        }
        yield return Until(() => chapter.Current == ManagerChapterController.State.Asleep, 15f);
        Check(chapter.Current == ManagerChapterController.State.Asleep && chapter.managerAgent.Current == ManagerAgent.State.Asleep && !station.HasToken, "맵4 매니저 수면 공유");
        yield return Barrier("sleep", slot);

        // 열쇠·문
        var key = chapter.keyPoint;
        var exit = FirstByName<Lab_FinalExitFlow>();
        yield return Until(() => key.Revealed, 8f);
        if (slot == 0)
        {
            Put(me, key.transform.position + Vector3.up * 0.3f);
            yield return new WaitForSecondsRealtime(1f);
            Put(me, key.transform.position + Vector3.up * 0.3f);
            yield return new WaitForSecondsRealtime(0.3f);
            key.TryTake(mover);
        }
        yield return Until(() => key.HasKey, 12f);
        Check(key.HasKey, "맵4 열쇠 공유");
        yield return Barrier("key", slot);
        if (slot == 1)
        {
            Put(me, exit.keyDoor.transform.position + Vector3.up * 0.3f);
            yield return new WaitForSecondsRealtime(0.6f);
            exit.keyDoor.TryOpen(mover);
        }
        yield return Until(() => exit.keyDoor.IsOpen, 12f);
        Check(exit.keyDoor.IsOpen, "맵4 잠긴 문 공유");
        yield return Barrier("door", slot);

        // 최종 탈출: 슬롯 2 한 명이 들어가면 세 몸이 모두 옮겨지고 맵이 끝난다
        if (slot == 2) Put(me, exit.GetComponent<Collider>().bounds.center);
        yield return Until(() => exit.Completed, 15f);
        Check(exit.Completed, "맵4 최종 탈출 공유");
        yield return new WaitForSecondsRealtime(1.5f);
        Vector3 finalSlot = exit.finalSlots[Mathf.Clamp(slot, 0, 2)].position;
        Check(new Vector2(me.position.x - finalSlot.x, me.position.z - finalSlot.z).magnitude < 2f, $"맵4 팀 이동: 내 몸이 내 자리로 이동 {me.position:F1}");
        // 스테이지 클리어는 최종 구역 안쪽의 체크포인트에 닿을 때 난다
        yield return Barrier("exit", slot);
        if (slot == 2) Put(me, exit.finalCheckpoint.GetComponent<Collider>().bounds.center);
        yield return Until(() => L.Cleared.Value, 15f);
        Check(L.Cleared.Value, "맵4 최종 지점 도달로 전원 클리어");
        FlushErrors("맵4 퍼즐");
    }

    static T FirstByName<T>() where T : Component =>
        FindObjectsByType<T>(FindObjectsSortMode.None).OrderBy(c => c.name).ThenBy(c => c.transform.position.x).ThenBy(c => c.transform.position.z).FirstOrDefault();

    // 맵 4 위험 장치: 서버가 시점을 정하고, 각 PC가 같은 모습을 보며, 피격은 몸 주인 PC에서만 일어난다
    IEnumerator CheckHazardsMp(int slot, bool host)
    {
        var me = NetProtoRunner.LocalPlayer;
        var laser = FirstByName<FixedPeriodicLaser>();
        var launcher = FirstByName<ProjectileLauncher>();
        var trap = FindObjectsByType<PeriodicTrapBase>(FindObjectsSortMode.None).Where(t => !(t is AxeTrap)).OrderBy(t => t.name).ThenBy(t => t.transform.position.x).FirstOrDefault();
        Check(laser != null && launcher != null && trap != null, "맵4 위험 장치 존재");
        if (laser == null || launcher == null || trap == null) yield break;
        if (host) { laser.Activate(); launcher.Activate(); trap.Activate(); }
        var line = laser.GetComponent<LineRenderer>();
        yield return Until(() => line.enabled, 10f);
        Check(line.enabled, "맵4 레이저 표시 공유");
        yield return Until(() => FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length > 0, 15f);
        Check(FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length > 0, "맵4 발사체 공유");
        Vector3 trapStart = trap.transform.position;
        yield return Until(() => (trap.transform.position - trapStart).magnitude > 0.2f, 15f);
        Check((trap.transform.position - trapStart).magnitude > 0.2f, $"맵4 함정 움직임 공유 ({trap.name})");
        int hits = 0;
        UnityEngine.Events.UnityAction<GameObject> onHit = _ => hits++;
        launcher.OnHazardHit.AddListener(onHit);
        float end = Time.realtimeSinceStartup + 14f;
        while (slot == 1 && hits == 0 && Time.realtimeSinceStartup < end)
        {
            Put(me, launcher.transform.position + launcher.transform.forward * 2.5f);
            yield return new WaitForSecondsRealtime(0.1f);
        }
        while (Time.realtimeSinceStartup < end) yield return null; // 다음 시험을 전원이 같은 때 시작한다
        launcher.OnHazardHit.RemoveListener(onHit);
        Check(slot == 1 ? hits > 0 : hits == 0, $"맵4 발사체 피격은 몸 주인 PC만 (이 PC {hits}회)");
        if (host) { laser.Deactivate(); launcher.Deactivate(); trap.Deactivate(); }
        yield return new WaitForSecondsRealtime(1f);
        FlushErrors("맵4 위험 장치");
    }

    // 맵 3: 부품 수집·설치·진행 공유, 레일카 좌석, 스티커, 퍼즐 초기화, 포탈 범위, 출구 클리어
    IEnumerator CheckToyWorldMp(MpLobby L, int slot)
    {
        var d = ToyWorldLevelDirector.Instance;
        var me = NetProtoRunner.LocalPlayer;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var items = FindObjectsByType<ToyWorldRepairItem>(FindObjectsSortMode.None);
        var sockets = FindObjectsByType<ToyWorldInstallSocket>(FindObjectsSortMode.None);

        // 레일카: 슬롯 0이 타고, 다른 사람은 탈 수 없다. 서버 카트가 떠오르지 않아야 한다.
        var rider = FindFirstObjectByType<RailCartRider>();
        var cart = rider.GetComponent<RailCart>();
        var occupant = typeof(RailCartRider).GetField("mpOccupant", flags);
        float cartY = cart.transform.position.y;
        if (slot == 0)
        {
            Put(me, rider.transform.position + rider.transform.right * 1.5f + Vector3.up * 0.6f);
            yield return new WaitForSecondsRealtime(0.5f);
            typeof(RailCartRider).GetMethod("TryBoard", flags).Invoke(rider, null);
        }
        yield return Until(() => (float)occupant.GetValue(rider) > 0f, 12f);
        Check((float)occupant.GetValue(rider) > 0f, "맵3 레일카 탑승 공유");
        yield return new WaitForSecondsRealtime(2.5f);
        Check(Mathf.Abs(cart.transform.position.y - cartY) < 0.4f, $"맵3 탑승 후 카트 안정 (y {cartY:F2}→{cart.transform.position.y:F2})");
        if (slot == 0)
        {
            Check(me.GetComponent<PlayerMover>().ExternallyDriven, "맵3 탑승자 조작 잠금");
            typeof(RailCartRider).GetMethod("OnInteract", flags).Invoke(rider, null);
            Check(!me.GetComponent<Rigidbody>().isKinematic && me.parent == null, "맵3 하차 후 몸 복원");
        }
        yield return Until(() => (float)occupant.GetValue(rider) <= 0f, 12f);
        Check((float)occupant.GetValue(rider) <= 0f, "맵3 하차 공유");

        // 스티커: 슬롯 2가 붙이면 전원에게 보이고, 떼면 사라진다
        var surface = FindObjectsByType<StickerSurface>(FindObjectsSortMode.None).OrderBy(s => s.name).First();
        if (slot == 2)
        {
            Put(me, surface.ResolvedCollider.bounds.center + Vector3.up * (surface.ResolvedCollider.bounds.extents.y + 0.8f));
            yield return new WaitForSecondsRealtime(0.3f);
            var carrier = me.GetComponent<PlayerStickerCarrier>();
            yield return Until(() => carrier != null || (carrier = me.GetComponent<PlayerStickerCarrier>()) != null, 3f);
            yield return null; yield return null;
            typeof(PlayerStickerCarrier).GetField("aimedSurface", flags).SetValue(carrier, surface);
            typeof(PlayerStickerCarrier).GetMethod("HandleAttachKey", flags).Invoke(carrier, null);
        }
        yield return Until(() => surface.Current != null, 12f);
        Check(surface.Current != null, "맵3 스티커 부착 공유");
        yield return new WaitForSecondsRealtime(2f);
        if (slot == 2)
        {
            var carrier = me.GetComponent<PlayerStickerCarrier>();
            typeof(PlayerStickerCarrier).GetField("aimedSurface", flags).SetValue(carrier, surface);
            typeof(PlayerStickerCarrier).GetMethod("HandleAttachKey", flags).Invoke(carrier, null);
        }
        yield return Until(() => surface.Current == null, 12f);
        Check(surface.Current == null, "맵3 스티커 회수 공유");

        // 퍼즐 초기화: 방장이 블록을 옮겨 두고, 슬롯 1이 초기화를 요청하면 전원에게 제자리로 돌아온다
        var block = FindObjectsByType<SnapBlock>(FindObjectsSortMode.None).OrderBy(b => b.name).First();
        Vector3 home = block.transform.position;
        if (slot == 0) { block.Body.position = home + Vector3.up * 3f; block.transform.position = block.Body.position; }
        yield return Until(() => block.transform.position.y > home.y + 0.5f, 6f);
        yield return new WaitForSecondsRealtime(0.2f);
        if (slot == 1) FindFirstObjectByType<PuzzleResetManager>().RequestReset();
        yield return new WaitForSecondsRealtime(1f);
        yield return Until(() => Vector3.Distance(block.transform.position, home) < 0.6f, 12f);
        Check(Vector3.Distance(block.transform.position, home) < 0.6f, "맵3 퍼즐 초기화 공유");

        // 포탈: 복사본에는 굴리기 수신기가 붙지 않는다
        var portal = FindFirstObjectByType<Portal>();
        if (slot == 1) { Put(me, portal.GetComponent<Collider>().bounds.center); }
        yield return new WaitForSecondsRealtime(2f);
        var cubeBody = FindObjectsByType<NetProtoPlayer>(FindObjectsSortMode.None).First(b => b.OwnerClientId == L.Owners[1]);
        if (slot != 1) Check(cubeBody.GetComponent<PlayerRollModeReceiver>() == null, "맵3 포탈이 남의 몸을 건드리지 않음");

        // 부품 수집 → 설치 → 활성화 → 출구
        foreach (var it in items) if ((int)it.itemType == slot) Put(me, it.GetComponent<Collider>().bounds.center);
        yield return Until(() => d.AllItemsCollected, 20f);
        Check(d.AllItemsCollected && items.All(i => !i.GetComponent<Collider>().enabled), "맵3 부품 3개 수집 공유");
        yield return new WaitForSecondsRealtime(1f);
        foreach (var s in sockets) if ((int)s.itemType == slot) Put(me, s.GetComponent<Collider>().bounds.center);
        yield return Until(() => d.AllItemsInstalled, 20f);
        Check(d.AllItemsInstalled, "맵3 소켓 3개 설치 공유");
        if (slot == 0) d.TryActivateMusicBox();
        yield return Until(() => d.IsMusicBoxActivated, 12f);
        Check(d.IsMusicBoxActivated, "맵3 오르골 활성화 공유");
        yield return new WaitForSecondsRealtime(1f);
        if (slot == 2) Put(me, FindFirstObjectByType<ToyWorldExitTrigger>().GetComponent<Collider>().bounds.center);
        yield return Until(() => L.Cleared.Value, 20f);
        Check(L.Cleared.Value, "맵3 출구 도달로 전원 클리어");
    }

    // 맵 2: 추격자 공유 → 슬롯 1이 열쇠 획득 → 슬롯 0이 R → 슬롯 2가 출구 도달로 전원 클리어
    IEnumerator CheckKitchenMp(MpLobby L, int slot)
    {
        var kitchen = FindFirstObjectByType<Map2KitchenDraft.KitchenDraftController>();
        var handoff = FindFirstObjectByType<Map2KitchenDraft.KitchenMapHandoff>();
        var me = NetProtoRunner.LocalPlayer;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        double clock = NetworkManager.Singleton.ServerTime.Time - (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        Debug.Log($"[Smoke] INFO 서버시계-UTC {clock:F3}");
        var rat = FindObjectsByType<Map2KitchenDraft.KitchenPursuit>(FindObjectsSortMode.None)
            .Where(p => p.Kind == Map2KitchenDraft.CreatureKind.Rat).OrderByDescending(p => p.PathLength).ThenBy(p => p.name).First();
        var ratRenderer = rat.GetComponentInChildren<Renderer>(true);
        if (slot == 2)
        {
            // 쥐는 플레이어가 출발 간격(spawnGap)보다 앞서야 움직인다 — 경로의 80% 지점에 선다
            var along = (Vector3)typeof(Map2KitchenDraft.KitchenPursuit).GetMethod("PointAt", flags).Invoke(rat, new object[] { rat.PathLength * 0.8f });
            Map2KitchenDraft.KitchenDraftController.MoveActor(me, along + Vector3.up * 0.6f);
        }
        yield return Until(() => ratRenderer.enabled, 15f);
        Check(ratRenderer.enabled, "맵2 추격자 표시 공유");
        yield return Until(() => (rat.transform.position - rat.route[0]).magnitude > 1f, 12f);
        Check((rat.transform.position - rat.route[0]).magnitude > 1f, $"맵2 추격자 이동 공유 {rat.transform.position:F1}");
        yield return new WaitForSecondsRealtime(2f);
        if (slot == 1)
        {
            Map2KitchenDraft.KitchenDraftController.MoveActor(me, kitchen.key.position);
            yield return new WaitForSecondsRealtime(0.5f); // 서버의 내 몸 복사본이 도착할 시간
            Map2KitchenDraft.KitchenDraftController.MoveActor(me, kitchen.key.position);
            yield return new WaitForSecondsRealtime(0.5f);
            kitchen.TryCollectKey(me);
        }
        yield return Until(() => kitchen.HasKey, 20f);
        Check(kitchen.HasKey && !kitchen.key.gameObject.activeSelf && !kitchen.doorBlocker.enabled, "맵2 열쇠·문 공유");
        yield return new WaitForSecondsRealtime(3f);
        if (slot == 0)
        {
            var shape = me.GetComponent<PlayerShapeController>();
            float s0 = me.localScale.x;
            shape.ToggleScale(PlayerShapeController.EScaleState.Grown);
            yield return new WaitForSecondsRealtime(1f);
            kitchen.RecoverOwnShape();
            var start = kitchen.chapterStarts[kitchen.CurrentChapter].position;
            yield return new WaitForFixedUpdate();
            Check(Vector3.Distance(me.position, start) < 1.5f && Mathf.Abs(me.localScale.x - s0) < 0.001f && kitchen.HasKey, "맵2 R: 내 도형만 구역 시작점·기본 크기, 열쇠 유지");
        }
        if (slot == 2)
        {
            var ledge = (BoxCollider)typeof(Map2KitchenDraft.KitchenMapHandoff).GetField("upperLedge", flags).GetValue(handoff);
            Map2KitchenDraft.KitchenDraftController.MoveActor(me, ledge.bounds.center);
            yield return new WaitForSecondsRealtime(1.5f);
            Map2KitchenDraft.KitchenDraftController.MoveActor(me, handoff.GetComponent<Collider>().bounds.center);
        }
        yield return Until(() => L.Cleared.Value, 25f);
        Check(L.Cleared.Value, "맵2 한 명 도착으로 전원 클리어");
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
            yield return new WaitForSecondsRealtime(12f); // 다른 PC가 확인할 시간
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
        if (map == 1) yield return CheckKitchenMp(L, slot);
        else if (map == 2) yield return CheckToyWorldMp(L, slot);
        else if (map == 3) { yield return CheckHazardsMp(slot, host); yield return CheckScaleResetMp(L, slot, host, map); yield return CheckLabPuzzlesMp(L, slot, host); }
        else yield return CheckScaleResetMp(L, slot, host, map);
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
