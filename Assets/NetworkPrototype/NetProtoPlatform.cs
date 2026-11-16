using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>[멀티 M1 프로토타입 R12] 서버가 앞뒤로 움직이는 키네마틱 발판. 오너 권한 몸이 그 위에 서 있을 때 미끄러지는지 본다.
/// 클라이언트의 발판은 보간 이동이라 Rigidbody 속도가 0 → PlayerGroundContact.GroundVelocity가 0이 된다(가설). -platrb로 NetworkRigidbody.UseRigidBodyForMotion을 켜서 비교.</summary>
public class NetProtoPlatform : NetworkBehaviour
{
    public static bool UseRbMotion;
    public const float Amplitude = 5f, Period = 6f;
    Rigidbody rb;
    Vector3 origin; float t0;
    public Vector3 CopyVelocity; Vector3 prevPos;

    void Awake()
    {
        var nrb = GetComponent<NetworkRigidbody>();
        if (nrb != null) nrb.UseRigidBodyForMotion = UseRbMotion;
    }

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();
        origin = transform.position; t0 = Time.time;
    }

    void FixedUpdate()
    {
        if (rb != null && !IsServer)
        {
            // 클라이언트 복사본: 보간된 위치의 변화로 속도를 추정(약한 저역통과) → 몸이 발판을 따라가게 PlayerGroundContact에 공급
            Vector3 raw = (transform.position - prevPos) / Time.fixedDeltaTime;
            prevPos = transform.position;
            CopyVelocity = Vector3.Lerp(CopyVelocity, raw, 0.3f);
        }
        if (!IsServer || rb == null) return;
        // NetworkRigidbody가 권한 쪽에서 isKinematic을 풀어 버린다(실측: 발판이 중력으로 추락) → 매 틱 고정
        rb.useGravity = false; rb.isKinematic = true;
        rb.MovePosition(origin + Vector3.forward * (Amplitude * Mathf.Sin(2f * Mathf.PI * (Time.time - t0) / Period)));
    }
}
