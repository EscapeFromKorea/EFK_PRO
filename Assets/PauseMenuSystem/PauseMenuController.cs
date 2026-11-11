using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ESC 일시정지 메뉴(돌아가기 / 설정 / 메인화면으로 나가기 / 게임 종료하기)와 그래픽 설정 창. 기존 UI들과 같은 IMGUI(`OnGUI`)로 그린다 —
/// 메인 화면 테마가 정해지면 이 클래스의 그리는 부분만 갈아끼우면 되도록 상태·적용 로직은 `GraphicsOptions`/`InputGate`에 뒀다.
/// 부트스트랩은 PerfSampler처럼 씬과 무관하게 스스로 생성한다.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    // 싱글플레이는 시간을 멈춘다. 온라인 협동으로 넘어가면 false(오버레이만 띄우고 게임은 계속)로 바꾼다.
    public bool freezeTime = true;

    enum State { Closed, Main, Settings }
    State _state = State.Closed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<PauseMenuController>();
    }

    // ── 열기/닫기 ─────────────────────────────────────────────
    bool _wasLocked, _camOrbitWas;
    float _prevTimeScale = 1f;
    PlayerFollowCamera _cam;

    void Open()
    {
        _state = State.Main;
        InputGate.Blocked = true;
        _wasLocked = Cursor.lockState == CursorLockMode.Locked;
        // PlayerFollowCamera는 건드리지 않고 공개 필드만 끈다 — 클릭 재잠금과 시점 회전, ESC 커서 해제가 같이 멈춘다.
        _cam = FindObjectOfType<PlayerFollowCamera>();
        if (_cam != null) { _camOrbitWas = _cam.enableMouseOrbit; _cam.enableMouseOrbit = false; }
        // 이동·점프·가속 입력은 패널용으로 이미 있는 PlayerMover.InputLocked를 그대로 재사용한다(PlayerSystem 수정 없음).
        _prevLocked.Clear();
        foreach (var m in FindObjectsOfType<PlayerMover>()) { _prevLocked[m] = m.InputLocked; m.InputLocked = true; }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (freezeTime)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }
    }

    void Close()
    {
        _state = State.Closed;
        KeepScreen(); // 확인 대기 중이던 해상도 변경은 유지로 본다(돌아가기를 눌렀다는 건 화면이 정상이라는 뜻).
        InputGate.Blocked = false;
        foreach (var kv in _prevLocked) if (kv.Key != null) kv.Key.InputLocked = kv.Value; // 메뉴가 열린 사이 파괴된 도형은 건너뛴다.
        _prevLocked.Clear();
        if (freezeTime)
        {
            Time.timeScale = _prevTimeScale;
            AudioListener.pause = false;
        }
        if (isActiveAndEnabled) StartCoroutine(RestoreCursor());
        else RestoreCursorNow();
    }

    readonly Dictionary<PlayerMover, bool> _prevLocked = new Dictionary<PlayerMover, bool>();

    // 한 프레임 뒤에 복원한다: ESC를 누른 같은 프레임의 카메라 LateUpdate가 enableMouseOrbit이 켜진 것을 보고 커서를 다시 풀어 버린다.
    IEnumerator RestoreCursor()
    {
        yield return null;
        RestoreCursorNow();
    }

    void RestoreCursorNow()
    {
        if (_cam != null) _cam.enableMouseOrbit = _camOrbitWas;
        if (_wasLocked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    void OnDisable()
    {
        if (_state != State.Closed) Close();
    }

    // ── 입력 ─────────────────────────────────────────────────
    void Update()
    {
        _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);

        if (_confirmActive)
        {
            _confirmLeft -= Time.unscaledDeltaTime;
            if (_confirmLeft <= 0f) RevertScreen();
        }

        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (_confirmActive) { RevertScreen(); return; }
        switch (_state)
        {
            case State.Closed:
                // 터미널 같은 화면이 열려 있거나 방금 ESC로 닫혔으면 그 ESC는 이미 쓰였다.
                if (InputGate.PanelOpenCount > 0 || InputGate.PanelClosedFrame == Time.frameCount) return;
                Open();
                break;
            case State.Main: Close(); break;
            case State.Settings: _state = State.Main; break;
        }
    }

    // ── 해상도/화면 모드: 누르는 즉시 적용하고, 설정 창 아래의 배너에서 15초 안에 "유지"하지 않으면 되돌린다 ───────────
    // 화면을 가리는 확인창을 띄우지 않는다 — 바로 적용된 화면을 보며 < > 를 계속 눌러 비교할 수 있게 하려는 것이다.
    bool _confirmActive;
    float _confirmLeft;
    int _prevW, _prevH;
    bool _prevFull;

    void ChangeScreen(int w, int h, bool full)
    {
        var d = GraphicsOptions.Current;
        if (!_confirmActive) // 연속으로 바꿔도 되돌릴 곳은 "마지막으로 확정한 값"이다.
        {
            _prevW = d.resW > 0 ? d.resW : Screen.width; // resW=0(현재 유지)을 그대로 저장하면 되돌릴 때 새 해상도가 기준이 된다.
            _prevH = d.resH > 0 ? d.resH : Screen.height;
            _prevFull = d.fullscreen;
        }
        d.resW = w; d.resH = h; d.fullscreen = full;
        GraphicsOptions.ApplyScreen();
        _confirmActive = true;
        _confirmLeft = 15f;
    }

    void KeepScreen()
    {
        if (!_confirmActive) return;
        _confirmActive = false;
        GraphicsOptions.Save();
    }

    void RevertScreen()
    {
        var d = GraphicsOptions.Current;
        d.resW = _prevW; d.resH = _prevH; d.fullscreen = _prevFull;
        GraphicsOptions.ApplyScreen();
        _confirmActive = false;
    }

    // ── 그리기 ───────────────────────────────────────────────
    float _fps = 60f;
    Vector2 _scroll;
    GUIStyle _title, _label, _hint, _button, _small, _box, _toggle;

    void EnsureStyles()
    {
        if (_title != null) return;
        _title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _label = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
        _hint = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, normal = { textColor = new Color(0.72f, 0.72f, 0.72f) } };
        _button = new GUIStyle(GUI.skin.button) { fontSize = 24 };
        _small = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleRight };
        _box = new GUIStyle(GUI.skin.box);
    }

    void OnGUI()
    {
        if (_state == State.Closed) return;
        EnsureStyles();
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = 1080f;

        // 메인 메뉴만 화면을 어둡게 가린다. 설정 창은 왼쪽에 붙이고 배경을 그대로 보여 줘서, 바꾼 값(해상도·그림자·AA 등)의 차이를 바로 볼 수 있게 한다.
        if (_state == State.Main)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        GUI.Label(new Rect(W - 420, 10, 410, 30), $"FPS {_fps:F0}  ·  {1000f / Mathf.Max(1f, _fps):F1} ms", _small);

        if (_state == State.Main) DrawMain(W, H);
        else DrawSettings(W, H);
    }

    void DrawMain(float W, float H)
    {
        var r = new Rect((W - 520) / 2, (H - 550) / 2, 520, 550);
        GUI.Box(r, GUIContent.none, _box);
        GUI.Label(new Rect(r.x, r.y + 20, r.width, 60), "일시정지", _title);
        if (GUI.Button(new Rect(r.x + 60, r.y + 110, 400, 70), "돌아가기", _button)) Close();
        if (GUI.Button(new Rect(r.x + 60, r.y + 200, 400, 70), "설정", _button)) { _state = State.Settings; _scroll = Vector2.zero; }
        GUI.enabled = false; // 메인 화면은 아직 없다.
        GUI.Button(new Rect(r.x + 60, r.y + 290, 400, 70), "메인화면으로 나가기 (준비 중)", _button);
        GUI.enabled = true;
        if (GUI.Button(new Rect(r.x + 60, r.y + 380, 400, 70), "게임 종료하기", _button)) QuitGame();
    }

    static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 에디터에선 Application.Quit이 무동작이라 플레이 모드를 끈다.
