using UnityEngine;

/// <summary>Kitchen-local adapter for a child ground detector on a compound Rigidbody.
/// Relays real collision contacts, never invents a ground hit or moves the body.
/// Team PlayerGroundContact stays unchanged. Remove when that component owns its root callbacks.
/// Depends on the existing PlayerGroundContact.OnCollisionStay(Collision) Unity message.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class V3_GroundContactRelay : MonoBehaviour
{
    public PlayerGroundContact receiver;
    Rigidbody body;
    public int forwardedContacts { get; private set; }
    void Awake() { body=GetComponent<Rigidbody>(); }
    void FixedUpdate()
    {
        // A sleeping body gets no Stay callbacks. Wake only when its detector has lost support;
        // the next physics step still has to supply an actual contact before a jump is permitted.
        if(Valid() && body.IsSleeping() && !receiver.IsGrounded) body.WakeUp();
    }
    bool Valid()
    {
        if(receiver==null || !receiver.isActiveAndEnabled || receiver.gameObject==gameObject) return false;
        var c=receiver.GetComponent<Collider>();
        return c!=null && c.enabled && c.attachedRigidbody==body;
    }
    void OnCollisionStay(Collision collision)
    {
        if(!Valid()) return;
        receiver.SendMessage("OnCollisionStay",collision,SendMessageOptions.RequireReceiver);
        forwardedContacts++;
    }
}
