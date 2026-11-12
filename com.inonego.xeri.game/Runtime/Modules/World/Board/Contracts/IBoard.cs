/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IBoard.cs
수정일 : 2026-05-01

# 설명
보드를 나타내는 제네릭 인터페이스 정의.
IBoard2D — Vector2Int 기반 2D 보드.
IBoard3D — Vector3Int 기반 3D 보드.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 보드를 나타내는 인터페이스입니다.
    /// </summary>
    // ============================================================
    public interface IBoard<TVector, TIndex, TPlaceable> : IEnumerable<KeyValuePair<TVector, IBoardSpace<TIndex, TPlaceable>>>
    where TVector : struct where TIndex : struct
    where TPlaceable : class
    {

    #region 이벤트

        public event Action<TVector, TIndex, TPlaceable> OnPlace;
        public event Action<TVector, TIndex, TPlaceable> OnRemove;

        public event Action<TVector> OnAddSpace;
        public event Action<TVector> OnRemoveSpace;

    #endregion

    #region 공간

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 벡터에 있는 공간을 반환합니다.
        /// </summary>
        //------------------------------------------------------------
        public IBoardSpace<TIndex, TPlaceable> this[TVector vector] { get; }

    #endregion

    #region 객체

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 벡터와 인덱스에 있는 객체를 반환합니다.
        /// </summary>
        //------------------------------------------------------------
        public TPlaceable this[TVector vector, TIndex index] { get; }

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 포인트에 있는 객체를 반환합니다.
        /// </summary>
        //------------------------------------------------------------
        public TPlaceable this[IBoardPoint<TVector, TIndex> point] { get; }

    #endregion

    #region 좌표

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 객체의 위치를 반환합니다.
        /// </summary>
        //------------------------------------------------------------
        public IBoardPoint<TVector, TIndex> this[TPlaceable placeable] { get; }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 2D 보드를 나타내는 인터페이스입니다.
    /// </summary>
    // ============================================================
    public interface IBoard2D<TIndex, TPlaceable> : IBoard<Vector2Int, TIndex, TPlaceable>
    where TIndex : struct where TPlaceable : class
    {
        public int Width  { get; }
        public int Height { get; }

        public Vector2Int Size { get; }
    }

    // ============================================================
    /// <summary>
    /// 3D 보드를 나타내는 인터페이스입니다.
    /// </summary>
    // ============================================================
    public interface IBoard3D<TIndex, TPlaceable> : IBoard<Vector3Int, TIndex, TPlaceable>
    where TIndex : struct where TPlaceable : class
    {
        public int Width  { get; }
        public int Height { get; }
        public int Depth  { get; }

        public Vector3Int Size { get; }
    }
}
