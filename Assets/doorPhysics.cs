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
    private Vector3 doorBottomPosition; // 변수는 유지되나 레버 로직에서는 제외됩니다.
    
    private Vector3 currentTargetPosition; 

    private bool isPadPressed = false;
    private bool isBlocked = false;

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

        // --- 수정된 우선순위 조건문 ---
        
        // 1. 레버가 30도 이상일 때 -> 무조건 위로 고정
        if (leverAngle >= leverTriggerAngle)
        {
            currentTargetPosition = doorTargetPosition;
        }
        // 2. 레버가 -30도 이하일 때 -> 무조건 원래 위치(중간)로 고정 ★ 수정된 부분
        else if (leverAngle <= -leverTriggerAngle)
        {
            currentTargetPosition = doorStartPosition;
        }
        // 3. 레버가 중간 범위(-30 < x < 30)에 있을 때 -> 발판(Pad) 상태에 따름
        else
        {
            if (isPadPressed)
            {
                currentTargetPosition = doorTargetPosition; // 발판 밟으면 위로
            }
            else
            {
                currentTargetPosition = doorStartPosition;  // 발판 안 밟으면 원래 위치(중간)로
            }
        }

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
        if (other.CompareTag("Player"))
            isBlocked = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            isBlocked = false;
    }
}