using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using EFormatArgumentType = UAssetAPI.UnrealTypes.EngineEnums.EFormatArgumentType;

namespace UAssetAPI.Tests;

[TestClass]
public class EdGraphPinRegressionTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");
    private static UAsset Read() => new(Fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);

    private static void AssertDestinationPreserved(UAsset asset)
    {
        string directory = Path.Combine(Path.GetTempPath(), "UAssetAPI-PinRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "existing.uasset");
        byte[] sentinel = { 1, 3, 5, 7 };
        File.WriteAllBytes(destination, sentinel);
        try
        {
            Assert.ThrowsException<FormatException>(() => asset.Write(destination));
            CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(destination));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }
        finally { File.Delete(destination); Directory.Delete(directory); }
    }

    [TestMethod]
    public void NativeUnsupportedMacroPreservesPayloadAndValidatesReverseLinks()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_MixedOpaque.uasset");
        var original = new UAsset(path, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        var asset = UAsset.DeserializeJson(original.SerializeJson());
        var macro = asset.Exports.Single(e => e.GetExportClassType().ToString() == "K2Node_MacroInstance");
        Assert.AreEqual(typeof(NormalExport), macro.GetType());
        Assert.IsTrue(macro.Extras.Length > 0);
        byte[] before = File.ReadAllBytes(path);
        using var written = asset.WriteData();
        byte[] after = written.ToArray();
        for (int i = 0; i < asset.Exports.Count; i++)
            CollectionAssert.AreEqual(before.AsSpan((int)original.Exports[i].SerialOffset, (int)original.Exports[i].SerialSize).ToArray(), after.AsSpan((int)asset.Exports[i].SerialOffset, (int)asset.Exports[i].SerialSize).ToArray());
        Assert.AreEqual(typeof(NormalExport), macro.GetType());
        var link = asset.Exports.OfType<EdGraphNodeExport>().SelectMany(n => n.Pins).Single(p => p.LinkedTo.Any(l => l.OwningNode.Index == asset.Exports.IndexOf(macro) + 1));
        link.LinkedTo = Array.Empty<EdGraphPinReference>();
        AssertDestinationPreserved(asset);
    }

    private static UAsset Mixed(bool raw)
    {
        var asset = Read();
        var node = asset.Exports.OfType<EdGraphNodeExport>().First(n => n.Pins.Any(p => p.LinkedTo.Length > 0));
        int index = asset.Exports.IndexOf(node);
        using var reader = new AssetBinaryReader(File.OpenRead(Fixture), asset);
        reader.BaseStream.Position = node.SerialOffset;
        if (raw)
        {
            var opaque = node.ConvertToChildExport<RawExport>();
            opaque.Data = reader.ReadBytes(checked((int)node.SerialSize));
            asset.Exports[index] = opaque;
        }
        else
        {
            var opaque = node.ConvertToChildExport<NormalExport>();
            opaque.Read(reader);
            opaque.Extras = reader.ReadBytes(checked((int)(node.SerialOffset + node.SerialSize - reader.BaseStream.Position)));
            asset.Exports[index] = opaque;
        }
        return UAsset.DeserializeJson(asset.SerializeJson());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MixedOpaqueNodesRoundTripWithoutChangingTheirPayload(bool raw)
    {
        var asset = Mixed(raw);
        var before = asset.Exports.Select(e => (e.SerialOffset, e.SerialSize)).ToArray();
        byte[] original = File.ReadAllBytes(Fixture);
        using var bytes = asset.WriteData();
        for (int i = 0; i < asset.Exports.Count; i++)
            CollectionAssert.AreEqual(original.AsSpan((int)before[i].SerialOffset, (int)before[i].SerialSize).ToArray(), bytes.ToArray().AsSpan((int)asset.Exports[i].SerialOffset, (int)asset.Exports[i].SerialSize).ToArray());
        Assert.AreEqual(5, asset.Exports.OfType<EdGraphNodeExport>().Count(), "Validation must not promote opaque exports");
        var reread = new UAsset(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.AreEqual(6, reread.Exports.OfType<EdGraphNodeExport>().Count());
        Assert.AreEqual(17, reread.Exports.OfType<EdGraphNodeExport>().Sum(n => n.Pins.Length));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MixedOpaqueNodesStillRejectDanglingAndAsymmetricLinks(bool raw)
    {
        var asset = Mixed(raw);
        var pin = asset.Exports.OfType<EdGraphNodeExport>().SelectMany(n => n.Pins).First(p => p.LinkedTo.Any(l => asset.Exports[l.OwningNode.Index - 1] is not EdGraphNodeExport));
        pin.LinkedTo[0].PinId = Guid.NewGuid();
        AssertDestinationPreserved(asset);
        asset = Mixed(raw);
        pin = asset.Exports.OfType<EdGraphNodeExport>().SelectMany(n => n.Pins).First(p => p.LinkedTo.Any(l => asset.Exports[l.OwningNode.Index - 1] is not EdGraphNodeExport));
        pin.LinkedTo = Array.Empty<EdGraphPinReference>();
        AssertDestinationPreserved(asset);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MalformedOpaquePinsRefuseBeforeReplacingDestination(bool raw)
    {
        var asset = Mixed(raw);
        var opaque = asset.Exports.Single(e => e.GetExportClassType().ToString() == "K2Node_FunctionEntry" && e is not EdGraphNodeExport);
        if (opaque is RawExport rawExport) rawExport.Data = new byte[1];
        else ((NormalExport)opaque).Extras = new byte[1];
        AssertDestinationPreserved(asset);
    }

    [TestMethod]
    public void PinTypeCloneOwnsNestedNamesAndObjectReferences()
    {
        var asset = Read();
        var type = asset.Exports.OfType<EdGraphNodeExport>().SelectMany(n => n.Pins).First(p => p.PinType.ContainerType == EPinContainerType.Map).PinType;
        var source = new EdGraphPinTypePropertyData(new FName(asset, "PinType")) { Value = type };
        string before = asset.SerializeJsonObject(source);
        var copy = (EdGraphPinTypePropertyData)source.Clone();
        Assert.AreNotSame(source.Value, copy.Value);
        Assert.AreNotSame(source.Value.PinCategory, copy.Value.PinCategory);
        Assert.AreNotSame(source.Value.PinSubCategory, copy.Value.PinSubCategory);
        Assert.AreNotSame(source.Value.MemberName, copy.Value.MemberName);
        Assert.AreNotSame(source.Value.PinValueType, copy.Value.PinValueType);
        Assert.AreNotSame(source.Value.PinValueType.TerminalCategory, copy.Value.PinValueType.TerminalCategory);
        Assert.AreNotSame(source.Value.PinValueType.TerminalSubCategory, copy.Value.PinValueType.TerminalSubCategory);
        copy.Value.PinSubCategoryObject.Index = 1;
        copy.Value.MemberParent.Index = 1;
        copy.Value.PinValueType.TerminalSubCategoryObject.Index = 1;
        copy.Value.PinValueType.TerminalCategory = new FName(asset, "int");
        Assert.AreEqual(before, asset.SerializeJsonObject(source));
        Assert.AreSame(asset, copy.Value.PinCategory.Asset);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        source.Write(new AssetBinaryWriter(first, asset), false);
        copy.Write(new AssetBinaryWriter(second, asset), false);
        Assert.IsFalse(first.ToArray().SequenceEqual(second.ToArray()));
    }

    [TestMethod]
    public void NamedFormatCloneOwnsRecursiveArgumentsAndUnnamedText()
    {
        var asset = Read();
        TextPropertyData Literal(string value) => new() { HistoryType = TextHistoryType.None, CultureInvariantString = new FString(value) };
        var source = new TextPropertyData(new FName(asset, "Text"))
        {
            HistoryType = TextHistoryType.NamedFormat,
            SourceFmt = Literal("{n}/{text}"),
            NamedArguments = new[]
            {
                new FFormatArgumentData(new FString("n"), new FFormatArgumentValue(EFormatArgumentType.Int, 123L)),
                new FFormatArgumentData(new FString("text"), new FFormatArgumentValue(EFormatArgumentType.Text, new TextPropertyData
                {
                    HistoryType = TextHistoryType.OrderedFormat, SourceFmt = Literal("{0}"),
                    Arguments = new[] { new FFormatArgumentValue(EFormatArgumentType.Text, Literal("original")) }
                }))
            }
        };
        byte[] Payload(TextPropertyData text)
        {
            using var stream = new MemoryStream();
            text.Write(new AssetBinaryWriter(stream, asset), false);
            return stream.ToArray();
        }
        byte[] before = Payload(source);
        var copy = (TextPropertyData)source.Clone();
        CollectionAssert.AreEqual(before, Payload(copy));
        copy.NamedArguments[0].ArgumentName.Value = "other";
        copy.NamedArguments[0].ArgumentValue.Value = 456L;
        copy.SourceFmt.CultureInvariantString.Value = "changed";
        var nested = (TextPropertyData)copy.NamedArguments[1].ArgumentValue.Value;
        ((TextPropertyData)nested.Arguments[0].Value).CultureInvariantString.Value = "clone only";
        CollectionAssert.AreEqual(before, Payload(source));
        Assert.IsFalse(before.SequenceEqual(Payload(copy)));
    }
}
