using System.Collections.Generic;
using System.Globalization;
using Samaz.Observatory.Astronomy;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space controls reserved for the TEST scene. It exposes the current observer heading
/// and location, provides curated city presets, and presents the first gaze-mission loop.
/// This is intentionally separate from the future diegetic VR UI.
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalSkyTestHud : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private LocalSkyController localSkyController;
    [SerializeField] private CelestialMissionController missionController;
    [SerializeField] private ConstellationOverlayRenderer constellationOverlayRenderer;
    [SerializeField] private Camera targetCamera;

    [Header("Actualización")]
    [SerializeField, Min(0.02f)] private float refreshIntervalSeconds = 0.1f;

    private readonly List<LocationButton> locationButtons = new List<LocationButton>();

    private GameObject canvasObject;
    private Text headingText;
    private Text locationText;
    private Text missionTitleText;
    private Text missionInstructionText;
    private Text missionStatusText;
    private Text constellationModeText;
    private Image missionProgressFill;
    private float nextRefreshTime;

    private static readonly Color PanelColor = new Color(0.012f, 0.022f, 0.05f, 0.91f);
    private static readonly Color SubPanelColor = new Color(0.035f, 0.07f, 0.13f, 0.96f);
    private static readonly Color TextColor = new Color(0.9f, 0.96f, 1f, 1f);
    private static readonly Color MutedTextColor = new Color(0.62f, 0.74f, 0.86f, 1f);
    private static readonly Color ActiveLocationColor = new Color(0.1f, 0.38f, 0.56f, 1f);
    private static readonly Color InactiveLocationColor = new Color(0.06f, 0.14f, 0.25f, 1f);

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureReferences();
        BuildHud();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureReferences();
        BuildHud();
        RefreshHud();
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
        {
            Destroy(canvasObject);
        }
    }

    private void OnValidate()
    {
        refreshIntervalSeconds = Mathf.Max(0.02f, refreshIntervalSeconds);
    }

    private void Update()
    {
        if (!Application.isPlaying || Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        EnsureReferences();
        RefreshHud();
    }

    private void BuildHud()
    {
        if (canvasObject != null)
        {
            return;
        }

        canvasObject = new GameObject("Local Sky Test HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildOrientationPanel(canvasObject.transform);
        BuildLocationAndMissionPanel(canvasObject.transform);
    }

    private void BuildOrientationPanel(Transform canvasTransform)
    {
        RectTransform panel = CreatePanel(
            "Observer Reference Panel",
            canvasTransform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-24f, -24f),
            new Vector2(390f, 156f),
            PanelColor
        );

        Text title = CreateText("Title", panel, 17, FontStyle.Bold, TextAnchor.UpperLeft, TextColor);
        SetTopRect(title.rectTransform, 16f, 14f, 358f, 26f);
        title.text = "REFERENCIA DEL JUGADOR";

        headingText = CreateText("Heading", panel, 20, FontStyle.Bold, TextAnchor.UpperLeft, TextColor);
        SetTopRect(headingText.rectTransform, 16f, 46f, 358f, 48f);

        locationText = CreateText("Location", panel, 14, FontStyle.Normal, TextAnchor.UpperLeft, MutedTextColor);
        SetTopRect(locationText.rectTransform, 16f, 99f, 358f, 44f);
    }

    private void BuildLocationAndMissionPanel(Transform canvasTransform)
    {
        RectTransform panel = CreatePanel(
            "Local Sky Test Controls",
            canvasTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(24f, -24f),
            new Vector2(390f, 890f),
            PanelColor
        );

        Text title = CreateText("Title", panel, 18, FontStyle.Bold, TextAnchor.UpperLeft, TextColor);
        SetTopRect(title.rectTransform, 16f, 14f, 358f, 27f);
        title.text = "UBICACIONES DE PRUEBA";

        Text explanation = CreateText("Explanation", panel, 13, FontStyle.Normal, TextAnchor.UpperLeft, MutedTextColor);
        SetTopRect(explanation.rectTransform, 16f, 43f, 358f, 36f);
        explanation.text = "Cambia de ciudad para verificar el horizonte y la orientación del cielo.";

        float buttonTop = 90f;
        IReadOnlyList<BuiltInSkyLocationDefinition> locations = BuiltInSkyLocations.All;
        for (int index = 0; index < locations.Count; index++)
        {
            BuiltInSkyLocationDefinition definition = locations[index];
            Button button = CreateButton(
                definition.DisplayName,
                panel,
                new Vector2(16f, -(buttonTop + index * 50f)),
                new Vector2(358f, 44f),
                InactiveLocationColor,
                out Image background
            );

            BuiltInSkyLocation locationId = definition.Id;
            button.onClick.AddListener(() => SelectLocation(locationId));
            locationButtons.Add(new LocationButton(definition, background));
        }

        Button constellationButton = CreateButton(
            "Trazado de constelaciones",
            panel,
            new Vector2(16f, -544f),
            new Vector2(358f, 36f),
            InactiveLocationColor,
            out _
        );
        constellationModeText = constellationButton.GetComponentInChildren<Text>(true);
        constellationModeText.alignment = TextAnchor.MiddleCenter;
        constellationModeText.fontSize = 12;
        constellationButton.onClick.AddListener(CycleConstellationMode);

        RectTransform missionPanel = CreatePanel(
            "Mission Panel",
            panel,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(12f, -590f),
            new Vector2(366f, 278f),
            SubPanelColor
        );

        Text missionHeader = CreateText("Mission Header", missionPanel, 15, FontStyle.Bold, TextAnchor.UpperLeft, TextColor);
        SetTopRect(missionHeader.rectTransform, 12f, 12f, 342f, 21f);
        missionHeader.text = "MISIÓN DE LOCALIZACIÓN";

        missionTitleText = CreateText("Mission Title", missionPanel, 18, FontStyle.Bold, TextAnchor.UpperLeft, TextColor);
        SetTopRect(missionTitleText.rectTransform, 12f, 38f, 342f, 26f);

        missionInstructionText = CreateText("Mission Instruction", missionPanel, 13, FontStyle.Normal, TextAnchor.UpperLeft, MutedTextColor);
        SetTopRect(missionInstructionText.rectTransform, 12f, 68f, 342f, 52f);

        missionStatusText = CreateText("Mission Status", missionPanel, 13, FontStyle.Normal, TextAnchor.UpperLeft, TextColor);
        SetTopRect(missionStatusText.rectTransform, 12f, 124f, 342f, 38f);

        RectTransform progressBackground = CreatePanel(
            "Mission Progress Background",
            missionPanel,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(12f, -169f),
            new Vector2(342f, 10f),
            new Color(0.01f, 0.02f, 0.04f, 1f)
        );
        missionProgressFill = CreatePanel(
            "Mission Progress Fill",
            progressBackground,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            new Color(0.14f, 0.72f, 0.9f, 1f)
        ).GetComponent<Image>();

        Button previousButton = CreateButton(
            "Anterior",
            missionPanel,
            new Vector2(12f, -196f),
            new Vector2(164f, 46f),
            InactiveLocationColor,
            out _
        );
        previousButton.onClick.AddListener(SelectPreviousMission);

        Button nextButton = CreateButton(
            "Siguiente misión",
            missionPanel,
            new Vector2(190f, -196f),
            new Vector2(164f, 46f),
            ActiveLocationColor,
            out _
        );
        nextButton.onClick.AddListener(SelectNextMission);

        Text controlsHint = CreateText("Controls Hint", missionPanel, 11, FontStyle.Normal, TextAnchor.MiddleCenter, MutedTextColor);
        SetTopRect(controlsHint.rectTransform, 12f, 248f, 342f, 16f);
        controlsHint.text = "C / botón: trazado · clic: inspeccionar · arrastra: girar";
    }

    private void RefreshHud()
    {
        if (headingText == null || locationText == null)
        {
            return;
        }

        RefreshOrientation();
        RefreshLocationButtons();
        RefreshConstellationOverlay();
        RefreshMission();
    }

    private void RefreshOrientation()
    {
        if (targetCamera == null)
        {
            headingText.text = "Rumbo: esperando cámara";
            locationText.text = "Ubicación: esperando controlador local";
            return;
        }

        Vector3 forward = targetCamera.transform.forward;
        float altitudeDegrees = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        forward.y = 0f;
        float virtualHeadingDegrees = forward.sqrMagnitude <= 0.0001f
            ? 0f
            : Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + 360f, 360f);
        float geographicHeadingDegrees = Mathf.Repeat(
            virtualHeadingDegrees - (localSkyController != null ? localSkyController.NorthYawDegrees : 0f),
            360f
        );

        headingText.text = string.Format(
            CultureInfo.InvariantCulture,
            "Rumbo: {0:000.0}° {1}\nAltura de mirada: {2:+0.0;-0.0;0.0}°",
            geographicHeadingDegrees,
            CompassPoint(geographicHeadingDegrees),
            altitudeDegrees
        );

        if (localSkyController == null || !localSkyController.HasActiveLocation)
        {
            locationText.text = "Ubicación: esperando coordenadas.";
            return;
        }

        GeoCoordinate location = localSkyController.ActiveLocation;
        string displayName = BuiltInSkyLocations.TryFindDefinition(location, out BuiltInSkyLocationDefinition definition)
            ? definition.DisplayName
            : "Coordenadas manuales";
        locationText.text = string.Format(
            CultureInfo.InvariantCulture,
            "{0}\nLat {1} · Lon {2} · {3:0} m",
            displayName,
            FormatLatitude(location.LatitudeDegrees),
            FormatLongitude(location.LongitudeDegrees),
            location.ElevationMeters
        );
    }

    private void RefreshLocationButtons()
    {
        if (localSkyController == null || !localSkyController.HasActiveLocation)
        {
            return;
        }

        GeoCoordinate activeLocation = localSkyController.ActiveLocation;
        for (int index = 0; index < locationButtons.Count; index++)
        {
            LocationButton locationButton = locationButtons[index];
            locationButton.Background.color = locationButton.Definition.Coordinate == activeLocation
                ? ActiveLocationColor
                : InactiveLocationColor;
        }
    }

    private void RefreshMission()
    {
        if (missionTitleText == null || missionInstructionText == null || missionStatusText == null || missionProgressFill == null)
        {
            return;
        }

        if (missionController == null || !missionController.HasMission)
        {
            missionTitleText.text = "Esperando misiones";
            missionInstructionText.text = "Añade CelestialMissionController a la cámara de pruebas.";
            missionStatusText.text = string.Empty;
            SetProgress(0f, InactiveLocationColor);
            return;
        }

        CelestialMissionDefinition mission = missionController.ActiveMission;
        missionTitleText.text = mission.title;
        missionInstructionText.text = mission.instruction;
        missionStatusText.text = missionController.StatusMessage;
        SetProgress(
            missionController.FocusProgress01,
            missionController.IsCompleted
                ? new Color(0.2f, 0.85f, 0.48f, 1f)
                : missionController.TargetInView ? new Color(0.14f, 0.72f, 0.9f, 1f) : InactiveLocationColor
        );
    }

    private void RefreshConstellationOverlay()
    {
        if (constellationModeText == null)
        {
            return;
        }

        if (constellationOverlayRenderer == null)
        {
            constellationModeText.text = "TRAZADO: esperando renderer";
            return;
        }

        string suffix = constellationOverlayRenderer.Mode == ConstellationOverlayMode.Mission
            ? constellationOverlayRenderer.ActiveConstellationDisplayName
            : string.Empty;
        constellationModeText.text = string.IsNullOrWhiteSpace(suffix)
            ? $"TRAZADO: {constellationOverlayRenderer.ModeDisplayName}"
            : $"TRAZADO: {constellationOverlayRenderer.ModeDisplayName} · {suffix}";
    }

    private void SelectLocation(BuiltInSkyLocation location)
    {
        if (localSkyController == null)
        {
            return;
        }

        localSkyController.SetBuiltInLocation(location);
        missionController?.ResetActiveMission();
        RefreshHud();
    }

    private void SelectNextMission()
    {
        missionController?.SelectNextMission();
        RefreshHud();
    }

    private void SelectPreviousMission()
    {
        missionController?.SelectPreviousMission();
        RefreshHud();
    }

    private void CycleConstellationMode()
    {
        constellationOverlayRenderer?.CycleVisibilityMode();
        RefreshHud();
    }

    private void SetProgress(float progress, Color color)
    {
        RectTransform fillRect = missionProgressFill.rectTransform;
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        missionProgressFill.color = color;
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

        if (localSkyController == null)
        {
            localSkyController = FindFirstObjectByType<LocalSkyController>();
        }

        if (missionController == null)
        {
            missionController = GetComponent<CelestialMissionController>();
        }

        if (constellationOverlayRenderer == null)
        {
            constellationOverlayRenderer = FindFirstObjectByType<ConstellationOverlayRenderer>();
        }
    }

    private static RectTransform CreatePanel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        Color color)
    {
        GameObject panelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(parent, false);

        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image image = panelObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rectTransform;
    }

    private static Button CreateButton(
        string label,
        Transform parent,
        Vector2 anchoredPosition,
        Vector2 size,
        Color backgroundColor,
        out Image background)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        background = buttonObject.GetComponent<Image>();
        background.color = backgroundColor;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.78f, 0.84f, 0.92f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        Text text = CreateText("Label", rectTransform, 14, FontStyle.Bold, TextAnchor.MiddleLeft, TextColor);
        text.text = label;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(14f, 0f);
        text.rectTransform.offsetMax = new Vector2(-10f, 0f);
        return button;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment,
        Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static void SetTopRect(RectTransform rectTransform, float left, float top, float width, float height)
    {
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(left, -top);
        rectTransform.sizeDelta = new Vector2(width, height);
    }

    private static string CompassPoint(float headingDegrees)
    {
        string[] points = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        int index = Mathf.RoundToInt(Mathf.Repeat(headingDegrees, 360f) / 45f) % points.Length;
        return points[index];
    }

    private static string FormatLatitude(double latitudeDegrees)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.0000}° {1}",
            System.Math.Abs(latitudeDegrees),
            latitudeDegrees >= 0d ? "N" : "S"
        );
    }

    private static string FormatLongitude(double longitudeDegrees)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.0000}° {1}",
            System.Math.Abs(longitudeDegrees),
            longitudeDegrees >= 0d ? "E" : "O"
        );
    }

    private sealed class LocationButton
    {
        public BuiltInSkyLocationDefinition Definition { get; }
        public Image Background { get; }

        public LocationButton(BuiltInSkyLocationDefinition definition, Image background)
        {
            Definition = definition;
            Background = background;
        }
    }
}
