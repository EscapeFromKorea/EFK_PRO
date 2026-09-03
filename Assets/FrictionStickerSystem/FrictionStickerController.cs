using UnityEngine;

/// <summary>
/// 마찰 스티커 시스템의 씬 측 진입점. 두 가지만 한다:
///  1. 튜닝값(<see cref="FrictionStickerSettings"/> + 캐리어 기본값)을 인스펙터에 노출.
///  2. 씬의 플레이어(PlayerMover 보유)들에게 <see cref="PlayerStickerCarrier"/>가 없으면 붙이고,
///     그 캐리어에 튜닝값을 밀어 넣는다.
///
/// [씬에 안 놔도 동작] Tools 메뉴로 만들어 인스펙터에서 튜닝할 수 있지만, 없으면
/// RuntimeInitializeOnLoadMethod가 기본값짜리 인스턴스를 하나 자동 생성한다. 즉 "씬에 스티커
/// 표면만 두면 바로 동작"한다(Portal이 리시버를 런타임에 붙여 씬 무수정으로 동작하는 것과 같은 취지).
/// 씬에 여러 개 생기면 첫 번째만 남기고 정리한다.
///
/// [교차 폴더 하드룰 준수] PlayerSystem 파일도 SampleScene도 수정하지 않는다. 플레이어에서
/// IsControlled/ExternallyDriven을 읽고 캐리어를 AddComponent할 뿐이다.
/// </summary>
[DisallowMultipleComponent]
public class FrictionStickerController : MonoBehaviour
{
    [Header("마찰 / 시각 튜닝 (모든 스티커 공통)")]
    public FrictionStickerSettings frictionSettings = new FrictionStickerSettings();

    [Header("플레이어 캐리어 기본값 (붙일 때 주입)")]
    [Tooltip("플레이어에서 이 거리(Unit) 안, 가장 가까운 StickerSurface를 대상으로 삼는다(근접 방식).")]
    public float aimRange = 4f;
    [Tooltip("부착 / 교체 / 회수 키. (F·G는 꿈의 실타래와 겹쳐 V 사용)")]
    public KeyCode attachKey = KeyCode.V;
    [Tooltip("미끄럼 ↔ 벨크로 전환 키.")]
    public KeyCode switchKindKey = KeyCode.Q;
    [Tooltip("도형별 미끄럼 스티커 보유 개수. -1이면 무한(그레이박스 기본).")]
    public int slipCount = -1;
    [Tooltip("도형별 벨크로 스티커 보유 개수. -1이면 무한(그레이박스 기본).")]
    public int velcroCount = -1;

    [Tooltip("몇 초마다 씬에서 플레이어를 다시 훑어 캐리어를 보장할지(초). 런타임에 플레이어가 " +
             "새로 생겨도 곧 붙는다.")]
    public float rescanInterval = 1f;

    private static FrictionStickerController instance;
    private float rescanTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Object.FindObjectOfType<FrictionStickerController>() != null) return;

        GameObject go = new GameObject("FrictionStickerController (auto)");
        go.AddComponent<FrictionStickerController>();
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
            PlayerStickerCarrier carrier = mover.GetComponent<PlayerStickerCarrier>();
            if (carrier == null)
                carrier = mover.gameObject.AddComponent<PlayerStickerCarrier>();

            PushSettings(carrier);
        }
    }

    // 튜닝값을 캐리어로 밀어 넣는다. 보유 개수는 아직 사용자가 안 만진 초기 상태(기본값 -1)일
    // 때만 덮어써, 플레이 중 소모된 개수를 되돌리지 않는다.
    private void PushSettings(PlayerStickerCarrier carrier)
    {
        carrier.settings = frictionSettings;
        carrier.aimRange = aimRange;
        carrier.attachKey = attachKey;
        carrier.switchKindKey = switchKindKey;

        if (carrier.slipCount == -1 && slipCount != -1) carrier.slipCount = slipCount;
        if (carrier.velcroCount == -1 && velcroCount != -1) carrier.velcroCount = velcroCount;
    }
}
