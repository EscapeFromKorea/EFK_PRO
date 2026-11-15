#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

/// <summary>CH5 도끼 첫 등장 PlayMode 기동 검사. 카메라 PNG는 API fixture 시야이며 실제 입력·완주 시험은 별도.</summary>
public static class Map4AxeRevealChecks
{
    private const int Width = 1280, Height = 720;
    public static IEnumerator Run(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        if (record == null) yield break;
        if (sector == null || sector.sectorId != 5)
        { record("CH5.reveal.scope", false, "섹터5가 아님"); yield break; }

        Transform[] all = sector.GetComponentsInChildren<Transform>(true);
        Transform axeRoot = Named(all, "S5_Axe_1");
        Transform zoneRoot = Named(all, "S5_FirstEntryZone_S5_Axe_1");
        Transform roofRoot = Named(all, "GEO_S5_AxeCanopy_1");
        AxeTrap axe = axeRoot != null ? axeRoot.GetComponent<AxeTrap>() : null;
        Lab_AxeReveal reveal = axeRoot != null ? axeRoot.GetComponent<Lab_AxeReveal>() : null;
        Lab_FirstEntryTrap trigger = zoneRoot != null ? zoneRoot.GetComponent<Lab_FirstEntryTrap>() : null;
        BoxCollider zone = zoneRoot != null ? zoneRoot.GetComponent<BoxCollider>() : null;
        Collider roof = roofRoot != null ? roofRoot.GetComponent<Collider>() : null;
        PlayerMover player = FirstBody(players);
        Rigidbody body = player != null ? player.GetComponent<Rigidbody>() : null;
        bool ready = axe != null && reveal != null && trigger != null && zone != null && roof != null &&
                     body != null && body.useGravity && trigger.target == axe.gameObject &&
                     !axe.gameObject.activeSelf && !trigger.Activated && reveal.enabled &&
                     axe.bladeCollider != null && axe.supportCollider != null &&
                     !axe.bladeCollider.enabled && !axe.supportCollider.enabled && !axe.enabled &&
                     reveal.warningLight != null && !reveal.warningLight.enabled &&
                     Mathf.Abs(reveal.revealBelowWorldY - roof.bounds.min.y) < 0.05f;
        record("CH5.reveal.initial", ready,
            $"도끼1 비활성·팀 운동/콜라이더 OFF·경고등 OFF·물리 트리거 1:1; 저장된 천장 밑면={(reveal != null ? reveal.revealBelowWorldY : float.NaN):F2}, 실제={(roof != null ? roof.bounds.min.y : float.NaN):F2} (불일치면 S5 재생성)");
        if (!ready) yield break;

        float visualLowest = VisualMinWorldY(axeRoot);
        float roofTop = roof.bounds.max.y;
        bool aboveRoof = !float.IsNaN(visualLowest) && visualLowest > roofTop + 0.1f;
        Vector3 sightFrom = zone.bounds.center + Vector3.up * 0.25f;
        Vector3 bladeCenter = axe.transform.TransformPoint(axe.bladeCollider.center);
        bool roofBlocks = Physics.Linecast(sightFrom, bladeCenter, out RaycastHit cover,
            ~0, QueryTriggerInteraction.Ignore) && cover.collider == roof;
        record("CH5.reveal.cover", aboveRoof && roofBlocks,
            $"실제 자식 메쉬 최저 y {visualLowest:F2} > 천장 윗면 {roofTop:F2}; 진입점→날 광선 첫 충돌={(cover.collider != null ? cover.collider.name : "없음")}. 측면 카메라 시야는 별도");

        Collider[] colliders = axe.GetComponentsInChildren<Collider>(true);
        bool onlyTeamColliders = colliders.Length == 2 &&
            ((colliders[0] == axe.bladeCollider && colliders[1] == axe.supportCollider) ||
             (colliders[1] == axe.bladeCollider && colliders[0] == axe.supportCollider));
        record("CH5.reveal.colliderCount", onlyTeamColliders,
            $"도끼 자식 포함 콜라이더 {colliders.Length}개; 팀 날/손잡이 2개 외 중복 없음");

        float[] phases = { 0f, 0.5f, 0.25f };
        bool threeTargets = true;
        string phaseDetails = "";
        for (int i = 0; i < 3; i++)
        {
            Transform root = Named(all, $"S5_Axe_{i + 1}");
            AxeTrap item = root != null ? root.GetComponent<AxeTrap>() : null;
            if (item == null) { threeTargets = false; continue; }
            float fraction = Mathf.Repeat(item.startPhase, Mathf.Max(0.05f, item.period)) / Mathf.Max(0.05f, item.period);
            float angle = -item.maxAngleDegrees * Mathf.Cos(2f * Mathf.PI * fraction);
            threeTargets &= Mathf.Abs(fraction - phases[i]) < 0.001f && item.localRotationAxis.sqrMagnitude > 0.9f;
            phaseDetails += $" {i + 1}:{fraction:F2}주기→{angle:F1}°";
        }
        record("CH5.reveal.phaseTargets", threeTargets,
            $"팀 공개 RotationAt(startPhase) 기준;{phaseDetails}; 축/위상과 생성 설정 비교");

        // 비활성 Rigidbody.rotation은 아직 물리 포즈가 동기화되지 않아 메뉴의 배치 회전
        // (Transform의 직렬화 값)과 다를 수 있다. 팀/맵 Awake는 활성화 후의 회전을 읽는다.
        Quaternion baseRotation = axe.transform.rotation;
        float inactivePoseDifference = Quaternion.Angle(baseRotation, axe.GetComponent<Rigidbody>().rotation);
        record("CH5.reveal.inactivePose", true,
            $"활성화 전 직렬화 Transform↔비활성 Rigidbody 회전차 {inactivePoseDifference:F2}°; 기대 위상 기준은 Transform 회전");
        Quaternion expectedStart = TeamStartRotation(axe, baseRotation);
        Camera gameCamera = Camera.main;
        PlayerFollowCamera follow = gameCamera != null ? gameCamera.GetComponent<PlayerFollowCamera>() : null;
        Transform previousTarget = follow != null ? follow.target : null;
        float? previousViewYaw = PlayerFollowCamera.ViewYaw;
        string viewFolder = Path.Combine(Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName,
            "검증", "N04_axeviews_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        bool viewReady = gameCamera != null && follow != null && gameCamera.enabled && gameCamera.gameObject.activeInHierarchy;
        if (viewReady)
        {
            Directory.CreateDirectory(viewFolder);
            PlayerFollowCamera.SetActiveTarget(player.transform);
            PlayerFollowCamera.FaceDirection(reveal.RestPosition - zone.bounds.center, player.transform);
            PlayerFollowCamera.SnapToTarget(player.transform);
        }
        record("CH5.reveal.camera", viewReady,
            viewReady ? $"기존 Main Camera와 PlayerFollowCamera.FaceDirection으로 접근 방향 정렬; PNG 경로={viewFolder}; API fixture 시야, IMGUI/사용자 입력·완주 아님" : "기존 활성 Main Camera/PlayerFollowCamera 없음");

        Vector3 original = body.position, oldVelocity = body.velocity, oldAngular = body.angularVelocity;
        bool hit = false;
        UnityAction<GameObject> onHit = root => { if (root == player.gameObject) hit = true; };
        if (axe.OnHazardHit != null) axe.OnHazardHit.AddListener(onHit);
        try
        {
            Vector3 start = zone.transform.TransformPoint(zone.center - Vector3.forward * (zone.size.z * 0.5f + 1f));
            Vector3 direction = zone.transform.forward;
            Put(body, start);
            yield return new WaitForFixedUpdate();

            bool sawWarning = false, sawDescent = false, sawWait = false, sawWarmup = false, sawRunning = false;
            bool collisionBeforeRest = false, prematureVisual = false;
            float lowestPivot = float.PositiveInfinity;
            float maxDescentStep = 0f, landingError = float.NaN, firstTeamStep = float.NaN;
            float maxTeamStep = 0f, minPhaseError = float.PositiveInfinity;
            Quaternion previousRotation = axe.transform.rotation;
            bool warningPng = false, descentPng = false, runningPng = false;
            for (int step = 0; step < 58; step++)
            {
                bool previousDescending = reveal.Phase == Lab_AxeReveal.RevealPhase.Descending;
                Put(body, body.position + direction * 0.14f); // 0.02초 고정 스텝이면 7U/s 상당
                yield return new WaitForFixedUpdate();
                Observe(reveal, ref sawWarning, ref sawDescent, ref sawWait, ref sawWarmup, ref sawRunning);
                float rotationStep = Quaternion.Angle(previousRotation, axe.transform.rotation);
                if (previousDescending || reveal.Phase == Lab_AxeReveal.RevealPhase.Descending)
                    maxDescentStep = Mathf.Max(maxDescentStep, rotationStep);
                if (reveal.ReachedRestAt > 0f && float.IsNaN(landingError))
                    landingError = Quaternion.Angle(axe.transform.rotation, baseRotation);
                if (axe.enabled)
                {
                    maxTeamStep = Mathf.Max(maxTeamStep, rotationStep);
                    minPhaseError = Mathf.Min(minPhaseError, Quaternion.Angle(axe.transform.rotation, expectedStart));
                    if (float.IsNaN(firstTeamStep) && rotationStep > 0.005f && reveal.ReachedRestAt > 0f)
                        firstTeamStep = rotationStep;
                }
                previousRotation = axe.transform.rotation;
                if (viewReady && !warningPng && reveal.Phase == Lab_AxeReveal.RevealPhase.Warning)
                    warningPng = CapturePhase(gameCamera, viewFolder, "warning", record);
                if (viewReady && !descentPng && reveal.Phase == Lab_AxeReveal.RevealPhase.Descending &&
                    Vector3.Distance(axe.transform.position, reveal.RestPosition) < reveal.lift * 0.55f)
                    descentPng = CapturePhase(gameCamera, viewFolder, "descending", record);
                if (trigger.Activated)
                {
                    lowestPivot = Mathf.Min(lowestPivot, axe.transform.position.y);
                    // 실제 Mesh 8꼭짓점과 회전 BoxCollider 8꼭짓점을 각각 검사한다.
                    // 런타임과 같은 두 점 계산을 되풀이하면 비스듬한 날 끝을 놓친다.
                    if (reveal.VisualsShown &&
                        Mathf.Min(BladeBottomY(axe), VisualMinWorldY(axeRoot)) > reveal.revealBelowWorldY + 0.02f)
                        prematureVisual = true;
                    if (reveal.Phase == Lab_AxeReveal.RevealPhase.Descending &&
                        (axe.bladeCollider.enabled || axe.supportCollider.enabled || axe.enabled)) collisionBeforeRest = true;
                }
            }
            bool entered = trigger.Activated && axe.gameObject.activeSelf && sawWarning && sawDescent;
            record("CH5.reveal.triggerAndWarning", entered,
                $"구역 밖→58×0.14U 연속 이동({0.14f / Time.fixedDeltaTime:F1}U/s 상당), 물리 Trigger={trigger.Activated}, 경고={sawWarning}, 하강={sawDescent}; 직접 이벤트 호출 없음");
            record("CH5.reveal.noSuddenHit", entered && !hit && !collisionBeforeRest,
                $"빠른 첫 접근 중 피격={hit}, 하강 중 날/손잡이 콜라이더·팀 운동 조기 활성={collisionBeforeRest}; 최저 피벗 y={lowestPivot:F2}");

            bool otherTrapsHidden = true;
            string[] others = { "S5_Axe_2", "S5_Axe_3", "S5_Spike_1", "S5_Spike_2" };
            foreach (string name in others)
            {
                Transform other = Named(all, name);
                otherTrapsHidden &= other != null && !other.gameObject.activeSelf;
            }
            record("CH5.reveal.otherTraps", otherTrapsHidden,
                "도끼1 경고·하강 중 다른 도끼2/3·가시1/2는 초기 비활성 유지");

            Put(body, start); // 실제 맵 카메라가 도끼 앞 진입로를 보도록 안전한 위치에서 관찰
            for (int step = 0; step < 150 && reveal.Phase != Lab_AxeReveal.RevealPhase.Running; step++)
            {
                yield return new WaitForFixedUpdate();
                Observe(reveal, ref sawWarning, ref sawDescent, ref sawWait, ref sawWarmup, ref sawRunning);
                float rotationStep = Quaternion.Angle(previousRotation, axe.transform.rotation);
                if (axe.enabled)
                {
                    maxTeamStep = Mathf.Max(maxTeamStep, rotationStep);
                    minPhaseError = Mathf.Min(minPhaseError, Quaternion.Angle(axe.transform.rotation, expectedStart));
                    if (float.IsNaN(firstTeamStep) && rotationStep > 0.005f)
                        firstTeamStep = rotationStep;
                }
                previousRotation = axe.transform.rotation;
            }
            bool sequence = sawWarning && sawDescent && sawWait && sawWarmup && sawRunning &&
                            (axe.transform.position - reveal.RestPosition).sqrMagnitude < 0.0001f &&
                            reveal.ReachedRestAt > reveal.StartedAt && reveal.TrapEnabledAt >= reveal.ReachedRestAt &&
                            axe.bladeCollider.enabled && axe.supportCollider.enabled && axe.enabled &&
                            reveal.VisualsShown && !reveal.warningLight.enabled;
            record("CH5.reveal.visualEmergence", entered && reveal.VisualsShown && !prematureVisual,
                $"회전 날 BoxCollider·실제 Mesh 8꼭짓점 최저점과 천장 밑면 {reveal.revealBelowWorldY:F2} 대조; 조기 노출={prematureVisual}, 최종 표시={reveal.VisualsShown}");
            record("CH5.reveal.sequence", sequence,
                $"경고→하강→빈자리대기→팀 예고→진자: {sawWarning}/{sawDescent}/{sawWait}/{sawWarmup}/{sawRunning}; 등장 {(reveal.ReachedRestAt - reveal.StartedAt):F2}s, 팀운동 시작 {(reveal.TrapEnabledAt - reveal.StartedAt):F2}s, 최종피벗 {axe.transform.position}");
            if (viewReady && reveal.Phase == Lab_AxeReveal.RevealPhase.Running)
                runningPng = CapturePhase(gameCamera, viewFolder, "running", record);
            record("CH5.reveal.cameraViews", viewReady && warningPng && descentPng && runningPng,
                $"실제 Main Camera.Render 캡처 경고/하강 중간/진자={warningPng}/{descentPng}/{runningPng}; {viewFolder}; API fixture 시야, IMGUI/사용자 입력·완주 아님");

            Quaternion rotation = axe.transform.rotation;
            float maxAngle = 0f;
            for (int step = 0; step < 75; step++)
            {
                yield return new WaitForFixedUpdate();
                maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(rotation, axe.transform.rotation));
                float rotationStep = Quaternion.Angle(previousRotation, axe.transform.rotation);
                if (axe.enabled)
                {
                    maxTeamStep = Mathf.Max(maxTeamStep, rotationStep);
                    minPhaseError = Mathf.Min(minPhaseError, Quaternion.Angle(axe.transform.rotation, expectedStart));
                    if (float.IsNaN(firstTeamStep) && rotationStep > 0.005f)
                        firstTeamStep = rotationStep;
                }
                previousRotation = axe.transform.rotation;
            }
            float teamStepLimit = axe.maxAngleDegrees * 2f * Mathf.PI /
                                  Mathf.Max(0.05f, axe.period) * Time.fixedDeltaTime + 0.1f;
            record("CH5.reveal.rotationBlend", sequence && !float.IsNaN(landingError) &&
                   maxDescentStep < 4f && landingError < 0.5f && !float.IsNaN(firstTeamStep) &&
                   maxTeamStep <= Mathf.Min(4f, teamStepLimit) && minPhaseError < 0.5f,
                $"중립 하강 최대 회전 스텝 {maxDescentStep:F2}°, 착지시 중립 오차 {landingError:F2}°, " +
                $"팀 첫 스텝 {firstTeamStep:F2}°·전체 최대 {maxTeamStep:F2}°(각속도 한계 {teamStepLimit:F2}°, 절대 4°), " +
                $"팀 시작위상 최소 오차 {minPhaseError:F2}°; 비활성 RB↔Transform 차이 {inactivePoseDifference:F2}°; " +
                "회전 연결·위상 검사이며 밸런스 확정값 아님");
            record("CH5.reveal.teamSwing", sequence && maxAngle > 1f,
                $"등장 후 팀 AxeTrap 원래 진자 회전 최대 {maxAngle:F1}°/75스텝; AxeTrap 코드 불변");
        }
        finally
        {
            if (viewReady)
            {
                PlayerFollowCamera.SetActiveTarget(previousTarget);
                if (previousViewYaw.HasValue)
                    PlayerFollowCamera.FaceDirection(Quaternion.Euler(0f, previousViewYaw.Value, 0f) * Vector3.forward, previousTarget);
                if (previousTarget != null) PlayerFollowCamera.SnapToTarget(previousTarget);
            }
            if (axe.OnHazardHit != null) axe.OnHazardHit.RemoveListener(onHit);
            Put(body, original);
            body.velocity = oldVelocity;
            body.angularVelocity = oldAngular;
        }
    }

