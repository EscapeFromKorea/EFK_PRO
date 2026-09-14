using UnityEngine;

/// <summary>
/// 이 마커가 붙은 Rigidbody만 <see cref="SpacePortal"/>을 통과할 수 있다(PRD §3.6, 2026-09-10
/// 확정 §1 항목 13). 태그/레이어가 아니라 컴포넌트 존재로 판별한다 — <see cref="PortalSurface"/>와
/// 같은 관례이며, SnapBlock 결합 구조물처럼 의도치 않게 통과해버리는 사고를 막는다.
///
/// CCD(<see cref="CollisionDetectionMode.ContinuousDynamic"/>, <c>SnapBlockSystem/SnapBlock.cs</c>가
/// 이미 쓰는 설정)를 함께 적용해 고속 진입 시 포탈 트리거를 관통하는 것을 막는다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PortalTraversable : MonoBehaviour
{
    private void Reset()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }
}
