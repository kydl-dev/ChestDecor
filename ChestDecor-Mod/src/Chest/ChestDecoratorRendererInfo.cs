using UnityEngine;

namespace ChestDecor
{
    internal sealed class ChestDecoratorRendererInfo
    {
        public Renderer Renderer;
        public Material[] Original;
        public string[] Keys; // null entry => not tintable
    }
}
