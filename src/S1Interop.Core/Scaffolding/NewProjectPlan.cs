namespace S1Interop.Core.Scaffolding;

/// <summary>
/// Describes the files that will be written for a new backend-neutral project.
/// </summary>
/// <param name="ProjectName">The generated project and root namespace name.</param>
/// <param name="TargetDirectory">The target directory for the scaffolded project.</param>
/// <param name="SolutionPath">The solution file path.</param>
/// <param name="ProjectPath">The project file path.</param>
/// <param name="CorePath">The starter mod source file path.</param>
/// <param name="StarterPath">The generated S1Interop starter declaration file path.</param>
/// <param name="LocalPropsExamplePath">The local path configuration example file path.</param>
/// <param name="GitignorePath">The generated <c>.gitignore</c> file path.</param>
/// <param name="ReadmePath">The generated project README path.</param>
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
    /// <summary>
    /// Gets all files that the scaffold apply step writes for this plan.
    /// </summary>
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
