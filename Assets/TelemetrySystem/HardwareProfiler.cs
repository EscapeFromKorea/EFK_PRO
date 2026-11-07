using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 플레이어 PC에 걸리는 부하를 주기적으로 Loki로 보낸다 (그라파나 HW 대시보드용).
///
/// [중요 — "사용률 %"가 아니다]
/// Unity는 크로스플랫폼 CPU/GPU 사용률(%)을 제공하지 않는다. 대신 FrameTimingManager가 주는
/// CPU/GPU 프레임타임(ms)과 FPS를 부하의 대리 지표로 쓴다. GPU 프레임타임은 플랫폼/그래픽 API/
/// 설정(Player Settings의 Frame Timing Stats)에 따라 0으로 나올 수 있으며, 그 경우 gpu_frame_ms는
/// 보내지 않고 `gpu_timing_unavailable` 이벤트를 1회 남긴다 — 값이 "0ms"로 찍혀 부하가 낮은 것처럼
/// 오해되는 걸 막기 위함이다.
///
/// 보내는 것:
///  - type="event" name="hw_info"   : 세션 시작 시 1회, 하드웨어 사양(JSON)
///  - type="metric" name=...        : SampleIntervalSeconds마다 fps / frame_ms_max / cpu_frame_ms /
///                                    cpu_main_ms / cpu_render_ms / gpu_frame_ms / sys_mem_mb / gfx_mem_mb
/// 모든 스트림에 build 라벨(editor|player)이 붙는다 — 에디터 플레이는 에디터 오버헤드가 섞여 있어
/// 플레이어 PC 부하로 보면 안 되므로 대시보드에서 분리해서 볼 수 있게 한다. os 라벨은 LokiClient가 붙인다.
/// </summary>
public class HardwareProfiler : MonoBehaviour
{
    const float SampleIntervalSeconds = 5f;
    const int NoTimingWarnAfterSamples = 3;
    const double BytesPerMegabyte = 1024.0 * 1024.0;

    [Serializable]
    struct HwInfo
    {
        public string cpu;
        public int cpuCores;
        public int cpuMhz;
        public string gpu;
        public string gpuVendor;
        public string gfxApi;
        public int ramMb;
        public int vramMb;
        public string osName;
        public string quality;
        public string resolution;
        public bool frameTimingEnabled;
    }

    readonly FrameTiming[] _timings = new FrameTiming[1];
    string _buildLabel = "player";

    ProfilerRecorder _sysMem;
    ProfilerRecorder _gfxMem;
    bool _frameTimingEnabled;

    float _elapsed;
    int _frames;
    float _worstDeltaTime;
    double _cpuSum, _cpuMainSum, _cpuRenderSum, _gpuSum;
    int _cpuCount, _gpuCount;
    int _noGpuSamples;
    bool _gpuWarned;

    // LokiClient는 BeforeSceneLoad에서 먼저 만들어진다. 여기는 AfterSceneLoad라서 항상 그 뒤에 돈다
    // (같은 시점이면 실행 순서가 보장되지 않아 hw_info 이벤트가 유실될 수 있다).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!LokiClient.IsActive) return;
        var go = new GameObject("HardwareProfiler");
        DontDestroyOnLoad(go);
        go.AddComponent<HardwareProfiler>();
    }

    void Start()
    {
        _buildLabel = Application.isEditor ? "editor" : "player";
        _frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
        // 메모리 카운터는 빌드 종류/플랫폼에 따라 안 열릴 수 있다 — Valid를 확인하고 안 되면 그 지표만 건너뛴다.
        _sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        _gfxMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
        SendHardwareInfo();
    }

    void OnDestroy()
    {
        if (_sysMem.Valid) _sysMem.Dispose();
        if (_gfxMem.Valid) _gfxMem.Dispose();
    }

    void Update()
    {
        var deltaTime = Time.unscaledDeltaTime;
        _elapsed += deltaTime;
        _frames++;
        if (deltaTime > _worstDeltaTime) _worstDeltaTime = deltaTime;

        if (_frameTimingEnabled) AccumulateFrameTiming();

        if (_elapsed >= SampleIntervalSeconds)
        {
            EmitSample();
            ResetWindow();
        }
    }

    void AccumulateFrameTiming()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _timings) == 0) return;

        var timing = _timings[0];
        if (timing.cpuFrameTime > 0)
        {
            _cpuSum += timing.cpuFrameTime;
            _cpuMainSum += timing.cpuMainThreadFrameTime;
            _cpuRenderSum += timing.cpuRenderThreadFrameTime;
            _cpuCount++;
        }
        if (timing.gpuFrameTime > 0)
        {
            _gpuSum += timing.gpuFrameTime;
            _gpuCount++;
        }
    }

    void EmitSample()
    {
        SendMetric("fps", _frames / _elapsed);
        SendMetric("frame_ms_max", _worstDeltaTime * 1000.0);

        if (_cpuCount > 0)
        {
            SendMetric("cpu_frame_ms", _cpuSum / _cpuCount);
            SendMetric("cpu_main_ms", _cpuMainSum / _cpuCount);
            SendMetric("cpu_render_ms", _cpuRenderSum / _cpuCount);
        }

        if (_gpuCount > 0)
        {
            SendMetric("gpu_frame_ms", _gpuSum / _gpuCount);
            _noGpuSamples = 0;
        }
        else
        {
            _noGpuSamples++;
            WarnGpuTimingUnavailableOnce();
        }

        if (_sysMem.Valid && _sysMem.LastValue > 0) SendMetric("sys_mem_mb", _sysMem.LastValue / BytesPerMegabyte);
        if (_gfxMem.Valid && _gfxMem.LastValue > 0) SendMetric("gfx_mem_mb", _gfxMem.LastValue / BytesPerMegabyte);
    }

    void WarnGpuTimingUnavailableOnce()
    {
        if (_gpuWarned || _noGpuSamples < NoTimingWarnAfterSamples) return;
        _gpuWarned = true;
        Send("event", "gpu_timing_unavailable",
            $"api={SystemInfo.graphicsDeviceType} frameTimingEnabled={_frameTimingEnabled} gpu={SystemInfo.graphicsDeviceName}");
    }

    void ResetWindow()
    {
        _elapsed = 0f;
        _frames = 0;
        _worstDeltaTime = 0f;
        _cpuSum = _cpuMainSum = _cpuRenderSum = _gpuSum = 0.0;
        _cpuCount = _gpuCount = 0;
    }

    void SendHardwareInfo()
    {
        var info = new HwInfo
        {
            cpu = SystemInfo.processorType,
            cpuCores = SystemInfo.processorCount,
            cpuMhz = SystemInfo.processorFrequency,
            gpu = SystemInfo.graphicsDeviceName,
            gpuVendor = SystemInfo.graphicsDeviceVendor,
            gfxApi = SystemInfo.graphicsDeviceType.ToString(),
            ramMb = SystemInfo.systemMemorySize,
            vramMb = SystemInfo.graphicsMemorySize,
            osName = SystemInfo.operatingSystem,
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            resolution = $"{Screen.width}x{Screen.height}",
            frameTimingEnabled = _frameTimingEnabled,
        };
        Send("event", "hw_info", JsonUtility.ToJson(info));
    }

    void SendMetric(string name, double value) =>
        Send("metric", name, value.ToString("F3", CultureInfo.InvariantCulture));

    void Send(string type, string name, string line) =>
        LokiClient.Enqueue(new Dictionary<string, string>
        {
            { "type", type },
            { "name", name },
            { "build", _buildLabel },
        }, line);
}
