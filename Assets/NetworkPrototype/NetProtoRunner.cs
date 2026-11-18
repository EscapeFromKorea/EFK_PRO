using System;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

/// <summary>
/// [멀티 M1 프로토타입] 어느 씬에서든 켜지는 일회용 테스트 진행기. 켜는 법: 실행 인자 `-netproto`, 또는
/// 에디터 메뉴 Tools/NetProto/Enable. 켜지 않으면 아무 일도 하지 않는다(기존 씬 영향 없음).
/// 인자: host | client, -ip 127.0.0.1, -port, -servbuf/-localbuf(NGO 시간 버퍼 초). 지연은 udpdelay.py 프록시로 건다.
///       -bot(클라 자동 입력), -quit 초.
/// </summary>
public class NetProtoRunner : MonoBehaviour
{
    public static Transform LocalPlayer;
    public static bool Bot;
    public static float CamYaw;

    string ip = "127.0.0.1";
    ushort port = 7777;
    double servBuf = -1, localBuf = -1;

    float quitAt = -1f;
    bool started;
    NetworkManager nm;
    Transform cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var args = Environment.GetCommandLineArgs();
        bool on = args.Contains("-netproto");
#if UNITY_EDITOR
        on |= UnityEditor.EditorPrefs.GetBool("NetProto.Enabled", false);
#endif
        if (!on) return;
        var go = new GameObject("NetProtoRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<NetProtoRunner>().Boot(args);
    }

    void Boot(string[] args)
    {
        string Arg(string k)
        {
            int i = Array.IndexOf(args, k);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        if (Arg("-ip") != null) ip = Arg("-ip");
        if (Arg("-port") != null) port = ushort.Parse(Arg("-port"));
        if (Arg("-servbuf") != null) servBuf = double.Parse(Arg("-servbuf"), System.Globalization.CultureInfo.InvariantCulture);
        if (Arg("-localbuf") != null) localBuf = double.Parse(Arg("-localbuf"), System.Globalization.CultureInfo.InvariantCulture);
        if (Arg("-quit") != null) quitAt = Time.realtimeSinceStartup + float.Parse(Arg("-quit"));
        Application.runInBackground = true; // 두 창을 띄워 직접 조작할 때, 포커스를 잃은 창이 멈춰 접속이 끊기지 않게
        Bot = args.Contains("-bot");
        NetProtoTransform.OwnerAuth = args.Contains("-ownerauth");
        Ride = args.Contains("-ride");
        Platform = args.Contains("-platform");
        if (args.Contains("-platvel"))
            PlayerGroundContact.PointVelocityOverride = (rb, p) => rb.TryGetComponent(out NetProtoPlatform pl) && !pl.IsServer ? pl.CopyVelocity : (Vector3?)null;
        NetProtoPlatform.UseRbMotion = args.Contains("-platrb");
        Shapes = args.Contains("-shapes");
        Relay = args.Contains("-relay");
        if (Arg("-map") != null) { StartCoroutine(MapThenNet(Arg("-map"), args)); return; }
        BuildWorld();
        if (args.Contains("host")) StartNet(true);
        else if (args.Contains("client")) StartNet(false);
    }

    public static Vector3 SpawnBase;
    public static bool MapMode;

    // 실제 맵 씬을 그대로 로드해 놓고(씬 동기화는 끔: 기믹은 각자 로컬로 돈다), 플레이어·조작·카메라 싱글톤만 치운 뒤 네트워크 플레이어를 스폰한다 — 접속 프로브용
    System.Collections.IEnumerator MapThenNet(string sceneName, string[] args)
    {
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
        yield return null;
        var movers = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
        SpawnBase = movers.Length > 0 ? movers.OrderBy(m => m.name).First().transform.position + Vector3.up * 0.3f : new Vector3(0, 2, 0);
        Debug.Log($"[NetProto] 맵 로드 {sceneName} 기존 플레이어 {movers.Length}개 스폰기준={SpawnBase:F2}");
        foreach (var m in movers) if (m != null) DestroyImmediate(m.gameObject);
        foreach (var sw in FindObjectsByType<PlayerControlSwitcher>(FindObjectsSortMode.None)) DestroyImmediate(sw);
        foreach (var fc in FindObjectsByType<PlayerFollowCamera>(FindObjectsSortMode.None)) DestroyImmediate(fc);
        var mc = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (mc != null) cam = mc.transform;
        MapMode = true;
        if (args.Contains("host")) StartNet(true);
        else if (args.Contains("client")) StartNet(false);
    }

    // 원래 씬을 가리고 빈 평지 월드 하나로 갈아탄다(씬 동기화는 끄므로 양쪽이 같은 월드를 직접 만든다)
    void BuildWorld()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            // 비활성화만 하면 PlayerControlSwitcher·PlayerFollowCamera의 static 인스턴스가 살아남아 새 플레이어의 조작권을 빼앗는다 → 즉시 파괴
            foreach (var r in SceneManager.GetSceneAt(i).GetRootGameObjects()) DestroyImmediate(r);
        var scene = SceneManager.CreateScene("NetProtoWorld");
        SceneManager.SetActiveScene(scene);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(200, 1, 200);
        ground.transform.position = new Vector3(0, -0.5f, 0);
        new GameObject("Sun").AddComponent<Light>().type = LightType.Directional;
        var c = new GameObject("Cam").AddComponent<Camera>();
        c.tag = "MainCamera";
        c.gameObject.AddComponent<AudioListener>();
        cam = c.transform;
    }

