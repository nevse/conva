namespace ConvA;

public static class PathHelper {
    public static string ExpandPath(string path) {
        path = Environment.ExpandEnvironmentVariables(path);
        if (!path.StartsWith("~")) {
            return Path.GetFullPath(path);
        }

        string lastPath = path.Substring(1).TrimStart(Path.DirectorySeparatorChar);
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), lastPath);

        return Path.GetFullPath(path);
    }

    public static string ToPlatformPath(this string path) {
        return path
            .Replace('\\', System.IO.Path.DirectorySeparatorChar)
            .Replace('/', System.IO.Path.DirectorySeparatorChar);
    }
}