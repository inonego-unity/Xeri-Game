/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : BoardTileFactory.cs
수정일 : 2026-05-28

# 설명
Tile view GameObject 획득/회수와 위치 적용을 담당한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Board tile view를 생성하고 회수한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public class BoardTileFactory<TTileView>
    where TTileView : MonoBehaviour
    {

    #region 필드

        private readonly IGameObjectProvider provider = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// GameObject provider를 주입받는다.
        /// </summary>
        // ------------------------------------------------------------
        public BoardTileFactory(IGameObjectProvider provider) : base()
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// localPosition에 tile view를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual TTileView CreateTile(Vector3 localPosition)
        {
            var go = provider.Acquire(worldPositionStays: false);

            if (go == null)
            {
                throw new InvalidOperationException("Tile view GameObject를 가져올 수 없습니다.");
            }

            if (!go.TryGetComponent(out TTileView tile))
            {
                provider.Release(go, worldPositionStays: false);

                throw new NullReferenceException($"게임 오브젝트에서 tile view 컴포넌트({typeof(TTileView).Name})를 찾을 수 없습니다.");
            }

            go.transform.localPosition = localPosition;
            go.SetActive(true);

            return tile;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// tile view를 회수한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void ReleaseTile(TTileView tile)
        {
            if (tile == null)
            {
                return;
            }

            var go = tile.gameObject;

            go.transform.localPosition = Vector3.zero;
            go.SetActive(false);

            provider.Release(go, worldPositionStays: false);
        }

    #endregion

    }
}
