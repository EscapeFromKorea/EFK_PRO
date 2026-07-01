using UnityEngine;

public class doorPhysics : MonoBehaviour
{
    [Header("Door 설정")]
    private Rigidbody doorRigidbody;
    public float doorTargetYOffset = 3f;
    public float doorSpeed = 2f;

    [Header("레버 설정")]
    public LeverHead leverHead;
    //public float leverTriggerAngle = 30f;

    private Vector3 doorStartPosition;
    private Vector3 doorTargetPosition;
    private Vector3 doorBottomPosition;

    private Vector3 currentTargetPosition;

    private bool isPadPressed = false;
    //private bool isOpenLocked = false;
    private bool isBlocked = false;

    private int blockedOverlapCount = 0;

    void Awake()
    {
        doorRigidbody = GetComponent<Rigidbody>();
        if (doorRigidbody != null)
        {
            doorRigidbody.isKinematic = true;
            doorRigidbody.useGravity = false;
            doorStartPosition = doorRigidbody.transform.position;
            doorTargetPosition = doorStartPosition + new Vector3(0, doorTargetYOffset, 0);
            doorBottomPosition = doorStartPosition - new Vector3(0, doorTargetYOffset, 0);

            currentTargetPosition = doorStartPosition;
        }
    }

    void FixedUpdate()
    {
        float leverAngle = leverHead != null ? leverHead.GetCurrentAngle() : -40f;

        float t = Mathf.InverseLerp(-40f, 40f, leverAngle);
        Vector3 leverBasedPosition = Vector3.Lerp(doorStartPosition, doorTargetPosition, t);

        currentTargetPosition = isPadPressed ? doorTargetPosition : leverBasedPosition;

        Vector3 moveTarget = isBlocked ? doorRigidbody.transform.position : currentTargetPosition;

        doorRigidbody.MovePosition(
            Vector3.MoveTowards(doorRigidbody.transform.position, moveTarget, doorSpeed * Time.fixedDeltaTime)
        );
    }

    public void SetPadPressed(bool pressed)
    {
        isPadPressed = pressed;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        blockedOverlapCount++;
        isBlocked = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        blockedOverlapCount = Mathf.Max(0, blockedOverlapCount - 1);
        if (blockedOverlapCount == 0)
            isBlocked = false;
    }
}