using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 리스폰(공용 안전망) 컨트롤러 — 씬에 하나. 낙사·소프트락을 "실패 처리"가 아니라 흐름을 끊지 않는
/// 복귀로 되돌린다. 두 가지 발동을 처리한다:
///
/// - <b>자동(낙하 리스폰)</b>: 킬 라인(killY) 아래에 outOfBoundsSeconds 이상 머무르면, 체크포인트
///   구역 상단으로 순간이동시킨 뒤 중력으로 자연 낙하시킨다. 암전도 페이드도 없어 화면상으로는
///   "하늘에서 다시 떨어져 내려온 것"으로 보인다. 장외 판정을 트리거 볼륨이 아니라 <b>높이 한 줄</b>로
///   두는 이유: 볼륨은 맵이 넓어질 때마다 늘려야 하고 빠뜨린 빈틈에 떨어진 플레이어는 영영
///   리스폰되지 않는다(구멍 난 안전망은 없는 것보다 나쁘다). 대신 맵 <b>옆으로</b> 튕겨나가 같은
///   높이에 뜬 경우는 못 잡는다 — 그건 R이 담당한다.
/// - <b>수동(페이드 리스폰)</b>: R 한 번으로 조작 중인 도형이 그 자리에서 흐려져 사라지고
///   체크포인트 바닥 위에서 나타난다. 대기 시간이 없는 이유는 수동 입력 자체가 확정 의사표시라서다.
///   지형에 끼거나 되돌릴 수 없는 협동 배치를 만들었을 때의 소프트락 방지 장치이기도 하다.
///
/// 카운터와 체크포인트를 이 컴포넌트가 소유한다. 씬에 하나뿐이므로 "세 도형 공유"가 구조적으로
/// 보장되고(도형별 카운터를 합산하는 구조를 만들지 않는다), 이 게임의 실패는 개인이 아니라 팀
/// 단위라는 전제와도 맞는다.
///
/// [플레이어에 상시 컴포넌트를 심지 않는다]
/// 리스폰 수신자를 플레이어에 붙이려면 PlayerSystem의 생성 메뉴를 고쳐야 해 교차 폴더 하드룰에
/// 걸린다. DreamThreadController가 검증한 회피 구조(씬 컨트롤러가 읽기 + 런타임 플래그 토글만)를
/// 그대로 따른다. 여기서는 카운터·체크포인트가 원래 공유 상태라 컨트롤러 소유가 더 자연스럽다.
///
/// [조작 차단 = PlayerMover.ExternallyDriven]
/// mover.enabled를 끄면 PlayerControlSwitcher 로스터에서 빠져 Tab 순환이 깨진다(실타래 Phase 1에서
/// 이미 치른 부채). 점프는 따로 막지 않는다 — 낙하 중엔 비접지, 페이드 중엔 isKinematic이라
/// 구조적으로 성립하지 않는다. 유일하게 뚫리던 경로는 부스트라 순간이동 시 CancelBoost()를 부른다.
/// </summary>
public class RespawnController : MonoBehaviour
{
    [Header("킬 라인 (장외 판정 — 항상 켜져 있는 바닥)")]
    [Tooltip("이 높이 아래에 머무르면 장외로 본다. 맵 최저 지형보다 충분히 아래(권장 -30 이하)에 " +
             "둬라 — 라인이 지형에 가까우면 낮은 발판에서 잠깐 벗어난 것만으로 체류 카운트가 시작된다. " +
             "씬 뷰에 붉은 기즈모 평면으로 그려진다. 맵 옆으로 튕겨나가는 경우처럼 이 라인이 못 잡는 " +
             "구역은 OutOfBoundsVolume(Tools > Respawn > Create Respawn Scale)을 따로 놓아 더한다 — " +
             "볼륨은 라인을 대체하지 않는다(빠뜨린 빈틈이 곧 영구 미복귀라, 무조건 덮는 바닥이 있어야 한다).")]
    public float killY = -30f;

    [Tooltip("장외(킬 라인 아래 또는 장외 볼륨 안)에 이 시간(초) 이상 머무르면 낙하 리스폰이 " +
             "발동한다. 즉시 되돌리면 아슬아슬한 점프가 성공 직전에 취소되고, 너무 길면 허공에서 기다린다.")]
    public float outOfBoundsSeconds = 3f;

