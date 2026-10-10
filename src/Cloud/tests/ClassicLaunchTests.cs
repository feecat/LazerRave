using LazerRave.Bridge;
using Xunit;

namespace Cloud.Tests;

public sealed class ClassicLaunchTests
{
    [Fact]
    public void ClassicLaunchUsesRuntimeDirectoryAndNoBridgeRequest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lazerrave-classic-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Throws<FileNotFoundException>(() => ClassicLaunch.Create(directory));
            var executable = Path.Combine(directory, "OpenLR2_x64.exe");
            File.WriteAllBytes(executable, []);
            var start = ClassicLaunch.Create(directory);
            Assert.Equal(executable, start.FileName);
            Assert.Equal(directory, start.WorkingDirectory);
            Assert.Empty(start.ArgumentList);
            Assert.False(start.UseShellExecute);
            Assert.True(start.CreateNoWindow);
        }
        finally { Directory.Delete(directory, true); }
    }
}
