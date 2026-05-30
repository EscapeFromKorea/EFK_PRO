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

    private Vector3 startPosition;
    private Vector3 targetPosition;
    private Vector3 padStartPosition;
    private Vector3 padPressedPosition;
    private bool isPressed = false;
    private doorPhysics doorBlockCheck;

    void Start()
    {
        if (doorRigidbody == null)
        {
            Debug.LogError("doorRigidbody가 할당되지 않았습니다.");
            return;
        }

        doorRigidbody.isKinematic = true;
        doorRigidbody.useGravity = false;

        startPosition = doorRigidbody.position;
        targetPosition = startPosition + new Vector3(0, targetYOffset, 0);

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