using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Master 씬(Scenes/Map4_Master.unity)에 하나. 시작 시 섹터 씬 8개를 additive 로드하고,
/// SectorController가 스스로 등록하는 것을 받아 레지스트리를 만든다(F1-2 — "씬 사이 직접 참조
/// 금지, 등록·이벤트로만").
/// </summary>
public class Map4Director : MonoBehaviour
{
    [Tooltip("로드할 섹터 씬 이름(Build Settings에 등록된 이름과 일치해야 한다). 비워두면 " +
             "'Map4_S1'..'Map4_S8'을 자동 생성한다.")]
    public string[] sectorSceneNames;

    private readonly Dictionary<int, SectorController> sectors = new Dictionary<int, SectorController>();

    void OnEnable()
    {
        SectorController.Registered += OnSectorRegistered;
        SectorController.Unregistered += OnSectorUnregistered;

        // [F1 재작업 판정 결정10/C-g] 에디터에서 "Open All Sectors"로 섹터를 미리 열어 둔 채
        // Play를 누르면, 그 섹터들의 SectorController.OnEnable(등록 이벤트 발행)은 이미 씬
        // 로드 시점에 끝나 있고 **이 Director는 아직 구독 전**이라 그 등록을 놓친다(이벤트는
        // 과거로 재생되지 않는다). 그래서 구독 직후 씬에 이미 존재하는 SectorController를
        // 직접 찾아 등록한다 — LoadAllSectors가 나중에 등록하는 것과 같은 코드 경로
        // (OnSectorRegistered)를 그대로 타므로 중복 걱정이 없다(Dictionary 키 덮어쓰기).
        foreach (SectorController s in Object.FindObjectsOfType<SectorController>())
            OnSectorRegistered(s);
    }

    void OnDisable()
    {
        SectorController.Registered -= OnSectorRegistered;
        SectorController.Unregistered -= OnSectorUnregistered;
    }

    private void OnSectorRegistered(SectorController s) => sectors[s.sectorId] = s;

    private void OnSectorUnregistered(SectorController s)
    {
        if (sectors.TryGetValue(s.sectorId, out SectorController cur) && cur == s)
            sectors.Remove(s.sectorId);
    }

    [Tooltip("[F1 재작업 판정 결정4] 로드 완료 후 도형 3개를 배치할 섹터 id.")]
    public int spawnSectorId = 1;

    /// <summary>[F1 재작업 판정 C4] Master 시작 → 섹터 additive 로드(비동기, 수백 ms~수 초) → 도형
    /// 배치까지 실행 순서를 명시한다:
    /// 1) Awake에서 씬에 이미 배치된 플레이어 3개(Master 씬 소속, 정식 Variant 프리팹 인스턴스)의
    ///    중력을 잠시 끈다 — 섹터 바닥이 아직 없는 동안 자유낙하해 킬라인(RespawnController.
    ///    killY) 밑으로 떨어지는 것을 막는다(구 밑면 1U 낙하 ≈0.45s, 로드가 그보다 오래 걸리면
    ///    실제로 발생했을 문제 — C4 지적).
    ///    [실측 09-28, 두 차례] 처음엔 isKinematic=true로 막았는데, (a) TeleportToSectorSpawn이
    ///    매 순간이동마다 velocity/angularVelocity를 0으로 씻어내는 것과, (b) 팀 PlayerMover.
    ///    FixedUpdate/LegacyVelocityFixedUpdate/DampWhenUncontrolled가 매 물리 스텝 velocity를
    ///    대입하는 것, 둘 다 "kinematic Rigidbody의 velocity는 대입 불가"(Unity 경고)에 걸렸다
    ///    (final_player_run.log·v2_playtest.log 실측 — 전자는 순서를 바꿔 피했지만 후자는 팀
    ///    코드라 순서를 바꿀 수 없다). useGravity=false는 Rigidbody를 다이내믹인 채로 두어(팀
    ///    PlayerMover가 매 프레임 하듯 velocity를 자유롭게 대입해도 무방) 중력만 끄므로 이
    ///    경고 자체가 발생할 여지가 없다 — 입력이 없으면(배치 재현 환경) 대입되는 값도 0이라
    ///    제자리에 뜬 채로 있는 결과는 동일하다.
    /// 2) Start 코루틴이 LoadAllSectors()를 완료까지 기다린다. SceneManager.LoadSceneAsync가
    ///    완료되는 시점(그 AsyncOperation의 yield가 끝나는 시점)에는 그 씬의 모든 루트 오브젝트가
    ///    이미 활성화되어 있고, MonoBehaviour의 OnEnable도 Unity 씬 활성화 절차의 일부로 동기
    ///    실행이 보장된다 — 그래서 각 섹터의 SectorController.OnEnable(등록)이 그 섹터의 로드
    ///    오퍼레이션이 끝나기 전에 이미 끝나 있다("섹터 등록 경쟁" 우려 해소, A 항목 대응).
    /// 3) 전부 로드된 뒤 SpawnPlayersAt(spawnSectorId, ...)으로 실제 배치 + 겹침 검사.
    /// 4) 배치가 끝난 뒤에만 중력을 되돌린다 — 배치 도중 물리가 개입해 위치가 어긋나는 것을
    ///    막는다.</summary>
    private readonly List<Rigidbody> masterPlayerBodies = new List<Rigidbody>();

