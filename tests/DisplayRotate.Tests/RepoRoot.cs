namespace DisplayRotate.Tests;

internal static class RepoRoot
{
    public static DirectoryInfo Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DisplayRotate.slnx")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException(
            "Could not find DisplayRotate.slnx above " + AppContext.BaseDirectory);
    }

    public static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Find().FullName, .. parts]));
}
