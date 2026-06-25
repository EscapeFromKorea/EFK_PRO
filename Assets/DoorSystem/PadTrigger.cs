using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PadTrigger : MonoBehaviour
{
    [Header("pad 설정")]
    public float padPressDepth = 0.1f;
    public float padSpeed = 5f;
    private Vector3 padStartPosition;      //pad 시작점
    private Vector3 padPressedPosition;     //pad 상호작용시 내려갈 정도
    private bool isPressed = false;          // door가 player 오브젝트와 충돌했는지의 여부(얘는 door의 두번째 rigidbody를 이용해서 설정)

    public doorPhysics doorPhysicsScript;

    void Start()
    {
        padStartPosition = transform.position;
        padPressedPosition = padStartPosition - new Vector3(0, padPressDepth, 0);
    }

    void FixedUpdate()
    {
        // pad 눌림 상태를 doorPhysics에 전달
        if (doorPhysicsScript != null)
            doorPhysicsScript.SetPadPressed(isPressed);

        // pad 자체 눌림 애니메이션
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