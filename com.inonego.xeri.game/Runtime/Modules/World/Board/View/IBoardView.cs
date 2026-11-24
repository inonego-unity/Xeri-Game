/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IBoardView.cs
수정일 : 2026-06-17

# 설명
Board 모델을 Unity view로 표현하기 위한 인터페이스.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Board 모델을 Unity view로 표현하는 인터페이스.
    /// </summary>
    // ============================================================
    public interface IBoardView<TBoard, TVector, TIndex, TSpace, TPlaceable, TTileView> : IBindable<TBoard>
    where TBoard : class, IBoard<TVector, TIndex, TPlaceable>
    where TVector : struct
    where TIndex : struct
    where TSpace : class, IBoardSpace<TIndex, TPlaceable>
    where TPlaceable : class
    where TTileView : MonoBehaviour
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 바인딩된 Board 모델.
        /// </summary>
        // ------------------------------------------------------------
        TBoard Board { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Board vector에 배치된 tile view map.
        /// </summary>
        // ------------------------------------------------------------
        IReadOnlyDictionary<TVector, TTileView> TileMap { get; }

        Vector3 ToLocalPos(TVector vector);
        Vector3 ToLocalPos(TVector vector, TIndex index);
        Vector3 ToLocalPos(IBoardPoint<TVector, TIndex> point);

        Vector3 ToWorldPos(TVector vector);
        Vector3 ToWorldPos(TVector vector, TIndex index);
        Vector3 ToWorldPos(IBoardPoint<TVector, TIndex> point);

        bool TryGetTile(TVector vector, out TTileView tileView);
        void ReloadTileMap();
    }
}
