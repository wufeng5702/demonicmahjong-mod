using System;
using System.Collections;

namespace DlcBugFix
{
    // The game has already subtracted the whole defeat loss. A negative result
    // encodes the damage still owed AFTER reaching zero for the first time.
    internal sealed class SequentialLifeLoss
    {
        internal long RemainingDamage { get; private set; }

        internal IEnumerator Resolve(Func<int> readHp, Action<int> setHp, Action loseOneHp,
            Func<Action<bool>, object> revive, Action<bool> complete)
        {
            int hp = readHp();
            RemainingDamage = Math.Max(0L, -(long)hp);
            if (hp < 0) setHp(0);

            while (true)
            {
                hp = readHp();
                if (hp <= 0)
                {
                    bool called = false;
                    yield return revive(_ =>
                    {
                        if (called) return;
                        called = true;
                    });
                    // Await the entire attempt, including consumption/animation,
                    // even if its callback fired before its iterator ended.
                    if (!called || readHp() <= 0)
                    {
                        complete(false);
                        yield break;
                    }
                    continue;
                }

                if (RemainingDamage == 0)
                {
                    complete(true);
                    yield break;
                }

                loseOneHp();
                RemainingDamage--;
                yield return null;
            }
        }
    }
}
