namespace OpenSandbox.Store.FileSystem.Options;

public sealed class FileSystemStoreOptions
{
    public const string SectionName = "FileSystemStore";

    public string StorePath { get; set; } = "data/sandboxes.json";
}
