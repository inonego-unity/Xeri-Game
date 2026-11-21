/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionTrack.cs
수정일 : 2026-11-20

# 설명
루트 모션처럼 시간에 따른 Actor 기준 변위를 담는 불변 Track 데이터와 TrackID 조회 Library를 정의한다.

# 특이사항, 제약사항
Track 데이터를 만드는 일(AnimationClip 굽기, 데이터 테이블 조회)은 사용처 책임이다.
Model이 Step 중 외부 코드를 부르지 않도록 Library는 생성 시 복사한 불변 데이터만 보관한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

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

    // ============================================================
    /// <summary>
    /// <br/> TrackID로 Track을 찾는 불변 Library.
    /// <br/> 생성 시 항목을 복사하고 이후 바뀌지 않는다.
    /// </summary>
    // ============================================================
    public sealed class MotionTrackLibrary
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Track이 없는 빈 Library.
        /// </summary>
        // ------------------------------------------------------------
        public static MotionTrackLibrary Empty { get; } = new(Array.Empty<KeyValuePair<int, MotionTrack>>());

        private readonly Dictionary<int, MotionTrack> tracks;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// TrackID와 Track 쌍을 복사해 Library를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionTrackLibrary(IEnumerable<KeyValuePair<int, MotionTrack>> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            tracks = new Dictionary<int, MotionTrack>();

            foreach (var entry in entries)
            {
                if (entry.Key <= 0 || entry.Value == null)
                {
                    throw new ArgumentException("TrackID는 양수이고 Track은 null이 아니어야 합니다.", nameof(entries));
                }

                tracks.Add(entry.Key, entry.Value);
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// TrackID에 해당하는 Track을 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGet(int trackID, out MotionTrack track) => tracks.TryGetValue(trackID, out track);

    #endregion

    }
}
