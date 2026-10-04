using System;
using UnityEngine;

/// <summary>
/// 섹터 씬(Scenes/Sectors/Map4_S{n}.unity) 루트(S{n}_Root)에 부착(F1-2). 자기 섹터 정보를 들고
/// Map4Director에 등록만 한다 — 씬 사이 직접 참조는 하지 않는다(정적 이벤트로만, F1-2 명세).
/// </summary>
public class SectorController : MonoBehaviour
{
    [Tooltip("1~8.")]
    public int sectorId;
    public string sectorName;
    [Tooltip("[실좌표 09-22] Map4Layout에서 동기화됨.")]
    public float width;
    public float length;

    [Tooltip("이 섹터 안에서 도형 3개가 스폰될 자리(Manual/Markers 아래, 없을 때만 생성 — 결정3) " +
             "— Map4Director.SpawnPlayersAt/TeleportToSectorSpawn이 읽는다.")]
    public Transform[] spawnSlots = new Transform[3];

    [Tooltip("[F1 재작업 판정 결정3] 이 섹터의 입구 지점(Manual/Markers/Entrance).")]
    public Transform entrance;
    [Tooltip("[F1 재작업 판정 결정3] 이 섹터의 출구 지점(Manual/Markers/Exit) — 다음 섹터로 " +
             "이어지는 연결 통로 시작부.")]
    public Transform exit;
    [Tooltip("[F1 재작업 판정 결정3] 이 섹터의 체크포인트 위치(Manual/Markers/Checkpoint) — " +
             "0단계에서는 마커만, RespawnZone 배선은 섹터 구체화 단계의 몫.")]
    public Transform checkpoint;

    public static event Action<SectorController> Registered;
    public static event Action<SectorController> Unregistered;

    void OnEnable() => Registered?.Invoke(this);
    void OnDisable() => Unregistered?.Invoke(this);
}
