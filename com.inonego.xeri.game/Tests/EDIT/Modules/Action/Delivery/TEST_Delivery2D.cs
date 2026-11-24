/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Delivery2D.cs
수정일 : 2026-11-12

# 설명
PhysicsDelivery2D의 Box·Circle·Capsule Query와 ProjectileDelivery2D의
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

    public sealed class TEST_Delivery2D : DeliveryTestFixtureBase
    {

    #region 헬퍼

        private readonly PhysicsDeliveryWorkspace2D queryWorkspace = new(8, 8);

        private static ContactFilter2D CreateFilter(bool useTriggers = true) => new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = 1 << 30,
            useTriggers = useTriggers
        };

        private Collider2D CreateCollider(Vector2 position)
        {
            var gameObject = CreateObject(new Vector3(position.x, position.y, 0f), "2D Delivery Physics Test");
            return gameObject.AddComponent<CircleCollider2D>();
        }

        private sealed class RejectColliderFilter : IDeliveryColliderFilter2D
        {
            private readonly Collider2D rejected;

            public RejectColliderFilter(Collider2D rejected)
            {
                this.rejected = rejected;
            }

            public bool IsAccepted(Collider2D collider) => collider != rejected;
        }


        private sealed class FakeQuery : DeliveryFakeQueryState<DeliveryContact2D>, IProjectileDeliveryQuery2D
        {
            public DeliveryContactQueryResult2D QueryAnyOverlap
            (
                in PhysicsVolume2D volume,
                ContactFilter2D contactFilter,
                PhysicsDeliveryWorkspace2D workspace,
                IDeliveryColliderFilter2D filter = null
            )
            {
                RecordOverlap();
                return new DeliveryContactQueryResult2D(HasOverlap, IsOverlapComplete, OverlapContact);
            }

            public DeliveryContactQueryResult2D QueryCast
            (
                in PhysicsVolume2D volume,
                Vector2 displacement,
                ContactFilter2D contactFilter,
                PhysicsDeliveryWorkspace2D workspace,
                IDeliveryColliderFilter2D filter = null
            )
            {
                RecordCast();
                return new DeliveryContactQueryResult2D(HasCast, IsCastComplete, CastContact);
            }
        }

        private static ProjectileDelivery2D CreateProjectile(Vector2 velocity, float lifetime = 1f)
        {
            var volume = PhysicsVolume2D.CreateCircle(Vector2.zero, 0.25f);
            var movingVolume = new MovingVolume2D(volume, velocity, lifetime);
            return new ProjectileDelivery2D(movingVolume, CreateFilter());
        }

    #endregion

    #region P-1: 이동 / Lifetime

        [Test]
        public void TEST_ProjectileDelivery2D_Contact없으면이동하고Lifetime에서만료()
        {
            var delivery = CreateProjectile(new Vector2(2f, 0f));
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
                new Vector2(0.5f, 0f),
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
                new Vector2(2f, 0f),
                delivery.MovingVolume.Volume.Position
            );
            Assert.IsTrue(delivery.IsExpired);
        }

    #endregion

    #region P-2: Zero Delta

        [Test]
        public void TEST_ProjectileDelivery2D_ZeroDelta는Active상태를유지()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery();

            var result = delivery.Advance
            (
                0f,
                query,
                queryWorkspace
            );

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, result.Status);
            Assert.IsFalse(delivery.IsExpired);
            Assert.AreEqual(Vector2.zero, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(0, query.CastCount);
        }

    #endregion

    #region P-3: Cast Contact

        [Test]
        public void TEST_ProjectileDelivery2D_CastContact에서Direction을확정()
        {
            var delivery = CreateProjectile(new Vector2(3f, 0f));
            var query = new FakeQuery
            {
                HasCast = true,
                CastContact = new DeliveryContact2D
                (
                    CreateCollider(new Vector2(20000f, 20000f)),
                    new Vector2(0.4f, 0f),
                    Vector2.left,
                    Vector2.zero,
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
            Assert.AreEqual(Vector2.right, result.Contact.Direction);
            Assert.AreEqual(1, query.CastCount);
            Assert.That(delivery.MovingVolume.Volume.Position.x, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(delivery.MovingVolume.ElapsedTime, Is.EqualTo(0.4f / 3f).Within(0.0001f));
            Assert.That(result.ConsumedTime, Is.EqualTo(0.4f / 3f).Within(0.0001f));
            Assert.That(result.RemainingTime, Is.EqualTo(0.5f - 0.4f / 3f).Within(0.0001f));
        }

    #endregion

    #region P-4: Initial Overlap

        [Test]
        public void TEST_ProjectileDelivery2D_InitialOverlap에현재VelocityDirection을부여()
        {
            var delivery = CreateProjectile(new Vector2(0f, 4f));
            var query = new FakeQuery
            {
                HasOverlap = true,
                OverlapContact = new DeliveryContact2D
                (
                    CreateCollider(new Vector2(20000f, 20000f)),
                    Vector2.zero,
                    Vector2.down,
                    Vector2.zero,
                    0f
                )
            };

            var contact = delivery.QueryInitialOverlap(query, queryWorkspace);
            Assert.IsTrue(contact.HasContact);
            Assert.AreEqual(Vector2.up, contact.Contact.Direction);
            Assert.AreEqual(1, query.OverlapCount);
        }

    #endregion

    #region P-5: 시작 접촉과 수명 경계

        [Test]
        public void TEST_ProjectileDelivery2D_Advance가최초Overlap을자동검사()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery
            {
                HasOverlap = true,
                OverlapContact = new DeliveryContact2D
                (
                    CreateCollider(new Vector2(20000f, 20000f)),
                    Vector2.zero,
                    Vector2.left,
                    Vector2.zero,
                    0f
                )
            };

            var result = delivery.Advance(0.5f, query, queryWorkspace);

            Assert.IsTrue(result.HasContact);
            Assert.AreEqual(1, query.OverlapCount);
            Assert.AreEqual(0, query.CastCount);
            Assert.AreEqual(0f, result.ConsumedTime);
            Assert.AreEqual(0.5f, result.RemainingTime);
            Assert.AreEqual(Vector2.zero, delivery.MovingVolume.Volume.Position);
        }

        [Test]
        public void TEST_ProjectileDelivery2D_명시적초기Overlap이후에도다음Sweep시작검사()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery();

            Assert.IsFalse(delivery.QueryInitialOverlap(query, queryWorkspace).HasContact);
            delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(1, query.CastCount);
        }

        [Test]
        public void TEST_ProjectileDelivery2D_수명끝경계Contact에서상태동시보고()
        {
            var delivery = CreateProjectile(new Vector2(4f, 0f), 0.2f);
            var query = new FakeQuery
            {
                HasCast = true,
                CastContact = new DeliveryContact2D
                (
                    CreateCollider(new Vector2(20000f, 20000f)),
                    new Vector2(0.8f, 0f),
                    Vector2.left,
                    Vector2.zero,
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
        public void TEST_ProjectileDelivery2D_시작검색이불완전하면상태를보존()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery
            {
                IsOverlapComplete = false
            };

            var failed = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Incomplete, failed.Status);
            Assert.IsFalse(failed.IsComplete);
            Assert.AreEqual(0f, failed.ConsumedTime);
            Assert.AreEqual(0.25f, failed.RemainingTime);
            Assert.AreEqual(Vector2.zero, delivery.MovingVolume.Volume.Position);

            query.IsOverlapComplete = true;
            var successful = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, successful.Status);
            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(0.25f, delivery.MovingVolume.ElapsedTime);
        }

        [Test]
        public void TEST_ProjectileDelivery2D_Cast검색이불완전하면이동을보류()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery
            {
                IsCastComplete = false
            };

            var failed = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Incomplete, failed.Status);
            Assert.AreEqual(Vector2.zero, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(0f, delivery.MovingVolume.ElapsedTime);

            query.IsCastComplete = true;
            var successful = delivery.Advance(0.25f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, successful.Status);
            Assert.AreEqual(2, query.OverlapCount);
            Assert.AreEqual(2, query.CastCount);
        }

        [Test]
        public void TEST_ProjectileDelivery2D_만료후추가Tick은요청시간을보존()
        {
            var delivery = CreateProjectile(Vector2.right, 0.2f);
            var query = new FakeQuery();
            delivery.Advance(0.2f, query, queryWorkspace);

            var result = delivery.Advance(0.4f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Expired, result.Status);
            Assert.AreEqual(0f, result.ConsumedTime);
            Assert.AreEqual(0.4f, result.RemainingTime);
            Assert.AreEqual(0.2f, delivery.MovingVolume.ElapsedTime);
        }

        [Test]
        public void TEST_ProjectileDelivery2D_속도변경은시간을소비하지않음()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery();

            delivery.SetVelocity(Vector2.up * 2f);
            var result = delivery.Advance(0.2f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, result.Status);
            Assert.AreEqual(Vector2.up * 0.4f, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(Vector2.up * 2f, delivery.MovingVolume.Velocity);
            Assert.That(result.ConsumedTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void TEST_ProjectileDelivery2D_포화된Cast후보가있어도이동상태를보존()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery
            {
                HasCast = true,
                IsCastComplete = false,
                CastContact = new DeliveryContact2D
                (
                    CreateCollider(new Vector2(20000f, 20000f)),
                    Vector2.zero,
                    Vector2.left,
                    Vector2.right,
                    0.1f
                )
            };

            var result = delivery.Advance(0.5f, query, queryWorkspace);

            Assert.AreEqual(ProjectileDeliveryStepStatus.Incomplete, result.Status);
            Assert.IsFalse(result.HasContact);
            Assert.AreEqual(0f, result.ConsumedTime);
            Assert.AreEqual(0.5f, result.RemainingTime);
            Assert.AreEqual(Vector2.zero, delivery.MovingVolume.Volume.Position);
            Assert.AreEqual(0f, delivery.MovingVolume.ElapsedTime);
        }

    #endregion

    #region P-7: 후속 Sweep 시작 겹침

        [Test]
        public void TEST_ProjectileDelivery2D_두번째Sweep시작에새로겹친대상을검출()
        {
            var delivery = CreateProjectile(Vector2.right);
            var query = new FakeQuery();

            var first = delivery.Advance(0.25f, query, queryWorkspace);
            Assert.AreEqual(ProjectileDeliveryStepStatus.Active, first.Status);
            Assert.AreEqual(1, query.OverlapCount);

            query.HasOverlap = true;
            query.OverlapContact = new DeliveryContact2D
            (
                CreateCollider(new Vector2(20000f, 20000f)),
                delivery.MovingVolume.Volume.Position,
                Vector2.left,
                Vector2.zero,
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

    #region Q-1: 형상별 Overlap과 Cast

        [Test]
        public void TEST_PhysicsDelivery2D_BoxCircleCapsuleOverlap을지원()
        {
            var origin = new Vector2(12000f, 12000f);
            var target = CreateCollider(origin);
            Physics2D.SyncTransforms();

            var delivery = new PhysicsDelivery2D();
            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);
            var filter = CreateFilter();
            var box = PhysicsVolume2D.CreateBox(origin, Vector2.one * 2f, 25f);
            var circle = PhysicsVolume2D.CreateCircle(origin, 1f);
            var capsule = PhysicsVolume2D.CreateCapsule(origin, new Vector2(1f, 2f), CapsuleDirection2D.Vertical, 20f);

            var volumes = new[]
            {
                box,
                circle,
                capsule
            };

            foreach (var volume in volumes)
            {
                var batch = delivery.QueryVolume(volume, filter, workspace);
                Assert.IsTrue(batch.IsComplete);
                Assert.AreEqual(1, batch.Count);
                Assert.AreSame(target, workspace.GetContact(0).Recipient);
                Assert.IsFalse(workspace.GetContact(0).HasSurfaceGeometry);
            }
        }

        [Test]
        public void TEST_PhysicsDelivery2D_BoxCircleCapsuleCast에서실제접촉을검출()
        {
            var origin = new Vector2(14000f, 14000f);
            var target = CreateCollider(origin + Vector2.right * 3f);
            Physics2D.SyncTransforms();

            var delivery = new PhysicsDelivery2D();
            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);
            var filter = CreateFilter();
            var box = PhysicsVolume2D.CreateBox(origin, Vector2.one * 0.5f, 0f);
            var circle = PhysicsVolume2D.CreateCircle(origin, 0.25f);
            var capsule = PhysicsVolume2D.CreateCapsule(origin, new Vector2(0.5f, 1f), CapsuleDirection2D.Vertical, 0f);

            var volumes = new[]
            {
                box,
                circle,
                capsule
            };

            foreach (var volume in volumes)
            {
                var result = delivery.QueryCast(volume, Vector2.right * 5f, filter, workspace);
                Assert.IsTrue(result.IsComplete);
                Assert.IsTrue(result.HasContact);
                Assert.AreSame(target, result.Contact.Recipient);
                Assert.IsTrue(result.Contact.HasSurfaceGeometry);
                Assert.That(result.Contact.Distance, Is.GreaterThan(1f).And.LessThan(3f));
            }
        }

    #endregion

    #region Q-2: 포화 및 필터

        [Test]
        public void TEST_PhysicsDelivery2D_포화일때부정확한Nearest를확정하지않음()
        {
            var origin = new Vector2(16000f, 16000f);
            CreateCollider(origin + Vector2.right * 2f);
            CreateCollider(origin + Vector2.right * 4f);
            CreateCollider(origin + Vector2.right * 6f);
            Physics2D.SyncTransforms();

            var delivery = new PhysicsDelivery2D();
            var filter = CreateFilter();
            var limited = new PhysicsDeliveryWorkspace2D(1, 2);
            var incomplete = delivery.QueryTrace(origin, Vector2.right, 10f, filter, limited);
            Assert.IsFalse(incomplete.IsComplete);
            Assert.IsTrue(incomplete.HasContact);
            Assert.IsTrue(incomplete.Contact.IsValid);

            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);
            var complete = delivery.QueryTrace(origin, Vector2.right, 10f, filter, workspace);
            Assert.IsTrue(complete.IsComplete);
            Assert.IsTrue(complete.HasContact);
            Assert.That(complete.Contact.Distance, Is.EqualTo(1.5f).Within(0.001f));
            Assert.AreEqual(4, workspace.Capacity);
        }

        [Test]
        public void TEST_PhysicsDelivery2D_Volume포화는부분결과를명시()
        {
            var origin = new Vector2(18000f, 18000f);
            CreateCollider(origin);
            CreateCollider(origin + Vector2.right * 0.25f);
            CreateCollider(origin + Vector2.left * 0.25f);
            Physics2D.SyncTransforms();

            var delivery = new PhysicsDelivery2D();
            var volume = PhysicsVolume2D.CreateCircle(origin, 2f);
            var filter = CreateFilter();
            var limited = new PhysicsDeliveryWorkspace2D(1, 2);
            var partial = delivery.QueryVolume(volume, filter, limited);
            Assert.IsFalse(partial.IsComplete);
            Assert.IsTrue(partial.IsSaturated);

            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);
            var complete = delivery.QueryVolume(volume, filter, workspace);
            Assert.IsTrue(complete.IsComplete);
            Assert.AreEqual(3, complete.Count);
        }

        [Test]
        public void TEST_PhysicsDelivery2D_거부된가장가까운Collider다음Contact를찾음()
        {
            var origin = new Vector2(20000f, 20000f);
            var rejected = CreateCollider(origin + Vector2.right * 2f);
            var accepted = CreateCollider(origin + Vector2.right * 4f);
            Physics2D.SyncTransforms();

            var delivery = new PhysicsDelivery2D();
            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);
            var result = delivery.QueryTrace(origin, Vector2.right, 10f, CreateFilter(), workspace, new RejectColliderFilter(rejected));
            Assert.IsTrue(result.IsComplete);
            Assert.AreSame(accepted, result.Contact.Recipient);
            Assert.That(result.Contact.Distance, Is.EqualTo(3.5f).Within(0.001f));
        }

    #endregion

    #region Q-3: Trigger 검색 정책

        [Test]
        public void TEST_PhysicsDelivery2D_Trigger허용정책을ContactFilter로지정()
        {
            var origin = new Vector2(24000f, 24000f);
            CreateCollider(origin);
            var trigger = CreateCollider(origin + Vector2.right * 0.2f);
            trigger.isTrigger = true;
            Physics2D.SyncTransforms();

            var delivery = new PhysicsDelivery2D();
            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);
            var volume = PhysicsVolume2D.CreateCircle(origin, 2f);
            Assert.AreEqual(2, delivery.QueryVolume(volume, CreateFilter(true), workspace).Count);
            Assert.AreEqual(1, delivery.QueryVolume(volume, CreateFilter(false), workspace).Count);
        }

    #endregion

    #region Q-4: Contact 결과 불변식

        [Test]
        public void TEST_DeliveryContactQueryResult2D_유효하지않은성공Contact를거부()
        {
            Assert.Throws<ArgumentException>(() => new DeliveryContactQueryResult2D(true, true, default));
            var empty = new DeliveryContactQueryResult2D(false, true);
            Assert.IsTrue(empty.IsComplete);
            Assert.IsFalse(empty.HasContact);
        }

    #endregion

    #region Q-5: 실제 Projectile Contact와 시간

        [Test]
        public void TEST_ProjectileDelivery2D_실제2DPhysics로세형상접촉시간을확정()
        {
            var origin = new Vector2(26000f, 26000f);
            var target = CreateCollider(origin + Vector2.right * 3f);
            Physics2D.SyncTransforms();

            var box = PhysicsVolume2D.CreateBox(origin, Vector2.one * 0.5f, 0f);
            var circle = PhysicsVolume2D.CreateCircle(origin, 0.25f);
            var capsule = PhysicsVolume2D.CreateCapsule(origin, new Vector2(0.5f, 1f), CapsuleDirection2D.Vertical, 0f);
            var volumes = new[]
            {
                box,
                circle,
                capsule
            };
            var physics = new PhysicsDelivery2D();
            var workspace = new PhysicsDeliveryWorkspace2D(1, 8);

            foreach (var volume in volumes)
            {
                var projectile = new ProjectileDelivery2D
                (
                    new MovingVolume2D(volume, Vector2.right * 10f, 1f),
                    CreateFilter()
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
