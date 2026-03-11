namespace OpenSandbox.Runtime.Docker.Options;

public sealed class DockerRuntimeOptions
{
    public const string SectionName = "DockerRuntime";

    public string DockerCommand { get; set; } = "docker";

    public List<int> PublishedPorts { get; set; } =
    [
        80,
        443,
        3000,
        4173,
        5000,
        5050,
        5173,
        8000,
        8080,
        8081,
        8787,
        18789
    ];
}
