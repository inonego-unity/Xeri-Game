/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_DeliveryWorkspace.cs
수정일 : 2026-11-12

# 설명
PhysicsDeliveryWorkspaceBase의 공유 수명·용량·중첩 질의 계약을 단일 테스트 정의로 검증한다.
2D·3D 픽스처는 실제 Unity Physics Query와 형상 생성만 제공한다.

# 테스트 구성
 W: 공통 Workspace의 생성·버퍼 확장·Contact 수명·재진입 계약
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Delivery
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// <br/>차원별 Workspace의 공개 동작을 동일한 검증으로 실행한다.
    /// <br/>물리 콜라이더 생성과 Query 연결만 하위 테스트가 소유한다.
    /// </summary>
    // ============================================================
    public abstract class TEST_PhysicsDeliveryWorkspaceBase<TCollider, THit, TContact> : DeliveryTestFixtureBase
    where TCollider : Component
    where THit : struct
    where TContact : struct
    {

    #region 픽스처

        protected abstract PhysicsDeliveryWorkspaceBase<TCollider, THit, TContact> CreateWorkspace
        (
            int capacity,
            int maximumCapacity
        );

        protected abstract void CreateTargets();
        protected abstract DeliveryBatchResult QueryVolume(PhysicsDeliveryWorkspaceBase<TCollider, THit, TContact> workspace);
        protected abstract void ClearByQuery(PhysicsDeliveryWorkspaceBase<TCollider, THit, TContact> workspace);
        protected abstract void RunReentrantQuery(PhysicsDeliveryWorkspaceBase<TCollider, THit, TContact> workspace);

    #endregion

    #region W-1: 용량 계약

        [Test]
        public void TEST_PhysicsDeliveryWorkspaceBase_잘못된용량을거부()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateWorkspace(0, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateWorkspace(2, 1));
        }

        [Test]
        public void TEST_PhysicsDeliveryWorkspaceBase_포화시상한까지성장하고부분결과를보고()
        {
            CreateTargets();
            var workspace = CreateWorkspace(1, 2);
            var result = QueryVolume(workspace);

            Assert.IsTrue(result.IsSaturated);
            Assert.IsFalse(result.IsComplete);
            Assert.AreEqual(2, workspace.Capacity);
            Assert.AreEqual(2, workspace.MaximumCapacity);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2, workspace.ContactCount);
        }

        [Test]
        public void TEST_PhysicsDeliveryWorkspaceBase_증설후모든접촉결과를조회()
        {
            CreateTargets();
            var workspace = CreateWorkspace(1, 8);
            var result = QueryVolume(workspace);

            Assert.IsTrue(result.IsComplete);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(3, workspace.ContactCount);
            Assert.AreEqual(4, workspace.Capacity);
            Assert.AreNotEqual(default(TContact), workspace.GetContact(0));
            Assert.AreNotEqual(default(TContact), workspace.GetContact(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => workspace.GetContact(3));
        }

    #endregion

    #region W-2: Contact 수명

        [Test]
        public void TEST_PhysicsDeliveryWorkspaceBase_다음질의후이전Contact를읽을수없음()
        {
            CreateTargets();
            var workspace = CreateWorkspace(1, 8);

            Assert.AreEqual(3, QueryVolume(workspace).Count);
            Assert.AreEqual(3, workspace.ContactCount);
            ClearByQuery(workspace);

            Assert.AreEqual(0, workspace.ContactCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => workspace.GetContact(0));
        }

    #endregion

    #region W-3: 중첩 질의 복구

        [Test]
        public void TEST_PhysicsDeliveryWorkspaceBase_중첩질의실패후재사용가능()
        {
            CreateTargets();
            var workspace = CreateWorkspace(1, 8);

            Assert.Throws<InvalidOperationException>(() => RunReentrantQuery(workspace));
            Assert.AreEqual(0, workspace.ContactCount);

            var retry = QueryVolume(workspace);
            Assert.IsTrue(retry.IsComplete);
            Assert.AreEqual(3, retry.Count);
            Assert.AreEqual(3, workspace.ContactCount);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 공통 Workspace 계약을 Unity 2D Physics 구현에 연결한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_PhysicsDeliveryWorkspace2D :
        TEST_PhysicsDeliveryWorkspaceBase<Collider2D, RaycastHit2D, DeliveryContact2D>
    {

    #region 픽스처

        private readonly PhysicsDelivery2D delivery = new();
        private static readonly Vector2 origin = new(32000f, 32000f);
        private static readonly ContactFilter2D contactFilter = new()
        {
            useLayerMask = true,
            layerMask = 1 << 30,
            useTriggers = true
        };

        protected override PhysicsDeliveryWorkspaceBase<Collider2D, RaycastHit2D, DeliveryContact2D> CreateWorkspace
        (
            int capacity,
            int maximumCapacity
        ) => new PhysicsDeliveryWorkspace2D(capacity, maximumCapacity);

        protected override void CreateTargets()
        {
            for (var index = 0; index < 3; index++)
            {
                CreateObject(new Vector3(origin.x + index * 0.2f, origin.y, 0f)).AddComponent<CircleCollider2D>();
            }

            Physics2D.SyncTransforms();
        }

        protected override DeliveryBatchResult QueryVolume(PhysicsDeliveryWorkspaceBase<Collider2D, RaycastHit2D, DeliveryContact2D> workspace)
        {
            var volume = PhysicsVolume2D.CreateCircle(origin, 2f);
            return delivery.QueryVolume(volume, contactFilter, (PhysicsDeliveryWorkspace2D)workspace);
        }

        protected override void ClearByQuery(PhysicsDeliveryWorkspaceBase<Collider2D, RaycastHit2D, DeliveryContact2D> workspace)
        {
            var volume = PhysicsVolume2D.CreateCircle(origin, 2f);
            var result = delivery.QueryCast(volume, Vector2.zero, contactFilter, (PhysicsDeliveryWorkspace2D)workspace);
            Assert.IsFalse(result.HasContact);
        }

        protected override void RunReentrantQuery(PhysicsDeliveryWorkspaceBase<Collider2D, RaycastHit2D, DeliveryContact2D> workspace)
        {
            var volume = PhysicsVolume2D.CreateCircle(origin, 2f);
            var typedWorkspace = (PhysicsDeliveryWorkspace2D)workspace;
            var filter = new ReentrantFilter(() => delivery.QueryVolume(volume, contactFilter, typedWorkspace));
            delivery.QueryVolume(volume, contactFilter, typedWorkspace, filter);
        }

        private sealed class ReentrantFilter : IDeliveryColliderFilter2D
        {
            private readonly Action reenter;

            public ReentrantFilter(Action reenter) => this.reenter = reenter;

            public bool IsAccepted(Collider2D collider)
            {
                reenter();
                return true;
            }
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 공통 Workspace 계약을 Unity 3D Physics 구현에 연결한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_PhysicsDeliveryWorkspace3D :
        TEST_PhysicsDeliveryWorkspaceBase<Collider, RaycastHit, DeliveryContact3D>
    {

    #region 픽스처

        private readonly PhysicsDelivery3D delivery = new();
        private static readonly Vector3 origin = new(32000f, 32000f, 32000f);
        private const int queryLayers = 1 << 30;

        protected override PhysicsDeliveryWorkspaceBase<Collider, RaycastHit, DeliveryContact3D> CreateWorkspace
        (
            int capacity,
            int maximumCapacity
        ) => new PhysicsDeliveryWorkspace3D(capacity, maximumCapacity);

        protected override void CreateTargets()
        {
            for (var index = 0; index < 3; index++)
            {
                CreateObject(origin + Vector3.right * (index * 0.2f)).AddComponent<SphereCollider>();
            }

            Physics.SyncTransforms();
        }

        protected override DeliveryBatchResult QueryVolume(PhysicsDeliveryWorkspaceBase<Collider, RaycastHit, DeliveryContact3D> workspace)
        {
            var volume = PhysicsVolume3D.CreateSphere(origin, 2f);
            return delivery.QueryVolume(volume, queryLayers, (PhysicsDeliveryWorkspace3D)workspace);
        }

        protected override void ClearByQuery(PhysicsDeliveryWorkspaceBase<Collider, RaycastHit, DeliveryContact3D> workspace)
        {
            var volume = PhysicsVolume3D.CreateSphere(origin, 2f);
            var result = delivery.QueryCast(volume, Vector3.zero, queryLayers, (PhysicsDeliveryWorkspace3D)workspace);
            Assert.IsFalse(result.HasContact);
        }

        protected override void RunReentrantQuery(PhysicsDeliveryWorkspaceBase<Collider, RaycastHit, DeliveryContact3D> workspace)
        {
            var volume = PhysicsVolume3D.CreateSphere(origin, 2f);
            var typedWorkspace = (PhysicsDeliveryWorkspace3D)workspace;
            var filter = new ReentrantFilter(() => delivery.QueryVolume(volume, queryLayers, typedWorkspace));
            delivery.QueryVolume(volume, queryLayers, typedWorkspace, filter);
        }

        private sealed class ReentrantFilter : IDeliveryColliderFilter3D
        {
            private readonly Action reenter;

            public ReentrantFilter(Action reenter) => this.reenter = reenter;

            public bool IsAccepted(Collider collider)
            {
                reenter();
                return true;
            }
        }

    #endregion

    }
}