#else
        Application.Quit();
#endif
    }

    static readonly string[] ShadowNames = { "끔", "낮음", "높음" };
    static readonly string[] AaNames = { "끔", "2x", "4x", "8x" };
    static readonly int[] AaValues = { 0, 2, 4, 8 };
    static readonly string[] TexNames = { "높음", "중간", "낮음" };
    static readonly string[] FpsNames = { "30", "60", "120", "무제한" };
    static readonly int[] FpsValues = { 30, 60, 120, 0 };
    static readonly string[] PortalResNames = { "256", "512", "1024" };
    static readonly int[] PortalResValues = { 256, 512, 1024 };

    void DrawSettings(float W, float H)
    {
        var d = GraphicsOptions.Current;
        var r = new Rect(20, 30, 800, H - 60); // 왼쪽에 붙여 오른쪽 장면이 보이게 한다.
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x, r.y + 10, r.width, 60), "그래픽 설정", _title);

        GUILayout.BeginArea(new Rect(r.x + 30, r.y + 80, r.width - 60, r.height - 250)); // 아래 250px: 되돌리기 배너 + 버튼
        _scroll = GUILayout.BeginScrollView(_scroll);

        // 추천
        int vram = SystemInfo.graphicsMemorySize, cores = SystemInfo.processorCount;
        var rec = GraphicsOptions.Recommend(vram, cores, SystemInfo.graphicsDeviceName, out string why);
        GUILayout.Label($"내 PC: {SystemInfo.graphicsDeviceName} · 그래픽 메모리 {vram / 1024f:F1}GB · CPU {cores}스레드", _hint);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"추천: {PresetName(rec)} — {why} (추정)", _label);
        if (GUILayout.Button("추천 적용", _button, GUILayout.Width(180), GUILayout.Height(44))) GraphicsOptions.ApplyPreset(rec);
        GUILayout.EndHorizontal();
        GUILayout.Space(10);

        // 프리셋
        GUILayout.Label(d.custom ? "프리셋 (현재: 사용자 지정)" : $"프리셋 (현재: {PresetName(PresetOf(d.baseLevel))})", _label);
        GUILayout.BeginHorizontal();
        foreach (GraphicsOptions.Preset p in System.Enum.GetValues(typeof(GraphicsOptions.Preset)))
            if (GUILayout.Button(PresetName(p), _button, GUILayout.Height(56))) GraphicsOptions.ApplyPreset(p);
        GUILayout.EndHorizontal();
        GUILayout.Label("성능: 그림자를 끄고 포탈 해상도를 낮춰 약한 PC에 맞춥니다. 균형: 기본값. 품질: 그림자 거리와 포탈 속 그림자까지 켭니다.", _hint);
        GUILayout.Space(14);

        // IMGUI는 한 이벤트 안에서 컨트롤 수가 바뀌면 예외가 난다 — 조건 분기는 이 프레임 시작 시점 값으로 고정한다.
        bool showFps = !d.vSync, showShadowDist = d.shadowMode > 0, portalOn = d.portalView;

        // 화면
        Section("화면");
        var list = GraphicsOptions.Resolutions();
        int cur = Mathf.Max(0, list.FindIndex(v => v.x == (d.resW > 0 ? d.resW : Screen.width) && v.y == (d.resH > 0 ? d.resH : Screen.height)));
        Row("해상도", "누르는 즉시 적용됩니다. 높을수록 선명하지만 GPU 부담이 커집니다. 바꾼 뒤 15초 안에 '유지'를 누르지 않으면 되돌아갑니다.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", _button, GUILayout.Width(60), GUILayout.Height(44)) && cur > 0) ChangeScreen(list[cur - 1].x, list[cur - 1].y, d.fullscreen);
        GUILayout.Label($"{list[cur].x} × {list[cur].y}", _label, GUILayout.Width(220));
        if (GUILayout.Button(">", _button, GUILayout.Width(60), GUILayout.Height(44)) && cur < list.Count - 1) ChangeScreen(list[cur + 1].x, list[cur + 1].y, d.fullscreen);
        GUILayout.EndHorizontal();
        Row("화면 모드", "창 모드는 다른 창과 함께 쓰기 편하고, 전체 화면은 성능이 조금 더 안정적입니다.");
        int fsel = GUILayout.SelectionGrid(d.fullscreen ? 0 : 1, new[] { "전체 화면", "창 모드" }, 2, _button, GUILayout.Height(44));
        if ((fsel == 0) != d.fullscreen) ChangeScreen(d.resW, d.resH, fsel == 0);
        bool v = Toggle("수직 동기화(VSync)", d.vSync, "화면 찢어짐을 막지만 프레임이 모니터 주사율에 묶입니다. 끄면 아래 프레임 제한이 적용됩니다.");
        if (v != d.vSync) { d.vSync = v; Changed(); }
        if (showFps)
        {
            Row("프레임 제한", "프레임을 제한하면 PC 발열과 소음이 줄어듭니다.");
            int fi = System.Array.IndexOf(FpsValues, d.fpsCap); if (fi < 0) fi = 3;
            int fn = GUILayout.SelectionGrid(fi, FpsNames, 4, _button, GUILayout.Height(44));
            if (fn != fi) { d.fpsCap = FpsValues[fn]; Changed(); }
        }
        GUILayout.Space(10);

        // 화질
        Section("화질");
        Row("그림자", "성능에 가장 큰 영향을 줍니다(2026-11-08 실측: Ultra→High에서 그리기 호출이 약 절반으로 감소). 끄면 가장 빠릅니다.");
        int sm = GUILayout.SelectionGrid(d.shadowMode, ShadowNames, 3, _button, GUILayout.Height(44));
        if (sm != d.shadowMode) { d.shadowMode = sm; Changed(); }
        if (showShadowDist)
        {
            Row($"그림자 거리: {d.shadowDistance:F0}", "멀리까지 그림자를 그립니다. 줄이면 가벼워집니다(추정).");
            float sd = GUILayout.HorizontalSlider(d.shadowDistance, 10f, 150f, GUILayout.Height(24));
            if (Mathf.Abs(sd - d.shadowDistance) > 0.01f) { d.shadowDistance = Mathf.Round(sd); Changed(); }
        }
        Row("안티앨리어싱", "모서리의 계단 현상을 줄입니다. 값이 클수록 GPU 부담이 커집니다(추정).");
        int ai = Mathf.Max(0, System.Array.IndexOf(AaValues, d.antiAliasing));
        int an = GUILayout.SelectionGrid(ai, AaNames, 4, _button, GUILayout.Height(44));
        if (an != ai) { d.antiAliasing = AaValues[an]; Changed(); }
        Row("텍스처 품질", "낮추면 그래픽 메모리를 아낍니다. 메모리가 부족한 PC에서 끊김이 줄 수 있습니다(추정).");
        int tn = GUILayout.SelectionGrid(d.textureLimit, TexNames, 3, _button, GUILayout.Height(44));
        if (tn != d.textureLimit) { d.textureLimit = tn; Changed(); }
        GUILayout.Space(10);

        // 포탈
        Section("포탈");
        bool pv = Toggle("포탈 너머 화면", d.portalView, "끄면 포탈이 검은 면으로 보이지만 가장 가벼워집니다(실측: 켜면 그리기 호출과 setpass가 크게 늘어남).");
        if (pv != d.portalView) { d.portalView = pv; Changed(); }
        GUI.enabled = portalOn;
        bool ps = Toggle("포탈 속 풍경 그림자", d.portalShadows, "켜면 포탈 속 풍경에도 그림자가 보이지만 비용이 큽니다(실측: 그림자 캐스터가 크게 늘어남).");
        if (ps != d.portalShadows) { d.portalShadows = ps; Changed(); }
        Row("포탈 화면 해상도", "낮추면 포탈 속 화면이 흐려지는 대신 GPU 부담이 줄어듭니다(추정).");
        int pi = Mathf.Max(0, System.Array.IndexOf(PortalResValues, d.portalRes));
        int pn = GUILayout.SelectionGrid(pi, PortalResNames, 3, _button, GUILayout.Height(44));
        if (pn != pi) { d.portalRes = PortalResValues[pn]; Changed(); }
        GUI.enabled = true;

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // 해상도·화면 모드를 바꾼 직후에만 뜨는 되돌리기 배너(화면을 가리지 않는다). ESC도 "되돌리기"다.
        if (_confirmActive)
        {
            GUI.Label(new Rect(r.x + 30, r.yMax - 160, r.width - 60, 36), $"화면 설정을 바꿨습니다 — {Mathf.CeilToInt(_confirmLeft)}초 뒤 이전 설정으로 돌아갑니다", _label);
            if (GUI.Button(new Rect(r.x + 30, r.yMax - 118, 340, 44), "유지", _button)) KeepScreen();
            if (GUI.Button(new Rect(r.x + 400, r.yMax - 118, 340, 44), "되돌리기 (ESC)", _button)) RevertScreen();
        }
        if (GUI.Button(new Rect(r.x + 30, r.yMax - 66, 330, 50), "기본값으로 되돌리기", _button)) GraphicsOptions.ResetToDefault();
        if (GUI.Button(new Rect(r.xMax - 250, r.yMax - 66, 220, 50), "뒤로", _button)) { KeepScreen(); _state = State.Main; }
    }

    // 상세 옵션 하나가 바뀔 때: 프리셋에서 벗어났음을 표시하고 즉시 적용·저장한다.
    static void Changed()
    {
        GraphicsOptions.Current.custom = true;
        GraphicsOptions.ApplyDetails();
        GraphicsOptions.Save();
    }

    void Section(string text) => GUILayout.Label($"── {text} ──", _title);

    void Row(string label, string hint)
    {
        GUILayout.Label(label, _label);
        GUILayout.Label(hint, _hint);
    }

    bool Toggle(string label, bool value, string hint)
    {
        GUILayout.Label(hint, _hint);
        _toggle ??= new GUIStyle(GUI.skin.toggle) { fontSize = 24 };
        return GUILayout.Toggle(value, "  " + label, _toggle, GUILayout.Height(40));
    }

    static string PresetName(GraphicsOptions.Preset p) => p == GraphicsOptions.Preset.Performance ? "성능" : p == GraphicsOptions.Preset.Balanced ? "균형" : "품질";

    static GraphicsOptions.Preset PresetOf(int level) => level <= 2 ? GraphicsOptions.Preset.Performance : level == 3 ? GraphicsOptions.Preset.Balanced : GraphicsOptions.Preset.Quality;
}
