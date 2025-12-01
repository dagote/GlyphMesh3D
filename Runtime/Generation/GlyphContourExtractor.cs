using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

namespace LanternPines.GlyphMesh3D.Generation
{
    /// <summary>
    /// Extracts and simplifies contours from TMP font atlas textures.
    /// Handles edge detection, chain ordering, and contour simplification.
    /// </summary>
    public static class GlyphContourExtractor
    {
        /// <summary>
        /// Settings for contour extraction
        /// </summary>
        public struct ContourExtractionSettings
        {
            public float simplifyArcLength;
            public float cornerAngleThreshold;
            public float postDpEpsilon;

            public ContourExtractionSettings(float arcLength, float angleThreshold, float dpEpsilon)
            {
                simplifyArcLength = arcLength;
                cornerAngleThreshold = angleThreshold;
                postDpEpsilon = dpEpsilon;
            }
        }

        /// <summary>
        /// Extract simplified contours from a glyph in a TMP font asset
        /// </summary>
        /// <param name="font">TMP font asset</param>
        /// <param name="character">Character to extract</param>
        /// <param name="settings">Extraction settings</param>
        /// <param name="xOffset">Horizontal offset for positioning multiple glyphs</param>
        /// <returns>List of contour boundaries (outer and holes)</returns>
        public static List<List<Vector2>> ExtractContours(TMP_FontAsset font, char character,
            ContourExtractionSettings settings, float xOffset = 0f)
        {
            var boundaries = new List<List<Vector2>>();

            if (!font.characterLookupTable.TryGetValue(character, out TMPro.TMP_Character glyphChar))
                return boundaries;

            UnityEngine.TextCore.Glyph glyph = GetGlyph(font, glyphChar);
            if (glyph == null)
                return boundaries;

            Texture2D atlasTexture = font.atlasTexture;
            if (atlasTexture == null)
                return boundaries;

            var glyphRect = glyph.glyphRect;
            if (glyphRect.width == 0 || glyphRect.height == 0)
                return boundaries;

            // Extract glyph edge pixels
            var edgePixels = ExtractGlyphEdgePixels(atlasTexture, glyphRect);
            if (edgePixels == null || edgePixels.Count == 0)
                return boundaries;

            // Find chains and create boundaries
            var chains = FindChains(edgePixels);

            // Debug logging for 'g' character
            if (character == 'g' || character == 'G')
            {
                Debug.Log($"GlyphContourExtractor: Character '{character}' - Found {chains.Count} chains from {edgePixels.Count} edge pixels");
            }

            foreach (var chain in chains)
            {
                var ordered = OrderChain(chain);
                if (ordered.Count < 3) continue;

                var pts = ordered.Select(p => new Vector2(p.x + xOffset, p.y)).ToList();
                var simplified = SimplifyHybrid(pts, settings.simplifyArcLength,
                    settings.cornerAngleThreshold, settings.postDpEpsilon);

                if (simplified.Count >= 3)
                {
                    float signedArea = GetSignedArea(simplified);
                    if (signedArea < 0f) simplified.Reverse();

                    // Debug logging for 'g' character
                    if (character == 'g' || character == 'G')
                    {
                        Debug.Log($"  Chain: {ordered.Count} points → {simplified.Count} simplified, area={signedArea:F2}");
                    }

                    boundaries.Add(simplified);
                }
            }

            return boundaries;
        }

