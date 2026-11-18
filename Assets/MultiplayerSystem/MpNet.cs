using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [멀티 M2] 세션 계층 — 방 만들기/코드 참가/나가기를 이 한 클래스에 가둔다(PRD D3: Steam 전환 때 여기만 교체).
/// 화면(MpMenu)은 이 클래스의 결과만 받아 그린다(H-05). NetworkManager는 여기서 코드로 조립하고 시작은 Sessions SDK가 한다.
/// 같은 PC에서 두 인스턴스를 시험할 땐 실행 인자 -mpprofile 이름 을 서로 다르게 준다(같은 익명 계정이면 "이미 멤버" 거부).
/// </summary>
public class MpNet : MonoBehaviour
{
    public const string HomeScene = "MpHome";
    public const int MaxPlayers = 3;

    public static MpNet I;
    static readonly string Token = Guid.NewGuid().ToString("N"); // E-07: 이 실행 동안 같은 사람임을 증명(나갔다 다시 들어와도 같다)
    public static string Message; // 홈으로 돌아왔을 때 보여 줄 메시지(H-04)

    public NetworkManager NM;
    public ISession Session;
    bool leaving;

    public static MpNet Ensure()
    {
        if (I != null) return I;
        var go = new GameObject("MpNet");
        DontDestroyOnLoad(go);
        I = go.AddComponent<MpNet>();
        I.Build();
        Application.runInBackground = true; // 포커스를 잃은 창이 멈춰 접속이 끊기지 않게
        return I;
    }

