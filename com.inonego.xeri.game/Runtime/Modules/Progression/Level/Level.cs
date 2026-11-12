/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : Level.cs
수정일 : 2026-05-01

# 설명
고정된 최대 레벨을 갖는 기본 레벨 클래스.
레벨은 0부터 시작하며, 최대 레벨은 생성자에서 설정된 값입니다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // =======================================================================================
    /// <summary>
    /// <br/>게임에서 레벨을 관리하기 위한 기본 클래스입니다.
    /// <br/>레벨은 0부터 시작하며, 최대 레벨은 생성자에서 설정된 값입니다.
    /// </summary>
    // =======================================================================================
    [Serializable]
    public partial class Level : LevelBase
    {
        [SerializeField]
        protected int lFullMax = 0;

        public override int FullMax => lFullMax;

        private Level() {}

        public Level(int lFullMax)
        {
            if (lFullMax < 0)
            {
                throw new InvalidMaxLevelException();
            }

            this.lFullMax = lFullMax;

            Reset();
        }
    }
}
