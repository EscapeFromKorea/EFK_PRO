using UnityEngine;

/// <summary>
/// [멀티 M4 분류 B1] 서버가 시뮬레이션하는 움직이는 바디(리프트·시소 등)의 자세를 참가자 PC에 전한다.
/// 기믹이 Awake에서 <see cref="Ensure"/>를 부르면 같은 오브젝트에 붙는다(멀티 판일 때만 — 오프라인은 아무것도 안 붙는다).
/// 서버는 매 물리 스텝 자세를 올리고, 참가자 PC는 그 바디를 키네마틱으로 바꿔 받은 자세를 따라간다(MovePosition이라 위에 탄 몸이 속도를 받는다).
/// 참가자 PC에서 기믹 자체의 로직(구동·토크·판정)은 각 기믹이 <c>MpSync.IsClient</c>로 건너뛴다.
/// </summary>
[DefaultExecutionOrder(-50)]
public class MpBodySync : MonoBehaviour, IMpPosed
{
    Rigidbody rb;
    uint id;
    bool hasTarget;
    /// <summary>참가자 PC에서 이 바디를 서버 자세로 끌지 않는다(내가 들고 있는 블록처럼 로컬이 직접 움직일 때).</summary>
    public bool Suspend;
    readonly System.Collections.Generic.Dictionary<Rigidbody, float> riders = new System.Collections.Generic.Dictionary<Rigidbody, float>();
    readonly System.Collections.Generic.List<Rigidbody> dead = new System.Collections.Generic.List<Rigidbody>();
    Vector3 tp;
    Quaternion tr;

    public static void Ensure(Component c)
    {
        if (!MpSync.Active || c.GetComponent<Rigidbody>() == null || c.GetComponent<MpBodySync>() != null) return;
        c.gameObject.AddComponent<MpBodySync>();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        id = MpSync.Register(this);
        if (MpSync.IsClient && rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    void OnDestroy() => MpSync.Unregister(id, this);

    public void OnMpPose(Vector3 position, Quaternion rotation)
    {
        tp = position;
        tr = rotation;
        hasTarget = true;
    }

    void FixedUpdate()
    {
        if (rb == null) return;
        if (MpSync.IsServer) { MpSync.PublishPose(id, rb.position, rb.rotation); return; }
        if (!MpSync.IsClient || !hasTarget || Suspend) return;
        Vector3 before = rb.position;
        Quaternion qBefore = rb.rotation;
        Vector3 next = Vector3.Lerp(before, tp, 0.5f);
        Quaternion qNext = Quaternion.Slerp(qBefore, tr, 0.5f);
        rb.MovePosition(next);
        rb.MoveRotation(qNext);
        CarryRiders(next - before, Quaternion.Angle(qBefore, qNext));
    }

    // 서버 PC에서는 기믹이 리프트·구름 위 라이더를 직접 옮긴다(r.position += delta). 참가자 PC에서는 같은 일을 여기서 한다 —
    // 내 몸이 이 바디 위에 올라타 있으면(충돌 접촉) 바디가 움직인 만큼 같이 옮긴다. 회전하는 바디(시소·회전판)는 마찰에 맡긴다.
    void CarryRiders(Vector3 delta, float rotDegrees)
    {
        if (riders.Count == 0 || rotDegrees > 0.2f || delta.sqrMagnitude < 1e-10f) return;
        dead.Clear();
        foreach (var kv in riders)
        {
            if (kv.Key == null || Time.time - kv.Value > 0.2f) { dead.Add(kv.Key); continue; }
            kv.Key.position += PlayerGroundContact.CarryDelta(kv.Key, delta);
        }
        foreach (var r in dead) riders.Remove(r);
    }

    void OnCollisionStay(Collision c)
    {
        if (!MpSync.IsClient) return;
        Rigidbody other = c.rigidbody;
        if (other == null || !MpSync.IsOwnBody(other)) return;
        for (int i = 0; i < c.contactCount; i++)
            if (c.GetContact(i).normal.y <= -0.5f) { riders[other] = Time.time; return; } // 위에서 누르는 접촉 = 올라타 있음
    }
}
