using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 하나 존재하며, Tab 키로 여러 PlayerMover 중 하나에만 조작권(IsControlled)을 부여한다.
/// 씬에 이 컴포넌트가 없으면 PlayerMover.IsControlled는 기본값(true)을 유지하므로
/// 기존 단일 플레이어 테스트 씬은 아무 영향을 받지 않는다.
///
/// [순환 순서 = GameObject 이름 오름차순(ordinal)]
/// 등록 순서(FindObjectsOfType 반환 순서 등)는 씬을 다시 열 때마다 달라질 수 있어 순환 순서가
/// 비결정적이었다. 그래서 목록을 항상 GameObject 이름 기준 ordinal 정렬로 유지해 순서를
/// 결정적으로 만든다(예: Player_Cube -> Player_Sphere -> Player_Tetrahedron). 정렬로 리스트
/// 인덱스가 바뀌어도 "현재 조작 중인 플레이어"는 인덱스가 아니라 참조(activePlayer)로 추적하므로
/// 계속 같은 플레이어를 가리킨다.
///
/// 활성 플레이어가 바뀔 때마다 PlayerFollowCamera에도 새 타깃을 밀어준다(카메라가 없으면 무시).
///
/// 추후 멀티플레이(각 플레이어가 오브젝트 하나씩 고정 소유)로 확장할 때는 이 클래스의 Tab 순환
/// 로직만 "네트워크 플레이어 N -> 슬롯 N" 배정 로직으로 교체하면 된다.
/// </summary>
public class PlayerControlSwitcher : MonoBehaviour
{
    private static PlayerControlSwitcher instance;

    private readonly List<PlayerMover> players = new List<PlayerMover>();

    // 조작 중인 플레이어를 인덱스가 아닌 참조로 추적한다 — 이름 정렬로 리스트 순서가 바뀌어도
    // 같은 플레이어를 계속 활성으로 유지하기 위함.
    private PlayerMover activePlayer;

    /// <summary>현재 활성 플레이어의 Transform(팔로우 카메라가 부착 순서와 무관하게 당겨올 수 있도록 노출).</summary>
    public static Transform ActiveTarget =>
        (instance != null && instance.activePlayer != null) ? instance.activePlayer.transform : null;

    void Awake()
    {
        instance = this;

        // PlayerMover.OnEnable()에서 스스로 등록하지만, Unity는 서로 다른 오브젝트 간
        // Awake/OnEnable 실행 순서를 보장하지 않는다. 이 Awake가 나중에 실행되면 이미
        // 지나간 PlayerMover.OnEnable()의 등록 시도가 instance==null이라 무시되어 아무도
        // 등록되지 않는 문제가 생긴다. 그래서 씬에 이미 존재하는 PlayerMover를 여기서
        // 직접 찾아 등록해 순서와 무관하게 항상 동작하도록 한다.
        foreach (PlayerMover mover in Object.FindObjectsOfType<PlayerMover>())
            RegisterPlayer(mover);
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Tab)) return;

        SortRoster();
        if (players.Count <= 1) return;

        // 정렬된 순서 기준으로 다음 플레이어로 순환한다.
        int idx = players.IndexOf(activePlayer);
        if (idx < 0) idx = 0;
        activePlayer = players[(idx + 1) % players.Count];

        ApplyActive();
    }

    // 죽은 참조를 정리하고 이름 오름차순(ordinal)으로 정렬한다. 등록/삭제/전환 등 목록이 바뀌는
    // 모든 시점에서 호출해 순서를 항상 결정적으로 유지한다.
    private void SortRoster()
    {
        players.RemoveAll(m => m == null);
        players.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
    }

    private void ApplyActive()
    {
        if (players.Count == 0)
        {
            activePlayer = null;
            NotifyCamera();
            return;
        }

        // 활성 플레이어가 없거나(초기) 사라졌으면 이름 순 첫 번째를 활성으로 삼는다.
        if (activePlayer == null || !players.Contains(activePlayer))
            activePlayer = players[0];

        foreach (PlayerMover mover in players)
            mover.SetControlled(mover == activePlayer);

        NotifyCamera();
    }

    private void NotifyCamera()
    {
        PlayerFollowCamera.SetActiveTarget(activePlayer != null ? activePlayer.transform : null);
    }

    public static void RegisterPlayer(PlayerMover mover)
    {
        if (instance == null) return;
        if (mover == null) return;
        if (instance.players.Contains(mover)) return;

        instance.players.Add(mover);
        instance.SortRoster();
        instance.ApplyActive();
    }

    public static void UnregisterPlayer(PlayerMover mover)
    {
        if (instance == null) return;
        if (!instance.players.Remove(mover)) return;

        // 정렬/재적용은 SortRoster+ApplyActive가 참조(activePlayer) 기준으로 처리하므로,
        // 인덱스를 수동으로 보정할 필요가 없다. 활성 플레이어가 빠졌으면 ApplyActive가
        // 이름 순 첫 번째로 자동 대체한다.
        instance.SortRoster();
        instance.ApplyActive();
    }
}
