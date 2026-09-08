
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// 교통 차량과 패널의 충돌 감지 및 충돌 속도에 따른 반동 또는 별 비행 연출 적용
/// </summary>
/// <remarks>
/// 물리 반응 결정 권한을 현재 소유권자로 제한
/// 고속 충돌의 표시 결과는 네트워크 호출로 전달하고 파티클은 각 클라이언트에서 로컬 재생
/// </remarks>
public class PanelFly : UdonSharpBehaviour
{
    [Header("Collision Response")]
    public float bounceForce = 25f;

    [Tooltip("이 속도 이상의 정면 충돌 시 실제 패널 대신 별로 변하는 로컬 3D 파티클 재생")]
    public float highImpactThresholdKmh = 18f;

    [Tooltip("고속 연출 시작 후 같은 패널의 고속 충돌 재처리 방지 시간")]
    public float highImpactCooldown = 2.5f;

    [Tooltip("Transform으로 이동하는 중앙 교통 차량과의 겹침 검사 간격")]
    public float vehicleProbeInterval = 0.08f;

    [Tooltip("한 차량이 패널을 통과하는 동안 같은 충돌의 반복 처리 방지 시간")]
    public float repeatedImpactCooldown = 0.4f;

    [Tooltip("검사 사이에 차량이 이동한 구간을 보완하는 최소 여유 거리")]
    public float vehicleProbeSweepPadding = 0.1f;

    [Tooltip("한 번의 검사에서 보완할 최대 차량 이동 거리")]
    public float maximumVehicleProbeSweep = 1.2f;

    [Header("High Impact Effect")]
    public TrafficSimulationManager trafficManager;
    public ParticleSystem starPanelParticle;
    public ParticleSystem starFlashParticle;
    [Tooltip("등신대가 솟구칠 때 남기는 약한 수평 이동 속도")]
    public float starHorizontalSpeed = 3.5f;

    [Tooltip("등신대가 화면 위의 하늘까지 올라가는 수직 속도")]
    public float starUpwardSpeed = 28f;

    public float starFlightDuration = 2.6f;
    [Tooltip("고속 충돌 후 실제 등신대 숨김 시간. 별 비행 시간보다 짧게 설정해도 별 비행 종료까지 숨김 유지")]
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

        // 프레임 사이의 접촉 누락 보완을 위한 작은 여유 추가
        // 화면상 떨어진 차량까지 충돌로 판정하지 않도록 여유 크기 제한
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

                // Cuding Edit: 다른 차량에 맞은 Cast를 현재 후보의 충돌로 사용하지 않음
                // 실제로 맞은 Collider는 자신의 후보 검사에서 해당 차량 속도로 처리
                if (sweepHit.collider != hit)
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

        // 플레이어가 패널을 든 상태에서도 겹침 검사 실행
        // 힘 적용 또는 원위치 복귀 전에 Pickup 해제
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

