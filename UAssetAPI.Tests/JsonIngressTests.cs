using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class JsonIngressTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");
    private static UAsset Read() => new(Fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);

    [TestMethod]
    public void AllJsonIngressesRejectUnrelatedTypeMetadata()
    {
        const string json = "{\"$type\":\"System.IO.FileInfo, System.Private.CoreLib\",\"fileName\":\"untrusted\"}";
        Assert.ThrowsException<JsonSerializationException>(() => UAsset.DeserializeJson(json));
        Assert.ThrowsException<JsonSerializationException>(() => UAsset.DeserializeJson(new MemoryStream(Encoding.UTF8.GetBytes(json))));
        Assert.ThrowsException<JsonSerializationException>(() => Read().DeserializeJsonObject<object>(json));
        string nested = "{\"Type\":\"Text\",\"Value\":" + json + "}";
        Assert.ThrowsException<JsonSerializationException>(() => Read().DeserializeJsonObject<FFormatArgumentValue>(nested));
    }

    [TestMethod]
    public void NewNativeNameIsAddedWithoutAddingJsonOnlyLabels()
    {
        var asset = Read();
        int before = asset.GetNameMapIndexList().Count;
        var json = JObject.Parse(asset.SerializeJson());
        var node = ((JArray)json["Exports"]).OfType<JObject>().First(x => x["Pins"] != null);
        ((JObject)((JArray)node["Pins"])[0])["PinName"] = "A_New_Native_Pin_Name_77";
        var restored = UAsset.DeserializeJson(json.ToString());
        using var bytes = restored.WriteData();
        Assert.AreEqual(before + 1, restored.GetNameMapIndexList().Count);
        var reread = new UAsset(new AssetBinaryReader(bytes, restored), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.AreEqual("A_New_Native_Pin_Name_77", reread.Exports.OfType<EdGraphNodeExport>().First().Pins[0].PinName.ToString());
    }

    [TestMethod]
    public void RefusedWritePreservesExistingDestinationAndLeavesNoTemporaryFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "UAssetAPI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "existing.uasset");
        File.Copy(Fixture, path);
        byte[] before = File.ReadAllBytes(path);
        try
        {
            var asset = Read();
            asset.Exports.OfType<EdGraphNodeExport>().First().Pins[0].OwningNode = new FPackageIndex(int.MaxValue);
            Assert.ThrowsException<FormatException>(() => asset.Write(path));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            if (OperatingSystem.IsWindows())
            {
                using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool refused = false;
                    try { Read().Write(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { refused = true; }
                    Assert.IsTrue(refused, "A locked destination must refuse replacement");
                }
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            }
            Read().Write(path);
            Assert.AreEqual(6, new UAsset(path, EngineVersion.VER_UE5_8).Exports.OfType<EdGraphNodeExport>().Count());
            Assert.AreEqual(1, Directory.GetFiles(folder).Length);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(folder);
        }
    }
}
