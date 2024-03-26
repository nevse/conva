namespace ConvA;

public abstract class ProjectConverterBase {
    protected ProjectConverterBase(RepoInfo repoInfo) {
        RepoInfo = repoInfo;
    }

    protected RepoInfo RepoInfo { get; }

    public void Convert(Project project) {
        var packageReferences = project.GetPackageReferences().Where(x => x.Name != null && RepoInfo.CanConvertPackage(x.Name)).ToList();
        HashSet<PackageInfo> packages = new();
        foreach (var packageReference in packageReferences) {
            if (packageReference.Name == null) {
                continue;
            }
            if (RepoInfo.PackagesByNameDictionary.TryGetValue(packageReference.Name, out var packageInfo))
                packages.Add(packageInfo);
            foreach (string dependency in RepoInfo.CalculateDependencies(packageReference.Name)) {
                if (!RepoInfo.PackagesByNameDictionary.TryGetValue(dependency, out packageInfo)) {
                    continue;
                }
                packages.Add(packageInfo);
            }
        }
        List<Reference>? references = project.GetDllReferences();
        List<Reference> convertableReferences = new();
        if (references != null) {
            foreach (Reference reference in references) {
                var package = RepoInfo.GetPackageFromReference(reference);
                if (package == null)
                    continue;
                packages.Add(package);
                convertableReferences.Add(reference);
            }
        }
        List<ProjectReference>? projectReferences = project.GetProjectReferences();
        List<ProjectReference> convertableProjectReferences = new();
        if (projectReferences != null) {
            foreach (ProjectReference projectReference in projectReferences) {
                var package = RepoInfo.GetPackageFromReference(projectReference);
                if (package == null)
                    continue;
                packages.Add(package);
                convertableProjectReferences.Add(projectReference);
            }
        }
        List<string> externPackages = GetExternDependencies(packages);
        List<string> externAssets = GetExternAssets(packages);
        ConvertCore(project, packages, externPackages, externAssets, convertableReferences, convertableProjectReferences);
        project.RemoveEmptyItemGroups();
    }
    protected abstract void ConvertCore(Project project, HashSet<PackageInfo> packages, List<string> externPackages, List<string> assets, List<Reference> references, List<ProjectReference> projectReferences);
    List<string> GetExternDependencies(HashSet<PackageInfo> packages, string startWith = "DevExpress.") {
        HashSet<string> externPackages = new();
        foreach (PackageInfo package in packages) {
            foreach (var dependency in package.Dependencies) {
                if (RepoInfo.CanConvertPackage(dependency))
                    continue;
                if (!String.IsNullOrEmpty(startWith) && dependency.StartsWith(startWith, StringComparison.CurrentCultureIgnoreCase))
                    externPackages.Add(dependency);
            }
            if (RepoInfo.CanConvertPackage(package.Id!))
                continue;
            if (!String.IsNullOrEmpty(startWith) && package.Id!.StartsWith(startWith, StringComparison.CurrentCultureIgnoreCase))
                externPackages.Add(package.Id!);
        }
        return externPackages.ToList();
    }
    List<string> GetExternAssets(HashSet<PackageInfo> packages) {
        List<string> assets = new();
        foreach (PackageInfo package in packages) {
            foreach (var asset in package.Assets) {
                assets.Add(asset);
            }
        }
        return assets;
    }
}