namespace ConvA;

public class DllReferenceConverter : ProjectConverterBase {
    public DllReferenceConverter(RepoInfo repoInfo) : base(repoInfo) {
    }

    protected override void ConvertCore(Project project, HashSet<PackageInfo> packages, List<Reference> references, List<ProjectReference> projectReferences) {
        Dictionary<string, string> androidReferences = new();
        Dictionary<string, string> iosReferences = new();
        foreach (PackageInfo package in packages) {
            foreach (var androidReferencePair in RepoInfo.GetAndroidReferences(package.Id!))
                androidReferences[androidReferencePair.Key] = androidReferencePair.Value;
            foreach (var iosReferencePair in RepoInfo.GetIosReferences(package.Id!))
                iosReferences[iosReferencePair.Key] = iosReferencePair.Value;
        }

        List<Reference> dllReferencesToRemove = new();
        foreach (var reference in references) {
            if (String.IsNullOrEmpty(reference.Condition)) {
                dllReferencesToRemove.Add(reference!);
                continue;
            }
            string referenceCondition = reference.Condition.ToLower();
            if (referenceCondition.Contains("-android")) {
                if (androidReferences.TryGetValue(reference.Name!, out string? value)) {
                    if (project.UpdateDllReference(reference.Name!, value, reference.Condition))
                        androidReferences.Remove(reference.Name!);
                }
            } else if (referenceCondition.Contains("-ios")) {
                if (iosReferences.TryGetValue(reference.Name!, out string? value)) {
                    if (project.UpdateDllReference(reference.Name!, value, reference.Condition))
                        iosReferences.Remove(reference.Name!);
                }
            } else {
                dllReferencesToRemove.Add(reference);
            }
        }

        project.RemoveDllReferences(dllReferencesToRemove);
        project.RemoveProjectReferences(projectReferences);
        project.AddDllReference(androidReferences, "android");
        project.AddDllReference(iosReferences, "ios");
        //remove packages
        foreach (PackageInfo package in packages) {
            if (package.Id != null) {
                project.RemovePackage(package.Id);
            }
        }
    }
}