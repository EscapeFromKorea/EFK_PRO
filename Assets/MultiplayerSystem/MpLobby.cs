using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [멀티 M2] 로비 상태(도형 슬롯 3개·준비·시작 맵·시작 여부). 호스트가 판정하고 전원이 읽는다(R-04~R-09).
/// 그리는 건 MpMenu — 이 클래스는 상태와 규칙만 가진다(H-05).
/// </summary>
public class MpLobby : NetworkBehaviour
{
    public const ulong Empty = ulong.MaxValue;
    // 슬롯 순서 = 프리팹 순서. M3에서 정식 도형 프리팹으로 교체
    public static readonly string[] ShapeNames = { "구", "정육면체", "정사면체" };
    public static readonly string[] ShapePrefabs = { "NetProtoPlayer", "NetProtoPlayer_Cube", "NetProtoPlayer_Tetra" };
    // R-12: 선택 가능한 맵 목록은 여기 한 곳에서만 정한다(정식 단계엔 "클리어한 맵만"으로 바꿀 자리)
    public static readonly string[] MapScenes = {
        "Assets/Scenes/Map1_DayDream/Map1_DayDream.unity",
        "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenPlayable.unity",
        "Assets/Scenes/Map3_ToyWorld/Scenes/Map3_ToyWorld.unity",
        "Assets/Scenes/Map4_Laboratory/Scenes/Map4_Master.unity" }; // LevelManager의 경로와 같아야 클리어 판정(CurrentIndex)이 맞는다
    public static readonly string[] MapNames = { "맵 1", "맵 2", "맵 3", "맵 4" };

    public static MpLobby I;

    public NetworkList<ulong> Owners = new NetworkList<ulong>();
    public NetworkList<bool> Ready = new NetworkList<bool>();
    public NetworkVariable<int> Map = new NetworkVariable<int>(0);
    public NetworkVariable<bool> Started = new NetworkVariable<bool>(false);
    // M3(b): Round가 오를 때마다 전원이 Map.Value 맵을 (다시) 로드한다. Cleared는 호스트가 판정한 클리어 상태(S-01)
    public NetworkVariable<int> Round = new NetworkVariable<int>(0);
    public NetworkVariable<bool> Cleared = new NetworkVariable<bool>(false);
    // M4: 맵 씬 기믹의 서버 상태 저장소(MpSync). 키 = 기믹ID<<4 | 채널. 늦게 합류한 PC도 NetworkList로 현재값을 받는다
    public NetworkList<MpVal> SyncVals = new NetworkList<MpVal>();
    readonly System.Collections.Generic.Dictionary<uint, int> syncIdx = new System.Collections.Generic.Dictionary<uint, int>();

    public void ServerSet(uint key, float v)
    {
        if (!IsServer) return;
        if (syncIdx.TryGetValue(key, out int i))
        {
            if (Mathf.Abs(SyncVals[i].V - v) < 1e-4f) return;
            SyncVals[i] = new MpVal { Key = key, V = v };
        }
        else { syncIdx[key] = SyncVals.Count; SyncVals.Add(new MpVal { Key = key, V = v }); }
    }

    // 움직이는 바디의 자세(MpBodySync). 0.2cm·0.2도 미만의 변화는 보내지 않는다
    public NetworkList<MpPose> PoseVals = new NetworkList<MpPose>();
    readonly System.Collections.Generic.Dictionary<uint, int> poseIdx = new System.Collections.Generic.Dictionary<uint, int>();

    public void ServerSetPose(uint id, Vector3 p, Quaternion r)
    {
        if (!IsServer) return;
        if (poseIdx.TryGetValue(id, out int i))
        {
            var cur = PoseVals[i];
            if ((cur.P - p).sqrMagnitude < 4e-6f && Quaternion.Angle(cur.R, r) < 0.2f) return;
            PoseVals[i] = new MpPose { Key = id, P = p, R = r };
        }
        else { poseIdx[id] = PoseVals.Count; PoseVals.Add(new MpPose { Key = id, P = p, R = r }); }
    }

    public void ServerSyncClear() { if (!IsServer) return; SyncVals.Clear(); syncIdx.Clear(); PoseVals.Clear(); poseIdx.Clear(); }

