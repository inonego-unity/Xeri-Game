/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundCheckerCommon.cs
수정일 : 2026-08-03

# 설명
바닥 감지에 사용되는 공통 구조체 모음.
GroundCheckSample2D/3D(검사 결과), GroundCheckerConfig(설정),
GroundCheckerCalculation(방향 계산), GroundCheckerDetection(콜라이더별 감지 정보)를 담는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// <br/>바닥 검사 표본의 공통 계약입니다.
    /// <br/>HasGround가 true이면 거리, 지점, 법선이 유효합니다.
    /// </summary>
    // ============================================================
    public interface IGroundCheckSample<TRigidbody, TCollider>
    where TRigidbody : Component
    where TCollider : Component
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 바닥을 감지했는지 여부입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasGround { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥 오브젝트입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GameObject Ground { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥 Collider입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TCollider GroundCollider { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥의 Rigidbody입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TRigidbody GroundRigid { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>검사 방향 축을 따른 형상과 지면 사이의 부호 있는 거리입니다.
        /// <br/>양수는 분리, 0은 접촉, 음수는 중첩을 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        public float Distance { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 지면의 월드 지점입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Point { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 지면의 월드 법선입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Normal { get; }
    }

    // ============================================================
    /// <summary>
    /// 한 번의 2D 바닥 검사에서 승인된 지면과 표면 정보를 담습니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public readonly struct GroundCheckSample2D : IGroundCheckSample<Rigidbody2D, Collider2D>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 바닥을 감지했는지 여부입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasGround => Ground != null;

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥 오브젝트입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GameObject Ground { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥 Collider2D입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Collider2D GroundCollider { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥의 Rigidbody2D입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Rigidbody2D GroundRigid { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>검사 방향 축을 따른 형상과 지면 사이의 부호 있는 거리입니다.
        /// <br/>양수는 분리, 0은 접촉, 음수는 중첩을 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        public float Distance { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 지면의 월드 지점입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Point { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 지면의 월드 법선입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Normal { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 바닥 검사 결과를 생성합니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckSample2D
        (
            Collider2D groundCollider,
            Rigidbody2D groundRigid,
            float distance,
            Vector3 point,
            Vector3 normal
        )
        {
            Ground         = groundCollider != null ? groundCollider.gameObject : null;
            GroundCollider = groundCollider;
            GroundRigid    = groundRigid;
            Distance       = distance;
            Point          = point;
            Normal         = normal;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 한 번의 3D 바닥 검사에서 승인된 지면과 표면 정보를 담습니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public readonly struct GroundCheckSample3D : IGroundCheckSample<Rigidbody, Collider>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 바닥을 감지했는지 여부입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasGround => Ground != null;

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥 오브젝트입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GameObject Ground { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥 Collider입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Collider GroundCollider { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 바닥의 Rigidbody입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Rigidbody GroundRigid { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>검사 방향 축을 따른 형상과 지면 사이의 부호 있는 거리입니다.
        /// <br/>양수는 분리, 0은 접촉, 음수는 중첩을 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        public float Distance { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 지면의 월드 지점입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Point { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지한 지면의 월드 법선입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Normal { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 바닥 검사 결과를 생성합니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckSample3D
        (
            Collider groundCollider,
            Rigidbody groundRigid,
            float distance,
            Vector3 point,
            Vector3 normal
        )
        {
            Ground         = groundCollider != null ? groundCollider.gameObject : null;
            GroundCollider = groundCollider;
            GroundRigid    = groundRigid;
            Distance       = distance;
            Point          = point;
            Normal         = normal;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 바닥 감지 설정을 담는 구조체입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct GroundCheckerConfig
    {
        public LayerMask Layer;
        public float Depth;

        // ------------------------------------------------------------
        /// <summary>
        /// 검사 반대 방향과 지면 법선 사이에 요구할 최소 정렬도입니다.
        /// </summary>
        // ------------------------------------------------------------
        [Range(0f, 1f)]
        public float MinimumGroundAlignment;

        public static float Thickness = 0.001f;
    }

    // ============================================================
    /// <summary>
    /// 바닥 감지 방향 계산 정보를 담는 구조체입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct GroundCheckerCalculation
    {
        public Vector3 LocalDirection;
        public Vector3 WorldDirection, WorldVector;
        public (Vector3 X, Vector3 Y, Vector3 Z) Basis;

        public float XAngle2D => GetAngle2D(Basis.X);
        public float YAngle2D => GetAngle2D(Basis.Y);
        public float ZAngle2D => GetAngle2D(Basis.Z);

        private float GetAngle2D(Vector3 vector) => Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;

        // ------------------------------------------------------------
        /// <summary>
        /// Transform을 기반으로 방향·기저 벡터를 계산하여 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public static GroundCheckerCalculation Create(Transform transform)
        {
            var result = new GroundCheckerCalculation();

            result.LocalDirection = Vector3.down;
            result.WorldDirection = transform.TransformDirection(result.LocalDirection);
            result.WorldVector    = transform.TransformVector(result.LocalDirection);

            var matrix = transform.localToWorldMatrix;

            result.Basis.X = new Vector3(matrix.m00, matrix.m10, matrix.m20); // X축 벡터
            result.Basis.Y = new Vector3(matrix.m01, matrix.m11, matrix.m21); // Y축 벡터
            result.Basis.Z = new Vector3(matrix.m02, matrix.m12, matrix.m22); // Z축 벡터

            return result;
        }
    }

    // ============================================================
    /// <summary>
    /// 바닥 감지 계산 정보를 담는 구조체입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public struct GroundCheckerDetection
    {
        public Vector3 Size;
        public Vector3 Center, Direction;
        public float Depth, Radius;
        public float Angle;
        public bool Flag;
        public Quaternion Rotation;

        // ------------------------------------------------------------
        /// <summary>
        /// BoxCollider2D 전용 생성자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection(Vector3 center, Vector3 size, float angle, Vector3 direction, float depth)
        {
            Radius   = 0f;
            Flag     = false;
            Rotation = Quaternion.identity;

            Center = center; Size = size; Angle = angle; Direction = direction; Depth = depth;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// BoxCollider 전용 생성자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection(Vector3 center, Vector3 size, Vector3 direction, float depth)
        {
            Radius   = 0f;
            Angle    = 0f;
            Flag     = false;
            Rotation = Quaternion.identity;

            Center = center; Size = size; Direction = direction; Depth = depth;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// SphereCollider 또는 CircleCollider2D 전용 생성자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection(Vector3 center, float radius, Vector3 direction, float depth)
        {
            Size     = Vector3.zero;
            Angle    = 0f;
            Flag     = false;
            Rotation = Quaternion.identity;

            Center = center; Radius = radius; Direction = direction; Depth = depth;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// CapsuleCollider2D(수직) 전용 생성자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection(Vector3 center, float radius, Vector3 direction, float depth, bool flag) :
        this(center, radius, direction, depth)
        {
            Flag = flag;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// CapsuleCollider2D(수평) 전용 생성자입니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection(Vector3 center, Vector3 size, float angle, Vector3 direction, float depth, bool flag) :
        this(center, size, angle, direction, depth)
        {
            Flag = flag;
        }
    }
}
