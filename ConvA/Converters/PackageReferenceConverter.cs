namespace ConvA;

public class PackageReferenceConverter : ProjectConverterBase {
    public PackageReferenceConverter(RepoInfo repoInfo, string version) : base(repoInfo) {
        Version = version;
    }

    private string Version { get; }

    protected override void ConvertCore(Project project, HashSet<PackageInfo> packages, List<Reference> references,
        List<ProjectReference> projectReferences) {

        foreach (PackageInfo package in packages) {
            if (package.Id != null) {
                project.AddOrUpdatePackageReference(package.Id, Version);
            }
        }
        project.RemoveProjectReferences(projectReferences);
        project.RemoveDllReferences(references);
    }
}