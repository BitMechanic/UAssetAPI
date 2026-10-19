using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

/// <summary>
/// Header-level dependency manifest and identity-based rebinding (FORK-S1 S1-4). Each case was
/// first run natively in UE5.8.3; these regressions pin the managed side of those controls.
/// </summary>
[TestClass]
public class PackageBindingTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", name);
    private static UAsset Read(string name) => new(Fixture(name), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    private static UAsset Rehydrate(string name) => UAsset.DeserializeJson(Read(name).SerializeJson());
    private static UAsset Reread(UAsset asset)
    {
        using var bytes = asset.WriteData();
        return new UAsset(new AssetBinaryReader(bytes, asset), EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading);
    }
    private static string[] Names(UAsset asset) => asset.GetNameMapIndexList().Select(n => n.Value).ToArray();
    private static int ImportIndex(UAsset asset, string package, string name) => asset.Imports.FindIndex(i => i.ObjectName.ToString() == name && PackageOf(asset, i) == package);
    private static string PackageOf(UAsset asset, Import import)
    {
        while (import.OuterIndex != null && import.OuterIndex.IsImport()) import = import.OuterIndex.ToImport(asset);
        return import.ObjectName.ToString();
    }
    private static NormalExport Cdo(UAsset asset, string name) => asset.Exports.OfType<NormalExport>().Single(e => e.ObjectName.ToString() == name);

    // Classes of the receiver destinations as read natively with Epic AssetTools.get_asset_class;
    // a Blueprint reports its generated class name.
    private static readonly Dictionary<string, string> Destinations = new()
    {
        ["/Game/S1Receiver/ReboundCurve.ReboundCurve"] = "CurveFloat",
        ["/Game/S1Receiver/ReceiverCurve.ReceiverCurve"] = "CurveFloat",
        ["/Game/S1Receiver/ReceiverLate.ReceiverLate"] = "CurveFloat",
        ["/Game/S1Receiver/UserParent.UserParent_C"] = "UserParent_C",
        ["/Game/S1Incompatible/UserParent.UserParent_C"] = "UserParent_C",
        ["/Game/S1Receiver/S_Other.S_Other"] = "UserDefinedStruct",
        ["/Game/S1Receiver/S_Same.S_Same"] = "UserDefinedStruct",
    };
    private static readonly Dictionary<string, string> Curves = new()
    {
        ["/Game/UAssetAPIProbe/UserCurve.UserCurve"] = "/Game/S1Receiver/ReceiverCurve.ReceiverCurve",
        ["/Game/UAssetAPIProbe/UserLate.UserLate"] = "/Game/S1Receiver/ReceiverLate.ReceiverLate",
    };
    private static Dictionary<string, string> Bindings(string parentSource, string parentDestination) => new(Curves)
    {
        ["/Game/UAssetAPIProbe/Source"] = "/Game/S1Lifecycle/Source",
        [parentSource] = parentDestination,
    };
    private static Dictionary<string, string> Lifecycle => Bindings("/Game/UAssetAPIProbe/UserParent", "/Game/S1Receiver/UserParent");

    [TestMethod]
    public void DescribeListsDependenciesAndLoadCriticalEntries()
    {
        var manifest = PackageBinding.Describe(Read("BP_SoftReference.uasset"));
        Assert.AreEqual("/Game/UAssetAPIProbe/Source", manifest.Package);
        Assert.AreEqual(8, manifest.Imports.Count);
        Assert.IsTrue(manifest.Imports.All(i => !i.Package.StartsWith("/Script/", StringComparison.Ordinal)));
        CollectionAssert.AreEquivalent(new[] { "UserParent_C", "/Game/UAssetAPIProbe/UserParent" }, manifest.Imports.Where(i => i.LoadCritical).Select(i => i.ObjectName).ToArray());
        CollectionAssert.AreEqual(new[] { "DefaultSceneRoot_GEN_VARIABLE", "UserParent_C", "/Game/UAssetAPIProbe/UserParent" },
            manifest.Imports.Single(i => i.ObjectName == "DefaultSceneRoot_GEN_VARIABLE").OuterChain);
        CollectionAssert.AreEqual(new[] { "/Game/UAssetAPIProbe/UserCurve.UserCurve" }, manifest.SoftPackages.Single(s => s.Package == "/Game/UAssetAPIProbe/UserCurve").Paths);
        Assert.AreEqual(6, manifest.SoftPackages.Single(s => s.Package == "/Game/UAssetAPIProbe/Source").Paths.Length);
    }

    // The S1-2 lifecycle through the helper: identical native results to the explicit mapping.
    [TestMethod]
    public void LifecycleBindingMovesIdentitiesWithoutRewritingNameEntries()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        string[] names = Names(asset);
        int imports = asset.Imports.Count;
        PackageBinding.Apply(asset, Lifecycle, Destinations);
        CollectionAssert.AreEqual(names, Names(asset).Take(names.Length).ToArray(), "existing name entries");
        var after = Reread(asset);
        Assert.AreEqual(imports, after.Imports.Count, "unshared package imports change identity in place; no orphans");
        var identities = after.Imports.SelectMany(i => new[] { i.ObjectName.ToString(), i.ClassPackage.ToString() })
            .Concat(after.SoftObjectPathList.Select(p => p.AssetPath.PackageName.ToString()))
            .Concat(after.SoftPackageReferenceList.Select(p => p.Value))
            .Concat(after.MetaData.ObjectMetaDataMap.Keys.Select(k => k.AssetPath.PackageName.ToString()));
        Assert.IsFalse(identities.Any(s => s.StartsWith("/Game/UAssetAPIProbe/", StringComparison.Ordinal)));
        int parent = after.Imports.FindIndex(i => i.ObjectName.ToString() == "/Game/S1Receiver/UserParent");
        foreach (string name in new[] { "UserParent_C", "Default__UserParent_C" })
            Assert.AreEqual(-parent - 1, after.Imports.Single(i => i.ObjectName.ToString() == name).OuterIndex.Index, name);
        Assert.AreEqual("/Game/S1Receiver/UserParent", after.Imports.Single(i => i.ObjectName.ToString() == "Default__UserParent_C").ClassPackage.ToString());
        Assert.AreEqual("/Game/S1Receiver/ReceiverCurve", PackageOf(after, after.Imports[ImportIndex(after, "/Game/S1Receiver/ReceiverCurve", "ReceiverCurve")]));
        var soft = (SoftObjectPropertyData)Cdo(after, "Default__Source_C").Data.Single(p => p.Name.ToString() == "SoftAsset");
        Assert.AreEqual("/Game/S1Receiver/ReceiverCurve.ReceiverCurve", soft.Value.AssetPath.PackageName + "." + soft.Value.AssetPath.AssetName);
        Assert.IsTrue(after.SoftObjectPathList.Any(p => p.AssetPath.PackageName.ToString() == "/Game/S1Lifecycle/Source"));
        using var written = asset.WriteData();
        CollectionAssert.AreEqual(written.ToArray(), Reread(after).WriteData().ToArray(), "stable rewrite");
    }

    [TestMethod]
    public void SharedShortNameBindingTouchesOnlyTheBoundObject()
    {
        var asset = Rehydrate("Binding/BP_SharedNames.uasset");
        int a = ImportIndex(asset, "/Game/S1Shared/A/Curve", "Curve"), b = ImportIndex(asset, "/Game/S1Shared/B/Curve", "Curve");
        Assert.IsTrue(a >= 0 && b >= 0 && a != b);
        int entry = asset.SearchNameReference(new FString("Curve"));
        string[] names = Names(asset);
        PackageBinding.Apply(asset, new Dictionary<string, string> { ["/Game/S1Shared/A/Curve.Curve"] = "/Game/S1Receiver/ReboundCurve.ReboundCurve" }, Destinations);
        CollectionAssert.AreEqual(names, Names(asset).Take(names.Length).ToArray(), "existing name entries");
        var after = Reread(asset);
        Assert.AreEqual("ReboundCurve", after.Imports[a].ObjectName.ToString());
        Assert.AreEqual("/Game/S1Receiver/ReboundCurve", PackageOf(after, after.Imports[a]));
        Assert.AreEqual("Curve", after.Imports[b].ObjectName.ToString());
        Assert.AreEqual("/Game/S1Shared/B/Curve", PackageOf(after, after.Imports[b]));
        Assert.AreEqual(entry, after.SearchNameReference(new FString("Curve")));
        var cdo = Cdo(after, "Default__BP_SharedNames_C");
        Assert.AreEqual(FPackageIndex.FromImport(a).Index, ((ObjectPropertyData)cdo.Data.Single(p => p.Name.ToString() == "HardA")).Value.Index);
        Assert.AreEqual(FPackageIndex.FromImport(b).Index, ((ObjectPropertyData)cdo.Data.Single(p => p.Name.ToString() == "HardB")).Value.Index);
        var soft = ((SoftObjectPropertyData)cdo.Data.Single(p => p.Name.ToString() == "SoftA")).Value;
        Assert.AreEqual("/Game/S1Receiver/ReboundCurve.ReboundCurve", soft.AssetPath.PackageName + "." + soft.AssetPath.AssetName);
        Assert.IsFalse(after.SoftObjectPathList.Any(p => p.AssetPath.PackageName.ToString() == "/Game/S1Shared/A/Curve"));
        // A Blueprint variable sharing the short name of the rebound asset keeps its declaration and default.
        Assert.IsTrue(after.Exports.OfType<ClassExport>().Single(e => e.ObjectName.ToString() == "BP_SharedNames_C").LoadedProperties.Any(p => p.Name.ToString() == "Curve"));
        Assert.AreEqual(3.5, ((DoublePropertyData)cdo.Data.Single(p => p.Name.ToString() == "Curve")).Value);
    }

    // The parent class and its default object share one package import. An object-level binding moves the
    // class alone (natively: loads and compiles, with a missing-package warning for the default object's
    // import); a package-level binding moves both (the lifecycle case).
    [TestMethod]
    public void ObjectBindingLeavesASharedPackageImportInPlace()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        int cls = ImportIndex(asset, "/Game/UAssetAPIProbe/UserParent", "UserParent_C");
        int cdo = ImportIndex(asset, "/Game/UAssetAPIProbe/UserParent", "Default__UserParent_C");
        int oldPackage = asset.Imports[cdo].OuterIndex.Index;
        PackageBinding.Apply(asset, Bindings("/Game/UAssetAPIProbe/UserParent.UserParent_C", "/Game/S1Receiver/UserParent.UserParent_C"), Destinations);
        var after = Reread(asset);
        Assert.AreEqual("/Game/S1Receiver/UserParent", PackageOf(after, after.Imports[cls]));
        Assert.AreEqual(oldPackage, after.Imports[cdo].OuterIndex.Index);
        Assert.AreEqual("/Game/UAssetAPIProbe/UserParent", PackageOf(after, after.Imports[cdo]));
        Assert.AreEqual("/Game/UAssetAPIProbe/UserParent", after.Imports[-oldPackage - 1].ObjectName.ToString());
    }

    // Natively an Actor-derived source bound to an Object-derived parent loads and compiles without errors while
    // the compiler removes parent calls and turns native events into custom events. Class identity cannot see
    // the native base, so the binding is accepted; the orchestrator compares the parent chain before binding.
    [TestMethod]
    public void ParentWithAnotherNativeBaseIsAcceptedOnClassIdentity()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        PackageBinding.Apply(asset, Bindings("/Game/UAssetAPIProbe/UserParent", "/Game/S1Incompatible/UserParent"), Destinations);
        var after = Reread(asset);
        Assert.AreEqual("/Game/S1Incompatible/UserParent", PackageOf(after, after.Imports[ImportIndex(after, "/Game/S1Incompatible/UserParent", "UserParent_C")]));
    }

    // Natively the serialized struct value was reset to defaults, silently, for both targets.
    [DataTestMethod]
    [DataRow("/Game/S1Struct/S_Original.S_Original", "/Game/S1Receiver/S_Other.S_Other")]
    [DataRow("/Game/S1Struct/S_Original.S_Original", "/Game/S1Receiver/S_Same.S_Same")]
    [DataRow("/Game/S1Struct/S_Original", "/Game/S1Receiver/S_Original")]
    public void StructIdentityChangeRefuses(string source, string destination)
    {
        var asset = Rehydrate("Binding/BP_StructUse.uasset");
        string json = asset.SerializeJson();
        var error = Assert.ThrowsException<FormatException>(() => PackageBinding.Apply(asset, new Dictionary<string, string> { [source] = destination }, Destinations));
        StringAssert.Contains(error.Message, "user-defined struct");
        Assert.AreEqual(json, asset.SerializeJson());
    }

    [DataTestMethod]
    [DataRow("/Game/UAssetAPIProbe/UserCurve.UserCurve", "/Game/S1Receiver/S_Other.S_Other")]
    [DataRow("/Game/UAssetAPIProbe/UserParent.UserParent_C", "/Game/S1Receiver/ReceiverCurve.ReceiverCurve")]
    public void ClassMismatchRefuses(string source, string destination)
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        string json = asset.SerializeJson();
        var error = Assert.ThrowsException<FormatException>(() => PackageBinding.Apply(asset, new Dictionary<string, string> { [source] = destination }, Destinations));
        StringAssert.Contains(error.Message, "not a");
        Assert.AreEqual(json, asset.SerializeJson());
    }

    [DataTestMethod]
    [DataRow("Game/NoSlash.ReceiverCurve")]
    [DataRow("/Game/Trailing/.ReceiverCurve")]
    [DataRow("/Game/S1Receiver/ReceiverCurve.ReceiverCurve.Extra")]
    [DataRow("/Game/Has Space.ReceiverCurve")]
    [DataRow("/Game//Double.ReceiverCurve")]
    [DataRow("/Game/S1Receiver/ReceiverCurve.")]
    [DataRow("")]
    public void MalformedIdentityRefuses(string destination)
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        var error = Assert.ThrowsException<FormatException>(() => PackageBinding.Apply(asset, new Dictionary<string, string> { ["/Game/UAssetAPIProbe/UserCurve.UserCurve"] = destination }, Destinations));
        StringAssert.Contains(error.Message, "Malformed");
    }

    [TestMethod]
    public void UnknownSourceRefuses()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        var error = Assert.ThrowsException<FormatException>(() => PackageBinding.Apply(asset, new Dictionary<string, string> { ["/Game/Nowhere/Curve.Curve"] = "/Game/S1Receiver/ReceiverCurve.ReceiverCurve" }, Destinations));
        StringAssert.Contains(error.Message, "Unknown source");
    }

    [TestMethod]
    public void UnmappedLoadCriticalEntryRefusesWithDestinationIntact()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        var bindings = new Dictionary<string, string>(Curves) { ["/Game/UAssetAPIProbe/Source"] = "/Game/S1Lifecycle/Source" };
        var destinations = Destinations.Where(d => d.Value != "UserParent_C").ToDictionary(d => d.Key, d => d.Value);
        string path = Path.Combine(Path.GetTempPath(), "UAssetBinding-" + Guid.NewGuid().ToString("N") + ".uasset");
        byte[] sentinel = { 6, 2, 6, 4, 3 };
        File.WriteAllBytes(path, sentinel);
        try
        {
            var error = Assert.ThrowsException<FormatException>(() => { PackageBinding.Apply(asset, bindings, destinations); asset.Write(path); });
            StringAssert.Contains(error.Message, "neither bound nor present");
            CollectionAssert.AreEqual(sentinel, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(path), "*" + Path.GetFileNameWithoutExtension(path) + "*").Length);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void NestedSoftValueRefuses()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        var cdo = Cdo(asset, "Default__Source_C");
        var soft = (SoftObjectPropertyData)cdo.Data.Single(p => p.Name.ToString() == "SoftAsset");
        cdo.Data.Add(new ArrayPropertyData(FName.FromString(asset, "Nested"))
        {
            ArrayType = FName.FromString(asset, "SoftObjectProperty"),
            Value = new PropertyData[] { new SoftObjectPropertyData(FName.FromString(asset, "Nested")) { Value = soft.Value } },
        });
        var error = Assert.ThrowsException<FormatException>(() => PackageBinding.Apply(asset, Lifecycle, Destinations));
        StringAssert.Contains(error.Message, "container property");
    }

    [TestMethod]
    public void RepeatedApplyIsIdempotent()
    {
        var asset = Rehydrate("BP_SoftReference.uasset");
        PackageBinding.Apply(asset, Lifecycle, Destinations);
        byte[] first = asset.WriteData().ToArray();
        PackageBinding.Apply(asset, Lifecycle, Destinations);
        CollectionAssert.AreEqual(first, asset.WriteData().ToArray());
        var written = Reread(asset);
        PackageBinding.Apply(written, Lifecycle, Destinations);
        CollectionAssert.AreEqual(first, written.WriteData().ToArray());
    }
}
