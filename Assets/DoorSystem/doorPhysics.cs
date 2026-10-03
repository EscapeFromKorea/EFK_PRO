using UnityEngine;

public class doorPhysics : MonoBehaviour
{
    [Header("Door 이동 설정")]
    private Rigidbody doorRigidbody;
    [Tooltip("문이 열릴 때 시작 위치에서 월드 X축으로 이동할 거리입니다. 음수면 왼쪽, 양수면 오른쪽으로 이동합니다.")]
    public float doorTargetXOffset = 0f;
    [Tooltip("문이 열릴 때 시작 위치에서 월드 Y축으로 이동할 거리입니다. 기존 세로 문은 이 값만 설정하면 됩니다.")]
    public float doorTargetYOffset = 3f;
    [Tooltip("문이 열릴 때 시작 위치에서 월드 Z축으로 이동할 거리입니다. 음수면 뒤쪽, 양수면 앞쪽으로 이동합니다.")]
    public float doorTargetZOffset = 0f;
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
            Vector3 openOffset = new Vector3(doorTargetXOffset, doorTargetYOffset, doorTargetZOffset);
            doorTargetPosition = doorStartPosition + openOffset;
            doorBottomPosition = doorStartPosition - openOffset;

            currentTargetPosition = doorStartPosition;
        }
    }

    void FixedUpdate()
    {
        if (doorRigidbody == null) return;

        float maxAngle = leverHead != null ? leverHead.maxAngle : 40f;
        float leverAngle = leverHead != null ? leverHead.GetCurrentAngle() : -maxAngle;
        bool usesP04TimedLever = leverHead != null && gameObject.name == "P04_Door";

        Vector3 leverBasedPosition;
        if (usesP04TimedLever)
        {
            leverBasedPosition = leverHead.IsActivated
                ? doorTargetPosition
                : doorStartPosition;
        }
        else
        {
            float t = Mathf.InverseLerp(-maxAngle, maxAngle, leverAngle);
            leverBasedPosition = Vector3.Lerp(doorStartPosition, doorTargetPosition, t);
        }

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
