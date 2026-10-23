using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class NiagaraVariableJsonTests
{
    private static byte[] Write(UAsset asset, NiagaraVariableBasePropertyData value)
    {
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, asset);
        value.Write(writer, false);
        return stream.ToArray();
    }

    [DataTestMethod]
    [DataRow("Base")]
    [DataRow("Variable")]
    [DataRow("Offset")]
    [DataRow("DataChannel")]
    public void JsonPreservesNativeVariablePayloadAndAllowsNameEdits(string kind)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");
        var asset = new UAsset(fixture, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        NiagaraVariableBasePropertyData value = kind switch
        {
            "Variable" => new NiagaraVariablePropertyData { VarData = new byte[] { 0, 1, 127, 128, 255 } },
            "Offset" => new NiagaraVariableWithOffsetPropertyData { VariableOffset = 1234 },
            "DataChannel" => new NiagaraDataChannelVariablePropertyData(),
            _ => new NiagaraVariableBasePropertyData()
        };
        value.Name = new FName(asset, "Variable");
        value.VariableName = new FName(asset, "Particles.CustomValue");
        value.TypeDef = new StructPropertyData(new FName(asset, "TypeDef"), new FName(asset, "NiagaraTypeDefinition"))
        {
            Value = new List<PropertyData> { new IntPropertyData(new FName(asset, "Flags"))
            {
                Value = 7,
                PropertyTypeName = new FPropertyTypeName(new List<FPropertyTypeNameNode>
                {
                    new() { Name = new FName(asset, "IntProperty"), InnerCount = 0 }
                }, true)
            } }
        };
        byte[] expected = Write(asset, value);
        string json = asset.SerializeJsonObject(value);
        var restored = asset.DeserializeJsonObject<NiagaraVariableBasePropertyData>(json);
        Assert.AreEqual(value.GetType(), restored.GetType());
        CollectionAssert.AreEqual(expected, Write(asset, restored), "JSON must retain the complete native variable payload");

        var edited = JObject.Parse(json);
        edited["VariableName"] = "Particles.ReconnectedValue";
        // Object-only JSON reads resolve names against this asset's existing name map.
        value.VariableName = new FName(asset, "Particles.ReconnectedValue");
        var changed = asset.DeserializeJsonObject<NiagaraVariableBasePropertyData>(edited.ToString());
        Assert.AreEqual("Particles.ReconnectedValue", changed.VariableName.ToString());
        CollectionAssert.AreEqual(Write(asset, value), Write(asset, changed), "A name edit must preserve type and payload bytes");
    }
}
