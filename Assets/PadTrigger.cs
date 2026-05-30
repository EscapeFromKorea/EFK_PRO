using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PadTrigger : MonoBehaviour
{
    [Header("움직일 오브젝트와 설정")]
    public Rigidbody doorRigidbody;
    public float targetYOffset = 3f;
    public float speed = 2f;
    public float pushUpAmount = 0.1f;

    [Header("pad 설정")]
    public float padPressDepth = 0.1f;
    public float padSpeed = 5f;

    private Vector3 startPosition;       //door 시작점
    private Vector3 targetPosition;       //door 끝점
    private Vector3 padStartPosition;      //pad 시작점
    private Vector3 padPressedPosition;     //pad 상호작용시 내려갈 정도
    private bool isPressed = false;          // door가 player 오브젝트와 충돌했는지의 여부(얘는 door의 두번째 rigidbody를 이용해서 설정)
    private doorPhysics doorBlockCheck;       // 위에랑 같이 쓰는거

    void Start()
    {
        if (doorRigidbody == null)     //door에 rigidbody가 할당되지 않으면 작동x
        {
            Debug.LogError("doorRigidbody가 할당되지 않았습니다.");
            return;
        }

        doorRigidbody.isKinematic = true;
        doorRigidbody.useGravity = false;   // door 오브젝트는 중력의 영향을 받지 않게 설정

        startPosition = doorRigidbody.position;  //door의 rigidbody가 시작점
        targetPosition = startPosition + new Vector3(0, targetYOffset, 0);  //door가 y축으로 어느정도까지 움직일 지 설정

        padStartPosition = transform.position;
        padPressedPosition = padStartPosition - new Vector3(0, padPressDepth, 0);

        doorBlockCheck = doorRigidbody.GetComponent<doorPhysics>();
    }

    void FixedUpdate()
    {
        if (doorRigidbody == null) return;

        Vector3 target = isPressed ? targetPosition : startPosition;

        if (doorBlockCheck != null && doorBlockCheck.isBlocked)
        {
            doorRigidbody.MovePosition(doorRigidbody.position);
            return;
        }

        doorRigidbody.MovePosition(
            Vector3.MoveTowards(doorRigidbody.position, target, speed * Time.fixedDeltaTime)
        );

        Vector3 padTarget = isPressed ? padPressedPosition : padStartPosition;
        transform.position = Vector3.MoveTowards(transform.position, padTarget, padSpeed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            isPressed = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            isPressed = false;
    }
}