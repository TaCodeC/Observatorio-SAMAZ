using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Visibility presets for the modern-western constellation overlay used in the TEST scene.
/// The IAU defines constellation regions and names, not one official set of stick figures;
/// this overlay therefore presents Stellarium's modern drawing convention explicitly.
/// </summary>
public enum ConstellationOverlayMode
{
    Hidden,
    Mission,
    All
}

/// <summary>
/// Draws Stellarium's HIP-based constellation topology on the same celestial dome as HYG.
/// One combined mesh is used instead of hundreds of LineRenderers, which keeps the beta
/// suitable for the Quest rendering budget while retaining geodesic arcs on the sphere.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConstellationOverlayRenderer : MonoBehaviour
{
    private const string FallbackShaderResourcePath = "Shaders/ConstellationLineURP";
    private const int TubeSides = 4;

    private static readonly int LineColorId = Shader.PropertyToID("_LineColor");
    private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    private static readonly int SkyCenterId = Shader.PropertyToID("_SkyCenter");
    private static readonly int HorizonClipEnabledId = Shader.PropertyToID("_HorizonClipEnabled");
    private static readonly int HorizonClipHeightId = Shader.PropertyToID("_HorizonClipHeight");

    [Header("Referencias")]
    [SerializeField] private StellarVaultRenderer vaultRenderer;
    [SerializeField] private CelestialMissionController missionController;

    [Header("Beta de trazado")]
    [SerializeField] private ConstellationOverlayMode mode = ConstellationOverlayMode.Mission;
    [Tooltip("Las líneas quedan ligeramente delante de las estrellas para conservar legibilidad.")]
    [SerializeField, Min(0.001f)] private float lineRadiusInset = 0.2f;
    [SerializeField, Min(0.005f)] private float tubeRadius = 0.055f;
    [SerializeField, Range(0.25f, 12f)] private float maxArcStepDegrees = 2.5f;
    [SerializeField] private Color lineColor = new Color(0.2f, 0.8f, 1f, 0.75f);
    [SerializeField, Min(0f)] private float emissionStrength = 1.45f;
    [SerializeField] private bool enableRuntimeToggleKey = true;

    private readonly List<Vector3> meshVertices = new List<Vector3>(8192);
    private readonly List<int> meshTriangles = new List<int>(16384);

    private ConstellationLineCatalog catalog;
    private GameObject runtimeObject;
    private MeshFilter runtimeMeshFilter;
    private MeshRenderer runtimeMeshRenderer;
    private Mesh runtimeMesh;
    private Material runtimeMaterial;
    private int observedFrameRevision = int.MinValue;
    private ConstellationOverlayMode observedMode;
    private string observedTargetCode;
    private bool rebuildRequested = true;
    private bool loadFailureLogged;
    private int drawnSegmentCount;
    private int missingEndpointCount;

    public ConstellationOverlayMode Mode => mode;
    public int DrawnSegmentCount => drawnSegmentCount;
    public int MissingEndpointCount => missingEndpointCount;
    public int LoadedFigureCount => catalog == null ? 0 : catalog.FigureCount;
    public string ActiveConstellationCode => ResolveMissionConstellationCode();

    public string ModeDisplayName
    {
        get
        {
            switch (mode)
            {
                case ConstellationOverlayMode.Hidden:
                    return "oculto";
                case ConstellationOverlayMode.All:
                    return "todas";
                default:
                    return "misión";
            }
        }
    }

    public string ActiveConstellationDisplayName
    {
        get
        {
            string code = ResolveMissionConstellationCode();
            if (string.IsNullOrWhiteSpace(code))
            {
                return string.Empty;
            }

            return catalog != null && catalog.TryGetFigure(code, out ConstellationLineFigure figure)
                ? figure.DisplayName
                : code;
        }
    }

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
        rebuildRequested = true;
    }

    private void OnDisable()
    {
        if (runtimeMeshRenderer != null)
        {
            runtimeMeshRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        DestroyRuntimeObjects();
    }

    private void OnValidate()
    {
        lineRadiusInset = Mathf.Max(0.001f, lineRadiusInset);
        tubeRadius = Mathf.Max(0.005f, tubeRadius);
        maxArcStepDegrees = Mathf.Clamp(maxArcStepDegrees, 0.25f, 12f);
        emissionStrength = Mathf.Max(0f, emissionStrength);
        rebuildRequested = true;
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        HandleRuntimeToggleKey();
        EnsureReferences();
        if (!EnsureCatalogLoaded() || !EnsureRuntimeObjects())
        {
            return;
        }

        int currentFrameRevision = vaultRenderer != null ? vaultRenderer.CelestialFrameRevision : int.MinValue;
        string currentTargetCode = ResolveMissionConstellationCode();
        if (rebuildRequested
            || currentFrameRevision != observedFrameRevision
            || mode != observedMode
            || !string.Equals(currentTargetCode, observedTargetCode, StringComparison.OrdinalIgnoreCase))
        {
            RebuildMesh(currentTargetCode);
            observedFrameRevision = currentFrameRevision;
            observedMode = mode;
            observedTargetCode = currentTargetCode;
            rebuildRequested = false;
        }
    }

    private void LateUpdate()
    {
        UpdateMaterialProperties();
    }

    /// <summary>Cycles the TEST controls through mission highlight, all figures, and hidden.</summary>
    public void CycleVisibilityMode()
    {
        switch (mode)
        {
            case ConstellationOverlayMode.Mission:
                SetVisibilityMode(ConstellationOverlayMode.All);
                break;
            case ConstellationOverlayMode.All:
                SetVisibilityMode(ConstellationOverlayMode.Hidden);
                break;
            default:
                SetVisibilityMode(ConstellationOverlayMode.Mission);
                break;
        }
    }

    public void SetVisibilityMode(ConstellationOverlayMode newMode)
    {
        if (mode == newMode)
        {
            return;
        }

        mode = newMode;
        rebuildRequested = true;
    }

    /// <summary>Requests an immediate rebuild after an external test control changes state.</summary>
    public void RequestRebuild()
    {
        rebuildRequested = true;
    }

    private void EnsureReferences()
    {
        if (vaultRenderer == null)
        {
            vaultRenderer = GetComponent<StellarVaultRenderer>();
        }

        if (vaultRenderer == null)
        {
            vaultRenderer = FindFirstObjectByType<StellarVaultRenderer>();
        }

        if (missionController == null)
        {
            missionController = FindFirstObjectByType<CelestialMissionController>();
        }
    }

    private bool EnsureCatalogLoaded()
    {
        if (catalog != null)
        {
            return true;
        }

        if (ConstellationLineCatalogLoader.TryLoad(out catalog, out string error))
        {
            return true;
        }

        if (!loadFailureLogged)
        {
            Debug.LogError($"No se pudo cargar el trazado de constelaciones: {error}", this);
            loadFailureLogged = true;
        }

        return false;
    }

    private bool EnsureRuntimeObjects()
    {
        if (runtimeMesh == null)
        {
            runtimeMesh = new Mesh
            {
                name = "Runtime Constellation Overlay Mesh",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave
            };
            runtimeMesh.MarkDynamic();
        }

        if (runtimeMaterial == null)
        {
            Shader shader = Resources.Load<Shader>(FallbackShaderResourcePath);
            if (shader == null)
            {
                shader = Shader.Find("Observatorio/ConstellationLineURP");
            }

            if (shader == null)
            {
                if (!loadFailureLogged)
                {
                    Debug.LogError("No se encontró el shader Observatorio/ConstellationLineURP.", this);
                    loadFailureLogged = true;
                }

                return false;
            }

            runtimeMaterial = new Material(shader)
            {
                name = "Runtime Constellation Overlay Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (runtimeObject == null)
        {
            runtimeObject = new GameObject(
                "Constellation Overlay (Runtime)",
                typeof(MeshFilter),
                typeof(MeshRenderer)
            )
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer
            };
            runtimeObject.transform.position = Vector3.zero;
            runtimeObject.transform.rotation = Quaternion.identity;
            runtimeObject.transform.localScale = Vector3.one;

            runtimeMeshFilter = runtimeObject.GetComponent<MeshFilter>();
            runtimeMeshRenderer = runtimeObject.GetComponent<MeshRenderer>();
            runtimeMeshRenderer.sharedMaterial = runtimeMaterial;
            runtimeMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            runtimeMeshRenderer.receiveShadows = false;
            runtimeMeshRenderer.lightProbeUsage = LightProbeUsage.Off;
            runtimeMeshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            runtimeMeshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        runtimeMeshFilter.sharedMesh = runtimeMesh;
        return true;
    }

    private void RebuildMesh(string missionConstellationCode)
    {
        meshVertices.Clear();
        meshTriangles.Clear();
        drawnSegmentCount = 0;
        missingEndpointCount = 0;

        if (runtimeMesh == null || runtimeMeshRenderer == null || vaultRenderer == null || !vaultRenderer.IsCatalogReady)
        {
            ClearRenderedMesh();
            return;
        }

        if (mode == ConstellationOverlayMode.All)
        {
            IReadOnlyList<ConstellationLineFigure> figures = catalog.Figures;
            for (int index = 0; index < figures.Count; index++)
            {
                AppendFigure(figures[index]);
            }
        }
        else if (mode == ConstellationOverlayMode.Mission
                 && !string.IsNullOrWhiteSpace(missionConstellationCode)
                 && catalog.TryGetFigure(missionConstellationCode, out ConstellationLineFigure figure))
        {
            AppendFigure(figure);
        }

        runtimeMesh.Clear(false);
        if (meshVertices.Count == 0)
        {
            runtimeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 0.01f);
            runtimeMeshRenderer.enabled = false;
            return;
        }

        runtimeMesh.SetVertices(meshVertices);
        runtimeMesh.SetTriangles(meshTriangles, 0, false);
        float boundsDiameter = Mathf.Max(1f, vaultRenderer.VaultRadius * 2.1f);
        runtimeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsDiameter);
        runtimeMeshRenderer.enabled = true;
    }

    private void ClearRenderedMesh()
    {
        if (runtimeMesh != null)
        {
            runtimeMesh.Clear(false);
        }

        if (runtimeMeshRenderer != null)
        {
            runtimeMeshRenderer.enabled = false;
        }
    }

    private void AppendFigure(ConstellationLineFigure figure)
    {
        IReadOnlyList<ConstellationLinePath> paths = figure.Polylines;
        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            IReadOnlyList<int> hipIds = paths[pathIndex].HipIds;
            for (int hipIndex = 0; hipIndex < hipIds.Count - 1; hipIndex++)
            {
                if (!vaultRenderer.TryGetStarDirectionByHipId(hipIds[hipIndex], out Vector3 firstDirection, out _)
                    || !vaultRenderer.TryGetStarDirectionByHipId(hipIds[hipIndex + 1], out Vector3 secondDirection, out _))
                {
                    missingEndpointCount++;
                    continue;
                }

                AppendGreatCircleTube(firstDirection, secondDirection);
                drawnSegmentCount++;
            }
        }
    }

    private void AppendGreatCircleTube(Vector3 firstDirection, Vector3 secondDirection)
    {
        float dot = Mathf.Clamp(Vector3.Dot(firstDirection, secondDirection), -1f, 1f);
        float arcDegrees = Mathf.Acos(dot) * Mathf.Rad2Deg;
        if (arcDegrees <= 0.0001f)
        {
            return;
        }

        Vector3 rotationAxis = Vector3.Cross(firstDirection, secondDirection);
        if (rotationAxis.sqrMagnitude <= 0.0000001f)
        {
            // The supplied Stellarium data has no antipodal segments, but keep this branch
            // deterministic should a future catalogue include one.
            rotationAxis = Vector3.Cross(firstDirection, PickStablePerpendicular(firstDirection));
        }

        rotationAxis.Normalize();
        int arcSteps = Mathf.Max(1, Mathf.CeilToInt(arcDegrees / maxArcStepDegrees));
        float domeRadius = Mathf.Max(0.01f, vaultRenderer.VaultRadius - lineRadiusInset);
        int firstVertex = meshVertices.Count;

        for (int step = 0; step <= arcSteps; step++)
        {
            float interpolation = step / (float)arcSteps;
            Vector3 direction = Quaternion.AngleAxis(arcDegrees * interpolation, rotationAxis) * firstDirection;
            direction.Normalize();
            Vector3 tangent = Vector3.Cross(rotationAxis, direction).normalized;
            Vector3 bitangent = Vector3.Cross(tangent, direction).normalized;
            Vector3 center = direction * domeRadius;

            for (int side = 0; side < TubeSides; side++)
            {
                float angle = side * (Mathf.PI * 2f / TubeSides);
                Vector3 offset = (bitangent * Mathf.Cos(angle) + direction * Mathf.Sin(angle)) * tubeRadius;
                meshVertices.Add(center + offset);
            }
        }

        for (int step = 0; step < arcSteps; step++)
        {
            int currentRing = firstVertex + step * TubeSides;
            int nextRing = currentRing + TubeSides;
            for (int side = 0; side < TubeSides; side++)
            {
                int nextSide = (side + 1) % TubeSides;
                meshTriangles.Add(currentRing + side);
                meshTriangles.Add(nextRing + side);
                meshTriangles.Add(nextRing + nextSide);
                meshTriangles.Add(currentRing + side);
                meshTriangles.Add(nextRing + nextSide);
                meshTriangles.Add(currentRing + nextSide);
            }
        }
    }

    private void UpdateMaterialProperties()
    {
        if (runtimeMaterial == null)
        {
            return;
        }

        runtimeMaterial.SetColor(LineColorId, lineColor);
        runtimeMaterial.SetFloat(EmissionStrengthId, emissionStrength);
        runtimeMaterial.SetVector(SkyCenterId, vaultRenderer != null ? vaultRenderer.SkyCenterWorldPosition : Vector3.zero);
        runtimeMaterial.SetFloat(
            HorizonClipEnabledId,
            vaultRenderer != null && vaultRenderer.HorizonClippingEnabled ? 1f : 0f
        );
        runtimeMaterial.SetFloat(
            HorizonClipHeightId,
            vaultRenderer != null ? vaultRenderer.HorizonClipRelativeHeight : 0f
        );

        // The mesh contains positions relative to the observer's sky center. Translate the
        // runtime object for correct Unity frustum culling, but never parent or rotate it with
        // the camera: celestial rotation is owned exclusively by StellarVaultRenderer.
        if (runtimeObject != null)
        {
            runtimeObject.transform.SetPositionAndRotation(
                vaultRenderer != null ? vaultRenderer.SkyCenterWorldPosition : Vector3.zero,
                Quaternion.identity
            );
        }
    }

    private string ResolveMissionConstellationCode()
    {
        if (missionController == null || !missionController.HasMission)
        {
            return string.Empty;
        }

        CelestialMissionDefinition mission = missionController.ActiveMission;
        if (mission.targetKind == CelestialMissionTargetKind.Constellation)
        {
            return mission.constellationCode?.Trim() ?? string.Empty;
        }

        if (mission.targetKind == CelestialMissionTargetKind.Star
            && vaultRenderer != null
            && vaultRenderer.TryGetStarByHipId(mission.hipId, out StellarVaultRenderer.StarInfo star))
        {
            return star.Constellation?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private void HandleRuntimeToggleKey()
    {
        if (!enableRuntimeToggleKey)
        {
            return;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        bool readInputSystem = false;
#endif
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            readInputSystem = true;
#endif
            if (keyboard.cKey.wasPressedThisFrame)
            {
                CycleVisibilityMode();
            }
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!readInputSystem && Input.GetKeyDown(KeyCode.C))
        {
            CycleVisibilityMode();
        }
#endif
    }

    private void DestroyRuntimeObjects()
    {
        Mesh meshToDestroy = runtimeMesh;
        Material materialToDestroy = runtimeMaterial;
        GameObject objectToDestroy = runtimeObject;

        runtimeMesh = null;
        runtimeMaterial = null;
        runtimeObject = null;
        runtimeMeshFilter = null;
        runtimeMeshRenderer = null;

        DestroyRuntimeObject(meshToDestroy);
        DestroyRuntimeObject(materialToDestroy);
        DestroyRuntimeObject(objectToDestroy);
    }

    private static Vector3 PickStablePerpendicular(Vector3 direction)
    {
        Vector3 reference = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
        return Vector3.Cross(reference, direction).normalized;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object objectToDestroy)
    {
        if (objectToDestroy == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(objectToDestroy);
        }
        else
        {
            DestroyImmediate(objectToDestroy);
        }
    }
}
