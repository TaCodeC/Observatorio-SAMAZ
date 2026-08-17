using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Topology for the modern western constellation figures.  The catalogue stores HIP
/// identifiers only; their positions always come from <see cref="StellarVaultRenderer"/>
/// and therefore use the same HYG data, local frame, and horizon as the stars themselves.
/// </summary>
[Serializable]
public sealed class ConstellationLineCatalog
{
    public int schemaVersion;
    public string sourceName;
    public string sourceRepository;
    public string sourceCommit;
    public string sourceLicense;
    public string sourceFileSha256;
    public ConstellationLineFigure[] figures;

    private Dictionary<string, ConstellationLineFigure> figuresByCode;

    public IReadOnlyList<ConstellationLineFigure> Figures => figures ?? Array.Empty<ConstellationLineFigure>();
    public int FigureCount => figures == null ? 0 : figures.Length;

    public int PolylineCount
    {
        get
        {
            int total = 0;
            for (int index = 0; index < FigureCount; index++)
            {
                total += Figures[index].PolylineCount;
            }

            return total;
        }
    }

    public int SegmentCount
    {
        get
        {
            int total = 0;
            for (int index = 0; index < FigureCount; index++)
            {
                total += Figures[index].SegmentCount;
            }

            return total;
        }
    }

    public bool TryGetFigure(string code, out ConstellationLineFigure figure)
    {
        figure = null;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        EnsureIndex();
        return figuresByCode.TryGetValue(code.Trim(), out figure);
    }

    internal void Validate()
    {
        if (schemaVersion != 1)
        {
            throw new InvalidOperationException($"Versión no compatible del catálogo de constelaciones: {schemaVersion}.");
        }

        if (figures == null || figures.Length == 0)
        {
            throw new InvalidOperationException("El catálogo de constelaciones no contiene figuras.");
        }

        EnsureIndex();
        for (int figureIndex = 0; figureIndex < figures.Length; figureIndex++)
        {
            ConstellationLineFigure figure = figures[figureIndex];
            if (figure == null || string.IsNullOrWhiteSpace(figure.code))
            {
                throw new InvalidOperationException("El catálogo de constelaciones contiene una figura sin código.");
            }

            if (figure.polylines == null || figure.polylines.Length == 0)
            {
                throw new InvalidOperationException($"La figura {figure.code} no contiene polilíneas.");
            }

            for (int pathIndex = 0; pathIndex < figure.polylines.Length; pathIndex++)
            {
                ConstellationLinePath path = figure.polylines[pathIndex];
                if (path == null || path.hipIds == null || path.hipIds.Length < 2)
                {
                    throw new InvalidOperationException($"La figura {figure.code} contiene una polilínea inválida.");
                }
            }
        }
    }

    private void EnsureIndex()
    {
        if (figuresByCode != null)
        {
            return;
        }

        figuresByCode = new Dictionary<string, ConstellationLineFigure>(StringComparer.OrdinalIgnoreCase);
        if (figures == null)
        {
            return;
        }

        for (int index = 0; index < figures.Length; index++)
        {
            ConstellationLineFigure figure = figures[index];
            if (figure == null || string.IsNullOrWhiteSpace(figure.code))
            {
                continue;
            }

            if (figuresByCode.ContainsKey(figure.code))
            {
                throw new InvalidOperationException($"El catálogo repite el código de constelación {figure.code}.");
            }

            figuresByCode.Add(figure.code, figure);
        }
    }
}

[Serializable]
public sealed class ConstellationLineFigure
{
    public string code;
    public string displayName;
    public ConstellationLinePath[] polylines;

    public string Code => code;
    public string DisplayName => displayName;
    public IReadOnlyList<ConstellationLinePath> Polylines => polylines ?? Array.Empty<ConstellationLinePath>();
    public int PolylineCount => polylines == null ? 0 : polylines.Length;

    public int SegmentCount
    {
        get
        {
            int total = 0;
            for (int index = 0; index < PolylineCount; index++)
            {
                total += Mathf.Max(0, Polylines[index].HipCount - 1);
            }

            return total;
        }
    }
}

[Serializable]
public sealed class ConstellationLinePath
{
    public int[] hipIds;

    public IReadOnlyList<int> HipIds => hipIds ?? Array.Empty<int>();
    public int HipCount => hipIds == null ? 0 : hipIds.Length;
}

/// <summary>
/// Loads the generated resource.  This is intentionally one small JSON asset rather than a
/// network dependency, so a Quest build can render constellations fully offline.
/// </summary>
public static class ConstellationLineCatalogLoader
{
    public const string ResourcePath = "Constellations/ModernConstellationLines";

    private static ConstellationLineCatalog cachedCatalog;

    public static ConstellationLineCatalog Load()
    {
        if (cachedCatalog != null)
        {
            return cachedCatalog;
        }

        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            throw new InvalidOperationException(
                $"No se encontró Resources/{ResourcePath}.json para las líneas de constelaciones."
            );
        }

        ConstellationLineCatalog loaded = JsonUtility.FromJson<ConstellationLineCatalog>(asset.text);
        if (loaded == null)
        {
            throw new InvalidOperationException("No se pudo deserializar el catálogo de constelaciones.");
        }

        loaded.Validate();
        cachedCatalog = loaded;
        return cachedCatalog;
    }

    public static bool TryLoad(out ConstellationLineCatalog catalog, out string error)
    {
        try
        {
            catalog = Load();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            catalog = null;
            error = exception.Message;
            return false;
        }
    }
}
