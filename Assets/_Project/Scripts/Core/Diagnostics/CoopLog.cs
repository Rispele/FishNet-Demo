using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Coop.Core.Diagnostics
{
    /// <summary>
    /// Тонкая обёртка над UnityEngine.Debug.
    ///
    /// Зачем она нужна:
    /// 1) единый формат префикса — по логу сразу видно, какая подсистема его написала;
    /// 2) вызовы Info/Verbose помечены <see cref="ConditionalAttribute"/>, поэтому в релизном
    ///    билде они вырезаются компилятором вместе с аргументами (включая склейку строк),
    ///    т.е. не стоят ни одного такта и не создают мусор для GC;
    /// 3) единая точка, куда позже можно подключить файловый лог/телеметрию.
    /// </summary>
    public static class CoopLog
    {
        // DEBUG определён в редакторе и в development-билдах, но не в релизном билде —
        // ровно то поведение, которое нужно от диагностического лога.
        [Conditional("UNITY_EDITOR"), Conditional("DEBUG")]
        public static void Info(string context, string message, Object owner = null)
            => Debug.Log(Format(context, message), owner);

        public static void Warning(string context, string message, Object owner = null)
            => Debug.LogWarning(Format(context, message), owner);

        public static void Error(string context, string message, Object owner = null)
            => Debug.LogError(Format(context, message), owner);

        public static void Exception(string context, System.Exception exception, Object owner = null)
        {
            Debug.LogError(Format(context, exception.Message), owner);
            Debug.LogException(exception, owner);
        }

        private static string Format(string context, string message) => $"[{context}] {message}";
    }
}
