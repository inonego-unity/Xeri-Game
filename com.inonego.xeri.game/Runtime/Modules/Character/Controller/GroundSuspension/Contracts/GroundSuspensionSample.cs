/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspensionSample.cs
수정일 : 2026-08-03

# 설명
GroundChecker2D/3D 표본으로 계산한 대표 지면 형상, 운동과 지지 가속도를 전달한다.
지면 상태를 별도로 소유하지 않고 해당 Tick의 관측값만 나타낸다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// Rigidbody 타입을 보존하는 한 고정 Tick의 지면 지지 표본.
    /// </summary>
    // ============================================================
    public readonly struct GroundSuspensionSample<TRigidbody>
    where TRigidbody : Component
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 대표 지면을 관측했는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasGround => Ground != null;

        // ------------------------------------------------------------
        /// <summary>
        /// 대표 지지면을 제공한 GameObject.
        /// </summary>
        // ------------------------------------------------------------
        public GameObject Ground { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 대표 지지면을 소유한 Rigidbody.
        /// </summary>
        // ------------------------------------------------------------
        public TRigidbody GroundRigid { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 검사 방향 축을 따른 캐릭터 형상과 대표 지지면 사이의 부호 있는 거리.
        /// </summary>
        // ------------------------------------------------------------
        public float Distance { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택한 대표 지지 월드 지점.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Point { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 선택한 대표 지지면의 월드 법선.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Normal { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 대표 지지 지점에서 관측한 지면 속도.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 GroundVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 대표 지지면 Rigidbody의 라디안 단위 월드 각속도 벡터.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 GroundAngularVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡슐 위쪽 축을 기준으로 적용할 지면 추종 가속도.
        /// </summary>
        // ------------------------------------------------------------
        public float Acceleration { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 지면 지지 표본을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundSuspensionSample
        (
            GameObject ground,
            TRigidbody groundRigid,
            float distance,
            Vector3 point,
            Vector3 normal,
            Vector3 groundVelocity,
            Vector3 groundAngularVelocity,
            float acceleration
        )
        {
            Ground                = ground;
            GroundRigid           = groundRigid;
            Distance              = distance;
            Point                 = point;
            Normal                = normal;
            GroundVelocity        = groundVelocity;
            GroundAngularVelocity = groundAngularVelocity;
            Acceleration          = acceleration;
        }

    #endregion

    }
}
