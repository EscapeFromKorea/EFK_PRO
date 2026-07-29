using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 낙석(무너져 내리는 꿈의 파편) 스포너 — 정해진 지점들에서 일정 주기로 낙석을 떨어뜨린다.
/// 낙석 자체는 평범한 dynamic Rigidbody로 중력에 의해 떨어지고(FallingRock 주석 참고), 이 컴포넌트는
/// 스폰 타이밍·규격·피격 집계만 담당한다.
///
/// [존재 이유 — 모래시계 슬로우와 짝을 이루는 타이밍 퍼즐 (사양 §2)]
/// 세모/네모가 몽환의 모래시계를 쳐서 감속 구역(HourglassSystem/SlowZone)을 켜면 낙하 중인 낙석이
/// 느려지고, 그 틈으로 이동속도 7 U/s인 구가 낙석 구간을 뚫고 통과한다. 낙석은 단독으로 완결되는
/// 기믹이 아니라 감속 구역 안에 배치해야 의미가 있는 종속 기믹이다.
///
/// [기획 확정 사항 (2026-07-29) — 임의로 바꾸지 말 것]
/// - Q1 별도 기믹으로 분리(이 폴더). Q3 낙하 패턴은 **고정 타이밍**(랜덤·웨이브 아님) → 그래서
///   사양 §4의 rockPattern에 해당하는 파라미터가 아예 없다. 랜덤을 넣으면 확정 사항 위반이다.
/// - Q2 지금은 물리 넉백/밀려남만. 리스폰·HP는 저장소에 존재하지 않고 여기서 만들지 않는다.
///   대신 피격 횟수 카운트와 임계 초과 알림(OnHitThresholdExceeded)까지만 넣어, 나중에 다른
///   사람이 리스폰 시스템을 머지하면 **인스펙터에서 이벤트에 배선만 하면 되게** 열어 두었다
///   (RainbowBridgeSwitch가 targetObjects를 public으로 빼둔 것과 같은 취지).
///   ★ 리스폰 동작이 없는 것은 미완성이 아니라 의도다. 여기에 리스폰을 구현하지 마라. ★
///
/// [파라미터 유도 — 사양 §4는 전부 [TBD]라 §0 공통 전제와 실제 도형 스탯에서 계산으로 정했다]
/// 전제: 1 Unit = 1m, 옆모습 2D 횡스크롤(전진 = Z축), 구 이속 7 U/s·지름 1 U(반지름 0.5, 스케일 1),
/// 낙석 1 U 큐브, 감속 구역은 Tools &gt; Hourglass 기본값인 6x6x6 볼륨(slowMultiplier 0.5,
/// maxFallSpeed 2 U/s), 스폰은 구역 상단·통과 통로는 구역 하단(낙하 6 U).
///
///   (1) 낙하 시간 = 통과 창의 원천
///       - 감속 없음: 6 U 자유낙하 = √(2·6/9.81) = 1.11초 (착지 속도 10.85 U/s ≈ "약 10 U/s")
///       - 감속 중  : 중력 0.5배(4.905)로 2 U/s까지 0.41초·0.41 U, 남은 5.59 U를 상한 2 U/s로
///                    2.80초 → 합 3.20초. 즉 낙하가 약 2.9배 길어진다.
///   (2) 통로 길이 L = 낙석 4줄 x 줄간격 3 U(= rockSize 1 + gapWidth 2) = 9 U, 진입·이탈 각 1 U
///       → L = 11 U. 구가 전속력으로 한 번에 통과하는 시간 = 11 / 7 = **1.57초**.
///       (구는 §0.4에서 "멈추기 힘듦"이 단점인 도형이라 2 U 틈을 대기실로 쓸 수 없다 — 틈은
///        지나갈 여유이고, 실제로는 통로 전체를 한 번에 달리는 것으로 계산해야 한다.)
///   (3) 낙석 한 발이 구의 몸 높이대(1 U)를 막는 시간 = (낙석 1 + 구 1) / 하강속도
///       → 감속 없음 2/10.85 = 0.18초, 감속 중 2/2 = 1.0초.
///   (4) 검산 — 감속 없음: 통로가 비어 있는 시간 = spawnInterval − 0.18 = 2.0 − 0.18 = **1.82초**
///       > 1.57초 → 여유 0.25초(16%)로 "어렵지만 가능". 사양 §5 Q6(슬로우 없이 아예 불가능인지)이
///       미확정이라 지금은 이쪽으로 뒀다. **불가능 쪽으로 바꾸려면 spawnInterval을 1.6초로**
///       낮추면 된다(1.6 − 0.18 = 1.42초 &lt; 1.57초 → 감속 없이는 통과 불가). initialFallSpeed를
///       올려도 같은 방향이다.
///   (5) ★ 낙석을 느리게 만드는 것만으로는 통과가 쉬워지지 않는다 — 유량 보존 ★
///       낙하 속도를 낮춰도 스폰 주기가 그대로면 낙석은 여전히 주기마다 통로에 도착한다
///       (들어오는 비율 = 나가는 비율. 느려지면 공간적으로 촘촘해질 뿐 시간당 도착 수는 같다).
///       반대로 (3)의 "막고 있는 시간"은 1/속도로 늘어난다(0.18 → 1.0초). 그래서 낙하만 늦추면
///       정상 구간의 빈 시간이 1.82 → 1.0초로 **오히려 줄어든다**(&lt; 1.57초 = 통과 불가).
///       사양 §2의 "느려진 타이밍을 이용해 통과"는 시간 자체가 느려지는(Time.timeScale) 모델을
///       전제하는데, SlowZone은 멀티 물리 동기화 때문에 전역 시간을 건드리지 않고 개별 물리만
///       늦춘다 — 두 모델의 차이가 정확히 여기서 드러난다.
///   (6) 그래서 감속 중에는 **스폰 주기도 함께 늘린다**(spawnIntervalWhileSlowed). 낙석의 출처까지
///       느려져야 "시간이 느려졌다"가 재현된다. 기본 5.0초 → 빈 시간 5.0 − 1.0 = **4.0초**로
///       1.57초 통과에 2.5배 여유. 여기에 감속 발동 직후 낙하가 1.11 → 3.20초로 늘어나 낙석 도착이
///       약 2.1초 더 밀리는 과도 구간이 겹쳐 첫 창은 더 넉넉하다. SlowZone.duration 기본 10초가
///       이 창을 덮는다.
///       ※ 주기를 상태와 무관한 값 하나로 두면 "감속 없이는 어렵다"와 "감속 중에는 통과된다"를
///          동시에 만족시킬 수 없다 — 두 상태에 각각 주기를 주는 것이 이 퍼즐의 핵심이다.
/// 위 계산은 플레이테스트 전 탁상 검산이다. 실측(구의 가감속, 조작 반응, 카메라 시야)으로 조정될 수
/// 있으니 값은 전부 Inspector에 열어 두었다.
/// </summary>
public class FallingRockSpawner : MonoBehaviour
{
    /// <summary>임계 초과를 알리는 이벤트. 인자는 맞은 플레이어의 Root GameObject —
    /// 나중에 들어올 리스폰 시스템이 "누구를 시작점으로 되돌릴지" 알 수 있어야 한다.</summary>
    [System.Serializable]
    public class PlayerHitEvent : UnityEvent<GameObject> { }

