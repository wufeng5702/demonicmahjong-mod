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
                    fontSize = 18,
                    padding = new RectOffset(10, 10, 6, 6)
                };
                _background = new Texture2D(1, 1);
                _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
                _background.Apply();
                _style.normal.background = _background;
                _style.normal.textColor = Color.white;
            }

            var size = _style.CalcSize(_content);
            const float margin = 12f;
            GUI.Label(new Rect(margin, Screen.height - size.y - margin, size.x, size.y), _content, _style);
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
        }
    }
}
