using System.Diagnostics.CodeAnalysis;
using CommandLine;
using System.Text.Json;
using CommandLine.Text;

namespace ConvA;

class Program {
    const string ConfigFileName = "convacfg.json";
    public static Program? Instance { get; private set; }
    public static bool IsHelpOrVersionRequested { get; set; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Program))]
    public static async Task<int> Main(string[] args) {
        var parser = new Parser(with => {
            with.CaseInsensitiveEnumValues = true;
        });

        ParserResult<Program>? parserResults = parser.ParseArguments<Program>(args);

        await parserResults.WithParsedAsync(async p => await p.Run());
        await parserResults.WithNotParsedAsync(async errors => await ShowErrors(errors));
        return ((parserResults is Parsed<Program>) || IsHelpOrVersionRequested ) ? 0 : -1;
    }
    static async Task ShowErrors(IEnumerable<Error> errors) {
        foreach (var error in errors) {
            if (error is HelpRequestedError) {
                Parser.Default.ParseArguments<Program>(new[] { "--help" });
                IsHelpOrVersionRequested = true;
                continue;
            } else if (error is VersionRequestedError) {
                Parser.Default.ParseArguments<Program>(new[] { "--version" });
                IsHelpOrVersionRequested = true;
                continue;
            } else if (error is UnknownOptionError unknownOptionError) {
                Console.WriteLine($"Unknown option: {unknownOptionError.Token}");
                Parser.Default.ParseArguments<Program>(new[] { "--help" });
                continue;
            }
        }
        await Task.CompletedTask;
    }

    public Config? Config { get; private set; }

    // Options
    [Value(0, MetaName = "input",  HelpText = "Path to working repository.")]
    public string? RepositoryPath { get; set; }

    [Option('t', "type", Required = false, Default = ConversionType.Proj, HelpText = "Project conversion type (package|dll|proj|proj2)")]
    public ConversionType Type { get; set; } = ConversionType.Proj;

    [Option('p', "path", Required = false, Default = null, HelpText = "Path to project to convert")]
    public string? ProjectPath { get; set; }

    [Option("patch-version", Required = false, Default = "", HelpText = "Version of package reference")]
    public string? PatchVersion { get; set; }

    //usage example
    [Usage(ApplicationAlias = "conva")]
    public static IEnumerable<Example> Examples {
        get {
            return new List<Example>() {
                new("Convert project in current dir to project references",
                    new Program { RepositoryPath = "~/work/my-repo", Type = ConversionType.Proj }),
                new("Convert project in current dir to dll references",
                    new Program { RepositoryPath = "~/work/my-repo", Type = ConversionType.Dll }),
                new("Convert project in current dir to package references with version 10.2.7",
                    new Program {
                        RepositoryPath = "~/work/my-repo",
                        Type = ConversionType.Package,
                        PatchVersion = "10.2.7"
                    }),
            };
        }
    }

    async Task Run() {
        Instance = this;
        string actualProjectPath = ProjectPath ?? Directory.GetCurrentDirectory();
        Config = await GetConfig(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), nameof(ConvA), ConfigFileName)) ?? new Config();
        RepositoryPath ??= Config.RepositoryPath;
        Config? localConfig = FindLocalConfig(actualProjectPath);
        RepositoryPath ??= localConfig?.RepositoryPath;
        RepositoryPath ??= GetRepositoryPathFromProject(actualProjectPath);
        if (String.IsNullOrEmpty(RepositoryPath) || !Directory.Exists(RepositoryPath)) {
            Console.WriteLine("Path to working repository is not specified.");
            Parser.Default.ParseArguments<Program>(new[] { "--help" });
            return;
        }

        RepositoryPath = PathHelper.ExpandPath(RepositoryPath);
        string projectDir = File.Exists(actualProjectPath) ? Path.GetDirectoryName(actualProjectPath)! : actualProjectPath;
        RepoInfo repoInfo = new(RepositoryPath);
        repoInfo.Build();
        string actualVersion = String.IsNullOrEmpty(PatchVersion) ? repoInfo.GetVersion() : PatchVersion;
        ProjectConverterBase converter = Type switch {
            ConversionType.Proj => new ProjectReferenceConverter(repoInfo, false),
            ConversionType.Proj2 => new ProjectReferenceConverter(repoInfo, true),
            ConversionType.Dll => new DllReferenceConverter(repoInfo),
            ConversionType.Package => new PackageReferenceConverter(repoInfo, actualVersion),
            _ => throw new NotSupportedException($"Conversion type {Type} is not supported")
        };
        IEnumerable<string> projectFiles = File.Exists(actualProjectPath) ? new[] { actualProjectPath } : Directory.EnumerateFiles(actualProjectPath, "*.csproj");
        foreach (var projectFileName in projectFiles) {
            Project project = new(projectFileName);
            converter.Convert(project);
            project.SaveBackup();
            project.Save();
        }
        await SaveConfig(RepositoryPath, Type, Path.Combine(projectDir, ConfigFileName));
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
    Config? FindLocalConfig(string path) {
        if (String.IsNullOrEmpty(path))
            return null;
        if (File.Exists(path))
            path = Path.GetDirectoryName(path)!;
        DirectoryInfo? directoryInfo = new(path);
        for (int i = 0; i < 3; i++) {
            var configFileInfo = new FileInfo(Path.Combine(directoryInfo.FullName, ConfigFileName));
            if (configFileInfo.Exists) {
                using var configStream = configFileInfo.OpenRead();
                return JsonSerializer.Deserialize<Config>(configStream) ?? new Config();
            }
            directoryInfo = directoryInfo.Parent;
            if (directoryInfo == null)
                return null;
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    async Task<Config?> GetConfig(string path) {
        var configFileInfo = new FileInfo(path);
        if (!configFileInfo.Exists)
            return new Config();
        await using var configStream = configFileInfo.OpenRead();
        return await JsonSerializer.DeserializeAsync<Config>(configStream);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public async Task SaveConfig(string repoPath, ConversionType conversionType, string path) {
        var configFileInfo = new FileInfo(path);
        if (!configFileInfo.Directory!.Exists)
            configFileInfo.Directory.Create();
        Config = new Config { RepositoryPath = repoPath, ConversionType = conversionType };
        await using var configStream = configFileInfo.OpenWrite();
        await JsonSerializer.SerializeAsync(configStream, Config);
    }
}