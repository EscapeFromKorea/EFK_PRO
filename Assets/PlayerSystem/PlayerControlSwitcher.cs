using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 하나 존재하며, Tab 키로 여러 PlayerMover 중 하나에만 조작권(IsControlled)을 부여한다.
/// 씬에 이 컴포넌트가 없으면 PlayerMover.IsControlled는 기본값(true)을 유지하므로
/// 기존 단일 플레이어 테스트 씬은 아무 영향을 받지 않는다.
/// 추후 멀티플레이(각 플레이어가 오브젝트 하나씩 고정 소유)로 확장할 때는
/// 이 클래스의 Tab 순환 로직만 "네트워크 플레이어 N -> 슬롯 N" 배정 로직으로 교체하면 된다.
/// </summary>
public class PlayerControlSwitcher : MonoBehaviour
{
    private static PlayerControlSwitcher instance;

    private readonly List<PlayerMover> players = new List<PlayerMover>();
    private int activeIndex = 0;

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
        if (players.Count <= 1) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            activeIndex = (activeIndex + 1) % players.Count;
            ApplyActiveIndex();
        }
    }

    private void ApplyActiveIndex()
    {
        for (int i = 0; i < players.Count; i++)
            players[i].IsControlled = (i == activeIndex);
    }

    public static void RegisterPlayer(PlayerMover mover)
    {
        if (instance == null) return;
        if (instance.players.Contains(mover)) return;

        instance.players.Add(mover);
        instance.ApplyActiveIndex();
    }

    public static void UnregisterPlayer(PlayerMover mover)
    {
        if (instance == null) return;

        instance.players.Remove(mover);
        if (instance.players.Count > 0)
        {
            instance.activeIndex = Mathf.Clamp(instance.activeIndex, 0, instance.players.Count - 1);
            instance.ApplyActiveIndex();
        }
    }
}
