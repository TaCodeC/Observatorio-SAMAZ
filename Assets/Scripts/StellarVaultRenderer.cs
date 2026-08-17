using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class StellarVaultRenderer : MonoBehaviour
{
    private const string CatalogMagic = "HYG\0";
    private const uint SupportedCatalogVersion = 1;
    private const int RecordSize = 48;
    private const int InstancesPerBatch = 1023;
    private const string FallbackShaderResourcePath = "Shaders/StarBillboardURP";

    private static readonly int StarColorId = Shader.PropertyToID("_StarColor");
    private static readonly int StarTwinkleId = Shader.PropertyToID("_StarTwinkle");
    private static readonly int GlobalIntensityId = Shader.PropertyToID("_GlobalIntensity");
    private static readonly int PointSoftnessId = Shader.PropertyToID("_PointSoftness");
    private static readonly int TwinkleStrengthId = Shader.PropertyToID("_TwinkleStrength");
    private static readonly int TwinkleSpeedId = Shader.PropertyToID("_TwinkleSpeed");
    private static readonly int SizeMultiplierId = Shader.PropertyToID("_SizeMultiplier");
    private static readonly int SkyCenterId = Shader.PropertyToID("_SkyCenter");
    private static readonly int HorizonClipEnabledId = Shader.PropertyToID("_HorizonClipEnabled");
    private static readonly int HorizonClipHeightId = Shader.PropertyToID("_HorizonClipHeight");

    public struct StarInfo
    {
        public int HipId;
        public string DisplayName;
        public bool HasProperName;
        public Vector3 EquatorialJ2000Direction;
        public Vector3 Direction;
        public Vector3 WorldPosition;
        public bool IsAboveHorizon;
        public float Magnitude;
        public float ColorIndex;
        public float DistanceParsecs;
        public float Luminosity;
        public string SpectralType;
        public string Constellation;
        public float TwinkleSeed;

        public bool HasDatabaseInfo
        {
            get
            {
                return HipId > 0
                    || HasValidFloat(Magnitude)
                    || HasValidFloat(DistanceParsecs)
                    || HasValidFloat(Luminosity)
                    || !string.IsNullOrWhiteSpace(SpectralType)
                    || !string.IsNullOrWhiteSpace(Constellation);
            }
        }
    }

    [Header("Catalogo")]
    [SerializeField] private string catalogRelativePath = "Data/hyg_stars.bytes";
    [SerializeField] private string starNamesRelativePath = "Data/star_names.json";
    [SerializeField, Range(-2f, 8f)] private float magnitudeLimit = 7f;
    [SerializeField] private bool excludeSun = true;

    [Header("Boveda")]
    [SerializeField, Min(1f)] private float vaultRadius = 90f;
    [SerializeField] private float yawOffsetDegrees;
    [Tooltip("Ancla opcional que mantiene el cielo centrado en el observador para evitar paralaje de traslación.")]
    [SerializeField] private Transform skyCenterAnchor;
    [SerializeField, Min(0.001f)] private float minStarSize = 0.025f;
    [SerializeField, Min(0.001f)] private float maxStarSize = 0.28f;

    [Header("Render")]
    [SerializeField] private Material starMaterial;
    [SerializeField] private bool preferWebCompatibleMeshFallback = true;
    [SerializeField, Min(0f)] private float globalIntensity = 1.35f;
    [SerializeField, Range(0.25f, 8f)] private float pointSoftness = 1.9f;
    [SerializeField, Range(0f, 1f)] private float twinkleStrength = 0.18f;
    [SerializeField, Min(0f)] private float twinkleSpeed = 1.1f;
    [SerializeField, Range(0.25f, 8f)] private float starSizeMultiplier = 1.25f;
    [SerializeField] private bool setDarkCameraBackground = true;
    [SerializeField] private Color darkSkyColor = new Color(0.002f, 0.0035f, 0.009f, 1f);

    [Header("Controles de prueba")]
    [SerializeField] private bool enableRuntimeVisibilityKeys = true;
    [SerializeField, Min(0.01f)] private float visibilityStep = 0.25f;
    [SerializeField, Min(0.01f)] private float starSizeStep = 0.15f;
    [SerializeField, Min(0.01f)] private float magnitudeStep = 0.25f;

    private readonly List<Matrix4x4[]> matrixBatches = new List<Matrix4x4[]>();
    private readonly List<Vector4[]> colorBatches = new List<Vector4[]>();
    private readonly List<Vector4[]> twinkleBatches = new List<Vector4[]>();
    private readonly List<int> batchCounts = new List<int>();
    private readonly List<StarInfo> visibleStars = new List<StarInfo>();
    private readonly List<Mesh> fallbackMeshBatches = new List<Mesh>();
    // Constellation overlays, selection, and missions resolve catalogue stars by HIP.
    // Keeping this index next to the render cache avoids a second catalogue and prevents
    // line endpoints from ever drifting away from the star positions being drawn.
    private readonly Dictionary<int, int> hipIndex = new Dictionary<int, int>();

    private Mesh starMesh;
    private Material runtimeMaterial;
    private MaterialPropertyBlock propertyBlock;
    private Dictionary<int, string> starNames = new Dictionary<int, string>();
    private Coroutine loadRoutine;
    private bool catalogReady;
    private bool loggedRenderSummary;
    private bool editorCatalogDirty;
    private byte[] cachedCatalogBytes;
    private int renderedStarCount;
    private Matrix4x4 equatorialJ2000ToWorld = Matrix4x4.identity;
    private bool horizonClippingEnabled;
    private float minimumHorizonAltitudeDegrees;
    private bool localSkyFrameEnabled;
    private int celestialFrameRevision;

    /// <summary>
    /// Becomes true after the HYG catalogue has been loaded and can be queried by test features
    /// such as guided localization missions.
    /// </summary>
    public bool IsCatalogReady => catalogReady;

    /// <summary>
    /// Increments whenever the local positions of the rendered catalogue are rebuilt.
    /// Auxiliary renderers can use it to refresh geometry without reimplementing sky math.
    /// </summary>
    public int CelestialFrameRevision => celestialFrameRevision;

    /// <summary>
    /// Center applied to render-only celestial geometry. Star positions returned by this
    /// class are relative to this point so the dome remains centered on the observer.
    /// </summary>
    public Vector3 SkyCenterWorldPosition => GetSkyCenter();

    public float VaultRadius => vaultRadius;
    public bool HorizonClippingEnabled => horizonClippingEnabled;
    public float HorizonClipRelativeHeight => GetHorizonClipHeight();

    /// <summary>
    /// Applies one shared J2000-to-local-world frame to the complete star catalogue.
    /// The renderer owns the resulting render matrices and selection positions so they cannot drift apart.
    /// </summary>
    public void SetCelestialFrame(Matrix4x4 frame, bool hideBelowHorizon, float minimumAltitudeDegrees = 0f)
    {
        bool catalogFilterChanged = !localSkyFrameEnabled;
        localSkyFrameEnabled = true;
        equatorialJ2000ToWorld = frame;
        horizonClippingEnabled = hideBelowHorizon;
        minimumHorizonAltitudeDegrees = Mathf.Clamp(minimumAltitudeDegrees, -89.9f, 89.9f);

        if (catalogReady)
        {
            if (catalogFilterChanged)
            {
                RebuildBatchesFromCache();
            }
            else
            {
                RefreshStarWorldState();
            }
        }

        UpdateMaterialProperties();
    }

    /// <summary>
    /// Restores the prototype's static J2000 orientation. Used when the local sky controller is disabled.
    /// </summary>
    public void ResetCelestialFrame()
    {
        bool catalogFilterChanged = localSkyFrameEnabled && !excludeSun;
        localSkyFrameEnabled = false;
        equatorialJ2000ToWorld = Matrix4x4.identity;
        horizonClippingEnabled = false;
        minimumHorizonAltitudeDegrees = 0f;

        if (catalogReady)
        {
            if (catalogFilterChanged)
            {
                RebuildBatchesFromCache();
            }
            else
            {
                RefreshStarWorldState();
            }
        }

        UpdateMaterialProperties();
    }

    public void SetSkyCenterAnchor(Transform anchor)
    {
        skyCenterAnchor = anchor;
        UpdateMaterialProperties();
    }

    private void OnEnable()
    {
        if (!EnsureRuntimeAssets())
        {
            enabled = false;
            return;
        }

        if (Application.isPlaying)
        {
            loadRoutine = StartCoroutine(LoadCatalogRoutine());
        }
        else
        {
            LoadCatalogFromStreamingAssetsFile();
        }
    }

    private void OnDisable()
    {
        if (loadRoutine != null)
        {
            StopCoroutine(loadRoutine);
            loadRoutine = null;
        }

        catalogReady = false;
    }

    private void OnDestroy()
    {
        DestroyFallbackMeshes();
        DestroyRuntimeObject(runtimeMaterial);
        DestroyRuntimeObject(starMesh);
    }

    public bool TryFindStarInView(Camera viewCamera, float maxAngleDegrees, out StarInfo starInfo)
    {
        if (viewCamera == null)
        {
            starInfo = default;
            return false;
        }

        return TryFindStarInView(viewCamera.transform.position, viewCamera.transform.forward, maxAngleDegrees, out starInfo);
    }

    public bool TryFindStarInView(Vector3 viewOrigin, Vector3 viewForward, float maxAngleDegrees, out StarInfo starInfo)
    {
        return TryFindMatchingStarInView(viewOrigin, viewForward, maxAngleDegrees, 0, null, out starInfo);
    }

    /// <summary>
    /// Finds one specific HIP target only when it is centered in the current view cone.
    /// This lets gameplay share exactly the same catalogue and horizon rule as rendering.
    /// </summary>
    public bool TryFindStarWithHipIdInView(Camera viewCamera, int hipId, float maxAngleDegrees, out StarInfo starInfo)
    {
        if (viewCamera == null || hipId <= 0)
        {
            starInfo = default;
            return false;
        }

        return TryFindMatchingStarInView(
            viewCamera.transform.position,
            viewCamera.transform.forward,
            maxAngleDegrees,
            hipId,
            null,
            out starInfo
        );
    }

    /// <summary>
    /// Finds any visible member of an IAU three-letter constellation code within the view cone.
    /// </summary>
    public bool TryFindStarInConstellationInView(
        Camera viewCamera,
        string constellationCode,
        float maxAngleDegrees,
        out StarInfo starInfo)
    {
        if (viewCamera == null || string.IsNullOrWhiteSpace(constellationCode))
        {
            starInfo = default;
            return false;
        }

        return TryFindMatchingStarInView(
            viewCamera.transform.position,
            viewCamera.transform.forward,
            maxAngleDegrees,
            0,
            constellationCode,
            out starInfo
        );
    }

    public bool TryGetStarByHipId(int hipId, out StarInfo starInfo)
    {
        starInfo = default;
        if (!catalogReady || hipId <= 0 || !hipIndex.TryGetValue(hipId, out int index))
        {
            return false;
        }

        if (index < 0 || index >= visibleStars.Count)
        {
            return false;
        }

        starInfo = visibleStars[index];
        return true;
    }

    /// <summary>
    /// Resolves an endpoint on the visual celestial dome. The position is deliberately
    /// relative to <see cref="SkyCenterWorldPosition"/>, just like the instanced stars.
    /// It is intended for overlays such as constellation arcs, not physical star distance.
    /// </summary>
    public bool TryGetStarOnVaultByHipId(int hipId, out Vector3 localPosition, out bool isAboveHorizon)
    {
        if (TryGetStarByHipId(hipId, out StarInfo star))
        {
            localPosition = star.WorldPosition;
            isAboveHorizon = star.IsAboveHorizon;
            return true;
        }

        localPosition = Vector3.zero;
        isAboveHorizon = false;
        return false;
    }

    /// <summary>
    /// Resolves the transformed unit direction used by the current celestial frame.
    /// </summary>
    public bool TryGetStarDirectionByHipId(int hipId, out Vector3 direction, out bool isAboveHorizon)
    {
        if (TryGetStarByHipId(hipId, out StarInfo star))
        {
            direction = star.Direction;
            isAboveHorizon = star.IsAboveHorizon;
            return true;
        }

        direction = Vector3.zero;
        isAboveHorizon = false;
        return false;
    }

    /// <summary>
    /// Gets the brightest catalogue member of a constellation. Gameplay uses this to explain
    /// whether a constellation objective is presently above the local horizon.
    /// </summary>
    public bool TryGetBrightestStarInConstellation(
        string constellationCode,
        bool requireAboveHorizon,
        out StarInfo starInfo)
    {
        starInfo = default;
        if (!catalogReady || string.IsNullOrWhiteSpace(constellationCode))
        {
            return false;
        }

        bool found = false;
        float lowestMagnitude = float.PositiveInfinity;
        for (int index = 0; index < visibleStars.Count; index++)
        {
            StarInfo candidate = visibleStars[index];
            if (!IsConstellationMatch(candidate, constellationCode)
                || (requireAboveHorizon && horizonClippingEnabled && !candidate.IsAboveHorizon)
                || candidate.Magnitude >= lowestMagnitude)
            {
                continue;
            }

            lowestMagnitude = candidate.Magnitude;
            starInfo = candidate;
            found = true;
        }

        return found;
    }

    private bool TryFindMatchingStarInView(
        Vector3 viewOrigin,
        Vector3 viewForward,
        float maxAngleDegrees,
        int requiredHipId,
        string requiredConstellationCode,
        out StarInfo starInfo)
    {
        starInfo = default;

        if (!catalogReady || visibleStars.Count == 0 || viewForward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float clampedAngle = Mathf.Clamp(maxAngleDegrees, 0.05f, 10f);
        float minDot = Mathf.Cos(clampedAngle * Mathf.Deg2Rad);
        float bestScore = minDot;
        bool found = false;
        Vector3 normalizedForward = viewForward.normalized;

        for (int i = 0; i < visibleStars.Count; i++)
        {
            StarInfo candidate = visibleStars[i];
            if ((requiredHipId > 0 && candidate.HipId != requiredHipId)
                || (!string.IsNullOrWhiteSpace(requiredConstellationCode)
                    && !IsConstellationMatch(candidate, requiredConstellationCode))
                || (horizonClippingEnabled && !candidate.IsAboveHorizon))
            {
                continue;
            }

            Vector3 worldPosition = GetSkyCenter() + candidate.WorldPosition;
            Vector3 toStar = worldPosition - viewOrigin;
            if (toStar.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            float dot = Vector3.Dot(normalizedForward, toStar.normalized);
            if (dot < minDot)
            {
                continue;
            }

            float brightnessBias = Mathf.Clamp01(Mathf.InverseLerp(magnitudeLimit, -1.5f, candidate.Magnitude)) * 0.0001f;
            float score = dot + brightnessBias;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            candidate.WorldPosition = worldPosition;
            starInfo = candidate;
            found = true;
        }

        return found;
    }

    private static bool IsConstellationMatch(StarInfo candidate, string constellationCode)
    {
        return string.Equals(
            candidate.Constellation?.Trim(),
            constellationCode.Trim(),
            StringComparison.OrdinalIgnoreCase
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ClampSerializedValues();
        editorCatalogDirty = true;
        RequestEditorRepaint();
    }
#endif

    private void Update()
    {
        if (Application.isPlaying)
        {
            HandleRuntimeVisibilityKeys();
            return;
        }

        if (editorCatalogDirty || !catalogReady)
        {
            if (EnsureRuntimeAssets())
            {
                LoadCatalogFromStreamingAssetsFile();
            }

            editorCatalogDirty = false;
        }
    }

    private void LateUpdate()
    {
        if (!catalogReady)
        {
            return;
        }

        if (setDarkCameraBackground)
        {
            ApplyDarkCameraBackground();
        }

        UpdateMaterialProperties();
        DrawStarBatches();
    }

    private IEnumerator LoadCatalogRoutine()
    {
        yield return LoadStarNamesRoutine();

        string catalogUri = BuildStreamingAssetsUri(catalogRelativePath);

        using (UnityWebRequest request = UnityWebRequest.Get(catalogUri))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"No se pudo cargar el catalogo HYG desde {catalogUri}: {request.error}", this);
                yield break;
            }

            BuildBatches(request.downloadHandler.data);
        }
    }

    private bool EnsureRuntimeAssets()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        if (starMesh == null)
        {
            starMesh = CreateStarQuad();
        }

        if (runtimeMaterial != null)
        {
            UpdateMaterialProperties();
            return true;
        }

        Material sourceMaterial = starMaterial;
        Shader shader = sourceMaterial != null ? sourceMaterial.shader : Resources.Load<Shader>(FallbackShaderResourcePath);
        if (shader == null)
        {
            shader = Shader.Find("Observatorio/StarBillboardURP");
        }

        if (sourceMaterial != null)
        {
            runtimeMaterial = new Material(sourceMaterial);
        }
        else if (shader != null)
        {
            runtimeMaterial = new Material(shader);
        }

        if (runtimeMaterial == null)
        {
            Debug.LogError("No se encontro el shader Observatorio/StarBillboardURP para dibujar la boveda estelar.", this);
            return false;
        }

        runtimeMaterial.name = "Runtime Star Billboard Material";
        runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        runtimeMaterial.enableInstancing = true;
        UpdateMaterialProperties();
        return true;
    }

    private void BuildBatches(byte[] catalogBytes, bool cacheCatalog = true)
    {
        if (cacheCatalog)
        {
            cachedCatalogBytes = catalogBytes;
        }

        ClearBatches();
        catalogReady = false;

        if (catalogBytes == null || catalogBytes.Length < 16)
        {
            Debug.LogError("El archivo HYG esta vacio o no contiene un header valido.", this);
            return;
        }

        using (MemoryStream stream = new MemoryStream(catalogBytes))
        using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
        {
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint version = reader.ReadUInt32();
            uint starCount = reader.ReadUInt32();
            reader.ReadUInt32();

            if (magic != CatalogMagic)
            {
                Debug.LogError("El archivo HYG no tiene la firma esperada.", this);
                return;
            }

            if (version != SupportedCatalogVersion)
            {
                Debug.LogError($"Version de catalogo HYG no soportada: {version}. Esperada: {SupportedCatalogVersion}.", this);
                return;
            }

            long expectedLength = 16L + (long)starCount * RecordSize;
            if (catalogBytes.LongLength < expectedLength)
            {
                Debug.LogError($"Catalogo HYG incompleto. Bytes: {catalogBytes.LongLength}, esperado: {expectedLength}.", this);
                return;
            }

            for (uint i = 0; i < starCount; i++)
            {
                ReadStarRecord(
                    reader,
                    out int hipId,
                    out float rightAscensionRad,
                    out float declinationRad,
                    out float magnitude,
                    out float colorIndex,
                    out float distanceParsecs,
                    out float luminosity,
                    out string spectralType,
                    out string constellation,
                    out uint flags
                );

                if (magnitude > magnitudeLimit)
                {
                    continue;
                }

                if ((excludeSun || localSkyFrameEnabled) && hipId < 0 && distanceParsecs <= 0.0001f && magnitude < -10f)
                {
                    continue;
                }

                AddStar(hipId, rightAscensionRad, declinationRad, magnitude, colorIndex, distanceParsecs, luminosity, spectralType, constellation, flags);
            }
        }

        BuildFallbackMeshes();
        catalogReady = renderedStarCount > 0;
        celestialFrameRevision++;
        loggedRenderSummary = false;

        if (!catalogReady)
        {
            Debug.LogWarning("El catalogo HYG cargo, pero no quedo ninguna estrella despues de aplicar filtros.", this);
        }

        RequestEditorRepaint();
    }

    private static void ReadStarRecord(
        BinaryReader reader,
        out int hipId,
        out float rightAscensionRad,
        out float declinationRad,
        out float magnitude,
        out float colorIndex,
        out float distanceParsecs,
        out float luminosity,
        out string spectralType,
        out string constellation,
        out uint flags
    )
    {
        hipId = reader.ReadInt32();
        rightAscensionRad = reader.ReadSingle();
        declinationRad = reader.ReadSingle();
        magnitude = reader.ReadSingle();
        colorIndex = reader.ReadSingle();
        distanceParsecs = reader.ReadSingle();
        luminosity = reader.ReadSingle();
        spectralType = DecodeFixedString(reader.ReadBytes(8));
        constellation = DecodeFixedString(reader.ReadBytes(4));
        reader.ReadBytes(4);
        flags = reader.ReadUInt32();
    }

    private void AddStar(
        int hipId,
        float rightAscensionRad,
        float declinationRad,
        float magnitude,
        float colorIndex,
        float distanceParsecs,
        float luminosity,
        string spectralType,
        string constellation,
        uint flags
    )
    {
        int batchIndex = renderedStarCount / InstancesPerBatch;
        int indexInBatch = renderedStarCount % InstancesPerBatch;

        if (indexInBatch == 0)
        {
            matrixBatches.Add(new Matrix4x4[InstancesPerBatch]);
            colorBatches.Add(new Vector4[InstancesPerBatch]);
            twinkleBatches.Add(new Vector4[InstancesPerBatch]);
            batchCounts.Add(0);
        }

        Vector3 equatorialJ2000Direction = SphericalToUnityDirection(rightAscensionRad, declinationRad);
        Vector3 direction = TransformEquatorialJ2000Direction(equatorialJ2000Direction);
        Quaternion rotation = Quaternion.LookRotation(-direction, PickStableUp(direction));
        float size = CalculateStarSize(magnitude);
        float twinkleSeed = Mathf.Repeat(renderedStarCount * 0.6180339f, 1f);
        string properName = string.Empty;
        bool hasProperName = (flags & 1u) != 0u && hipId > 0 && starNames.TryGetValue(hipId, out properName);
        string displayName = hasProperName ? properName : hipId > 0 ? $"HIP {hipId}" : "Estrella sin HIP";

        matrixBatches[batchIndex][indexInBatch] = Matrix4x4.TRS(direction * vaultRadius, rotation, Vector3.one * size);
        colorBatches[batchIndex][indexInBatch] = CalculateStarColor(magnitude, colorIndex);
        twinkleBatches[batchIndex][indexInBatch] = new Vector4(twinkleSeed, 0f, 0f, 0f);
        visibleStars.Add(new StarInfo
        {
            HipId = hipId,
            DisplayName = displayName,
            HasProperName = hasProperName,
            EquatorialJ2000Direction = equatorialJ2000Direction,
            Direction = direction,
            WorldPosition = direction * vaultRadius,
            IsAboveHorizon = IsAboveHorizon(direction),
            Magnitude = magnitude,
            ColorIndex = colorIndex,
            DistanceParsecs = distanceParsecs,
            Luminosity = luminosity,
            SpectralType = spectralType,
            Constellation = constellation,
            TwinkleSeed = twinkleSeed
        });
        if (hipId > 0 && !hipIndex.ContainsKey(hipId))
        {
            hipIndex.Add(hipId, visibleStars.Count - 1);
        }

        batchCounts[batchIndex] = indexInBatch + 1;
        renderedStarCount++;
    }

    private void DrawStarBatches()
    {
        if (runtimeMaterial == null || starMesh == null)
        {
            return;
        }

        if (ShouldUseMeshFallback())
        {
            DrawFallbackMeshBatches();
        }
        else
        {
            DrawInstancedStarBatches();
        }

        if (!loggedRenderSummary && Application.isPlaying)
        {
            string renderPath = ShouldUseMeshFallback() ? "mallas combinadas" : "GPU instancing";
            Debug.Log($"Boveda estelar prototipo: {renderedStarCount:n0} estrellas en {matrixBatches.Count:n0} lotes ({renderPath}).", this);
            loggedRenderSummary = true;
        }
    }

    private void DrawInstancedStarBatches()
    {
        for (int i = 0; i < matrixBatches.Count; i++)
        {
            propertyBlock.Clear();
            propertyBlock.SetVectorArray(StarColorId, colorBatches[i]);
            propertyBlock.SetVectorArray(StarTwinkleId, twinkleBatches[i]);
            propertyBlock.SetVector(SkyCenterId, GetSkyCenter());
            propertyBlock.SetFloat(HorizonClipEnabledId, horizonClippingEnabled ? 1f : 0f);
            propertyBlock.SetFloat(HorizonClipHeightId, GetHorizonClipHeight());

            Graphics.DrawMeshInstanced(
                starMesh,
                0,
                runtimeMaterial,
                matrixBatches[i],
                batchCounts[i],
                propertyBlock,
                ShadowCastingMode.Off,
                false,
                gameObject.layer
            );
        }
    }

    private void DrawFallbackMeshBatches()
    {
        if (fallbackMeshBatches.Count == 0)
        {
            return;
        }

        for (int i = 0; i < fallbackMeshBatches.Count; i++)
        {
            Graphics.DrawMesh(
                fallbackMeshBatches[i],
                equatorialJ2000ToWorld,
                runtimeMaterial,
                gameObject.layer,
                null,
                0,
                null,
                ShadowCastingMode.Off,
                false
            );
        }
    }

    private void UpdateMaterialProperties()
    {
        if (runtimeMaterial == null)
        {
            return;
        }

        runtimeMaterial.SetFloat(GlobalIntensityId, globalIntensity);
        runtimeMaterial.SetFloat(PointSoftnessId, pointSoftness);
        runtimeMaterial.SetFloat(TwinkleStrengthId, twinkleStrength);
        runtimeMaterial.SetFloat(TwinkleSpeedId, twinkleSpeed);
        runtimeMaterial.SetFloat(SizeMultiplierId, starSizeMultiplier);
        runtimeMaterial.SetVector(SkyCenterId, GetSkyCenter());
        runtimeMaterial.SetFloat(HorizonClipEnabledId, horizonClippingEnabled ? 1f : 0f);
        runtimeMaterial.SetFloat(HorizonClipHeightId, GetHorizonClipHeight());
    }

    private bool ShouldUseMeshFallback()
    {
        if (!SystemInfo.supportsInstancing)
        {
            return true;
        }

        return preferWebCompatibleMeshFallback && Application.platform == RuntimePlatform.WebGLPlayer;
    }

    private void ApplyDarkCameraBackground()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = darkSkyColor;
    }

    private void ClearBatches()
    {
        matrixBatches.Clear();
        colorBatches.Clear();
        twinkleBatches.Clear();
        batchCounts.Clear();
        visibleStars.Clear();
        hipIndex.Clear();
        DestroyFallbackMeshes();
        renderedStarCount = 0;
    }

    private void BuildFallbackMeshes()
    {
        DestroyFallbackMeshes();

        if (visibleStars.Count == 0)
        {
            return;
        }

        for (int startIndex = 0; startIndex < visibleStars.Count; startIndex += InstancesPerBatch)
        {
            int count = Mathf.Min(InstancesPerBatch, visibleStars.Count - startIndex);
            fallbackMeshBatches.Add(CreateFallbackMeshBatch(startIndex, count));
        }
    }

    private Mesh CreateFallbackMeshBatch(int startIndex, int count)
    {
        Vector3[] vertices = new Vector3[count * 4];
        Vector2[] uvs = new Vector2[count * 4];
        Vector2[] twinkleUvs = new Vector2[count * 4];
        List<Vector3> centerUvs = new List<Vector3>(count * 4);
        Color[] colors = new Color[count * 4];
        int[] indices = new int[count * 6];

        Vector2 uv00 = new Vector2(0f, 0f);
        Vector2 uv10 = new Vector2(1f, 0f);
        Vector2 uv01 = new Vector2(0f, 1f);
        Vector2 uv11 = new Vector2(1f, 1f);

        for (int i = 0; i < count; i++)
        {
            StarInfo star = visibleStars[startIndex + i];
            Vector3 direction = star.EquatorialJ2000Direction;
            Vector3 center = direction * vaultRadius;
            float halfSize = CalculateStarSize(star.Magnitude) * 0.5f;
            Vector3 right = Vector3.Cross(PickStableUp(direction), direction).normalized * halfSize;
            Vector3 up = Vector3.Cross(direction, right).normalized * halfSize;
            Color color = CalculateStarColor(star.Magnitude, star.ColorIndex);
            Vector2 twinkle = new Vector2(star.TwinkleSeed, 0f);

            int vertexIndex = i * 4;
            vertices[vertexIndex] = center - right - up;
            vertices[vertexIndex + 1] = center + right - up;
            vertices[vertexIndex + 2] = center - right + up;
            vertices[vertexIndex + 3] = center + right + up;

            uvs[vertexIndex] = uv00;
            uvs[vertexIndex + 1] = uv10;
            uvs[vertexIndex + 2] = uv01;
            uvs[vertexIndex + 3] = uv11;

            twinkleUvs[vertexIndex] = twinkle;
            twinkleUvs[vertexIndex + 1] = twinkle;
            twinkleUvs[vertexIndex + 2] = twinkle;
            twinkleUvs[vertexIndex + 3] = twinkle;

            centerUvs.Add(center);
            centerUvs.Add(center);
            centerUvs.Add(center);
            centerUvs.Add(center);

            colors[vertexIndex] = color;
            colors[vertexIndex + 1] = color;
            colors[vertexIndex + 2] = color;
            colors[vertexIndex + 3] = color;

            int indexIndex = i * 6;
            indices[indexIndex] = vertexIndex;
            indices[indexIndex + 1] = vertexIndex + 2;
            indices[indexIndex + 2] = vertexIndex + 1;
            indices[indexIndex + 3] = vertexIndex + 2;
            indices[indexIndex + 4] = vertexIndex + 3;
            indices[indexIndex + 5] = vertexIndex + 1;
        }

        Mesh mesh = new Mesh
        {
            name = $"Star Web Fallback Batch {fallbackMeshBatches.Count + 1}"
        };
        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.indexFormat = IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = twinkleUvs;
        mesh.SetUVs(2, centerUvs);
        mesh.colors = colors;
        mesh.triangles = indices;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * vaultRadius * 3f);
        mesh.UploadMeshData(false);
        return mesh;
    }

    private void DestroyFallbackMeshes()
    {
        for (int i = 0; i < fallbackMeshBatches.Count; i++)
        {
            DestroyRuntimeObject(fallbackMeshBatches[i]);
        }

        fallbackMeshBatches.Clear();
    }

    private void RebuildBatchesFromCache()
    {
        if (cachedCatalogBytes == null || cachedCatalogBytes.Length == 0)
        {
            LoadCatalogFromStreamingAssetsFile();
            return;
        }

        BuildBatches(cachedCatalogBytes, false);
    }

    private void RefreshStarWorldState()
    {
        for (int starIndex = 0; starIndex < visibleStars.Count; starIndex++)
        {
            StarInfo star = visibleStars[starIndex];
            Vector3 direction = TransformEquatorialJ2000Direction(star.EquatorialJ2000Direction);
            Quaternion rotation = Quaternion.LookRotation(-direction, PickStableUp(direction));
            float size = CalculateStarSize(star.Magnitude);

            star.Direction = direction;
            star.WorldPosition = direction * vaultRadius;
            star.IsAboveHorizon = IsAboveHorizon(direction);
            visibleStars[starIndex] = star;

            int batchIndex = starIndex / InstancesPerBatch;
            int indexInBatch = starIndex % InstancesPerBatch;
            matrixBatches[batchIndex][indexInBatch] = Matrix4x4.TRS(
                star.WorldPosition,
                rotation,
                Vector3.one * size
            );
        }

        celestialFrameRevision++;
    }

    private Vector3 TransformEquatorialJ2000Direction(Vector3 equatorialJ2000Direction)
    {
        Vector3 transformed = equatorialJ2000ToWorld.MultiplyVector(equatorialJ2000Direction);
        return transformed.sqrMagnitude > 0.0000001f ? transformed.normalized : equatorialJ2000Direction;
    }

    private bool IsAboveHorizon(Vector3 worldDirection)
    {
        if (!horizonClippingEnabled)
        {
            return true;
        }

        return worldDirection.y >= Mathf.Sin(minimumHorizonAltitudeDegrees * Mathf.Deg2Rad);
    }

    private float GetHorizonClipHeight()
    {
        return vaultRadius * Mathf.Sin(minimumHorizonAltitudeDegrees * Mathf.Deg2Rad);
    }

    private Vector3 GetSkyCenter()
    {
        return skyCenterAnchor != null ? skyCenterAnchor.position : Vector3.zero;
    }

    private void ClampSerializedValues()
    {
        magnitudeLimit = Mathf.Clamp(magnitudeLimit, -2f, 8f);
        vaultRadius = Mathf.Max(1f, vaultRadius);
        minStarSize = Mathf.Max(0.001f, minStarSize);
        maxStarSize = Mathf.Max(minStarSize, maxStarSize);
        globalIntensity = Mathf.Max(0f, globalIntensity);
        pointSoftness = Mathf.Clamp(pointSoftness, 0.25f, 8f);
        twinkleStrength = Mathf.Clamp01(twinkleStrength);
        twinkleSpeed = Mathf.Max(0f, twinkleSpeed);
        starSizeMultiplier = Mathf.Clamp(starSizeMultiplier, 0.25f, 8f);
        visibilityStep = Mathf.Max(0.01f, visibilityStep);
        starSizeStep = Mathf.Max(0.01f, starSizeStep);
        magnitudeStep = Mathf.Max(0.01f, magnitudeStep);
    }

    private void LoadCatalogFromStreamingAssetsFile()
    {
        LoadStarNamesFromStreamingAssetsFile();

        string catalogPath = BuildStreamingAssetsFilePath(catalogRelativePath);
        if (!File.Exists(catalogPath))
        {
            Debug.LogWarning($"No se encontro el catalogo HYG para preview en editor: {catalogPath}", this);
            return;
        }

        BuildBatches(File.ReadAllBytes(catalogPath));
    }

    private IEnumerator LoadStarNamesRoutine()
    {
        string namesUri = BuildStreamingAssetsUri(starNamesRelativePath);

        using (UnityWebRequest request = UnityWebRequest.Get(namesUri))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                starNames.Clear();
                Debug.LogWarning($"No se pudieron cargar nombres de estrellas desde {namesUri}: {request.error}", this);
                yield break;
            }

            starNames = ParseStarNamesJson(request.downloadHandler.text);
        }
    }

    private void LoadStarNamesFromStreamingAssetsFile()
    {
        string namesPath = BuildStreamingAssetsFilePath(starNamesRelativePath);
        if (!File.Exists(namesPath))
        {
            starNames.Clear();
            Debug.LogWarning($"No se encontro el archivo de nombres de estrellas: {namesPath}", this);
            return;
        }

        starNames = ParseStarNamesJson(File.ReadAllText(namesPath, Encoding.UTF8));
    }

    private void HandleRuntimeVisibilityKeys()
    {
        if (!enableRuntimeVisibilityKeys)
        {
            return;
        }

        bool materialChanged = false;
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
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
            {
                globalIntensity += visibilityStep;
                materialChanged = true;
            }

            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
            {
                globalIntensity = Mathf.Max(0f, globalIntensity - visibilityStep);
                materialChanged = true;
            }

            if (keyboard.rightBracketKey.wasPressedThisFrame)
            {
                starSizeMultiplier = Mathf.Clamp(starSizeMultiplier + starSizeStep, 0.25f, 8f);
                materialChanged = true;
            }

            if (keyboard.leftBracketKey.wasPressedThisFrame)
            {
                starSizeMultiplier = Mathf.Clamp(starSizeMultiplier - starSizeStep, 0.25f, 8f);
                materialChanged = true;
            }

            if (keyboard.pageUpKey.wasPressedThisFrame)
            {
                magnitudeLimit = Mathf.Clamp(magnitudeLimit + magnitudeStep, -2f, 8f);
                RebuildBatchesFromCache();
            }

            if (keyboard.pageDownKey.wasPressedThisFrame)
            {
                magnitudeLimit = Mathf.Clamp(magnitudeLimit - magnitudeStep, -2f, 8f);
                RebuildBatchesFromCache();
            }

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                magnitudeLimit = 7f;
                globalIntensity = 1.35f;
                starSizeMultiplier = 1.25f;
                RebuildBatchesFromCache();
                materialChanged = true;
            }
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!readInputSystem)
        {
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                globalIntensity += visibilityStep;
                materialChanged = true;
            }

            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                globalIntensity = Mathf.Max(0f, globalIntensity - visibilityStep);
                materialChanged = true;
            }

            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                starSizeMultiplier = Mathf.Clamp(starSizeMultiplier + starSizeStep, 0.25f, 8f);
                materialChanged = true;
            }

            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                starSizeMultiplier = Mathf.Clamp(starSizeMultiplier - starSizeStep, 0.25f, 8f);
                materialChanged = true;
            }

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                magnitudeLimit = Mathf.Clamp(magnitudeLimit + magnitudeStep, -2f, 8f);
                RebuildBatchesFromCache();
            }

            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                magnitudeLimit = Mathf.Clamp(magnitudeLimit - magnitudeStep, -2f, 8f);
                RebuildBatchesFromCache();
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                magnitudeLimit = 7f;
                globalIntensity = 1.35f;
                starSizeMultiplier = 1.25f;
                RebuildBatchesFromCache();
                materialChanged = true;
            }
        }
