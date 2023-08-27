using System.Xml;

namespace ConvA;

public class PackageInfo {
    public PackageInfo(string nuspecPath) {
        NuspecPath = nuspecPath;
    }

    public string NuspecPath { get; set; }
    public string? Id { get; private set; }
    public List<string> Dependencies { get; } = new();
    public Dictionary<string, string> ReferencesIos { get; } = new();
    public Dictionary<string, string> ReferencesAndroid { get; } = new();

    public void Parse() {
        if (!File.Exists(NuspecPath)) {
            throw new FileNotFoundException($"File not found: {NuspecPath}");
        }

        var nuspecTree = new XmlDocument();
        nuspecTree.Load(NuspecPath);
        var nuspecRoot = nuspecTree.DocumentElement;
        if (nuspecRoot == null) {
            throw new InvalidDataException("Nuspec root is null");
        }
        var defaultNamespace = new XmlNamespaceManager(nuspecTree.NameTable);
        defaultNamespace.AddNamespace("ns", nuspecRoot.GetNamespaceOfPrefix(""));
        Id = nuspecRoot.SelectSingleNode("//ns:package/ns:metadata/ns:id", defaultNamespace)?.InnerText ??
             throw new InvalidDataException("Package id is null");
        if (Id == null || !Id.Contains("maui", StringComparison.CurrentCultureIgnoreCase)) {
            throw new NotSupportedException($"Package {Id} is not a maui package");
        }

        BuildMauiPackageInfo(nuspecRoot, Id, defaultNamespace);
    }

    public void BuildMauiPackageInfo(XmlElement nuspecRoot, string nuspecPackageId,
        XmlNamespaceManager defaultNamespace) {
        var packageDependencies =
            nuspecRoot.SelectNodes("//ns:package/ns:metadata/ns:dependencies/ns:group/ns:dependency/@id",
                defaultNamespace);
        if (packageDependencies != null) {
            foreach (XmlNode dependency in packageDependencies) {
                if (dependency.Value != null) {
                    Dependencies.Add(dependency.Value);
                }
            }
        }

        var iosReferences =
            nuspecRoot.SelectNodes(
                "//ns:package/ns:metadata/ns:references/ns:group[contains(@targetFramework, 'ios')]/ns:reference/@file",
                defaultNamespace);
        var androidReferences =
            nuspecRoot.SelectNodes(
                "//ns:package/ns:metadata/ns:references/ns:group[contains(@targetFramework, 'android')]/ns:reference/@file",
                defaultNamespace);
        var files = nuspecRoot.SelectNodes("//ns:package/ns:files/ns:file", defaultNamespace);
        if (files == null) {
            return;
        }

        foreach (XmlNode file in files) {
            string? source = file.Attributes?["src"]?.Value;
            string? target = file.Attributes?["target"]?.Value;
            string? dllName = Path.GetFileName(TrimPath(source));
            if (target != null && target.Contains("ios")) {
                if (dllName != null && source != null && iosReferences is { Count: 0 } && source.EndsWith(".dll")) {
                    ReferencesIos.Add(GetReferenceFromDll(dllName), ToAbsolutePath(source));
                }

                if (iosReferences != null) {
                    foreach (XmlNode iosReference in iosReferences) {
                        if (source == null || iosReference.Value == null || !iosReference.Value.Contains(source)) {
                            continue;
                        }

                        ReferencesIos.Add(GetReferenceFromDll(iosReference.Value), ToAbsolutePath(source));
                        break;
                    }
                }
            }

            if (target != null && !target.Contains("android")) {
                continue;
            }

            if (dllName != null && source != null && androidReferences is { Count: 0 } && source.EndsWith(".dll")) {
                ReferencesAndroid.Add(GetReferenceFromDll(dllName), ToAbsolutePath(source));
            }

            if (androidReferences == null) {
                continue;
            }

            foreach (XmlNode androidReference in androidReferences) {
                if (source != null && androidReference.Value != null && androidReference.Value.Contains(source)) {
                    ReferencesAndroid.Add(GetReferenceFromDll(androidReference.Value), ToAbsolutePath(source));
                    break;
                }
            }
        }
    }

    string GetReferenceFromDll(string dllName) {
        return dllName.Replace(".dll", "");
    }

    string ToAbsolutePath(string? path) {
        if (path == null) {
            return NuspecPath;
        }

        string? nuspecDirectory = Path.GetDirectoryName(NuspecPath);
        if (nuspecDirectory != null) {
            return Path.GetFullPath(Path.Combine(nuspecDirectory, TrimPath(path) ?? String.Empty));
        }
        throw new InvalidDataException("Nuspec directory is null");
    }

    string? TrimPath(string? path) {
        if (path != null && path.StartsWith(".\\"))
            path = path.Substring(2);
        return NormalizePath(path);
    }

    string? NormalizePath(string? path) {
        return path?.Replace("\\", "/");
    }
}