    void Awake()
    {
        foreach (PlayerMover mover in Object.FindObjectsOfType<PlayerMover>())
        {
            Rigidbody rb = mover.GetComponent<Rigidbody>();
            if (rb == null) continue;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            masterPlayerBodies.Add(rb);
        }
    }

    void Start()
    {
        StartCoroutine(RunStartup());
    }

    private IEnumerator RunStartup()
    {
        yield return LoadAllSectors();

        bool ok = SpawnPlayersAt(spawnSectorId, masterPlayerBodies, out int overlapCount);
        if (!ok)
            Debug.LogError($"[Map4Director] 섹터 {spawnSectorId} 스폰 배치에서 겹침 {overlapCount}건 감지됐다.");
        else
            Debug.Log($"[Map4Director] 도형 {masterPlayerBodies.Count}개를 섹터 {spawnSectorId}에 배치 완료(겹침 0).");

        foreach (Rigidbody rb in masterPlayerBodies)
            if (rb != null) rb.useGravity = true;
    }

    public IEnumerator LoadAllSectors()
    {
        string[] names = (sectorSceneNames != null && sectorSceneNames.Length > 0)
            ? sectorSceneNames
            : DefaultNames();

        foreach (string sceneName in names)
        {
            bool alreadyLoaded = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).name == sceneName) { alreadyLoaded = true; break; }
            if (alreadyLoaded) continue;

            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[Map4Director] 섹터 씬 '{sceneName}'을 로드하지 못했다 — Build Settings에 " +
                                "등록됐는지 확인해라.");
                continue;
            }
            yield return op;
        }
    }

    private static string[] DefaultNames()
    {
        string[] names = new string[8];
        for (int i = 0; i < 8; i++) names[i] = $"Map4_S{i + 1}";
        return names;
    }

    public bool TryGetSector(int id, out SectorController sector) => sectors.TryGetValue(id, out sector);

    public int LoadedSectorCount => sectors.Count;

    /// <summary>F1-6 항목4 지원 — Rigidbody 하나를 지정 섹터의 스폰 슬롯(index)으로 순간이동시킨다.
    /// 슬롯이 없으면 섹터 루트 위 1U 지점을 쓴다(안전 폴백, 콘솔에 경고).</summary>
    public bool TeleportToSectorSpawn(Rigidbody rb, int sectorId, int slotIndex)
    {
        if (!TryGetSector(sectorId, out SectorController sector))
        {
            Debug.LogError($"[Map4Director] 섹터 {sectorId}가 로드되지 않았다.");
            return false;
        }

        Vector3 target;
        if (sector.spawnSlots != null && slotIndex < sector.spawnSlots.Length && sector.spawnSlots[slotIndex] != null)
        {
            target = sector.spawnSlots[slotIndex].position;
        }
        else
        {
            Debug.LogWarning($"[Map4Director] 섹터 {sectorId} 스폰 슬롯 {slotIndex}이 없어 루트 위 1U로 폴백한다.");
            target = sector.transform.position + Vector3.up * 1f;
        }

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = target;
        rb.transform.position = target;
        return true;
    }

    /// <summary>F1-4 마지막 문장 — 도형 3개를 섹터 스폰 슬롯 3곳에 배치하고 겹침을 검사한다.
    /// [F1 재작업 판정 A2] 도형끼리의 겹침뿐 아니라 **주변 지형(정적 콜라이더)과의 겹침도**
    /// 포함한다 — 슬롯 간격(2U)만으로 "자명하게 0"이던 이전 검사를 보강. 겹치면 false를 돌려주고
    /// 로그만 남긴다(강제 분리는 Lab_AntiStuck의 몫).</summary>
    public bool SpawnPlayersAt(int sectorId, IList<Rigidbody> playerBodies, out int overlapCount)
    {
        overlapCount = 0;
        if (!TryGetSector(sectorId, out SectorController sector))
        {
            Debug.LogError($"[Map4Director] 섹터 {sectorId}가 로드되지 않아 스폰할 수 없다.");
            return false;
        }

        for (int i = 0; i < playerBodies.Count; i++)
        {
            if (playerBodies[i] == null) continue;
            TeleportToSectorSpawn(playerBodies[i], sectorId, i);
        }

        Physics.SyncTransforms();

        for (int i = 0; i < playerBodies.Count; i++)
        {
            for (int j = i + 1; j < playerBodies.Count; j++)
            {
                if (playerBodies[i] == null || playerBodies[j] == null) continue;
                if (HasOverlap(playerBodies[i], playerBodies[j])) overlapCount++;
            }
        }

        for (int i = 0; i < playerBodies.Count; i++)
        {
            if (playerBodies[i] == null) continue;
            int terrainHits = CountTerrainOverlap(playerBodies[i]);
            if (terrainHits > 0)
            {
                Debug.LogError($"[Map4Director] '{playerBodies[i].name}' 스폰 위치가 주변 지형과 " +
                                $"{terrainHits}건 겹친다(섹터 {sectorId}).");
                overlapCount += terrainHits;
            }
        }

        return overlapCount == 0;
    }

    private static bool HasOverlap(Rigidbody a, Rigidbody b)
    {
        foreach (Collider ca in a.GetComponentsInChildren<Collider>())
        {
            if (ca.isTrigger) continue;
            foreach (Collider cb in b.GetComponentsInChildren<Collider>())
            {
                if (cb.isTrigger) continue;
                if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation,
                        cb, cb.transform.position, cb.transform.rotation, out _, out float dist)
                    && dist > 0.001f)
                    return true;
            }
        }
        return false;
    }

    /// <summary>[F1 재작업 판정 A2] 이 도형의 솔리드 콜라이더와, 근처의 "정적"(attachedRigidbody
    /// 없는) 콜라이더 사이 관통 건수. 섹터 바닥·벽처럼 스폰 슬롯 주변 지형과의 겹침을 잡는다.</summary>
    private static int CountTerrainOverlap(Rigidbody rb)
    {
        int hits = 0;
        Collider[] buffer = new Collider[16];
        foreach (Collider mine in rb.GetComponentsInChildren<Collider>())
        {
            if (mine.isTrigger) continue;
            float radius = mine.bounds.extents.magnitude + 0.5f;
            int count = Physics.OverlapSphereNonAlloc(mine.bounds.center, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = buffer[i];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody != null) continue; // 정적 지형만.
                if (other.transform.IsChildOf(rb.transform)) continue; // 자기 자신 제외.

                bool ok;
                try
                {
                    ok = Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float dist)
                        && dist > 0.001f;
                }
                catch { continue; }
                if (ok) hits++;
            }
        }
        return hits;
    }
}
