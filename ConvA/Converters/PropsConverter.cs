namespace ConvA;

public class PropsConverter(RepoInfo repoInfo) : ProjectConverterBase(repoInfo)  {
    protected override void ConvertCore(Project project, HashSet<PackageInfo> packages, List<string> externPackages, List<string> assets, List<Reference> references,
        List<ProjectReference> projectReferences) {
        //
        // * remove old props (move code for detecting props in RepoInfo.GetPropsImports)
        // * read from props all imported packages add dlls and maybe project references
        //

        foreach(string externPackage in externPackages) {
            project.RemovePackage(externPackage);
        }
        foreach(PackageInfo package in packages) {
            project.RemovePackage(package.Id!);
        }
        project.RemoveProjectReferences(projectReferences);
        project.RemoveDllReferences(references);
        foreach (var asset in assets) {
            project.RemoveAsset(asset);
        }

        HashSet<string> props = new();
        foreach (PackageInfo package in packages) {
            foreach (var prop in RepoInfo.GetPropsImports(package.Id!)) {
                props.Add(prop);
            }
        }
        project.AddImports(props);
        HashSet<string> packagesFromProps = new();
        foreach (string propsFile in props) {
            var propsProject = new Project(propsFile);
            foreach (var package in propsProject.GetPackageReferences()) {
                if (package.Name != null) {
                    packagesFromProps.Add(package.Name);
                }
            }
        }

        project.RemovePackages(packagesFromProps, reportMissed: false);
        project.AddJsonProjectReference("m");//TODO: after detect dependencies
    }
}