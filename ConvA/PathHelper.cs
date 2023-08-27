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
    public static bool HasCommonPath(string path, string projectPath) {
        int commonLength = 0;
        int commonPathLength = Math.Min(projectPath.Length, path.Length);
        for (int i = 0; i < commonPathLength; i++) {
            if (projectPath[projectPath.Length - 1 - i] != path[path.Length - 1 - i])
                break;
            commonLength++;
        }

        if (commonLength == commonPathLength) {
            return true;
        }

        return false;
    }
    public static string GetCommonPath(string path, string projectPath) {
        int commonLength = 0;
        int commonPathLength = Math.Min(projectPath.Length, path.Length);
        for (int i = 0; i < commonPathLength; i++) {
            if (projectPath[projectPath.Length - 1 - i] != path[path.Length - 1 - i])
                break;
            commonLength++;
        }

        if (commonLength == commonPathLength) {
            return path;
        }

        return path.Substring(path.Length - commonLength + 1);
    }
}