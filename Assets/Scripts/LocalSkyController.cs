using System;
using System.Collections;
using System.Globalization;
using UnityEngine;

namespace Samaz.Observatory.Astronomy
{
    public enum SkyTimeMode
    {
        SystemUtc,
        FixedUtc,
        AcceleratedUtc
    }

    /// <summary>
    /// Unity-facing coordinator for the local sky simulation.
    /// UI code can call its public methods for manual coordinates, location presets, or an
    /// explicitly user-requested device location without knowing about Astronomy Engine.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StellarVaultRenderer))]
    public sealed class LocalSkyController : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private StellarVaultRenderer vaultRenderer;
        [Tooltip("Ancla que mantiene la bóveda centrada en el observador para evitar paralaje al mover la cabeza.")]
        [SerializeField] private Transform skyCenterAnchor;

        [Header("Ubicación inicial manual")]
        [Tooltip("Latitud: norte positiva, sur negativa.")]
        [SerializeField, Range(-90f, 90f)] private double manualLatitudeDegrees = 23.2003158d;
        [Tooltip("Longitud: este positiva, oeste negativa.")]
        [SerializeField, Range(-180f, 180f)] private double manualLongitudeDegrees = -106.4222214d;
        [SerializeField, Min(-1000f)] private double manualElevationMeters = 10d;
        [SerializeField] private LocationPreset initialLocationPreset;

        [Header("Tiempo")]
        [SerializeField] private SkyTimeMode timeMode = SkyTimeMode.SystemUtc;
        [Tooltip("Usado por los modos FixedUtc y AcceleratedUtc. Debe estar en UTC, por ejemplo 2026-01-01T00:00:00Z.")]
        [SerializeField] private string fixedUtcIso8601 = "2026-01-01T00:00:00Z";
        [Tooltip("Segundos simulados por segundo real. Valores negativos permiten retroceder el tiempo.")]
        [SerializeField] private double simulationSpeed = 60d;
        [SerializeField, Min(0.05f)] private float refreshIntervalSeconds = 0.5f;

        [Header("Horizonte y calibración")]
        [SerializeField] private bool hideBelowHorizon = true;
        [SerializeField, Range(-5f, 20f)] private float minimumAltitudeDegrees = 0f;
        [Tooltip("Giro alrededor del cenit para alinear el norte virtual con un ancla física/XR si existe.")]
        [SerializeField] private float northYawDegrees;

        [Header("Geolocalización opcional")]
        [Tooltip("Sólo se solicita al llamar RequestDeviceLocation desde una acción explícita de la persona usuaria.")]
        [SerializeField, Min(1f)] private float deviceLocationTimeoutSeconds = 20f;
        [SerializeField, Min(1f)] private float deviceDesiredAccuracyMeters = 50f;
        [SerializeField, Min(1f)] private float deviceUpdateDistanceMeters = 500f;

        private ILocalSkyTransform skyTransform;
        private GeoCoordinate activeLocation;
        private bool hasActiveLocation;
        private bool refreshRequested;
        private float nextRefreshTime;
        private DateTime simulationEpochUtc;
        private float simulationEpochRealtime;
        private bool simulationClockInitialized;
        private Coroutine deviceLocationRoutine;

        public GeoCoordinate ActiveLocation => activeLocation;
        public bool HasActiveLocation => hasActiveLocation;
        public float NorthYawDegrees => northYawDegrees;

        private void Awake()
        {
            EnsureReferences();
            skyTransform = new AstronomyEngineLocalSkyTransform();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureReferences();
            if (skyTransform == null)
            {
                skyTransform = new AstronomyEngineLocalSkyTransform();
            }

            ApplyInitialLocation();
            ConfigureSkyCenterAnchor();
            RequestRefresh();
        }

        private void OnDisable()
        {
            if (deviceLocationRoutine != null)
            {
                StopCoroutine(deviceLocationRoutine);
                deviceLocationRoutine = null;
                Input.location.Stop();
            }

            if (Application.isPlaying && vaultRenderer != null)
            {
                vaultRenderer.ResetCelestialFrame();
                vaultRenderer.SetSkyCenterAnchor(null);
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || !hasActiveLocation || vaultRenderer == null)
            {
                return;
            }

            bool clockNeedsRefresh = timeMode != SkyTimeMode.FixedUtc;
            if (!refreshRequested && (!clockNeedsRefresh || Time.unscaledTime < nextRefreshTime))
            {
                return;
            }

            RefreshSky();
        }

