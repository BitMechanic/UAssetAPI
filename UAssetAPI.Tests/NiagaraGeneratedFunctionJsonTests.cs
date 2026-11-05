using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.IO;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class NiagaraGeneratedFunctionJsonTests
{
    [TestMethod]
    public void SpecifierPairsSurviveJsonAndNativeWrite()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");
        var asset = new UAsset(fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        var original = new FNiagaraDataInterfaceGeneratedFunction
        {
            DefinitionName = new FName(asset, "ReadGrid"), InstanceName = new FString("ReadGrid_0"),
            Specifiers = new[] { (new FName(asset, "Attribute"), new FName(asset, "Velocity")),
                                 (new FName(asset, "Type"), new FName(asset, "Vector")) }
        };
        byte[] Write(FNiagaraDataInterfaceGeneratedFunction value)
        {
            using var stream = new MemoryStream();
            using var writer = new AssetBinaryWriter(stream, asset);
            value.Write(writer);
            return stream.ToArray();
        }
        var restored = asset.DeserializeJsonObject<FNiagaraDataInterfaceGeneratedFunction>(asset.SerializeJsonObject(original));
        Assert.AreEqual("Velocity", restored.Specifiers[0].Item2.ToString());
        CollectionAssert.AreEqual(Write(original), Write(restored));
    }

    [TestMethod]
    public void ValueTupleDoesNotAdmitUnrelatedArgumentTypes()
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        const string json = "{\"$type\":\"System.ValueTuple`2[[System.IO.FileInfo, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]], System.Private.CoreLib\"}";
        Assert.ThrowsException<JsonSerializationException>(() => asset.DeserializeJsonObject<object>(json));
    }
}