    void StartNet(bool host)
    {
        var prefab = Resources.Load<GameObject>("NetProtoPlayer");
        if (prefab == null)
        {
            Debug.LogError("[NetProto] Resources/NetProtoPlayer.prefab 없음 — 메뉴 Tools/NetProto/Create Player Prefab 먼저");
            return;
        }
        // NetworkManager는 설정을 먼저 넣고 켜야 하므로 비활성 상태로 조립한다
        var go = new GameObject("NetProtoNM");
        go.SetActive(false);
        var utp = go.AddComponent<UnityTransport>();
        nm = go.AddComponent<NetworkManager>();
        nm.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = utp,
            EnableSceneManagement = false,
            TickRate = 50,
            PlayerPrefab = prefab,
        };
        DontDestroyOnLoad(go);
        go.SetActive(true);
        nm.AddNetworkPrefab(prefab);
        var cartPrefab = Ride ? Resources.Load<GameObject>("NetProtoCart") : null;
        if (cartPrefab != null) nm.AddNetworkPrefab(cartPrefab);
        var platPrefab = Platform ? Resources.Load<GameObject>("NetProtoPlatform") : null;
        if (platPrefab != null) nm.AddNetworkPrefab(platPrefab);
        // 도형 3종: 서버가 접속 순서대로 구·정육면체·정사면체를 직접 스폰(PlayerPrefab 자동 스폰은 끈다)
        var shapePrefabs = new GameObject[0];
        if (Shapes)
        {
            shapePrefabs = new[] { prefab, Resources.Load<GameObject>("NetProtoPlayer_Cube"), Resources.Load<GameObject>("NetProtoPlayer_Tetra") };
            foreach (var sp in shapePrefabs.Skip(1)) nm.AddNetworkPrefab(sp);
            nm.NetworkConfig.PlayerPrefab = null;
            nm.OnClientConnectedCallback += id =>
            {
                if (!nm.IsServer) return;
                var go2 = Instantiate(shapePrefabs[(int)(id % 3)]);
                go2.GetComponent<NetworkObject>().SpawnAsPlayerObject(id);
                Debug.Log($"[NetProto] 도형 스폰 clientId={id} 프리팹={shapePrefabs[(int)(id % 3)].name}");
            };
        }
        utp.SetConnectionData(ip, port, host ? "0.0.0.0" : null);
        nm.OnClientConnectedCallback += id => Debug.Log($"[NetProto] 접속됨 clientId={id} (내 id={nm.LocalClientId})");
        nm.OnClientDisconnectCallback += id => Debug.Log($"[NetProto] 접속 끊김 clientId={id} 사유='{nm.DisconnectReason}'");
        if (Relay) { started = true; _ = RelayStart(host); }
        else started = host ? nm.StartHost() : nm.StartClient();
        if (host && platPrefab != null)
        {
            var pf = Instantiate(platPrefab, new Vector3(3f, 3.25f, 0f), Quaternion.identity);
            pf.GetComponent<NetworkObject>().Spawn();
        }
        if (host && cartPrefab != null)
        {
            var cartGo = Instantiate(cartPrefab, new Vector3(3f, 0f, 4f), Quaternion.identity);
            cartGo.GetComponent<NetworkObject>().Spawn();
        }
        // NGO 기본 시간 버퍼(각 0.05초)가 순수 서버 권한의 기준선 지연을 만든다 → 인자로 조정해 비교
        if (servBuf >= 0) nm.NetworkTimeSystem.ServerBufferSec = servBuf;
        if (localBuf >= 0) nm.NetworkTimeSystem.LocalBufferSec = localBuf;
        Debug.Log($"[NetProto] 버퍼 server={nm.NetworkTimeSystem.ServerBufferSec} local={nm.NetworkTimeSystem.LocalBufferSec}");
        Debug.Log($"[NetProto] {(host ? "Host" : "Client")} 시작={started} port={port}");
    }

    // [A-5 스파이크] Multiplayer Services SDK(Sessions)로 Relay 위에 호스트/클라이언트를 붙인다. 참가 코드는 임시 파일로 주고받는다(같은 PC 테스트용).
    async System.Threading.Tasks.Task RelayStart(bool host)
    {
        string codeFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netproto_relay_code.txt");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 같은 PC의 호스트/클라가 같은 익명 계정을 쓰면 "이미 멤버" 거부 → 역할별 프로필 분리
            var opt = new InitializationOptions();
            opt.SetProfile(host ? "host" : "client");
            await UnityServices.InitializeAsync(opt);
            Debug.Log($"[NetProto] UGS 초기화 완료 {sw.ElapsedMilliseconds}ms");
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[NetProto] 익명 로그인 완료 {sw.ElapsedMilliseconds}ms");
            if (host)
            {
                if (System.IO.File.Exists(codeFile)) System.IO.File.Delete(codeFile);
                var s = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = 3 }.WithRelayNetwork());
                System.IO.File.WriteAllText(codeFile, s.Code);
                Debug.Log($"[NetProto] 릴레이 세션 생성 완료 code={s.Code} {sw.ElapsedMilliseconds}ms network={s.Network.State}");
            }
            else
            {
                for (int i = 0; i < 300 && !System.IO.File.Exists(codeFile); i++) await System.Threading.Tasks.Task.Delay(100);
                string code = System.IO.File.ReadAllText(codeFile).Trim();
                var s = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Debug.Log($"[NetProto] 릴레이 세션 참가 완료 code={code} {sw.ElapsedMilliseconds}ms network={s.Network.State}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[NetProto] 릴레이 실패: " + e);
        }
    }

    float nextStatus;

    void Update()
    {
        if (nm != null && Time.realtimeSinceStartup > nextStatus)
        {
            nextStatus = Time.realtimeSinceStartup + 2f;
            Debug.Log($"[NetProto] 상태 connected={nm.IsConnectedClient} host={nm.IsHost} 내몸={(LocalPlayer != null ? LocalPlayer.position.ToString("F2") : "없음")} 스폰수={nm.SpawnManager?.SpawnedObjects.Count}");
        }
        if (quitAt > 0 && Time.realtimeSinceStartup > quitAt)
        {
            Debug.Log("[NetProto] 종료");
            Application.Quit();
        }
        if (Input.GetKey(KeyCode.Q)) CamYaw -= 90f * Time.deltaTime;
        if (Input.GetKey(KeyCode.E)) CamYaw += 90f * Time.deltaTime;
    }

    void LateUpdate()
    {
        if (LocalPlayer == null || cam == null) return;
        var rot = Quaternion.Euler(30f, CamYaw, 0f);
        cam.SetPositionAndRotation(LocalPlayer.position + rot * new Vector3(0, 0, -9f), rot);
    }

    void OnGUI()
    {
        if (started)
        {
            GUILayout.Label(nm.IsHost ? "HOST" : "CLIENT");
            return;
        }
        ip = GUILayout.TextField(ip, GUILayout.Width(160));
        if (GUILayout.Button("Host", GUILayout.Width(160))) StartNet(true);
        if (GUILayout.Button("Client", GUILayout.Width(160))) StartNet(false);
    }

    // ── 자동 입력(-bot): 스폰 후 3~5초 전진, 6초 점프, 8~10초 우측 이동 ──
    public static Vector2 BotAxes(float spawnAt)
    {
        if (Platform) return Vector2.zero;
        float t = Time.realtimeSinceStartup - spawnAt;
        if (t >= 3f && t < 5f) return new Vector2(0, 1);
        if (t >= 8f && t < 10f) return new Vector2(1, 0);
        return Vector2.zero;
    }

    public static bool Ride, Platform, Shapes, Relay;
    static bool jumped;

    public static bool BotJumpNow(float spawnAt)
    {
        if (Platform || jumped || Time.realtimeSinceStartup - spawnAt < 6f) return false;
        jumped = true;
        return true;
    }
}
