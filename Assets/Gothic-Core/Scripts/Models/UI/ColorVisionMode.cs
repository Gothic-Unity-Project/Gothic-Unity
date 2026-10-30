namespace Gothic.Core.Models.UI
{
    /// <summary>
    /// Colors adjusted for a kind of color blindness (ColorVisionService). Stored as the option's number.
    /// </summary>
    public enum ColorVisionMode
    {
        Normal = 0,
        // Red-weak.
        Protanopia = 1,
        // Green-weak.
        Deuteranopia = 2,
        // Blue-weak.
        Tritanopia = 3
    }
}
