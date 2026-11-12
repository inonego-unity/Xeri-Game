/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : BoardSpaceBase.cs
수정일 : 2026-05-01

# 설명
보드 공간의 기본 추상 클래스.
XDictionary_VR<TIndex, TPlaceable>로 배치된 객체를 관리하며 IEnumerable을 구현한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Serializable; // inonego.Xeri.Serializable

    // ============================================================
    /// <summary>
    /// 보드 공간의 기본 클래스입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class BoardSpaceBase<TIndex, TPlaceable> : IBoardSpace<TIndex, TPlaceable>, IEnumerable<KeyValuePair<TIndex, TPlaceable>>, IEnumerable
    where TIndex : struct
    where TPlaceable : class
    {

    #region 필드

        [SerializeField]
        protected XDictionary_VR<TIndex, TPlaceable> placeableMap = new();

    #endregion

    #region 인터페이스 구현

        IEnumerator<KeyValuePair<TIndex, TPlaceable>> IEnumerable<KeyValuePair<TIndex, TPlaceable>>.GetEnumerator()
        {
            foreach (var (index, placeable) in placeableMap)
            {
                yield return new KeyValuePair<TIndex, TPlaceable>(index, placeable);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => placeableMap.GetEnumerator();

    #endregion

    #region 메서드

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 인덱스에 있는 객체를 반환합니다.
        /// </summary>
        //------------------------------------------------------------
        public TPlaceable this[TIndex index]
        {
            get => placeableMap.TryGetValue(index, out var placeable) ? placeable : null;
        }

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 인덱스에 객체를 배치할 수 있는지 확인합니다.
        /// </summary>
        //------------------------------------------------------------
        public bool CanPlace(TIndex index, TPlaceable placeable = null)
        {
            // 비어있거나 같은 객체라면 배치할 수 있습니다.
            return !placeableMap.ContainsKey(index) || this[index] == placeable;
        }

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 인덱스에 객체를 배치합니다.
        /// </summary>
        //------------------------------------------------------------
        public void Place(TIndex index, TPlaceable placeable)
        {
            if (placeable == null)
            {
                throw new ArgumentNullException("배치할 객체를 설정해주세요.");
            }

            if (!CanPlace(index, placeable))
            {
                throw new InvalidOperationException("이미 해당 위치에 객체가 존재합니다. Remove() 메서드를 사용하여 제거 후 다시 배치해주세요.");
            }

            placeableMap[index] = placeable;
        }

        //------------------------------------------------------------
        /// <summary>
        /// 지정된 인덱스의 객체를 제거합니다.
        /// </summary>
        //------------------------------------------------------------
        public bool Remove(TIndex index)
        {
            return placeableMap.Remove(index);
        }

        //------------------------------------------------------------
        /// <summary>
        /// 모든 객체를 제거합니다.
        /// </summary>
        //------------------------------------------------------------
        public void RemoveAll()
        {
            placeableMap.Clear();
        }

    #endregion

    }
}
