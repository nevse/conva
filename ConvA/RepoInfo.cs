using System.Text.RegularExpressions;

namespace ConvA;

public class RepoInfo {
    const string ProjectsBasePath = "xamarin/maui";
    const string NuspecBasePath = "nuspec";

    public RepoInfo(string path) {
        BasePath = path;
    }

    string BasePath { get; }
    public Dictionary<string, string> ProjectsByNameDictionary { get; } = new();
    public Dictionary<string, PackageInfo> PackagesByNameDictionary { get; } = new();
    public string? DevExpressDataVersion { get; set; }

    public void Build() {
        DirectoryInfo nuspecDirectory = new(Path.Combine(BasePath, NuspecBasePath.ToPlatformPath()));
        foreach (FileInfo nuspecFileInfo in
                 nuspecDirectory.EnumerateFiles("*.nuspec", SearchOption.AllDirectories)) {
            PackageInfo packageInfo = new(nuspecFileInfo.FullName);
            packageInfo.Parse();
            if (packageInfo.Id != null) {
                PackagesByNameDictionary[packageInfo.Id] = packageInfo;
            }
        }

        DirectoryInfo projectDirectory = new(Path.Combine(BasePath, ProjectsBasePath.ToPlatformPath()));
        foreach (FileInfo projectPath in projectDirectory.EnumerateFiles("*.csproj", SearchOption.AllDirectories)) {
            Project project = new(projectPath.FullName);
            if (String.IsNullOrEmpty(DevExpressDataVersion)) {
                DevExpressDataVersion = project.EvaluateProperty("DevExpress_Data");
            }
            string? assemblyName = project.EvaluateProperty("AssemblyName");
            if (Path.GetFileNameWithoutExtension(projectPath.FullName) != assemblyName)
                continue;
            ProjectsByNameDictionary.Add(assemblyName, projectPath.FullName);
        }
    }

    public Dictionary<string, string> GetAndroidReferences(string packageName) {
        PackageInfo package = PackagesByNameDictionary[packageName];
        return package.ReferencesAndroid;
    }

    public Dictionary<string, string> GetIosReferences(string packageName) {
        PackageInfo package = PackagesByNameDictionary[packageName];
        return package.ReferencesIos;
    }

    public IEnumerable<string> CalculateDependencies(string packageReferenceName, HashSet<string>? visited = null) {
        visited ??= new HashSet<string>();
        if (!PackagesByNameDictionary.TryGetValue(packageReferenceName, out PackageInfo? package))
            yield break;
        foreach (string packageReference in package.Dependencies) {
            if (visited.Contains(packageReference))
                continue;
            yield return packageReference;
            foreach (var dependency in CalculateDependencies(packageReference, visited)) {
                if (visited.Contains(dependency))
                    continue;
                yield return dependency;
            }
        }
    }

    public string GetProjectPath(string reference) {
        return ProjectsByNameDictionary[reference];
    }

    public PackageInfo? GetPackageFromReference(Reference projectReference) {
        if (projectReference.Name != null && !ProjectsByNameDictionary.ContainsKey(projectReference.Name))
            return null;
        foreach (var pair in PackagesByNameDictionary) {
            PackageInfo packageFromReference = pair.Value;
            if (projectReference.Name != null && (packageFromReference.ReferencesAndroid.ContainsKey(projectReference.Name) ||
                                                  packageFromReference.ReferencesIos.ContainsKey(projectReference.Name)))
                return packageFromReference;
        }
        return null;
    }

    public string GetVersion() {
        Regex regex = new(@"(\d+\.\d+\.\d+)-.*");
        if (DevExpressDataVersion is not null) {
            Match match = regex.Match(DevExpressDataVersion);
            if (match.Success)
                return match.Groups[1].Value;
        }
        return "1.0.0";
    }

    public bool CanConvertPackage(string packageName) {
        return PackagesByNameDictionary.ContainsKey(packageName);
    }
}