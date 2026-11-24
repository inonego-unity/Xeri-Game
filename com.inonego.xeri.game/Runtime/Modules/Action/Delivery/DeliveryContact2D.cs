/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : DeliveryContact2D.cs
수정일 : 2026-11-02

# 설명
2D Delivery가 확정한 raw Collider2D contact와 공간 정보를 보존한다.
Collider2D를 Hurtbox·Entity·Damage 대상으로 해석하지 않고 project adapter가 recipient 의미를 결정한다.
물리 Contact의 표면 기하 신뢰도와 Query의 결과 완전성을 분리한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Physics 결과를 전달 도메인의 접촉 지점과 방향 정보로 정규화한다.
    /// </summary>
    // ============================================================
    public readonly struct DeliveryContact2D
    {

        // ------------------------------------------------------------
        /// <summary>
        /// Unity Physics에서 접촉한 Collider2D를 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public Collider2D Recipient { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉 지점 또는 Overlap 근사 지점의 월드 위치.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Position { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉 법선 또는 Overlap에서 계산한 근사 법선.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Normal { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Delivery의 진행 방향.
        /// </summary>
        // ------------------------------------------------------------
        public Vector2 Direction { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Sweep 시작점으로부터 접촉까지의 거리.
        /// </summary>
        // ------------------------------------------------------------
        public float Distance { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Cast 표면 지점인지, Overlap 근사 지점인지 구별한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasSurfaceGeometry { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 Collider2D 참조가 있는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid => Recipient != null;

        // ------------------------------------------------------------
        /// <summary>
        /// Collider2D의 접촉 위치와 이동 방향을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContact2D
        (
            Collider2D recipient,
            Vector2 position,
            Vector2 normal,
            Vector2 direction,
            float distance,
            bool hasSurfaceGeometry = true
        )
        {
            Recipient = recipient;
            Position = position;
            Normal = normal;
            Direction = direction;
            Distance = Mathf.Max(0f, distance);
            HasSurfaceGeometry = hasSurfaceGeometry;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 다른 접촉 정보를 유지한 채 전달 진행 방향만 변경한 값을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContact2D WithDirection(Vector2 direction) => new(Recipient, Position, Normal, direction, Distance, HasSurfaceGeometry);

    }

    // ============================================================
    /// <summary>
    /// 단일 접촉의 존재 여부와 전체 후보에 대한 검색 완전성을 보존한다.
    /// </summary>
    // ============================================================
    public readonly struct DeliveryContactQueryResult2D
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 허용된 접촉 후보가 존재하는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasContact { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 후보 집합이 완전해 nearest나 부재를 확정할 수 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsComplete { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 존재가 확인된 접촉 값을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContact2D Contact { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉 존재와 검색 완전성을 별도로 보고한다.
        /// </summary>
        // ------------------------------------------------------------
        public DeliveryContactQueryResult2D
        (
            bool hasContact,
            bool isComplete,
            in DeliveryContact2D contact = default
        )
        {
            if (hasContact && !contact.IsValid)
            {
                throw new ArgumentException("접촉 성공 결과에는 유효한 Collider2D가 필요합니다.", nameof(contact));
            }

            HasContact = hasContact;
            IsComplete = isComplete;
            Contact = hasContact ? contact : default;
        }

    }

    // ============================================================
    /// <summary>
    /// 접촉 후보 Collider2D가 전달 가능한 대상인지 판정하는 확장 계약.
    /// </summary>
    // ============================================================
    public interface IDeliveryColliderFilter2D
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 전달 질의에서 해당 Collider2D의 접촉 허용 여부를 판정한다.
        /// </summary>
        // ------------------------------------------------------------
        bool IsAccepted(Collider2D collider);
    }
}