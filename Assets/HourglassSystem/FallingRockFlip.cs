using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모래시계(낙석 대역 오브젝트)에 부착. 도형 게이트를 통과한 상대가 충분한 운동량
/// (때린 쪽의 질량 x 상대속도)으로 부딪히면 뒤집혀 연결된 SlowZone을 활성화한다.
/// 운동량은 "때린 쪽"(collision.rigidbody)의 질량으로 잰다 - 이 오브젝트 자신의 질량이 아니다.
///
/// [도형 3역 분담 (2026-07-30 사용자 확정) — 세모=발동 / 네모=이동 / 구=통과]
/// 모래시계에 대해 세 도형이 하는 일이 서로 겹치지 않게 나뉘어 있다.
/// - 세모(Tetrahedron): 쳐서 <b>발동</b>시킨다. 발동시킨 세모에게 모래시계는 밀려나지 않는다.
/// - 네모(Cube): <b>발동시키지 못한다.</b> 대신 접촉해서 <b>밀어 옮길</b> 수 있다 - 모래시계를
///   낙석 커튼 밖으로 치우거나 원하는 자리로 옮기는 배치 역할이다.
/// - 구(Sphere): 발동도 밀기도 못한다. 슬로우가 열어준 창으로 <b>통과</b>하는 역할만 한다.
/// 이 분담 때문에 도형 게이트는 <b>세모 단독 허용</b>이다. 사양 2장은 "세모나 사각형이 쳐서 발동"
/// 이라 적고 있어 네모까지 허용이었지만, 네모에게 "밀기"라는 고유 역할을 준 이상 발동까지 겸하면
/// 네모 혼자 발동+배치를 다 하게 되어 협동 분담이 무너진다. 그래서 사용자가 명시적으로 사양을
/// 바꿔 네모의 발동 권한을 회수했다 - 임의 축소가 아니다.
///
/// [게이트를 운동량이 아니라 도형으로 나누는 이유 — 임계 하나로는 못 나눈다]
/// 실제 에셋 스탯으로 계산하면 구 1.5x7=10.5, 세모 1.0x5=5.0, 네모 3.0x3.5=10.5 - 구와 네모가
/// 완전히 같고 세모가 가장 약하다. 임계를 올려 구를 막으려 하면 사양과 반대로 세모부터 잘린다.
/// 무게 게이트(PlayerWeight)로도 표현할 수 없다 - Assets/CLAUDE.md의 "무게로 게이트" 하드 룰은
/// "무거워서 못 한다"류 판정에만 성립하는데, 여기는 가장 가벼운 세모(1.0)만 통과하고 중간 무게인
/// 구(1.5)와 가장 무거운 네모(3.0)가 막혀야 해서 단조 임계로는 아예 나눌 수 없다. 그래서 이 판정만
/// 예외적으로 도형 판별 컨벤션(GetComponentInParent&lt;PlayerShapeIdentity&gt;().Kind)을 쓴다 -
/// CompareTag/이름 하드코딩 대신이며, 게이트를 끄면 예전처럼 운동량만으로 판정한다.
///
/// [밀림 방지는 게이트가 아니라 물리 결과다 — isKinematic 금지]
/// "세모에게는 안 밀리고 네모에게는 밀린다"를 충돌 판정에서 걸러낼 수는 없다. 밀림은 PhysX가
/// 접촉에서 만들어내는 결과라 코드가 개입할 지점이 없기 때문이다. 그래서 Rigidbody constraints로
/// <b>위치를 잠가 두고, 네모가 접촉 중인 동안만</b> 잠금을 푼다(FixedUpdate에서 접촉 집합을 보고
/// 판단). isKinematic으로 바꾸면 잠기기는 하지만 네모의 밀기까지 같이 불가능해지므로 금지다.
/// 접촉이 끊기면 velocity/angularVelocity를 0으로 되돌리고 다시 잠근다 - "밀린 채 그 자리에 유지"
/// (레일/축 제한 없는 자유 이동, 손 떼면 정지)가 이렇게 성립한다.
/// 회전은 잠금 여부와 무관하게 항상 고정한다 - 네모가 모서리를 밀어 굴려버리면 안 되기 때문이다.
///
/// [뒤집기는 시각 전용이다 (2026-07-30) — 콜라이더를 돌리면 PhysX가 플레이어를 튕겨낸다]
/// 예전에는 FlipRoutine이 <b>Root의 transform.rotation</b>을 돌렸다. Root에는 BoxCollider가 붙어
/// 있으므로 뒤집히는 0.3초 동안 콜라이더가 세모를 훑고 지나가며, PhysX가 그 관통을 밀어내기로
/// 해소해 발동시킨 플레이어를 세게 날려버렸다("상호작용하면 튕긴다" 플레이테스트 보고).
/// 그래서 <b>콜라이더가 붙지 않은 시각 자식(Hourglass_RockVisual)의 localRotation만</b> 돌린다 -
/// 플레이어 다면체가 FreezeRotation으로 물리 회전을 고정한 채 PlayerVisualRoll이 Player_MeshVisual만
/// 기울여 구르는 느낌을 내는 것과 <b>같은 구조의 재사용</b>이다(Assets/CLAUDE.md 플레이어 계층 참고).
/// 시각 자식의 localRotation은 Rigidbody constraints와 완전히 독립이라, 밀림 정책이 잠금(FreezeAll)
/// 이든 해제(FreezeRotation)든 뒤집기 연출은 항상 그대로 나온다.
/// Animator를 쓸 때도 같다 - <b>Animator는 반드시 시각 자식에</b> 붙여야 한다. Root에 붙이면 클립이
/// Root를 회전시켜 콜라이더가 다시 같이 돌아가고 위 튕김이 부활한다(Awake에서 감지해 경고한다).
///
/// [폐기된 대안: "구는 반발로 튕겨나가 조준이 어렵게"]
/// 역효과다. 반발이 커지면 collision.relativeVelocity가 커져서 운동량 판정을 오히려 더 쉽게
/// 통과한다 - 구를 막는 방향이 아니라 돕는 방향이다. 도형 분담은 물리 머티리얼이 아니라 위
/// 게이트로 만든다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FallingRockFlip : MonoBehaviour
{
    [Header("발동 조건")]
    [Tooltip("발동에 필요한 충돌 운동량(때린 쪽 질량 x 상대속도). 도형 구분은 아래 도형 게이트가 " +
             "담당하므로 이 값은 '살짝 스친 것'만 걸러낸다. 발동 주체인 세모(전속력 1.0x5=5.0) " +
             "기준으로 여유를 둔 3.0 - 전속력의 60%로 부딪혀도 발동한다.")]
    public float requiredMomentum = 3f;
    [Tooltip("켜면 세모(Tetrahedron)만 모래시계를 발동시킨다 - 구는 통과 역할, 네모는 밀어 옮기는 " +
             "역할이라 둘 다 발동에서 막힌다(도형 3역 분담). PlayerShapeIdentity가 없는 물체" +
             "(낙석·상자 등)도 발동시키지 못한다. 끄면 도형·플레이어 여부와 무관하게 운동량만으로 " +
             "판정한다 - 사양 5장 6번(슬로우 없이도 통과 가능한 난이도인가)을 플레이테스트로 " +
             "확인할 때 끈다. 밀림 정책은 이 스위치와 별개다(아래 항목).")]
    public bool useShapeGate = true;

    [Header("밀림 정책 (도형 3역 분담)")]
    [Tooltip("켜면(기본) 이 모래시계는 네모(Cube)가 접촉해 미는 동안만 움직이고, 접촉이 끝나면 " +
             "그 자리에 멈춰 고정된다 - 발동시킨 세모나 지나가는 구, 부딪힌 낙석에는 밀려나지 " +
             "않는다. 미는 동안은 레일/축 제한이 없어 자유롭게 옮길 수 있고(회전만 고정 - 굴러가면 " +
             "뒤집기 연출의 기준 자세가 어긋난다. 뒤집기는 시각 자식만 돌리므로 잠금/해제 어느 " +
             "상태에서도 그대로 동작한다), 손을 떼면 그 자리에서 멈춘다. 끄면 이 컴포넌트가 " +
             "constraints에 전혀 손대지 않아 Rigidbody에 설정한 값 그대로 동작한다" +
             "(기본 None이면 누구에게나 밀린다).")]
    public bool lockUnlessPushedByCube = true;

    [Header("뒤집기 연출 / 연결")]
    [Tooltip("뒤집어 보일 시각 자식(Hourglass_RockVisual). MeshFilter/MeshRenderer만 있고 콜라이더는 " +
             "없어야 한다 - 콜라이더가 붙은 Root를 돌리면 회전하는 콜라이더가 플레이어를 훑어 PhysX가 " +
             "세게 튕겨낸다. Tools > Hourglass가 자동 배선한다. 비워두면 경고와 함께 Root를 돌리는 " +
             "예전 동작으로 폴백한다(= 튕김 재발).")]
    public Transform visualRoot;
    public float cooldown = 2f;
    public float flipDuration = 0.3f;
    public SlowZone targetSlowZone;
    [Tooltip("있으면 \"Flip\" 트리거로 애니메이션 재생. 없으면 코드로 180도 회전. " +
             "Animator는 반드시 위 시각 자식에 붙여라 - Root에 붙이면 클립이 콜라이더까지 회전시켜 " +
             "플레이어가 튕겨나간다.")]
    public Animator animator;

    private float nextAllowedTime;
    private Rigidbody body;
    private readonly HashSet<Collider> cubePushers = new HashSet<Collider>();
    private bool positionLocked;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (targetSlowZone == null)
            Debug.LogError("[FallingRockFlip] targetSlowZone이 비어있다. Inspector에서 연결해라.", this);

        // 침묵 실패를 만들지 않는다 - 폴백은 동작하지만 그 폴백이 곧 "플레이어가 튕기는" 옛 버그다.
        if (visualRoot == null)
            Debug.LogWarning("[FallingRockFlip] visualRoot가 비어 있다 - 뒤집기가 콜라이더까지 회전시켜 " +
                             "플레이어를 튕겨낸다(예전 동작으로 폴백 중). 이 인스턴스는 시각 자식이 없는 " +
                             "구버전 계층이다. Tools > Hourglass로 재생성해라.", this);

        if (animator != null && animator.gameObject == gameObject)
            Debug.LogWarning("[FallingRockFlip] Animator가 Root에 붙어 있다 - 클립이 콜라이더까지 회전시켜 " +
                             "플레이어를 튕겨낸다. Animator는 시각 자식(Hourglass_RockVisual)으로 옮겨라.", this);

        // 씬에 이미 놓인 인스턴스는 Rigidbody constraints가 예전 값(None)으로 직렬화돼 있다.
        // 여기서 잠가야 씬 재생성 없이도 밀림 정책이 적용된다(이 시스템의 반복 함정 - 코드 기본값
        // 변경은 이미 배치된 인스턴스에 반영되지 않는다). 정책을 끈 경우에는 constraints에 손대지
        // 않아 Rigidbody에 설정한 값이 그대로 살아 있는다.
        if (lockUnlessPushedByCube) LockInPlace();
    }

    private void FixedUpdate()
    {
        if (!lockUnlessPushedByCube)
        {
            Unlock();
            return;
        }

        // 밀던 네모가 파괴·비활성·도형 변형(ScalingSystem)으로 사라지면 OnCollisionExit이 오지 않을
        // 수 있다. 유령 접촉 하나가 남아 잠금이 영구히 풀린 채로 방치되는 것을 막는다.
        cubePushers.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        if (cubePushers.Count == 0) LockInPlace();
    }

    private void OnCollisionEnter(Collision collision)
    {
        PlayerShapeIdentity shape = collision.collider != null
            ? collision.collider.GetComponentInParent<PlayerShapeIdentity>()
            : null;

        TrackPusher(collision.collider, shape);
        TryFlip(collision, shape);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider != null) cubePushers.Remove(collision.collider);
    }

    // 네모의 접촉만 잠금을 푼다. 발동 게이트(useShapeGate)와 독립이다 - 게이트를 플레이테스트용으로
    // 껐다고 해서 밀림 정책까지 같이 풀려서는 안 된다(둘은 다른 축의 분담이다).
    private void TrackPusher(Collider other, PlayerShapeIdentity shape)
    {
        if (!lockUnlessPushedByCube || other == null) return;
        if (shape == null || shape.Kind != PlayerShapeStats.ShapeKind.Cube) return;

        // 접촉 즉시 풀어야 그 프레임의 밀기가 낭비되지 않는다(FixedUpdate까지 기다리면 한 스텝 씹힌다).
        cubePushers.Add(other);
        Unlock();
    }

    private void TryFlip(Collision collision, PlayerShapeIdentity shape)
    {
        if (Time.time < nextAllowedTime) return;

        // 때린 쪽에 Rigidbody가 없으면(정적 바닥·벽) 발동 조건 자체가 아니다. 자기 질량으로 대체하면
        // 낙석이 바닥에 떨어지는 것만으로 판정을 통과해(mass 2 x 2m/s = 4.0) 아무도 안 건드렸는데
        // 감속 구역이 켜진다. "때린 쪽의 운동량"은 때린 쪽이 있을 때만 성립하는 값이다.
        if (collision.rigidbody == null) return;

        // 도형이 없는 Rigidbody(낙석·상자 등 플레이어 아닌 물체)도 게이트가 켜져 있으면 발동시키지
        // 못한다 - 발동은 세모의 역할이고, 굴러온 물체가 우연히 켜는 것을 막는 취지가 위 정적 바닥
        // 가드와 같다. 협동을 우회할 다른 경로를 열어두지 않는다.
        if (useShapeGate && (shape == null || shape.Kind != PlayerShapeStats.ShapeKind.Tetrahedron))
            return;

        float momentum = collision.rigidbody.mass * collision.relativeVelocity.magnitude;
        if (momentum < requiredMomentum) return;

        nextAllowedTime = Time.time + cooldown;
        PlayFlip();

        if (targetSlowZone != null)
        {
            targetSlowZone.Activate();
            Debug.Log($"[FallingRockFlip] '{name}' 충돌 운동량 {momentum:F2} (>= {requiredMomentum}) - 뒤집기 + 감속 구역 발동");
        }
    }

    // 잠금 = FreezeAll. 남아 있던 속도를 0으로 되돌려 "밀리던 관성으로 더 미끄러지지 않고 그 자리에
    // 선다"를 만든다(constraints만 걸면 축 속도는 솔버가 지우지만, 명시적으로 지워 상태를 확정한다).
    private void LockInPlace()
    {
        if (positionLocked) return;
        positionLocked = true;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.constraints = RigidbodyConstraints.FreezeAll;
    }

    // 잠금 해제 = 위치만 자유(FreezeRotation 유지). 회전을 풀면 네모가 모서리를 밀 때 모래시계가
    // 굴러가 뒤집기 연출의 기준 자세가 어긋난다. 뒤집기는 시각 자식의 localRotation만 건드리므로
    // constraints와 독립이다 - 잠금/해제 어느 쪽이든 연출은 그대로 나온다.
    private void Unlock()
    {
        if (!positionLocked) return;
        positionLocked = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void PlayFlip()
    {
        if (animator != null)
        {
            animator.SetTrigger("Flip");
            return;
        }
        StopAllCoroutines();
        StartCoroutine(FlipRoutine());
    }

    // 시각 자식의 localRotation만 돌린다. 물리(Rigidbody/BoxCollider)는 Root에 있고 여기 손대지
    // 않으므로 콜라이더는 뒤집기 중에도 제자리다 - 회전 콜라이더가 플레이어를 훑어 튕겨내는 일이 없다.
    // X축 180도는 생성 형상(정육면체 + 대칭 BoxCollider)에서 콜라이더 정합에 손실이 없다:
    // 뒤집힌 뒤의 콜라이더 점유 공간이 뒤집기 전과 동일해, 시각과 콜라이더가 어긋나 보이지 않는다.
    private IEnumerator FlipRoutine()
    {
        Transform target = visualRoot != null ? visualRoot : transform;
        Quaternion start = target.localRotation;
        Quaternion end = start * Quaternion.Euler(180f, 0f, 0f);
        float t = 0f;
        while (t < flipDuration)
        {
            t += Time.deltaTime;
            target.localRotation = Quaternion.Slerp(start, end, t / flipDuration);
            yield return null;
        }
        target.localRotation = end;
    }
}
