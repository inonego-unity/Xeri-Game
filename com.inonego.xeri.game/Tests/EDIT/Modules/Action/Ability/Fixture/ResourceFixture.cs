/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ResourceFixture.cs
수정일 : 2026-11-19

# 설명
Owner별 자원 잔액과 원자적 소비를 제공하는 테스트 픽스처다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    // ============================================================
    /// <summary>
    /// 독립된 Owner의 자원 잔액을 관리한다.
    /// </summary>
    // ============================================================
    internal sealed class ResourceFixture
    {
        private readonly Dictionary<object, int> balances = new Dictionary<object, int>();

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 초기 잔액을 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Add(object owner, int amount)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            balances.Add(owner, amount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 현재 잔액을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Get(object owner) => balances[owner];

        // ------------------------------------------------------------
        /// <summary>
        /// 잔액이 충분할 때만 소비한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryDebit(object owner, int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            var balance = balances[owner];
            if (balance < amount) return false;
            balances[owner] = balance - amount;
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Owner의 잔액을 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Credit(object owner, int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            balances[owner] = checked(balances[owner] + amount);
        }
    }
}
