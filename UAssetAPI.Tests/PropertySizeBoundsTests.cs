using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class PropertySizeBoundsTests
{
    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    [DataRow(9)]
    [DataRow(8)]
    public void SpeculativeUnknownPropertyReadIsBoundedByAvailableBytes(int length)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        asset.ClearNameIndexList();
        using var stream = new MemoryStream();
        using var writer = new AssetBinaryWriter(stream, asset);
        writer.Write(new FName(asset, "Test"));
        writer.Write(new FName(asset, "UnrecognizedProperty"));
        writer.Write(0); // complete type name: no parameters
        writer.Write(length);
        writer.Write((byte)0); // property tag flags
        writer.Write(new byte[8]);
        stream.Position = 0;
        using var reader = new AssetBinaryReader(stream, asset);
        if (length == 8)
        {
            var property = (UnknownPropertyData)MainSerializer.Read(reader, null, null, null, null, true);
            Assert.AreEqual(8, property.Value.Length);
            Assert.AreEqual(stream.Length, stream.Position);
        }
        else Assert.ThrowsException<FormatException>(() => MainSerializer.Read(reader, null, null, null, null, true));
    }
}
