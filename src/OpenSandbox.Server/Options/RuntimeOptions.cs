namespace OpenSandbox.Server.Options;

public sealed class RuntimeOptions
{
    public const string SectionName = "Runtime";

    public string Type { get; set; } = "docker";
}
