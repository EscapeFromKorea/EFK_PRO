/// <summary>
/// 격리 퍼즐의 기호 이름 — 레버 번호(1~3)에 대응하는 기호. 순서판(ClueBoard)과 대응표(CorrespondenceMap)가
/// 같은 이름을 쓰도록 한 곳에 둔다.
///
/// [콘텐츠 미확정]
/// docs/PRD/IsolationRescue.md §4/§5가 기호·레버 매핑을 "미확정 콘텐츠 구성안"으로 표시했다. 원본 예시는
/// 달=2 / 파도=1 / 십자=3이고, 정답 순서 [2,1,3]이 "달·파도·십자"가 된다. 여기 기본값은 그 예시 그대로이며
/// 실제 채택 여부는 콘텐츠 확정 단계에서 바뀔 수 있다 — 두 표시 컴포넌트의 symbolNames 필드로 덮어쓴다.
/// </summary>
public static class IsolationSymbols
{
    /// <summary>인덱스 0 = 레버 1. 원본 예시(파도=1, 달=2, 십자=3).</summary>
    public static readonly string[] DefaultNames = { "파도", "달", "십자" };

    /// <summary>레버 번호(1~3)의 기호 이름. 범위 밖이거나 이름이 모자라면 "?"로 돌려 화면이 깨지지 않게 한다.</summary>
    public static string NameOf(string[] names, int leverNumber)
    {
        if (names == null || leverNumber < 1 || leverNumber > names.Length) return "?";
        return string.IsNullOrEmpty(names[leverNumber - 1]) ? "?" : names[leverNumber - 1];
    }
}
