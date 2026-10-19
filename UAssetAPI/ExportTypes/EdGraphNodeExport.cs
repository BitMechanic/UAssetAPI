using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>Qualified native editor graph nodes, including their owned pins and signatures.</summary>
public class EdGraphNodeExport : NormalExport
{
    public EdGraphPin[] Pins;
    public FUserPinInfo[] UserDefinedPins;
    public byte? DynamicCastPureState;

    // Exact native identities whose inheritance/serializer has been checked. Adding
    // a name here is not a claim of fixture or runtime coverage for that class.
    private static readonly HashSet<string> BlueprintClasses = new(StringComparer.Ordinal)
    {
        "K2Node_CallFunction", "K2Node_CallParentFunction", "K2Node_CommutativeAssociativeBinaryOperator",
        "K2Node_DynamicCast", "K2Node_Event", "K2Node_ExecutionSequence", "K2Node_Knot",
        "K2Node_PromotableOperator", "K2Node_Select", "K2Node_VariableGet", "K2Node_VariableSet",
        "K2Node_FunctionEntry", "K2Node_FunctionResult", "K2Node_CustomEvent", "K2Node_Tunnel",
        "K2Node_IfThenElse"
    };
    private static readonly HashSet<string> AnimationClasses = new(StringComparer.Ordinal)
    {
        "AnimGraphNode_BlendSpacePlayer", "AnimGraphNode_Root", "AnimGraphNode_SaveCachedPose",
        "AnimGraphNode_SequencePlayer", "AnimGraphNode_Slot", "AnimGraphNode_StateMachine",
        "AnimGraphNode_StateResult", "AnimGraphNode_TransitionResult", "AnimGraphNode_UseCachedPose",
        "AnimStateAliasNode", "AnimStateEntryNode", "AnimStateNode", "AnimStateTransitionNode"
    };
    private static bool HasUserPins(string name) => name is "K2Node_Event" or "K2Node_CustomEvent" or
        "K2Node_FunctionEntry" or "K2Node_FunctionResult" or "K2Node_Tunnel";

    internal static bool Supports(Export export)
    {
        var asset = export.Asset;
        if (!EdGraphPinSerialization.SupportsProfile(asset)) return false;
        if (export.ClassIndex?.IsImport() != true) return false;
        var cls = export.ClassIndex.ToImport(asset);
        if (cls?.ClassName?.ToString() != "Class" || cls.ClassPackage?.ToString() != "/Script/CoreUObject" || cls.OuterIndex?.IsImport() != true) return false;
        var package = cls.OuterIndex.ToImport(asset);
        string module = package?.ObjectName?.ToString(), name = cls.ObjectName?.ToString();
        return module == "/Script/BlueprintGraph" && BlueprintClasses.Contains(name) ||
            module == "/Script/AnimGraph" && AnimationClasses.Contains(name) ||
            module == "/Script/ControlRigDeveloper" && name == "AnimGraphNode_ControlRig";
    }

    public override void Read(AssetBinaryReader reader, int nextStarting = 0)
    {
        EdGraphPinSerialization.RequireProfile(reader.Asset);
        try
        {
            base.Read(reader, nextStarting);
            Pins = new EdGraphPin[EdGraphPinSerialization.ReadCount(reader, 4)];
            for (int i = 0; i < Pins.Length; i++) Pins[i] = EdGraphPin.Read(reader);
            string name = GetExportClassType().ToString();
            if (HasUserPins(name))
            {
                UserDefinedPins = new FUserPinInfo[EdGraphPinSerialization.ReadCount(reader, 9)];
                for (int i = 0; i < UserDefinedPins.Length; i++) UserDefinedPins[i] = FUserPinInfo.Read(reader);
            }
            if (name == "K2Node_DynamicCast")
            {
                DynamicCastPureState = reader.ReadByte();
                if (DynamicCastPureState > 2) throw new FormatException("Invalid dynamic-cast pure state");
            }
            // An unrecognized subclass tail must remain an opaque export. The
            // normal raw fallback retains it; do not advertise unsafe typed edits.
            if (nextStarting > 0 && reader.BaseStream.Position != nextStarting)
                throw new NotSupportedException("Unqualified native node tail");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not FName.DummyFNameSerializationException)
        {
            throw new FormatException($"Node {ObjectName} at byte {reader.BaseStream.Position}: {ex.Message}", ex);
        }
    }

