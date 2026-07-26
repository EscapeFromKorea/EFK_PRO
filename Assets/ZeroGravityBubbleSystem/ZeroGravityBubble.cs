using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무중력 기류 버블 — 구역 진입 시 대상의 "개별" 중력만 조정한다(전역 Physics.gravity는 안 건드림).
/// PlayerShapeIdentity.Kind로 도형을 판별해 세모=최고 높이 / 구=최장 거리 / 네모=거의 안 뜸 차등을
/// 준다(요구사항 확정값: 세모0.16 / 구0.25 / 네모0.60).
///
/// 실제 중력 오버라이드는 대상에 붙는 PlayerGravityOverride가 담당한다(없으면 여기서 자동 부착).
/// </summary>
[RequireComponent(typeof(Collider))]
public class ZeroGravityBubble : MonoBehaviour
{
    [Header("도형별 중력 배율 (요구사항 확정값)")]
    public float sphereGravityScale = 0.25f;
    public float tetrahedronGravityScale = 0.16f;
    public float cubeGravityScale = 0.60f;

    [Header("점프 강화")]
    [Tooltip("구역 안에 있는 동안 점프 목표 높이에 곱하는 배율. 1 = 평소와 같은 높이, 1.5 = 1.5배 높이.\n" +
             "중력 배율(체공이 길어짐)과는 별개 축이다 — 중력만 낮추면 오래 떠 있을 뿐 도달 높이는 그대로라, " +
             "'버블 안에서 더 높이 뛴다'는 체감은 이 값으로 낸다.\n" +
             "기본 점프뿐 아니라 구역 안의 점프대/구름 트램펄린 발사에도 같이 적용된다(모든 발사가 " +
             "PlayerJump.LaunchToHeight 하나로 수렴하기 때문). 기믹 발사는 원래 높이를 유지하고 싶으면 " +
             "그 기믹을 구역 밖에 두거나 이 값을 1로 둬라.")]
    public float jumpHeightMultiplier = 1.5f;

    [Header("공통 파라미터")]
    [Tooltip("구역 안에서 늘어나는 저항(drag) 배율. 기본값 x 이 값.")]
    public float dragMultiplier = 2f;
    [Tooltip("진입 시 중력 배율이 바뀌는 데 걸리는 시간(초). 살짝 부드럽게 떠오르는 느낌을 준다.")]
    public float transitionTime = 0.5f;
    [Tooltip("이탈 시 정상 중력으로 돌아가는 데 걸리는 시간(초). 0이면 즉시 복원 - 경계를 확실하게 " +
             "느끼려면 진입보다 훨씬 짧게(또는 0) 두는 걸 권장한다. 진입과 같은 값으로 부드럽게 " +
             "빠져나가고 싶으면 transitionTime과 같은 값으로 올려라.")]
    public float exitTransitionTime = 0f;
    [Tooltip("중력 감소와 별개로 추가로 위로 미는 힘. 0이면 순수 중력 감소만(기본값).")]
    public float upliftAcceleration = 0f;
    [Tooltip("0보다 크면 진입 지점 기준 이 높이(Unit) 이상 못 올라가게 막는다(무한 상승 방지, 선택).\n" +
             "주의: upliftAcceleration을 같이 켜면 이 높이에서 계속 위로 밀리는 힘이 못 벗어나 '낀' " +
             "것처럼 멈출 수 있다. 구역(Collider) 상단으로 자연스럽게 빠져나가게 두는 게 더 안전하다.")]
    public float maxRiseHeight = 0f;
    [Tooltip("이 태그를 가진 대상만 반응한다. 플레이어 외 운반 오브젝트 지원은 그런 오브젝트가 " +
             "실제로 생기면 그때 확장한다(현재 프로젝트에 아직 없음).")]
    public string playerTag = "Player";

