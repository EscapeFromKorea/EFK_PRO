using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [멀티 M4] 맵 씬에 이미 놓인 기믹을 NetworkObject 없이 동기화하는 공용 틀(2026-11-17 결정: 공용 동기화 매니저 1개).
/// 기믹은 Awake에서 <see cref="MpSync.Register"/>로 자기를 등록하고(ID = 씬 이름+계층 경로+타입 해시, 모든 PC가 같은 씬이라 같은 값),
/// 서버는 상태를 <see cref="MpSync.Publish"/>로 올리고, 클라이언트는 <see cref="IMpSynced.OnMpValue"/>로 받아 로컬 오브젝트에 적용한다.
/// 상태 저장소는 MpLobby의 NetworkList라 늦게 합류·재접속한 PC도 Register 때 현재값을 받는다.
/// 오프라인(혼자 플레이)이면 <see cref="MpSync.Active"/>가 false라 기믹은 기존 동작 그대로다.
/// </summary>
public interface IMpSynced
{
    /// <summary>서버가 정한 값이 도착했다(클라이언트에서만 호출). channel은 기믹이 정한 번호(0~15).</summary>
    void OnMpValue(int channel, float value);
}

/// <summary>몸 주인 PC가 서버로 올리는 이벤트(예: 레버를 민 방향)를 받는 기믹.</summary>
public interface IMpEventSink
{
    void OnMpEvent(int kind, Vector3 data, uint arg);
}

/// <summary>서버가 움직이는 물리 바디(리프트·시소 등)의 자세를 올릴 때 쓴다. 참가자 PC는 키네마틱으로 따라간다(MpBodySync).</summary>
public interface IMpPosed
{
    void OnMpPose(Vector3 position, Quaternion rotation);
}

public struct MpPose : INetworkSerializable, IEquatable<MpPose>
{
    public uint Key;
    public Vector3 P;
    public Quaternion R;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref Key); s.SerializeValue(ref P); s.SerializeValue(ref R); }
    public bool Equals(MpPose o) => Key == o.Key && P == o.P && R == o.R;
}

public struct MpVal : INetworkSerializable, IEquatable<MpVal>
{
    public uint Key;
    public float V;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref Key); s.SerializeValue(ref V); }
    public bool Equals(MpVal o) => Key == o.Key && V == o.V;
}

public static class MpSync
{
    static readonly Dictionary<uint, Component> reg = new Dictionary<uint, Component>();

    /// <summary>멀티플레이 판 안인가(로비가 스폰돼 있고 라운드가 시작됨).</summary>
    public static bool Active => MpLobby.I != null && MpLobby.I.IsSpawned && MpLobby.I.Round.Value > 0;
    /// <summary>이 PC가 서버(호스트)인가. 오프라인이면 false.</summary>
    public static bool IsServer => Active && MpLobby.I.IsServer;
    /// <summary>서버가 아닌 참가자 PC인가 — 기믹 로직을 돌리지 않고 서버가 보낸 값만 적용한다. 오프라인이면 false.</summary>
    public static bool IsClient => Active && !MpLobby.I.IsServer;

    /// <summary>이 몸을 내 PC가 굴리는가. 오프라인이면 항상 true. 몸 복사본(남의 몸)에 로컬 효과를 걸면 네트워크 동기화와 충돌하므로 몸에 효과를 주는 기믹이 확인한다.</summary>
    public static bool IsOwnBody(Component c)
    {
        if (!Active) return true;
        var no = c.GetComponentInParent<NetworkObject>();
        return no != null && no.IsOwner;
    }

    /// <summary>기믹을 등록하고 ID를 돌려준다. 이미 서버가 올린 값이 있으면 곧바로 적용한다(늦게 합류·재접속).</summary>
    public static uint Register(Component c)
    {
        uint id = IdOf(c);
        if (reg.TryGetValue(id, out var old) && old != null && old != c)
            Debug.LogWarning($"[MpSync] ID 충돌 {id}: {PathOf(old)} / {PathOf(c)} — 같은 타입이 한 오브젝트에 둘이거나 해시 충돌");
        reg[id] = c;
        if (IsClient && c is IMpSynced s)
            foreach (var v in MpLobby.I.SyncVals)
                if ((v.Key >> 4) == id) s.OnMpValue((int)(v.Key & 15), v.V);
        if (IsClient && c is IMpPosed ps)
            foreach (var v in MpLobby.I.PoseVals)
                if (v.Key == id) ps.OnMpPose(v.P, v.R);
        return id;
    }

