
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// 교통 차량과 패널의 충돌을 감지하고 충돌 속도에 따라 반동 또는 별 비행 연출을 적용한다.
/// </summary>
/// <remarks>
/// 물리 반응은 현재 소유권자만 결정한다. 고속 충돌의 표시 결과는 네트워크 호출로
/// 전달하지만 파티클 자체는 각 클라이언트에서 로컬로 재생한다.
/// </remarks>
public class PanelFly : UdonSharpBehaviour
{
    [Header("Collision Response")]
    public float bounceForce = 25f;

    [Tooltip("이 속도 이상의 정면 충돌은 실제 패널 대신 별이 되는 로컬 3D 파티클을 재생합니다.")]
    public float highImpactThresholdKmh = 18f;

    [Tooltip("고속 연출이 시작된 뒤 같은 패널에서 다시 고속 충돌을 받지 않는 시간입니다.")]
    public float highImpactCooldown = 2.5f;

    [Tooltip("Transform으로 이동하는 중앙 교통 차량과의 겹침을 확인하는 간격입니다.")]
    public float vehicleProbeInterval = 0.08f;

    [Tooltip("한 차량이 패널을 통과하는 동안 같은 충돌을 반복 처리하지 않는 시간입니다.")]
    public float repeatedImpactCooldown = 0.4f;

    [Tooltip("검사 사이에 차량이 이동한 구간을 보완하는 최소 여유 거리입니다.")]
    public float vehicleProbeSweepPadding = 0.1f;

    [Tooltip("한 번의 검사에서 보완할 최대 차량 이동 거리입니다.")]
    public float maximumVehicleProbeSweep = 1.2f;

    [Header("High Impact Effect")]
    public TrafficSimulationManager trafficManager;
    public ParticleSystem starPanelParticle;
    public ParticleSystem starFlashParticle;
    [Tooltip("등신대가 솟구칠 때 남기는 약한 수평 이동 속도입니다.")]
    public float starHorizontalSpeed = 3.5f;

    [Tooltip("등신대가 화면 위의 하늘까지 올라가는 수직 속도입니다.")]
    public float starUpwardSpeed = 28f;

    public float starFlightDuration = 2.6f;
    [Tooltip("고속 충돌 뒤 실제 등신대를 숨겨 두는 시간입니다. 별 비행 시간보다 짧게 설정해도 별 비행이 끝날 때까지는 나타나지 않습니다.")]
    public float highImpactHiddenDuration = 2.6f;
    public float starFlashSize = 2.25f;

    private Rigidbody rb;
    private VRCPickup pickup;
    private VRCObjectSync objectSync;
    private BoxCollider panelCollider;
    private Renderer[] panelRenderers = new Renderer[0];
    private bool[] initialRendererEnabled = new bool[0];
    private Renderer starPanelParticleRenderer;
    private Renderer starFlashParticleRenderer;
    private Collider[] panelColliders = new Collider[0];
    private bool[] initialColliderEnabled = new bool[0];
    private Collider[] vehicleProbeResults = new Collider[8];
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool initialRigidbodyKinematic;
    private float nextHighImpactTime;
    private float nextVehicleProbeTime;
    private float nextImpactTime;
    private Vector3 pendingStarPosition;
    private int starTwinkleStep;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        pickup = GetComponent<VRCPickup>();
        objectSync = GetComponent<VRCObjectSync>();
        panelCollider = GetComponent<BoxCollider>();
        if (starPanelParticle != null)
        {
            starPanelParticleRenderer =
                starPanelParticle.GetComponent<Renderer>();
        }

        if (starFlashParticle != null)
        {
            starFlashParticleRenderer =
                starFlashParticle.GetComponent<Renderer>();
        }

        panelRenderers = GetComponentsInChildren<Renderer>(true);
        initialRendererEnabled = new bool[panelRenderers.Length];
        for (int i = 0; i < panelRenderers.Length; i++)
        {
            Renderer panelRenderer = panelRenderers[i];
            initialRendererEnabled[i] = panelRenderer != null &&
                panelRenderer.enabled;
        }

