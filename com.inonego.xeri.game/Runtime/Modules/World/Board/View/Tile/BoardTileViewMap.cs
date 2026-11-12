/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : BoardTileViewMap.cs
수정일 : 2026-05-28

# 설명
Board vector와 tile view의 대응 관계만 관리하는 map.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Board vector에 대응하는 tile view를 저장하고 조회한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class BoardTileViewMap<TVector, TTileView>
    where TVector : struct
    where TTileView : MonoBehaviour
    {

    #region 필드

        private readonly Dictionary<TVector, TTileView> views = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 등록된 tile view map.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyDictionary<TVector, TTileView> Views => views;

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// vector에 tile view를 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Register(TVector vector, TTileView tile)
        {
            if (tile == null)
            {
                throw new ArgumentNullException(nameof(tile));
            }

            if (views.ContainsKey(vector))
            {
                throw new InvalidOperationException($"이미 동일 vector({vector})에 대응하는 tile view가 등록되어 있습니다.");
            }

            views.Add(vector, tile);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// vector에 대응하는 tile view 등록을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Unregister(TVector vector)
        {
            return views.Remove(vector);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// vector에 대응하는 tile view를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public TTileView Find(TVector vector)
        {
            return views.TryGetValue(vector, out var tile) ? tile : null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// vector에 대응하는 tile view를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGet(TVector vector, out TTileView tile)
        {
            return views.TryGetValue(vector, out tile);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// vector 등록 여부를 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Contains(TVector vector)
        {
            return views.ContainsKey(vector);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 등록을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Clear()
        {
            views.Clear();
        }

    #endregion

    }
}
