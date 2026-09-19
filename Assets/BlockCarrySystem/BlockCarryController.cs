using UnityEngine;

/// <summary>
/// 경량 도형 블록 들어올리기 시스템의 씬 측 진입점. 두 가지만 한다:
///  1. 튜닝 기본값(픽업 범위 / 머리 위 높이 / 내려놓기 거리 / 최대 질량 / 키)을 인스펙터에 노출.
///  2. 씬의 플레이어(PlayerMover 보유)에게 <see cref="PlayerBlockCarrier"/>가 없으면 붙이고,
///     그 캐리어에 튜닝값을 밀어 넣는다.
///
/// [씬에 안 놔도 동작] Tools 메뉴로 만들어 튜닝할 수 있지만, 없으면 RuntimeInitializeOnLoadMethod가
/// 기본값 인스턴스를 하나 자동 생성한다 — 씬에 SnapBlock만 있으면 바로 든다.
/// (FrictionStickerController / SnapBlockController 와 동일한 취지.)
///
/// [교차 폴더 하드룰 준수] PlayerSystem 파일도 씬의 플레이어 3종 프리팹도 SnapBlockSystem도
/// 수정하지 않는다. 플레이어에서 IsControlled / ExternallyDriven 을 읽고 캐리어를 AddComponent하며,
/// 블록은 밖에서 Rigidbody / Collider 만 제어한다. 전역 Physics.* 미변경(MP-01).
/// </summary>
[DisallowMultipleComponent]
public class BlockCarryController : MonoBehaviour
{
    [Header("플레이어 캐리어 기본값 (붙일 때 주입)")]
    [Tooltip("플레이어에서 이 거리(Unit) 안, 가장 가까운 자유 상태 SnapBlock을 대상으로 삼는다(근접 방식).")]
    public float pickupRange = 3.5f;

    [Tooltip("든 블록을 플레이어 중심에서 이 높이(Unit) 위에 고정한다. 머리 위로 얹는 지점.")]
    public float carryHeight = 1.1f;

    [Tooltip("내려놓기 지점을 플레이어 중심에서 바라보는 방향으로 이 거리(Unit)만큼 앞에 잡는다.")]
    public float dropDistance = 1.2f;

    [Tooltip("들 수 있는 블록 Rigidbody 질량 상한. 이보다 무거운 블록은 거부한다. " +
             "SnapBlock 생성 기본 질량은 1이라 기본값 2면 표준 블록은 통과한다.")]
    public float maxCarryMass = 2f;

    [Tooltip("픽업 / 내려놓기 토글 키. (E=결합, V=스티커, F·G=실타래와 겹치지 않게 C 사용)")]
    public KeyCode pickupKey = KeyCode.C;

    [Tooltip("몇 초마다 씬에서 플레이어를 다시 훑어 캐리어를 보장할지(초). 런타임에 플레이어가 " +
             "새로 생겨도 곧 붙는다.")]
    public float rescanInterval = 1f;

    private static BlockCarryController instance;
    private float rescanTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Object.FindObjectOfType<BlockCarryController>() != null) return;

        GameObject go = new GameObject("BlockCarryController (auto)");
        go.AddComponent<BlockCarryController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Start()
    {
        ScanAndAttach();
    }

    private void Update()
    {
        rescanTimer += Time.deltaTime;
        if (rescanTimer < Mathf.Max(0.1f, rescanInterval)) return;
        rescanTimer = 0f;
        ScanAndAttach();
    }

    private void ScanAndAttach()
    {
        foreach (PlayerMover mover in Object.FindObjectsOfType<PlayerMover>())
        {
            PlayerBlockCarrier carrier = mover.GetComponent<PlayerBlockCarrier>();
            if (carrier == null)
                carrier = mover.gameObject.AddComponent<PlayerBlockCarrier>();

            PushSettings(carrier);
        }
    }

    private void PushSettings(PlayerBlockCarrier carrier)
    {
        carrier.pickupRange = pickupRange;
        carrier.carryHeight = carryHeight;
        carrier.dropDistance = dropDistance;
        carrier.maxCarryMass = maxCarryMass;
        carrier.pickupKey = pickupKey;
    }
}
