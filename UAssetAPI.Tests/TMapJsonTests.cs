using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class TMapJsonTests
{
    [TestMethod]
    public void NestedPairsPreserveOrderValuesAndNullThroughExistingJsonFormat()
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        asset.ClearNameIndexList();
        var name = new FName(asset, "TestMap");
        var inner = new MapPropertyData(name) { Value = new TMap<PropertyData,PropertyData>() };
        inner.Value.Add(new IntPropertyData(name) { Value=1 }, new IntPropertyData(name) { Value=3 });
        var original = new MapPropertyData(name) { Value = new TMap<PropertyData,PropertyData>() };
        original.Value.Add(new IntPropertyData(name) { Value=1 }, inner);
        original.Value.Add(new IntPropertyData(name) { Value=2 }, new MapPropertyData(name) { Value = new TMap<PropertyData,PropertyData>() });
        original.Value.Add(new IntPropertyData(name) { Value=3 }, null);
        string json = asset.SerializeJsonObject(original);
        var restored = asset.DeserializeJsonObject<MapPropertyData>(json);
        Assert.AreEqual(3, restored.Value.Count);
        Assert.AreEqual(3, ((IntPropertyData)((MapPropertyData)restored.Value[0]).Value[0]).Value);
        Assert.AreEqual(0, ((MapPropertyData)restored.Value[1]).Value.Count);
        Assert.IsNull(restored.Value[2]);
        Assert.AreEqual(json, asset.SerializeJsonObject(restored));
        Assert.IsNull(asset.DeserializeJsonObject<MapPropertyData>("{\"Value\":null}").Value);
    }

    [DataTestMethod]
    [DataRow("{}")]
    [DataRow("[[]]")]
    [DataRow("[[null]]")]
    [DataRow("[[null,null,null]]")]
    [DataRow("[[null,null]")]
    public void InvalidPairsAreRejected(string json)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        string property = "{\"Value\":" + json + "}";
        bool refused=false;
        try { asset.DeserializeJsonObject<MapPropertyData>(property); }
        catch (JsonException) { refused=true; }
        Assert.IsTrue(refused, "Malformed native map pairs must be rejected");
    }
}
