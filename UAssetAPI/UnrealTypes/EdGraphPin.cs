using System;
using System.IO;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.UnrealTypes;

public enum EEdGraphPinDirection : byte { Input, Output }
public enum EPinContainerType : byte { None, Array, Set, Map }

/// <summary>The persistent, native FEdGraphTerminalType used for a map value.</summary>
public class FEdGraphTerminalType
{
    public FName TerminalCategory;
    public FName TerminalSubCategory;
    public FPackageIndex TerminalSubCategoryObject;
    public bool IsConst;
    public bool IsWeakPointer;
    public bool IsUObjectWrapper;

    internal static FEdGraphTerminalType Read(AssetBinaryReader reader) => new()
    {
        TerminalCategory = EdGraphPinSerialization.ReadName(reader),
        TerminalSubCategory = EdGraphPinSerialization.ReadName(reader),
        TerminalSubCategoryObject = EdGraphPinSerialization.ReadObject(reader),
        IsConst = reader.ReadBooleanInt(),
        IsWeakPointer = reader.ReadBooleanInt(),
        IsUObjectWrapper = reader.ReadBooleanInt()
    };

    internal void Write(AssetBinaryWriter writer)
    {
        writer.Write(TerminalCategory);
        writer.Write(TerminalSubCategory);
        EdGraphPinSerialization.WriteObject(writer, TerminalSubCategoryObject);
        writer.WriteBooleanInt(IsConst);
        writer.WriteBooleanInt(IsWeakPointer);
        writer.WriteBooleanInt(IsUObjectWrapper);
    }
}

/// <summary>Native FEdGraphPinType, shared by node pins and reflected struct properties.</summary>
public class FEdGraphPinType
{
    public FName PinCategory;
    public FName PinSubCategory;
    public FPackageIndex PinSubCategoryObject;
    public EPinContainerType ContainerType;
    public FEdGraphTerminalType PinValueType;
    public bool IsReference;
    public bool IsWeakPointer;
    public FPackageIndex MemberParent;
    public FName MemberName;
    public Guid MemberGuid;
    public bool IsConst;
    public bool IsUObjectWrapper;
    public bool SerializeAsSinglePrecisionFloat;

    public static FEdGraphPinType Read(AssetBinaryReader reader)
    {
        EdGraphPinSerialization.RequireProfile(reader.Asset);
        var result = new FEdGraphPinType
        {
            PinCategory = EdGraphPinSerialization.ReadName(reader),
            PinSubCategory = EdGraphPinSerialization.ReadName(reader),
            PinSubCategoryObject = EdGraphPinSerialization.ReadObject(reader),
            ContainerType = (EPinContainerType)reader.ReadByte()
        };
        if (result.ContainerType > EPinContainerType.Map) throw new FormatException($"Invalid pin container at byte {reader.BaseStream.Position - 1}");
        if (result.ContainerType == EPinContainerType.Map) result.PinValueType = FEdGraphTerminalType.Read(reader);
        result.IsReference = reader.ReadBooleanInt();
        result.IsWeakPointer = reader.ReadBooleanInt();
        result.MemberParent = EdGraphPinSerialization.ReadObject(reader);
        result.MemberName = EdGraphPinSerialization.ReadName(reader);
        result.MemberGuid = reader.ReadGuid();
        result.IsConst = reader.ReadBooleanInt();
        result.IsUObjectWrapper = reader.ReadBooleanInt();
        result.SerializeAsSinglePrecisionFloat = reader.ReadBooleanInt();
        return result;
    }

    public void Write(AssetBinaryWriter writer)
    {
        EdGraphPinSerialization.RequireProfile(writer.Asset);
        if (ContainerType > EPinContainerType.Map || (ContainerType == EPinContainerType.Map) != (PinValueType != null))
            throw new FormatException("Invalid pin container or map terminal type");
        writer.Write(PinCategory);
        writer.Write(PinSubCategory);
        EdGraphPinSerialization.WriteObject(writer, PinSubCategoryObject);
        writer.Write((byte)ContainerType);
        PinValueType?.Write(writer);
        writer.WriteBooleanInt(IsReference);
        writer.WriteBooleanInt(IsWeakPointer);
        EdGraphPinSerialization.WriteObject(writer, MemberParent);
        writer.Write(MemberName);
        writer.Write(MemberGuid);
        writer.WriteBooleanInt(IsConst);
        writer.WriteBooleanInt(IsUObjectWrapper);
        writer.WriteBooleanInt(SerializeAsSinglePrecisionFloat);
    }
}

public class FUserPinInfo
{
    public FName PinName;
    public FEdGraphPinType PinType;
    public EEdGraphPinDirection DesiredPinDirection;
    public FString PinDefaultValue;

