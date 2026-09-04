using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyRailDerailPad : MonoBehaviour
{
    public ToyRailCart cart;
    public Vector3 lateralImpulse = new Vector3(0f, 1.5f, 5f);
    public bool oneShot = true;

    private bool used;

    private void Awake()
    {
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogError("[ToyWorld] Derail pad must be a trigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if ((used && oneShot) || other.GetComponentInParent<PlayerMover>() == null || cart == null) return;
        used = true;
        cart.Derail();
        Rigidbody body = cart.GetComponent<Rigidbody>();
        body.AddForce(transform.TransformDirection(lateralImpulse), ForceMode.VelocityChange);
    }
}
