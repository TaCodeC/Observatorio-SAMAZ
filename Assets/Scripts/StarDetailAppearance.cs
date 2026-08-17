using System;
using UnityEngine;

/// <summary>
/// Visual parameters derived from the photometric fields currently kept in the local HYG
/// catalogue. These are intentionally presentation values, not a claim about a star's measured
/// surface chemistry or a physically resolved stellar atmosphere.
/// </summary>
public struct StarDetailAppearance
{
    public float EstimatedTemperatureKelvin;
    public float EstimatedRadiusSolarRadii;
    public float VisualDiameter;
    public Color PhotosphereColor;
    public Color CellColor;
    public Color RimColor;
    public float EmissionStrength;
    public float GranulationScale;
    public float GranulationSpeed;
    public float Activity;
    public float TimeSeed;
}

/// <summary>
/// Keeps the conversion from catalogue photometry to the one-star detail shader in one place.
/// When an enriched HIP/Gaia detail catalogue is introduced, it can replace this estimator while
/// retaining the controller and shader interfaces.
/// </summary>
public static class StarDetailAppearanceFactory
{
    private const float SolarTemperatureKelvin = 5772f;

    public static StarDetailAppearance Create(StellarVaultRenderer.StarInfo starInfo)
    {
        float temperature = EstimateTemperatureKelvin(starInfo.ColorIndex, starInfo.SpectralType);
        float radius = EstimateRadiusSolarRadii(starInfo.Luminosity, temperature);
        float radius01 = Mathf.InverseLerp(-1.3f, 2.5f, Mathf.Log10(Mathf.Max(0.05f, radius)));
        float luminosity01 = HasPositiveFiniteValue(starInfo.Luminosity)
            ? Mathf.InverseLerp(-2f, 5f, Mathf.Log10(starInfo.Luminosity))
            : Mathf.InverseLerp(7f, -1.5f, starInfo.Magnitude);
        float temperature01 = Mathf.InverseLerp(2600f, 30000f, temperature);
        SpectralSurfaceProfile surface = ResolveSurfaceProfile(starInfo.SpectralType, temperature);
        Color photosphereColor = ColorFromTemperature(temperature);

        return new StarDetailAppearance
        {
            EstimatedTemperatureKelvin = temperature,
            EstimatedRadiusSolarRadii = radius,
            VisualDiameter = Mathf.Lerp(0.82f, 1.55f, radius01),
            PhotosphereColor = photosphereColor,
            CellColor = Color.Lerp(photosphereColor, new Color(0.22f, 0.08f, 0.02f, 1f), surface.CellContrast),
            RimColor = Color.Lerp(photosphereColor, Color.white, 0.28f + temperature01 * 0.25f),
            EmissionStrength = Mathf.Lerp(1.5f, 7.5f, luminosity01) + temperature01 * 1.5f,
            GranulationScale = surface.GranulationScale,
            GranulationSpeed = surface.GranulationSpeed,
            Activity = surface.Activity,
            TimeSeed = ResolveTimeSeed(starInfo)
        };
    }

    /// <summary>
    /// Approximation from B-V colour index. If HYG has no colour value, the broad spectral class
    /// provides a conservative visual fallback.
    /// </summary>
    public static float EstimateTemperatureKelvin(float colorIndex, string spectralType)
    {
        if (IsFinite(colorIndex))
        {
            float bv = Mathf.Clamp(colorIndex, -0.4f, 2f);
            return Mathf.Clamp(
                4600f * ((1f / (0.92f * bv + 1.7f)) + (1f / (0.92f * bv + 0.62f))),
                2300f,
                40000f
            );
        }

        switch (GetSpectralClass(spectralType))
        {
            case 'O': return 30000f;
            case 'B': return 15000f;
            case 'A': return 8500f;
            case 'F': return 6750f;
            case 'G': return 5750f;
            case 'K': return 4500f;
            case 'M': return 3200f;
            default: return SolarTemperatureKelvin;
        }
    }