    // 플레이어는 콜라이더가 2개(Player_Mesh 트리거 + Player_Collider 솔리드)이고 둘 다 Player
    // 태그를 달고 있어, 하나의 리지드바디가 들어오고 나갈 때 OnTriggerEnter/Exit가 각각 2번씩
    // 따로 불린다. 콜라이더 단위로 즉시 켜고 끄면 두 콜라이더가 정확히 같은 프레임에 겹치지
    // 않는 이상 몸의 절반이 아직 안에 있는데 중력이 복원되는 깜빡임이 생긴다. 그래서 콜라이더가
    // 아니라 "이 리지드바디에 속한 콜라이더가 몇 개나 안에 있는지"를 세어, 0->1이 될 때만 걸고
    // 1->0이 될 때만 푼다.
    private readonly Dictionary<Rigidbody, int> activeColliderCount = new Dictionary<Rigidbody, int>();
    private readonly Dictionary<Rigidbody, float> entryHeightY = new Dictionary<Rigidbody, float>();

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        int count = activeColliderCount.TryGetValue(rb, out int c) ? c + 1 : 1;
        activeColliderCount[rb] = count;
        if (count > 1) return; // 같은 리지드바디의 다른 콜라이더가 이미 처리해뒀다.

        PlayerGravityOverride body = rb.GetComponent<PlayerGravityOverride>();
        if (body == null) body = rb.gameObject.AddComponent<PlayerGravityOverride>();

        entryHeightY[rb] = rb.position.y;
        float scale = ResolveGravityScale(other);
        body.SetGravityScale(scale, dragMultiplier, upliftAcceleration, transitionTime, jumpHeightMultiplier);
        Debug.Log($"[ZeroGravityBubble] '{rb.name}' 진입 - 중력 배율 {scale}, 점프 배율 {jumpHeightMultiplier} 적용 시작 (전이 {transitionTime}s)");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || !activeColliderCount.TryGetValue(rb, out int count)) return;

        count -= 1;
        if (count > 0)
        {
            activeColliderCount[rb] = count;
            return; // 같은 리지드바디의 다른 콜라이더가 아직 안에 있다 - 아직 복원하지 않는다.
        }

        activeColliderCount.Remove(rb);
        entryHeightY.Remove(rb);

        PlayerGravityOverride body = rb.GetComponent<PlayerGravityOverride>();
        if (body != null) body.RestoreDefault(exitTransitionTime);
        Debug.Log($"[ZeroGravityBubble] '{rb.name}' 이탈 - 정상 중력으로 복원 (전이 {exitTransitionTime}s)");
    }

    /// <summary>선택 파라미터: maxRiseHeight > 0이면 진입 지점보다 그 이상 못 올라가게 상승속도를 끊는다.</summary>
    private void OnTriggerStay(Collider other)
    {
        if (maxRiseHeight <= 0f) return;
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || rb.isKinematic) return; // 벽 부착 중(ThreadPinPlacer)이면 velocity 대입 금지.
        if (!entryHeightY.TryGetValue(rb, out float baseY)) return;

        if (rb.position.y - baseY >= maxRiseHeight && rb.velocity.y > 0f)
        {
            Vector3 v = rb.velocity;
            v.y = 0f;
            rb.velocity = v;
        }
    }

    /// <summary>도형 판별 불가(운반 오브젝트 등)는 기본 중력(배율 1)으로 취급한다.</summary>
    private float ResolveGravityScale(Collider other)
    {
        PlayerShapeIdentity shape = other.GetComponentInParent<PlayerShapeIdentity>();
        if (shape == null) return 1f;

        switch (shape.Kind)
        {
            case PlayerShapeStats.ShapeKind.Sphere: return sphereGravityScale;
            case PlayerShapeStats.ShapeKind.Tetrahedron: return tetrahedronGravityScale;
            case PlayerShapeStats.ShapeKind.Cube: return cubeGravityScale;
            default: return 1f;
        }
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
    }
}
