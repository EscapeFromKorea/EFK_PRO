using Unity.Netcode.Components;

/// <summary>[멀티 M1 프로토타입] OwnerAuth=true면 소유 클라이언트가 위치 권한을 갖는다(내 조작 즉시 반영). false면 서버 권한(기존).</summary>
public class NetProtoTransform : NetworkTransform
{
    public static bool OwnerAuth;
    protected override bool OnIsServerAuthoritative() => !OwnerAuth;
}