        panelColliders = GetComponentsInChildren<Collider>(true);
        initialColliderEnabled = new bool[panelColliders.Length];
        for (int i = 0; i < panelColliders.Length; i++)
        {
            Collider childCollider = panelColliders[i];
            initialColliderEnabled[i] = childCollider != null &&
                childCollider.enabled;
        }

        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialRigidbodyKinematic = rb != null && rb.isKinematic;
    }

    void Update()
    {
        if (Time.time < nextVehicleProbeTime)
        {
            return;
        }

        float probeInterval = Mathf.Max(0.04f, vehicleProbeInterval);
        nextVehicleProbeTime = Time.time + probeInterval;

        if (Time.time < nextImpactTime ||
            !Networking.IsOwner(gameObject) ||
            trafficManager == null ||
            panelCollider == null ||
            !panelCollider.enabled ||
            !gameObject.activeInHierarchy)
        {
            return;
        }

        Vector3 scale = transform.lossyScale;
        Vector3 halfExtents = Vector3.Scale(
            panelCollider.size * 0.5f,
            new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            )
        );

        // 화면상 떨어진 차량까지 충돌로 보지 않으면서 프레임 사이의 접촉 누락만
        // 보완할 수 있도록 매우 작은 여유를 더한다.
        halfExtents += new Vector3(0.015f, 0.015f, 0.015f);

        Vector3 probeCenter =
            transform.TransformPoint(panelCollider.center);
        int hitCount = Physics.OverlapBoxNonAlloc(
            probeCenter,
            halfExtents,
            vehicleProbeResults,
            transform.rotation,
            1 << 24,
            QueryTriggerInteraction.Collide
        );
        bool requiresSweepTest = hitCount == 0;

        if (requiresSweepTest)
        {
            float maximumSweep = Mathf.Max(
                0.1f,
                maximumVehicleProbeSweep
            );
            hitCount = Physics.OverlapBoxNonAlloc(
                probeCenter,
                halfExtents + Vector3.one * maximumSweep,
                vehicleProbeResults,
                transform.rotation,
                1 << 24,
                QueryTriggerInteraction.Collide
            );
        }

        Vector3 bestVelocity = Vector3.zero;
        Vector3 bestVehiclePosition = Vector3.zero;
        float bestSpeedSquared = 0f;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = vehicleProbeResults[i];
            if (hit == null)
            {
                continue;
            }

            Vector3 velocity =
                trafficManager.GetCollisionVehicleVelocity(hit.transform);
            float speedSquared = velocity.sqrMagnitude;
            if (speedSquared <= bestSpeedSquared)
            {
                continue;
            }

            if (requiresSweepTest)
            {
                float speed = Mathf.Sqrt(speedSquared);
                float castDistance = Mathf.Clamp(
                    speed * (probeInterval + 0.025f) +
                        Mathf.Max(0f, vehicleProbeSweepPadding),
                    0.05f,
                    Mathf.Max(0.1f, maximumVehicleProbeSweep)
                );
                Vector3 castDirection = velocity / speed;
                if (Vector3.Dot(
                        hit.bounds.center - probeCenter,
                        castDirection
                    ) < 0f)
                {
                    castDirection = -castDirection;
                }

                RaycastHit sweepHit;
                if (!Physics.BoxCast(
                        probeCenter,
                        halfExtents + Vector3.one * 0.01f,
                        castDirection,
                        out sweepHit,
                        transform.rotation,
                        castDistance,
                        1 << 24,
                        QueryTriggerInteraction.Collide
                    ))
                {
                    continue;
                }
            }

            bestSpeedSquared = speedSquared;
            bestVelocity = velocity;
            bestVehiclePosition = hit.bounds.center;
        }

        if (bestSpeedSquared > 0.0001f)
        {
            HandleVehicleImpact(
                bestVelocity,
                bestVehiclePosition,
                Vector3.zero,
                false
            );
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer != 24 ||
            !Networking.IsOwner(gameObject) ||
            Time.time < nextImpactTime)
        {
            return;
        }

        Vector3 vehicleVelocity = Vector3.zero;
        if (trafficManager != null)
        {
            vehicleVelocity = trafficManager.GetCollisionVehicleVelocity(
                collision.transform
            );
        }

        if (vehicleVelocity.sqrMagnitude < 0.0001f)
        {
            vehicleVelocity = -collision.relativeVelocity;
        }

        Vector3 contactNormal = Vector3.zero;
        bool hasContactNormal = collision.contactCount > 0;
        if (hasContactNormal)
        {
            contactNormal = collision.GetContact(0).normal;
        }

        HandleVehicleImpact(
            vehicleVelocity,
            collision.transform.position,
            contactNormal,
            hasContactNormal
        );
    }

    private void HandleVehicleImpact(
        Vector3 vehicleVelocity,
        Vector3 vehiclePosition,
        Vector3 contactNormal,
        bool hasContactNormal)
    {
        if (Time.time < nextImpactTime)
        {
            return;
        }

        nextImpactTime = Time.time +
            Mathf.Max(0.1f, repeatedImpactCooldown);

        // 플레이어가 패널을 든 상태에서도 겹침 검사가 실행되므로 힘을 적용하거나
        // 원위치로 돌리기 전에 Pickup을 놓게 한다.
        if (pickup != null)
        {
            pickup.Drop();
        }

        float collisionSpeed = vehicleVelocity.magnitude;
        if (collisionSpeed < 0.01f)
        {
            return;
        }

        float impactSpeed = collisionSpeed;
        if (hasContactNormal)
        {
            impactSpeed = Mathf.Abs(Vector3.Dot(
                vehicleVelocity,
                contactNormal.normalized
            ));
        }
        else
        {
            Vector3 approachDirection =
                transform.position - vehiclePosition;
            approachDirection.y = 0f;
            if (approachDirection.sqrMagnitude > 0.0001f)
            {
                impactSpeed = Mathf.Abs(Vector3.Dot(
                    vehicleVelocity,
                    approachDirection.normalized
                ));
            }
        }

        float highImpactThreshold =
            highImpactThresholdKmh / 3.6f;

        if (Time.time >= nextHighImpactTime &&
            impactSpeed >= highImpactThreshold)
        {
            nextHighImpactTime = Time.time + highImpactCooldown;

            float directionX = vehicleVelocity.x;
            if (Mathf.Abs(directionX) < 0.01f)
            {
                directionX = transform.position.x -
                    vehiclePosition.x;
            }

            Vector3 launchDirection = directionX >= 0f
                ? Vector3.right
                : Vector3.left;
            Vector3 launchPosition = transform.position;
            Vector3 launchVelocity =
                launchDirection * starHorizontalSpeed +
                Vector3.up * starUpwardSpeed;
            Quaternion launchRotation = transform.rotation;

            HidePanelForHighImpact();

            // 숨긴 실제 패널 대신 같은 외형의 파티클이 날아간다.
            EmitStarPanel(
                launchPosition,
                launchVelocity,
                launchRotation
            );
            SendCustomNetworkEvent(
                NetworkEventTarget.Others,
                nameof(RemoteStarLaunch),
                launchPosition,
                launchVelocity,
                launchRotation
            );

            nextImpactTime = Mathf.Max(
                nextImpactTime,
                nextHighImpactTime
            );
            RespawnAtWorldStart();
            return;
        }

        ApplySoftBounce(vehicleVelocity, vehiclePosition);
    }

    private void ApplySoftBounce(
        Vector3 vehicleVelocity,
        Vector3 vehiclePosition)
    {
        if (rb == null)
        {
            return;
        }

        Vector3 planarDirection = vehicleVelocity;
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            planarDirection = transform.position - vehiclePosition;
            planarDirection.y = 0f;
        }

        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            planarDirection = Vector3.right;
        }

        Vector3 bounceDirection =
            (planarDirection.normalized + Vector3.up * 0.75f)
                .normalized;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(
            bounceDirection * bounceForce,
            ForceMode.Impulse
        );
    }

    private void RespawnAtWorldStart()
    {
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (objectSync != null)
        {
            // VRCObjectSync가 보관한 실제 월드 시작 위치를 사용한다.
            objectSync.Respawn();
            objectSync.FlagDiscontinuity();
        }
        else
        {
            // ObjectSync가 없는 예외적인 패널만 Start 시점 좌표를 사용한다.
            transform.SetPositionAndRotation(
                initialPosition,
                initialRotation
            );
        }

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 충돌이 발생한 물리 프레임과 Rigidbody 보간이 방금 복구한 위치를
        // 다시 덮어쓰지 못하도록 다음 프레임에 복귀를 확정한다.
        SendCustomEventDelayedFrames(
            nameof(FinalizeWorldStartRespawn),
            1
        );
    }

    /// <summary>
    /// 충돌 다음 프레임에 패널의 월드 시작 위치와 정지 상태를 다시 확정한다.
    /// </summary>
    /// <remarks>소유권자에서 지연 이벤트로 호출되는 로컬 전용 진입점이다.</remarks>
    public void FinalizeWorldStartRespawn()
    {
        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (objectSync != null)
        {
            objectSync.Respawn();
            objectSync.FlagDiscontinuity();
        }
        else
        {
            transform.SetPositionAndRotation(
                initialPosition,
                initialRotation
            );
        }

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }
    }

    /// <summary>
    /// 모든 클라이언트에서 실제 패널을 숨기고 같은 시작 상태의 별 파티클을 재생한다.
    /// </summary>
    /// <param name="launchPosition">별 파티클의 월드 시작 위치이다.</param>
    /// <param name="launchVelocity">별 파티클의 초기 월드 속도이다.</param>
    /// <param name="launchRotation">별 파티클의 초기 월드 회전이다.</param>
    [NetworkCallable]
    public void RemoteStarLaunch(
        Vector3 launchPosition,
        Vector3 launchVelocity,
        Quaternion launchRotation)
    {
        HidePanelForHighImpact();
        EmitStarPanel(
            launchPosition,
            launchVelocity,
            launchRotation
        );
    }

    private void HidePanelForHighImpact()
    {
        SetPanelPresentationVisible(false);

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        float hiddenDuration = Mathf.Max(
            0.2f,
            Mathf.Max(
                starFlightDuration,
                highImpactHiddenDuration
            )
        );
        SendCustomEventDelayedSeconds(
            nameof(RestorePanelAfterHighImpact),
            hiddenDuration
        );
    }

    /// <summary>
    /// 별 비행 연출이 끝난 뒤 패널의 Renderer, Collider, Rigidbody 상태를 복원한다.
    /// </summary>
    public void RestorePanelAfterHighImpact()
    {
        SetPanelPresentationVisible(true);

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = initialRigidbodyKinematic;
            rb.Sleep();
        }
    }

    private void SetPanelPresentationVisible(bool visible)
    {
        for (int i = 0; i < panelRenderers.Length; i++)
        {
            Renderer panelRenderer = panelRenderers[i];

            if (panelRenderer == null ||
                panelRenderer == starPanelParticleRenderer ||
                panelRenderer == starFlashParticleRenderer)
            {
                continue;
            }

            panelRenderer.enabled = visible &&
                initialRendererEnabled[i];
        }

        for (int i = 0; i < panelColliders.Length; i++)
        {
            Collider childCollider = panelColliders[i];

            if (childCollider == null)
            {
                continue;
            }

            childCollider.enabled = visible &&
                initialColliderEnabled[i];
        }
    }

    private void EmitStarPanel(
        Vector3 launchPosition,
        Vector3 launchVelocity,
        Quaternion launchRotation)
    {
        if (starPanelParticle == null)
        {
            return;
        }

        ParticleSystem.EmitParams emitParams =
            new ParticleSystem.EmitParams();
        emitParams.position = launchPosition;
        emitParams.velocity = launchVelocity;
        emitParams.startLifetime = starFlightDuration;
        emitParams.startSize = 1f;
        emitParams.rotation3D =
            launchRotation.eulerAngles * Mathf.Deg2Rad;
        starPanelParticle.Play(false);
        starPanelParticle.Emit(emitParams, 1);

        float flashDelay = starFlightDuration * 0.82f;
        pendingStarPosition =
            launchPosition +
            launchVelocity * flashDelay +
            Physics.gravity * 0.5f * flashDelay * flashDelay;
        SendCustomEventDelayedSeconds(
            nameof(_PlayStarFlash),
            flashDelay
        );
    }

    /// <summary>
    /// 별 비행이 끝나는 위치에서 주 섬광과 첫 번째 보조 섬광을 재생한다.
    /// </summary>
    public void _PlayStarFlash()
    {
        if (starFlashParticle == null)
        {
            return;
        }

        starFlashParticle.Play(false);
        EmitFlashStar(
            pendingStarPosition,
            Mathf.Max(0.1f, starFlashSize),
            1f,
            0f
        );

        // 첫 세 개의 보조 별은 주 섬광과 함께 표시하고, 나머지는 시간차를 두어
        // 하나의 정적인 섬광이 아니라 짧은 파란 반짝임으로 보이게 한다.
        float smallSize = Mathf.Max(0.1f, starFlashSize * 0.28f);
        EmitFlashStar(
            pendingStarPosition + new Vector3(1.25f, 0.7f, 0f),
            smallSize,
            0.6f,
            18f
        );
        EmitFlashStar(
            pendingStarPosition + new Vector3(-1.1f, 0.45f, 0f),
            smallSize * 0.8f,
            0.48f,
            -25f
        );
        EmitFlashStar(
            pendingStarPosition + new Vector3(0.2f, -1f, 0f),
            smallSize * 0.7f,
            0.52f,
            40f
        );

        starTwinkleStep = 0;
        SendCustomEventDelayedSeconds(
            nameof(_PlaySmallStarTwinkle),
            0.14f
        );
    }

    /// <summary>
    /// 주 섬광 뒤에 시간차를 둔 작은 별을 순서대로 재생한다.
    /// </summary>
    public void _PlaySmallStarTwinkle()
    {
        if (starFlashParticle == null || starTwinkleStep >= 3)
        {
            return;
        }

        starTwinkleStep++;
        starFlashParticle.Play(false);
        float angle = 0.75f + starTwinkleStep * 1.85f;
        float radius = 1.1f + starTwinkleStep * 0.28f;
        float smallSize = Mathf.Max(0.1f, starFlashSize * 0.24f);
        Vector3 offset = new Vector3(
            Mathf.Cos(angle) * radius,
            Mathf.Sin(angle) * radius,
            0f
        );

        EmitFlashStar(
            pendingStarPosition + offset,
            smallSize,
            0.45f,
            starTwinkleStep * 31f
        );
        EmitFlashStar(
            pendingStarPosition - offset * 0.8f,
            smallSize * 0.72f,
            0.38f,
            -starTwinkleStep * 27f
        );

        if (starTwinkleStep < 3)
        {
            SendCustomEventDelayedSeconds(
                nameof(_PlaySmallStarTwinkle),
                0.14f
            );
        }
    }

    private void EmitFlashStar(
        Vector3 position,
        float size,
        float lifetime,
        float rotationDegrees)
    {
        ParticleSystem.EmitParams emitParams =
            new ParticleSystem.EmitParams();
        emitParams.position = position;
        emitParams.velocity = Vector3.zero;
        emitParams.startLifetime = lifetime;
        emitParams.startSize = size;
        emitParams.startColor = Color.white;
        emitParams.rotation3D = new Vector3(
            0f,
            0f,
            rotationDegrees * Mathf.Deg2Rad
        );
        starFlashParticle.Emit(emitParams, 1);
    }
}
