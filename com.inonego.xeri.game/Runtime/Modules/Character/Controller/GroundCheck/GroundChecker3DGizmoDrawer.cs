/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundChecker3DGizmoDrawer.cs
수정일 : 2026-05-01

# 설명
GroundChecker3D의 감지 영역을 에디터에서 시각화하는 MonoBehaviour.
바닥 상태에 따라 초록색(착지) / 빨간색(이탈)으로 표시한다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

using inonego.Xeri;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// GroundChecker3D의 기즈모를 그리는 컴포넌트입니다.
    /// </summary>
    // ============================================================
    public class GroundChecker3DGizmoDrawer : MonoBehaviour, INeedToInit<GroundChecker3D>
    {

    #region 생성자

        private GroundChecker3D groundChecker;

        // ------------------------------------------------------------
        /// <summary>
        /// GroundChecker3D를 주입하여 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Init(GroundChecker3D checker)
        {
            groundChecker = checker;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 참조를 해제합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void Release()
        {
            groundChecker = null;
        }

    #endregion

    #region 그리기 업데이트

        private void OnDrawGizmos()
        {
            if (groundChecker == null || groundChecker.GameObject == null) return;

            var colliders = groundChecker.GameObject.GetComponents<Collider>();

            foreach (var collider in colliders)
            {
                if (collider is BoxCollider boxCollider)
                {
                    DrawBoxColliderGizmo(boxCollider);
                }
                else if (collider is SphereCollider sphereCollider)
                {
                    DrawSphereColliderGizmo(sphereCollider);
                }
                else if (collider is CapsuleCollider capsuleCollider)
                {
                    DrawCapsuleColliderGizmo(capsuleCollider);
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// BoxCollider 감지 영역을 그립니다.
        /// </summary>
        // ------------------------------------------------------------
        private void DrawBoxColliderGizmo(BoxCollider boxCollider)
        {
            var info      = groundChecker.GetBoxColliderDetectionInfo(boxCollider, Time.fixedDeltaTime);
            var depthSize = new Vector3(info.Size.x, info.Depth, info.Size.z);
            var position  = info.Center + info.Direction * info.Depth * 0.5f;
            var color     = groundChecker.IsOnGround ? Color.green : Color.red;
            var rotation  = boxCollider.transform.rotation;

            GizmoHelper.DrawWireCube(position, rotation, depthSize, color);

            var (start, end) = (info.Center, info.Center + info.Direction * info.Depth);
            GizmoHelper.DrawLine(start, end, Color.yellow);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// SphereCollider 감지 영역을 그립니다.
        /// </summary>
        // ------------------------------------------------------------
        private void DrawSphereColliderGizmo(SphereCollider sphereCollider)
        {
            var info     = groundChecker.GetSphereColliderDetectionInfo(sphereCollider, Time.fixedDeltaTime);
            var rotation = Quaternion.LookRotation(Vector3.forward, info.Direction);
            var color    = groundChecker.IsOnGround ? Color.green : Color.red;
            var height   = info.Depth + 2 * info.Radius;
            var center   = info.Center + info.Direction * info.Depth * 0.5f;

            GizmoHelper.DrawWireCapsule(center, rotation, info.Radius, height, color);

            var (start, end) = (info.Center, info.Center + info.Direction * info.Depth);
            GizmoHelper.DrawLine(start, end, Color.yellow);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// CapsuleCollider 감지 영역을 그립니다.
        /// </summary>
        // ------------------------------------------------------------
        private void DrawCapsuleColliderGizmo(CapsuleCollider capsuleCollider)
        {
            var info  = groundChecker.GetCapsuleColliderDetectionInfo(capsuleCollider, Time.fixedDeltaTime);
            var color = groundChecker.IsOnGround ? Color.green : Color.red;

            if (info.Flag)
            {
                var rotation = capsuleCollider.transform.rotation;
                var height   = info.Depth + 2 * info.Radius;
                var center   = info.Center + info.Direction * info.Depth * 0.5f;

                GizmoHelper.DrawWireCapsule(center, rotation, info.Radius, height, color);
            }

            var (start, end) = (info.Center, info.Center + info.Direction * info.Depth);
            GizmoHelper.DrawLine(start, end, Color.yellow);
        }

    #endregion

    }
}
