using UnityEngine;

/// <summary>
/// 투석기 바퀴(`Catapult_Wheel`)가 투석기 이동 속도에 맞춰 굴러가는 것처럼 보이게 하는 순수 시각
/// 컴포넌트 — 11차 개편(2026-08-05) 신규. `PlayerVisualRoll`(정육면체/정사면체가 구르는 느낌을
/// 시각 메쉬만 기울여 내는 것)과 같은 성격의 장식이다: 콜라이더/Rigidbody에는 전혀 관여하지 않고,
/// 자기 자신의 Transform 회전만 돌린다.
///
/// [왜 GetComponentInParent&lt;Rigidbody&gt;()로 속도를 읽나]
/// 바퀴 자신은 Rigidbody가 없는 static child라(11차 개편으로 콜라이더는 복구했지만 물리 시뮬레이션
/// 대상은 아니다), 이동 속도를 알 방법이 없다. 투석기 루트의 Rigidbody(조향 SpringJoint가
/// 움직이는 그 바디)가 유일한 속도 정보원이다.
///
/// [왜 로컬 X축인가]
/// `CatapultMenuItem.CreateWheel`이 이미 원통의 높이축을 로컬 Z 90도 회전으로 로컬 X(바퀴의 축
/// 방향)로 돌려놨다 — 바퀴가 굴러가는 회전은 그 축(로컬 X)을 중심으로 일어나야 한다.
///
/// [12차 개편(2026-08-05) — 회전이 과하다는 신고, 진짜 원인은 수직(Y) 속도 유입]
/// 코디네이터의 첫 가설(`CreateWheel`이 `wheelRadius`를 안 채워 기본값 1f가 실제 반지름(1.5)보다
/// 작아 과회전한다)을 코드로 재확인했는데, 이미 `wheel.AddComponent&lt;CatapultWheelVisual&gt;()
/// .wheelRadius = radius;`로 정확히 채워지고 있었다 — 그 가설은 아니었다. 실제 원인은
/// `rootBody.velocity.magnitude`가 수평(구르는) 속도뿐 아니라 **수직(Y, 낙하·바운스·착지 정착)
/// 속도까지 그대로 섞어** 굴림 속도로 오인했다는 것이다 — 투석기가 스폰 직후 중력으로 떨어지거나
/// 착지 순간 튀는 등 순수 수직 운동만 있을 때도(실제로는 전혀 구르지 않는데도) 그 크기가 고스란히
/// 회전으로 나타나 "과하게 돈다"는 인상을 줬다. 수평(XZ) 성분만 남기도록 고쳤다 — 실제 바퀴가
/// 굴러가는 물리와 일치한다(바퀴는 앞으로 갈 때만 돌지, 위아래로 튈 때는 돌지 않는다).
/// </summary>
public class CatapultWheelVisual : MonoBehaviour
{
    [Tooltip("바퀴 반지름(월드 유닛). CatapultMenuItem.CreateWheel이 이미 계산한 radius를 그대로 " +
             "넘겨준다 — 각속도(도/초) = (선속도/반지름)×(180/π).")]
    public float wheelRadius = 1f;

    private Rigidbody rootBody;

    void Awake()
    {
        rootBody = GetComponentInParent<Rigidbody>();
    }

    void Update()
    {
        if (rootBody == null || wheelRadius <= 0f) return;

        // 12차 개편 — 수평(XZ) 속도만 굴림 속도로 쓴다. 전체 velocity.magnitude를 쓰면 낙하/바운스의
        // 수직(Y) 성분까지 회전으로 오인돼 과회전한다(클래스 상단 "12차 개편" 주석 참고).
        Vector3 v = rootBody.velocity;
        float speed = new Vector2(v.x, v.z).magnitude;
        float degPerSec = (speed / wheelRadius) * Mathf.Rad2Deg;
        transform.Rotate(Vector3.right, degPerSec * Time.deltaTime, Space.Self);
    }
}
