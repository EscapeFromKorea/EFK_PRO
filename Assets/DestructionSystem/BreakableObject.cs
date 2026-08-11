using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무게+속도 조건을 동시에 만족하는 충돌로 부서지는 장애물. 발판을 밟거나 위에 올라서야 반응하는
/// 계열(DoorSystem/ExitWeightPlate)과 달리, "얼마나 세게 부딪혔는가" 자체가 트리거다 — 가속
/// 발판이나 실타래 스윙으로 가속한 도형이 벽에 부딪혀 깨뜨리면 새 통로가 열리는 레벨 디자인
/// 의도다(docs/PRD/Destruction.md 1.1절 참고).
///
/// 무게 판정은 raw Rigidbody.mass가 아니라 저장소 공통 창구 PlayerWeight.Of(rb)를 쓴다 — 무중력
/// 버블 등으로 실효 무게가 바뀐 도형이 여전히 원래 무게로 판정되는 모순을 막기 위해서다(다른
/// 무게 게이트 기믹 전부와 같은 기준).
///
/// fragmentPrefabs는 Tools 생성 시 비워 둔 채로 만들어진다 — 레벨 디자이너가 직접 채울 때까지는
/// SpawnProceduralFragments가 대신 큐브 파편을 절차적으로 흩뿌린다(2026-08-11 추가, 사용자 요청 —
/// "파편이 후두둑 떨어지는 게 보였으면 한다"). fragmentPrefabs를 채우면 그 프리팹이 우선한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableObject : MonoBehaviour
{
    [Header("파괴 조건 (AND)")]
    [Tooltip("PlayerWeight.Of(rb) 기준 임계 무게. 기본값 3은 정육면체(무게 3.0) 단독 통과를 의도한 " +
             "값이다 — 구+세모 협동만 통과시키려면 더 높게 잡는다(PlayerShapeStats: 구 1.5/세모 1.0/" +
             "네모 3.0). 속도 조건과 별개의 AND 조건이라, 무게를 넘겨도 느리게 부딪히면 깨지지 않는다.")]
    public float requiredWeight = 3f;

    [Tooltip("충돌 속도 임계값(collision.relativeVelocity.magnitude). 무게 조건과 AND로 결합된다.")]
    public float breakThreshold = 5f;

    [Header("파편")]
    [Tooltip("파괴 시 이 중 하나를 무작위로 Instantiate한다. 비어 있으면(Tools 생성 직후 기본 상태) " +
             "대신 아래 절차적 파편을 생성한다 — 레벨 디자이너가 실제 아트 프리팹을 채우면 그쪽을 " +
             "우선 쓴다.")]
    public List<GameObject> fragmentPrefabs = new List<GameObject>();

    [Tooltip("생성된 파편이 자동으로 사라지기까지의 시간(초).")]
    public float fragmentLifetime = 5f;

    [Header("절차적 파편 (fragmentPrefabs가 비어 있을 때만 사용)")]
    [Tooltip("절차적으로 생성할 파편 개수. FallingRockSystem의 shardCount(기본 6)보다 적게 잡아 " +
             "조각 하나하나가 더 크게 보이게 한다.")]
    public int proceduralFragmentCount = 4;

    [Tooltip("절차적 파편 크기 — 원본 localScale에 곱하는 비율. FallingRockSystem의 " +
             "shardSizeRatio(기본 0.35)보다 크게 잡았다(사용자 요청 — 낙석 파편보다 큰 파편).")]
    public float proceduralFragmentSizeRatio = 0.5f;

    [Header("폭발력")]
    [Tooltip("파편에 가할 폭발력 크기(Rigidbody.AddExplosionForce). 낮게 잡아 밖으로 터지기보다 " +
             "중력 위주로 '후두둑' 무너지는 느낌을 낸다(사용자 확정).")]
    public float explosionForce = 3f;

    [Tooltip("폭발력이 미치는 반경(Rigidbody.AddExplosionForce).")]
    public float explosionRadius = 2f;

    private bool broken;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = false;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (broken) return;

        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null) return; // Rigidbody 없는 상대는 무게를 잴 수 없어 판별 대상에서 제외한다.

        bool weightOk = PlayerWeight.Of(otherRb) >= requiredWeight;
        bool speedOk = collision.relativeVelocity.magnitude >= breakThreshold;
        if (!weightOk || !speedOk) return;

        Break(collision.GetContact(0).point);
    }

    private void Break(Vector3 hitPoint)
    {
        broken = true;

        if (fragmentPrefabs != null && fragmentPrefabs.Count > 0)
        {
            GameObject prefab = fragmentPrefabs[Random.Range(0, fragmentPrefabs.Count)];
            if (prefab != null)
            {
                GameObject fragment = Instantiate(prefab, transform.position, transform.rotation);
                foreach (Rigidbody body in fragment.GetComponentsInChildren<Rigidbody>())
                    body.AddExplosionForce(explosionForce, hitPoint, explosionRadius);
                Destroy(fragment, fragmentLifetime);
            }
        }
        else
        {
            SpawnProceduralFragments(hitPoint);
        }

        // 이중 충돌 방지 — 비활성화 전에 콜라이더/리지드바디를 먼저 정지시킨다(PRD 3.1 의사코드 참고).
        GetComponent<Collider>().enabled = false;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        gameObject.SetActive(false);
    }

    /// <summary>fragmentPrefabs가 비어 있을 때(아트 없이도 바로 파편이 보이도록) 원본 위치 근방에
    /// 작은 큐브 조각을 흩뿌린다. FallingRockSystem.Shatter의 절차적 생성 아이디어를 참고했지만
    /// (코드는 복제하지 않는다 — 이 저장소 관례상 작은 패턴은 컴포넌트마다 각자 구현한다), 조각을
    /// 더 크고 적게 잡고(proceduralFragmentSizeRatio 0.5 vs FallingRock 0.35), 위로 치우친 방향
    /// 없이 explosionForce·explosionRadius를 낮게 잡아 튀어 오르기보다 중력 위주로 무너지도록
    /// 했다(사용자 확정 — "후두둑 떨어지는" 느낌). 원본 렌더러의 머티리얼을 그대로 물려받아
    /// 균열 텍스처와 시각적으로 어울린다.</summary>
    private void SpawnProceduralFragments(Vector3 hitPoint)
    {
        if (proceduralFragmentCount <= 0 || proceduralFragmentSizeRatio <= 0f) return;

        Renderer sourceRenderer = GetComponent<Renderer>();
        Material sharedMat = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
        Vector3 fragmentScale = transform.localScale * proceduralFragmentSizeRatio;
        // 흩뿌리는 반경은 원본 절반 정도 - 조각들이 원본이 있던 자리에서 갈라져 나온 것처럼 보인다
        // (FallingRock.Shatter와 같은 계산).
        float spread = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z) * 0.5f;

        for (int i = 0; i < proceduralFragmentCount; i++)
        {
            GameObject fragment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fragment.name = $"{name}_Fragment{i}";
            fragment.transform.SetPositionAndRotation(
                transform.position + Random.insideUnitSphere * spread, Random.rotation);
            fragment.transform.localScale = fragmentScale;
            if (sharedMat != null) fragment.GetComponent<Renderer>().sharedMaterial = sharedMat;

            Rigidbody fragBody = fragment.AddComponent<Rigidbody>();
            // 가볍게 잡아 플레이어를 밀지 못하게 한다(FallingRockSystem 파편과 같은 이유 —
            // FallingRockSystem/CLAUDE.md "파편 질량 = 원본 / 파편수" 참고).
            fragBody.mass = 0.3f;
            fragBody.AddExplosionForce(explosionForce, hitPoint, explosionRadius);

            Destroy(fragment, fragmentLifetime);
        }
    }
}
