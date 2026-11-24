/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_WorldObjectSceneBinding.cs
수정일 : 2026-11-16

# 설명
로컬 WorldObject의 초기 상태, 기존 상태 적용과 Scene Binding 수명을 검증한다.
ID 충돌은 연결 전에 거절하며, 연결 실패에는 역순 해제와 예약 반환을 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._World
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// WorldObject Store와 Scene Binding의 로컬 수명 계약을 검증한다.
    /// </summary>
    // ============================================================
    public class TEST_WorldObjectSceneBinding
    {

    #region 테스트 구현

        // ============================================================
        /// <summary>
        /// WorldObject의 지속 값을 저장하는 테스트 상태.
        /// </summary>
        // ============================================================
        [Serializable]
        private sealed class TestState : WorldObjectState
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 로컬 WorldObject의 지속 값.
            /// </summary>
            // ------------------------------------------------------------
            public int Value
            {
                get => value;
                set => this.value = value;
            }

            [SerializeField]
            private int value;

            // ------------------------------------------------------------
            /// <summary>
            /// 지정한 ID와 값으로 상태를 구성한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestState(string id, int value) : base(id)
            {
                this.value = value;
            }
        }

        // ============================================================
        /// <summary>
        /// Scene 객체와 영속 상태의 Binding을 검증한다.
        /// </summary>
        // ============================================================
        private sealed class TestWorldObject : IWorldObject, IWorldObjectCurrentStateReceiver
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Scene 객체의 안정 ID.
            /// </summary>
            // ------------------------------------------------------------
            public string ID { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// Scene 초기 상태 값.
            /// </summary>
            // ------------------------------------------------------------
            public int InitialValue { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 연결된 상태.
            /// </summary>
            // ------------------------------------------------------------
            public TestState State { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태를 새로 생성한 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int CreatedCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태를 연결한 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int BoundCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태를 해제한 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int UnboundCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 BindState 실패를 유도한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailBind { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 UnbindState 실패를 유도한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailUnbind { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 연결 해제 전에 예외를 발생시킨다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailUnbindBeforeClear { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 현재 상태 검증 실패를 유도한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailCurrentValidation { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 다음 현재 상태 적용 실패를 유도한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool FailCurrentApply { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 상태 적용 호출 횟수.
            /// </summary>
            // ------------------------------------------------------------
            public int CurrentApplyCount { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 초기 상태 생성 결과를 교체한다.
            /// </summary>
            // ------------------------------------------------------------
            public TestState CreatedState { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 생성 중 실행할 검증 동작.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnCreateState { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 연결 중 예약된 ID의 Store 등록을 검증한다.
            /// </summary>
            // ------------------------------------------------------------
            public Action OnBindState { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 각 객체의 연결·해제 순서를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public List<string> Trace { get; set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 지정 ID의 테스트 Scene 객체를 만든다.
            /// </summary>
            // ------------------------------------------------------------
            public TestWorldObject(string id, int initialValue = 0)
            {
                ID = id;
                InitialValue = initialValue;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Scene 기본값으로 지속 상태를 만든다.
            /// </summary>
            // ------------------------------------------------------------
            public WorldObjectState CreateState()
            {
                CreatedCount++;
                OnCreateState?.Invoke();
                return CreatedState ?? new TestState(ID, InitialValue);
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Runtime에 대상 상태를 연결한다.
            /// </summary>
            // ------------------------------------------------------------
            public void BindState(WorldObjectState state)
            {
                BoundCount++;
                State = (TestState)state;
                Trace?.Add($"{ID}.Bind");
                OnBindState?.Invoke();

                if (FailBind)
                {
                    throw new InvalidOperationException($"{ID} Bind failed");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// Runtime 상태 연결을 해제한다.
            /// </summary>
            // ------------------------------------------------------------
            public void UnbindState()
            {
                UnboundCount++;
                Trace?.Add($"{ID}.Unbind");

                if (FailUnbindBeforeClear)
                {
                    throw new InvalidOperationException($"{ID} Unbind failed before clear");
                }

                State = null;

                if (FailUnbind)
                {
                    throw new InvalidOperationException($"{ID} Unbind failed");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 확정 상태를 변경 없이 검증한다.
            /// </summary>
            // ------------------------------------------------------------
            public void ValidateCurrentState(WorldObjectState next)
            {
                if (next is not TestState || next.ID != ID || FailCurrentValidation)
                {
                    throw new ArgumentException("현재 상태 검증 실패");
                }
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 외부 확정 State를 참조하고 호출 횟수를 기록한다.
            /// </summary>
            // ------------------------------------------------------------
            public void ApplyCurrentState(WorldObjectState next)
            {
                CurrentApplyCount++;
                State = (TestState)next;

                if (FailCurrentApply)
                {
                    throw new InvalidOperationException("현재 상태 적용 실패");
                }
            }
        }

        // ============================================================
        /// <summary>
        /// 현재 상태 교체 인터페이스 없이 Scene 연결만 지원하는 객체.
        /// </summary>
        // ============================================================
        private sealed class SceneOnlyObject : IWorldObject
        {
            // ------------------------------------------------------------
            /// <summary>
            /// Scene 객체의 안정 ID.
            /// </summary>
            // ------------------------------------------------------------
            public string ID { get; }

            // ------------------------------------------------------------
            /// <summary>
            /// 현재 연결한 지속 상태.
            /// </summary>
            // ------------------------------------------------------------
            public WorldObjectState State { get; private set; }

            // ------------------------------------------------------------
            /// <summary>
            /// 상태 교체를 지원하지 않는 객체를 구성한다.
            /// </summary>
            // ------------------------------------------------------------
            public SceneOnlyObject(string id)
            {
                ID = id;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 지속 상태 참조를 연결한다.
            /// </summary>
            // ------------------------------------------------------------
            public void BindState(WorldObjectState state)
            {
                State = state;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 지속 상태 참조만 해제한다.
            /// </summary>
            // ------------------------------------------------------------
            public void UnbindState()
            {
                State = null;
            }
        }

        private static IWorldObject[] Objects(params IWorldObject[] objects) => objects;

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트 객체의 Factory를 통해 Scene 객체를 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        private static void Bind(WorldObjectSceneBinding binding, IEnumerable<IWorldObject> objects)
        {
            binding.Bind(objects, obj => ((TestWorldObject)obj).CreateState());
        }

    #endregion

    #region 초기 상태와 재연결

        // ------------------------------------------------------------
        /// <summary>
        /// Scene 초기값을 생성하고 다음 Scene에도 상태를 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_새_상태를_생성하고_재연결()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("switch", 3);

            Bind(binding, Objects(first));

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(1, first.CreatedCount);
            Assert.AreEqual(3, first.State.Value);
            Assert.IsTrue(store.TryGetState("switch", out var saved));

            first.State.Value = 7;
            binding.Unbind();

            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsNull(first.State);
            Assert.AreEqual(7, ((TestState)saved).Value);

            var second = new TestWorldObject("switch", 100);
            Bind(binding, Objects(second));

            Assert.AreEqual(0, second.CreatedCount);
            Assert.AreSame(saved, second.State);
            Assert.AreEqual(7, second.State.Value);

            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 기존 저장 상태를 사용하고 미로드 상태는 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_복원된_상태를_초기적용()
        {
            var door = new TestState("door", 44);
            var unloaded = new TestState("unloaded", 8);
            var savedStates = new WorldObjectState[]
            {
                door, unloaded
            };
            var store = new WorldObjectStateStore(savedStates);
            var binding = new WorldObjectSceneBinding(store);
            var obj = new TestWorldObject("door", 1);

            Bind(binding, Objects(obj));

            Assert.AreSame(door, obj.State);
            Assert.AreEqual(44, obj.State.Value);
            Assert.AreEqual(0, obj.CreatedCount);
            Assert.AreEqual(2, store.States.Count);
            Assert.IsTrue(store.TryGetState("unloaded", out var state));
            Assert.AreSame(unloaded, state);

            binding.Unbind();
            Assert.AreEqual(2, store.States.Count);
        }

    #endregion

    #region 유효성 검사

        // ------------------------------------------------------------
        /// <summary>
        /// 중복 저장 ID와 중복 Scene ID를 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_중복_ID를_Bind전에_거절()
        {
            var previous = new TestState("duplicate", 1);
            var duplicateStates = new WorldObjectState[]
            {
                previous,
                new TestState("duplicate", 2)
            };

            Assert.Throws<ArgumentException>(() => new WorldObjectStateStore(duplicateStates));

            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("duplicate", 1);
            var second = new TestWorldObject("duplicate", 2);

            Assert.Throws<InvalidOperationException>
            (
                () => Bind(binding, Objects(first, second))
            );

            Assert.AreEqual(0, first.BoundCount);
            Assert.AreEqual(0, second.BoundCount);
            Assert.AreEqual(0, store.States.Count);
            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 생성한 State의 ID 불일치도 연결 전에 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_잘못된_생성_State를_거절()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var obj = new TestWorldObject("local")
            {
                CreatedState = new TestState("other", 7)
            };

            Assert.Throws<InvalidOperationException>
            (
                () => Bind(binding, Objects(obj))
            );

            Assert.AreEqual(0, obj.BoundCount);
            Assert.AreEqual(0, store.States.Count);
        }

    #endregion

    #region Binding 사전 단계의 재진입

        // ------------------------------------------------------------
        /// <summary>
        /// CreateState가 호출되는 중에도 중첩 Bind·Unbind를 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_CreateState중_재진입을_거절()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var obj = new TestWorldObject("first");

            obj.OnCreateState = () =>
            {
                Assert.Throws<InvalidOperationException>
                (
                    () => Bind(binding, Objects(new TestWorldObject("nested")))
                );

                Assert.Throws<InvalidOperationException>(() => binding.Unbind());
            };

            Bind(binding, Objects(obj));

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(1, store.States.Count);
            Assert.IsTrue(store.TryGetState("first", out _));
            Assert.IsFalse(store.TryGetState("nested", out _));

            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 생성 실패 시 Binding이 재시도 가능한 상태로 돌아간다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_CreateState실패후_다시연결가능()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var obj = new TestWorldObject("first")
            {
                OnCreateState = () => throw new InvalidOperationException("create failure")
            };

            Assert.Throws<InvalidOperationException>(() => Bind(binding, Objects(obj)));
            Assert.AreEqual(0, store.States.Count);
            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));

            obj.OnCreateState = null;
            Bind(binding, Objects(obj));

            Assert.AreEqual(1, store.States.Count);
            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            binding.Unbind();
        }

    #endregion

    #region 실패 처리

        // ------------------------------------------------------------
        /// <summary>
        /// 연결 실패 시 부분 연결을 역순 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Bind실패는_부분연결을_롤백()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var trace = new List<string>();
            var first = new TestWorldObject("first")
            {
                Trace = trace
            };
            var second = new TestWorldObject("second")
            {
                Trace = trace,
                FailBind = true
            };

            Assert.Throws<InvalidOperationException>
            (
                () => Bind(binding, Objects(first, second))
            );

            CollectionAssert.AreEqual
            (
                new[]
                {
                    "first.Bind",
                    "second.Bind",
                    "second.Unbind",
                    "first.Unbind"
                },
                trace
            );

            Assert.IsNull(first.State);
            Assert.IsNull(second.State);
            Assert.AreEqual(0, store.States.Count);
            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));

            second.FailBind = false;
            Bind(binding, Objects(first, second));
            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(2, store.States.Count);
            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 실패에도 나머지 객체와 상태를 정리한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Unbind실패에도_종료()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("first")
            {
                FailUnbind = true
            };
            var second = new TestWorldObject("second");

            Bind(binding, Objects(first, second));

            Assert.Throws<AggregateException>(() => binding.Unbind());

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsNull(first.State);
            Assert.IsNull(second.State);
            Assert.AreEqual(1, first.UnboundCount);
            Assert.AreEqual(1, second.UnboundCount);
            Assert.AreEqual(2, store.States.Count);

            first.FailUnbind = false;
            binding.Unbind();

            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(2, first.UnboundCount);
            Assert.AreEqual(1, second.UnboundCount);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Binding과 롤백이 모두 실패한 원인을 함께 전달한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Bind와_롤백_실패를_함께_보고()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("first")
            {
                FailUnbind = true
            };
            var second = new TestWorldObject("second")
            {
                FailBind = true
            };

            var exception = Assert.Throws<AggregateException>
            (
                () => Bind(binding, Objects(first, second))
            );

            Assert.AreEqual(2, exception.InnerExceptions.Count);
            Assert.AreEqual("second Bind failed", exception.InnerExceptions[0].Message);
            Assert.AreEqual("first Unbind failed", exception.InnerExceptions[1].Message);
            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(0, store.States.Count);

            first.FailUnbind = false;
            binding.Unbind();
            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 해제 전 실패한 연결만 보존하고 해제 재시도로 수명을 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Unbind실패한_객체만_재시도()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("first")
            {
                FailUnbindBeforeClear = true
            };
            var second = new TestWorldObject("second");

            Bind(binding, Objects(first, second));

            Assert.Throws<AggregateException>(() => binding.Unbind());

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsNotNull(first.State);
            Assert.IsNull(second.State);
            Assert.Throws<InvalidOperationException>(() => Bind(binding, Objects(new TestWorldObject("other"))));

            first.FailUnbindBeforeClear = false;
            binding.Unbind();
            binding.Unbind();

            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsNull(first.State);
            Assert.AreEqual(2, first.UnboundCount);
            Assert.AreEqual(1, second.UnboundCount);
            Assert.AreEqual(2, store.States.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Binding 롤백도 해제하지 못한 객체의 재시도 권한을 보유한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Bind롤백_해제실패후_재시도()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("first")
            {
                FailUnbindBeforeClear = true
            };
            var second = new TestWorldObject("second")
            {
                FailBind = true
            };

            Assert.Throws<AggregateException>(() => Bind(binding, Objects(first, second)));

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsNotNull(first.State);
            Assert.IsNull(second.State);
            Assert.AreEqual(0, store.States.Count);

            first.FailUnbindBeforeClear = false;
            binding.Unbind();

            Assert.IsFalse((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.IsNull(first.State);

            second.FailBind = false;
            Bind(binding, Objects(first, second));

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(2, store.States.Count);
            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Binding의 중복 연결은 기존 상태를 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_연결중_중복_Bind를_거절()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("first");

            Bind(binding, Objects(first));

            Assert.Throws<InvalidOperationException>
            (
                () => Bind(binding, Objects(new TestWorldObject("second")))
            );

            Assert.IsTrue((binding.Status != WorldObjectBindingStatus.Unbound));
            Assert.AreEqual(1, first.BoundCount);
            Assert.AreEqual(1, store.States.Count);
            binding.Unbind();
        }

    #endregion

    #region Store 및 현재 상태 적용

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 등록한 Live State를 조회하고 같은 ID의 중복 등록을 거절한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectStateStore_직접등록_조회_중복검증()
        {
            var store = new WorldObjectStateStore();
            var original = new TestState("gate", 3);

            store.AddState(original);
            Assert.IsTrue(store.TryGetState("gate", out var stored));
            Assert.AreSame(original, stored);
            Assert.AreEqual(1, store.States.Count);

            Assert.Throws<InvalidOperationException>(() => store.AddState(new TestState("gate", 9)));
            Assert.Throws<ArgumentException>(() => store.AddState(null));
            Assert.IsTrue(store.TryGetState("gate", out stored));
            Assert.AreSame(original, stored);
            Assert.AreEqual(1, store.States.Count);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// State 확정 전 예약 중인 ID에는 직접 등록할 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectStateStore_바인딩중_예약된_ID의_직접등록_거절()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("gate");
            var denied = false;

            target.OnBindState = () =>
            {
                Assert.IsFalse(store.TryGetState("gate", out _));
                Assert.Throws<InvalidOperationException>(() => store.AddState(new TestState("gate", 8)));
                Assert.AreEqual(0, store.States.Count);
                denied = true;
            };

            Bind(binding, Objects(target));

            Assert.IsTrue(denied);
            Assert.AreEqual(WorldObjectBindingStatus.Bound, binding.Status);
            Assert.IsTrue(store.TryGetState("gate", out var stored));
            Assert.AreSame(target.State, stored);
            Assert.AreEqual(1, store.States.Count);
            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 일괄 예약 중 충돌하면 먼저 잡은 ID 예약을 누수 없이 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_부분_예약후_충돌하면_예약을_반환()
        {
            var store = new WorldObjectStateStore
            (
                new WorldObjectState[]
                {
                    new TestState("free", 1),
                    new TestState("busy", 2)
                }
            );
            var first = new WorldObjectSceneBinding(store);
            var second = new WorldObjectSceneBinding(store);
            first.Bind(Objects(new TestWorldObject("busy")));

            Assert.Throws<InvalidOperationException>
            (
                () => second.Bind(Objects(new TestWorldObject("free"), new TestWorldObject("busy")))
            );

            Assert.AreEqual(WorldObjectBindingStatus.Unbound, second.Status);
            Assert.IsTrue(store.RemoveState("free"));
            Assert.Throws<InvalidOperationException>(() => store.RemoveState("busy"));
            Assert.AreEqual(WorldObjectBindingStatus.Bound, first.Status);

            first.Unbind();
            Assert.IsTrue(store.RemoveState("busy"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결 중에는 State 교체·제거를 막고 해제 후에는 허용한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Store는_연결_종료후_교체와_삭제를_허용()
        {
            var store = new WorldObjectStateStore
            (
                new WorldObjectState[]
                {
                    new TestState("gate", 1)
                }
            );
            var binding = new WorldObjectSceneBinding(store);
            binding.Bind(Objects(new TestWorldObject("gate")));

            Assert.Throws<InvalidOperationException>(() => store.ReplaceState(new TestState("gate", 7)));
            Assert.Throws<InvalidOperationException>(() => store.RemoveState("gate"));
            binding.Unbind();

            var replacement = new TestState("gate", 7);
            store.ReplaceState(replacement);
            Assert.IsTrue(store.TryGetState("gate", out var saved));
            Assert.AreSame(replacement, saved);
            Assert.IsTrue(store.RemoveState("gate"));
            Assert.IsFalse(store.TryGetState("gate", out _));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 Scene 연결은 활성 수명을 생성하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_빈_연결은_Unbound를_유지()
        {
            var binding = new WorldObjectSceneBinding(new WorldObjectStateStore());

            binding.Bind(Array.Empty<IWorldObject>());
            Assert.AreEqual(WorldObjectBindingStatus.Unbound, binding.Status);
            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 서로 다른 Store의 같은 ID는 독립된 상태로 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_서로_다른_Scope에서_같은_ID허용()
        {
            var firstStates = new WorldObjectState[]
            {
                new TestState("gate", 1)
            };
            var secondStates = new WorldObjectState[]
            {
                new TestState("gate", 2)
            };
            var firstStore = new WorldObjectStateStore(firstStates);
            var secondStore = new WorldObjectStateStore(secondStates);
            var firstBinding = new WorldObjectSceneBinding(firstStore);
            var secondBinding = new WorldObjectSceneBinding(secondStore);
            var first = new TestWorldObject("gate");
            var second = new TestWorldObject("gate");

            firstBinding.Bind(Objects(first));
            secondBinding.Bind(Objects(second));

            Assert.AreEqual(1, first.State.Value);
            Assert.AreEqual(2, second.State.Value);
            firstBinding.Unbind();
            secondBinding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Store에서 서로 다른 ID를 별도로 연결해 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_서로_다른_Scene을_독립적으로_해제()
        {
            var store = new WorldObjectStateStore();
            var firstBinding = new WorldObjectSceneBinding(store);
            var secondBinding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("first", 4);
            var second = new TestWorldObject("second", 8);

            Bind(firstBinding, Objects(first));
            Bind(secondBinding, Objects(second));
            firstBinding.Unbind();

            Assert.AreEqual(WorldObjectBindingStatus.Unbound, firstBinding.Status);
            Assert.AreEqual(WorldObjectBindingStatus.Bound, secondBinding.Status);
            Assert.AreEqual(8, second.State.Value);
            Assert.AreEqual(2, store.States.Count);

            secondBinding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Store의 활성 객체 ID는 다른 연결이 점유할 수 없다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_동일_Scope_중복_예약을_거절()
        {
            var initialStates = new WorldObjectState[]
            {
                new TestState("gate", 1)
            };
            var store = new WorldObjectStateStore(initialStates);
            var firstBinding = new WorldObjectSceneBinding(store);
            var secondBinding = new WorldObjectSceneBinding(store);
            var first = new TestWorldObject("gate");
            var second = new TestWorldObject("gate");

            firstBinding.Bind(Objects(first));

            Assert.Throws<InvalidOperationException>
            (
                () => secondBinding.Bind(Objects(second))
            );

            Assert.AreEqual(0, second.BoundCount);
            Assert.Throws<InvalidOperationException>(() => store.RemoveState("gate"));
            Assert.Throws<InvalidOperationException>(() => store.ReplaceState(new TestState("gate", 2)));

            firstBinding.Unbind();
            secondBinding.Bind(Objects(second));
            Assert.AreEqual(1, second.State.Value);
            secondBinding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 역직렬화로 State ID가 바뀌어도 최초 예약한 ID를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_상태_ID가_바뀌어도_원래_예약을_해제()
        {
            var state = new TestState("gate", 1);
            var initialStates = new WorldObjectState[]
            {
                state
            };
            var store = new WorldObjectStateStore(initialStates);
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("gate");

            binding.Bind(Objects(target));
            JsonUtility.FromJsonOverwrite("{\"id\":\"changed\"}", state);
            Assert.AreEqual("changed", state.ID);

            binding.Unbind();

            Assert.AreEqual(WorldObjectBindingStatus.Unbound, binding.Status);
            Assert.IsTrue(store.RemoveState("gate"));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Store의 키와 State의 ID가 다르면 Scene 객체에 연결하기 전에 거절한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_Store의_ID_불일치를_Bind전에_거절()
        {
            var state = new TestState("gate", 1);
            var initialStates = new WorldObjectState[]
            {
                state
            };
            var store = new WorldObjectStateStore(initialStates);
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("gate");

            JsonUtility.FromJsonOverwrite("{\"id\":\"changed\"}", state);
            Assert.AreEqual("changed", state.ID);

            Assert.Throws<InvalidOperationException>(() => binding.Bind(Objects(target)));
            Assert.AreEqual(0, target.BoundCount);
            Assert.AreEqual(WorldObjectBindingStatus.Unbound, binding.Status);
            Assert.IsTrue(store.RemoveState("gate"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태 적용은 최초 연결 ID로 대상을 식별하고 정상 ID를 복구한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_확정상태_적용은_원래_ID로_찾는다()
        {
            var previous = new TestState("gate", 1);
            var initialStates = new WorldObjectState[]
            {
                previous
            };
            var store = new WorldObjectStateStore(initialStates);
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("gate");

            binding.Bind(Objects(target));
            JsonUtility.FromJsonOverwrite("{\"id\":\"changed\"}", previous);

            var next = new TestState("gate", 55);
            binding.ApplyCurrentState(next);

            Assert.AreSame(next, target.State);
            Assert.IsTrue(store.TryGetState("gate", out var stored));
            Assert.AreSame(next, stored);
            binding.Unbind();
            Assert.IsTrue(store.RemoveState("gate"));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 존재하지 않는 상태는 Factory가 없으면 생성하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_상태_없을때_자동생성을_금지()
        {
            var store = new WorldObjectStateStore();
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("missing");

            Assert.Throws<InvalidOperationException>
            (
                () => binding.Bind(Objects(target))
            );

            Assert.AreEqual(0, target.CreatedCount);
            Assert.AreEqual(0, target.BoundCount);
            Assert.AreEqual(0, store.States.Count);
            Assert.AreEqual(WorldObjectBindingStatus.Unbound, binding.Status);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 현재 상태 적용은 Scene과 Store 참조를 함께 바꾼다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_확정_현재상태를_적용()
        {
            var previous = new TestState("gate", 1);
            var initialStates = new WorldObjectState[]
            {
                previous
            };
            var store = new WorldObjectStateStore(initialStates);
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("gate");
            binding.Bind(Objects(target));

            var next = new TestState("gate", 22);
            binding.ApplyCurrentState(next);

            Assert.AreSame(next, target.State);
            Assert.IsTrue(store.TryGetState("gate", out var saved));
            Assert.AreSame(next, saved);
            Assert.AreEqual(1, target.CurrentApplyCount);
            Assert.AreEqual(WorldObjectBindingStatus.Bound, binding.Status);
            binding.Unbind();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 상태 교체를 지원하지 않는 Scene 객체는 기존 연결을 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_현재_상태_미지원은_기존_연결을_유지()
        {
            var state = new TestState("gate", 3);
            var store = new WorldObjectStateStore
            (
                new WorldObjectState[]
                {
                    state
                }
            );
            var binding = new WorldObjectSceneBinding(store);
            var target = new SceneOnlyObject("gate");
            binding.Bind(Objects(target));

            Assert.Throws<NotSupportedException>
            (
                () => binding.ApplyCurrentState(new TestState("gate", 4))
            );

            Assert.AreEqual(WorldObjectBindingStatus.Bound, binding.Status);
            Assert.AreSame(state, target.State);
            Assert.IsTrue(store.TryGetState("gate", out var stored));
            Assert.AreSame(state, stored);
            binding.Unbind();
            Assert.IsNull(target.State);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 검증 실패는 변경하지 않고 적용 실패는 정리를 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_WorldObjectSceneBinding_현재상태_검증과_적용실패를_구별()
        {
            var previous = new TestState("gate", 1);
            var initialStates = new WorldObjectState[]
            {
                previous
            };
            var store = new WorldObjectStateStore(initialStates);
            var binding = new WorldObjectSceneBinding(store);
            var target = new TestWorldObject("gate")
            {
                FailCurrentValidation = true
            };

            binding.Bind(Objects(target));

            Assert.Throws<ArgumentException>
            (
                () => binding.ApplyCurrentState(new TestState("gate", 2))
            );

            Assert.AreSame(previous, target.State);
            Assert.AreEqual(WorldObjectBindingStatus.Bound, binding.Status);

            target.FailCurrentValidation = false;
            target.FailCurrentApply = true;

            Assert.Throws<InvalidOperationException>
            (
                () => binding.ApplyCurrentState(new TestState("gate", 3))
            );

            Assert.AreEqual(WorldObjectBindingStatus.CleanupPending, binding.Status);
            Assert.IsTrue(store.TryGetState("gate", out var saved));
            Assert.AreSame(previous, saved);
            binding.Unbind();

            Assert.AreEqual(WorldObjectBindingStatus.Unbound, binding.Status);
            Assert.IsNull(target.State);
        }

    #endregion

    }
}
