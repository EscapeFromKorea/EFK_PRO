using System;

public enum ToyWorldRepairItemType
{
    WindUpSpring = 0,
    PowerGear = 1,
    MelodyCylinder = 2
}

public interface IPuzzleResettable
{
    void CaptureResetState();
    void ResetPuzzleObject();
}

/// <summary>
/// Unity 오브젝트와 무관한 맵 진행 상태. Director와 Editor 검증기가 같은 규칙을 사용한다.
/// 비트 마스크를 써서 같은 아이템의 중복 획득/설치가 카운트를 늘릴 수 없다.
/// </summary>
[Serializable]
public sealed class ToyWorldProgressState
{
    private const int AllItemsMask = (1 << 3) - 1;

    private int collectedMask;
    private int installedMask;
    private bool musicBoxActivated;
    private bool levelCompleted;

    public int CollectedCount => CountBits(collectedMask);
    public int InstalledCount => CountBits(installedMask);
    public bool AllCollected => collectedMask == AllItemsMask;
    public bool AllInstalled => installedMask == AllItemsMask;
    public bool MusicBoxActivated => musicBoxActivated;
    public bool LevelCompleted => levelCompleted;
    public bool CanExit => AllCollected && AllInstalled && musicBoxActivated;

    public bool IsCollected(ToyWorldRepairItemType type) =>
        (collectedMask & Bit(type)) != 0;

    public bool IsInstalled(ToyWorldRepairItemType type) =>
        (installedMask & Bit(type)) != 0;

    public bool TryCollect(ToyWorldRepairItemType type)
    {
        int bit = Bit(type);
        if ((collectedMask & bit) != 0) return false;
        collectedMask |= bit;
        return true;
    }

    /// <summary>명세의 설치 순서(스프링 → 기어 → 실린더)를 강제한다.</summary>
    public bool TryInstall(ToyWorldRepairItemType type)
    {
        int index = (int)type;
        int bit = Bit(type);
        if (!AllCollected || (collectedMask & bit) == 0 || (installedMask & bit) != 0)
            return false;
        if (index != InstalledCount) return false;

        installedMask |= bit;
        return true;
    }

    public bool TryActivateMusicBox()
    {
        if (!AllInstalled || musicBoxActivated) return false;
        musicBoxActivated = true;
        return true;
    }

    public bool TryCompleteLevel()
    {
        if (!CanExit || levelCompleted) return false;
        levelCompleted = true;
        return true;
    }

    public void Reset()
    {
        collectedMask = 0;
        installedMask = 0;
        musicBoxActivated = false;
        levelCompleted = false;
    }

    private static int Bit(ToyWorldRepairItemType type)
    {
        int index = (int)type;
        if (index < 0 || index > 2)
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown ToyWorld repair item type.");
        return 1 << index;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}
