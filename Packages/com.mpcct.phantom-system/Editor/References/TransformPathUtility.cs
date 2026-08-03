using System.Collections.Generic;
using UnityEngine;

namespace MPCCT.PhantomSystem.Editor
{
    public static class TransformPathUtility
    {
        public static string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
            {
                return null;
            }

            if (target == root)
            {
                return string.Empty;
            }

            var stack = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", stack) : null;
        }

        public static string SafeName(string value, string fallback = PhantomSlot.DefaultId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value.Trim();
        }
    }
}
