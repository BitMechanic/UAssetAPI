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

Template adaptation still requires valid graph membership and explicit dependency
identities. General import/export remapping, automatic dependency/soft-path
discovery, complete node-family fixtures, source animation/mesh decoding and
JSON-only cross-project dependency bundles remain unfinished. Native compilation
must regenerate executable/derived data after authoring edits. An AnimBlueprint
compile/readback result does not qualify live animation-pose evaluation.
