using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

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
}
