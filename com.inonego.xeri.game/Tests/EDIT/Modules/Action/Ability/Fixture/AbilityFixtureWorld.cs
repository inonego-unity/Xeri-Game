/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityFixtureWorld.cs
수정일 : 2026-11-17

# 설명
Ability Fixture에서 활성 소유자 교체와 결과 출처 기록을 재현한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    internal sealed class AbilityFixtureRoster
    {

    #region 소유자

        private readonly HashSet<ulong> owners;

        internal ulong ActiveOwner { get; private set; }

        internal AbilityFixtureRoster(ulong activeOwner, params ulong[] otherOwners)
        {
            owners = new HashSet<ulong>(otherOwners)
            {
                activeOwner,
            };
            ActiveOwner = activeOwner;
        }

        internal bool TrySelect(ulong owner)
        {
            if (!owners.Contains(owner)) return false;

            ActiveOwner = owner;

            return true;
        }

    #endregion

    }

    internal sealed class AbilityFixtureResults<TOrigin>
    {

    #region 결과 기록

        private readonly List<(TOrigin Origin, ulong Recipient, int Amount)> entries = new();

        internal IReadOnlyList<(TOrigin Origin, ulong Recipient, int Amount)> Entries { get; }

        internal AbilityFixtureResults()
        {
            Entries = entries.AsReadOnly();
        }

        internal void Record(TOrigin origin, ulong recipient, int amount)
        {
            entries.Add((origin, recipient, amount));
        }

    #endregion

    }
}
