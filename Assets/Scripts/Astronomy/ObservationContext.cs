using System;

namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// The complete observer state used for a deterministic astronomical calculation.
    /// Times are always represented in UTC; timezone conversion belongs in presentation/UI code.
    /// </summary>
    public struct ObservationContext
    {
        public GeoCoordinate Location { get; }
        public DateTime UtcTime { get; }

        public ObservationContext(GeoCoordinate location, DateTime utcTime)
        {
            if (utcTime.Kind == DateTimeKind.Unspecified)
            {
                throw new ArgumentException("La fecha debe indicar explícitamente UTC o una zona horaria local.", nameof(utcTime));
            }

            Location = location;
            UtcTime = utcTime.ToUniversalTime();
        }
    }
}
