using UnityEngine;

namespace Coop.Networking
{
    /// <summary>
    /// Маркер точки спавна в игровой сцене.
    ///
    /// Точки не сериализуются в префаб координатора: координатор живёт в DontDestroyOnLoad
    /// и не может ссылаться на объекты сцены. Поэтому он ищет маркеры уже после загрузки
    /// игровой сцены — это стандартный способ связать «вечные» системы с содержимым сцены.
    /// </summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.65f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
            Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 1.5f);
        }
    }
}
