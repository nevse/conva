namespace ConvA;

public class PackageReferenceConverter : ProjectConverterBase {
    public PackageReferenceConverter(RepoInfo repoInfo, string version) : base(repoInfo) {
        Version = version;
    }

    private string Version { get; }

    protected override void ConvertCore(Project project, HashSet<PackageInfo> packages, List<string> externPackages, List<string> assets, List<Reference> references, List<ProjectReference> projectReferences) {
        foreach (PackageInfo package in packages) {
            if (package.Id != null) {
                project.AddOrUpdatePackageReference(package.Id, Version);
            }
        }
        foreach(string externPackage in externPackages) {
            project.RemovePackage(externPackage);
        }
        project.RemoveProjectReferences(projectReferences);
        project.RemoveDllReferences(references);
        foreach (var asset in assets) {
            project.RemoveAsset(asset);
        }
    }
}