    [Tooltip("씬 뷰에 그릴 킬 라인 평면의 한 변 길이(Unit). 표시 전용 — 판정은 무한 평면이다.")]
    public float killLineGizmoSize = 100f;

    [Header("낙하 리스폰 (자동)")]
    [Tooltip("착지를 못 잡았을 때(체크포인트 바닥이 사라진 경우 등) 조작을 강제로 되돌려 주는 시간(초). " +
             "이 안전망이 없으면 조작 불능으로 영구히 남는다.")]
    public float landingTimeoutSeconds = 5f;

    [Header("페이드 리스폰 (수동 R)")]
    [Tooltip("사라지는 데 걸리는 시간(초).")]
    public float fadeOutSeconds = 0.35f;

    [Tooltip("나타나는 데 걸리는 시간(초). '복귀'가 '퇴장'보다 눈에 남아야 해서 일부러 더 길다.")]
    public float fadeInSeconds = 0.6f;

    [Tooltip("페이드 스폰 시 콜라이더 밑면과 바닥 사이 간격(Unit). 0이면 콜라이더가 바닥에 파묻혀 " +
             "PhysX가 밀어내며 튕긴다. 도형 크기가 아니라 '바닥과의 틈'이라 스케일이 커져도 이 값 그대로다.")]
    public float groundClearance = 0.05f;

    /// <summary>세 도형이 공유하는 총 리스폰 횟수(팀 단위 스코어). 도형별·사유별 집계는 만들지
    /// 않는다 — 쓸 곳이 정해지기 전에 만드는 통계는 죽은 코드가 된다(디버그 로그로만 남긴다).
    /// 세션 간 저장도 하지 않는다(그건 세이브 시스템의 일이다).</summary>
    public int RespawnCount { get; private set; }

    private static RespawnController instance;

    // 체크포인트는 도형별이 아니라 마지막에 갱신된 하나만 공유한다. 좌표로 들고 있어서 구역이
    // 파괴/비활성화돼도 복귀 지점이 살아 있다.
    private RespawnZone currentZone;
    private bool hasCheckpoint;
    private Vector3 dropPoint;
    private Vector3 groundPoint;
    private bool warnedNoCheckpoint;

    private readonly List<PlayerMover> players = new List<PlayerMover>();
    private readonly List<PlayerMover> pruneBuffer = new List<PlayerMover>();
    private float nextRosterRefresh;

    // 플레이어별 "킬 라인 아래로 내려간 시각". 라인 위로 돌아오면 지운다. 좌표 비교라 플레이어의
    // 콜라이더가 둘(Player_Mesh/Player_Collider)이어서 생기는 Enter/Exit 중복 문제가 아예 없다.
    private readonly Dictionary<PlayerMover, float> belowSince = new Dictionary<PlayerMover, float>();

    // 연출이 진행 중인 플레이어. 연출 중 재입력(R 연타)과 중복 발동을 막는다.
    private readonly HashSet<PlayerMover> busy = new HashSet<PlayerMover>();

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        if (instance != null && instance != this)
            Debug.LogWarning("[Respawn] RespawnController가 씬에 둘 이상이다 — 마지막 하나만 " +
                             "체크포인트를 받는다. 하나만 남겨라.", this);
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnDisable()
    {
        // 연출 도중 컨트롤러가 꺼지면 코루틴이 멈춰 ExternallyDriven/isKinematic이 켜진 채로 남아
        // 영구 조작 불능이 된다. 진행 중이던 대상만 풀어 준다(리스폰은 붙잡히지 않은 플레이어에게만
        // 시작하므로, 여기서의 복원값은 항상 '풀린 상태'가 맞다).
        StopAllCoroutines();
        foreach (PlayerMover mover in busy)
        {
            if (mover == null) continue;
            mover.ExternallyDriven = false;
            Rigidbody rb = mover.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
        }
        busy.Clear();
    }

    /// <summary>RespawnZone이 플레이어 진입 시 호출한다. 컨트롤러가 없으면 저장되지 않는다.</summary>
    public static void SetCheckpoint(RespawnZone zone)
    {
        if (zone == null) return;
        if (instance == null)
        {
            Debug.LogWarning("[Respawn] 씬에 RespawnController가 없어 체크포인트를 저장하지 못했다.", zone);
            return;
        }
        instance.StoreCheckpoint(zone);
    }

