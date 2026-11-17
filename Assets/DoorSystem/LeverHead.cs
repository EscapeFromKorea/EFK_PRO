using UnityEngine;

public class LeverHead : MonoBehaviour, IMpSynced, IMpEventSink
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
    private float restAngle = -45f;

    private float exitGraceTimer = 0f;
    private bool pendingExit = false;

    public bool IsActivated { get; private set; }

    // [멀티 M4 분류 A] 레버는 서버만 상태 기계를 돌리고 각도(채널 0)를 올린다. 서버는 참가자 몸의 "복사본"과는 충돌 이벤트가 안 나므로
    // (키네마틱 vs 정적 콜라이더) 몸 주인 PC가 부딪힘을 감지해 SendEvent로 알린다(2026-11-17 결정). 이벤트 종류: 0=닿음 1=미는 중 2=떨어짐
    private uint mpId;
    private float mpAngle;
    private Vector3 ownerSmoothed;
    private float nextStaySend;

    void Awake()
    {
        mpAngle = GetCurrentAngle(); // 참가자 PC: 서버 값이 오기 전에 0도로 휙 돌지 않게 현재 각도로 시작(Register가 받은 값을 덮어쓰지 않게 그 앞에서)
        mpId = MpSync.Register(this);
    }

    void OnDestroy() => MpSync.Unregister(mpId, this);

    public void OnMpValue(int channel, float value) { if (channel == 0) mpAngle = value; }

    public void OnMpEvent(int kind, Vector3 n, uint arg)
    {
        if (kind == 0) HandleEnter(n);
        else if (kind == 1) HandleStay(n, true);
        else HandleExit();
    }
    private bool UsesP04TimedBehavior => gameObject.name == "P04_LeverHead";

    void Start()
    {
        if (leverPivot != null && UsesP04TimedBehavior)
        {
            restAngle = GetCurrentAngle();
            targetAngle = restAngle;
        }

        IsActivated = false;

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

        if (MpSync.IsClient)
        {
            // 참가자 PC: 서버가 정한 각도로 따라간다
            float cur = GetCurrentAngle();
            float to = Mathf.MoveTowardsAngle(cur, mpAngle, 400f * Time.fixedDeltaTime);
            leverPivot.localRotation = Quaternion.Euler(leverPivot.localEulerAngles.x, to, leverPivot.localEulerAngles.z);
            IsActivated = Mathf.Abs(Mathf.DeltaAngle(restAngle, GetCurrentAngle())) >= maxAngle * 0.75f;
            return;
        }

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

                float newY = Mathf.MoveTowardsAngle(currentY, restAngle, returnSpeed * 20f * Time.fixedDeltaTime);

                leverPivot.localRotation = Quaternion.Euler(
                    leverPivot.localEulerAngles.x,
                    newY,
                    leverPivot.localEulerAngles.z
                );

                if (Mathf.Abs(Mathf.DeltaAngle(newY, restAngle)) < 0.5f)
                {
                    isReturning = false;
                    leverPivot.localRotation = Quaternion.Euler(
                        leverPivot.localEulerAngles.x,
                        restAngle,
                        leverPivot.localEulerAngles.z
                    );
                }
            }
        }

        IsActivated = Mathf.Abs(Mathf.DeltaAngle(restAngle, GetCurrentAngle())) >= maxAngle * 0.75f;
        MpSync.Publish(mpId, 0, GetCurrentAngle());
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        Vector3 n = AverageContactNormal(collision);
        if (MpSync.IsClient) { if (IsOwnBody(collision)) { ownerSmoothed = n; MpSync.SendEvent(mpId, 0, n); } return; }
        HandleEnter(n);
    }

    // 서버(또는 오프라인)의 상태 기계 입력 — 호스트 자신의 몸은 충돌 이벤트로, 참가자의 몸은 OnMpEvent로 들어온다
    private void HandleEnter(Vector3 n)
    {
        pendingExit = false;
        isBeingPushed = true;
        isReturning = false;
        returnTimer = 0f;

        smoothedNormal = n;
        UpdatePushDirection(smoothedNormal);
    }

    private void HandleExit()
    {
        pendingExit = true;
        exitGraceTimer = pushExitGrace;
    }

    // 몸 주인 PC에서만 의미가 있다 — 남의 몸 복사본은 키네마틱이라 이벤트가 안 오지만 만약을 위해 확인한다
    private static bool IsOwnBody(Collision c)
    {
        var no = c.gameObject.GetComponentInParent<Unity.Netcode.NetworkObject>();
        return no != null && no.IsOwner;
    }

    void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        if (MpSync.IsClient)
        {
            // 법선 보간은 보내는 쪽에서 하고, 서버로는 초당 10번만 올린다
            if (!IsOwnBody(collision)) return;
            ownerSmoothed = Vector3.Lerp(ownerSmoothed, AverageContactNormal(collision), normalSmoothSpeed * Time.fixedDeltaTime);
            if (Time.time >= nextStaySend) { nextStaySend = Time.time + 0.1f; MpSync.SendEvent(mpId, 1, ownerSmoothed); }
            return;
        }

        HandleStay(AverageContactNormal(collision), false);
    }

    private void HandleStay(Vector3 n, bool alreadySmoothed)
    {
        if (UsesP04TimedBehavior)
        {
            pendingExit = false;
            isBeingPushed = true;
            isReturning = false;
            returnTimer = 0f;
        }

        smoothedNormal = alreadySmoothed ? n : Vector3.Lerp(smoothedNormal, n, normalSmoothSpeed * Time.fixedDeltaTime);
        UpdatePushDirection(smoothedNormal);
    }

    // 플레이어가 자유롭게 회전하며 모서리/꼭짓점으로 부딪힐 수 있어 접점이 여러 개
    // 생길 수 있다. contacts[0] 하나만 보면 물리 엔진이 배열 순서를 보장하지 않아
    // 매 프레임 다른 접점을 골라 방향이 흔들릴 수 있으므로, 모든 접점의 법선을
    // 평균 내어 더 안정적인 방향을 얻는다.
    private static Vector3 AverageContactNormal(Collision collision)
    {
        // collision.contacts는 호출마다 배열을 새로 할당한다 — GetContact(i)/contactCount로
        // 할당 없이 순회한다(2026-09-29 점검에서 발견).
        int count = collision.contactCount;
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < count; i++)
            sum += collision.GetContact(i).normal;

        return sum.sqrMagnitude > 0.0001f ? sum.normalized : collision.GetContact(0).normal;
    }

    void OnCollisionExit(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        if (MpSync.IsClient) { if (IsOwnBody(collision)) MpSync.SendEvent(mpId, 2, Vector3.zero); return; }
        HandleExit();
    }

    private void UpdatePushDirection(Vector3 worldNormal)
    {
        if (leverPivot == null) return;

        Vector3 localDir;
        if (UsesP04TimedBehavior)
        {
            Transform referenceFrame = leverPivot.parent != null ? leverPivot.parent : leverPivot;
            localDir = referenceFrame.InverseTransformDirection(-worldNormal);
        }
        else
        {
            localDir = leverPivot.InverseTransformDirection(worldNormal);
        }

        if (Mathf.Abs(localDir.x) > Mathf.Abs(localDir.z))
            return;

        float pushDirection = localDir.z > 0 ? 1f : -1f;
        targetAngle = UsesP04TimedBehavior
            ? restAngle + pushDirection * maxAngle
            : Mathf.Clamp(pushDirection * maxAngle, -maxAngle, maxAngle);
    }
}
