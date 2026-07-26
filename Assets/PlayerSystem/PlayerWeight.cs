using UnityEngine;

/// <summary>
/// "무거워서 못 한다"류 판정의 저장소 공통 창구. 어떤 바디가 지금 실제로 얼마나 무거운지를
/// **질량 × 그 바디에 실제로 걸리는 중력 배율**로 답한다.
///
/// [왜 질량이 아니라 무게인가]
/// 실이 끊어지고 구름이 내려앉고 무게판이 눌리는 건 질량이 아니라 무게 때문이다. 무게로 재면
/// 무중력 버블처럼 개별 중력을 낮추는 구역 안에서 네모(질량 3.0 → 무게 1.8)가 구름에 튕기고 실에
/// 매달리는 상호작용이 **도형별 특수 분기 없이** 성립한다. 나중에 중력을 낮추는 다른 기믹(모래시계
/// 감속 구역 등)이 추가돼도 판정 코드를 고칠 필요가 없다.
///
/// [왜 정적 헬퍼 하나인가 — 컴포넌트도 인터페이스도 아님]
/// 상태가 없고 구현이 하나뿐이라 컴포넌트로 만들면 플레이어마다 붙여야 하는 부담만 생기고,
/// 인터페이스로 추상화하면 구현 하나짜리 확장점이 된다. 지금 필요한 건 "식이 한 군데에만 있을 것"
/// 뿐이라 정적 메서드 하나로 끝낸다.
///
/// 쓰는 곳: DreamThreadController(매달림 게이트 + 매달린 중 재판정), ThreadBridge(줄다리 처짐),
/// DoorSystem/ExitWeightPlate(출구 무게판), CloudTrampoline(튕김·과부하 붕괴).
/// **식을 복제하지 말고 이 메서드를 호출할 것** — 판정 기준이 갈라지면 "가벼워서 매달렸는데
/// 무거워서 문이 열린다" 같은 모순이 난다.
/// </summary>
public static class PlayerWeight
{
    /// <summary>이 바디의 무게. PlayerGravityOverride가 없거나 오버라이드 중이 아니면 배율 1이라
    /// 질량 그대로다. body가 null이면 0.</summary>
    public static float Of(Rigidbody body)
    {
        if (body == null) return 0f;

        float g = Mathf.Abs(Physics.gravity.y);
        PlayerGravityOverride grav = body.GetComponent<PlayerGravityOverride>();
        float ratio = (grav != null && g > 0f) ? grav.EffectiveGravityMagnitude / g : 1f;
        return body.mass * ratio;
    }
}