    public static void Unregister(uint id, Component c)
    {
        if (reg.TryGetValue(id, out var cur) && cur == c) reg.Remove(id);
    }

    /// <summary>서버만 호출한다. 값이 거의 안 바뀌었으면 보내지 않는다.</summary>
    public static void Publish(uint id, int channel, float value)
    {
        if (IsServer) MpLobby.I.ServerSet((id << 4) | (uint)channel, value);
    }

    /// <summary>서버만 호출한다. 거의 안 움직였으면 보내지 않는다.</summary>
    public static void PublishPose(uint id, Vector3 p, Quaternion r)
    {
        if (IsServer) MpLobby.I.ServerSetPose(id, p, r);
    }

    internal static void ApplyPose(MpPose v)
    {
        if (reg.TryGetValue(v.Key, out var c) && c is IMpPosed ps) ps.OnMpPose(v.P, v.R);
    }

    /// <summary>남의 몸에 리스폰을 걸어야 할 때(서버의 낙하 판정 등) 그 몸의 주인 PC에 복귀를 요청한다. 복귀는 주인 PC가 실행한다.</summary>
    public static void RequestRespawnAtOwner(GameObject body)
    {
        var no = body.GetComponentInParent<NetworkObject>();
        if (no == null || !Active) return;
        if (no.IsOwner) { var rc = UnityEngine.Object.FindFirstObjectByType<RespawnController>(); if (rc != null) rc.RespawnPlayer(no.gameObject); return; }
        MpLobby.I.RespawnBodyRpc(no.NetworkObjectId, MpLobby.I.NetworkManager.RpcTarget.Single(no.OwnerClientId, RpcTargetUse.Temp));
    }

    /// <summary>몸 주인 PC가 서버로 이벤트를 올린다. 서버 자신이면 곧바로 처리한다.</summary>
    public static void SendEvent(uint id, int kind, Vector3 data, uint arg = 0)
    {
        if (!Active) return;
        if (MpLobby.I.IsServer) Dispatch(id, kind, data, arg);
        else MpLobby.I.SyncEventRpc(id, kind, data, arg);
    }

    /// <summary>지금 처리 중인 이벤트를 보낸 클라이언트(서버가 RPC로 받은 경우). 서버 자신이 낸 이벤트면 MaxValue.</summary>
    public static ulong SenderId = ulong.MaxValue;

    /// <summary>이 이벤트를 보낸 PC가 그 몸(NetworkObjectId)의 주인인가. 남의 몸 이름으로 보낸 이벤트를 걸러낼 때 쓴다.</summary>
    public static bool SenderOwnsBody(ulong bodyNetId)
    {
        if (SenderId == ulong.MaxValue) return true;
        var nm = NetworkManager.Singleton;
        return nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(bodyNetId, out var no) && no != null && no.OwnerClientId == SenderId;
    }

    /// <summary>등록된 기믹을 ID로 찾는다(이벤트의 상대 기믹 지정용).</summary>
    public static Component Find(uint id) => reg.TryGetValue(id, out var c) ? c : null;

    internal static void Dispatch(uint id, int kind, Vector3 data, uint arg)
    {
        if (reg.TryGetValue(id, out var c) && c is IMpEventSink sink) sink.OnMpEvent(kind, data, arg);
    }

    internal static void Apply(MpVal v)
    {
        if (reg.TryGetValue(v.Key >> 4, out var c) && c is IMpSynced s) s.OnMpValue((int)(v.Key & 15), v.V);
    }

    static string PathOf(Component c)
    {
        var sb = new StringBuilder();
        for (var t = c.transform; t != null; t = t.parent) sb.Insert(0, "/" + t.GetSiblingIndex() + t.name);
        return c.gameObject.scene.name + sb + "#" + c.GetType().Name;
    }

    static uint IdOf(Component c)
    {
        uint h = 2166136261u; // FNV-1a
        foreach (char ch in PathOf(c)) { h ^= ch; h *= 16777619u; }
        return h & 0x0FFFFFFFu; // 채널 4비트를 붙여도 uint에 들어가게
    }
}
