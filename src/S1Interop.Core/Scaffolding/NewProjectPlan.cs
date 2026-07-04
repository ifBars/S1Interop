namespace S1Interop.Core.Scaffolding;

public sealed record NewProjectPlan(
    string ProjectName,
    string TargetDirectory,
    string SolutionPath,
    string ProjectPath,
    string CorePath,
    string StarterPath,
    string LocalPropsExamplePath,
    string GitignorePath,
    string ReadmePath)
{
    public IReadOnlyList<string> PlannedFiles =>
    [
        SolutionPath,
        ProjectPath,
        CorePath,
        StarterPath,
        LocalPropsExamplePath,
        GitignorePath,
        ReadmePath
    ];
}
