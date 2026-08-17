using System;
using System.Collections.Generic;

namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// Curated, code-owned locations for the local-sky test menu. Production content can use
    /// <see cref="LocationPreset"/> assets without changing this list.
    /// </summary>
    public enum BuiltInSkyLocation
    {
        GuanajuatoMexico,
        MazatlanMexico,
        MexicoCityMexico,
        GreenwichUnitedKingdom,
        QuitoEcuador,
        TromsoNorway,
        TokyoJapan,
        SydneyAustralia,
        UshuaiaArgentina
    }

    /// <summary>
    /// A named site suitable for displaying in a test UI. Coordinates use north/east-positive
    /// WGS84-style degrees and elevation in metres above mean sea level.
    /// </summary>
    public readonly struct BuiltInSkyLocationDefinition
    {
        public BuiltInSkyLocation Id { get; }
        public string DisplayName { get; }
        public string TestPurpose { get; }
        public GeoCoordinate Coordinate { get; }

        public BuiltInSkyLocationDefinition(
            BuiltInSkyLocation id,
            string displayName,
            string testPurpose,
            GeoCoordinate coordinate)
        {
            Id = id;
            DisplayName = displayName;
            TestPurpose = testPurpose;
            Coordinate = coordinate;
        }
    }

    public static class BuiltInSkyLocations
    {
        private static readonly BuiltInSkyLocationDefinition[] Definitions =
        {
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.GuanajuatoMexico,
                "Guanajuato, México",
                "Altitud elevada del altiplano mexicano.",
                new GeoCoordinate(21.0160975d, -101.2536214d, 2020d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.MazatlanMexico,
                "Mazatlán, México",
                "Costa del Pacífico y sede de SAMAZ.",
                new GeoCoordinate(23.2003158d, -106.4222214d, 10d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.MexicoCityMexico,
                "Ciudad de México, México",
                "Comparación con otra ciudad mexicana de gran altitud.",
                new GeoCoordinate(19.4333333d, -99.1333333d, 2240d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.GreenwichUnitedKingdom,
                "Greenwich, Reino Unido",
                "Referencia del meridiano cero.",
                new GeoCoordinate(51.4772222d, 0d, 46d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.QuitoEcuador,
                "Quito, Ecuador",
                "Casi sobre el ecuador terrestre.",
                new GeoCoordinate(-0.215d, -78.5025d, 2823d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.TromsoNorway,
                "Tromsø, Noruega",
                "Latitud dentro del círculo polar ártico.",
                new GeoCoordinate(69.6493d, 18.95571d, 5d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.TokyoJapan,
                "Tokio, Japón",
                "Longitud oriental y hemisferio norte.",
                new GeoCoordinate(35.6916667d, 139.75d, 6d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.SydneyAustralia,
                "Sídney, Australia",
                "Hemisferio sur y longitud oriental.",
                new GeoCoordinate(-33.8607d, 151.205d, 39d)),
            new BuiltInSkyLocationDefinition(
                BuiltInSkyLocation.UshuaiaArgentina,
                "Ushuaia, Argentina",
                "Latitud austral extrema para pruebas de hemisferio.",
                new GeoCoordinate(-54.8108889d, -68.2956944d, 25d))
        };

        public static IReadOnlyList<BuiltInSkyLocationDefinition> All => Definitions;

        public static GeoCoordinate Get(BuiltInSkyLocation location)
        {
            return GetDefinition(location).Coordinate;
        }

        public static BuiltInSkyLocationDefinition GetDefinition(BuiltInSkyLocation location)
        {
            for (int index = 0; index < Definitions.Length; index++)
            {
                if (Definitions[index].Id == location)
                {
                    return Definitions[index];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(location), location, "Ubicación predefinida no soportada.");
        }

        public static bool TryFindDefinition(GeoCoordinate coordinate, out BuiltInSkyLocationDefinition definition)
        {
            for (int index = 0; index < Definitions.Length; index++)
            {
                if (Definitions[index].Coordinate == coordinate)
                {
                    definition = Definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }
    }
}