    /// <summary>
    /// Stefan-Boltzmann rearrangement in solar units: R/Rsun = sqrt(L/Lsun) * (Tsun/T)^2.
    /// It is an estimator because HYG's B-V-derived temperature is not a direct measurement.
    /// </summary>
    public static float EstimateRadiusSolarRadii(float luminositySolar, float temperatureKelvin)
    {
        if (!HasPositiveFiniteValue(luminositySolar) || !HasPositiveFiniteValue(temperatureKelvin))
        {
            return 1f;
        }

        float radius = Mathf.Sqrt(luminositySolar) * Mathf.Pow(SolarTemperatureKelvin / temperatureKelvin, 2f);
        return Mathf.Clamp(radius, 0.05f, 1000f);
    }

    public static Color ColorFromColorIndex(float colorIndex)
    {
        return IsFinite(colorIndex)
            ? ColorFromTemperature(EstimateTemperatureKelvin(colorIndex, null))
            : Color.white;
    }

    public static Color ColorFromTemperature(float kelvin)
    {
        float temperature = Mathf.Clamp(kelvin, 1000f, 40000f) / 100f;
        float red;
        float green;
        float blue;

        if (temperature <= 66f)
        {
            red = 255f;
            green = 99.4708025861f * Mathf.Log(temperature) - 161.1195681661f;
            blue = temperature <= 19f ? 0f : 138.5177312231f * Mathf.Log(temperature - 10f) - 305.0447927307f;
        }
        else
        {
            red = 329.698727446f * Mathf.Pow(temperature - 60f, -0.1332047592f);
            green = 288.1221695283f * Mathf.Pow(temperature - 60f, -0.0755148492f);
            blue = 255f;
        }

        return new Color(
            Mathf.Clamp01(red / 255f),
            Mathf.Clamp01(green / 255f),
            Mathf.Clamp01(blue / 255f),
            1f
        );
    }

    private static float ResolveTimeSeed(StellarVaultRenderer.StarInfo starInfo)
    {
        if (IsFinite(starInfo.TwinkleSeed))
        {
            return Mathf.Repeat(starInfo.TwinkleSeed, 1f);
        }

        return Mathf.Repeat(starInfo.HipId * 0.61803398875f, 1f);
    }

    private static SpectralSurfaceProfile ResolveSurfaceProfile(string spectralType, float temperature)
    {
        switch (GetSpectralClass(spectralType))
        {
            case 'O':
            case 'B':
            case 'A':
                return new SpectralSurfaceProfile(1.25f, 0.28f, 0.95f, 0.45f);
            case 'F':
            case 'G':
                return new SpectralSurfaceProfile(3.7f, 0.48f, 0.56f, 0.82f);
            case 'K':
            case 'M':
                return new SpectralSurfaceProfile(5.5f, 0.28f, 0.48f, 0.62f);
            default:
                float warm01 = Mathf.InverseLerp(3000f, 12000f, temperature);
                return new SpectralSurfaceProfile(
                    Mathf.Lerp(5.2f, 2.1f, warm01),
                    Mathf.Lerp(0.3f, 0.45f, warm01),
                    Mathf.Lerp(0.5f, 0.7f, warm01),
                    Mathf.Lerp(0.6f, 0.75f, warm01)
                );
        }
    }

    private static char GetSpectralClass(string spectralType)
    {
        if (string.IsNullOrWhiteSpace(spectralType))
        {
            return '\0';
        }

        for (int index = 0; index < spectralType.Length; index++)
        {
            char candidate = char.ToUpperInvariant(spectralType[index]);
            if (candidate == 'O' || candidate == 'B' || candidate == 'A' || candidate == 'F'
                || candidate == 'G' || candidate == 'K' || candidate == 'M')
            {
                return candidate;
            }
        }

        return '\0';
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool HasPositiveFiniteValue(float value)
    {
        return IsFinite(value) && value > 0f;
    }

    private readonly struct SpectralSurfaceProfile
    {
        public readonly float GranulationScale;
        public readonly float GranulationSpeed;
        public readonly float CellContrast;
        public readonly float Activity;

        public SpectralSurfaceProfile(float granulationScale, float granulationSpeed, float cellContrast, float activity)
        {
            GranulationScale = granulationScale;
            GranulationSpeed = granulationSpeed;
            CellContrast = cellContrast;
            Activity = activity;
        }
    }
}
