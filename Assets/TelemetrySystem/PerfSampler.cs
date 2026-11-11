using System.Globalization;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 빌드 전용 성능 샘플러. SAMPLE_SECONDS마다 구간 평균 FPS·CPU/GPU 프레임 시간·최악 프레임·카메라 위치를
/// Loki에 `name=perf` 이벤트 한 줄(logfmt)로 보낸다. 에디터에선 F9 토글만 동작하고 샘플은 보내지 않는다(FrameTimingManager가 부정확).
/// 조회: {job="efk-pro", name="perf"} | logfmt | unwrap gpu_ms
/// F11 = 품질 레벨 순환(Ultra/High 비교). cpu_main_ms/cpu_render_ms는 메인·렌더 스레드 시간(cpu_ms는 합산 프레임 시간).
/// 샘플 전송은 Development Build에서는 기본 켜짐, 릴리스 빌드에서는 기본 꺼짐이며 F10으로 토글한다.
/// 상시 사양·부하 수집은 HardwareProfiler 몫이고, 이 클래스는 위치·드로우콜을 붙인 병목 조사 전용이다.
/// F9로 SpacePortal 뷰스루 전체를 토글할 수 있다(portals=개수, vt=on/off).
/// GPU 시간은 ProjectSettings의 Frame Timing Stats가 켜져 있고 그래픽 API가 지원할 때만 나온다(없으면 gpu_ms=-1).
/// draw/setpass/batches/shadow_casters는 Development Build에서만 값이 있다(릴리스 빌드는 -1).
/// </summary>
public class PerfSampler : MonoBehaviour
{
    const float SampleSeconds = 2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PerfSampler");
        DontDestroyOnLoad(go);
        go.AddComponent<PerfSampler>();
    }

    readonly FrameTiming[] _timing = new FrameTiming[1];
    ProfilerRecorder _draw, _setPass, _batches, _casters;
    float _elapsed, _worst, _cpuSum, _gpuSum, _mainSum, _renderSum;
    int _frames, _gpuFrames;
    bool _vtOff;
    bool _active;

    void Awake() => _active = Debug.isDebugBuild; // 필드 초기화에서 호출하면 UnityException

    void OnEnable()
    {
        _draw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
    }

    void OnDisable()
    {
        _draw.Dispose(); _setPass.Dispose(); _batches.Dispose(); _casters.Dispose();
    }

    void Update()
    {
        // F9: 모든 SpacePortal의 뷰스루를 한꺼번에 껐다 켠다 — 같은 자리에서 A/B 비교용(vt= 필드로 기록됨).
        if (Input.GetKeyDown(KeyCode.F9))
        {
            _vtOff = !_vtOff;
            foreach (var sp in FindObjectsOfType<SpacePortal>()) sp.enableViewThrough = !_vtOff;
            LokiTelemetry.Event("perf_toggle", "viewthrough=" + (_vtOff ? "off" : "on"));
        }
        if (Input.GetKeyDown(KeyCode.F10))
        {
            _active = !_active;
            ResetAcc();
            LokiTelemetry.Event("perf_toggle", "sampling=" + (_active ? "on" : "off"));
        }
        // F11: 품질 레벨 순환 — 빌드 하나로 Ultra/High 비교용. q= 필드와 perf_toggle 이벤트에 레벨 이름이 남는다.
        if (Input.GetKeyDown(KeyCode.F11))
        {
            int n = QualitySettings.names.Length;
            int next = (QualitySettings.GetQualityLevel() + 1) % n;
            QualitySettings.SetQualityLevel(next, true);
            ResetAcc();
            LokiTelemetry.Event("perf_toggle", "quality=" + next + " qname=" + QualitySettings.names[next].Replace(' ', '_'));
        }
#if UNITY_EDITOR
        return; // 에디터에선 F9 토글만 쓴다 — FrameTimingManager가 부정확해 샘플은 보내지 않는다.
#endif
#pragma warning disable CS0162
        if (!_active) return;
        float dt = Time.unscaledDeltaTime;
        _elapsed += dt;
        _frames++;
        if (dt > _worst) _worst = dt;

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
        {
            _cpuSum += (float)_timing[0].cpuFrameTime;
            _mainSum += (float)_timing[0].cpuMainThreadFrameTime;
            _renderSum += (float)_timing[0].cpuRenderThreadFrameTime;
            if (_timing[0].gpuFrameTime > 0) { _gpuSum += (float)_timing[0].gpuFrameTime; _gpuFrames++; }
        }

        if (_elapsed < SampleSeconds) return;
        Report();
        ResetAcc();
#pragma warning restore CS0162
    }

    void ResetAcc()
    {
        _elapsed = _worst = _cpuSum = _gpuSum = _mainSum = _renderSum = 0f;
        _frames = _gpuFrames = 0;
    }

    void Report()
    {
        var ci = CultureInfo.InvariantCulture;
        var cam = Camera.main;
        var p = cam != null ? cam.transform.position : Vector3.zero;
        var sb = new StringBuilder(160);
        sb.Append("scene=").Append(SceneManager.GetActiveScene().name)
          .Append(" fps=").Append((_frames / _elapsed).ToString("F1", ci))
          .Append(" worst_ms=").Append((_worst * 1000f).ToString("F1", ci))
          .Append(" cpu_ms=").Append(_frames > 0 ? (_cpuSum / _frames).ToString("F2", ci) : "-1")
          .Append(" cpu_main_ms=").Append(_frames > 0 ? (_mainSum / _frames).ToString("F2", ci) : "-1")
          .Append(" cpu_render_ms=").Append(_frames > 0 ? (_renderSum / _frames).ToString("F2", ci) : "-1")
          .Append(" gpu_ms=").Append(_gpuFrames > 0 ? (_gpuSum / _gpuFrames).ToString("F2", ci) : "-1")
          .Append(" draw=").Append(Val(_draw))
          .Append(" setpass=").Append(Val(_setPass))
          .Append(" batches=").Append(Val(_batches))
          .Append(" shadow_casters=").Append(Val(_casters))
          .Append(" portals=").Append(FindObjectsOfType<SpacePortal>().Length)
          .Append(" vt=").Append(_vtOff ? "off" : "on")
          .Append(" x=").Append(p.x.ToString("F0", ci))
          .Append(" y=").Append(p.y.ToString("F0", ci))
          .Append(" z=").Append(p.z.ToString("F0", ci))
          .Append(" gpu_name=").Append(SystemInfo.graphicsDeviceName.Replace(' ', '_'))
          .Append(" q=").Append(QualitySettings.GetQualityLevel());
        LokiTelemetry.Event("perf", sb.ToString());
    }

    static long Val(ProfilerRecorder r) => r.Valid ? r.LastValue : -1;
}