    private void StoreCheckpoint(RespawnZone zone)
    {
        // 같은 구역의 재진입(콜라이더 둘, 경계에서의 반복 Enter)은 무시한다 — 바닥 레이를 다시
        // 쏘지 않기 위한 게이트이기도 하다.
        if (currentZone == zone) return;

        // 초록 깃발은 씬에서 정확히 하나 — 직전 체크포인트는 "지나온 경로"인 흰 깃발로 내린다.
        // 잡은 곳을 전부 초록으로 두면 실제 저장된 체크포인트는 하나뿐인데 여러 개가 켜져 있어
        // UI가 거짓말을 한다.
        if (currentZone != null) currentZone.SetFlagState(RespawnZone.FlagState.Visited);
        zone.SetFlagState(RespawnZone.FlagState.Active);

        currentZone = zone;
        dropPoint = zone.DropSpawnPoint;
        groundPoint = zone.FindGroundPoint();
        hasCheckpoint = true;
        warnedNoCheckpoint = false;
        Debug.Log($"[Respawn] 체크포인트 갱신: '{zone.name}' (낙하 {dropPoint}, 페이드 바닥 {groundPoint})");
    }

    /// <summary>외부 기믹이 리스폰을 강제 발동하는 진입점(낙석 피격 등). 인자는 맞은 플레이어의 Root.
    ///
    /// FallingRockSpawner의 PlayerHitEvent가 UnityEvent&lt;GameObject&gt;라, 인스펙터에서 <b>동적 인자</b>로
    /// 꽂으려면 시그니처가 정확히 void(GameObject)여야 한다 — 이름이나 인자를 바꾸면 이미 해둔 배선이
    /// 조용히 끊긴다(정적 인자로 떨어져 항상 같은 오브젝트를 리스폰한다).</summary>
    public void RespawnPlayer(GameObject playerRoot)
    {
        if (playerRoot == null) return;

        PlayerMover mover = playerRoot.GetComponentInParent<PlayerMover>();
        if (mover == null)
        {
            Debug.LogWarning($"[Respawn] '{playerRoot.name}'에서 PlayerMover를 못 찾아 리스폰을 건너뛴다 " +
                             "(인자는 플레이어 Root여야 한다).", playerRoot);
            return;
        }
        TryRespawn(mover, useFade: true, reason: "외부 호출");
    }

    private void Update()
    {
        RefreshRoster();

        if (Input.GetKeyDown(KeyCode.R))
        {
            PlayerMover mover = ControlledPlayer();
            if (mover != null) TryRespawn(mover, useFade: true, reason: "수동 R");
        }

        CheckOutOfBounds();
    }

    // ponytail: 1초에 한 번 씬을 다시 훑는다. 플레이어는 씬에 미리 배치되는 3개뿐이라 이 주기로
    // 충분하고, FindObjectsOfType를 매 프레임 돌리는 건 비싸다. 런타임 생성이 잦아지면 PlayerMover가
    // 스스로 등록하는 방식으로 바꾼다(= PlayerSystem 수정이므로 지금은 하지 않는다).
    private void RefreshRoster()
    {
        if (Time.unscaledTime < nextRosterRefresh) return;
        nextRosterRefresh = Time.unscaledTime + 1f;

        players.Clear();
        players.AddRange(Object.FindObjectsOfType<PlayerMover>());

        // 파괴된 플레이어가 남긴 항목 정리.
        pruneBuffer.Clear();
        foreach (KeyValuePair<PlayerMover, float> entry in belowSince)
            if (entry.Key == null) pruneBuffer.Add(entry.Key);
        foreach (PlayerMover dead in pruneBuffer)
            belowSince.Remove(dead);
        busy.RemoveWhere(m => m == null);
    }

    private PlayerMover ControlledPlayer()
    {
        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active != null)
        {
            PlayerMover m = active.GetComponent<PlayerMover>();
            if (m != null) return m;
        }

        // 스위처가 없는 단일 플레이어 테스트 씬 폴백 — IsControlled 기본값이 true다.
        foreach (PlayerMover m in players)
            if (m != null && m.IsControlled) return m;

