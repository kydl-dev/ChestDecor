using UnityEngine;

namespace ChestDecor
{
    internal sealed class AttachVisualPart
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Matrix4x4 Local; // relative to the prefab root
    }
}
