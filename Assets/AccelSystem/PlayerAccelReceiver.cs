using UnityEngine;

/// <summary>
/// 플레이어의 Rigidbody에 부착.
/// AccelPad로부터 부스트 요청을 받아 velocity를 직접 관리한다.
/// 다른 이동 스크립트보다 항상 나중에 실행되어(DefaultExecutionOrder)
/// 부스트 중에는 velocity의 최종 결정권을 가진다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(100)]
public class PlayerAccelReceiver : MonoBehaviour
{
    private enum State { None, RampUp, Hold, Decel }

    private Rigidbody rb;
    private PlayerMover mover;
    private State state = State.None;

    private Vector3 boostVelocity;
    private Vector3 rampStartVelocity;
    private float rampTimer;
    public float rampDuration = 0.15f;
    
    [Range(0f, 1f)]
    public float steerControlWhileBoosting = 0.3f;

    private float holdTimer;

    private Vector3 decelStartVelocity;
    private float decelTimer;
    private float decelDuration;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mover = GetComponent<PlayerMover>();
    }

    /// <summary>남은 부스트를 즉시 버린다. 리스폰처럼 외부가 이 바디를 순간이동시킬 때 부른다.
    ///
    /// 부스트 중에는 FixedUpdate가 매 스텝 velocity를 통째로 대입하므로(이 컴포넌트의 존재 이유),
    /// 순간이동 직후 속도를 0으로 초기화해도 다음 물리 스텝에 부스트 속도가 그대로 되살아나
    /// 복귀 지점 밖으로 다시 발사된다. 가속 발판을 밟고 장외로 날아간 플레이어를 되돌리는 경우가
    /// 정확히 이 상황이다.
    ///
    /// enabled 토글로는 해결되지 않는다 — state와 holdTimer가 남아 다시 켜는 순간 남은 부스트가
    /// 이어진다. 그래서 상태를 버리는 전용 진입점을 둔다. velocity는 건드리지 않는다(호출한
    /// 쪽이 원하는 속도를 정한다 — 여기서 0으로 만들면 부스트 중 일시정지 같은 다른 용도를 막는다).</summary>
    public void CancelBoost()
    {
        state = State.None;
        boostVelocity = Vector3.zero;
    }

    /// <summary>AccelPad가 호출하는 진입점.</summary>
    public void ApplyBoost(Vector3 velocity, float hold, float decel)
    {
        Vector3 currentHorizontal = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        float currentSpeed = currentHorizontal.magnitude;
        float boostSpeedMag = velocity.magnitude;

        // 기존 속도가 더 빠르면 방향만 부스트 방향으로, 속도는 기존 값 유지
        boostVelocity = currentSpeed > boostSpeedMag
            ? velocity.normalized * currentSpeed
            : velocity;

        rampStartVelocity = currentHorizontal;
        rampTimer = 0f;
        holdTimer = hold;
        decelDuration = decel;
        decelTimer = 0f;
        state = State.RampUp;
    }

    private void FixedUpdate()
    {
        if (state == State.None) return;

        float currentY = rb.velocity.y; // 중력/점프는 건드리지 않음

        Vector3 inputVel = Vector3.zero;
        if (mover != null && mover.IsControlled)
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            inputVel = new Vector3(h, 0f, v) * mover.moveSpeed;

            Vector3 boostDir = boostVelocity.sqrMagnitude > 0.0001f ? boostVelocity.normalized : Vector3.zero;
            if (boostDir != Vector3.zero)
            {
                Vector3 parallel = Vector3.Dot(inputVel, boostDir) * boostDir;
                Vector3 perpendicular = inputVel - parallel;
                inputVel = parallel + perpendicular * steerControlWhileBoosting;
            }
        }

        if (state == State.RampUp)
        {
            rampTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(rampTimer / rampDuration);
            Vector3 ramped = Vector3.Lerp(rampStartVelocity, boostVelocity, t) + inputVel;
            rb.velocity = new Vector3(ramped.x, currentY, ramped.z);

            if (t >= 1f)
                state = State.Hold;
        }
        else if (state == State.Hold)
        {
            Vector3 holdResult = boostVelocity + inputVel;
            rb.velocity = new Vector3(holdResult.x, currentY, holdResult.z);

            holdTimer -= Time.fixedDeltaTime;
            if (holdTimer <= 0f)
            {
                decelStartVelocity = boostVelocity;
                decelTimer = 0f;
                state = decelDuration > 0f ? State.Decel : State.None;
            }
        }
        else if (state == State.Decel)
        {
            decelTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(decelTimer / decelDuration);

            Vector3 decelVelocity = Vector3.Lerp(decelStartVelocity, Vector3.zero, t) + inputVel;
            rb.velocity = new Vector3(decelVelocity.x, currentY, decelVelocity.z);

            if (t >= 1f)
            {
                state = State.None; // velocity 제어권을 다른 이동 스크립트로 완전히 반납
            }
        }
    }
}