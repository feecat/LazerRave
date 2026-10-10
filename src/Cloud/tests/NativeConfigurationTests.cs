using System.Text;
using LazerRave.Bridge;
using Xunit;

namespace Cloud.Tests;

public sealed class NativeConfigurationTests : IDisposable
{
    private readonly string runtime = Path.Combine(Path.GetTempPath(), "lazerrave-native-" + Guid.NewGuid());
    private readonly NativeConfiguration configuration;
    public NativeConfigurationTests()
    {
        configuration = new(runtime);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration.ConfigPath)!);
    }
    public void Dispose() => Directory.Delete(runtime, true);
    private void Write(string xml) => File.WriteAllText(configuration.ConfigPath, xml, new UTF8Encoding(false));

    [Fact]
    public void ChangesKeepAccountsUnknownNodesAndLatestExternalEdits()
    {
        Write("<config><!--keep--><system><vsync>0</vsync><future>external edit</future></system><player><pass>unchanged</pass></player><sound><driver>3</driver></sound></config>");
        var before = File.ReadAllBytes(configuration.ConfigPath);
        configuration.Save(new Dictionary<string, string> { ["system/vsync"] = "1" });
        var result = NativeConfiguration.Read(configuration.ConfigPath);
        Assert.Equal("1", NativeConfiguration.Value(result, "system/vsync"));
        Assert.Equal("external edit", NativeConfiguration.Value(result, "system/future"));
        Assert.Equal("unchanged", NativeConfiguration.Value(result, "player/pass"));
        Assert.Equal("3", NativeConfiguration.Value(result, "sound/driver"));
        Assert.Contains("<!--keep-->", result.ToString());
        Assert.Equal(before, File.ReadAllBytes(configuration.ConfigPath + ".lazerrave.bak"));
    }

    [Fact]
    public void Cp932SerializationKeepsJapaneseAndNonCp932Paths()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(configuration.ConfigPath, "<?xml version=\"1.0\" encoding=\"Shift_JIS\"?><config><skin><fontname>日本語</fontname></skin></config>", Encoding.GetEncoding(932));
        configuration.Save(new Dictionary<string, string> { ["skin/select"] = @"LR2files\中文皮肤\select.lr2skin" });
        var result = NativeConfiguration.Read(configuration.ConfigPath);
        Assert.Equal("日本語", NativeConfiguration.Value(result, "skin/fontname"));
        Assert.Equal(@"LR2files\中文皮肤\select.lr2skin", NativeConfiguration.Value(result, "skin/select"));
        Assert.Equal("shift_jis", result.Declaration!.Encoding, ignoreCase: true);
        Assert.Equal((byte)'\n', File.ReadAllBytes(configuration.ConfigPath)[^1]);
    }

    [Fact]
    public void LibraryReplacementPreservesOtherJukeboxFieldsAndDeduplicatesRoots()
    {
        Write("<config><jukebox><path>old</path><custom>keep</custom></jukebox></config>");
        string bms = Path.Combine(runtime, "BMS"), shared = Path.Combine(runtime, "Shared");
        configuration.Save(new Dictionary<string, string>(), libraryRoots: [bms, bms, shared]);
        var result = NativeConfiguration.Read(configuration.ConfigPath);
        Assert.Equal(new[] { bms, shared }, result.Root!.Element("jukebox")!.Elements("path").Select(element => element.Value));
        Assert.Equal("keep", NativeConfiguration.Value(result, "jukebox/custom"));
    }

    [Fact]
    public void RepeatedSaveDoesNotRewriteOrReplaceBackup()
    {
        Write("<config><system><vsync>0</vsync></system></config>");
        var change = new Dictionary<string, string> { ["system/vsync"] = "1" };
        configuration.Save(change);
        var bytes = File.ReadAllBytes(configuration.ConfigPath);
        var backup = File.ReadAllBytes(configuration.ConfigPath + ".lazerrave.bak");
        configuration.Save(change);
        Assert.Equal(bytes, File.ReadAllBytes(configuration.ConfigPath));
        Assert.Equal(backup, File.ReadAllBytes(configuration.ConfigPath + ".lazerrave.bak"));
    }

    [Theory]
    [InlineData("<config><system><vsync>0</vsync><vsync>1</vsync></system></config>")]
    [InlineData("<!DOCTYPE config [<!ENTITY unsafe SYSTEM 'file:///etc/passwd'>]><config><system><vsync>&unsafe;</vsync></system></config>")]
    [InlineData("<wrong />")]
    public void InvalidConfigurationRemainsUntouched(string xml)
    {
        Write(xml);
        var before = File.ReadAllBytes(configuration.ConfigPath);
        Assert.ThrowsAny<Exception>(() => configuration.Save(new Dictionary<string, string> { ["system/vsync"] = "1" }));
        Assert.Equal(before, File.ReadAllBytes(configuration.ConfigPath));
        Assert.False(File.Exists(configuration.ConfigPath + ".lazerrave.bak"));
    }

    [Fact]
    public void InvalidExtensionDoesNotPartiallyWriteMainConfiguration()
    {
        Write("<config><system><screenmode>1</screenmode></system></config>");
        File.WriteAllText(configuration.ExtensionPath, "<broken>");
        var before = File.ReadAllBytes(configuration.ConfigPath);
        Assert.ThrowsAny<Exception>(() => configuration.Save(new Dictionary<string, string> { ["system/screenmode"] = "0" },
            new Dictionary<string, string> { ["system/screenmode"] = "2" }));
        Assert.Equal(before, File.ReadAllBytes(configuration.ConfigPath));
    }

    [Fact]
    public void LockedExtensionRollsBackInstalledMainFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        Write("<config><system><screenmode>1</screenmode></system></config>");
        File.WriteAllText(configuration.ExtensionPath, "<config><system><screenmode>1</screenmode></system></config>");
        var before = File.ReadAllBytes(configuration.ConfigPath);
        var extensionBefore = File.ReadAllBytes(configuration.ExtensionPath);
        using var locked = new FileStream(configuration.ExtensionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => configuration.Save(new Dictionary<string, string> { ["system/screenmode"] = "0" },
            new Dictionary<string, string> { ["system/screenmode"] = "2" }));
        Assert.True(error is IOException or UnauthorizedAccessException, "A locked native configuration must reject replacement.");
        Assert.Equal(before, File.ReadAllBytes(configuration.ConfigPath));
        Assert.Equal(extensionBefore, File.ReadAllBytes(configuration.ExtensionPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(configuration.ConfigPath)!, "*.tmp"));
    }

    [Fact]
    public void MissingConfigurationCanBeCreatedWithExtensionMode()
    {
        configuration.Save(new Dictionary<string, string> { ["system/screenmode"] = "0" }, new Dictionary<string, string> { ["system/screenmode"] = "2" });
        Assert.Equal("0", NativeConfiguration.Value(NativeConfiguration.Read(configuration.ConfigPath), "system/screenmode"));
        Assert.Equal("2", NativeConfiguration.Value(NativeConfiguration.Read(configuration.ExtensionPath), "system/screenmode"));
    }
}
