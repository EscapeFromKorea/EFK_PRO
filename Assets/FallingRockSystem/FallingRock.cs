using UnityEngine;

/// <summary>
/// 낙석 하나(무너져 내리는 꿈의 파편). FallingRockSpawner가 생성하고 이 컴포넌트를 붙인다.
/// 하는 일은 두 가지뿐이다 — (1) 스폰 위치에서 일정 거리 내려가면 스스로 소멸, (2) 플레이어에
/// 부딪히면 스포너에 피격을 보고. 낙하 자체는 코드가 관여하지 않는다.
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
        if (owner == null) return;

        // 도형 판별 컨벤션(Assets/CLAUDE.md): CompareTag/이름 하드코딩 대신 부모의 PlayerShapeIdentity.
        // 플레이어가 아닌 물체(바닥·다른 낙석)는 여기서 걸러진다.
        PlayerShapeIdentity shape = collision.collider.GetComponentInParent<PlayerShapeIdentity>();
        if (shape == null) return;

        Rigidbody playerRb = collision.collider.attachedRigidbody;
        if (playerRb == null) return;

        if (!owner.RegisterHit(playerRb, shape)) return;

        if (extraKnockbackImpulse > 0f)
        {
            Vector3 away = playerRb.worldCenterOfMass - transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;
            playerRb.AddForce(away.normalized * extraKnockbackImpulse, ForceMode.Impulse);
        }
    }
}
