/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PhysicsDeliveryWorkspaceBase.cs
수정일 : 2026-11-02

# 설명
2D·3D Physics Delivery에서 공유하는 NonAlloc 버퍼·Contact 수명과 재진입 차단을 소유한다.
물리 결과와 Collider 타입만 차원별 Workspace가 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// <br/>2D·3D 공통의 Physics 질의 버퍼와 Contact 결과 수명을 관리한다.
    /// <br/>같은 Workspace의 중첩 질의를 차단하며 포화 시 최대 용량까지만 증설한다.
    /// </summary>
    // ======================================================================
    public abstract class PhysicsDeliveryWorkspaceBase<TCollider, THit, TContact>
    where TCollider : UnityEngine.Component
    where THit : struct
    where TContact : struct
    {

    #region 버퍼

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 물리 후보 버퍼의 용량.
        /// </summary>
        // ------------------------------------------------------------
        public int Capacity => overlap.Length;

        // ------------------------------------------------------------
        /// <summary>
        /// 버퍼 증설을 허용하는 최대 용량.
        /// </summary>
        // ------------------------------------------------------------
        public int MaximumCapacity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Volume 질의에서 반환한 Contact 수.
        /// </summary>
        // ------------------------------------------------------------
        public int ContactCount { get; private set; }

        internal TCollider[] Overlap => overlap;
        private TCollider[] overlap;

        internal THit[] Cast => cast;
        private THit[] cast;

        private TContact[] contacts;
        private bool isQuerying;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 초기 용량과 최대 용량을 받아 재사용 물리 버퍼를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        protected PhysicsDeliveryWorkspaceBase(int capacity, int maximumCapacity)
        {
            if (capacity <= 0 || maximumCapacity < capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            MaximumCapacity = maximumCapacity;
            overlap = new TCollider[capacity];
            cast = new THit[capacity];
            contacts = new TContact[capacity];
        }

    #endregion

    #region Contact 수명

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>마지막 Volume 질의에서 수집한 Contact를 반환한다.
        /// <br/>다음 질의가 시작되면 이전 결과 인덱스는 무효화된다.
        /// </summary>
        // ------------------------------------------------------------
        public TContact GetContact(int index)
        {
            if (isQuerying)
            {
                throw new InvalidOperationException("진행 중인 Workspace 질의의 Contact는 읽을 수 없습니다.");
            }

            if (index < 0 || index >= ContactCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return contacts[index];
        }

        internal void AddContact(in TContact contact)
        {
            contacts[ContactCount++] = contact;
        }

        internal void ResetContacts()
        {
            ContactCount = 0;
        }

        internal void Begin()
        {
            if (isQuerying)
            {
                throw new InvalidOperationException($"동일 {GetType().Name}의 중첩 질의는 지원하지 않습니다.");
            }

            isQuerying = true;
            ContactCount = 0;
        }

        internal void End()
        {
            isQuerying = false;
        }

    #endregion

    #region 버퍼 확장

        // ------------------------------------------------------------
        /// <summary>
        /// 최대 용량 이내에서 모든 물리 후보·Contact 버퍼를 함께 증설한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryGrow()
        {
            if (Capacity >= MaximumCapacity)
            {
                return false;
            }

            var nextCapacity = Capacity <= MaximumCapacity / 2
                ? Capacity * 2
                : MaximumCapacity;
            Array.Resize(ref overlap, nextCapacity);
            Array.Resize(ref cast, nextCapacity);
            Array.Resize(ref contacts, nextCapacity);
            return true;
        }

    #endregion

    }
}
