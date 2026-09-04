using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public sealed class ToyRailSwitch : MonoBehaviour
{
    public Vector3 divertedLocalEuler = new Vector3(0f, 28f, 0f);
    public float rotationSpeed = 80f;

    private Rigidbody body;
    private Quaternion alignedRotation;
    private Quaternion divertedRotation;
    private bool aligned;

    public bool IsAligned => aligned;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        alignedRotation = transform.rotation;
        divertedRotation = alignedRotation * Quaternion.Euler(divertedLocalEuler);
        body.rotation = divertedRotation;
    }

    private void FixedUpdate()
    {
        Quaternion target = aligned ? alignedRotation : divertedRotation;
        body.MoveRotation(Quaternion.RotateTowards(body.rotation, target, rotationSpeed * Time.fixedDeltaTime));
    }

    public void SetAligned(bool value) => aligned = value;

    private void OnPuzzleReset() => aligned = false;
}
