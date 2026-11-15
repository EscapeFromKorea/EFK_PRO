using UnityEngine;

/// <summary>
/// KitchenMapV3 소유 표식 (2026-09-12, Codex 검수 K01 — 전용 씬 경계).
///
/// V3 도구(V3_Play·V3_Checkpoints 등)가 "이번 호출로 직접 만든" 씬 오브젝트, 또는 V3 전용 씬에서
/// 명시적으로 인수한 오브젝트(예: 새 씬의 기본 Main Camera)에만 붙는다. V3 도구는 이 표식이 없는
/// 동명·동종 오브젝트(Player_*·PlayerControlSwitcher·RespawnController)를 재사용·이동·변경·삭제하지
/// 않고, 복제해서 중복시키지도 않는다 — 오류 로그 후 무변경으로 중단한다(Editor/V3_Core.cs
/// V3.EnsureOwnedScene). 런타임 동작 없음(데이터 표식뿐). 팀 코드는 이 컴포넌트를 모른다(무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class V3_Owned : MonoBehaviour
{
    [Tooltip("표식을 붙인 도구(진단용 — 동작에 영향 없음)")]
    public string createdBy;
}
