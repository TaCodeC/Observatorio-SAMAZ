namespace Samaz.Observatory.Astronomy
{
    /// <summary>
    /// Converts the catalogue's fixed J2000 equatorial frame into the local Unity sky frame.
    /// This seam keeps rendering and UI independent from the astronomical library in use.
    /// </summary>
    public interface ILocalSkyTransform
    {
        SkyFrame CreateFrame(ObservationContext context);
    }
}
