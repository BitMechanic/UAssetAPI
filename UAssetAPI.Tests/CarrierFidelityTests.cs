using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

/// <summary>
/// Native UE5.8 packages must survive the JSON carrier byte for byte: native bytes,
/// SerializeJson, DeserializeJson, WriteData, compared with the original file.
/// </summary>
[TestClass]
public class CarrierFidelityTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", name);
    private static UAsset Read(string path) => new(path, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static UAsset Read(byte[] bytes, UAsset names) => new(new AssetBinaryReader(new MemoryStream(bytes), names), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);

    private static void AssertSameBytes(byte[] expected, byte[] actual, string label)
    {
        int length = Math.Min(expected.Length, actual.Length);
        int first = Enumerable.Range(0, length).FirstOrDefault(i => expected[i] != actual[i], -1);
        Assert.IsTrue(expected.Length == actual.Length && first < 0, $"{label}: lengths {expected.Length} -> {actual.Length}, first difference at byte {first}");
    }

    [DataTestMethod]
    [DataRow("Usage/BP_Usage.uasset")]
    [DataRow("BP_PinMatrix.uasset")]
    [DataRow("BP_MixedOpaque.uasset")]
    [DataRow("BP_SoftReference.uasset")]
    [DataRow("Usage/Transient/BP_Usage.uasset")]
    [DataRow("Usage/Weak/BP_Usage.uasset")]
    [DataRow("Usage/Old.uasset")]
    [DataRow("Usage/Other.uasset")]
    [DataRow("Usage/Target.uasset")]
    public void NativePackageSurvivesDirectAndJsonWrites(string fixture)
    {
        string path = Fixture(fixture);
        byte[] native = File.ReadAllBytes(path);
        var asset = Read(path);
        Assert.IsTrue(asset.VerifyBinaryEquality(), "direct write");

        string json = asset.SerializeJson();
        using var written = UAsset.DeserializeJson(json).WriteData();
        AssertSameBytes(native, written.ToArray(), "JSON write");
        using var repeated = UAsset.DeserializeJson(json).WriteData();
        AssertSameBytes(native, repeated.ToArray(), "repeated JSON write");
    }

    // The engine places an empty gatherable-text section whenever the package qualifies
    // for the gather cache (CurveFloat here), and leaves the offset 0 when it does not
    // (Blueprints with script). JSON must carry the empty section, not drop it.
    [DataTestMethod]
    [DataRow("Usage/Old.uasset", true)]
    [DataRow("Usage/Target.uasset", true)]
    [DataRow("Usage/BP_Usage.uasset", false)]
    public void EmptyGatherableTextSectionIsCarriedThroughJson(string fixture, bool hasSection)
    {
        var asset = Read(Fixture(fixture));
        Assert.AreEqual(hasSection, asset.GatherableTextData != null);
        if (hasSection) Assert.AreEqual(0, asset.GatherableTextData.Count);
        var carried = JObject.Parse(asset.SerializeJson())["GatherableTextData"];
        Assert.AreEqual(hasSection ? JTokenType.Array : JTokenType.Null, carried.Type);
    }

    // The editor checks both script offsets against the export start on load
    // (FLinkerLoad::MarkScriptSerializationStart/End), so they must count the
    // native Json prefix that UAssetImportData writes before its tagged properties.
    [TestMethod]
    public void AssetImportDataScriptOffsetsCountTheNativePrefix()
    {
        var asset = Read(Fixture("Usage/Old.uasset"));
        var import = asset.Exports.OfType<AssetImportDataExport>().Single();
        Assert.AreEqual("[]", import.Json.Value);
        Assert.AreEqual(7, import.ScriptSerializationStartOffset);
        Assert.AreEqual(16, import.ScriptSerializationEndOffset);

        import.Json = new FString("[{}]");
        var reread = Read(asset.WriteData().ToArray(), asset);
        var written = reread.Exports.OfType<AssetImportDataExport>().Single();
        Assert.AreEqual("[{}]", written.Json.Value);
        Assert.AreEqual(9, written.ScriptSerializationStartOffset);
        Assert.AreEqual(18, written.ScriptSerializationEndOffset);
    }

    // FLinkerLoad::SerializeImportTypeHierarchies: int32 count, then per entry the key
    // FPackageIndex and the hierarchy (int32 super count, four FNames per super).
    [TestMethod]
    public void ImportTypeHierarchyEntriesUseTheEngineStreamLayout()
    {
        var asset = Read(Fixture("Usage/BP_Usage.uasset"));
        Assert.IsNotNull(asset.ImportTypeHierarchies);
        Assert.AreEqual(0, asset.ImportTypeHierarchies.Count);
        foreach (string name in new[] { "Actor", "/Script/Engine", "Class", "/Script/CoreUObject" }) asset.AddNameReference(new FString(name));

        var actor = FPackageIndex.FromImport(asset.Imports.FindIndex(i => i.ObjectName.ToString() == "Actor" && i.ClassName.ToString() == "Class"));
        var generated = FPackageIndex.FromExport(asset.Exports.FindIndex(e => e.ObjectName.ToString() == "BP_Usage_C"));
        Assert.IsTrue(actor.Index < 0 && generated.Index > 0);
        var super = new FTypeResource
        {
            TypeName = FName.FromString(asset, "Actor"),
            PackageName = FName.FromString(asset, "/Script/Engine"),
            ClassName = FName.FromString(asset, "Class"),
            ClassPackageName = FName.FromString(asset, "/Script/CoreUObject"),
        };
        // SavePackage sorts entries by FPackageIndex before writing.
        asset.ImportTypeHierarchies.Add(actor, Hierarchy(asset));
        asset.ImportTypeHierarchies.Add(generated, Hierarchy(asset, super));

        byte[] bytes = asset.WriteData().ToArray();
        var reread = Read(bytes, asset);
        Assert.AreEqual(2, reread.ImportTypeHierarchiesCount);
        int offset = reread.ImportTypeHierarchiesOffset;
        int[] expected =
        {
            2,
            actor.Index, 0,
            generated.Index, 1,
            reread.SearchNameReference(new FString("Actor")), 0,
            reread.SearchNameReference(new FString("/Script/Engine")), 0,
            reread.SearchNameReference(new FString("Class")), 0,
            reread.SearchNameReference(new FString("/Script/CoreUObject")), 0,
        };
        CollectionAssert.AreEqual(expected, Enumerable.Range(0, expected.Length).Select(i => BitConverter.ToInt32(bytes, offset + 4 * i)).ToArray());

        CollectionAssert.AreEqual(new[] { actor.Index, generated.Index }, reread.ImportTypeHierarchies.Keys.Select(k => k.Index).ToArray());
        Assert.AreEqual(0, reread.ImportTypeHierarchies[actor].SuperTypes.Length);
        var readSuper = reread.ImportTypeHierarchies[generated].SuperTypes.Single();
        CollectionAssert.AreEqual(new[] { "Actor", "/Script/Engine", "Class", "/Script/CoreUObject" },
            new[] { readSuper.TypeName, readSuper.PackageName, readSuper.ClassName, readSuper.ClassPackageName }.Select(n => n.ToString()).ToArray());

        AssertSameBytes(bytes, reread.WriteData().ToArray(), "rewrite");
        AssertSameBytes(bytes, UAsset.DeserializeJson(reread.SerializeJson()).WriteData().ToArray(), "JSON write");

        // The loader fails a package whose stream count differs from the summary count.
        byte[] mismatched = (byte[])bytes.Clone();
        BitConverter.GetBytes(3).CopyTo(mismatched, offset);
        Assert.ThrowsException<FormatException>(() => Read(mismatched, asset));
    }

    private static FImportTypeHierarchy Hierarchy(UAsset asset, params FTypeResource[] supers)
    {
        using var buffer = new MemoryStream();
        var writer = new AssetBinaryWriter(buffer, asset);
        writer.Write(supers.Length);
        foreach (var super in supers) super.Write(writer);
        buffer.Position = 0;
        return new FImportTypeHierarchy(new AssetBinaryReader(buffer, asset));
    }
}
