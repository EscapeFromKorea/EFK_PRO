using Unity.Netcode;
using UnityEngine;

/// <summary>[멀티 M1 프로토타입 R12] 진짜 RailCart를 서버 권한으로 굴린다. 서버가 레일을 코드로 만들어 연결하고,
/// 일정 시간 뒤 태엽 신호 대신 직접 구동 신호를 준다. 클라이언트는 위치만 받아 그린다.</summary>
public class NetProtoCart : NetworkBehaviour
{
    public const float DriveDelay = 10f, TrackLength = 80f;
    RailCart cart;
    float driveAt;
    bool driving;

    public override void OnNetworkSpawn()
    {
        cart = GetComponent<RailCart>();
        var rider = GetComponent<RailCartRider>();
        if (rider != null) rider.enabled = false; // 탑승은 NetProtoPlayer가 흉내낸다(InteractionController 없음)
        if (!IsServer) { cart.enabled = false; return; }
        var po = new GameObject("NetProtoRail");
        var path = po.AddComponent<RailPath>();
        Transform Wp(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(po.transform, false); t.position = p; return t; }
        var a = Wp("A", transform.position);
        var b = Wp("B", transform.position + Vector3.forward * TrackLength);
        path.waypoints = new[] { a, b };
        path.segmentMaxSafeSpeed = new[] { 99f };
        path.curveControlPoints = new Transform[] { null };
        cart.path = path;
        cart.releaseDelay = 0.1f;
        driveAt = Time.realtimeSinceStartup + DriveDelay;
    }

    void FixedUpdate()
    {
        if (!IsServer || driving || Time.realtimeSinceStartup < driveAt) return;
        driving = true;
        cart.ApplyOutput(1f, 1f);
        cart.OnCrankSwing(1f);
        Debug.Log("[NetProto] 카트 구동 시작");
    }
}
