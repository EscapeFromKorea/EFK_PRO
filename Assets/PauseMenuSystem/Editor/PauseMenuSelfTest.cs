using UnityEditor;
using UnityEngine;

/// <summary>
/// 배치모드로 돌리는 가장 작은 점검: `Unity -batchmode -executeMethod PauseMenuSelfTest.Run`. 실패하면 종료 코드 1.
/// UI 자체(OnGUI)는 플레이로만 확인할 수 있어 여기서는 순수 로직(추천·보정·저장 왕복·ESC 게이트)만 본다.
/// </summary>
public static class PauseMenuSelfTest
{
    static int _fail;

    static void Check(string name, bool ok)
    {
        Debug.Log((ok ? "PASS " : "FAIL ") + name);
        if (!ok) _fail++;
    }

    [MenuItem("Tools/PauseMenuSystem/Run Self Test")]
    public static void Run()
    {
        _fail = 0;

        // 추천: 약한 PC → 성능, 보통 → 균형, 넉넉 → 품질
        Check("rec: 내장 그래픽 → 성능", GraphicsOptions.Recommend(4096, 8, "Intel(R) UHD Graphics", out _) == GraphicsOptions.Preset.Performance);
        Check("rec: VRAM 1GB → 성능", GraphicsOptions.Recommend(1024, 8, "GTX 750", out _) == GraphicsOptions.Preset.Performance);
        Check("rec: 4코어 → 성능", GraphicsOptions.Recommend(8192, 4, "RTX 3060", out _) == GraphicsOptions.Preset.Performance);
        Check("rec: 중간 → 균형", GraphicsOptions.Recommend(4096, 6, "GTX 1650", out _) == GraphicsOptions.Preset.Balanced);
        Check("rec: 넉넉 → 품질", GraphicsOptions.Recommend(8192, 12, "RTX 3070", out _) == GraphicsOptions.Preset.Quality);
        Check("rec: Intel Arc는 내장으로 보지 않음", GraphicsOptions.Recommend(8192, 12, "Intel(R) Arc(TM) A770", out _) == GraphicsOptions.Preset.Quality);

        // 보정: 범위 밖/깨진 값
        var bad = new GraphicsOptions.Data { baseLevel = 99, shadowMode = 7, shadowDistance = 9999f, antiAliasing = 3, textureLimit = -2, portalRes = 300 };
        var fixedData = GraphicsOptions.Sanitize(bad);
        Check("sanitize: baseLevel 범위", fixedData.baseLevel >= 0 && fixedData.baseLevel < QualitySettings.names.Length);
        Check("sanitize: 그림자/거리/AA/텍스처/포탈 해상도", fixedData.shadowMode == 2 && fixedData.shadowDistance == 150f && fixedData.antiAliasing == 0 && fixedData.textureLimit == 0 && fixedData.portalRes == 512);
        Check("sanitize: null → 기본값", GraphicsOptions.Sanitize(null) != null);

        // 저장 왕복(기존 저장값은 복원한다). QualitySettings는 건드리지 않는다 — 에디터가 ProjectSettings를 다시 저장해 버린다.
        string backup = PlayerPrefs.GetString("gfx.v1", "");
        var saved = new GraphicsOptions.Data { baseLevel = 1, portalRes = 1024, custom = true, shadowMode = 0 };
        var beforeCurrent = JsonUtility.ToJson(GraphicsOptions.Current);
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(saved), GraphicsOptions.Current);
        GraphicsOptions.Save();
        var back = JsonUtility.FromJson<GraphicsOptions.Data>(PlayerPrefs.GetString("gfx.v1"));
        Check("roundtrip: baseLevel/portalRes/custom/shadowMode", back.baseLevel == 1 && back.portalRes == 1024 && back.custom && back.shadowMode == 0);
        JsonUtility.FromJsonOverwrite(beforeCurrent, GraphicsOptions.Current);
        if (backup == "") PlayerPrefs.DeleteKey("gfx.v1"); else PlayerPrefs.SetString("gfx.v1", backup);
        PlayerPrefs.Save();

        // ESC 게이트
        InputGate.PanelOpened(); InputGate.PanelOpened(); InputGate.PanelClosed();
        Check("gate: 패널 2개 열고 1개 닫으면 1", InputGate.PanelOpenCount == 1);
        InputGate.PanelClosed(); InputGate.PanelClosed();
        Check("gate: 0 아래로 내려가지 않음", InputGate.PanelOpenCount == 0);

        Debug.Log(_fail == 0 ? "SELFTEST OK" : "SELFTEST FAILED " + _fail);
        if (Application.isBatchMode) EditorApplication.Exit(_fail == 0 ? 0 : 1);
    }
}
