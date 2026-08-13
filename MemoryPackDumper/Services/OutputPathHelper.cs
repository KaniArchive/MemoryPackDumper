using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Services;

public static class OutputPathHelper
{
    public static string BuildFilePath(string outputDir, string namespacePath, string fileName, string extension)
    {
        if (string.IsNullOrEmpty(namespacePath))
        {
            EnsureDirectory(outputDir);
            return Path.Combine(outputDir, $"{fileName}{extension}");
        }

        var folderPath = Path.Combine(outputDir, namespacePath.Replace('.', Path.DirectorySeparatorChar));
        EnsureDirectory(folderPath);

        return Path.Combine(folderPath, $"{fileName}{extension}");
    }

    public static string SanitizeFileName(string typeName) => SchemaTypeConverter.StripGenerics(typeName);

    public static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }
}