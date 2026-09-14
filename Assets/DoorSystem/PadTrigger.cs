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

    // 플레이어 오브젝트가 트리거(Player_Mesh)와 솔리드(Player_Collider) 등 여러 콜라이더를
    // 가질 수 있어 Enter/Exit가 중복 호출될 수 있다. 카운터로 실제 겹침 개수를 추적해
    // 하나만 먼저 빠져나가도 눌림이 풀리지 않게 한다.
    private int overlapCount = 0;

    public doorPhysics doorPhysicsScript;

    void Start()
    {
        EnsureP04TriggerReachesAboveFloor();
        padStartPosition = transform.position;
        padPressedPosition = padStartPosition - new Vector3(0, padPressDepth, 0);
    }

    private void EnsureP04TriggerReachesAboveFloor()
    {
        if (gameObject.name != "P04_HoldPad") return;
        if (!(GetComponent<Collider>() is BoxCollider box)) return;

        float worldScaleY = Mathf.Abs(transform.lossyScale.y);
        if (worldScaleY <= Mathf.Epsilon) return;

        const float minimumWorldHeight = 0.5f;
        float currentWorldHeight = box.size.y * worldScaleY;
        if (currentWorldHeight >= minimumWorldHeight) return;

        // Preserve the trigger bottom and extend only its top above the courtyard floor.
        float addedLocalHeight = (minimumWorldHeight - currentWorldHeight) / worldScaleY;
        Vector3 size = box.size;
        Vector3 center = box.center;
        size.y += addedLocalHeight;
        center.y += addedLocalHeight * 0.5f;
        box.size = size;
        box.center = center;
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
        if (!other.CompareTag("Player")) return;

        overlapCount++;
        isPressed = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        overlapCount = Mathf.Max(0, overlapCount - 1);
        if (overlapCount == 0)
            isPressed = false;
    }
}
