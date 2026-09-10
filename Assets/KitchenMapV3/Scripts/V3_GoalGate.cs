using UnityEngine;

/// <summary>
/// P5 뒷마당 GOAL 판정 — 기믹배선_계획_2026-09-06.md §1(R2) B-2 / §0 "GOAL_Gate 0/1".
///
/// 판 A(무게 2.75)·B·C(실 게이트 3.0 너머 도달) 셋이 **거의 동시에** 눌리면 게이트를 연다.
///
/// [R1+Q1 반려, map-reviewer 22차 — 판정식 정정]
/// 이전 버전은 "세 센서가 지금 이 프레임에 전부 Pressed==true"(동일 프레임 AND)를 요구한 뒤에야
/// LastPressedTime 스프레드를 봤다 — 그러면 셋 중 하나라도 먼저 밟고 판을 떠나면(예: 판 A를 밟은
/// 세모가 다음 순간 판을 벗어나 Pressed가 다시 false가 됨) 나머지 둘을 아무리 3초 안에 밟아도
/// 다시는 셋이 "동시에" Pressed==true인 프레임이 안 와 영영 안 열렸다. 지시대로 판마다 독립적으로
/// "세 센서 모두 (Time.time − LastPressedTime) ≤ toleranceSec(3)"만 보도록 정정한다 — 동일
/// 프레임 AND가 아니라 "마지막으로 눌렸던 시각이 서로 3초 창 안에 들어오는가"이므로, 먼저 밟고
/// 내려온 판도 3초 이내면 유효하다.
///
/// [왜 doorPhysics(MoveTowards)를 안 쓰나 — 🔒H2 P5 "0 아니면 1"]
/// doorPhysics는 목표 위치까지 서서히 이동한다(문이 열리는 도중의 중간 상태가 존재) — P5는 판정이
/// "열림/안 열림" 둘 중 하나뿐이어야 한다는 잠금이라, 문이 아니라 `GameObject.SetActive(false)`로
/// 게이트 오브젝트 자체를 즉시·1회·되돌림 없이 끈다(렌더러+콜라이더가 한 프레임에 함께 사라진다 —
/// 중간 상태가 물리적으로 존재할 수 없다).
///
/// [신호 0]
/// 지시 원문 "판별 피드백·개수 신호 0" — 이 컴포넌트는 Debug.Log·소리·조명을 전혀 내지 않는다.
/// (다만 형제로 붙는 판 A의 저장소 사본 `ExitWeightPlate`는 이 컴포넌트와 무관하게 자기 자신의
/// 열림/닫힘마다 Debug.Log를 낸다 — 저장소 코드라 수정하지 않았다. 검토 요청 사항으로 별도 보고.)
/// </summary>
public class V3_GoalGate : MonoBehaviour
{
    [Tooltip("판 A — 무게 2.75 판정(ExitWeightPlate와 같은 GameObject의 형제 컴포넌트).")]
    public V3_PlateSensor plateA;
    [Tooltip("판 B — 실 게이트 3.0 너머 도달 판정(도형 존재만).")]
    public V3_PlateSensor plateB;
    [Tooltip("판 C — 실 게이트 3.0 너머 도달 판정(도형 존재만).")]
    public V3_PlateSensor plateC;

    [Tooltip("판마다 '지금(Time.time) − 그 판의 마지막 눌림 시각'이 이 값(초) 이내여야 한다 — 세 판 " +
             "모두 각자 이 조건을 만족하면 '동시'로 본다(동일 프레임에 셋 다 눌려 있을 필요는 없다).")]
    public float toleranceSec = 3f;

    [Tooltip("판정 성립 시 SetActive(false)할 게이트 오브젝트 — 'GOAL_Gate_닫힘(0/1·무변화)'.")]
    public GameObject gateObject;

    private bool triggered;

    void Update()
    {
        if (triggered) return;
        if (plateA == null || plateB == null || plateC == null || gateObject == null) return;

        // R1+Q1: 동일 프레임 AND가 아니라, 판마다 독립적으로 "마지막 눌림 기준 3초 창"을 본다 —
        // 세 판 모두 지금 이 순간 Pressed일 필요는 없다(하나가 먼저 밟혔다 떠나도 3초 안이면 유효).
        float now = Time.time;
        if (now - plateA.LastPressedTime > toleranceSec) return;
        if (now - plateB.LastPressedTime > toleranceSec) return;
        if (now - plateC.LastPressedTime > toleranceSec) return;

        triggered = true;
        gateObject.SetActive(false); // 즉시·1회·되돌림 없음(🔒H2 P5).
    }
}
