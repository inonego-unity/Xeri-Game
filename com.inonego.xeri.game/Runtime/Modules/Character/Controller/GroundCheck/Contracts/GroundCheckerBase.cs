/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundCheckerBase.cs
수정일 : 2026-08-01

# 설명
IGroundChecker의 공통 구현을 담당하는 추상 기본 클래스.
승인된 GroundCheckSample을 현재 상태로 소유하고 바닥 변경·이벤트 발생을 처리한다.
감지·방향 처리는 서브클래스에 위임한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego.Xeri;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// IGroundChecker의 공통 구현을 담당하는 추상 기본 클래스입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class GroundCheckerBase<TRigidbody, TCollider, TSample> : IGroundChecker
    where TRigidbody : Component
    where TCollider : Component
    where TSample : struct, IGroundCheckSample<TRigidbody, TCollider>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 검사에서 승인된 바닥 표본입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TSample Sample => sample;
        protected TSample sample = default;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 밟고 있는 바닥 오브젝트를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        public GameObject Ground => sample.Ground;

        public abstract Vector3 Velocity { get; }
        public abstract Vector3 GroundLinearVelocity { get; }
        public abstract Vector3 GroundAngularVelocity { get; }
        public abstract Vector3 Gravity { get; }
        public abstract GameObject GameObject { get; }

        public bool IsOnGround => sample.HasGround;

    #endregion

    #region 이벤트

        public event ValueChangeEventHandler<GameObject> OnLand = null;
        public event ValueChangeEventHandler<GameObject> OnLeave = null;

    #endregion

    #region 메서드

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 월드 지점에서 현재 바닥 Rigidbody의 속도를 가져옵니다.
        /// </summary>
        // ----------------------------------------------------------------------
        public abstract Vector3 GetGroundPointVelocity(Vector3 worldPoint);

        // ------------------------------------------------------------
        /// <summary>
        /// 바닥을 감지하고 변경 및 이벤트를 발생시킵니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Check(float deltaTime)
        {
            if (GameObject == null) return;

            var detected = Detect(deltaTime);

            Process(detected);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 레이캐스트 등의 방법을 통해 바닥을 감지합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract TSample Detect(float deltaTime);

        // ------------------------------------------------------------
        /// <summary>
        /// 주어진 벡터에 따라 게임 오브젝트가 바닥으로 향하고 있는지 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected static bool IsHeadingToGround(Vector3 velocity, Vector3 groundVelocity, Vector3 gravity)
        {
            var delta = velocity - groundVelocity;

            return Vector3.Dot(delta.normalized, gravity.normalized) > -0.001f;
        }

        protected abstract void ProcessGround(TSample prev, ref TSample next);

        // ------------------------------------------------------------
        /// <summary>
        /// 바닥에 대한 조건을 확인하고 변경 및 이벤트를 발생시킵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected void Process(TSample detected)
        {
            var (prev, next) = (sample, detected);

            ProcessGround(prev, ref next);

            // 같은 바닥을 유지하는 동안에도 거리와 법선은 바뀔 수 있으므로 표본은 항상 갱신합니다.
            sample = next;

            if (prev.Ground == next.Ground) return;

            bool wasOnGround = prev.HasGround;
            bool isOnGround  = next.HasGround;

            // 바닥 오브젝트만 교체된 경우는 계속 접지 중이므로 착지/이탈이 아닙니다.
            if (wasOnGround == isOnGround)
            {
                return;
            }

            if (isOnGround)
            {
                OnLand?.Invoke(this, new(prev.Ground, next.Ground));
            }
            else
            {
                OnLeave?.Invoke(this, new(prev.Ground, next.Ground));
            }
        }

    #endregion

    }
}
