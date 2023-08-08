using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using MSPath = System.IO.Path;
using System.Xml;
using System.Linq;

namespace ConvA;

public class Project {
    public static Project Load(string filePath) {
        Project project = new Project(filePath);
        return project;
    }

    public string Name { get; set; }
    public string ProjectPath { get; set; }
    public List<string> Frameworks { get; set; }
    XmlDocument Document { get; }

    public Project(string path) {
        Name = MSPath.GetFileNameWithoutExtension(path);
        ProjectPath = MSPath.GetFullPath(path);
        Document = new XmlDocument();
        Document.Load(path);
    }

    public override string ToString() {
        return $"Name: {Name}, FilePath: {ProjectPath}";
    }

    public string EvaluateProperty(string name, string defaultValue = null) {
        MatchCollection propertyMatches = GetPropertyMatches(ProjectPath, name);
        if (propertyMatches == null)
            return defaultValue;

        string propertyValue = GetPropertyValue(name, propertyMatches);
        if (String.IsNullOrEmpty(propertyValue))
            return defaultValue;

        return propertyValue;
    }

    public string? GetOutputAssembly(string configuration, string framework, string runtimeId, string platform) {
        string rootDirectory = MSPath.GetDirectoryName(ProjectPath);
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
            string executableName = EvaluateProperty("AssemblyName", Name);
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
        XmlNodeList nodes = Document.SelectNodes("//ItemGroup/Reference");
        List<string> removedReferences = new List<string>();
        if (nodes == null)
            return removedReferences;
        HashSet<string> referenceNames = new(references.Select(r => r.Name));
        foreach (XmlNode node in nodes) {
            string referenceName = node?.Attributes?["Include"]?.Value;
            if (!referenceNames.Contains(referenceName)) {
                continue;
            }

            node?.ParentNode?.RemoveChild(node);
            removedReferences.Add(referenceName);
        }

        return removedReferences;
    }

    public void RemoveEmptyItemGroups() {
        XmlNodeList nodes = Document.SelectNodes("//ItemGroup");
        if (nodes == null)
            return;
        foreach (XmlNode node in nodes) {
            if (node?.ChildNodes.Count == 0)
                node.ParentNode?.RemoveChild(node);
        }
    }

    public void AddPackageReference(string packageName, string version) {
        XmlNode itemGroup = GetItemGroupWithPackageReferences();
        XmlElement packageReferenceElement = Document.CreateElement("PackageReference");
        XmlAttribute includeAttribute = Document.CreateAttribute("Include");
        includeAttribute.Value = packageName;
        XmlAttribute versionAttribute = Document.CreateAttribute("Version");
        versionAttribute.Value = version;
        packageReferenceElement.Attributes.Append(includeAttribute);
        packageReferenceElement.Attributes.Append(versionAttribute);
        RemovePackageReference(packageName, itemGroup);
        itemGroup.AppendChild(packageReferenceElement);
    }

