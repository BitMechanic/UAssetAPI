using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.FieldTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class DependencyUsageTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "Usage", "BP_Usage.uasset");
    private static UAsset Read(string fixture = "BP_Usage.uasset") => new(Path.Combine(Path.GetDirectoryName(Fixture), fixture), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static JObject Document(string fixture = "BP_Usage.uasset") => JObject.Parse(Read(fixture).SerializeJson());
    private static JObject Cdo(JObject json) => json["Exports"].OfType<JObject>().Single(e => (string)e["ObjectName"] == "Default__BP_Usage_C");
    private static JObject Property(JObject json, string name) => Cdo(json)["Data"].OfType<JObject>().Single(p => (string)p["Name"] == name);
    private static int Import(UAsset asset, string name) => asset.Imports.FindIndex(i => i.ObjectName.ToString() == name);
    private static int Package(UAsset asset, string name) => asset.SoftPackageReferenceList.FindIndex(p => p.Value == "/Game/Usage/" + name);
    private static UAsset Edit(bool hard = true, bool soft = true, string fixture = "BP_Usage.uasset")
    {
        var json = Document(fixture);
        if (hard) Property(json, "RuntimeHard")["Value"] = -Import(Read(fixture), "Target") - 1;
        if (soft) Property(json, "RuntimeSoft")["Value"] = Property(json, "EditorSoft")["Value"].DeepClone();
        return UAsset.DeserializeJson(json.ToString());
    }
    private static UAsset RoundTrip(UAsset asset)
    {
        using var bytes = asset.WriteData();
        return new(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    }
    private static void Refuses(UAsset asset)
    {
        string path = Path.Combine(Path.GetTempPath(), "UAssetUsage-" + Guid.NewGuid().ToString("N") + ".uasset");
        byte[] sentinel = { 6, 2, 6, 4, 3 };
        File.WriteAllBytes(path, sentinel);
        try
        {
            Assert.ThrowsException<FormatException>(() => UAsset.DeserializeJson(asset.SerializeJson()).Write(path));
            CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [DataTestMethod]
    [DataRow(true, false, "BP_Usage.uasset")]
    [DataRow(false, true, "BP_Usage.uasset")]
    [DataRow(true, true, "BP_Usage.uasset")]
    [DataRow(true, false, "Transient/BP_Usage.uasset")]
    [DataRow(false, true, "Transient/BP_Usage.uasset")]
    [DataRow(true, true, "Transient/BP_Usage.uasset")]
    public void ExistingReferencesPromoteOnlyTheirRuntimeUsage(bool hard, bool soft, string fixture)
    {
        var before = Read(fixture);
        Assert.IsFalse(before.ImportBits[Import(before, "Target")]);
        Assert.IsFalse(before.SoftPackageBits[Package(before, "Target")]);
        var asset = Edit(hard, soft, fixture);
        var after = RoundTrip(asset);
        Assert.AreEqual(hard, after.ImportBits[Import(after, "Target")]);
        Assert.AreEqual(hard, after.ImportBits[Import(after, "/Game/Usage/Target")]);
        Assert.AreEqual(soft, after.SoftPackageBits[Package(after, "Target")]);
        for (int i = 0; i < before.ImportBits.Length; i++)
            if (!hard || (i != Import(before, "Target") && i != Import(before, "/Game/Usage/Target")))
                Assert.AreEqual(before.ImportBits[i], after.ImportBits[i], before.Imports[i].ObjectName.ToString());
        for (int i = 0; i < before.SoftPackageBits.Length; i++)
            if (!soft || i != Package(before, "Target")) Assert.AreEqual(before.SoftPackageBits[i], after.SoftPackageBits[i]);
        foreach (string table in new[] { "Imports", "SoftObjectPathList", "SoftPackageReferenceList", "DependsMap", "SearchableNames" })
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(before.SerializeJson())[table], JObject.Parse(after.SerializeJson())[table]), table);
        var beforeCdo = Cdo(JObject.Parse(before.SerializeJson()));
        var afterCdo = Cdo(JObject.Parse(after.SerializeJson()));
        foreach (var value in beforeCdo["Data"].OfType<JObject>())
        {
            string name = (string)value["Name"];
            if ((hard && name == "RuntimeHard") || (soft && name == "RuntimeSoft")) continue;
            Assert.IsTrue(JToken.DeepEquals(value, afterCdo["Data"].OfType<JObject>().Single(p => (string)p["Name"] == name)), name);
        }
        using var first = asset.WriteData();
        using var repeat = UAsset.DeserializeJson(asset.SerializeJson()).WriteData();
        CollectionAssert.AreEqual(first.ToArray(), repeat.ToArray());
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

    [DataTestMethod]
    [DataRow("EditorHard")]
    [DataRow("OtherHard")]
    public void EditorOnlyPropertyEditDoesNotPromoteAnImport(string field)
    {
        var json = Document();
        Property(json, field)["Value"] = -Import(Read(), "Other") - 1;
        var after = RoundTrip(UAsset.DeserializeJson(json.ToString()));
        CollectionAssert.AreEqual(Read().ImportBits.Cast<bool>().ToArray(), after.ImportBits.Cast<bool>().ToArray());
    }

    [TestMethod]
    public void EditorOnlySoftEditDoesNotPromoteAnIndexedPath()
    {
        var json = Document();
        Property(json, "EditorSoft")["Value"] = Property(json, "OtherSoft")["Value"].DeepClone();
        var after = RoundTrip(UAsset.DeserializeJson(json.ToString()));
        CollectionAssert.AreEqual(Read().SoftPackageBits.Cast<bool>().ToArray(), after.SoftPackageBits.Cast<bool>().ToArray());
    }

    [TestMethod]
    public void NullRuntimeHardReferenceDoesNotPromoteEditorOnlyImports()
    {
        var json = Document(); Property(json, "RuntimeHard")["Value"] = 0;
        var after = RoundTrip(UAsset.DeserializeJson(json.ToString()));
        Assert.IsFalse(after.ImportBits[Import(after, "Target")]);
    }

    [TestMethod]
    public void CyclicImportOuterRefusesWithDestinationIntact()
    {
        var asset = Edit(); int index = Import(asset, "Target");
        asset.Imports[index].OuterIndex = FPackageIndex.FromImport(index); Refuses(asset);
    }

    [DataTestMethod]
    [DataRow(-2147483648)]
    [DataRow(2147483647)]
    public void InvalidOuterRefusesWithDestinationIntact(int index)
    {
        var asset = Edit(); asset.Imports[Import(asset, "Target")].OuterIndex = new(index); Refuses(asset);
    }

    [TestMethod]
    public void MissingSoftUsageFlagsRefuseWithDestinationIntact()
    {
        var asset = Edit(); asset.SoftPackageBits = null; Refuses(asset);
    }

    [TestMethod]
    public void MismatchedSoftUsageFlagsRefuseWithDestinationIntact()
    {
        var asset = Edit(); asset.SoftPackageBits.Length++; Refuses(asset);
    }

    [TestMethod]
    public void NonnativeImportClassPromotionRequiresItsOwnDiscovery()
    {
        var asset = Edit(); asset.Imports[Import(asset, "Target")].ClassPackage = FName.FromString(asset, "/Game/UnknownClass"); Refuses(asset);
    }

    [TestMethod]
    public void ExternalPackagePromotionRequiresItsOwnDiscovery()
    {
        var asset = Edit(); asset.Imports[Import(asset, "Target")].PackageName = FName.FromString(asset, "/Game/External"); Refuses(asset);
    }

    [TestMethod]
    public void IndexedRuntimePathCollectsAMissingPackageWithoutChangingPathIndices()
    {
        var asset = Edit(false, true); int target = Package(asset, "Target");
        asset.SoftPackageReferenceList.RemoveAt(target);
        asset.SoftPackageBits = new BitArray(asset.SoftPackageBits.Cast<bool>().Where((_, i) => i != target).ToArray());
        var paths = asset.SoftObjectPathList.ToArray();
        var after = RoundTrip(UAsset.DeserializeJson(asset.SerializeJson()));
        CollectionAssert.AreEqual(paths, after.SoftObjectPathList.ToArray());
        Assert.AreEqual("/Game/Usage/Target", after.SoftPackageReferenceList.Last().Value);
        Assert.IsTrue(after.SoftPackageBits[after.SoftPackageBits.Length - 1]);
    }

    [DataTestMethod]
    [DataRow(EPropertyFlags.CPF_EditorOnly)]
    public void RuntimeFieldWithNonRuntimeDeclarationDoesNotPromote(EPropertyFlags flag)
    {
        var asset = Edit();
        foreach (var field in asset.Exports.OfType<ClassExport>().Single(e => e.ObjectName.ToString() == "BP_Usage_C").LoadedProperties)
            if (field.Name.ToString().StartsWith("Runtime", StringComparison.Ordinal)) field.PropertyFlags |= flag;
        var after = RoundTrip(UAsset.DeserializeJson(asset.SerializeJson()));
        Assert.IsFalse(after.ImportBits[Import(after, "Target")]);
        Assert.IsFalse(after.SoftPackageBits[Package(after, "Target")]);
    }

    [DataTestMethod]
    [DataRow("Untracked")]
    [DataRow("untracked")]
    public void UntrackedSoftFieldDoesNotPromoteItsPackage(string metadata)
    {
        var asset = Edit(false, true);
        var field = asset.Exports.OfType<ClassExport>().Single(e => e.ObjectName.ToString() == "BP_Usage_C").LoadedProperties.Single(p => p.Name.ToString() == "RuntimeSoft");
        field.MetaDataMap ??= new();
        field.MetaDataMap.Add(FName.FromString(asset, metadata), new FString(""));
        var after = RoundTrip(UAsset.DeserializeJson(asset.SerializeJson()));
        Assert.IsFalse(after.SoftPackageBits[Package(after, "Target")]);
    }

    [TestMethod]
    public void EditorOnlyCdoDoesNotPromoteItsDeclaredReferences()
    {
        var asset = Edit();
        var cdo = asset.Exports.Single(e => e.ObjectName.ToString() == "Default__BP_Usage_C");
        cdo.bNotForClient = cdo.bNotForServer = true;
        var after = RoundTrip(UAsset.DeserializeJson(asset.SerializeJson()));
        Assert.IsFalse(after.ImportBits[Import(after, "Target")]);
        Assert.IsFalse(after.SoftPackageBits[Package(after, "Target")]);
    }
}
