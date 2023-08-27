namespace ConvA;

public class ProjectReferenceConverter : ProjectConverterBase {
    public ProjectReferenceConverter(RepoInfo repoInfo, bool useRefsForProjectReferences) : base(repoInfo) {
        UseRefsForProjectReferences = useRefsForProjectReferences;
    }

    bool UseRefsForProjectReferences { get;}

    protected override void ConvertCore(Project project, HashSet<PackageInfo> packages, List<Reference> references, List<ProjectReference> projectReferences) {
        Dictionary<string, string> androidReferences = new();
        Dictionary<string, string> iosReferences = new();
        foreach (PackageInfo package in packages) {
            foreach (var androidReferencePair in RepoInfo.GetAndroidReferences(package.Id!))
                androidReferences[androidReferencePair.Key] = androidReferencePair.Value;
            foreach (var iosReferencePair in RepoInfo.GetIosReferences(package.Id!))
                iosReferences[iosReferencePair.Key] = iosReferencePair.Value;
        }

        HashSet<string> commonProjectReferences = new();
        HashSet<string> androidProjectReferences = new();
        HashSet<string> iosProjectReferences = new(iosReferences.Keys);
        foreach (var androidReference in androidReferences) {
            if (iosProjectReferences.Contains(androidReference.Key)) {
                commonProjectReferences.Add(androidReference.Key);
                iosProjectReferences.Remove(androidReference.Key);
            } else {
                androidProjectReferences.Add(androidReference.Key);
            }
        }

        List<ProjectReference> projectReferencesToRemove = new();
        foreach (var projectReference in projectReferences) {
            string? dllReferenceName = RepoInfo.FindDllReferenceByProjectPath(projectReference.Path);
            if (dllReferenceName == null) {
                continue;
            }
            if (String.IsNullOrEmpty(projectReference.Condition)) {
                if (commonProjectReferences.Contains(dllReferenceName)) {
                    if (project.UpdateProjectReference(projectReference.Path, RepoInfo.GetProjectPath(dllReferenceName),
                            projectReference.Condition))
                        commonProjectReferences.Remove(dllReferenceName);
                    else {
                        projectReferencesToRemove.Add(projectReference);
                    }
                }
            } else if (projectReference.Condition.Contains("android", StringComparison.CurrentCultureIgnoreCase)) {
                if (androidProjectReferences.Contains(dllReferenceName)) {
                    if (project.UpdateProjectReference(projectReference.Path, RepoInfo.GetProjectPath(dllReferenceName),
                            projectReference.Condition))
                        androidProjectReferences.Remove(dllReferenceName);
                    else {
                        projectReferencesToRemove.Add(projectReference);
                    }
                }
            } else if (projectReference.Condition.Contains("ios", StringComparison.CurrentCultureIgnoreCase)) {
                if (iosProjectReferences.Contains(dllReferenceName)) {
                    if (project.UpdateProjectReference(projectReference.Path, RepoInfo.GetProjectPath(dllReferenceName),
                            projectReference.Condition))
                        iosProjectReferences.Remove(dllReferenceName);
                    else {
                        projectReferencesToRemove.Add(projectReference);
                    }
                }
            }
        }

        project.RemoveProjectReferences(projectReferencesToRemove);
        project.AddProjectReference(commonProjectReferences.Select(GetProjectPath));
        project.AddProjectReference(androidProjectReferences.Select(GetProjectPath), "android");
        project.AddProjectReference(iosProjectReferences.Select(GetProjectPath), "ios");

        //remove packages
        foreach (PackageInfo package in packages) {
            if (package.Id != null) {
                project.RemovePackage(package.Id);
            }
        }
        //remove dll references
        project.RemoveDllReferences(references);
    }
    string GetProjectPath(string reference) {
        string projectPath = RepoInfo.GetProjectPath(reference);
        if (UseRefsForProjectReferences)
            return Path.ChangeExtension(projectPath, "Refs.csproj");
        return projectPath;
    }
}