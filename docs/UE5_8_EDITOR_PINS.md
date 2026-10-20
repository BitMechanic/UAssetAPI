# UE5.8 editor-pin JSON checkpoint

The managed `SerializeJson` / `DeserializeJson` / `Write` route exposes native
editor pins for a bounded set of Blueprint and Animation node classes. This is
an implementation checkpoint, not comprehensive Blueprint/Animation support.

The qualified archive profile is UE5.8.3, tagged, uncooked and unfiltered:
`VER_UE4_CORRECT_LICENSEE_FLAG`, `IMPORT_TYPE_HIERARCHIES`, Framework37,
UE5Main123, Release44, UE5Release68 and Blueprints10. Other profiles continue
through the existing ordinary/opaque export path; writing typed pins with an
unsupported profile throws explicitly.

`EdGraphNodeExport.Pins` contains owner/GUID identities, direction, native
`FEdGraphPinType`, defaults, text, flags, links, split/parent/pass-through
references and persistent GUIDs. Null owning-array entries are retained.
`UserDefinedPins` exposes the separate native signatures on supported editable
nodes. Edit the matching signature when changing a user-defined parameter.
The reflected `EdGraphPinType` struct uses that same type serializer.

Classification uses native class imports and module identity. The checked class
list is in `ExportTypes/EdGraphNodeExport.cs`; it is not inferred from instance
names. Unknown subclass tails retain the existing opaque export fallback.
Typed exports cannot also write opaque pin bytes through `Extras`. Legacy JSON
documents with ordinary exports and `Extras` remain readable.

Mixed typed/opaque graphs validate the common native pin prefix of opaque nodes
listed in the owning graph's `Nodes` array. The validator keeps the original
export and tail unchanged; it does not promote these nodes to editable support.
This also checks reverse links after a typed link has been removed. Unreadable
prefixes and dangling/asymmetric links refuse before destination replacement.
The entirely opaque legacy route is unchanged. This is not general remapping of
references hidden in unknown subclass tails.

Cloned pin-type properties own their nested names, package references and map
terminal types. Cloned FText histories own recursive source text and format
arguments; names retain their owning asset/name-map association.

FString JSON keeps existing string/null shorthand. If shorthand would change
the native encoding, it uses `{"Value":"text","Encoding":"utf-16"}` (or
`utf-8`). A null native string encodes a zero count; an empty string retains its
terminator. NamedFormat FText uses the existing text/argument serializers,
including recursive arguments and exact 64-bit integers. Malformed lengths,
terminators, counts and excessive text nesting are rejected.

Tagged script boundaries are computed from actual property writes. Names newly
introduced through JSON are bound when the writer encounters them, so JSON-only
array labels do not pollute the native name table. Single-file destination writes
stage their output before replacement. Paired/external bulk payload portability
and transactional replacement of multiple files remain unqualified.

The JSON readers constrain type metadata to library data, standard data
containers and an explicitly requested object root. They do not enable arbitrary
.NET object construction. Arbitrary third-party nested JSON model types are not
implicitly authorized by this change.

Validation includes the generated `TestEditorUE5_8/BP_PinMatrix.uasset` fixture,
the existing JSON/FText tests and negative controls. External UE5.8 native probes
also cover 95 AnimBlueprint nodes/225 pins and a 12-node/37-pin Branch execution
seed, without redistributing the mannequin assets.

`TestEditorUE5_8/BP_MixedOpaque.uasset` was generated in UE5.8.3 using the native
Blueprint toolset: an Actor Blueprint with a `Mixed` function whose entry connects
to the engine ForLoop macro. `K2Node_MacroInstance` intentionally remains outside
the typed class list. Its unchanged JSON reconstruction was loaded, compiled,
invoked, saved and invoked again in a fresh process. Regression tests compare all
export payloads and check destination preservation for invalid reverse links.

Dependency usage bits (`ImportUsedInGame`, `SoftPackageUsedInGame`) are read only by
the Asset Registry and are regenerated from live references by every native save.
The writer preserves the bits it read, gives appended imports and newly appended soft
packages a conservative used-in-game bit, and refuses missing or mismatched bit
arrays. Those appended bits are non-authoritative placeholders, not a dependency
scope decision: a rehydrated package is loaded, compiled and saved natively before
use, and that save replaces every derived table. Packages written with stale and
with corrected bits converge to identical bytes after native save.

`PackageBinding.Describe` lists a package's dependency manifest (imports outside `/Script/`,
soft packages with their paths, the package's own name) and marks load-critical entries.
`PackageBinding.Apply` rebinds exact identities before native load without rewriting a name-map
entry: hard references follow their import, soft references follow their table entry (parsed
top-level soft values and metadata keys are moved with it), and a shared package import is never
renamed on behalf of one object. Chained or swapped bindings refuse; a package binding needs every
top-level object it moves among the destinations; soft references inside containers, optionals or
set/map removal entries refuse. Metadata string values and derived tables (searchable names, registry
tags) are not rebound: the former are not references and native save regenerates the latter.
Destinations carry the class read with Epic
`AssetTools.get_asset_class`. In UE5.8.3 a struct whose identity changes loses its serialized values
silently, so struct identity changes refuse; a parent with another native base passes the class
check and is rewritten by the native compile, so parent chains are compared before binding.

Template adaptation still requires valid graph membership and explicit dependency
identities. General import/export remapping, automatic dependency/soft-path
discovery, complete node-family fixtures, source animation/mesh decoding and
JSON-only cross-project dependency bundles remain unfinished. Native compilation
must regenerate executable/derived data after authoring edits. An AnimBlueprint
compile/readback result does not qualify live animation-pose evaluation.
