using UnityEngine;

/// <summary>
/// 구역 안에 있는 플레이어를 이 오브젝트의 forward 방향으로 지속적으로 밀어내는 돌풍 구역.
/// AccelPad(순간 부스트, 조작 대신 velocity를 완전히 넘겨받음)와 달리 구역 안에 머무는 동안
/// 계속 밀리기만 하며, 플레이어의 이동/조향은 전혀 제한하지 않는다 — 실제 velocity 계산은
/// PlayerWindReceiver가 플레이어 입력 위에 바람 세기를 얹는 방식으로 처리한다(발신자-수신자
/// 분리). 공중에 뜬 플레이어는 airMultiplier만큼 더 강하게 밀린다.
/// Collider는 반드시 Is Trigger = true로 설정하세요.
/// </summary>
[ExecuteAlways] // 에디터에서 콜라이더 크기를 드래그로 조정하는 즉시(플레이 모드 아니어도) 파티클이 따라오게 한다.
[RequireComponent(typeof(Collider))]
public class WindZone : MonoBehaviour
{
    [Header("돌풍 설정")]
    [Tooltip("구역 안에서 이 오브젝트의 forward 방향으로 미는 속도(m/s).")]
    public float windSpeed = 8f;

    [Tooltip("공중에 뜬 플레이어에게는 windSpeed에 이 배율을 추가로 곱한다(더 강하게 밀림).")]
    public float airMultiplier = 2f;

    [Header("무게 기반 반응 (PlayerWeight.Of 재사용 — Kind 하드코딩 아님)")]
    [Tooltip("이 무게 이상이면 바람을 전혀 받지 않는다(0배). 정육면체 기본 무게(3.0)를 기준으로 " +
        "잡으면 정육면체는 면역, 그보다 가벼운 도형만 영향권에 든다.")]
    public float heavyWeightCutoff = 3f;
    [Tooltip("이 무게 이하면 lightWeightBoost 배율을 그대로(최대로) 받는다. 정사면체 기본 무게(1.0) " +
        "기준이면 정사면체가 가장 강하게 밀린다.")]
    public float lightWeightThreshold = 1f;
    [Tooltip("lightWeightThreshold 이하 무게가 받는 배율(windSpeed/airMultiplier와 별도로 곱해진다). " +
        "heavyWeightCutoff와 lightWeightThreshold 사이는 이 값에서 0까지 선형 보간된다.")]
    public float lightWeightBoost = 2f;

    [Tooltip("바람 방향을 보여주는 파티클(WindZone_Visual). 지정하면 이 구역의 BoxCollider " +
        "size/center가 바뀔 때마다 파티클 방출 범위를 자동으로 맞춘다 — 안 그러면 씬에서 구역을 " +
        "리사이즈했을 때(특히 한쪽 면 핸들만 드래그하면 center도 같이 움직여) 파티클이 옛 중심에만 " +
        "남아 늘어난 쪽엔 안 채워진다. Tools 메뉴로 생성하면 자동 연결된다.")]
    public ParticleSystem windVisual;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void LateUpdate()
    {
        SyncVisualToBox();
    }

    private void SyncVisualToBox()
    {
        if (windVisual == null) return;
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        ParticleSystem.ShapeModule shape = windVisual.shape;
        shape.scale = box.size;
        shape.position = box.center;
    }

    private void OnTriggerStay(Collider other)
    {
        PlayerWindReceiver receiver = other.GetComponentInParent<PlayerWindReceiver>();
        if (receiver == null) return;

        float weight = PlayerWeight.Of(receiver.GetComponent<Rigidbody>());
        float weightMultiplier = Mathf.Lerp(0f, lightWeightBoost, Mathf.InverseLerp(heavyWeightCutoff, lightWeightThreshold, weight));

        receiver.SetWindTarget(transform.forward * windSpeed * weightMultiplier, airMultiplier);
    }

    // OnTriggerExit는 따로 두지 않는다 — PlayerWindReceiver가 매 FixedUpdate 자신의 상태를
    // 소비하고 리셋하므로, 구역을 벗어나 이번 스텝에 OnTriggerStay가 안 불리는 것만으로
    // 다음 물리 스텝에 자연히 0으로 꺼진다("즉시 제거" 요구사항을 그대로 만족).

    // 씬 뷰에서 구역 범위와 바람 방향을 시각화한다.
    // [수정 이력] 예전엔 transform.lossyScale로 큐브를 그렸는데, 실제 트리거는 BoxCollider.size(기본
    // 4×3×4)를 쓰고 Zone 오브젝트 자체 스케일은 보통 1이라 기즈모가 실제 볼륨보다 훨씬 작게
    // 그려지는 버그였다. BoxCollider의 center/size를 직접 읽어 실제 트리거와 일치시킨다.
    private void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Vector3 localCenter = box != null ? box.center : Vector3.zero;
        Vector3 localSize = box != null ? box.size : Vector3.one;

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.18f);
        Gizmos.DrawCube(localCenter, localSize);
        Gizmos.color = new Color(0.25f, 0.95f, 1f, 0.9f);
        Gizmos.DrawWireCube(localCenter, localSize);
        Gizmos.matrix = Matrix4x4.identity;

        Vector3 worldSize = Vector3.Scale(localSize, transform.lossyScale);
        DrawWindArrowGrid(worldSize);
    }

    // 화살표 하나로는 큰 구역에서 눈에 잘 안 띄어, 단면에 격자로 여러 개를 그려 방향을 확실히 읽히게 한다.
    private void DrawWindArrowGrid(Vector3 worldSize)
    {
        Vector3 dir = transform.forward;
        float arrowLength = Mathf.Max(0.5f, worldSize.z * 0.4f);

        Gizmos.color = Color.cyan;
        for (int ix = -1; ix <= 1; ix++)
        {
            for (int iy = -1; iy <= 1; iy += 2)
            {
                Vector3 origin = transform.position
                    + transform.right * (ix * worldSize.x * 0.3f)
                    + transform.up * (iy * worldSize.y * 0.25f);
                DrawArrow(origin, dir, arrowLength);
            }
        }
    }

    private static void DrawArrow(Vector3 origin, Vector3 dir, float length)
    {
        Vector3 tip = origin + dir * length;
        Gizmos.DrawLine(origin, tip);
        Vector3 right = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 150f, 0) * Vector3.forward;
        Vector3 left = Quaternion.LookRotation(dir) * Quaternion.Euler(0, -150f, 0) * Vector3.forward;
        Gizmos.DrawLine(tip, tip + right * (length * 0.3f));
        Gizmos.DrawLine(tip, tip + left * (length * 0.3f));
    }
}
