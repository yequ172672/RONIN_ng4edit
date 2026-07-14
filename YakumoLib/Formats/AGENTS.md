<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-07-14 | Updated: 2026-07-14 -->

# YakumoLib/Formats

## Purpose
MDL model format handling: parse binary MDL files, write binary MDL files, import ASCII FBX files, and export to ASCII FBX. All format components work with the `MDLFullData` intermediate representation.

## Key Files
| File | Description |
|------|-------------|
| `MDLFullData.cs` | Core data models: `MDLFullData`, `VertexGroup`, `MDLBatch`, `MDLBoneData` — the intermediate representation for MDL model data |
| `MDLParserExtended.cs` | Binary MDL parser — reads `.mdl` files and produces `MDLFullData` objects |
| `MDLWriter.cs` | Binary MDL writer — writes `MDLFullData` back to `.mdl` binary format |
| `MdlToFbxConverter.cs` | FBX ASCII exporter — converts `MDLFullData` to ASCII FBX format (version 7400) |
| `FbxToMdlConverter.cs` | FBX ASCII importer — parses ASCII FBX text and produces `MDLFullData` objects |

## For AI Agents

### Working In This Directory
- All classes use namespace `YakumoLib.Formats` with file-scoped namespace declarations
- Uses `System.Numerics` (`Vector3`, `Vector2`, `Vector4`, `Matrix4x4`) for 3D math
- FBX ASCII parser (`FbxToMdlConverter`) is line-based: builds a tree of `FbxNode` objects, then extracts geometry, skinning, and hierarchy data
- `MDLBatch` fields: meshGroupID, materialID, boneMapID, vertexGroupID, indiceCount, indiceStart, vertexCount
- VertexGroup default strides: MainStride=36, ExStride=8

### FBX ASCII Parsing Notes
- The FBX parser handles: multiple geometry blocks, normals/UVs/tangents/colors with ByPolygonVertex/ByVertex mapping, Direct/IndexToDirect reference types, Deformer/Cluster skinning data, and node hierarchy via Connections
- Polygon indices use FBX triangle-fan format (negative last vertex), converted to triangle list
- Connections use `C: "OO",childId,parentId` format (multiple lines with same key `C:` are stored as children)

## Dependencies

### Internal
- `YakumoLib` core types

<!-- MANUAL: -->