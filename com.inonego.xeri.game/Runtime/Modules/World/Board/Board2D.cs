/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : Board2D.cs
수정일 : 2026-05-01

# 설명
2D 크기(Width, Height) 범위 내에서 동작하는 보드.
생성자에서 크기를 지정하며 init = true이면 유효 좌표 전체에 공간을 자동 생성한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 2D 크기(Width, Height) 범위 내에서 동작하는 보드입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public class Board2D<TPlaceable> : Board2D<int, BoardSpace<TPlaceable>, TPlaceable>
    where TPlaceable : class
    {
        public Board2D(int width, int height, bool init = true) : base(width, height, init) {}
    }

    // ============================================================
    /// <summary>
    /// 2D 크기(Width, Height) 범위 내에서 동작하는 보드입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public class Board2D<TIndex, TSpace, TPlaceable> :
    BoardBase<Vector2Int, TIndex, TSpace, TPlaceable>, IBoard2D<TIndex, TPlaceable>
    where TIndex : struct
    where TSpace : BoardSpaceBase<TIndex, TPlaceable>, new()
    where TPlaceable : class
    {
        [SerializeField]
        protected int width;

        [SerializeField]
        protected int height;

        // ------------------------------------------------------------
        /// <summary>
        /// 보드의 가로 크기입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int Width => width;

        // ------------------------------------------------------------
        /// <summary>
        /// 보드의 세로 크기입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int Height => height;

        // ------------------------------------------------------------
        /// <summary>
        /// 보드의 크기입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2Int Size => new Vector2Int(width, height);

        // ------------------------------------------------------------
        /// <summary>
        /// 벡터가 유효한 보드 범위 내에 있는지 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected override bool IsValidVector(Vector2Int vector)
        {
            return 0 <= vector.x && vector.x < width && 0 <= vector.y && vector.y < height;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 생성자에서 크기를 지정하고 공간을 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public Board2D(int width, int height, bool init = true)
        {
            this.width = Math.Max(0, width);
            this.height = Math.Max(0, height);

            if (init)
            {
                Init();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 보드의 모든 유효 좌표에 대해 공간을 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected void Init()
        {
            spaceMap.Clear();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    AddSpace(new Vector2Int(x, y));
                }
            }
        }
    }
}
