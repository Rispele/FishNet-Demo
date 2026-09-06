using UnityEngine;

namespace Coop.Networking
{
    /// <summary>
    /// Палитра цветов игроков.
    ///
    /// Цвет выбирает сервер по индексу и реплицирует его SyncVar'ом, а не вычисляет каждый
    /// клиент самостоятельно. Правило простое: любое состояние, которое должно совпадать
    /// у всех участников, должно приходить с сервера — даже такое безобидное, как цвет капсулы.
    /// </summary>
    public static class PlayerPalette
    {
        private static readonly Color[] Colors =
        {
            new(0.20f, 0.60f, 1.00f), // синий
            new(1.00f, 0.45f, 0.30f), // оранжевый
            new(0.35f, 0.85f, 0.45f), // зелёный
            new(0.95f, 0.80f, 0.25f), // жёлтый
            new(0.80f, 0.45f, 0.95f), // фиолетовый
            new(0.30f, 0.90f, 0.90f)  // бирюзовый
        };

        public static int Count => Colors.Length;

        public static Color Get(int index)
        {
            if (Colors.Length == 0)
                return Color.white;

            int safeIndex = ((index % Colors.Length) + Colors.Length) % Colors.Length;
            return Colors[safeIndex];
        }
    }
}
