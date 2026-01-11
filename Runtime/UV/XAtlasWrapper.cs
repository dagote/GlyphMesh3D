using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LanternPines.GlyphMesh3D.UV
{
    /// <summary>
    /// C# wrapper for xatlas library (xatlasLib.dll) for UV unwrapping.
    /// Based on guycalledfrank/xatlasLib wrapper.
    /// Enhanced wrapper with simplified API for glyph mesh UV generation.
    /// </summary>
    public class XAtlasWrapper : IDisposable
{
    private IntPtr atlasPtr = IntPtr.Zero;
    private bool isDisposed = false;

    // Configuration options
    public int padding = 4;
    public float texelsPerUnit = 1.0f;
    public int resolution = 1024;
    public int maxChartSize = 0;
    public int packAttempts = 4096;
    public bool bruteForce = false;

    #region Native Functions

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr xatlasCreateAtlas();

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasClear(IntPtr atlas);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasAddMesh(IntPtr atlas, int vertexCount, IntPtr positions, IntPtr normals, IntPtr uv, int indexCount, int[] indices32);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasAddUVMesh(IntPtr atlas, int vertexCount, IntPtr uv, int indexCount, int[] indices32, bool allowRotate);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasParametrize(IntPtr atlas);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasPack(IntPtr atlas, int attempts, float texelsPerUnit, int resolution, int maxChartSize, int padding, bool bruteForce);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasNormalize(IntPtr atlas, int[] atlasSizes);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlasGetAtlasCount(IntPtr atlas);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlasGetAtlasIndex(IntPtr atlas, int meshIndex, int chartIndex);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlasGetVertexCount(IntPtr atlas, int meshIndex);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlasGetIndexCount(IntPtr atlas, int meshIndex);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlasGetData(IntPtr atlas, int meshIndex, IntPtr outUV, IntPtr outRef, IntPtr outIndices);

    #endregion

    /// <summary>
    /// Initialize xatlas atlas
    /// </summary>
    public XAtlasWrapper()
    {
        atlasPtr = xatlasCreateAtlas();
        if (atlasPtr == IntPtr.Zero)
        {
            throw new Exception("Failed to create xatlas instance");
        }
    }

    /// <summary>
    /// Add a mesh to the atlas for UV unwrapping
    /// </summary>
    /// <param name="vertices">Vertex positions</param>
    /// <param name="triangles">Triangle indices</param>
    /// <param name="normals">Optional vertex normals (if null, will be computed)</param>
    /// <returns>True if successful</returns>
    public bool AddMesh(Vector3[] vertices, int[] triangles, Vector3[] normals = null)
    {
        if (atlasPtr == IntPtr.Zero)
        {
            return false;
        }

        if (vertices == null || vertices.Length == 0)
        {
            return false;
        }

        if (triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
        {
            return false;
        }

        try
        {
            // Convert Unity Vector3[] to float array for positions
            float[] positions = new float[vertices.Length * 3];
            for (int i = 0; i < vertices.Length; i++)
            {
                positions[i * 3 + 0] = vertices[i].x;
                positions[i * 3 + 1] = vertices[i].y;
                positions[i * 3 + 2] = vertices[i].z;
            }

            // Convert normals if provided
            float[] normalData = null;
            if (normals != null && normals.Length == vertices.Length)
            {
                normalData = new float[normals.Length * 3];
                for (int i = 0; i < normals.Length; i++)
                {
                    normalData[i * 3 + 0] = normals[i].x;
                    normalData[i * 3 + 1] = normals[i].y;
                    normalData[i * 3 + 2] = normals[i].z;
                }
            }

            // Pin arrays in memory
            GCHandle posHandle = GCHandle.Alloc(positions, GCHandleType.Pinned);
            GCHandle normalHandle = normalData != null ? GCHandle.Alloc(normalData, GCHandleType.Pinned) : default(GCHandle);

            try
            {
                IntPtr posPtr = posHandle.AddrOfPinnedObject();
                IntPtr normalPtr = normalData != null ? normalHandle.AddrOfPinnedObject() : IntPtr.Zero;

                xatlasAddMesh(atlasPtr, vertices.Length, posPtr, normalPtr, IntPtr.Zero, triangles.Length, triangles);

                return true;
            }
            finally
            {
                posHandle.Free();
                if (normalHandle.IsAllocated)
                    normalHandle.Free();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to add mesh to xatlas: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Compute UV parametrization (charts/islands)
    /// </summary>
    /// <returns>True if successful</returns>
    public bool ComputeCharts()
    {
        if (atlasPtr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            xatlasParametrize(atlasPtr);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to compute UV charts: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pack charts into a texture atlas
    /// </summary>
    /// <returns>True if successful</returns>
    public bool PackCharts()
    {
        if (atlasPtr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            xatlasPack(atlasPtr, packAttempts, texelsPerUnit, resolution, maxChartSize, padding, bruteForce);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to pack UV charts: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Normalize UV coordinates to 0-1 range
    /// </summary>
    public void Normalize()
    {
        if (atlasPtr == IntPtr.Zero)
            return;

        try
        {
            int atlasCount = xatlasGetAtlasCount(atlasPtr);
            int[] atlasSizes = new int[atlasCount * 2]; // width, height pairs
            for (int i = 0; i < atlasCount; i++)
            {
                atlasSizes[i * 2 + 0] = resolution;
                atlasSizes[i * 2 + 1] = resolution;
            }
            xatlasNormalize(atlasPtr, atlasSizes);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to normalize UVs: {ex.Message}");
        }
    }

    /// <summary>
    /// Get generated UV coordinates for a mesh
    /// </summary>
    /// <param name="meshIndex">Index of the mesh (0 for first added mesh)</param>
    /// <returns>Array of UV coordinates</returns>
    public Vector2[] GetUVs(int meshIndex = 0)
    {
        if (atlasPtr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int vertexCount = xatlasGetVertexCount(atlasPtr, meshIndex);
            if (vertexCount <= 0)
            {
                return null;
            }

            // Allocate arrays for output data
            float[] uvData = new float[vertexCount * 2];
            int[] xrefData = new int[vertexCount];
            int indexCount = xatlasGetIndexCount(atlasPtr, meshIndex);
            int[] indexData = new int[indexCount];

            // Pin arrays
            GCHandle uvHandle = GCHandle.Alloc(uvData, GCHandleType.Pinned);
            GCHandle xrefHandle = GCHandle.Alloc(xrefData, GCHandleType.Pinned);
            GCHandle indexHandle = GCHandle.Alloc(indexData, GCHandleType.Pinned);

            try
            {
                xatlasGetData(atlasPtr, meshIndex, uvHandle.AddrOfPinnedObject(),
                             xrefHandle.AddrOfPinnedObject(), indexHandle.AddrOfPinnedObject());

                // Convert to Vector2 array
                Vector2[] uvs = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    uvs[i] = new Vector2(uvData[i * 2 + 0], uvData[i * 2 + 1]);
                }

                return uvs;
            }
            finally
            {
                uvHandle.Free();
                xrefHandle.Free();
                indexHandle.Free();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to get UVs from xatlas: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the number of atlases created
    /// </summary>
    public int GetAtlasCount()
    {
        if (atlasPtr == IntPtr.Zero)
            return 0;
        return xatlasGetAtlasCount(atlasPtr);
    }

    /// <summary>
    /// Get atlas dimensions (returns resolution since we set fixed size)
    /// </summary>
    public int GetAtlasWidth()
    {
        return resolution;
    }

    /// <summary>
    /// Get atlas dimensions (returns resolution since we set fixed size)
    /// </summary>
    public int GetAtlasHeight()
    {
        return resolution;
    }

    /// <summary>
    /// Get new vertex count after UV generation (xatlas may split vertices)
    /// </summary>
    public int GetVertexCount(int meshIndex = 0)
    {
        if (atlasPtr == IntPtr.Zero)
            return 0;
        return xatlasGetVertexCount(atlasPtr, meshIndex);
    }

    /// <summary>
    /// Get new triangle indices after UV generation
    /// </summary>
    public int[] GetIndices(int meshIndex = 0)
    {
        if (atlasPtr == IntPtr.Zero)
            return null;

        try
        {
            int indexCount = xatlasGetIndexCount(atlasPtr, meshIndex);
            if (indexCount <= 0)
                return null;

            int[] indexData = new int[indexCount];
            GCHandle indexHandle = GCHandle.Alloc(indexData, GCHandleType.Pinned);

            try
            {
                // We need to call GetData with null for UV and xref to get just indices
                xatlasGetData(atlasPtr, meshIndex, IntPtr.Zero, IntPtr.Zero, indexHandle.AddrOfPinnedObject());
                return indexData;
            }
            finally
            {
                indexHandle.Free();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to get indices from xatlas: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get vertex reference array (maps new vertices back to original)
    /// </summary>
    public int[] GetVertexReferences(int meshIndex = 0)
    {
        if (atlasPtr == IntPtr.Zero)
            return null;

        try
        {
            int vertexCount = xatlasGetVertexCount(atlasPtr, meshIndex);
            if (vertexCount <= 0)
                return null;

            int[] xrefData = new int[vertexCount];
            GCHandle xrefHandle = GCHandle.Alloc(xrefData, GCHandleType.Pinned);

            try
            {
                xatlasGetData(atlasPtr, meshIndex, IntPtr.Zero, xrefHandle.AddrOfPinnedObject(), IntPtr.Zero);
                return xrefData;
            }
            finally
            {
                xrefHandle.Free();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to get vertex references from xatlas: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Clean up native resources
    /// </summary>
    public void Dispose()
    {
        if (!isDisposed && atlasPtr != IntPtr.Zero)
        {
            xatlasClear(atlasPtr);
            atlasPtr = IntPtr.Zero;
            isDisposed = true;
        }
    }

    ~XAtlasWrapper()
    {
        Dispose();
    }

        /// <summary>
        /// High-level helper: Generate UV coordinates for a glyph mesh
        /// </summary>
        /// <param name="vertices">Mesh vertices</param>
        /// <param name="triangles">Mesh triangles</param>
        /// <param name="normals">Optional mesh normals</param>
        /// <param name="padding">Padding between UV islands</param>
        /// <param name="resolution">Target texture resolution</param>
        /// <param name="texelsPerUnit">Texels per unit for consistent density</param>
        /// <returns>UV coordinates, or null if generation failed</returns>
        public static Vector2[] GenerateGlyphUVs(Vector3[] vertices, int[] triangles, Vector3[] normals = null,
            int padding = 4, int resolution = 1024, float texelsPerUnit = 1.0f)
        {
            try
            {
                using (var atlas = new XAtlasWrapper())
                {
                    atlas.padding = padding;
                    atlas.resolution = resolution;
                    atlas.texelsPerUnit = texelsPerUnit;

                    if (!atlas.AddMesh(vertices, triangles, normals))
                    {
                        return null;
                    }

                    if (!atlas.ComputeCharts())
                    {
                        return null;
                    }

                    if (!atlas.PackCharts())
                    {
                        return null;
                    }

                    atlas.Normalize();

                    Vector2[] uvs = atlas.GetUVs(0);
                    if (uvs == null || uvs.Length != vertices.Length)
                    {
                        return null;
                    }

                    return uvs;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to generate glyph UVs with xatlas: {ex.Message}");
                return null;
            }
        }
    }
}
