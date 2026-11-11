using UnityEngine;

/// <summary>
/// 일시정지 메뉴와 게임 입력 사이의 신호 한 쌍. 기믹이 `Input`을 읽는 자리는 22곳이라 timeScale=0만으로는
/// Update 기반 입력(점프·상호작용·Tab·R 등)이 계속 들어온다 — 그 자리들이 `InputGate.Blocked`를 보고 스스로 무시한다.
/// </summary>
public static class InputGate
{
    /// <summary>일시정지 메뉴가 열려 있는 동안 true. 게임 입력은 이 값이 true면 무시한다.</summary>
    public static bool Blocked { get; internal set; }

    /// <summary>터미널·CCTV처럼 자체 화면을 여는 쪽이 올리고 내린다. 0보다 크면 ESC는 그 화면이 먼저 쓴다.</summary>
    public static int PanelOpenCount { get; private set; }

    /// <summary>패널이 마지막으로 닫힌 프레임. 같은 프레임의 ESC로 패널이 닫힌 직후 메뉴가 같이 열리지 않게 한다.</summary>
    public static int PanelClosedFrame { get; private set; } = -1;

    public static void PanelOpened() => PanelOpenCount++;

    public static void PanelClosed()
    {
        PanelOpenCount = Mathf.Max(0, PanelOpenCount - 1);
        PanelClosedFrame = Time.frameCount;
    }

    // 도메인 리로드를 끈 에디터 플레이에서도 이전 플레이의 값이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Blocked = false;
        PanelOpenCount = 0;
        PanelClosedFrame = -1;
    }
}
