using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.UnrealTypes.EngineEnums;

namespace UAssetAPI.Tests;

[TestClass]
public class NamedFormatTests
{
    private static UAsset Asset() => new(EngineVersion.VER_UE5_8)
    {
        CustomVersionContainer = new List<CustomVersion>
        {
            new(nameof(FEditorObjectVersion), 40), new(nameof(FFortniteMainBranchObjectVersion), 268),
            new(nameof(FUE5ReleaseStreamObjectVersion), 68)
        }
    };

    private static void Literal(BinaryWriter writer, string value)
    {
        writer.Write((uint)0);
        writer.Write((sbyte)-1);
        writer.Write(1);
        byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    // Independent native history=1 layout; NamedFormat always uses 64-bit Int,
    // including versions where ArgumentFormat uses the older 32-bit form.
    private static byte[] NativePayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)0);
        writer.Write((sbyte)1);
        Literal(writer, "{n}/{u}/{f}/{d}/{t}");
        writer.Write(5);
        foreach (var type in new byte[] { 0, 1, 2, 3, 4 })
        {
            writer.Write(-2);
            writer.Write(new byte[] { (byte)(65 + type), 0, 0, 0 });
            writer.Write(type);
            switch (type)
            {
                case 0: writer.Write(long.MinValue); break;
                case 1: writer.Write(ulong.MaxValue); break;
                case 2: writer.Write(-0.0f); break;
                case 3: writer.Write(double.PositiveInfinity); break;
                case 4:
                    writer.Write((uint)0);
                    writer.Write((sbyte)1);
                    Literal(writer, "nested");
                    writer.Write(0);
                    break;
            }
        }
        return stream.ToArray();
    }

    [TestMethod]
    public void RecursiveNamedFormatAndExactNumericArgumentsSurviveManagedJson()
    {
        var asset = Asset();
        asset.ClearNameIndexList();
        byte[] native = NativePayload();
        using var reader = new AssetBinaryReader(new MemoryStream(native), asset);
        var text = new TextPropertyData();
        text.Read(reader, false, native.Length);
        Assert.AreEqual(native.Length, reader.BaseStream.Position);
        var restored = asset.DeserializeJsonObject<TextPropertyData>(asset.SerializeJsonObject(text));
        using var output = new MemoryStream();
        using var writer = new AssetBinaryWriter(output, asset);
        Assert.AreEqual(native.Length, restored.Write(writer, false));
        CollectionAssert.AreEqual(native, output.ToArray());
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void MalformedNamedArgumentCountIsRejected(int count)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((uint)0); writer.Write((sbyte)1);
            Literal(writer, "format"); writer.Write(count);
        }
        stream.Position = 0;
        using var reader = new AssetBinaryReader(stream, Asset());
        Assert.ThrowsException<FormatException>(() => new TextPropertyData().Read(reader, false, stream.Length));
    }

    [TestMethod]
    public void ExcessiveTextNestingIsRejected()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            for (int i = 0; i < 100; i++) { writer.Write((uint)0); writer.Write((sbyte)1); }
        stream.Position = 0;
        using var reader = new AssetBinaryReader(stream, Asset());
        Assert.ThrowsException<FormatException>(() => new TextPropertyData().Read(reader, false, stream.Length));
    }
}
