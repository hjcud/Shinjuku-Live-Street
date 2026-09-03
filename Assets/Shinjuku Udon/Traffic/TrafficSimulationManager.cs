using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TrafficSimulationManager : UdonSharpBehaviour
{
    private const int NetworkSlotCapacity = 16;
    private const int MaximumLaneChangeRules = 16;
    private const int ManeuverPathSampleCount = 49;

    private const float PositionQuantum = 0.02f;
    private const float SpeedQuantum = 0.05f;
    private const float AccelerationQuantum = 0.1f;
    private const float MinimumRemoteRenderDelay = 0.35f;
    private const float MaximumRemoteRenderDelay = 1.25f;
    private const float RemoteDelayAdjustmentSpeed = 0.05f;
    private const float RemoteExtrapolationLimit = 0.15f;

    private const int ActiveBit = 1;
    private const int LaneShift = 1;
    private const int PositionShift = 4;
    private const int SpeedShift = 21;
    private const int SignalCommitBit = 1 << 30;
    // Bit 30 is only used by SignalCommitBit while no lane change is active.
    // Reuse it during a normal lane change so every client renders the same
    // short emergency path without increasing the synchronized payload.
    private const int LaneChangeEmergencyManeuverBit = 1 << 30;
    private const int LaneChangeActiveBit = 1 << 19;
    private const int LaneChangeTargetShift = 20;
    private const int LaneChangeProgressShift = 23;
    private const int RecoveryDistanceShift = 29;
    private const int VehicleSpawnGenerationBit =
        unchecked((int)0x80000000);
    private const int LaneChangeReverseManeuverBit =
        unchecked((int)0x80000000);
    private const int LaneChangePlayerBlock = 1;
    private const int LaneChangeVehicleBlock = 2;
    private const int LaneChangeRoadBoundaryBlock = 4;
    private const float MinimumReverseRecoveryDistance = 0.6f;
    private const float RecoveryForwardPreparationFactor = 0.65f;
    private const float RecoveryPhaseCompletionPositionTolerance = 0.08f;
    private const float RecoveryPhaseCompletionRotationTolerance = 2f;
    private const float RecoveryPhaseCompletionPhysicalMargin = 0.02f;

    public Vector3 GetCollisionVehicleVelocity(
        Transform collidedTransform)
    {
        if (collidedTransform == null ||
            vehicleRoots == null ||
            visualActive == null ||
            vehicleCollisionVelocities == null)
        {
            return Vector3.zero;
        }

        int count = Mathf.Min(
            vehicleRoots.Length,
            Mathf.Min(
                visualActive.Length,
                vehicleCollisionVelocities.Length
            )
        );

        for (int i = 0; i < count; i++)
        {
            Transform vehicleRoot = vehicleRoots[i];
            if (vehicleRoot == null || !visualActive[i])
            {
                continue;
            }

            Transform current = collidedTransform;
            for (int depth = 0;
                depth < 12 && current != null;
                depth++)
            {
                if (current == vehicleRoot)
                {
                    return vehicleCollisionVelocities[i];
                }

                current = current.parent;
            }
        }

        return Vector3.zero;
    }

    [Header("References")]
    public TrafficLaneDatabase laneDatabase;
    public ShinhoTime mainSignal;

    [Tooltip("다점 기동 중 차량 뒤쪽이 벗어나면 안 되는 도로 범위입니다. TEST.unity의 RoadArea BoxCollider를 사용합니다.")]
    public BoxCollider recoveryRoadArea;

    [Tooltip("Truck을 슬롯 0에 두고 CHR부터 Zest까지 순서대로 등록합니다.")]
    public Transform[] vehicleRoots = new Transform[0];

    [Header("Population")]
    [Range(1, NetworkSlotCapacity)]
    public int targetActiveVehicles = 10;

    public int randomSeed = 12345;

    [Header("Simulation")]
    [Range(0.05f, 0.2f)]
    public float simulationInterval = 0.1f;

    [Range(1, 8)]
    public int maximumStepsPerFrame = 4;

    [Header("Normal Car")]
    public float vehicleLength = 4.5f;
    public float vehicleWidth = 1.85f;
    public float cruiseSpeed = 8f;
    public float minimumSpeedFactor = 0.85f;
    public float maximumSpeedFactor = 1.05f;

    public float acceleration = 2.5f;
    public float deceleration = 5f;
    public float emergencyDeceleration = 9f;

    public float minimumGap = 1.5f;
    public float timeHeadway = 0.8f;

    [Header("Truck - Slot 0")]
    [Range(0, NetworkSlotCapacity - 1)]
    public int truckSlotIndex;

    public float truckVehicleLength = 12f;
    public float truckVehicleWidth = 2.5f;
    public float truckCruiseSpeed = 6.5f;
    public float truckAcceleration = 1.4f;
    public float truckDeceleration = 3.5f;
    public float truckEmergencyDeceleration = 7f;
    public float truckMinimumGap = 2.5f;
    public float truckTimeHeadway = 1.2f;

    [Header("Spawning")]
    public float spawnClearance = 8f;
    public float truckSpawnClearance = 12f;
    public float spawnInitialSpeed = 3f;

    [Tooltip("한 차선이 신호 대기 차량을 독점하지 않도록 동시에 배치할 최대 차량 수입니다. 차선 변경 중인 차량은 출발/도착 차선 양쪽에 포함됩니다.")]
    [Range(1, 6)]
    public int maximumVehiclesPerSpawnLane = 2;

    [Tooltip("빈 슬롯을 한꺼번에 채우지 않고 한 대씩 보충하는 간격입니다.")]
    [Range(0.15f, 2f)]
    public float respawnInterval = 0.4f;

    [Tooltip("첫 권한자가 새 교통 상태를 만들 때 차량을 차선 전체에 분산 배치합니다.")]
    public bool distributeVehiclesOnStartup = true;

    [Range(5f, 40f)]
    public float startupDistributionEdgePadding = 18f;

    [Range(2f, 20f)]
    public float startupStopLineClearance = 10f;
    
    [Header("Signal")]
    public float stopLineFrontBuffer = 1f;
    public float signalComfortDeceleration = 3f;
    public float yellowDecisionMargin = 2f;

    [Header("Player Avoidance")]
    public float playerFrontBuffer = 2f;
    public float playerComfortDeceleration = 4f;

    [Tooltip("권한자만 실행하는 플레이어 물리 검사입니다. 신호는 베이크된 정지선과 동기화된 신호 상태로 처리하고, 원격 클라이언트는 동기화된 차량 상태만 표시합니다.")]
    public bool enableAuthorityPhysicsObstacles = true;

    [Tooltip("Player(9), PlayerLocal(10), 고정 장애물 ObstacleCollider(25)를 포함합니다. 신호와 차량 간 판정은 각각 정지선/차선 좌표 계산을 사용합니다.")]
    public int authorityObstacleLayerMask =
        (1 << 9) | (1 << 10) | (1 << 25);

    [Tooltip("한 렌더 프레임에 플레이어 감지를 갱신할 차량 수입니다. 차량들을 순환하며 분산 처리합니다.")]
    [Range(1, 4)]
    public int authorityObstacleVehiclesPerFrame = 2;

    [Tooltip("일반 주행 전방 BoxCast가 확보할 최소 거리입니다.")]
    [Range(2f, 15f)]
    public float minimumObstacleLookAhead = 5f;

    [Tooltip("원격 플레이어 위치 지연을 보완하기 위해 현재 속도에 곱하는 추가 예측 시간입니다.")]
    [Range(0f, 0.75f)]
    public float obstacleNetworkLookAheadTime = 0.35f;

    [Tooltip("물리 검사 상자의 높이입니다. 플레이어 캡슐과 승용차/트럭 장애물을 함께 감지합니다.")]
    [Range(0.8f, 3f)]
    public float authorityObstacleCastHeight = 1.6f;

    [Tooltip("차량 기준점에서 물리 검사 상자 중심까지의 위쪽 거리입니다.")]
    [Range(0.3f, 1.5f)]
    public float authorityObstacleCastVerticalOffset = 0.8f;

    [Tooltip("실제 플레이어 캡슐과 차량 차체 사이에 추가하는 수평 안전 여유입니다.")]
    [Range(0f, 0.5f)]
    public float authorityObstacleSafetyMargin = 0.2f;

    [Tooltip("플레이어 점유가 순간적으로 사라져도 마지막 정지점을 유지하는 시간입니다.")]
    [Range(0.1f, 2f)]
    public float playerObstacleReleaseHold = 0.6f;

    [Tooltip("사람이나 물리 장애물 때문에 완전히 멈춘 차량이 출발을 검토하기 위해 먼저 확보해야 하는 전방 거리입니다.")]
    [Range(0.5f, 4f)]
    public float physicalObstacleRestartClearance = 1.5f;

    [Tooltip("확보된 전방 공간이 깜빡이지 않고 유지되어야 하는 시간입니다. 이후 Stop And Go Reaction의 재출발 반응 시간이 적용됩니다.")]
    [Range(0.1f, 1f)]
    public float physicalObstacleClearConfirmationTime = 0.35f;

    [Tooltip("차선 변경 중 차체가 플레이어를 통과하지 않도록 다음 이동 구간을 월드 좌표에서 검사합니다.")]
    public bool enableLaneChangePlayerSweep = true;

    [Tooltip("차선 변경 차체와 플레이어 사이에 추가하는 안전 여유입니다.")]
    [Range(0.05f, 0.5f)]
    public float laneChangePlayerSafetyMargin = 0.2f;

    [Tooltip("현재 위치부터 다음 시뮬레이션 위치까지 검사할 차체 자세 수입니다.")]
    [Range(2, 5)]
    public int laneChangeObstacleSweepSamples = 3;

    [Tooltip("차선 변경 중 다른 차량의 회전 차체와 다음 이동 구간이 겹치는지 검사합니다.")]
    public bool enableLaneChangeVehicleSweep = true;

    [Tooltip("차선 변경 차량끼리 또는 목표 차선 차량과 유지할 추가 차체 여유입니다.")]
    [Range(0.05f, 0.75f)]
    public float laneChangeVehicleSafetyMargin = 0.25f;

    [Header("Stop And Go Reaction")]
    [Range(0.2f, 2f)]
    public float obstacleRestartDelayMinimum = 0.6f;

    [Range(0.2f, 2.5f)]
    public float obstacleRestartDelayMaximum = 1.1f;

    [Range(0.05f, 0.5f)]
    public float stoppedSpeedThreshold = 0.15f;

    [Tooltip("정지 목표에 가까워졌을 때 저속 수렴을 시작하는 거리입니다.")]
    [Range(0.5f, 2.5f)]
    public float stopSmoothingDistance = 1.2f;

    [Tooltip("마지막 정지 구간에서 남은 거리를 속도로 변환하는 시간 상수입니다.")]
    [Range(0.25f, 1f)]
    public float stopSmoothingTime = 0.45f;

    [Tooltip("이 거리와 속도보다 작아지면 눈에 띄지 않는 범위에서 정확한 정지 위치로 고정합니다.")]
    [Range(0.005f, 0.1f)]
    public float stopSnapDistance = 0.025f;

    [Range(0.01f, 0.2f)]
    public float stopSnapSpeed = 0.06f;

    [Header("Vehicle Audio")]
    [Range(0f, 1f)]
    public float maximumEngineVolume = 0.25f;

    public float idleEnginePitch = 0.85f;
    public float maximumEnginePitch = 1.1f;

    [Range(0f, 1f)]
    public float truckMaximumEngineVolume = 0.22f;

    public float truckIdleEnginePitch = 0.7f;
    public float truckMaximumEnginePitch = 0.95f;

    [Header("Wheels")]
    public float wheelRadius = 0.32f;
    public float truckWheelRadius = 0.52f;

    [Tooltip("경로 곡률에 맞춰 앞바퀴가 꺾일 수 있는 최대 시각 조향각입니다.")]
    [Range(20f, 40f)]
    public float maximumFrontWheelSteeringAngle = 34f;

    [Tooltip("앞바퀴가 목표 조향각을 따라가는 초당 회전 속도입니다.")]
    [Range(90f, 360f)]
    public float frontWheelSteeringResponse = 240f;

    [Header("Motion Visual Update")]
    [Tooltip("엔진음 볼륨과 피치를 갱신하는 간격입니다.")]
    [Range(0.05f, 0.25f)]
    public float audioVisualUpdateInterval = 0.1f;

    [Header("Lane Change - Normal Cars")]
    public bool enableLaneChanges = true;

    [Range(2f, 15f)]
    public float laneChangeCooldown = 7f;

    [Range(0.2f, 2f)]
    public float laneChangeEvaluationInterval = 0.5f;

    public float laneChangeSpeedLimit = 6.5f;
    public float overtakeTriggerDistance = 22f;
    public float overtakeSpeedAdvantage = 0.75f;
    public float laneChangeBenefitDistance = 8f;

    [Tooltip("차선 변경이 가능한 구간에서 플레이어/고정 장애물을 미리 찾는 거리입니다. 기존 BoxCast를 확장하므로 물리 호출 횟수는 증가하지 않습니다.")]
    [Range(20f, 35f)]
    public float laneChangePlanningLookAhead = 28f;

    [Tooltip("일반 차선 변경에서 한 차선을 완전히 옮기는 전진거리입니다.")]
    [Range(12f, 22f)]
    public float normalLaneChangeTravelDistance = 16f;

    [Tooltip("장애물 접촉 예상 지점이 이 거리 안이면 짧은 긴급 회피 경로를 사용합니다.")]
    [Range(6f, 16f)]
    public float emergencyLaneChangeTriggerDistance = 12f;

    [Tooltip("바로 앞 장애물을 피할 때 한 차선을 완전히 옮기는 전진거리입니다.")]
    [Range(8f, 14f)]
    public float emergencyLaneChangeTravelDistance = 10f;

    [Tooltip("10m 긴급 회피 경로에서 허용되는 최대 차체 방향각입니다.")]
    [Range(24f, 38f)]
    public float emergencyLaneChangeSteeringAngle = 31f;

    [Tooltip("차선 변경 곡선에서 허용할 최대 횡가속도입니다. 곡률이 큰 10m 긴급 경로는 이 값에 맞춰 먼저 감속합니다.")]
    [Range(1.5f, 4f)]
    public float laneChangeMaximumLateralAcceleration = 2.5f;

    [Tooltip("곡선 제한 속도에 이 값만큼 가까워지면 직진 감속 준비를 끝내고 횡이동을 시작합니다.")]
    [Range(0.05f, 0.4f)]
    public float laneChangePreparationSpeedTolerance = 0.15f;

    [Tooltip("차선 변경을 시작하기 전에 전체 경로를 검사할 지점 수입니다. 시작할 때만 실행됩니다.")]
    [Range(6, 12)]
    public int laneChangePreflightSamples = 10;

    [Range(12f, 35f)]
    public float maximumLaneChangeSteeringAngle = 20f;

    [Tooltip("저속 막힘 회복 뒤 최종 합류에서 확보할 최소 전진거리입니다.")]
    [Range(12f, 24f)]
    public float laneChangeMinimumTravelDistance = 22f;

    [Tooltip("차량 중심이 이 비율만큼 옆으로 이동한 뒤부터 목표 차선의 장애물로 등록합니다.")]
    [Range(0.02f, 0.3f)]
    public float targetLaneOccupancyStart = 0.15f;

    [Tooltip("차선 변경 차량이 이전 차선의 선행 장애물 판정에서 완전히 벗어나는 횡이동 비율입니다. 0.55는 일반 차량 폭을 고려한 중앙선 통과 여유입니다.")]
    [Range(0.45f, 0.7f)]
    public float laneChangeSourceConstraintEnd = 0.55f;

    [Tooltip("뒤 차량이 차선 변경 차량을 이전 차선의 장애물로 유지하는 횡이동 비율입니다.")]
    [Range(0.7f, 0.95f)]
    public float laneChangeSourceOccupancyEnd = 0.85f;

    [Range(10f, 80f)]
    public float signalQueueLaneChangeBlockDistance = 45f;

    [Tooltip("녹색 전환 뒤 대기열이 이 속도에 도달할 때까지 신호 대기 차량의 추월 차선 변경을 막습니다.")]
    [Range(1f, 5f)]
    public float signalQueueReleaseSpeed = 2.5f;

    [Tooltip("녹색 전환 직후 대기열이 출발하는 동안 저속 선행 차량을 추월하지 않도록 보호하는 시간입니다.")]
    [Range(0.5f, 8f)]
    public float signalQueueReleaseGraceTime = 3f;

    [Header("Blocked Lane Change Recovery - Normal Cars")]
    public bool enableBlockedLaneChangeReverse = true;

    [Tooltip("이 속도 이하에서 앞이 막히면 일반 차선 변경 대신 제한 공간 다점 기동을 사용합니다.")]
    [Range(0.1f, 2.5f)]
    public float reverseMaximumStartSpeed = 1.5f;

    [Range(1f, 6f)]
    public float reverseFrontGapThreshold = 3f;

    [Range(0.5f, 3f)]
    public float laneChangeReverseDistance = 2f;

    [Range(0.5f, 2f)]
    public float laneChangeReverseSpeed = 1.2f;

    [Tooltip("고정 회피 경로의 전진 조향 구간 속도입니다.")]
    [Range(0.5f, 2f)]
    public float blockedRecoveryForwardSpeed = 1f;

    [Tooltip("한 번의 후진과 전진 사이에서 기어를 바꾸며 잠시 정지하는 시간입니다.")]
    [Range(0.15f, 0.75f)]
    public float blockedRecoveryGearShiftPause = 0.15f;

    [Tooltip("저속 후진 복구 중 뒤 차량 범퍼와 유지할 간격입니다.")]
    [Range(0.25f, 2f)]
    public float reverseRearClearance = 0.6f;

    [Range(0.15f, 0.4f)]
    public float reversePhaseFraction = 0.25f;

    [Tooltip("막힘 회피 시 목표 차선 반대쪽으로 후진하는 차선 폭 비율입니다. 0.12는 약 40cm 정도의 옆 이동입니다.")]
    [Range(0.05f, 0.25f)]
    public float reverseLateralProgress = 0.12f;

    [Range(0f, 1f)]
    public float r4BranchProbability = 0.35f;

    [Tooltip("막힘 회복 기동에만 허용되는 앞바퀴 최대 조향각입니다.")]
    [Range(20f, 40f)]
    public float blockedRecoveryMaximumSteeringAngle = 34f;

    [Tooltip("다점 기동 준비 중 차체가 차선 방향에서 벗어날 수 있는 최대 각도입니다. 일반 차선 변경 각도와 별개입니다.")]
    [Range(18f, 38f)]
    public float blockedRecoveryMaximumBodyAngle = 35f;

    [Header("Visual")]
    [Tooltip("일반 차량 모델에만 적용되는 시각 크기 보정입니다. TEST 월드 실측 절충값은 1.02이며 주행 좌표와 네트워크 데이터에는 영향을 주지 않습니다.")]
    [Range(0.8f, 1.25f)]
    public float normalCarVisualScale = 1.02f;

    [Tooltip("트럭 모델에만 적용되는 시각 크기 보정입니다.")]
    [Range(0.8f, 1.25f)]
    public float truckVisualScale = 1f;

    [Header("Baked Vehicle Bounds")]
    [Tooltip("차체 외곽에 추가할 앞/뒤 보정 거리입니다. 차간 안전거리는 별도 주행 설정으로 처리하므로 기본값은 0입니다.")]
    [Range(0f, 0.3f)]
    public float vehicleBoundsLongitudinalMargin;

    [Tooltip("차체 외곽에 추가할 좌/우 보정 거리입니다. 차선 변경 안전 여유와 분리하기 위해 기본값은 0입니다.")]
    [Range(0f, 0.3f)]
    public float vehicleBoundsLateralMargin;

    [HideInInspector]
    public float[] bakedVehicleFrontExtents = new float[0];

    [HideInInspector]
    public float[] bakedVehicleRearExtents = new float[0];

    [HideInInspector]
    public float[] bakedVehicleWidths = new float[0];

    public float heightOffset;
    public float spawnVisualDistance = 4f;
    public float despawnVisualDistance = 6f;

    [Header("Startup")]
    public bool playOnStart = true;

    [Header("Network Authority")]
    [Range(0.2f, 1f)]
    public float networkSnapshotInterval = 0.25f;

    [Range(3f, 15f)]
    public float authorityTakeoverTimeout = 6f;

    [UdonSynced]
    private int syncedAuthorityEpoch;

    [UdonSynced]
    private int syncedSnapshotSequence;

    [UdonSynced]
    private int syncedRandomState;

    [UdonSynced]
    private int[] syncedVehicleStateA =
        new int[NetworkSlotCapacity];

    [UdonSynced]
    private int[] syncedVehicleStateB =
        new int[NetworkSlotCapacity];

    [HideInInspector]
    public int activeVehicleCount;

    [HideInInspector]
    public bool localIsAuthority;

    [HideInInspector]
    public int lastReceivedSequence;

    [HideInInspector]
    public int lastSerializationBytes;

    [HideInInspector]
    public bool lastSerializationSucceeded;

    [HideInInspector]
    public int authorityPhysicsQueryCount;

    [HideInInspector]
    public int authorityPhysicsHitCount;

    [HideInInspector]
    public int activeLaneChangeCount;

    private bool[] vehicleActive = new bool[0];
    private bool[] vehicleSpawnGeneration = new bool[0];
    private bool[] processedSlots = new bool[0];
    private bool[] signalCommittedToCross = new bool[0];

    private int[] sourceLaneVehicleOrder = new int[0];
    private int[] sourceLaneVehicleCounts = new int[0];
    private int[] reservationLaneVehicleOrder = new int[0];
    private int[] reservationLaneVehicleCounts = new int[0];
    private int[] cachedSourceLeaderIndices = new int[0];

    private int[] vehicleLaneIds = new int[0];
    private int[] sampleHints = new int[0];
    private int[] laneChangeTargetLaneIds = new int[0];
    private int[] laneChangeRuleIndices = new int[0];
    private int[] laneChangeBranchDecisions = new int[0];

    private float[] vehicleS = new float[0];
    private float[] previousVehicleS = new float[0];
    private float[] previousVehicleSpeeds = new float[0];
    private float[] previousLaneChangeProgress = new float[0];
    private int[] previousVehicleLaneIds = new int[0];
    private bool[] previousLaneChangeActive = new bool[0];
    private float[] vehicleSpeeds = new float[0];
    private float[] vehicleAccelerations = new float[0];
    private float[] vehicleRenderMaximumS = new float[0];
    private float[] speedFactors = new float[0];
    private float[] wheelAngles = new float[0];
    private float[] wheelTravelDistanceDeltas = new float[0];
    private float[] frontWheelSteeringAngles = new float[0];
    private float[] laneChangeProgress = new float[0];
    private float[] laneChangeCooldownRemaining = new float[0];
    private float[] laneChangeEvaluationRemaining = new float[0];
    private float[] playerObstacleHoldRemaining = new float[0];
    private float[] lastPlayerStopS = new float[0];
    private float[] obstacleRestartDelayRemaining = new float[0];
    private float[] physicalObstacleRestartHoldS = new float[0];
    private float[] physicalObstacleClearHoldRemaining = new float[0];
    private float[] physicalObstacleReactionHoldRemaining = new float[0];
    private float[] laneChangeObstacleRestartHoldRemaining =
        new float[0];
    private float[] laneChangeObstacleClearHoldRemaining =
        new float[0];
    private float[] laneChangeRecoveryDistance = new float[0];
    private float[] laneChangeRecoveryGearHoldRemaining =
        new float[0];
    private bool[] laneChangeActive = new bool[0];
    private bool[] waitingForObstacleRelease = new bool[0];
    private bool[] physicalObstacleRestartPending = new bool[0];
    private bool[] laneChangePlayerBlocked = new bool[0];
    private bool[] laneChangeVehicleBlocked = new bool[0];
    private bool[] laneChangeRoadBoundaryBlocked = new bool[0];
    private bool[] laneChangeObstacleSweepDebugValid = new bool[0];

    private bool[] physicsObstacleCastDebugValid = new bool[0];
    private bool[] physicsObstacleCastDebugBlocked = new bool[0];
    private bool[] physicsObstacleCastDebugLaneLimited =
        new bool[0];
    private bool[] signalStopDebugActive = new bool[0];

    private Vector3[] physicsObstacleCastDebugCenters =
        new Vector3[0];

    private Vector3[] physicsObstacleCastDebugHalfExtents =
        new Vector3[0];

    private Vector3[] physicsObstacleCastDebugDirections =
        new Vector3[0];

    private Quaternion[] physicsObstacleCastDebugRotations =
        new Quaternion[0];

    private float[] physicsObstacleCastDebugDistances = new float[0];
    private float[] physicsObstacleCastDebugHitDistances = new float[0];
    private float[] physicsObstacleStopS = new float[0];
    private float[] physicsObstacleHitS = new float[0];

    private Vector3[] laneChangeObstacleSweepStartPositions =
        new Vector3[0];

    private Vector3[] laneChangeObstacleSweepEndPositions =
        new Vector3[0];

    private Quaternion[] laneChangeObstacleSweepStartRotations =
        new Quaternion[0];

    private Quaternion[] laneChangeObstacleSweepEndRotations =
        new Quaternion[0];

    private bool[] visualActive = new bool[0];
    private bool[] visualSpawnGeneration = new bool[0];

    private bool[] snapshotPreviousActive = new bool[0];
    private bool[] snapshotNextActive = new bool[0];
    private bool[] snapshotPreviousSpawnGeneration = new bool[0];
    private bool[] snapshotNextSpawnGeneration = new bool[0];

    private int[] snapshotPreviousLaneIds = new int[0];
    private int[] snapshotNextLaneIds = new int[0];
    private int[] snapshotPreviousTargetLaneIds = new int[0];
    private int[] snapshotNextTargetLaneIds = new int[0];

    private float[] snapshotPreviousS = new float[0];
    private float[] snapshotNextS = new float[0];
    private float[] snapshotPreviousSpeeds = new float[0];
    private float[] snapshotNextSpeeds = new float[0];
    private float[] snapshotPreviousAccelerations = new float[0];
    private float[] snapshotNextAccelerations = new float[0];
    private float[] snapshotPreviousSpeedFactors = new float[0];
    private float[] snapshotNextSpeedFactors = new float[0];
    private float[] snapshotPreviousLaneChangeProgress = new float[0];
    private float[] snapshotNextLaneChangeProgress = new float[0];
    private float[] snapshotPreviousRecoveryDistance = new float[0];
    private float[] snapshotNextRecoveryDistance = new float[0];

    private bool[] snapshotPreviousLaneChangeActive = new bool[0];
    private bool[] snapshotNextLaneChangeActive = new bool[0];
    private bool[] snapshotPreviousEmergencyManeuver = new bool[0];
    private bool[] snapshotNextEmergencyManeuver = new bool[0];
    private bool[] snapshotPreviousReverseManeuver = new bool[0];
    private bool[] snapshotNextReverseManeuver = new bool[0];
    private int[] snapshotBufferedStateA = new int[0];
    private int[] snapshotBufferedStateB = new int[0];
    private bool[] laneChangeReverseManeuver = new bool[0];
    private bool[] laneChangeEmergencyManeuver = new bool[0];
    private bool[] laneChangePreparing = new bool[0];
    private float[] laneChangeCurveSpeedLimits = new float[0];
    private float[] laneChangeReverseStartS = new float[0];

    // A manoeuvre is generated once at the position where it starts. Every
    // runtime consumer (motion, body rotation, steering and obstacle sweep)
    // samples this same path instead of rebuilding a slightly different
    // curve from the current lane separation every frame.
    private bool[] maneuverPathValid = new bool[0];
    private int[] maneuverPathSourceLaneIds = new int[0];
    private int[] maneuverPathTargetLaneIds = new int[0];
    private int[] maneuverPathRuleIndices = new int[0];
    private bool[] maneuverPathReverse = new bool[0];
    private bool[] maneuverPathEmergency = new bool[0];
    private float[] maneuverPathRecoveryDistances = new float[0];
    private Vector3[] maneuverPathPositions = new Vector3[0];
    private Quaternion[] maneuverPathRotations = new Quaternion[0];
    private float[] maneuverPathSourceS = new float[0];
    private float[] maneuverPathDistances = new float[0];
    private float[] maneuverPathSteeringAngles = new float[0];
    private int[] maneuverPhaseRangeScratch = new int[2];

    private float[] ruleTargetStartS =
        new float[MaximumLaneChangeRules];

    private float[] ruleTargetEndS =
        new float[MaximumLaneChangeRules];

    private bool[] rulePhysicsCorridorValid =
        new bool[MaximumLaneChangeRules];

    private Vector3[] rulePhysicsCorridorAxis =
        new Vector3[MaximumLaneChangeRules];

    private float[] rulePhysicsCorridorMinimum =
        new float[MaximumLaneChangeRules];

    private float[] rulePhysicsCorridorMaximum =
        new float[MaximumLaneChangeRules];

    private Vector3[] baseLocalScales = new Vector3[0];
    private Vector3[] previousVisualPositions = new Vector3[0];
    private bool[] previousVisualPositionValid = new bool[0];
    private Vector3[] vehicleCollisionVelocities = new Vector3[0];
    private AudioSource[] vehicleAudioSources =
        new AudioSource[0];

    private Transform[] vehicleWheelTransforms =
        new Transform[0];

    private Quaternion[] wheelBaseLocalRotations =
        new Quaternion[0];

    private int slotCount;
    private int randomState;
    private int authorityObstacleFrameCursor;
    private int currentSignalState = ShinhoTime.SignalGreen;
    private float signalQueueReleaseHoldRemaining;

    private float simulationAccumulator;

    private float laneChangeObstacleSweepSafeS;
    private float laneChangeObstacleSweepSafeProgress;
    private float networkSnapshotTimer;
    private float respawnTimer;
    private float timeSinceLastSnapshot;
    private float ownershipRecoveryTimer;
    private float snapshotPreviousSendTime;
    private float snapshotNextSendTime;
    private float snapshotBufferedSendTime;
    private float remoteRenderDelay;
    private float remoteTargetRenderDelay;
    private float remoteTransitTimeEstimate;
    private float remoteTransitJitterEstimate;
    private float remoteSnapshotIntervalEstimate;
    private float audioVisualUpdateTimer;

    private int acceptedAuthorityEpoch = -1;
    private int serializedSequence;
    private int remoteSnapshotCount;

    private bool authorityReady;
    private bool hasReceivedSnapshot;
    private bool remoteTimingInitialized;
    private bool serializationPending;
    private bool networkStateDirty;
    private bool laneVehicleCacheReady;
    private bool laneVehicleCacheDirty;
    private bool refreshAudioVisualsThisFrame = true;
    private bool initialized;
    private bool running;

    private void Start()
    {
        initialized = InitializeManager();

        if (!initialized)
        {
            return;
        }

        ResetSlots();

        running = playOnStart;

        timeSinceLastSnapshot = 0f;
        ownershipRecoveryTimer = 0f;
        networkSnapshotTimer = 0f;
        localIsAuthority =
            Networking.IsOwner(gameObject);

        if (localIsAuthority)
        {
            SendCustomEventDelayedFrames(
                nameof(_BeginAuthority),
                1
            );
        }

        ApplyNetworkVisuals();
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        UpdateMotionVisualRefreshState(
            Mathf.Min(Time.deltaTime, 0.1f)
        );

        bool isOwner = Networking.IsOwner(gameObject);

        if (isOwner != localIsAuthority)
        {
            localIsAuthority = isOwner;

            if (!isOwner)
            {
                authorityReady = false;
            }
        }

        if (localIsAuthority && authorityReady && running)
        {
            UpdateAuthorityPhysicsObstacleSensorsFrame();
            UpdateAuthoritySimulation();
            UpdateNetworkSender();
        }
        else if (!localIsAuthority)
        {
            UpdateAuthorityRecovery();
        }

        ApplyNetworkVisuals();
    }

    private void UpdateAuthoritySimulation()
    {
        float stepDuration = Mathf.Clamp(
            simulationInterval,
            0.02f,
            0.25f
        );

        simulationAccumulator += Mathf.Min(
            Time.deltaTime,
            0.25f
        );

        int completedSteps = 0;

        while (simulationAccumulator >= stepDuration &&
               completedSteps < maximumStepsPerFrame)
        {
            SimulateStep(stepDuration);

            simulationAccumulator -= stepDuration;
            completedSteps++;
        }

        if (simulationAccumulator >= stepDuration)
        {
            // 긴 프레임 이후 누적 시뮬레이션 폭주 방지
            simulationAccumulator = 0f;
        }
    }

    public void _BeginAuthority()
    {
        if (!initialized ||
            !Networking.IsOwner(gameObject))
        {
            return;
        }

        if (!Networking.IsNetworkSettled)
        {
            SendCustomEventDelayedSeconds(
                nameof(_BeginAuthority),
                0.5f
            );

            return;
        }

        localIsAuthority = true;
        authorityReady = false;
        EnsureNetworkBuffers();

        bool canRestore = syncedSnapshotSequence > 0;

        if (canRestore)
        {
            RestoreAuthorityFromSyncedState();
        }
        else
        {
            ResetSlots();

            randomState = randomSeed & 65535;

            if (randomState == 0)
            {
                randomState = 1;
            }

            if (running)
            {
                if (distributeVehiclesOnStartup)
                {
                    DistributeStartupVehicles();
                }

                TryFillVehicleSlots();
            }
        }

        syncedAuthorityEpoch = Mathf.Max(
            1,
            syncedAuthorityEpoch + 1
        );

        simulationAccumulator = 0f;
        respawnTimer = 0f;
        networkSnapshotTimer = 0f;
        authorityReady = true;

        PackNetworkSnapshot();
        networkSnapshotTimer = Mathf.Max(
            0.2f,
            networkSnapshotInterval
        );
        TryRequestNetworkSnapshot();
    }

    private void UpdateNetworkSender()
    {
        networkSnapshotTimer -= Time.deltaTime;

        if (networkSnapshotTimer <= 0f)
        {
            networkSnapshotTimer = Mathf.Max(
                0.2f,
                networkSnapshotInterval
            );

            PackNetworkSnapshot();
        }

        TryRequestNetworkSnapshot();
    }

    private void PackNetworkSnapshot()
    {
        EnsureNetworkBuffers();

        syncedSnapshotSequence++;
        syncedRandomState = randomState;

        for (int i = 0;
             i < NetworkSlotCapacity;
             i++)
        {
            if (i >= slotCount || !vehicleActive[i])
            {
                syncedVehicleStateA[i] = 0;
                syncedVehicleStateB[i] = 0;
                continue;
            }

            int laneId = Mathf.Clamp(
                vehicleLaneIds[i],
                0,
                7
            );

            int positionValue = Mathf.Clamp(
                Mathf.RoundToInt(
                    vehicleS[i] / PositionQuantum
                ),
                0,
                131071
            );

            int speedValue = Mathf.Clamp(
                Mathf.RoundToInt(
                    vehicleSpeeds[i] / SpeedQuantum
                ),
                0,
                511
            );

            int stateA = ActiveBit |
                (laneId << LaneShift) |
                (positionValue << PositionShift) |
                (speedValue << SpeedShift);

            if (laneChangeActive[i] &&
                laneChangeReverseManeuver[i])
            {
                stateA |= LaneChangeReverseManeuverBit;
            }
            else if (laneChangeActive[i] &&
                     laneChangeEmergencyManeuver[i])
            {
                stateA |= LaneChangeEmergencyManeuverBit;
            }
            else if (!laneChangeActive[i] &&
                     signalCommittedToCross[i])
            {
                stateA |= SignalCommitBit;
            }

            int speedFactorValue = Mathf.Clamp(
                Mathf.RoundToInt(
                    Mathf.InverseLerp(
                        0.5f,
                        1.5f,
                        speedFactors[i]
                    ) * 255f
                ),
                0,
                255
            );

            int accelerationValue = Mathf.Clamp(
                Mathf.RoundToInt(
                    vehicleAccelerations[i] /
                    AccelerationQuantum
                ),
                -128,
                127
            ) & 255;

            int stateB =
                speedFactorValue |
                (accelerationValue << 8) |
                (laneId << 16);

            if (vehicleSpawnGeneration[i])
            {
                stateB |= VehicleSpawnGenerationBit;
            }

            if (laneChangeActive[i])
            {
                int targetLaneId = Mathf.Clamp(
                    laneChangeTargetLaneIds[i],
                    0,
                    7
                );

                bool recoveryManeuver =
                    laneChangeReverseManeuver[i];
                int progressMaximum = recoveryManeuver
                    ? 63
                    : 255;
                int progressValue = Mathf.Clamp(
                    Mathf.RoundToInt(
                        laneChangeProgress[i] * progressMaximum
                    ),
                    0,
                    progressMaximum
                );

                stateB |= LaneChangeActiveBit |
                    (targetLaneId <<
                        LaneChangeTargetShift) |
                    (progressValue <<
                        LaneChangeProgressShift);

                if (recoveryManeuver)
                {
                    stateB |=
                        EncodeRecoveryDistance(
                            laneChangeRecoveryDistance[i]
                        ) << RecoveryDistanceShift;
                }
            }

            syncedVehicleStateA[i] = stateA;
            syncedVehicleStateB[i] = stateB;
        }

        networkStateDirty = true;
    }

    private void TryRequestNetworkSnapshot()
    {
        if (!localIsAuthority ||
            !authorityReady ||
            !networkStateDirty ||
            serializationPending ||
            Networking.IsClogged)
        {
            return;
        }

        serializationPending = true;
        RequestSerialization();
    }

    public override void OnPreSerialization()
    {
        serializedSequence = syncedSnapshotSequence;
    }

    public override void OnPostSerialization(
        SerializationResult result)
    {
        serializationPending = false;
        lastSerializationSucceeded = result.success;
        lastSerializationBytes = result.byteCount;

        if (result.success &&
            serializedSequence ==
            syncedSnapshotSequence)
        {
            networkStateDirty = false;
        }
        else
        {
            networkStateDirty = true;
        }
    }

    public override void OnDeserialization(
        DeserializationResult result)
    {
        if (!initialized ||
            Networking.IsOwner(gameObject))
        {
            return;
        }

        AcceptSyncedSnapshot(
            result.sendTime,
            result.receiveTime,
            result.isFromStorage
        );
    }

    public override void OnOwnershipTransferred(
        VRCPlayerApi newOwner)
    {
        if (!initialized)
        {
            return;
        }

        bool becameOwner =
            newOwner != null && newOwner.isLocal;

        localIsAuthority = becameOwner;
        authorityReady = false;
        serializationPending = false;

        if (becameOwner)
        {
            SendCustomEventDelayedFrames(
                nameof(_BeginAuthority),
                2
            );
        }
        else
        {
            ResetRemoteSnapshotBuffer();
        }
    }

    private void UpdateAuthorityRecovery()
    {
        timeSinceLastSnapshot += Time.deltaTime;
        ownershipRecoveryTimer = Mathf.Max(
            0f,
            ownershipRecoveryTimer - Time.deltaTime
        );

        if (!Networking.IsNetworkSettled ||
            !Networking.IsMaster ||
            timeSinceLastSnapshot <
            Mathf.Max(3f, authorityTakeoverTimeout) ||
            ownershipRecoveryTimer > 0f)
        {
            return;
        }

        VRCPlayerApi localPlayer =
            Networking.LocalPlayer;

        if (localPlayer == null)
        {
            return;
        }

        ownershipRecoveryTimer = Mathf.Max(
            3f,
            authorityTakeoverTimeout
        );

        Networking.SetOwner(
            localPlayer,
            gameObject
        );
    }

    private void RestoreAuthorityFromSyncedState()
    {
        activeVehicleCount = 0;
        activeLaneChangeCount = 0;
        authorityObstacleFrameCursor = 0;
        randomState = syncedRandomState & 65535;

        if (randomState == 0)
        {
            randomState = randomSeed & 65535;

            if (randomState == 0)
            {
                randomState = 1;
            }
        }

        for (int i = 0; i < slotCount; i++)
        {
            int stateA = syncedVehicleStateA[i];
            int stateB = syncedVehicleStateB[i];

            bool active =
                (stateA & ActiveBit) != 0;

            vehicleActive[i] = active;
            vehicleSpawnGeneration[i] =
                (stateB & VehicleSpawnGenerationBit) != 0;
            processedSlots[i] = false;
            vehicleRenderMaximumS[i] = -1f;
            laneChangeRecoveryDistance[i] = 0f;
            laneChangeRecoveryGearHoldRemaining[i] = 0f;
            ResetVehicleObstacleState(i);

            if (!active)
            {
                vehicleLaneIds[i] = -1;
                sampleHints[i] = -1;
                vehicleS[i] = 0f;
                previousVehicleS[i] = 0f;
                previousVehicleSpeeds[i] = 0f;
                previousLaneChangeProgress[i] = 0f;
                previousVehicleLaneIds[i] = -1;
                previousLaneChangeActive[i] = false;
                vehicleSpeeds[i] = 0f;
                vehicleAccelerations[i] = 0f;
                speedFactors[i] = 1f;
                signalCommittedToCross[i] = false;
                laneChangeActive[i] = false;
                laneChangeReverseManeuver[i] = false;
                laneChangeEmergencyManeuver[i] = false;
                laneChangePreparing[i] = false;
                laneChangeCurveSpeedLimits[i] = 0f;
                laneChangeReverseStartS[i] = 0f;
                laneChangeTargetLaneIds[i] = -1;
                laneChangeRuleIndices[i] = -1;
                laneChangeProgress[i] = 0f;
                laneChangeCooldownRemaining[i] = 0f;
                laneChangeEvaluationRemaining[i] = 0f;
                laneChangeBranchDecisions[i] = 0;
                continue;
            }

            vehicleLaneIds[i] = DecodeLaneId(stateA);
            vehicleS[i] = DecodePosition(stateA);
            previousVehicleS[i] = vehicleS[i];
            vehicleSpeeds[i] = DecodeSpeed(stateA);
            vehicleAccelerations[i] =
                DecodeAcceleration(stateB);
            speedFactors[i] = DecodeSpeedFactor(stateB);
            laneChangeActive[i] =
                DecodeLaneChangeActive(stateB);

            laneChangeReverseManeuver[i] =
                laneChangeActive[i] &&
                DecodeReverseManeuver(stateA);

            laneChangeEmergencyManeuver[i] =
                laneChangeActive[i] &&
                !laneChangeReverseManeuver[i] &&
                DecodeEmergencyManeuver(stateA);


            signalCommittedToCross[i] =
                !laneChangeActive[i] &&
                (stateA & SignalCommitBit) != 0;

            laneChangeTargetLaneIds[i] =
                laneChangeActive[i]
                    ? DecodeLaneChangeTarget(stateB)
                    : -1;

            laneChangeProgress[i] =
                laneChangeActive[i]
                    ? DecodeLaneChangeProgress(
                        stateB,
                        laneChangeReverseManeuver[i]
                      )
                    : 0f;

            laneChangeRuleIndices[i] =
                laneChangeActive[i]
                    ? FindLaneChangeRule(
                        vehicleLaneIds[i],
                        laneChangeTargetLaneIds[i]
                    )
                    : -1;

            if (laneChangeReverseManeuver[i] &&
                laneChangeRuleIndices[i] >= 0)
            {
                laneChangeRecoveryDistance[i] =
                    DecodeRecoveryDistance(stateB);

                laneChangeReverseStartS[i] =
                    vehicleS[i] -
                    GetRecoveryLongitudinalOffset(
                        i,
                        laneChangeProgress[i],
                        laneChangeRecoveryDistance[i]
                    );
            }
            else
            {
                laneChangeReverseStartS[i] = vehicleS[i];
            }

            if (laneChangeRuleIndices[i] < 0)
            {
                laneChangeActive[i] = false;
                laneChangeReverseManeuver[i] = false;
                laneChangeEmergencyManeuver[i] = false;
                laneChangePreparing[i] = false;
                laneChangeCurveSpeedLimits[i] = 0f;
                laneChangeReverseStartS[i] = 0f;
                laneChangeTargetLaneIds[i] = -1;
                laneChangeProgress[i] = 0f;
            }
            else
            {
                laneChangeCurveSpeedLimits[i] =
                    laneChangeReverseManeuver[i]
                        ? 0f
                        : GetLaneChangeCurveSpeedLimit(
                            i,
                            laneChangeEmergencyManeuver[i],
                            vehicleS[i]
                          );
                laneChangePreparing[i] =
                    !laneChangeReverseManeuver[i] &&
                    laneChangeProgress[i] <= 0.0001f &&
                    vehicleSpeeds[i] >
                        laneChangeCurveSpeedLimits[i] +
                        Mathf.Max(
                            0.01f,
                            laneChangePreparationSpeedTolerance
                        );
                activeLaneChangeCount++;
            }

            laneChangeCooldownRemaining[i] = 0f;
            laneChangeEvaluationRemaining[i] =
                Mathf.Max(
                    0.2f,
                    laneChangeEvaluationInterval
                );

            laneChangeBranchDecisions[i] =
                laneChangeActive[i] &&
                laneChangeTargetLaneIds[i] ==
                    TrafficLaneDatabase.LaneR4Branch
                    ? 2
                    : 0;

            sampleHints[i] =
                laneDatabase.FindSampleIndex(
                    vehicleLaneIds[i],
                    vehicleS[i],
                    -1
                );

            previousVehicleSpeeds[i] = vehicleSpeeds[i];
            previousLaneChangeProgress[i] =
                laneChangeProgress[i];
            previousVehicleLaneIds[i] = vehicleLaneIds[i];
            previousLaneChangeActive[i] = laneChangeActive[i];

            activeVehicleCount++;
        }
    }

    private void ResetRemoteSnapshotBuffer()
    {
        remoteSnapshotCount = 0;
        snapshotPreviousSendTime = 0f;
        snapshotNextSendTime = 0f;
        snapshotBufferedSendTime = 0f;
        remoteTransitTimeEstimate = 0f;
        remoteTransitJitterEstimate = 0f;
        remoteSnapshotIntervalEstimate = Mathf.Max(
            0.2f,
            networkSnapshotInterval
        );
        remoteTargetRenderDelay = Mathf.Clamp(
            remoteSnapshotIntervalEstimate + 0.1f,
            MinimumRemoteRenderDelay,
            MaximumRemoteRenderDelay
        );
        remoteRenderDelay = remoteTargetRenderDelay;
        remoteTimingInitialized = false;
        hasReceivedSnapshot = false;
    }

    private void AcceptSyncedSnapshot(
        float sendTime,
        float receiveTime,
        bool isFromStorage)
    {
        if (syncedVehicleStateA == null ||
            syncedVehicleStateB == null ||
            syncedVehicleStateA.Length < slotCount ||
            syncedVehicleStateB.Length < slotCount)
        {
            return;
        }

        bool newerEpoch =
            syncedAuthorityEpoch > acceptedAuthorityEpoch;

        if (newerEpoch)
        {
            ResetRemoteSnapshotBuffer();
        }

        if (hasReceivedSnapshot &&
            !newerEpoch &&
            syncedAuthorityEpoch == acceptedAuthorityEpoch &&
            syncedSnapshotSequence <= lastReceivedSequence)
        {
            return;
        }

        float localReceiveTime = receiveTime;

        if (localReceiveTime <= 0f)
        {
            localReceiveTime = Time.realtimeSinceStartup;
        }

        float effectiveSendTime = isFromStorage
            ? localReceiveTime
            : sendTime;

        float latestSendTime = snapshotNextSendTime;

        if (remoteSnapshotCount >= 3)
        {
            latestSendTime = snapshotBufferedSendTime;
        }

        if (remoteSnapshotCount > 0)
        {
            float observedInterval =
                effectiveSendTime - latestSendTime;

            if (observedInterval > 0.001f)
            {
                float clampedInterval = Mathf.Clamp(
                    observedInterval,
                    0.05f,
                    2f
                );

                remoteSnapshotIntervalEstimate = Mathf.Lerp(
                    remoteSnapshotIntervalEstimate,
                    clampedInterval,
                    0.2f
                );
            }
            else
            {
                effectiveSendTime = latestSendTime + 0.001f;
            }
        }

        if (!isFromStorage)
        {
            float transitTime = Mathf.Clamp(
                localReceiveTime - effectiveSendTime,
                0f,
                2.5f
            );

            if (!remoteTimingInitialized)
            {
                remoteTransitTimeEstimate = transitTime;
                remoteTransitJitterEstimate = 0f;
                remoteTimingInitialized = true;
            }
            else
            {
                float timingError = Mathf.Abs(
                    transitTime - remoteTransitTimeEstimate
                );

                remoteTransitTimeEstimate = Mathf.Lerp(
                    remoteTransitTimeEstimate,
                    transitTime,
                    0.12f
                );
                remoteTransitJitterEstimate = Mathf.Lerp(
                    remoteTransitJitterEstimate,
                    timingError,
                    0.15f
                );
            }

            float intervalReserve = Mathf.Max(
                Mathf.Max(0.2f, networkSnapshotInterval),
                remoteSnapshotIntervalEstimate
            );
            float jitterReserve = Mathf.Max(
                0.05f,
                remoteTransitJitterEstimate * 2f
            );

            remoteTargetRenderDelay = Mathf.Clamp(
                remoteTransitTimeEstimate +
                    intervalReserve +
                    jitterReserve,
                MinimumRemoteRenderDelay,
                MaximumRemoteRenderDelay
            );

            if (remoteSnapshotCount == 0)
            {
                remoteRenderDelay = remoteTargetRenderDelay;
            }
        }

        if (remoteSnapshotCount == 0)
        {
            DecodeStateToNextSnapshot(
                syncedVehicleStateA,
                syncedVehicleStateB
            );
            CopyNextSnapshotToPrevious();
            snapshotPreviousSendTime = effectiveSendTime;
            snapshotNextSendTime = effectiveSendTime;
            remoteSnapshotCount = 1;
        }
        else if (remoteSnapshotCount == 1)
        {
            DecodeStateToNextSnapshot(
                syncedVehicleStateA,
                syncedVehicleStateB
            );
            snapshotNextSendTime = effectiveSendTime;
            remoteSnapshotCount = 2;
        }
        else
        {
            if (remoteSnapshotCount >= 3)
            {
                PromoteBufferedSnapshotToPair();
            }

            CopySyncedStateToBufferedSnapshot();
            snapshotBufferedSendTime = effectiveSendTime;
            remoteSnapshotCount = 3;
        }

        acceptedAuthorityEpoch = syncedAuthorityEpoch;
        lastReceivedSequence = syncedSnapshotSequence;
        timeSinceLastSnapshot = 0f;
        hasReceivedSnapshot = true;
    }

    private void CopyNextSnapshotToPrevious()
    {
        for (int i = 0; i < slotCount; i++)
        {
            snapshotPreviousActive[i] =
                snapshotNextActive[i];
            snapshotPreviousSpawnGeneration[i] =
                snapshotNextSpawnGeneration[i];
            snapshotPreviousLaneIds[i] =
                snapshotNextLaneIds[i];
            snapshotPreviousTargetLaneIds[i] =
                snapshotNextTargetLaneIds[i];
            snapshotPreviousS[i] = snapshotNextS[i];
            snapshotPreviousSpeeds[i] =
                snapshotNextSpeeds[i];
            snapshotPreviousAccelerations[i] =
                snapshotNextAccelerations[i];
            snapshotPreviousSpeedFactors[i] =
                snapshotNextSpeedFactors[i];
            snapshotPreviousLaneChangeProgress[i] =
                snapshotNextLaneChangeProgress[i];
            snapshotPreviousLaneChangeActive[i] =
                snapshotNextLaneChangeActive[i];
            snapshotPreviousEmergencyManeuver[i] =
                snapshotNextEmergencyManeuver[i];
            snapshotPreviousReverseManeuver[i] =
                snapshotNextReverseManeuver[i];
            snapshotPreviousRecoveryDistance[i] =
                snapshotNextRecoveryDistance[i];
        }
    }

    private void CopySyncedStateToBufferedSnapshot()
    {
        for (int i = 0; i < slotCount; i++)
        {
            snapshotBufferedStateA[i] =
                syncedVehicleStateA[i];
            snapshotBufferedStateB[i] =
                syncedVehicleStateB[i];
        }
    }

    private void PromoteBufferedSnapshotToPair()
    {
        if (remoteSnapshotCount < 3)
        {
            return;
        }

        CopyNextSnapshotToPrevious();
        snapshotPreviousSendTime = snapshotNextSendTime;

        DecodeStateToNextSnapshot(
            snapshotBufferedStateA,
            snapshotBufferedStateB
        );
        snapshotNextSendTime = snapshotBufferedSendTime;
        snapshotBufferedSendTime = 0f;
        remoteSnapshotCount = 2;
    }

    private void DecodeStateToNextSnapshot(
        int[] stateAValues,
        int[] stateBValues)
    {
        for (int i = 0; i < slotCount; i++)
        {
            int stateA = stateAValues[i];
            int stateB = stateBValues[i];

            bool active =
                (stateA & ActiveBit) != 0;

            snapshotNextActive[i] = active;
            snapshotNextSpawnGeneration[i] =
                (stateB & VehicleSpawnGenerationBit) != 0;

            if (!active)
            {
                snapshotNextLaneIds[i] = -1;
                snapshotNextS[i] = 0f;
                snapshotNextSpeeds[i] = 0f;
                snapshotNextAccelerations[i] = 0f;
                snapshotNextSpeedFactors[i] = 1f;
                snapshotNextTargetLaneIds[i] = -1;
                snapshotNextLaneChangeProgress[i] = 0f;
                snapshotNextLaneChangeActive[i] = false;
                snapshotNextEmergencyManeuver[i] = false;
                snapshotNextReverseManeuver[i] = false;
                snapshotNextRecoveryDistance[i] = 0f;
                continue;
            }

            snapshotNextLaneIds[i] =
                DecodeLaneId(stateA);
            snapshotNextS[i] = DecodePosition(stateA);
            snapshotNextSpeeds[i] = DecodeSpeed(stateA);
            snapshotNextAccelerations[i] =
                DecodeAcceleration(stateB);
            snapshotNextSpeedFactors[i] =
                DecodeSpeedFactor(stateB);
            snapshotNextLaneChangeActive[i] =
                DecodeLaneChangeActive(stateB);
            snapshotNextReverseManeuver[i] =
                snapshotNextLaneChangeActive[i] &&
                DecodeReverseManeuver(stateA);
            snapshotNextEmergencyManeuver[i] =
                snapshotNextLaneChangeActive[i] &&
                !snapshotNextReverseManeuver[i] &&
                DecodeEmergencyManeuver(stateA);
            snapshotNextTargetLaneIds[i] =
                snapshotNextLaneChangeActive[i]
                    ? DecodeLaneChangeTarget(stateB)
                    : -1;
            snapshotNextLaneChangeProgress[i] =
                snapshotNextLaneChangeActive[i]
                    ? DecodeLaneChangeProgress(
                        stateB,
                        snapshotNextReverseManeuver[i]
                      )
                    : 0f;
            snapshotNextRecoveryDistance[i] =
                snapshotNextReverseManeuver[i]
                    ? DecodeRecoveryDistance(stateB)
                    : 0f;
        }
    }

    private int DecodeLaneId(int stateA)
    {
        return (stateA >> LaneShift) & 7;
    }

    private float DecodePosition(int stateA)
    {
        return ((stateA >> PositionShift) & 131071) *
            PositionQuantum;
    }

    private float DecodeSpeed(int stateA)
    {
        return ((stateA >> SpeedShift) & 511) *
            SpeedQuantum;
    }

    private float DecodeAcceleration(int stateB)
    {
        int value = (stateB >> 8) & 255;

        if (value >= 128)
        {
            value -= 256;
        }

        return value * AccelerationQuantum;
    }

    private float DecodeSpeedFactor(int stateB)
    {
        int value = stateB & 255;

        return Mathf.Lerp(
            0.5f,
            1.5f,
            value / 255f
        );
    }

    private bool DecodeLaneChangeActive(int stateB)
    {
        return (stateB & LaneChangeActiveBit) != 0;
    }

    private bool DecodeReverseManeuver(int stateA)
    {
        return (stateA &
                LaneChangeReverseManeuverBit) != 0;
    }

    private bool DecodeEmergencyManeuver(int stateA)
    {
        return (stateA &
                LaneChangeEmergencyManeuverBit) != 0;
    }

    private int DecodeLaneChangeTarget(int stateB)
    {
        return (
            stateB >> LaneChangeTargetShift
        ) & 7;
    }

    private float DecodeLaneChangeProgress(
        int stateB,
        bool recoveryManeuver)
    {
        int value = (
            stateB >> LaneChangeProgressShift
        ) & (recoveryManeuver ? 63 : 255);

        return value / (recoveryManeuver ? 63f : 255f);
    }

    private int EncodeRecoveryDistance(float distance)
    {
        float maximumDistance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            laneChangeReverseDistance
        );

        if (maximumDistance <=
            MinimumReverseRecoveryDistance + 0.0001f)
        {
            return 0;
        }

        return Mathf.Clamp(
            Mathf.RoundToInt(
                Mathf.InverseLerp(
                    MinimumReverseRecoveryDistance,
                    maximumDistance,
                    distance
                ) * 3f
            ),
            0,
            3
        );
    }

    private float DecodeRecoveryDistance(int stateB)
    {
        int value = (stateB >> RecoveryDistanceShift) & 3;
        return Mathf.Lerp(
            MinimumReverseRecoveryDistance,
            Mathf.Max(
                MinimumReverseRecoveryDistance,
                laneChangeReverseDistance
            ),
            value / 3f
        );
    }

    private float QuantizeRecoveryDistanceDown(float distance)
    {
        float maximumDistance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            laneChangeReverseDistance
        );

        if (maximumDistance <=
            MinimumReverseRecoveryDistance + 0.0001f)
        {
            return MinimumReverseRecoveryDistance;
        }

        int value = Mathf.Clamp(
            Mathf.FloorToInt(
                Mathf.InverseLerp(
                    MinimumReverseRecoveryDistance,
                    maximumDistance,
                    distance
                ) * 3f + 0.0001f
            ),
            0,
            3
        );

        return Mathf.Lerp(
            MinimumReverseRecoveryDistance,
            maximumDistance,
            value / 3f
        );
    }

    private void EnsureNetworkBuffers()
    {
        if (syncedVehicleStateA == null ||
            syncedVehicleStateA.Length !=
            NetworkSlotCapacity)
        {
            syncedVehicleStateA =
                new int[NetworkSlotCapacity];
        }

        if (syncedVehicleStateB == null ||
            syncedVehicleStateB.Length !=
            NetworkSlotCapacity)
        {
            syncedVehicleStateB =
                new int[NetworkSlotCapacity];
        }
    }

    private bool InitializeManager()
    {
        if (laneDatabase == null)
        {
            Debug.LogError(
                "[TrafficSimulationManager] " +
                "Lane Database가 지정되지 않았습니다."
            );

            return false;
        }

        if (!laneDatabase.IsReady())
        {
            Debug.LogError(
                "[TrafficSimulationManager] " +
                "Lane Database가 베이크되지 않았습니다."
            );

            return false;
        }

        if (vehicleRoots == null ||
            vehicleRoots.Length == 0)
        {
            Debug.LogError(
                "[TrafficSimulationManager] " +
                "Vehicle Roots가 비어 있습니다."
            );

            return false;
        }

        slotCount = vehicleRoots.Length;

        if (slotCount > NetworkSlotCapacity)
        {
            Debug.LogError(
                "[TrafficSimulationManager] " +
                "차량 슬롯은 최대 16개까지 지원합니다."
            );

            return false;
        }

        EnsureNetworkBuffers();

        vehicleActive = new bool[slotCount];
        vehicleSpawnGeneration = new bool[slotCount];
        processedSlots = new bool[slotCount];

        int laneCacheCapacity =
            TrafficLaneDatabase.FixedLaneCount * slotCount;
        sourceLaneVehicleOrder =
            new int[laneCacheCapacity];
        sourceLaneVehicleCounts =
            new int[TrafficLaneDatabase.FixedLaneCount];
        reservationLaneVehicleOrder =
            new int[laneCacheCapacity];
        reservationLaneVehicleCounts =
            new int[TrafficLaneDatabase.FixedLaneCount];
        cachedSourceLeaderIndices = new int[slotCount];

        vehicleLaneIds = new int[slotCount];
        sampleHints = new int[slotCount];
        laneChangeTargetLaneIds = new int[slotCount];
        laneChangeRuleIndices = new int[slotCount];
        laneChangeBranchDecisions = new int[slotCount];

        vehicleS = new float[slotCount];
        previousVehicleS = new float[slotCount];
        previousVehicleSpeeds = new float[slotCount];
        previousLaneChangeProgress = new float[slotCount];
        previousVehicleLaneIds = new int[slotCount];
        previousLaneChangeActive = new bool[slotCount];
        vehicleSpeeds = new float[slotCount];
        vehicleAccelerations = new float[slotCount];
        vehicleRenderMaximumS = new float[slotCount];
        speedFactors = new float[slotCount];
        wheelAngles = new float[slotCount];
        wheelTravelDistanceDeltas = new float[slotCount];
        frontWheelSteeringAngles = new float[slotCount];
        laneChangeProgress = new float[slotCount];
        laneChangeCooldownRemaining = new float[slotCount];
        laneChangeEvaluationRemaining = new float[slotCount];
        playerObstacleHoldRemaining = new float[slotCount];
        lastPlayerStopS = new float[slotCount];
        obstacleRestartDelayRemaining = new float[slotCount];
        physicalObstacleRestartHoldS = new float[slotCount];
        physicalObstacleClearHoldRemaining = new float[slotCount];
        physicalObstacleReactionHoldRemaining = new float[slotCount];
        laneChangeObstacleRestartHoldRemaining =
            new float[slotCount];
        laneChangeObstacleClearHoldRemaining =
            new float[slotCount];
        laneChangeRecoveryDistance = new float[slotCount];
        laneChangeRecoveryGearHoldRemaining =
            new float[slotCount];
        laneChangeActive = new bool[slotCount];
        waitingForObstacleRelease = new bool[slotCount];
        physicalObstacleRestartPending = new bool[slotCount];
        laneChangePlayerBlocked = new bool[slotCount];
        laneChangeVehicleBlocked = new bool[slotCount];
        laneChangeRoadBoundaryBlocked = new bool[slotCount];
        laneChangeObstacleSweepDebugValid =
            new bool[slotCount];
        physicsObstacleCastDebugValid = new bool[slotCount];
        physicsObstacleCastDebugBlocked = new bool[slotCount];
        physicsObstacleCastDebugLaneLimited =
            new bool[slotCount];
        signalStopDebugActive = new bool[slotCount];
        physicsObstacleCastDebugCenters = new Vector3[slotCount];
        physicsObstacleCastDebugHalfExtents =
            new Vector3[slotCount];
        physicsObstacleCastDebugDirections =
            new Vector3[slotCount];
        physicsObstacleCastDebugRotations =
            new Quaternion[slotCount];
        physicsObstacleCastDebugDistances = new float[slotCount];
        physicsObstacleCastDebugHitDistances =
            new float[slotCount];
        physicsObstacleStopS = new float[slotCount];
        physicsObstacleHitS = new float[slotCount];
        laneChangeObstacleSweepStartPositions =
            new Vector3[slotCount];
        laneChangeObstacleSweepEndPositions =
            new Vector3[slotCount];
        laneChangeObstacleSweepStartRotations =
            new Quaternion[slotCount];
        laneChangeObstacleSweepEndRotations =
            new Quaternion[slotCount];
        laneChangeReverseManeuver = new bool[slotCount];
        laneChangeEmergencyManeuver = new bool[slotCount];
        laneChangePreparing = new bool[slotCount];
        laneChangeCurveSpeedLimits = new float[slotCount];
        laneChangeReverseStartS = new float[slotCount];
        maneuverPathValid = new bool[slotCount];
        maneuverPathSourceLaneIds = new int[slotCount];
        maneuverPathTargetLaneIds = new int[slotCount];
        maneuverPathRuleIndices = new int[slotCount];
        maneuverPathReverse = new bool[slotCount];
        maneuverPathEmergency = new bool[slotCount];
        maneuverPathRecoveryDistances = new float[slotCount];
        int maneuverSampleCapacity =
            slotCount * ManeuverPathSampleCount;
        maneuverPathPositions =
            new Vector3[maneuverSampleCapacity];
        maneuverPathRotations =
            new Quaternion[maneuverSampleCapacity];
        maneuverPathSourceS = new float[maneuverSampleCapacity];
        maneuverPathDistances = new float[maneuverSampleCapacity];
        maneuverPathSteeringAngles =
            new float[maneuverSampleCapacity];
        signalCommittedToCross = new bool[slotCount];

        visualActive = new bool[slotCount];
        visualSpawnGeneration = new bool[slotCount];

        snapshotPreviousActive = new bool[slotCount];
        snapshotNextActive = new bool[slotCount];
        snapshotPreviousSpawnGeneration = new bool[slotCount];
        snapshotNextSpawnGeneration = new bool[slotCount];

        snapshotPreviousLaneIds = new int[slotCount];
        snapshotNextLaneIds = new int[slotCount];
        snapshotPreviousTargetLaneIds = new int[slotCount];
        snapshotNextTargetLaneIds = new int[slotCount];

        snapshotPreviousS = new float[slotCount];
        snapshotNextS = new float[slotCount];

        snapshotPreviousSpeeds = new float[slotCount];
        snapshotNextSpeeds = new float[slotCount];

        snapshotPreviousAccelerations = new float[slotCount];
        snapshotNextAccelerations = new float[slotCount];

        snapshotPreviousSpeedFactors = new float[slotCount];
        snapshotNextSpeedFactors = new float[slotCount];
        snapshotPreviousLaneChangeProgress = new float[slotCount];
        snapshotNextLaneChangeProgress = new float[slotCount];
        snapshotPreviousRecoveryDistance = new float[slotCount];
        snapshotNextRecoveryDistance = new float[slotCount];
        snapshotPreviousLaneChangeActive = new bool[slotCount];
        snapshotNextLaneChangeActive = new bool[slotCount];
        snapshotPreviousEmergencyManeuver = new bool[slotCount];
        snapshotNextEmergencyManeuver = new bool[slotCount];
        snapshotPreviousReverseManeuver = new bool[slotCount];
        snapshotNextReverseManeuver = new bool[slotCount];
        snapshotBufferedStateA = new int[slotCount];
        snapshotBufferedStateB = new int[slotCount];

        baseLocalScales = new Vector3[slotCount];
        previousVisualPositions = new Vector3[slotCount];
        previousVisualPositionValid = new bool[slotCount];
        vehicleCollisionVelocities = new Vector3[slotCount];
        vehicleAudioSources = new AudioSource[slotCount];
        vehicleWheelTransforms =
            new Transform[slotCount * 4];

        wheelBaseLocalRotations =
            new Quaternion[slotCount * 4];

        randomState = randomSeed & 65535;

        if (randomState == 0)
        {
            randomState = 1;
        }

        BuildLaneChangeRuleMappings();

        for (int i = 0; i < slotCount; i++)
        {
            Transform vehicleRoot = vehicleRoots[i];

            if (vehicleRoot == null)
            {
                baseLocalScales[i] = Vector3.one;
                continue;
            }

            baseLocalScales[i] =
                vehicleRoot.localScale *
                GetVehicleVisualScale(i);
            CacheVehicleMotionVisuals(i, vehicleRoot);
            vehicleRoot.gameObject.SetActive(false);
        }

        return true;
    }

    private void ResetSlots()
    {
        activeVehicleCount = 0;
        activeLaneChangeCount = 0;
        authorityPhysicsQueryCount = 0;
        authorityPhysicsHitCount = 0;
        authorityObstacleFrameCursor = 0;
        simulationAccumulator = 0f;
        respawnTimer = 0f;
        ResetRemoteSnapshotBuffer();
        laneVehicleCacheReady = false;
        laneVehicleCacheDirty = true;
        audioVisualUpdateTimer = 0f;
        refreshAudioVisualsThisFrame = true;

        for (int i = 0; i < slotCount; i++)
        {
            vehicleActive[i] = false;
            vehicleSpawnGeneration[i] = false;
            processedSlots[i] = false;
            signalCommittedToCross[i] = false;
            cachedSourceLeaderIndices[i] = -1;

            vehicleLaneIds[i] = -1;
            sampleHints[i] = -1;
            laneChangeTargetLaneIds[i] = -1;
            laneChangeRuleIndices[i] = -1;
            laneChangeBranchDecisions[i] = 0;

            vehicleS[i] = 0f;
            previousVehicleS[i] = 0f;
            previousVehicleSpeeds[i] = 0f;
            previousLaneChangeProgress[i] = 0f;
            previousVehicleLaneIds[i] = -1;
            previousLaneChangeActive[i] = false;
            vehicleSpeeds[i] = 0f;
            vehicleAccelerations[i] = 0f;
            vehicleRenderMaximumS[i] = -1f;
            speedFactors[i] = 1f;
            wheelAngles[i] = 0f;
            frontWheelSteeringAngles[i] = 0f;
            laneChangeProgress[i] = 0f;
            laneChangeCooldownRemaining[i] = 0f;
            laneChangeEvaluationRemaining[i] = 0f;
            laneChangeRecoveryDistance[i] = 0f;
            laneChangeRecoveryGearHoldRemaining[i] = 0f;
            laneChangeActive[i] = false;
            ResetVehicleObstacleState(i);
            physicsObstacleCastDebugCenters[i] = Vector3.zero;
            physicsObstacleCastDebugHalfExtents[i] = Vector3.zero;
            physicsObstacleCastDebugDirections[i] = Vector3.forward;
            physicsObstacleCastDebugRotations[i] = Quaternion.identity;
            physicsObstacleCastDebugDistances[i] = 0f;
            physicsObstacleCastDebugHitDistances[i] = 0f;
            physicsObstacleStopS[i] = -1f;
            physicsObstacleHitS[i] = -1f;
            laneChangeObstacleSweepStartPositions[i] =
                Vector3.zero;
            laneChangeObstacleSweepEndPositions[i] =
                Vector3.zero;
            laneChangeObstacleSweepStartRotations[i] =
                Quaternion.identity;
            laneChangeObstacleSweepEndRotations[i] =
                Quaternion.identity;
            laneChangeReverseManeuver[i] = false;
            laneChangeEmergencyManeuver[i] = false;
            laneChangePreparing[i] = false;
            laneChangeCurveSpeedLimits[i] = 0f;
            laneChangeReverseStartS[i] = 0f;
            maneuverPathValid[i] = false;
            maneuverPathSourceLaneIds[i] = -1;
            maneuverPathTargetLaneIds[i] = -1;
            maneuverPathRuleIndices[i] = -1;
            maneuverPathReverse[i] = false;
            maneuverPathEmergency[i] = false;
            maneuverPathRecoveryDistances[i] = 0f;

            visualActive[i] = false;
            visualSpawnGeneration[i] = false;

            snapshotPreviousActive[i] = false;
            snapshotNextActive[i] = false;
            snapshotPreviousSpawnGeneration[i] = false;
            snapshotNextSpawnGeneration[i] = false;
            snapshotPreviousLaneIds[i] = -1;
            snapshotNextLaneIds[i] = -1;
            snapshotPreviousTargetLaneIds[i] = -1;
            snapshotNextTargetLaneIds[i] = -1;

            snapshotPreviousS[i] = 0f;
            snapshotNextS[i] = 0f;
            snapshotPreviousSpeeds[i] = 0f;
            snapshotNextSpeeds[i] = 0f;
            snapshotPreviousAccelerations[i] = 0f;
            snapshotNextAccelerations[i] = 0f;
            snapshotPreviousSpeedFactors[i] = 1f;
            snapshotNextSpeedFactors[i] = 1f;
            snapshotPreviousLaneChangeProgress[i] = 0f;
            snapshotNextLaneChangeProgress[i] = 0f;
            snapshotPreviousRecoveryDistance[i] = 0f;
            snapshotNextRecoveryDistance[i] = 0f;
            snapshotPreviousLaneChangeActive[i] = false;
            snapshotNextLaneChangeActive[i] = false;
            snapshotPreviousEmergencyManeuver[i] = false;
            snapshotNextEmergencyManeuver[i] = false;
            snapshotPreviousReverseManeuver[i] = false;
            snapshotNextReverseManeuver[i] = false;
            snapshotBufferedStateA[i] = 0;
            snapshotBufferedStateB[i] = 0;
            previousVisualPositions[i] = Vector3.zero;
            previousVisualPositionValid[i] = false;
            vehicleCollisionVelocities[i] = Vector3.zero;

            ResetVehicleMotionVisuals(i);

            Transform vehicleRoot = vehicleRoots[i];

            if (vehicleRoot == null)
            {
                continue;
            }

            vehicleRoot.localScale =
                baseLocalScales[i];

            vehicleRoot.gameObject.SetActive(false);
        }
    }

    private void SimulateStep(float deltaTime)
    {
        int nextSignalState;

        if (mainSignal != null)
        {
            nextSignalState = mainSignal.GetSignalState();
        }
        else
        {
            nextSignalState = ShinhoTime.SignalGreen;
        }

        bool signalQueueReleased =
            currentSignalState != ShinhoTime.SignalGreen &&
            nextSignalState == ShinhoTime.SignalGreen;

        currentSignalState = nextSignalState;

        if (signalQueueReleased)
        {
            signalQueueReleaseHoldRemaining = Mathf.Max(
                0.5f,
                signalQueueReleaseGraceTime
            );
        }
        else if (currentSignalState == ShinhoTime.SignalGreen)
        {
            signalQueueReleaseHoldRemaining = Mathf.Max(
                0f,
                signalQueueReleaseHoldRemaining - deltaTime
            );
        }
        else
        {
            signalQueueReleaseHoldRemaining = 0f;
        }
        
        activeLaneChangeCount = 0;

        for (int i = 0; i < slotCount; i++)
        {
            processedSlots[i] = false;

            if (vehicleActive[i])
            {
                previousVehicleS[i] = vehicleS[i];
                previousVehicleSpeeds[i] = vehicleSpeeds[i];
                previousLaneChangeProgress[i] =
                    laneChangeProgress[i];
                previousVehicleLaneIds[i] =
                    vehicleLaneIds[i];
                previousLaneChangeActive[i] =
                    laneChangeActive[i];
                laneChangeCooldownRemaining[i] =
                    Mathf.Max(
                        0f,
                        laneChangeCooldownRemaining[i] -
                        deltaTime
                    );

                laneChangeEvaluationRemaining[i] =
                    Mathf.Max(
                        0f,
                        laneChangeEvaluationRemaining[i] -
                        deltaTime
                    );

                if (laneChangeActive[i])
                {
                    activeLaneChangeCount++;
                }
            }
        }

        BuildLaneVehicleCaches();

        for (int laneId = 0;
             laneId < laneDatabase.laneCount;
             laneId++)
        {
            SimulateLane(laneId, deltaTime);
        }

        respawnTimer = Mathf.Max(
            0f,
            respawnTimer - Mathf.Max(0f, deltaTime)
        );
        TryFillVehicleSlots();
    }

    private void SimulateLane(
        int laneId,
        float deltaTime)
    {
        if (laneVehicleCacheReady &&
            laneId >= 0 &&
            laneId < sourceLaneVehicleCounts.Length)
        {
            int count = sourceLaneVehicleCounts[laneId];
            int offset = laneId * slotCount;

            for (int orderIndex = count - 1;
                 orderIndex >= 0;
                 orderIndex--)
            {
                int vehicleIndex =
                    sourceLaneVehicleOrder[offset + orderIndex];

                if (vehicleIndex < 0 ||
                    vehicleIndex >= slotCount ||
                    !vehicleActive[vehicleIndex] ||
                    processedSlots[vehicleIndex] ||
                    vehicleLaneIds[vehicleIndex] != laneId)
                {
                    continue;
                }

                processedSlots[vehicleIndex] = true;
                AdvanceVehicle(vehicleIndex, deltaTime);
            }

            return;
        }

        while (true)
        {
            int nextVehicleIndex = -1;
            float greatestS = -1000000f;

            for (int i = 0; i < slotCount; i++)
            {
                if (!vehicleActive[i] ||
                    processedSlots[i] ||
                    vehicleLaneIds[i] != laneId)
                {
                    continue;
                }

                if (previousVehicleS[i] > greatestS)
                {
                    greatestS = previousVehicleS[i];
                    nextVehicleIndex = i;
                }
            }

            if (nextVehicleIndex < 0)
            {
                break;
            }

            processedSlots[nextVehicleIndex] = true;

            AdvanceVehicle(
                nextVehicleIndex,
                deltaTime
            );
        }
    }

    private void BuildLaneVehicleCaches()
    {
        int laneCount = Mathf.Min(
            laneDatabase.laneCount,
            TrafficLaneDatabase.FixedLaneCount
        );

        for (int laneId = 0;
             laneId < TrafficLaneDatabase.FixedLaneCount;
             laneId++)
        {
            sourceLaneVehicleCounts[laneId] = 0;
            reservationLaneVehicleCounts[laneId] = 0;
        }

        for (int i = 0; i < slotCount; i++)
        {
            cachedSourceLeaderIndices[i] = -1;

            if (!vehicleActive[i])
            {
                continue;
            }

            int sourceLaneId = vehicleLaneIds[i];

            if (sourceLaneId < 0 || sourceLaneId >= laneCount)
            {
                continue;
            }

            AddSourceLaneVehicle(sourceLaneId, i);
            AddReservationLaneVehicle(sourceLaneId, i);

            if (!laneChangeActive[i])
            {
                continue;
            }

            int targetLaneId = laneChangeTargetLaneIds[i];

            if (targetLaneId >= 0 &&
                targetLaneId < laneCount &&
                targetLaneId != sourceLaneId)
            {
                AddReservationLaneVehicle(targetLaneId, i);
            }
        }

        laneVehicleCacheReady = true;
        laneVehicleCacheDirty = false;
    }

    private void AddSourceLaneVehicle(
        int laneId,
        int vehicleIndex)
    {
        int count = sourceLaneVehicleCounts[laneId];

        if (count >= slotCount)
        {
            return;
        }

        int offset = laneId * slotCount;
        int insertIndex = count;
        float sourceS = previousVehicleS[vehicleIndex];

        while (insertIndex > 0)
        {
            int previousIndex = sourceLaneVehicleOrder[
                offset + insertIndex - 1
            ];

            if (previousVehicleS[previousIndex] <= sourceS)
            {
                break;
            }

            sourceLaneVehicleOrder[offset + insertIndex] =
                previousIndex;
            insertIndex--;
        }

        sourceLaneVehicleOrder[offset + insertIndex] =
            vehicleIndex;
        sourceLaneVehicleCounts[laneId] = count + 1;
    }

    private void AddReservationLaneVehicle(
        int laneId,
        int vehicleIndex)
    {
        int count = reservationLaneVehicleCounts[laneId];

        if (count >= slotCount)
        {
            return;
        }

        reservationLaneVehicleOrder[
            laneId * slotCount + count
        ] = vehicleIndex;
        reservationLaneVehicleCounts[laneId] = count + 1;
    }

    private void InvalidateLaneVehicleCaches()
    {
        laneVehicleCacheDirty = true;
    }

    private void AdvanceVehicle(
        int vehicleIndex,
        float deltaTime)
    {
        int laneId = vehicleLaneIds[vehicleIndex];
        vehicleRenderMaximumS[vehicleIndex] = -1f;

        float oldS =
            previousVehicleS[vehicleIndex];

        float currentSpeed =
            vehicleSpeeds[vehicleIndex];
        signalStopDebugActive[vehicleIndex] = false;

        if (laneChangeActive[vehicleIndex] &&
            !laneChangePreparing[vehicleIndex])
        {
            EnsureManeuverPath(
                vehicleIndex,
                oldS,
                laneChangeProgress[vehicleIndex]
            );

            if (!IsManeuverPathUsable(vehicleIndex))
            {
                StopVehicleForLaneChangeObstacle(
                    vehicleIndex,
                    deltaTime
                );
                return;
            }
        }

        if (!laneChangeActive[vehicleIndex])
        {
            laneChangePlayerBlocked[vehicleIndex] = false;
            laneChangeVehicleBlocked[vehicleIndex] = false;
            laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
            laneChangeObstacleRestartHoldRemaining[
                vehicleIndex
            ] = 0f;
            laneChangeObstacleClearHoldRemaining[
                vehicleIndex
            ] = 0f;
            laneChangeObstacleSweepDebugValid[
                vehicleIndex
            ] = false;
        }
        else if (laneChangeReverseManeuver[vehicleIndex] &&
                 laneChangeProgress[vehicleIndex] <
                     GetRecoveryPreparationEndProgress())
        {
            UpdateBlockedLaneChangeRecoveryManeuver(
                vehicleIndex,
                deltaTime
            );

            return;
        }
        else if (ShouldHoldLaneChangeForObstacle(
                     vehicleIndex,
                     deltaTime
                 ))
        {
            return;
        }

        int leaderIndex = FindNearestLeaderOnLane(
            vehicleIndex,
            laneId,
            oldS
        );

        if (TryBeginBlockedLaneChangeRecovery(
            vehicleIndex,
            leaderIndex
        ))
        {
            return;
        }

        bool laneChangeWasActive =
            laneChangeActive[vehicleIndex];

        TryBeginLaneChange(
            vehicleIndex,
            leaderIndex
        );

        // A newly-created reverse recovery path starts by moving backward.
        // Do not let the remainder of this forward-driving tick remap that
        // non-monotonic path from the vehicle's source-lane coordinate.
        if (!laneChangeWasActive &&
            laneChangeActive[vehicleIndex] &&
            laneChangeReverseManeuver[vehicleIndex])
        {
            return;
        }

        if (laneChangeActive[vehicleIndex] &&
            !laneChangePreparing[vehicleIndex] &&
            !IsManeuverPathUsable(vehicleIndex))
        {
            StopVehicleForLaneChangeObstacle(
                vehicleIndex,
                deltaTime
            );
            return;
        }

        float sourceLeaderInfluence =
            GetSourceLaneConstraintInfluence(vehicleIndex);

        float targetSpeed = GetCruiseSpeed(
            vehicleIndex,
            laneId
        );

        float bumperGap = 1000000f;
        float followingMinimumGap =
            GetMinimumGap(vehicleIndex);

        if (leaderIndex >= 0)
        {
            float leaderS = GetVehicleSOnLane(
                leaderIndex,
                laneId
            );

            bumperGap =
                leaderS -
                oldS -
                GetCombinedHalfLength(
                    vehicleIndex,
                    leaderIndex
                );

            float followingSpeed =
                (bumperGap - followingMinimumGap) /
                Mathf.Max(
                    0.1f,
                    GetTimeHeadway(vehicleIndex)
                );

            float restrictedSpeed = Mathf.Max(
                0f,
                followingSpeed
            );

            targetSpeed = Mathf.Min(
                targetSpeed,
                Mathf.Lerp(
                    targetSpeed,
                    restrictedSpeed,
                    sourceLeaderInfluence
                )
            );
        }

        int targetLeaderIndex = -1;
        float targetLeaderS = 0f;
        float targetBumperGap = 1000000f;

        if (laneChangeActive[vehicleIndex])
        {
            int targetLaneId =
                laneChangeTargetLaneIds[vehicleIndex];

            int ruleIndex =
                laneChangeRuleIndices[vehicleIndex];

            float targetS = MapSourceToTargetS(
                ruleIndex,
                oldS
            );

            targetLeaderIndex =
                FindNearestLeaderOnLane(
                    vehicleIndex,
                    targetLaneId,
                    targetS
                );

            if (targetLeaderIndex >= 0)
            {
                targetLeaderS = GetVehicleSOnLane(
                    targetLeaderIndex,
                    targetLaneId
                );

                targetBumperGap =
                    targetLeaderS -
                    targetS -
                    GetCombinedHalfLength(
                        vehicleIndex,
                        targetLeaderIndex
                    );

                float targetFollowingSpeed =
                    (targetBumperGap -
                     followingMinimumGap) /
                    Mathf.Max(
                        0.1f,
                        GetTimeHeadway(vehicleIndex)
                    );

                targetSpeed = Mathf.Min(
                    targetSpeed,
                    Mathf.Max(
                        0f,
                        targetFollowingSpeed
                    )
                );
            }

            if (laneChangeSpeedLimit > 0f)
            {
                targetSpeed = Mathf.Min(
                    targetSpeed,
                    laneChangeSpeedLimit
                );
            }

            if (!laneChangeReverseManeuver[vehicleIndex])
            {
                float curveSpeedLimit =
                    laneChangeCurveSpeedLimits[vehicleIndex] > 0.1f
                        ? laneChangeCurveSpeedLimits[vehicleIndex]
                        : GetLaneChangeCurveSpeedLimit(
                            vehicleIndex,
                            laneChangeEmergencyManeuver[vehicleIndex],
                            oldS
                          );

                targetSpeed = Mathf.Min(
                    targetSpeed,
                    curveSpeedLimit
                );

                if (laneChangePreparing[vehicleIndex] &&
                    currentSpeed <=
                        curveSpeedLimit +
                        Mathf.Max(
                            0.01f,
                            laneChangePreparationSpeedTolerance
                        ))
                {
                    float remainingRuleDistance =
                        laneDatabase.changeEndS[ruleIndex] - oldS;
                    bool canEnterCurve =
                        remainingRuleDistance >=
                            GetNormalLaneChangeTravelDistance(
                                laneChangeEmergencyManeuver[vehicleIndex]
                            ) &&
                        IsLaneChangeFullPathClear(
                            vehicleIndex,
                            ruleIndex,
                            laneChangeEmergencyManeuver[vehicleIndex],
                            oldS
                        );

                    if (canEnterCurve)
                    {
                        laneChangePreparing[vehicleIndex] = false;
                        BuildManeuverPath(
                            vehicleIndex,
                            oldS
                        );
                    }
                    else
                    {
                        // Keep both lanes reserved and wait in the source
                        // lane. Entering a no-longer-clear curve would force
                        // either a lateral slide or an overlap.
                        targetSpeed = 0f;
                    }
                }
            }
        }

        float signalStopS = GetSignalStopS(
            vehicleIndex,
            oldS,
            currentSpeed
        );

        float detectedPlayerStopS = GetPlayerStopS(
            vehicleIndex,
            laneId,
            oldS
        );

        float playerStopS = ApplyPlayerObstacleReleaseHold(
            vehicleIndex,
            detectedPlayerStopS,
            oldS,
            deltaTime
        );

        bool physicalObstacleRestartHold =
            UpdatePhysicalObstacleRestartGate(
                vehicleIndex,
                detectedPlayerStopS,
                oldS,
                currentSpeed,
                deltaTime
            );

        if (physicalObstacleRestartHold)
        {
            playerStopS = physicalObstacleRestartHoldS[vehicleIndex];
        }

        signalStopDebugActive[vehicleIndex] =
            signalStopS >= 0f &&
            (playerStopS < 0f || signalStopS <= playerStopS);

        float virtualStopS = -1f;
        float virtualDeceleration =
            signalComfortDeceleration;

        if (signalStopS >= 0f)
        {
            virtualStopS = signalStopS;
        }

        if (playerStopS >= 0f &&
            (virtualStopS < 0f ||
             playerStopS < virtualStopS))
        {
            virtualStopS = playerStopS;
            virtualDeceleration =
                playerComfortDeceleration;
        }

        if (virtualStopS >= 0f)
        {
            float distanceToStop =
                Mathf.Max(
                    0f,
                    virtualStopS - oldS
                );

            float virtualSpeedLimit = Mathf.Sqrt(
                2f *
                Mathf.Max(
                    0.1f,
                    virtualDeceleration
                ) *
                distanceToStop
            );

            targetSpeed = Mathf.Min(
                targetSpeed,
                virtualSpeedLimit
            );
        }

        float stopThreshold = Mathf.Clamp(
            stoppedSpeedThreshold,
            0.05f,
            0.5f
        );

        bool naturallyStoppedByObstacle =
            targetSpeed <= stopThreshold &&
            currentSpeed <= stopThreshold + 0.1f &&
            ((leaderIndex >= 0 &&
              sourceLeaderInfluence > 0.001f) ||
             targetLeaderIndex >= 0 ||
             virtualStopS >= 0f);

        if (physicalObstacleRestartHold)
        {
            targetSpeed = 0f;
            waitingForObstacleRelease[vehicleIndex] = false;
            obstacleRestartDelayRemaining[vehicleIndex] = 0f;
        }
        else if (naturallyStoppedByObstacle)
        {
            waitingForObstacleRelease[vehicleIndex] = true;
            obstacleRestartDelayRemaining[vehicleIndex] =
                GetObstacleRestartDelay(vehicleIndex);
        }
        else if (waitingForObstacleRelease[vehicleIndex])
        {
            obstacleRestartDelayRemaining[vehicleIndex] =
                TickObstacleHoldTimer(
                    obstacleRestartDelayRemaining[vehicleIndex],
                    deltaTime
                );

            if (obstacleRestartDelayRemaining[vehicleIndex] > 0f)
            {
                targetSpeed = 0f;
            }
            else
            {
                waitingForObstacleRelease[vehicleIndex] = false;
            }
        }

        float effectiveSourceBumperGap = 1000000f;

        if (leaderIndex >= 0 &&
            sourceLeaderInfluence > 0.001f)
        {
            float sourcePassAllowance =
                GetVehicleLength(vehicleIndex) +
                GetVehicleLength(leaderIndex) +
                followingMinimumGap;

            effectiveSourceBumperGap = bumperGap +
                (1f - sourceLeaderInfluence) *
                sourcePassAllowance;
        }

        float speedChangeRate;

        if (targetSpeed >= currentSpeed)
        {
            speedChangeRate =
                GetAcceleration(vehicleIndex);
        }
        else if (Mathf.Min(
                     effectiveSourceBumperGap,
                     targetBumperGap
                 ) <
                 followingMinimumGap + 1f)
        {
            speedChangeRate =
                GetEmergencyDeceleration(
                    vehicleIndex
                );
        }
        else
        {
            speedChangeRate =
                GetDeceleration(vehicleIndex);
        }

        float newSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            speedChangeRate * deltaTime
        );

        float longitudinalMotionFactor =
            laneChangeActive[vehicleIndex] &&
            !laneChangePreparing[vehicleIndex]
                ? GetManeuverSourceAdvanceFactor(
                    vehicleIndex,
                    laneChangeProgress[vehicleIndex]
                  )
                : 1f;
        float newS =
            oldS +
            (currentSpeed + newSpeed) *
            0.5f *
            longitudinalMotionFactor *
            deltaTime;

        bool hasMaximumS = false;
        float maximumS = 1000000f;

        if (leaderIndex >= 0 &&
            sourceLeaderInfluence > 0.001f)
        {
            float leaderS = GetVehicleSOnLane(
                leaderIndex,
                laneId
            );

            float baseMaximumS =
                leaderS -
                GetCombinedHalfLength(
                    vehicleIndex,
                    leaderIndex
                ) -
                followingMinimumGap;

            float passAllowance =
                GetVehicleLength(vehicleIndex) +
                GetVehicleLength(leaderIndex) +
                followingMinimumGap;

            maximumS =
                baseMaximumS +
                (1f - sourceLeaderInfluence) *
                passAllowance;

            hasMaximumS = true;
        }

        if (targetLeaderIndex >= 0)
        {
            float targetMaximumS =
                targetLeaderS -
                GetCombinedHalfLength(
                    vehicleIndex,
                    targetLeaderIndex
                ) -
                followingMinimumGap;

            float sourceMaximumS = MapTargetToSourceS(
                laneChangeRuleIndices[vehicleIndex],
                targetMaximumS
            );

            maximumS = hasMaximumS
                ? Mathf.Min(maximumS, sourceMaximumS)
                : sourceMaximumS;

            hasMaximumS = true;
        }

        if (virtualStopS >= 0f)
        {
            maximumS = hasMaximumS
                ? Mathf.Min(maximumS, virtualStopS)
                : virtualStopS;

            hasMaximumS = true;
        }

        vehicleRenderMaximumS[vehicleIndex] = hasMaximumS
            ? maximumS
            : -1f;

        if (hasMaximumS && maximumS >= oldS)
        {
            float remainingDistance = maximumS - oldS;
            float smoothingDistance = Mathf.Max(
                0.1f,
                stopSmoothingDistance
            );

            if (remainingDistance <= smoothingDistance)
            {
                float smoothSpeedLimit =
                    remainingDistance /
                    Mathf.Max(0.1f, stopSmoothingTime);

                newSpeed = Mathf.Min(
                    newSpeed,
                    smoothSpeedLimit
                );

                newS = oldS +
                    (currentSpeed + newSpeed) *
                    0.5f *
                    longitudinalMotionFactor *
                    deltaTime;

                if (remainingDistance <= Mathf.Max(
                        0.001f,
                        stopSnapDistance
                    ) &&
                    newSpeed <= Mathf.Max(
                        0.005f,
                        stopSnapSpeed
                    ))
                {
                    newS = maximumS;
                    newSpeed = 0f;
                }
            }
        }

        if (hasMaximumS &&
            newS > maximumS)
        {
            if (maximumS >= oldS)
            {
                newS = maximumS;

                newSpeed = Mathf.Min(
                    newSpeed,
                    Mathf.Max(
                        0f,
                        (newS - oldS) / deltaTime
                    )
                );
            }
            else
            {
                newS = oldS;
                newSpeed = 0f;
            }
        }

        bool obstacleSweepBlocked = false;

        if (laneChangeActive[vehicleIndex])
        {
            float candidateProgress;

            if (laneChangePreparing[vehicleIndex])
            {
                candidateProgress = 0f;
            }
            else if (IsManeuverPathUsable(vehicleIndex))
            {
                float pathTravelDistance =
                    (currentSpeed + newSpeed) *
                    0.5f *
                    deltaTime;
                candidateProgress =
                    AdvanceManeuverProgressByDistance(
                        vehicleIndex,
                        laneChangeProgress[vehicleIndex],
                        pathTravelDistance,
                        1f
                    );

                newS = SampleManeuverPathSourceS(
                    vehicleIndex,
                    candidateProgress
                );

                if (hasMaximumS)
                {
                    if (maximumS < oldS)
                    {
                        candidateProgress =
                            laneChangeProgress[vehicleIndex];
                        newS = oldS;
                        newSpeed = 0f;
                    }
                    else if (newS > maximumS)
                    {
                        candidateProgress =
                            FindManeuverProgressForSourceS(
                                vehicleIndex,
                                laneChangeProgress[vehicleIndex],
                                maximumS
                            );
                        newS = SampleManeuverPathSourceS(
                            vehicleIndex,
                            candidateProgress
                        );

                        float allowedPathDistance = Mathf.Max(
                            0f,
                            SampleManeuverPathDistance(
                                vehicleIndex,
                                candidateProgress
                            ) -
                            SampleManeuverPathDistance(
                                vehicleIndex,
                                laneChangeProgress[vehicleIndex]
                            )
                        );
                        float allowedEndSpeed = Mathf.Max(
                            0f,
                            2f * allowedPathDistance /
                                Mathf.Max(0.0001f, deltaTime) -
                            currentSpeed
                        );
                        newSpeed = Mathf.Min(
                            newSpeed,
                            allowedEndSpeed
                        );
                    }
                }
            }
            else
            {
                // A manoeuvre has one authoritative trajectory. If its fixed
                // path is temporarily unavailable, hold this tick instead of
                // generating a second procedural motion.
                candidateProgress = laneChangeProgress[vehicleIndex];
                newS = oldS;
                newSpeed = 0f;
            }

            obstacleSweepBlocked =
                EvaluateLaneChangeObstacleSweep(
                    vehicleIndex,
                    oldS,
                    laneChangeProgress[vehicleIndex],
                    newS,
                    candidateProgress
                );

            if (obstacleSweepBlocked)
            {
                newS = laneChangeObstacleSweepSafeS;
                laneChangeProgress[vehicleIndex] =
                    laneChangeObstacleSweepSafeProgress;
                newSpeed = 0f;

                vehicleRenderMaximumS[vehicleIndex] = newS;
            }
            else
            {
                laneChangeProgress[vehicleIndex] =
                    candidateProgress;
            }
        }

        vehicleS[vehicleIndex] = newS;
        vehicleSpeeds[vehicleIndex] = newSpeed;
        vehicleAccelerations[vehicleIndex] =
            deltaTime > 0.0001f
                ? (newSpeed - currentSpeed) / deltaTime
                : 0f;

        if (!obstacleSweepBlocked)
        {
            UpdateLaneChange(vehicleIndex);
        }

        laneId = vehicleLaneIds[vehicleIndex];

        if (vehicleS[vehicleIndex] >=
            GetLaneEndS(laneId))
        {
            DeactivateVehicle(vehicleIndex);
        }
    }

    private float GetPlayerStopS(
        int vehicleIndex,
        int laneId,
        float currentS)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= physicsObstacleStopS.Length)
        {
            return -1f;
        }

        return physicsObstacleStopS[vehicleIndex];
    }

    private float GetPhysicsObstacleHitS(int vehicleIndex)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= physicsObstacleHitS.Length)
        {
            return -1f;
        }

        return physicsObstacleHitS[vehicleIndex];
    }

    private void UpdateAuthorityPhysicsObstacleSensorsFrame()
    {
        authorityPhysicsQueryCount = 0;
        authorityPhysicsHitCount = 0;

        if (!enableAuthorityPhysicsObstacles ||
            authorityObstacleLayerMask == 0 ||
            !localIsAuthority ||
            !authorityReady)
        {
            for (int i = 0; i < slotCount; i++)
            {
                ClearAuthorityPhysicsObstacleState(i);
            }

            return;
        }

        int budget = Mathf.Clamp(
            authorityObstacleVehiclesPerFrame,
            1,
            4
        );
        int checkedSlots = 0;
        int updatedVehicles = 0;

        while (checkedSlots < slotCount &&
               updatedVehicles < budget)
        {
            int vehicleIndex = authorityObstacleFrameCursor;
            authorityObstacleFrameCursor++;

            if (authorityObstacleFrameCursor >= slotCount)
            {
                authorityObstacleFrameCursor = 0;
            }

            checkedSlots++;

            if (!vehicleActive[vehicleIndex])
            {
                ClearAuthorityPhysicsObstacleState(vehicleIndex);
                continue;
            }

            UpdateAuthorityPhysicsObstacleSensor(
                vehicleIndex,
                vehicleS[vehicleIndex],
                vehicleSpeeds[vehicleIndex]
            );
            updatedVehicles++;
        }
    }

    private void ClearAuthorityPhysicsObstacleState(int vehicleIndex)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= physicsObstacleStopS.Length)
        {
            return;
        }

        physicsObstacleStopS[vehicleIndex] = -1f;
        physicsObstacleHitS[vehicleIndex] = -1f;
        physicsObstacleCastDebugValid[vehicleIndex] = false;
        physicsObstacleCastDebugBlocked[vehicleIndex] = false;
        physicsObstacleCastDebugLaneLimited[vehicleIndex] = false;
        physicsObstacleCastDebugHitDistances[vehicleIndex] = 0f;
    }

    private void UpdateAuthorityPhysicsObstacleSensor(
        int vehicleIndex,
        float currentS,
        float currentSpeed)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= physicsObstacleStopS.Length)
        {
            return;
        }

        ClearAuthorityPhysicsObstacleState(vehicleIndex);

        if (!enableAuthorityPhysicsObstacles ||
            authorityObstacleLayerMask == 0 ||
            !localIsAuthority ||
            !authorityReady)
        {
            return;
        }

        int laneId = vehicleLaneIds[vehicleIndex];

        if (laneId < 0 || laneId >= laneDatabase.laneCount)
        {
            return;
        }

        Vector3 vehiclePosition = laneChangeActive[vehicleIndex]
            ? GetLaneChangePathPosition(
                vehicleIndex,
                currentS,
                laneChangeProgress[vehicleIndex]
              )
            : laneDatabase.GetLanePosition(
                laneId,
                currentS,
                sampleHints[vehicleIndex]
              );

        Quaternion vehicleRotation =
            GetCurrentVehicleCollisionRotation(vehicleIndex);

        Vector3 boxCenter;
        Vector3 halfExtents;
        GetAuthorityPhysicsBox(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            Mathf.Max(0f, authorityObstacleSafetyMargin),
            out boxCenter,
            out halfExtents
        );

        Vector3 castDirection =
            vehicleRotation * Vector3.forward;

        if (castDirection.sqrMagnitude <= 0.0001f)
        {
            castDirection = Vector3.forward;
        }
        else
        {
            castDirection.Normalize();
        }

        float brakingDistance =
            currentSpeed * currentSpeed /
            (2f * Mathf.Max(0.1f, playerComfortDeceleration));

        float castDistance = Mathf.Max(
            Mathf.Max(0.5f, minimumObstacleLookAhead),
            brakingDistance +
            Mathf.Max(0f, playerFrontBuffer) +
            currentSpeed * Mathf.Max(
                0f,
                obstacleNetworkLookAheadTime
            )
        );

        if (!laneChangeActive[vehicleIndex] &&
            !IsTruck(vehicleIndex) &&
            HasAvailableLaneChangeRule(
                vehicleIndex,
                currentS
            ))
        {
            castDistance = Mathf.Max(
                castDistance,
                Mathf.Max(
                    Mathf.Max(
                        normalLaneChangeTravelDistance,
                        emergencyLaneChangeTravelDistance
                    ),
                    laneChangePlanningLookAhead
                )
            );
        }

        if (laneChangeActive[vehicleIndex])
        {
            bool laneLimited;
            castDistance = LimitPhysicsCastToLaneChangeCorridor(
                vehicleIndex,
                boxCenter,
                halfExtents,
                vehicleRotation,
                castDirection,
                castDistance,
                out laneLimited
            );
            physicsObstacleCastDebugLaneLimited[vehicleIndex] =
                laneLimited;
        }

        physicsObstacleCastDebugValid[vehicleIndex] = true;
        physicsObstacleCastDebugCenters[vehicleIndex] = boxCenter;
        physicsObstacleCastDebugHalfExtents[vehicleIndex] =
            halfExtents;
        physicsObstacleCastDebugDirections[vehicleIndex] =
            castDirection;
        physicsObstacleCastDebugRotations[vehicleIndex] =
            vehicleRotation;
        physicsObstacleCastDebugDistances[vehicleIndex] =
            castDistance;
        physicsObstacleCastDebugHitDistances[vehicleIndex] =
            castDistance;

        authorityPhysicsQueryCount++;

        bool blocked = Physics.CheckBox(
            boxCenter,
            halfExtents,
            vehicleRotation,
            authorityObstacleLayerMask
        );

        float hitDistance = 0f;
        if (!blocked && castDistance > 0.001f)
        {
            RaycastHit hit;
            authorityPhysicsQueryCount++;
            blocked = Physics.BoxCast(
                boxCenter,
                halfExtents,
                castDirection,
                out hit,
                vehicleRotation,
                castDistance,
                authorityObstacleLayerMask
            );

            if (blocked)
            {
                hitDistance = Mathf.Max(0f, hit.distance);
            }
        }

        physicsObstacleCastDebugBlocked[vehicleIndex] = blocked;

        if (!blocked)
        {
            return;
        }

        authorityPhysicsHitCount++;
        physicsObstacleCastDebugHitDistances[vehicleIndex] =
            hitDistance;
        physicsObstacleHitS[vehicleIndex] =
            currentS + Mathf.Max(0f, hitDistance);
        physicsObstacleStopS[vehicleIndex] = currentS +
            Mathf.Max(
                0f,
                hitDistance - Mathf.Max(0f, playerFrontBuffer)
            );
    }

    private float LimitPhysicsCastToLaneChangeCorridor(
        int vehicleIndex,
        Vector3 boxCenter,
        Vector3 halfExtents,
        Quaternion vehicleRotation,
        Vector3 castDirection,
        float requestedDistance,
        out bool limited)
    {
        limited = false;

        int ruleIndex = laneChangeRuleIndices[vehicleIndex];

        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules ||
            !rulePhysicsCorridorValid[ruleIndex])
        {
            return requestedDistance;
        }

        Vector3 targetSide = rulePhysicsCorridorAxis[ruleIndex];
        float minimumCoordinate =
            rulePhysicsCorridorMinimum[ruleIndex];
        float maximumCoordinate =
            rulePhysicsCorridorMaximum[ruleIndex];
        Vector3 boxRight = vehicleRotation * Vector3.right;
        Vector3 boxForward = vehicleRotation * Vector3.forward;
        float lateralRadius =
            Mathf.Abs(Vector3.Dot(boxRight, targetSide)) *
                halfExtents.x +
            Mathf.Abs(Vector3.Dot(boxForward, targetSide)) *
                halfExtents.z;
        float centerCoordinate = Vector3.Dot(boxCenter, targetSide);
        float lateralRate = Vector3.Dot(castDirection, targetSide);
        float corridorDistance = requestedDistance;

        if (lateralRate > 0.0001f)
        {
            corridorDistance =
                (maximumCoordinate -
                 (centerCoordinate + lateralRadius)) /
                lateralRate;
        }
        else if (lateralRate < -0.0001f)
        {
            corridorDistance =
                ((centerCoordinate - lateralRadius) -
                 minimumCoordinate) /
                -lateralRate;
        }

        corridorDistance = Mathf.Max(0f, corridorDistance);

        if (corridorDistance >= requestedDistance - 0.001f)
        {
            return requestedDistance;
        }

        limited = true;
        return corridorDistance;
    }

    private bool HasAvailableLaneChangeRule(
        int vehicleIndex,
        float sourceS)
    {
        if (!enableLaneChanges ||
            laneDatabase == null ||
            IsTruck(vehicleIndex))
        {
            return false;
        }

        int laneId = vehicleLaneIds[vehicleIndex];

        if (laneId < 0 ||
            laneId >= laneDatabase.laneCount)
        {
            return false;
        }

        int first = laneDatabase.laneRuleStarts[laneId];
        int count = laneDatabase.laneRuleCounts[laneId];

        for (int ruleIndex = first;
             ruleIndex < first + count;
             ruleIndex++)
        {
            if (laneDatabase.IsChangeAllowed(
                    ruleIndex,
                    sourceS,
                    TrafficLaneDatabase.VehicleCar
                ))
            {
                return true;
            }
        }

        return false;
    }

    private void GetAuthorityPhysicsBox(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        float safetyMargin,
        out Vector3 boxCenter,
        out Vector3 halfExtents)
    {
        Vector3 up = vehicleRotation * Vector3.up;

        if (up.sqrMagnitude <= 0.0001f)
        {
            up = Vector3.up;
        }
        else
        {
            up.Normalize();
        }

        boxCenter = GetVehicleFootprintCenter(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation
        ) + up * Mathf.Max(
            0.1f,
            authorityObstacleCastVerticalOffset
        );

        float margin = Mathf.Max(0f, safetyMargin);
        halfExtents = new Vector3(
            GetVehicleWidth(vehicleIndex) * 0.5f + margin,
            Mathf.Max(0.2f, authorityObstacleCastHeight * 0.5f),
            GetVehicleLength(vehicleIndex) * 0.5f + margin
        );
    }

    private bool IsPhysicsPoseBlockedByObstacle(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        float safetyMargin)
    {
        if (!enableAuthorityPhysicsObstacles ||
            authorityObstacleLayerMask == 0)
        {
            return false;
        }

        Vector3 boxCenter;
        Vector3 halfExtents;
        GetAuthorityPhysicsBox(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            safetyMargin,
            out boxCenter,
            out halfExtents
        );

        authorityPhysicsQueryCount++;
        bool blocked = Physics.CheckBox(
            boxCenter,
            halfExtents,
            vehicleRotation,
            authorityObstacleLayerMask
        );

        if (blocked)
        {
            authorityPhysicsHitCount++;
        }

        return blocked;
    }

    private bool IsPhysicsRearHalfBlocked(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        float safetyMargin)
    {
        if (!enableAuthorityPhysicsObstacles ||
            authorityObstacleLayerMask == 0)
        {
            return false;
        }

        Vector3 center;
        Vector3 halfExtents;
        GetPhysicsRearHalfBox(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            safetyMargin,
            out center,
            out halfExtents
        );

        authorityPhysicsQueryCount++;
        bool blocked = Physics.CheckBox(
            center,
            halfExtents,
            vehicleRotation,
            authorityObstacleLayerMask
        );

        if (blocked)
        {
            authorityPhysicsHitCount++;
        }

        return blocked;
    }

    private void GetPhysicsRearHalfBox(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        float safetyMargin,
        out Vector3 center,
        out Vector3 halfExtents)
    {
        Vector3 up = vehicleRotation * Vector3.up;
        up = up.sqrMagnitude > 0.0001f
            ? up.normalized
            : Vector3.up;

        Vector3 forward = vehicleRotation * Vector3.forward;
        forward = forward.sqrMagnitude > 0.0001f
            ? forward.normalized
            : Vector3.forward;

        float margin = Mathf.Max(0f, safetyMargin);
        float vehicleHalfLength =
            GetVehicleLength(vehicleIndex) * 0.5f;
        float rearHalfDepth = vehicleHalfLength + margin;

        center = GetVehicleFootprintCenter(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation
        );
        center -= forward * (
            vehicleHalfLength * 0.5f + margin * 0.5f
        );
        center += up * Mathf.Max(
            0.1f,
            authorityObstacleCastVerticalOffset
        );

        halfExtents = new Vector3(
            GetVehicleWidth(vehicleIndex) * 0.5f + margin,
            Mathf.Max(0.2f, authorityObstacleCastHeight * 0.5f),
            rearHalfDepth * 0.5f
        );
    }

    private bool IsPhysicsTargetSideBlocked(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        Vector3 targetLaneOffset,
        float safetyMargin)
    {
        Vector3 up = vehicleRotation * Vector3.up;
        up = up.sqrMagnitude > 0.0001f
            ? up.normalized
            : Vector3.up;

        Vector3 forward = Vector3.ProjectOnPlane(
            vehicleRotation * Vector3.forward,
            up
        );

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        float targetSideSign = Vector3.Dot(
            targetLaneOffset,
            right
        ) >= 0f ? 1f : -1f;

        Vector3 fullCenter;
        Vector3 fullHalfExtents;
        GetAuthorityPhysicsBox(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            safetyMargin,
            out fullCenter,
            out fullHalfExtents
        );

        float sideHalfWidth = fullHalfExtents.x * 0.5f;
        Vector3 center = fullCenter +
            right * (targetSideSign * sideHalfWidth);
        Vector3 halfExtents = new Vector3(
            sideHalfWidth,
            fullHalfExtents.y,
            fullHalfExtents.z
        );

        authorityPhysicsQueryCount++;
        bool blocked = Physics.CheckBox(
            center,
            halfExtents,
            vehicleRotation,
            authorityObstacleLayerMask
        );

        if (blocked)
        {
            authorityPhysicsHitCount++;
        }

        return blocked;
    }

    private float ApplyPhysicsReverseLimit(
        int vehicleIndex,
        float currentS,
        float minimumS)
    {
        float castDistance = Mathf.Max(0f, currentS - minimumS);

        if (!enableAuthorityPhysicsObstacles ||
            authorityObstacleLayerMask == 0 ||
            castDistance <= 0.001f)
        {
            return minimumS;
        }

        Vector3 vehiclePosition = GetLaneChangePathPosition(
            vehicleIndex,
            currentS,
            laneChangeProgress[vehicleIndex]
        );
        Quaternion vehicleRotation =
            GetCurrentVehicleCollisionRotation(vehicleIndex);
        Vector3 center;
        Vector3 halfExtents;
        GetPhysicsRearHalfBox(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            Mathf.Max(0f, authorityObstacleSafetyMargin),
            out center,
            out halfExtents
        );

        authorityPhysicsQueryCount++;
        bool blocked = Physics.CheckBox(
            center,
            halfExtents,
            vehicleRotation,
            authorityObstacleLayerMask
        );

        if (blocked)
        {
            authorityPhysicsHitCount++;
            return currentS;
        }

        Vector3 reverseDirection =
            -(vehicleRotation * Vector3.forward);
        reverseDirection = reverseDirection.sqrMagnitude > 0.0001f
            ? reverseDirection.normalized
            : Vector3.back;
        RaycastHit hit;
        authorityPhysicsQueryCount++;

        if (!Physics.BoxCast(
                center,
                halfExtents,
                reverseDirection,
                out hit,
                vehicleRotation,
                castDistance,
                authorityObstacleLayerMask
            ))
        {
            return minimumS;
        }

        authorityPhysicsHitCount++;
        float allowedReverseDistance = Mathf.Max(
            0f,
            hit.distance - Mathf.Max(0f, reverseRearClearance)
        );

        return Mathf.Max(
            minimumS,
            currentS - allowedReverseDistance
        );
    }

    private float ApplyPlayerObstacleReleaseHold(
        int vehicleIndex,
        float detectedStopS,
        float currentS,
        float deltaTime)
    {
        if (detectedStopS >= 0f)
        {
            lastPlayerStopS[vehicleIndex] = detectedStopS;
            playerObstacleHoldRemaining[vehicleIndex] =
                Mathf.Max(0f, playerObstacleReleaseHold);

            return detectedStopS;
        }

        float remaining = TickObstacleHoldTimer(
            playerObstacleHoldRemaining[vehicleIndex],
            deltaTime
        );

        playerObstacleHoldRemaining[vehicleIndex] = remaining;

        if (remaining <= 0f ||
            lastPlayerStopS[vehicleIndex] < 0f)
        {
            lastPlayerStopS[vehicleIndex] = -1f;
            return -1f;
        }

        return Mathf.Max(
            currentS,
            lastPlayerStopS[vehicleIndex]
        );
    }

    private bool UpdatePhysicalObstacleRestartGate(
        int vehicleIndex,
        float detectedStopS,
        float currentS,
        float currentSpeed,
        float deltaTime)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= physicalObstacleRestartPending.Length ||
            laneChangeActive[vehicleIndex])
        {
            return false;
        }

        float stopThreshold = Mathf.Clamp(
            stoppedSpeedThreshold,
            0.05f,
            0.5f
        );
        float detectedClearance = detectedStopS >= 0f
            ? Mathf.Max(0f, detectedStopS - currentS)
            : 1000000f;
        float latchDistance = Mathf.Max(
            0.25f,
            Mathf.Max(0.001f, stopSnapDistance) * 4f
        );

        if (!physicalObstacleRestartPending[vehicleIndex])
        {
            bool stoppedAtPhysicalObstacle =
                detectedStopS >= 0f &&
                detectedClearance <= latchDistance &&
                currentSpeed <= stopThreshold + 0.1f;

            if (!stoppedAtPhysicalObstacle)
            {
                return false;
            }

            physicalObstacleRestartPending[vehicleIndex] = true;
            physicalObstacleRestartHoldS[vehicleIndex] = currentS;
            ResetPhysicalObstacleRestartTimers(vehicleIndex);
            return true;
        }

        float requiredClearance = Mathf.Max(
            0.5f,
            physicalObstacleRestartClearance
        );

        if (detectedStopS >= 0f &&
            detectedClearance < requiredClearance)
        {
            // Do not follow an obstacle that only moved a short distance.
            // The full reaction sequence starts again after stable clearance.
            ResetPhysicalObstacleRestartTimers(vehicleIndex);
            return true;
        }

        float clearHold = TickObstacleHoldTimer(
            physicalObstacleClearHoldRemaining[vehicleIndex],
            deltaTime
        );
        physicalObstacleClearHoldRemaining[vehicleIndex] = clearHold;

        if (clearHold > 0f)
        {
            return true;
        }

        float reactionHold = TickObstacleHoldTimer(
            physicalObstacleReactionHoldRemaining[vehicleIndex],
            deltaTime
        );
        physicalObstacleReactionHoldRemaining[vehicleIndex] = reactionHold;

        if (reactionHold > 0f)
        {
            return true;
        }

        ClearPhysicalObstacleRestartState(vehicleIndex);
        waitingForObstacleRelease[vehicleIndex] = false;
        obstacleRestartDelayRemaining[vehicleIndex] = 0f;
        return false;
    }

    private void ResetPhysicalObstacleRestartTimers(int vehicleIndex)
    {
        physicalObstacleClearHoldRemaining[vehicleIndex] =
            Mathf.Max(0.1f, physicalObstacleClearConfirmationTime);
        physicalObstacleReactionHoldRemaining[vehicleIndex] =
            GetObstacleRestartDelay(vehicleIndex);
    }

    private float TickObstacleHoldTimer(
        float remaining,
        float deltaTime)
    {
        return Mathf.Max(
            0f,
            remaining - Mathf.Max(0f, deltaTime)
        );
    }

    private float GetObstacleClearConfirmationTime(
        bool playerBlocked)
    {
        return playerBlocked
            ? Mathf.Max(0.1f, playerObstacleReleaseHold)
            : 0.25f;
    }

    private void ClearPhysicalObstacleRestartState(int vehicleIndex)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= physicalObstacleRestartPending.Length)
        {
            return;
        }

        physicalObstacleRestartPending[vehicleIndex] = false;
        physicalObstacleRestartHoldS[vehicleIndex] = -1f;
        physicalObstacleClearHoldRemaining[vehicleIndex] = 0f;
        physicalObstacleReactionHoldRemaining[vehicleIndex] = 0f;
    }

    private void ResetVehicleObstacleState(int vehicleIndex)
    {
        playerObstacleHoldRemaining[vehicleIndex] = 0f;
        lastPlayerStopS[vehicleIndex] = -1f;
        obstacleRestartDelayRemaining[vehicleIndex] = 0f;
        waitingForObstacleRelease[vehicleIndex] = false;
        ClearPhysicalObstacleRestartState(vehicleIndex);

        laneChangePlayerBlocked[vehicleIndex] = false;
        laneChangeVehicleBlocked[vehicleIndex] = false;
        laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
        laneChangeObstacleRestartHoldRemaining[vehicleIndex] = 0f;
        laneChangeObstacleClearHoldRemaining[vehicleIndex] = 0f;
        laneChangeObstacleSweepDebugValid[vehicleIndex] = false;
        signalStopDebugActive[vehicleIndex] = false;
        ClearAuthorityPhysicsObstacleState(vehicleIndex);
    }

    private bool ShouldHoldLaneChangeForObstacle(
        int vehicleIndex,
        float deltaTime)
    {
        if (!laneChangeActive[vehicleIndex] ||
            (!enableLaneChangePlayerSweep &&
             !enableLaneChangeVehicleSweep &&
             recoveryRoadArea == null))
        {
            laneChangePlayerBlocked[vehicleIndex] = false;
            laneChangeVehicleBlocked[vehicleIndex] = false;
            laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
            laneChangeObstacleRestartHoldRemaining[
                vehicleIndex
            ] = 0f;
            laneChangeObstacleClearHoldRemaining[
                vehicleIndex
            ] = 0f;

            return false;
        }

        if (laneChangePlayerBlocked[vehicleIndex] ||
            laneChangeVehicleBlocked[vehicleIndex] ||
            laneChangeRoadBoundaryBlocked[vehicleIndex])
        {
            float currentS = vehicleS[vehicleIndex];
            float currentProgress =
                laneChangeProgress[vehicleIndex];

            Vector3 currentPosition =
                GetLaneChangePathPosition(
                    vehicleIndex,
                    currentS,
                    currentProgress
                );

            Quaternion currentRotation =
                GetLaneChangeSweepRotation(
                    vehicleIndex,
                    currentS,
                    currentProgress,
                    currentS,
                    currentProgress,
                    0f
                );

            int remainingBlockMask = 0;

            if (laneChangePlayerBlocked[vehicleIndex] &&
                (IsLaneChangePoseBlockedByPlayer(
                     vehicleIndex,
                     currentPosition,
                     currentRotation
                 ) ||
                 IsLaneChangePoseBlockedByPlayer(
                     vehicleIndex,
                     laneChangeObstacleSweepEndPositions[
                         vehicleIndex
                     ],
                     laneChangeObstacleSweepEndRotations[
                         vehicleIndex
                     ]
                 )))
            {
                remainingBlockMask |= LaneChangePlayerBlock;
            }

            if (laneChangeVehicleBlocked[vehicleIndex] &&
                (IsLaneChangePoseBlockedByVehicle(
                     vehicleIndex,
                     currentPosition,
                     currentRotation
                 ) ||
                 IsLaneChangePoseBlockedByVehicle(
                     vehicleIndex,
                     laneChangeObstacleSweepEndPositions[
                         vehicleIndex
                     ],
                     laneChangeObstacleSweepEndRotations[
                         vehicleIndex
                     ]
                 )))
            {
                remainingBlockMask |= LaneChangeVehicleBlock;
            }

            if (laneChangeRoadBoundaryBlocked[vehicleIndex] &&
                (!IsRecoveryRearInsideRoadArea(
                     vehicleIndex,
                     currentPosition,
                     currentRotation
                 ) ||
                 !IsRecoveryRearInsideRoadArea(
                     vehicleIndex,
                     laneChangeObstacleSweepEndPositions[
                         vehicleIndex
                     ],
                     laneChangeObstacleSweepEndRotations[
                         vehicleIndex
                     ]
                 )))
            {
                remainingBlockMask |= LaneChangeRoadBoundaryBlock;
            }

            if (remainingBlockMask != 0)
            {
                float clearHold =
                    GetObstacleClearConfirmationTime(
                        (remainingBlockMask &
                         LaneChangePlayerBlock) != 0
                    );

                laneChangeObstacleClearHoldRemaining[
                    vehicleIndex
                ] = clearHold;

                StopVehicleForLaneChangeObstacle(
                    vehicleIndex,
                    deltaTime
                );

                return true;
            }

            float clearRemaining = TickObstacleHoldTimer(
                laneChangeObstacleClearHoldRemaining[
                    vehicleIndex
                ],
                deltaTime
            );

            laneChangeObstacleClearHoldRemaining[
                vehicleIndex
            ] = clearRemaining;

            if (clearRemaining > 0f)
            {
                StopVehicleForLaneChangeObstacle(
                    vehicleIndex,
                    deltaTime
                );

                return true;
            }

            laneChangePlayerBlocked[vehicleIndex] = false;
            laneChangeVehicleBlocked[vehicleIndex] = false;
            laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
            laneChangeObstacleRestartHoldRemaining[
                vehicleIndex
            ] = GetObstacleRestartDelay(vehicleIndex);

            StopVehicleForLaneChangeObstacle(
                vehicleIndex,
                deltaTime
            );

            return true;
        }

        float restartHold =
            laneChangeObstacleRestartHoldRemaining[
                vehicleIndex
            ];

        if (restartHold <= 0f)
        {
            return false;
        }

        laneChangeObstacleRestartHoldRemaining[
            vehicleIndex
        ] = TickObstacleHoldTimer(
            restartHold,
            deltaTime
        );

        StopVehicleForLaneChangeObstacle(
            vehicleIndex,
            deltaTime
        );

        return true;
    }

    private void StopVehicleForLaneChangeObstacle(
        int vehicleIndex,
        float deltaTime)
    {
        float previousSpeed = vehicleSpeeds[vehicleIndex];

        vehicleSpeeds[vehicleIndex] = 0f;
        vehicleAccelerations[vehicleIndex] =
            deltaTime > 0.0001f
                ? -previousSpeed / deltaTime
                : 0f;

        vehicleRenderMaximumS[vehicleIndex] =
            vehicleS[vehicleIndex];
    }

    private bool EvaluateLaneChangeObstacleSweep(
        int vehicleIndex,
        float startS,
        float startProgress,
        float endS,
        float endProgress)
    {
        laneChangeObstacleSweepSafeS = startS;
        laneChangeObstacleSweepSafeProgress = startProgress;

        Vector3 startPosition = GetLaneChangePathPosition(
            vehicleIndex,
            startS,
            startProgress
        );

        Vector3 endPosition = GetLaneChangePathPosition(
            vehicleIndex,
            endS,
            endProgress
        );

        Quaternion startRotation =
            GetLaneChangeSweepRotation(
                vehicleIndex,
                startS,
                startProgress,
                endS,
                endProgress,
                0f
            );

        Quaternion endRotation =
            GetLaneChangeSweepRotation(
                vehicleIndex,
                startS,
                startProgress,
                endS,
                endProgress,
                1f
            );

        laneChangeObstacleSweepDebugValid[
            vehicleIndex
        ] = true;

        laneChangeObstacleSweepStartPositions[
            vehicleIndex
        ] = startPosition;

        laneChangeObstacleSweepEndPositions[
            vehicleIndex
        ] = endPosition;

        laneChangeObstacleSweepStartRotations[
            vehicleIndex
        ] = startRotation;

        laneChangeObstacleSweepEndRotations[
            vehicleIndex
        ] = endRotation;

        if (!enableLaneChangePlayerSweep &&
            !enableLaneChangeVehicleSweep &&
            recoveryRoadArea == null)
        {
            laneChangePlayerBlocked[vehicleIndex] = false;
            laneChangeVehicleBlocked[vehicleIndex] = false;
            laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
            return false;
        }

        bool allowReverseEscape =
            laneChangeReverseManeuver[vehicleIndex] &&
            endS < startS - 0.0001f &&
            IsReversePhase(true, startProgress);

        bool allowSourceLanePass =
            endProgress > startProgress + 0.0001f &&
            !IsReversePhase(
                laneChangeReverseManeuver[vehicleIndex],
                startProgress
            );

        int startBlockMask = GetLaneChangePoseUnsafeMask(
            vehicleIndex,
            startPosition,
            startRotation,
            allowReverseEscape,
            allowSourceLanePass
        );

        if (startBlockMask != 0)
        {
            MarkLaneChangeObstacleBlocked(
                vehicleIndex,
                startBlockMask
            );
            return true;
        }

        int sampleCount = Mathf.Clamp(
            laneChangeObstacleSweepSamples,
            2,
            5
        );

        float previousSafeFraction = 0f;

        for (int sample = 1;
             sample <= sampleCount;
             sample++)
        {
            float fraction =
                (float)sample / sampleCount;

            int sampleBlockMask =
                GetLaneChangeSweepFractionUnsafeMask(
                    vehicleIndex,
                    startS,
                    startProgress,
                    endS,
                    endProgress,
                    fraction,
                    allowReverseEscape,
                    allowSourceLanePass
                );

            if (sampleBlockMask == 0)
            {
                previousSafeFraction = fraction;
                continue;
            }

            float safeFraction = previousSafeFraction;

            laneChangeObstacleSweepSafeProgress = Mathf.Lerp(
                startProgress,
                endProgress,
                safeFraction
            );

            laneChangeObstacleSweepSafeS =
                IsManeuverPathUsable(vehicleIndex)
                    ? SampleManeuverPathSourceS(
                        vehicleIndex,
                        laneChangeObstacleSweepSafeProgress
                      )
                    : Mathf.Lerp(
                        startS,
                        endS,
                        safeFraction
                      );

            MarkLaneChangeObstacleBlocked(
                vehicleIndex,
                sampleBlockMask
            );
            return true;
        }

        laneChangeObstacleSweepSafeS = endS;
        laneChangeObstacleSweepSafeProgress = endProgress;
        laneChangePlayerBlocked[vehicleIndex] = false;
        laneChangeVehicleBlocked[vehicleIndex] = false;
        laneChangeRoadBoundaryBlocked[vehicleIndex] = false;

        return false;
    }

    private void MarkLaneChangeObstacleBlocked(
        int vehicleIndex,
        int blockMask)
    {
        laneChangePlayerBlocked[vehicleIndex] =
            (blockMask & LaneChangePlayerBlock) != 0;

        laneChangeVehicleBlocked[vehicleIndex] =
            (blockMask & LaneChangeVehicleBlock) != 0;

        laneChangeRoadBoundaryBlocked[vehicleIndex] =
            (blockMask & LaneChangeRoadBoundaryBlock) != 0;

        laneChangeObstacleClearHoldRemaining[
            vehicleIndex
        ] = GetObstacleClearConfirmationTime(
            laneChangePlayerBlocked[vehicleIndex]
        );

        laneChangeObstacleRestartHoldRemaining[
            vehicleIndex
        ] = 0f;

    }

    private int GetLaneChangeSweepFractionUnsafeMask(
        int vehicleIndex,
        float startS,
        float startProgress,
        float endS,
        float endProgress,
        float fraction,
        bool allowReverseEscape,
        bool allowSourceLanePass)
    {
        float sampleS = Mathf.Lerp(
            startS,
            endS,
            fraction
        );

        float sampleProgress = Mathf.Lerp(
            startProgress,
            endProgress,
            fraction
        );

        Vector3 position = GetLaneChangePathPosition(
            vehicleIndex,
            sampleS,
            sampleProgress
        );

        Quaternion rotation = GetLaneChangeSweepRotation(
            vehicleIndex,
            startS,
            startProgress,
            endS,
            endProgress,
            fraction
        );

        return GetLaneChangePoseUnsafeMask(
            vehicleIndex,
            position,
            rotation,
            allowReverseEscape,
            allowSourceLanePass
        );
    }

    private int GetLaneChangePoseUnsafeMask(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        bool allowReverseEscape,
        bool allowSourceLanePass)
    {
        int blockMask = GetLaneChangePoseBlockMask(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation
        );

        if (blockMask == 0)
        {
            return 0;
        }

        if (allowReverseEscape &&
            CanReverseEscapeLaneChangePose(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation,
                blockMask
            ))
        {
            return 0;
        }

        if (!allowSourceLanePass)
        {
            return blockMask;
        }

        int remainingMask = blockMask;

        if ((remainingMask & LaneChangePlayerBlock) != 0 &&
            CanAdvancePastSourceLanePlayerMargin(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation
            ))
        {
            remainingMask &= ~LaneChangePlayerBlock;
        }

        if ((remainingMask & LaneChangeVehicleBlock) != 0 &&
            CanAdvancePastSourceLaneSafetyMargin(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation
            ))
        {
            remainingMask &= ~LaneChangeVehicleBlock;
        }

        return remainingMask;
    }

    private int GetLaneChangePoseBlockMask(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation)
    {
        int blockMask = 0;

        if (enableLaneChangePlayerSweep &&
            IsLaneChangePoseBlockedByPlayer(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation
            ))
        {
            blockMask |= LaneChangePlayerBlock;
        }

        if (enableLaneChangeVehicleSweep &&
            IsLaneChangePoseBlockedByVehicle(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation
            ))
        {
            blockMask |= LaneChangeVehicleBlock;
        }

        if (laneChangeReverseManeuver[vehicleIndex] &&
            !IsRecoveryRearInsideRoadArea(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation
            ))
        {
            blockMask |= LaneChangeRoadBoundaryBlock;
        }

        return blockMask;
    }

    private bool IsRecoveryRearInsideRoadArea(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation)
    {
        if (recoveryRoadArea == null)
        {
            return true;
        }

        Vector3 up = vehicleRotation * Vector3.up;
        up = up.sqrMagnitude > 0.0001f
            ? up.normalized
            : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(
            vehicleRotation * Vector3.forward,
            up
        );

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        float halfWidth =
            GetVehicleWidth(vehicleIndex) * 0.5f;
        Vector3 rearCenter =
            vehiclePosition -
            forward * GetVehicleRearExtent(vehicleIndex);

        return IsPointInsideRecoveryRoadArea(
                   rearCenter - right * halfWidth
               ) &&
               IsPointInsideRecoveryRoadArea(
                   rearCenter + right * halfWidth
               );
    }

    private bool IsPointInsideRecoveryRoadArea(
        Vector3 worldPosition)
    {
        if (recoveryRoadArea == null)
        {
            return true;
        }

        Vector3 localPosition =
            recoveryRoadArea.transform.InverseTransformPoint(
                worldPosition
            );
        Vector3 center = recoveryRoadArea.center;
        Vector3 halfSize = recoveryRoadArea.size * 0.5f;
        float edgeMargin = 0.05f;

        return Mathf.Abs(localPosition.x - center.x) <=
                   Mathf.Max(0f, halfSize.x - edgeMargin) &&
               Mathf.Abs(localPosition.z - center.z) <=
                   Mathf.Max(0f, halfSize.z - edgeMargin);
    }

    private bool CanReverseEscapeLaneChangePose(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        int blockMask)
    {
        Vector3 up = vehicleRotation * Vector3.up;

        if (up.sqrMagnitude <= 0.0001f)
        {
            up = Vector3.up;
        }
        else
        {
            up.Normalize();
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            vehicleRotation * Vector3.forward,
            up
        );

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        forward.Normalize();

        Vector3 right = Vector3.Cross(up, forward);

        if (right.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        right.Normalize();

        Vector3 footprintPosition = GetVehicleFootprintCenter(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation
        );

        float frontThreshold = 0.25f;

        if ((blockMask & LaneChangePlayerBlock) != 0)
        {
            if (IsPhysicsRearHalfBlocked(
                    vehicleIndex,
                    vehiclePosition,
                    vehicleRotation,
                    Mathf.Max(
                        authorityObstacleSafetyMargin,
                        laneChangePlayerSafetyMargin
                    )
                ))
            {
                return false;
            }
        }

        if ((blockMask & LaneChangeVehicleBlock) != 0)
        {
            float subjectHalfLength =
                GetVehicleLength(vehicleIndex) * 0.5f;

            float subjectHalfWidth =
                GetVehicleWidth(vehicleIndex) * 0.5f;

            bool foundVehicleBlocker = false;

            for (int otherIndex = 0;
                 otherIndex < slotCount;
                 otherIndex++)
            {
                if (otherIndex == vehicleIndex ||
                    !vehicleActive[otherIndex])
                {
                    continue;
                }

                Vector3 otherPosition =
                    GetLaneChangePathPosition(
                        otherIndex,
                        vehicleS[otherIndex],
                        laneChangeProgress[otherIndex]
                    );

                Quaternion otherRotation =
                    GetCurrentVehicleCollisionRotation(
                        otherIndex
                    );
                Vector3 otherFootprintPosition =
                    GetVehicleFootprintCenter(
                        otherIndex,
                        otherPosition,
                        otherRotation
                    );

                float otherHalfLength =
                    GetVehicleLength(otherIndex) * 0.5f;

                float otherHalfWidth =
                    GetVehicleWidth(otherIndex) * 0.5f;

                if (!DoVehicleFootprintsOverlap(
                        footprintPosition,
                        vehicleRotation,
                        subjectHalfLength,
                        subjectHalfWidth,
                        otherFootprintPosition,
                        otherRotation,
                        otherHalfLength,
                        otherHalfWidth,
                        Mathf.Max(
                            0f,
                            laneChangeVehicleSafetyMargin
                        )
                    ))
                {
                    continue;
                }

                foundVehicleBlocker = true;

                if (Vector3.Dot(
                        otherFootprintPosition - footprintPosition,
                        forward
                    ) <= frontThreshold)
                {
                    return false;
                }
            }

            if (!foundVehicleBlocker)
            {
                return false;
            }
        }

        return true;
    }

    private bool CanAdvancePastSourceLanePlayerMargin(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation)
    {
        if (!enableAuthorityPhysicsObstacles ||
            authorityObstacleLayerMask == 0)
        {
            return false;
        }

        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        int targetLaneId = laneChangeTargetLaneIds[vehicleIndex];
        int ruleIndex = laneChangeRuleIndices[vehicleIndex];

        if (sourceLaneId < 0 ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return false;
        }

        float sourceS = vehicleS[vehicleIndex];
        Vector3 sourceCenter = laneDatabase.GetLanePosition(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 targetCenter = laneDatabase.GetLanePosition(
            targetLaneId,
            MapSourceToTargetS(ruleIndex, sourceS),
            -1
        );
        Vector3 laneOffset = targetCenter - sourceCenter;

        if (laneOffset.sqrMagnitude <= 0.01f)
        {
            return false;
        }

        float coreMargin = Mathf.Max(
            0f,
            authorityObstacleSafetyMargin
        );
        float expandedMargin = coreMargin + Mathf.Max(
            0f,
            laneChangePlayerSafetyMargin
        );

        // Only the extra safety band on the source side may be ignored. A
        // collider touching the actual body envelope, or anything occupying
        // the target side, must still stop the manoeuvre.
        if (!IsPhysicsPoseBlockedByObstacle(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation,
                expandedMargin
            ) ||
            IsPhysicsPoseBlockedByObstacle(
                vehicleIndex,
                vehiclePosition,
                vehicleRotation,
                coreMargin
            ))
        {
            return false;
        }

        return !IsPhysicsTargetSideBlocked(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            laneOffset,
            expandedMargin
        );
    }

    private bool CanAdvancePastSourceLaneSafetyMargin(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation)
    {
        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        int targetLaneId = laneChangeTargetLaneIds[vehicleIndex];

        if (sourceLaneId < 0 ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount)
        {
            return false;
        }

        float subjectHalfLength =
            GetVehicleLength(vehicleIndex) * 0.5f;
        float subjectHalfWidth =
            GetVehicleWidth(vehicleIndex) * 0.5f;
        Vector3 footprintPosition = GetVehicleFootprintCenter(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation
        );
        bool foundSourceLeaderMargin = false;

        for (int otherIndex = 0;
             otherIndex < slotCount;
             otherIndex++)
        {
            if (otherIndex == vehicleIndex ||
                !vehicleActive[otherIndex] ||
                !OtherVehicleHasLaneChangePriority(
                    vehicleIndex,
                    otherIndex
                ))
            {
                continue;
            }

            Vector3 otherPosition =
                GetLaneChangePathPosition(
                    otherIndex,
                    vehicleS[otherIndex],
                    laneChangeProgress[otherIndex]
                );
            Quaternion otherRotation =
                GetCurrentVehicleCollisionRotation(otherIndex);
            Vector3 otherFootprintPosition =
                GetVehicleFootprintCenter(
                    otherIndex,
                    otherPosition,
                    otherRotation
                );
            float otherHalfLength =
                GetVehicleLength(otherIndex) * 0.5f;
            float otherHalfWidth =
                GetVehicleWidth(otherIndex) * 0.5f;

            if (!DoVehicleFootprintsOverlap(
                    footprintPosition,
                    vehicleRotation,
                    subjectHalfLength,
                    subjectHalfWidth,
                    otherFootprintPosition,
                    otherRotation,
                    otherHalfLength,
                    otherHalfWidth,
                    Mathf.Max(
                        0f,
                        laneChangeVehicleSafetyMargin
                    )
                ))
            {
                continue;
            }

            bool isSourceLeader =
                VehicleOccupiesLane(otherIndex, sourceLaneId) &&
                GetVehicleSOnLane(otherIndex, sourceLaneId) >
                    vehicleS[vehicleIndex] + 0.01f;

            if (!isSourceLeader ||
                VehicleOccupiesLane(otherIndex, targetLaneId) ||
                DoVehicleFootprintsOverlap(
                    footprintPosition,
                    vehicleRotation,
                    subjectHalfLength,
                    subjectHalfWidth,
                    otherFootprintPosition,
                    otherRotation,
                    otherHalfLength,
                    otherHalfWidth,
                    0.03f
                ))
            {
                return false;
            }

            foundSourceLeaderMargin = true;
        }

        return foundSourceLeaderMargin;
    }

    private bool IsLaneChangePoseBlockedByPlayer(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation)
    {
        return IsPhysicsPoseBlockedByObstacle(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            Mathf.Max(0f, authorityObstacleSafetyMargin) +
            Mathf.Max(0f, laneChangePlayerSafetyMargin)
        );
    }

    private bool IsLaneChangePoseBlockedByVehicle(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation)
    {
        return IsLaneChangePoseBlockedByVehicleWithMargin(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation,
            Mathf.Max(0f, laneChangeVehicleSafetyMargin)
        );
    }

    private bool IsLaneChangePoseBlockedByVehicleWithMargin(
        int vehicleIndex,
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        float safetyMargin)
    {
        float subjectHalfLength =
            GetVehicleLength(vehicleIndex) * 0.5f;

        float subjectHalfWidth =
            GetVehicleWidth(vehicleIndex) * 0.5f;

        Vector3 footprintPosition = GetVehicleFootprintCenter(
            vehicleIndex,
            vehiclePosition,
            vehicleRotation
        );

        for (int otherIndex = 0;
             otherIndex < slotCount;
             otherIndex++)
        {
            if (otherIndex == vehicleIndex ||
                !vehicleActive[otherIndex] ||
                !OtherVehicleHasLaneChangePriority(
                    vehicleIndex,
                    otherIndex
                ))
            {
                continue;
            }

            Vector3 otherPosition =
                GetLaneChangePathPosition(
                    otherIndex,
                    vehicleS[otherIndex],
                    laneChangeProgress[otherIndex]
                );

            Quaternion otherRotation =
                GetCurrentVehicleCollisionRotation(
                    otherIndex
                );
            Vector3 otherFootprintPosition =
                GetVehicleFootprintCenter(
                    otherIndex,
                    otherPosition,
                    otherRotation
                );

            float otherHalfLength =
                GetVehicleLength(otherIndex) * 0.5f;

            float otherHalfWidth =
                GetVehicleWidth(otherIndex) * 0.5f;

            if (DoVehicleFootprintsOverlap(
                    footprintPosition,
                    vehicleRotation,
                    subjectHalfLength,
                    subjectHalfWidth,
                    otherFootprintPosition,
                    otherRotation,
                    otherHalfLength,
                    otherHalfWidth,
                    Mathf.Max(0f, safetyMargin)
                ))
            {
                return true;
            }
        }

        return false;
    }

    private bool OtherVehicleHasLaneChangePriority(
        int vehicleIndex,
        int otherIndex)
    {
        if (IsReversePhase(
                laneChangeReverseManeuver[vehicleIndex],
                laneChangeProgress[vehicleIndex]
            ))
        {
            // A reversing vehicle yields to every occupied footprint. The
            // reverse-escape exception later ignores only the blocker that is
            // positively in front, never a vehicle behind or beside it.
            return true;
        }

        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        float sourceS = vehicleS[vehicleIndex];

        if (VehicleOccupiesLane(otherIndex, sourceLaneId) &&
            GetVehicleSOnLane(otherIndex, sourceLaneId) <
                sourceS - 0.01f)
        {
            return false;
        }

        int targetLaneId =
            laneChangeTargetLaneIds[vehicleIndex];

        int ruleIndex =
            laneChangeRuleIndices[vehicleIndex];

        if (targetLaneId >= 0 &&
            targetLaneId < laneDatabase.laneCount &&
            ruleIndex >= 0 &&
            VehicleOccupiesLane(otherIndex, targetLaneId))
        {
            float subjectTargetS = MapSourceToTargetS(
                ruleIndex,
                sourceS
            );

            if (GetVehicleSOnLane(
                    otherIndex,
                    targetLaneId
                ) < subjectTargetS - 0.01f)
            {
                return false;
            }
        }

        if (!laneChangeActive[otherIndex])
        {
            return true;
        }

        float subjectProgress = Mathf.Abs(
            GetLaneChangeVisualProgress(
                vehicleIndex,
                laneChangeReverseManeuver[vehicleIndex],
                laneChangeProgress[vehicleIndex]
            )
        );

        float otherProgress = Mathf.Abs(
            GetLaneChangeVisualProgress(
                otherIndex,
                laneChangeReverseManeuver[otherIndex],
                laneChangeProgress[otherIndex]
            )
        );

        if (otherProgress > subjectProgress + 0.05f)
        {
            return true;
        }

        if (subjectProgress > otherProgress + 0.05f)
        {
            return false;
        }

        return otherIndex < vehicleIndex;
    }

    private Quaternion GetCurrentVehicleCollisionRotation(
        int vehicleIndex)
    {
        int laneId = vehicleLaneIds[vehicleIndex];
        float currentS = vehicleS[vehicleIndex];

        if (!laneChangeActive[vehicleIndex])
        {
            return laneDatabase.GetLaneRotation(
                laneId,
                currentS,
                -1
            );
        }

        float currentProgress =
            laneChangeProgress[vehicleIndex];

        if (IsManeuverPathUsable(vehicleIndex))
        {
            return SampleManeuverPathRotation(
                vehicleIndex,
                currentProgress
            );
        }

        return GetLaneChangeSweepRotation(
            vehicleIndex,
            currentS,
            currentProgress,
            currentS,
            currentProgress,
            0f
        );
    }

    private bool DoVehicleFootprintsOverlap(
        Vector3 positionA,
        Quaternion rotationA,
        float halfLengthA,
        float halfWidthA,
        Vector3 positionB,
        Quaternion rotationB,
        float halfLengthB,
        float halfWidthB,
        float safetyMargin)
    {
        Vector3 upA = rotationA * Vector3.up;

        if (upA.sqrMagnitude <= 0.0001f)
        {
            upA = Vector3.up;
        }
        else
        {
            upA.Normalize();
        }

        Vector3 forwardA = Vector3.ProjectOnPlane(
            rotationA * Vector3.forward,
            upA
        );

        if (forwardA.sqrMagnitude <= 0.0001f)
        {
            forwardA = Vector3.forward;
        }
        else
        {
            forwardA.Normalize();
        }

        Vector3 rightA = Vector3.Cross(
            upA,
            forwardA
        );

        if (rightA.sqrMagnitude <= 0.0001f)
        {
            rightA = Vector3.right;
        }
        else
        {
            rightA.Normalize();
        }

        Vector3 upB = rotationB * Vector3.up;

        if (upB.sqrMagnitude <= 0.0001f)
        {
            upB = Vector3.up;
        }
        else
        {
            upB.Normalize();
        }

        Vector3 forwardB = Vector3.ProjectOnPlane(
            rotationB * Vector3.forward,
            upB
        );

        if (forwardB.sqrMagnitude <= 0.0001f)
        {
            forwardB = Vector3.forward;
        }
        else
        {
            forwardB.Normalize();
        }

        Vector3 rightB = Vector3.Cross(
            upB,
            forwardB
        );

        if (rightB.sqrMagnitude <= 0.0001f)
        {
            rightB = Vector3.right;
        }
        else
        {
            rightB.Normalize();
        }

        Vector3 difference = positionB - positionA;

        if (Mathf.Abs(Vector3.Dot(difference, upA)) > 2f)
        {
            return false;
        }

        float margin = Mathf.Max(0f, safetyMargin);

        if (IsVehicleFootprintSeparatedOnAxis(
                difference,
                forwardA,
                forwardA,
                rightA,
                halfLengthA + margin,
                halfWidthA + margin,
                forwardB,
                rightB,
                halfLengthB,
                halfWidthB
            ) ||
            IsVehicleFootprintSeparatedOnAxis(
                difference,
                rightA,
                forwardA,
                rightA,
                halfLengthA + margin,
                halfWidthA + margin,
                forwardB,
                rightB,
                halfLengthB,
                halfWidthB
            ) ||
            IsVehicleFootprintSeparatedOnAxis(
                difference,
                forwardB,
                forwardA,
                rightA,
                halfLengthA + margin,
                halfWidthA + margin,
                forwardB,
                rightB,
                halfLengthB,
                halfWidthB
            ) ||
            IsVehicleFootprintSeparatedOnAxis(
                difference,
                rightB,
                forwardA,
                rightA,
                halfLengthA + margin,
                halfWidthA + margin,
                forwardB,
                rightB,
                halfLengthB,
                halfWidthB
            ))
        {
            return false;
        }

        return true;
    }

    private bool IsVehicleFootprintSeparatedOnAxis(
        Vector3 centerDifference,
        Vector3 axis,
        Vector3 forwardA,
        Vector3 rightA,
        float halfLengthA,
        float halfWidthA,
        Vector3 forwardB,
        Vector3 rightB,
        float halfLengthB,
        float halfWidthB)
    {
        float centerDistance = Mathf.Abs(
            Vector3.Dot(centerDifference, axis)
        );

        float radiusA =
            halfLengthA * Mathf.Abs(
                Vector3.Dot(forwardA, axis)
            ) +
            halfWidthA * Mathf.Abs(
                Vector3.Dot(rightA, axis)
            );

        float radiusB =
            halfLengthB * Mathf.Abs(
                Vector3.Dot(forwardB, axis)
            ) +
            halfWidthB * Mathf.Abs(
                Vector3.Dot(rightB, axis)
            );

        return centerDistance > radiusA + radiusB;
    }

    private int GetManeuverPathOffset(int vehicleIndex)
    {
        return vehicleIndex * ManeuverPathSampleCount;
    }

    private void InvalidateManeuverPath(int vehicleIndex)
    {
        if (vehicleIndex >= 0 &&
            vehicleIndex < maneuverPathValid.Length)
        {
            maneuverPathValid[vehicleIndex] = false;
        }
    }

    private bool IsManeuverPathUsable(int vehicleIndex)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= maneuverPathValid.Length ||
            !maneuverPathValid[vehicleIndex] ||
            !laneChangeActive[vehicleIndex])
        {
            return false;
        }

        return maneuverPathSourceLaneIds[vehicleIndex] ==
                   vehicleLaneIds[vehicleIndex] &&
               maneuverPathTargetLaneIds[vehicleIndex] ==
                   laneChangeTargetLaneIds[vehicleIndex] &&
               maneuverPathRuleIndices[vehicleIndex] ==
                   laneChangeRuleIndices[vehicleIndex] &&
               maneuverPathReverse[vehicleIndex] ==
                   laneChangeReverseManeuver[vehicleIndex] &&
               maneuverPathEmergency[vehicleIndex] ==
                   laneChangeEmergencyManeuver[vehicleIndex] &&
               (!laneChangeReverseManeuver[vehicleIndex] ||
                Mathf.Abs(
                    maneuverPathRecoveryDistances[vehicleIndex] -
                    laneChangeRecoveryDistance[vehicleIndex]
                ) <= 0.051f);
    }

    private float GetManeuverPathSampleCoordinate(float progress)
    {
        return Mathf.Clamp01(progress) *
            (ManeuverPathSampleCount - 1);
    }

    private Vector3 SampleManeuverPathPosition(
        int vehicleIndex,
        float progress)
    {
        float coordinate = GetManeuverPathSampleCoordinate(progress);
        int first = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 1
        );
        int second = Mathf.Min(
            first + 1,
            ManeuverPathSampleCount - 1
        );
        float interpolation = coordinate - first;
        int offset = GetManeuverPathOffset(vehicleIndex);

        return Vector3.Lerp(
            maneuverPathPositions[offset + first],
            maneuverPathPositions[offset + second],
            interpolation
        );
    }

    private Quaternion SampleManeuverPathRotation(
        int vehicleIndex,
        float progress)
    {
        float coordinate = GetManeuverPathSampleCoordinate(progress);
        int first = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 1
        );
        int second = Mathf.Min(
            first + 1,
            ManeuverPathSampleCount - 1
        );
        float interpolation = coordinate - first;
        int offset = GetManeuverPathOffset(vehicleIndex);

        return Quaternion.Slerp(
            maneuverPathRotations[offset + first],
            maneuverPathRotations[offset + second],
            interpolation
        );
    }

    private float SampleManeuverPathSourceS(
        int vehicleIndex,
        float progress)
    {
        float coordinate = GetManeuverPathSampleCoordinate(progress);
        int first = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 1
        );
        int second = Mathf.Min(
            first + 1,
            ManeuverPathSampleCount - 1
        );
        float interpolation = coordinate - first;
        int offset = GetManeuverPathOffset(vehicleIndex);

        return Mathf.Lerp(
            maneuverPathSourceS[offset + first],
            maneuverPathSourceS[offset + second],
            interpolation
        );
    }

    private float SampleManeuverPathDistance(
        int vehicleIndex,
        float progress)
    {
        float coordinate = GetManeuverPathSampleCoordinate(progress);
        int first = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 1
        );
        int second = Mathf.Min(
            first + 1,
            ManeuverPathSampleCount - 1
        );
        float interpolation = coordinate - first;
        int offset = GetManeuverPathOffset(vehicleIndex);

        return Mathf.Lerp(
            maneuverPathDistances[offset + first],
            maneuverPathDistances[offset + second],
            interpolation
        );
    }

    private float FindManeuverProgressForDistance(
        int vehicleIndex,
        float minimumProgress,
        float maximumProgress,
        float targetDistance)
    {
        float safeMinimum = Mathf.Clamp01(minimumProgress);
        float safeMaximum = Mathf.Clamp(
            maximumProgress,
            safeMinimum,
            1f
        );
        float minimumDistance = SampleManeuverPathDistance(
            vehicleIndex,
            safeMinimum
        );
        float maximumDistance = SampleManeuverPathDistance(
            vehicleIndex,
            safeMaximum
        );

        if (targetDistance <= minimumDistance + 0.0001f)
        {
            return safeMinimum;
        }

        if (targetDistance >= maximumDistance - 0.0001f)
        {
            return safeMaximum;
        }

        int offset = GetManeuverPathOffset(vehicleIndex);
        int last = ManeuverPathSampleCount - 1;
        int firstSample = Mathf.Clamp(
            Mathf.FloorToInt(
                GetManeuverPathSampleCoordinate(safeMinimum)
            ),
            0,
            last - 1
        );
        int finalSample = Mathf.Clamp(
            Mathf.CeilToInt(
                GetManeuverPathSampleCoordinate(safeMaximum)
            ),
            firstSample + 1,
            last
        );

        for (int sample = firstSample;
             sample < finalSample;
             sample++)
        {
            float startDistance =
                maneuverPathDistances[offset + sample];
            float endDistance =
                maneuverPathDistances[offset + sample + 1];

            if (targetDistance <= endDistance ||
                sample == finalSample - 1)
            {
                float segmentProgress = Mathf.InverseLerp(
                    startDistance,
                    endDistance,
                    targetDistance
                );

                return Mathf.Clamp(
                    (sample + segmentProgress) / last,
                    safeMinimum,
                    safeMaximum
                );
            }
        }

        return safeMaximum;
    }

    private float AdvanceManeuverProgressByDistance(
        int vehicleIndex,
        float progress,
        float travelDistance,
        float maximumProgress)
    {
        float safeProgress = Mathf.Clamp01(progress);

        if (!IsManeuverPathUsable(vehicleIndex) ||
            travelDistance <= 0.000001f)
        {
            return safeProgress;
        }

        float safeMaximum = Mathf.Clamp(
            maximumProgress,
            safeProgress,
            1f
        );
        float currentDistance = SampleManeuverPathDistance(
            vehicleIndex,
            safeProgress
        );

        return FindManeuverProgressForDistance(
            vehicleIndex,
            safeProgress,
            safeMaximum,
            currentDistance + travelDistance
        );
    }

    private float FindManeuverProgressForSourceS(
        int vehicleIndex,
        float minimumProgress,
        float sourceS)
    {
        int offset = GetManeuverPathOffset(vehicleIndex);
        int last = ManeuverPathSampleCount - 1;
        int firstSample = Mathf.Clamp(
            Mathf.FloorToInt(
                GetManeuverPathSampleCoordinate(minimumProgress)
            ),
            0,
            last - 1
        );

        if (sourceS <= maneuverPathSourceS[offset + firstSample])
        {
            return Mathf.Clamp01(minimumProgress);
        }

        for (int sample = firstSample;
             sample < last;
             sample++)
        {
            float startS = maneuverPathSourceS[offset + sample];
            float endS = maneuverPathSourceS[offset + sample + 1];

            if (sourceS <= endS || sample == last - 1)
            {
                float segmentProgress = Mathf.InverseLerp(
                    startS,
                    endS,
                    sourceS
                );

                return Mathf.Clamp01(
                    (sample + segmentProgress) / last
                );
            }
        }

        return 1f;
    }

    private float SampleManeuverPathSteering(
        int vehicleIndex,
        float progress)
    {
        float coordinate = GetManeuverPathSampleCoordinate(progress);
        int first = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 1
        );
        int second = Mathf.Min(
            first + 1,
            ManeuverPathSampleCount - 1
        );
        float interpolation = coordinate - first;
        int offset = GetManeuverPathOffset(vehicleIndex);

        return Mathf.Lerp(
            maneuverPathSteeringAngles[offset + first],
            maneuverPathSteeringAngles[offset + second],
            interpolation
        );
    }

    private void GetManeuverPathPhaseSampleRange(
        bool reverseManeuver,
        int sampleIndex,
        float progress,
        int[] phaseRange)
    {
        int last = ManeuverPathSampleCount - 1;
        int phaseStart = 0;
        int phaseEnd = last;

        if (reverseManeuver)
        {
            int reverseEnd = Mathf.Clamp(
                Mathf.RoundToInt(
                    GetRecoveryFirstReverseEndProgress() * last
                ),
                1,
                last - 2
            );
            int preparationEnd = Mathf.Clamp(
                Mathf.RoundToInt(
                    GetRecoveryPreparationEndProgress() * last
                ),
                reverseEnd + 1,
                last - 1
            );

            if (IsReversePhase(true, progress))
            {
                phaseEnd = reverseEnd;
            }
            else if (progress <
                     GetRecoveryPreparationEndProgress() - 0.0001f)
            {
                phaseStart = reverseEnd;
                phaseEnd = preparationEnd;
            }
            else
            {
                phaseStart = preparationEnd;
            }
        }

        phaseRange[0] = Mathf.Max(
            phaseStart,
            sampleIndex - 2
        );
        phaseRange[1] = Mathf.Min(
            phaseEnd,
            sampleIndex + 2
        );

        if (phaseRange[0] == phaseRange[1])
        {
            phaseRange[0] = Mathf.Max(
                phaseStart,
                phaseRange[0] - 1
            );
            phaseRange[1] = Mathf.Min(
                phaseEnd,
                phaseRange[1] + 1
            );
        }
    }

    private bool BuildManeuverPath(
        int vehicleIndex,
        float anchorS)
    {
        InvalidateManeuverPath(vehicleIndex);

        if (vehicleIndex < 0 ||
            vehicleIndex >= slotCount ||
            !laneChangeActive[vehicleIndex] ||
            laneChangePreparing[vehicleIndex] ||
            laneChangeRuleIndices[vehicleIndex] < 0 ||
            laneChangeTargetLaneIds[vehicleIndex] < 0)
        {
            return false;
        }

        bool reverseManeuver =
            laneChangeReverseManeuver[vehicleIndex];
        bool emergencyManeuver =
            laneChangeEmergencyManeuver[vehicleIndex];
        float recoveryDistance = reverseManeuver
            ? Mathf.Max(
                MinimumReverseRecoveryDistance,
                laneChangeRecoveryDistance[vehicleIndex]
              )
            : 0f;
        float travelDistance = reverseManeuver
            ? 0f
            : GetNormalLaneChangeTravelDistance(
                emergencyManeuver
              );
        int offset = GetManeuverPathOffset(vehicleIndex);
        float cumulativeDistance = 0f;

        for (int sample = 0;
             sample < ManeuverPathSampleCount;
             sample++)
        {
            float progress =
                (float)sample /
                (ManeuverPathSampleCount - 1);
            float sourceS = reverseManeuver
                ? GetRecoveryPathSourceS(
                    vehicleIndex,
                    progress,
                    recoveryDistance
                  )
                : anchorS + travelDistance * progress;
            Vector3 position =
                GetLaneChangePathPositionUncached(
                    vehicleIndex,
                    sourceS,
                    progress
                );
            int pathIndex = offset + sample;

            maneuverPathSourceS[pathIndex] = sourceS;
            maneuverPathPositions[pathIndex] = position;

            if (sample > 0)
            {
                cumulativeDistance += Vector3.Distance(
                    maneuverPathPositions[pathIndex - 1],
                    position
                );
            }

            maneuverPathDistances[pathIndex] = cumulativeDistance;
        }

        int[] phaseRange = maneuverPhaseRangeScratch;

        for (int sample = 0;
             sample < ManeuverPathSampleCount;
             sample++)
        {
            float progress =
                (float)sample /
                (ManeuverPathSampleCount - 1);

            if (reverseManeuver)
            {
                // The reverse and forward preparation arcs share one exact
                // kinematic heading. This keeps the body angle continuous
                // while the vehicle is stationary at the gear change.
                maneuverPathRotations[offset + sample] =
                    GetRecoveryKinematicWorldRotation(
                        vehicleIndex,
                        maneuverPathSourceS[offset + sample],
                        progress
                    );
                continue;
            }

            GetManeuverPathPhaseSampleRange(
                reverseManeuver,
                sample,
                progress,
                phaseRange
            );
            Vector3 pathForward =
                maneuverPathPositions[offset + phaseRange[1]] -
                maneuverPathPositions[offset + phaseRange[0]];

            Quaternion surfaceRotation =
                GetLaneChangeSurfaceRotation(
                    vehicleIndex,
                    maneuverPathSourceS[offset + sample],
                    progress
                );
            Vector3 surfaceUp =
                surfaceRotation * Vector3.up;
            pathForward = Vector3.ProjectOnPlane(
                pathForward,
                surfaceUp
            );

            maneuverPathRotations[offset + sample] =
                pathForward.sqrMagnitude > 0.000001f
                    ? Quaternion.LookRotation(
                        pathForward.normalized,
                        surfaceUp.normalized
                      )
                    : surfaceRotation;
        }

        float wheelBase = Mathf.Max(
            2.2f,
            GetVehicleLength(vehicleIndex) * 0.55f
        );
        float maximumSteering = Mathf.Min(
            Mathf.Clamp(
                maximumFrontWheelSteeringAngle,
                20f,
                40f
            ),
            reverseManeuver
                ? Mathf.Clamp(
                    blockedRecoveryMaximumSteeringAngle,
                    5f,
                    40f
                  )
                : (emergencyManeuver
                    ? Mathf.Clamp(
                        emergencyLaneChangeSteeringAngle,
                        5f,
                        40f
                      )
                    : Mathf.Clamp(
                        maximumLaneChangeSteeringAngle,
                        5f,
                        40f
                      ))
        );

        for (int sample = 0;
             sample < ManeuverPathSampleCount;
             sample++)
        {
            float progress =
                (float)sample /
                (ManeuverPathSampleCount - 1);
            GetManeuverPathPhaseSampleRange(
                reverseManeuver,
                sample,
                progress,
                phaseRange
            );
            int before = offset + phaseRange[0];
            int after = offset + phaseRange[1];
            float arcDistance = Mathf.Max(
                0.001f,
                maneuverPathDistances[after] -
                maneuverPathDistances[before]
            );
            float signedDistance =
                IsReversePhase(reverseManeuver, progress)
                    ? -arcDistance
                    : arcDistance;
            Vector3 up =
                maneuverPathRotations[offset + sample] *
                Vector3.up;
            float yawRadians = Mathf.Deg2Rad *
                Vector3.SignedAngle(
                    maneuverPathRotations[before] * Vector3.forward,
                    maneuverPathRotations[after] * Vector3.forward,
                    up
                );
            float curvature = yawRadians / signedDistance;

            maneuverPathSteeringAngles[offset + sample] =
                Mathf.Clamp(
                    Mathf.Rad2Deg *
                        Mathf.Atan(wheelBase * curvature),
                    -maximumSteering,
                    maximumSteering
                );
        }

        maneuverPathSourceLaneIds[vehicleIndex] =
            vehicleLaneIds[vehicleIndex];
        maneuverPathTargetLaneIds[vehicleIndex] =
            laneChangeTargetLaneIds[vehicleIndex];
        maneuverPathRuleIndices[vehicleIndex] =
            laneChangeRuleIndices[vehicleIndex];
        maneuverPathReverse[vehicleIndex] = reverseManeuver;
        maneuverPathEmergency[vehicleIndex] = emergencyManeuver;
        maneuverPathRecoveryDistances[vehicleIndex] =
            recoveryDistance;
        maneuverPathValid[vehicleIndex] = true;
        return true;
    }

    private void EnsureManeuverPath(
        int vehicleIndex,
        float sourceS,
        float progress)
    {
        if (IsManeuverPathUsable(vehicleIndex) ||
            !laneChangeActive[vehicleIndex] ||
            laneChangePreparing[vehicleIndex])
        {
            return;
        }

        float anchorS = laneChangeReverseManeuver[vehicleIndex]
            ? laneChangeReverseStartS[vehicleIndex]
            : sourceS -
              GetNormalLaneChangeTravelDistance(
                  laneChangeEmergencyManeuver[vehicleIndex]
              ) * Mathf.Clamp01(progress);

        BuildManeuverPath(vehicleIndex, anchorS);
    }

    private Vector3 GetLaneChangePathPosition(
        int vehicleIndex,
        float sourceS,
        float progress)
    {
        if (IsManeuverPathUsable(vehicleIndex))
        {
            return SampleManeuverPathPosition(
                vehicleIndex,
                progress
            );
        }

        return GetLaneChangePathPositionUncached(
            vehicleIndex,
            sourceS,
            progress
        );
    }

    private Vector3 GetLaneChangePathPositionUncached(
        int vehicleIndex,
        float sourceS,
        float progress)
    {
        int sourceLaneId = vehicleLaneIds[vehicleIndex];

        Vector3 sourcePosition =
            laneDatabase.GetLanePosition(
                sourceLaneId,
                sourceS,
                -1
            );

        if (!laneChangeActive[vehicleIndex])
        {
            return sourcePosition;
        }

        int targetLaneId =
            laneChangeTargetLaneIds[vehicleIndex];

        int ruleIndex =
            laneChangeRuleIndices[vehicleIndex];

        if (targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return sourcePosition;
        }

        float targetS = MapSourceToTargetSUnclamped(
            ruleIndex,
            sourceS
        );

        Vector3 targetPosition =
            laneDatabase.GetLanePosition(
                targetLaneId,
                targetS,
                -1
            );

        float lateralProgress =
            GetLaneChangeVisualProgress(
                vehicleIndex,
                laneChangeReverseManeuver[vehicleIndex],
                progress
            );

        return Vector3.LerpUnclamped(
            sourcePosition,
            targetPosition,
            lateralProgress
        );
    }

    private Quaternion GetLaneChangeSurfaceRotation(
        int vehicleIndex,
        float sourceS,
        float progress)
    {
        int sourceLaneId = vehicleLaneIds[vehicleIndex];

        Quaternion sourceRotation =
            laneDatabase.GetLaneRotation(
                sourceLaneId,
                sourceS,
                -1
            );

        int targetLaneId =
            laneChangeTargetLaneIds[vehicleIndex];

        int ruleIndex =
            laneChangeRuleIndices[vehicleIndex];

        if (!laneChangeActive[vehicleIndex] ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return sourceRotation;
        }

        float targetS = MapSourceToTargetSUnclamped(
            ruleIndex,
            sourceS
        );

        Quaternion targetRotation =
            laneDatabase.GetLaneRotation(
                targetLaneId,
                targetS,
                -1
            );

        float lateralProgress = Mathf.Clamp01(
            GetLaneChangeVisualProgress(
                vehicleIndex,
                laneChangeReverseManeuver[vehicleIndex],
                progress
            )
        );

        return Quaternion.Slerp(
            sourceRotation,
            targetRotation,
            lateralProgress
        );
    }

    private Quaternion GetNormalLaneChangePathRotation(
        int vehicleIndex,
        float sourceS,
        float progress)
    {
        if (IsManeuverPathUsable(vehicleIndex))
        {
            return SampleManeuverPathRotation(
                vehicleIndex,
                progress
            );
        }

        Quaternion surfaceRotation =
            GetLaneChangeSurfaceRotation(
                vehicleIndex,
                sourceS,
                progress
            );
        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        int targetLaneId = laneChangeTargetLaneIds[vehicleIndex];
        int ruleIndex = laneChangeRuleIndices[vehicleIndex];

        if (sourceLaneId < 0 ||
            sourceLaneId >= laneDatabase.laneCount ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return surfaceRotation;
        }

        float targetS = MapSourceToTargetSUnclamped(
            ruleIndex,
            sourceS
        );
        Vector3 sourcePosition = laneDatabase.GetLanePosition(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 targetPosition = laneDatabase.GetLanePosition(
            targetLaneId,
            targetS,
            -1
        );
        Quaternion sourceRotation = laneDatabase.GetLaneRotation(
            sourceLaneId,
            sourceS,
            -1
        );
        Quaternion targetRotation = laneDatabase.GetLaneRotation(
            targetLaneId,
            targetS,
            -1
        );
        Vector3 surfaceUp = surfaceRotation * Vector3.up;
        Vector3 sourceForward = Vector3.ProjectOnPlane(
            sourceRotation * Vector3.forward,
            surfaceUp
        ).normalized;
        Vector3 targetForward = Vector3.ProjectOnPlane(
            targetRotation * Vector3.forward,
            surfaceUp
        ).normalized;
        float lateralProgress = SmoothLaneChange01(progress);
        float travelDistance = GetNormalLaneChangeTravelDistance(
            laneChangeEmergencyManeuver[vehicleIndex]
        );
        Vector3 tangent =
            Vector3.Lerp(
                sourceForward,
                targetForward,
                lateralProgress
            ) * travelDistance +
            (targetPosition - sourcePosition) *
            GetSmoothLaneChangeFirstDerivative(progress);

        tangent = Vector3.ProjectOnPlane(tangent, surfaceUp);

        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return surfaceRotation;
        }

        return Quaternion.LookRotation(
            tangent.normalized,
            surfaceUp
        );
    }

    private Quaternion GetLaneChangeSweepRotation(
        int vehicleIndex,
        float startS,
        float startProgress,
        float endS,
        float endProgress,
        float fraction)
    {
        float sampleS = Mathf.Lerp(
            startS,
            endS,
            fraction
        );

        float sampleProgress = Mathf.Lerp(
            startProgress,
            endProgress,
            fraction
        );

        if (laneChangeReverseManeuver[vehicleIndex])
        {
            return GetRecoveryKinematicWorldRotation(
                vehicleIndex,
                sampleS,
                sampleProgress
            );
        }

        return GetNormalLaneChangePathRotation(
            vehicleIndex,
            sampleS,
            sampleProgress
        );
    }

    private Quaternion GetRecoveryKinematicWorldRotation(
        int vehicleIndex,
        float sourceS,
        float progress)
    {
        if (IsManeuverPathUsable(vehicleIndex))
        {
            return SampleManeuverPathRotation(
                vehicleIndex,
                progress
            );
        }

        float clampedProgress = Mathf.Clamp01(progress);
        float preparationEnd = GetRecoveryPreparationEndProgress();
        float recoveryDistance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            laneChangeRecoveryDistance[vehicleIndex] > 0f
                ? laneChangeRecoveryDistance[vehicleIndex]
                : laneChangeReverseDistance
        );

        if (clampedProgress <= preparationEnd + 0.0001f)
        {
            Vector3 kinematicPose =
                GetRecoveryKinematicLocalPose(
                    vehicleIndex,
                    clampedProgress,
                    recoveryDistance
                );

            return GetRecoveryKinematicHeadingRotation(
                vehicleIndex,
                sourceS,
                progress,
                kinematicPose.z
            );
        }

        float phaseStart = preparationEnd;
        float phaseEnd = 1f;
        float tangentWindow =
            2f / (ManeuverPathSampleCount - 1);
        float beforeProgress = Mathf.Max(
            phaseStart,
            clampedProgress - tangentWindow
        );
        float afterProgress = Mathf.Min(
            phaseEnd,
            clampedProgress + tangentWindow
        );

        if (afterProgress - beforeProgress < 0.0001f)
        {
            return GetLaneChangeSurfaceRotation(
                vehicleIndex,
                sourceS,
                progress
            );
        }

        float beforeS = GetRecoveryPathSourceS(
            vehicleIndex,
            beforeProgress,
            recoveryDistance
        );
        float afterS = GetRecoveryPathSourceS(
            vehicleIndex,
            afterProgress,
            recoveryDistance
        );
        Vector3 pathForward =
            GetLaneChangePathPositionUncached(
                vehicleIndex,
                afterS,
                afterProgress
            ) -
            GetLaneChangePathPositionUncached(
                vehicleIndex,
                beforeS,
                beforeProgress
            );

        return GetRecoveryPathTangentRotation(
            vehicleIndex,
            sourceS,
            progress,
            pathForward
        );
    }

    private Quaternion GetRecoveryKinematicHeadingRotation(
        int vehicleIndex,
        float sourceS,
        float progress,
        float localHeadingRadians)
    {
        Quaternion surfaceRotation =
            GetLaneChangeSurfaceRotation(
                vehicleIndex,
                sourceS,
                progress
            );
        Vector3 surfaceUp = surfaceRotation * Vector3.up;
        Vector3 surfaceForward = Vector3.ProjectOnPlane(
            surfaceRotation * Vector3.forward,
            surfaceUp
        );

        if (surfaceForward.sqrMagnitude <= 0.0001f)
        {
            return surfaceRotation;
        }

        surfaceUp.Normalize();
        surfaceForward.Normalize();
        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        int targetLaneId = laneChangeTargetLaneIds[vehicleIndex];
        int ruleIndex = laneChangeRuleIndices[vehicleIndex];

        if (sourceLaneId < 0 ||
            sourceLaneId >= laneDatabase.laneCount ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return surfaceRotation;
        }

        Vector3 sourcePosition = laneDatabase.GetLanePosition(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 targetPosition = laneDatabase.GetLanePosition(
            targetLaneId,
            MapSourceToTargetSUnclamped(ruleIndex, sourceS),
            -1
        );
        Vector3 targetSide = Vector3.ProjectOnPlane(
            targetPosition - sourcePosition,
            surfaceUp
        );
        targetSide -= surfaceForward *
            Vector3.Dot(targetSide, surfaceForward);

        if (targetSide.sqrMagnitude <= 0.0001f)
        {
            return surfaceRotation;
        }

        targetSide.Normalize();
        float targetSign = Mathf.Sign(
            Vector3.SignedAngle(
                surfaceForward,
                targetSide,
                surfaceUp
            )
        );
        float headingAngle = Mathf.Clamp(
            Mathf.Abs(localHeadingRadians) * Mathf.Rad2Deg,
            0f,
            Mathf.Clamp(
                blockedRecoveryMaximumBodyAngle,
                18f,
                38f
            )
        );

        return Quaternion.AngleAxis(
            targetSign * headingAngle,
            surfaceUp
        ) * surfaceRotation;
    }

    private Quaternion GetRecoveryPathTangentRotation(
        int vehicleIndex,
        float sourceS,
        float progress,
        Vector3 pathForward)
    {
        Quaternion surfaceRotation =
            GetLaneChangeSurfaceRotation(
                vehicleIndex,
                sourceS,
                progress
            );
        Vector3 surfaceUp = surfaceRotation * Vector3.up;
        Vector3 surfaceForward = Vector3.ProjectOnPlane(
            surfaceRotation * Vector3.forward,
            surfaceUp
        );
        pathForward = Vector3.ProjectOnPlane(
            pathForward,
            surfaceUp
        );

        if (surfaceForward.sqrMagnitude <= 0.0001f ||
            pathForward.sqrMagnitude <= 0.000001f)
        {
            return surfaceRotation;
        }

        surfaceForward.Normalize();
        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        int targetLaneId = laneChangeTargetLaneIds[vehicleIndex];
        int ruleIndex = laneChangeRuleIndices[vehicleIndex];

        if (sourceLaneId < 0 ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return surfaceRotation;
        }

        Vector3 sourcePosition = laneDatabase.GetLanePosition(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 targetPosition = laneDatabase.GetLanePosition(
            targetLaneId,
            MapSourceToTargetSUnclamped(ruleIndex, sourceS),
            -1
        );
        Vector3 targetSide = Vector3.ProjectOnPlane(
            targetPosition - sourcePosition,
            surfaceUp
        );
        targetSide -= surfaceForward *
            Vector3.Dot(targetSide, surfaceForward);

        if (targetSide.sqrMagnitude <= 0.0001f)
        {
            return surfaceRotation;
        }

        targetSide.Normalize();
        float maximumBodyAngle = Mathf.Clamp(
            blockedRecoveryMaximumBodyAngle,
            18f,
            38f
        );
        float targetSign = Mathf.Sign(
            Vector3.SignedAngle(
                surfaceForward,
                targetSide,
                surfaceUp
            )
        );
        float pathAngle = Vector3.SignedAngle(
            surfaceForward,
            pathForward.normalized,
            surfaceUp
        );
        float angleTowardTarget = Mathf.Max(
            0f,
            pathAngle * targetSign
        );
        float constrainedAngle = targetSign * Mathf.Clamp(
            angleTowardTarget,
            0f,
            maximumBodyAngle
        );

        return Quaternion.AngleAxis(
            constrainedAngle,
            surfaceUp
        ) * surfaceRotation;
    }

    private float GetRecoveryPathSourceS(
        int vehicleIndex,
        float progress,
        float recoveryDistance)
    {
        float preparationEnd =
            GetRecoveryPreparationEndProgress();
        float clampedProgress = Mathf.Clamp01(progress);
        Vector3 preparationPose =
            GetRecoveryKinematicLocalPose(
                vehicleIndex,
                Mathf.Min(clampedProgress, preparationEnd),
                recoveryDistance
            );

        if (clampedProgress <= preparationEnd)
        {
            return laneChangeReverseStartS[vehicleIndex] +
                preparationPose.x;
        }

        float finalTravelDistance =
            GetRecoveryFinalTravelDistance(
                preparationPose,
                GetLaneChangeSeparation(
                    vehicleIndex,
                    laneChangeReverseStartS[vehicleIndex]
                )
            );
        float finalProgress = Mathf.InverseLerp(
            preparationEnd,
            1f,
            clampedProgress
        );

        return laneChangeReverseStartS[vehicleIndex] +
            preparationPose.x +
            finalTravelDistance * finalProgress;
    }

    private float GetObstacleRestartDelay(int vehicleIndex)
    {
        float minimum = Mathf.Max(
            0.2f,
            obstacleRestartDelayMinimum
        );

        float maximum = Mathf.Max(
            minimum,
            obstacleRestartDelayMaximum
        );

        int pattern =
            (vehicleIndex * 37 + 17) % 100;

        return Mathf.Lerp(
            minimum,
            maximum,
            pattern / 99f
        );
    }

    private float GetSignalStopS(
        int vehicleIndex,
        float currentS,
        float currentSpeed)
    {
        if (mainSignal == null)
        {
            return -1f;
        }

        int laneId =
            vehicleLaneIds[vehicleIndex];

        if (laneId < 0 ||
            laneId >= laneDatabase.laneCount)
        {
            return -1f;
        }

        if (laneDatabase.signalGroupIds[laneId] < 0)
        {
            return -1f;
        }

        float lineS =
            laneDatabase.stopLineS[laneId];

        if (lineS < 0f)
        {
            return -1f;
        }

        if (currentSignalState ==
            ShinhoTime.SignalGreen)
        {
            signalCommittedToCross[vehicleIndex] =
                false;

            return -1f;
        }

        float stopS =
            lineS -
            GetVehicleFrontExtent(vehicleIndex) -
            stopLineFrontBuffer;

        float distanceToStop =
            stopS - currentS;

        // 정지선을 실제로 통과한 차량만 계속 진행
        // 정확히 정지선에 도착한 차량은 신호가 바뀔 때까지 유지
        if (distanceToStop < 0f)
        {
            return -1f;
        }

        if (signalCommittedToCross[vehicleIndex])
        {
            return -1f;
        }

        if (currentSignalState ==
            ShinhoTime.SignalYellow)
        {
            float brakingRate = Mathf.Max(
                0.1f,
                signalComfortDeceleration
            );

            float requiredBrakingDistance =
                currentSpeed *
                currentSpeed /
                (2f * brakingRate) +
                yellowDecisionMargin;

            // 황색 전환 시 너무 가까우면 급정지하지 않고 통과
            if (distanceToStop <=
                requiredBrakingDistance)
            {
                signalCommittedToCross[vehicleIndex] =
                    true;

                return -1f;
            }
        }

        return stopS;
    }

    private float GetCruiseSpeed(
        int vehicleIndex,
        int laneId)
    {
        float targetSpeed =
            GetBaseCruiseSpeed(vehicleIndex) *
            speedFactors[vehicleIndex];

        float speedLimit =
            laneDatabase.speedLimits[laneId];

        if (speedLimit > 0f)
        {
            targetSpeed = Mathf.Min(
                targetSpeed,
                speedLimit
            );
        }

        return Mathf.Max(0f, targetSpeed);
    }

    private bool IsTruck(int vehicleIndex)
    {
        return vehicleIndex == truckSlotIndex;
    }

    private float GetVehicleLength(int vehicleIndex)
    {
        return GetVehicleFrontExtent(vehicleIndex) +
            GetVehicleRearExtent(vehicleIndex);
    }

    private float GetVehicleWidth(int vehicleIndex)
    {
        if (HasBakedVehicleBounds(vehicleIndex))
        {
            return Mathf.Max(
                0.1f,
                bakedVehicleWidths[vehicleIndex]
            );
        }

        float fallbackWidth = IsTruck(vehicleIndex)
            ? truckVehicleWidth
            : vehicleWidth;

        return Mathf.Max(0.1f, fallbackWidth) *
            GetVehicleVisualScale(vehicleIndex);
    }

    private bool HasBakedVehicleBounds(int vehicleIndex)
    {
        return vehicleIndex >= 0 &&
            bakedVehicleFrontExtents != null &&
            bakedVehicleRearExtents != null &&
            bakedVehicleWidths != null &&
            vehicleIndex < bakedVehicleFrontExtents.Length &&
            vehicleIndex < bakedVehicleRearExtents.Length &&
            vehicleIndex < bakedVehicleWidths.Length &&
            bakedVehicleFrontExtents[vehicleIndex] > 0.01f &&
            bakedVehicleRearExtents[vehicleIndex] > 0.01f &&
            bakedVehicleWidths[vehicleIndex] > 0.01f;
    }

    private float GetVehicleFrontExtent(int vehicleIndex)
    {
        if (HasBakedVehicleBounds(vehicleIndex))
        {
            return Mathf.Max(
                0.05f,
                bakedVehicleFrontExtents[vehicleIndex]
            );
        }

        float fallbackLength = IsTruck(vehicleIndex)
            ? truckVehicleLength
            : vehicleLength;

        return Mathf.Max(0.1f, fallbackLength) *
            GetVehicleVisualScale(vehicleIndex) * 0.5f;
    }

    private float GetVehicleRearExtent(int vehicleIndex)
    {
        if (HasBakedVehicleBounds(vehicleIndex))
        {
            return Mathf.Max(
                0.05f,
                bakedVehicleRearExtents[vehicleIndex]
            );
        }

        float fallbackLength = IsTruck(vehicleIndex)
            ? truckVehicleLength
            : vehicleLength;

        return Mathf.Max(0.1f, fallbackLength) *
            GetVehicleVisualScale(vehicleIndex) * 0.5f;
    }

    private float GetVehicleCenterOffset(int vehicleIndex)
    {
        return (
            GetVehicleFrontExtent(vehicleIndex) -
            GetVehicleRearExtent(vehicleIndex)
        ) * 0.5f;
    }

    private Vector3 GetVehicleFootprintCenter(
        int vehicleIndex,
        Vector3 rootPosition,
        Quaternion rootRotation)
    {
        Vector3 up = rootRotation * Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(
            rootRotation * Vector3.forward,
            up
        );

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return rootPosition;
        }

        return rootPosition +
            forward.normalized * GetVehicleCenterOffset(vehicleIndex);
    }

    private float GetCombinedHalfLength(
        int followerIndex,
        int leaderIndex)
    {
        return GetVehicleFrontExtent(followerIndex) +
            GetVehicleRearExtent(leaderIndex);
    }

    private float GetBaseCruiseSpeed(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckCruiseSpeed
            : cruiseSpeed;
    }

    private float GetAcceleration(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckAcceleration
            : acceleration;
    }

    private float GetDeceleration(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckDeceleration
            : deceleration;
    }

    private float GetEmergencyDeceleration(
        int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckEmergencyDeceleration
            : emergencyDeceleration;
    }

    private float GetMinimumGap(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckMinimumGap
            : minimumGap;
    }

    private float GetTimeHeadway(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckTimeHeadway
            : timeHeadway;
    }

    private float GetSpawnClearance(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckSpawnClearance
            : spawnClearance;
    }

    private int GetVehicleMask(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? TrafficLaneDatabase.VehicleTruck
            : TrafficLaneDatabase.VehicleCar;
    }

    private float GetMaximumEngineVolume(
        int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckMaximumEngineVolume
            : maximumEngineVolume;
    }

    private float GetIdleEnginePitch(int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckIdleEnginePitch
            : idleEnginePitch;
    }

    private float GetMaximumEnginePitch(
        int vehicleIndex)
    {
        return IsTruck(vehicleIndex)
            ? truckMaximumEnginePitch
            : maximumEnginePitch;
    }

    private float GetWheelRadius(int vehicleIndex)
    {
        float baseRadius = IsTruck(vehicleIndex)
            ? truckWheelRadius
            : wheelRadius;

        return baseRadius *
               GetVehicleVisualScale(vehicleIndex);
    }

    private float GetVehicleVisualScale(
        int vehicleIndex)
    {
        float visualScale = IsTruck(vehicleIndex)
            ? truckVisualScale
            : normalCarVisualScale;

        return Mathf.Clamp(
            visualScale,
            0.8f,
            1.25f
        );
    }

    private void BuildLaneChangeRuleMappings()
    {
        for (int i = 0;
             i < MaximumLaneChangeRules;
             i++)
        {
            ruleTargetStartS[i] = 0f;
            ruleTargetEndS[i] = 0f;
            rulePhysicsCorridorValid[i] = false;
            rulePhysicsCorridorAxis[i] = Vector3.zero;
            rulePhysicsCorridorMinimum[i] = 0f;
            rulePhysicsCorridorMaximum[i] = 0f;
        }

        int ruleCount = Mathf.Min(
            laneDatabase.changeToLaneIds.Length,
            MaximumLaneChangeRules
        );

        for (int sourceLaneId = 0;
             sourceLaneId < laneDatabase.laneCount;
             sourceLaneId++)
        {
            int first =
                laneDatabase.laneRuleStarts[sourceLaneId];

            int count =
                laneDatabase.laneRuleCounts[sourceLaneId];

            int last = Mathf.Min(
                first + count,
                ruleCount
            );

            for (int ruleIndex = first;
                 ruleIndex < last;
                 ruleIndex++)
            {
                int targetLaneId =
                    laneDatabase.changeToLaneIds[ruleIndex];

                Vector3 sourceStartPosition =
                    laneDatabase.GetLanePosition(
                        sourceLaneId,
                        laneDatabase.changeStartS[ruleIndex],
                        -1
                    );

                Vector3 sourceEndPosition =
                    laneDatabase.GetLanePosition(
                        sourceLaneId,
                        laneDatabase.changeEndS[ruleIndex],
                        -1
                    );

                ruleTargetStartS[ruleIndex] =
                    ProjectPositionToLaneS(
                        targetLaneId,
                        sourceStartPosition
                    );

                ruleTargetEndS[ruleIndex] =
                    ProjectPositionToLaneS(
                        targetLaneId,
                        sourceEndPosition
                    );

                BuildPhysicsCorridorRuleCache(
                    ruleIndex,
                    sourceLaneId,
                    targetLaneId
                );
            }
        }
    }

    private void BuildPhysicsCorridorRuleCache(
        int ruleIndex,
        int sourceLaneId,
        int targetLaneId)
    {
        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules ||
            sourceLaneId < 0 ||
            sourceLaneId >= laneDatabase.laneCount ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount)
        {
            return;
        }

        float sourceS = Mathf.Lerp(
            laneDatabase.changeStartS[ruleIndex],
            laneDatabase.changeEndS[ruleIndex],
            0.5f
        );
        float targetS = Mathf.Lerp(
            ruleTargetStartS[ruleIndex],
            ruleTargetEndS[ruleIndex],
            0.5f
        );
        Vector3 sourcePosition = laneDatabase.GetLanePosition(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 targetPosition = laneDatabase.GetLanePosition(
            targetLaneId,
            targetS,
            -1
        );
        Quaternion surfaceRotation = laneDatabase.GetLaneRotation(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 surfaceUp = surfaceRotation * Vector3.up;
        Vector3 surfaceForward = surfaceRotation * Vector3.forward;

        if (surfaceUp.sqrMagnitude <= 0.0001f ||
            surfaceForward.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        surfaceUp.Normalize();
        surfaceForward = Vector3.ProjectOnPlane(
            surfaceForward,
            surfaceUp
        );

        if (surfaceForward.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        surfaceForward.Normalize();
        Vector3 targetSide = Vector3.ProjectOnPlane(
            targetPosition - sourcePosition,
            surfaceUp
        );
        targetSide -= surfaceForward *
            Vector3.Dot(targetSide, surfaceForward);

        if (targetSide.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        targetSide.Normalize();
        float sourceCoordinate = Vector3.Dot(
            sourcePosition,
            targetSide
        );
        float targetCoordinate = Vector3.Dot(
            targetPosition,
            targetSide
        );
        float laneSeparation = targetCoordinate - sourceCoordinate;

        if (laneSeparation <= 0.5f)
        {
            return;
        }

        float halfLaneWidth = laneSeparation * 0.5f;
        rulePhysicsCorridorValid[ruleIndex] = true;
        rulePhysicsCorridorAxis[ruleIndex] = targetSide;
        rulePhysicsCorridorMinimum[ruleIndex] =
            sourceCoordinate - halfLaneWidth;
        rulePhysicsCorridorMaximum[ruleIndex] =
            targetCoordinate + halfLaneWidth;
    }

    private float ProjectPositionToLaneS(
        int laneId,
        Vector3 position)
    {
        int first = laneDatabase.laneSampleStarts[laneId];
        int count = laneDatabase.laneSampleCounts[laneId];
        int last = first + count - 1;

        float nearestS = 0f;
        float nearestSqr = 100000000f;

        for (int i = first; i < last; i++)
        {
            Vector3 start =
                laneDatabase.samplePositions[i];

            Vector3 end =
                laneDatabase.samplePositions[i + 1];

            Vector3 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;

            if (lengthSqr <= 0.0001f)
            {
                continue;
            }

            float t = Mathf.Clamp01(
                Vector3.Dot(position - start, segment) /
                lengthSqr
            );

            Vector3 closest =
                Vector3.Lerp(start, end, t);

            float distanceSqr =
                (position - closest).sqrMagnitude;

            if (distanceSqr >= nearestSqr)
            {
                continue;
            }

            nearestSqr = distanceSqr;
            nearestS = Mathf.Lerp(
                laneDatabase.sampleDistances[i],
                laneDatabase.sampleDistances[i + 1],
                t
            );
        }

        return nearestS;
    }

    private int FindLaneChangeRule(
        int sourceLaneId,
        int targetLaneId)
    {
        if (sourceLaneId < 0 ||
            sourceLaneId >= laneDatabase.laneCount)
        {
            return -1;
        }

        int first =
            laneDatabase.laneRuleStarts[sourceLaneId];

        int count =
            laneDatabase.laneRuleCounts[sourceLaneId];

        for (int i = first;
             i < first + count;
             i++)
        {
            if (laneDatabase.changeToLaneIds[i] ==
                targetLaneId)
            {
                return i;
            }
        }

        return -1;
    }

    private float MapSourceToTargetS(
        int ruleIndex,
        float sourceS)
    {
        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules ||
            ruleIndex >= laneDatabase.changeStartS.Length)
        {
            return sourceS;
        }

        float t = Mathf.InverseLerp(
            laneDatabase.changeStartS[ruleIndex],
            laneDatabase.changeEndS[ruleIndex],
            sourceS
        );

        return Mathf.Lerp(
            ruleTargetStartS[ruleIndex],
            ruleTargetEndS[ruleIndex],
            t
        );
    }

    private float MapTargetToSourceS(
        int ruleIndex,
        float targetS)
    {
        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules ||
            ruleIndex >= laneDatabase.changeStartS.Length)
        {
            return targetS;
        }

        float t = Mathf.InverseLerp(
            ruleTargetStartS[ruleIndex],
            ruleTargetEndS[ruleIndex],
            targetS
        );

        return Mathf.Lerp(
            laneDatabase.changeStartS[ruleIndex],
            laneDatabase.changeEndS[ruleIndex],
            t
        );
    }

    private float MapSourceToTargetSUnclamped(
        int ruleIndex,
        float sourceS)
    {
        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules ||
            ruleIndex >= laneDatabase.changeStartS.Length)
        {
            return sourceS;
        }

        float sourceStart =
            laneDatabase.changeStartS[ruleIndex];

        float sourceLength =
            laneDatabase.changeEndS[ruleIndex] -
            sourceStart;

        if (Mathf.Abs(sourceLength) < 0.001f)
        {
            return ruleTargetStartS[ruleIndex];
        }

        float targetLength =
            ruleTargetEndS[ruleIndex] -
            ruleTargetStartS[ruleIndex];

        return ruleTargetStartS[ruleIndex] +
               (sourceS - sourceStart) *
               targetLength / sourceLength;
    }

    private float MapTargetToSourceSUnclamped(
        int ruleIndex,
        float targetS)
    {
        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules ||
            ruleIndex >= laneDatabase.changeStartS.Length)
        {
            return targetS;
        }

        float targetStart =
            ruleTargetStartS[ruleIndex];

        float targetLength =
            ruleTargetEndS[ruleIndex] -
            targetStart;

        if (Mathf.Abs(targetLength) < 0.001f)
        {
            return laneDatabase.changeStartS[ruleIndex];
        }

        float sourceLength =
            laneDatabase.changeEndS[ruleIndex] -
            laneDatabase.changeStartS[ruleIndex];

        return laneDatabase.changeStartS[ruleIndex] +
               (targetS - targetStart) *
               sourceLength / targetLength;
    }

    private bool VehicleOccupiesLane(
        int vehicleIndex,
        int laneId)
    {
        if (!vehicleActive[vehicleIndex])
        {
            return false;
        }

        if (vehicleLaneIds[vehicleIndex] == laneId)
        {
            if (laneChangeActive[vehicleIndex])
            {
                float sourceLateralSeparation =
                    GetLaneChangeVisualProgress(
                        vehicleIndex,
                        laneChangeReverseManeuver[vehicleIndex],
                        laneChangeProgress[vehicleIndex]
                    );

                return sourceLateralSeparation < Mathf.Clamp(
                    laneChangeSourceOccupancyEnd,
                    0.7f,
                    0.95f
                );
            }

            return true;
        }

        if (!laneChangeActive[vehicleIndex] ||
            laneChangeTargetLaneIds[vehicleIndex] !=
                laneId)
        {
            return false;
        }

        // A confined-space recovery reserves the adjacent lane before the
        // first reverse movement. This prevents a rear vehicle from entering
        // the shunt box while the subject changes gear and swings its body.
        if (laneChangeReverseManeuver[vehicleIndex])
        {
            return true;
        }

        float visualProgress = GetLaneChangeVisualProgress(
            vehicleIndex,
            laneChangeReverseManeuver[vehicleIndex],
            laneChangeProgress[vehicleIndex]
        );

        float lateralSeparation = visualProgress;

        return lateralSeparation >= Mathf.Clamp(
            targetLaneOccupancyStart,
            0.02f,
            0.3f
        );
    }

    private float GetSourceLaneConstraintInfluence(
        int vehicleIndex)
    {
        if (!laneChangeActive[vehicleIndex] ||
            IsReversePhase(
                laneChangeReverseManeuver[vehicleIndex],
                laneChangeProgress[vehicleIndex]
            ))
        {
            return 1f;
        }

        if (laneChangeReverseManeuver[vehicleIndex] &&
            laneChangeProgress[vehicleIndex] >=
                GetRecoveryPreparationEndProgress() - 0.0001f)
        {
            return 0f;
        }

        float lateralSeparation =
            GetLaneChangeVisualProgress(
                vehicleIndex,
                laneChangeReverseManeuver[vehicleIndex],
                laneChangeProgress[vehicleIndex]
            );

        return 1f - Mathf.InverseLerp(
            0f,
            Mathf.Clamp(
                laneChangeSourceConstraintEnd,
                0.45f,
                0.7f
            ),
            lateralSeparation
        );
    }

    private bool VehicleReservesLane(
        int vehicleIndex,
        int laneId)
    {
        if (VehicleOccupiesLane(vehicleIndex, laneId))
        {
            return true;
        }

        return vehicleActive[vehicleIndex] &&
               laneChangeActive[vehicleIndex] &&
               laneChangeTargetLaneIds[vehicleIndex] == laneId;
    }

    private float GetVehicleSOnLane(
        int vehicleIndex,
        int laneId)
    {
        float sourceS = vehicleS[vehicleIndex];

        if (laneChangeActive[vehicleIndex] &&
            laneChangeReverseManeuver[vehicleIndex])
        {
            // Keep the rear edge of the manoeuvring box fixed at the
            // furthest planned reverse pose. A following vehicle therefore
            // cannot enter the space when the subject moves forward again.
            sourceS = laneChangeReverseStartS[vehicleIndex] -
                Mathf.Max(
                    MinimumReverseRecoveryDistance,
                    laneChangeRecoveryDistance[vehicleIndex]
                );
        }
        if (vehicleLaneIds[vehicleIndex] == laneId)
        {
            return sourceS;
        }

        if (laneChangeActive[vehicleIndex] &&
            laneChangeTargetLaneIds[vehicleIndex] ==
            laneId)
        {
            return MapSourceToTargetS(
                laneChangeRuleIndices[vehicleIndex],
                sourceS
            );
        }

        return -1f;
    }

    private int FindNearestLeaderOnLane(
        int vehicleIndex,
        int laneId,
        float referenceS)
    {
        int nearestIndex = -1;
        float nearestS = 1000000f;

        bool useLaneCache =
            laneVehicleCacheReady &&
            !laneVehicleCacheDirty &&
            laneId >= 0 &&
            laneId < reservationLaneVehicleCounts.Length;

        int candidateCount = useLaneCache
            ? reservationLaneVehicleCounts[laneId]
            : slotCount;

        int cacheOffset = laneId >= 0
            ? laneId * slotCount
            : 0;

        for (int candidate = 0;
             candidate < candidateCount;
             candidate++)
        {
            int i = useLaneCache
                ? reservationLaneVehicleOrder[
                    cacheOffset + candidate
                  ]
                : candidate;

            if (i == vehicleIndex ||
                !VehicleOccupiesLane(i, laneId))
            {
                continue;
            }

            float candidateS = GetVehicleSOnLane(
                i,
                laneId
            );

            if (candidateS <= referenceS + 0.01f ||
                candidateS >= nearestS)
            {
                continue;
            }

            nearestS = candidateS;
            nearestIndex = i;
        }

        if (vehicleIndex >= 0 &&
            vehicleIndex < cachedSourceLeaderIndices.Length &&
            laneId == vehicleLaneIds[vehicleIndex])
        {
            cachedSourceLeaderIndices[vehicleIndex] =
                nearestIndex;
        }

        return nearestIndex;
    }

    private float GetLaneChangeSafetyScore(
        int vehicleIndex,
        int ruleIndex)
    {
        return GetLaneChangeSafetyScoreAtS(
            vehicleIndex,
            ruleIndex,
            vehicleS[vehicleIndex]
        );
    }

    private float GetLaneChangeSafetyScoreAtS(
        int vehicleIndex,
        int ruleIndex,
        float sourceS)
    {
        int targetLaneId =
            laneDatabase.changeToLaneIds[ruleIndex];

        float targetS = MapSourceToTargetS(
            ruleIndex,
            sourceS
        );

        float nearestFrontGap = 1000000f;
        float nearestRearGap = 1000000f;
        int nearestRearIndex = -1;

        bool useLaneCache =
            laneVehicleCacheReady &&
            !laneVehicleCacheDirty &&
            targetLaneId >= 0 &&
            targetLaneId < reservationLaneVehicleCounts.Length;

        int candidateCount = useLaneCache
            ? reservationLaneVehicleCounts[targetLaneId]
            : slotCount;

        int cacheOffset = targetLaneId >= 0
            ? targetLaneId * slotCount
            : 0;

        for (int candidate = 0;
             candidate < candidateCount;
             candidate++)
        {
            int i = useLaneCache
                ? reservationLaneVehicleOrder[
                    cacheOffset + candidate
                  ]
                : candidate;

            if (i == vehicleIndex ||
                !VehicleReservesLane(i, targetLaneId))
            {
                continue;
            }

            float otherS = GetVehicleSOnLane(
                i,
                targetLaneId
            );

            if (otherS >= targetS)
            {
                nearestFrontGap = Mathf.Min(
                    nearestFrontGap,
                    otherS - targetS -
                    GetCombinedHalfLength(vehicleIndex, i)
                );
            }
            else
            {
                float rearGap =
                    targetS - otherS -
                    GetCombinedHalfLength(i, vehicleIndex);

                if (rearGap < nearestRearGap)
                {
                    nearestRearGap = rearGap;
                    nearestRearIndex = i;
                }
            }
        }

        if (enableAuthorityPhysicsObstacles &&
            targetLaneId >= 0 &&
            targetLaneId < laneDatabase.laneCount)
        {
            Vector3 targetPosition = laneDatabase.GetLanePosition(
                targetLaneId,
                targetS,
                -1
            );
            Quaternion targetRotation = laneDatabase.GetLaneRotation(
                targetLaneId,
                targetS,
                -1
            );
            float targetSafetyMargin = Mathf.Max(
                Mathf.Max(0f, authorityObstacleSafetyMargin) +
                Mathf.Max(0f, laneChangePlayerSafetyMargin),
                Mathf.Max(0f, playerFrontBuffer)
            );

            if (IsPhysicsPoseBlockedByObstacle(
                    vehicleIndex,
                    targetPosition,
                    targetRotation,
                    targetSafetyMargin
                ))
            {
                return -1f;
            }
        }

        float requiredFrontGap = Mathf.Max(
            GetMinimumGap(vehicleIndex) + 1f,
            vehicleSpeeds[vehicleIndex] *
            GetTimeHeadway(vehicleIndex)
        );

        float requiredRearGap =
            GetMinimumGap(vehicleIndex) + 1f;

        if (nearestRearIndex >= 0)
        {
            requiredRearGap = Mathf.Max(
                GetMinimumGap(nearestRearIndex) + 1f,
                vehicleSpeeds[nearestRearIndex] *
                GetTimeHeadway(nearestRearIndex)
            );
        }

        if (nearestFrontGap < requiredFrontGap ||
            nearestRearGap < requiredRearGap)
        {
            return -1f;
        }

        return Mathf.Min(nearestFrontGap, 1000f);
    }

    private float GetNormalLaneChangeTravelDistance(
        bool emergencyManeuver)
    {
        return Mathf.Max(
            2f,
            emergencyManeuver
                ? emergencyLaneChangeTravelDistance
                : normalLaneChangeTravelDistance
        );
    }

    private float GetLaneChangeCurveSpeedLimit(
        int vehicleIndex,
        bool emergencyManeuver,
        float sourceS)
    {
        float travelDistance =
            GetNormalLaneChangeTravelDistance(
                emergencyManeuver
            );
        float laneSeparation = GetLaneChangeSeparation(
            vehicleIndex,
            sourceS
        );
        float maximumCurvature = 0f;

        // The normal lane-change path is x = Lp,
        // y = D(6p^5 - 15p^4 + 10p^3). Sample its curvature once when
        // evaluating/preparing a manoeuvre and cap speed by lateral
        // acceleration instead of rotating the body at cruise speed.
        for (int sample = 1; sample < 16; sample++)
        {
            float progress = sample / 16f;
            float firstDerivative =
                GetSmoothLaneChangeFirstDerivative(progress);
            float secondDerivative =
                GetSmoothLaneChangeSecondDerivative(progress);
            float denominatorBase =
                travelDistance * travelDistance +
                laneSeparation * laneSeparation *
                firstDerivative * firstDerivative;
            float denominator = Mathf.Pow(
                Mathf.Max(0.0001f, denominatorBase),
                1.5f
            );
            float curvature = Mathf.Abs(
                travelDistance * laneSeparation *
                secondDerivative / denominator
            );

            maximumCurvature = Mathf.Max(
                maximumCurvature,
                curvature
            );
        }

        float curveSpeed = maximumCurvature > 0.0001f
            ? Mathf.Sqrt(
                Mathf.Max(
                    0.5f,
                    laneChangeMaximumLateralAcceleration
                ) / maximumCurvature
              )
            : laneChangeSpeedLimit;

        if (laneChangeSpeedLimit > 0f)
        {
            curveSpeed = Mathf.Min(
                curveSpeed,
                laneChangeSpeedLimit
            );
        }

        return Mathf.Max(0.5f, curveSpeed);
    }

    private float GetLaneChangePreparationDistance(
        int vehicleIndex,
        float currentSpeed,
        float curveSpeedLimit)
    {
        float speedSquaredDifference =
            currentSpeed * currentSpeed -
            curveSpeedLimit * curveSpeedLimit;

        if (speedSquaredDifference <= 0f)
        {
            return 0f;
        }

        return speedSquaredDifference /
            (2f * Mathf.Max(0.1f, GetDeceleration(vehicleIndex)));
    }

    private float GetSmoothLaneChangeFirstDerivative(float progress)
    {
        float p = Mathf.Clamp01(progress);
        float oneMinusP = 1f - p;

        return 30f * p * p * oneMinusP * oneMinusP;
    }

    private float GetSmoothLaneChangeSecondDerivative(float progress)
    {
        float p = Mathf.Clamp01(progress);

        return 60f * p *
            (2f * p * p - 3f * p + 1f);
    }

    private bool IsLaneChangeFullPathClear(
        int vehicleIndex,
        int ruleIndex,
        bool emergencyManeuver,
        float pathStartS)
    {
        if (ruleIndex < 0 ||
            ruleIndex >= MaximumLaneChangeRules)
        {
            return false;
        }

        int targetLaneId =
            laneDatabase.changeToLaneIds[ruleIndex];

        if (targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount)
        {
            return false;
        }

        bool savedActive = laneChangeActive[vehicleIndex];
        bool savedReverse = laneChangeReverseManeuver[vehicleIndex];
        bool savedEmergency =
            laneChangeEmergencyManeuver[vehicleIndex];
        int savedTargetLaneId =
            laneChangeTargetLaneIds[vehicleIndex];
        int savedRuleIndex =
            laneChangeRuleIndices[vehicleIndex];
        float savedProgress =
            laneChangeProgress[vehicleIndex];
        float savedS = vehicleS[vehicleIndex];

        laneChangeActive[vehicleIndex] = true;
        laneChangeReverseManeuver[vehicleIndex] = false;
        laneChangeEmergencyManeuver[vehicleIndex] =
            emergencyManeuver;
        laneChangeTargetLaneIds[vehicleIndex] = targetLaneId;
        laneChangeRuleIndices[vehicleIndex] = ruleIndex;
        laneChangeProgress[vehicleIndex] = 0f;

        float travelDistance =
            GetNormalLaneChangeTravelDistance(
                emergencyManeuver
            );
        float endS = pathStartS + travelDistance;
        int sampleCount = Mathf.Clamp(
            laneChangePreflightSamples,
            6,
            12
        );
        bool pathClear = true;

        for (int sample = 0;
             sample <= sampleCount;
             sample++)
        {
            float fraction =
                (float)sample / sampleCount;
            float sampleS = Mathf.Lerp(
                pathStartS,
                endS,
                fraction
            );

            vehicleS[vehicleIndex] = sampleS;
            laneChangeProgress[vehicleIndex] = fraction;

            Vector3 position = GetLaneChangePathPosition(
                vehicleIndex,
                sampleS,
                fraction
            );
            Quaternion rotation = GetLaneChangeSweepRotation(
                vehicleIndex,
                pathStartS,
                0f,
                endS,
                1f,
                fraction
            );

            if (GetLaneChangePoseUnsafeMask(
                    vehicleIndex,
                    position,
                    rotation,
                    false,
                    sample > 0
                ) != 0)
            {
                pathClear = false;
                break;
            }
        }

        vehicleS[vehicleIndex] = savedS;
        laneChangeActive[vehicleIndex] = savedActive;
        laneChangeReverseManeuver[vehicleIndex] = savedReverse;
        laneChangeEmergencyManeuver[vehicleIndex] =
            savedEmergency;
        laneChangeTargetLaneIds[vehicleIndex] =
            savedTargetLaneId;
        laneChangeRuleIndices[vehicleIndex] = savedRuleIndex;
        laneChangeProgress[vehicleIndex] = savedProgress;

        return pathClear;
    }

    private void TryBeginLaneChange(
        int vehicleIndex,
        int sourceLeaderIndex)
    {
        if (!enableLaneChanges ||
            IsTruck(vehicleIndex) ||
            laneChangeActive[vehicleIndex] ||
            laneChangeCooldownRemaining[vehicleIndex] > 0f ||
            laneChangeEvaluationRemaining[vehicleIndex] > 0f)
        {
            return;
        }

        laneChangeEvaluationRemaining[vehicleIndex] =
            Mathf.Max(
                0.2f,
                laneChangeEvaluationInterval
            );

        int sourceLaneId =
            vehicleLaneIds[vehicleIndex];

        float currentS = vehicleS[vehicleIndex];
        float currentSpeed = vehicleSpeeds[vehicleIndex];

        int first =
            laneDatabase.laneRuleStarts[sourceLaneId];

        int count =
            laneDatabase.laneRuleCounts[sourceLaneId];

        int bestRuleIndex = -1;
        float bestScore = -1f;
        bool bestEmergencyManeuver = false;

        float sourceBumperGap = 1000000f;
        bool shouldOvertake = false;

        if (sourceLeaderIndex >= 0)
        {
            float sourceLeaderS = GetVehicleSOnLane(
                sourceLeaderIndex,
                sourceLaneId
            );

            sourceBumperGap =
                sourceLeaderS - currentS -
                GetCombinedHalfLength(
                    vehicleIndex,
                    sourceLeaderIndex
                );

            shouldOvertake =
                sourceBumperGap <=
                    overtakeTriggerDistance &&
                vehicleSpeeds[sourceLeaderIndex] +
                    overtakeSpeedAdvantage <
                    GetCruiseSpeed(
                        vehicleIndex,
                        sourceLaneId
                    );

            if (shouldOvertake &&
                IsSignalQueueLaneChangeBlocked(
                    vehicleIndex,
                    sourceLeaderIndex,
                    sourceLaneId,
                    currentS
                ))
            {
                shouldOvertake = false;
            }
        }

        float obstacleHitS =
            GetPhysicsObstacleHitS(vehicleIndex);
        float physicsObstacleGap = 1000000f;
        bool physicsObstacleAhead =
            obstacleHitS >= currentS;

        if (physicsObstacleAhead)
        {
            physicsObstacleGap =
                obstacleHitS - currentS;

            sourceBumperGap = Mathf.Min(
                sourceBumperGap,
                physicsObstacleGap
            );

            if (physicsObstacleGap <= overtakeTriggerDistance)
            {
                shouldOvertake = true;
            }
        }

        bool preferEmergencyManeuver =
            physicsObstacleAhead &&
            physicsObstacleGap <=
                Mathf.Max(
                    emergencyLaneChangeTravelDistance,
                    emergencyLaneChangeTriggerDistance
                );

        if (shouldOvertake &&
            IsSignalAreaLaneChangeBlocked(
                vehicleIndex,
                sourceLaneId,
                currentS
            ))
        {
            shouldOvertake = false;
        }

        for (int ruleIndex = first;
             ruleIndex < first + count;
             ruleIndex++)
        {
            if (!laneDatabase.IsChangeAllowed(
                ruleIndex,
                currentS,
                TrafficLaneDatabase.VehicleCar
            ))
            {
                continue;
            }

            int targetLaneId =
                laneDatabase.changeToLaneIds[ruleIndex];

            bool isR4Branch =
                targetLaneId ==
                TrafficLaneDatabase.LaneR4Branch;

            if (isR4Branch)
            {
                if (laneChangeBranchDecisions[vehicleIndex] == 0)
                {
                    laneChangeBranchDecisions[vehicleIndex] =
                        NextRandom01() < r4BranchProbability
                            ? 1
                            : 2;
                }

                if (laneChangeBranchDecisions[vehicleIndex] != 1)
                {
                    continue;
                }
            }
            else if (!shouldOvertake)
            {
                continue;
            }

            bool candidateBlockedRecovery =
                !isR4Branch &&
                enableBlockedLaneChangeReverse &&
                currentSpeed <= reverseMaximumStartSpeed &&
                sourceBumperGap <= reverseFrontGapThreshold;

            bool candidateEmergencyManeuver =
                !isR4Branch &&
                !candidateBlockedRecovery &&
                preferEmergencyManeuver;

            float availableTravelDistance =
                laneDatabase.changeEndS[ruleIndex] -
                currentS;

            float candidateCurveSpeedLimit =
                candidateBlockedRecovery
                    ? currentSpeed
                    : GetLaneChangeCurveSpeedLimit(
                        vehicleIndex,
                        candidateEmergencyManeuver,
                        currentS
                      );
            float candidatePreparationDistance =
                candidateBlockedRecovery
                    ? 0f
                    : GetLaneChangePreparationDistance(
                        vehicleIndex,
                        currentSpeed,
                        candidateCurveSpeedLimit
                      );
            float requiredTravelDistance =
                candidateBlockedRecovery
                    ? Mathf.Max(
                        8f,
                        laneChangeMinimumTravelDistance
                      )
                    : candidatePreparationDistance +
                      GetNormalLaneChangeTravelDistance(
                          candidateEmergencyManeuver
                      );

            if (availableTravelDistance < requiredTravelDistance)
            {
                if (isR4Branch ||
                    candidateBlockedRecovery ||
                    candidateEmergencyManeuver)
                {
                    continue;
                }

                candidateEmergencyManeuver = true;
                candidateCurveSpeedLimit =
                    GetLaneChangeCurveSpeedLimit(
                        vehicleIndex,
                        true,
                        currentS
                    );
                candidatePreparationDistance =
                    GetLaneChangePreparationDistance(
                        vehicleIndex,
                        currentSpeed,
                        candidateCurveSpeedLimit
                    );
                requiredTravelDistance =
                    candidatePreparationDistance +
                    GetNormalLaneChangeTravelDistance(true);

                if (availableTravelDistance <
                    requiredTravelDistance)
                {
                    continue;
                }
            }

            float score = GetLaneChangeSafetyScore(
                vehicleIndex,
                ruleIndex
            );

            if (score < 0f)
            {
                continue;
            }

            if (!isR4Branch &&
                score < sourceBumperGap +
                    laneChangeBenefitDistance)
            {
                continue;
            }

            if (!candidateBlockedRecovery)
            {
                bool pathClear = IsLaneChangeFullPathClear(
                    vehicleIndex,
                    ruleIndex,
                    candidateEmergencyManeuver,
                    currentS + candidatePreparationDistance
                );

                if (!pathClear &&
                    !isR4Branch &&
                    !candidateEmergencyManeuver &&
                    availableTravelDistance >=
                        GetNormalLaneChangeTravelDistance(true))
                {
                    candidateEmergencyManeuver = true;
                    candidateCurveSpeedLimit =
                        GetLaneChangeCurveSpeedLimit(
                            vehicleIndex,
                            true,
                            currentS
                        );
                    candidatePreparationDistance =
                        GetLaneChangePreparationDistance(
                            vehicleIndex,
                            currentSpeed,
                            candidateCurveSpeedLimit
                        );

                    if (availableTravelDistance <
                        candidatePreparationDistance +
                        GetNormalLaneChangeTravelDistance(true))
                    {
                        continue;
                    }

                    pathClear = IsLaneChangeFullPathClear(
                        vehicleIndex,
                        ruleIndex,
                        true,
                        currentS + candidatePreparationDistance
                    );
                }

                if (!pathClear)
                {
                    continue;
                }
            }

            if (isR4Branch)
            {
                bestRuleIndex = ruleIndex;
                bestEmergencyManeuver = false;
                break;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRuleIndex = ruleIndex;
                bestEmergencyManeuver =
                    candidateEmergencyManeuver;
            }
        }

        if (bestRuleIndex < 0)
        {
            return;
        }

        int bestTargetLaneId =
            laneDatabase.changeToLaneIds[bestRuleIndex];

        bool isBlockedOvertake =
            bestTargetLaneId !=
                TrafficLaneDatabase.LaneR4Branch &&
            enableBlockedLaneChangeReverse &&
            currentSpeed <= reverseMaximumStartSpeed &&
            sourceBumperGap <= reverseFrontGapThreshold;

        if (isBlockedOvertake &&
            !CanBeginLaneChangeReverse(
                vehicleIndex,
                bestRuleIndex
            ))
        {
            return;
        }

        laneChangeActive[vehicleIndex] = true;
        InvalidateManeuverPath(vehicleIndex);
        InvalidateLaneVehicleCaches();
        laneChangeRuleIndices[vehicleIndex] =
            bestRuleIndex;
        laneChangeTargetLaneIds[vehicleIndex] =
            bestTargetLaneId;
        laneChangeProgress[vehicleIndex] = 0f;
        laneChangeReverseManeuver[vehicleIndex] =
            isBlockedOvertake;
        laneChangeEmergencyManeuver[vehicleIndex] =
            !isBlockedOvertake &&
            bestEmergencyManeuver;
        laneChangeCurveSpeedLimits[vehicleIndex] =
            isBlockedOvertake
                ? 0f
                : GetLaneChangeCurveSpeedLimit(
                    vehicleIndex,
                    bestEmergencyManeuver,
                    currentS
                  );
        laneChangePreparing[vehicleIndex] =
            !isBlockedOvertake &&
            currentSpeed >
                laneChangeCurveSpeedLimits[vehicleIndex] +
                Mathf.Max(
                    0.01f,
                    laneChangePreparationSpeedTolerance
                );
        laneChangeReverseStartS[vehicleIndex] =
            vehicleS[vehicleIndex];
        laneChangeRecoveryDistance[vehicleIndex] = 0f;
        laneChangeRecoveryGearHoldRemaining[vehicleIndex] = 0f;

        if (isBlockedOvertake)
        {
            if (!InitializeBlockedRecoveryManeuver(vehicleIndex))
            {
                laneChangeActive[vehicleIndex] = false;
                InvalidateLaneVehicleCaches();
                laneChangeReverseManeuver[vehicleIndex] = false;
                laneChangeEmergencyManeuver[vehicleIndex] = false;
                laneChangePreparing[vehicleIndex] = false;
                laneChangeCurveSpeedLimits[vehicleIndex] = 0f;
                laneChangeTargetLaneIds[vehicleIndex] = -1;
                laneChangeRuleIndices[vehicleIndex] = -1;
                laneChangeRecoveryDistance[vehicleIndex] = 0f;
                return;
            }
        }

        if (!laneChangePreparing[vehicleIndex])
        {
            BuildManeuverPath(
                vehicleIndex,
                vehicleS[vehicleIndex]
            );
        }

        laneChangePlayerBlocked[vehicleIndex] = false;
        laneChangeVehicleBlocked[vehicleIndex] = false;
        laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
        laneChangeObstacleRestartHoldRemaining[
            vehicleIndex
        ] = 0f;
        laneChangeObstacleClearHoldRemaining[vehicleIndex] = 0f;
        laneChangeObstacleSweepDebugValid[vehicleIndex] = false;
        signalStopDebugActive[vehicleIndex] = false;
        ClearAuthorityPhysicsObstacleState(vehicleIndex);
        ClearPhysicalObstacleRestartState(vehicleIndex);

        if (laneChangeTargetLaneIds[vehicleIndex] ==
            TrafficLaneDatabase.LaneR4Branch)
        {
            laneChangeBranchDecisions[vehicleIndex] = 2;
        }

        activeLaneChangeCount++;
    }

    private bool TryBeginBlockedLaneChangeRecovery(
        int vehicleIndex,
        int sourceLeaderIndex)
    {
        if (!enableBlockedLaneChangeReverse ||
            !laneChangeActive[vehicleIndex] ||
            laneChangePreparing[vehicleIndex] ||
            laneChangeReverseManeuver[vehicleIndex] ||
            vehicleSpeeds[vehicleIndex] >
                reverseMaximumStartSpeed)
        {
            return false;
        }

        float visualProgress =
            GetLaneChangeVisualProgress(
                vehicleIndex,
                false,
                laneChangeProgress[vehicleIndex]
            );

        if (visualProgress > 0.03f)
        {
            return false;
        }

        int laneId = vehicleLaneIds[vehicleIndex];
        float currentS = vehicleS[vehicleIndex];
        float frontGap = 1000000f;

        if (sourceLeaderIndex >= 0)
        {
            frontGap =
                GetVehicleSOnLane(
                    sourceLeaderIndex,
                    laneId
                ) -
                currentS -
                GetCombinedHalfLength(
                    vehicleIndex,
                    sourceLeaderIndex
                );
        }

        float playerStopS =
            GetPhysicsObstacleHitS(vehicleIndex);

        if (playerStopS >= currentS)
        {
            frontGap = Mathf.Min(
                frontGap,
                playerStopS - currentS
            );
        }

        if (frontGap > reverseFrontGapThreshold ||
            IsSignalAreaLaneChangeBlocked(
                vehicleIndex,
                laneId,
                currentS
            ))
        {
            return false;
        }

        int ruleIndex =
            laneChangeRuleIndices[vehicleIndex];

        if (ruleIndex < 0 ||
            !CanBeginLaneChangeReverse(
                vehicleIndex,
                ruleIndex
            ))
        {
            return false;
        }

        bool previousEmergencyManeuver =
            laneChangeEmergencyManeuver[vehicleIndex];
        laneChangeReverseManeuver[vehicleIndex] = true;
        laneChangeEmergencyManeuver[vehicleIndex] = false;
        laneChangePreparing[vehicleIndex] = false;
        laneChangeCurveSpeedLimits[vehicleIndex] = 0f;
        laneChangeReverseStartS[vehicleIndex] = currentS;
        InvalidateManeuverPath(vehicleIndex);

        if (!InitializeBlockedRecoveryManeuver(vehicleIndex))
        {
            laneChangeReverseManeuver[vehicleIndex] = false;
            laneChangeEmergencyManeuver[vehicleIndex] =
                previousEmergencyManeuver;
            laneChangeRecoveryDistance[vehicleIndex] = 0f;
            return false;
        }

        laneChangeProgress[vehicleIndex] = 0f;
        vehicleSpeeds[vehicleIndex] = 0f;
        vehicleAccelerations[vehicleIndex] = 0f;
        BuildManeuverPath(vehicleIndex, currentS);
        return true;
    }

    private bool IsSignalQueueLaneChangeBlocked(
        int vehicleIndex,
        int leaderIndex,
        int laneId,
        float currentS)
    {
        if (leaderIndex < 0 ||
            laneId < 0 ||
            laneId >= laneDatabase.laneCount ||
            laneDatabase.signalGroupIds[laneId] < 0)
        {
            return false;
        }

        float lineS = laneDatabase.stopLineS[laneId];

        if (lineS < 0f)
        {
            return false;
        }

        float stopS =
            lineS -
            GetVehicleFrontExtent(vehicleIndex) -
            stopLineFrontBuffer;

        if (currentS >= stopS ||
            stopS - currentS >
                Mathf.Max(
                    10f,
                    signalQueueLaneChangeBlockDistance
                ))
        {
            return false;
        }

        if (signalQueueReleaseHoldRemaining > 0f)
        {
            return true;
        }

        float leaderS = GetVehicleSOnLane(
            leaderIndex,
            laneId
        );

        bool leaderIsInSignalQueue =
            leaderS <=
            lineS +
            GetVehicleRearExtent(leaderIndex) +
            2f;

        if (!leaderIsInSignalQueue)
        {
            return false;
        }

        if (currentSignalState != ShinhoTime.SignalGreen)
        {
            return true;
        }

        float releaseSpeed = Mathf.Max(
            1f,
            signalQueueReleaseSpeed
        );

        return vehicleSpeeds[vehicleIndex] < releaseSpeed ||
               vehicleSpeeds[leaderIndex] < releaseSpeed;
    }

    private bool IsSignalAreaLaneChangeBlocked(
        int vehicleIndex,
        int laneId,
        float currentS)
    {
        if (laneId < 0 ||
            laneId >= laneDatabase.laneCount ||
            laneDatabase.signalGroupIds[laneId] < 0)
        {
            return false;
        }

        float lineS = laneDatabase.stopLineS[laneId];

        if (lineS < 0f)
        {
            return false;
        }

        float distanceToLine = lineS - currentS;

        bool insideSignalQueueArea =
            distanceToLine >=
                -GetVehicleRearExtent(vehicleIndex) &&
            distanceToLine <= Mathf.Max(
                10f,
                signalQueueLaneChangeBlockDistance
            );

        return insideSignalQueueArea &&
            (currentSignalState != ShinhoTime.SignalGreen ||
             signalQueueReleaseHoldRemaining > 0f);
    }

    private bool CanBeginLaneChangeReverse(
        int vehicleIndex,
        int ruleIndex)
    {
        float reverseDistance = Mathf.Max(
            0.5f,
            laneChangeReverseDistance
        );

        float currentS = vehicleS[vehicleIndex];
        float targetSourceS = currentS - reverseDistance;

        int sourceLaneId =
            vehicleLaneIds[vehicleIndex];

        float minimumS = GetReverseMinimumS(
            vehicleIndex,
            sourceLaneId
        );

        targetSourceS = Mathf.Max(
            targetSourceS,
            minimumS
        );

        if (currentS - targetSourceS <
                MinimumReverseRecoveryDistance ||
            targetSourceS <=
                laneDatabase.spawnS[sourceLaneId] ||
            !laneDatabase.IsChangeAllowed(
                ruleIndex,
                targetSourceS,
                TrafficLaneDatabase.VehicleCar
            ))
        {
            return false;
        }

        return GetLaneChangeSafetyScoreAtS(
            vehicleIndex,
            ruleIndex,
            targetSourceS
        ) >= 0f;
    }

    private float GetReverseMinimumS(
        int vehicleIndex,
        int laneId)
    {
        float minimumS = Mathf.Max(
            0f,
            laneDatabase.spawnS[laneId]
        );

        float currentS = vehicleS[vehicleIndex];

        for (int i = 0; i < slotCount; i++)
        {
            if (i == vehicleIndex ||
                !VehicleOccupiesLane(i, laneId))
            {
                continue;
            }

            float otherS = GetVehicleSOnLane(
                i,
                laneId
            );

            if (otherS >= currentS)
            {
                continue;
            }

            // This is a short, low-speed recovery manoeuvre. Requiring the
            // normal following gap here prevents every useful reverse in a
            // compact queue and creates a permanent bottleneck.
            float rearGap = Mathf.Max(
                0.25f,
                reverseRearClearance
            );

            minimumS = Mathf.Max(
                minimumS,
                otherS +
                GetCombinedHalfLength(
                    i,
                    vehicleIndex
                ) +
                rearGap
            );
        }

        return ApplyPhysicsReverseLimit(
            vehicleIndex,
            currentS,
            minimumS
        );
    }

    private bool InitializeBlockedRecoveryManeuver(
        int vehicleIndex)
    {
        float availableRearDistance =
            laneChangeReverseStartS[vehicleIndex] -
            GetReverseMinimumS(
                vehicleIndex,
                vehicleLaneIds[vehicleIndex]
            );

        if (availableRearDistance <
            MinimumReverseRecoveryDistance)
        {
            laneChangeRecoveryDistance[vehicleIndex] = 0f;
            return false;
        }

        laneChangeRecoveryDistance[vehicleIndex] =
            QuantizeRecoveryDistanceDown(
                Mathf.Clamp(
                    availableRearDistance,
                    MinimumReverseRecoveryDistance,
                    Mathf.Max(
                        MinimumReverseRecoveryDistance,
                        laneChangeReverseDistance
                    )
                )
            );

        laneChangeRecoveryGearHoldRemaining[vehicleIndex] = 0f;

        if (recoveryRoadArea == null)
        {
            return true;
        }

        float minimumDistance = MinimumReverseRecoveryDistance;
        float maximumDistance = laneChangeRecoveryDistance[vehicleIndex];

        if (!IsRecoveryRoadPathValid(
                vehicleIndex,
                minimumDistance
            ))
        {
            laneChangeRecoveryDistance[vehicleIndex] = 0f;
            return false;
        }

        if (IsRecoveryRoadPathValid(
                vehicleIndex,
                maximumDistance
            ))
        {
            laneChangeRecoveryDistance[vehicleIndex] =
                QuantizeRecoveryDistanceDown(maximumDistance);
            return true;
        }

        float safeDistance = minimumDistance;
        float unsafeDistance = maximumDistance;

        for (int iteration = 0; iteration < 6; iteration++)
        {
            float middleDistance =
                (safeDistance + unsafeDistance) * 0.5f;

            if (IsRecoveryRoadPathValid(
                    vehicleIndex,
                    middleDistance
                ))
            {
                safeDistance = middleDistance;
            }
            else
            {
                unsafeDistance = middleDistance;
            }
        }

        laneChangeRecoveryDistance[vehicleIndex] =
            QuantizeRecoveryDistanceDown(safeDistance);
        return true;
    }

    private bool IsRecoveryRoadPathValid(
        int vehicleIndex,
        float recoveryDistance)
    {
        if (recoveryRoadArea == null)
        {
            return true;
        }

        float originalDistance =
            laneChangeRecoveryDistance[vehicleIndex];
        laneChangeRecoveryDistance[vehicleIndex] = recoveryDistance;
        float preparationEnd =
            GetRecoveryPreparationEndProgress();

        for (int sample = 0; sample <= 12; sample++)
        {
            float progress = preparationEnd * sample / 12f;
            float sourceS = laneChangeReverseStartS[vehicleIndex] +
                GetRecoveryLongitudinalOffset(
                    vehicleIndex,
                    progress,
                    recoveryDistance
                );
            Vector3 position = GetLaneChangePathPosition(
                vehicleIndex,
                sourceS,
                progress
            );
            Quaternion rotation =
                GetRecoveryKinematicWorldRotation(
                    vehicleIndex,
                    sourceS,
                    progress
                );

            if (!IsRecoveryRearInsideRoadArea(
                    vehicleIndex,
                    position,
                    rotation
                ))
            {
                laneChangeRecoveryDistance[vehicleIndex] =
                    originalDistance;
                return false;
            }
        }

        laneChangeRecoveryDistance[vehicleIndex] =
            originalDistance;
        return true;
    }

    private float GetRecoveryFirstReverseEndProgress()
    {
        return Mathf.Clamp(
            reversePhaseFraction,
            0.2f,
            0.35f
        );
    }

    private float GetRecoveryPreparationEndProgress()
    {
        return Mathf.Min(
            0.7f,
            GetRecoveryFirstReverseEndProgress() + 0.25f
        );
    }

    private float GetRecoveryFinalTravelDistance(
        Vector3 preparationPose,
        float laneWidth)
    {
        float configuredDistance = Mathf.Max(
            8f,
            laneChangeMinimumTravelDistance
        );
        float safeLaneWidth = Mathf.Max(0.5f, laneWidth);
        float headingTangent = Mathf.Abs(
            Mathf.Tan(preparationPose.z)
        );

        if (headingTangent <= 0.001f)
        {
            return configuredDistance;
        }

        float startLateral =
            preparationPose.y / safeLaneWidth;
        float remainingLateral = Mathf.Max(
            0.25f,
            1f - startLateral
        );

        // Preserve the exact escape heading while keeping the cubic merge
        // monotonic. A long fixed 22m merge required clamping the starting
        // tangent, which snapped a 30~35 degree body angle down to ~20
        // degrees at the phase boundary.
        float maximumNormalizedTangent = Mathf.Min(
            2.5f,
            2.4f * remainingLateral
        );
        float continuityDistance =
            maximumNormalizedTangent *
            safeLaneWidth /
            headingTangent;

        return Mathf.Clamp(
            continuityDistance,
            8f,
            configuredDistance
        );
    }

    private Vector3 GetRecoveryKinematicLocalPose(
        int vehicleIndex,
        float progress,
        float recoveryDistance)
    {
        float distance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            recoveryDistance
        );
        float reverseOneEnd =
            GetRecoveryFirstReverseEndProgress();
        float preparationEnd =
            GetRecoveryPreparationEndProgress();
        float primaryCurvature =
            GetRecoveryPrimaryCurvature(vehicleIndex, distance);
        Vector3 pose = Vector3.zero;

        float phaseRatio = SmoothLaneChange01(
            Mathf.InverseLerp(0f, reverseOneEnd, progress)
        );

        if (progress < reverseOneEnd)
        {
            return AdvanceRecoveryKinematicPose(
                pose,
                -distance * phaseRatio,
                -primaryCurvature
            );
        }

        pose = AdvanceRecoveryKinematicPose(
            pose,
            -distance,
            -primaryCurvature
        );

        phaseRatio = SmoothRecoveryForwardExit01(
            Mathf.InverseLerp(
                reverseOneEnd,
                preparationEnd,
                progress
            )
        );

        return AdvanceRecoveryKinematicPose(
            pose,
            distance * RecoveryForwardPreparationFactor * phaseRatio,
            primaryCurvature
        );
    }

    private float SmoothRecoveryForwardExit01(float progress)
    {
        float t = Mathf.Clamp01(progress);

        // Start the forward gear from rest, but leave the preparation arc
        // with unit velocity. The old quintic ease reached zero velocity at
        // both ends, creating a visible pause before the final merge.
        return t * t * (2f - t);
    }

    private Vector3 AdvanceRecoveryKinematicPose(
        Vector3 pose,
        float signedDistance,
        float curvature)
    {
        float heading = pose.z;

        if (Mathf.Abs(curvature) <= 0.0001f)
        {
            pose.x += signedDistance * Mathf.Cos(heading);
            pose.y += signedDistance * Mathf.Sin(heading);
            return pose;
        }

        float nextHeading = heading + curvature * signedDistance;
        pose.x +=
            (Mathf.Sin(nextHeading) - Mathf.Sin(heading)) /
            curvature;
        pose.y +=
            (-Mathf.Cos(nextHeading) + Mathf.Cos(heading)) /
            curvature;
        pose.z = nextHeading;
        return pose;
    }

    private float GetRecoveryPrimaryCurvature(
        int vehicleIndex,
        float recoveryDistance)
    {
        float distance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            recoveryDistance
        );
        float laneWidth = GetLaneChangeSeparation(
            vehicleIndex,
            vehicleS[vehicleIndex]
        );
        float desiredLateralDistance = Mathf.Clamp(
            reverseLateralProgress,
            0.05f,
            0.25f
        ) * laneWidth;
        float desiredCurvature =
            2f * desiredLateralDistance /
            Mathf.Max(0.36f, distance * distance);
        float wheelBase = Mathf.Max(
            2.2f,
            GetVehicleLength(vehicleIndex) * 0.55f
        );
        float maximumCurvature = Mathf.Tan(
            Mathf.Deg2Rad * Mathf.Clamp(
                blockedRecoveryMaximumSteeringAngle,
                20f,
                40f
            )
        ) / wheelBase;
        float maximumBodyCurvature =
            Mathf.Deg2Rad * Mathf.Clamp(
                blockedRecoveryMaximumBodyAngle,
                18f,
                38f
            ) /
            // The unified recovery path has one full reverse arc followed by
            // one shorter forward arc. Limit the combined heading change to
            // the configured body angle.
            Mathf.Max(
                0.1f,
                distance *
                    (1f + RecoveryForwardPreparationFactor)
            );

        return Mathf.Clamp(
            desiredCurvature,
            0.02f,
            Mathf.Max(
                0.02f,
                Mathf.Min(
                    maximumCurvature,
                    maximumBodyCurvature
                )
            )
        );
    }

    private float GetLaneChangeSeparation(
        int vehicleIndex,
        float sourceS)
    {
        int sourceLaneId = vehicleLaneIds[vehicleIndex];
        int targetLaneId = laneChangeTargetLaneIds[vehicleIndex];
        int ruleIndex = laneChangeRuleIndices[vehicleIndex];

        if (sourceLaneId < 0 ||
            sourceLaneId >= laneDatabase.laneCount ||
            targetLaneId < 0 ||
            targetLaneId >= laneDatabase.laneCount ||
            ruleIndex < 0)
        {
            return 3.2f;
        }

        Vector3 sourcePosition = laneDatabase.GetLanePosition(
            sourceLaneId,
            sourceS,
            -1
        );
        Vector3 targetPosition = laneDatabase.GetLanePosition(
            targetLaneId,
            MapSourceToTargetSUnclamped(ruleIndex, sourceS),
            -1
        );

        return Mathf.Max(
            0.5f,
            Vector3.Distance(sourcePosition, targetPosition)
        );
    }

    private float GetRecoveryLongitudinalOffset(
        int vehicleIndex,
        float progress,
        float recoveryDistance)
    {
        return GetRecoveryKinematicLocalPose(
            vehicleIndex,
            progress,
            recoveryDistance
        ).x;
    }

    private float GetRecoveryCurrentPhaseEndProgress(
        float progress)
    {
        if (progress < GetRecoveryFirstReverseEndProgress() - 0.0001f)
        {
            return GetRecoveryFirstReverseEndProgress();
        }

        return GetRecoveryPreparationEndProgress();
    }

    private bool CanCompleteRecoveryPhaseAtVehicleMargin(
        int vehicleIndex,
        float safeS,
        float safeProgress,
        float phaseEnd,
        float recoveryDistance)
    {
        if (!laneChangeVehicleBlocked[vehicleIndex] ||
            laneChangePlayerBlocked[vehicleIndex] ||
            laneChangeRoadBoundaryBlocked[vehicleIndex])
        {
            return false;
        }

        float phaseEndS = laneChangeReverseStartS[vehicleIndex] +
            GetRecoveryLongitudinalOffset(
                vehicleIndex,
                phaseEnd,
                recoveryDistance
            );

        Vector3 safePosition = GetLaneChangePathPosition(
            vehicleIndex,
            safeS,
            safeProgress
        );
        Vector3 phaseEndPosition = GetLaneChangePathPosition(
            vehicleIndex,
            phaseEndS,
            phaseEnd
        );

        if (Vector3.Distance(safePosition, phaseEndPosition) >
            RecoveryPhaseCompletionPositionTolerance)
        {
            return false;
        }

        Quaternion safeRotation = GetRecoveryKinematicWorldRotation(
            vehicleIndex,
            safeS,
            safeProgress
        );
        Quaternion phaseEndRotation =
            GetRecoveryKinematicWorldRotation(
                vehicleIndex,
                phaseEndS,
                phaseEnd
            );

        if (Quaternion.Angle(safeRotation, phaseEndRotation) >
            RecoveryPhaseCompletionRotationTolerance)
        {
            return false;
        }

        // The normal sweep includes the configurable comfort margin. Near a
        // gear boundary that margin can leave a vehicle a few centimetres
        // short forever. Complete only when the real bodies, plus a very
        // small physical margin, still do not overlap at the phase endpoint.
        return !IsLaneChangePoseBlockedByVehicleWithMargin(
            vehicleIndex,
            phaseEndPosition,
            phaseEndRotation,
            RecoveryPhaseCompletionPhysicalMargin
        );
    }

    private void UpdateBlockedLaneChangeRecoveryManeuver(
        int vehicleIndex,
        float deltaTime)
    {
        float gearHold =
            laneChangeRecoveryGearHoldRemaining[vehicleIndex];

        if (gearHold > 0f)
        {
            laneChangeRecoveryGearHoldRemaining[vehicleIndex] =
                Mathf.Max(0f, gearHold - deltaTime);
            StopVehicleForLaneChangeObstacle(
                vehicleIndex,
                deltaTime
            );
            return;
        }

        float oldS = vehicleS[vehicleIndex];
        float oldProgress = laneChangeProgress[vehicleIndex];
        float recoveryDistance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            laneChangeRecoveryDistance[vehicleIndex]
        );
        float phaseEnd =
            GetRecoveryCurrentPhaseEndProgress(oldProgress);
        bool movingInReverse = IsReversePhase(
            true,
            oldProgress
        );
        float previousSpeed = vehicleSpeeds[vehicleIndex];
        float targetPathSpeed = movingInReverse
            ? Mathf.Max(0.1f, laneChangeReverseSpeed)
            : Mathf.Max(0.1f, blockedRecoveryForwardSpeed);
        float remainingPhaseDistance = Mathf.Max(
            0f,
            SampleManeuverPathDistance(
                vehicleIndex,
                phaseEnd
            ) -
            SampleManeuverPathDistance(
                vehicleIndex,
                oldProgress
            )
        );

        if (movingInReverse)
        {
            float stoppingDeceleration = Mathf.Max(
                0.1f,
                GetDeceleration(vehicleIndex)
            );
            float stoppingSpeed = Mathf.Max(
                0f,
                Mathf.Sqrt(
                    2f *
                    stoppingDeceleration *
                    remainingPhaseDistance
                ) - stoppingDeceleration * deltaTime
            );
            targetPathSpeed = Mathf.Min(
                targetPathSpeed,
                stoppingSpeed
            );
        }

        float speedChangeRate =
            targetPathSpeed >= previousSpeed
                ? GetAcceleration(vehicleIndex)
                : GetDeceleration(vehicleIndex);
        float movementSpeed = Mathf.MoveTowards(
            previousSpeed,
            targetPathSpeed,
            speedChangeRate * deltaTime
        );
        float pathTravelDistance =
            (previousSpeed + movementSpeed) *
            0.5f *
            deltaTime;
        float candidateProgress =
            remainingPhaseDistance <= 0.01f
                ? phaseEnd
                : AdvanceManeuverProgressByDistance(
                    vehicleIndex,
                    oldProgress,
                    pathTravelDistance,
                    phaseEnd
                  );

        float newS = SampleManeuverPathSourceS(
            vehicleIndex,
            candidateProgress
        );

        bool obstacleSweepBlocked =
            EvaluateLaneChangeObstacleSweep(
                vehicleIndex,
                oldS,
                oldProgress,
                newS,
                candidateProgress
            );

        if (obstacleSweepBlocked)
        {
            newS = laneChangeObstacleSweepSafeS;
            candidateProgress =
                laneChangeObstacleSweepSafeProgress;

            if (CanCompleteRecoveryPhaseAtVehicleMargin(
                    vehicleIndex,
                    newS,
                    candidateProgress,
                    phaseEnd,
                    recoveryDistance
                ))
            {
                // Change gear from the phase endpoint instead of waiting for
                // a comfort-margin overlap to disappear. Player and road
                // boundary blocks never use this exception.
                newS = laneChangeReverseStartS[vehicleIndex] +
                    GetRecoveryLongitudinalOffset(
                        vehicleIndex,
                        phaseEnd,
                        recoveryDistance
                    );
                candidateProgress = phaseEnd;
                obstacleSweepBlocked = false;
                laneChangePlayerBlocked[vehicleIndex] = false;
                laneChangeVehicleBlocked[vehicleIndex] = false;
                laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
                laneChangeObstacleClearHoldRemaining[vehicleIndex] = 0f;
            }
        }

        if (obstacleSweepBlocked)
        {
            movementSpeed = 0f;
        }

        vehicleS[vehicleIndex] = newS;
        laneChangeProgress[vehicleIndex] = candidateProgress;
        vehicleSpeeds[vehicleIndex] = movementSpeed;
        vehicleAccelerations[vehicleIndex] =
            deltaTime > 0.0001f
                ? (movementSpeed - previousSpeed) / deltaTime
                : 0f;
        vehicleRenderMaximumS[vehicleIndex] = newS;

        if (obstacleSweepBlocked ||
            candidateProgress < phaseEnd - 0.0001f)
        {
            return;
        }

        bool completedReverse =
            phaseEnd == GetRecoveryFirstReverseEndProgress();

        if (!completedReverse)
        {
            laneChangeProgress[vehicleIndex] =
                GetRecoveryPreparationEndProgress();
            vehicleS[vehicleIndex] = SampleManeuverPathSourceS(
                vehicleIndex,
                laneChangeProgress[vehicleIndex]
            );
            vehicleSpeeds[vehicleIndex] = movementSpeed;
            vehicleAccelerations[vehicleIndex] =
                deltaTime > 0.0001f
                    ? (vehicleSpeeds[vehicleIndex] -
                       previousSpeed) / deltaTime
                    : 0f;
            laneChangeRecoveryGearHoldRemaining[vehicleIndex] = 0f;
            laneChangeObstacleRestartHoldRemaining[vehicleIndex] = 0f;
            obstacleRestartDelayRemaining[vehicleIndex] = 0f;
            waitingForObstacleRelease[vehicleIndex] = false;
            vehicleRenderMaximumS[vehicleIndex] = -1f;
            return;
        }

        // The unified recovery has exactly one gear change: reverse away
        // from the obstacle, then drive forward onto the normal merge curve.
        laneChangeRecoveryGearHoldRemaining[vehicleIndex] =
            Mathf.Max(0.15f, blockedRecoveryGearShiftPause);
        vehicleSpeeds[vehicleIndex] = 0f;
        vehicleAccelerations[vehicleIndex] = 0f;
    }

    private void UpdateLaneChange(int vehicleIndex)
    {
        if (!laneChangeActive[vehicleIndex])
        {
            return;
        }

        if (laneChangePreparing[vehicleIndex])
        {
            laneChangeProgress[vehicleIndex] = 0f;
            return;
        }

        if (!IsManeuverPathUsable(vehicleIndex))
        {
            return;
        }

        if (laneChangeProgress[vehicleIndex] < 1f)
        {
            return;
        }

        int ruleIndex =
            laneChangeRuleIndices[vehicleIndex];

        int targetLaneId =
            laneChangeTargetLaneIds[vehicleIndex];

        float targetS = MapSourceToTargetSUnclamped(
            ruleIndex,
            vehicleS[vehicleIndex]
        );

        vehicleLaneIds[vehicleIndex] = targetLaneId;
        vehicleS[vehicleIndex] = targetS;
        previousVehicleS[vehicleIndex] = targetS;
        vehicleRenderMaximumS[vehicleIndex] = -1f;
        sampleHints[vehicleIndex] =
            laneDatabase.FindSampleIndex(
                targetLaneId,
                targetS,
                -1
            );

        laneChangeActive[vehicleIndex] = false;
        InvalidateManeuverPath(vehicleIndex);
        InvalidateLaneVehicleCaches();
        laneChangeReverseManeuver[vehicleIndex] = false;
        laneChangeEmergencyManeuver[vehicleIndex] = false;
        laneChangePreparing[vehicleIndex] = false;
        laneChangeCurveSpeedLimits[vehicleIndex] = 0f;
        laneChangeReverseStartS[vehicleIndex] = 0f;
        laneChangeRecoveryDistance[vehicleIndex] = 0f;
        laneChangeRecoveryGearHoldRemaining[vehicleIndex] = 0f;
        activeLaneChangeCount = Mathf.Max(
            0,
            activeLaneChangeCount - 1
        );
        laneChangeTargetLaneIds[vehicleIndex] = -1;
        laneChangeRuleIndices[vehicleIndex] = -1;
        laneChangeProgress[vehicleIndex] = 0f;
        laneChangePlayerBlocked[vehicleIndex] = false;
        laneChangeVehicleBlocked[vehicleIndex] = false;
        laneChangeRoadBoundaryBlocked[vehicleIndex] = false;
        laneChangeObstacleRestartHoldRemaining[
            vehicleIndex
        ] = 0f;
        laneChangeObstacleClearHoldRemaining[vehicleIndex] = 0f;
        laneChangeObstacleSweepDebugValid[vehicleIndex] = false;
        laneChangeCooldownRemaining[vehicleIndex] =
            Mathf.Max(0f, laneChangeCooldown);
        laneChangeEvaluationRemaining[vehicleIndex] =
            Mathf.Max(
                0.2f,
                laneChangeEvaluationInterval
            );
    }

    private float GetManeuverSourceAdvanceFactor(
        int vehicleIndex,
        float progress)
    {
        if (!IsManeuverPathUsable(vehicleIndex))
        {
            return 1f;
        }

        float coordinate =
            GetManeuverPathSampleCoordinate(progress);
        int segment = Mathf.Clamp(
            Mathf.FloorToInt(coordinate),
            0,
            ManeuverPathSampleCount - 2
        );
        int offset = GetManeuverPathOffset(vehicleIndex);
        float sourceAdvance =
            maneuverPathSourceS[offset + segment + 1] -
            maneuverPathSourceS[offset + segment];
        float pathAdvance =
            maneuverPathDistances[offset + segment + 1] -
            maneuverPathDistances[offset + segment];

        return Mathf.Clamp01(
            sourceAdvance /
            Mathf.Max(0.0001f, pathAdvance)
        );
    }

    private void TryFillVehicleSlots()
    {
        int targetCount = Mathf.Clamp(
            targetActiveVehicles,
            0,
            slotCount
        );

        if (activeVehicleCount >= targetCount ||
            respawnTimer > 0f)
        {
            return;
        }

        bool spawned = TrySpawnOneVehicle();
        respawnTimer = spawned
            ? Mathf.Max(0.15f, respawnInterval)
            : Mathf.Max(0.1f, respawnInterval * 0.5f);
    }

    private void DistributeStartupVehicles()
    {
        int targetCount = Mathf.Clamp(
            targetActiveVehicles,
            0,
            slotCount
        );

        int laneCount = laneDatabase.laneCount;
        int[] startupLaneCounts =
            new int[laneCount];

        int startupLaneCursor =
            Mathf.Abs(randomState) %
            Mathf.Max(1, laneCount);

        int assignedCount = 0;

        for (int i = 0;
             i < slotCount &&
             assignedCount < targetCount;
             i++)
        {
            if (vehicleRoots[i] == null)
            {
                continue;
            }

            int laneId = ChooseStartupLane(
                i,
                startupLaneCounts,
                startupLaneCursor
            );

            if (laneId < 0)
            {
                continue;
            }

            vehicleLaneIds[i] = laneId;
            startupLaneCounts[laneId]++;
            assignedCount++;
            startupLaneCursor =
                (laneId + 1) % laneCount;
        }

        for (int laneId = 0;
             laneId < laneCount;
             laneId++)
        {
            int vehiclesOnLane =
                startupLaneCounts[laneId];

            if (vehiclesOnLane <= 0)
            {
                continue;
            }

            float laneStart =
                laneDatabase.spawnS[laneId];

            float laneEnd = GetLaneEndS(laneId);

            float padding = Mathf.Clamp(
                startupDistributionEdgePadding,
                0f,
                Mathf.Max(
                    0f,
                    (laneEnd - laneStart) * 0.2f
                )
            );

            float usableStart = laneStart + padding;
            float usableEnd = laneEnd - padding;

            if (usableEnd <= usableStart)
            {
                usableStart = laneStart;
                usableEnd = laneEnd;
            }

            float cellLength =
                (usableEnd - usableStart) /
                vehiclesOnLane;

            int ordinal = 0;

            for (int i = 0;
                 i < slotCount;
                 i++)
            {
                if (vehicleRoots[i] == null ||
                    vehicleLaneIds[i] != laneId ||
                    vehicleActive[i])
                {
                    continue;
                }

                float cellOffset = Mathf.Lerp(
                    0.3f,
                    0.7f,
                    NextRandom01()
                );

                float laneS =
                    usableStart +
                    (ordinal + cellOffset) *
                    cellLength;

                laneS = MoveStartupPositionAwayFromStopLine(
                    i,
                    laneId,
                    laneS,
                    usableStart,
                    usableEnd
                );

                speedFactors[i] = 1f;

                ActivateVehicleAt(
                    i,
                    laneId,
                    laneS,
                    1f,
                    GetCruiseSpeed(i, laneId),
                    4f + NextRandom01() * 3f
                );

                ordinal++;
            }
        }
    }

    private int ChooseStartupLane(
        int vehicleIndex,
        int[] startupLaneCounts,
        int laneCursor)
    {
        int bestLaneId = -1;
        float bestScore = 1000000f;

        int laneCount = laneDatabase.laneCount;

        for (int offset = 0;
             offset < laneCount;
             offset++)
        {
            int laneId =
                (laneCursor + offset) % laneCount;

            if (!CanUseStartupLane(
                laneId,
                vehicleIndex
            ) ||
                startupLaneCounts[laneId] >= Mathf.Max(
                    1,
                    maximumVehiclesPerSpawnLane
                ))
            {
                continue;
            }

            float weight = Mathf.Max(
                0.01f,
                laneDatabase.spawnWeights[laneId]
            );

            float score =
                (startupLaneCounts[laneId] + 0.5f) /
                weight;

            bool lowerScore =
                score < bestScore - 0.0001f;

            bool betterTie =
                Mathf.Abs(score - bestScore) <= 0.0001f &&
                (bestLaneId < 0 ||
                 startupLaneCounts[laneId] <
                 startupLaneCounts[bestLaneId]);

            if (!lowerScore && !betterTie)
            {
                continue;
            }

            bestLaneId = laneId;
            bestScore = score;
        }

        return bestLaneId;
    }

    private bool CanUseStartupLane(
        int laneId,
        int vehicleIndex)
    {
        if (laneId < 0 ||
            laneId >= laneDatabase.laneCount ||
            laneDatabase.spawnS[laneId] < 0f ||
            laneDatabase.spawnWeights[laneId] <= 0f)
        {
            return false;
        }

        if ((laneDatabase.laneVehicleMasks[laneId] &
             GetVehicleMask(vehicleIndex)) == 0)
        {
            return false;
        }

        return GetLaneEndS(laneId) >
               laneDatabase.spawnS[laneId];
    }

    private float MoveStartupPositionAwayFromStopLine(
        int vehicleIndex,
        int laneId,
        float laneS,
        float usableStart,
        float usableEnd)
    {
        float stopLineS =
            laneDatabase.stopLineS[laneId];

        if (stopLineS < 0f)
        {
            return laneS;
        }

        float clearance = Mathf.Max(
            0f,
            startupStopLineClearance
        ) + Mathf.Max(
            GetVehicleFrontExtent(vehicleIndex),
            GetVehicleRearExtent(vehicleIndex)
        );

        float distanceToStopLine =
            laneS - stopLineS;

        if (Mathf.Abs(distanceToStopLine) >= clearance)
        {
            return laneS;
        }

        float beforeStopLine =
            stopLineS - clearance;

        float afterStopLine =
            stopLineS + clearance;

        bool canMoveBefore =
            beforeStopLine >= usableStart;

        bool canMoveAfter =
            afterStopLine <= usableEnd;

        if (canMoveBefore && canMoveAfter)
        {
            return Mathf.Abs(laneS - beforeStopLine) <=
                   Mathf.Abs(laneS - afterStopLine)
                ? beforeStopLine
                : afterStopLine;
        }

        if (canMoveBefore)
        {
            return beforeStopLine;
        }

        if (canMoveAfter)
        {
            return afterStopLine;
        }

        return laneS;
    }

    private bool TrySpawnOneVehicle()
    {
        int freeSlot = -1;
        int laneId = -1;

        for (int i = 0; i < slotCount; i++)
        {
            if (vehicleActive[i] ||
                vehicleRoots[i] == null)
            {
                continue;
            }

            int candidateLane = ChooseSpawnLane(i);

            if (candidateLane < 0)
            {
                continue;
            }

            freeSlot = i;
            laneId = candidateLane;
            break;
        }

        if (freeSlot < 0)
        {
            return false;
        }

        float startS = laneDatabase.spawnS[laneId];

        float speedFactor = Mathf.Lerp(
            minimumSpeedFactor,
            maximumSpeedFactor,
            NextRandom01()
        );

        ActivateVehicleAt(
            freeSlot,
            laneId,
            startS,
            speedFactor,
            spawnInitialSpeed,
            2f + NextRandom01() * 3f
        );

        return true;
    }

    private void ActivateVehicleAt(
        int vehicleIndex,
        int laneId,
        float laneS,
        float speedFactor,
        float initialSpeed,
        float initialLaneChangeCooldown)
    {
        speedFactors[vehicleIndex] = Mathf.Clamp(
            speedFactor,
            0.5f,
            1.5f
        );

        vehicleSpawnGeneration[vehicleIndex] =
            !vehicleSpawnGeneration[vehicleIndex];
        vehicleActive[vehicleIndex] = true;
        InvalidateLaneVehicleCaches();
        vehicleLaneIds[vehicleIndex] = laneId;
        signalCommittedToCross[vehicleIndex] = false;
        laneChangeActive[vehicleIndex] = false;
        InvalidateManeuverPath(vehicleIndex);
        laneChangeReverseManeuver[vehicleIndex] = false;
        laneChangeEmergencyManeuver[vehicleIndex] = false;
        laneChangePreparing[vehicleIndex] = false;
        laneChangeCurveSpeedLimits[vehicleIndex] = 0f;
        laneChangeReverseStartS[vehicleIndex] = laneS;
        laneChangeRecoveryDistance[vehicleIndex] = 0f;
        laneChangeRecoveryGearHoldRemaining[vehicleIndex] = 0f;
        laneChangeTargetLaneIds[vehicleIndex] = -1;
        laneChangeRuleIndices[vehicleIndex] = -1;
        laneChangeBranchDecisions[vehicleIndex] = 0;
        laneChangeProgress[vehicleIndex] = 0f;
        laneChangeCooldownRemaining[vehicleIndex] =
            Mathf.Max(0f, initialLaneChangeCooldown);
        laneChangeEvaluationRemaining[vehicleIndex] =
            Mathf.Max(
                0.2f,
                laneChangeEvaluationInterval
            );
        ResetVehicleObstacleState(vehicleIndex);

        float clampedS = Mathf.Clamp(
            laneS,
            laneDatabase.spawnS[laneId],
            GetLaneEndS(laneId)
        );

        vehicleS[vehicleIndex] = clampedS;
        previousVehicleS[vehicleIndex] = clampedS;

        vehicleSpeeds[vehicleIndex] = Mathf.Min(
            Mathf.Max(0f, initialSpeed),
            GetCruiseSpeed(vehicleIndex, laneId)
        );

        previousVehicleSpeeds[vehicleIndex] =
            vehicleSpeeds[vehicleIndex];
        previousLaneChangeProgress[vehicleIndex] = 0f;
        previousVehicleLaneIds[vehicleIndex] = laneId;
        previousLaneChangeActive[vehicleIndex] = false;

        vehicleAccelerations[vehicleIndex] = 0f;
        vehicleRenderMaximumS[vehicleIndex] = -1f;

        sampleHints[vehicleIndex] =
            laneDatabase.FindSampleIndex(
                laneId,
                clampedS,
                -1
            );

        activeVehicleCount++;
    }

    private int ChooseSpawnLane(int vehicleIndex)
    {
        float totalWeight = 0f;

        for (int laneId = 0;
             laneId < laneDatabase.laneCount;
             laneId++)
        {
            if (!CanSpawnOnLane(
                laneId,
                vehicleIndex
            ))
            {
                continue;
            }

            totalWeight += GetAvailableSpawnLaneWeight(laneId);
        }

        if (totalWeight <= 0f)
        {
            return -1;
        }

        float selection =
            NextRandom01() * totalWeight;

        int fallbackLane = -1;

        for (int laneId = 0;
             laneId < laneDatabase.laneCount;
             laneId++)
        {
            if (!CanSpawnOnLane(
                laneId,
                vehicleIndex
            ))
            {
                continue;
            }

            float weight = GetAvailableSpawnLaneWeight(laneId);

            if (weight <= 0f)
            {
                continue;
            }

            fallbackLane = laneId;
            selection -= weight;

            if (selection <= 0f)
            {
                return laneId;
            }
        }

        return fallbackLane;
    }

    private float GetAvailableSpawnLaneWeight(int laneId)
    {
        int population = GetSpawnLanePopulation(laneId);
        int capacity = Mathf.Max(
            1,
            maximumVehiclesPerSpawnLane
        );
        float remainingRatio = Mathf.Clamp01(
            (capacity - population) / (float)capacity
        );

        return Mathf.Max(
            0f,
            laneDatabase.spawnWeights[laneId]
        ) * remainingRatio * remainingRatio;
    }

    private int GetSpawnLanePopulation(int laneId)
    {
        int population = 0;

        for (int i = 0; i < slotCount; i++)
        {
            if (VehicleOccupiesLane(i, laneId))
            {
                population++;
            }
        }

        return population;
    }

    private bool CanSpawnOnLane(
        int laneId,
        int vehicleIndex)
    {
        if (laneId < 0 ||
            laneId >= laneDatabase.laneCount)
        {
            return false;
        }

        if ((laneDatabase.laneVehicleMasks[laneId] &
             GetVehicleMask(vehicleIndex)) == 0)
        {
            return false;
        }

        if (GetSpawnLanePopulation(laneId) >= Mathf.Max(
                1,
                maximumVehiclesPerSpawnLane
            ))
        {
            return false;
        }

        float startS = laneDatabase.spawnS[laneId];

        if (startS < 0f ||
            laneDatabase.spawnWeights[laneId] <= 0f)
        {
            return false;
        }

        float endS = GetLaneEndS(laneId);

        if (endS <= startS)
        {
            return false;
        }

        for (int i = 0; i < slotCount; i++)
        {
            if (!vehicleActive[i] ||
                vehicleLaneIds[i] != laneId)
            {
                continue;
            }

            float occupiedS = vehicleS[i];

            if (laneChangeReverseManeuver[i] &&
                vehicleLaneIds[i] == laneId)
            {
                occupiedS = Mathf.Min(
                    occupiedS,
                    laneChangeReverseStartS[i] -
                        Mathf.Max(
                            MinimumReverseRecoveryDistance,
                            laneChangeRecoveryDistance[i]
                        )
                );
            }

            float bumperGap =
                occupiedS -
                startS -
                GetCombinedHalfLength(
                    vehicleIndex,
                    i
                );

            if (bumperGap <
                GetSpawnClearance(vehicleIndex))
            {
                return false;
            }
        }

        return true;
    }

    private float GetLaneEndS(int laneId)
    {
        float endS =
            laneDatabase.despawnS[laneId];

        if (endS < 0f)
        {
            endS =
                laneDatabase.laneLengths[laneId];
        }

        return Mathf.Clamp(
            endS,
            0f,
            laneDatabase.laneLengths[laneId]
        );
    }

    private void DeactivateVehicle(int vehicleIndex)
    {
        if (!vehicleActive[vehicleIndex])
        {
            return;
        }

        if (laneChangeActive[vehicleIndex])
        {
            activeLaneChangeCount = Mathf.Max(
                0,
                activeLaneChangeCount - 1
            );
        }

        vehicleActive[vehicleIndex] = false;
        InvalidateLaneVehicleCaches();
        vehicleLaneIds[vehicleIndex] = -1;
        sampleHints[vehicleIndex] = -1;
        laneChangeActive[vehicleIndex] = false;
        InvalidateManeuverPath(vehicleIndex);
        laneChangeReverseManeuver[vehicleIndex] = false;
        laneChangeEmergencyManeuver[vehicleIndex] = false;
        laneChangePreparing[vehicleIndex] = false;
        laneChangeCurveSpeedLimits[vehicleIndex] = 0f;
        laneChangeReverseStartS[vehicleIndex] = 0f;
        laneChangeRecoveryDistance[vehicleIndex] = 0f;
        laneChangeRecoveryGearHoldRemaining[vehicleIndex] = 0f;
        laneChangeTargetLaneIds[vehicleIndex] = -1;
        laneChangeRuleIndices[vehicleIndex] = -1;
        laneChangeBranchDecisions[vehicleIndex] = 0;
        laneChangeProgress[vehicleIndex] = 0f;
        laneChangeCooldownRemaining[vehicleIndex] = 0f;
        laneChangeEvaluationRemaining[vehicleIndex] = 0f;
        ResetVehicleObstacleState(vehicleIndex);
        
        vehicleSpeeds[vehicleIndex] = 0f;
        vehicleAccelerations[vehicleIndex] = 0f;
        vehicleRenderMaximumS[vehicleIndex] = -1f;
        signalCommittedToCross[vehicleIndex] = false;

        activeVehicleCount = Mathf.Max(
            0,
            activeVehicleCount - 1
        );
    }

    private void ApplyNetworkVisuals()
    {
        if (localIsAuthority && authorityReady)
        {
            ApplyAuthorityVisuals();
            return;
        }

        if (!hasReceivedSnapshot)
        {
            return;
        }

        float delayAdjustmentSpeed =
            remoteTargetRenderDelay > remoteRenderDelay
                ? RemoteDelayAdjustmentSpeed * 4f
                : RemoteDelayAdjustmentSpeed;

        remoteRenderDelay = Mathf.MoveTowards(
            remoteRenderDelay,
            remoteTargetRenderDelay,
            Time.unscaledDeltaTime * delayAdjustmentSpeed
        );

        float renderTime =
            Time.realtimeSinceStartup - remoteRenderDelay;

        if (remoteSnapshotCount >= 3 &&
            renderTime >= snapshotNextSendTime)
        {
            PromoteBufferedSnapshotToPair();
        }

        float snapshotDuration = Mathf.Max(
            0.001f,
            snapshotNextSendTime - snapshotPreviousSendTime
        );
        float interpolation = remoteSnapshotCount <= 1
            ? 1f
            : Mathf.Clamp01(
                (renderTime - snapshotPreviousSendTime) /
                snapshotDuration
              );
        float extrapolationTime =
            remoteSnapshotCount >= 2 &&
            renderTime > snapshotNextSendTime
                ? Mathf.Min(
                    RemoteExtrapolationLimit,
                    renderTime - snapshotNextSendTime
                  )
                : 0f;

        int renderedActiveCount = 0;
        int renderedLaneChangeCount = 0;

        for (int i = 0; i < slotCount; i++)
        {
            bool useNextIdentity = interpolation >= 1f;

            bool shouldBeActive = useNextIdentity
                ? snapshotNextActive[i]
                : snapshotPreviousActive[i];

            bool spawnGenerationChanged =
                snapshotPreviousActive[i] &&
                snapshotNextActive[i] &&
                snapshotPreviousSpawnGeneration[i] !=
                    snapshotNextSpawnGeneration[i];

            bool renderSpawnGeneration = useNextIdentity
                ? snapshotNextSpawnGeneration[i]
                : snapshotPreviousSpawnGeneration[i];

            if (shouldBeActive &&
                visualActive[i] &&
                visualSpawnGeneration[i] !=
                    renderSpawnGeneration)
            {
                SetVehicleVisualActive(i, false);
            }

            SetVehicleVisualActive(i, shouldBeActive);

            if (shouldBeActive)
            {
                visualSpawnGeneration[i] =
                    renderSpawnGeneration;
            }

            if (!shouldBeActive ||
                vehicleRoots[i] == null)
            {
                continue;
            }

            renderedActiveCount++;

            int laneId = snapshotNextLaneIds[i];

            float renderS;
            float renderSpeed;
            float renderSpeedFactor;
            bool renderLaneChangeActive = false;
            bool renderReverseManeuver = false;
            bool renderEmergencyManeuver = false;
            float renderRecoveryDistance = 0f;
            int renderTargetLaneId = -1;
            float renderLaneChangeProgress = 0f;

            int completionRuleIndex = -1;

            bool completesLaneChange =
                snapshotPreviousActive[i] &&
                snapshotNextActive[i] &&
                snapshotPreviousLaneChangeActive[i] &&
                !snapshotNextLaneChangeActive[i] &&
                snapshotPreviousTargetLaneIds[i] ==
                    snapshotNextLaneIds[i] &&
                snapshotPreviousLaneIds[i] !=
                    snapshotNextLaneIds[i];

            if (completesLaneChange)
            {
                completionRuleIndex = FindLaneChangeRule(
                    snapshotPreviousLaneIds[i],
                    snapshotNextLaneIds[i]
                );
            }

            bool canInterpolate =
                snapshotPreviousActive[i] &&
                snapshotNextActive[i] &&
                !spawnGenerationChanged &&
                (snapshotPreviousLaneIds[i] ==
                    snapshotNextLaneIds[i] ||
                 completionRuleIndex >= 0);

            if (canInterpolate)
            {
                if (completionRuleIndex >= 0)
                {
                    laneId = snapshotPreviousLaneIds[i];

                    float completedSourceS =
                        MapTargetToSourceSUnclamped(
                            completionRuleIndex,
                            snapshotNextS[i]
                        );

                    renderS = Mathf.Lerp(
                        snapshotPreviousS[i],
                        completedSourceS,
                        interpolation
                    );
                }
                else
                {
                    laneId = snapshotNextLaneIds[i];

                    renderS = Mathf.Lerp(
                        snapshotPreviousS[i],
                        snapshotNextS[i],
                        interpolation
                    );
                }

                renderSpeed = Mathf.Lerp(
                    snapshotPreviousSpeeds[i],
                    snapshotNextSpeeds[i],
                    interpolation
                );

                renderSpeedFactor = Mathf.Lerp(
                    snapshotPreviousSpeedFactors[i],
                    snapshotNextSpeedFactors[i],
                    interpolation
                );

                if (completionRuleIndex >= 0)
                {
                    renderLaneChangeActive = true;
                    renderReverseManeuver =
                        snapshotPreviousReverseManeuver[i];
                    renderEmergencyManeuver =
                        snapshotPreviousEmergencyManeuver[i];
                    renderRecoveryDistance =
                        snapshotPreviousRecoveryDistance[i];
                    renderTargetLaneId =
                        snapshotNextLaneIds[i];
                    float completedSourceS =
                        MapTargetToSourceSUnclamped(
                            completionRuleIndex,
                            snapshotNextS[i]
                        );

                    renderLaneChangeProgress =
                        renderReverseManeuver
                            ? Mathf.Lerp(
                                snapshotPreviousLaneChangeProgress[i],
                                1f,
                                interpolation
                              )
                            : Mathf.Lerp(
                                snapshotPreviousLaneChangeProgress[i],
                                1f,
                                interpolation
                              );
                }
                else
                {
                    bool sameLaneChange =
                        snapshotPreviousLaneChangeActive[i] &&
                        snapshotNextLaneChangeActive[i] &&
                        snapshotPreviousTargetLaneIds[i] ==
                        snapshotNextTargetLaneIds[i];

                    if (sameLaneChange)
                    {
                        renderLaneChangeActive = true;
                        renderReverseManeuver =
                            snapshotNextReverseManeuver[i];
                        renderEmergencyManeuver =
                            snapshotNextEmergencyManeuver[i];
                        renderRecoveryDistance =
                            snapshotNextRecoveryDistance[i];
                        renderTargetLaneId =
                            snapshotNextTargetLaneIds[i];
                        renderLaneChangeProgress =
                            renderReverseManeuver
                                ? Mathf.Lerp(
                                    snapshotPreviousLaneChangeProgress[i],
                                    snapshotNextLaneChangeProgress[i],
                                    interpolation
                                  )
                                : Mathf.Lerp(
                                    snapshotPreviousLaneChangeProgress[i],
                                    snapshotNextLaneChangeProgress[i],
                                    interpolation
                                  );
                    }
                    else if (snapshotNextLaneChangeActive[i])
                    {
                        renderLaneChangeActive = true;
                        renderReverseManeuver =
                            snapshotNextReverseManeuver[i];
                        renderEmergencyManeuver =
                            snapshotNextEmergencyManeuver[i];
                        renderRecoveryDistance =
                            snapshotNextRecoveryDistance[i];
                        renderTargetLaneId =
                            snapshotNextTargetLaneIds[i];
                        renderLaneChangeProgress = Mathf.Lerp(
                            0f,
                            snapshotNextLaneChangeProgress[i],
                            interpolation
                        );
                    }
                }
            }
            else
            {
                bool usePrevious =
                    shouldBeActive &&
                    (!snapshotNextActive[i] ||
                     (spawnGenerationChanged &&
                      !useNextIdentity));

                if (usePrevious)
                {
                    laneId = snapshotPreviousLaneIds[i];
                    renderS = snapshotPreviousS[i];
                    renderSpeed =
                        snapshotPreviousSpeeds[i];
                    renderSpeedFactor =
                        snapshotPreviousSpeedFactors[i];
                    renderLaneChangeActive =
                        snapshotPreviousLaneChangeActive[i];
                    renderReverseManeuver =
                        snapshotPreviousReverseManeuver[i];
                    renderEmergencyManeuver =
                        snapshotPreviousEmergencyManeuver[i];
                    renderRecoveryDistance =
                        snapshotPreviousRecoveryDistance[i];
                    renderTargetLaneId =
                        snapshotPreviousTargetLaneIds[i];
                    renderLaneChangeProgress =
                        snapshotPreviousLaneChangeProgress[i];
                }
                else
                {
                    laneId = snapshotNextLaneIds[i];
                    renderS = snapshotNextS[i];
                    renderSpeed = snapshotNextSpeeds[i];
                    renderSpeedFactor =
                        snapshotNextSpeedFactors[i];
                    renderLaneChangeActive =
                        snapshotNextLaneChangeActive[i];
                    renderReverseManeuver =
                        snapshotNextReverseManeuver[i];
                    renderEmergencyManeuver =
                        snapshotNextEmergencyManeuver[i];
                    renderRecoveryDistance =
                        snapshotNextRecoveryDistance[i];
                    renderTargetLaneId =
                        snapshotNextTargetLaneIds[i];
                    renderLaneChangeProgress =
                        snapshotNextLaneChangeProgress[i];
                }
            }

            if (extrapolationTime > 0f &&
                snapshotNextActive[i] &&
                useNextIdentity)
            {
                float nextSpeed =
                    snapshotNextSpeeds[i];
                float nextAcceleration =
                    snapshotNextAccelerations[i];
                float predictionDuration = extrapolationTime;

                if (nextAcceleration < -0.001f)
                {
                    float timeToStop = nextSpeed /
                        -nextAcceleration;

                    predictionDuration = Mathf.Min(
                        predictionDuration,
                        timeToStop
                    );
                }

                float predictedSpeed = Mathf.Max(
                    0f,
                    nextSpeed +
                        nextAcceleration * predictionDuration
                );
                float predictedDistance = Mathf.Max(
                    0f,
                    nextSpeed * predictionDuration +
                        0.5f * nextAcceleration *
                        predictionDuration * predictionDuration
                );
                bool nextSnapshotIsReversing =
                    IsReversePhase(
                        snapshotNextReverseManeuver[i],
                        snapshotNextLaneChangeProgress[i]
                    );

                renderS += predictedDistance *
                    (nextSnapshotIsReversing ? -1f : 1f);
                renderSpeed = predictedSpeed;

                if (renderLaneChangeActive &&
                    completionRuleIndex < 0 &&
                    predictedSpeed > 0.001f)
                {
                    float progressRate = 0f;
                    bool continuesSameLaneChange =
                        snapshotPreviousLaneChangeActive[i] &&
                        snapshotNextLaneChangeActive[i] &&
                        snapshotPreviousLaneIds[i] ==
                            snapshotNextLaneIds[i] &&
                        snapshotPreviousTargetLaneIds[i] ==
                            snapshotNextTargetLaneIds[i];

                    if (continuesSameLaneChange)
                    {
                        progressRate = Mathf.Max(
                            0f,
                            (snapshotNextLaneChangeProgress[i] -
                             snapshotPreviousLaneChangeProgress[i]) /
                                snapshotDuration
                        );
                    }
                    else if (snapshotNextLaneChangeActive[i])
                    {
                        progressRate = Mathf.Max(
                            0f,
                            snapshotNextLaneChangeProgress[i] /
                                snapshotDuration
                        );
                    }

                    renderLaneChangeProgress = Mathf.Clamp01(
                        renderLaneChangeProgress +
                            progressRate * extrapolationTime
                    );
                }
            }

            int previousRenderedLaneId = vehicleLaneIds[i];
            float previousRenderedS = vehicleS[i];
            bool previousRenderValid =
                previousVisualPositionValid[i];
            bool previousLaneChangeActive = laneChangeActive[i];
            bool previousReverseManeuver =
                laneChangeReverseManeuver[i];
            bool previousEmergencyManeuver =
                laneChangeEmergencyManeuver[i];
            int previousTargetLaneId =
                laneChangeTargetLaneIds[i];
            float previousRenderedProgress =
                laneChangeProgress[i];
            float previousReverseStartS =
                laneChangeReverseStartS[i];

            bool sameRenderedLaneChange =
                previousRenderValid &&
                previousLaneChangeActive &&
                renderLaneChangeActive &&
                previousRenderedLaneId == laneId &&
                previousTargetLaneId == renderTargetLaneId &&
                previousReverseManeuver == renderReverseManeuver &&
                previousEmergencyManeuver == renderEmergencyManeuver;

            bool renderedManeuverContextChanged =
                previousLaneChangeActive != renderLaneChangeActive ||
                (renderLaneChangeActive &&
                 (!previousRenderValid ||
                  previousRenderedLaneId != laneId ||
                  previousTargetLaneId != renderTargetLaneId ||
                  previousReverseManeuver != renderReverseManeuver ||
                  previousEmergencyManeuver !=
                      renderEmergencyManeuver));

            if (renderedManeuverContextChanged)
            {
                // A later manoeuvre may use the same lane pair as the old
                // one. Do not let that new manoeuvre inherit the old fixed
                // path anchor on a remote client.
                InvalidateManeuverPath(i);
            }

            if (sameRenderedLaneChange)
            {
                float signedProgressAdvance =
                    (renderLaneChangeProgress -
                     previousRenderedProgress);

                if (signedProgressAdvance < 0f)
                {
                    renderLaneChangeProgress =
                        previousRenderedProgress;
                }
            }

            // Non-authority clients do not run the vehicle simulation arrays.
            // Populate the visual lane-change context before evaluating the
            // recovery curve so its position and body heading use the same
            // source lane, target lane, and rule as the decoded snapshot.
            vehicleLaneIds[i] = laneId;
            vehicleS[i] = renderS;
            laneChangeActive[i] = renderLaneChangeActive;
            laneChangeReverseManeuver[i] = renderReverseManeuver;
            laneChangeEmergencyManeuver[i] =
                renderEmergencyManeuver;
            laneChangePreparing[i] =
                renderLaneChangeActive &&
                !renderReverseManeuver &&
                renderLaneChangeProgress <= 0.0001f &&
                snapshotNextAccelerations[i] < -0.05f;
            laneChangeTargetLaneIds[i] = renderTargetLaneId;
            laneChangeProgress[i] = renderLaneChangeProgress;
            laneChangeRuleIndices[i] = renderLaneChangeActive
                ? FindLaneChangeRule(laneId, renderTargetLaneId)
                : -1;
            laneChangeRecoveryDistance[i] = renderReverseManeuver
                ? Mathf.Max(
                    MinimumReverseRecoveryDistance,
                    renderRecoveryDistance > 0f
                        ? renderRecoveryDistance
                        : laneChangeReverseDistance
                  )
                : 0f;

            if (renderLaneChangeActive &&
                renderReverseManeuver &&
                renderLaneChangeProgress <=
                    GetRecoveryPreparationEndProgress() + 0.0001f)
            {
                float recoveryDistance = Mathf.Max(
                    MinimumReverseRecoveryDistance,
                    laneChangeRecoveryDistance[i] > 0f
                        ? laneChangeRecoveryDistance[i]
                        : laneChangeReverseDistance
                );
                float nextRecoveryStartS =
                    snapshotNextS[i] -
                    GetRecoveryLongitudinalOffset(
                        i,
                        snapshotNextLaneChangeProgress[i],
                        recoveryDistance
                    );
                float recoveryStartS = nextRecoveryStartS;
                bool sameRecoveryInBothSnapshots =
                    canInterpolate &&
                    snapshotPreviousLaneChangeActive[i] &&
                    snapshotNextLaneChangeActive[i] &&
                    snapshotPreviousReverseManeuver[i] &&
                    snapshotNextReverseManeuver[i] &&
                    snapshotPreviousLaneIds[i] ==
                        snapshotNextLaneIds[i] &&
                    snapshotPreviousTargetLaneIds[i] ==
                        snapshotNextTargetLaneIds[i];

                if (sameRecoveryInBothSnapshots)
                {
                    float previousRecoveryStartS =
                        snapshotPreviousS[i] -
                        GetRecoveryLongitudinalOffset(
                            i,
                            snapshotPreviousLaneChangeProgress[i],
                            recoveryDistance
                        );

                    recoveryStartS = Mathf.Lerp(
                        previousRecoveryStartS,
                        nextRecoveryStartS,
                        interpolation
                    );
                }
                else if (!snapshotNextReverseManeuver[i] &&
                         snapshotPreviousReverseManeuver[i])
                {
                    recoveryStartS =
                        snapshotPreviousS[i] -
                        GetRecoveryLongitudinalOffset(
                            i,
                            snapshotPreviousLaneChangeProgress[i],
                            recoveryDistance
                        );
                }

                renderS = recoveryStartS +
                    GetRecoveryLongitudinalOffset(
                        i,
                        renderLaneChangeProgress,
                        recoveryDistance
                    );

                laneChangeReverseStartS[i] = recoveryStartS;
                vehicleS[i] = renderS;
            }
            else
            {
                laneChangeReverseStartS[i] = renderS;
            }

            bool renderingInReverse =
                IsReversePhase(
                    renderReverseManeuver,
                    renderLaneChangeProgress
                );

            if (previousRenderValid &&
                previousRenderedLaneId == laneId)
            {
                float signedRenderedAdvance =
                    (renderS - previousRenderedS) *
                    (renderingInReverse ? -1f : 1f);

                if (signedRenderedAdvance < 0f)
                {
                    // Never visibly rewind a remote car for a late or
                    // quantized snapshot. Hold the complete path pose until
                    // authority catches up; advancing lateral progress while
                    // only S is held makes the body pivot and slide sideways.
                    renderS = previousRenderedS;

                    if (sameRenderedLaneChange)
                    {
                        renderLaneChangeProgress =
                            previousRenderedProgress;

                        if (renderReverseManeuver)
                        {
                            laneChangeReverseStartS[i] =
                                previousReverseStartS;
                        }
                    }
                    else if (renderLaneChangeActive &&
                             !previousLaneChangeActive)
                    {
                        // Do not begin a newly received manoeuvre with a
                        // lateral-only correction.
                        renderLaneChangeProgress = 0f;
                    }
                }
            }

            vehicleS[i] = renderS;
            vehicleSpeeds[i] = renderSpeed;
            vehicleAccelerations[i] = Mathf.Lerp(
                snapshotPreviousAccelerations[i],
                snapshotNextAccelerations[i],
                interpolation
            );
            speedFactors[i] = renderSpeedFactor;
            laneChangeProgress[i] = renderLaneChangeProgress;
            laneChangePreparing[i] =
                renderLaneChangeActive &&
                !renderReverseManeuver &&
                renderLaneChangeProgress <= 0.0001f &&
                snapshotNextAccelerations[i] < -0.05f;

            ApplyVehiclePose(
                i,
                laneId,
                renderS,
                renderLaneChangeActive,
                renderReverseManeuver,
                renderTargetLaneId,
                renderLaneChangeProgress,
                renderSpeed
            );

            if (renderLaneChangeActive)
            {
                renderedLaneChangeCount++;
            }

            ApplyVehicleMotionVisuals(
                i,
                laneId,
                renderSpeed,
                renderSpeedFactor
            );
        }

        if (!localIsAuthority)
        {
            activeVehicleCount = renderedActiveCount;
            activeLaneChangeCount =
                renderedLaneChangeCount;
        }
    }

    private void ApplyAuthorityVisuals()
    {
        float stepDuration = Mathf.Clamp(
            simulationInterval,
            0.02f,
            0.25f
        );
        float stateInterpolation = Mathf.Clamp01(
            simulationAccumulator / stepDuration
        );

        for (int i = 0; i < slotCount; i++)
        {
            bool shouldBeActive = vehicleActive[i];

            if (shouldBeActive &&
                visualActive[i] &&
                visualSpawnGeneration[i] !=
                    vehicleSpawnGeneration[i])
            {
                SetVehicleVisualActive(i, false);
            }

            SetVehicleVisualActive(i, shouldBeActive);

            if (!shouldBeActive ||
                vehicleRoots[i] == null)
            {
                continue;
            }

            visualSpawnGeneration[i] =
                vehicleSpawnGeneration[i];

            int laneId = vehicleLaneIds[i];

            if (laneId < 0 ||
                laneId >= laneDatabase.laneCount)
            {
                continue;
            }

            bool sameLaneState =
                previousVehicleLaneIds[i] == laneId;
            bool continuousManeuverState =
                !previousLaneChangeActive[i] ||
                laneChangeActive[i];
            bool interpolateState =
                sameLaneState && continuousManeuverState;
            float renderSpeed = Mathf.Max(
                0f,
                interpolateState
                    ? Mathf.Lerp(
                        previousVehicleSpeeds[i],
                        vehicleSpeeds[i],
                        stateInterpolation
                      )
                    : vehicleSpeeds[i]
            );
            float renderProgress =
                laneChangeProgress[i];
            bool renderCachedManeuver =
                laneChangeActive[i] &&
                !laneChangePreparing[i] &&
                IsManeuverPathUsable(i);
            float renderS = vehicleS[i];

            if (interpolateState && renderCachedManeuver)
            {
                float previousProgress =
                    previousLaneChangeActive[i]
                        ? previousLaneChangeProgress[i]
                        : 0f;
                renderProgress = Mathf.Lerp(
                    previousProgress,
                    laneChangeProgress[i],
                    stateInterpolation
                );
                renderS = SampleManeuverPathSourceS(
                    i,
                    renderProgress
                );
            }
            else if (interpolateState)
            {
                renderS = Mathf.Lerp(
                    previousVehicleS[i],
                    vehicleS[i],
                    stateInterpolation
                );
            }

            renderS = Mathf.Clamp(
                renderS,
                0f,
                laneDatabase.laneLengths[laneId]
            );

            ApplyVehiclePose(
                i,
                laneId,
                renderS,
                laneChangeActive[i],
                laneChangeReverseManeuver[i],
                laneChangeTargetLaneIds[i],
                renderProgress,
                renderSpeed
            );

            ApplyVehicleMotionVisuals(
                i,
                laneId,
                renderSpeed,
                speedFactors[i]
            );
        }
    }

    private void SetVehicleVisualActive(
        int vehicleIndex,
        bool active)
    {
        if (visualActive[vehicleIndex] == active)
        {
            return;
        }

        visualActive[vehicleIndex] = active;
        previousVisualPositionValid[vehicleIndex] = false;
        vehicleCollisionVelocities[vehicleIndex] = Vector3.zero;

        if (!active)
        {
            InvalidateManeuverPath(vehicleIndex);
        }

        Transform vehicleRoot =
            vehicleRoots[vehicleIndex];

        if (vehicleRoot == null)
        {
            return;
        }

        if (active)
        {
            ResetVehicleMotionVisuals(vehicleIndex);
            vehicleRoot.gameObject.SetActive(true);

            AudioSource audioSource =
                vehicleAudioSources[vehicleIndex];

            if (audioSource != null &&
                !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
        else
        {
            ResetVehicleMotionVisuals(vehicleIndex);
            vehicleRoot.localScale =
                baseLocalScales[vehicleIndex];
            vehicleRoot.gameObject.SetActive(false);
        }
    }

    private void CacheVehicleMotionVisuals(
        int vehicleIndex,
        Transform vehicleRoot)
    {
        AudioSource audioSource =
            vehicleRoot.GetComponentInChildren<AudioSource>(true);

        vehicleAudioSources[vehicleIndex] =
            audioSource;

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.volume = 0f;
            audioSource.pitch =
                GetIdleEnginePitch(vehicleIndex);
            audioSource.Stop();
        }

        Transform[] childTransforms =
            vehicleRoot.GetComponentsInChildren<Transform>(true);

        int wheelOffset = vehicleIndex * 4;

        for (int i = 0;
             i < childTransforms.Length;
             i++)
        {
            Transform child = childTransforms[i];

            if (child == null)
            {
                continue;
            }

            int wheelIndex = -1;
            string childName = child.name;

            if (childName == "Wheel_FL")
            {
                wheelIndex = 0;
            }
            else if (childName == "Wheel_FR" ||
                     childName == "Wheel_FB")
            {
                // Serena의 Wheel_FB 오타를 오른쪽 앞바퀴로 취급
                wheelIndex = 1;
            }
            else if (childName == "Wheel_BL")
            {
                wheelIndex = 2;
            }
            else if (childName == "Wheel_BR")
            {
                wheelIndex = 3;
            }

            if (wheelIndex < 0)
            {
                continue;
            }

            int flatIndex = wheelOffset + wheelIndex;

            vehicleWheelTransforms[flatIndex] = child;
            wheelBaseLocalRotations[flatIndex] =
                child.localRotation;
        }
    }

    private void ResetVehicleMotionVisuals(
        int vehicleIndex)
    {
        wheelAngles[vehicleIndex] = 0f;
        wheelTravelDistanceDeltas[vehicleIndex] = 0f;
        frontWheelSteeringAngles[vehicleIndex] = 0f;

        AudioSource audioSource =
            vehicleAudioSources[vehicleIndex];

        if (audioSource != null)
        {
            audioSource.volume = 0f;
            audioSource.pitch =
                GetIdleEnginePitch(vehicleIndex);
            audioSource.Stop();
        }

        int wheelOffset = vehicleIndex * 4;

        for (int i = 0; i < 4; i++)
        {
            int flatIndex = wheelOffset + i;
            Transform wheel =
                vehicleWheelTransforms[flatIndex];

            if (wheel != null)
            {
                wheel.localRotation =
                    wheelBaseLocalRotations[flatIndex];
            }
        }
    }

    private void UpdateMotionVisualRefreshState(float deltaTime)
    {
        audioVisualUpdateTimer -= Mathf.Max(0f, deltaTime);
        refreshAudioVisualsThisFrame =
            audioVisualUpdateTimer <= 0f;

        if (refreshAudioVisualsThisFrame)
        {
            audioVisualUpdateTimer = Mathf.Max(
                0.05f,
                audioVisualUpdateInterval
            );
        }
    }

    private void ApplyVehicleMotionVisuals(
        int vehicleIndex,
        int laneId,
        float renderSpeed,
        float renderSpeedFactor)
    {
        if (refreshAudioVisualsThisFrame)
        {
            float referenceSpeed = Mathf.Max(
                0.1f,
                GetBaseCruiseSpeed(vehicleIndex) *
                renderSpeedFactor
            );

            if (laneId >= 0 &&
                laneId < laneDatabase.laneCount &&
                laneDatabase.speedLimits[laneId] > 0f)
            {
                referenceSpeed = Mathf.Min(
                    referenceSpeed,
                    laneDatabase.speedLimits[laneId]
                );
            }

            float speedRatio = Mathf.Clamp01(
                renderSpeed / referenceSpeed
            );

            AudioSource audioSource =
                vehicleAudioSources[vehicleIndex];

            if (audioSource != null)
            {
                audioSource.volume =
                    Mathf.Clamp01(
                        GetMaximumEngineVolume(vehicleIndex)
                    ) *
                    speedRatio;

                audioSource.pitch = Mathf.Lerp(
                    GetIdleEnginePitch(vehicleIndex),
                    GetMaximumEnginePitch(vehicleIndex),
                    speedRatio
                );

                if (!audioSource.isPlaying)
                {
                    audioSource.Play();
                }
            }
        }

        float safeWheelRadius = Mathf.Max(
            0.05f,
            GetWheelRadius(vehicleIndex)
        );

        float angleDelta =
            wheelTravelDistanceDeltas[vehicleIndex] /
            safeWheelRadius *
            Mathf.Rad2Deg;

        wheelAngles[vehicleIndex] = Mathf.Repeat(
            wheelAngles[vehicleIndex] + angleDelta,
            360f
        );

        Quaternion wheelRotation =
            Quaternion.AngleAxis(
                wheelAngles[vehicleIndex],
                Vector3.right
            );
        float centerSteering =
            frontWheelSteeringAngles[vehicleIndex];
        float leftSteering = centerSteering;
        float rightSteering = centerSteering;

        if (Mathf.Abs(centerSteering) > 0.01f)
        {
            float wheelBase = Mathf.Max(
                2.2f,
                GetVehicleLength(vehicleIndex) * 0.55f
            );
            float trackWidth = Mathf.Max(
                1f,
                GetVehicleWidth(vehicleIndex) * 0.78f
            );
            float turnRadius = wheelBase /
                Mathf.Max(
                    0.001f,
                    Mathf.Tan(
                        Mathf.Abs(centerSteering) *
                        Mathf.Deg2Rad
                    )
                );
            float innerAngle = Mathf.Rad2Deg * Mathf.Atan(
                wheelBase /
                Mathf.Max(0.1f, turnRadius - trackWidth * 0.5f)
            );
            float outerAngle = Mathf.Rad2Deg * Mathf.Atan(
                wheelBase /
                (turnRadius + trackWidth * 0.5f)
            );
            float maximum = Mathf.Clamp(
                maximumFrontWheelSteeringAngle + 6f,
                20f,
                45f
            );
            innerAngle = Mathf.Min(innerAngle, maximum);
            outerAngle = Mathf.Min(outerAngle, maximum);

            if (centerSteering > 0f)
            {
                leftSteering = outerAngle;
                rightSteering = innerAngle;
            }
            else
            {
                leftSteering = -innerAngle;
                rightSteering = -outerAngle;
            }
        }

        Quaternion leftSteeringRotation =
            Quaternion.AngleAxis(leftSteering, Vector3.up);
        Quaternion rightSteeringRotation =
            Quaternion.AngleAxis(rightSteering, Vector3.up);

        int wheelOffset = vehicleIndex * 4;

        for (int i = 0; i < 4; i++)
        {
            int flatIndex = wheelOffset + i;
            Transform wheel =
                vehicleWheelTransforms[flatIndex];

            if (wheel != null)
            {
                Quaternion steeringRotation = i == 0
                    ? leftSteeringRotation
                    : rightSteeringRotation;

                wheel.localRotation = i < 2
                    ? steeringRotation *
                        wheelBaseLocalRotations[flatIndex] *
                        wheelRotation
                    : wheelBaseLocalRotations[flatIndex] *
                        wheelRotation;
            }
        }
    }

    private void UpdateFrontWheelSteeringVisual(
        int vehicleIndex,
        Vector3 position,
        Quaternion rotation,
        Transform vehicleRoot,
        bool previousPoseValid,
        bool laneChangeIsActive,
        float laneChangeProgress,
        bool reversingNow,
        float renderSpeed)
    {
        if (vehicleIndex < 0 ||
            vehicleIndex >= frontWheelSteeringAngles.Length ||
            vehicleRoot == null)
        {
            return;
        }

        float currentSteering =
            frontWheelSteeringAngles[vehicleIndex];
        float targetSteering = currentSteering;
        Vector3 movement = previousPoseValid
            ? position - previousVisualPositions[vehicleIndex]
            : Vector3.zero;
        float travelDistance = movement.magnitude;
        float minimumSteeringTravel = 0.001f;

        if (laneChangeIsActive)
        {
            targetSteering = IsManeuverPathUsable(vehicleIndex)
                ? SampleManeuverPathSteering(
                    vehicleIndex,
                    laneChangeProgress
                  )
                : 0f;
        }
        else if (previousPoseValid &&
            travelDistance > minimumSteeringTravel &&
            renderSpeed > 0.02f)
        {
            Vector3 surfaceUp = rotation * Vector3.up;

            if (surfaceUp.sqrMagnitude <= 0.0001f)
            {
                surfaceUp = Vector3.up;
            }
            else
            {
                surfaceUp.Normalize();
            }

            Vector3 previousForward = Vector3.ProjectOnPlane(
                vehicleRoot.rotation * Vector3.forward,
                surfaceUp
            );
            Vector3 currentForward = Vector3.ProjectOnPlane(
                rotation * Vector3.forward,
                surfaceUp
            );

            if (previousForward.sqrMagnitude > 0.0001f &&
                currentForward.sqrMagnitude > 0.0001f)
            {
                previousForward.Normalize();
                currentForward.Normalize();

                float yawRadians = Mathf.Deg2Rad *
                    Vector3.SignedAngle(
                        previousForward,
                        currentForward,
                        surfaceUp
                    );
                float signedTravelDistance =
                    reversingNow
                        ? -travelDistance
                        : travelDistance;
                float curvature = yawRadians /
                    signedTravelDistance;
                float wheelBase = Mathf.Max(
                    2.2f,
                    GetVehicleLength(vehicleIndex) * 0.55f
                );

                targetSteering = Mathf.Rad2Deg * Mathf.Atan(
                    wheelBase * curvature
                );
                targetSteering = Mathf.Clamp(
                    targetSteering,
                    -Mathf.Clamp(
                        maximumFrontWheelSteeringAngle,
                        20f,
                        40f
                    ),
                    Mathf.Clamp(
                        maximumFrontWheelSteeringAngle,
                        20f,
                        40f
                    )
                );
            }
        }
        else
        {
            targetSteering = 0f;
        }

        frontWheelSteeringAngles[vehicleIndex] =
            Mathf.MoveTowards(
                currentSteering,
                targetSteering,
                Mathf.Clamp(
                    frontWheelSteeringResponse,
                    90f,
                    360f
                ) *
                Mathf.Min(Time.deltaTime, 0.1f)
            );
    }

    private void ApplyVehiclePose(
        int vehicleIndex,
        int laneId,
        float renderS,
        bool renderLaneChangeActive,
        bool renderReverseManeuver,
        int targetLaneId,
        float changeProgress,
        float renderSpeed)
    {
        if (laneId < 0 ||
            laneId >= laneDatabase.laneCount)
        {
            return;
        }

        int sampleIndex =
            laneDatabase.FindSampleIndex(
                laneId,
                renderS,
                sampleHints[vehicleIndex]
            );

        if (sampleIndex < 0)
        {
            return;
        }

        sampleHints[vehicleIndex] = sampleIndex;

        int lastSample =
            laneDatabase.laneSampleStarts[laneId] +
            laneDatabase.laneSampleCounts[laneId] -
            1;

        int nextSample = Mathf.Min(
            sampleIndex + 1,
            lastSample
        );

        float startDistance =
            laneDatabase.sampleDistances[sampleIndex];

        float endDistance =
            laneDatabase.sampleDistances[nextSample];

        float segmentLength =
            endDistance - startDistance;

        float segmentInterpolation = 0f;

        if (segmentLength > 0.0001f)
        {
            segmentInterpolation =
                Mathf.Clamp01(
                    (renderS - startDistance) /
                    segmentLength
                );
        }

        Vector3 position = Vector3.Lerp(
            laneDatabase.samplePositions[sampleIndex],
            laneDatabase.samplePositions[nextSample],
            segmentInterpolation
        );

        Quaternion rotation = Quaternion.Slerp(
            laneDatabase.sampleRotations[sampleIndex],
            laneDatabase.sampleRotations[nextSample],
            segmentInterpolation
        );

        if (renderLaneChangeActive &&
            targetLaneId >= 0 &&
            targetLaneId < laneDatabase.laneCount)
        {
            int ruleIndex = FindLaneChangeRule(
                laneId,
                targetLaneId
            );

            if (ruleIndex >= 0)
            {
                EnsureManeuverPath(
                    vehicleIndex,
                    renderS,
                    changeProgress
                );

                if (IsManeuverPathUsable(vehicleIndex))
                {
                    position = SampleManeuverPathPosition(
                        vehicleIndex,
                        changeProgress
                    );
                    rotation = SampleManeuverPathRotation(
                        vehicleIndex,
                        changeProgress
                    );
                }
            }
        }

        Transform vehicleRoot =
            vehicleRoots[vehicleIndex];
        bool previousPoseValid =
            previousVisualPositionValid[vehicleIndex];
        bool renderingInReverse =
            IsReversePhase(
                renderReverseManeuver,
                changeProgress
            );
        float visualTravelDistance = previousPoseValid
            ? Vector3.Distance(
                position,
                previousVisualPositions[vehicleIndex]
              )
            : 0f;

        wheelTravelDistanceDeltas[vehicleIndex] =
            renderingInReverse
                ? -visualTravelDistance
                : visualTravelDistance;
        UpdateFrontWheelSteeringVisual(
            vehicleIndex,
            position,
            rotation,
            vehicleRoot,
            previousPoseValid,
            renderLaneChangeActive,
            changeProgress,
            renderingInReverse,
            renderSpeed
        );

        previousVisualPositions[vehicleIndex] = position;
        previousVisualPositionValid[vehicleIndex] = true;

        position +=
            rotation *
            Vector3.up *
            heightOffset;

        Vector3 previousRootPosition = vehicleRoot.position;

        float collisionDeltaTime = Mathf.Max(
            0.0001f,
            Mathf.Min(Time.deltaTime, 0.1f)
        );

        if (previousPoseValid)
        {
            vehicleCollisionVelocities[vehicleIndex] =
                (position - previousRootPosition) /
                collisionDeltaTime;
        }
        else
        {
            vehicleCollisionVelocities[vehicleIndex] =
                rotation * Vector3.forward *
                Mathf.Max(0f, renderSpeed);
        }

        vehicleRoot.SetPositionAndRotation(
            position,
            rotation
        );

        ApplyVehicleScale(
            vehicleIndex,
            laneId,
            renderS
        );
    }

    private bool IsReversePhase(
        bool reverseManeuver,
        float progress)
    {
        if (!reverseManeuver)
        {
            return false;
        }

        return progress <
            GetRecoveryFirstReverseEndProgress() - 0.0001f;
    }

    private float GetLaneChangeVisualProgress(
        int vehicleIndex,
        bool reverseManeuver,
        float progress)
    {
        if (!reverseManeuver)
        {
            return SmoothLaneChange01(progress);
        }

        float preparationEnd =
            GetRecoveryPreparationEndProgress();
        float recoveryDistance = Mathf.Max(
            MinimumReverseRecoveryDistance,
            laneChangeRecoveryDistance[vehicleIndex] > 0f
                ? laneChangeRecoveryDistance[vehicleIndex]
                : laneChangeReverseDistance
        );
        Vector3 preparationPose =
            GetRecoveryKinematicLocalPose(
                vehicleIndex,
                Mathf.Min(progress, preparationEnd),
                recoveryDistance
            );
        float laneWidth = GetLaneChangeSeparation(
            vehicleIndex,
            laneChangeReverseStartS[vehicleIndex]
        );

        if (progress < preparationEnd)
        {
            return preparationPose.y / laneWidth;
        }

        float startLateral = preparationPose.y / laneWidth;
        float finalTravelDistance =
            GetRecoveryFinalTravelDistance(
                preparationPose,
                laneWidth
            );
        float startTangent =
            Mathf.Tan(preparationPose.z) *
            finalTravelDistance / laneWidth;
        float u = Mathf.InverseLerp(
            preparationEnd,
            1f,
            progress
        );
        float u2 = u * u;
        float u3 = u2 * u;

        return Mathf.Clamp(
            (2f * u3 - 3f * u2 + 1f) * startLateral +
            (u3 - 2f * u2 + u) * startTangent +
            (-2f * u3 + 3f * u2),
            -0.5f,
            1.1f
        );
    }

    private float SmoothLaneChange01(float t)
    {
        float clampedT = Mathf.Clamp01(t);

        return clampedT * clampedT * clampedT *
            (clampedT *
             (clampedT * 6f - 15f) + 10f);
    }

    private void ApplyVehicleScale(
        int vehicleIndex,
        int laneId,
        float renderS)
    {
        float startS =
            laneDatabase.spawnS[laneId];

        float endS =
            GetLaneEndS(laneId);

        float spawnFactor = 1f;

        if (spawnVisualDistance > 0.01f)
        {
            spawnFactor = Mathf.InverseLerp(
                startS,
                startS + spawnVisualDistance,
                renderS
            );
        }

        float despawnFactor = 1f;

        if (despawnVisualDistance > 0.01f)
        {
            despawnFactor = Mathf.InverseLerp(
                endS,
                endS - despawnVisualDistance,
                renderS
            );
        }

        float visibility = Mathf.Clamp01(
            Mathf.Min(
                spawnFactor,
                despawnFactor
            )
        );

        visibility =
            visibility *
            visibility *
            (3f - 2f * visibility);

        vehicleRoots[vehicleIndex].localScale =
            baseLocalScales[vehicleIndex] *
            visibility;
    }

    private float NextRandom01()
    {
        randomState =
            (randomState * 25173 + 13849) &
            65535;

        return randomState / 65535f;
    }
}
