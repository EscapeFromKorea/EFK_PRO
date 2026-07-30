using UnityEngine;

/// <summary>
/// 낙석이 바닥에 부딪혀 부서질 때 튀어나오는 파편 하나. FallingRock.Shatter가 런타임에 생성하고
/// 이 컴포넌트를 붙인다(씬에 손으로 배치하는 컴포넌트가 아니다).
///
/// [왜 알파 페이드가 아니라 축소인가]
/// 낙석 머티리얼은 불투명이다. 페이드를 하려면 공유 머티리얼의 블렌드 설정을 런타임에 바꾸거나
/// (그러면 원본 낙석까지 반투명해진다) 파편용 머티리얼을 새로 만들어야 한다. 이 저장소는 이미
/// 반투명 머티리얼의 블렌드 설정이 반복해서 드리프트하는 문제를 안고 있어
/// (CloudTrampoline·RainbowBridge 머티리얼), 그 영역을 늘리지 않는 편이 싸다. 크기를 줄이는 것은
/// Transform 한 줄이고 콜라이더도 함께 줄어든다(BoxCollider 크기는 로컬 공간이므로).
///
/// [수명 끝에서 0이 아니라 minScale로 자르는 이유]
/// 스케일이 0에 가까운 콜라이더는 PhysX가 경고를 뱉는다. 마지막 프레임까지 유효한 크기를 유지한
/// 채로 Destroy한다 - 어차피 그 시점엔 화면에서 거의 안 보인다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FallingRockShard : MonoBehaviour
{
    /// <summary>수명(초). 생성한 FallingRock이 자기 shardLifetime을 넣어준다.</summary>
    [HideInInspector] public float lifetime = 1.2f;

    private const float MinScale = 0.05f;

    private Vector3 startScale;
    private float bornAt;

    // Awake가 아니라 Start다 - 생성 측이 AddComponent 전에 localScale을 대입하므로 Start 시점에는
    // 최종 크기가 들어와 있다. (Awake는 AddComponent 그 자리에서 즉시 돌아 순서 보장이 더 까다롭다.)
    private void Start()
    {
        startScale = transform.localScale;
        bornAt = Time.time;
    }

    private void Update()
    {
        if (lifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        float t = (Time.time - bornAt) / lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.localScale = startScale * Mathf.Max(MinScale, 1f - t);
    }
}
