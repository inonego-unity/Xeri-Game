/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : BoardViewBase.cs
수정일 : 2026-06-17

# 설명
Board 모델을 Unity에서 표현하는 view host.
Board 바인딩, 좌표 변환, tile view hook 표면 API를 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Board 모델을 Unity view로 표현하기 위한 베이스 클래스.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class BoardViewBase<TBoard, TVector, TIndex, TSpace, TPlaceable, TTileView>
        : MonoBehaviour,
          IBoardView<TBoard, TVector, TIndex, TSpace, TPlaceable, TTileView>
    where TBoard : class, IBoard<TVector, TIndex, TPlaceable>
    where TVector : struct
    where TIndex : struct
    where TSpace : class, IBoardSpace<TIndex, TPlaceable>
    where TPlaceable : class
    where TTileView : MonoBehaviour
    {

    #region 필드

        [SerializeReference, HideInInspector]
        private TBoard board = null;

        [SerializeReference]
        private IGameObjectProvider lTileProvider = new PrefabGameObjectProvider();

        private BoardTileFactory<TTileView> lTileFactory = null;
        private BoardTileViewMap<TVector, TTileView> lTileMap = null;
        private BoardViewBinder<TBoard, TVector, TIndex, TSpace, TPlaceable, TTileView> binder = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 바인딩된 Board 모델.
        /// </summary>
        // ------------------------------------------------------------
        public TBoard Board => board;

        // ------------------------------------------------------------
        /// <summary>
        /// tile view GameObject provider.
        /// </summary>
        // ------------------------------------------------------------
        public IGameObjectProvider TileProvider
        {
            get => lTileProvider;
            set
            {
                lTileProvider = value ?? throw new ArgumentNullException(nameof(value));
                lTileFactory  = null;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Board vector에 배치된 tile view map.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyDictionary<TVector, TTileView> TileMap => TileViewMap.Views;

        private BoardTileViewMap<TVector, TTileView> TileViewMap
        {
            get
            {
                lTileMap ??= new BoardTileViewMap<TVector, TTileView>();

                return lTileMap;
            }
        }

        private BoardTileFactory<TTileView> TileFactory
        {
            get
            {
                lTileFactory ??= CreateTileFactory();

                return lTileFactory;
            }
        }

        private BoardViewBinder<TBoard, TVector, TIndex, TSpace, TPlaceable, TTileView> Binder
        {
            get
            {
                binder ??= new BoardViewBinder<TBoard, TVector, TIndex, TSpace, TPlaceable, TTileView>
                (
                    this,
                    TileFactory,
                    TileViewMap
                );

                return binder;
            }
        }

    #endregion

    #region 바인딩

        // ------------------------------------------------------------
        /// <summary>
        /// Board 모델에 바인딩한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Bind(TBoard board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (this.board != null)
            {
                Unbind();
            }

            this.board = board;

            Binder.Bind(board);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Board 모델 바인딩을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Unbind()
        {
            Binder.Unbind();

            board = null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Board 상태 기준으로 tile view map을 다시 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ReloadTileMap()
        {
            if (board == null)
            {
                throw new InvalidOperationException("보드가 초기화되지 않았습니다. Bind()를 먼저 호출해주세요.");
            }

            Binder.ReloadTileMap();
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// vector에 대응하는 tile view를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetTile(TVector vector, out TTileView tileView)
        {
            return TileViewMap.TryGet(vector, out tileView);
        }

    #endregion

    #region 좌표 변환

        public abstract Vector3 ToLocalPos(TVector vector);
        public abstract Vector3 ToLocalPos(TVector vector, TIndex index);

        // ------------------------------------------------------------
        /// <summary>
        /// Board point를 로컬 좌표로 변환한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 ToLocalPos(IBoardPoint<TVector, TIndex> point)
        {
            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }

            return ToLocalPos(point.Vector, point.Index);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Board vector를 월드 좌표로 변환한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 ToWorldPos(TVector vector)
        {
            return GetTileParent().TransformPoint(ToLocalPos(vector));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Board vector/index를 월드 좌표로 변환한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 ToWorldPos(TVector vector, TIndex index)
        {
            return GetTileParent().TransformPoint(ToLocalPos(vector, index));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Board point를 월드 좌표로 변환한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 ToWorldPos(IBoardPoint<TVector, TIndex> point)
        {
            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }

            return ToWorldPos(point.Vector, point.Index);
        }

        private Transform GetTileParent()
        {
            return lTileProvider?.Parent != null ? lTileProvider.Parent : transform;
        }

    #endregion

    #region Factory

        // ------------------------------------------------------------
        /// <summary>
        /// tile view factory를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual BoardTileFactory<TTileView> CreateTileFactory()
        {
            return new BoardTileFactory<TTileView>(TileProvider);
        }

    #endregion

    #region 확장 Hook

        // ------------------------------------------------------------
        /// <summary>
        /// 지정된 space에 tile view를 배치할 수 있는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual bool CanPlaceTile(TVector vector, TSpace space) => true;

        // ------------------------------------------------------------
        /// <summary>
        /// tile view 배치 직전에 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnPrePlaceTile(TVector vector, TSpace space) {}

        // ------------------------------------------------------------
        /// <summary>
        /// tile view 배치 직후 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnPlaceTile(TVector vector, TSpace space, TTileView tileView) {}

        // ------------------------------------------------------------
        /// <summary>
        /// tile view 제거 직전에 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnPreRemoveTile(TVector vector, TTileView tileView) {}

        // ------------------------------------------------------------
        /// <summary>
        /// tile view 제거 직후 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void OnRemoveTile(TVector vector) {}

    #endregion

    #region Binder 호출 슬롯

        internal bool CanPlaceTileFromBinder(TVector vector, TSpace space)
        {
            return CanPlaceTile(vector, space);
        }

        internal void InvokePrePlaceTile(TVector vector, TSpace space)
        {
            OnPrePlaceTile(vector, space);
        }

        internal void InvokePlaceTile(TVector vector, TSpace space, TTileView tileView)
        {
            OnPlaceTile(vector, space, tileView);
        }

        internal void InvokePreRemoveTile(TVector vector, TTileView tileView)
        {
            OnPreRemoveTile(vector, tileView);
        }

        internal void InvokeRemoveTile(TVector vector)
        {
            OnRemoveTile(vector);
        }

    #endregion

    }
}
