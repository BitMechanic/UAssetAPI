using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class SoftObjectPathEditTests
{
    private static UAsset Read() => new(Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_SoftReference.uasset"), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static SoftObjectPropertyData Soft(UAsset asset) => asset.Exports.OfType<NormalExport>().Single(e => e.ObjectName.ToString() == "Default__Source_C").Data.OfType<SoftObjectPropertyData>().Single(p => p.Name.ToString() == "SoftAsset");

    [TestMethod]
    public void JsonSoftReferenceEditMaintainsTablesWithoutRenumbering()
    {
        var original = Read();
        var oldPaths = original.SoftObjectPathList.ToArray();
        var oldPackages = original.SoftPackageReferenceList.Select(p => p.Value).ToArray();
        var oldBits = original.SoftPackageBits.Cast<bool>().ToArray();
        var document = JObject.Parse(original.SerializeJson());
        var cdo = document["Exports"].OfType<JObject>().Single(e => (string)e["ObjectName"] == "Default__Source_C");
        var property = cdo["Data"].OfType<JObject>().Single(p => (string)p["Name"] == "SoftAsset");
        property["Value"]["AssetPath"]["PackageName"] = "/Game/UAssetAPIProbe/UserNew";
        property["Value"]["AssetPath"]["AssetName"] = "UserNew";
        var asset = UAsset.DeserializeJson(document.ToString());
        using var written = asset.WriteData();
        Assert.AreEqual(oldPaths.Length + 1, asset.SoftObjectPathList.Count);
        for (int i = 0; i < oldPaths.Length; i++) Assert.AreEqual(oldPaths[i], asset.SoftObjectPathList[i]);
        CollectionAssert.AreEqual(oldPackages, asset.SoftPackageReferenceList.Take(oldPackages.Length).Select(p => p.Value).ToArray());
        CollectionAssert.AreEqual(oldBits, asset.SoftPackageBits.Cast<bool>().Take(oldBits.Length).ToArray());
        Assert.AreEqual("/Game/UAssetAPIProbe/UserNew", asset.SoftPackageReferenceList.Last().Value);
        Assert.IsTrue(asset.SoftPackageBits[asset.SoftPackageBits.Count - 1]);
        byte[] sourceBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_SoftReference.uasset"));
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].ObjectName.ToString() == "Default__Source_C") continue;
            var before = original.Exports[i];
            var after = asset.Exports[i];
            CollectionAssert.AreEqual(sourceBytes.AsSpan((int)before.SerialOffset, (int)before.SerialSize).ToArray(), written.ToArray().AsSpan((int)after.SerialOffset, (int)after.SerialSize).ToArray(), after.ObjectName.ToString());
        }
        using var repeated = asset.WriteData();
        CollectionAssert.AreEqual(written.ToArray(), repeated.ToArray());
        var reread = new UAsset(new AssetBinaryReader(written, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
        Assert.AreEqual(Soft(asset).Value, Soft(reread).Value);
        Assert.AreEqual(asset.SoftObjectPathList.Count, reread.SoftObjectPathList.Count);
        Assert.AreEqual(asset.SoftPackageReferenceList.Count, reread.SoftPackageBits.Count);

        // Another subobject in the same package needs its own path, but no new package slot.
        int packages = asset.SoftPackageReferenceList.Count;
        var subobject = Soft(asset).Value;
        subobject.SubPathString = new FString("Component");
        Soft(asset).Value = subobject;
        using var changedAgain = asset.WriteData();
        Assert.AreEqual(oldPaths.Length + 2, asset.SoftObjectPathList.Count);
        Assert.AreEqual(packages, asset.SoftPackageReferenceList.Count);
        using var jsonAgain = UAsset.DeserializeJson(asset.SerializeJson()).WriteData();
        CollectionAssert.AreEqual(changedAgain.ToArray(), jsonAgain.ToArray());
    }

    [TestMethod]
    public void NewPathInExistingPackageConservativelyPromotesItsUsageBit()
    {
        var asset = Read();
        var path = Soft(asset).Value;
        int package = asset.SoftPackageReferenceList.FindIndex(p => p.Value == path.AssetPath.PackageName.ToString());
        Assert.IsTrue(package >= 0);
        int count = asset.SoftPackageReferenceList.Count;
        asset.SoftPackageBits[package] = false;
        path.AssetPath.AssetName = FName.FromString(asset, "AnotherObject");
        Soft(asset).Value = path;
        using var bytes = asset.WriteData();
        Assert.AreEqual(count, asset.SoftPackageReferenceList.Count);
        Assert.IsTrue(asset.SoftPackageBits[package]);
    }

    [TestMethod]
    public void NewNullPathIsMappedWithoutAddingAPackageDependency()
    {
        var asset = Read();
        int packages = asset.SoftPackageReferenceList.Count;
        int paths = asset.SoftObjectPathList.Count;
        Soft(asset).Value = new FSoftObjectPath(FName.FromString(asset, "None"), FName.FromString(asset, "None"), new FString(""));
        using var bytes = asset.WriteData();
        Assert.AreEqual(paths + 1, asset.SoftObjectPathList.Count);
        Assert.AreEqual(packages, asset.SoftPackageReferenceList.Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EmptyOrMissingPathTableKeepsInlineRepresentation(bool missing)
    {
        var asset = Read();
        if (missing) asset.SoftObjectPathList = null;
        else asset.SoftObjectPathList.Clear();
        var path = Soft(asset).Value;
        using var bytes = new MemoryStream();
        using var writer = new AssetBinaryWriter(bytes, asset);
        path.Write(writer);
        Assert.IsTrue(bytes.Length > sizeof(int));
        bytes.Position = 0;
        Assert.AreEqual(path, new FSoftObjectPath(new AssetBinaryReader(bytes, asset)));
        Assert.IsTrue(asset.SoftObjectPathList == null || asset.SoftObjectPathList.Count == 0);
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void InvalidSoftPathIndexIsAFormatError(int index)
    {
        var asset = Read();
        using var bytes = new MemoryStream(BitConverter.GetBytes(index));
        Assert.ThrowsException<FormatException>(() => new FSoftObjectPath(new AssetBinaryReader(bytes, asset)));
    }

    [TestMethod]
    public void InvalidDependencyBitsRefuseBeforeReplacingDestination()
    {
        var asset = Read();
        var path = Soft(asset).Value;
        path.AssetPath.PackageName = FName.FromString(asset, "/Game/UAssetAPIProbe/UserNew");
        path.AssetPath.AssetName = FName.FromString(asset, "UserNew");
        Soft(asset).Value = path;
        asset.SoftPackageBits.Length = asset.SoftPackageReferenceList.Count + 1;
        string destination = Path.Combine(Path.GetTempPath(), "UAssetAPI-SoftPath-" + Guid.NewGuid().ToString("N") + ".uasset");
        byte[] before = { 3, 1, 4, 1, 5 };
        File.WriteAllBytes(destination, before);
        try
        {
            Assert.ThrowsException<FormatException>(() => asset.Write(destination));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(destination));
        }
        finally { File.Delete(destination); }
    }
}
