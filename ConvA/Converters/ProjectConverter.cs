namespace ConvA;

public class ProjectConverter : ProjectConverterBase {
    public ProjectConverter(RepoInfo repoInfo, bool useDllReference, bool useRefsForProjectReferences) : base(repoInfo) {
        UseDllReference = useDllReference;
        UseRefsForProjectReferences = useRefsForProjectReferences;
    }

    public bool UseDllReference { get; }
    public bool UseRefsForProjectReferences { get; }

    public override void Convert(Project project) {
        var packageReferences = project.GetPackageReferences().Where(x => x.Name != null && RepoInfo.CanConvertPackage(x.Name)).ToList();
        HashSet<string> packageNames = new();
        foreach (var packageReference in packageReferences) {
            if (packageReference.Name == null) {
                continue;
            }
            packageNames.Add(packageReference.Name);
            foreach (string dependency in RepoInfo.CalculateDependencies(packageReference.Name)) {
                if (!RepoInfo.CanConvertPackage(dependency))
                    continue;
                packageNames.Add(dependency);
            }
        }

        Dictionary<string, string> androidReferences = new();
        Dictionary<string, string> iosReferences = new();
        foreach (string packageName in packageNames) {
            foreach (var androidReferencePair in RepoInfo.GetAndroidReferences(packageName))
                androidReferences[androidReferencePair.Key] = androidReferencePair.Value;
            foreach (var iosReferencePair in RepoInfo.GetIosReferences(packageName))
                iosReferences[iosReferencePair.Key] = iosReferencePair.Value;
        }

        if (UseDllReference) {
            project.AddDllReference(androidReferences, "android");
            project.AddDllReference(iosReferences, "ios");
        } else {
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

            project.AddProjectReference(commonProjectReferences.Select(GetProjectPath));
            project.AddProjectReference(androidProjectReferences.Select(GetProjectPath), "android");
            project.AddProjectReference(iosProjectReferences.Select(GetProjectPath), "ios");
        }

        //remove old packages
        foreach (PackageReference packageReference in packageReferences.ToList()) {
            if (packageReference.Name != null) {
                project.RemovePackage(packageReference.Name);
            }
        }
    }
    string GetProjectPath(string reference) {
        string projectPath = RepoInfo.GetProjectPath(reference);
        if (UseRefsForProjectReferences)
            return Path.ChangeExtension(projectPath, "Refs.csproj");
        return projectPath;
    }
}