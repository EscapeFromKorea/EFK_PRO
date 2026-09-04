using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class WindUpLift : MonoBehaviour, IWindUpReceiver
{
    public Vector3 travel = new Vector3(0f, 5f, 0f);
    public float progressPerPowerSecond = 0.18f;
    public float moveSpeed = 4f;

    private Rigidbody body;
    private Vector3 startPosition;
    private float targetProgress;

    public float Progress => targetProgress;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        startPosition = body.position;
    }

    public void ApplyWindUpPower(float signedPower, float fixedDeltaTime)
    {
        targetProgress = Mathf.Clamp01(targetProgress + signedPower * progressPerPowerSecond * fixedDeltaTime);
    }

    private void FixedUpdate()
    {
        Vector3 target = startPosition + travel * targetProgress;
        body.MovePosition(Vector3.MoveTowards(body.position, target, moveSpeed * Time.fixedDeltaTime));
    }

    private void OnPuzzleReset()
    {
        targetProgress = 0f;
    }
}
