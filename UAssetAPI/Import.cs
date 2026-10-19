using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI
{
    /// <summary>
    /// UObject resource type for objects that are referenced by this package, but contained within another package.
    /// </summary>
    public class Import
    {
        ///<summary>The name of the UObject represented by this resource.</summary>
        public FName ObjectName;
        ///<summary>Location of the resource for this resource's Outer (import/other export). 0 = this resource is a top-level UPackage</summary>
        public FPackageIndex OuterIndex;
        public FName ClassPackage;
        public FName ClassName;
        /// <summary>
        /// Package Name this import belongs to. Can be none, in that case follow the outer chain
        /// until a set PackageName is found or until OuterIndex is null
        /// </summary>
        public FName PackageName;
        public bool bImportOptional;

        public Import(string classPackage, string className, FPackageIndex outerIndex, string objectName, bool importOptional, UAsset asset)
        {
            ObjectName = new FName(asset, objectName);
            OuterIndex = outerIndex;
            ClassPackage = new FName(asset, classPackage);
            ClassName = new FName(asset, className);
            bImportOptional = importOptional;
        }

        public Import(FName classPackage, FName className, FPackageIndex outerIndex, FName objectName, bool importOptional)
        {
            ObjectName = objectName;
            OuterIndex = outerIndex;
            ClassPackage = classPackage;
            ClassName = className;
            bImportOptional = importOptional;
        }

        public Import(AssetBinaryReader reader)
        {
            ClassPackage = reader.ReadFName();
            ClassName = reader.ReadFName();
            OuterIndex = new FPackageIndex(reader.ReadInt32());
            ObjectName = reader.ReadFName();

            if (reader.Asset.ObjectVersion >= ObjectVersion.VER_UE4_NON_OUTER_PACKAGE_IMPORT
                && !reader.Asset.IsFilterEditorOnly)
                PackageName = reader.ReadFName();

            if (reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.OPTIONAL_RESOURCES) bImportOptional = reader.ReadInt32() == 1;
        }

        public Import()
        {

        }

        /// <summary>
        /// Refuses out-of-table or cyclic outer chains among imports; the loader follows them recursively.
        /// </summary>
        internal static void ValidateOuterChains(UAsset asset)
        {
            for (int i = 0; i < asset.Imports.Count; i++)
            {
                var visited = new HashSet<int> { i };
                for (var outer = asset.Imports[i].OuterIndex; outer != null && outer.IsImport(); outer = asset.Imports[-outer.Index - 1].OuterIndex)
                {
                    outer.Validate(asset);
                    if (!visited.Add(-outer.Index - 1)) throw new FormatException("Cyclic import outer chain at import " + (-i - 1));
                }
            }
        }
    }
}