    private static void Observe(Lab_AxeReveal reveal, ref bool warning, ref bool descending, ref bool waiting,
        ref bool warmup, ref bool running)
    {
        warning |= reveal.Phase == Lab_AxeReveal.RevealPhase.Warning;
        descending |= reveal.Phase == Lab_AxeReveal.RevealPhase.Descending;
        waiting |= reveal.Phase == Lab_AxeReveal.RevealPhase.WaitingForClearance;
        warmup |= reveal.Phase == Lab_AxeReveal.RevealPhase.TeamWarmup;
        running |= reveal.Phase == Lab_AxeReveal.RevealPhase.Running;
    }

    private static float VisualMinWorldY(Transform root)
    {
        float bottom = float.PositiveInfinity;
        int count = 0;
        foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh.sharedMesh == null) continue;
            Bounds b = mesh.sharedMesh.bounds;
            Matrix4x4 m = mesh.transform.localToWorldMatrix;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
                        bottom = Mathf.Min(bottom, m.MultiplyPoint3x4(corner).y);
                    }
            count++;
        }
        return count > 0 ? bottom : float.NaN;
    }

    private static float BladeBottomY(AxeTrap axe)
    {
        BoxCollider blade = axe.bladeCollider;
        float bottom = float.PositiveInfinity;
        Vector3 half = blade.size * 0.5f;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    bottom = Mathf.Min(bottom, axe.transform.TransformPoint(
                        blade.center + Vector3.Scale(half, new Vector3(x, y, z))).y);
        return bottom;
    }

    private static Quaternion TeamStartRotation(AxeTrap axe, Quaternion basis)
    {
        float period = Mathf.Max(0.05f, axe.period);
        float phase = Mathf.Repeat(axe.startPhase, period) / period;
        float angle = -axe.maxAngleDegrees * Mathf.Cos(2f * Mathf.PI * phase);
        return basis * Quaternion.AngleAxis(angle, axe.localRotationAxis);
    }

    private static bool CapturePhase(Camera camera, string folder, string phase,
        Action<string, bool, string> record)
    {
        string path = Path.Combine(folder, phase + ".png");
        try
        {
            CaptureExistingCamera(camera, path);
            record("CH5.reveal.png." + phase, true, path + " (실제 맵 카메라 API fixture 시야)");
            return true;
        }
        catch (Exception ex)
        {
            record("CH5.reveal.png." + phase, false, "PNG 저장 실패: " + ex.Message);
            return false;
        }
    }

    private static void CaptureExistingCamera(Camera camera, string path)
    {
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.Destroy(image);
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
        }
    }

    private static Transform Named(Transform[] all, string name)
    { foreach (Transform t in all) if (t != null && t.name == name) return t; return null; }

    private static PlayerMover FirstBody(PlayerMover[] players)
    { if (players != null) foreach (PlayerMover p in players) if (p != null && p.GetComponent<Rigidbody>() != null) return p; return null; }

    private static void Put(Rigidbody body, Vector3 position)
    { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.position = position; Physics.SyncTransforms(); }
}
#endif
