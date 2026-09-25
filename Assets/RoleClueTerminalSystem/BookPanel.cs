using UnityEngine;

/// <summary>
/// 가이드북 패널 — 역할 사물(책)의 자료 표시. 페이지 이전/다음. CH7·CH8 공용으로 재사용하고 본문
/// 콘텐츠만 챕터별로 다르다(docs/PRD/RoleClueTerminal.md §3).
///
/// [정보 격리 — 소유자 화면에만 노출(§4 확정)]
/// 그리는 조건은 RoleSlot.CanShowTo(지금 조작 중인 참가자) 하나다: 그 사물의 화면을 열어 둔 참가자
/// 본인의 화면일 때만 본문을 그린다. 전체 HUD에는 어떤 본문도 노출하지 않는다.
///
/// [입력은 키보드 — 커서 잠금 때문]
/// PlayerFollowCamera가 마우스 궤도 모드에서 커서를 잠그므로(Esc로 풀림) OnGUI 버튼은 평소에 누를 수
/// 없다. 그래서 페이지 이동은 방향키로 받고 화면은 표시만 한다. 실제 UI 시스템이 생기면 그쪽으로
/// 옮긴다(§5 "UI 레이아웃/입력 키 미정" — 임시 표시 관례).
///
/// [콘텐츠 미확정]
/// 책 본문(CH7/CH8)은 PRD가 "콘텐츠 구성안(미확정)"이라 여기서는 구조만 둔다. pages가 비어 있으면
/// 안내만 표시한다.
/// </summary>
public class BookPanel : MonoBehaviour
{
    [Tooltip("이 책이 속한 역할 사물. 화면 열림·사용자 판별을 여기서 읽는다.")]
    public RoleSlot slot;

    [Tooltip("페이지 본문. 실제 콘텐츠(CH7 생물학 자료, CH8 조합법)는 미확정이라 씬/데이터에서 채운다.")]
    [TextArea(3, 8)]
    public string[] pages = new string[0];

    public KeyCode prevKey = KeyCode.LeftArrow;
    public KeyCode nextKey = KeyCode.RightArrow;

    /// <summary>현재 페이지 인덱스(0부터). 화면을 닫았다 열어도 읽던 위치를 유지한다.</summary>
    public int PageIndex { get; private set; }

    public int PageCount => pages != null ? pages.Length : 0;

    public string CurrentText => PageCount > 0 ? pages[Mathf.Clamp(PageIndex, 0, PageCount - 1)] : string.Empty;

    /// <summary>다음 페이지로. 마지막이면 그대로 두고 false.</summary>
    public bool NextPage()
    {
        if (PageIndex + 1 >= PageCount) return false;
        PageIndex++;
        return true;
    }

    /// <summary>이전 페이지로. 첫 페이지면 그대로 두고 false.</summary>
    public bool PrevPage()
    {
        if (PageIndex <= 0) return false;
        PageIndex--;
        return true;
    }

    /// <summary>챕터 종료/전체 재시작 시 책 첫 페이지로 초기화한다(§2.7).</summary>
    public void ResetToFirstPage()
    {
        PageIndex = 0;
    }

    private bool bound;

    private void OnEnable()
    {
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    /// <summary>매니저의 챕터 재시작 신호를 구독한다. OnEnable에서 자동으로 부르며 여러 번 불러도 한
    /// 번만 걸린다(Editor 자가검증이 OnEnable 없이 직접 부르기도 한다).</summary>
    public void Bind()
    {
        if (bound || slot == null || slot.manager == null) return;
        bound = true;
        slot.manager.ChapterReset += ResetToFirstPage;
    }

    public void Unbind()
    {
        if (!bound) return;
        bound = false;
        if (slot != null && slot.manager != null) slot.manager.ChapterReset -= ResetToFirstPage;
    }

    private void Start()
    {
        if (slot == null)
            Debug.LogWarning($"[BookPanel] '{name}'에 slot이 연결되지 않아 화면이 열리지 않는다.", this);
        if (PageCount == 0)
            Debug.LogWarning($"[BookPanel] '{name}'의 pages가 비어 있다(본문 콘텐츠 미확정).", this);
    }

    private void Update()
    {
        if (!IsShownToLocalPlayer()) return;

        if (Input.GetKeyDown(nextKey)) NextPage();
        else if (Input.GetKeyDown(prevKey)) PrevPage();
    }

    private bool IsShownToLocalPlayer()
    {
        if (slot == null || slot.manager == null) return false;
        return slot.CanShowTo(slot.manager.ControlledPlayer());
    }

    private void OnGUI()
    {
        if (!IsShownToLocalPlayer()) return;

        float w = Mathf.Min(560f, Screen.width - 40f);
        Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f - 130f, w, 260f);
        string text = PageCount == 0 ? "(자료 없음 — 콘텐츠 미확정)" : CurrentText;
        string footer = PageCount == 0
            ? "Esc: 사용 종료"
            : $"{PageIndex + 1} / {PageCount}    ←/→: 페이지    Esc: 사용 종료";

        GUI.Box(box, $"가이드북 — {slot.roleId}");
        GUI.Label(new Rect(box.x + 12f, box.y + 28f, box.width - 24f, box.height - 62f), text);
        GUI.Label(new Rect(box.x + 12f, box.yMax - 28f, box.width - 24f, 22f), footer);
    }
}
