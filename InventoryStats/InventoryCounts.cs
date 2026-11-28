using System.Collections.Generic;
using System.Linq;
using MaJiang;
using MaJiang.DataConstruct;
using MaJiang.DataConstruct.BaoLingPai;
using MaJiang.DataConstruct.MaJiang;
using MaJiang.GM;
using MaJiang.PlayMaJiang;
using MaJiang.PlayMaJiang.Player.Relic;

namespace InventoryStats
{
    internal static class InventoryCounts
    {
        internal static (int Spirits, int NianSpirits, int Treasures, int NegativeTreasures, int Curses)
            Read(Saver saver, SceneHelper battle)
        {
            var battleBind = battle != null ? battle.playerPaiMianBind : null;
            var bindings = (battleBind != null ? battleBind._curList : null) ?? saver._workbenchPaiMianBindList;
            var spirits = new Dictionary<PaiMian, BaoLing>();
            var treasures = new Dictionary<PaiMian, BaoLing>();
            if (bindings != null)
            {
                for (int i = 0, count = bindings.Count; i < count; i++)
                {
                    var binding = bindings[i];
                    if (binding.LingId != BaoLing.None) spirits[binding.PaiMian] = binding.LingId;
                    if (binding.BaoId != BaoLing.None) treasures[binding.PaiMian] = binding.BaoId;
                }
            }

            // 对局中的永久改动在结算时写回牌库；临时附着不进入这两个记录。
            if (battleBind != null)
            {
                ApplyPending(spirits, battleBind.permanentLingRecord);
                ApplyPending(treasures, battleBind.permanentBaoRecord);
            }

            var data = GlobalDataCenter.Instance;
            var nianIds = new HashSet<BaoLing>();
            var negativeBaoIds = new HashSet<BaoLing>();
            var payloads = data != null ? data.MergedBaoLingPayloadList : null;
            if (payloads == null && battleBind != null)
            {
                var battlePayloads = battleBind._baoLingPrefabList;
                if (battlePayloads != null) payloads = battlePayloads.value;
            }
            if (payloads != null)
            {
                foreach (var payload in payloads)
                {
                    if (payload == null) continue;
                    var tags = payload.tags;
                    if (tags == null) continue;
                    if (tags.Contains(Tag.NianLing)) nianIds.Add(payload.id);
                    if (payload.baoLingType == BaoLingPayload.BaoLingType.BaoPai && tags.Contains(Tag.Negative))
                        negativeBaoIds.Add(payload.id);
                }
            }

            int curses = 0;
            var battleRelics = battle != null ? battle.playerRelicContainer : null;
            if (battleRelics != null)
            {
                for (int i = 0, count = battleRelics.Count; i < count; i++)
                    if (IsCursed(battleRelics[i])) curses++;
            }
            else
            {
                var owned = saver.RelicSaveDatas;
                if (owned != null && data != null)
                {
                    for (int i = 0, count = owned.Count; i < count; i++)
                    {
                        var relic = owned[i];
                        if (relic != null && IsCursed(data.TryGetRelicDisplay(relic.DisplayId))) curses++;
                    }
                }
            }

            return (spirits.Count, spirits.Values.Count(nianIds.Contains),
                treasures.Count, treasures.Values.Count(negativeBaoIds.Contains), curses);
        }

        private static void ApplyPending(Dictionary<PaiMian, BaoLing> tiles,
            Il2CppSystem.Collections.Generic.Dictionary<PaiMian, BaoLingPayload> changes)
        {
            if (changes == null) return;
            foreach (var change in changes)
            {
                var payload = change.Value;
                if (payload != null && payload.id != BaoLing.None) tiles[change.Key] = payload.id;
                else tiles.Remove(change.Key);
            }
        }

        private static bool IsCursed(RelicDisplay relic)
        {
            if (relic == null) return false;
            var tags = relic.tags;
            return tags != null && tags.Contains(Tag.Curse);
        }
    }
}
