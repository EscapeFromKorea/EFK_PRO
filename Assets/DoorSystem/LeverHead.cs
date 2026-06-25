using UnityEngine;

public class LeverHead : MonoBehaviour
{
    [Header("레버 설정")]
    public Transform leverPivot;
    public float rotateSpeed = 3f;
    public float maxAngle = 45f;
    public float normalSmoothSpeed = 5f;

    private float targetAngle = 0f;
    private bool isBeingPushed = false;
    private Vector3 smoothedNormal = Vector3.zero;

    public float GetCurrentAngle()
    {
        float angle = leverPivot.localEulerAngles.y;
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    void FixedUpdate()
    {
        // 레버 회전
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
        if (collision.gameObject.CompareTag("Player"))
        {
            isBeingPushed = true;
            smoothedNormal = collision.contacts[0].normal;
            UpdatePushDirection();
        }
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            smoothedNormal = Vector3.Lerp(
                smoothedNormal,
                collision.contacts[0].normal,
                normalSmoothSpeed * Time.fixedDeltaTime
            );
            UpdatePushDirection();
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            isBeingPushed = false;
            smoothedNormal = Vector3.zero;
            targetAngle = leverPivot.localEulerAngles.y;
            if (targetAngle > 180f) targetAngle -= 360f;
        }
    }

    void UpdatePushDirection()
    {
        Vector3 localDir = leverPivot.InverseTransformDirection(smoothedNormal);
        float pushDirection = localDir.z > 0 ? 1f : -1f;
        targetAngle = Mathf.Clamp(pushDirection * maxAngle, -maxAngle, maxAngle);
    }
}