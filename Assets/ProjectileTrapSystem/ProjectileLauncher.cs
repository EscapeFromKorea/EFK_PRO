using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 측면 발사 함정 — 발사구. 상태 `정지 → 예고(W초) → 발사 1회 → 휴식(R초) → 예고`를 반복한다(사양
/// §3·§4). 발사 방향은 <c>transform.forward</c>(씬에서 이 오브젝트를 돌려 조준한다) — 별도 방향
/// 필드를 두지 않는다.
///
/// [정지 → 시작은 겹치지 않는다] <see cref="Activate"/>는 이미 도는 주기 중에 다시 불려도 아무 일도
/// 하지 않는다(사양 §3 확정). <see cref="Deactivate"/>는 예약된 발사를 취소하고 이 발사구가 낸 탄을
/// 전부 지운다 — PRD §4 "전체 재시작/구간 종료"의 개별 발사구 몫이다. 여러 발사구를 챕터 단위로
/// 한꺼번에 재시작하는 오케스트레이션은 아직 저장소에 없다(챕터 재시작 시스템 자체가 없어
/// PeriodicTrap과 같은 이유로 TBD로 남긴다 — CLAUDE.md 참고).
///
/// [피격 알림은 SectionHitCounter 경유 구간 복귀] <see cref="OnHazardHit"/>은 씬에서 챕터별
/// SectionHitCounter.RegisterHitEvent에 배선한다. CH3/CH5가 서로 다른 목적지("CH3 시작"/"CH5 시작")로
/// 가야 한다는 요구는 인스턴스마다 다른 카운터를 꽂는 것으로 끝나고, 이 스크립트는 목적지를 모른다.
/// </summary>
public class ProjectileLauncher : MonoBehaviour
{
    private enum Phase { Stopped, Warning, Resting }

    [System.Serializable]
    public class PlayerHitEvent : UnityEvent<GameObject> { }

    [Header("주기 (사양 §4 W/R — 그레이박스 기본값, 배치 시 실측)")]
    [Tooltip("예고 지속시간(초, W). W ≥ 0.")]
    public float warningSeconds = 1f;

    [Tooltip("발사 후 다음 예고까지의 휴식 시간(초, R). R > 0.")]
    public float restSeconds = 2f;

    [Header("탄 (사양 §4 V/L/반경 — 그레이박스 기본값)")]
    [Tooltip("탄속(U/s, V). V > 0.")]
    public float projectileSpeed = 8f;

    [Tooltip("탄 수명(초, L) — 벽 충돌/유효 대상 접촉이 없어도 이 시간 뒤 소멸한다.")]
    public float projectileLifetime = 5f;

    [Tooltip("탄 반경(U).")]
    public float projectileRadius = 0.3f;

    [Tooltip("탄 프리팹(선택). 비우면 기본 구체(그레이박스)를 즉석에서 만든다.")]
    public GameObject projectilePrefab;

    [Header("동작")]
    [Tooltip("씬 시작과 동시에 주기를 켠다. 끄면 외부(레벨 진입 로직 등)가 Activate()를 불러야 " +
             "발사가 시작된다 — 지금은 그런 오케스트레이션이 저장소에 없어 기본값은 켬이다.")]
    public bool autoActivateOnStart = true;

    [Tooltip("맞은 대상을 판별할 태그.")]
    public string playerTag = "Player";

    [Header("예고 표시 (선택)")]
    public ProjectileWarning warning;

    [Tooltip("위험부에 유효 접촉이 발생하면 발화(인자 = 맞은 플레이어 Root). 챕터별 " +
             "SectionHitCounter.RegisterHitEvent에 배선한다.")]
    public PlayerHitEvent OnHazardHit;

    private Phase phase = Phase.Stopped;
    private float phaseElapsed;
    private readonly List<Projectile> live = new List<Projectile>();

    /// <summary>이미 도는 주기 중이면 아무 일도 하지 않는다(사양 §3 "시작 입력 중첩 금지").</summary>
    public void Activate()
    {
        if (phase != Phase.Stopped) return;
        EnterPhase(Phase.Warning);
    }

    /// <summary>예약된 발사를 취소하고 이 발사구가 낸 탄을 전부 지운 뒤 정지한다.</summary>
    public void Deactivate()
    {
        phase = Phase.Stopped;
        phaseElapsed = 0f;
        warning?.SetWarning(false);

        foreach (Projectile p in live)
            if (p != null) Destroy(p.gameObject);
        live.Clear();
    }

    private void Start()
    {
        if (autoActivateOnStart) Activate();
    }

    private void FixedUpdate()
    {
        live.RemoveAll(p => p == null);

        switch (phase)
        {
            case Phase.Warning:
                phaseElapsed += Time.fixedDeltaTime;
                if (phaseElapsed >= warningSeconds)
                {
                    Fire();
                    EnterPhase(Phase.Resting);
                }
                break;

            case Phase.Resting:
                phaseElapsed += Time.fixedDeltaTime;
                if (phaseElapsed >= restSeconds) EnterPhase(Phase.Warning);
                break;
        }
    }

    private void EnterPhase(Phase next)
    {
        phase = next;
        phaseElapsed = 0f;
        warning?.SetWarning(next == Phase.Warning);
    }

    private void Fire()
    {
        GameObject go = projectilePrefab != null
            ? Instantiate(projectilePrefab, transform.position, transform.rotation)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        if (projectilePrefab == null)
        {
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);
            go.transform.localScale = Vector3.one * (projectileRadius * 2f);
        }
        go.name = $"Projectile_{name}";

        // 다이내믹이어야 벽(Rigidbody 없는 정적 콜라이더)과도 OnCollisionEnter가 발화한다
        // (Projectile 클래스 주석 — 키네마틱은 정적 콜라이더와 아예 반응하지 않는다).
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;

        SphereCollider col = go.GetComponent<SphereCollider>();
        if (col == null) col = go.AddComponent<SphereCollider>();
        col.isTrigger = false; // 기본 구체는 콜라이더 반경 0.5 × 위 스케일(radius*2) = 월드 반경 radius

        Projectile projectile = go.GetComponent<Projectile>();
        if (projectile == null) projectile = go.AddComponent<Projectile>();
        projectile.Launch(this, transform.forward * projectileSpeed, projectileLifetime, playerTag);

        live.Add(projectile);
    }

    /// <summary>Projectile이 유효 대상 접촉을 보고한다(대상당 1회는 Projectile 쪽이 보장).</summary>
    public void ReportHit(GameObject playerRoot) => OnHazardHit?.Invoke(playerRoot);

    /// <summary>탄이 스스로 소멸할 때(수명/벽 충돌) 목록에서 빠진다. Deactivate가 이미 지운 탄이면
    /// 목록에 없어 아무 일도 하지 않는다.</summary>
    public void NotifyDespawned(Projectile p) => live.Remove(p);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 3f);
        Gizmos.DrawWireSphere(transform.position + transform.forward * 3f, 0.15f);
    }
}
