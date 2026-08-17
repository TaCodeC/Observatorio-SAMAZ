using System;

namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// Geographic coordinates used by the sky simulation.
    /// Latitude is positive north of the equator; longitude is positive east of Greenwich.
    /// </summary>
    public struct GeoCoordinate : IEquatable<GeoCoordinate>
    {
        public const double MinimumElevationMeters = -1000d;
        public const double MaximumElevationMeters = 100000d;

        public double LatitudeDegrees { get; }
        public double LongitudeDegrees { get; }
        public double ElevationMeters { get; }

        public GeoCoordinate(double latitudeDegrees, double longitudeDegrees, double elevationMeters = 0d)
        {
            if (!IsFinite(latitudeDegrees) || latitudeDegrees < -90d || latitudeDegrees > 90d)
            {
                throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), "La latitud debe estar entre -90 y 90 grados.");
            }

            if (!IsFinite(longitudeDegrees))
            {
                throw new ArgumentOutOfRangeException(nameof(longitudeDegrees), "La longitud debe ser un número finito.");
            }

            if (!IsFinite(elevationMeters) || elevationMeters < MinimumElevationMeters || elevationMeters > MaximumElevationMeters)
            {
                throw new ArgumentOutOfRangeException(nameof(elevationMeters), $"La elevación debe estar entre {MinimumElevationMeters} y {MaximumElevationMeters} metros.");
            }

            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = NormalizeLongitude(longitudeDegrees);
            ElevationMeters = elevationMeters;
        }

        public bool Equals(GeoCoordinate other)
        {
            return LatitudeDegrees.Equals(other.LatitudeDegrees)
                && LongitudeDegrees.Equals(other.LongitudeDegrees)
                && ElevationMeters.Equals(other.ElevationMeters);
        }

        public override bool Equals(object obj)
        {
            return obj is GeoCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = LatitudeDegrees.GetHashCode();
                hashCode = (hashCode * 397) ^ LongitudeDegrees.GetHashCode();
                hashCode = (hashCode * 397) ^ ElevationMeters.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"{LatitudeDegrees:F6}°, {LongitudeDegrees:F6}°, {ElevationMeters:F1} m";
        }

        public static bool operator ==(GeoCoordinate left, GeoCoordinate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GeoCoordinate left, GeoCoordinate right)
        {
            return !left.Equals(right);
        }

        public static double NormalizeLongitude(double longitudeDegrees)
        {
            double normalized = longitudeDegrees % 360d;
            if (normalized >= 180d)
            {
                normalized -= 360d;
            }
            else if (normalized < -180d)
            {
                normalized += 360d;
            }

            return normalized;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
