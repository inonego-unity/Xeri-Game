/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Delivery3D.cs
수정일 : 2026-11-12

# 설명
PhysicsDelivery3D의 Box·Sphere·Capsule Query와 ProjectileDelivery3D의
시작 겹침·선형 이동·시간·검색 완전성 계약을 실제 Unity Physics 및 가짜 질의로 검증한다.

# 테스트 구성
 P: Projectile 이동·접촉·수명 계약
 Q: Physics 질의·형상·포화·필터·실제 접촉 계약
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

    public sealed class TEST_Delivery3D : DeliveryTestFixtureBase
    {

    #region 헬퍼

        private readonly PhysicsDeliveryWorkspace3D queryWorkspace = new(8, 8);

        private Collider CreateCollider(Vector3 position)
        {
            var gameObject = CreateObject(position);
            return gameObject.AddComponent<SphereCollider>();
        }

        private sealed class RejectColliderFilter : IDeliveryColliderFilter3D
        {
            private readonly Collider rejected;

            public RejectColliderFilter(Collider rejected)
            {
                this.rejected = rejected;
            }

            public bool IsAccepted(Collider collider) => collider != rejected;
        }


        private sealed class FakeQuery : DeliveryFakeQueryState<DeliveryContact3D>, IProjectileDeliveryQuery3D
        {
            public DeliveryContactQueryResult3D QueryAnyOverlap
            (
                in PhysicsVolume3D volume,
                LayerMask layerMask,
                PhysicsDeliveryWorkspace3D workspace,
                IDeliveryColliderFilter3D filter = null
            )
            {
                RecordOverlap();
                return new DeliveryContactQueryResult3D(HasOverlap, IsOverlapComplete, OverlapContact);
            }

            public DeliveryContactQueryResult3D QueryCast
            (
                in PhysicsVolume3D volume,
                Vector3 displacement,
                LayerMask layerMask,
                PhysicsDeliveryWorkspace3D workspace,
                IDeliveryColliderFilter3D filter = null
            )
            {
                RecordCast();
                return new DeliveryContactQueryResult3D(HasCast, IsCastComplete, CastContact);
            }
        }

        private static ProjectileDelivery3D CreateProjectile(Vector3 velocity, float lifetime = 1f)
        {
            var volume = PhysicsVolume3D.CreateSphere
            (
                Vector3.zero,
                0.25f
            );
            var movingVolume = new MovingVolume3D
            (
                volume,
                velocity,
                lifetime
            );
            return new ProjectileDelivery3D(movingVolume, ~0);
        }

    #endregion

    #region P-1: 이동 / Lifetime

        [Test]
        public void TEST_ProjectileDelivery3D_Contact없으면이동하고Lifetime에서만료()
        {
            var delivery = CreateProjectile(new Vector3(2f, 0f, 0f));
            var query = new FakeQuery();

            var first = delivery.Advance
            (
                0.25f,
                query,
                queryWorkspace
            );

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, first.Status);
            Assert.AreEqual
            (
                new Vector3(0.5f, 0f, 0f),
                delivery.MovingVolume.Volume.Position
            );

            var second = delivery.Advance
            (
                0.75f,
                query,
                queryWorkspace
            );

            Assert.AreEqual(ProjectileDeliveryStepStatus.Expired, second.Status);
            Assert.AreEqual
            (
                new Vector3(2f, 0f, 0f),
                delivery.MovingVolume.Volume.Position
            );
            Assert.IsTrue(delivery.IsExpired);
        }

    #endregion

    #region P-2: Zero Delta

        [Test]
        public void TEST_ProjectileDelivery3D_ZeroDelta는Active상태를유지()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery();

            var result = delivery.Advance
            (
                0f,
                query,
                queryWorkspace
            );

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, result.Status);
            Assert.IsFalse(delivery.IsExpired);
            Assert.AreEqual(Vector3.zero, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(0, query.CastCount);
        }

    #endregion

    #region P-3: Cast Contact

        [Test]
        public void TEST_ProjectileDelivery3D_CastContact에서Direction을확정()
        {
            var delivery = CreateProjectile(new Vector3(3f, 0f, 0f));
            var query = new FakeQuery
            {
                HasCast = true,
                CastContact = new DeliveryContact3D
                (
                    CreateCollider(new Vector3(20000f, 20000f, 20000f)),
                    new Vector3(0.4f, 0f, 0f),
                    Vector3.left,
                    Vector3.zero,
                    0.4f
                )
            };

            var result = delivery.Advance
            (
                0.5f,
                query,
                queryWorkspace
            );

            Assert.AreEqual(ProjectileDeliveryStepStatus.Contact, result.Status);
            Assert.AreEqual(Vector3.right, result.Contact.Direction);
            Assert.AreEqual(1, query.CastCount);
            Assert.That(delivery.MovingVolume.Volume.Position.x, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(delivery.MovingVolume.ElapsedTime, Is.EqualTo(0.4f / 3f).Within(0.0001f));
            Assert.That(result.ConsumedTime, Is.EqualTo(0.4f / 3f).Within(0.0001f));
            Assert.That(result.RemainingTime, Is.EqualTo(0.5f - 0.4f / 3f).Within(0.0001f));
        }

    #endregion

    #region P-4: Initial Overlap

        [Test]
        public void TEST_ProjectileDelivery3D_InitialOverlap에현재VelocityDirection을부여()
        {
            var delivery = CreateProjectile(new Vector3(0f, 0f, 4f));
            var query = new FakeQuery
            {
                HasOverlap = true,
                OverlapContact = new DeliveryContact3D
                (
                    CreateCollider(new Vector3(20000f, 20000f, 20000f)),
                    Vector3.zero,
                    Vector3.back,
                    Vector3.zero,
                    0f
                )
            };

            var contact = delivery.QueryInitialOverlap(query, queryWorkspace);
            Assert.IsTrue(contact.HasContact);
            Assert.AreEqual(Vector3.forward, contact.Contact.Direction);
            Assert.AreEqual(1, query.OverlapCount);
        }

    #endregion

    #region P-5: 시작 접촉과 수명 경계

        [Test]
        public void TEST_ProjectileDelivery3D_Advance가최초Overlap을자동검사()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery
            {
                HasOverlap = true,
                OverlapContact = new DeliveryContact3D
                (
                    CreateCollider(new Vector3(20000f, 20000f, 20000f)),
                    Vector3.zero,
                    Vector3.left,
                    Vector3.zero,
                    0f
                )
            };

            var result = delivery.Advance(0.5f, query, queryWorkspace);

            Assert.IsTrue(result.HasContact);
            Assert.AreEqual(1, query.OverlapCount);
            Assert.AreEqual(0, query.CastCount);
            Assert.AreEqual(0f, result.ConsumedTime);
            Assert.AreEqual(0.5f, result.RemainingTime);
            Assert.AreEqual(Vector3.zero, delivery.MovingVolume.Volume.Position);
        }

        [Test]
        public void TEST_ProjectileDelivery3D_명시적초기Overlap이후에도다음Sweep시작검사()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery();

            Assert.IsFalse(delivery.QueryInitialOverlap(query, queryWorkspace).HasContact);
            delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(1, query.CastCount);
        }

        [Test]
        public void TEST_ProjectileDelivery3D_수명끝경계Contact에서상태동시보고()
        {
            var delivery = CreateProjectile(new Vector3(4f, 0f, 0f), 0.2f);
            var query = new FakeQuery
            {
                HasCast = true,
                CastContact = new DeliveryContact3D
                (
                    CreateCollider(new Vector3(20000f, 20000f, 20000f)),
                    new Vector3(0.8f, 0f, 0f),
                    Vector3.left,
                    Vector3.zero,
                    0.8f
                )
            };

            var result = delivery.Advance(1f, query, queryWorkspace);

            Assert.IsTrue(result.HasContact);
            Assert.IsTrue(result.IsExpired);
            Assert.IsTrue(delivery.IsExpired);
            Assert.That(result.ConsumedTime, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(result.RemainingTime, Is.EqualTo(0.8f).Within(0.0001f));
        }

    #endregion

    #region P-6: 검색 미완료와 시간 보존

        [Test]
        public void TEST_ProjectileDelivery3D_시작검색이불완전하면상태를보존()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery
            {
                IsOverlapComplete = false
            };

            var failed = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Incomplete, failed.Status);
            Assert.IsFalse(failed.IsComplete);
            Assert.AreEqual(0f, failed.ConsumedTime);
            Assert.AreEqual(0.25f, failed.RemainingTime);
            Assert.AreEqual(Vector3.zero, delivery.MovingVolume.Volume.Position);

            query.IsOverlapComplete = true;
            var successful = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, successful.Status);
            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(0.25f, delivery.MovingVolume.ElapsedTime);
        }

        [Test]
        public void TEST_ProjectileDelivery3D_Cast검색이불완전하면이동을보류()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery
            {
                IsCastComplete = false
            };

            var failed = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Incomplete, failed.Status);
            Assert.AreEqual(Vector3.zero, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(0f, delivery.MovingVolume.ElapsedTime);

            query.IsCastComplete = true;
            var successful = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, successful.Status);
            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(2, query.CastCount);
        }

        [Test]
        public void TEST_ProjectileDelivery3D_만료후추가Tick은요청시간을보존()
        {
            var delivery = CreateProjectile(Vector3.right, 0.2f);
            var query = new FakeQuery();
            delivery.Advance(0.2f, query, queryWorkspace);

            var result = delivery.Advance(0.4f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Expired, result.Status);
            Assert.AreEqual(0f, result.ConsumedTime);
            Assert.AreEqual(0.4f, result.RemainingTime);
            Assert.AreEqual(0.2f, delivery.MovingVolume.ElapsedTime);
        }

        [Test]
        public void TEST_ProjectileDelivery3D_속도변경은시간을소비하지않음()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery();

            delivery.SetVelocity(Vector3.up * 2f);
            var result = delivery.Advance(0.2f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, result.Status);
            Assert.AreEqual(Vector3.up * 0.4f, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(Vector3.up * 2f, delivery.MovingVolume.Velocity);
            Assert.That(result.ConsumedTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void TEST_ProjectileDelivery3D_포화된Cast후보가있어도이동상태를보존()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery
            {
                HasCast = true,
                IsCastComplete = false,
                CastContact = new DeliveryContact3D
                (
                    CreateCollider(new Vector3(20000f, 20000f, 20000f)),
                    Vector3.zero,
                    Vector3.left,
                    Vector3.right,
                    0.1f
                )
            };

            var result = delivery.Advance(0.5f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Incomplete, result.Status);
            Assert.IsFalse(result.HasContact);
            Assert.AreEqual(0f, result.ConsumedTime);
            Assert.AreEqual(0.5f, result.RemainingTime);
            Assert.AreEqual(Vector3.zero, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(0f, delivery.MovingVolume.ElapsedTime);
        }

    #endregion

    #region P-7: 후속 Sweep 시작 겹침

        [Test]
        public void TEST_ProjectileDelivery3D_두번째Sweep시작에새로겹친대상을검출()
        {
            var delivery = CreateProjectile(Vector3.right);
            var query = new FakeQuery();

            var first = delivery.Advance(0.25f, query, queryWorkspace);
            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, first.Status);
            Assert.AreEqual(1, query.OverlapCount);

            query.HasOverlap = true;
            query.OverlapContact = new DeliveryContact3D
            (
                CreateCollider(new Vector3(20000f, 20000f, 20000f)),
                delivery.MovingVolume.Volume.Position,
                Vector3.left,
                Vector3.zero,
                0f,
                hasSurfaceGeometry: false
            );

            var second = delivery.Advance(0.25f, query, queryWorkspace);
            Assert.AreEqual(ProjectileDeliveryStepStatus.Contact, second.Status);
            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(1, query.CastCount);
            Assert.AreEqual(0f, second.ConsumedTime);
            Assert.AreEqual(0.25f, second.RemainingTime);
            Assert.AreEqual(0.25f, delivery.MovingVolume.ElapsedTime);
        }

    #endregion

    #region Q-1: Box/Sphere/Capsule Physics Queries

        [Test]
        public void TEST_PhysicsDelivery3D_BoxSphereCapsule_Overlap()
        {
            var origin = new Vector3(38000f, 38000f, 38000f);
            var target = CreateCollider(origin);
            Physics.SyncTransforms();

            var delivery = new PhysicsDelivery3D();
            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);
            var box = PhysicsVolume3D.CreateBox(origin, Vector3.one * 2f, Quaternion.Euler(0f, 30f, 0f));
            var sphere = PhysicsVolume3D.CreateSphere(origin, 1f);
            var capsule = PhysicsVolume3D.CreateCapsule(origin, 0.5f, 2f, Quaternion.Euler(0f, 0f, 20f));
            var volumes = new[]
            {
                box,
                sphere,
                capsule
            };

            foreach (var volume in volumes)
            {
                var batch = delivery.QueryVolume(volume, (LayerMask)(1 << 30), workspace);
                Assert.IsTrue(batch.IsComplete);
                Assert.AreEqual(1, batch.Count);
                Assert.AreSame(target, workspace.GetContact(0).Recipient);
                Assert.IsFalse(workspace.GetContact(0).HasSurfaceGeometry);
            }
        }

        [Test]
        public void TEST_PhysicsDelivery3D_BoxSphereCapsule_Cast()
        {
            var origin = new Vector3(40000f, 40000f, 40000f);
            var target = CreateCollider(origin + Vector3.right * 3f);
            Physics.SyncTransforms();

            var delivery = new PhysicsDelivery3D();
            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);
            var box = PhysicsVolume3D.CreateBox(origin, Vector3.one * 0.5f, Quaternion.identity);
            var sphere = PhysicsVolume3D.CreateSphere(origin, 0.25f);
            var capsule = PhysicsVolume3D.CreateCapsule(origin, 0.25f, 1f, Quaternion.identity);
            var volumes = new[]
            {
                box,
                sphere,
                capsule
            };

            foreach (var volume in volumes)
            {
                var result = delivery.QueryCast(volume, Vector3.right * 5f, (LayerMask)(1 << 30), workspace);
                Assert.IsTrue(result.IsComplete);
                Assert.IsTrue(result.HasContact);
                Assert.AreSame(target, result.Contact.Recipient);
                Assert.IsTrue(result.Contact.HasSurfaceGeometry);
                Assert.That(result.Contact.Distance, Is.GreaterThan(1f).And.LessThan(3f));
            }
        }

    #endregion

    #region Q-2: Physics 포화와 독립 Workspace

        [Test]
        public void TEST_PhysicsDelivery3D_포화시완전하지않은Nearest를확정하지않음()
        {
            var origin = new Vector3(12000f, 12000f, 12000f);
            CreateCollider(origin + new Vector3(0f, 0f, 2f));
            CreateCollider(origin + new Vector3(0f, 0f, 4f));
            CreateCollider(origin + new Vector3(0f, 0f, 6f));
            Physics.SyncTransforms();

            var delivery = new PhysicsDelivery3D();
            var layerMask = (LayerMask)(1 << 30);
            var limited = new PhysicsDeliveryWorkspace3D(1, 2);
            var partial = delivery.QueryTrace(origin, Vector3.forward, 10f, layerMask, limited);

            Assert.IsFalse(partial.IsComplete);
            Assert.IsTrue(partial.HasContact);
            Assert.IsTrue(partial.Contact.IsValid);

            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);
            var complete = delivery.QueryTrace
            (
                origin, Vector3.forward, 10f, layerMask, workspace
            );

            Assert.IsTrue(complete.IsComplete);
            Assert.IsTrue(complete.HasContact);
            Assert.That(complete.Contact.Distance, Is.EqualTo(1.5f).Within(0.001f));
            Assert.AreEqual(4, workspace.Capacity);
            Assert.AreEqual(2, limited.Capacity);
        }

        [Test]
        public void TEST_PhysicsDelivery3D_Volume부분결과와Workspace완전결과를구별()
        {
            var origin = new Vector3(14000f, 14000f, 14000f);
            CreateCollider(origin);
            CreateCollider(origin + new Vector3(0.2f, 0f, 0f));
            CreateCollider(origin + new Vector3(0.4f, 0f, 0f));
            Physics.SyncTransforms();

            var delivery = new PhysicsDelivery3D();
            var volume = PhysicsVolume3D.CreateSphere(origin, 2f);
            var layerMask = (LayerMask)(1 << 30);
            var limited = new PhysicsDeliveryWorkspace3D(1, 2);
            var partial = delivery.QueryVolume(volume, layerMask, limited);

            Assert.IsTrue(partial.IsSaturated);
            Assert.IsFalse(partial.IsComplete);

            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);
            var complete = delivery.QueryVolume(volume, layerMask, workspace);

            Assert.IsTrue(complete.IsComplete);
            Assert.AreEqual(3, complete.Count);
            Assert.IsFalse(workspace.GetContact(0).HasSurfaceGeometry);
        }

        [Test]
        public void TEST_PhysicsDelivery3D_가장가까운거부대상다음Contact선택()
        {
            var origin = new Vector3(16000f, 16000f, 16000f);
            var rejected = CreateCollider(origin + new Vector3(0f, 0f, 2f));
            var accepted = CreateCollider(origin + new Vector3(0f, 0f, 4f));
            Physics.SyncTransforms();

            var delivery = new PhysicsDelivery3D();
            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);
            var query = delivery.QueryTrace
            (
                origin,
                Vector3.forward,
                10f,
                (LayerMask)(1 << 30),
                workspace,
                new RejectColliderFilter(rejected)
            );

            Assert.IsTrue(query.IsComplete);
            Assert.IsTrue(query.HasContact);
            Assert.AreSame(accepted, query.Contact.Recipient);
            Assert.That(query.Contact.Distance, Is.EqualTo(3.5f).Within(0.001f));
        }

    #endregion

    #region Q-3: Trigger 검색 정책

        [Test]
        public void TEST_PhysicsDelivery3D_Trigger검색정책을게임별지정()
        {
            var origin = new Vector3(20000f, 20000f, 20000f);
            CreateCollider(origin);
            var trigger = CreateCollider(origin + new Vector3(0.2f, 0f, 0f));
            trigger.isTrigger = true;
            Physics.SyncTransforms();

            var layerMask = (LayerMask)(1 << 30);
            var volume = PhysicsVolume3D.CreateSphere(origin, 2f);
            var include = new PhysicsDelivery3D(QueryTriggerInteraction.Collide);
            var exclude = new PhysicsDelivery3D(QueryTriggerInteraction.Ignore);
            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);

            Assert.AreEqual(2, include.QueryVolume(volume, layerMask, workspace).Count);
            Assert.AreEqual(1, exclude.QueryVolume(volume, layerMask, workspace).Count);
        }

    #endregion

    #region Q-4: Contact 결과 불변식

        [Test]
        public void TEST_DeliveryContactQueryResult3D_유효하지않은Contact성공결과를거부()
        {
            Assert.Throws<ArgumentException>
            (
                () => new DeliveryContactQueryResult3D(true, true, default)
            );

            var empty = new DeliveryContactQueryResult3D(false, true);
            Assert.IsFalse(empty.HasContact);
            Assert.IsTrue(empty.IsComplete);
        }

    #endregion

    #region Q-5: 실제 Projectile Contact와 시간

        [Test]
        public void TEST_ProjectileDelivery3D_RealPhysicsBoxSphereCapsuleContactTime()
        {
            var origin = new Vector3(42000f, 42000f, 42000f);
            var target = CreateCollider(origin + Vector3.right * 3f);
            Physics.SyncTransforms();

            var box = PhysicsVolume3D.CreateBox(origin, Vector3.one * 0.5f, Quaternion.identity);
            var sphere = PhysicsVolume3D.CreateSphere(origin, 0.25f);
            var capsule = PhysicsVolume3D.CreateCapsule(origin, 0.25f, 1f, Quaternion.identity);
            var volumes = new[]
            {
                box,
                sphere,
                capsule
            };
            var physics = new PhysicsDelivery3D();
            var workspace = new PhysicsDeliveryWorkspace3D(1, 8);

            foreach (var volume in volumes)
            {
                var projectile = new ProjectileDelivery3D
                (
                    new MovingVolume3D(volume, Vector3.right * 10f, 1f),
                    (LayerMask)(1 << 30)
                );
                var result = projectile.Advance(0.5f, physics, workspace);

                Assert.AreEqual(ProjectileDeliveryStepStatus.Contact, result.Status);
                Assert.AreSame(target, result.Contact.Recipient);
                Assert.IsTrue(result.Contact.HasSurfaceGeometry);
                Assert.That(result.ConsumedTime, Is.EqualTo(result.Contact.Distance / 10f).Within(0.002f));
                Assert.That(projectile.MovingVolume.ElapsedTime, Is.EqualTo(result.ConsumedTime).Within(0.002f));
                Assert.That(result.RemainingTime, Is.EqualTo(0.5f - result.ConsumedTime).Within(0.002f));
                Assert.That
                (
                    projectile.MovingVolume.Volume.Position.x - origin.x,
                    Is.EqualTo(result.Contact.Distance).Within(0.01f)
                );
            }
        }

    #endregion

    }
}
