using UnityEngine;

/// <summary>
/// 정육면체 블록 도킹 시스템의 씬 측 진입점. 두 가지만 한다:
///  1. 튜닝 기본값(도킹 거리 / 결합 판정 / 한도 / 키)을 인스펙터에 노출.
///  2. 씬의 플레이어(PlayerMover 보유)에게 <see cref="PlayerCubeDock"/>이 없으면 붙이고,
///     그 컴포넌트에 튜닝값을 밀어 넣는다. (도형 게이트는 컴포넌트 쪽에서 — 정육면체가 아니면
///     조준·도킹을 스스로 거부한다. 붙어 있어도 무해하다.)
///
/// [씬에 안 놔도 동작] Tools 메뉴로 만들어 튜닝할 수 있지만, 없으면 RuntimeInitializeOnLoadMethod가
/// 기본값 인스턴스를 하나 자동 생성한다 — 씬에 SnapBlock만 있으면 바로 도킹된다.
/// (FrictionStickerController / SnapBlockController / BlockCarryController 와 동일한 취지.)
///
/// [교차 폴더 하드룰 준수] PlayerSystem 파일도 씬의 플레이어 3종 프리팹도 SnapBlockSystem도
/// 수정하지 않는다. 플레이어에서 IsControlled / ExternallyDriven / Kind 를 읽고 도킹 컴포넌트를
/// AddComponent하며, 결합은 정육면체 Rigidbody에 붙인 전용 ConfigurableJoint로만 처리한다.
/// 전역 Physics.* 미변경(MP-01).
/// </summary>
[DisallowMultipleComponent]
public class CubeDockController : MonoBehaviour
{
    [Header("플레이어 도킹 기본값 (붙일 때 주입)")]
    [Tooltip("플레이어에서 이 거리(Unit) 안, 가장 가까운 자유 상태 SnapBlock을 대상으로 삼는다(근접 방식).")]
    public float dockRange = 3.5f;

    [Tooltip("정육면체 면과 블록 면의 중심이 이 거리(Unit) 안일 때 도킹 후보가 된다(딱딱 블록 결합과 동일 기준).")]
    public float snapDistance = 0.6f;

    [Tooltip("두 면 법선이 정반대에서 이 각도(도) 이내로 마주 볼 때만 도킹 후보가 된다.")]
    public float snapAngleToleranceDeg = 20f;

    [Tooltip("도킹 직전, 정육면체를 블록 면에 맞물리도록 면 법선 방향으로만 밀어 정렬한다(위치만, 회전 미변경).")]
    public bool snapAlignOnDock = true;

    [Tooltip("도킹 가능한 결합 구조물의 최대 블록 수. 초과하면 도킹을 거부한다.")]
    public int maxDockedBlocks = 6;

    [Tooltip("도킹 가능한 결합 구조물의 최대 총 질량. 초과하면 도킹을 거부한다.")]
    public float maxDockedMass = 12f;

    [Tooltip("도킹 / 해제 토글 키. (E=결합, V=스티커, C=블록 들기, F/G/T=실타래(ThreadPinPlacer 포함)가 " +
             "이미 씀, X=관성 축전기 방출 — 안 겹치는 H 사용)")]
    public KeyCode dockKey = KeyCode.H;

    [Tooltip("몇 초마다 씬에서 플레이어를 다시 훑어 도킹 컴포넌트를 보장할지(초).")]
    public float rescanInterval = 1f;

    private static CubeDockController instance;
    private float rescanTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Object.FindObjectOfType<CubeDockController>() != null) return;

        GameObject go = new GameObject("CubeDockController (auto)");
        go.AddComponent<CubeDockController>();
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
            PlayerCubeDock dock = mover.GetComponent<PlayerCubeDock>();
            if (dock == null)
                dock = mover.gameObject.AddComponent<PlayerCubeDock>();

            PushSettings(dock);
        }
    }

    private void PushSettings(PlayerCubeDock dock)
    {
        dock.dockRange = dockRange;
        dock.snapDistance = snapDistance;
        dock.snapAngleToleranceDeg = snapAngleToleranceDeg;
        dock.snapAlignOnDock = snapAlignOnDock;
        dock.maxDockedBlocks = maxDockedBlocks;
        dock.maxDockedMass = maxDockedMass;
        dock.dockKey = dockKey;
    }
}
