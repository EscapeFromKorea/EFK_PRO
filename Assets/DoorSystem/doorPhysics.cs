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
        float maxAngle = leverHead != null ? leverHead.maxAngle : 40f;
        float leverAngle = leverHead != null ? leverHead.GetCurrentAngle() : -maxAngle;

        float t = Mathf.InverseLerp(-maxAngle, maxAngle, leverAngle);
        Vector3 leverBasedPosition = Vector3.Lerp(doorStartPosition, doorTargetPosition, t);

        currentTargetPosition = isPadPressed ? doorTargetPosition : leverBasedPosition;

        // 끼임 방지는 내려갈 때만 — 올라가는(열리는) 문은 플레이어를 누를 일이 없다. 전엔 문에 붙어 선 플레이어의
        // Player_Mesh 트리거 때문에 열리는 것까지 멈췄다(CH8 열쇠 문 "앞에 서 있으면 안 열림").
        bool descending = currentTargetPosition.y < doorRigidbody.transform.position.y;
        Vector3 moveTarget = isBlocked && descending ? doorRigidbody.transform.position : currentTargetPosition;

        doorRigidbody.MovePosition(
            Vector3.MoveTowards(doorRigidbody.transform.position, moveTarget, doorSpeed * Time.fixedDeltaTime)
        );
    }

    public void SetPadPressed(bool pressed)
    {
        isPadPressed = pressed;
    }

    /// <summary>플레이어가 문틈(트리거)에 겹쳐 있어 내려가기를 멈춘 상태인가. 읽기 전용 —
    /// IsolationEntryDoor가 격리 진입 시 끼임 안전검사에 쓴다(동작 변경 없음).</summary>
    public bool IsBlocked => isBlocked;

    /// <summary>문이 닫힘 위치(시작 위치)에 도달했는가. 읽기 전용 — 위와 같은 안전검사용.</summary>
    public bool IsAtClosedPosition =>
        doorRigidbody != null && (doorRigidbody.position - doorStartPosition).sqrMagnitude < 0.0001f;

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