    [Header("스폰 위치 (사양 §4 spawnPositions)")]
    [Tooltip("낙석이 떨어지는 지점들. 각 지점에서 같은 주기로 동시에 떨어진다(고정 타이밍). " +
             "여러 지점을 전진 축(Z)으로 줄지어 배치해 '낙석이 떨어지는 구간'을 만든다. " +
             "빈 자식 오브젝트를 드래그해 넣으면 씬에서 눈으로 옮기며 조정할 수 있다.")]
    public Transform[] spawnPositions;

    [Header("낙석 규격 (사양 §4 rockSize / gapWidth)")]
    [Tooltip("낙석 한 변의 길이(Unit). 기본 1은 §0.1의 '1 Unit = 유니티 기본 큐브' 그대로이며, " +
             "구의 지름(1 U)과 같아 틈 계산의 기준이 된다.")]
    public float rockSize = 1f;

    [Tooltip("구가 지나갈 틈(Unit). 기본 2 = 구 지름 1 U + 좌우 0.5 U 여유 — 구는 §0.4에서 " +
             "'방향 제어 어려움'이 단점인 도형이라 정밀하게 비집는 폭(1 U)으로 두면 부당하게 어렵다. " +
             "실제 줄 간격(= rockSize + 이 값 = 3 U)이 스폰 지점 배치와 맞는지는 Start에서 검사해 " +
             "경고한다.")]
    public float gapWidth = 2f;

