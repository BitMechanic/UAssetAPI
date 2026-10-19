using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.FieldTypes;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class PackageReferenceWriteTests
{
    private static UAsset Read() => new(Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_SoftReference.uasset"), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);

    public static IEnumerable<object[]> InvalidReferences()
    {
        string[] fields = { "ClassIndex", "ClassImportOuter", "SuperIndex", "TemplateIndex", "OuterIndex", "SuperStruct", "Children", "FuncMap", "ClassWithin", "ClassGeneratedBy", "ClassDefaultObject", "Interfaces", "PropertyClass", "DependsMap", "SearchableNames", "SerializationBeforeSerializationDependencies", "CreateBeforeSerializationDependencies", "SerializationBeforeCreateDependencies", "CreateBeforeCreateDependencies" };
        foreach (string field in fields)
            foreach (int index in new[] { int.MinValue, int.MaxValue })
                yield return new object[] { field, index };
    }

    [DataTestMethod]
    [DynamicData(nameof(InvalidReferences), DynamicDataSourceType.Method)]
    public void JsonReferenceOutsideTablesPreservesDestination(string field, int index)
    {
        var asset = Read();
        var generated = asset.Exports.OfType<ClassExport>().Single();
        var reference = new FPackageIndex(index);
        switch (field)
        {
            case "ClassIndex": generated.ClassIndex = reference; break;
            case "ClassImportOuter": generated.ClassIndex.ToImport(asset).OuterIndex = reference; break;
            case "SuperIndex": generated.SuperIndex = reference; break;
            case "TemplateIndex": generated.TemplateIndex = reference; break;
            case "OuterIndex": generated.OuterIndex = reference; break;
            case "SuperStruct": generated.SuperStruct = reference; break;
            case "Children": generated.Children = new[] { reference }; break;
            case "FuncMap": generated.FuncMap.Add(FName.FromString(asset, "InvalidFunction"), reference); break;
            case "ClassWithin": generated.ClassWithin = reference; break;
            case "ClassGeneratedBy": generated.ClassGeneratedBy = reference; break;
            case "ClassDefaultObject": generated.ClassDefaultObject = reference; break;
            case "Interfaces": generated.Interfaces = new[] { new SerializedInterfaceReference(index, 0, true) }; break;
            case "PropertyClass": generated.LoadedProperties.OfType<FObjectProperty>().First().PropertyClass = reference; break;
            case "DependsMap": asset.DependsMap[0] = new[] { index }; break;
            case "SearchableNames": asset.SearchableNames = new SortedDictionary<FPackageIndex, List<FName>> { [reference] = new() }; break;
            default:
                asset.UsesEventDrivenLoader = true;
                ((List<FPackageIndex>)typeof(Export).GetField(field).GetValue(generated)).Add(reference);
                break;
        }
        string json = asset.SerializeJson();
        AssertDestinationPreserved(() => UAsset.DeserializeJson(json));
    }

    private static void AssertDestinationPreserved(Func<UAsset> restore)
    {
        string directory = Path.Combine(Path.GetTempPath(), "UAssetAPI-ReferenceWrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "existing.uasset");
        byte[] sentinel = { 3, 1, 4, 1, 5 };
        File.WriteAllBytes(destination, sentinel);
        try
        {
            Assert.ThrowsException<FormatException>(() => restore().Write(destination));
            CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(destination));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [DataTestMethod]
    [DataRow("FEnumProperty", "Enum")]
    [DataRow("FClassProperty", "MetaClass")]
    [DataRow("FSoftClassProperty", "MetaClass")]
    [DataRow("FDelegateProperty", "SignatureFunction")]
    [DataRow("FInterfaceProperty", "InterfaceClass")]
    [DataRow("FByteProperty", "Enum")]
    [DataRow("FStructProperty", "Struct")]
    public void JsonReflectedPropertyReferenceIsChecked(string type, string field)
    {
        // Reuse a native FProperty prefix; these negative declarations never
        // become native fixtures or a claim of class/signature compatibility.
        foreach (int index in new[] { int.MinValue, int.MaxValue })
        {
            var json = JObject.Parse(Read().SerializeJson());
            var generated = json["Exports"].OfType<JObject>().Single(e => ((string)e["$type"]).Contains(".ClassExport,"));
            var property = generated["LoadedProperties"].OfType<JObject>().First(p => ((string)p["$type"]).Contains(".FObjectProperty,"));
            property["$type"] = "UAssetAPI.FieldTypes." + type + ", UAssetAPI";
            property[field] = index;
            if (type == "FEnumProperty") property["UnderlyingProp"] = null;
            AssertDestinationPreserved(() => UAsset.DeserializeJson(json.ToString()));
        }
    }

    [DataTestMethod]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void BytecodeFieldPathOwnerIsChecked(int index)
    {
        var asset = Read();
        var path = new FFieldPath(new[] { FName.FromString(asset, "ExternalAsset") }, new FPackageIndex(index));
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, asset);
        Assert.ThrowsException<FormatException>(() => path.Write(writer));
    }

    [TestMethod]
    public void SearchableNameKeysSurviveJsonAndNativeWritingInAnotherCulture()
    {
        var asset = Read();
        var previous = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = culture;
            asset.SearchableNames = new SortedDictionary<FPackageIndex, List<FName>>
            {
                [FPackageIndex.FromImport(asset.Imports.Count - 1)] = new() { FName.FromString(asset, "ExternalAsset") },
                [FPackageIndex.FromExport(asset.Exports.Count - 1)] = new() { FName.FromString(asset, "SoftAsset") }
            };
            string json = asset.SerializeJson();
            var restored = UAsset.DeserializeJson(json);
            CollectionAssert.AreEqual(asset.SearchableNames.Keys.Select(k => k.Index).ToArray(), restored.SearchableNames.Keys.Select(k => k.Index).ToArray());
            using var bytes = restored.WriteData();
            var reread = new UAsset(new AssetBinaryReader(bytes, restored), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
            CollectionAssert.AreEqual(asset.SearchableNames.Keys.Select(k => k.Index).ToArray(), reread.SearchableNames.Keys.Select(k => k.Index).ToArray());
            CollectionAssert.AreEqual(new[] { "ExternalAsset", "SoftAsset" }, reread.SearchableNames.Values.SelectMany(v => v).Select(n => n.ToString()).ToArray());
            using var repeat = UAsset.DeserializeJson(restored.SerializeJson()).WriteData();
            CollectionAssert.AreEqual(bytes.ToArray(), repeat.ToArray());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [DataTestMethod]
    [DataRow("1.5")]
    [DataRow("2147483648")]
    [DataRow("-2147483649")]
    [DataRow("1e3")]
    [DataRow("NaN")]
    public void SearchableNameKeysRequireExactInt32(string key)
    {
        var json = JObject.Parse(Read().SerializeJson());
        json["SearchableNames"] = new JObject { [key] = new JArray() };
        Assert.ThrowsException<JsonSerializationException>(() => UAsset.DeserializeJson(json.ToString()));
    }

    [TestMethod]
    public void ValidReferenceBoundariesAndNullExportHeadersKeepTheirEncoding()
    {
        var asset = Read();
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, asset);
        foreach (int index in new[] { -asset.Imports.Count, -1, 0, 1, asset.Exports.Count })
            new FPackageIndex(index).Write(writer);
        CollectionAssert.AreEqual(new[] { -asset.Imports.Count, -1, 0, 1, asset.Exports.Count }.SelectMany(BitConverter.GetBytes).ToArray(), stream.ToArray());
        Assert.ThrowsException<FormatException>(() => new FPackageIndex(-asset.Imports.Count - 1).Write(writer));
        Assert.ThrowsException<FormatException>(() => new FPackageIndex(asset.Exports.Count + 1).Write(writer));
        var export = new Export();
        using var header = new MemoryStream();
        export.WriteExportMapEntry(new AssetBinaryWriter(header, asset));
        CollectionAssert.AreEqual(new byte[16], header.ToArray().Take(16).ToArray());
        Assert.AreEqual(Export.GetExportMapEntrySize(asset), header.Length);
    }
}
