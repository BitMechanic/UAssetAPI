using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class InstancedStructTests
{
    [DataTestMethod]
    [DataRow(-1, false)]
    [DataRow(-1, true)]
    [DataRow(0, false)]
    [DataRow(0, true)]
    public void TaggedStructTerminatesBeforeNextArrayElement(int version, bool populated)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8)
        {
            CustomVersionContainer = new List<CustomVersion> { new(nameof(FInstancedStructCustomVersion), version) },
            Imports = new List<Import>()
        };
        asset.ClearNameIndexList();
        asset.Imports.Add(new Import("/Script/CoreUObject", "ScriptStruct", new FPackageIndex(0), "TaggedFixture", false, asset));
        var value = new InstancedStructPropertyData(new FName(asset, "Values"))
        {
            Struct = new FPackageIndex(-1),
            Value = new StructPropertyData(new FName(asset, "InstancedStruct"))
        };
        if (populated) value.Value.Value.Add(new IntPropertyData(new FName(asset, "Count"))
        {
            Value = 42,
            PropertyTypeName = new FPropertyTypeName(new List<FPropertyTypeNameNode>
            {
                new() { Name = new FName(asset, "IntProperty"), InnerCount = 0 }
            }, true)
        });
        // Independent native tagged layout: type index, byte count, properties, None.
        using var expectedStream = new MemoryStream();
        using (var writer = new AssetBinaryWriter(expectedStream, asset))
        {
            if (version < 0) { writer.Write(0xABABABABu); writer.Write((byte)0); }
            writer.Write(-1);
            long sizeOffset = expectedStream.Position; writer.Write(0);
            foreach (var property in value.Value.Value) MainSerializer.Write(property, writer, true);
            writer.Write(new FName(asset, "None"));
            long end = expectedStream.Position;
            expectedStream.Position = sizeOffset; writer.Write((int)(end - sizeOffset - 4));
        }
        byte[] expected = expectedStream.ToArray();
        foreach (var candidate in new[] { value, asset.DeserializeJsonObject<InstancedStructPropertyData>(asset.SerializeJsonObject(value)) })
        {
            using var output = new MemoryStream();
            using (var writer = new AssetBinaryWriter(output, asset)) { candidate.Write(writer, false); candidate.Write(writer, false); }
            byte[] adjacent = [.. expected, .. expected];
            CollectionAssert.AreEqual(adjacent, output.ToArray(), "Each array element needs its own None terminator and inclusive byte count");
            using var input = new MemoryStream(output.ToArray());
            using var reader = new AssetBinaryReader(input, asset);
            for (int i = 0; i < 2; i++)
            {
                var restored = new InstancedStructPropertyData(value.Name);
                restored.Read(reader, false, expected.Length);
                Assert.AreEqual(populated ? 1 : 0, restored.Value.Value.Count);
                if (populated) Assert.AreEqual(42, ((IntPropertyData)restored.Value.Value[0]).Value);
                Assert.AreEqual((long)(i + 1) * expected.Length, input.Position, "Reading one element must not consume its successor");
            }
        }
    }

    [TestMethod]
    public void NullStructHasNoPropertyTerminator()
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8)
        { CustomVersionContainer = new List<CustomVersion> { new(nameof(FInstancedStructCustomVersion), 0) } };
        asset.ClearNameIndexList();
        var value = new InstancedStructPropertyData(new FName(asset, "Values")) { Struct = new FPackageIndex(0) };
        using var stream = new MemoryStream();
        using (var writer = new AssetBinaryWriter(stream, asset)) value.Write(writer, false);
        CollectionAssert.AreEqual(new byte[8], stream.ToArray());
    }
}
