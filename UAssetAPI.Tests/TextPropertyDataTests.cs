using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.UnrealTypes.EngineEnums;

namespace UAssetAPI.Tests
{
    [TestClass]
    public class TextPropertyDataTests
    {
        private const int Sentinel = 0x12345678;
        private const string Notes = "Translator context: café";

        private static UAsset CreateAsset(int version, bool filtered)
        {
            var asset = new UAsset(EngineVersion.VER_UE5_8);
            asset.CustomVersionContainer = new List<CustomVersion>
            {
                new CustomVersion(nameof(FFortniteMainBranchObjectVersion), version)
            };
            if (filtered) asset.PackageFlags |= EPackageFlags.PKG_FilterEditorOnly;
            return asset;
        }

        // Construct the native Base history layout independently of TextPropertyData.
        private static byte[] NativePayload(UAsset asset, bool hasNotes, string notes)
        {
            using var stream = new MemoryStream();
            using var writer = new AssetBinaryWriter(stream, asset);
            writer.Write((uint)0);
            writer.Write((sbyte)TextHistoryType.Base);
            writer.Write(new FString("Namespace"));
            writer.Write(new FString("Key"));
            writer.Write(new FString("Source"));
            if (hasNotes) writer.Write(new FString(notes));
            writer.Write(Sentinel);
            return stream.ToArray();
        }

        [DataTestMethod]
        [DataRow(-1, false, false)]
        [DataRow(259, false, false)]
        [DataRow(260, false, true)]
        [DataRow(261, false, true)]
        [DataRow(259, true, false)]
        [DataRow(260, true, false)]
        [DataRow(261, true, false)]
        public void ReadBaseHistoryPreservesFollowingData(int version, bool filtered, bool hasNotes)
        {
            var asset = CreateAsset(version, filtered);
            using var stream = new MemoryStream(NativePayload(asset, hasNotes, Notes));
            using var reader = new AssetBinaryReader(stream, asset);
            var text = new TextPropertyData();
            text.Read(reader, false, stream.Length - sizeof(int));

            Assert.AreEqual("Namespace", text.Namespace.Value);
            Assert.AreEqual("Key", text.Value.Value);
            Assert.AreEqual("Source", text.CultureInvariantString.Value);
            Assert.AreEqual(hasNotes ? Notes : "", text.DevNotes.Value);
            Assert.AreEqual(Sentinel, reader.ReadInt32());
            Assert.AreEqual(stream.Length, stream.Position);
        }

        [DataTestMethod]
        [DataRow(-1, false, false)]
        [DataRow(259, false, false)]
        [DataRow(260, false, true)]
        [DataRow(261, false, true)]
        [DataRow(259, true, false)]
        [DataRow(260, true, false)]
        [DataRow(261, true, false)]
        public void WriteBaseHistoryMatchesNativeLayout(int version, bool filtered, bool hasNotes)
        {
            var asset = CreateAsset(version, filtered);
            var text = new TextPropertyData
            {
                Namespace = new FString("Namespace"),
                Value = new FString("Key"),
                CultureInvariantString = new FString("Source"),
                DevNotes = new FString(Notes)
            };
            using var stream = new MemoryStream();
            using var writer = new AssetBinaryWriter(stream, asset);
            int length = text.Write(writer, false);
            Assert.AreEqual(stream.Position, (long)length);
            writer.Write(Sentinel);

            CollectionAssert.AreEqual(NativePayload(asset, hasNotes, Notes), stream.ToArray());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(Notes)]
        public void JsonRoundTripRetainsDeveloperNotes(string notes)
        {
            var asset = CreateAsset(260, false);
            using var stream = new MemoryStream(NativePayload(asset, true, notes));
            using var reader = new AssetBinaryReader(stream, asset);
            var original = new TextPropertyData();
            original.Read(reader, false, stream.Length - sizeof(int));
            var restored = asset.DeserializeJsonObject<TextPropertyData>(asset.SerializeJsonObject(original));

            Assert.AreEqual(notes, restored.DevNotes.Value);
            using var output = new MemoryStream();
            using var writer = new AssetBinaryWriter(output, asset);
            restored.Write(writer, false);
            writer.Write(Sentinel);
            CollectionAssert.AreEqual(stream.ToArray(), output.ToArray());
        }

        [TestMethod]
        public void JsonWithoutDeveloperNotesWritesEmptyNotes()
        {
            var asset = CreateAsset(260, false);
            var text = new TextPropertyData
            {
                Namespace = new FString("Namespace"),
                Value = new FString("Key"),
                CultureInvariantString = new FString("Source")
            };
            var json = JObject.Parse(asset.SerializeJsonObject(text));
            Assert.IsTrue(json.Remove("DevNotes"));
            var restored = asset.DeserializeJsonObject<TextPropertyData>(json.ToString());
            using var stream = new MemoryStream();
            using var writer = new AssetBinaryWriter(stream, asset);
            restored.Write(writer, false);
            writer.Write(Sentinel);

            CollectionAssert.AreEqual(NativePayload(asset, true, ""), stream.ToArray());
        }

        [TestMethod]
        public void CloneDoesNotShareDeveloperNotes()
        {
            var asset = CreateAsset(260, false);
            var original = new TextPropertyData(FName.DefineDummy(asset, "Text"))
            {
                DevNotes = new FString(Notes)
            };
            var clone = (TextPropertyData)original.Clone();
            Assert.AreEqual(original.DevNotes, clone.DevNotes);
            Assert.AreNotSame(original.DevNotes, clone.DevNotes);
            clone.DevNotes.Value = "Changed";
            Assert.AreEqual(Notes, original.DevNotes.Value);
        }
    }
}
