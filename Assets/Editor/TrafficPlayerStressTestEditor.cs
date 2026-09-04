using System;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

/// <summary>
/// ClientSim 원격 플레이어 80명의 배치를 바꾸며 교통 시스템의 Profiler 구간을 기록한다.
/// </summary>
/// <remarks>
/// WAIT, DIST, CROWD 상태를 각각 600프레임씩 세 번 반복한다. 각 상태의 첫
/// 60프레임은 워밍업이며 구간 정보는 Temp/TrafficPlayerStressPhases.csv에 기록한다.
/// </remarks>
[InitializeOnLoad]
public static class TrafficPlayerStressTestEditor
{
    private const int ExpectedRemotePlayers = 80;
    private const int MainLaneCount = 6;
    private const int ProfileStateDurationFrames = 600;
    private const int ProfileWarmupFrames = 60;
    private const int ProfileCycleCount = 3;
    private const string ProfileCyclePendingKey =
        "ShinjukuTraffic.V1.ProfileCyclePending";

    private enum ProfileState
    {
        Wait,
        Distributed,
        Crowd
    }

    private static readonly CustomSampler WaitWarmupSampler =
        CustomSampler.Create("TrafficStress.WAIT.Warmup");
    private static readonly CustomSampler WaitMeasureSampler =
        CustomSampler.Create("TrafficStress.WAIT.Measure");
    private static readonly CustomSampler DistributedWarmupSampler =
        CustomSampler.Create("TrafficStress.DIST.Warmup");
    private static readonly CustomSampler DistributedMeasureSampler =
        CustomSampler.Create("TrafficStress.DIST.Measure");
    private static readonly CustomSampler CrowdWarmupSampler =
        CustomSampler.Create("TrafficStress.CROWD.Warmup");
    private static readonly CustomSampler CrowdMeasureSampler =
        CustomSampler.Create("TrafficStress.CROWD.Measure");
    private static readonly CustomSampler TransitionSampler =
        CustomSampler.Create("TrafficStress.Transition");

    private static readonly Vector3[] MainLaneStarts =
    {
        new Vector3(132.304622f, 7f, 6.279638f),
        new Vector3(132.936111f, 7f, 3.177539f),
        new Vector3(133.613187f, 7f, -0.037266f),
        new Vector3(-111.328974f, 6.979349f, -5.073514f),
        new Vector3(-111.738974f, 6.929349f, -1.533514f),
        new Vector3(-112.108974f, 6.889349f, 1.716486f)
    };

    private static readonly Vector3[] MainLaneEnds =
    {
        new Vector3(-127.507997f, 6.98f, -12.513869f),
        new Vector3(-128.131423f, 6.98f, -16.213989f),
        new Vector3(-127.619872f, 6.98f, -19.592125f),
        new Vector3(139.935544f, 7.551635f, 12.852523f),
        new Vector3(139.627024f, 7.513578f, 15.508408f),
        new Vector3(139.300353f, 7.478677f, 18.380920f)
    };

    private static double waitUntil;
    private static double nextSpawnTime;
    private static bool profileCycleRunning;
    private static ProfileState profileState;
    private static int profileCycleIndex;
    private static int profileStateStartFrame;
    private static int profileStateStartProfilerFrame;
    private static int profileMeasureProfilerFrame;
    private static string profileStateStartUtc;
    private static string profileLogPath;
    private static string profileStatusText;
    private static bool profileStatusWarmup;

    static TrafficPlayerStressTestEditor()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        SceneView.duringSceneGui -= DrawProfileStatus;
        SceneView.duringSceneGui += DrawProfileStatus;

