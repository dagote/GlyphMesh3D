using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// C# wrapper for xatlas library (xatlasLib.dll) for UV unwrapping
/// </summary>
public class XAtlas : IDisposable
{
    private IntPtr atlasPtr = IntPtr.Zero;
    private bool isDisposed = false;

    // Chart options configuration
    public ChartOptions chartOptions = new ChartOptions();

    // Pack options configuration
    public PackOptions packOptions = new PackOptions();

    #region Native Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct ChartOptions
    {
        public float maxChartArea;
        public float maxBoundaryLength;
        public float normalDeviationWeight;
        public float roundnessWeight;
        public float straightnessWeight;
        public float normalSeamWeight;
        public float textureSeamWeight;
        public float maxCost;
        public int maxIterations;
        public int useInputMeshUvs;
        public int fixWinding;

        public ChartOptions(bool useDefaults = true)
        {
            if (useDefaults)
            {
                maxChartArea = 0f;
                maxBoundaryLength = 0f;
                normalDeviationWeight = 2.0f;
                roundnessWeight = 0.01f;
                straightnessWeight = 6.0f;
                normalSeamWeight = 4.0f;
                textureSeamWeight = 0.5f;
                maxCost = 2.0f;
                maxIterations = 1;  // For planar faces
                useInputMeshUvs = 0;
                fixWinding = 0;
            }
            else
            {
                maxChartArea = 0f;
                maxBoundaryLength = 0f;
                normalDeviationWeight = 0f;
                roundnessWeight = 0f;
                straightnessWeight = 0f;
                normalSeamWeight = 0f;
                textureSeamWeight = 0f;
                maxCost = 0f;
                maxIterations = 0;
                useInputMeshUvs = 0;
                fixWinding = 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PackOptions
    {
        public int maxChartSize;
        public int padding;
        public float texelsPerUnit;
        public int resolution;
        public int bilinear;
        public int blockAlign;
        public int bruteForce;
        public int createImage;
        public int rotateChartsToAxis;
        public int rotateCharts;

        public PackOptions(bool useDefaults = true)
        {
            if (useDefaults)
            {
                maxChartSize = 0;
                padding = 4;  // 4 pixels between islands
                texelsPerUnit = 0f;
                resolution = 1024;  // Default texture size
                bilinear = 1;
                blockAlign = 0;
                bruteForce = 0;
                createImage = 0;
                rotateChartsToAxis = 1;
                rotateCharts = 1;
            }
            else
            {
                maxChartSize = 0;
                padding = 0;
                texelsPerUnit = 0f;
                resolution = 0;
                bilinear = 0;
                blockAlign = 0;
                bruteForce = 0;
                createImage = 0;
                rotateChartsToAxis = 0;
                rotateCharts = 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshDecl
    {
        public IntPtr vertexPositionData;
        public IntPtr vertexNormalData;
        public IntPtr vertexUvData;
        public IntPtr indexData;
        public int vertexCount;
        public int indexCount;
        public int vertexPositionStride;
        public int vertexNormalStride;
        public int vertexUvStride;
        public int indexFormat;
    }

    #endregion

    #region Native Functions

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr xatlas_Create();

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlas_Destroy(IntPtr atlas);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_AddMesh(IntPtr atlas, ref MeshDecl meshDecl, uint meshCountHint);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlas_AddMeshJoin(IntPtr atlas);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_ComputeCharts(IntPtr atlas, ref ChartOptions chartOptions);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_PackCharts(IntPtr atlas, ref PackOptions packOptions);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_GetVertexCount(IntPtr atlas, int meshIndex);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_GetIndexCount(IntPtr atlas, int meshIndex);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlas_GetVertexArray(IntPtr atlas, int meshIndex, IntPtr outVertexArray);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlas_GetIndexArray(IntPtr atlas, int meshIndex, IntPtr outIndexArray);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xatlas_GetUVs(IntPtr atlas, int meshIndex, IntPtr outUVs);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_GetWidth(IntPtr atlas);

    [DllImport("xatlasLib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int xatlas_GetHeight(IntPtr atlas);

    #endregion

    /// <summary>
    /// Initialize xatlas atlas
    /// </summary>
    public XAtlas()
    {
        atlasPtr = xatlas_Create();
        if (atlasPtr == IntPtr.Zero)
        {
            throw new Exception("Failed to create xatlas instance");
        }

        // Set default options
        chartOptions = new ChartOptions(true);
        packOptions = new PackOptions(true);
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
            Debug.LogError("XAtlas: Atlas not initialized");
            return false;
        }

        if (vertices == null || vertices.Length == 0)
        {
            Debug.LogError("XAtlas: Vertices array is null or empty");
            return false;
        }

        if (triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
        {
            Debug.LogError("XAtlas: Triangles array is null, empty, or not divisible by 3");
            return false;
        }

        // Convert Unity Vector3[] to float array for positions
        float[] positions = new float[vertices.Length * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            positions[i * 3 + 0] = vertices[i].x;
            positions[i * 3 + 1] = vertices[i].y;
            positions[i * 3 + 2] = vertices[i].z;
        }

        // Convert normals if provided, otherwise use null
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

        // Convert indices to uint array
        uint[] indices = new uint[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            indices[i] = (uint)triangles[i];
        }

        // Pin arrays in memory
        GCHandle posHandle = GCHandle.Alloc(positions, GCHandleType.Pinned);
        GCHandle normalHandle = normalData != null ? GCHandle.Alloc(normalData, GCHandleType.Pinned) : default(GCHandle);
        GCHandle indexHandle = GCHandle.Alloc(indices, GCHandleType.Pinned);

        try
        {
            MeshDecl meshDecl = new MeshDecl
            {
                vertexPositionData = posHandle.AddrOfPinnedObject(),
                vertexNormalData = normalData != null ? normalHandle.AddrOfPinnedObject() : IntPtr.Zero,
                vertexUvData = IntPtr.Zero,
                indexData = indexHandle.AddrOfPinnedObject(),
                vertexCount = vertices.Length,
                indexCount = triangles.Length,
                vertexPositionStride = sizeof(float) * 3,
                vertexNormalStride = normalData != null ? sizeof(float) * 3 : 0,
                vertexUvStride = 0,
                indexFormat = 1  // 32-bit indices
            };

            int result = xatlas_AddMesh(atlasPtr, ref meshDecl, 0);

            if (result != 0)
            {
                Debug.LogError($"XAtlas: AddMesh failed with error code {result}");
                return false;
            }

            return true;
        }
        finally
        {
            posHandle.Free();
            if (normalHandle.IsAllocated)
                normalHandle.Free();
            indexHandle.Free();
        }
    }

    /// <summary>
    /// Compute charts (UV islands) for all added meshes
    /// </summary>
    /// <returns>True if successful</returns>
    public bool ComputeCharts()
    {
        if (atlasPtr == IntPtr.Zero)
        {
            Debug.LogError("XAtlas: Atlas not initialized");
            return false;
        }

        int result = xatlas_ComputeCharts(atlasPtr, ref chartOptions);

        if (result != 0)
        {
            Debug.LogError($"XAtlas: ComputeCharts failed with error code {result}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Pack charts into a texture atlas
    /// </summary>
    /// <returns>True if successful</returns>
    public bool PackCharts()
    {
        if (atlasPtr == IntPtr.Zero)
        {
            Debug.LogError("XAtlas: Atlas not initialized");
            return false;
        }

        int result = xatlas_PackCharts(atlasPtr, ref packOptions);

        if (result != 0)
        {
            Debug.LogError($"XAtlas: PackCharts failed with error code {result}");
            return false;
        }

        return true;
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
            Debug.LogError("XAtlas: Atlas not initialized");
            return null;
        }

        int vertexCount = xatlas_GetVertexCount(atlasPtr, meshIndex);
        if (vertexCount <= 0)
        {
            Debug.LogError($"XAtlas: Invalid vertex count {vertexCount} for mesh {meshIndex}");
            return null;
        }

        float[] uvData = new float[vertexCount * 2];
        GCHandle uvHandle = GCHandle.Alloc(uvData, GCHandleType.Pinned);

        try
        {
            xatlas_GetUVs(atlasPtr, meshIndex, uvHandle.AddrOfPinnedObject());

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
        }
    }

    /// <summary>
    /// Get the atlas texture width after packing
    /// </summary>
    public int GetAtlasWidth()
    {
        if (atlasPtr == IntPtr.Zero)
            return 0;
        return xatlas_GetWidth(atlasPtr);
    }

    /// <summary>
    /// Get the atlas texture height after packing
    /// </summary>
    public int GetAtlasHeight()
    {
        if (atlasPtr == IntPtr.Zero)
            return 0;
        return xatlas_GetHeight(atlasPtr);
    }

    /// <summary>
    /// Get new vertex positions after UV generation (xatlas may split vertices)
    /// </summary>
    public Vector3[] GetVertices(Vector3[] originalVertices, int meshIndex = 0)
    {
        if (atlasPtr == IntPtr.Zero || originalVertices == null)
            return null;

        int vertexCount = xatlas_GetVertexCount(atlasPtr, meshIndex);
        int indexCount = xatlas_GetIndexCount(atlasPtr, meshIndex);

        if (vertexCount <= 0 || indexCount <= 0)
            return null;

        // Get the vertex remapping indices
        uint[] vertexArray = new uint[vertexCount];
        GCHandle vertHandle = GCHandle.Alloc(vertexArray, GCHandleType.Pinned);

        try
        {
            xatlas_GetVertexArray(atlasPtr, meshIndex, vertHandle.AddrOfPinnedObject());

            Vector3[] newVertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                uint originalIndex = vertexArray[i];
                if (originalIndex < originalVertices.Length)
                {
                    newVertices[i] = originalVertices[originalIndex];
                }
            }

            return newVertices;
        }
        finally
        {
            vertHandle.Free();
        }
    }

    /// <summary>
    /// Get new triangle indices after UV generation
    /// </summary>
    public int[] GetIndices(int meshIndex = 0)
    {
        if (atlasPtr == IntPtr.Zero)
            return null;

        int indexCount = xatlas_GetIndexCount(atlasPtr, meshIndex);
        if (indexCount <= 0)
            return null;

        uint[] indexArray = new uint[indexCount];
        GCHandle indexHandle = GCHandle.Alloc(indexArray, GCHandleType.Pinned);

        try
        {
            xatlas_GetIndexArray(atlasPtr, meshIndex, indexHandle.AddrOfPinnedObject());

            int[] indices = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                indices[i] = (int)indexArray[i];
            }

            return indices;
        }
        finally
        {
            indexHandle.Free();
        }
    }

    /// <summary>
    /// Clean up native resources
    /// </summary>
    public void Dispose()
    {
        if (!isDisposed && atlasPtr != IntPtr.Zero)
        {
            xatlas_Destroy(atlasPtr);
            atlasPtr = IntPtr.Zero;
            isDisposed = true;
        }
    }

    ~XAtlas()
    {
        Dispose();
    }
}
