using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.FieldTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

/// <summary>
/// Preservation and refusal checks of the writer's dependency tables, re-homed from
/// DependencyUsageTests when the CDO usage collector was retired. Usage bits are
/// registry-only metadata that native save regenerates; the writer preserves what it
/// read and refuses tables it cannot write consistently, leaving the destination intact.
/// </summary>
[TestClass]
public class DependencyTableWriteTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "Usage", "BP_Usage.uasset");
    private static UAsset Read(string fixture = "BP_Usage.uasset") => new(Path.Combine(Path.GetDirectoryName(Fixture), fixture), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static JObject Document(string fixture = "BP_Usage.uasset") => JObject.Parse(Read(fixture).SerializeJson());
    private static JObject Cdo(JObject json) => json["Exports"].OfType<JObject>().Single(e => (string)e["ObjectName"] == "Default__BP_Usage_C");
    private static JObject Property(JObject json, string name) => Cdo(json)["Data"].OfType<JObject>().Single(p => (string)p["Name"] == name);
    private static int Import(UAsset asset, string name) => asset.Imports.FindIndex(i => i.ObjectName.ToString() == name);
    private static UAsset Edit()
    {
        var json = Document();
        Property(json, "RuntimeHard")["Value"] = -Import(Read(), "Target") - 1;
        Property(json, "RuntimeSoft")["Value"] = Property(json, "EditorSoft")["Value"].DeepClone();
        return UAsset.DeserializeJson(json.ToString());
    }
    private static void Refuses(UAsset asset, string reason)
    {
        string path = Path.Combine(Path.GetTempPath(), "UAssetUsage-" + Guid.NewGuid().ToString("N") + ".uasset");
        byte[] sentinel = { 6, 2, 6, 4, 3 };
        File.WriteAllBytes(path, sentinel);
        try
        {
            var error = Assert.ThrowsException<FormatException>(() => UAsset.DeserializeJson(asset.SerializeJson()).Write(path));
            StringAssert.Contains(error.Message, reason);
            CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void NativeEditorOnlyReferencesAndUnchangedBytesArePreserved()
    {
        var original = Read();
        var asset = UAsset.DeserializeJson(original.SerializeJson());
        using var bytes = asset.WriteData();
        byte[] source = File.ReadAllBytes(Fixture), written = bytes.ToArray();
        for (int i = 0; i < asset.Exports.Count; i++)
            CollectionAssert.AreEqual(source.AsSpan((int)original.Exports[i].SerialOffset, (int)original.Exports[i].SerialSize).ToArray(), written.AsSpan((int)asset.Exports[i].SerialOffset, (int)asset.Exports[i].SerialSize).ToArray());
        CollectionAssert.AreEqual(original.ImportBits.Cast<bool>().ToArray(), asset.ImportBits.Cast<bool>().ToArray());
        CollectionAssert.AreEqual(original.SoftPackageBits.Cast<bool>().ToArray(), asset.SoftPackageBits.Cast<bool>().ToArray());
    }

    [TestMethod]
    public void NativeWeakCdoReferencePreservesUsageAndPayloadsOnJsonRoundTrip()
    {
        var original = Read("Weak/BP_Usage.uasset");
        var field = original.Exports.OfType<ClassExport>().Single(e => e.ObjectName.ToString() == "BP_Usage_C")
            .LoadedProperties.Single(p => p.Name.ToString() == "RuntimeHard");
        Assert.IsInstanceOfType(field, typeof(FWeakObjectProperty));
        var cdo = original.Exports.OfType<NormalExport>().Single(e => e.ObjectName.ToString() == "Default__BP_Usage_C");
        var reference = (ObjectPropertyData)cdo.Data.Single(p => p.Name.ToString() == "RuntimeHard");
        Assert.AreEqual(-Import(original, "Target") - 1, reference.Value.Index);
        Assert.IsFalse(original.ImportBits[Import(original, "Target")]);
        Assert.IsFalse(original.ImportBits[Import(original, "/Game/Usage/Target")]);

        var asset = UAsset.DeserializeJson(original.SerializeJson());
        using var bytes = asset.WriteData();
        var after = new UAsset(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8,
            customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        CollectionAssert.AreEqual(original.ImportBits.Cast<bool>().ToArray(), after.ImportBits.Cast<bool>().ToArray());
        CollectionAssert.AreEqual(original.SoftPackageBits.Cast<bool>().ToArray(), after.SoftPackageBits.Cast<bool>().ToArray());
        foreach (string table in new[] { "Imports", "SoftObjectPathList", "SoftPackageReferenceList", "DependsMap", "SearchableNames" })
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(original.SerializeJson())[table], JObject.Parse(after.SerializeJson())[table]), table);
        byte[] source = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Fixture), "Weak", "BP_Usage.uasset"));
        byte[] written = bytes.ToArray();
        for (int i = 0; i < original.Exports.Count; i++)
            CollectionAssert.AreEqual(source.AsSpan((int)original.Exports[i].SerialOffset, (int)original.Exports[i].SerialSize).ToArray(),
                written.AsSpan((int)after.Exports[i].SerialOffset, (int)after.Exports[i].SerialSize).ToArray());
        using var repeat = UAsset.DeserializeJson(after.SerializeJson()).WriteData();
        CollectionAssert.AreEqual(written, repeat.ToArray());
    }

    // The writer collects no dependencies offline. A package whose soft package list lacks
    // a package that an indexed soft path still references round-trips unchanged; native
    // load, compile and save restore the list (FORK-S1 S1-2 convergence check).
    [TestMethod]
    public void WriterDoesNotCollectDependencies()
    {
        var json = Document();
        Property(json, "RuntimeSoft")["Value"] = Property(json, "EditorSoft")["Value"].DeepClone();
        var asset = UAsset.DeserializeJson(json.ToString());
        int target = asset.SoftPackageReferenceList.FindIndex(p => p.Value == "/Game/Usage/Target");
        asset.SoftPackageReferenceList.RemoveAt(target);
        asset.SoftPackageBits = new System.Collections.BitArray(asset.SoftPackageBits.Cast<bool>().Where((_, i) => i != target).ToArray());
        var paths = asset.SoftObjectPathList.ToArray();
        byte[] written = asset.WriteData().ToArray();
        var reread = new UAsset(new AssetBinaryReader(new MemoryStream(written), asset), EngineVersion.VER_UE5_8,
            customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.IsFalse(reread.SoftPackageReferenceList.Any(p => p.Value == "/Game/Usage/Target"));
        CollectionAssert.AreEqual(paths, reread.SoftObjectPathList.ToArray());
        CollectionAssert.AreEqual(written, reread.WriteData().ToArray());
        CollectionAssert.AreEqual(written, UAsset.DeserializeJson(reread.SerializeJson()).WriteData().ToArray());
    }

    [TestMethod]
    public void CyclicImportOuterRefusesWithDestinationIntact()
    {
        var asset = Edit(); int index = Import(asset, "Target");
        asset.Imports[index].OuterIndex = FPackageIndex.FromImport(index); Refuses(asset, "Cyclic import outer chain");
    }

    [DataTestMethod]
    [DataRow(-2147483648)]
    [DataRow(2147483647)]
    public void InvalidOuterRefusesWithDestinationIntact(int index)
    {
        var asset = Edit(); asset.Imports[Import(asset, "Target")].OuterIndex = new(index); Refuses(asset, "outside the import/export tables");
    }

    [TestMethod]
    public void MissingSoftUsageFlagsRefuseWithDestinationIntact()
    {
        var asset = Edit(); asset.SoftPackageBits = null; Refuses(asset, "Soft package dependency bits do not match");
    }

    [TestMethod]
    public void MismatchedSoftUsageFlagsRefuseWithDestinationIntact()
    {
        var asset = Edit(); asset.SoftPackageBits.Length++; Refuses(asset, "Soft package dependency bits do not match");
    }
}
