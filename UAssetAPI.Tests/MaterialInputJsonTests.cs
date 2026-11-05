using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.Tests;

[TestClass]
public class MaterialInputJsonTests
{
    private static byte[] Write<T>(UAsset asset, MaterialInputPropertyData<T> value)
    {
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, asset);
        value.Write(writer, false);
        return stream.ToArray();
    }

    private static void AssertRoundTrip<T>(MaterialInputPropertyData<T> value)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");
        var asset = new UAsset(fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        value.Name = new FName(asset, "Input");
        value.Expression = new FPackageIndex(3);
        value.OutputIndex = 2;
        value.InputName = new FName(asset, "ConnectedInput");
        value.InputNameOld = new FString("LegacyInput");
        value.ExpressionName = new FName(asset, "LegacyExpression");
        value.Mask = 1; value.MaskR = 1; value.MaskG = 0; value.MaskB = 1; value.MaskA = 1;
        var restored = asset.DeserializeJsonObject<MaterialInputPropertyData<T>>(asset.SerializeJsonObject(value));
        Assert.AreEqual(value.GetType(), restored.GetType());
        CollectionAssert.AreEqual(Write(asset, value), Write(asset, restored), "JSON must preserve the native connection, output, input name, masks and constant value");
        Assert.AreEqual(value.InputNameOld.ToString(), restored.InputNameOld?.ToString(), "Retain the version-dependent legacy field");
        Assert.AreEqual(value.ExpressionName.ToString(), restored.ExpressionName?.ToString(), "Retain the cooked legacy expression name");
    }

    [TestMethod]
    public void JsonPreservesExpressionConnection() => AssertRoundTrip(new ExpressionInputPropertyData());

    [TestMethod]
    public void JsonPreservesScalarRootConnectionAndConstant() => AssertRoundTrip(new ScalarMaterialInputPropertyData { Value = 0.375f });

    // Construct the engine's wire layout independently of the input property's writer.
    [DataTestMethod]
    [DataRow("color-old", false)]
    [DataRow("color-old", true)]
    [DataRow("color", false)]
    [DataRow("color", true)]
    [DataRow("vector", false)]
    [DataRow("vector", true)]
    [DataRow("vector2", true)]
    [DataRow("scalar", true)]
    public void NativeMaterialConstantLayoutIsPreserved(string kind, bool useConstant)
    {
        var asset = new UAsset(kind == "color-old" ? EngineVersion.VER_UE5_5 : EngineVersion.VER_UE5_8);
        asset.ClearNameIndexList();
        var name = new FName(asset, "Input");
        var inputName = new FName(asset, "ConnectedInput");
        PropertyData input = kind switch
        {
            "color-old" or "color" => new ColorMaterialInputPropertyData(name),
            "vector" => new VectorMaterialInputPropertyData(name),
            "vector2" => new Vector2MaterialInputPropertyData(name),
            _ => new ScalarMaterialInputPropertyData(name)
        };
        using var native = new MemoryStream();
        using var writer = new AssetBinaryWriter(native, asset);
        writer.Write(3); // expression
        writer.Write(2); // output index
        writer.Write(inputName);
        foreach (int mask in new[] { 1, 1, 0, 1, 1 }) writer.Write(mask);
        writer.Write(useConstant ? 1 : 0);
        if (kind == "color-old") writer.Write(new byte[] { 10, 20, 30, 40 });
        else
        {
            int components = kind == "color" ? 4 : kind == "vector" ? 3 : kind == "vector2" ? 2 : 1;
            for (int i = 0; i < components; i++) writer.Write(1.0f + i * 0.125f);
        }
        byte[] expected = native.ToArray();
        native.Position = 0;
        using var reader = new AssetBinaryReader(native, asset);
        input.Read(reader, false, expected.Length);
        Assert.AreEqual(native.Length, native.Position, "The next property must begin at the native boundary");
        if (kind == "color") Assert.IsInstanceOfType(((ColorMaterialInputPropertyData)input).Value, typeof(LinearColorPropertyData));
        if (kind == "color-old") Assert.IsInstanceOfType(((ColorMaterialInputPropertyData)input).Value, typeof(ColorPropertyData));
        var restored = asset.DeserializeJsonObject<PropertyData>(asset.SerializeJsonObject(input));
        using var roundtrip = new MemoryStream();
        using var rebuilt = new AssetBinaryWriter(roundtrip, asset);
        Assert.AreEqual(expected.Length, restored.Write(rebuilt, false));
        CollectionAssert.AreEqual(expected, roundtrip.ToArray());
    }
}
