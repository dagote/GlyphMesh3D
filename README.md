# GlyphMesh3D

A Unity package for generating high-quality extruded 3D meshes from text glyphs with customizable extrusion profiles, advanced UV mapping, and multi-material support.

## What It Does

GlyphMesh3D converts TrueType/OpenType fonts (via TextMeshPro) into fully customizable 3D meshes. It extracts glyph contours, triangulates them, and extrudes them into 3D geometry with:

- **Customizable extrusion profiles** using AnimationCurves for depth, scale, and offset
- **Advanced UV mapping** via xAtlas library or built-in projection methods
- **Multi-material support** with separate material slots for faces and extrusion bands
- **Runtime text rendering** with a lightweight component for displaying 3D text

Perfect for creating logos, signage, title screens, and stylized 3D typography.

## Key Features

- **Contour Extraction**: Extracts precise glyph outlines from TextMeshPro font atlases with SDF support
- **Flexible Extrusion**: Control depth, scale, and offset at multiple keyframes using AnimationCurves
- **UV Unwrapping**:
  - xAtlas integration for automatic UV unwrapping
  - Planar, cylindrical, and box projection mapping
  - Per-section UV mapping for faces and extrusion strips
- **Material Slots**: Assign different materials to front/back faces and individual extrusion bands
- **Runtime Component**: `GlyphText3D` component for displaying pre-baked glyphs at runtime
- **Clean Architecture**: Modular design with separated concerns (see [ARCHITECTURE.md](ARCHITECTURE.md))

## Installation

### Install via Git URL

Install directly from GitHub using Unity's Package Manager:

1. Open Unity Editor
2. Open Package Manager (Window > Package Manager)
3. Click the `+` button in the top-left corner
4. Select "Add package from git URL..."
5. Enter: `https://github.com/dagoteai/GlyphMesh3D.git`
6. Click "Add"

### Install a Specific Version

To install a specific version tag:
```
https://github.com/dagoteai/GlyphMesh3D.git#v1.0.0
```

### Install via manifest.json

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.dagoteai.glyphmesh3d": "https://github.com/dagoteai/GlyphMesh3D.git"
  }
}
```

## Requirements

- Unity 2020.3 or later
- TextMeshPro 3.0.6+ (automatically installed as dependency)

## Basic Usage

### Generating 3D Glyphs

1. **Open the Generator**:
   - Create a `GlyphText3DAsset` via `Assets > Create > Glyph Text 3D Asset`
   - Or use the generator window (future feature)

2. **Configure Settings**:
   - Select a TextMeshPro font asset
   - Enter the characters you want to generate
   - Configure extrusion profile (depth, keyframes, scale curves)
   - Choose UV mapping method (xAtlas recommended)

3. **Generate**:
   - Click "Generate" to create the 3D meshes
   - Meshes are baked into the `GlyphText3DAsset` ScriptableObject

### Using at Runtime

1. **Create a GlyphText3D GameObject**:
   ```
   GameObject > 3D Object > Glyph Text 3D
   ```

2. **Assign Asset**:
   - Drag your generated `GlyphText3DAsset` into the component's Asset field

3. **Configure Text**:
   - Set the `text` field to display your content
   - Adjust `fontSize`, `characterSpacing`, `wordSpacing`, etc.

4. **Materials**:
   - Assign materials to the MeshRenderer
   - Slot 0: Front/back faces
   - Slots 1+: Extrusion bands (based on keyframe count)

### Example Script

```csharp
using dagote.ai.GlyphMesh3D.Core;
using UnityEngine;

public class Example : MonoBehaviour
{
    public GlyphText3DAsset myAsset;

    void Start()
    {
        var textObj = new GameObject("My3DText");
        var glyphText = textObj.AddComponent<GlyphText3D>();

        glyphText.asset = myAsset;
        glyphText.text = "Hello World";
        glyphText.fontSize = 72f;
        glyphText.characterSpacing = 0.15f;
    }
}
```

## Project Structure

```
Runtime/
├── Core/              # GlyphText3D component and asset containers
├── Generation/        # Mesh generation pipeline (contours, triangulation, extrusion)
├── UV/                # UV mapping strategies (xAtlas, projection mapping)
├── Utilities/         # Helper utilities and package info
├── Visualization/     # Debug and preview tools
├── Materials/         # Example materials
├── Shader/            # Custom shaders
└── Textures/          # Example textures
```

For detailed architecture information, see [ARCHITECTURE.md](ARCHITECTURE.md).

## Known Issues

- **Font Compatibility**: Some fonts don't generate correctly or produce unexpected results. Testing with different font assets is recommended.
- **High-Resolution Generation Lag**: Generating 3D glyph maps with high resolution causes significant performance lag during the generation process.
- **Low-Resolution Breakdown**: Very low resolution 2D glyph maps can break the 3D generation pipeline, resulting in malformed geometry.
- **Character Spacing**: Spacing between characters sometimes breaks, particularly with certain font and configuration combinations.
- **Extrusion Weight Normalization**: Extrusion weights are not normalized correctly, which can lead to inconsistent depth distribution.
- **General Stability**: Various bugs and improvements are needed. The package is functional for most use cases but may encounter edge cases.

## Documentation

- **[ARCHITECTURE.md](ARCHITECTURE.md)**: Detailed technical architecture and design patterns
- **[CHANGELOG.md](CHANGELOG.md)**: Version history and changes
- **[LICENSE.md](LICENSE.md)**: MIT License

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Author

**dagote.ai**
- Email: ops@dagote.ai
- GitHub: [@dagoteai](https://github.com/dagoteai)