#endif

        if (materialChanged)
        {
            UpdateMaterialProperties();
        }
    }

    private Vector3 SphericalToUnityDirection(float rightAscensionRad, float declinationRad)
    {
        float yaw = rightAscensionRad + yawOffsetDegrees * Mathf.Deg2Rad;
        float cosDec = Mathf.Cos(declinationRad);

        return new Vector3(
            cosDec * Mathf.Sin(yaw),
            Mathf.Sin(declinationRad),
            cosDec * Mathf.Cos(yaw)
        ).normalized;
    }

    private float CalculateStarSize(float magnitude)
    {
        float bright01 = Mathf.InverseLerp(magnitudeLimit, -1.5f, magnitude);
        bright01 = Mathf.Pow(Mathf.Clamp01(bright01), 1.75f);
        return Mathf.Lerp(minStarSize, maxStarSize, bright01);
    }

    private Vector4 CalculateStarColor(float magnitude, float colorIndex)
    {
        Color color = ColorFromColorIndex(colorIndex);
        float bright01 = Mathf.InverseLerp(magnitudeLimit, -1.5f, magnitude);
        bright01 = Mathf.Pow(Mathf.Clamp01(bright01), 1.45f);

        float brightness = Mathf.Lerp(0.08f, 2.35f, bright01);
        float alpha = Mathf.Lerp(0.25f, 1f, bright01);
        return new Vector4(color.r * brightness, color.g * brightness, color.b * brightness, alpha);
    }

    private static Color ColorFromColorIndex(float colorIndex)
    {
        return StarDetailAppearanceFactory.ColorFromColorIndex(colorIndex);
    }

    private static string DecodeFixedString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        int count = Array.IndexOf(bytes, (byte)0);
        if (count < 0)
        {
            count = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes, 0, count).Trim();
    }

    private static Dictionary<int, string> ParseStarNamesJson(string json)
    {
        Dictionary<int, string> parsedNames = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return parsedNames;
        }

        MatchCollection matches = Regex.Matches(json, "\"(?<id>-?\\d+)\"\\s*:\\s*\"(?<name>(?:\\\\.|[^\"])*)\"");
        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Groups["id"].Value, out int hipId))
            {
                continue;
            }

            parsedNames[hipId] = DecodeJsonString(match.Groups["name"].Value);
        }

        return parsedNames;
    }

    private static string DecodeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
        {
            return value;
        }

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (current != '\\' || i + 1 >= value.Length)
            {
                builder.Append(current);
                continue;
            }

            char escaped = value[++i];
            switch (escaped)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escaped);
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    if (i + 4 < value.Length && TryParseHexCode(value, i + 1, out char unicodeChar))
                    {
                        builder.Append(unicodeChar);
                        i += 4;
                    }
                    break;
                default:
                    builder.Append(escaped);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryParseHexCode(string value, int startIndex, out char result)
    {
        result = '\0';
        if (startIndex < 0 || startIndex + 4 > value.Length)
        {
            return false;
        }

        int code = 0;
        for (int i = 0; i < 4; i++)
        {
            char current = value[startIndex + i];
            int digit;
            if (current >= '0' && current <= '9')
            {
                digit = current - '0';
            }
            else if (current >= 'a' && current <= 'f')
            {
                digit = current - 'a' + 10;
            }
            else if (current >= 'A' && current <= 'F')
            {
                digit = current - 'A' + 10;
            }
            else
            {
                return false;
            }

            code = (code << 4) + digit;
        }

        result = (char)code;
        return true;
    }

    private static bool HasValidFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static Vector3 PickStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.96f ? Vector3.forward : Vector3.up;
    }

    private static Mesh CreateStarQuad()
    {
        Mesh mesh = new Mesh
        {
            name = "Star Billboard Quad"
        };
        mesh.hideFlags = HideFlags.HideAndDontSave;

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };

        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static string BuildStreamingAssetsUri(string relativePath)
    {
        string cleanRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        string path = $"{Application.streamingAssetsPath}/{cleanRelativePath}";
        return path.IndexOf("://", StringComparison.Ordinal) >= 0 ? path : $"file://{path}";
    }

    private static string BuildStreamingAssetsFilePath(string relativePath)
    {
        string cleanRelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, '/');
        return Path.Combine(Application.streamingAssetsPath, cleanRelativePath);
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static void RequestEditorRepaint()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            SceneView.RepaintAll();
        }
#endif
    }
}
