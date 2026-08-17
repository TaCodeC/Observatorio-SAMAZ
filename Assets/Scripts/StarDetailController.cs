using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Selects a catalogue star from a screen click or sustained gaze, then animates a single
/// procedural detail sphere from its rendered sky position to an inspection distance in front of
/// the observer. It never moves the catalogue star itself, so star rendering and mission queries
/// continue using the same local-sky frame.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class StarDetailController : MonoBehaviour
{
    private const string DetailShaderResourcePath = "Shaders/StarDetailURP";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int CellColorId = Shader.PropertyToID("_CellColor");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    private static readonly int GranulationScaleId = Shader.PropertyToID("_GranulationScale");
    private static readonly int GranulationSpeedId = Shader.PropertyToID("_GranulationSpeed");
    private static readonly int ActivityId = Shader.PropertyToID("_Activity");
    private static readonly int TimeSeedId = Shader.PropertyToID("_TimeSeed");

    [Header("Referencias")]
    [SerializeField] private StellarVaultRenderer vaultRenderer;
    [SerializeField] private StellarFlyCameraController flyCameraController;
    [SerializeField] private Camera targetCamera;

    [Header("Selección")]
    [SerializeField, Range(0.1f, 5f)] private float clickSelectionAngleDegrees = 0.75f;
    [SerializeField] private bool enableGazeSelection = true;
    [SerializeField, Range(0.1f, 5f)] private float gazeSelectionAngleDegrees = 0.5f;
    [SerializeField, Min(0.25f)] private float gazeFocusSeconds = 2.25f;
    [SerializeField, Min(0.02f)] private float gazeScanIntervalSeconds = 0.08f;

    [Header("Vista de detalle")]
    [SerializeField, Min(0.5f)] private float detailDistance = 2.75f;
    [SerializeField, Min(0.05f)] private float approachDurationSeconds = 1.25f;
    [SerializeField, Min(0.001f)] private float minimumStartDiameter = 0.015f;

    private GameObject detailSphereObject;
    private Material detailMaterial;
    private StellarFlyCameraController subscribedFlyCameraController;
    private StellarVaultRenderer.StarInfo selectedStar;
    private StarDetailAppearance selectedAppearance;
    private Vector3 approachStartPosition;
    private float approachStartDiameter;
    private float approachStartedAt;
    private float nextGazeScanTime;
    private float lastGazeScanTime;
    private float gazeFocusElapsed;
    private int gazeCandidateHipId = int.MinValue;
    private string gazeCandidateName = string.Empty;
    private DetailState detailState;
    private bool hasSelectedStar;

    public bool HasSelectedStar => hasSelectedStar;
    public bool IsApproaching => detailState == DetailState.Approaching;
    public StellarVaultRenderer.StarInfo SelectedStar => selectedStar;
    public StarDetailAppearance SelectedAppearance => selectedAppearance;

    private enum DetailState
    {
        Hidden,
        Approaching,
        Inspecting
    }

    private void Awake()
    {
        EnsureReferences();
        EnsureDetailSphere();
    }

    private void OnEnable()
    {
        EnsureReferences();
        SubscribeToClickEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromClickEvents();
        DismissSelection();
    }

    private void OnDestroy()
    {
        UnsubscribeFromClickEvents();

        if (detailSphereObject != null)
        {
            Destroy(detailSphereObject);
        }

        if (detailMaterial != null)
        {
            Destroy(detailMaterial);
        }
    }

    private void OnValidate()
    {
        clickSelectionAngleDegrees = Mathf.Clamp(clickSelectionAngleDegrees, 0.1f, 5f);
        gazeSelectionAngleDegrees = Mathf.Clamp(gazeSelectionAngleDegrees, 0.1f, 5f);
        gazeFocusSeconds = Mathf.Max(0.25f, gazeFocusSeconds);
        gazeScanIntervalSeconds = Mathf.Max(0.02f, gazeScanIntervalSeconds);
        detailDistance = Mathf.Max(0.5f, detailDistance);
        approachDurationSeconds = Mathf.Max(0.05f, approachDurationSeconds);
        minimumStartDiameter = Mathf.Max(0.001f, minimumStartDiameter);
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureReferences();
        SubscribeToClickEvents();

        if (WasDismissPressed())
        {
            DismissSelection();
            return;
        }

        if (!hasSelectedStar)
        {
            UpdateGazeSelection();
        }
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !hasSelectedStar || detailSphereObject == null || targetCamera == null)
        {
            return;
        }

        Vector3 targetPosition = targetCamera.transform.position + targetCamera.transform.forward * detailDistance;
        if (detailState == DetailState.Approaching)
        {
            float elapsed = Time.unscaledTime - approachStartedAt;
            float progress = Mathf.Clamp01(elapsed / approachDurationSeconds);
            float smoothProgress = progress * progress * (3f - 2f * progress);
            float diameter = Mathf.Lerp(approachStartDiameter, selectedAppearance.VisualDiameter, smoothProgress);

            detailSphereObject.transform.position = Vector3.Lerp(approachStartPosition, targetPosition, smoothProgress);
            detailSphereObject.transform.localScale = Vector3.one * diameter;

            if (progress >= 1f)
            {
                detailState = DetailState.Inspecting;
            }
        }
        else if (detailState == DetailState.Inspecting)
        {
            detailSphereObject.transform.position = targetPosition;
            detailSphereObject.transform.localScale = Vector3.one * selectedAppearance.VisualDiameter;
        }
    }

    /// <summary>
    /// Allows an XR ray interactor or future UI layer to select a catalogue result without
    /// duplicating the approach effect.
    /// </summary>
    public bool SelectStar(StellarVaultRenderer.StarInfo starInfo)
    {
        if (!EnsureDetailSphere())
        {
            return false;
        }

        if (hasSelectedStar && IsSameStar(selectedStar, starInfo))
        {
            return true;
        }

        selectedStar = starInfo;
        selectedAppearance = StarDetailAppearanceFactory.Create(starInfo);
        hasSelectedStar = true;
        detailState = DetailState.Approaching;
        approachStartedAt = Time.unscaledTime;
        approachStartPosition = detailSphereObject.activeSelf
            ? detailSphereObject.transform.position
            : starInfo.WorldPosition;
        approachStartDiameter = detailSphereObject.activeSelf
            ? detailSphereObject.transform.localScale.x
            : minimumStartDiameter;

        ApplyAppearance(selectedAppearance);
        detailSphereObject.transform.position = approachStartPosition;
        detailSphereObject.transform.localScale = Vector3.one * Mathf.Max(minimumStartDiameter, approachStartDiameter);
        detailSphereObject.SetActive(true);
        ResetGazeCandidate();
        return true;
    }

    public void DismissSelection()
    {
        hasSelectedStar = false;
        detailState = DetailState.Hidden;
        selectedStar = default;
        selectedAppearance = default;
        ResetGazeCandidate();

        if (detailSphereObject != null)
        {
            detailSphereObject.SetActive(false);
        }
    }

    private void HandleWorldClick(Vector2 screenPosition)
    {
        if (vaultRenderer == null || targetCamera == null)
        {
            return;
        }

        Ray clickRay = targetCamera.ScreenPointToRay(screenPosition);
        if (vaultRenderer.TryFindStarInView(
                clickRay.origin,
                clickRay.direction,
                clickSelectionAngleDegrees,
                out StellarVaultRenderer.StarInfo starInfo))
        {
            SelectStar(starInfo);
        }
    }

    private void UpdateGazeSelection()
    {
        if (!enableGazeSelection || vaultRenderer == null || targetCamera == null)
        {
            ResetGazeCandidate();
            return;
        }

        if (Time.unscaledTime < nextGazeScanTime)
        {
            return;
        }

        float now = Time.unscaledTime;
        float elapsed = Mathf.Max(0f, now - lastGazeScanTime);
        lastGazeScanTime = now;
        nextGazeScanTime = now + gazeScanIntervalSeconds;

        if (!vaultRenderer.TryFindStarInView(targetCamera, gazeSelectionAngleDegrees, out StellarVaultRenderer.StarInfo starInfo))
        {
            ResetGazeCandidate();
            return;
        }

        string displayName = starInfo.DisplayName ?? string.Empty;
        if (starInfo.HipId != gazeCandidateHipId || displayName != gazeCandidateName)
        {
            gazeCandidateHipId = starInfo.HipId;
            gazeCandidateName = displayName;
            gazeFocusElapsed = 0f;
        }
        else
        {
            gazeFocusElapsed += elapsed;
        }

        if (gazeFocusElapsed >= gazeFocusSeconds)
        {
            SelectStar(starInfo);
        }
    }

    private void ResetGazeCandidate()
    {
        gazeCandidateHipId = int.MinValue;
        gazeCandidateName = string.Empty;
        gazeFocusElapsed = 0f;
        nextGazeScanTime = 0f;
        lastGazeScanTime = Time.unscaledTime;
    }

    private void EnsureReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (vaultRenderer == null)
        {
            vaultRenderer = FindFirstObjectByType<StellarVaultRenderer>();
        }

        if (flyCameraController == null)
        {
            flyCameraController = GetComponent<StellarFlyCameraController>();
        }
    }

    private void SubscribeToClickEvents()
    {
        if (subscribedFlyCameraController == flyCameraController)
        {
            return;
        }

        UnsubscribeFromClickEvents();
        subscribedFlyCameraController = flyCameraController;

        if (subscribedFlyCameraController != null)
        {
            subscribedFlyCameraController.WorldClickReleased += HandleWorldClick;
        }
    }

    private void UnsubscribeFromClickEvents()
    {
        if (subscribedFlyCameraController != null)
        {
            subscribedFlyCameraController.WorldClickReleased -= HandleWorldClick;
            subscribedFlyCameraController = null;
        }
    }

    private bool EnsureDetailSphere()
    {
        if (detailSphereObject != null && detailMaterial != null)
        {
            return true;
        }

        Shader shader = Resources.Load<Shader>(DetailShaderResourcePath);
        if (shader == null)
        {
            shader = Shader.Find("Observatorio/StarDetailURP");
        }

        if (shader == null)
        {
            Debug.LogError("No se encontró el shader Observatorio/StarDetailURP para la vista de detalle.", this);
            return false;
        }

        detailMaterial = new Material(shader)
        {
            name = "Runtime Star Detail Material",
            hideFlags = HideFlags.HideAndDontSave
        };

        detailSphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        detailSphereObject.name = "Selected Star Detail";
        detailSphereObject.hideFlags = HideFlags.HideAndDontSave;

        Collider collider = detailSphereObject.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        MeshRenderer meshRenderer = detailSphereObject.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = detailMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        detailSphereObject.SetActive(false);
        return true;
    }

    private void ApplyAppearance(StarDetailAppearance appearance)
    {
        if (detailMaterial == null)
        {
            return;
        }

        detailMaterial.SetColor(BaseColorId, appearance.PhotosphereColor);
        detailMaterial.SetColor(CellColorId, appearance.CellColor);
        detailMaterial.SetColor(RimColorId, appearance.RimColor);
        detailMaterial.SetFloat(EmissionStrengthId, appearance.EmissionStrength);
        detailMaterial.SetFloat(GranulationScaleId, appearance.GranulationScale);
        detailMaterial.SetFloat(GranulationSpeedId, appearance.GranulationSpeed);
        detailMaterial.SetFloat(ActivityId, appearance.Activity);
        detailMaterial.SetFloat(TimeSeedId, appearance.TimeSeed);
    }

    private static bool IsSameStar(StellarVaultRenderer.StarInfo first, StellarVaultRenderer.StarInfo second)
    {
        return first.HipId > 0 && first.HipId == second.HipId
            || (first.HipId <= 0 && second.HipId <= 0
                && first.EquatorialJ2000Direction == second.EquatorialJ2000Direction);
    }

    private static bool WasDismissPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            return keyboard.escapeKey.wasPressedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }
}
