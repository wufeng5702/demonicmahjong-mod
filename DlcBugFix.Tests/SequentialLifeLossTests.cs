using System;
using System.Collections;
using System.Collections.Generic;
using DlcBugFix;
using Xunit;

namespace DlcBugFix.Tests
{
    public class SequentialLifeLossTests
    {
        [Theory]
        [InlineData(1, 1, "E", false, 0, 2, "E")]
        [InlineData(1, 1, "EP", true, 1, 1, "EP")]
        [InlineData(1, 2, "EP", false, 0, 1, "EP")]
        [InlineData(1, 3, "EP", false, 0, 1, "EP")]
        [InlineData(1, 4, "EEEEE", true, 1, 6, "EEEEE")]
        [InlineData(2, 1, "E", true, 1, 3, "E")]
        [InlineData(3, 1, "EP", true, 1, 3, "")]
        [InlineData(1, 1, "", false, 0, 1, "")]
        [InlineData(1, 0, "E", true, 1, 2, "E")]
        public void Resolves_all_damage_with_revivals_between_zero_crossings(int hp, int swords,
            string items, bool alive, int expectedHp, int expectedMax, string consumed)
        {
            var world = new World(hp - 1 - swords, hp, items);
            var loss = new SequentialLifeLoss();
            Drain(loss.Resolve(() => world.Hp, world.SetHp, world.Damage, world.Revive, world.Finish));

            Assert.Equal(alive, world.Alive);
            Assert.Equal(expectedHp, world.Hp);
            Assert.Equal(expectedMax, world.MaxHp);
            Assert.Equal(consumed, world.Consumed);
            Assert.Equal(consumed.Split('E').Length - 1, world.ChuFeng);
            Assert.Equal(1, world.Completions);
            if (consumed.Contains('E')) Assert.Equal(world.MaxSoul, world.Soul);
            if (alive) Assert.Equal(0, loss.RemainingDamage);
        }

        [Fact]
        public void Waits_for_consumption_and_animation_before_next_sword_point()
        {
            var world = new World(-1, 1, "EP");
            Drain(new SequentialLifeLoss().Resolve(() => world.Hp, world.SetHp,
                world.Damage, world.Revive, world.Finish));
            Assert.Equal(new[]
            {
                "hp=0", "E.begin", "E.end", "damage:0", "P.begin", "P.end", "done:True"
            }, world.Events);
        }

        [Fact]
        public void Stops_if_a_reported_revival_does_not_restore_positive_hp()
        {
            var world = new World(-10, 1, "");
            int calls = 0;
            object NoProgress(Action<bool> done) { calls++; done(true); return null; }
            Drain(new SequentialLifeLoss().Resolve(() => world.Hp, world.SetHp,
                world.Damage, NoProgress, world.Finish));
            Assert.False(world.Alive);
            Assert.Equal(1, calls);
            Assert.Equal(1, world.Completions);
        }

        [Fact]
        public void Missing_completion_does_not_report_survival_or_spin()
        {
            var world = new World(-1, 1, "");
            Drain(new SequentialLifeLoss().Resolve(() => world.Hp, world.SetHp,
                world.Damage, _ => null, world.Finish));
            Assert.False(world.Alive);
            Assert.Equal(1, world.Completions);
        }

        [Fact]
        public void Does_not_overflow_on_the_lowest_stored_hp()
        {
            var world = new World(int.MinValue, 1, "");
            var loss = new SequentialLifeLoss();
            Drain(loss.Resolve(() => world.Hp, world.SetHp, world.Damage, world.Revive, world.Finish));
            Assert.Equal(2147483648L, loss.RemainingDamage);
            Assert.Equal(0, world.Hp);
            Assert.False(world.Alive);
        }

        [Fact]
        public void Uses_settled_hp_if_another_queued_effect_heals_after_a_failed_item_search()
        {
            int hp = -1;
            bool alive = false;
            object Heal(Action<bool> done) { hp = 2; done(false); return null; }
            Drain(new SequentialLifeLoss().Resolve(() => hp, n => hp = n, () => hp--,
                Heal, result => alive = result));
            Assert.True(alive);
            Assert.Equal(1, hp);
        }

        [Fact]
        public void A_larger_revival_absorbs_multiple_remaining_points_without_extra_consumption()
        {
            int hp = -2;
            int attempts = 0;
            bool alive = false;
            object Revive(Action<bool> done) { attempts++; hp = 4; done(true); return null; }
            Drain(new SequentialLifeLoss().Resolve(() => hp, n => hp = n, () => hp--,
                Revive, result => alive = result));
            Assert.True(alive);
            Assert.Equal(2, hp);
            Assert.Equal(1, attempts);
        }

        private static void Drain(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            int ticks = 0;
            try
            {
                while (stack.Count != 0)
                {
                    Assert.True(++ticks < 1000, "Resolution must terminate.");
                    var iterator = stack.Peek();
                    if (!iterator.MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (iterator.Current is IEnumerator child) stack.Push(child);
                }
            }
            finally { while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose(); }
        }

        private sealed class World
        {
            internal int Hp;
            internal int MaxHp;
            internal int Soul;
            internal readonly int MaxSoul = 10;
            internal string Consumed = "";
            internal int ChuFeng;
            internal int Completions;
            internal bool Alive;
            internal readonly List<string> Events = new List<string>();
            private readonly Queue<char> items;
            private bool animating;

            internal World(int hp, int maxHp, string items)
            {
                Hp = hp;
                MaxHp = maxHp;
                this.items = new Queue<char>(items);
            }
            internal void SetHp(int hp) { Hp = hp; Events.Add("hp=" + hp); }
            internal void Damage()
            {
                Assert.False(animating);
                Assert.True(Hp > 0);
                Hp--;
                Events.Add("damage:" + Hp);
            }
            internal object Revive(Action<bool> done) => Animation(done);
            private IEnumerator Animation(Action<bool> done)
            {
                Assert.Equal(0, Hp);
                Assert.False(animating);
                if (items.Count == 0) { done(false); yield break; }
                animating = true;
                char item = items.Dequeue();
                Events.Add(item + ".begin");
                if (item == 'E') { MaxHp++; Hp++; Soul = MaxSoul; }
                else { MaxHp = 1; Hp = 1; }
                done(true);
                yield return null;
                yield return null;
                Consumed += item;
                if (item == 'E') ChuFeng++;
                Events.Add(item + ".end");
                animating = false;
            }
            internal void Finish(bool alive)
            {
                Assert.False(animating);
                Completions++;
                Alive = alive;
                Events.Add("done:" + alive);
            }
        }
    }
}
