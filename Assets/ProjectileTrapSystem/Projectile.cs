using UnityEngine;

/// <summary>
/// 측면 발사 함정 — 탄. <see cref="ProjectileLauncher"/>가 발사마다 생성한다. 정해진 방향으로 등속
/// 이동하며 표적을 추적하지 않는다(사양 §3). 벽 충돌 / 유효 대상 접촉 / 수명 중 가장 먼저 만족하는
/// 조건에서 소멸하고, 소멸 후에는 재피해를 주지 않는다.
///
/// [다이내믹 Rigidbody다 — 키네마틱이 아니다] 처음엔 PeriodicTrapSystem처럼 키네마틱 +
/// MovePosition으로 만들었는데, **키네마틱 Rigidbody는 Rigidbody가 없는 정적 콜라이더(레벨 벽)와
/// 부딪혀도 OnCollisionEnter가 아예 발화하지 않는다**(Unity 충돌 매트릭스: 키네마틱 vs 정적 = 무반응,
/// 키네마틱 vs 다이내믹만 반응). PeriodicTrap의 트랩은 플레이어(다이내믹)만 감지하면 돼서 이 구멍을
/// 안 겪었는데, 탄은 벽도 감지해야 해서 실측에서 "벽을 그냥 통과한다"로 드러났다(2026-09-22).
/// 다이내믹(중력·드래그 0, 속도 1회 대입)으로 바꾸면 벽(정적)과도 정상 발화한다 — 등속이 충돌
/// 반작용으로 한 프레임 꺾일 수 있지만, 어차피 그 프레임에 바로 Destroy하므로 보이지 않는다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class Projectile : MonoBehaviour
{
    private ProjectileLauncher launcher;
    private string playerTag = "Player";
    private float lifetime = 5f;
    private float spawnTime;
    private bool despawned;
    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.drag = 0f;
        body.angularDrag = 0f;
    }

    /// <summary>발사 직후 발사구가 1회 호출해 초기화한다. AddComponent 시점의 Awake는 이미 지나가
    /// 있어 필드를 나중에 대입해도 못 본다 — 그래서 명시적 진입점을 둔다.</summary>
    public void Launch(ProjectileLauncher owner, Vector3 initialVelocity, float lifetimeSeconds,
                       string targetTag)
    {
        launcher = owner;
        playerTag = targetTag;
        lifetime = lifetimeSeconds;
        spawnTime = Time.time;
        body.velocity = initialVelocity;
    }

    private void FixedUpdate()
    {
        if (Time.time - spawnTime >= lifetime) Despawn();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (despawned) return;

        if (collision.collider.CompareTag(playerTag))
        {
            GameObject playerRoot = collision.rigidbody != null
                ? collision.rigidbody.gameObject
                : collision.collider.gameObject;
            launcher?.ReportHit(playerRoot);
        }

        Despawn(); // 벽이든 유효 대상이든 최초 접촉에서 소멸(사양 §3 "가장 먼저 만족하는 조건")
    }

    private void Despawn()
    {
        if (despawned) return;
        despawned = true;
        launcher?.NotifyDespawned(this);
        Destroy(gameObject);
    }
}
