using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.CustomVersions;
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
    [DataRow("BlueprintFamilies/BP_ComponentFamily.uasset")]
    [DataRow("BlueprintFamilies/BP_ObjectFamily.uasset")]
    [DataRow("BlueprintFamilies/BP_DataOnlyFamily.uasset")]
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

    // Native data-only Blueprint: populated gather cache, with the UE5.8 DevNotes
    // FString between IsOptional and InfoMetaData. Previously both writes lost 4 bytes.
    [TestMethod]
    public void NativeGatherableTextLegacyJsonWithoutNotesPreservesEmptyString()
    {
        string path = Fixture("BlueprintFamilies/BP_DataOnlyFamily.uasset");
        var asset = Read(path);
        var context = asset.GatherableTextData.Single().SourceSiteContexts.Single();
        Assert.AreEqual("Default", context.KeyName.Value);
        Assert.IsNull(context.DevNotes); // Native zero-length FString representation.
        var json = JObject.Parse(asset.SerializeJson());
        Assert.IsTrue(((JObject)json["GatherableTextData"][0]["SourceSiteContexts"][0]).Remove("DevNotes"));
        AssertSameBytes(File.ReadAllBytes(path), UAsset.DeserializeJson(json.ToString()).WriteData().ToArray(), "legacy JSON");
    }

    [DataTestMethod]
    [DataRow(259, false)]
    [DataRow(260, false)]
    [DataRow(268, false)]
    [DataRow(268, true)]
    public void GatherableTextDeveloperNotesFollowNativeVersionedLayout(int version, bool filtered)
    {
        var asset = Read(Fixture("BlueprintFamilies/BP_DataOnlyFamily.uasset"));
        asset.CustomVersionContainer.Single(v => v.Key == CustomVersion.GetCustomVersionGuidFromFriendlyName(nameof(FFortniteMainBranchObjectVersion))).Version = version;
        if (filtered)
        {
            asset.PackageFlags |= EPackageFlags.PKG_FilterEditorOnly;
            // Only exercise the gather-cache reader: editor graph exports are
            // deliberately unsupported in a filtered archive.
            asset.Exports.Clear();
            typeof(UAsset).GetField("ExportCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(asset, 0);
        }
        var text = asset.GatherableTextData.Single();
        var context = text.SourceSiteContexts.Single();
        context.DevNotes = new FString("Translator context: caf\u00e9 \u03a9", System.Text.Encoding.Unicode);
        text.SourceSiteContexts[0] = context;
        var following = context;
        following.KeyName = new FString("Following context");
        following.DevNotes = null;
        text.SourceSiteContexts.Add(following);

        // Independent transcription of GatherableTextData.cpp, not the production writer.
        using var expected = new MemoryStream();
        using (var writer = new AssetBinaryWriter(expected, System.Text.Encoding.UTF8, true, asset))
        {
            writer.Write(text.NamespaceName);
            writer.Write(text.SourceData.SourceString);
            writer.Write(0); // SourceStringMetaData map.
            writer.Write(2);
            foreach (var c in text.SourceSiteContexts)
            {
                writer.Write(c.KeyName);
                writer.Write(c.SiteDescription);
                writer.WriteBooleanInt(c.IsEditorOnly);
                writer.WriteBooleanInt(c.IsOptional);
                if (version >= 260 && !filtered) writer.Write(c.DevNotes);
                writer.Write(0); // InfoMetaData map.
                writer.Write(0); // KeyMetaData map.
            }
        }
        byte[] bytes = asset.WriteData().ToArray();
        if (filtered)
        {
            // WriteData omits the entire gather cache for filtered packages, so
            // simply flipping the flag cannot exercise the reader's field gate.
            // Append our independently encoded section and point the summary at
            // it. This is a synthetic reader control, not a native cooked fixture.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(UAsset).GetField("GatherableTextDataOffset", flags).SetValue(asset, bytes.Length);
            typeof(UAsset).GetField("GatherableTextDataCount", flags).SetValue(asset, 1);
            bytes = bytes.Concat(expected.ToArray()).ToArray();
            ((byte[])typeof(UAsset).GetMethod("MakeHeader", flags).Invoke(asset, null)).CopyTo(bytes, 0);
        }
        var reread = Read(bytes, asset);
        Assert.AreEqual(filtered, reread.IsFilterEditorOnly);
        int offset = (int)typeof(UAsset).GetField("GatherableTextDataOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(reread);
        AssertSameBytes(expected.ToArray(), bytes.Skip(offset).Take((int)expected.Length).ToArray(), "native gather layout");
        var contexts = reread.GatherableTextData.Single().SourceSiteContexts;
        Assert.AreEqual(2, contexts.Count);
        Assert.AreEqual(version >= 260 && !filtered ? context.DevNotes.Value : null, contexts[0].DevNotes?.Value);
        Assert.AreEqual("Following context", contexts[1].KeyName.Value);
        Assert.IsNull(contexts[1].DevNotes);
        if (!filtered) AssertSameBytes(bytes, UAsset.DeserializeJson(reread.SerializeJson()).WriteData().ToArray(), "versioned JSON");
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
