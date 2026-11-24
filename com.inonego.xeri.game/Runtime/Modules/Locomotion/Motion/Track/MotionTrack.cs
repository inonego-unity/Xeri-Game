/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionTrack.cs
수정일 : 2026-11-22

# 설명
루트 모션처럼 시간에 따른 Actor 기준 변위를 담는 불변 Track 데이터와 구간 속도 평가를 정의한다.

# 특이사항, 제약사항
Motion Model은 Track을 모른다. 재생 시간·재생 속도·종료는 사용처가 갖고, 평가한 속도를 Modifier 값으로 넣는다.
Animator 없이 평가하므로 서버에서도 같은 결과가 나온다.
Track 데이터를 만드는 일(AnimationClip 굽기, 데이터 테이블 조회)은 사용처 책임이다.
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
    /// <br/> 일정 간격으로 샘플링한 Actor 기준 누적 변위와 Yaw 각도.
    /// <br/> 위치는 Actor 로컬(+Z 전방, +Y 위) 기준이다.
    /// </summary>
    // ============================================================
    public sealed class MotionTrack
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 초당 샘플 수.
        /// </summary>
        // ------------------------------------------------------------
        public float SampleRate { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Track 길이(초).
        /// </summary>
        // ------------------------------------------------------------
        public float Length { get; }

        private readonly Vector3[] positions;
        private readonly float[] yawDegrees;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 샘플을 복사해 Track을 만든다.
        /// <br/> Yaw를 생략하면 회전 변위가 없는 Track이다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionTrack(float sampleRate, ReadOnlySpan<Vector3> positions, ReadOnlySpan<float> yawDegrees = default)
        {
            if (!sampleRate.IsFinite() || sampleRate <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            if (positions.Length < 2)
            {
                throw new ArgumentException("Track은 샘플이 두 개 이상 필요합니다.", nameof(positions));
            }

            if (yawDegrees.Length != 0 && yawDegrees.Length != positions.Length)
            {
                throw new ArgumentException("Yaw 샘플 수가 위치 샘플 수와 다릅니다.", nameof(yawDegrees));
            }

            // 비유한 샘플이 있으면 평가 결과가 Body에 그대로 전달되므로 생성 시 거절한다.
            for (var index = 0; index < positions.Length; index++)
            {
                if (!positions[index].IsFinite() || (yawDegrees.Length != 0 && !yawDegrees[index].IsFinite()))
                {
                    throw new ArgumentException("Track 샘플은 유한해야 합니다.", nameof(positions));
                }
            }

            SampleRate = sampleRate;
            Length = (positions.Length - 1) / sampleRate;
            this.positions = positions.ToArray();
            this.yawDegrees = yawDegrees.ToArray();
        }

    #endregion

    #region 평가

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 두 시점 사이의 로컬 변위와 Yaw 변화(도)를 계산한다.
        /// <br/> 시점은 [0, Length]로 제한한다.
        /// </summary>
        // ------------------------------------------------------------
        public void EvaluateDelta(float from, float to, out Vector3 localDelta, out float yawDeltaDegrees)
        {
            localDelta = SamplePosition(to) - SamplePosition(from);
            yawDeltaDegrees = yawDegrees.Length == 0 ? 0f : SampleYaw(to) - SampleYaw(from);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 두 시점 사이의 변위를 delta초 동안의 월드 선속도·각속도(rad/s)로 바꾼다.
        /// <br/> 로컬 변위는 rotation으로 월드에 놓는다.
        /// <br/> Yaw 변화는 Body의 Up 축 회전으로 바꾼다.
        /// <br/> Up은 rotation의 로컬 +Y다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void EvaluateVelocity
        (
            float from, float to, float delta, Quaternion rotation,
            out Vector3 linear, out Vector3 angular
        )
        {
            if (!delta.IsFinite() || delta <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }

            EvaluateDelta(from, to, out var localDelta, out var yawDeltaDegrees);

            linear = rotation * localDelta / delta;
            angular = rotation * Vector3.up * (yawDeltaDegrees * Mathf.Deg2Rad / delta);
        }

        private Vector3 SamplePosition(float time)
        {
            Locate(time, out var index, out var weight);
            return Vector3.LerpUnclamped(positions[index], positions[index + 1], weight);
        }

        private float SampleYaw(float time)
        {
            Locate(time, out var index, out var weight);
            return Mathf.LerpUnclamped(yawDegrees[index], yawDegrees[index + 1], weight);
        }

        private void Locate(float time, out int index, out float weight)
        {
            var clamped = Mathf.Clamp(time, 0f, Length);
            var position = clamped * SampleRate;
            var last = positions.Length - 2;

            index = Math.Min((int)position, last);
            weight = position - index;
        }

    #endregion

    }
}
