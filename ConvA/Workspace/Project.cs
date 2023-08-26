using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using MSPath = System.IO.Path;
using System.Xml;

namespace ConvA;

public class Project {
    public static Project Load(string filePath) {
        Project project = new (filePath);
        return project;
    }

    public string Name { get; set; }
    public string ProjectPath { get; set; }
    XmlDocument Document { get; }
    string ProjectDirectory { get; }

    public Project(string path) {
        Name = MSPath.GetFileNameWithoutExtension(path);
        ProjectPath = MSPath.GetFullPath(path);
        ProjectDirectory = MSPath.GetDirectoryName(ProjectPath) ?? throw new InvalidOperationException();
        Document = new XmlDocument();
        Document.Load(path);
    }

    public override string ToString() {
        return $"Name: {Name}, FilePath: {ProjectPath}";
    }

    public string? EvaluateProperty(string name, string? defaultValue = null) {
        MatchCollection? propertyMatches = GetPropertyMatches(ProjectPath, name);
        if (propertyMatches == null)
            return defaultValue;

        string propertyValue = GetPropertyValue(name, propertyMatches);
        if (String.IsNullOrEmpty(propertyValue))
            return defaultValue;

        return propertyValue;
    }

    public string? GetOutputAssembly(string configuration, string framework, string runtimeId, string platform) {
        string rootDirectory = MSPath.GetDirectoryName(ProjectPath) ?? throw new InvalidOperationException();
        string outputDirectory = MSPath.Combine(rootDirectory, "bin", configuration, framework);

        if (!String.IsNullOrEmpty(runtimeId))
            outputDirectory = MSPath.Combine(outputDirectory, runtimeId);

        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"Could not find output directory {outputDirectory}");

        if (platform.IsAndroid()) {
            string[] files = Directory.GetFiles(outputDirectory, "*-Signed.apk", SearchOption.TopDirectoryOnly);
            if (!files.Any())
                throw new FileNotFoundException($"Could not find \"*-Signed.apk\" in {outputDirectory}");
            if (files.Length > 1)
                throw new EvaluateException($"Found more than one \"*-Signed.apk\" in {outputDirectory}");
            return files.FirstOrDefault();
        }

        if (platform.IsWindows()) {
            string? executableName = EvaluateProperty("AssemblyName", Name);
            string[] files = Directory.GetFiles(outputDirectory, $"{executableName}.exe", SearchOption.AllDirectories);
            if (!files.Any())
                throw new FileNotFoundException($"Could not find \"{executableName}.exe\" in {outputDirectory}");
            return files.FirstOrDefault();
        }

        if (platform.IsIPhone() || platform.IsMacCatalyst()) {
            string[] bundle = Directory.GetDirectories(outputDirectory, "*.app", SearchOption.TopDirectoryOnly);
            if (!bundle.Any())
                throw new DirectoryNotFoundException($"Could not find \"*.app\" in {outputDirectory}");
            if (bundle.Length > 1)
                throw new EvaluateException($"Found more than one \"*.app\" in {outputDirectory}");
            return bundle.FirstOrDefault();
        }