    // 서버 → 몸 주인 PC: 이 몸을 체크포인트로 복귀시켜라(낙하 판정 등을 서버가 했을 때)
    [Rpc(SendTo.SpecifiedInParams)]
    public void RespawnBodyRpc(ulong networkObjectId, RpcParams rpcParams = default)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var no) || !no.IsOwner) return;
        var rc = FindFirstObjectByType<RespawnController>();
        if (rc != null) rc.RespawnPlayer(no.gameObject);
    }

    // 서버 → 정육면체 주인 PC: 도킹된 블록의 몸 기준 상대 자세. 주인 PC가 도킹 블록을 로컬에서 직접 따라오게 한다(지연 없이)
    [Rpc(SendTo.SpecifiedInParams)]
    public void DockInfoRpc(uint blockId, bool begin, ulong cubeNetId, Vector3 relPos, Quaternion relRot, RpcParams rpcParams = default)
    {
        if (MpSync.Find(blockId) is SnapBlock sb) sb.MpLocalDock(begin, cubeNetId, relPos, relRot);
    }

    /// <summary>몸 주인 PC → 서버 이벤트(레버를 민 방향 등). 호출자가 몸 주인인지는 호출 쪽이 확인한다.</summary>
    [Rpc(SendTo.Server)]
    public void SyncEventRpc(uint id, int kind, Vector3 data, uint arg, RpcParams rpcParams = default)
    {
        // NaN·무한대는 기믹 상태를 영구히 오염시킨다(Clamp(NaN)=NaN) — 입구에서 버린다
        if (!float.IsFinite(data.x) || !float.IsFinite(data.y) || !float.IsFinite(data.z)) return;
        MpSync.SenderId = rpcParams.Receive.SenderClientId;
        try { MpSync.Dispatch(id, kind, data, arg); }
        finally { MpSync.SenderId = ulong.MaxValue; }
    }
    readonly System.Collections.Generic.HashSet<ulong> loaded = new System.Collections.Generic.HashSet<ulong>();
    // E-07 재접속: 입장 때 낸 토큰을 서버가 기억해 두었다가, 시작 후 끊긴 슬롯은 같은 토큰으로 돌아온 사람에게만 돌려준다
    readonly System.Collections.Generic.Dictionary<ulong, string> tokenOf = new System.Collections.Generic.Dictionary<ulong, string>();
    readonly string[] tokenBySlot = new string[3];
    readonly System.Collections.Generic.List<NetworkObject> shapes = new System.Collections.Generic.List<NetworkObject>();

    public override void OnNetworkSpawn()
    {
        I = this;
        DontDestroyOnLoad(gameObject); // 맵 씬을 로드하면 홈 씬이 언로드되는데, 로비가 같이 파괴되면 시작 알림(LoadedRpc)이 호스트에 못 간다
        if (IsServer) for (int i = 0; i < 3; i++) { Owners.Add(Empty); Ready.Add(false); }
        Round.OnValueChanged += (_, now) => { if (now > 0) MpGame.Begin(this); };
        PoseVals.OnListChanged += e => { if (!IsServer && (e.Type == NetworkListEvent<MpPose>.EventType.Add || e.Type == NetworkListEvent<MpPose>.EventType.Value || e.Type == NetworkListEvent<MpPose>.EventType.Insert)) { MpSync.ApplyPose(e.Value); } };
        SyncVals.OnListChanged += e => { if (!IsServer && (e.Type == NetworkListEvent<MpVal>.EventType.Add || e.Type == NetworkListEvent<MpVal>.EventType.Value || e.Type == NetworkListEvent<MpVal>.EventType.Insert)) { MpSync.Apply(e.Value); } };
        if (!IsServer && Round.Value > 0) MpGame.Begin(this); // 이미 시작된 뒤 스폰이 도착한 경우(안전망)
    }

    public override void OnNetworkDespawn() { if (I == this) I = null; }

    public int SlotOf(ulong clientId)
    {
        for (int i = 0; i < Owners.Count; i++) if (Owners[i] == clientId) return i;
        return -1;
    }

    /// <summary>R-08: 3개 슬롯이 모두 점유되고 전원 준비(접속 3명은 슬롯 3개 점유로 보장된다).</summary>
    public bool CanStart
    {
        get
        {
            if (Owners.Count < 3 || Started.Value) return false;
            for (int i = 0; i < 3; i++) if (Owners[i] == Empty || !Ready[i]) return false;
            return true;
        }
    }

    // R-05·R-06: 비어 있는 슬롯만, 먼저 도착한 요청이 이긴다. 슬롯을 바꾸려면 먼저 준비 해제. 이전 슬롯은 즉시 비운다
    [Rpc(SendTo.Server)]
    public void ClaimSlotRpc(int slot, RpcParams p = default)
    {
        ulong who = p.Receive.SenderClientId;
        if (Started.Value || slot < 0 || slot >= 3 || Owners[slot] != Empty) return;
        int mine = SlotOf(who);
        if (mine >= 0) { if (Ready[mine]) return; Owners[mine] = Empty; }
        Owners[slot] = who;
        Ready[slot] = false;
    }

    // R-07: 슬롯을 점유한 사람만 준비 토글
    [Rpc(SendTo.Server)]
    public void SetReadyRpc(bool ready, RpcParams p = default)
    {
        int mine = SlotOf(p.Receive.SenderClientId);
        if (Started.Value || mine < 0) return;
        Ready[mine] = ready;
    }

    // R-12: 개발·테스트 단계는 호스트가 맵 선택
    [Rpc(SendTo.Server)]
    public void SetMapRpc(int map, RpcParams p = default)
    {
        if (p.Receive.SenderClientId != NetworkManager.ServerClientId || Started.Value || map < 0 || map >= MapScenes.Length) return;
        Map.Value = map;
    }

    [Rpc(SendTo.Server)]
    public void StartRpc(RpcParams p = default)
    {
        if (p.Receive.SenderClientId != NetworkManager.ServerClientId || !CanStart) return;
        loaded.Clear();
        for (int i = 0; i < 3; i++) tokenBySlot[i] = tokenOf.TryGetValue(Owners[i], out var t) ? t : null;
        Started.Value = true;
        Round.Value++; // 전원이 이 값 변화를 보고 각자 맵을 로드한다(MpGame)
    }

    /// <summary>S-01: 호스트만 클리어를 확정한다(LevelManager 훅). 클라이언트의 호출은 무시.</summary>
    public void ServerNoteToken(ulong id, string token) => tokenOf[id] = token;

    /// <summary>시작 후 입장 요청이 예약 슬롯의 주인인지(그 슬롯의 사람이 지금 접속 중이 아니어야 한다).</summary>
    public bool ServerCanRejoin(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        for (int i = 0; i < 3; i++)
            if (tokenBySlot[i] == token && !Connected(Owners[i])) return true;
        return false;
    }

    /// <summary>재접속이 승인된 클라이언트가 실제로 연결되면 슬롯 주인을 새 id로 바꾼다.</summary>
    public void ServerRejoined(ulong id)
    {
        if (!tokenOf.TryGetValue(id, out var token)) return;
        for (int i = 0; i < 3; i++)
            if (tokenBySlot[i] == token && Owners[i] != id) { Owners[i] = id; return; }
    }

    public void ServerClear() { if (IsServer && Started.Value) Cleared.Value = true; }

    /// <summary>S-02: Next Stage는 방장만. 도형을 내리고 다음 맵 번호로 새 라운드를 연다. 마지막 맵이면 거부.</summary>
    [Rpc(SendTo.Server)]
    public void NextStageRpc(RpcParams p = default)
    {
        if (p.Receive.SenderClientId != NetworkManager.ServerClientId || !Cleared.Value || Map.Value + 1 >= MapScenes.Length) return;
        foreach (var s in shapes) if (s != null && s.IsSpawned) s.Despawn(true); // 씬 동기화를 껐으니 맵이 바뀌기 전에 직접 내린다
        shapes.Clear();
        loaded.Clear();
        ServerSyncClear(); // 새 맵의 기믹은 처음 상태부터
        Cleared.Value = false;
        Map.Value++;
        Round.Value++;
    }

    // 맵을 다 불러온 쪽이 알린다 → 3명이 다 불러오면 서버가 도형을 스폰(스폰 위치가 맵 로드에 의존하므로)
    [Rpc(SendTo.Server)]
    public void LoadedRpc(RpcParams p = default)
    {
        ulong who = p.Receive.SenderClientId;
        if (!loaded.Add(who)) return;
        if (shapes.Count > 0) { int mine = SlotOf(who); if (mine >= 0) SpawnShape(mine); return; } // 재접속·늦은 합류: 내 슬롯 도형만
        int expected = 0;
        for (int i = 0; i < 3; i++) if (Connected(Owners[i])) expected++; // 끊겨 있는 예약 슬롯은 기다리지 않는다
        if (loaded.Count < expected) return;
        for (int slot = 0; slot < 3; slot++)
            if (Connected(Owners[slot])) SpawnShape(slot);
    }

    bool Connected(ulong id) => NetworkManager.ConnectedClients.ContainsKey(id);

    void SpawnShape(int slot)
    {
        var go = Instantiate(Resources.Load<GameObject>(ShapePrefabs[slot]));
        var no = go.GetComponent<NetworkObject>();
        no.SpawnAsPlayerObject(Owners[slot]);
        shapes.Add(no);
        Debug.Log($"[Mp] 도형 스폰 slot={slot}({ShapeNames[slot]}) owner={Owners[slot]}");
    }

    /// <summary>접속이 끊긴 사람의 슬롯 비우기(R-10). 시작 전에만.</summary>
    public void ServerFree(ulong clientId)
    {
        if (Started.Value) return; // 시작 후 이탈 처리는 M6
        int s = SlotOf(clientId);
        if (s < 0) return;
        Owners[s] = Empty;
        Ready[s] = false;
    }
}
