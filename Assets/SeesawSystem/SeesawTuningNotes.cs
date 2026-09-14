using UnityEngine;

/// <summary>
/// 판의 Rigidbody/HingeJoint 값을 왜 이렇게 잡았는지 인스펙터 hover로 보여주는 주석 전용
/// 컴포넌트. Update/FixedUpdate가 없어 런타임 동작에는 전혀 관여하지 않는다 — 여기 필드는 실제
/// 물리 값을 복제하지 않는다(복제하면 둘이 어긋날 수 있다). 값 자체는 항상 위 Rigidbody/
/// HingeJoint 컴포넌트에서 직접 조정한다.
/// </summary>
public class SeesawTuningNotes : MonoBehaviour
{
    [Tooltip("Rigidbody > Mass. 판 자체 질량 — 낮출수록 회전 관성이 줄어 같은 무게 차이에도 더 " +
        "잘 기운다(뻑뻑하면 이 값부터 낮춰본다). 올릴수록 뻑뻑해진다.")]
    public string mass = "→ 위 Rigidbody 컴포넌트 참고";

    [Tooltip("Rigidbody > Angular Drag. 회전 저항 — 낮을수록 잘 움직이지만 무게가 바뀔 때 좌우로 " +
        "계속 흔들릴 위험이 있다. 2026-09-07엔 Seesaw_Fulcrum이 판에 박혀 흔들리던 버그를 이 값을 " +
        "6배(0.5→3)로 올려서 덮었던 적이 있다 — Fulcrum을 제대로 맞춘 뒤에는 이 값을 다시 낮춰보고 " +
        "흔들림이 재현되지 않는 최소값을 찾는다.")]
    public string angularDrag = "→ 위 Rigidbody 컴포넌트 참고";

    [Tooltip("HingeJoint > Limits (min/max). 판이 기울 수 있는 최대 각도 — 뻑뻑함과는 무관하고, " +
        "지면에 처박히거나 과도하게 서는 것만 막는다.")]
    public string hingeLimits = "→ 위 HingeJoint 컴포넌트 참고";
}
