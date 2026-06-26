using UnityEngine;

public class LeverHead : MonoBehaviour
{
    [Header("레버 설정")]
    public Transform leverPivot;
    public float rotateSpeed = 3f;
    public float maxAngle = 45f;
    [Tooltip("충돌 노멀을 보간하는 속도. 값이 클수록 방향 전환이 빠릅니다.")]
    public float normalSmoothSpeed = 5f;
    public float returnDelay = 2f;
    public float returnSpeed = 1.5f;

    private float targetAngle = 0f;
    private bool isBeingPushed = false;
    private Vector3 smoothedNormal = Vector3.zero;
    private float returnTimer = 0f;
    private bool isReturning = false;

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
        else if (isReturning)
        {
            returnTimer -= Time.fixedDeltaTime;
            if (returnTimer <= 0f)
            {
                leverPivot.localRotation = Quaternion.Slerp(
                    leverPivot.localRotation,
                    Quaternion.Euler(
                        leverPivot.localEulerAngles.x,
                        -45f,
                        leverPivot.localEulerAngles.z
                    ),
                    1f - Mathf.Exp(-returnSpeed * Time.fixedDeltaTime)
                );

                float current = leverPivot.localEulerAngles.y;
                if (current > 180f) current -= 360f;
                if (Mathf.Abs(current - (-45f)) < 0.5f)
                {
                    isReturning = false;
                    leverPivot.localRotation = Quaternion.Euler(
                        leverPivot.localEulerAngles.x,
                        -45f,
                        leverPivot.localEulerAngles.z
                    );
                }
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        isBeingPushed = true;
        isReturning = false;
        returnTimer = 0f;

        smoothedNormal = collision.contacts[0].normal;
        UpdatePushDirection(smoothedNormal);
    }

    void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

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
        isReturning = true;
        returnTimer = returnDelay;

        if (leverPivot == null) return;
        targetAngle = leverPivot.localEulerAngles.y;
        if (targetAngle > 180f) targetAngle -= 360f;
    }

    private void UpdatePushDirection(Vector3 worldNormal)
    {
        if (leverPivot == null) return;

        Vector3 localDir = leverPivot.InverseTransformDirection(worldNormal);

        float pushDirection;
        if (Mathf.Abs(localDir.z) >= Mathf.Abs(localDir.x))
            pushDirection = localDir.z > 0 ? 1f : -1f;
        else
            pushDirection = localDir.x > 0 ? 1f : -1f;

        targetAngle = Mathf.Clamp(pushDirection * maxAngle, -maxAngle, maxAngle);
    }
}