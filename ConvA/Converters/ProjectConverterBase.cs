namespace ConvA;

public abstract class ProjectConverterBase {
    protected ProjectConverterBase(RepoInfo repoInfo) {
        RepoInfo = repoInfo;
    }

    public RepoInfo RepoInfo { get; }

    public abstract void Convert(Project project);
}