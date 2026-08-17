using System;
using UnityEngine;

namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// A single, immutable J2000-to-local-sky transformation for one observer and instant.
    /// Unity convention: +X east, +Y zenith, +Z north before optional north calibration.
    /// </summary>
    public struct SkyFrame
    {
        public ObservationContext Context { get; }
        public Matrix4x4 EquatorialJ2000ToWorld { get; }

        public SkyFrame(ObservationContext context, Matrix4x4 equatorialJ2000ToWorld)
        {
            Context = context;
            EquatorialJ2000ToWorld = equatorialJ2000ToWorld;
        }

        public Vector3 TransformEquatorialJ2000Direction(Vector3 equatorialDirection)
        {
            if (equatorialDirection.sqrMagnitude <= 0.0000001f)
            {
                throw new ArgumentException("La dirección ecuatorial no puede ser cero.", nameof(equatorialDirection));
            }

            return EquatorialJ2000ToWorld.MultiplyVector(equatorialDirection).normalized;
        }

        public bool IsAboveHorizon(Vector3 equatorialDirection, float minimumAltitudeDegrees = 0f)
        {
            float horizonY = Mathf.Sin(minimumAltitudeDegrees * Mathf.Deg2Rad);
            return TransformEquatorialJ2000Direction(equatorialDirection).y >= horizonY;
        }

        public SkyFrame WithNorthYaw(float northYawDegrees)
        {
            Matrix4x4 calibration = Matrix4x4.Rotate(Quaternion.Euler(0f, northYawDegrees, 0f));
            return new SkyFrame(Context, calibration * EquatorialJ2000ToWorld);
        }
    }
}