    void Build()
    {
        // NetworkManager는 설정을 먼저 넣고 켜야 하므로 비활성 상태로 조립한다
        var go = new GameObject("MpNM");
        go.SetActive(false);
        var utp = go.AddComponent<UnityTransport>();
        NM = go.AddComponent<NetworkManager>();
        NM.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = utp,
            EnableSceneManagement = false, // M2: 맵은 각자 로컬로 로드(씬 동기화는 M3 이후)
            TickRate = 50,
            PlayerPrefab = null,
            ConnectionApproval = true,
        };
        NM.ConnectionApprovalCallback = Approve;
        DontDestroyOnLoad(go);
        go.SetActive(true);
        NM.AddNetworkPrefab(Resources.Load<GameObject>("MpLobby"));
        foreach (var n in MpLobby.ShapePrefabs) NM.AddNetworkPrefab(Resources.Load<GameObject>(n));
        NM.OnClientDisconnectCallback += OnDisconnect;
        NM.OnClientConnectedCallback += id => { if (NM.IsServer && MpLobby.I != null) MpLobby.I.ServerRejoined(id); };
        NetProtoTransform.OwnerAuth = true; // 몸은 오너 권한(D1)
    }

    // R-02·R-03: 버전 불일치·정원 초과·이미 시작된 방은 구분된 사유로 거부
    void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
    {
        res.CreatePlayerObject = false;
        if (req.ClientNetworkId == NM.LocalClientId) { res.Approved = true; return; } // 호스트 자신
        string[] parts = Encoding.UTF8.GetString(req.Payload).Split('|');
        string theirs = parts[0], token = parts.Length > 1 ? parts[1] : null;
        if (MpLobby.I != null) MpLobby.I.ServerNoteToken(req.ClientNetworkId, token);
        bool rejoin = MpLobby.I != null && MpLobby.I.Started.Value && MpLobby.I.ServerCanRejoin(token);
        string why = null;
        if (theirs != Application.version) why = $"버전이 다릅니다(방장 {Application.version} / 내 {theirs})";
        else if (NM.ConnectedClientsIds.Count >= MaxPlayers) why = "정원(3명)이 가득 찼습니다";
        else if (MpLobby.I != null && MpLobby.I.Started.Value && !rejoin) why = "이미 시작된 방입니다";
        res.Approved = why == null;
        res.Reason = why ?? "";
    }

    void OnDisconnect(ulong id)
    {
        if (NM.IsServer) { if (MpLobby.I != null && id != NM.LocalClientId) MpLobby.I.ServerFree(id); return; }
        if (id == NM.LocalClientId && !leaving) // 클라이언트: 방장이 나갔거나 끊김
            _ = Leave("방장이 나갔거나 연결이 끊겼습니다" + (string.IsNullOrEmpty(NM.DisconnectReason) ? "" : ": " + NM.DisconnectReason));
    }

    static async Task InitAuth()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var opt = new InitializationOptions();
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-mpprofile");
            if (i >= 0 && i + 1 < args.Length) opt.SetProfile(args[i + 1]);
            await UnityServices.InitializeAsync(opt);
        }
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    /// <summary>방 만들기. 성공하면 null, 실패하면 사용자에게 보여 줄 메시지.</summary>
    public async Task<string> CreateRoom()
    {
        try
        {
            await InitAuth();
            Session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork());
            Instantiate(Resources.Load<GameObject>("MpLobby")).GetComponent<NetworkObject>().Spawn();
            return null;
        }
        catch (Exception e) { Debug.LogWarning("[Mp] 방 만들기 실패: " + e); await Leave(null, false); return Describe(e); }
    }

    /// <summary>코드로 방 입장. 성공하면 null, 실패하면 사용자에게 보여 줄 메시지.</summary>
    public async Task<string> JoinRoom(string code)
    {
        try
        {
            await InitAuth();
            NM.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Application.version + "|" + Token);
            Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
            // 연결 승인(버전·정원)은 세션 참가 뒤 NGO 단계에서 거부될 수 있다
            for (int i = 0; i < 100 && !NM.IsConnectedClient && NM.IsListening; i++) await Task.Delay(100);
            if (!NM.IsConnectedClient) throw new Exception(string.IsNullOrEmpty(NM.DisconnectReason) ? "연결하지 못했습니다" : NM.DisconnectReason);
            return null;
        }
        catch (Exception e) { Debug.LogWarning("[Mp] 방 입장 실패: " + e); await Leave(null, false); return Describe(e); }
    }

    /// <summary>R-10: 클라이언트는 슬롯이 비워지고 끊김, 호스트가 나가면 방이 해산돼 전원 홈으로. toHome=false면 씬 전환 없이 정리만.</summary>
    public async Task Leave(string msg, bool toHome = true)
    {
        if (leaving) return;
        leaving = true;
        Message = msg;
        LevelManager.OnClearOverride = null;
        PauseMenuController.OnLeaveToMain = null; // 홈에서는 "메인화면으로 나가기"를 다시 끈다
        try
        {
            if (Session != null)
            {
                if (Session.IsHost) await Session.AsHost().DeleteAsync(); else await Session.LeaveAsync();
            }
        }
        catch (Exception e) { Debug.LogWarning("[Mp] 세션 정리 중 오류(무시): " + e.Message); }
        Session = null;
        foreach (var pl in FindObjectsByType<NetProtoPlayer>(FindObjectsSortMode.None)) Destroy(pl.gameObject); // DDOL로 남은 도형 정리
        if (NM != null) { Destroy(NM.gameObject); await Task.Yield(); } // 같은 NetworkManager 재사용은 불안정 → 새로 조립
        Build();
        leaving = false;
        if (toHome && SceneManager.GetActiveScene().name != HomeScene) SceneManager.LoadScene(HomeScene);
    }

    static string Describe(Exception e)
    {
        if (e is SessionException se)
            switch (se.Error)
            {
                case SessionError.SessionNotFound:
                case SessionError.InvalidSessionIdentifier:
                case SessionError.InvalidParameter: return "잘못된 코드이거나 이미 닫힌 방입니다";
                case SessionError.Forbidden: return "참가할 수 없습니다(정원 초과 등)";
                case SessionError.SessionConflict: return "이미 이 방에 참가 중입니다";
                case SessionError.RateLimitExceeded: return "요청이 너무 많습니다. 잠시 후 다시 시도하세요";
                default: return se.Message;
            }
        return e.Message;
    }
}