    [Tooltip("낙석 질량. 기본 1.5는 구의 질량(1.5)과 같게 둔 값 — 동질량 충돌이라 구가 밀리는 " +
             "정도가 과하지도 부족하지도 않고, 질량 3.0인 네모는 상대적으로 덜 밀린다(§0.4의 " +
             "'네모 = 무게·안정성'과 자연히 일치한다. 도형별 분기 코드가 필요 없다).")]
    public float rockMass = 1.5f;

    [Header("타이밍 (사양 §4 spawnInterval / fallSpeed — Q3: 고정 주기)")]
    [Tooltip("낙석 투하 주기(초). 이 값 하나가 난이도를 좌우한다 — 클래스 주석 (4)(5) 검산 참고. " +
             "기본 2.0은 '감속 없이도 어렵지만 가능'(빈 시간 1.82초 vs 통과 1.57초). " +
             "1.6으로 낮추면 '감속 없이는 불가능'이 된다(사양 §5 Q6 미확정 — 어느 쪽이든 여기서 튜닝).")]
    public float spawnInterval = 2f;

    [Tooltip("감속 구역이 켜져 있는 동안 쓰는 투하 주기(초). ★ 이 값이 사양 §2를 성립시키는 핵심 ★ — " +
             "낙하만 늦추면 유량 보존 때문에 통과가 오히려 어려워진다(클래스 주석 (5) 참고). " +
             "낙석의 출처인 스폰까지 함께 늦춰야 '시간이 느려졌다'가 재현된다. 기본 5.0은 빈 시간 " +
             "4.0초로 통과(1.57초)에 2.5배 여유. 위 spawnInterval보다 작으면 감속이 오히려 " +
             "불리해지므로 Start에서 검사해 경고한다. referenceSlowZone을 연결해야 동작한다.")]
    public float spawnIntervalWhileSlowed = 5f;

    [Tooltip("스폰 순간 한 번만 주는 초기 하강 속도(U/s). ★ 매 프레임 강제하는 속도가 아니다 ★ — " +
             "낙하는 중력에 맡겨야 모래시계 감속이 걸린다(FallingRock 클래스 주석). 기본 0은 " +
             "정지 상태에서 떨어지기 시작하는 것이고, 올리면 감속 없는 상태의 낙하가 빨라져 " +
             "spawnInterval을 줄이는 것과 같은 방향으로 난이도가 올라간다.")]
    public float initialFallSpeed = 0f;

    [Header("소멸 (사양 §4 despawnRule — Q5: 쌓이지 않고 소멸)")]
    [Tooltip("스폰 위치에서 이만큼 내려가면 낙석이 소멸한다. ★ 반드시 감속 구역보다 아래여야 한다 ★ " +
             "(구역 안에서 소멸하면 SlowZone 내부 목록에 죽은 참조가 누적된다). 기본 8 = 구역 높이 " +
             "6 U + 구역 바닥 아래 2 U. 아래 referenceSlowZone을 연결해두면 Start에서 위반을 검사한다.")]
    public float despawnFallDistance = 8f;

    [Tooltip("짝을 이루는 감속 구역. ★ 연결하지 않으면 사양 §2가 성립하지 않는다 ★ — " +
             "구역이 켜졌는지(IsActive)를 읽어 spawnIntervalWhileSlowed로 전환하는 데 쓰고, " +
             "스폰/소멸 지점이 구역과 올바르게 맞물렸는지도 Start에서 검사한다. " +
             "읽기만 한다 — 구역을 켜는 것은 세모/네모가 모래시계(FallingRockFlip)를 치는 것뿐이다.")]
    public SlowZone referenceSlowZone;

