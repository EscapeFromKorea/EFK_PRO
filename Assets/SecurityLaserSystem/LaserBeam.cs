using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 보안 레이저 공통 판정/시각 — CH4(<see cref="CharacterLockedLaser"/>)·CH5
/// (<see cref="FixedPeriodicLaser"/>)가 상속이 아니라 소유해서 매 FixedUpdate 호출하는 합성 부품
/// (`PeriodicTrapSystem`이 상속 대신 판정 로직만 복제한 것과 같은 결의 판단 — 자세한 이유는
/// `SecurityLaserSystem/CLAUDE.md` 참고). 발사선의 시각 표현과 실제 판정이 항상 같은 시작/종점
/// (origin→endPoint)을 공유해야 한다는 PRD §3·§4 "발사선-판정 일치" 요구를 이 클래스 하나로 강제한다.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class LaserBeam : MonoBehaviour
{
    [Header("공통 판정 (PRD §4 사거리·광선 폭·차폐 레이어 — 미정, 그레이박스 기본값)")]
    [Tooltip("레이저 최대 사거리(U). 벽에 안 맞으면 이 거리까지 표시/판정한다.")]
    public float range = 20f;

    [Tooltip("발사 판정에 쓰는 광선 반경(U, SphereCast). 시각 두께(activeWidth)와는 별개 값이다.")]
    public float beamRadius = 0.15f;

    [Tooltip("차폐 레이어 — 벽 첫 충돌점까지만 광선을 표시/판정한다(PRD §4 확정).")]
    public LayerMask wallMask = ~0;

    public string playerTag = "Player";

    [Header("시각 — 예고(점선 대체: 얇고 반투명)/발사(굵은 실선)")]
    public Color telegraphColor = new Color(1f, 0.6f, 0.1f, 0.5f);
    public Color activeColor = new Color(1f, 0.15f, 0.1f, 1f);
    public float telegraphWidth = 0.03f;
    public float activeWidth = 0.12f;

    [System.Serializable] public class PlayerHitEvent : UnityEvent<GameObject> { }

    [Tooltip("경로상의 Player 콜라이더(교차 피격 포함)가 활성 광선에 유효 접촉하면 발화. " +
             "구간별 SectionHitCounter.RegisterHitEvent에 배선한다(CH4 레이저 구간 시작 / CH5 시작).")]
    public PlayerHitEvent OnHazardHit;

    private LineRenderer line;
    private readonly HashSet<Rigidbody> hitThisFiring = new HashSet<Rigidbody>();
    private bool wasFiring;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
        if (line.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (s != null) line.material = new Material(s);
        }
    }

    /// <summary>CH4/CH5가 매 FixedUpdate 호출하는 유일한 공개 진입점.</summary>
    public void Tick(Vector3 origin, Vector3 direction, bool telegraphOn, bool firingOn)
    {
        // 장치·활성 구간(주기)당 1회 집계 — 새 발사 활성 구간이 시작되는 전이에서만 비운다
        // (PeriodicTrapBase.TryRegisterHit과 같은 개념, 상속 없이 독립 구현).
        if (firingOn && !wasFiring) hitThisFiring.Clear();
        wasFiring = firingOn;

        if (!telegraphOn && !firingOn)
        {
            line.enabled = false;
            return;
        }

        Vector3 dir = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
        float dist = range;
        Vector3 endPoint = origin + dir * range;

        // 플레이어 자신의 콜라이더는 "벽"이 아니다 — 포함시키면 광선이 조준 대상(또는 경로상의 다른
        // 도형)의 솔리드 콜라이더에서 멈춰버려 PRD의 "벽 첫 충돌점까지만" 요구와 "교차 피격"(경로의
        // 모든 도형을 맞혀야 함) 요구를 둘 다 깬다. RaycastAll로 전체 후보를 모아 Player 태그가 아닌
        // 첫 충돌만 벽으로 인정한다.
        RaycastHit[] wallCandidates = Physics.RaycastAll(origin, dir, range, wallMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(wallCandidates, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit candidate in wallCandidates)
        {
            if (candidate.collider != null && candidate.collider.CompareTag(playerTag)) continue;
            dist = candidate.distance;
            endPoint = candidate.point;
            break;
        }

        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, endPoint);

        if (firingOn)
        {
            line.widthMultiplier = activeWidth;
            if (line.material != null) line.material.color = activeColor;
        }
        else
        {
            line.widthMultiplier = telegraphWidth;
            if (line.material != null) line.material.color = telegraphColor;
        }

        if (!firingOn) return;

        // 시각과 정확히 같은 origin/dir/dist로 판정한다(발사선-판정 일치, PRD §4 확정).
        RaycastHit[] hits = Physics.SphereCastAll(origin, beamRadius, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || !h.collider.CompareTag(playerTag)) continue;
            Rigidbody rb = h.collider.attachedRigidbody;
            if (rb == null) continue;

            // 지정 대상 외 경로상의 다른 도형도 전부 피격(PRD §5 "교차 피격" 확정).
            if (hitThisFiring.Add(rb))
                OnHazardHit?.Invoke(rb.gameObject);
        }
    }
}