        /// <summary>
        /// Get the SDF texture for a specific glyph (for debugging/visualization)
        /// </summary>
        public static Texture2D GetGlyphSDFTexture(TMP_FontAsset font, char character)
        {
            if (!font.characterLookupTable.TryGetValue(character, out TMPro.TMP_Character glyphChar))
                return null;

            UnityEngine.TextCore.Glyph glyph = GetGlyph(font, glyphChar);
            if (glyph == null)
                return null;

            Texture2D atlasTexture = font.atlasTexture;
            if (atlasTexture == null)
                return null;

            var glyphRect = glyph.glyphRect;
            if (glyphRect.width == 0 || glyphRect.height == 0)
                return null;

            // Extract just the glyph region
            try
            {
                RenderTexture rt = RenderTexture.GetTemporary(atlasTexture.width, atlasTexture.height, 0);
                Graphics.Blit(atlasTexture, rt);
                RenderTexture.active = rt;
                Texture2D readable = new Texture2D(atlasTexture.width, atlasTexture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, atlasTexture.width, atlasTexture.height), 0, 0);
                readable.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                Color[] pixels = readable.GetPixels(glyphRect.x, glyphRect.y, glyphRect.width, glyphRect.height);
                Texture2D glyphTexture = new Texture2D(glyphRect.width, glyphRect.height);
                glyphTexture.SetPixels(pixels);
                glyphTexture.Apply();

                Object.DestroyImmediate(readable);
                return glyphTexture;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        #region Private Methods

        private static UnityEngine.TextCore.Glyph GetGlyph(TMP_FontAsset font, TMPro.TMP_Character glyphChar)
        {
            if (font.glyphLookupTable != null && font.glyphLookupTable.ContainsKey(glyphChar.glyphIndex))
            {
                return font.glyphLookupTable[glyphChar.glyphIndex];
            }
            return font.glyphTable.FirstOrDefault(g => g.index == glyphChar.glyphIndex);
        }

        private static List<Vector2Int> ExtractGlyphEdgePixels(Texture2D atlasTexture, UnityEngine.TextCore.GlyphRect glyphRect)
        {
            try
            {
                RenderTexture rt = RenderTexture.GetTemporary(atlasTexture.width, atlasTexture.height, 0);
                Graphics.Blit(atlasTexture, rt);
                RenderTexture.active = rt;
                Texture2D readable = new Texture2D(atlasTexture.width, atlasTexture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, atlasTexture.width, atlasTexture.height), 0, 0);
                readable.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                Color[] pixels = readable.GetPixels(glyphRect.x, glyphRect.y, glyphRect.width, glyphRect.height);
                const float threshold = 0.5f;
                List<Vector2Int> edgePixels = new List<Vector2Int>();

                for (int y = 0; y < glyphRect.height; y++)
                {
                    int flippedY = glyphRect.height - 1 - y;
                    for (int x = 0; x < glyphRect.width; x++)
                    {
                        float alpha = pixels[flippedY * glyphRect.width + x].a;
                        if (alpha > threshold && IsEdge(x, flippedY, glyphRect.width, glyphRect.height, pixels, threshold))
                        {
                            edgePixels.Add(new Vector2Int(x, flippedY));
                        }
                    }
                }

                Object.DestroyImmediate(readable);
                return edgePixels;
            }
            catch (System.Exception)
            {
                return new List<Vector2Int>();
            }
        }

        private static bool IsEdge(int x, int y, int width, int height, Color[] pixels, float threshold)
        {
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    return true;

                if (pixels[ny * width + nx].a <= threshold)
                    return true;
            }
            return false;
        }