    public override void Write(AssetBinaryWriter writer)
    {
        EdGraphPinSerialization.RequireProfile(writer.Asset);
        if (!Supports(this)) throw new NotSupportedException($"Unqualified native node class for {ObjectName}");
        if (Extras?.Length > 0) throw new NotSupportedException($"Node {ObjectName} contains an unqualified native tail");
        try
        {
            base.Write(writer);
            EdGraphPinSerialization.WriteCount(writer, Pins?.Length);
            foreach (var pin in Pins)
            {
                if (pin == null) EdGraphPinReference.Write(writer, null);
                else pin.Write(writer);
            }
            string name = GetExportClassType().ToString();
            if (HasUserPins(name))
            {
                EdGraphPinSerialization.WriteCount(writer, UserDefinedPins?.Length);
                foreach (var pin in UserDefinedPins) pin.Write(writer);
            }
            else if (UserDefinedPins != null) throw new FormatException("User-defined signature on a non-editable node");
            if (name == "K2Node_DynamicCast")
            {
                if (DynamicCastPureState is null or > 2) throw new FormatException("Invalid dynamic-cast pure state");
                writer.Write(DynamicCastPureState.Value);
            }
            else if (DynamicCastPureState != null) throw new FormatException("Dynamic-cast state on another node class");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not FName.DummyFNameSerializationException)
        {
            throw new FormatException($"Node {ObjectName} at byte {writer.BaseStream.Position}: {ex.Message}", ex);
        }
    }

    /// <summary>Validate persistent pin identities, references, reciprocal links and split pins. Graph cycles are valid.</summary>
    public static void ValidatePins(UAsset asset)
    {
        var pins = new Dictionary<(int Owner, Guid Id), EdGraphPin>();
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i] is not EdGraphNodeExport node) continue;
            if (node.Pins == null) throw new FormatException($"Missing pins on node {node.ObjectName}");
            foreach (var pin in node.Pins)
            {
                if (pin == null) continue;
                if (pin.OwningNode?.Index != i + 1 || pin.PinId == Guid.Empty || !pins.TryAdd((i + 1, pin.PinId), pin))
                    throw new FormatException($"Invalid owner or duplicate pin identity on node {node.ObjectName}");
            }
        }
        EdGraphPin Resolve(EdGraphPinReference reference)
        {
            if (reference == null) return null;
            if (reference.OwningNode == null || !pins.TryGetValue((reference.OwningNode.Index, reference.PinId), out var target))
                throw new FormatException($"Unresolved pin reference {reference.OwningNode?.Index}/{reference.PinId}");
            return target;
        }
        bool Matches(EdGraphPinReference reference, EdGraphPin pin) => reference != null && reference.OwningNode?.Index == pin.OwningNode.Index && reference.PinId == pin.PinId;
        foreach (var pin in pins.Values)
        {
            if (pin.LinkedTo == null || pin.SubPins == null) throw new FormatException("Missing pin reference array");
            var seen = new HashSet<(int, Guid)>();
            foreach (var link in pin.LinkedTo)
            {
                var peer = Resolve(link);
                if (peer == null) continue;
                if (!seen.Add((link.OwningNode.Index, link.PinId)) || peer.LinkedTo?.Any(x => Matches(x, pin)) != true)
                    throw new FormatException($"Duplicate or nonreciprocal link on pin {pin.PinId}");
            }
            seen.Clear();
            foreach (var child in pin.SubPins)
            {
                var peer = Resolve(child);
                if (peer == null || peer.OwningNode.Index != pin.OwningNode.Index || !seen.Add((child.OwningNode.Index, child.PinId)) || !Matches(peer.ParentPin, pin))
                    throw new FormatException($"Invalid split child on pin {pin.PinId}");
            }
            var parent = Resolve(pin.ParentPin);
            if (parent != null && (parent.OwningNode.Index != pin.OwningNode.Index || parent.SubPins?.Any(x => Matches(x, pin)) != true))
                throw new FormatException($"Invalid split parent on pin {pin.PinId}");
            Resolve(pin.ReferencePassThroughConnection);
        }
    }
}