        return null;
    }

    /// <summary>장외 = 킬 라인 아래 <b>또는</b> 장외 볼륨(OutOfBoundsVolume) 안. 둘은 대체 관계가
    /// 아니라 합집합이다 — 킬 라인이 맵 아래를 무조건 덮어 "빈틈에 떨어져 영영 안 돌아오는" 사고를
    /// 막고, 볼륨은 킬 라인이 못 잡는 곳(맵 옆으로 튕겨나가 같은 높이에 뜬 경우 등)을 골라 덮는다.
    /// 볼륨이 하나도 없으면 AnyContains는 즉시 false라 비용이 0이다.</summary>
    private bool IsOutOfBounds(PlayerMover mover)
    {
        Vector3 p = mover.transform.position;
        return p.y < killY || OutOfBoundsVolume.AnyContains(p);
    }

    private void CheckOutOfBounds()
    {
        foreach (PlayerMover mover in players)
        {
            if (mover == null) continue;

            if (!IsOutOfBounds(mover))
            {
                belowSince.Remove(mover);
                continue;
            }

            if (!belowSince.TryGetValue(mover, out float since))
            {
                belowSince[mover] = Time.time;
                continue;
            }
            if (Time.time - since < outOfBoundsSeconds) continue;
            if (busy.Contains(mover)) continue;

            // 붙잡힌 플레이어는 리스폰을 건너뛰는 게 아니라 미룬다 — 체류 타이머를 그대로 두고
            // 풀리는 즉시 실행한다. 건너뛰면 실타래 발사(놓은 뒤 착지까지 최대 6초 ExternallyDriven)로
            // 장외로 날아간 플레이어가 6초 + 재계측 3초를 허공에서 기다린다.
            if (IsHeld(mover)) continue;

            // 실패(체크포인트 없음)하면 타이머를 살려둬, 체크포인트가 생기는 즉시 복귀시킨다.
            if (TryRespawn(mover, useFade: false, reason: "장외"))
                belowSince.Remove(mover);
        }
    }

    private bool TryRespawn(PlayerMover mover, bool useFade, string reason)
    {
        if (mover == null) return false;
        if (busy.Contains(mover)) return false; // 연출 중 재입력 무시

        if (!hasCheckpoint)
        {
            if (!warnedNoCheckpoint)
            {
                warnedNoCheckpoint = true;
                Debug.LogWarning("[Respawn] 아직 지나온 체크포인트(RespawnZone)가 없어 되돌릴 곳이 없다. " +
                                 "시작 지점에 구역을 하나 놓아라 — 체크포인트가 생기면 즉시 복귀한다.", this);
            }
            return false;
        }

        // 조인트/키네마틱으로 붙잡힌 바디를 순간이동시키면 기믹 상태가 꼬인다. 수동·외부 발동은
        // 미루지 않고 거절한다 — R은 "지금" 되돌리라는 의사표시라, 몇 초 뒤 갑자기 발동하면 더 나쁘다.
        if (IsHeld(mover))
        {
            Debug.Log($"[Respawn] '{ShapeLabel(mover)}'는 다른 기믹이 붙잡고 있어 리스폰하지 않는다 " +
                      "(매달림은 F, 벽 부착은 F/점프로 먼저 푼 뒤 다시 시도해라).");
            return false;
        }

        StartCoroutine(RespawnRoutine(mover, useFade, reason));
        return true;
    }

    /// <summary>"다른 기믹이 이 바디를 붙잡고 있는가" — 저장소에 소유 신호가 하나가 아니다.
    /// 실타래 매달림은 ExternallyDriven을 세우지만, 세모 벽 부착(ThreadPinPlacer)은 로스터 churn을
    /// 피하려고 <b>isKinematic으로만</b> 고정한다. 플래그만 보면 벽에 붙은 세모가 R을 눌렀을 때
    /// 키네마틱인 채 체크포인트 공중에 정지하고, 배치기는 여전히 부착 중이라 믿어 완전 소프트락이 된다.</summary>
    private static bool IsHeld(PlayerMover mover)
    {
        if (mover.ExternallyDriven) return true;
        Rigidbody rb = mover.GetComponent<Rigidbody>();
        return rb != null && rb.isKinematic;
    }

    private IEnumerator RespawnRoutine(PlayerMover mover, bool useFade, string reason)
    {
        Rigidbody rb = mover.GetComponent<Rigidbody>();
        busy.Add(mover);

        // 플래그는 "내가 켠 경우에만" 되돌린다(진입 시 값 저장 → 종료 시 복원). ExternallyDriven을
        // 실타래와 공유하는데 소유권 카운팅이 없어, 무조건 false로 되돌리면 남의 소유를 뺏는다.
        bool prevDriven = mover.ExternallyDriven;
        bool prevKinematic = rb != null && rb.isKinematic;
        mover.ExternallyDriven = true;

        RespawnCount++;
        Debug.Log($"[Respawn] '{ShapeLabel(mover)}' 리스폰 — {reason} / {(useFade ? "페이드" : "낙하")}, " +
                  $"팀 누적 {RespawnCount}회");

        List<FadeMaterial> fadeMats = null;
        if (useFade)
        {
            // 반투명한 채 미끄러지거나 바닥 5cm 위에서 굴러 내려가면 연출이 깨진다. 완전히 나타난
            // 순간에 물리를 되살린다.
            if (rb != null) rb.isKinematic = true;
            fadeMats = BeginFade(mover.transform);
            yield return FadeAlpha(fadeMats, 1f, 0f, fadeOutSeconds);
        }

        if (mover == null || rb == null)
        {
            Cleanup(mover, fadeMats);
            yield break;
        }

        yield return TeleportRoutine(mover, rb, useFade ? FadeSpawnPosition(mover) : dropPoint);

        if (mover == null || rb == null)
        {
            Cleanup(mover, fadeMats);
            yield break;
        }

        if (useFade)
        {
            yield return FadeAlpha(fadeMats, 0f, 1f, fadeInSeconds);
            EndFade(fadeMats);

            if (rb != null)
            {
                rb.isKinematic = prevKinematic;
                // 키네마틱 동안 대입한 속도는 반영되지 않는다 — 다이내믹으로 돌아온 직후 다시 지운다.
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            // 낙하 중 조작을 열어 두면 반사적으로 방향키를 눌러 복귀 지점 밖으로 튕겨나가고 그대로
            // 다시 장외 → 리스폰 루프가 된다. 그래서 착지까지 막아 둔다.
            yield return WaitForLanding(mover);
        }

        if (mover != null) mover.ExternallyDriven = prevDriven;
        busy.Remove(mover);
    }

    // 대상이 연출 도중 파괴된 경로. 머티리얼은 이미 사라졌을 수 있으니 살아 있는 것만 되돌린다.
    private void Cleanup(PlayerMover mover, List<FadeMaterial> fadeMats)
    {
        EndFade(fadeMats);
        busy.Remove(mover);
    }

    private IEnumerator TeleportRoutine(PlayerMover mover, Rigidbody rb, Vector3 position)
    {
        // 세 도형 모두 Interpolate라, 보간된 바디를 순간이동시키면 한 프레임 동안 이전 위치에서 새
        // 위치로 길게 늘어나 보인다. "암전 없이 자연스럽게"가 이 시스템의 전부인데 정확히 그 한
        // 프레임이 깨지므로, 물리 스텝 하나가 지나 보간 버퍼가 새 위치로 갱신될 때까지 꺼 둔다.
        RigidbodyInterpolation prevInterpolation = rb.interpolation;
        rb.interpolation = RigidbodyInterpolation.None;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;                  // 물리 쪽 위치
        mover.transform.position = position;     // 렌더/즉시 조회 쪽 위치

        // 속도 0만으로는 부족하다: 부스트 중이면 PlayerAccelReceiver가 매 FixedUpdate에 velocity를
        // 통째 대입해 다음 물리 스텝에 되살아난다(가속 발판을 밟고 장외로 날아간 경우가 정확히 이
        // 상황이다). enabled 토글로는 state/holdTimer가 남아 소용없다.
        PlayerAccelReceiver accel = mover.GetComponent<PlayerAccelReceiver>();
        if (accel != null) accel.CancelBoost();

        // 버블·감속 구역 안에서 리스폰을 발동하면 배율이 걸린 채 밖으로 나간다. 특히 페이드 중에는
        // isKinematic이라 PlayerGravityOverride.FixedUpdate가 즉시 return해 배율이 얼어붙는다.
        PlayerGravityOverride gravity = mover.GetComponent<PlayerGravityOverride>();
        if (gravity != null) gravity.RestoreDefault(0f);

        yield return new WaitForFixedUpdate();
        if (rb != null) rb.interpolation = prevInterpolation;
    }

    /// <summary>페이드 스폰 좌표 — 체크포인트 바닥 위로 "콜라이더 밑면이 groundClearance만큼 뜨는" 높이.
    /// 요구사항의 "바닥 + 5cm"는 스케일 1 전제라, ScalingSystem으로 커진 도형은 5cm가 몸 절반보다
    /// 낮아 바닥에 파묻힌다. 현재 콜라이더 바운즈로 피벗~발바닥 거리를 재서 스케일과 무관하게 세운다.</summary>
    private Vector3 FadeSpawnPosition(PlayerMover mover)
    {
        float pivotToBottom = 0f;
        if (TryGetSolidBounds(mover.transform, out Bounds body))
            pivotToBottom = Mathf.Max(0f, mover.transform.position.y - body.min.y);

        return new Vector3(groundPoint.x, groundPoint.y + pivotToBottom + groundClearance, groundPoint.z);
    }

    // 트리거 콜라이더(Player_Mesh)는 시각 메쉬 크기라 실제로 바닥에 닿는 면이 아니다 — 솔리드
    // 콜라이더만 합쳐 몸의 아랫면을 잡는다(정사면체처럼 콜라이더가 여러 개인 도형도 포함된다).
    private static bool TryGetSolidBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Collider col in root.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger) continue;
            if (!any) { bounds = col.bounds; any = true; }
            else bounds.Encapsulate(col.bounds);
        }
        return any;
    }

    /// <summary>착지까지 기다린다. 공중에서 조작을 미리 풀면 리스폰 루프가 재현되고, 착지보다 늦게
    /// 풀면 조작이 씹힌 것처럼 느껴진다.
    ///
    /// 접지 창구는 Root의 PlayerShapeController.IsGrounded()다 — PlayerGroundContact는 Root가 아니라
    /// Player_Collider 자식에 붙어 있어 Root에서 GetComponent하면 null이다(DreamThreadController가
    /// 쓰는 것과 같은 창구). 둘 다 없으면 타임아웃이 마지막 안전망이다.</summary>
    private IEnumerator WaitForLanding(PlayerMover mover)
    {
        PlayerShapeController shape = mover.GetComponent<PlayerShapeController>();
        PlayerGroundContact contact = shape == null ? mover.GetComponentInChildren<PlayerGroundContact>() : null;

        // 접지 판정에는 유예 창(PlayerGroundContact.groundedGraceTime, 기본 0.1초)이 있어, 킬 라인
        // 아래 바닥에 앉아 있다 되돌아온 경우 순간이동 직후에도 몇 프레임은 접지로 읽힌다. 그 창을
        // 지나 보내고 나서 착지를 재기 시작한다.
        yield return new WaitForSeconds(0.15f);

        float deadline = Time.time + landingTimeoutSeconds;
        while (Time.time < deadline)
        {
            if (mover == null) yield break;
            if (shape != null ? shape.IsGrounded() : (contact != null && contact.IsGrounded)) yield break;
            yield return null;
        }

        if (mover != null)
            Debug.Log($"[Respawn] '{ShapeLabel(mover)}' 착지를 못 잡아 {landingTimeoutSeconds}초 타임아웃으로 " +
                      "조작을 되돌린다(체크포인트 아래에 바닥이 있는지 확인해라).");
    }

    // ── 페이드 ────────────────────────────────────────────────────────────────
    // renderer.material(인스턴스)의 알파를 직접 보간한다. MaterialPropertyBlock을 쓰지 않는 이유:
    // 블렌드 모드(Opaque→Transparent)는 프로퍼티 블록으로 못 바꿔 어차피 인스턴스가 생기고, 인스턴스가
    // 생긴 순간 MPB는 순수 중복이다(무지개 다리가 MPB를 쓰는 건 에디터 로더가 에셋을 미리 Transparent로
    // 만들어 둬 인스턴스화를 피하기 때문인데, 플레이어는 그 전제가 없다).
    // 프로퍼티는 _Color 하나만 본다 — 이 프로젝트는 Built-in RP이고 _BaseColor(URP/HDRP) 대응은 죽은 코드다.

    private struct FadeMaterial
    {
        public Material mat;
        public Color baseColor;
    }

    private List<FadeMaterial> BeginFade(Transform root)
    {
        List<FadeMaterial> list = new List<FadeMaterial>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            // [인스턴스 수명] renderer.materials 접근은 공유 에셋(구·네모는 Unity 내장 Default-Material을
            // 공유한다)의 사본을 만든다 — 에셋 원본은 오염되지 않고, 이 저장소에서 반복 재발한 머티리얼
            // 드리프트도 애초에 생기지 않는다. 사본은 렌더러가 소유해 렌더러와 함께 파괴되고, 두 번째
            // 리스폰부터는 같은 사본이 재사용된다(호출할 때마다 늘어나지 않는다). 그래서 우리가 Material을
            // 만들지도 Destroy하지도 않는다 — 우리가 지는 책임은 연출이 끝나면 Opaque로 되돌리는 것뿐이다.
            // 대가는 이 렌더러가 배칭에서 빠지는 것 하나이고, 그건 플레이어 3개짜리 씬에서 무해하다.
            foreach (Material m in r.materials)
            {
                if (m == null || !m.HasProperty(ColorId)) continue;
                list.Add(new FadeMaterial { mat = m, baseColor = m.GetColor(ColorId) });
                SetTransparent(m);
            }
        }
        return list;
    }

    private void EndFade(List<FadeMaterial> mats)
    {
        if (mats == null) return;
        foreach (FadeMaterial fm in mats)
        {
            if (fm.mat == null) continue;
            fm.mat.SetColor(ColorId, fm.baseColor);
            SetOpaque(fm.mat);
        }
        mats.Clear();
    }

    private IEnumerator FadeAlpha(List<FadeMaterial> mats, float from, float to, float duration)
    {
        if (mats == null || mats.Count == 0) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ApplyAlpha(mats, Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        ApplyAlpha(mats, to);
    }

    private static void ApplyAlpha(List<FadeMaterial> mats, float alpha)
    {
        foreach (FadeMaterial fm in mats)
        {
            if (fm.mat == null) continue;
            Color c = fm.baseColor;
            c.a = alpha;
            fm.mat.SetColor(ColorId, c);
        }
    }

    // Built-in Standard에서 Opaque↔Transparent 전환은 한 줄이 아니라 여섯 세팅이다(무지개 다리
    // 에디터 로더가 하는 일을 런타임에 옮긴 것). 이 비용을 알고 쓴다.
    private static void SetTransparent(Material m)
    {
        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    // 연출이 끝나면 반드시 되돌린다 — 반투명(ZWrite off)으로 남으면 정렬 아티팩트가 난다
    // (구름 트램펄린이 겪고 _ZWrite=1로 해결한 그 문제다). 원래 블렌드 상태를 저장했다 복원하지 않고
    // Opaque로 못 박는 이유는 플레이어 머티리얼이 전부 Opaque이기 때문이다(구·네모는 내장
    // Default-Material, 세모는 씬 Standard). 반투명 플레이어 머티리얼을 쓰게 되면 그때 상태를 저장해라.
    private static void SetOpaque(Material m)
    {
        m.SetFloat("_Mode", 0f);
        m.SetInt("_SrcBlend", (int)BlendMode.One);
        m.SetInt("_DstBlend", (int)BlendMode.Zero);
        m.SetInt("_ZWrite", 1);
        m.DisableKeyword("_ALPHABLEND_ON");
        m.renderQueue = -1; // 셰이더 기본 큐로 복귀
    }

    private static string ShapeLabel(PlayerMover mover)
    {
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        return identity != null ? identity.Kind.ToString() : mover.gameObject.name;
    }

    // 임시 표시. 이 저장소에는 UI 시스템(Canvas/uGUI/TMP)이 아직 없어, 정식 HUD를 여기서 신설하면
    // 리스폰 작업이 UI 시스템 작업이 된다. 씬 세팅이 0인 라벨 한 줄로 두고, 정식 HUD가 생기면 이
    // 메서드만 지우고 RespawnCount를 읽어 가면 된다. 내장 GUI 폰트에 한글 글리프가 없어 영문이다.
    private void OnGUI()
    {
        GUI.Label(new Rect(12f, 12f, 220f, 22f), $"Respawns: {RespawnCount}");
    }

    private void OnDrawGizmos()
    {
        Vector3 center = new Vector3(transform.position.x, killY, transform.position.z);
        Vector3 size = new Vector3(killLineGizmoSize, 0.01f, killLineGizmoSize);

        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.12f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.7f);
        Gizmos.DrawWireCube(center, size);

        if (!hasCheckpoint) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(dropPoint, 0.4f);
        Gizmos.DrawLine(dropPoint, groundPoint);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundPoint, 0.3f);
    }
}
