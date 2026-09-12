using UnityEngine;

namespace Shared
{
    public static class TransformPath
    {
        /// <summary>构建 Transform 的完整路径（用 / 分隔），最多 maxDepth 层。</summary>
        public static string GoPath(Transform t, int maxDepth = 7)
        {
            if (t == null) return "";
            var names = new System.Collections.Generic.List<string>();
            while (t != null && names.Count < maxDepth)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
