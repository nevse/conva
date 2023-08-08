
namespace ConvA {
    public static class Platforms {
        public const string Android = "android";
        public const string iOS = "ios";
        public const string MacCatalyst = "maccatalyst";
        public const string Windows = "windows";

        public static bool IsAndroid(this string platform) {
            return platform == Android;
        }
        public static bool IsIPhone(this string platform) {
            return platform == iOS;
        }
        public static bool IsMacCatalyst(this string platform) {
            return platform == MacCatalyst;
        }
        public static bool IsWindows(this string platform) {
            return platform == Windows;
        }
    }
}