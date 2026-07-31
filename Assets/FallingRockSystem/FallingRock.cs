using UnityEngine;

/// <summary>
/// 낙석 하나(무너져 내리는 꿈의 파편). FallingRockSpawner가 생성하고 이 컴포넌트를 붙인다.
/// 하는 일은 세 가지뿐이다 — (1) 스폰 위치에서 일정 거리 내려가면 스스로 소멸, (2) 플레이어에
/// 부딪히면 스포너에 피격을 보고, (3) <b>무엇에든</b> 부딪히면(바닥·벽·플레이어) 파편으로 부서지며
/// 소멸. 낙하 자체는 코드가 관여하지 않는다.
///
/// ★ 낙하는 "평범한 dynamic Rigidbody + 중력"이다. 절대 velocity를 매 프레임 대입하지 마라. ★
/// 이 기믹의 존재 이유는 몽환의 모래시계(HourglassSystem/SlowZone)가 낙석을 느리게 만들어
/// 통과 창을 여는 타이밍 퍼즐이다(사양 §2·§3). SlowZone은 대상의 중력 배율
/// (PlayerGravityOverride)과 하강 속도 상한(maxFallSpeed)으로 감속시키므로, 낙석이 FixedUpdate에서
/// rb.velocity를 자기 낙하 속도로 덮어쓰거나 isKinematic으로 움직이면 감속이 전부 무효화되고
/// 퍼즐 자체가 성립하지 않는다. 나중에 "낙하 속도가 일정하지 않다"는 이유로 velocity 대입으로
/// 바꾸면 기믹이 조용히 죽는다 — 스폰 순간의 초기 속도(FallingRockSpawner.initialFallSpeed)를
/// 1회 대입하는 것이 코드가 속도에 손대는 유일한 지점이다.
///
/// [낙하 기준은 절대 월드 Y가 아니라 스폰 위치 상대 거리]
/// HourglassSystem/RespawningFallingDebris가 겪은 사고를 그대로 따른다: 절대 높이로 두면 구역이나
/// 스폰 지점을 옮긴 순간 그 값만 제자리에 남아 조용히 어긋난다(실제로 파편이 감속 구역 상단에서
/// 사라져 감속 구간을 통과하지 않았다). 스폰 위치는 Awake에 스스로 읽으므로 어디로 옮겨도 따라온다.
///
/// [소멸 지점은 반드시 SlowZone 볼륨보다 아래여야 한다 — 하드 제약]
/// 감속 구역 안에서 Destroy되면 SlowZone.affected(HashSet)에 죽은 참조가 남는다. SlowZone의
/// OnTriggerExit은 파괴되며 들어온 collider를 null 체크로 걸러내고 바로 반환하므로 정리되지 않고,
/// 스폰마다 항목이 하나씩 누적된다. SlowZone은 수정 금지 대상이라 이쪽에서 회피해야 한다 —
/// 구역을 완전히 통과한 뒤 소멸하면 OnTriggerExit이 정상적으로 정리해준다.
/// 위반 검사는 스포너(FallingRockSpawner.Validate)가 담당한다.
///
/// [바닥 충돌 = 부서짐. 쌓이지 않는다는 사양 §5 Q5를 물리로 성립시킨다]
/// 예전에는 바닥에 닿아도 아무 일이 없어서, 낙석이 통로 바닥에 그대로 서 있다가 stuckTimeout
/// (6초)의 안전장치로 경고와 함께 사라졌다 — 그 6초 동안은 지형이 바뀌어 고정 타이밍 퍼즐이
/// 깨진다. 이제 솔리드에 닿는 순간 파편으로 부서지며 소멸하므로 쌓이는 구간이 아예 없다.
/// stuckTimeout은 "바닥이 아닌 무언가에 끼인" 경우를 위한 안전장치로 그대로 남는다.
///
/// 파편은 순수 연출이다 — 원본의 머티리얼을 공유하는 작은 큐브라 새 에셋·파티클 시스템·셰이더가
/// 하나도 들어가지 않는다(RainbowBridge의 알파 페이드, DreamThread의 LineRenderer 뜯김과 같은
/// 취지). 질량을 원본의 1/파편수로 나눠 주므로 플레이어를 밀어내지 않고, PlayerShapeIdentity가
/// 없어서 모래시계(FallingRockFlip)의 도형 게이트에도 걸리지 않는다(파편이 감속 구역을 자기
/// 발동시키는 우회가 생기지 않는다).
/// 파편이 감속 구역 안에서 소멸하면 위 [하드 제약]의 죽은 참조 문제가 파편 수만큼 배로 생기는데,
/// 바닥 충돌 지점은 설계상 구역 볼륨보다 아래이므로(despawnFallDistance 주석 참고) 파편은 구역에
/// 들어가지 않는다. 구역 안에 바닥을 두면 이 전제가 깨진다 — 그 배치는 원래도 금지다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class FallingRock : MonoBehaviour
{
    [Header("소멸 규칙 (사양 §4 despawnRule)")]
    [Tooltip("스폰 위치에서 이만큼 아래로 내려가면 소멸한다(절대 높이가 아니라 상대 거리 — " +
             "스폰 지점을 옮겨도 따라온다). ★ 이 지점은 반드시 감속 구역(SlowZone) 볼륨보다 " +
             "아래여야 한다 — 구역 안에서 소멸하면 SlowZone 내부 목록에 죽은 참조가 스폰마다 " +
             "누적된다. 기본 8은 '6칸 구역 상단에서 스폰 → 구역 하단(=통과 통로)까지 6칸 낙하 → " +
             "구역 바닥보다 2칸 더 아래에서 소멸' 배치를 전제한 값이다.")]
    public float despawnFallDistance = 8f;

    [Tooltip("낙하 경로가 막혀 낙석이 멈춘 채 남는 경우의 안전장치(초). 0이면 끈다. " +
             "쌓인 낙석은 지형을 바꿔 고정 타이밍 퍼즐을 플레이 도중에 깨뜨리고 물리 부하도 " +
             "계속 늘어난다(사양 §5 Q5는 '쌓이지 않고 소멸'로 결정). 이 경로로 소멸하면 아직 " +
             "감속 구역 안일 수 있으므로 경고를 남긴다 — 경고가 보이면 낙하 경로(구역 아래)를 비워라.")]
    public float stuckTimeout = 6f;

    [Header("피격")]
    [Tooltip("추가 넉백 임펄스. 기본 0 = 사용하지 않는다. 낙석은 질량 있는 dynamic Rigidbody라 " +
             "부딪히면 물리가 이미 플레이어를 밀어낸다 — 물리가 해주는 일을 코드로 다시 하지 않는다. " +
             "플레이테스트에서 체감이 부족할 때만 올려라. (넉백을 상시 쓰게 되면 이 코드는 " +
             "PlayerSystem 쪽 리시버(가칭 PlayerKnockbackReceiver)로 옮겨야 한다 — 저장소 컨벤션인 " +
             "발신자-수신자 분리 패턴. 지금은 그 리시버가 없고 교차 폴더 수정 금지라 여기서 직접 준다.)")]
    public float extraKnockbackImpulse = 0f;

    [Header("부서짐 연출 (바닥·벽 충돌)")]
    [Tooltip("바닥에 닿을 때 튀어나올 파편 개수. 0이면 부서짐 연출을 끄고 그냥 소멸한다. " +
             "파편은 원본 머티리얼을 공유하는 작은 큐브라 새 에셋이 들지 않는다.")]
    public int shardCount = 6;

    [Tooltip("파편 크기 = 원본 크기 x 이 비율. 기본 0.35는 1 U 낙석에서 0.35 U 파편이 나온다 — " +
             "구 지름(1 U)보다 확실히 작아 '부서진 조각'으로 읽힌다.")]
    public float shardSizeRatio = 0.35f;

    [Tooltip("파편이 흩어지는 속도(U/s). 질량과 무관하게 속도로 직접 준다 — 이 저장소의 " +
             "'발사는 질량 무관 결정론'(PlayerJump.LaunchToHeight) 관례와 같다. 실제 적용값은 " +
             "충돌 속도에 비례해 줄어든다: 감속 구역 안에서 2 U/s로 살살 닿으면 조금만 튀고, " +
             "구역 밖 자유낙하 10 U/s로 내리치면 최대로 튄다 — 느려진 낙석이 세게 터지면 " +
             "감속이 걸렸다는 시각 신호와 어긋난다.")]
    public float shardScatterSpeed = 3f;

    [Tooltip("위 속도가 최대가 되는 충돌 속도(U/s). 기본 10은 감속 구역 밖 자유낙하 도달 속도다. " +
             "감속 수치를 바꿨으면 이 값도 같이 본다.")]
    public float shardFullImpactSpeed = 10f;

    [Tooltip("파편 수명(초). 이 시간에 걸쳐 크기가 0으로 줄며 사라진다. 길게 두면 파편이 통로에 " +
             "남아 다음 낙석 타이밍을 가린다.")]
    public float shardLifetime = 1.2f;

    /// <summary>피격 집계 주체. 스포너가 생성 시 넣어준다(누적 횟수는 낙석보다 오래 살아야 하므로
    /// 스포너가 들고 있다). 손으로 배치한 낙석은 비어 있어 피격을 집계하지 않는다.</summary>
    [HideInInspector] public FallingRockSpawner owner;

    private Rigidbody rb;
    private Vector3 startPosition;
    private float movingSince;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        movingSince = Time.time;
    }

    private void FixedUpdate()
    {
        if (startPosition.y - rb.position.y >= despawnFallDistance)
        {
            Destroy(gameObject);
            return;
        }

        if (stuckTimeout <= 0f) return;

        if (rb.velocity.sqrMagnitude > 0.0025f) // 0.05 U/s 이상이면 아직 움직이는 중.
        {
            movingSince = Time.time;
            return;
        }

        if (Time.time - movingSince >= stuckTimeout)
        {
            Debug.LogWarning($"[FallingRock] '{name}'이 낙하 도중 {stuckTimeout}초간 멈춰 있어 " +
                             "제자리에서 소멸시킨다. 낙석이 쌓이면 고정 타이밍 퍼즐이 깨지므로 " +
                             "방치하지 않는다. 다만 이 지점이 감속 구역 안이면 SlowZone에 죽은 참조가 " +
                             "남는다 — 스폰 지점 아래 낙하 경로(구역 바닥 아래까지)를 비워라.", this);
            Destroy(gameObject);
        }
    }

    // 솔리드 충돌만 본다. 플레이어의 트리거 자식(Player_Mesh)은 물리 충돌 이벤트를 만들지 않으므로
    // OnTriggerEnter를 따로 구현하면 같은 피격이 두 번 잡힌다 - 아예 구현하지 않는다. 그래도
    // 스포너 쪽에서 attachedRigidbody 기준 중복 제거 + 무적 시간을 한 번 더 거른다(솔리드 콜라이더가
    // 여러 개인 플레이어가 나중에 생기거나, 접촉이 끊겼다 붙어 Enter가 연속으로 오는 경우 대비).
    private void OnCollisionEnter(Collision collision)
    {
        // 다른 낙석이 남긴 파편에 닿은 것은 착지가 아니다. 파편은 콜라이더가 있어야 바닥에서 튀는데,
        // 그대로 두면 뒤따라 내려오는 낙석이 **공중에서 파편을 치고 부서진다**(낙석끼리는 스포너가
        // Physics.IgnoreCollision으로 막지만 파편은 그 목록에 없다). 파편 판별로 걸러낸다.
        if (collision.collider.GetComponent<FallingRockShard>() != null) return;

        // 도형 판별 컨벤션(Assets/CLAUDE.md): CompareTag/이름 하드코딩 대신 부모의 PlayerShapeIdentity.
        PlayerShapeIdentity shape = collision.collider.GetComponentInParent<PlayerShapeIdentity>();

        // 플레이어가 아닌 솔리드(바닥·벽·프롭) = 착지. 부서지며 소멸한다. owner 유무와 무관하게
        // 동작해야 한다 - 손으로 배치한 낙석도 바닥에 그대로 서 있으면 안 된다.
        if (shape == null)
        {
            Shatter(collision.relativeVelocity.magnitude);
            return;
        }

        Rigidbody playerRb = collision.collider.attachedRigidbody;
        if (owner != null && playerRb != null && owner.RegisterHit(playerRb, shape) &&
            extraKnockbackImpulse > 0f)
        {
            Vector3 away = playerRb.worldCenterOfMass - transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;
            playerRb.AddForce(away.normalized * extraKnockbackImpulse, ForceMode.Impulse);
        }

        // 플레이어 피격도 부서짐으로 끝낸다(2026-07-31). 그 전에는 맞고도 멀쩡히 지나가, (1) "맞았다"는
        // 신호가 물리적 밀림 하나뿐이라 약하고 (2) 낙석이 플레이어 위에 얹힌 채 통로에 남아 아래
        // "쌓이지 않는다"가 플레이어 몸 위에서만 깨졌다. 밀려나는 느낌은 그대로 남는다 —
        // OnCollisionEnter은 PhysX가 그 스텝의 충돌 impulse를 이미 적용한 뒤에 오므로 밀린 다음 부서진다.
        //
        // 집계 성공 여부(RegisterHit)와 무관하게 부서진다: 무적 시간처럼 눈에 안 보이는 상태에 따라
        // 부서지고 안 부서지고가 갈리면 플레이어에게는 그냥 버그로 보인다. 어차피 부서지며 소멸하므로
        // 같은 낙석이 두 번 때리는 경로도 사라진다(스포너 쪽 중복 제거는 콜라이더가 여러 개인
        // 도형용으로 남는다).
        //
        // 파편이 감속 구역 안에서 소멸하면 위 [하드 제약]의 죽은 참조가 생긴다 — 통과 통로는 설계상
        // 구역 밑이라 괜찮지만, 구역 <b>안</b>에서 맞으면 파편 수만큼 남는다. 실제로 문제가 되면
        // shardCount를 0으로 두는 것이 아니라 통로 배치를 본다.
        Shatter(collision.relativeVelocity.magnitude);
    }

    /// <summary>파편을 흩뿌리고 자신은 소멸한다. impactSpeed(충돌 상대속도)가 클수록 세게 튄다.</summary>
    private void Shatter(float impactSpeed)
    {
        if (shardCount > 0 && shardSizeRatio > 0f)
        {
            Renderer source = GetComponent<Renderer>();
            Material shared = source != null ? source.sharedMaterial : null;

            Vector3 shardScale = transform.localScale * shardSizeRatio;
            // 원본 질량을 파편들이 나눠 갖는다 - 총 질량이 보존되고, 조각 하나는 플레이어(1.5~3.0)에
            // 비해 가벼워 밀어내지 못한다.
            float shardMass = Mathf.Max(0.01f, rb.mass / shardCount);
            float speed = shardScatterSpeed *
                          (shardFullImpactSpeed > 0f ? Mathf.Clamp01(impactSpeed / shardFullImpactSpeed) : 1f);
            // 흩뿌리는 반경은 원본 반쪽 - 조각들이 원본이 있던 자리에서 갈라져 나온 것처럼 보인다.
            float spread = Mathf.Max(transform.localScale.x, transform.localScale.z) * 0.5f;

            for (int i = 0; i < shardCount; i++)
            {
                GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = $"{name}_Shard{i}";
                shard.transform.SetPositionAndRotation(
                    transform.position + Random.insideUnitSphere * spread, Random.rotation);
                shard.transform.localScale = shardScale;

                if (shared != null) shard.GetComponent<Renderer>().sharedMaterial = shared;

                Rigidbody shardBody = shard.AddComponent<Rigidbody>();
                shardBody.mass = shardMass;
                // 위로 치우친 랜덤 방향 - 바닥에서 튀어 오르며 갈라지는 그림이 된다.
                Vector3 dir = (Random.insideUnitSphere + Vector3.up).normalized;
                // VelocityChange/Acceleration이라 질량 무관 - shardMass를 바꿔도 튀는 모양이 안 변한다.
                shardBody.AddForce(dir * speed, ForceMode.VelocityChange);
                shardBody.AddTorque(Random.insideUnitSphere * speed, ForceMode.VelocityChange);

                shard.AddComponent<FallingRockShard>().lifetime = shardLifetime;
            }
        }

        Destroy(gameObject);
    }
}
