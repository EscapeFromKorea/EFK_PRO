using UnityEngine;

public class LeverHead : MonoBehaviour
{
    [Header("레버 설정")]
    public Transform leverPivot;
    public float rotateSpeed = 3f;
    public float maxAngle = 45f;
    [Tooltip("충돌 노멀을 보간하는 속도. 값이 클수록 방향 전환이 빠릅니다.")]
    public float normalSmoothSpeed = 5f;
    public float returnDelay = 5f;
    public float returnSpeed = 1.5f;

    [Header("충돌 끊김 보정")]
    public float pushExitGrace = 0.3f;

    private float targetAngle = 0f;
    private bool isBeingPushed = false;
    private Vector3 smoothedNormal = Vector3.zero;
    private float returnTimer = 0f;
    private bool isReturning = false;

    private float exitGraceTimer = 0f;
    private bool pendingExit = false;

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

        if (pendingExit)
        {
            exitGraceTimer -= Time.fixedDeltaTime;
            if (exitGraceTimer <= 0f)
            {
                pendingExit = false;
                isBeingPushed = false;
                smoothedNormal = Vector3.zero;
                isReturning = true;
                returnTimer = returnDelay;

                targetAngle = leverPivot.localEulerAngles.y;
                if (targetAngle > 180f) targetAngle -= 360f;
            }
        }

        if (isBeingPushed)
        {
            float currentY = leverPivot.localEulerAngles.y;
            if (currentY > 180f) currentY -= 360f;

            float newY = Mathf.MoveTowardsAngle(currentY, targetAngle, rotateSpeed * 20f * Time.fixedDeltaTime);

            leverPivot.localRotation = Quaternion.Euler(
                leverPivot.localEulerAngles.x,
                newY,
                leverPivot.localEulerAngles.z
            );
        }
        else if (isReturning)
        {
            returnTimer -= Time.fixedDeltaTime;
            if (returnTimer <= 0f)
            {
                float currentY = leverPivot.localEulerAngles.y;
                if (currentY > 180f) currentY -= 360f;

                float newY = Mathf.MoveTowardsAngle(currentY, -45f, returnSpeed * 20f * Time.fixedDeltaTime);

                leverPivot.localRotation = Quaternion.Euler(
                    leverPivot.localEulerAngles.x,
                    newY,
                    leverPivot.localEulerAngles.z
                );

                if (Mathf.Abs(newY - (-45f)) < 0.5f)
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

        pendingExit = false;
        isBeingPushed = true;
        isReturning = false;
        returnTimer = 0f;

        smoothedNormal = AverageContactNormal(collision);
        UpdatePushDirection(smoothedNormal);
    }

    void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        smoothedNormal = Vector3.Lerp(
            smoothedNormal,
            AverageContactNormal(collision),
            normalSmoothSpeed * Time.fixedDeltaTime
        );
        UpdatePushDirection(smoothedNormal);
    }

    // 플레이어가 자유롭게 회전하며 모서리/꼭짓점으로 부딪힐 수 있어 접점이 여러 개
    // 생길 수 있다. contacts[0] 하나만 보면 물리 엔진이 배열 순서를 보장하지 않아
    // 매 프레임 다른 접점을 골라 방향이 흔들릴 수 있으므로, 모든 접점의 법선을
    // 평균 내어 더 안정적인 방향을 얻는다.
    private static Vector3 AverageContactNormal(Collision collision)
    {
        Vector3 sum = Vector3.zero;
        foreach (ContactPoint contact in collision.contacts)
            sum += contact.normal;

        return sum.sqrMagnitude > 0.0001f ? sum.normalized : collision.contacts[0].normal;
    }

    void OnCollisionExit(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        pendingExit = true;
        exitGraceTimer = pushExitGrace;
    }

    private void UpdatePushDirection(Vector3 worldNormal)
    {
        if (leverPivot == null) return;

        Vector3 localDir = leverPivot.InverseTransformDirection(worldNormal);

        if (Mathf.Abs(localDir.x) > Mathf.Abs(localDir.z))
            return;

        float pushDirection = localDir.z > 0 ? 1f : -1f;
        targetAngle = Mathf.Clamp(pushDirection * maxAngle, -maxAngle, maxAngle);
    }
}