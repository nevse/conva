using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ConvA;

class Program {
    const string ConfigFileName = "convacfg.json";
    public static Program? Instance { get; private set; }
    public static bool IsHelpOrVersionRequested { get; set; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Program))]
    public static async Task<int> Main(string[] args) {
        var repositoryPathArgument = new Argument<string>
            ("path to repo", () => "" , "Path to working repository.");

        var fileOption = new Option<ConversionType?>(
            aliases: new[] { "-t", "--type" },
            description: "Project conversion type",
            getDefaultValue: () => ConversionType.Proj);

        var projectPathOption = new Option<string>(
            aliases: new[] { "-p", "--path" },
            description: "Path to project to convert");

        var patchVersionOption = new Option<string>(
            name: "--patch-version",
            description: "Version of package reference");

        var rootCommand = new RootCommand("Convert project references to/from dll references");
        rootCommand.AddArgument(repositoryPathArgument);
        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(projectPathOption);
        rootCommand.AddOption(patchVersionOption);

        rootCommand.SetHandler(Run, repositoryPathArgument, fileOption, projectPathOption, patchVersionOption);
        return await rootCommand.InvokeAsync(args);
    }

    static async Task<int> Run(string? repositoryPath, ConversionType? type, string? projectPath, string? packageVersion) {
        string actualProjectPath = projectPath ?? Directory.GetCurrentDirectory();
        var config = await GetConfig(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), nameof(ConvA), ConfigFileName)) ?? new Config();
        if (String.IsNullOrEmpty(repositoryPath))
            repositoryPath = null;
        repositoryPath ??= config.RepositoryPath;
        Config? localConfig = FindLocalConfig(actualProjectPath);
        repositoryPath ??= localConfig?.RepositoryPath;
        repositoryPath ??= GetRepositoryPathFromProject(actualProjectPath);
        if (String.IsNullOrEmpty(repositoryPath) || !Directory.Exists(repositoryPath)) {
            Console.WriteLine("Path to working repository is not specified.");
            //Parser.Default.ParseArguments<Program>(new[] { "--help" });
            return -1;
        }

        repositoryPath = PathHelper.ExpandPath(repositoryPath);
        string projectDir = File.Exists(actualProjectPath) ? Path.GetDirectoryName(actualProjectPath)! : actualProjectPath;
        RepoInfo repoInfo = new(repositoryPath);
        repoInfo.Build();
        string actualVersion = String.IsNullOrEmpty(packageVersion) ? repoInfo.GetVersion() : packageVersion;
        ProjectConverterBase converter = type switch {
            ConversionType.Proj => new ProjectReferenceConverter(repoInfo, false),
            ConversionType.Proj2 => new ProjectReferenceConverter(repoInfo, true),
            ConversionType.Dll => new DllReferenceConverter(repoInfo),
            ConversionType.Package => new PackageReferenceConverter(repoInfo, actualVersion),
            _ => throw new NotSupportedException($"Conversion type {type} is not supported")
        };
        IEnumerable<string> projectFiles = File.Exists(actualProjectPath) ? new[] { actualProjectPath } : Directory.EnumerateFiles(actualProjectPath, "*.csproj");
        foreach (var projectFileName in projectFiles) {
            Project project = new(projectFileName);
            converter.Convert(project);
            project.SaveBackup();
            project.Save();
        }
        await SaveConfig(repositoryPath, type, Path.Combine(projectDir, ConfigFileName));
        return 0;
    }

    static string? GetRepositoryPathFromProject(string actualProjectPath) {
        if (String.IsNullOrEmpty(actualProjectPath))
            return null;
        string? repositoryPath = null;
        if (Directory.Exists(actualProjectPath)) {
            foreach (string projectPath in Directory.EnumerateFiles(actualProjectPath, "*.csproj")) {
                string? someRepoRoot = GetRepositoryPathFromProject(projectPath);
                if (someRepoRoot == null)
                    continue;
                if (repositoryPath == null)
                    repositoryPath = someRepoRoot;
                else if (repositoryPath != someRepoRoot)
                    return null;
            }
            return repositoryPath;
        }
        Project project = new(actualProjectPath);
        foreach (Reference dllReference in project.GetDllReferences()) {
            if (String.IsNullOrEmpty(dllReference.HintPath))
                continue;
            string? someRepoRoot = FindGitRepo(dllReference.HintPath);
            if (repositoryPath == null)
                repositoryPath = someRepoRoot;
            else if (repositoryPath != someRepoRoot)
                return null;
        }
        return repositoryPath;
    }
    static string? FindGitRepo(string dllReferenceHintPath) {
        if (String.IsNullOrEmpty(dllReferenceHintPath))
            return null;
        if (File.Exists(dllReferenceHintPath))
            dllReferenceHintPath = Path.GetDirectoryName(dllReferenceHintPath)!;
        DirectoryInfo? directoryInfo = new(dllReferenceHintPath);
        do {
            string path = Path.Combine(directoryInfo.FullName, ".git");
            if (File.Exists(path) || Directory.Exists(path))
                return directoryInfo.FullName;
            directoryInfo = directoryInfo.Parent;
        } while (directoryInfo != null);
        return null;
    }

    static Config? FindLocalConfig(string path) {
        if (String.IsNullOrEmpty(path))
            return null;
        if (File.Exists(path))
            path = Path.GetDirectoryName(path)!;
        DirectoryInfo? directoryInfo = new(path);
        for (int i = 0; i < 3; i++) {
            var configFileInfo = new FileInfo(Path.Combine(directoryInfo.FullName, ConfigFileName));
            if (configFileInfo.Exists) {
                using var configStream = configFileInfo.OpenRead();
                return JsonSerializer.Deserialize<Config>(configStream, SourceGenerationContext.Default.Config) ?? new Config();
            }
            directoryInfo = directoryInfo.Parent;
            if (directoryInfo == null)
                return null;
        }

        return null;
    }

    static async Task<Config?> GetConfig(string path) {
        var configFileInfo = new FileInfo(path);
        if (!configFileInfo.Exists)
            return new Config();
        await using var configStream = configFileInfo.OpenRead();
        return await JsonSerializer.DeserializeAsync<Config>(configStream, SourceGenerationContext.Default.Config);
    }

    static async Task SaveConfig(string repoPath, ConversionType? conversionType, string path) {
        var configFileInfo = new FileInfo(path);
        if (!configFileInfo.Directory!.Exists)
            configFileInfo.Directory.Create();
        var config = new Config { RepositoryPath = repoPath, ConversionType = conversionType };
        await using var configStream = configFileInfo.OpenWrite();
        await JsonSerializer.SerializeAsync(configStream, config, SourceGenerationContext.Default.Config);
    }
}