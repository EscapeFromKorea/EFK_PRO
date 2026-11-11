using UnityEngine;

/// <summary>
/// 조준 예고 보안 레이저 — CH5 전용(고정 방향 주기 반복, 조준 로직 없음). CH4
/// (<see cref="CharacterLockedLaser"/>)와 판정/시각(<see cref="LaserBeam"/>)은 공유하지만, `W`/`B`/`R`
/// 자체 파라미터로 독립 동작한다(PRD §4 "CH4/CH5 파라미터 독립" 확정 — CH4 값을 복사하지 않는다).
/// `ProjectileTrapSystem/ProjectileLauncher.cs`의 `정지 → 예고(W) → 발사 1회 → 휴식(R) → 예고`
/// 상태머신을 그대로 이식했다(탄 대신 LaserBeam을 튕긴다). 방향은 항상 <c>transform.forward</c>다.
/// </summary>
public class FixedPeriodicLaser : MonoBehaviour
{
    private enum Phase { Stopped, Warning, Firing, Resting }

    [Tooltip("판정/시각을 담당하는 공용 레이저(같은 오브젝트에 부착해 인스펙터로 연결).")]
    public LaserBeam beam;

    [Header("주기 (PRD §4 W/B/R — CH4 값 복사 금지, 그레이박스 기본값)")]
    [Tooltip("예고 지속시간(초, W). [TBD, 임시값]")]
    public float warningSeconds = 1f;

    [Tooltip("광선 발사 지속시간(초, B). CH4의 beamDuration과 같은 개념이지만 별도 필드다(PRD 명시, " +
             "CH4 값 복사 금지). [TBD, 임시값]")]
    public float beamSeconds = 0.4f;

    [Tooltip("발사 후 다음 예고까지의 휴식 시간(초, R). [TBD, 임시값]")]
    public float restSeconds = 2f;

    [Tooltip("씬 시작과 동시에 주기를 켠다. 끄면 외부(구간 진입 컨트롤러 등)가 Activate()를 불러야 " +
             "발사가 시작된다.")]
    public bool autoActivateOnStart = true;

    private Phase phase = Phase.Stopped;
    private float phaseElapsed;

    /// <summary>이미 도는 주기 중이면 아무 일도 하지 않는다(ProjectileLauncher와 같은 규칙).</summary>
    public void Activate()
    {
        if (phase != Phase.Stopped) return;
        EnterPhase(Phase.Warning);
    }

    /// <summary>즉시 정지하고 광선을 끈다.</summary>
    public void Deactivate()
    {
        phase = Phase.Stopped;
        phaseElapsed = 0f;
        beam?.Tick(transform.position, transform.forward, telegraphOn: false, firingOn: false);
    }

    private void Start()
    {
        if (autoActivateOnStart) Activate();
    }

    private void FixedUpdate()
    {
        if (beam == null) return;

        switch (phase)
        {
            case Phase.Warning:
                phaseElapsed += Time.fixedDeltaTime;
                beam.Tick(transform.position, transform.forward, telegraphOn: true, firingOn: false);
                if (phaseElapsed >= warningSeconds) EnterPhase(Phase.Firing);
                break;

            case Phase.Firing:
                phaseElapsed += Time.fixedDeltaTime;
                beam.Tick(transform.position, transform.forward, telegraphOn: false, firingOn: true);
                if (phaseElapsed >= beamSeconds) EnterPhase(Phase.Resting);
                break;

            case Phase.Resting:
                phaseElapsed += Time.fixedDeltaTime;
                beam.Tick(transform.position, transform.forward, telegraphOn: false, firingOn: false);
                if (phaseElapsed >= restSeconds) EnterPhase(Phase.Warning);
                break;
        }
    }

    private void EnterPhase(Phase next)
    {
        phase = next;
        phaseElapsed = 0f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 3f);
    }
}
