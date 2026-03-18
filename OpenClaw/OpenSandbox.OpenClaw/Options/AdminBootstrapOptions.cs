namespace OpenSandbox.OpenClaw.Options;

public sealed class AdminBootstrapOptions
{
    public const string SectionName = "AdminBootstrap";

    public string UserNameEnv { get; set; } = "OPENCLAW_ADMIN_USERNAME";
    public string PasswordEnv { get; set; } = "OPENCLAW_ADMIN_PASSWORD";
    public string DisplayNameEnv { get; set; } = "OPENCLAW_ADMIN_DISPLAY_NAME";
}