        private void OnValidate()
        {
            refreshIntervalSeconds = Mathf.Max(0.05f, refreshIntervalSeconds);
            deviceLocationTimeoutSeconds = Mathf.Max(1f, deviceLocationTimeoutSeconds);
            deviceDesiredAccuracyMeters = Mathf.Max(1f, deviceDesiredAccuracyMeters);
            deviceUpdateDistanceMeters = Mathf.Max(1f, deviceUpdateDistanceMeters);
        }

        /// <summary>
        /// Applies coordinates entered by a future text field or another UI surface.
        /// Values are not persisted by this component.
        /// </summary>
        public void SetManualLocation(double latitudeDegrees, double longitudeDegrees, double elevationMeters = 0d)
        {
            GeoCoordinate location = new GeoCoordinate(latitudeDegrees, longitudeDegrees, elevationMeters);
            manualLatitudeDegrees = latitudeDegrees;
            manualLongitudeDegrees = longitudeDegrees;
            manualElevationMeters = elevationMeters;
            ApplyLocation(location);
        }

        /// <summary>
        /// Parsing helper for a Spanish/English numeric UI. It accepts either invariant or current culture formatting.
        /// </summary>
        public bool TrySetManualLocation(string latitudeText, string longitudeText, string elevationText, out string error)
        {
            if (!TryParseNumber(latitudeText, out double latitudeDegrees)
                || !TryParseNumber(longitudeText, out double longitudeDegrees)
                || !TryParseNumber(elevationText, out double elevationMeters))
            {
                error = "Las coordenadas deben ser números válidos.";
                return false;
            }

            try
            {
                SetManualLocation(latitudeDegrees, longitudeDegrees, elevationMeters);
                error = string.Empty;
                return true;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public void SetLocationPreset(LocationPreset preset)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            initialLocationPreset = preset;
            ApplyLocation(preset.ToGeoCoordinate());
        }

        /// <summary>
        /// Convenience entry point for a compact location menu. Larger curated lists use LocationPreset assets.
        /// </summary>
        public void SetBuiltInLocation(BuiltInSkyLocation location)
        {
            initialLocationPreset = null;
            ApplyLocation(BuiltInSkyLocations.Get(location));
        }

        public void SetSystemUtcTime()
        {
            timeMode = SkyTimeMode.SystemUtc;
            RequestRefresh();
        }

        public void SetFixedUtcTime(DateTime utcTime)
        {
            if (utcTime.Kind == DateTimeKind.Unspecified)
            {
                throw new ArgumentException("La fecha debe indicar UTC o una zona horaria local.", nameof(utcTime));
            }

            DateTime normalizedUtc = utcTime.ToUniversalTime();
            fixedUtcIso8601 = normalizedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            timeMode = SkyTimeMode.FixedUtc;
            simulationClockInitialized = false;
            RequestRefresh();
        }

        public void SetAcceleratedUtcTime(DateTime utcTime, double secondsPerRealSecond)
        {
            SetFixedUtcTime(utcTime);
            simulationSpeed = secondsPerRealSecond;
            timeMode = SkyTimeMode.AcceleratedUtc;
            simulationEpochUtc = utcTime.ToUniversalTime();
            simulationEpochRealtime = Time.unscaledTime;
            simulationClockInitialized = true;
            RequestRefresh();
        }

        public void SetNorthYaw(float degrees)
        {
            northYawDegrees = degrees;
            RequestRefresh();
        }

        public void SetSkyCenterAnchor(Transform anchor)
        {
            skyCenterAnchor = anchor;
            ConfigureSkyCenterAnchor();
        }

        /// <summary>
        /// Starts a one-shot device lookup after an explicit UI action. The sampled location stays in memory only.
        /// Quest hardware may not provide a GPS fix, so manual coordinates remain the guaranteed fallback.
        /// </summary>
        public void RequestDeviceLocation()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("La geolocalización sólo puede solicitarse durante Play Mode.", this);
                return;
            }

            if (deviceLocationRoutine != null)
            {
                return;
            }

