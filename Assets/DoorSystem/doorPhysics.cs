using UnityEngine;

public class doorPhysics : MonoBehaviour
{
    [Header("Door 설정")]
    private Rigidbody doorRigidbody;
    public float doorTargetYOffset = 3f;
    public float doorSpeed = 2f;

    [Header("레버 설정")]
    public LeverHead leverHead;
    public float leverTriggerAngle = 30f;

    private Vector3 doorStartPosition;
    private Vector3 doorTargetPosition;
    private Vector3 doorBottomPosition;

    private Vector3 currentTargetPosition;

    private bool isPadPressed = false;
    private bool isOpenLocked = false;
    private bool isBlocked = false;

    // ④ Player_Root + Player_Mesh 둘 다 Tag: Player이므로 Enter/Exit 중복 호출 방지
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
        float leverAngle = leverHead != null ? leverHead.GetCurrentAngle() : 0f;
        //레버에 의한 문 열림/닫힘 세부수정부분
        if (leverAngle >= leverTriggerAngle)
        {
            isOpenLocked = true;
        }
        else if (leverAngle <= 0f)
        {
            isOpenLocked = false;
        }

        currentTargetPosition = (isOpenLocked || isPadPressed) ? doorTargetPosition : doorStartPosition;

        Vector3 moveTarget = isBlocked ? doorRigidbody.transform.position : currentTargetPosition;

        doorRigidbody.MovePosition(
            Vector3.MoveTowards(doorRigidbody.transform.position, moveTarget, doorSpeed * Time.fixedDeltaTime)
        );

        Debug.Log($"leverAngle: {leverAngle:F1} | Pad: {isPadPressed} | Blocked: {isBlocked} | Target: {currentTargetPosition}");
    }

    public void SetPadPressed(bool pressed)
    {
        isPadPressed = pressed;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // ④ 카운터 증가 — 첫 번째 Enter에서만 isBlocked = true
        blockedOverlapCount++;
        isBlocked = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // ④ 카운터를 줄이고, 0이 됐을 때만 isBlocked 해제
        blockedOverlapCount = Mathf.Max(0, blockedOverlapCount - 1);
        if (blockedOverlapCount == 0)
            isBlocked = false;
    }
}