    [Header("피격 (사양 §4 onHitBehavior — Q2: 지금은 물리 넉백만)")]
    [Tooltip("같은 플레이어의 피격을 다시 세기까지의 무적 시간(초). 한 번 부딪힐 때 접촉이 " +
             "끊겼다 붙으며 Enter가 연속으로 오거나, 두 낙석이 동시에 닿아도 1회로 센다.")]
    public float hitCooldown = 0.5f;

    [Tooltip("누적 피격이 이 횟수를 넘으면 OnHitThresholdExceeded를 발화한다. 0 = 비활성(기본). " +
             "리스폰 동작은 의도적으로 구현하지 않았다 — 리스폰 시스템이 머지되면 아래 이벤트에 " +
             "배선만 하면 된다(Q2 기획 확정).")]
    public int hitsBeforeRespawn = 0;

    [Tooltip("낙석에 맞은 플레이어에게 물리 충돌과 별개로 더 줄 넉백 임펄스. 기본 0 = 사용 안 함 " +
             "(질량 있는 낙석이 부딪히면 물리가 이미 밀어낸다). 체감이 부족할 때만 올려라.")]
    public float extraKnockbackImpulse = 0f;

    [Tooltip("누적 피격이 hitsBeforeRespawn을 넘은 순간 발화(인자 = 맞은 플레이어 Root). " +
             "지금은 비어 있는 게 정상 — 나중에 리스폰 시스템을 여기에 연결한다.")]
    public PlayerHitEvent OnHitThresholdExceeded;

    [Header("선택")]
    [Tooltip("비워두면 기본 큐브(rockSize 크기 + Rigidbody + FallingRock)를 즉석에서 만든다. " +
             "프리팹을 넣으면 스케일은 프리팹 값을 존중하고 질량/소멸 규칙만 이 스포너 값으로 덮는다.")]
    public GameObject rockPrefab;

    [Tooltip("낙석의 수평 위치와 회전을 고정한다(기본 켬). 고정 타이밍 퍼즐이므로 낙석이 플레이어에 " +
             "밀리거나 튕겨 옆줄로 흘러가면 배치 자체가 무의미해진다. 세로 축은 자유롭게 두므로 " +
             "모래시계 감속(중력 배율·하강 상한)은 그대로 걸린다. 끄면 밀 수 있는 낙석이 된다.")]
    public bool keepLane = true;

    private float nextVolleyAt;
    private readonly List<FallingRock> live = new List<FallingRock>();
    private readonly Dictionary<Rigidbody, HitRecord> hits = new Dictionary<Rigidbody, HitRecord>();

    private class HitRecord
    {
        public int count;
        public float nextAllowedTime;
    }

    private void Start()
    {
        string problem = Validate();
        if (problem != null) Debug.LogWarning($"[FallingRockSpawner] {problem}", this);
    }

    private void OnDisable()
    {
        // 이미 떨어지고 있는 낙석은 파괴하지 않는다 - 스스로 소멸 규칙(FallingRock)을 갖고 있어
        // 낙하를 끝까지 마치고 사라진다. 여기서 Destroy하면 아직 감속 구역 안에 있는 낙석이
        // 구역 내부에서 파괴돼 SlowZone에 죽은 참조를 남긴다(소멸 지점 하드 제약과 같은 이유).
        live.Clear();
    }

    private void Update()
    {
        if (spawnPositions == null || spawnPositions.Length == 0) return;
        if (Time.time < nextVolleyAt) return;

        // 감속 중에는 스폰 주기도 늘린다. 낙하만 늦추면 유량 보존(시간당 도착 수 불변) 때문에
        // 통과가 오히려 어려워진다 - 클래스 주석 (5)(6). 구역이 없으면 평소 주기로만 돈다.
        bool slowed = referenceSlowZone != null && referenceSlowZone.IsActive;
        float interval = slowed ? spawnIntervalWhileSlowed : spawnInterval;
        nextVolleyAt = Time.time + Mathf.Max(0.05f, interval);

        live.RemoveAll(r => r == null);
        foreach (Transform point in spawnPositions)
            if (point != null) Spawn(point);
    }

