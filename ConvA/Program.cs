using System.Diagnostics.CodeAnalysis;
using CommandLine;
using System.Text.Json;

namespace ConvA;

class Program {
    public static Program? Instance { get; private set; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Program))]
    public static async Task Main(string[] args) {
        var parser = Parser.Default.ParseArguments<Program>(args);
        await parser.WithParsedAsync(async p => await p.Run());
    }

    public Config? Config { get; private set; }

    public string? ProjectPath { get; private set; }

    // Options
    [Value(0, MetaName = "input", HelpText = "Path to working repository.")]
    public string? RepositoryPath { get; set; }

    [Option('d', "dll", Required = false, Default = false, HelpText = "Convert to dll reference")]
    public bool UseDllReference { get; set; } = false;

    [Option('r', "reverse", Required = false, Default = false, HelpText = "Reverse conversion from dll or project reference to package reference")]
    public bool ReverseConversion { get; set; } = false;

    [Option('p', "project-refs", Required = false, Default = false, HelpText = "Use .Refs. project reference")]
    public bool UseRefsForProjectReferences { get; set; } = false;

    [Option('v', "version", Required = false, Default = "", HelpText = "Version of package reference")]
    public string? Version { get; set; }

    async Task Run() {
        Instance = this;
        ProjectPath = Directory.GetCurrentDirectory();
        Config = await GetConfig() ?? new Config();
        RepositoryPath ??= Config.RepositoryPath;
        if (String.IsNullOrEmpty(RepositoryPath)) {
            Console.WriteLine("Path to working repository is not specified.");
            Parser.Default.ParseArguments<Program>(new[] { "--help" });
            return;
        }

        RepositoryPath = PathHelper.ExpandPath(RepositoryPath);
        DirectoryInfo fileInfo = new(RepositoryPath);
        if (!fileInfo.Exists) {
            Console.WriteLine("Path to working repository does not exist.");
            Parser.Default.ParseArguments<Program>(new[] { "--help" });
            return;
        }

        RepoInfo repoInfo = new(RepositoryPath);
        repoInfo.Build();
        string actualVersion = String.IsNullOrEmpty(Version) ? repoInfo.GetVersion() : Version;
        ProjectConverterBase converter = ReverseConversion ? new ProjectRevertConverter(repoInfo, actualVersion) : new ProjectConverter(repoInfo, UseDllReference, UseRefsForProjectReferences);
        foreach (var projectFileName in new DirectoryInfo(ProjectPath).EnumerateFiles("*.csproj")) {
            Project project = new(projectFileName.FullName);
            converter.Convert(project);
            project.SaveBackup();
            project.Save();
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    async Task<Config?> GetConfig() {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configFileInfo = new FileInfo(Path.Combine(path, nameof(ConvA), "config.json"));
        if (!configFileInfo.Exists)
            return new Config();
        await using var configStream = configFileInfo.OpenRead();
        return await JsonSerializer.DeserializeAsync<Config>(configStream);
    }
}