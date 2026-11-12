/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundChecker3D.cs
수정일 : 2026-08-18

# 설명
Rigidbody/Collider를 사용하는 3D 바닥 체커.
BoxCollider, SphereCollider, CapsuleCollider를 지원하며,
Overlap과 Cast의 다중 후보에서 바닥 방향 표면을 선택하고,
부호 있는 거리, 지점, 법선을 표본에 기록한다.
GC 할당 방지를 위해 재사용 가능한 결과 배열을 관리하고 공통 PhysicsQuery3D를 사용한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego.Xeri;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// Rigidbody/Collider를 사용하는 3D 바닥 체커입니다.
    /// </summary>
    // ============================================================
    [Serializable]
    [RequireComponent(typeof(Rigidbody))]
    public class GroundChecker3D : GroundChecker<Rigidbody, Collider, GroundCheckSample3D>, INeedToInit<GameObject>
    {

    #region 필드

        public override Vector3 Gravity => Physics.gravity;

        private const int HitCapacity = 8;

        // GC 할당 없이 같은 물리 질의의 후보를 비교하기 위한 재사용 배열입니다.
        private readonly Collider[] overlapHits = new Collider[HitCapacity];
        private readonly RaycastHit[] castHits = new RaycastHit[HitCapacity];

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// GameObject를 통해 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Init(GameObject gameObject)
        {
            base.Init(gameObject);
        }

    #endregion

    #region Rigidbody 및 Collider 연결

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 선형 속도를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetLinearVelocity(Rigidbody rigidbody) => rigidbody.linearVelocity;

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 월드 각속도를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetAngularVelocity(Rigidbody rigidbody) => rigidbody.angularVelocity;

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 지정한 월드 지점 속도를 가져옵니다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetPointVelocity(Rigidbody rigidbody, Vector3 worldPoint) => rigidbody.GetPointVelocity(worldPoint);

        // ------------------------------------------------------------
        /// <summary>
        /// 지면 감지에 사용할 수 있는 Collider인지 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        protected override bool CheckColliderAvailable(Collider collider) => collider.enabled && !collider.isTrigger;

    #endregion

    #region Collider 탐지

        // -------------------------------------------------------------
        /// <summary>
        /// Collider를 사용하여 바닥을 감지합니다.
        /// </summary>
        // -------------------------------------------------------------
        protected override GroundCheckSample3D DetectWithCollider(Collider collider, float deltaTime)
        {
            if (collider is BoxCollider boxCollider)
            {
                return DetectWithBoxCollider(boxCollider, deltaTime);
            }
            else if (collider is SphereCollider sphereCollider)
            {
                return DetectWithSphereCollider(sphereCollider, deltaTime);
            }
            else if (collider is CapsuleCollider capsuleCollider)
            {
                return DetectWithCapsuleCollider(capsuleCollider, deltaTime);
            }

            return default;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>BoxCollider를 사용하여 바닥을 감지합니다.
        /// <br/>OverlapBox와 BoxCast의 지지 후보를 함께 비교합니다.
        /// </summary>
        // ------------------------------------------------------------
        private GroundCheckSample3D DetectWithBoxCollider(BoxCollider boxCollider, float deltaTime)
        {
            var info = GetBoxColliderDetectionInfo(boxCollider, deltaTime);

            var center      = info.Center - info.Direction * GroundCheckerConfig.Thickness * 0.5f;
            var size        = new Vector3(info.Size.x, GroundCheckerConfig.Thickness, info.Size.z);
            var orientation = boxCollider.transform.rotation;

            var volume = PhysicsVolume3D.CreateBox(center, size, orientation);

            // 시작 중첩 후보에서 바닥 방향 표면을 우선 복원합니다.
            int overlapCount = PhysicsQuery3D.Overlap
            (
                volume,
                overlapHits,
                Config.Layer,
                QueryTriggerInteraction.Ignore
            );
            var selected = SelectOverlapSample
            (
                boxCollider,
                overlapCount,
                info.Center,
                info.Direction
            );

            // Overlap과 Cast를 같은 후보 기준으로 비교하도록 아래쪽 표면까지 계속 확인합니다.
            int castCount = PhysicsQuery3D.Cast
            (
                volume,
                info.Direction,
                info.Depth,
                castHits,
                Config.Layer,
                QueryTriggerInteraction.Ignore
            );

            return SelectCastSample
            (
                selected,
                castCount,
                overlapCount,
                info.Direction
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>SphereCollider를 사용하여 바닥을 감지합니다.
        /// <br/>OverlapSphere와 SphereCast의 지지 후보를 함께 비교합니다.
        /// </summary>
        // ------------------------------------------------------------
        private GroundCheckSample3D DetectWithSphereCollider(SphereCollider sphereCollider, float deltaTime)
        {
            var info = GetSphereColliderDetectionInfo(sphereCollider, deltaTime);

            var volume = PhysicsVolume3D.CreateSphere(info.Center, info.Radius);

            // 시작 중첩 후보에서 바닥 방향 표면을 우선 복원합니다.
            int overlapCount = PhysicsQuery3D.Overlap
            (
                volume,
                overlapHits,
                Config.Layer,
                QueryTriggerInteraction.Ignore
            );
            var selected = SelectOverlapSample
            (
                sphereCollider,
                overlapCount,
                info.Center,
                info.Direction
            );

            // Overlap과 Cast를 같은 후보 기준으로 비교하도록 아래쪽 표면까지 계속 확인합니다.
            int castCount = PhysicsQuery3D.Cast
            (
                volume,
                info.Direction,
                info.Depth,
                castHits,
                Config.Layer,
                QueryTriggerInteraction.Ignore
            );

            return SelectCastSample
            (
                selected,
                castCount,
                overlapCount,
                info.Direction
            );
        }

        // ------------------------------------------------------------------------------
        /// <summary>
        /// <br/>CapsuleCollider를 사용하여 바닥을 감지합니다.
        /// <br/>수직 캡슐의 OverlapSphere와 SphereCast 지지 후보를 함께 비교합니다.
        /// </summary>
        // ------------------------------------------------------------------------------
        private GroundCheckSample3D DetectWithCapsuleCollider(CapsuleCollider capsuleCollider, float deltaTime)
        {
            var info = GetCapsuleColliderDetectionInfo(capsuleCollider, deltaTime);

            // ------------------------------------------------------------
            // 수직 캡슐
            // ------------------------------------------------------------
            if (info.Flag)
            {
                var volume = PhysicsVolume3D.CreateSphere(info.Center, info.Radius);

                int overlapCount = PhysicsQuery3D.Overlap
                (
                    volume,
                    overlapHits,
                    Config.Layer,
                    QueryTriggerInteraction.Ignore
                );
                var selected = SelectOverlapSample
                (
                    capsuleCollider,
                    overlapCount,
                    info.Center,
                    info.Direction
                );

                int castCount = PhysicsQuery3D.Cast
                (
                    volume,
                    info.Direction,
                    info.Depth,
                    castHits,
                    Config.Layer,
                    QueryTriggerInteraction.Ignore
                );

                return SelectCastSample
                (
                    selected,
                    castCount,
                    overlapCount,
                    info.Direction
                );
            }
            // ------------------------------------------------------------
            // 수평 캡슐 — 미구현
            // ------------------------------------------------------------

            return default;
        }

    #endregion

    #region 후보 선택

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// <br/>시작 중첩 Collider의 실제 표면 정보를 복원하고
        /// <br/>공통 지지 후보 기준으로 비교합니다.
        /// </summary>
        // ------------------------------------------------------------------------------------------
        private GroundCheckSample3D SelectOverlapSample
        (
            Collider sourceCollider,
            int overlapCount,
            Vector3 detectionCenter,
            Vector3 detectionDirection
        )
        {
            var selected = default(GroundCheckSample3D);

            for (int i = 0; i < overlapCount; i++)
            {
                var candidate = CreateOverlapSample
                (
                    sourceCollider,
                    overlapHits[i],
                    detectionCenter,
                    detectionDirection
                );
                selected = SelectCandidate
                (
                    selected,
                    candidate,
                    detectionDirection,
                    Ground,
                    Physics.defaultContactOffset
                );
            }

            return selected;
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// <br/>시작 중첩에서 이미 복원한 Collider의 Cast 결과를 제외하고
        /// <br/>남은 Cast 결과를 공통 지지 후보 기준으로 비교합니다.
        /// </summary>
        // ------------------------------------------------------------------------------------------
        private GroundCheckSample3D SelectCastSample
        (
            in GroundCheckSample3D selectedSample,
            int castCount,
            int overlapCount,
            Vector3 detectionDirection
        )
        {
            var selected = selectedSample;

            for (int i = 0; i < castCount; i++)
            {
                var hit = castHits[i];

                if (ContainsOverlap(hit.collider, overlapCount)) continue;

                var candidate = CreateSample(hit);
                selected = SelectCandidate
                (
                    selected,
                    candidate,
                    detectionDirection,
                    Ground,
                    Physics.defaultContactOffset
                );
            }

            return selected;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Collider가 이번 질의의 시작 중첩에 포함됐는지 확인합니다.
        /// </summary>
        // ------------------------------------------------------------
        private bool ContainsOverlap(Collider collider, int overlapCount)
        {
            for (int i = 0; i < overlapCount; i++)
            {
                if (overlapHits[i] == collider) return true;
            }

            return false;
        }

    #endregion

    #region 표본 생성

        // ------------------------------------------------------------
        /// <summary>
        /// 3D Cast 결과를 공통 바닥 표본으로 변환합니다.
        /// </summary>
        // ------------------------------------------------------------
        private GroundCheckSample3D CreateSample(RaycastHit hit)
        {
            return BuildSample
            (
                hit.collider,
                hit.distance,
                hit.point,
                hit.normal
            );
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Overlap된 Collider 쌍의 부호 있는 거리, 지점, 법선을 계산합니다.
        /// <br/>관통이 아닌 접촉 경계는 검사 반대 방향의 외부 지점에서 표면을 복원합니다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private GroundCheckSample3D CreateOverlapSample
        (
            Collider sourceCollider,
            Collider groundCollider,
            Vector3 detectionCenter,
            Vector3 detectionDirection
        )
        {
            var sourceTransform = sourceCollider.transform;
            var groundTransform = groundCollider.transform;

            // 실제 관통은 Collider를 분리하는 방향으로 지지면 자격을 먼저 판정합니다.
            if
            (
                Physics.ComputePenetration
                (
                    sourceCollider,
                    sourceTransform.position,
                    sourceTransform.rotation,
                    groundCollider,
                    groundTransform.position,
                    groundTransform.rotation,
                    out var normal,
                    out var penetration
                )
            )
            {
                if (!TryGetGroundAlignment(normal, detectionDirection, out var alignment)) return default;

                var outsidePoint = detectionCenter +
                                   normal * (penetration + Physics.defaultContactOffset);
                var point = groundCollider.ClosestPoint(outsidePoint);

                // 법선 방향 관통 깊이를 Cast와 같은 검사 축 기준의 음수 거리로 정규화합니다.
                return BuildSample
                (
                    groundCollider,
                    -penetration / alignment,
                    point,
                    normal
                );
            }

            // 수치상 관통하지 않는 접촉 경계에서는 바닥 검사 반대편에서 실제 표면 법선을 구합니다.
            var direction = detectionDirection.normalized;
            var outsideContactPoint = detectionCenter -
                                      direction * Physics.defaultContactOffset;
            var contactPoint  = groundCollider.ClosestPoint(outsideContactPoint);
            var contactOffset = outsideContactPoint - contactPoint;

            if (contactOffset.sqrMagnitude <= Mathf.Epsilon)
            {
                return default;
            }

            return BuildSample
            (
                groundCollider,
                0f,
                contactPoint,
                contactOffset.normalized
            );
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 3D 바닥 표본을 생성합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected override GroundCheckSample3D CreateSample
        (
            Collider groundCollider,
            Rigidbody groundRigid,
            float distance,
            Vector3 point,
            Vector3 normal
        )
        {
            return new GroundCheckSample3D
            (
                groundCollider,
                groundRigid,
                distance,
                point,
                normal
            );
        }

    #endregion

    #region Box 범위 계산

        // ------------------------------------------------------------
        /// <summary>
        /// BoxCollider의 바닥 감지 계산 정보를 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection GetBoxColliderDetectionInfo(BoxCollider boxCollider, float deltaTime)
        {
            return GetBoxColliderDetectionInfo(boxCollider.transform, boxCollider.center, boxCollider.size, deltaTime);
        }

        private GroundCheckerDetection GetBoxColliderDetectionInfo(Transform boxTransform, Vector3 boxCenter, Vector3 boxSize, float deltaTime)
        {
            var info = GroundCheckerCalculation.Create(boxTransform);

            // ------------------------------------------------------------
            // 방향 계산
            // ------------------------------------------------------------
            var worldDirection = info.WorldDirection;

            // ------------------------------------------------------------
            // 크기 계산
            // ------------------------------------------------------------
            var scale = Vector3.Scale(boxSize, boxTransform.lossyScale);
            var size  = new Vector3(scale.x, 0f, scale.z);

            // ------------------------------------------------------------
            // 중심점 계산
            // ------------------------------------------------------------
            Vector3 localCenter = boxCenter;
            Vector3 worldCenter = boxTransform.TransformPoint(localCenter);

            // 바닥면의 중심점을 계산합니다.
            worldCenter += worldDirection * scale.y * 0.5f;

            // ------------------------------------------------------------
            // 깊이 계산
            // ------------------------------------------------------------
            var depth = GetDepth(worldDirection, deltaTime);

            return new GroundCheckerDetection(worldCenter, size, worldDirection, depth);
        }

    #endregion

    #region Sphere 범위 계산

        // ------------------------------------------------------------
        /// <summary>
        /// SphereCollider의 바닥 감지 계산 정보를 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection GetSphereColliderDetectionInfo(SphereCollider sphereCollider, float deltaTime)
        {
            return GetSphereColliderDetectionInfo(sphereCollider.transform, sphereCollider.center, sphereCollider.radius, deltaTime);
        }

        private GroundCheckerDetection GetSphereColliderDetectionInfo(Transform sphereTransform, Vector3 sphereCenter, float sphereRadius, float deltaTime)
        {
            var info = GroundCheckerCalculation.Create(sphereTransform);

            // ------------------------------------------------------------
            // 중심점 계산
            // ------------------------------------------------------------
            Vector3 localCenter = sphereCenter;
            Vector3 worldCenter = sphereTransform.TransformPoint(localCenter);

            // ------------------------------------------------------------
            // 방향 계산
            // ------------------------------------------------------------
            var worldDirection = info.WorldDirection;

            // ------------------------------------------------------------
            // 반지름 계산 — 월드 스케일 적용
            // ------------------------------------------------------------
            var worldScale  = sphereTransform.lossyScale;
            var worldRadius = sphereRadius * Mathf.Max(worldScale.x, worldScale.y, worldScale.z);

            // ------------------------------------------------------------
            // 깊이 계산
            // ------------------------------------------------------------
            var depth = GetDepth(worldDirection, deltaTime);

            return new GroundCheckerDetection(worldCenter, worldRadius, worldDirection, depth);
        }

    #endregion

    #region Capsule 범위 계산

        // ------------------------------------------------------------
        /// <summary>
        /// CapsuleCollider의 바닥 감지 계산 정보를 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public GroundCheckerDetection GetCapsuleColliderDetectionInfo(CapsuleCollider capsuleCollider, float deltaTime)
        {
            var capsuleTransform = capsuleCollider.transform;
            var (capsuleCenter, capsuleRadius, capsuleHeight) = (capsuleCollider.center, capsuleCollider.radius, capsuleCollider.height);

            if (capsuleCollider.direction == 1) // Y축 (수직)
            {
                var info = GroundCheckerCalculation.Create(capsuleTransform);

                // ------------------------------------------------------------
                // 중심점 계산
                // ------------------------------------------------------------
                Vector3 localCenter = capsuleCenter;
                Vector3 worldCenter = capsuleTransform.TransformPoint(localCenter);

                // ------------------------------------------------------------
                // 방향 계산
                // ------------------------------------------------------------
                var worldDirection = info.WorldDirection;

                // ------------------------------------------------------------
                // 크기 계산
                // ------------------------------------------------------------
                var worldScale  = capsuleTransform.lossyScale;
                var worldRadius = capsuleRadius * Mathf.Max(worldScale.x, worldScale.z);
                var worldHeight = capsuleHeight * worldScale.y;

                var yOffset = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);

                // 바닥면의 중심점을 계산합니다.
                worldCenter += worldDirection * yOffset;

                // ------------------------------------------------------------
                // 깊이 계산
                // ------------------------------------------------------------
                var depth = GetDepth(worldDirection, deltaTime);

                return new GroundCheckerDetection(worldCenter, worldRadius, worldDirection, depth, true);
            }
            else
            {
                // 수평 캡슐 — 빈 구현
                return new GroundCheckerDetection(Vector3.zero, Vector3.zero, 0f, Vector3.down, 0f, false);
            }
        }

    #endregion

    }
}
