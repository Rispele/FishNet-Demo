using TMPro;
using UnityEngine;

namespace Coop.Gameplay
{
    /// <summary>
    /// Ник над головой персонажа. Чисто визуальный компонент: он ничего не синхронизирует,
    /// а только показывает данные, которые уже пришли по сети через PlayerSession.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNameplate : MonoBehaviour
    {
        [SerializeField] private TextMeshPro label;

        private Transform cameraTransform;

        public void SetText(string value, Color color)
        {
            if (label == null)
                return;

            label.text = value;
            label.color = color;
        }

        private void LateUpdate()
        {
            if (label == null)
                return;

            // Camera.main кэшируем: обращение к нему делает поиск по тегу.
            if (cameraTransform == null)
            {
                Camera main = Camera.main;
                if (main == null)
                    return;

                cameraTransform = main.transform;
            }

            // Билборд: разворачиваем табличку к камере.
            transform.rotation = Quaternion.LookRotation(transform.position - cameraTransform.position, Vector3.up);
        }
    }
}
