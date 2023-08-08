namespace ConvA;

public class ProjectRevertConverter : ProjectConverterBase {
    public ProjectRevertConverter(RepoInfo repoInfo, string version) : base(repoInfo) {
        Version = version;
    }

    private string Version { get; set; }

    public override void Convert(Project project) {
        List<Reference> references = project.GetDllReferences();
        HashSet<PackageInfo> packageToAdd = new();
        List<Reference> referencesToRemove = new();
        foreach (Reference reference in references) {
            var package = RepoInfo.GetPackageFromReference(reference);
            if (package == null)
                continue;
            packageToAdd.Add(package);
            referencesToRemove.Add(reference);
        }

        foreach (var package in packageToAdd) {
            project.AddPackageReference(package.Id, Version);
        }

        var removeDllReferences = project.RemoveDllReferences(referencesToRemove);
        foreach (var removedReference in removeDllReferences) {
            Console.WriteLine($"Removed {removedReference}");
        }
    }
}