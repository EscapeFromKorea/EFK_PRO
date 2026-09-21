using UnityEngine;

/// <summary>
/// 주기형 물리 함정 — 가시. 상태 순서 수납 → 바닥 예고 → 상승 → 돌출 유지 → 하강(반복). 상승/돌출
/// 유지 상태에서만 돌출부(솔리드)가 위험하고, 나머지는 무피해다. <see cref="HammerTrap"/>과 이동
/// 방향(위험 구간이 상승 쪽)만 반대인 대칭 구조 — 상세: docs/PRD/PeriodicTraps.md §3·§4.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SpikeTrap : PeriodicTrapBase
{
    private enum Phase { Retracted, Rising, Extended, Retracting }

    [Header("가시 — 상태 지속시간 (사양 §4, 미정값 그레이박스)")]
    [Tooltip("수납 + 바닥 예고를 합친 시간(초). 예고 표시는 마지막 telegraphLeadSeconds 동안만 켠다.")]
    public float retractedWaitSeconds = 2f;

    public float telegraphLeadSeconds = 0.6f;

    [Tooltip("돌출 유지 시간(초). 이 동안도 위험하다(사양 §4 표).")]
    public float extendedWaitSeconds = 0.4f;

    [Header("가시 — 이동 (속도 기반, 거리는 배치 시 실측)")]
    [Tooltip("수납 위치에서 돌출되는 거리(Unit, 월드 +Y).")]
    public float popDistance = 1f;

    public float risingSpeed = 8f;
    public float retractingSpeed = 4f;

    [Header("가시 — 콜라이더")]
    [Tooltip("돌출부(솔리드). 상승/돌출 유지 상태에서만 위험. 비우면 Reset이 기본값을 만든다.")]
    public BoxCollider spikeCollider;

    [Header("예고 표시 (선택)")]
    public Renderer telegraphMarker;
    public Color telegraphColor = new Color(1f, 0.6f, 0.1f);
    public Color idleColor = Color.white;

    private Rigidbody body;
    private Vector3 retractedPosition;
    private Vector3 extendedPosition;
    private Phase phase;
    private float phaseElapsed;
    private MaterialPropertyBlock mpb;

    private void Reset()
    {
        if (spikeCollider == null) spikeCollider = GetComponent<BoxCollider>();
        if (spikeCollider == null) spikeCollider = gameObject.AddComponent<BoxCollider>();
        spikeCollider.size = new Vector3(0.6f, 1f, 0.6f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        retractedPosition = body.position;
        extendedPosition = retractedPosition + Vector3.up * popDistance;
        phase = Phase.Retracted;
        phaseElapsed = Mathf.Repeat(startPhase, Mathf.Max(0.05f, retractedWaitSeconds));
        mpb = new MaterialPropertyBlock();
    }

    private bool IsDangerous => phase == Phase.Rising || phase == Phase.Extended;

    private void FixedUpdate()
    {
        UpdateTelegraph();

        switch (phase)
        {
            case Phase.Retracted:
                phaseElapsed += Time.fixedDeltaTime;
                if (phaseElapsed >= retractedWaitSeconds) EnterPhase(Phase.Rising);
                break;

            case Phase.Rising:
                if (MoveToward(extendedPosition, risingSpeed)) EnterPhase(Phase.Extended);
                break;

            case Phase.Extended:
                phaseElapsed += Time.fixedDeltaTime;
                if (phaseElapsed >= extendedWaitSeconds) EnterPhase(Phase.Retracting);
                break;

            case Phase.Retracting:
                if (MoveToward(retractedPosition, retractingSpeed)) EnterPhase(Phase.Retracted);
                break;
        }
    }

    private void EnterPhase(Phase next)
    {
        // 위험 상태를 벗어나는 전이(돌출 유지 → 하강)에서만 피격 집계 창을 연다(사양 §4 "장치·주기·
        // 대상당 1회").
        if (IsDangerous && next == Phase.Retracting) ResetHitWindow();
        phase = next;
        phaseElapsed = 0f;
    }

    private bool MoveToward(Vector3 target, float speed)
    {
        Vector3 next = Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);
        Vector3 delta = next - body.position;

        if (!IsDangerous && spikeCollider != null && !CanAdvance(spikeCollider, delta))
            return false; // 압착 위험 — 이 스텝은 제자리에 머문다.

        body.MovePosition(next);
        return (next - target).sqrMagnitude < 1e-6f;
    }

    private void OnCollisionEnter(Collision collision) => HandleCollision(collision);
    private void OnCollisionStay(Collision collision) => HandleCollision(collision);

    private void HandleCollision(Collision collision)
    {
        if (!IsDangerous || !collision.collider.CompareTag(playerTag)) return;
        TryRegisterHit(collision.rigidbody);
    }

    private void UpdateTelegraph()
    {
        if (telegraphMarker == null) return;
        bool warn = phase == Phase.Retracted && (retractedWaitSeconds - phaseElapsed) <= telegraphLeadSeconds;
        mpb.SetColor("_Color", warn ? telegraphColor : idleColor);
        telegraphMarker.SetPropertyBlock(mpb);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 bottom = Application.isPlaying ? retractedPosition : transform.position;
        Vector3 top = bottom + Vector3.up * popDistance;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.DrawLine(bottom, top);
        Gizmos.DrawWireSphere(top, 0.2f);
    }
}
