namespace ChefKnifeStudios.PokerAttack.Client.Shared.Constants;

public static class ColorConstants
{
    /// <summary>
    /// Material 3 Baseline Light Color Scheme
    /// https://m3.material.io/styles/color/static/baseline
    /// </summary>
    public static class LightNotUsed
    {
        public const string Primary = "#6750A4";
        public const string OnPrimary = "#FFFFFF";
        public const string PrimaryContainer = "#EADDFF";
        public const string OnPrimaryContainer = "#21005D";

        public const string Secondary = "#625B71";
        public const string OnSecondary = "#FFFFFF";
        public const string SecondaryContainer = "#E8DEF8";
        public const string OnSecondaryContainer = "#1D192B";

        public const string Tertiary = "#7D5260";
        public const string OnTertiary = "#FFFFFF";
        public const string TertiaryContainer = "#FFD8E4";
        public const string OnTertiaryContainer = "#31111D";

        public const string Error = "#B3261E";
        public const string OnError = "#FFFFFF";
        public const string ErrorContainer = "#F9DEDC";
        public const string OnErrorContainer = "#410E0B";

        public const string Outline = "#79747E";

        public const string Surface = "#FFFBFE";
        public const string OnSurface = "#1C1B1F";
        public const string SurfaceVariant = "#E7E0EC";
        public const string OnSurfaceVariant = "#49454F";

        public const string Background = "#FFFBFE";
        public const string OnBackground = "#1C1B1F";
    }

    /// <summary>
    /// Material 3 Baseline Dark Color Scheme
    /// https://m3.material.io/styles/color/static/baseline
    /// </summary>
    public static class DarkNotUsed
    {
        public const string Primary = "#D0BCFF";
        public const string OnPrimary = "#381E72";
        public const string PrimaryContainer = "#4F378B";
        public const string OnPrimaryContainer = "#EADDFF";

        public const string Secondary = "#CCC2DC";
        public const string OnSecondary = "#332D41";
        public const string SecondaryContainer = "#4A4458";
        public const string OnSecondaryContainer = "#E8DEF8";

        public const string Tertiary = "#EFB8C8";
        public const string OnTertiary = "#492532";
        public const string TertiaryContainer = "#633B48";
        public const string OnTertiaryContainer = "#FFD8E4";

        public const string Error = "#F2B8B5";
        public const string OnError = "#601410";
        public const string ErrorContainer = "#8C1D18";
        public const string OnErrorContainer = "#F9DEDC";

        public const string Outline = "#938F99";

        public const string Surface = "#1C1B1F";
        public const string OnSurface = "#E6E1E5";
        public const string SurfaceVariant = "#49454F";
        public const string OnSurfaceVariant = "#CAC4D0";

        public const string Background = "#1C1B1F";
        public const string OnBackground = "#E6E1E5";
    }

    public static class Light
    {
        // Primary - Deep Blue (#214e7e)
        public const string Primary = "#214E7E";
        public const string OnPrimary = "#FFFFFF";
        public const string PrimaryContainer = "#D7E2FF";
        public const string OnPrimaryContainer = "#001B3F";

        // Secondary - Steel Blue
        // Note: User's #96aacc is used as Container for better visibility
        // The main 'Secondary' is darker for text accessibility.
        public const string Secondary = "#385E7F";
        public const string OnSecondary = "#FFFFFF";
        public const string SecondaryContainer = "#96AACC";
        public const string OnSecondaryContainer = "#0E1D2A";

        // Tertiary - Complementary Slate/Violet
        public const string Tertiary = "#65587B";
        public const string OnTertiary = "#FFFFFF";
        public const string TertiaryContainer = "#ECDCFF";
        public const string OnTertiaryContainer = "#211634";

        // Error
        public const string Error = "#BA1A1A";
        public const string OnError = "#FFFFFF";
        public const string ErrorContainer = "#FFDAD6";
        public const string OnErrorContainer = "#410002";

        // Outline
        public const string Outline = "#74777F";

        // Surface & Background (Cool White Tint)
        public const string Surface = "#F5F9FF";
        public const string OnSurface = "#191C20";
        public const string SurfaceVariant = "#E0E2EC";
        public const string OnSurfaceVariant = "#43474E";

        public const string Background = "#F5F9FF";
        public const string OnBackground = "#191C20";
    }

    public static class Dark
    {
        // Primary - Pastel Blue (Lighter version of #214E7E for visibility)
        public const string Primary = "#A9C7FF"; 
        public const string OnPrimary = "#003062";     // Deep blue text on the pastel button
        public const string PrimaryContainer = "#004786"; // A rich mid-tone blue
        public const string OnPrimaryContainer = "#D7E2FF";

        // Secondary - Ice Blue
        // Lighter version of your #96AACC to pop against dark grey
        public const string Secondary = "#B1CBED"; 
        public const string OnSecondary = "#1C344E";
        public const string SecondaryContainer = "#334A66";
        public const string OnSecondaryContainer = "#CFE5FF";

        // Tertiary - Muted Lavender
        public const string Tertiary = "#D0BCFF";
        public const string OnTertiary = "#36264F";
        public const string TertiaryContainer = "#4D3D67";
        public const string OnTertiaryContainer = "#E9DDFF";

        // Error
        public const string Error = "#FFB4AB";
        public const string OnError = "#690005";
        public const string ErrorContainer = "#93000A";
        public const string OnErrorContainer = "#FFDAD6";

        // Outline
        public const string Outline = "#8E9099";

        // Surface & Background (Deep "Midnight" Grey)
        public const string Surface = "#1E1F20";
        public const string OnSurface = "#E2E2E6";
        public const string SurfaceVariant = "#43474E";
        public const string OnSurfaceVariant = "#C3C7CF";

        public const string Background = "#111318";
        public const string OnBackground = "#E2E2E6";
    }
}
