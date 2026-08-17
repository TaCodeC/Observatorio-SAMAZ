using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class StarGazeInfoPanel : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private StellarVaultRenderer vaultRenderer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private StarDetailController starDetailController;

    [Header("Seleccion")]
    [SerializeField, Range(0.1f, 5f)] private float maxSelectionAngleDegrees = 0.75f;
    [SerializeField, Min(0.01f)] private float scanInterval = 0.05f;

    [Header("Panel")]
    [SerializeField, Min(180f)] private float panelWidth = 340f;
    [SerializeField, Min(120f)] private float panelHeight = 280f;
    [SerializeField, Min(8f)] private float panelPadding = 18f;
    [SerializeField] private Vector2 panelOffset = new Vector2(-32f, -32f);

    private GameObject canvasObject;
    private GameObject panelObject;
    private Text titleText;
    private Text detailsText;
    private float nextScanTime;
    private int lastHipId = int.MinValue;
    private string lastDisplayName = string.Empty;
    private bool lastWasSelected;

    private void Awake()
    {
        EnsureReferences();
        BuildPanel();
        HidePanel();
    }

    private void OnEnable()
    {
        EnsureReferences();
        BuildPanel();
        HidePanel();
    }

    private void OnDestroy()
    {
        if (canvasObject == null)
        {
            return;
        }

        Destroy(canvasObject);
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (Time.unscaledTime < nextScanTime)
        {
            return;
        }

        nextScanTime = Time.unscaledTime + scanInterval;
        EnsureReferences();

        if (starDetailController != null && starDetailController.HasSelectedStar)
        {
            ShowStar(starDetailController.SelectedStar, true, starDetailController.SelectedAppearance);
            return;
        }

        if (vaultRenderer == null || targetCamera == null)
        {
            HidePanel();
            return;
        }

        if (vaultRenderer.TryFindStarInView(targetCamera, maxSelectionAngleDegrees, out StellarVaultRenderer.StarInfo starInfo))
        {
            ShowStar(starInfo, false, default);
        }
        else
        {
            HidePanel();
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

        if (starDetailController == null)
        {
            starDetailController = GetComponent<StarDetailController>();
        }
    }

    private void BuildPanel()
    {
        if (panelObject != null)
        {
            return;
        }

        canvasObject = new GameObject("Star Gaze HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        panelObject = new GameObject("Star Info Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = panelOffset;
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.015f, 0.022f, 0.04f, 0.86f);
        panelImage.raycastTarget = false;

        titleText = CreateText("Star Name", panelRect, 24, FontStyle.Bold);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(panelPadding, -panelPadding);
        titleRect.sizeDelta = new Vector2(-panelPadding * 2f, 34f);

        detailsText = CreateText("Star Details", panelRect, 16, FontStyle.Normal);
        RectTransform detailsRect = detailsText.rectTransform;
        detailsRect.anchorMin = Vector2.zero;
        detailsRect.anchorMax = Vector2.one;
        detailsRect.offsetMin = new Vector2(panelPadding, panelPadding);
        detailsRect.offsetMax = new Vector2(-panelPadding, -(panelPadding + 44f));
    }

    private Text CreateText(string name, Transform parent, int fontSize, FontStyle fontStyle)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        if (font != null)
        {
            text.font = font;
        }

        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = new Color(0.92f, 0.96f, 1f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private void ShowStar(
        StellarVaultRenderer.StarInfo starInfo,
        bool isSelected,
        StarDetailAppearance detailAppearance)
    {
        if (panelObject == null)
        {
            BuildPanel();
        }

        string displayName = string.IsNullOrWhiteSpace(starInfo.DisplayName) ? "Estrella" : starInfo.DisplayName;
        if (panelObject.activeSelf
            && starInfo.HipId == lastHipId
            && displayName == lastDisplayName
            && isSelected == lastWasSelected)
        {
            return;
        }

        lastHipId = starInfo.HipId;
        lastDisplayName = displayName;
        lastWasSelected = isSelected;
        titleText.text = isSelected ? $"{displayName} · seleccionada" : displayName;
        detailsText.text = BuildDetails(starInfo, isSelected, detailAppearance);
        panelObject.SetActive(true);
    }

    private void HidePanel()
    {
        lastHipId = int.MinValue;
        lastDisplayName = string.Empty;
        lastWasSelected = false;

        if (panelObject != null)
        {
            panelObject.SetActive(false);
        }
    }

    private static string BuildDetails(
        StellarVaultRenderer.StarInfo starInfo,
        bool isSelected,
        StarDetailAppearance detailAppearance)
    {
        StringBuilder builder = new StringBuilder();

        if (starInfo.HipId > 0)
        {
            builder.AppendLine($"HIP: {starInfo.HipId}");
        }

        if (HasUsableFloat(starInfo.Magnitude))
        {
            builder.AppendLine($"Magnitud aparente: {Format(starInfo.Magnitude, "0.00")}");
        }

        if (HasUsableFloat(starInfo.DistanceParsecs) && starInfo.DistanceParsecs > 0f)
        {
            float lightYears = starInfo.DistanceParsecs * 3.26156f;
            builder.AppendLine($"Distancia: {Format(starInfo.DistanceParsecs, "0.0")} pc ({Format(lightYears, "0.0")} anos luz)");
        }

        if (!string.IsNullOrWhiteSpace(starInfo.SpectralType))
        {
            builder.AppendLine($"Tipo espectral: {starInfo.SpectralType}");
        }

        if (!string.IsNullOrWhiteSpace(starInfo.Constellation))
        {
            builder.AppendLine($"Constelacion IAU: {starInfo.Constellation.ToUpperInvariant()}");
        }

        if (HasUsableFloat(starInfo.Luminosity) && starInfo.Luminosity > 0f)
        {
            builder.AppendLine($"Luminosidad: {Format(starInfo.Luminosity, "0.###")} L solar");
        }

        if (HasUsableFloat(starInfo.ColorIndex))
        {
            builder.AppendLine($"Indice B-V: {Format(starInfo.ColorIndex, "0.00")}");
        }

        if (builder.Length == 0 || !starInfo.HasDatabaseInfo)
        {
            builder.Append("Sin datos adicionales en la base local.");
        }

        if (isSelected)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine(
                $"Modelo visual: {Format(detailAppearance.EstimatedTemperatureKelvin, "0")} K · "
                + $"{Format(detailAppearance.EstimatedRadiusSolarRadii, "0.##")} radios solares estimados"
            );
            builder.Append("Basado en B-V, tipo espectral y luminosidad. Esc: volver.");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Format(float value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static bool HasUsableFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