            deviceLocationRoutine = StartCoroutine(RequestDeviceLocationRoutine());
        }

        public void RequestRefresh()
        {
            refreshRequested = true;
            nextRefreshTime = 0f;
        }

        private void RefreshSky()
        {
            try
            {
                ObservationContext context = new ObservationContext(activeLocation, GetCurrentUtcTime());
                SkyFrame skyFrame = skyTransform.CreateFrame(context).WithNorthYaw(northYawDegrees);
                vaultRenderer.SetCelestialFrame(
                    skyFrame.EquatorialJ2000ToWorld,
                    hideBelowHorizon,
                    minimumAltitudeDegrees
                );

                refreshRequested = false;
                nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            }
            catch (Exception exception)
            {
                refreshRequested = true;
                nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
                Debug.LogError($"No se pudo actualizar el cielo local: {exception.Message}", this);
            }
        }

        private DateTime GetCurrentUtcTime()
        {
            switch (timeMode)
            {
                case SkyTimeMode.FixedUtc:
                    return ParseFixedUtcTime();

                case SkyTimeMode.AcceleratedUtc:
                    if (!simulationClockInitialized)
                    {
                        simulationEpochUtc = ParseFixedUtcTime();
                        simulationEpochRealtime = Time.unscaledTime;
                        simulationClockInitialized = true;
                    }

                    return simulationEpochUtc.AddSeconds((Time.unscaledTime - simulationEpochRealtime) * simulationSpeed);

                default:
                    return DateTime.UtcNow;
            }
        }

        private DateTime ParseFixedUtcTime()
        {
            if (!DateTime.TryParse(
                    fixedUtcIso8601,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsedUtc))
            {
                throw new FormatException("La fecha UTC fija debe usar ISO 8601, por ejemplo 2026-01-01T00:00:00Z.");
            }

            return parsedUtc;
        }

        private void ApplyInitialLocation()
        {
            try
            {
                ApplyLocation(initialLocationPreset != null
                    ? initialLocationPreset.ToGeoCoordinate()
                    : new GeoCoordinate(manualLatitudeDegrees, manualLongitudeDegrees, manualElevationMeters));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                hasActiveLocation = false;
                Debug.LogError($"La ubicación inicial no es válida: {exception.Message}", this);
            }
        }

        private void ApplyLocation(GeoCoordinate location)
        {
            activeLocation = location;
            hasActiveLocation = true;
            RequestRefresh();
        }

        private void EnsureReferences()
        {
            if (vaultRenderer == null)
            {
                vaultRenderer = GetComponent<StellarVaultRenderer>();
            }

            if (skyCenterAnchor == null && Camera.main != null)
            {
                skyCenterAnchor = Camera.main.transform;
            }
        }

        private void ConfigureSkyCenterAnchor()
        {
            if (vaultRenderer != null)
            {
                vaultRenderer.SetSkyCenterAnchor(skyCenterAnchor);
            }
        }

        private IEnumerator RequestDeviceLocationRoutine()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                    float permissionDeadline = Time.realtimeSinceStartup + deviceLocationTimeoutSeconds;
                    while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation)
                        && Time.realtimeSinceStartup < permissionDeadline)
                    {
                        yield return null;
                    }

                    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
                    {
                        Debug.LogWarning("No se concedió permiso de ubicación; se conserva la ubicación manual o predefinida.", this);
                        yield break;
                    }
                }
#endif

                if (!Input.location.isEnabledByUser)
                {
                    Debug.LogWarning("La ubicación está desactivada en el dispositivo; se conserva la ubicación manual o predefinida.", this);
                    yield break;
                }

                Input.location.Start(deviceDesiredAccuracyMeters, deviceUpdateDistanceMeters);
                float deadline = Time.realtimeSinceStartup + deviceLocationTimeoutSeconds;
                while (Input.location.status == LocationServiceStatus.Initializing && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (Input.location.status != LocationServiceStatus.Running)
                {
                    Debug.LogWarning("No se pudo obtener una ubicación del dispositivo; se conserva la ubicación manual o predefinida.", this);
                    yield break;
                }

                LocationInfo locationInfo = Input.location.lastData;
                SetManualLocation(locationInfo.latitude, locationInfo.longitude, locationInfo.altitude);
                Debug.Log("Ubicación del dispositivo aplicada al cielo local. No se guardó historial de ubicación.", this);
            }
            finally
            {
                Input.location.Stop();
                deviceLocationRoutine = null;
            }
        }

        private static bool TryParseNumber(string text, out double value)
        {
            const NumberStyles Styles = NumberStyles.Float | NumberStyles.AllowThousands;
            return double.TryParse(text, Styles, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, Styles, CultureInfo.CurrentCulture, out value);
        }
    }
}
