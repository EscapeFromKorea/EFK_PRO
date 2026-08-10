using UnityEngine;

/// <summary>
/// 물리 회전을 고정(RigidbodyConstraints.FreezeRotation)한 플레이어(정육면체/정사면체)의
/// 시각 메쉬(Player_MeshVisual)만 살짝 기울여 점프에 "약간의 회전감"을 준다. 콜라이더와
/// 무게중심은 축이 고정돼 점프 도달 궤적·착지가 결정론적으로 유지되므로(레벨 도달 규격표의 전제),
/// 회전은 순수 시각 효과로만 존재한다.
///
/// [연속 회전을 쓰지 않는 이유] 예전엔 공중에서 v=ωr로 계속 굴렸는데, 체공이 길면 한 바퀴 넘게
/// 누적돼 착지 시 똑바로 되감는 동작이 "확 돌아가는" 스냅으로 보였다. 그래서 누적하지 않고,
/// 이동 방향으로 각도가 제한된 목표 기울기를 향해 매 프레임 수렴만 시킨다(공중=기울임, 접지=직립).
/// 되감을 양이 작고 항상 부드럽게 수렴하므로 착지 스냅이 없다.
///
/// [경사면 정렬] (2026-08-10 추가) 접지 중 목표 자세는 원래 월드 직립(identity)이었다. 콜라이더가
/// FreezeRotation이라 물리적으로는 그게 맞지만, 비탈 위에서 다면체가 월드 기준으로 꼿꼿이 선 채
/// 아래쪽 모서리 하나로만 닿아 "비스듬히 박힌" 것처럼 보였다(FreezeRotation의 구조적 귀결이며,
/// 경사면 가감속·흘러내림이 들어가 다면체가 비탈에서 실제로 움직이게 되면서 드러났다). 그래서
/// 접지 중에는 바닥 법선에 맞춰 시각 메쉬만 눕힌다 — 콜라이더/Rigidbody 회전은 그대로 두므로
/// 점프 도달 궤적·착지 접촉 형상은 하나도 바뀌지 않는다(도달 규격표 재검증 불필요).
/// 눕히면 접촉 지점이 바뀌므로 법선 방향 침하(sink) 보정이 반드시 함께 가야 한다 — 아래 참고.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerVisualRoll : MonoBehaviour
{
    [Tooltip("기울일 시각 메쉬(Player_MeshVisual). 물리에는 영향을 주지 않는 순수 시각 오브젝트.")]
    public Transform visual;

    [Tooltip("접지 판정(PlayerGroundContact). 공중에서만 기울이기 위해 참조한다. 비면 항상 직립.")]
    public PlayerGroundContact groundContact;

    [Tooltip("공중에서 이동 방향으로 기울이는 최대 각도(도). 점프 시 '약간 회전'하는 연출. 0이면 회전 없음.")]
    public float maxTumbleAngle = 30f;

    [Tooltip("이 수평 속력(이상)에서 최대 각도에 도달한다. 느리게 움직이면 덜 기운다.")]
    public float speedForMaxTumble = 4f;

    [Tooltip("목표 기울기/직립으로 수렴하는 속도. 높을수록 빠릿하다. 착지 복귀도 이 속도라 자연스럽게 펴진다.")]
    public float responsiveness = 10f;

    [Tooltip("이 수직 속력(|velocity.y|)을 넘을 때만 '진짜 공중'으로 보고 기울인다. 접지 판정이 " +
             "순간 false로 튀어도(정사면체는 접촉 패치가 얕아 흔히 발생) 평지에서는 y속도가 ~0이라 " +
             "기울임이 새지 않는다. 점프/낙하는 y속도가 뚜렷해 연출이 그대로 나온다.")]
    public float airborneVerticalSpeed = 0.5f;

    [Header("경사면 정렬(시각 전용)")]
    [Tooltip("접지 중 시각 메쉬를 바닥 법선에 맞춰 눕힌다. 끄면 예전 동작(항상 월드 직립)으로 " +
             "완전히 되돌아가므로 플레이테스트에서 켜고/끄고 비교하는 용도로 쓴다.")]
    public bool alignToGround = true;

    [Tooltip("비탈에 눕힌 메쉬를 법선 방향으로 얼마나 내릴지 정하는 기준 길이(로컬 단위). " +
             "정육면체는 '반높이' 그 자체이므로 0.5가 기하적으로 정확한 값이다(축 정렬 상태의 " +
             "중심-비탈면 거리 h(|n.x|+|n.y|+|n.z|)와 누웠을 때의 h의 차이를 그대로 준다). " +
             "정사면체는 형상이 3회 대칭이라 이 4각 대칭 식과 정확히 맞는 값이 존재하지 않는다 — " +
             "비탈이 어느 방향을 보느냐에 따라 이론값이 0.29~0.57로 흔들리므로, 파고드는 쪽보다 " +
             "살짝 뜨는 쪽이 덜 어색하다고 보고 0.45에서 시작해 눈으로 맞춘다. " +
             "0이면 침하 보정 없음 = 눕힌 메쉬가 비탈 위에 뜬다.")]
    public float groundSinkFactor = 0.5f;

    [Tooltip("접지 상태로 미끄러질 때 이동 방향으로 추가로 주는 기울임(도). 기본 0 = 끔 — 비탈을 " +
             "미끄러지는 다면체는 '구르는' 게 아니라 자세 고정이 물리적으로 맞기 때문이다. " +
             "그래도 어색해 보이면 2~5도만 줘서 '끌리는' 느낌을 낸다(눈으로 보고 정할 값).")]
    public float groundSlideTiltAngle = 0f;

    private Rigidbody rb;

    // 시각 메쉬의 원래 로컬 위치(보통 zero). 침하 보정은 매 프레임 이 값 기준으로 다시 계산한다 —
    // 현재 위치에서 빼는 식으로 누적시키면 메쉬가 지면 아래로 계속 파고든다.
    private Vector3 baseLocalPosition;
    private float sinkAmount;
    private Vector3 sinkNormal = Vector3.up;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (visual != null) baseLocalPosition = visual.localPosition;
    }

    void Update()
    {
        if (visual == null) return;

        bool grounded = groundContact != null && groundContact.IsGrounded;
        // 접지 플래그만으로 판단하면, 정사면체처럼 접촉 패치가 얕아 IsGrounded가 순간 false로
        // 튀는 도형은 평지 이동 중에도 아래 기울임 연출이 새어 나와 "면이 안 붙고 살짝 롤이 든"
        // 것처럼 보인다. 진짜 체공은 수직 속도가 뚜렷하다는 점을 함께 걸어(FreezeRotation이라
        // 물리 자체는 절대 안 기울므로 화면 기울임의 유일한 원인이 이 스크립트다), 평지에서는
        // y속도≈0이라 접지 판정이 깜빡여도 기울임이 나오지 않게 한다.
        bool airborne = !grounded && Mathf.Abs(rb.velocity.y) > airborneVerticalSpeed;

        // 기울임 축/세기는 공중 텀블과 접지 슬라이드 기울임이 같은 식(이동 방향 기준)을 공유한다.
        Vector3 horiz = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        float speed = horiz.magnitude;
        Vector3 tiltAxis = speed > 0.01f ? Vector3.Cross(Vector3.up, horiz.normalized) : Vector3.zero;
        float speedT = speedForMaxTumble > 0.01f ? Mathf.Clamp01(speed / speedForMaxTumble) : 1f;
        // ScalingSystem으로 커지면 같은 각도라도 메쉬가 커진 만큼 모서리가 더 크게 휘둘려
        // 기울임이 과장돼 보인다. 스케일이 1보다 클 때 각도를 스케일로 나눠, 모서리의 실제
        // 처짐량(각도×크기)을 대략 일정하게 유지한다. 작아졌을 때(스케일<1)는 건드리지 않는다.
        float scale = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
        float angleScale = scale > 1f ? 1f / scale : 1f;

        // 바닥 법선. alignToGround가 꺼져 있거나 접지 판정이 없으면 up이라, 아래 정렬 회전이
        // identity가 되고 침하량도 0이 되어 예전(월드 직립) 동작과 완전히 같아진다.
        Vector3 groundNormal = (alignToGround && groundContact != null) ? groundContact.GroundNormal : Vector3.up;

        // 목표 자세: 공중 이동 중이면 이동 방향으로 제한된 기울임, 접지면 바닥 법선에 정렬(평지=identity).
        // 공중에서는 바닥 법선이 의미가 없으므로(그리고 GroundNormal은 공중에서 마지막 값이 그대로
        // 남으므로) 정렬을 쓰지 않고 예전처럼 identity 기준으로만 기울인다.
        // Root/Player_Mesh 회전이 identity라 월드축=로컬축이므로 월드축 기준 회전을 localRotation에
        // 그대로 대입해도 정합한다.
        Quaternion target = Quaternion.identity;
        if (airborne)
        {
            if (maxTumbleAngle > 0.01f && speed > 0.01f)
                target = Quaternion.AngleAxis(maxTumbleAngle * angleScale * speedT, tiltAxis);
        }
        else if (grounded)
        {
            target = Quaternion.FromToRotation(Vector3.up, groundNormal);
            if (groundSlideTiltAngle > 0.01f && speed > 0.01f)
                target = Quaternion.AngleAxis(groundSlideTiltAngle * angleScale * speedT, tiltAxis) * target;
        }

        // 프레임레이트 무관 지수 수렴. 누적이 아니라 목표를 향한 수렴이라 착지 시 되감을 양이 작다.
        float k = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
        visual.localRotation = Quaternion.Slerp(visual.localRotation, target, k);

        // [침하 보정] 회전만 시키면 이번엔 메쉬가 비탈 위에 뜬다. 축 정렬된 정육면체(반높이 h)는
        // 아래 모서리 하나로 지탱해 중심이 비탈면에서 h(|n.x|+|n.y|+|n.z|)만큼 떠 있는데, 비탈에
        // 나란히 누우면 h뿐이기 때문이다(20°에서 0.28h ≈ 0.14). 그래서 "지금 이 회전 상태에서
        // 필요한 거리"를 매 프레임 다시 재서 법선 방향으로 그만큼 내린다 — 목표 자세가 아니라
        // 현재 자세로 재기 때문에 수렴 도중에도, 비탈→평지처럼 법선이 갑자기 바뀌는 순간에도
        // 면이 지면에 붙은 상태가 유지된다(파고들거나 뜨지 않는다).
        if (grounded && alignToGround)
        {
            sinkNormal = groundNormal;
            sinkAmount = SupportDistance(Quaternion.identity, groundNormal)
                       - SupportDistance(visual.localRotation, groundNormal);
        }
        else
        {
            // 공중엔 기댈 바닥이 없다. 이륙(접지 유예 종료) 순간 툭 튀지 않도록 회전과 같은
            // 계수로 0까지 수렴시킨다.
            sinkAmount = Mathf.Lerp(sinkAmount, 0f, k);
        }
        visual.localPosition = baseLocalPosition - sinkNormal * sinkAmount;
    }

    /// <summary>
    /// 회전 q인 정육면체(반높이 groundSinkFactor)의 중심에서 dir 방향 표면까지의 거리(지지함수).
    /// q=identity면 h(|dir.x|+|dir.y|+|dir.z|), 경사에 정렬되면 h가 되어 두 값의 차가 곧 침하량이다.
    /// 정사면체는 형상이 달라 이 식이 정확하진 않지만 각도에 따른 증가 경향은 같으므로,
    /// groundSinkFactor를 정사면체용 값(≈0.45)으로 낮춰 근사한다.
    /// </summary>
    private float SupportDistance(Quaternion q, Vector3 dir)
    {
        return groundSinkFactor * (
            Mathf.Abs(Vector3.Dot(dir, q * Vector3.right)) +
            Mathf.Abs(Vector3.Dot(dir, q * Vector3.up)) +
            Mathf.Abs(Vector3.Dot(dir, q * Vector3.forward)));
    }
}
