using CosineKitty;
using UnityEngine;
using AstronomyEngine = CosineKitty.Astronomy;

namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// Astronomy Engine adapter. It calculates one common EQJ-to-horizon frame, then the
    /// renderer applies that frame to the complete HYG catalogue without per-star ephemeris calls.
    /// </summary>
    public sealed class AstronomyEngineLocalSkyTransform : ILocalSkyTransform
    {
        public SkyFrame CreateFrame(ObservationContext context)
        {
            AstroTime time = new AstroTime(context.UtcTime);
            Observer observer = new Observer(
                context.Location.LatitudeDegrees,
                context.Location.LongitudeDegrees,
                context.Location.ElevationMeters
            );

            RotationMatrix equatorialJ2000ToHorizon = AstronomyEngine.Rotation_EQJ_HOR(time, observer);
            return new SkyFrame(context, BuildUnityWorldMatrix(equatorialJ2000ToHorizon));
        }

        /// <summary>
        /// Astronomy Engine horizontal vectors use x=north, y=west, z=zenith.
        /// SAMAZ's Unity frame uses x=east, y=up, z=north.
        /// </summary>
        public static Vector3 HorizontalToUnity(Vector3 horizontalDirection)
        {
            return new Vector3(-horizontalDirection.y, horizontalDirection.z, horizontalDirection.x);
        }

        private static Matrix4x4 BuildUnityWorldMatrix(RotationMatrix equatorialJ2000ToHorizon)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, ToColumn(TransformEquatorialJ2000Direction(equatorialJ2000ToHorizon, Vector3.right)));
            matrix.SetColumn(1, ToColumn(TransformEquatorialJ2000Direction(equatorialJ2000ToHorizon, Vector3.up)));
            matrix.SetColumn(2, ToColumn(TransformEquatorialJ2000Direction(equatorialJ2000ToHorizon, Vector3.forward)));
            matrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            return matrix;
        }

        private static Vector3 TransformEquatorialJ2000Direction(RotationMatrix rotation, Vector3 unityEquatorialDirection)
        {
            // StellarVaultRenderer maps Astronomy Engine's EQJ axes as Unity (x=EQJ.y, y=EQJ.z, z=EQJ.x).
            double equatorialX = unityEquatorialDirection.z;
            double equatorialY = unityEquatorialDirection.x;
            double equatorialZ = unityEquatorialDirection.y;

            // This is deliberately identical to Astronomy.RotateVector, avoiding an AstroVector
            // allocation for each basis direction while keeping the upstream matrix convention.
            double horizonX = rotation.rot[0, 0] * equatorialX + rotation.rot[1, 0] * equatorialY + rotation.rot[2, 0] * equatorialZ;
            double horizonY = rotation.rot[0, 1] * equatorialX + rotation.rot[1, 1] * equatorialY + rotation.rot[2, 1] * equatorialZ;
            double horizonZ = rotation.rot[0, 2] * equatorialX + rotation.rot[1, 2] * equatorialY + rotation.rot[2, 2] * equatorialZ;

            return HorizontalToUnity(new Vector3((float)horizonX, (float)horizonY, (float)horizonZ)).normalized;
        }

        private static Vector4 ToColumn(Vector3 vector)
        {
            return new Vector4(vector.x, vector.y, vector.z, 0f);
        }
    }
}