    internal static FUserPinInfo Read(AssetBinaryReader reader) => new()
    {
        PinName = EdGraphPinSerialization.ReadName(reader),
        PinType = FEdGraphPinType.Read(reader),
        DesiredPinDirection = EdGraphPinSerialization.ReadDirection(reader),
        PinDefaultValue = reader.ReadFString()
    };

    internal void Write(AssetBinaryWriter writer)
    {
        if (DesiredPinDirection > EEdGraphPinDirection.Output) throw new FormatException("Invalid signature pin direction");
        writer.Write(PinName);
        PinType.Write(writer);
        writer.Write((byte)DesiredPinDirection);
        writer.Write(PinDefaultValue);
    }
}

public class EdGraphPinReference
{
    public FPackageIndex OwningNode;
    public Guid PinId;

    internal static EdGraphPinReference Read(AssetBinaryReader reader)
    {
        if (reader.ReadBooleanInt()) return null;
        var result = new EdGraphPinReference { OwningNode = EdGraphPinSerialization.ReadObject(reader), PinId = reader.ReadGuid() };
        if (result.OwningNode.Index == 0 || result.PinId == Guid.Empty) throw new FormatException($"Invalid pin identity at byte {reader.BaseStream.Position - 20}");
        return result;
    }

    internal static void Write(AssetBinaryWriter writer, EdGraphPinReference reference)
    {
        writer.WriteBooleanInt(reference == null);
        if (reference == null) return;
        if (reference.OwningNode == null || reference.OwningNode.Index == 0 || reference.PinId == Guid.Empty) throw new FormatException("Invalid pin identity");
        EdGraphPinSerialization.WriteObject(writer, reference.OwningNode);
        writer.Write(reference.PinId);
    }
}

/// <summary>A persistent UEdGraphPin owned by a node. References are owner/GUID pairs.</summary>
public class EdGraphPin : EdGraphPinReference
{
    public FName PinName;
    public TextPropertyData PinFriendlyName;
    public int SourceIndex;
    public FString PinToolTip;
    public EEdGraphPinDirection Direction;
    public FEdGraphPinType PinType;
    public FString DefaultValue;
    public FString AutogeneratedDefaultValue;
    public FPackageIndex DefaultObject;
    public TextPropertyData DefaultTextValue;
    public EdGraphPinReference[] LinkedTo;
    public EdGraphPinReference[] SubPins;
    public EdGraphPinReference ParentPin;
    public EdGraphPinReference ReferencePassThroughConnection;
    public Guid PersistentGuid;
    public uint Flags;

    internal static new EdGraphPin Read(AssetBinaryReader reader)
    {
        var identity = EdGraphPinReference.Read(reader);
        if (identity == null) return null;
        var pin = new EdGraphPin
        {
            OwningNode = EdGraphPinSerialization.ReadObject(reader),
            PinId = reader.ReadGuid(),
            PinName = EdGraphPinSerialization.ReadName(reader),
            PinFriendlyName = ReadText(reader),
            SourceIndex = reader.ReadInt32(),
            PinToolTip = reader.ReadFString(),
            Direction = EdGraphPinSerialization.ReadDirection(reader),
            PinType = FEdGraphPinType.Read(reader),
            DefaultValue = reader.ReadFString(),
            AutogeneratedDefaultValue = reader.ReadFString(),
            DefaultObject = EdGraphPinSerialization.ReadObject(reader),
            DefaultTextValue = ReadText(reader),
            LinkedTo = ReadReferences(reader),
            SubPins = ReadReferences(reader),
            ParentPin = EdGraphPinReference.Read(reader),
            ReferencePassThroughConnection = EdGraphPinReference.Read(reader),
            PersistentGuid = reader.ReadGuid(),
            Flags = reader.ReadUInt32()
        };
        if (!identity.OwningNode.Equals(pin.OwningNode) || identity.PinId != pin.PinId)
            throw new FormatException($"Owning pin identity mismatch at byte {reader.BaseStream.Position}");
        return pin;
    }

    private static TextPropertyData ReadText(AssetBinaryReader reader)
    {
        var text = new TextPropertyData();
        text.Read(reader, false, 0);
        return text;
    }

    private static EdGraphPinReference[] ReadReferences(AssetBinaryReader reader)
    {
        var references = new EdGraphPinReference[EdGraphPinSerialization.ReadCount(reader, 4)];
        for (int i = 0; i < references.Length; i++) references[i] = EdGraphPinReference.Read(reader);
        return references;
    }

    internal static void WriteReferences(AssetBinaryWriter writer, EdGraphPinReference[] references)
    {
        EdGraphPinSerialization.WriteCount(writer, references?.Length);
        foreach (var reference in references) EdGraphPinReference.Write(writer, reference);
    }

