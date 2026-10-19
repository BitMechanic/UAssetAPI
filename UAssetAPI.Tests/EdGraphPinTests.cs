using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class EdGraphPinTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");
    private static UAsset ReadFixture() => new(Fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static UAsset Restore(UAsset asset) => UAsset.DeserializeJson(asset.SerializeJson());

    [TestMethod]
    public void NativeContainerFixtureExposesPinsSignaturesAndExactPayloads()
    {
        var asset = ReadFixture();
        foreach (var raw in asset.Exports.OfType<RawExport>().Where(x => x.GetExportClassType().ToString().StartsWith("K2Node_")))
        {
            using var reader = new AssetBinaryReader(File.OpenRead(Fixture), asset);
            reader.BaseStream.Position = raw.SerialOffset;
            raw.ConvertToChildExport<EdGraphNodeExport>().Read(reader, checked((int)(raw.SerialOffset + raw.SerialSize)));
        }
        Assert.AreEqual(6, asset.Exports.OfType<EdGraphNodeExport>().Count());
        Assert.AreEqual(17, asset.Exports.OfType<EdGraphNodeExport>().Sum(x => x.Pins.Length));
        var entry = asset.Exports.OfType<EdGraphNodeExport>().Single(x => x.UserDefinedPins?.Length == 5);
        CollectionAssert.AreEquivalent(new[] { EPinContainerType.Array, EPinContainerType.Set, EPinContainerType.Map, EPinContainerType.None, EPinContainerType.None }, entry.UserDefinedPins.Select(x => x.PinType.ContainerType).ToArray());
        Assert.AreEqual("real", entry.UserDefinedPins.Single(x => x.PinType.ContainerType == EPinContainerType.Map).PinType.PinValueType.TerminalCategory.ToString());
        var restored = Restore(asset);
        EdGraphNodeExport.ValidatePins(restored);
        byte[] native = File.ReadAllBytes(Fixture);
        using var output = restored.WriteData();
        foreach (var node in restored.Exports.OfType<EdGraphNodeExport>())
        {
            var original = asset.Exports[restored.Exports.IndexOf(node)];
            CollectionAssert.AreEqual(native.AsSpan((int)original.SerialOffset, (int)original.SerialSize).ToArray(), output.ToArray().AsSpan((int)node.SerialOffset, (int)node.SerialSize).ToArray(), node.ObjectName.ToString());
            Assert.AreEqual(0, node.Extras.Length);
        }
    }

    [TestMethod]
    public void JsonSignatureAndPinDefaultEditSurvivesReconstruction()
    {
        var json = JObject.Parse(ReadFixture().SerializeJson());
        var result = ((JArray)json["Exports"]).OfType<JObject>().Single(x => (string)x["ObjectName"] == "K2Node_FunctionResult_0");
        ((JArray)result["Pins"]).OfType<JObject>().Single(x => (string)x["PinName"] == "Answer")["DefaultValue"] = "true";
        ((JArray)result["UserDefinedPins"]).OfType<JObject>().Single()["PinDefaultValue"] = "true";
        var restored = UAsset.DeserializeJson(json.ToString());
        using var payload = restored.WriteData();
        var reread = new UAsset(new AssetBinaryReader(payload, restored), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        var node = reread.Exports.OfType<EdGraphNodeExport>().Single(x => x.GetExportClassType().ToString() == "K2Node_FunctionResult");
        Assert.AreEqual("true", node.Pins.Single(x => x.PinName.ToString() == "Answer").DefaultValue.Value);
        Assert.AreEqual("true", node.UserDefinedPins.Single().PinDefaultValue.Value);
    }

    [TestMethod]
    public void NullOwningArrayEntriesArePreserved()
    {
        var asset = ReadFixture();
        var node = asset.Exports.OfType<EdGraphNodeExport>().First();
        node.Pins = node.Pins.Append(null).ToArray();
        var restored = Restore(asset);
        using var bytes = restored.WriteData();
        var reread = new UAsset(new AssetBinaryReader(bytes, restored), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.IsNull(reread.Exports.OfType<EdGraphNodeExport>().First().Pins.Last());
    }

    [TestMethod]
    public void DuplicatePinIdentityAndAsymmetricLinksAreRejected()
    {
        var asset = ReadFixture();
        var node = asset.Exports.OfType<EdGraphNodeExport>().First(x => x.Pins.Length > 1);
        node.Pins = node.Pins.Append(node.Pins[0]).ToArray();
        Assert.ThrowsException<FormatException>(() => asset.WriteData());
        asset = ReadFixture();
        var pin = asset.Exports.OfType<EdGraphNodeExport>().SelectMany(x => x.Pins).First(x => x.LinkedTo.Length > 0);
        pin.LinkedTo = Array.Empty<EdGraphPinReference>();
        Assert.ThrowsException<FormatException>(() => asset.WriteData());
    }

    [TestMethod]
    public void UnsupportedArchiveAndUnknownTailCannotBeWrittenAsTypedNodes()
    {
        var asset = ReadFixture();
        asset.PackageFlags |= EPackageFlags.PKG_FilterEditorOnly;
        Assert.ThrowsException<NotSupportedException>(() => asset.Exports.OfType<EdGraphNodeExport>().First().Write(new AssetBinaryWriter(new MemoryStream(), asset)));
        asset = ReadFixture();
        asset.Exports.OfType<EdGraphNodeExport>().First().Extras = new byte[] { 1 };
        Assert.ThrowsException<NotSupportedException>(() => asset.WriteData());
    }

    [TestMethod]
    public void ComputedScriptBoundaryTracksLengthChanges()
    {
        var asset = ReadFixture();
        var node = asset.Exports.OfType<EdGraphNodeExport>().First();
        long oldEnd = node.ScriptSerializationEndOffset;
        string comment = new string('x', 400);
        ((StrPropertyData)node["NodeComment"]).Value = new FString(comment);
        using var bytes = asset.WriteData();
        Assert.IsTrue(node.ScriptSerializationEndOffset > oldEnd);
        var reread = new UAsset(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        var restored = (EdGraphNodeExport)reread.Exports[asset.Exports.IndexOf(node)];
        Assert.AreEqual(node.ScriptSerializationEndOffset, restored.ScriptSerializationEndOffset);
        Assert.AreEqual(comment, ((StrPropertyData)restored["NodeComment"]).Value.Value);
    }

    [TestMethod]
    public void ClassIdentityControlsDispatchAfterObjectRename()
    {
        var asset = ReadFixture();
        var node = asset.Exports.OfType<EdGraphNodeExport>().First();
        node.ObjectName = new FName(asset, "AnObjectWithoutANodeNamePrefix");
        using var bytes = asset.WriteData();
        var restored = new UAsset(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.IsInstanceOfType(restored.Exports.Single(x => x.ObjectName.ToString() == "AnObjectWithoutANodeNamePrefix"), typeof(EdGraphNodeExport));
    }

    [TestMethod]
    public void LegacyExtrasJsonStillReconstructsNativePins()
    {
        var asset = ReadFixture();
        var json = JObject.Parse(asset.SerializeJson());
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i] is not EdGraphNodeExport node) continue;
            using var reader = new AssetBinaryReader(File.OpenRead(Fixture), asset);
            reader.BaseStream.Position = node.SerialOffset;
            var normal = node.ConvertToChildExport<NormalExport>();
            normal.Read(reader);
            var exp = (JObject)json["Exports"][i];
            exp["$type"] = "UAssetAPI.ExportTypes.NormalExport, UAssetAPI";
            exp["Extras"] = Convert.ToBase64String(reader.ReadBytes(checked((int)(node.SerialOffset + node.SerialSize - reader.BaseStream.Position))));
            exp.Remove("Pins"); exp.Remove("UserDefinedPins"); exp.Remove("DynamicCastPureState");
        }
        var restored = UAsset.DeserializeJson(json.ToString());
        Assert.AreEqual(0, restored.Exports.OfType<EdGraphNodeExport>().Count());
        using var bytes = restored.WriteData();
        var reread = new UAsset(new AssetBinaryReader(bytes, restored), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.AreEqual(6, reread.Exports.OfType<EdGraphNodeExport>().Count());
        Assert.AreEqual(17, reread.Exports.OfType<EdGraphNodeExport>().Sum(x => x.Pins.Length));
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void MalformedNativePinCountHasExportAndByteDiagnostics(int count)
    {
        var asset = ReadFixture();
        var node = asset.Exports.OfType<EdGraphNodeExport>().First();
        byte[] native = File.ReadAllBytes(Fixture);
        using (var reader = new AssetBinaryReader(new MemoryStream(native), asset))
        {
            reader.BaseStream.Position = node.SerialOffset;
            node.ConvertToChildExport<NormalExport>().Read(reader);
            BitConverter.GetBytes(count).CopyTo(native, checked((int)reader.BaseStream.Position));
        }
        using var malformed = new AssetBinaryReader(new MemoryStream(native), asset);
        malformed.BaseStream.Position = node.SerialOffset;
        var error = Assert.ThrowsException<FormatException>(() => node.Read(malformed, checked((int)(node.SerialOffset + node.SerialSize))));
        StringAssert.Contains(error.Message, node.ObjectName.ToString());
        StringAssert.Contains(error.Message, "byte");
    }

    [TestMethod]
    public void MissingSplitParentAndInvalidDirectionsAreRejected()
    {
        var asset = ReadFixture();
        asset.Exports.OfType<EdGraphNodeExport>().First().Pins[0].ParentPin = new EdGraphPinReference { OwningNode = new FPackageIndex(1), PinId = Guid.NewGuid() };
        Assert.ThrowsException<FormatException>(() => asset.WriteData());
        asset = ReadFixture();
        asset.Exports.OfType<EdGraphNodeExport>().First().Pins[0].Direction = (EEdGraphPinDirection)255;
        Assert.ThrowsException<FormatException>(() => asset.WriteData());
    }
}
