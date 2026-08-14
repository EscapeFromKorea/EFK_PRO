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
/// 파편을 절차적으로 만든다(2026-08-11 추가, 사용자 요청 — "파편이 후두둑 떨어지는 게 보였으면
/// 한다"). fragmentPrefabs를 채우면 그 프리팹이 우선한다.
///
/// 절차적 경로는 두 갈래다(2026-08-12).
/// - BoxCollider Breakable(벽·기둥 등 대부분) → SpawnGridFragments: 원본 형태를 격자로 갈라
///   "부서지기 직전 실루엣이 그대로 조각난" 그림을 만든다. 랜덤 큐브를 흩뿌리던 기존 연출은
///   원본과 무관한 모양·부피여서 벽이 증발한 뒤 자갈이 쏟아지는 것처럼 보였다.
/// - 그 외 콜라이더(구 등) → SpawnProceduralFragments: 격자로 자르면 구가 계단 모양이 되므로
///   기존 랜덤 파편 연출을 그대로 쓴다.
/// 경로 판별은 콜라이더 종류로 한다 — 어차피 형태 정합이 목적이라 디자이너가 따로 고를 이유가
/// 없고, 노브를 하나 더 늘리면 콜라이더와 어긋난 설정이 가능해진다.
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
    [Tooltip("조각 수 목표값 — 두 절차적 경로가 함께 쓴다. 격자 경로(BoxCollider): 이 값에 가까운 총 " +
             "조각 수가 나오도록 축별 분할 수를 자동 계산한다(비율은 오브젝트가 정한다 — 납작한 벽은 " +
             "넓은 면만, 긴 기둥은 긴 축만 쪼개진다). 구 폴백 경로: 이 값에 평균 스케일을 곱한 만큼 " +
             "랜덤 파편을 흩뿌린다. 두 경로 모두 아래 min/max로 clamp된다.")]
    public int proceduralFragmentCount = 8;

    [Tooltip("조각 수의 하한. 오브젝트를 작게 스케일해도 조각이 0~1개로 줄지 않게 막는다.")]
    public int minProceduralFragmentCount = 2;

    [Tooltip("조각 수의 상한. 격자 경로에서는 타격점 재분할까지 포함한 '총 조각 수'의 상한으로도 " +
             "쓰인다 — 큰 벽을 잘게 쪼갤 때 순간 부하가 튀는 것을 막는 안전장치다. 재분할은 셀 하나를 " +
             "8개로 바꿔 7개가 순증하므로, 이 값이 목표 조각 수보다 최소 7 이상 커야 타격점 재분할이 " +
             "실제로 일어난다 — 너무 빠듯하게 잡으면 재분할이 조용히 사라진다.")]
    public int maxProceduralFragmentCount = 24;

    [Tooltip("[구 폴백 경로 전용] 랜덤 파편 크기 — 원본 localScale에 곱하는 비율. 격자 경로는 원본을 " +
             "빈틈없이 나눠 갖기 때문에 이 값을 쓰지 않는다. 플레이테스트 결과 파편이 너무 커 " +
             "기존값(0.5)의 1/3로 줄였다(2026-08-11, 사용자 확정).")]
    public float proceduralFragmentSizeRatio = 0.1667f;

    [Tooltip("[구 폴백 경로 전용] 랜덤 파편이 스폰 직후 작은 크기에서 최종 크기로 커지는 데 걸리는 " +
             "시간(초). 0으로 두면 즉시 최종 크기로 등장한다. 격자 조각에는 일부러 적용하지 않는다 — " +
             "격자는 파괴 순간 이미 원본 실루엣을 이루므로, 키우는 연출을 넣으면 오히려 벽이 한 프레임 " +
             "증발했다가 부풀어 오르는 역효과가 난다.")]
    public float fragmentGrowDuration = 0.12f;

    [Tooltip("절차적 파편이 수명 끝에서 투명해지며 사라지는 데 걸리는 시간(초). 두 경로(격자/구 폴백) " +
             "공통이다 — 등장 연출은 경로마다 다르지만 퇴장은 같아도 된다. 0으로 두면 예전처럼 수명 " +
             "끝에 한 프레임에 사라진다(하드 팝). fragmentLifetime보다 크면 수명만큼으로 줄여 " +
             "적용된다. fragmentPrefabs로 지정한 아트 파편에는 적용되지 않는다(그쪽 소멸 연출은 " +
             "프리팹이 알아서 할 몫이다).")]
    public float fragmentFadeDuration = 0.6f;

    [Header("폭발력 (구 폴백 경로 전용)")]
    [Tooltip("[구 폴백 경로 전용] 랜덤 파편에 가할 폭발력 크기(Rigidbody.AddExplosionForce). 낮게 " +
             "잡아 밖으로 터지기보다 중력 위주로 '후두둑' 무너지는 느낌을 낸다(사용자 확정). 격자 " +
             "경로는 폭발이 아니라 '부딪혀 온 방향으로 밀린다'가 목적이라 아래 별도 속도 노브를 쓴다.")]
    public float explosionForce = 3f;

    [Tooltip("[구 폴백 경로 전용] 폭발력이 미치는 반경(Rigidbody.AddExplosionForce).")]
    public float explosionRadius = 2f;

    [Header("격자 분할 (BoxCollider Breakable 전용)")]
    [Tooltip("타격점에서 이 거리(월드 단위) 안에 있는 셀을 가까운 순서대로 한 번 더 2x2x2로 쪼갠다 — " +
             "직접 얻어맞은 부위가 더 잘게 부서지는 그림을 위해서다. 거리는 셀 중심이 아니라 셀 박스 " +
             "까지 재므로, 타격점이 든 칸은 셀 크기와 무관하게 항상 쪼개지고 이 값은 '맞은 칸 바깥으로 " +
             "얼마나 더 번지는가'를 뜻한다. 재분할은 1단계까지만 하고, 총 조각 수가 " +
             "maxProceduralFragmentCount를 넘게 되면 중단한다. 0이면 재분할하지 않는다.")]
    public float hitRefineRadius = 1f;

    [Tooltip("격자 조각이 튀어 나가는 속도 = 이 값 x 충돌 속도(relativeVelocity.magnitude). 속도를 " +
             "직접 주므로(ForceMode.VelocityChange) 조각 질량과 무관하게 결과가 같다 — FallingRock 파편, " +
             "PlayerJump.LaunchToHeight와 같은 방식이다. 타격점에서 먼 조각일수록 감쇠돼 느리게 밀린다.")]
    public float gridFragmentSpeed = 0.35f;

    // 조각별 산란 비율(주축은 어디까지나 밀려 들어온 방향이고, 여기에 '타격점에서 바깥으로' 성분을
    // 조금 섞어 조각들이 부채꼴로 벌어지게 한다)과 먼 조각의 최소 속도 비율. 노브로 뺄 만큼 의미 있는
    // 조정 축이 아니라 상수로 둔다.
    private const float GridScatterMix = 0.35f;
    private const float GridMinSpeedRatio = 0.25f;

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

        float impactSpeed = collision.relativeVelocity.magnitude;
        bool weightOk = PlayerWeight.Of(otherRb) >= requiredWeight;
        bool speedOk = impactSpeed >= breakThreshold;
        if (!weightOk || !speedOk) return;

        Break(collision.GetContact(0).point, impactSpeed);
    }

    private void Break(Vector3 hitPoint, float impactSpeed)
    {
        broken = true;

        GameObject prefab = (fragmentPrefabs != null && fragmentPrefabs.Count > 0)
            ? fragmentPrefabs[Random.Range(0, fragmentPrefabs.Count)]
            : null;

        if (prefab != null)
        {
            GameObject fragment = Instantiate(prefab, transform.position, transform.rotation);
            foreach (Rigidbody body in fragment.GetComponentsInChildren<Rigidbody>())
                body.AddExplosionForce(explosionForce, hitPoint, explosionRadius);
            Destroy(fragment, fragmentLifetime);
        }
        // 아래 두 갈래가 절차적 폴백이다. 리스트에 슬롯은 있지만 그 슬롯이 비어있는 경우(디자이너가
        // 아직 다 안 채움)도 여기로 온다 — 아니면 파괴됐는데 파편이 하나도 안 보이는 침묵 실패가 된다.
        else if (GetComponent<Collider>() is BoxCollider)
        {
            SpawnGridFragments(hitPoint, impactSpeed);
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
    /// (코드는 복제하지 않는다 — 이 저장소 관례상 작은 패턴은 컴포넌트마다 각자 구현한다), 위로
    /// 치우친 방향 없이 explosionForce·explosionRadius를 낮게 잡아 튀어 오르기보다 중력 위주로
    /// 무너지도록 했다(사용자 확정 — "후두둑 떨어지는" 느낌). 원본 렌더러의 머티리얼을 그대로
    /// 물려받아 균열 텍스처와 시각적으로 어울린다.
    ///
    /// 크기(fragmentScale)는 localScale에 곱하는 비율이라 원래도 오브젝트 스케일에 비례했다. 개수는
    /// 스케일과 무관하게 고정값이었는데(2026-08-11 발견), 레벨 디자이너가 같은 Breakable 프리팹을
    /// 상황마다 다르게 스케일해 배치할 걸 감안해 평균 스케일에 비례하도록 바꿨다 — 큰 벽이 작은
    /// 조각 4개로만 부서지면 허전해 보이고, 작은 오브젝트가 큰 오브젝트와 같은 개수를 뿌리면
    /// 조각당 밀도가 과해진다. min/max로 clamp해 극단적 스케일에서도 0개나 성능을 해치는 대량
    /// 생성으로 가지 않게 막는다.
    ///
    /// 스폰 위치 흩어짐 범위는 원본 오브젝트 크기 기준이다 — 조각들이 오브젝트 전체 면적 곳곳에서
    /// 나온 것처럼 보이려면 이 정도로 넓게 퍼뜨려야 한다(2026-08-11, "한 지점에서 우르르 나온다"
    /// 피드백으로 파편 크기 기준의 좁은 반경에서 되돌림 — 그 시도는 반경을 좁혀 "갑자기 변하는
    /// 느낌"을 고치려 했지만, 그 문제의 실제 원인은 스폰 위치가 아니라 파편이 스폰 즉시 최종
    /// 크기로 나타나는 것이었다. 그건 DestructionFragment(아래)가 담당하므로 범위는 원래 목적
    /// (오브젝트 전역에 고르게 분포)대로 되돌려도 된다). 다만 그 "오브젝트 크기"는 축별로 재야
    /// 한다 — 아래 spread 주석 참고(2026-08-12 수정).</summary>
    private void SpawnProceduralFragments(Vector3 hitPoint)
    {
        if (proceduralFragmentSizeRatio <= 0f) return;

        float avgScale = (transform.localScale.x + transform.localScale.y + transform.localScale.z) / 3f;
        int fragmentCount = Mathf.Clamp(Mathf.RoundToInt(proceduralFragmentCount * avgScale),
            minProceduralFragmentCount, maxProceduralFragmentCount);
        if (fragmentCount <= 0) return;

        // GetComponentInChildren이다 - 이 저장소의 계층 관례는 시각 메쉬를 자식에 두는 쪽이고
        // (Player_MeshVisual, Hourglass_RockVisual), 루트에서만 찾으면 그런 Breakable에서 렌더러가
        // null이 돼 아래 바운즈가 1x1x1 폴백으로 떨어진다 - 12x8 벽이 1 크기 조각 더미로 부서지는
        // 침묵 실패였다(2026-08-14 코드 리뷰에서 발견. 머티리얼도 못 물려받아 파편이 흰색이 됐다).
        Renderer sourceRenderer = GetComponentInChildren<Renderer>();
        Material sharedMat = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
        Vector3 fragmentScale = transform.localScale * proceduralFragmentSizeRatio;

        // 흩뿌리는 범위는 원본 렌더러의 로컬 바운즈에 내접하는 타원체다. 축별 반경을 따로 쓰는 게
        // 핵심 - 예전엔 "가장 긴 축의 절반"을 반지름으로 한 등방 구였는데(FallingRock.Shatter에서
        // 그대로 가져온 식), 낙석과 달리 Breakable은 납작한 벽처럼 비균일 스케일로 배치되는 게
        // 정상이라 얇은 축으로 형태 밖까지 파편이 튀어나왔다(2026-08-12 사용자 피드백). 회전과
        // 부모 스케일도 함께 반영한다(lossyScale + transform.rotation) - 오프셋만 월드 축이면
        // 기울어 배치한 벽에서 다시 어긋난다. 파편 자신의 외접반경만큼 안쪽으로 좁혀, 파편 중심이
        // 표면에 놓여 반쪽이 밖으로 삐져나오는 것까지 막는다(Random.rotation이라 축별이 아니라
        // 외접반경 기준). 얇은 축은 0으로 clamp돼 그 축에선 중심면에 정렬된다.
        Bounds localBounds = sourceRenderer != null
            ? sourceRenderer.localBounds
            : new Bounds(Vector3.zero, Vector3.one);
        Vector3 spread = Vector3.Max(
            Vector3.Scale(localBounds.extents, transform.lossyScale) - Vector3.one * (fragmentScale.magnitude * 0.5f),
            Vector3.zero);
        Vector3 spawnCenter = transform.TransformPoint(localBounds.center);

        for (int i = 0; i < fragmentCount; i++)
        {
            GameObject fragment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fragment.name = $"{name}_Fragment{i}";
            fragment.transform.SetPositionAndRotation(
                spawnCenter + transform.rotation * Vector3.Scale(Random.insideUnitSphere, spread),
                Random.rotation);
            fragment.transform.localScale = fragmentScale;
            if (sharedMat != null) fragment.GetComponent<Renderer>().sharedMaterial = sharedMat;

            Rigidbody fragBody = fragment.AddComponent<Rigidbody>();
            // 가볍게 잡아 플레이어를 밀지 못하게 한다(FallingRockSystem 파편과 같은 이유 —
            // FallingRockSystem/CLAUDE.md "파편 질량 = 원본 / 파편수" 참고).
            fragBody.mass = 0.3f;
            // 파편들은 좁은 범위에 겹쳐 스폰되고 DestructionFragment가 콜라이더째 키우므로, 기본값
            // 그대로면 PhysX 겹침 해소가 폭발력보다 세게 서로를 밀어내 파편이 넓게 퍼진다.
            fragBody.maxDepenetrationVelocity = 1f;
            fragBody.AddExplosionForce(explosionForce, hitPoint, explosionRadius);

            // 수명·소멸도 DestructionFragment가 맡는다(Destroy(obj, t)를 쓰지 않는다) — 페이드가
            // 끝난 뒤에 사라져야 하므로 둘을 한 컴포넌트가 함께 봐야 한다.
            DestructionFragment growth = fragment.AddComponent<DestructionFragment>();
            growth.growDuration = fragmentGrowDuration;
            growth.lifetime = fragmentLifetime;
            growth.fadeDuration = fragmentFadeDuration;
        }
    }

    /// <summary>원본을 격자로 갈라 조각을 만든다(BoxCollider Breakable 전용). 조각들의 합은 원본
    /// 부피와 정확히 같고 틈도 겹침도 없어서, 파괴되는 순간의 실루엣이 원본 그대로다 — 랜덤 큐브를
    /// 흩뿌리던 기존 연출이 "벽이 증발한 뒤 자갈이 쏟아진다"로 보였던 것을 고치기 위한 것이라 이
    /// 정합이 핵심이다(2026-08-12, 사용자 요청).
    ///
    /// [튀는 방향을 속도 벡터가 아니라 기하로 잡는 이유]
    /// relativeVelocity나 상대 Rigidbody의 velocity는 부호 규약과 충돌 해소 타이밍에 따라 씬에서
    /// 실측하지 않으면 방향이 뒤집힐 수 있다. 대신 (원본 중심 - 타격점)을 쓴다 — 타격점은 플레이어가
    /// 부딪힌 면 위에 있고 중심은 그 반대편이므로, 이 벡터가 곧 "밀고 들어온 방향"이고 부호 모호성이
    /// 없다. 세기에만 relativeVelocity의 크기를 쓴다(크기는 부호 문제가 없다).</summary>
    private void SpawnGridFragments(Vector3 hitPoint, float impactSpeed)
    {
        // 랜덤 경로와 같은 이유로 GetComponentInChildren이다 — 거기 주석 참고. 격자 경로에서는
        // 형태 정합이 기능의 전부라 렌더러를 놓치면 기믹 자체가 무의미해진다.
        Renderer sourceRenderer = GetComponentInChildren<Renderer>();
        Bounds localBounds = sourceRenderer != null
            ? sourceRenderer.localBounds
            : new Bounds(Vector3.zero, Vector3.one);

        // 랜덤 경로와 같은 기준이다 — localScale만 쓰면 부모 스케일과 메쉬 오프셋에서 어긋난다.
        // 음수 스케일로 뒤집어 배치한 오브젝트에서도 크기가 음수가 되지 않게 절댓값을 쓴다.
        Vector3 lossy = transform.lossyScale;
        Vector3 worldSize = Vector3.Scale(localBounds.size,
            new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
        Vector3 boundsCenter = transform.TransformPoint(localBounds.center);
        Quaternion rot = transform.rotation;

        Vector3Int div = ComputeGridDivisions(worldSize, proceduralFragmentCount,
            minProceduralFragmentCount, maxProceduralFragmentCount);
        List<GridCell> cells = BuildGridCells(worldSize, div);

        // 셀 좌표계는 "회전을 뺀 박스 중심 기준"이다. 회전은 거리를 보존하므로 타격점만 같은 프레임
        // 으로 옮겨 놓으면 여기서 잰 거리가 그대로 월드 거리다.
        Vector3 localHit = Quaternion.Inverse(rot) * (hitPoint - boundsCenter);
        RefineCellsNearHit(cells, localHit, hitRefineRadius, maxProceduralFragmentCount);

        Material sharedMat = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
        // 기준은 transform.position이 아니라 boundsCenter다 — 피벗이 구석에 박힌 임포트 메쉬에서도
        // "부딪힌 면에서 형태의 중심 쪽"이라는 의미가 유지된다(셀 배치도 같은 기준을 쓴다).
        Vector3 toCenter = boundsCenter - hitPoint;
        Vector3 pushDir = toCenter.sqrMagnitude > 1e-6f ? toCenter.normalized : Vector3.up;
        float speedBase = gridFragmentSpeed * impactSpeed;
        // 감쇠 기준 거리는 오브젝트 자신의 크기다 — 작은 상자든 큰 벽이든 "타격점 근처는 빠르고
        // 반대편 끝은 느리게 밀린다"가 같은 그림으로 나온다.
        float falloffRange = Mathf.Max(worldSize.magnitude * 0.5f, 0.01f);

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 pos = boundsCenter + rot * cells[i].center;

            GameObject fragment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fragment.name = $"{name}_Piece{i}";
            // 조각은 부모 없는 루트지만 회전은 원본을 그대로 물려받는다 — 기울여 배치한 벽에서도
            // 조각들이 원본 실루엣에 정렬된 채로 갈라진다.
            fragment.transform.SetPositionAndRotation(pos, rot);
            fragment.transform.localScale = cells[i].size;
            if (sharedMat != null) fragment.GetComponent<Renderer>().sharedMaterial = sharedMat;

            Rigidbody body = fragment.AddComponent<Rigidbody>();
            // 가볍게 잡아 플레이어(무게 1.5~3.0)를 밀지 못하게 한다(랜덤 경로/FallingRock 파편과 같은 이유).
            body.mass = 0.3f;
            // 격자 조각은 면끼리 맞닿은 채로 생기므로 PhysX contact offset만큼 미세 겹침이 생긴다.
            // 기본값이면 그 겹침 해소 속도가 조각을 튀는 속도보다 세게 밀어내 형태가 즉시 흩어진다.
            body.maxDepenetrationVelocity = 1f;

            Vector3 fromHit = cells[i].center - localHit;
            float dist = fromHit.magnitude;
            Vector3 scatter = dist > 1e-4f ? rot * (fromHit / dist) : Vector3.zero;
            Vector3 dir = (pushDir + scatter * GridScatterMix).normalized;
            float falloff = Mathf.Lerp(1f, GridMinSpeedRatio, Mathf.Clamp01(dist / falloffRange));
            body.AddForce(dir * (speedBase * falloff), ForceMode.VelocityChange);

            // growDuration 0 — 격자 조각에 성장 연출은 금물이다(위 클래스 주석). 소멸 페이드만
            // 랜덤 경로와 공유한다: 등장은 경로마다 달라야 하지만 퇴장이 하드 팝인 건 양쪽 다 문제였다.
            DestructionFragment life = fragment.AddComponent<DestructionFragment>();
            life.growDuration = 0f;
            life.lifetime = fragmentLifetime;
            life.fadeDuration = fragmentFadeDuration;
        }
    }

    /// <summary>격자 셀 하나. 중심은 "회전을 뺀 박스 중심 기준" 월드 스케일 오프셋이고 크기는 월드
    /// 단위다(조각의 localScale로 그대로 들어간다).</summary>
    public struct GridCell
    {
        public Vector3 center;
        public Vector3 size;
    }

    /// <summary>목표 조각 수에 가장 가깝게, 그러면서 셀이 최대한 정육면체에 가깝게 나오도록 축별
    /// 분할 수를 정한다. 매번 "지금 셀에서 가장 긴 변"의 축만 하나 더 쪼개는 그리디라, 납작한 벽은
    /// 두께 축이 1로 남고 긴 기둥은 긴 축만 쪼개진다 — 디자이너가 축별 분할 수를 손으로 맞출 필요도,
    /// 리사이즈할 때마다 다시 손댈 필요도 없다. 총 개수가 상한을 넘게 되는 시점에서 멈춘다.
    /// (에디터 자체 점검 DestructionMenuItem.ValidateGridSplit이 이 함수를 직접 검증한다.)</summary>
    public static Vector3Int ComputeGridDivisions(Vector3 worldSize, int targetCount, int minCount, int maxCount)
    {
        int maxTotal = Mathf.Max(1, maxCount);
        int target = Mathf.Clamp(targetCount, Mathf.Clamp(minCount, 1, maxTotal), maxTotal);

        Vector3 size = new Vector3(
            Mathf.Max(Mathf.Abs(worldSize.x), 1e-4f),
            Mathf.Max(Mathf.Abs(worldSize.y), 1e-4f),
            Mathf.Max(Mathf.Abs(worldSize.z), 1e-4f));

        Vector3Int div = Vector3Int.one;
        int product = 1;
        while (product < target)
        {
            int axis = 0;
            float longest = size.x / div.x;
            if (size.y / div.y > longest) { longest = size.y / div.y; axis = 1; }
            if (size.z / div.z > longest) { axis = 2; }

            int next = product / div[axis] * (div[axis] + 1);
            if (next > maxTotal) break;

            div[axis] += 1;
            product = next;
        }

        return div;
    }

    /// <summary>분할 수대로 박스를 빈틈없이 채우는 셀 목록을 만든다. 셀 크기가 size/div의 정확한
    /// 분할이라 합이 원본 부피와 일치한다.</summary>
    public static List<GridCell> BuildGridCells(Vector3 worldSize, Vector3Int div)
    {
        var cells = new List<GridCell>(div.x * div.y * div.z);
        Vector3 cellSize = new Vector3(worldSize.x / div.x, worldSize.y / div.y, worldSize.z / div.z);
        Vector3 min = worldSize * -0.5f;

        for (int x = 0; x < div.x; x++)
            for (int y = 0; y < div.y; y++)
                for (int z = 0; z < div.z; z++)
                {
                    cells.Add(new GridCell
                    {
                        center = min + Vector3.Scale(cellSize, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)),
                        size = cellSize
                    });
                }

        return cells;
    }

    /// <summary>타격점 반경 안의 셀을 <b>가까운 순서대로</b> 2x2x2로 한 번 더 쪼갠다. 쪼갠 결과는
    /// 리스트 뒤에 붙고 순회는 뒤에서 앞으로 가므로 재귀가 1단계에서 자연히 멈춘다(더 깊게 가면 조각
    /// 수가 폭발한다).
    ///
    /// [왜 거리순 정렬이 필수인가]
    /// 총 조각 수 상한이 재분할 예산도 겸하기 때문에, 반경 안 셀을 전부 쪼갤 수 있는 경우는 오히려
    /// 드물다 — 작은 오브젝트는 모든 셀이 반경 안에 들어오는데 예산은 한두 개뿐이다. 정렬 없이 리스트
    /// 순서대로 쪼개면 BuildGridCells가 쌓은 순서(x→y→z) 탓에 예산이 항상 +x+y+z 구석 셀에 쓰여,
    /// 얻어맞은 데가 아니라 엉뚱한 모서리가 잘게 부서진다(2026-08-12 발견).</summary>
    public static void RefineCellsNearHit(List<GridCell> cells, Vector3 localHit, float radius, int maxTotal)
    {
        if (radius <= 0f) return;

        // 먼 것부터 오도록 정렬한다 — 아래 순회가 뒤에서 앞으로 가므로 가까운 셀이 먼저 쪼개진다.
        cells.Sort((a, b) => SqrDistanceToCell(b, localHit).CompareTo(SqrDistanceToCell(a, localHit)));

        float sqrRadius = radius * radius;
        for (int i = cells.Count - 1; i >= 0; i--)
        {
            if (cells.Count + 7 > maxTotal) return; // 하나를 8개로 바꾸면 7개가 순증한다.
            if (SqrDistanceToCell(cells[i], localHit) > sqrRadius) continue;

            Vector3 half = cells[i].size * 0.5f;
            Vector3 center = cells[i].center;
            cells.RemoveAt(i);

            for (int s = 0; s < 8; s++)
            {
                Vector3 offset = new Vector3(
                    ((s & 1) == 0 ? -0.5f : 0.5f) * half.x,
                    ((s & 2) == 0 ? -0.5f : 0.5f) * half.y,
                    ((s & 4) == 0 ? -0.5f : 0.5f) * half.z);
                cells.Add(new GridCell { center = center + offset, size = half });
            }
        }
    }

    /// <summary>타격점에서 셀 <b>박스</b>까지의 제곱거리(셀 중심까지가 아니다). 안에 들어있으면 0이다.
    ///
    /// 중심 거리로 재면 반경의 의미가 셀 크기에 따라 달라진다 — 12x8 벽은 셀 하나가 3x4라 타격점이
    /// 든 셀조차 중심까지 2.5나 떨어져, 기본 반경 1로는 재분할이 한 번도 일어나지 않았다(2026-08-12
    /// 발견. 벽에 부딪혀 통로를 여는 게 이 기믹의 주 용도라 하필 가장 중요한 경우에서 기능이 죽어
    /// 있었다). 박스 거리로 재면 맞은 칸은 크기와 무관하게 항상 쪼개지고, 반경은 "맞은 칸 바깥으로
    /// 얼마나 더 번지는가"라는 일관된 뜻이 된다.</summary>
    private static float SqrDistanceToCell(GridCell cell, Vector3 point)
    {
        Vector3 half = cell.size * 0.5f;
        Vector3 gap = new Vector3(
            Mathf.Max(0f, Mathf.Abs(point.x - cell.center.x) - half.x),
            Mathf.Max(0f, Mathf.Abs(point.y - cell.center.y) - half.y),
            Mathf.Max(0f, Mathf.Abs(point.z - cell.center.z) - half.z));
        return gap.sqrMagnitude;
    }
}
