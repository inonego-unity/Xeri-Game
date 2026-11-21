/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionFeedback.cs
수정일 : 2026-11-22

# 설명
직전 Physics Step의 관측(회전·접지·차단 법선)을 다음 Tick의 값 계산에 넘기는 값을 정의한다.

# 특이사항, 제약사항
Host가 Contact·지면·Body 상태에서 채운다. 차단 법선은 정규화해 최대 4개까지 보관한다.
Motion Model은 이 값을 읽지 않는다. 사용처가 Track 평가(Rotation)와 벽 방향 성분 제거(RemoveBlocked)에 쓴다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// <br/> 직전 Physics Step 관측을 다음 Tick의 값 계산에 넘기는 값.
    /// <br/> Host가 Contact·지면·Body 상태에서 채운다.
    /// </summary>
    // ============================================================
    public readonly struct MotionFeedback
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Feedback이 담는 차단 법선의 최대 개수.
        /// </summary>
        // ------------------------------------------------------------
        public const int MAX_BLOCKING_NORMALS = 4;

        // ------------------------------------------------------------
        /// <summary>
        /// 관측 정보가 없는 기본 Feedback.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionFeedback None => new(Quaternion.identity, false);

        // ------------------------------------------------------------
        /// <summary>
        /// Body의 현재 회전. Track 변위를 월드로 바꿀 때 쓴다.
        /// </summary>
        // ------------------------------------------------------------
        public Quaternion Rotation => hasRotation ? rotation : Quaternion.identity;

        private readonly Quaternion rotation;
        private readonly bool hasRotation;

        // ------------------------------------------------------------
        /// <summary>
        /// Body가 지면에 닿아 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsGrounded { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 담긴 차단 법선 개수.
        /// </summary>
        // ------------------------------------------------------------
        public int BlockingNormalCount { get; }

        private readonly Vector3 normal0;
        private readonly Vector3 normal1;
        private readonly Vector3 normal2;
        private readonly Vector3 normal3;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 회전과 접지 상태로 Feedback을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionFeedback(Quaternion rotation, bool isGrounded)
        {
            if (!rotation.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(rotation));
            }

            this.rotation = rotation;
            hasRotation = true;
            IsGrounded = isGrounded;
            BlockingNormalCount = 0;
            normal0 = normal1 = normal2 = normal3 = Vector3.zero;
        }

        private MotionFeedback
        (
            in MotionFeedback source, int count,
            Vector3 normal0, Vector3 normal1, Vector3 normal2, Vector3 normal3
        )
        {
            rotation = source.rotation;
            hasRotation = source.hasRotation;
            IsGrounded = source.IsGrounded;
            BlockingNormalCount = count;
            this.normal0 = normal0;
            this.normal1 = normal1;
            this.normal2 = normal2;
            this.normal3 = normal3;
        }

    #endregion

    #region 차단 법선

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 차단 법선을 하나 추가한 사본을 만든다.
        /// <br/> 유한하지 않거나 길이가 0이거나 가득 찼으면 원본을 그대로 돌려준다.
        /// </summary>
        // ----------------------------------------------------------------------
        public MotionFeedback WithBlockingNormal(Vector3 normal)
        {
            if (BlockingNormalCount >= MAX_BLOCKING_NORMALS || !normal.TryNormalize(out var unit))
            {
                return this;
            }

            return BlockingNormalCount switch
            {
                0 => new MotionFeedback(this, 1, unit, normal1, normal2, normal3),
                1 => new MotionFeedback(this, 2, normal0, unit, normal2, normal3),
                2 => new MotionFeedback(this, 3, normal0, normal1, unit, normal3),
                _ => new MotionFeedback(this, 4, normal0, normal1, normal2, unit)
            };
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 위치의 정규화된 차단 법선을 읽는다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 GetBlockingNormal(int index)
        {
            if (index < 0 || index >= BlockingNormalCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return index switch
            {
                0 => normal0,
                1 => normal1,
                2 => normal2,
                _ => normal3
            };
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 속도에서 차단 법선 쪽(벽 안쪽)으로 향하는 성분을 법선마다 뺀다.
        /// <br/> 결과를 사용처 값으로 보관하면 벽에서 떨어져도 제거한 성분이 되살아나지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public Vector3 RemoveBlocked(Vector3 velocity)
        {
            for (var index = 0; index < BlockingNormalCount; index++)
            {
                var normal = GetBlockingNormal(index);
                var into = Vector3.Dot(velocity, normal);

                if (into < 0f)
                {
                    velocity -= normal * into;
                }
            }

            return velocity;
        }

    #endregion

    }
}