            // 숨긴 실제 패널 대신 같은 외형의 파티클 비행
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
            // VRCObjectSync가 보관한 실제 월드 시작 위치 사용
            objectSync.Respawn();
            objectSync.FlagDiscontinuity();
        }
        else
        {
            // ObjectSync가 없는 예외적인 패널에만 Start 시점 좌표 사용
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

        // 다음 프레임에 복귀를 확정해 충돌 물리 프레임과 Rigidbody 보간에 의한
        // 복구 위치 덮어쓰기 방지
        SendCustomEventDelayedFrames(
            nameof(FinalizeWorldStartRespawn),
            1
        );
    }

    /// <summary>
    /// 충돌 다음 프레임에 패널의 월드 시작 위치와 정지 상태 재확정
    /// </summary>
    /// <remarks>소유권자에서 지연 이벤트로 호출되는 로컬 전용 진입점</remarks>
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
    /// 모든 클라이언트에서 실제 패널 숨김 및 동일한 시작 상태의 별 파티클 재생
    /// </summary>
    /// <param name="launchPosition">별 파티클의 월드 시작 위치</param>
    /// <param name="launchVelocity">별 파티클의 초기 월드 속도</param>
    /// <param name="launchRotation">별 파티클의 초기 월드 회전</param>
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
    /// 별 비행 연출 종료 후 패널의 Renderer, Collider, Rigidbody 상태 복원
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
    /// 별 비행 종료 위치에서 주 섬광과 첫 번째 보조 섬광 재생
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
            Vector3.zero,
            Mathf.Max(0.1f, starFlashSize),
            1.3f,
            0f,
            new Color(0.96f, 0.91f, 0.80f, 1f)
        );

        // Cuding Edit: 한 평면의 고정 섬광 대신 구형으로 퍼지는 별을 방출.
        // 동일한 인덱스로 방향/색을 정해 원격에서도 같은 연출을 재생하고,
        // 방출 이후 이동은 ParticleSystem에 맡겨 매 프레임 Udon 계산을 피한다.
        // Cuding Edit: 주 별을 가볍게 꾸미는 6개만 사용 (처음 3개 + 시간차 3개).
        EmitFlashBurst(0, 3);

        starTwinkleStep = 0;
        SendCustomEventDelayedSeconds(
            nameof(_PlaySmallStarTwinkle),
            0.14f
        );
    }

    /// <summary>
    /// 주 섬광 이후 시간차를 둔 작은 별 순차 재생
    /// </summary>
    public void _PlaySmallStarTwinkle()
    {
        if (starFlashParticle == null || starTwinkleStep >= 3)
        {
            return;
        }

        starTwinkleStep++;
        starFlashParticle.Play(false);
        EmitFlashBurst(2 + starTwinkleStep, 1);

        if (starTwinkleStep < 3)
        {
            SendCustomEventDelayedSeconds(
                nameof(_PlaySmallStarTwinkle),
                0.14f
            );
        }
    }

    private void EmitFlashBurst(int first, int count)
    {
        for (int i = first; i < first + count; i++)
        {
            // Cuding Edit: 방출 순서를 섞어 첫 섬광부터 구의 한쪽에 몰리지 않도록 한다.
            int sample = (i * 5) % 6;
            float y = 1f - 2f * (sample + 0.5f) / 6f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = sample * 2.399963f;
            Vector3 direction = new Vector3(
                Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            float variation = (i % 5) / 4f;
            float tint = sample / 5f;
            // Cuding Edit: 역 건물의 따뜻한 조명에 맞춘 저채도 골드~아이보리~하늘색.
            Color gold = new Color(0.88f, 0.75f, 0.55f, 1f);
            Color ivory = new Color(0.96f, 0.91f, 0.80f, 1f);
            Color sky = new Color(0.67f, 0.77f, 0.84f, 1f);
            Color color = tint < 0.5f
                ? Color.Lerp(gold, ivory, tint * 2f)
                : Color.Lerp(ivory, sky, (tint - 0.5f) * 2f);
            EmitFlashStar(
                pendingStarPosition + direction * 0.15f,
                direction * (1.4f + variation * 0.6f),
                Mathf.Max(0.1f, starFlashSize * (0.16f + variation * 0.06f)),
                1.4f + variation * 0.4f,
                sample * 31f,
                color);
        }
    }

    private void EmitFlashStar(
        Vector3 position,
        Vector3 velocity,
        float size,
        float lifetime,
        float rotationDegrees,
        Color color)
    {
        ParticleSystem.EmitParams emitParams =
            new ParticleSystem.EmitParams();
        emitParams.position = position;
        emitParams.velocity = velocity;
        emitParams.startLifetime = lifetime;
        emitParams.startSize = size;
        emitParams.startColor = color;
        emitParams.rotation3D = new Vector3(
            0f,
            0f,
            rotationDegrees * Mathf.Deg2Rad
        );
        starFlashParticle.Emit(emitParams, 1);
    }
}