    private void Spawn(Transform point)
    {
        GameObject go;
        if (rockPrefab != null)
        {
            go = Instantiate(rockPrefab, point.position, point.rotation);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetPositionAndRotation(point.position, point.rotation);
            go.transform.localScale = Vector3.one * rockSize;
        }
        go.name = $"FallingRock_{point.name}";
        // 태그는 건드리지 않는다(Untagged 유지). Player 태그를 붙이면 SlowZone이 includePlayer=false
        // 기본값에서 낙석을 감속 대상에서 제외해 퍼즐이 성립하지 않는다.

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = rockMass;
        rb.useGravity = true;
        rb.isKinematic = false;
        // drag는 건드리지 않는다(기본 0). 감속의 하강 상한은 SlowZone.maxFallSpeed가 직접 자르므로
        // 대상의 authored drag에 의존하지 않는다(SlowZone 클래스 주석의 결론).
        if (keepLane)
            rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ
                             | RigidbodyConstraints.FreezeRotation;
        rb.velocity = Vector3.down * initialFallSpeed; // 초기 속도 1회 대입. 이후 속도는 물리에 맡긴다.

        FallingRock rock = go.GetComponent<FallingRock>();
        if (rock == null) rock = go.AddComponent<FallingRock>();
        rock.despawnFallDistance = despawnFallDistance;
        rock.extraKnockbackImpulse = extraKnockbackImpulse;
        rock.owner = this;

        // 낙석끼리는 부딪히지 않게 한다. 같은 줄에서 위 낙석이 아래 낙석을 따라잡으면(감속 구역
        // 경계를 걸쳐 배치했거나 아래 낙석이 무언가에 걸린 경우) 낙하 타이밍이 어긋나 고정 타이밍
        // 퍼즐이 조용히 깨진다. 레이어를 새로 만들면 프로젝트 설정(다른 폴더)을 건드려야 하므로
        // 콜라이더 쌍 단위로 무시시킨다 - 동시 생존 수가 한 자리라 비용도 무의미하다.
        Collider newCol = go.GetComponent<Collider>();
        if (newCol != null)
            foreach (FallingRock other in live)
            {
                Collider otherCol = other != null ? other.GetComponent<Collider>() : null;
                if (otherCol != null) Physics.IgnoreCollision(newCol, otherCol);
            }

        live.Add(rock);
    }

    /// <summary>FallingRock이 플레이어 피격을 보고한다. 중복/무적 시간에 걸리면 false.
    /// 집계를 낙석이 아니라 스포너가 들고 있는 이유는 낙석이 곧 소멸하기 때문이다.</summary>
    public bool RegisterHit(Rigidbody playerRb, PlayerShapeIdentity shape)
    {
        if (playerRb == null) return false;

        if (!hits.TryGetValue(playerRb, out HitRecord record))
        {
            record = new HitRecord();
            hits[playerRb] = record;
        }

        if (Time.time < record.nextAllowedTime) return false;
        record.nextAllowedTime = Time.time + hitCooldown;
        record.count++;

        Debug.Log($"[FallingRockSpawner] '{playerRb.name}'({shape.Kind}) 낙석 피격 {record.count}회. " +
                  "밀려남은 물리 충돌로 처리된다.");

        if (hitsBeforeRespawn > 0 && record.count > hitsBeforeRespawn)
        {
            // 발화 후 카운트를 0으로 되돌린다 - 나중에 리스폰이 연결되면 "리스폰 이후 다시 N번"이
            // 자연히 성립한다(되돌리지 않으면 첫 임계 이후 영원히 매 피격마다 발화한다).
            record.count = 0;
            // 여기서 리스폰을 구현하지 않는다(Q2 기획 확정). 이벤트만 쏘고 끝 - 리스폰 시스템이
            // 머지되면 인스펙터에서 이 이벤트에 배선한다.
            OnHitThresholdExceeded?.Invoke(playerRb.gameObject);
            Debug.Log($"[FallingRockSpawner] '{playerRb.name}' 누적 피격이 임계 {hitsBeforeRespawn}회를 " +
                      "넘어 OnHitThresholdExceeded 발화(리스폰 동작은 미구현 - 의도됨).");
        }

        return true;
    }

