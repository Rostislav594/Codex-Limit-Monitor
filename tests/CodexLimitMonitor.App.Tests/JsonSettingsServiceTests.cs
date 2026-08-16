using System.IO;
using CodexLimitMonitor.App.Services;

namespace CodexLimitMonitor.App.Tests;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripNormalizedSettings()
    {
        var testDirectory = CreateTestDirectory();
        var path = Path.Combine(testDirectory, "settings.json");
        try
        {
            var service = new JsonSettingsService(path);
            var source = new AppSettings
            {
                WindowLeft = 120.5,
                WindowTop = -45,
                IsCompact = true,
                Opacity = 0.82,
                RefreshIntervalSeconds = 90,
                StartMinimized = true,
            };

            await service.SaveAsync(source);
            var loaded = await service.LoadAsync();

            Assert.Equal(source.Normalize(), loaded);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task InvalidJsonFallsBackToDefaults()
    {
        var testDirectory = CreateTestDirectory();
        var path = Path.Combine(testDirectory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{not-json");

            var loaded = await new JsonSettingsService(path).LoadAsync();

            Assert.Equal(new AppSettings(), loaded);
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexLimitMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path))
        {
            File.Delete(file);
        }

        Directory.Delete(path, recursive: false);
    }
}
