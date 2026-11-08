using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 그래픽 설정의 현재값·적용·저장. UI(`PauseMenuController`)는 값만 바꾸고 `ApplyDetails()`/`Save()`를 부른다.
/// 저장은 PlayerPrefs에 JSON 한 덩어리다. 저장값이 없는 첫 실행은 High(균형)로 시작한다 —
/// Unity가 Windows에서 마지막 품질을 레지스트리에 따로 기억하므로(`UnityGraphicsQuality`) 그 값에 끌려가지 않게 항상 명시적으로 정한다.
/// </summary>
public static class GraphicsOptions
{
    public enum Preset { Performance, Balanced, Quality }

    // 프리셋 → QualitySettings 레벨(0 VeryLow … 5 Ultra): 성능=Low, 균형=High(배포 기본), 품질=Ultra.
    static readonly int[] PresetLevel = { 1, 3, 5 };

    [Serializable]
    public class Data
    {
        public int baseLevel = 3;
        public bool custom;               // 프리셋 값에서 하나라도 바꿨으면 true
        public int shadowMode = 2;        // 0 끔, 1 낮음(하드), 2 높음(소프트)
        public float shadowDistance = 40f;
        public int antiAliasing;          // 0/2/4/8
        public int textureLimit;          // 0 높음, 1 중간, 2 낮음
        public bool vSync = true;
        public int fpsCap;                // VSync 끔일 때만. 0=무제한
        public int resW, resH;            // 0이면 현재 해상도 유지
        public bool fullscreen = true;
        public bool portalView = true;    // SpacePortal 뷰스루(포탈 너머 화면)
        public bool portalShadows;        // 포탈 속 풍경의 그림자(2026-11-08 실측: 켜면 casters·draw가 크게 늘어 기본 끔)
        public int portalRes = 512;       // 256/512/1024
    }

    const string Key = "gfx.v1";

    public static Data Current { get; private set; } = new Data();

    // SpacePortal이 읽는 값.
    public static bool PortalView => Current.portalView;
    public static bool PortalShadows => Current.portalShadows;
    public static int PortalResolution => Mathf.Clamp(Current.portalRes, 64, 2048);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() => Load();

    public static void Load()
    {
        string json = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(json))
        {
            ApplyPreset(Preset.Balanced, save: false);
            return;
        }
        Current = Sanitize(JsonUtility.FromJson<Data>(json));
        QualitySettings.SetQualityLevel(Current.baseLevel, true);
        ApplyDetails();
        ApplyScreen();
    }

    public static void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(Current));
        PlayerPrefs.Save();
    }

    /// <summary>프리셋을 적용하고 그 결과 값을 읽어 Current를 채운다(상세 옵션이 프리셋 값에서 시작하도록).</summary>
    public static void ApplyPreset(Preset p, bool save = true)
    {
        var d = new Data { baseLevel = PresetLevel[(int)p], resW = Current.resW, resH = Current.resH, fullscreen = Current.fullscreen, fpsCap = Current.fpsCap };
        QualitySettings.SetQualityLevel(d.baseLevel, true);
        d.shadowMode = QualitySettings.shadows == ShadowQuality.Disable ? 0 : QualitySettings.shadows == ShadowQuality.HardOnly ? 1 : 2;
        d.shadowDistance = QualitySettings.shadowDistance;
        d.antiAliasing = QualitySettings.antiAliasing;
        d.textureLimit = QualitySettings.globalTextureMipmapLimit;
        d.vSync = QualitySettings.vSyncCount > 0;
        d.portalView = true;
        d.portalShadows = p == Preset.Quality;
        d.portalRes = p == Preset.Performance ? 256 : 512;
        Current = d;
        ApplyDetails();
        if (save) Save();
    }

    public static void ResetToDefault()
    {
        Current = new Data();
        ApplyPreset(Preset.Balanced);
    }

    /// <summary>Current의 개별 값을 QualitySettings/Application에 반영한다(레벨은 건드리지 않는다).</summary>
    public static void ApplyDetails()
    {
        var d = Current;
        QualitySettings.shadows = d.shadowMode == 0 ? ShadowQuality.Disable : d.shadowMode == 1 ? ShadowQuality.HardOnly : ShadowQuality.All;
        QualitySettings.shadowDistance = d.shadowDistance;
        QualitySettings.antiAliasing = d.antiAliasing;
        QualitySettings.globalTextureMipmapLimit = d.textureLimit;
        QualitySettings.vSyncCount = d.vSync ? 1 : 0;
        Application.targetFrameRate = d.vSync || d.fpsCap <= 0 ? -1 : d.fpsCap; // VSync가 켜져 있으면 프레임 제한은 무의미하다.
    }

    public static void ApplyScreen()
    {
        var d = Current;
        int w = d.resW > 0 ? d.resW : Screen.width, h = d.resH > 0 ? d.resH : Screen.height;
        Screen.SetResolution(w, h, d.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    /// <summary>중복(주사율만 다른) 해상도를 접은 오름차순 목록.</summary>
    public static List<Vector2Int> Resolutions()
    {
        var set = new SortedSet<(int, int)>();
        foreach (var r in Screen.resolutions) if (r.width >= 800 && r.height >= 600) set.Add((r.width, r.height));
        var list = new List<Vector2Int>();
        foreach (var (w, h) in set) list.Add(new Vector2Int(w, h));
        if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
        return list;
    }

    /// <summary>하드웨어로 프리셋을 추천한다. 기준은 휴리스틱이며 실측은 RTX 3060 노트북 한 대뿐이다(약한 PC 미검증).</summary>
    public static Preset Recommend(int vramMb, int cores, string gpuName, out string reason)
    {
        bool integrated = !string.IsNullOrEmpty(gpuName) &&
                          (gpuName.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 && gpuName.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) < 0
                           || gpuName.IndexOf("UHD", StringComparison.OrdinalIgnoreCase) >= 0
                           || gpuName.IndexOf("Iris", StringComparison.OrdinalIgnoreCase) >= 0);
        if (integrated || vramMb < 2048 || cores <= 4)
        {
            reason = integrated ? "내장 그래픽이라 그림자를 줄이는 쪽이 안전합니다" : vramMb < 2048 ? "그래픽 메모리가 적습니다" : "CPU 코어가 적습니다";
            return Preset.Performance;
        }
        if (vramMb >= 6000 && cores >= 8)
        {
            reason = "그래픽 메모리와 CPU가 넉넉합니다";
            return Preset.Quality;
        }
        reason = "무난한 사양입니다";
        return Preset.Balanced;
    }

    public static Data Sanitize(Data d)
    {
        if (d == null) return new Data();
        d.baseLevel = Mathf.Clamp(d.baseLevel, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        d.shadowMode = Mathf.Clamp(d.shadowMode, 0, 2);
        d.shadowDistance = Mathf.Clamp(d.shadowDistance, 10f, 150f);
        d.antiAliasing = Array.IndexOf(new[] { 0, 2, 4, 8 }, d.antiAliasing) >= 0 ? d.antiAliasing : 0;
        d.textureLimit = Mathf.Clamp(d.textureLimit, 0, 2);
        d.portalRes = Array.IndexOf(new[] { 256, 512, 1024 }, d.portalRes) >= 0 ? d.portalRes : 512;
        if (d.resW < 0 || d.resH < 0) { d.resW = 0; d.resH = 0; }
        return d;
    }
}
