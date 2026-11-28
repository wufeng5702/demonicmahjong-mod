using System;
using BepInEx.Logging;
using MaJiang.GameMap;
using MaJiang.GM;
using MaJiang.PlayMaJiang;
using MaJiang.UI.Bag;
using UnityEngine;

namespace InventoryStats
{
    public class InventoryHud : MonoBehaviour
    {
        internal static ManualLogSource Log;
        internal static bool AlwaysShow;
        internal static float YOffset;
        internal static int FontSize;

        private const float PollInterval = 0.25f;

        private float _nextPoll;
        private GUIContent _content;
        private bool _errorLogged;
        private GUIStyle _style;
        private Texture2D _background;

        private void Update()
        {
            try
            {
                var battle = SceneHelper.Instance;
                if (battle != null && !battle.isActiveAndEnabled) battle = null;
                bool visible = AlwaysShow ? battle != null || GameMapMgr.IsGameState : IsBagOpen(battle);
                if (!visible)
                {
                    _content = null;
                    _nextPoll = 0f;
                    return;
                }

                float now = Time.unscaledTime;
                if (now < _nextPoll) return;
                _nextPoll = now + PollInterval;

                var manager = GameManager.Instance;
                var saver = manager != null ? manager.Saver : null;
                if (saver == null)
                {
                    _content = null;
                    return;
                }

                var (spirits, nianSpirits, treasures, negativeTreasures, curses) = InventoryCounts.Read(saver, battle);
                _content ??= new GUIContent();
                _content.text = $"念灵＋牌灵：{spirits}({nianSpirits})\n宝牌＋负面宝牌：{treasures}({negativeTreasures})\n诅咒遗物：{curses}";
            }
            catch (Exception e)
            {
                _content = null;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log?.LogWarning("InventoryStats refresh failed: " + e);
                }
            }
        }

        private static bool IsBagOpen(SceneHelper battle)
        {
            var mapBag = BagFramework.Instance;
            if (mapBag != null && mapBag.isActiveAndEnabled) return true;

            var battleUi = battle != null ? battle.bagAndIllustrationCreate : null;
            var battleBag = battleUi != null ? battleUi._bagPanelCtr : null;
            return battleBag != null && battleBag.isActiveAndEnabled;
        }

        private void OnGUI()
        {
            if (_content == null || Event.current.type != EventType.Repaint) return;

            if (_style == null)
            {
                _style = new GUIStyle
                {
                    fontSize = FontSize,
                    wordWrap = false,
                    padding = new RectOffset(10, 10, 6, 6)
                };
                _background = new Texture2D(1, 1);
                _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
                _background.Apply();
                _style.normal.background = _background;
                _style.normal.textColor = Color.white;
            }

            const float margin = 12f;
            // 统计固定为三行，测量和绘制都使用不自动换行的尺寸。
            var size = _style.CalcSize(_content);
            float width = Mathf.Ceil(size.x);
            float height = Mathf.Ceil(size.y);
            float availableWidth = Mathf.Max(1f, Screen.width - 2f * margin);
            float availableHeight = Mathf.Max(1f, Screen.height - 2f * margin);
            float scale = Mathf.Min(1f, Mathf.Min(availableWidth / width, availableHeight / height));
            float bottom = Mathf.Max(margin, Screen.height - height * scale - margin);
            float y = Mathf.Lerp(bottom, margin, YOffset);
            var bounds = new Rect(margin, y, width, height);
            if (scale == 1f)
            {
                GUI.Label(bounds, _content, _style);
                return;
            }

            var matrix = GUI.matrix;
            try
            {
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(margin, y));
                GUI.Label(bounds, _content, _style);
            }
            finally { GUI.matrix = matrix; }
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
        }
    }
}
