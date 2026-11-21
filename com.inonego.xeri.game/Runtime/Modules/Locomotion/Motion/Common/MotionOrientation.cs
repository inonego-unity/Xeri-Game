/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionOrientation.cs
수정일 : 2026-11-22

# 설명
Up 축이 바뀔 때 Body 자세를 새 Up에 맞추는 각속도를 계산하는 순수 함수를 정의한다.

# 특이사항, 제약사항
Motion Model은 자세를 모른다. 사용처가 이 값을 Rotation 채널 Modifier로 넣는다.
Body 회전은 Unity 규칙(로컬 +Y가 Up, +Z가 Forward)으로 해석한다. 축이 다른 모델은 사용처가 오프셋 회전을 곱해 넘긴다.
두 축 사이의 최소 회전이라 Up에 수직인 방향(Forward)은 새 평면으로 옮겨져 유지된다.
정반대 Up은 Forward를 축으로 반 바퀴 돌아 Up만 뒤집고 바라보던 방향은 유지한다.
회전은 정규화해 쓰고, 1µs보다 짧은 delta는 쓸 수 없는 각속도를 만들므로 예외로 거절한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Primitive;

namespace inonego.Xeri.Game.Motion
{
    // ============================================================
    /// <summary>
    /// Body 자세를 Up 축에 맞추는 계산 도구.
    /// </summary>
    // ============================================================
    public static class MotionOrientation
    {

    #region 내부 데이터

        private const float PARALLEL_EPSILON = 1e-6f;

        // 이보다 짧은 delta는 반 바퀴만 돌아도 쓸 수 없는 크기의 각속도가 된다.
        private const float MIN_DELTA = 1e-6f;

    #endregion

    #region 정렬

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> Body의 Up(rotation의 로컬 +Y)이 delta초 뒤 targetUp을 향하도록 돌리는
        /// <br/> 각속도(rad/s)를 구한다.
        /// <br/> 이미 같은 방향이면 0이다.
        /// <br/> 정반대면 Body의 Forward(로컬 +Z)를 축으로 돌아 방향을 유지한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public static Vector3 AlignUp(Quaternion rotation, Vector3 targetUp, float delta)
        {
            if (!delta.IsFinite() || delta < MIN_DELTA)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }

            if (!rotation.TryNormalize(out var unitRotation))
            {
                throw new ArgumentOutOfRangeException(nameof(rotation));
            }

            if (!targetUp.TryNormalize(out var target))
            {
                throw new ArgumentOutOfRangeException(nameof(targetUp));
            }

            return Resolve(unitRotation, target) / delta;
        }

        // 현재 Up을 목표 Up으로 돌리는 회전 벡터(축 × 라디안)를 구한다.
        private static Vector3 Resolve(Quaternion rotation, Vector3 target)
        {
            var current = (rotation * Vector3.up).normalized;
            var cross = Vector3.Cross(current, target);
            var sin = cross.magnitude;
            var cos = Vector3.Dot(current, target);

            if (sin > PARALLEL_EPSILON)
            {
                return cross / sin * Mathf.Atan2(sin, cos);
            }

            if (cos > 0f)
            {
                return Vector3.zero;
            }

            // 정반대면 회전축이 정해지지 않는다. Up과 늘 수직인 Forward를 축으로 돌아 바라보던 방향을 유지한다.
            return (rotation * Vector3.forward).normalized * Mathf.PI;
        }

    #endregion

    }
}
