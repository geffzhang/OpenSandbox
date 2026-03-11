namespace OpenSandbox.Abstractions.Contracts;

public sealed class SandboxImageReference
{
    public string Uri { get; set; } = string.Empty;
}

public sealed class SandboxResourceLimits
{
    public string? Cpu { get; set; }
    public string? Memory { get; set; }
}

public sealed class SandboxHostVolume
{
    public string Path { get; set; } = string.Empty;
}

public sealed class SandboxVolumeSpec
{
    public string Name { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public bool? ReadOnly { get; set; }
    public string? SubPath { get; set; }
    public SandboxHostVolume? Host { get; set; }
}
