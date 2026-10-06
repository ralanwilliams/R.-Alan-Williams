using System.Reflection;

namespace Cv.Data.Migrations;

/// <summary>Loads migration SQL embedded in this assembly (see Cv.Data.csproj).</summary>
internal static class SqlScript
{
    public static string Load(string fileName)
    {
        var assembly = typeof(SqlScript).Assembly;
        using var stream = assembly.GetManifestResourceStream(fileName)
            ?? throw new InvalidOperationException(
                $"Embedded migration script '{fileName}' not found. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
