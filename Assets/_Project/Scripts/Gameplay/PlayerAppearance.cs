using UnityEngine;
using UnityEngine.Serialization;

namespace Coop.Gameplay
{
    /// <summary>
    /// Визуальная часть персонажа: цвет и подсветка «это я».
    ///
    /// Цвет применяется через MaterialPropertyBlock, а не через renderer.material.
    /// Обращение к renderer.material создаёт копию материала для каждого объекта — на сцене
    /// с десятком игроков это десяток материалов, отдельные SRP-батчи и лишняя нагрузка
    /// на GC. PropertyBlock меняет параметры без клонирования материала.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAppearance : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        [FormerlySerializedAs("_renderers")]
        [SerializeField] private Renderer[] renderers;

        private MaterialPropertyBlock propertyBlock;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();

            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        public void SetColor(Color color)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, color);   // URP / HDRP
                propertyBlock.SetColor(LegacyColorId, color); // Built-in
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
