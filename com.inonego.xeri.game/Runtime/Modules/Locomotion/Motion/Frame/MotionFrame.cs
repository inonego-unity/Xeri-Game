/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotionFrame.cs
수정일 : 2026-11-21

# 설명
Motion Model이 한 Tick에 합성한 결과(MotionFrame)와 채널 적용 방식(MotionChannelMode)을 정의한다.

# 특이사항, 제약사항
MotionFrame은 Motor가 Tick당 한 번 적용한다. 선속도는 Up 축으로 Planar·Elevation을 나눈다.
각속도 단위는 rad/s이며 2D Backend가 자기 단위로 변환한다.
생성자는 값을 검증하지 않는다. Backend는 쓰기 전에 IsValid(Up 단위 벡터, 정의된 채널 방식, 유효한 Actor·Tick)를 확인한다.
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
    /// 채널별 적용 방식.
    /// </summary>
    // ============================================================
    public enum MotionChannelMode { Free, Set }

    // ============================================================
    /// <summary>
    /// <br/> Motion Model이 한 Tick에 합성한 운동 결과.
    /// <br/> Set 채널은 값으로 덮고 Free 채널은 Body 속도를 유지한다.
    /// </summary>
    // ============================================================
    public readonly struct MotionFrame
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 결과를 만든 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 결과를 만든 Tick.
        /// </summary>
        // ------------------------------------------------------------
        public MotionTick Tick { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 결과를 만든 Model의 실행 버전. 이전 결과를 구분한다.
        /// </summary>
        // ------------------------------------------------------------
        public long StepVersion { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Planar와 Elevation을 나누는 정규화된 Up 축.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Up { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Planar 채널 적용 방식.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelMode PlanarMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Up 축에 수직인 Planar 속도.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Planar { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Elevation 채널 적용 방식.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelMode ElevationMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Up 축 방향 속도.
        /// </summary>
        // ------------------------------------------------------------
        public float Elevation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Rotation 채널 적용 방식.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannelMode RotationMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 각속도(rad/s).
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Rotation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 채널 적용 뒤 더하는 선속도 변화량.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Impulse { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 가장 위에 있는 Override의 동작 식별자. 없으면 0.
        /// </summary>
        // ------------------------------------------------------------
        public int ActionID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 가장 위에 있는 Override가 시작한 Tick. 없으면 -1.
        /// </summary>
        // ------------------------------------------------------------
        public long ActionStartTick { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 의도 배율이 0인 채널.
        /// </summary>
        // ------------------------------------------------------------
        public MotionChannel BlockedChannels { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 합성 결과를 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionFrame
        (
            MotionActorKey actor, MotionTick tick, long stepVersion, Vector3 up,
            MotionChannelMode planarMode, Vector3 planar,
            MotionChannelMode elevationMode, float elevation,
            MotionChannelMode rotationMode, Vector3 rotation,
            Vector3 impulse, int actionID, long actionStartTick, MotionChannel blockedChannels
        )
        {
            Actor = actor;
            Tick = tick;
            StepVersion = stepVersion;
            Up = up;
            PlanarMode = planarMode;
            Planar = planar;
            ElevationMode = elevationMode;
            Elevation = elevation;
            RotationMode = rotationMode;
            Rotation = rotation;
            Impulse = impulse;
            ActionID = actionID;
            ActionStartTick = actionStartTick;
            BlockedChannels = blockedChannels;
        }

    #endregion

    #region 유효성 검사

        // Up 축 크기 제곱이 1에서 벗어날 수 있는 허용 오차. Model은 double로 정규화하므로 float 오차보다 충분히 크다.
        private const float UP_UNIT_TOLERANCE = 1e-3f;

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 값이 유한한지 검사한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsFinite =>
            Up.IsFinite() && Planar.IsFinite() && Elevation.IsFinite() &&
            Rotation.IsFinite() && Impulse.IsFinite();

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Body에 적용해도 되는 형태인지 검사한다.
        /// <br/> 유한한 값, 단위 Up, 정의된 채널 방식, 유효한 Actor·Tick.
        /// <br/> 생성자는 검증하지 않으므로 적용 전에 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsValid =>
            IsFinite && Actor.ScopeID > 0 && Tick.IsValid && IsUnitUp() &&
            IsDefinedMode(PlanarMode) && IsDefinedMode(ElevationMode) && IsDefinedMode(RotationMode);

        private bool IsUnitUp() => Mathf.Abs(Up.sqrMagnitude - 1f) <= UP_UNIT_TOLERANCE;

        private static bool IsDefinedMode(MotionChannelMode mode) =>
            mode == MotionChannelMode.Free || mode == MotionChannelMode.Set;

    #endregion

    #region 적용 계산

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 Body 선속도에 채널 방식과 Impulse를 적용한 결과를 계산한다.
        /// <br/> Free 채널은 현재 속도의 해당 성분을 유지한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public Vector3 ComposeLinear(Vector3 current)
        {
            var currentElevation = Vector3.Dot(current, Up);
            var currentPlanar = current - Up * currentElevation;

            var planar = PlanarMode == MotionChannelMode.Set ? Planar : currentPlanar;
            var elevation = ElevationMode == MotionChannelMode.Set ? Elevation : currentElevation;

            return planar + Up * elevation + Impulse;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Body 각속도에 Rotation 채널 방식을 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 ComposeAngular(Vector3 current) =>
            RotationMode == MotionChannelMode.Set ? Rotation : current;

    #endregion

    }
}
