using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class FStringFidelityTests
{
    [DataTestMethod]
    [DataRow("")]
    [DataRow("ASCII kept wide")]
    [DataRow("café")]
    public void JsonPreservesNativeStringEncoding(string value)
    {
        var original = new FString(value, Encoding.Unicode);
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        var restored = asset.DeserializeJsonObject<FString>(asset.SerializeJsonObject(original));
        CollectionAssert.AreEqual(Payload(original), Payload(restored));
    }

    [TestMethod]
    public void LegacyJsonStringsAndZeroLengthRemainReadable()
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        Assert.AreEqual("plain", asset.DeserializeJsonObject<FString>("\"plain\"").Value);
        Assert.IsNull(asset.DeserializeJsonObject<FString>("null"));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, Payload(null));
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 0 }, Payload(new FString("")));
    }

    [DataTestMethod]
    [DataRow(1, new byte[] { 65 })]
    [DataRow(-1, new byte[] { 65, 0 })]
    public void UnterminatedStringsAreRejected(int count, byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(count);
            writer.Write(bytes);
        }
        stream.Position = 0;
        using var reader = new UnrealBinaryReader(stream);
        Assert.ThrowsException<FormatException>(() => reader.ReadFString());
    }

    [DataTestMethod]
    [DataRow(int.MinValue)]
    [DataRow(-int.MaxValue)]
    [DataRow(int.MaxValue)]
    public void OversizedStringLengthsAreRejectedBeforeAllocation(int count)
    {
        using var reader = new UnrealBinaryReader(new MemoryStream(BitConverter.GetBytes(count)));
        Assert.ThrowsException<FormatException>(() => reader.ReadFString());
    }

    [TestMethod]
    public void TruncatedWideStringIsRejected()
    {
        using var reader = new UnrealBinaryReader(new MemoryStream(new byte[] { 254, 255, 255, 255, 65, 0 }));
        Assert.ThrowsException<EndOfStreamException>(() => reader.ReadFString());
    }

    [TestMethod]
    public void UnsupportedJsonEncodingIsRejected()
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        Assert.ThrowsException<JsonSerializationException>(() =>
            asset.DeserializeJsonObject<FString>("{\"Value\":\"x\",\"Encoding\":\"utf-32\"}"));
    }

    private static byte[] Payload(FString value)
    {
        using var stream = new MemoryStream();
        using var writer = new UnrealBinaryWriter(stream);
        writer.Write(value);
        return stream.ToArray();
    }
}
