using UnityEngine;

public class LeverHead : MonoBehaviour
{
    [Header("레버 설정")]
    public Transform leverPivot;
    public float rotateSpeed = 3f;
    public float maxAngle = 45f;
    [Tooltip("충돌 노멀을 보간하는 속도. 값이 클수록 방향 전환이 빠릅니다.")]
    public float normalSmoothSpeed = 5f;

    private float targetAngle = 0f;
    private bool isBeingPushed = false;
    private Vector3 smoothedNormal = Vector3.zero;

    void Start()
    {
        if (leverPivot == null)
            Debug.LogWarning($"[LeverHead] '{gameObject.name}'의 leverPivot이 연결되지 않았습니다. 레버가 동작하지 않습니다.");
    }

    public float GetCurrentAngle()
    {
        if (leverPivot == null) return 0f;

        float angle = leverPivot.localEulerAngles.y;
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    void FixedUpdate()
    {
        if (leverPivot == null) return;

        if (isBeingPushed)
        {
            leverPivot.localRotation = Quaternion.Slerp(
                leverPivot.localRotation,
                Quaternion.Euler(
                    leverPivot.localEulerAngles.x,
                    targetAngle,
                    leverPivot.localEulerAngles.z
                ),
                1f - Mathf.Exp(-rotateSpeed * Time.fixedDeltaTime)
            );
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        isBeingPushed = true;

        // 충돌 첫 프레임 노멀을 초기값으로 설정
        smoothedNormal = collision.contacts[0].normal;
        UpdatePushDirection(smoothedNormal);
    }

    void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        // 충돌 노멀을 부드럽게 Lerp하여 스케일 변화로 인한 노멀 흔들림 완화
        smoothedNormal = Vector3.Lerp(
            smoothedNormal,
            collision.contacts[0].normal,
            normalSmoothSpeed * Time.fixedDeltaTime
        );
        UpdatePushDirection(smoothedNormal);
    }

    void OnCollisionExit(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        isBeingPushed = false;
        smoothedNormal = Vector3.zero;

        if (leverPivot == null) return;
        targetAngle = leverPivot.localEulerAngles.y;
        if (targetAngle > 180f) targetAngle -= 360f;
    }

    /// <summary>충돌 노멀을 leverPivot 로컬 공간으로 변환해 targetAngle 결정</summary>
    private void UpdatePushDirection(Vector3 worldNormal)
    {
        if (leverPivot == null) return;

        Vector3 localDir = leverPivot.InverseTransformDirection(worldNormal);

        // XZ 평면에서 절댓값이 더 큰 축 기준으로 방향 판단
        // 레버 배치 방향에 무관하게 안정적으로 동작
        float pushDirection;
        if (Mathf.Abs(localDir.z) >= Mathf.Abs(localDir.x))
            pushDirection = localDir.z > 0 ? 1f : -1f;
        else
            pushDirection = localDir.x > 0 ? 1f : -1f;

        targetAngle = Mathf.Clamp(pushDirection * maxAngle, -maxAngle, maxAngle);
    }
}