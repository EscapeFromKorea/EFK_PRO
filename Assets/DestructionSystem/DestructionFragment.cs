using UnityEngine;

/// <summary>
/// BreakableObject.SpawnProceduralFragments가 런타임에 생성한 파편 하나에 붙는다(씬에 손으로
/// 배치하는 컴포넌트가 아니다 — FallingRockShard와 같은 패턴).
///
/// [왜 필요한가]
/// 파편이 스폰 즉시 최종 크기로 나타나면 원본이 그 프레임에 순간적으로 파편으로 바뀌는 하드 컷처럼
/// 보인다(2026-08-11 사용자 피드백 — "갑자기 변하는 느낌"). FallingRockShard가 수명 끝에서 크기를
/// 줄여 사라지는 것과 반대로, 여기서는 작은 크기에서 최종 크기로 growDuration 동안 커지게 해
/// "부서져 나오는" 느낌을 낸다. 물리(AddExplosionForce)는 스케일과 무관하게 스폰 즉시 걸리므로,
/// 조각이 커지는 동안에도 이미 날아가고 있다 — 튀어나오며 커지는 그림이 된다.
///
/// 콜라이더 크기도 로컬 스케일을 그대로 따라가므로, 0에 가까운 스케일에서 PhysX가 경고를 뱉지
/// 않도록 MinScale로 하한을 둔다(FallingRockShard와 같은 이유).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DestructionFragment : MonoBehaviour
{
    private const float MinScale = 0.05f;

    /// <summary>생성한 BreakableObject가 자기 fragmentGrowDuration을 넣어준다.</summary>
    [HideInInspector] public float growDuration = 0.12f;

    private Vector3 targetScale;
    private float bornAt;

    // Start다 - 생성 측이 AddComponent 전에 localScale을 최종 크기로 대입하므로 Start 시점에는
    // 그 값을 targetScale로 붙잡을 수 있다(FallingRockShard와 같은 순서 이유).
    private void Start()
    {
        targetScale = transform.localScale;
        if (growDuration <= 0f) return; // 0이면 이전처럼 즉시 최종 크기(하드 컷)로 둔다.

        transform.localScale = targetScale * MinScale;
        bornAt = Time.time;
    }

    private void Update()
    {
        if (growDuration <= 0f) return;

        float t = (Time.time - bornAt) / growDuration;
        if (t >= 1f)
        {
            transform.localScale = targetScale;
            enabled = false;
            return;
        }

        transform.localScale = targetScale * Mathf.Max(MinScale, t);
    }
}