    public bool CheckCondition(XmlElement element, string? condition) {
        var conditionAttr = element.GetAttribute("Condition");
        if (string.IsNullOrEmpty(conditionAttr)) {
            return false;
        }

        return conditionAttr.Replace(" ", "") == condition.Replace(" ", "");
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
            XmlNodeList projectNodes = Document.SelectNodes("//Project");
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
            refNode.SetAttribute("Include", projectPath);
            Console.WriteLine($"Add reference {projectPath}");
        }
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
            XmlNodeList projectNodes = Document.SelectNodes("//Project");
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
            refNode.SetAttribute("Include", reference);
            var hintPathNode = refNode.AppendChild(Document.CreateElement("HintPath")) as XmlElement;
            var refAbsPath = hintPath;
            var refRelPath = Path.GetRelativePath(Path.GetDirectoryName(ProjectPath), refAbsPath);
            hintPathNode.InnerText = refRelPath.ToPlatformPath();
            Console.WriteLine($"Add reference {reference}");
        }
    }

    public bool RemovePackage(string packageName) {
        XmlNodeList packages = Document.SelectNodes("//ItemGroup/PackageReference");
        bool isRemoved = false;
        if (packages == null)
            return isRemoved;
        foreach (XmlNode node in packages) {
            string packageNameFromNode = node.Attributes?["Include"]?.Value;
            if (String.Equals(packageNameFromNode, packageName, StringComparison.InvariantCultureIgnoreCase)) {
                node.ParentNode?.RemoveChild(node);
                isRemoved = true;
            }
        }

        return isRemoved;
    }

    public List<String> RemovePackageRegex(string packageRegexString) {
        XmlNodeList packages = Document.SelectNodes("//ItemGroup/PackageReference");
        List<string> removedPackages = new List<string>();
        if (packages == null)
            return removedPackages;
        Regex packageRegex = new Regex(packageRegexString, RegexOptions.IgnoreCase);
        foreach (XmlNode node in packages) {
            string packageNameFromNode = node.Attributes?["Include"]?.Value;
            if (packageRegex.IsMatch(packageNameFromNode)) {
                node.ParentNode?.RemoveChild(node);
                removedPackages.Add(packageNameFromNode);
            }
        }

        return removedPackages;
    }

    public void Save() {
        Document.Save(ProjectPath);
    }

    public IEnumerable<PackageReference> GetPackageReferences() {
        XmlNodeList nodes = Document.SelectNodes("//ItemGroup");
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
            string packageNameFromNode = node.Attributes?["Include"]?.Value;
            if (String.Equals(packageNameFromNode, packageName, StringComparison.InvariantCultureIgnoreCase))
                node.ParentNode?.RemoveChild(node);
        }
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
        XmlNodeList nodes = Document.SelectNodes("//ItemGroup");
        if (nodes != null) {
            foreach (XmlNode node in nodes) {
                string condition = node.Attributes?["Condition"]?.Value;
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
            XmlNodeList propertyGroups = Document.SelectNodes("//PropertyGroup");
            if (propertyGroups?.Count == 0) {
                XmlNodeList projectNodes = Document.SelectNodes("//Project");
                projectNodes?[0]?.AppendChild(itemGroup);
            } else {
                propertyGroups?[0]?.ParentNode?.InsertAfter(itemGroup, propertyGroups[0]);
            }
        } else {
            nodes?[0]?.ParentNode?.InsertAfter(itemGroup, nodes[0]);
        }

        return itemGroup;
    }

    MatchCollection GetPropertyMatches(string projectPath, string propertyName, bool isEndPoint = false) {
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
            string basePath = MSPath.GetDirectoryName(projectPath);
            string importedProjectName =
                importMatch.Groups[1].Value.Replace("$(MSBuildThisFileDirectory)", String.Empty);
            string importedProjectPath = MSPath.Combine(basePath, importedProjectName).ToPlatformPath();

            if (!File.Exists(importedProjectPath))
                importedProjectPath = importMatch.Groups[1].Value.ToPlatformPath();
            if (!File.Exists(importedProjectPath))
                return null;

            MatchCollection importedProjectPropertyMatches =
                GetPropertyMatches(importedProjectPath, propertyName, isEndPoint);
            if (importedProjectPropertyMatches != null)
                return importedProjectPropertyMatches;
        }

        /* Already at the end of the import chain */
        if (isEndPoint)
            return null;
        /* Find in Directory.Build.props */
        string propsFile = GetDirectoryPropsPath(MSPath.GetDirectoryName(projectPath));
        if (propsFile == null)
            return null;

        return GetPropertyMatches(propsFile, propertyName, true);
    }

    private string GetDirectoryPropsPath(string workspacePath) {
        string[] propFiles = Directory.GetFiles(workspacePath, "Directory.Build.props", SearchOption.TopDirectoryOnly);
        if (propFiles.Length > 0)
            return propFiles[0];

        DirectoryInfo parentDirectory = Directory.GetParent(workspacePath);
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
                string includePropertyValue = EvaluateProperty(includePropertyName);
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
        XmlNodeList nodes = Document.SelectNodes("//Reference");
        if (nodes == null)
            return null;

        List<Reference> references = new();
        foreach (XmlNode node in nodes) {
            string include = node.Attributes?["Include"]?.Value;
            if (include == null)
                continue;
            string hintPath = node.SelectSingleNode("HintPath")?.InnerText;
            references.Add(new Reference() {
                Name = include,
                HintPath = hintPath
            });
        }

        return references;
    }
}