using System;
using UnityEngine;

namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// Reusable location data for a future VR menu. It contains no location history or user data.
    /// </summary>
    [CreateAssetMenu(fileName = "LocationPreset", menuName = "SAMAZ/Astronomía/Ubicación predefinida")]
    public sealed class LocationPreset : ScriptableObject
    {
        [SerializeField] private string displayName = "Ubicación";
        [SerializeField, Range(-90f, 90f)] private double latitudeDegrees;
        [SerializeField, Range(-180f, 180f)] private double longitudeDegrees;
        [SerializeField, Min(-1000f)] private double elevationMeters;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        public GeoCoordinate ToGeoCoordinate()
        {
            return new GeoCoordinate(latitudeDegrees, longitudeDegrees, elevationMeters);
        }
    }
}
