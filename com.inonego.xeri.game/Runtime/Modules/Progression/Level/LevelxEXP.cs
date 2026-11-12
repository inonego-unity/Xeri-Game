/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : LevelxEXP.cs
수정일 : 2026-05-01

# 설명
경험치 기반 레벨 클래스. EXP 설정 시 자동으로 레벨업을 처리하며,
오버플로우 경험치를 다음 레벨로 이월한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // =======================================================================================
    /// <summary>
    /// <br/>게임에서 레벨과 경험치를 관리하기 위한 클래스입니다.
    /// <br/>레벨은 0부터 시작하며, 최대 레벨은 ['레벨 업에 필요한 경험치' 목록의 개수] 입니다.
    /// </summary>
    // =======================================================================================
    [Serializable]
    public partial class LevelxEXP : LevelBase, ILevel, IReadOnlyLevel
    {

    #region 필드

        public override int Value
        {
            get => base.Value;
            set
            {
                base.Value = value;

                this.exp = 0;
            }
        }

        public override int FullMax => RequiredEXPToLevelUpArray.Count;

        [SerializeField]
        private int exp = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 경험치입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int EXP
        {
            get => exp;
            set
            {
                int newEXP = value;

                if (newEXP < 0)
                {
                    throw new InvalidEXPException();
                }

                // 레벨업 처리
                while (CanLevelUp)
                {
                    if (MaxEXP < 0)
                    {
                        throw new InvalidMaxEXPException();
                    }

                    // 최대 EXP보다 작으면 레벨업 처리 종료
                    if (newEXP < MaxEXP) break;

                    newEXP -= MaxEXP;

                    LevelUp();
                }

                // EXP 마지막에 업데이트
                this.exp = Mathf.Min(newEXP, MaxEXP);
            }
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// <br/>레벨업하는데 필요한 경험치 목록입니다.
        /// <br/>i번째 인덱스의 값은 레벨 (i)에서 레벨 (i + 1)로 넘어가기 위해 필요한 경험치를 의미합니다.
        /// </summary>
        // ------------------------------------------------------------------------------------------
        [SerializeField, ReadOnly]
        private int[] requiredEXPToLevelUpArray;
        public IReadOnlyList<int> RequiredEXPToLevelUpArray => requiredEXPToLevelUpArray;

        // ------------------------------------------------------------
        /// <summary>
        /// 해당 레벨의 필요 경험치를 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public int GetRequiredEXPToLevelUp(int level) => Min <= level && level <= Max - 1 ? RequiredEXPToLevelUpArray[level] : 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 레벨의 최대 경험치를 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public int MaxEXP => GetRequiredEXPToLevelUp(Value);

    #endregion

    #region 생성자

        private LevelxEXP() {}

        public LevelxEXP(int[] requiredEXPToLevelUpArray)
        {
            // 경험치 테이블 유효성 검사
            if (requiredEXPToLevelUpArray == null)
            {
                throw new NullEXPTableException();
            }

            for (int i = 0; i < requiredEXPToLevelUpArray.Length; i++)
            {
                if (requiredEXPToLevelUpArray[i] < 0)
                {
                    throw new InvalidEXPTableException();
                }
            }

            this.requiredEXPToLevelUpArray = requiredEXPToLevelUpArray;

            Reset();
        }

    #endregion

    }
}
