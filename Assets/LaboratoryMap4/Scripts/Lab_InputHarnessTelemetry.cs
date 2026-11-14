using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// [F1 재작업 판정 결정6] F1-6 항목5(실제 입력 경로 검증)를 위한 텔레메트리 — "창 모드 EXE + OS
/// 키 입력 주입" 방식의 절반(플레이어 쪽 관측)을 맡는다. 나머지 절반(입력 주입 + 판정)은
/// tools/map4_drive.ps1이 맡는다.
///
/// [선례] 맵2_V3_릴레이설계/로컬수정_2026-09-12/증거/drive_lib.ps1·k06c_drive.ps1이 쓰던
/// "텔레메트리 파일 + 명령 파일" 방식을 그대로 참고했다(그 스크립트들은 읽기만 했다 — 로직은
/// 이 프로젝트 전용으로 새로 짰다). 팀 PlayerMover 등은 무수정 — 이 컴포넌트는 관측만 한다.
///
/// [기본값 = 완전 비활성] MAP4_TELE_PATH 환경변수가 없으면 Awake에서 즉시 자기 자신을
/// 비활성화한다 — 일반 플레이(사용자가 그냥 빌드/LaboratoryMap4.exe를 실행하는 경우)에는 파일
/// 입출력이 전혀 일어나지 않는다. 사용자 허락 없이는 절대 켜지지 않는다(map-builder는 이 회차에서
/// 이 하네스를 실행하지 않았다 — 준비만 했다).
/// </summary>
public class Lab_InputHarnessTelemetry : MonoBehaviour
{
    [Tooltip("텔레메트리를 몇 초 간격으로 쓸지.")]
    public float writeInterval = 0.05f;

    private string telePath;
    private string cmdPath;
    private float nextWrite;

    void Awake()
    {
        telePath = Environment.GetEnvironmentVariable("MAP4_TELE_PATH");
        cmdPath = Environment.GetEnvironmentVariable("MAP4_CMD_PATH");
        if (string.IsNullOrEmpty(telePath))
        {
            enabled = false; // 환경변수 없음 = 일반 실행 — 완전히 조용히 꺼진다.
            return;
        }

        try
        {
            File.WriteAllText(telePath, $"# Map4 input harness telemetry start {DateTime.Now:O}\n");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lab_InputHarnessTelemetry] 텔레메트리 파일을 열 수 없다({telePath}): {e.Message}");
            enabled = false;
        }
    }

    void Update()
    {
        if (Time.unscaledTime < nextWrite) return;
        nextWrite = Time.unscaledTime + writeInterval;

        ProcessCommands();
        WriteSample();
    }

    private Transform ActivePlayer()
    {
        Transform t = PlayerControlSwitcher.ActiveTarget;
        return t;
    }

    private void WriteSample()
    {
        Transform p = ActivePlayer();
        if (p == null) return;
        Rigidbody rb = p.GetComponent<Rigidbody>();
        PlayerShapeController shape = p.GetComponent<PlayerShapeController>();
        Vector3 pos = p.position;
        Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
        bool grounded = shape != null && shape.IsGrounded();

        string line = string.Format(CultureInfo.InvariantCulture,
            "{0:F3} active={1} pos=({2:F3}, {3:F3}, {4:F3}) vel=({5:F3}, {6:F3}, {7:F3}) grounded={8}\n",
            Time.time, p.name, pos.x, pos.y, pos.z, vel.x, vel.y, vel.z, grounded);
        try { File.AppendAllText(telePath, line); }
        catch { /* 파일 잠금 등 일시적 실패는 다음 샘플에서 다시 시도 — 검증용 도구라 최선 노력이면 충분하다. */ }
    }

    /// <summary>명령 파일에서 한 줄씩 읽어 처리하고 비운다. 지원 명령: "tp x y z"(활성 플레이어
    /// 순간이동). 텔레메트리와 마찬가지로 MAP4_CMD_PATH가 없으면 아무 것도 하지 않는다.</summary>
    private void ProcessCommands()
    {
        if (string.IsNullOrEmpty(cmdPath) || !File.Exists(cmdPath)) return;

        string content;
        try { content = File.ReadAllText(cmdPath); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(content)) return;

        try { File.WriteAllText(cmdPath, string.Empty); } catch { /* 다음 프레임에 재시도 */ }

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            string[] parts = line.Split(' ');
            if (parts[0] == "tp" && parts.Length >= 4)
            {
                Transform p = ActivePlayer();
                if (p == null) continue;
                if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    Rigidbody rb = p.GetComponent<Rigidbody>();
                    Vector3 pos = new Vector3(x, y, z);
                    if (rb != null) { rb.velocity = Vector3.zero; rb.position = pos; }
                    p.position = pos;
                }
            }
        }
    }
}