    internal void Write(AssetBinaryWriter writer)
    {
        if (Direction > EEdGraphPinDirection.Output) throw new FormatException("Invalid pin direction");
        EdGraphPinReference.Write(writer, this);
        EdGraphPinSerialization.WriteObject(writer, OwningNode);
        writer.Write(PinId);
        writer.Write(PinName);
        PinFriendlyName.Write(writer, false);
        writer.Write(SourceIndex);
        writer.Write(PinToolTip);
        writer.Write((byte)Direction);
        PinType.Write(writer);
        writer.Write(DefaultValue);
        writer.Write(AutogeneratedDefaultValue);
        EdGraphPinSerialization.WriteObject(writer, DefaultObject);
        DefaultTextValue.Write(writer, false);
        WriteReferences(writer, LinkedTo);
        WriteReferences(writer, SubPins);
        EdGraphPinReference.Write(writer, ParentPin);
        EdGraphPinReference.Write(writer, ReferencePassThroughConnection);
        writer.Write(PersistentGuid);
        writer.Write(Flags);
    }
}

internal static class EdGraphPinSerialization
{
    // UE5MainStreamObjectVersions.inl: EdGraphPinSourceIndex = 50. This fork's
    // enum predates that symbol; the qualified installed 5.8.3 archive has 123.
    internal static void RequireProfile(UAsset asset)
    {
        if (!SupportsProfile(asset))
            throw new NotSupportedException("Native editor pins require the qualified UE5.8.3 tagged, uncooked, unfiltered archive profile (Framework=37, UE5Main=123, Release=44, UE5Release=68, Blueprints=10)");
    }

    internal static bool SupportsProfile(UAsset asset) => !(
            asset.ObjectVersion != ObjectVersion.VER_UE4_CORRECT_LICENSEE_FLAG ||
            asset.ObjectVersionUE5 != ObjectVersionUE5.IMPORT_TYPE_HIERARCHIES ||
            asset.IsFilterEditorOnly || asset.HasUnversionedProperties || asset.PackageFlags.HasFlag(EPackageFlags.PKG_Cooked) ||
            (int)asset.GetCustomVersion<FFrameworkObjectVersion>() != 37 ||
            asset.GetCustomVersion(UAPUtils.GUID(0x697dd581, 0xe64f41ab, 0xaa4a51ec, 0xbeb7b628)) != 123 ||
            (int)asset.GetCustomVersion<FReleaseObjectVersion>() != 44 ||
            (int)asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>() != 68 ||
            asset.GetCustomVersion(UAPUtils.GUID(0xb0d832e4, 0x1f894f0d, 0xaccf7eb7, 0x36fd4aa2)) != 10);

    internal static int ReadCount(AssetBinaryReader reader, int minimumBytes)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MainSerializer.MaxSerializedArrayLength || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumBytes)
            throw new FormatException($"Invalid pin array count {count} at byte {reader.BaseStream.Position - 4}");
        return count;
    }

    internal static void WriteCount(AssetBinaryWriter writer, int? count)
    {
        if (!count.HasValue || count < 0 || count > MainSerializer.MaxSerializedArrayLength) throw new FormatException("Invalid pin array count");
        writer.Write(count.Value);
    }

    internal static EEdGraphPinDirection ReadDirection(AssetBinaryReader reader)
    {
        var direction = (EEdGraphPinDirection)reader.ReadByte();
        if (direction > EEdGraphPinDirection.Output) throw new FormatException($"Invalid pin direction at byte {reader.BaseStream.Position - 1}");
        return direction;
    }

    internal static FName ReadName(AssetBinaryReader reader)
    {
        int index = reader.ReadInt32(), number = reader.ReadInt32();
        if (index < 0 || index >= reader.Asset.GetNameMapIndexList().Count || number < 0) throw new FormatException($"Invalid pin FName at byte {reader.BaseStream.Position - 8}");
        return new FName(reader.Asset, index, number);
    }

    internal static FPackageIndex ReadObject(AssetBinaryReader reader)
    {
        var index = new FPackageIndex(reader.ReadInt32());
        ValidateObject(reader.Asset, index);
        return index;
    }

    internal static void ValidateObject(UAsset asset, FPackageIndex index)
    {
        if (index == null || index.Index > asset.Exports.Count || (long)index.Index < -(long)asset.Imports.Count)
            throw new FormatException($"Invalid pin package reference {index?.Index}");
    }

    internal static void WriteObject(AssetBinaryWriter writer, FPackageIndex index)
    {
        ValidateObject(writer.Asset, index);
        writer.Write(index.Index);
    }
}