        if (EditorPrefs.GetBool(ProfileCyclePendingKey, false))
        {
            BeginPlayerPreparation();
        }
    }

    /// <summary>
    /// Play Mode에서 원격 플레이어 준비를 시작하고 전체 측정 주기를 실행한다.
    /// </summary>
    [MenuItem(
        "Tools/Traffic V1 Test/Profiler/Start WAIT-DIST-CROWD Cycle _F8"
    )]
    public static void StartProfileCycleFromMenu()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Traffic Player Test",
                "Play Mode에서 실행하세요.",
                "OK"
            );
            return;
        }

        StopProfileCycle(false);
        EditorPrefs.SetBool(ProfileCyclePendingKey, true);
        BeginPlayerPreparation();
    }

    /// <summary>
    /// 진행 중인 측정을 중단하고 완료된 구간을 부분 결과로 기록한다.
    /// </summary>
    [MenuItem(
        "Tools/Traffic V1 Test/Profiler/Stop Profile Cycle #F8"
    )]
    public static void StopProfileCycleFromMenu()
    {
        StopProfileCycle(true);
    }

    private static void OnPlayModeStateChanged(
        PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            StopProfileCycle(false);
            EditorPrefs.SetBool(ProfileCyclePendingKey, false);
        }
    }

    private static void BeginPlayerPreparation()
    {
        waitUntil = EditorApplication.timeSinceStartup + 60d;
        nextSpawnTime = 0d;
        EditorApplication.update -= TryPreparePlayers;
        EditorApplication.update += TryPreparePlayers;
    }

    private static void TryPreparePlayers()
    {
        if (!Application.isPlaying)
        {
            if (EditorApplication.timeSinceStartup >= waitUntil)
            {
                EditorApplication.update -= TryPreparePlayers;
                EditorPrefs.SetBool(ProfileCyclePendingKey, false);
            }

            return;
        }

        int remotePlayerCount = CountRemotePlayers();

        if (remotePlayerCount < ExpectedRemotePlayers)
        {
            if (ClientSimMain.HasInstance() &&
                EditorApplication.timeSinceStartup >= nextSpawnTime)
            {
                ClientSimMain.SpawnRemotePlayer(
                    "Traffic Test V1 " + (remotePlayerCount + 1)
                );

                nextSpawnTime =
                    EditorApplication.timeSinceStartup + 0.08d;
            }

            if (EditorApplication.timeSinceStartup >= waitUntil)
            {
                EditorApplication.update -= TryPreparePlayers;
                EditorPrefs.SetBool(ProfileCyclePendingKey, false);
                Debug.LogError(
                    "[Traffic Player Test V1] Could not create " +
                    ExpectedRemotePlayers + " remote players."
                );
            }

            return;
        }

        EditorApplication.update -= TryPreparePlayers;
        StartProfileCycle();
    }

    private static void StartProfileCycle()
    {
        if (!Application.isPlaying ||
            CountRemotePlayers() < ExpectedRemotePlayers)
        {
            return;
        }

        profileLogPath = Path.GetFullPath(
            Path.Combine(
                Application.dataPath,
                "../Temp/TrafficPlayerStressPhases.csv"
            )
        );

        Directory.CreateDirectory(
            Path.GetDirectoryName(profileLogPath)
        );

        File.WriteAllText(
            profileLogPath,
            "cycle,state,status,unity_start_frame," +
            "unity_measure_start_frame,unity_end_frame," +
            "profiler_start_frame,profiler_measure_start_frame," +
            "profiler_end_frame,remote_players,utc_start,utc_end" +
            Environment.NewLine
        );

        EditorPrefs.SetBool(ProfileCyclePendingKey, false);
        profileCycleRunning = true;
        profileCycleIndex = 0;

        EditorApplication.update -= UpdateProfileCycle;
        EditorApplication.update += UpdateProfileCycle;

        BeginProfileState(ProfileState.Wait);

        Debug.Log(
            "[Traffic Player Test V1] Profiler cycle started. " +
            "WAIT/DIST/CROWD, 600 frames each, 3 cycles. " +
            "Phase log: " + profileLogPath
        );
    }

    private static void UpdateProfileCycle()
    {
        if (!profileCycleRunning)
        {
            EditorApplication.update -= UpdateProfileCycle;
            return;
        }

        if (!Application.isPlaying)
        {
            StopProfileCycle(false);
            return;
        }

        int currentFrame = Time.frameCount;
        int elapsedFrames = Mathf.Max(
            0,
            currentFrame - profileStateStartFrame
        );

        if (elapsedFrames >= ProfileStateDurationFrames)
        {
            WriteProfileStateLog("COMPLETE", currentFrame);

            if (profileState == ProfileState.Crowd)
            {
                profileCycleIndex++;

                if (profileCycleIndex >= ProfileCycleCount)
                {
                    FinishProfileCycle();
                    return;
                }

                BeginProfileState(ProfileState.Wait);
            }
            else
            {
                BeginProfileState(
                    (ProfileState)((int)profileState + 1)
                );
            }

            return;
        }

        bool warmup = elapsedFrames < ProfileWarmupFrames;

        if (!warmup && profileMeasureProfilerFrame < 0)
        {
            profileMeasureProfilerFrame = GetProfilerFrame();
            profileStatusWarmup = false;
            UpdateProfileStatusText();
            SceneView.RepaintAll();
        }

        CustomSampler sampler = GetStateSampler(
            profileState,
            warmup
        );

        sampler.Begin();
        sampler.End();
    }

    private static void BeginProfileState(ProfileState state)
    {
        profileState = state;
        profileStateStartFrame = Time.frameCount;
        profileStateStartProfilerFrame = GetProfilerFrame();
        profileMeasureProfilerFrame = -1;
        profileStateStartUtc = DateTime.UtcNow.ToString("O");
        profileStatusWarmup = true;

        TransitionSampler.Begin();
        int arrangedCount = ApplyProfileState(state);
        TransitionSampler.End();

        if (arrangedCount < ExpectedRemotePlayers)
        {
            Debug.LogError(
                "[Traffic Player Test V1] Profile cycle stopped: " +
                "only " + arrangedCount +
                " remote players could be arranged."
            );
            StopProfileCycle(false);
            return;
        }

        UpdateProfileStatusText();
        SceneView.RepaintAll();

        Debug.Log(
            "[Traffic Player Test V1] Cycle " +
            (profileCycleIndex + 1) + "/" +
            ProfileCycleCount + " entered " +
            GetStateName(state) +
            " at Unity frame " + profileStateStartFrame +
            ". Frames 0-" + (ProfileWarmupFrames - 1) +
            " are warmup."
        );
    }

    private static int ApplyProfileState(ProfileState state)
    {
        switch (state)
        {
            case ProfileState.Wait:
                return ArrangeInWaitingArea();

            case ProfileState.Distributed:
                return ArrangeAcrossMainLanes();

            case ProfileState.Crowd:
                return ArrangeAtSinglePoint(
                    TrafficLaneDatabase.LaneR2
                );

            default:
                return 0;
        }
    }

    private static void WriteProfileStateLog(
        string status,
        int endFrame)
    {
        if (string.IsNullOrEmpty(profileLogPath))
        {
            return;
        }

        int remotePlayerCount = CountRemotePlayers();
        string row =
            (profileCycleIndex + 1) + "," +
            GetStateName(profileState) + "," +
            status + "," +
            profileStateStartFrame + "," +
            (profileStateStartFrame + ProfileWarmupFrames) + "," +
            endFrame + "," +
            profileStateStartProfilerFrame + "," +
            profileMeasureProfilerFrame + "," +
            GetProfilerFrame() + "," +
            remotePlayerCount + "," +
            profileStateStartUtc + "," +
            DateTime.UtcNow.ToString("O") +
            Environment.NewLine;

        File.AppendAllText(profileLogPath, row);
    }

    private static void FinishProfileCycle()
    {
        profileCycleRunning = false;
        profileStatusText = null;
        EditorPrefs.SetBool(ProfileCyclePendingKey, false);
        EditorApplication.update -= UpdateProfileCycle;
        SceneView.RepaintAll();

        Debug.Log(
            "[Traffic Player Test V1] Profiler cycle completed. " +
            "Phase log: " + profileLogPath
        );
    }

    private static void StopProfileCycle(bool writePartialState)
    {
        if (profileCycleRunning && writePartialState)
        {
            WriteProfileStateLog("PARTIAL", Time.frameCount);
        }

        bool wasRunning = profileCycleRunning;
        profileCycleRunning = false;
        profileStatusText = null;
        EditorPrefs.SetBool(ProfileCyclePendingKey, false);
        EditorApplication.update -= TryPreparePlayers;
        EditorApplication.update -= UpdateProfileCycle;
        SceneView.RepaintAll();

        if (wasRunning && writePartialState)
        {
            Debug.Log(
                "[Traffic Player Test V1] Profiler cycle stopped. " +
                "Partial phase log: " + profileLogPath
            );
        }
    }

    private static CustomSampler GetStateSampler(
        ProfileState state,
        bool warmup)
    {
        if (state == ProfileState.Wait)
        {
            return warmup
                ? WaitWarmupSampler
                : WaitMeasureSampler;
        }

        if (state == ProfileState.Distributed)
        {
            return warmup
                ? DistributedWarmupSampler
                : DistributedMeasureSampler;
        }

        return warmup
            ? CrowdWarmupSampler
            : CrowdMeasureSampler;
    }

    private static int GetProfilerFrame()
    {
        return ProfilerDriver.lastFrameIndex;
    }

    private static string GetStateName(ProfileState state)
    {
        switch (state)
        {
            case ProfileState.Wait:
                return "WAIT";

            case ProfileState.Distributed:
                return "DIST";

            case ProfileState.Crowd:
                return "CROWD";

            default:
                return "UNKNOWN";
        }
    }

    private static void UpdateProfileStatusText()
    {
        profileStatusText =
            "TRAFFIC V1 PROFILE  " +
            (profileCycleIndex + 1) + "/" +
            ProfileCycleCount + "  |  " +
            GetStateName(profileState) + "  |  " +
            (profileStatusWarmup ? "WARMUP" : "MEASURE");
    }

    private static void DrawProfileStatus(SceneView sceneView)
    {
        if (!profileCycleRunning ||
            string.IsNullOrEmpty(profileStatusText))
        {
            return;
        }

        Handles.BeginGUI();
        GUI.Box(
            new Rect(12f, 12f, 350f, 30f),
            profileStatusText
        );
        Handles.EndGUI();
    }

    private static int ArrangeAcrossMainLanes()
    {
        VRCPlayerApi[] remotePlayers = GetRemotePlayers();

        if (remotePlayers == null || remotePlayers.Length <= 0)
        {
            return 0;
        }

        int rows = Mathf.CeilToInt(
            remotePlayers.Length / (float)MainLaneCount
        );
        int arrangedCount = 0;

        for (int i = 0; i < remotePlayers.Length; i++)
        {
            VRCPlayerApi player = remotePlayers[i];

            if (!Utilities.IsValid(player))
            {
                continue;
            }

            int laneId = i % MainLaneCount;
            int row = i / MainLaneCount;
            float rowT = rows <= 1
                ? 0.5f
                : row / (float)(rows - 1);
            float laneT = Mathf.Lerp(0.18f, 0.82f, rowT);
            Vector3 position = Vector3.Lerp(
                MainLaneStarts[laneId],
                MainLaneEnds[laneId],
                laneT
            );
            Quaternion rotation = Quaternion.LookRotation(
                MainLaneEnds[laneId] - MainLaneStarts[laneId],
                Vector3.up
            );

            if (TeleportClientSimPlayer(
                player,
                position + Vector3.up * 0.05f,
                rotation))
            {
                arrangedCount++;
            }
        }

        if (arrangedCount > 0)
        {
            Physics.SyncTransforms();
        }

        return arrangedCount;
    }

    private static int ArrangeInWaitingArea()
    {
        VRCPlayerApi[] remotePlayers = GetRemotePlayers();

        if (remotePlayers == null || remotePlayers.Length <= 0)
        {
            return 0;
        }

        const int columns = 10;
        const float spacing = 1.5f;
        Vector3 waitingOrigin = new Vector3(
            -6.75f,
            7f,
            -55f
        );
        int arrangedCount = 0;

        for (int i = 0; i < remotePlayers.Length; i++)
        {
            VRCPlayerApi player = remotePlayers[i];

            if (!Utilities.IsValid(player))
            {
                continue;
            }

            int column = i % columns;
            int row = i / columns;
            Vector3 position = waitingOrigin + new Vector3(
                (column - 4.5f) * spacing,
                0f,
                -row * spacing
            );

            if (TeleportClientSimPlayer(
                player,
                position,
                Quaternion.identity))
            {
                arrangedCount++;
            }
        }

        if (arrangedCount > 0)
        {
            Physics.SyncTransforms();
        }

        return arrangedCount;
    }

    private static int ArrangeAtSinglePoint(int laneId)
    {
        if (laneId < 0 || laneId >= MainLaneCount)
        {
            return 0;
        }

        VRCPlayerApi[] remotePlayers = GetRemotePlayers();

        if (remotePlayers == null || remotePlayers.Length <= 0)
        {
            return 0;
        }

        Vector3 laneDirection =
            (MainLaneEnds[laneId] - MainLaneStarts[laneId]).normalized;
        Vector3 position = Vector3.Lerp(
            MainLaneStarts[laneId],
            MainLaneEnds[laneId],
            0.5f
        ) + Vector3.up * 0.05f;
        Quaternion rotation = Quaternion.LookRotation(
            laneDirection,
            Vector3.up
        );
        int arrangedCount = 0;

        for (int i = 0; i < remotePlayers.Length; i++)
        {
            VRCPlayerApi player = remotePlayers[i];

            if (!Utilities.IsValid(player))
            {
                continue;
            }

            if (TeleportClientSimPlayer(
                player,
                position,
                rotation))
            {
                arrangedCount++;
            }
        }

        if (arrangedCount > 0)
        {
            Physics.SyncTransforms();
        }

        return arrangedCount;
    }

    private static int CountRemotePlayers()
    {
        VRCPlayerApi[] players = GetAllPlayers();

        if (players == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < players.Length; i++)
        {
            if (Utilities.IsValid(players[i]) &&
                !players[i].isLocal)
            {
                count++;
            }
        }

        return count;
    }

    private static VRCPlayerApi[] GetRemotePlayers()
    {
        VRCPlayerApi[] players = GetAllPlayers();

        if (players == null)
        {
            return null;
        }

        int remoteCount = 0;

        for (int i = 0; i < players.Length; i++)
        {
            if (Utilities.IsValid(players[i]) &&
                !players[i].isLocal)
            {
                remoteCount++;
            }
        }

        VRCPlayerApi[] remotePlayers =
            new VRCPlayerApi[remoteCount];
        int writeIndex = 0;

        for (int i = 0; i < players.Length; i++)
        {
            if (!Utilities.IsValid(players[i]) ||
                players[i].isLocal)
            {
                continue;
            }

            remotePlayers[writeIndex++] = players[i];
        }

        return remotePlayers;
    }

    private static VRCPlayerApi[] GetAllPlayers()
    {
        if (!Application.isPlaying ||
            !ClientSimMain.HasInstance())
        {
            return null;
        }

        int playerCount = VRCPlayerApi.GetPlayerCount();

        if (playerCount <= 0)
        {
            return null;
        }

        VRCPlayerApi[] players =
            new VRCPlayerApi[playerCount];

        VRCPlayerApi.GetPlayers(players);
        return players;
    }

    private static bool TeleportClientSimPlayer(
        VRCPlayerApi player,
        Vector3 position,
        Quaternion rotation)
    {
        ClientSimPlayer clientSimPlayer =
            player.GetClientSimPlayer();

        if (clientSimPlayer == null)
        {
            return false;
        }

        ClientSimPlayerController controller =
            clientSimPlayer.GetPlayerController();

        if (controller != null)
        {
            controller.Teleport(position, rotation, false);
        }
        else
        {
            clientSimPlayer.transform.SetPositionAndRotation(
                position,
                rotation
            );
        }

        return true;
    }
}
