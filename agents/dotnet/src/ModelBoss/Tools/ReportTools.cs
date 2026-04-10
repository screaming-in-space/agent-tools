using System.ComponentModel;

namespace ModelBoss.Tools;

/// <summary>
/// File output tool for the ModelBoss agent to write benchmark reports.
/// </summary>
public sealed class ReportTools(string outputDirectory)
{
    [Description("Writes the benchmark report to a file. The content should be raw markdown, not wrapped in code fences.")]
    public async Task<string> WriteReportAsync(string fileName, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Error: fileName is required.";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "Error: content is required.";
        }

        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Sanitize filename
        var safeName = Path.GetFileName(fileName);
        var filePath = Path.Combine(outputDirectory, safeName);

        await File.WriteAllTextAsync(filePath, content, ct);

        return $"Report written to {filePath}";
    }
}
