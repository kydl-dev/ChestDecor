using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestDecor
{
    internal sealed class AttachVisualModel
    {
        // Meshes with more vertices than this are measured by their bounding box instead of vertex by vertex.
        private const int MaxMeshVertices = 60000;

        public readonly List<AttachVisualPart> Parts = new();
        public Bounds Bounds;   // in prefab-root space
        public bool Valid => Parts.Count > 0;

        // Points that outline the model, in holder space (the model centred on Bounds.center, before the
        // holder's rotation and scale). Built the first time an extent is asked for: the catalog reads the
        // model of every candidate, but only attached items need this.
        private Vector3[] _outline;

        // The last extent that was measured, and the turn and size it was measured for. Dragging a position
        // slider asks again and again for the same turn and size, so the (long) walk over the outline points
        // is only repeated when one of them really changed.
        private bool _hasExtent;
        private Quaternion _extentRotation;
        private float _extentScale;
        private Bounds _extent;

        /// <summary>
        /// Bounds of the model's geometry around the holder's position (the model's centre) once the holder is
        /// turned by <paramref name="rotation"/> and scaled by <paramref name="scale"/>, measured along the
        /// axes of the space the holder sits in (the chest's axes). Not necessarily centred on zero: after a
        /// rotation, an item can reach further to one side of its position than to the other.
        /// </summary>
        public Bounds ExtentAround(Quaternion rotation, float scale)
        {
            var points = _outline ??= BuildOutline();
            if (points.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);

            // exact comparison on purpose: a near-equal turn could still move an edge by a few millimetres
            if (_hasExtent && _extentScale == scale &&
                _extentRotation.x == rotation.x && _extentRotation.y == rotation.y &&
                _extentRotation.z == rotation.z && _extentRotation.w == rotation.w)
                return _extent;

            var m = Matrix4x4.Rotate(rotation);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                var x = (m.m00 * p.x + m.m01 * p.y + m.m02 * p.z) * scale;
                var y = (m.m10 * p.x + m.m11 * p.y + m.m12 * p.z) * scale;
                var z = (m.m20 * p.x + m.m21 * p.y + m.m22 * p.z) * scale;

                if (x < min.x) min.x = x;
                if (y < min.y) min.y = y;
                if (z < min.z) min.z = z;
                if (x > max.x) max.x = x;
                if (y > max.y) max.y = y;
                if (z > max.z) max.z = z;
            }

            var result = new Bounds();
            result.SetMinMax(min, max);

            _extentRotation = rotation;
            _extentScale = scale;
            _extent = result;
            _hasExtent = true;
            return result;
        }

        private Vector3[] BuildOutline()
        {
            var list = new List<Vector3>();
            var seen = new HashSet<Vector3>();   // exact duplicates (UV seams, hard edges) can't change an extent
            var centre = Bounds.center;

            foreach (var part in Parts)
            {
                var mesh = part.Mesh;
                if (mesh == null) continue;

                // The real vertices when Unity lets us read them: a tilted round item then really ends where
                // its surface ends. Otherwise the corners of the mesh's bounding box (never too small).
                Vector3[] vertices = null;
                try
                {
                    if (mesh.isReadable && mesh.vertexCount > 0 && mesh.vertexCount <= MaxMeshVertices)
                        vertices = mesh.vertices;
                }
                catch (Exception)
                {
                    vertices = null;
                }

                if (vertices != null && vertices.Length > 0)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var point = part.Local.MultiplyPoint3x4(vertices[i]) - centre;
                        if (seen.Add(point)) list.Add(point);
                    }
                }
                else
                {
                    var mb = mesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = mb.center + Vector3.Scale(mb.extents,
                            new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        list.Add(part.Local.MultiplyPoint3x4(corner) - centre);
                    }
                }
            }
            return list.ToArray();
        }
    }
}
