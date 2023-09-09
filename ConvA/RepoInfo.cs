using System.Text.RegularExpressions;
using System;

namespace ConvA;

public partial class RepoInfo {
    const string ProjectsBasePath = "xamarin/maui";
    const string NuspecBasePath = "nuspec";


    [GeneratedRegex(@"(\d+\.\d+\.\d+)-.*")]
    private static partial Regex VersionRegex();

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
            string assemblyNameFromProject = Path.GetFileNameWithoutExtension(projectPath.FullName);
            if (String.IsNullOrEmpty(assemblyName))
                Console.WriteLine($"Can't find assemblyName, suppose it as {assemblyNameFromProject} from project name.");
            if (!String.IsNullOrEmpty(assemblyName) && assemblyNameFromProject != assemblyName)
                continue;
            ProjectsByNameDictionary.Add(assemblyNameFromProject, projectPath.FullName);
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
        List<string>? projectReferences = projectReference is ExpandedReference expandedReference ? expandedReference.ExpandedNames : new List<string> { projectReference?.Name!};
        foreach (string referenceName in projectReferences) {
            if (GetPackageFromReference(referenceName) is { } packageFromReference)
                return packageFromReference;
        }
        return null;
    }

    public PackageInfo? GetPackageFromReference(ProjectReference projectReference) {
        if (projectReference.Path == null)
            return null;
        string? dllReferenceName = FindDllReferenceByProjectPath(projectReference.Path);
        return GetPackageFromReference(dllReferenceName);
    }

    public string? FindDllReferenceByProjectPath(string path) {
        const string refsExtension = ".refs.csproj";
        if (path.ToLower().EndsWith(refsExtension))
            path = String.Concat(path.AsSpan(0, path.Length - refsExtension.Length), ".csproj");
        foreach (KeyValuePair<string, string> info in ProjectsByNameDictionary) {
            string name = info.Key;
            string projectPath = info.Value.Substring(BasePath.Length + 1);
            if (HasCommonPath(path, projectPath))
                return name;
        }
        return null;
    }

    static bool HasCommonPath(string path, string projectPath) {
        path = path.ToLower();
        projectPath = projectPath.ToLower();
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

    public string GetVersion() {
        Regex regex = VersionRegex();
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

    PackageInfo? GetPackageFromReference(string? referenceName) {
        if (referenceName != null && !ProjectsByNameDictionary.ContainsKey(referenceName))
            return null;
        foreach (var pair in PackagesByNameDictionary) {
            PackageInfo packageFromReference = pair.Value;
            if (referenceName != null && (packageFromReference.ReferencesAndroid.ContainsKey(referenceName) ||
                                          packageFromReference.ReferencesIos.ContainsKey(referenceName)))
                return packageFromReference;
        }
        return null;
    }
}