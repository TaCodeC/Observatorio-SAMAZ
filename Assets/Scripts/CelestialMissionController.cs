using System;
using UnityEngine;

/// <summary>
/// The two target categories supported by the first local-sky mission prototype.
/// A constellation objective completes when the player centers any visible catalogue star
/// belonging to its IAU three-letter code.
/// </summary>
public enum CelestialMissionTargetKind
{
    Star,
    Constellation
}

/// <summary>
/// Serializable mission data kept independent from its HUD. Future content can replace the
/// inline test list with ScriptableObject-backed mission packs without changing the controller.
/// </summary>
[Serializable]
public struct CelestialMissionDefinition
{
    [Tooltip("Identificador estable para telemetría local o contenido futuro.")]
    public string id;
    public string title;
    [TextArea]
    public string instruction;
    public CelestialMissionTargetKind targetKind;
    [Tooltip("HIP requerido cuando el objetivo es una estrella.")]
    public int hipId;
    [Tooltip("Código IAU de tres letras, por ejemplo Ori o UMa, cuando el objetivo es una constelación.")]
    public string constellationCode;
}

/// <summary>
/// A gaze-based local-sky mission loop for the TEST scene. It deliberately asks the renderer
/// about targets instead of calculating separate positions, keeping mission validation aligned
/// with the rendered catalogue and the active horizon rule.
/// </summary>
[DisallowMultipleComponent]
public sealed class CelestialMissionController : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private StellarVaultRenderer vaultRenderer;
    [SerializeField] private Camera targetCamera;

    [Header("Validación por mirada")]
    [SerializeField, Range(0.1f, 5f)] private float targetAngleDegrees = 1.25f;
    [SerializeField, Min(0.1f)] private float requiredFocusSeconds = 1.25f;
    [SerializeField, Min(0.01f)] private float scanIntervalSeconds = 0.05f;

    [Header("Misiones de prueba")]
    [SerializeField] private CelestialMissionDefinition[] missions =
    {
        new CelestialMissionDefinition
        {
            id = "find-polaris",
            title = "Localiza Polaris",
            instruction = "Encuentra la Estrella Polar y mantenla en el centro de tu vista.",
            targetKind = CelestialMissionTargetKind.Star,
            hipId = 11767
        },
        new CelestialMissionDefinition
        {
            id = "find-orion",
            title = "Localiza Orión",
            instruction = "Apunta a cualquier estrella de la constelación de Orión y mantenla centrada.",
            targetKind = CelestialMissionTargetKind.Constellation,
            constellationCode = "Ori"
        },
        new CelestialMissionDefinition
        {
            id = "find-sirius",
            title = "Localiza Sirius",
            instruction = "Busca Sirius, la estrella brillante del Can Mayor, y sostenla en el centro.",
            targetKind = CelestialMissionTargetKind.Star,
            hipId = 32349
        },
        new CelestialMissionDefinition
        {
            id = "find-ursa-major",
            title = "Localiza la Osa Mayor",
            instruction = "Apunta a una estrella de la Osa Mayor y mantenla centrada.",
            targetKind = CelestialMissionTargetKind.Constellation,
            constellationCode = "UMa"
        },
        new CelestialMissionDefinition
        {
            id = "find-vega",
            title = "Localiza Vega",
            instruction = "Encuentra Vega y mantenla en el centro de tu vista.",
            targetKind = CelestialMissionTargetKind.Star,
            hipId = 91262
        }
    };

    private int activeMissionIndex;
    private float focusSeconds;
    private float nextScanTime;
    private float lastScanTime;
    private bool targetInView;
    private bool targetObservable;
    private bool completed;
    private string statusMessage = "Esperando catálogo estelar.";

    public bool HasMission => missions != null && missions.Length > 0;
    public CelestialMissionDefinition ActiveMission => HasMission ? missions[activeMissionIndex] : default;
    public int ActiveMissionIndex => activeMissionIndex;
    public float FocusProgress01 => requiredFocusSeconds <= 0f ? 0f : Mathf.Clamp01(focusSeconds / requiredFocusSeconds);
    public bool TargetInView => targetInView;
    public bool TargetObservable => targetObservable;
    public bool IsCompleted => completed;
    public string StatusMessage => statusMessage;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
        ResetActiveMission();
    }

    private void OnValidate()
    {
        targetAngleDegrees = Mathf.Clamp(targetAngleDegrees, 0.1f, 5f);
        requiredFocusSeconds = Mathf.Max(0.1f, requiredFocusSeconds);
        scanIntervalSeconds = Mathf.Max(0.01f, scanIntervalSeconds);
        ClampActiveMissionIndex();
    }

    private void Update()
    {
        if (!Application.isPlaying || !HasMission)
        {
            return;
        }

        if (Time.unscaledTime < nextScanTime)
        {
            return;
        }

        float currentTime = Time.unscaledTime;
        float elapsedSeconds = Mathf.Max(0f, currentTime - lastScanTime);
        lastScanTime = currentTime;
        nextScanTime = currentTime + scanIntervalSeconds;
        EvaluateMission(elapsedSeconds);
    }

    public void SelectNextMission()
    {
        if (!HasMission)
        {
            return;
        }

        activeMissionIndex = (activeMissionIndex + 1) % missions.Length;
        ResetActiveMission();
    }

    public void SelectPreviousMission()
    {
        if (!HasMission)
        {
            return;
        }

        activeMissionIndex = (activeMissionIndex - 1 + missions.Length) % missions.Length;
        ResetActiveMission();
    }

    public void ResetActiveMission()
    {
        ClampActiveMissionIndex();
        focusSeconds = 0f;
        targetInView = false;
        targetObservable = false;
        completed = false;
        statusMessage = "Busca el objetivo en el cielo.";
        nextScanTime = 0f;
        lastScanTime = Time.unscaledTime;
    }

    private void EvaluateMission(float elapsedSeconds)
    {
        EnsureReferences();
        if (vaultRenderer == null || targetCamera == null)
        {
            targetInView = false;
            targetObservable = false;
            focusSeconds = 0f;
            statusMessage = "Falta conectar la cámara o la bóveda estelar.";
            return;
        }

        if (!vaultRenderer.IsCatalogReady)
        {
            targetInView = false;
            targetObservable = false;
            focusSeconds = 0f;
            statusMessage = "Cargando catálogo estelar…";
            return;
        }

        CelestialMissionDefinition mission = ActiveMission;
        targetObservable = IsMissionObservable(mission);
        if (!targetObservable)
        {
            targetInView = false;
            focusSeconds = 0f;
            statusMessage = "El objetivo está bajo el horizonte para esta ubicación y hora.";
            return;
        }

        targetInView = IsMissionInView(mission);
        if (completed)
        {
            statusMessage = "¡Misión completada! Puedes elegir otra misión.";
            return;
        }

        if (!targetInView)
        {
            focusSeconds = 0f;
            statusMessage = "Busca el objetivo y llévalo al centro de tu vista.";
            return;
        }

        focusSeconds = Mathf.Min(requiredFocusSeconds, focusSeconds + elapsedSeconds);
        if (focusSeconds >= requiredFocusSeconds)
        {
            completed = true;
            statusMessage = "¡Misión completada! Objetivo localizado.";
            return;
        }

        statusMessage = "Objetivo localizado: mantenlo centrado…";
    }

    private bool IsMissionObservable(CelestialMissionDefinition mission)
    {
        switch (mission.targetKind)
        {
            case CelestialMissionTargetKind.Star:
                return vaultRenderer.TryGetStarByHipId(mission.hipId, out StellarVaultRenderer.StarInfo star)
                    && star.IsAboveHorizon;

            case CelestialMissionTargetKind.Constellation:
                return vaultRenderer.TryGetBrightestStarInConstellation(
                    mission.constellationCode,
                    true,
                    out _
                );

            default:
                return false;
        }
    }

    private bool IsMissionInView(CelestialMissionDefinition mission)
    {
        switch (mission.targetKind)
        {
            case CelestialMissionTargetKind.Star:
                return vaultRenderer.TryFindStarWithHipIdInView(
                    targetCamera,
                    mission.hipId,
                    targetAngleDegrees,
                    out _
                );

            case CelestialMissionTargetKind.Constellation:
                return vaultRenderer.TryFindStarInConstellationInView(
                    targetCamera,
                    mission.constellationCode,
                    targetAngleDegrees,
                    out _
                );

            default:
                return false;
        }
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
    }

    private void ClampActiveMissionIndex()
    {
        if (!HasMission)
        {
            activeMissionIndex = 0;
            return;
        }

        activeMissionIndex = Mathf.Clamp(activeMissionIndex, 0, missions.Length - 1);
    }
}
