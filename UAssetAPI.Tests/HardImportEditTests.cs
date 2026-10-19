using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class HardImportEditTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_SoftReference.uasset");
    private static UAsset Read() => new(Fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static ObjectPropertyData Hard(UAsset asset) => asset.Exports.OfType<NormalExport>().Single(e => e.ObjectName.ToString() == "Default__Source_C").Data.OfType<ObjectPropertyData>().Single(p => p.Name.ToString() == "ExternalAsset");

    private static UAsset JsonEdit()
    {
        var json = JObject.Parse(Read().SerializeJson());
        var imports = (JArray)json["Imports"];
        var package = (JObject)imports.OfType<JObject>().Single(i => (string)i["ObjectName"] == "/Game/UAssetAPIProbe/UserCurve").DeepClone();
        package["ObjectName"] = "/Game/UAssetAPIProbe/UserHard";
        imports.Add(package);
        var obj = (JObject)imports.OfType<JObject>().Single(i => (string)i["ObjectName"] == "UserCurve" && (string)i["ClassName"] == "CurveFloat").DeepClone();
        obj["ObjectName"] = "UserHard";
        obj["OuterIndex"] = -imports.Count;
        imports.Add(obj);
        var cdo = json["Exports"].OfType<JObject>().Single(e => (string)e["ObjectName"] == "Default__Source_C");
        cdo["Data"].OfType<JObject>().Single(p => (string)p["Name"] == "ExternalAsset")["Value"] = -imports.Count;
        return UAsset.DeserializeJson(json.ToString());
    }

    [TestMethod]
    public void JsonHardReferenceAppendMaintainsImportUsageAndOldSlots()
    {
        var original = Read();
        var asset = JsonEdit();
        var oldBits = original.ImportBits.Cast<bool>().ToArray();
        using var bytes = asset.WriteData();
        Assert.AreEqual(original.Imports.Count + 2, asset.Imports.Count);
        Assert.AreEqual(asset.Imports.Count, asset.ImportBits.Count);
        CollectionAssert.AreEqual(oldBits, asset.ImportBits.Cast<bool>().Take(oldBits.Length).ToArray());
        Assert.IsTrue(asset.ImportBits[oldBits.Length] && asset.ImportBits[oldBits.Length + 1]);
        var oldImports = JObject.Parse(original.SerializeJson())["Imports"];
        var afterImports = new JArray(JObject.Parse(asset.SerializeJson())["Imports"].Take(original.Imports.Count));
        Assert.IsTrue(JToken.DeepEquals(oldImports, afterImports));
        byte[] source = File.ReadAllBytes(Fixture);
        byte[] written = bytes.ToArray();
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].ObjectName.ToString() == "Default__Source_C") continue;
            var before = original.Exports[i];
            var after = asset.Exports[i];
            CollectionAssert.AreEqual(source.AsSpan((int)before.SerialOffset, (int)before.SerialSize).ToArray(), written.AsSpan((int)after.SerialOffset, (int)after.SerialSize).ToArray(), after.ObjectName.ToString());
        }
        var reread = new UAsset(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.AreEqual(reread.Imports.Count, reread.ImportBits.Count);
        var hard = Hard(reread).Value.ToImport(reread);
        Assert.AreEqual("UserHard", hard.ObjectName.ToString());
        Assert.AreEqual("/Game/UAssetAPIProbe/UserHard", hard.OuterIndex.ToImport(reread).ObjectName.ToString());
        using var repeat = UAsset.DeserializeJson(asset.SerializeJson()).WriteData();
        CollectionAssert.AreEqual(written, repeat.ToArray());
    }

    [TestMethod]
    public void AddImportAlsoMaintainsUsageFlags()
    {
        var asset = Read();
        int before = asset.Imports.Count;
        var import = new Import("/Script/CoreUObject", "Package", new FPackageIndex(), "/Game/UAssetAPIProbe/UserHard", false, asset) { PackageName = FName.FromString(asset, "None") };
        Assert.AreEqual(-before - 1, asset.AddImport(import).Index);
        using var bytes = asset.WriteData();
        Assert.AreEqual(asset.Imports.Count, asset.ImportBits.Count);
        Assert.IsTrue(asset.ImportBits[before]);
    }

    private static void AssertDestinationPreserved(UAsset asset)
    {
        string destination = Path.Combine(Path.GetTempPath(), "UAssetAPI-HardImport-" + Guid.NewGuid().ToString("N") + ".uasset");
        byte[] sentinel = { 2, 7, 1, 8 };
        File.WriteAllBytes(destination, sentinel);
        try
        {
            Assert.ThrowsException<FormatException>(() => asset.Write(destination));
            CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(destination));
        }
        finally { File.Delete(destination); }
    }

    [DataTestMethod]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void InvalidObjectReferenceRefusesBeforeDestinationWrite(int index)
    {
        var asset = Read();
        Hard(asset).Value = new FPackageIndex(index);
        AssertDestinationPreserved(UAsset.DeserializeJson(asset.SerializeJson()));
    }

    [TestMethod]
    public void NullObjectReferenceUsesTheExistingNativeNullDefault()
    {
        var asset = Read();
        Hard(asset).Value = null;
        var restored = UAsset.DeserializeJson(asset.SerializeJson());
        Assert.AreEqual(0, Hard(restored).Value.Index);
        using var bytes = restored.WriteData();
        var reread = new UAsset(new AssetBinaryReader(bytes, restored), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.AreEqual(0, Hard(reread).Value.Index);
    }

    [DataTestMethod]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void InvalidImportOuterRefusesBeforeDestinationWrite(int index)
    {
        var asset = JsonEdit();
        asset.Imports.Last().OuterIndex = new FPackageIndex(index);
        AssertDestinationPreserved(asset);
    }

    [TestMethod]
    public void ExcessImportFlagsRefuseWithoutTruncation()
    {
        var asset = Read();
        asset.ImportBits.Length = asset.Imports.Count + 1;
        AssertDestinationPreserved(asset);
        Assert.AreEqual(asset.Imports.Count + 1, asset.ImportBits.Count);
    }

    [TestMethod]
    public void MissingModernImportFlagsRefuseBeforeDestinationWrite()
    {
        var asset = Read();
        asset.ImportBits = null;
        AssertDestinationPreserved(asset);
    }
}
