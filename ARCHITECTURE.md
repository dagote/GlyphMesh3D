# GlyphMesh3D Architecture

## Overview
This document describes the clean architecture structure of the GlyphMesh3D package.

## Directory Structure

```
Runtime/
├── Core/                           # Main driver scripts and data containers
│   ├── GlyphText3D.cs             # Runtime component for instantiating glyph meshes
│   └── GlyphText3DAsset.cs        # ScriptableObject data container for baked meshes
│
├── Editor/                         # Editor-only scripts
│   └── GlyphText3DGenerator.cs    # Editor window for mesh generation (driver)
│
├── Generation/                     # Mesh generation helpers
│   ├── GlyphContourExtractor.cs   # Extracts and simplifies contours from font atlas
│   ├── GlyphMeshBuilder.cs        # Main mesh builder orchestrator
│   ├── GlyphTriangulator.cs       # Triangulation using TriangleNet
│   └── GlyphExtrusionProcessor.cs # Extrusion layer generation and normals
│
├── UV/                             # UV mapping and unwrapping
│   ├── GlyphUVMapper.cs           # Simple UV projection mapping
│   ├── XAtlasWrapper.cs           # XAtlas library integration for advanced UV unwrapping
│   └── UVProjectionMapper.cs      # Planar/cylindrical/box UV projections
│
├── Utilities/                      # General utilities
│   └── GlyphMesh3DInfo.cs         # Package metadata and version info
│
└── Visualization/                  # Debug and preview tools
    └── UVMapGenerator.cs          # UV visualization and export tool
```

## Architecture Principles

### 1. Separation of Concerns
Each helper class has a single, well-defined responsibility:

- **GlyphContourExtractor**: Handles contour extraction and simplification from SDF textures
- **GlyphTriangulator**: Manages triangulation of 2D contours using TriangleNet
- **GlyphExtrusionProcessor**: Computes extrusion layers, boundary normals, and offsets
- **GlyphMeshBuilder**: Orchestrates the complete mesh building process
- **UV Helpers**: Provide various UV generation strategies

### 2. Driver Pattern
Main components act as drivers that orchestrate helper functions:

- **GlyphText3DGenerator**: Editor window driver that coordinates mesh generation
- **GlyphText3D**: Runtime driver that instantiates meshes from baked assets

### 3. Testability
Helper classes are static utility classes or have minimal dependencies, making them easy to unit test independently.

### 4. Reusability
Helpers can be used outside the main generation pipeline:

- UV mappers work with any Unity mesh
- Contour extraction can be used independently
- Triangulation helpers are general-purpose

## Key Components

### Core Components

#### GlyphText3D.cs
- Runtime MonoBehaviour component
- Loads and instances meshes from GlyphText3DAsset
- Handles dynamic text updates (future feature)

#### GlyphText3DAsset.cs
- ScriptableObject data container
- Stores pre-baked mesh data and generation settings
- Created by the generator, consumed at runtime

### Generation Pipeline

#### GlyphContourExtractor.cs
```csharp
public static List<List<Vector2>> ExtractContours(
    TMP_FontAsset font,
    char character,
    ContourExtractionSettings settings,
    float xOffset = 0f)
```
- Extracts edge pixels from TMP font atlas SDF textures
- Orders pixel chains into closed contours
- Simplifies contours using hybrid approach (arc length + Douglas-Peucker)

#### GlyphTriangulator.cs
```csharp
public static TriangulationResult Triangulate(List<List<Vector2>> boundaries)
public static List<List<List<Vector2>>> GroupBoundariesByOuter(List<List<Vector2>> boundaries)
```
- Triangulates 2D contours using TriangleNet library
- Groups boundaries into outer + holes
- Provides deterministic vertex ID mapping

#### GlyphExtrusionProcessor.cs
```csharp
public static List<ExtrusionLayer> BuildExtrusionLayers(ExtrusionProfile profile)
public static BoundaryNormalsResult CalculateBoundaryNormals(List<List<Vector2>> boundaries)
```
- Builds extrusion layers from AnimationCurve keyframes
- Calculates boundary normals for perpendicular offset
- Handles hole vertices differently (inverted offset)

#### GlyphMeshBuilder.cs
```csharp
public static Mesh BuildMesh(List<List<Vector2>> boundaries, MeshBuildSettings settings)
```
- Main orchestrator for complete mesh generation
- Coordinates triangulation, extrusion, and UV generation
- Manages material slot mapping
- Handles both flat and extruded mesh generation

### UV Mapping

#### XAtlasWrapper.cs
- C# wrapper for xatlasLib.dll (UV unwrapping library)
- Provides high-level `GenerateGlyphUVs()` static method
- Handles chart generation, packing, and normalization

#### GlyphUVMapper.cs
- Simple multi-section face unwrapping
- Maps front/back faces to dedicated UV regions
- Handles extrusion strips

#### UVProjectionMapper.cs
- Provides planar, cylindrical, and box projections
- General-purpose UV projection utilities

## Material Slot System

The system uses a slot-based material assignment:

```
MaterialSlotMap:
- KeyframeCount determines total slots
- Slot 0: Front and back faces
- Slots 1..N-1: Extrusion bands (band i is between layer i and layer i+1)
```

Example with 3 keyframes:
- Slot 0: Front/back faces
- Slot 1: Extrusion band 0 (layer 0 → layer 1)
- Slot 2: Extrusion band 1 (layer 1 → layer 2)

## Namespace Organization

```
dagote.ai.GlyphMesh3D
├── Core                    # Core components
├── Generation              # Mesh generation helpers
├── UV                      # UV mapping utilities
├── Visualization           # Debug/preview tools
└── (root)                  # Utilities
```

## Extension Points

### Custom UV Generators
Implement custom UV generation by creating new classes in the `UV/` folder that follow the pattern:

```csharp
public static Vector2[] GenerateCustomUVs(Vector3[] vertices, ...)
```

### Custom Simplification Algorithms
Extend `GlyphContourExtractor` with additional simplification methods.

### Material Slot Customization
Extend `MaterialSlotMap` for custom material assignment strategies.

## Migration from Old Architecture

The old monolithic `GlyphText3DGenerator.cs` (2200+ lines) has been refactored into:

- 4 focused generation helpers (~300-500 lines each)
- 3 UV mapping utilities
- Clean driver that orchestrates helpers

This improves:
- **Readability**: Smaller, focused files
- **Maintainability**: Changes isolated to specific helpers
- **Testability**: Independent helper functions
- **Reusability**: Helpers usable in different contexts

## Future Enhancements

1. **Runtime Mesh Generation**: Extend `GlyphText3D` to generate meshes at runtime
2. **Font Cache System**: Cache frequently used glyphs
3. **Advanced UV Strategies**: Gradient-aware UV unwrapping
4. **Multi-threading**: Parallelize contour extraction for multiple glyphs
5. **LOD Generation**: Automatic Level-of-Detail mesh variants
