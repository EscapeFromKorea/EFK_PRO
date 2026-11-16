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
    public static readonly string[] MapScenes = { "Map1_DayDream", "Map2_Kitchen", "Map3_ToyWorld", "Map4_Master" };
    public static readonly string[] MapNames = { "맵 1", "맵 2", "맵 3", "맵 4" };

    public static MpLobby I;

    public NetworkList<ulong> Owners = new NetworkList<ulong>();
    public NetworkList<bool> Ready = new NetworkList<bool>();
    public NetworkVariable<int> Map = new NetworkVariable<int>(0);
    public NetworkVariable<bool> Started = new NetworkVariable<bool>(false);
    int loadedCount;

    public override void OnNetworkSpawn()
    {
        I = this;
        DontDestroyOnLoad(gameObject); // 맵 씬을 로드하면 홈 씬이 언로드되는데, 로비가 같이 파괴되면 시작 알림(LoadedRpc)이 호스트에 못 간다
        if (IsServer) for (int i = 0; i < 3; i++) { Owners.Add(Empty); Ready.Add(false); }
        Started.OnValueChanged += (_, now) => { if (now) MpGame.Begin(this); };
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
        loadedCount = 0;
        Started.Value = true; // 전원이 이 값 변화를 보고 각자 맵을 로드한다(MpGame)
    }

    // 맵을 다 불러온 쪽이 알린다 → 3명이 다 불러오면 서버가 도형을 스폰(스폰 위치가 맵 로드에 의존하므로)
    [Rpc(SendTo.Server)]
    public void LoadedRpc()
    {
        if (++loadedCount < 3) return;
        for (int slot = 0; slot < 3; slot++)
        {
            var go = Instantiate(Resources.Load<GameObject>(ShapePrefabs[slot]));
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(Owners[slot]);
            Debug.Log($"[Mp] 도형 스폰 slot={slot}({ShapeNames[slot]}) owner={Owners[slot]}");
        }
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
