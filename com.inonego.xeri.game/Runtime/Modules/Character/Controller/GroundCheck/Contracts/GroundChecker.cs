/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundChecker.cs
수정일 : 2026-08-03

# 설명
2D/3D 공통 로직을 담는 제네릭 추상 바닥 체커.
리지드바디와 콜라이더 타입을 보존한 GroundCheckSample 생성과 접지 승인 정책을 담당한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego.Xeri;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// 2D/3D 공통 로직을 담는 제네릭 추상 바닥 체커입니다.
    /// </summary>
    // ============================================================
    public abstract class GroundChecker<TRigidbody, TCollider, TSample> : GroundCheckerBase<TRigidbody, TCollider, TSample>
    where TRigidbody : Component
    where TCollider  : Component
    where TSample : struct, IGroundCheckSample<TRigidbody, TCollider>
    {

    #region 필드

        [SerializeField]
        public GroundCheckerConfig Config;

        [SerializeField, ReadOnly]
        private GameObject gameObject = null;
        public override GameObject GameObject => gameObject;

        [SerializeField]
        private TRigidbody rigid = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 오브젝트에 붙어 있는 리지드바디를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        public TRigidbody Rigid => rigid;

        [SerializeField]
        private TCollider[] colliders = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 오브젝트에 붙어 있는 콜라이더 배열을 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        public TCollider[] Colliders => colliders;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 밟고 있는 바닥 오브젝트의 리지드바디를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        public TRigidbody GroundRigid => Sample.GroundRigid;

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody에서 선형 속도를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 GetLinearVelocity(TRigidbody rigidbody);

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody에서 라디안 단위 월드 각속도를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 GetAngularVelocity(TRigidbody rigidbody);

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 지정한 월드 지점 속도를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 GetPointVelocity(TRigidbody rigidbody, Vector3 worldPoint);

        // ------------------------------------------------------------
        /// <summary>
        /// 사용할 수 있는 Collider인지 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract bool CheckColliderAvailable(TCollider collider);

        public override Vector3 Velocity
        {
            get => rigid != null ? GetLinearVelocity(rigid) : Vector3.zero;
        }

        public override Vector3 GroundLinearVelocity
        {
            get => GroundRigid != null ? GetLinearVelocity(GroundRigid) : Vector3.zero;
        }

        public override Vector3 GroundAngularVelocity
        {
            get => GroundRigid != null ? GetAngularVelocity(GroundRigid) : Vector3.zero;
        }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>GameObject를 통해 리지드바디와 콜라이더를 초기화합니다.
        /// <br/>INeedToInit 계열 인터페이스를 통해 호출됩니다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Init(GameObject gameObject)
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException("GameObject가 null입니다.");
            }

            this.gameObject = gameObject;

            this.rigid     = gameObject.GetComponent<TRigidbody>();
            this.colliders = gameObject.GetComponents<TCollider>();

            if (rigid == null)
            {
                throw new ArgumentNullException($"{typeof(TRigidbody).Name}가 null입니다. GameObject에 {typeof(TRigidbody).Name} 컴포넌트가 필요합니다.");
            }

            if (colliders == null || colliders.Length == 0)
            {
                throw new ArgumentNullException($"{typeof(TCollider).Name}가 null 또는 빈 배열입니다. GameObject에 {typeof(TCollider).Name} 컴포넌트가 필요합니다.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 참조를 해제합니다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void Release()
        {
            sample     = default;
            gameObject = null;
            rigid      = null;
            colliders  = null;
        }

    #endregion

    #region 후보 선택

        // ----------------------------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Overlap과 Cast 후보에 같은 지면 정렬 기준을 적용해 비교합니다.
        /// <br/>접촉 오차 안에서는 현재 지면을 유지하고, 명확히 높은 지지면은 즉시 선택합니다.
        /// </summary>
        // ----------------------------------------------------------------------------------------------------
        protected TSample SelectCandidate
        (
            in TSample selected,
            in TSample candidate,
            Vector3 detectionDirection,
            GameObject currentGround,
            float contactTolerance
        )
        {
            if (!TryGetGroundAlignment(candidate.Normal, detectionDirection, out _)) return selected;
            if (!selected.HasGround) return candidate;

            var groundDirection = -detectionDirection.normalized;
            var heightDelta = Vector3.Dot
            (
                candidate.Point - selected.Point,
                groundDirection
            );
            var tolerance = Mathf.Max(0f, contactTolerance);

            // 실제 지지점 높이가 구분되면 높은 표면을 선택해 계단과 턱 전환을 지연시키지 않습니다.
            if (heightDelta > tolerance) return candidate;
            if (heightDelta < -tolerance) return selected;

            // 같은 접촉 범위에서는 기존 지면을 유지해 맞닿은 Collider 사이의 반복 전환을 막습니다.
            var selectedIsCurrent  = selected.Ground == currentGround;
            var candidateIsCurrent = candidate.Ground == currentGround;

            if (selectedIsCurrent != candidateIsCurrent)
            {
                return candidateIsCurrent ? candidate : selected;
            }

            var selectedAlignment = Vector3.Dot
            (
                selected.Normal.normalized,
                groundDirection
            );
            var candidateAlignment = Vector3.Dot
            (
                candidate.Normal.normalized,
                groundDirection
            );

            if (candidateAlignment > selectedAlignment) return candidate;
            if (candidateAlignment < selectedAlignment) return selected;

            return candidate.Distance < selected.Distance ? candidate : selected;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 표면 법선이 검사 반대 방향을 향하고 설정된 지면 정렬 기준을 만족하는지 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected bool TryGetGroundAlignment
        (
            Vector3 normal,
            Vector3 detectionDirection,
            out float alignment
        )
        {
            alignment = Vector3.Dot
            (
                normal.normalized,
                -detectionDirection.normalized
            );

            return alignment > 0f &&
                   alignment >= Mathf.Clamp01(Config.MinimumGroundAlignment);
        }

    #endregion

    #region 지면 처리

        // ----------------------------------------------------------------------
        /// <summary>
        /// 지정한 월드 지점에서 현재 바닥 Rigidbody의 속도를 가져옵니다.
        /// </summary>
        // ----------------------------------------------------------------------
        public override Vector3 GetGroundPointVelocity(Vector3 worldPoint)
        {
            return GroundRigid != null
                ? GetPointVelocity(GroundRigid, worldPoint)
                : Vector3.zero;
        }

        // ------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// <br/>바닥을 처리하고 바닥 오브젝트의 리지드바디를 캐시합니다.
        /// <br/>최초 접지 시에는 바닥 방향 운동을 요구하고, 검출 중인 지면은 형상이 벗어날 때까지 유지합니다.
        /// </summary>
        // ------------------------------------------------------------------------------------------------------------------------
        protected override void ProcessGround(TSample prev, ref TSample next)
        {
            if (!next.HasGround)
            {
                return;
            }

            // 회전하는 지면에서도 실제 접근 방향을 판정하도록 감지 지점의 속도를 사용합니다.
            var nextGroundRigid = next.GroundRigid;
            var groundVelocity = nextGroundRigid != null ? GetPointVelocity(nextGroundRigid, next.Point) : Vector3.zero;

            var (velocity, gravity) = (GetLinearVelocity(rigid), Gravity);

            // 최초 접지에만 운동 방향을 적용해 상승 중인 표면을 새 바닥으로 획득하지 않는다.
            // 이미 검출 중인 지면은 지지력이 만든 작은 상승 속도로 접지가 흔들리지 않도록 유지한다.
            var isHeadingToGround = prev.HasGround || IsHeadingToGround(velocity, groundVelocity, gravity);

            if (!isHeadingToGround)
            {
                next = default;
                return;
            }
        }

    #endregion

    #region 감지

        // -------------------------------------------------------------
        /// <summary>
        /// 바닥을 감지합니다.
        /// </summary>
        // -------------------------------------------------------------
        protected override TSample Detect(float deltaTime)
        {
            if (rigid == null)
            {
                throw new ArgumentNullException($"{typeof(TRigidbody).Name}가 null입니다. Init 메서드를 통해 초기화해주세요.");
            }

            if (colliders == null || colliders.Length == 0)
            {
                throw new ArgumentNullException($"{typeof(TCollider).Name}가 null 또는 빈 배열입니다. Init 메서드를 통해 초기화해주세요.");
            }

            foreach (var collider in Colliders)
            {
                if (!CheckColliderAvailable(collider)) continue;

                var detected = DetectWithCollider(collider, deltaTime);

                // 바닥이 감지되면 즉시 반환
                if (detected.HasGround)
                {
                    return detected;
                }
            }

            return default;
        }

        // -------------------------------------------------------------
        /// <summary>
        /// 콜라이더를 사용하여 바닥을 감지합니다.
        /// </summary>
        // -------------------------------------------------------------
        protected abstract TSample DetectWithCollider(TCollider collider, float deltaTime);

    #endregion

    #region 표본 생성

        // ----------------------------------------------------------------------
        /// <summary>
        /// 감지한 Collider와 표면 정보로 바닥 표본을 생성합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected TSample BuildSample
        (
            TCollider groundCollider,
            float distance,
            Vector3 point,
            Vector3 normal
        )
        {
            if (groundCollider == null)
            {
                return default;
            }

            var detectedRigid = groundCollider.GetComponentInParent<TRigidbody>();

            return CreateSample
            (
                groundCollider,
                detectedRigid,
                distance,
                point,
                normal
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 타입이 지정된 바닥 표본을 생성합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected abstract TSample CreateSample
        (
            TCollider groundCollider,
            TRigidbody groundRigid,
            float distance,
            Vector3 point,
            Vector3 normal
        );

    #endregion

    #region 탐지 깊이

        // -------------------------------------------------------------
        /// <summary>
        /// 체크할 깊이를 구합니다.
        /// </summary>
        // -------------------------------------------------------------
        protected float GetDepth(Vector3 vector, float deltaTime)
        {
            // 회전하는 지면의 접점이 멀어지는 경우까지 한 프레임의 상대 이동량만큼 탐지 깊이를 늘립니다.
            var groundVelocity = GetGroundPointVelocity(Sample.Point);
            var depthByGround = Vector3.Dot(groundVelocity - Velocity, vector.normalized) * deltaTime;

            return Config.Depth + Mathf.Max(0f, depthByGround);
        }

    #endregion

    }
}