        return null;
    }

    public List<string> RemoveDllReferences(List<Reference> references) {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup/Reference");
        List<string> removedReferences = new List<string>();
        if (nodes == null)
            return removedReferences;
        HashSet<string> referenceNames = new(references.Select(r => r.Name)!);
        foreach (XmlNode node in nodes) {
            string? referenceName = node?.Attributes?["Include"]?.Value;
            if (referenceName == null || !referenceNames.Contains(referenceName)) {
                continue;
            }
            node?.ParentNode?.RemoveChild(node);
            removedReferences.Add(referenceName);
            Console.WriteLine($"Remove dll reference {referenceName}");
        }
        return removedReferences;
    }
    public List<string> RemoveProjectReferences(List<ProjectReference> references) {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup/ProjectReference");
        List<string> removedReferences = new List<string>();
        if (nodes == null)
            return removedReferences;
        HashSet<string> referenceNames = new(references.Select(r => r.Path)!);
        foreach (XmlNode node in nodes) {
            string? referenceName = node?.Attributes?["Include"]?.Value;
            if (referenceName == null || !referenceNames.Contains(referenceName)) {
                continue;
            }
            node?.ParentNode?.RemoveChild(node);
            removedReferences.Add(referenceName);
            Console.WriteLine($"Remove project reference {referenceName}");
        }
        return removedReferences;
    }

    public void RemoveEmptyItemGroups() {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup");
        if (nodes == null)
            return;
        foreach (XmlNode node in nodes) {
            if (node?.ChildNodes.Count == 0)
                node.ParentNode?.RemoveChild(node);
        }
    }

    public void AddOrUpdatePackageReference(string packageName, string version) {
        XmlElement? packageReferenceNode = FindPackageReferenceNode(packageName);
        if (packageReferenceNode != null) {
            packageReferenceNode.SetAttribute("Version", version);
            Console.WriteLine($"Update package {packageName} to {version}");
            return;
        }
        XmlNode itemGroup = GetItemGroupWithPackageReferences();
        XmlElement packageReferenceElement = Document.CreateElement("PackageReference");
        XmlAttribute includeAttribute = Document.CreateAttribute("Include");
        includeAttribute.Value = packageName;
        XmlAttribute versionAttribute = Document.CreateAttribute("Version");
        versionAttribute.Value = version;
        packageReferenceElement.Attributes.Append(includeAttribute);
        packageReferenceElement.Attributes.Append(versionAttribute);
        itemGroup.AppendChild(packageReferenceElement);
    }

    public bool CheckCondition(XmlElement? element, string? condition) {
        var conditionAttr = element?.GetAttribute("Condition");
        if (string.IsNullOrEmpty(conditionAttr) && string.IsNullOrEmpty(condition)) {
            return true;
        }
        if (string.IsNullOrEmpty(conditionAttr)) {
            return false;
        }

        return conditionAttr.Replace(" ", "") == condition?.Replace(" ", "");
    }

    public void AddProjectReference(IEnumerable<string> references, string? platform = "", string? repoPath = null) {
        IEnumerable<XmlNode> packageRefNodes = GetItemGroupWithPackageReference();
        string? condition = null;
        XmlElement? itemGroupNode = null;
        if (!String.IsNullOrEmpty(platform)) {
            condition = platform;
            foreach (XmlNode node in packageRefNodes) {
                if (!CheckCondition(node as XmlElement, condition)) {
                    continue;
                }

                itemGroupNode = node as XmlElement;
                break;
            }
        } else {
            itemGroupNode = packageRefNodes.FirstOrDefault() as XmlElement;
        }

        if (itemGroupNode == null) {
            XmlNodeList? projectNodes = Document.SelectNodes("//Project");
            if (projectNodes == null)
                throw new EvaluateException("Could not find project node");
            var projectNode = projectNodes[0];
            itemGroupNode = Document.CreateElement("ItemGroup");

            projectNode?.AppendChild(itemGroupNode);
            if (condition != null) {
                itemGroupNode.SetAttribute("Condition", $"$(TargetFramework.Contains('-{condition}'))");
            }
        }

        foreach (var projectPath in references) {
            var refNode = itemGroupNode.AppendChild(Document.CreateElement("ProjectReference")) as XmlElement;
            refNode?.SetAttribute("Include", projectPath);
            Console.WriteLine($"Add reference {projectPath}");
        }
    }

    public bool UpdateProjectReference(string path, string newPath, string? condition = "") {
        XmlNodeList? itemGroupNodes = Document.SelectNodes("//ItemGroup");
        if (itemGroupNodes == null)
            return false;
        foreach (object? itemGroupNode in itemGroupNodes) {
            if (!CheckCondition(itemGroupNode as XmlElement, condition)) {
                continue;
            }
            var referenceNodes = (itemGroupNode as XmlNode)?.SelectNodes("ProjectReference");
            if (referenceNodes == null)
                continue;
            foreach (XmlElement referenceNode in referenceNodes) {
                string? include = referenceNode.Attributes?["Include"]?.Value;
                if (!String.Equals(include, path, StringComparison.InvariantCultureIgnoreCase))
                    continue;
                referenceNode.SetAttribute("Include", newPath);
                Console.WriteLine($"Update project reference {newPath}");
                return true;
            }
        }
        return false;
    }

    public bool UpdateDllReference(string reference, string hintPath, string? condition = "") {
        XmlNodeList? itemGroupNodes = Document.SelectNodes("//ItemGroup");
        if (itemGroupNodes == null)
            return false;
        foreach (object? itemGroupNode in itemGroupNodes) {
            if (!CheckCondition(itemGroupNode as XmlElement, condition)) {
                continue;
            }
            var referenceNodes = (itemGroupNode as XmlNode)?.SelectNodes("Reference");
            if (referenceNodes == null)
                continue;
            foreach (XmlNode referenceNode in referenceNodes) {
                string? include = referenceNode.Attributes?["Include"]?.Value;
                if (!String.Equals(include, reference, StringComparison.InvariantCultureIgnoreCase))
                    continue;
                var hintPathNode = referenceNode.SelectSingleNode("HintPath");
                if (hintPathNode == null)
                    continue;
                hintPathNode.InnerText = Path.GetRelativePath(Path.GetDirectoryName(ProjectPath)!, hintPath);
                Console.WriteLine($"Update reference {reference} to {hintPathNode.InnerText}");
                return true;
            }
        }
        return false;
    }
    public void AddDllReference(Dictionary<string, string> references, string? platform = "", string? repoPath = null) {
        IEnumerable<XmlNode> packageRefNodes = GetItemGroupWithPackageReference();
        string? condition = null;
        XmlElement? itemGroupNode = null;
        if (!String.IsNullOrEmpty(platform)) {
            condition = platform;
            foreach (XmlNode node in packageRefNodes) {
                if (!CheckCondition(node.ParentNode as XmlElement, condition)) {
                    continue;
                }
                itemGroupNode = node.ParentNode as XmlElement;
                break;
            }
        } else {
            itemGroupNode = packageRefNodes.FirstOrDefault() as XmlElement;
        }

        var refContentNode = Document.CreateElement("ItemGroup");
        if (condition != null) {
            refContentNode.SetAttribute("Condition", $"$(TargetFramework.Contains('-{condition}'))");
        }

        if (itemGroupNode == null) {
            XmlNodeList? projectNodes = Document.SelectNodes("//Project");
            if (projectNodes == null)
                throw new EvaluateException("Could not find project node");
            var projectNode = projectNodes[0];
            projectNode?.AppendChild(refContentNode);
        } else {
            itemGroupNode.InsertAfter(refContentNode, packageRefNodes.Last());
        }

        foreach (var referencePair in references) {
            string reference = referencePair.Key;
            string hintPath = referencePair.Value;
            var refNode = refContentNode.AppendChild(Document.CreateElement("Reference")) as XmlElement;
            refNode?.SetAttribute("Include", reference);
            var hintPathNode = refNode?.AppendChild(Document.CreateElement("HintPath")) as XmlElement;
            string refAbsPath = hintPath;
            string refRelPath = Path.GetRelativePath(Path.GetDirectoryName(ProjectPath)!, refAbsPath);
            if (hintPathNode != null) {
                hintPathNode.InnerText = refRelPath.ToPlatformPath();
            }

            Console.WriteLine($"Add reference {reference}");
        }
    }

    public bool RemovePackage(string packageName) {
        XmlNodeList? packages = Document.SelectNodes("//ItemGroup/PackageReference");
        bool isRemoved = false;
        if (packages == null)
            return isRemoved;
        foreach (XmlNode node in packages) {
            string? packageNameFromNode = node.Attributes?["Include"]?.Value;
            if (!String.Equals(packageNameFromNode, packageName, StringComparison.InvariantCultureIgnoreCase)) {
                continue;
            }
            node.ParentNode?.RemoveChild(node);
            isRemoved = true;
        }
        Console.WriteLine($"Remove package {packageName}");
        return isRemoved;
    }

    public List<String> RemovePackageRegex(string packageRegexString) {
        XmlNodeList? packages = Document.SelectNodes("//ItemGroup/PackageReference");
        List<string> removedPackages = new List<string>();
        if (packages == null)
            return removedPackages;
        Regex packageRegex = new Regex(packageRegexString, RegexOptions.IgnoreCase);
        foreach (XmlNode node in packages) {
            string? packageNameFromNode = node.Attributes?["Include"]?.Value;
            if (packageNameFromNode != null && !packageRegex.IsMatch(packageNameFromNode)) {
                continue;
            }
            node.ParentNode?.RemoveChild(node);
            if (packageNameFromNode != null) {
                removedPackages.Add(packageNameFromNode);
            }
        }
        return removedPackages;
    }

    public void Save() {
        Document.Save(ProjectPath);
    }

    public IEnumerable<PackageReference> GetPackageReferences() {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup");
        if (nodes != null) {
            foreach (XmlNode node in nodes) {
                foreach (XmlNode subNode in node.ChildNodes) {
                    if (String.Equals("PackageReference", subNode.Name, StringComparison.InvariantCultureIgnoreCase)) {
                        string packageName = subNode.Attributes?["Include"]?.Value ?? String.Empty;
                        string version = subNode.Attributes?["Version"]?.Value ?? String.Empty;
                        yield return new PackageReference() {
                            Name = packageName,
                            Version = version
                        };
                    }
                }
            }
        }
    }

    void RemovePackageReference(string packageName, XmlNode itemGroup) {
        foreach (XmlNode node in itemGroup.ChildNodes) {
            if (!String.Equals("PackageReference", node.Name, StringComparison.InvariantCultureIgnoreCase))
                continue;
            string? packageNameFromNode = node.Attributes?["Include"]?.Value;
            if (String.Equals(packageNameFromNode, packageName, StringComparison.InvariantCultureIgnoreCase))
                node.ParentNode?.RemoveChild(node);
        }
    }
    XmlElement? FindPackageReferenceNode(string packageName) {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup");
        if (nodes != null) {
            foreach (XmlNode node in nodes) {
                foreach (XmlNode subNode in node.ChildNodes) {
                    if (String.Equals("PackageReference", subNode.Name, StringComparison.InvariantCultureIgnoreCase)) {
                        string packageNameFromNode = subNode.Attributes?["Include"]?.Value ?? String.Empty;
                        if (String.Equals(packageNameFromNode, packageName, StringComparison.InvariantCultureIgnoreCase))
                            return (XmlElement)subNode;
                    }
                }
            }
        }
        return null;
    }

    IEnumerable<XmlNode> GetItemGroupWithPackageReference() {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup");
        if (nodes == null) {
            yield break;
        }
        foreach (XmlNode node in nodes) {
            foreach (XmlNode subNode in node.ChildNodes) {
                if (!String.Equals("PackageReference", subNode.Name, StringComparison.InvariantCultureIgnoreCase)) {
                    continue;
                }

                yield return node;
                break;
            }
        }
    }

    XmlNode GetItemGroupWithPackageReferences() {
        XmlNodeList? nodes = Document.SelectNodes("//ItemGroup");
        if (nodes != null) {
            foreach (XmlNode node in nodes) {
                string? condition = node.Attributes?["Condition"]?.Value;
                if (!String.IsNullOrEmpty(condition))
                    continue;
                foreach (XmlNode subNode in node.ChildNodes) {
                    if (String.Equals("PackageReference", subNode.Name, StringComparison.InvariantCultureIgnoreCase))
                        return node;
                }
            }
        }

        XmlNode itemGroup = Document.CreateElement("ItemGroup");
        if (nodes?.Count == 0) {
            XmlNodeList? propertyGroups = Document.SelectNodes("//PropertyGroup");
            if (propertyGroups?.Count == 0) {
                XmlNodeList? projectNodes = Document.SelectNodes("//Project");
                projectNodes?[0]?.AppendChild(itemGroup);
            } else {
                propertyGroups?[0]?.ParentNode?.InsertAfter(itemGroup, propertyGroups[0]);
            }
        } else {
            nodes?[0]?.ParentNode?.InsertAfter(itemGroup, nodes[0]);
        }

        return itemGroup;
    }

    MatchCollection? GetPropertyMatches(string projectPath, string propertyName, bool isEndPoint = false) {
        if (!File.Exists(projectPath))
            return null;

        string content = File.ReadAllText(projectPath);
        content = Regex.Replace(content, @"<!--.*?-->", String.Empty, RegexOptions.Singleline);
        /* Find in current project */
        MatchCollection propertyMatch =
            new Regex($@"<{propertyName}\s?.*>(.*?)<\/{propertyName}>\s*\n").Matches(content);
        if (propertyMatch.Count > 0)
            return propertyMatch;
        Regex importRegex = new Regex(@"<Import\s+Project\s*=\s*""(.*?)""");
        /* Find in imported project */
        foreach (Match importMatch in importRegex.Matches(content).Cast<Match>()) {
            string? basePath = MSPath.GetDirectoryName(projectPath);
            string importedProjectName =
                importMatch.Groups[1].Value.Replace("$(MSBuildThisFileDirectory)", String.Empty);
            if (basePath == null) {
                continue;
            }
            string importedProjectPath = MSPath.Combine(basePath, importedProjectName).ToPlatformPath();

            if (!File.Exists(importedProjectPath))
                importedProjectPath = importMatch.Groups[1].Value.ToPlatformPath();
            if (!File.Exists(importedProjectPath))
                return null;

            MatchCollection? importedProjectPropertyMatches =
                GetPropertyMatches(importedProjectPath, propertyName, isEndPoint);
            if (importedProjectPropertyMatches != null)
                return importedProjectPropertyMatches;
        }

        /* Already at the end of the import chain */
        if (isEndPoint)
            return null;
        /* Find in Directory.Build.props */
        string? propsFile = GetDirectoryPropsPath(MSPath.GetDirectoryName(projectPath));
        if (propsFile == null)
            return null;

        return GetPropertyMatches(propsFile, propertyName, true);
    }

    private string? GetDirectoryPropsPath(string? workspacePath) {
        if (workspacePath != null) {
            string[] propFiles = Directory.GetFiles(workspacePath, "Directory.Build.props", SearchOption.TopDirectoryOnly);
            if (propFiles.Length > 0)
                return propFiles[0];
        }

        if (workspacePath == null) {
            return null;
        }

        DirectoryInfo? parentDirectory = Directory.GetParent(workspacePath);
        if (parentDirectory == null)
            return null;

        return GetDirectoryPropsPath(parentDirectory.FullName);
    }

    private string GetPropertyValue(string propertyName, MatchCollection matches) {
        Regex includeRegex = new Regex(@"\$\((?<inc>.*?)\)");
        StringBuilder resultSequence = new StringBuilder();
        /* Process all property entrance */
        foreach (Match match in matches.Cast<Match>()) {
            string propertyValue = match.Groups[1].Value;
            /* If property reference self */
            if (propertyValue.Contains($"$({propertyName})")) {
                propertyValue = propertyValue.Replace($"$({propertyName})", resultSequence.ToString());
                resultSequence.Clear();
            }

            /* If property reference other property */
            foreach (Match includeMatch in includeRegex.Matches(propertyValue).Cast<Match>()) {
                string includePropertyName = includeMatch.Groups["inc"].Value;
                string? includePropertyValue = EvaluateProperty(includePropertyName);
                propertyValue = propertyValue.Replace($"$({includePropertyName})", includePropertyValue ?? "");
            }

            /* Add separator and property to builder */
            if (resultSequence.Length != 0)
                resultSequence.Append(';');
            resultSequence.Append(propertyValue);
        }

        return resultSequence.ToString();
    }

    public void SaveBackup() {
        string backupPath = GetBackupPath();
        File.Copy(ProjectPath, backupPath);
    }

    string GetBackupPath() {
        string backupPath = $"{ProjectPath}.bak";
        int i = 1;
        while (File.Exists(backupPath)) {
            backupPath = $"{ProjectPath}.bak{i}";
            i++;
        }

        return backupPath;
    }

    public List<Reference> GetDllReferences() {
        XmlNodeList? nodes = Document.SelectNodes("//Reference");
        if (nodes == null)
            return new List<Reference>();

        List<Reference> references = new();
        foreach (XmlNode node in nodes) {
            string? include = node.Attributes?["Include"]?.Value;
            if (include == null)
                continue;
            string? condition = node.ParentNode?.Attributes?["Condition"]?.Value;
            string? hintPath = node.SelectSingleNode("HintPath")?.InnerText;
            references.Add(new Reference() {
                Name = include,
                HintPath = hintPath,
                Condition = condition
            });
        }

        return references;
    }

    public List<ProjectReference> GetProjectReferences() {
        List<ProjectReference> references = new();
        XmlNodeList? projectReferenceNodes = Document.SelectNodes("//ProjectReference");
        if (projectReferenceNodes == null)
            return references;
        foreach (XmlNode projectReferenceNode in projectReferenceNodes) {
            string? include = projectReferenceNode.Attributes?["Include"]?.Value;
            if (include == null)
                continue;
            string? condition = projectReferenceNode.ParentNode?.Attributes?["Condition"]?.Value;
            if (!Path.IsPathRooted(include))
                include = Path.GetFullPath(ProjectDirectory, include);
            references.Add(new ProjectReference() {
                Path = include,
                Condition = condition
            });
        }
        return references;
    }
}