using UnityEngine;
using UnityEngine.UI;

namespace FengChenInn
{
    public sealed class InnRain : MaskableGraphic
    {
        private float redrawAt;

        private void Update()
        {
            if (Time.unscaledTime < redrawAt) return;
            redrawAt = Time.unscaledTime + 1f / 30f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect area = rectTransform.rect;
            for (int i = 0; i < 104; i++)
            {
                float seed = Mathf.Repeat(i * .6180339f, 1f);
                float depth = Mathf.Repeat(i * .4142135f, 1f);
                float time = Time.unscaledTime * (.47f + depth * .33f);
                float y = area.yMax - Mathf.Repeat(seed * 23f + time, 1f) * (area.height + 45f);
                float x = area.xMin + Mathf.Repeat(seed + time * .055f, 1f) * area.width;
                float length = 16f + depth * 36f;
                byte alpha = (byte)(11 + 38 * seed * depth);
                Color32 tint = new Color32(176, 192, 217, alpha);
                int first = mesh.currentVertCount;
                mesh.AddVert(new Vector3(x, y), tint, Vector2.zero);
                mesh.AddVert(new Vector3(x + 1.2f, y), tint, Vector2.zero);
                mesh.AddVert(new Vector3(x - length * .25f, y + length), tint, Vector2.zero);
                mesh.AddVert(new Vector3(x - length * .25f - 1.2f, y + length), tint, Vector2.zero);
                mesh.AddTriangle(first, first + 1, first + 2);
                mesh.AddTriangle(first, first + 2, first + 3);
            }
        }
    }
}