    /// <summary>배치가 사양대로 맞물렸는지 검사한다. 문제가 없으면 null.
    /// Start와 Tools &gt; FallingRock &gt; Validate Scene Setup이 같은 검사를 공유한다.</summary>
    public string Validate()
    {
        if (spawnPositions == null || spawnPositions.Length == 0)
            return "spawnPositions가 비어 있어 낙석이 생성되지 않는다.";

        float requiredSpacing = rockSize + gapWidth;
        for (int i = 1; i < spawnPositions.Length; i++)
        {
            if (spawnPositions[i] == null || spawnPositions[i - 1] == null)
                return "spawnPositions에 빈 항목이 있다.";

            Vector3 a = spawnPositions[i - 1].position;
            Vector3 b = spawnPositions[i].position;
            float spacing = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            if (spacing + 0.01f < requiredSpacing)
                return $"스폰 지점 {i - 1}-{i} 간격이 {spacing:F2} U로, 낙석({rockSize}) + 틈({gapWidth}) = " +
                       $"{requiredSpacing} U보다 좁다. 구(지름 1 U)가 지나갈 틈이 남지 않는다.";
        }

        if (spawnIntervalWhileSlowed < spawnInterval)
            return $"spawnIntervalWhileSlowed({spawnIntervalWhileSlowed})가 spawnInterval({spawnInterval})보다 " +
                   "작다. 감속 중 낙석이 더 자주 떨어지게 되고, 낙하가 느려 막고 있는 시간까지 길어져 " +
                   "슬로우가 통과를 돕는 대신 방해한다(사양 §2 반대). 감속 쪽을 더 크게 두어라.";

        if (referenceSlowZone == null)
            return "referenceSlowZone이 비어 있다. 낙석은 감속 구역과 짝을 이뤄야 의미가 있는 종속 " +
                   "기믹인데(사양 §0), 연결이 없으면 감속 중 스폰 주기 전환이 동작하지 않아 " +
                   "슬로우가 통과를 돕지 못한다(클래스 주석 (5)). 배치·소멸 지점 검사도 건너뛴다.";

        Collider zone = referenceSlowZone.GetComponent<Collider>();
        if (zone == null) return "referenceSlowZone에 Collider가 없다.";

        foreach (Transform point in spawnPositions)
        {
            if (point == null) continue;

            if (!zone.bounds.Contains(point.position))
                return $"스폰 지점 '{point.name}'이 감속 구역 밖이다. 구역 안에서 떨어져야 모래시계 " +
                       "슬로우가 낙하에 걸린다(사양 §3).";

            float despawnY = point.position.y - despawnFallDistance + rockSize * 0.5f;
            if (despawnY > zone.bounds.min.y)
                return $"소멸 지점이 감속 구역 안이다(소멸 y {despawnY:F2} > 구역 바닥 " +
                       $"{zone.bounds.min.y:F2}). 구역 안에서 낙석이 파괴되면 SlowZone 내부 목록에 " +
                       $"죽은 참조가 스폰마다 누적된다. despawnFallDistance를 " +
                       $"{point.position.y - zone.bounds.min.y + rockSize:F1} 이상으로 올려라.";
        }

        return null;
    }

    // 스폰 지점, 낙하 경로, 소멸 지점, 그리고 구가 지나갈 틈을 씬 뷰에 그린다(배치·확인용).
    private void OnDrawGizmos()
    {
        if (spawnPositions == null) return;

        for (int i = 0; i < spawnPositions.Length; i++)
        {
            Transform point = spawnPositions[i];
            if (point == null) continue;

            Vector3 top = point.position;
            Vector3 bottom = top + Vector3.down * despawnFallDistance;

            Gizmos.color = new Color(0.85f, 0.55f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(top, Vector3.one * rockSize);
            Gizmos.DrawLine(top, bottom);

            Gizmos.color = new Color(0.4f, 0.4f, 0.4f, 0.9f); // 소멸 지점(감속 구역보다 아래여야 함).
            Gizmos.DrawWireCube(bottom, new Vector3(rockSize, 0.05f, rockSize));

            if (i == 0) continue;
            Transform prev = spawnPositions[i - 1];
            if (prev == null) continue;

            Gizmos.color = Color.green; // 구가 지나갈 틈.
            Gizmos.DrawLine(prev.position + Vector3.down * 0.5f, point.position + Vector3.down * 0.5f);
        }
    }
}
