namespace InProcess.DevTools
{
    internal static class Conventions
    {
        public static IScreenshotHandler DefaultScreenshotHandler { get; } =
            new Screenshots.FilePickerHandler();
    }
}