        private static List<List<Vector2Int>> FindChains(List<Vector2Int> pixels)
        {
            var pixelSet = new HashSet<Vector2Int>(pixels);
            var visited = new HashSet<Vector2Int>();
            var chains = new List<List<Vector2Int>>();

            // Use 8-way connectivity so diagonally-connected edge pixels are treated as one chain.
            // This prevents contours from breaking into multiple segments on high-resolution glyphs
            // (e.g., the tail of a lowercase "g"), which caused simplification artifacts.
            Vector2Int[] neighbors8 =
            {
                new Vector2Int(-1, 1), Vector2Int.up, new Vector2Int(1, 1),
                Vector2Int.left,                    Vector2Int.right,
                new Vector2Int(-1, -1), Vector2Int.down, new Vector2Int(1, -1)
            };

            foreach (var start in pixels)
            {
                if (visited.Contains(start)) continue;

                var chain = new List<Vector2Int>();
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(start);
                visited.Add(start);
                chain.Add(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var dir in neighbors8)
                    {
                        var neighbor = current + dir;
                        if (pixelSet.Contains(neighbor) && !visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                            chain.Add(neighbor);
                        }
                    }
                }
                chains.Add(chain);
            }
            return chains;
        }

        private static List<Vector2Int> OrderChain(List<Vector2Int> chain)
        {
            var ordered = new List<Vector2Int>();
            var orderedSet = new HashSet<Vector2Int>();
            if (chain == null || chain.Count == 0) return ordered;

            var set = new HashSet<Vector2Int>(chain);
            Vector2Int start = chain.OrderBy(p => p.y).ThenBy(p => p.x).First();
            ordered.Add(start);
            orderedSet.Add(start);
            var current = start;
            Vector2Int prev = new Vector2Int(int.MinValue, int.MinValue);

            int safety = 0;
            while (true)
            {
                safety++;
                if (safety > 10000) break;

                var neighbors = GetNeighbors8(current).Where(n => set.Contains(n)).ToList();
                neighbors.RemoveAll(n => n == prev);

                if (neighbors.Count == 0) break;

                Vector2Int next = neighbors[0];
                var unvisited = neighbors.Where(n => !orderedSet.Contains(n)).ToList();

                if (unvisited.Count > 0)
                {
                    Vector2 dirPrev = prev.x == int.MinValue ? Vector2.zero : new Vector2(current.x - prev.x, current.y - prev.y);
                    float bestDot = float.MinValue;
                    Vector2Int best = unvisited[0];

                    foreach (var cand in unvisited)
                    {
                        if (dirPrev == Vector2.zero) { best = cand; break; }
                        Vector2 dir = new Vector2(cand.x - current.x, cand.y - current.y);
                        float dot = Vector2.Dot(dir.normalized, dirPrev.normalized);
                        if (dot > bestDot)
                        {
                            bestDot = dot;
                            best = cand;
                        }
                    }
                    next = best;
                }
                else
                {
                    // Check if we can close the loop back to start
                    if (neighbors.Contains(start) && ordered.Count > 2)
                    {
                        break;
                    }
                    break;
                }

                ordered.Add(next);
                orderedSet.Add(next);
                prev = current;
                current = next;

                // Check if next is adjacent to start to close the loop
                if (ordered.Count > 2 && GetNeighbors8(next).Contains(start))
                {
                    break;
                }
            }

            // Ensure the boundary forms a proper closed loop
            if (ordered.Count > 2)
            {
                Vector2Int first = ordered[0];
                Vector2Int last = ordered[ordered.Count - 1];
                float dist = Vector2Int.Distance(first, last);

                if (dist > 1.5f)
                {
                    var pathToStart = new List<Vector2Int>();
                    var visited = new HashSet<Vector2Int>(ordered);
                    var current2 = last;

                    for (int attempt = 0; attempt < 10 && current2 != first; attempt++)
                    {
                        var neighbors = GetNeighbors8(current2)
                            .Where(n => set.Contains(n) && (!visited.Contains(n) || n == first))
                            .OrderBy(n => Vector2Int.Distance(n, first))
                            .ToList();

                        if (neighbors.Count == 0) break;

                        var next = neighbors[0];
                        if (next == first) break;

                        pathToStart.Add(next);
                        visited.Add(next);
                        current2 = next;
                    }

                    ordered.AddRange(pathToStart);
                }
            }

            return ordered;
        }

        private static IEnumerable<Vector2Int> GetNeighbors8(Vector2Int p)
        {
            yield return new Vector2Int(p.x - 1, p.y - 1);
            yield return new Vector2Int(p.x, p.y - 1);
            yield return new Vector2Int(p.x + 1, p.y - 1);
            yield return new Vector2Int(p.x - 1, p.y);
            yield return new Vector2Int(p.x + 1, p.y);
            yield return new Vector2Int(p.x - 1, p.y + 1);
            yield return new Vector2Int(p.x, p.y + 1);
            yield return new Vector2Int(p.x + 1, p.y + 1);
        }

        private static List<Vector2> SimplifyHybrid(List<Vector2> points, float targetDistance,
            float angleThresholdDeg, float dpEps)
        {
            if (points == null || points.Count < 2) return new List<Vector2>(points);

            int n = points.Count;
            var anchors = new List<int> { 0 };

            for (int i = 1; i < n - 1; i++)
            {
                Vector2 v1 = points[i] - points[i - 1];
                Vector2 v2 = points[i + 1] - points[i];
                if (v1.sqrMagnitude == 0f || v2.sqrMagnitude == 0f) continue;
                float angle = Vector2.Angle(v1, v2);
                if (angle >= angleThresholdDeg) anchors.Add(i);
            }

            if (!anchors.Contains(n - 1)) anchors.Add(n - 1);

            var result = new List<Vector2>();
            for (int ai = 0; ai < anchors.Count - 1; ai++)
            {
                int a = anchors[ai];
                int b = anchors[ai + 1];
                if (a == b) continue;

                var segment = points.GetRange(a, b - a + 1);
                var sampled = SimplifyByArcLength(segment, targetDistance);

                if (result.Count > 0 && result[result.Count - 1] == sampled[0])
                    result.AddRange(sampled.Skip(1));
                else
                    result.AddRange(sampled);
            }

            if (result.Count == 0) return new List<Vector2>(points);
            if (result[0] != points[0]) result.Insert(0, points[0]);
            if (result[result.Count - 1] != points[n - 1]) result.Add(points[n - 1]);

            if (dpEps > 0f)
                result = DouglasPeucker(result, dpEps);

            return result;
        }

        private static List<Vector2> SimplifyByArcLength(List<Vector2> points, float targetDistance)
        {
            if (points == null || points.Count < 2) return new List<Vector2>(points);

            var result = new List<Vector2> { points[0] };
            float currentDistance = 0f;

            for (int i = 1; i < points.Count; i++)
            {
                float dist = Vector2.Distance(points[i - 1], points[i]);
                currentDistance += dist;

                if (currentDistance >= targetDistance)
                {
                    result.Add(points[i]);
                    currentDistance = 0f;
                }
            }

            if (!result[result.Count - 1].Equals(points[points.Count - 1]))
                result.Add(points[points.Count - 1]);

            return result;
        }

        private static List<Vector2> DouglasPeucker(List<Vector2> points, float epsilon)
        {
            if (points == null || points.Count < 3) return new List<Vector2>(points);

            int index = -1;
            float maxDist = 0f;
            for (int i = 1; i < points.Count - 1; i++)
            {
                float dist = PerpendicularDistance(points[i], points[0], points[points.Count - 1]);
                if (dist > maxDist)
                {
                    index = i;
                    maxDist = dist;
                }
            }

            if (maxDist > epsilon)
            {
                var left = DouglasPeucker(points.GetRange(0, index + 1), epsilon);
                var right = DouglasPeucker(points.GetRange(index, points.Count - index), epsilon);
                var result = new List<Vector2>(left);
                result.RemoveAt(result.Count - 1);
                result.AddRange(right);
                return result;
            }
            else
            {
                return new List<Vector2> { points[0], points[points.Count - 1] };
            }
        }

        private static float PerpendicularDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            if (dx == 0 && dy == 0) return Vector2.Distance(p, a);
            float t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / (dx * dx + dy * dy);
            Vector2 proj = new Vector2(a.x + t * dx, a.y + t * dy);
            return Vector2.Distance(p, proj);
        }

        private static float GetSignedArea(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                int next = (i + 1) % polygon.Count;
                area += polygon[i].x * polygon[next].y;
                area -= polygon[next].x * polygon[i].y;
            }
            return area * 0.5f;
        }

        #endregion
    }
}
