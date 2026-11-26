/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveDirectory.cs
수정일 : 2026-11-26

# 설명
ObjectiveDirectory의 등록·조회, 읽기 기록 기반 변경 전파, 일괄 갱신·시간, 복원·반복과의 연결을 검증한다.

# 특이사항, 제약사항
조회는 현재 확정된 Instance를 읽기만 하며 Spec은 Engine 참조를 갖지 않는다. 변경 전파는 명령 도중이 아니라 RefreshDirty에서 일어난다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Objective;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ================================================================================
    /// <summary>
    /// <br/>ObjectiveDirectory의 등록·조회, 읽기 기록 기반 변경 전파, 일괄 갱신·시간,
    /// <br/>복원·반복과의 연결을 검증한다.
    /// </summary>
    // ================================================================================
    public sealed class TEST_ObjectiveDirectory
    {

    #region 테스트 도우미

        private sealed class OtherIs : ICond<ObjectiveContext>
        {
            private readonly string scopeID;
            private readonly string specID;
            private readonly ObjectiveStatus expected;

            // ----------------------------------------------------------------------
            /// <summary>
            /// 조회할 Objective와 기대하는 상태를 보관한다. Scope가 null이면 같은 Scope다.
            /// </summary>
            // ----------------------------------------------------------------------
            public OtherIs(string scopeID, string specID, ObjectiveStatus expected)
            {
                this.scopeID = scopeID;
                this.specID = specID;
                this.expected = expected;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 다른 Objective의 현재 상태가 기대와 같은지 평가한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                bool found = scopeID == null
                    ? context.TryGetObjective(specID, out var other)
                    : context.TryGetObjective(scopeID, specID, out other);
                return found && other.Status == expected;
            }
        }

        private sealed class Probe : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 마지막 평가에서 본 다른 Objective의 상태.
            /// </summary>
            // ------------------------------------------------------------
            public ObjectiveStatus? Seen { get; private set; }

            // ----------------------------------------------------------------------
            /// <summary>
            /// 같은 Scope의 source Objective 상태를 기록하고 항상 미완료를 돌려준다.
            /// </summary>
            // ----------------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                Seen = context.TryGetObjective("source", out var other) ? other.Status : null;
                return false;
            }
        }

        private static ObjectiveSpec Spec
        (
            string specID, ICond<ObjectiveContext> condition,
            ObjectiveRepeatMode repeat = ObjectiveRepeatMode.Once
        )
        {
            return new ObjectiveSpec(specID, 1, new TaskSpec("root", condition), repeatMode: repeat);
        }

        private static ObjectiveEngine Create
        (
            ObjectiveSpec spec, ObjectiveDirectory directory, string scope = "world", string run = "run"
        )
        {
            var engine = new ObjectiveEngine(spec, new ObjectiveInstance(spec.SpecID, 1, run, scope), directory: directory);
            directory.Register(engine);
            return engine;
        }

        private static ObjectiveEngine CreateRegistered
        (
            ObjectiveDirectory directory, string id, ICond<ObjectiveContext> condition,
            string scope = "world", bool repeatable = false
        )
        {
            var spec = new ObjectiveSpec
            (
                id, 1, new TaskSpec("root", condition),
                repeatMode: repeatable ? ObjectiveRepeatMode.Repeatable : ObjectiveRepeatMode.Once
            );
            var engine = new ObjectiveEngine(spec, new ObjectiveInstance(id, 1, "run-" + id, scope), directory: directory);
            directory.Register(engine);
            return engine;
        }

        private sealed class FailsAfterStart : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 시간이 흐른 뒤의 평가에서 예외를 던진다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                if (context.ElapsedTime > 0)
                {
                    throw new InvalidOperationException("injected");
                }

                return false;
            }
        }

        private sealed class ReadsGhost : ICond<ObjectiveContext>
        {
            // ------------------------------------------------------------
            /// <summary>
            /// 등록하지 않은 소스를 읽어 예외를 낸 뒤 거짓을 반환한다.
            /// </summary>
            // ------------------------------------------------------------
            public bool Check(ObjectiveContext context)
            {
                context.Source("ghost");
                return false;
            }
        }

    #endregion

    #region 등록·조회

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Scope와 Spec ID로 Engine·Instance를 찾고, 같은 키의 나중 등록이 현재 회차가 된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_나중등록이현재회차이다()
        {
            var directory = new ObjectiveDirectory();
            var first = Create(Spec("quest", new ToggleCond()), directory, run: "run-1");
            Assert.That(directory.TryGetEngine("world", "quest", out var found), Is.True);
            Assert.That(found, Is.SameAs(first));
            Assert.That(directory.TryGetInstance("world", "quest", out var instance), Is.True);
            Assert.That(instance, Is.SameAs(first.Instance));
            Assert.That(directory.TryGetInstance("other", "quest", out _), Is.False);
            Assert.That(directory.TryGetInstance(null, "quest", out _), Is.False);

            var second = Create(Spec("quest", new ToggleCond()), directory, run: "run-2");
            Assert.That(directory.TryGetEngine("world", "quest", out found), Is.True);
            Assert.That(found, Is.SameAs(second));
            Assert.That(directory.Unregister(first), Is.False);
            Assert.That(directory.Unregister(second), Is.True);
            Assert.That(directory.TryGetEngine("world", "quest", out _), Is.False);
            Assert.Throws<ArgumentNullException>(() => directory.Register(null));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Condition이 Engine 참조 없이 다른 Objective의 현재 상태를 읽는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_Condition이다른Objective상태를조회한다()
        {
            var directory = new ObjectiveDirectory();
            var ordinary = Create(Spec("ordinary", new ToggleCond()), directory);
            var secret = Create(Spec("secret", new OtherIs(null, "ordinary", ObjectiveStatus.Failed)), directory);
            ordinary.Activate();
            secret.Activate();
            Assert.That(secret.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            ordinary.Force(ObjectiveStatus.Failed);
            secret.Refresh();

            Assert.That(secret.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 다른 Scope의 Objective도 Scope를 지정해 조회하고, 같은 Scope 조회와 섞이지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_Scope를지정해다른Scope를조회한다()
        {
            var directory = new ObjectiveDirectory();
            var party = Create(Spec("boss", new ToggleCond()), directory, scope: "party");
            var personal = Create(Spec("report", new OtherIs("party", "boss", ObjectiveStatus.Succeeded)), directory, scope: "alice");
            var sameScope = Create(Spec("local", new OtherIs(null, "boss", ObjectiveStatus.Succeeded)), directory, scope: "alice");
            party.Activate();
            personal.Activate();
            sameScope.Activate();

            party.Force(ObjectiveStatus.Succeeded);
            personal.Refresh();
            sameScope.Refresh();

            Assert.That(personal.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(sameScope.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Directory 없이 만든 Engine의 조회는 찾지 못함으로 끝난다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_Directory가없으면찾지못한다()
        {
            var spec = Spec("secret", new OtherIs(null, "ordinary", ObjectiveStatus.Failed));
            var engine = new ObjectiveEngine(spec, new ObjectiveInstance("secret", 1, "run", "world"));

            Assert.That(engine.Activate(), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Evaluate().RootResult, Is.EqualTo(TaskResult.Pending));
        }

    #endregion

    #region 반복·복원

        // ------------------------------------------------------------
        /// <summary>
        /// 반복으로 만든 새 회차가 현재 회차로 등록된다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_반복한새회차가현재회차가된다()
        {
            var directory = new ObjectiveDirectory();
            var first = Create(Spec("daily", new ToggleCond(), ObjectiveRepeatMode.Repeatable), directory, run: "run-1");
            first.Activate();
            first.Force(ObjectiveStatus.Cancelled);

            var second = first.CreateRepeat("run-2");

            Assert.That(directory.TryGetEngine("world", "daily", out var current), Is.True);
            Assert.That(current, Is.SameAs(second));
            Assert.That(directory.TryGetInstance("world", "daily", out var instance), Is.True);
            Assert.That(instance.InstanceID, Is.EqualTo("run-2"));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 복원 배치의 Condition이 초기 평가에서 같은 배치의 다른 Objective를 본다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_복원한배치가서로를조회한다()
        {
            var registry = new ObjectiveSpecRegistry();
            var probe = new Probe();
            registry.Register(Spec("source", new ToggleCond()));
            registry.Register(Spec("watcher", probe));
            var source = new ObjectiveInstance("source", 1, "run-source", "world");
            source.Apply
            (
                candidate =>
                {
                    candidate.ChangeStatus(ObjectiveStatus.Active);
                    candidate.ChangeStatus(ObjectiveStatus.Failed);
                }
            );

            var watcher = new ObjectiveInstance("watcher", 1, "run-watcher", "world");
            watcher.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active));
            var directory = new ObjectiveDirectory();

            var saved = new[]
            {
                watcher,
                source
            };
            var engines = ObjectiveRuntimeBinding.Bind(registry, saved, directory: directory);

            Assert.That(engines.Count, Is.EqualTo(2));
            Assert.That(probe.Seen, Is.EqualTo(ObjectiveStatus.Failed));
            Assert.That(directory.TryGetInstance("world", "source", out _), Is.True);
            Assert.That(directory.TryGetInstance("world", "watcher", out _), Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 배치가 실패하면 Directory의 등록은 이전 상태로 돌아간다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_복원이실패하면등록을되돌린다()
        {
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Spec("source", new ToggleCond()));
            registry.Register(Spec("broken", new ThrowCond()));
            var directory = new ObjectiveDirectory();
            registry.TryResolve("source", 1, out var spec);
            var previous = Create(spec, directory, run: "run-old");

            var replacement = new ObjectiveInstance("source", 1, "run-new", "world");
            var broken = new ObjectiveInstance("broken", 1, "run-broken", "world");
            broken.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active));

            var saved = new[]
            {
                replacement,
                broken
            };

            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, saved, directory: directory));

            Assert.That(directory.TryGetEngine("world", "source", out var current), Is.True);
            Assert.That(current, Is.SameAs(previous));
            Assert.That(directory.TryGetEngine("world", "broken", out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 배치가 실패하면 등록 순서도 이전 순서로 돌아간다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_복원이실패하면등록순서도되돌린다()
        {
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Spec("source", new ToggleCond()));
            registry.Register(Spec("other", new ToggleCond()));
            registry.Register(Spec("broken", new ThrowCond()));
            var directory = new ObjectiveDirectory();
            registry.TryResolve("source", 1, out var sourceSpec);
            registry.TryResolve("other", 1, out var otherSpec);
            var previous = Create(sourceSpec, directory, run: "run-old");
            var other = Create(otherSpec, directory, run: "run-other");

            var replacement = new ObjectiveInstance("source", 1, "run-new", "world");
            var broken = new ObjectiveInstance("broken", 1, "run-broken", "world");
            broken.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active));

            var saved = new ObjectiveInstance[]
            {
                replacement,
                broken
            };
            var expected = new ObjectiveEngine[]
            {
                previous,
                other
            };

            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, saved, directory: directory));
            Assert.That(directory.GetEngines(), Is.EqualTo(expected));
        }

    #endregion

    #region 읽기 기록 기반 변경 전파

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>다른 Objective가 바뀌면 그것을 읽은 Engine은 명령 도중이 아니라 RefreshDirty에서
        /// <br/>갱신된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_읽은Objective가바뀌면RefreshDirty에서갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var source = CreateRegistered(directory, "source", new ToggleCond());
            var watcher = CreateRegistered(directory, "watcher", ObjectiveCond.DependsOn("source", ObjectiveStatus.Failed));
            source.Activate();
            watcher.Activate();

            source.Force(ObjectiveStatus.Failed);
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(directory.RefreshDirty(), Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 갱신이 다른 Engine을 다시 바꾸면 수렴할 때까지 이어서 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_연쇄된변경도한번의RefreshDirty로끝난다()
        {
            var directory = new ObjectiveDirectory();
            var first = CreateRegistered(directory, "first", new ToggleCond());
            var second = CreateRegistered(directory, "second", ObjectiveCond.DependsOn("first", ObjectiveStatus.Failed));
            var third = CreateRegistered(directory, "third", ObjectiveCond.DependsOn("second", ObjectiveStatus.Succeeded));
            first.Activate();
            second.Activate();
            third.Activate();

            first.Force(ObjectiveStatus.Failed);
            int refreshed = directory.RefreshDirty();

            Assert.That(refreshed, Is.EqualTo(2));
            Assert.That(second.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(third.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 읽지 않은 Engine과 Active가 아닌 Engine은 dirty가 되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_읽지않았거나Active가아니면갱신하지않는다()
        {
            var directory = new ObjectiveDirectory();
            var source = CreateRegistered(directory, "source", new ToggleCond());
            var bystander = CreateRegistered(directory, "bystander", new ToggleCond());
            var sleeping = CreateRegistered(directory, "sleeping", ObjectiveCond.DependsOn("source", ObjectiveStatus.Failed));
            source.Activate();
            bystander.Activate();

            source.Force(ObjectiveStatus.Failed);

            Assert.That(directory.RefreshDirty(), Is.Zero);
            Assert.That(sleeping.Instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 변경을 관찰할 수 있는 Fact 소스는 값이 바뀌면 자동으로, 수동 Invalidate로도 dirty가 된다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_Fact소스변경을읽은Engine이갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var account = new ValueRegistry();
            directory.RegisterSource("account", account);
            var engine = CreateRegistered(directory, "milestone", ObjectiveCond.MinValueI("kills", 100, source: "account"));
            engine.Activate();
            Assert.That(directory.RefreshDirty(), Is.Zero);

            account.SetI("kills", 50);
            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            directory.Invalidate("account");
            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));

            account.SetI("kills", 100);
            directory.RefreshDirty();
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 소스를 빼면 구독이 풀리고, 중복 등록과 잘못된 이름은 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_소스등록해제와검증()
        {
            var directory = new ObjectiveDirectory();
            var account = new ValueRegistry();
            directory.RegisterSource("account", account);

            Assert.Throws<ArgumentException>(() => directory.RegisterSource("account", new ValueRegistry()));
            Assert.Throws<ArgumentException>(() => directory.RegisterSource(" ", new ValueRegistry()));
            Assert.Throws<ArgumentNullException>(() => directory.RegisterSource("other", null));
            Assert.That(directory.TryGetSource("account", out var found), Is.True);
            Assert.That(found, Is.SameAs(account));
            Assert.That(directory.UnregisterSource("account"), Is.True);
            Assert.That(directory.UnregisterSource("account"), Is.False);
            Assert.That(directory.TryGetSource("account", out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록에서 빠진 Engine은 더 이상 갱신 대상이 아니다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_등록에서빠지면갱신하지않는다()
        {
            var directory = new ObjectiveDirectory();
            var source = CreateRegistered(directory, "source", new ToggleCond());
            var watcher = CreateRegistered(directory, "watcher", ObjectiveCond.DependsOn("source", ObjectiveStatus.Failed));
            source.Activate();
            watcher.Activate();
            Assert.That(directory.Unregister(watcher), Is.True);

            source.Force(ObjectiveStatus.Failed);

            Assert.That(directory.RefreshDirty(), Is.Zero);
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>한 Engine의 갱신이 실패해도 나머지는 끝까지 갱신하고 첫 예외를 던진다.
        /// <br/>실패한 Engine은 dirty로 남아 다음 호출에서 다시 시도한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_한대상이실패해도나머지를갱신한다()
        {
            var directory = new ObjectiveDirectory();
            directory.RegisterSource("ghost", new ValueRegistry());
            var broken = CreateRegistered(directory, "broken", new ReadsGhost());
            var tail = CreateRegistered(directory, "tail", new ToggleCond());
            var watcher = CreateRegistered(directory, "watcher", ObjectiveCond.DependsOn("tail", ObjectiveStatus.Failed));
            broken.Activate();
            tail.Activate();
            watcher.Activate();
            directory.UnregisterSource("ghost");
            tail.Force(ObjectiveStatus.Failed);

            Assert.Throws<InvalidOperationException>(() => directory.RefreshDirty());
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.Throws<InvalidOperationException>(() => directory.RefreshDirty());

            directory.RegisterSource("ghost", new ValueRegistry());
            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 갱신에 실패한 대상이 둘 이상이면 AggregateException으로 던지고 둘 다 dirty로 남긴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_둘이상실패하면AggregateException으로던진다()
        {
            var directory = new ObjectiveDirectory();
            directory.RegisterSource("ghost", new ValueRegistry());
            var first = CreateRegistered(directory, "first", new ReadsGhost());
            var second = CreateRegistered(directory, "second", new ReadsGhost());
            first.Activate();
            second.Activate();
            directory.UnregisterSource("ghost");

            var thrown = Assert.Throws<AggregateException>(() => directory.RefreshDirty());

            Assert.That(thrown.InnerExceptions.Count, Is.EqualTo(2));
            directory.RegisterSource("ghost", new ValueRegistry());
            Assert.That(directory.RefreshDirty(), Is.EqualTo(2));
        }

    #endregion

    #region 목록·일괄 시간

        // ----------------------------------------------------------------------
        /// <summary>
        /// Scope별 목록은 등록 순서를 지키고, 교체되면 이전 회차는 목록에서 빠진다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_목록은등록순서이고교체를반영한다()
        {
            var directory = new ObjectiveDirectory();
            var one = CreateRegistered(directory, "one", new ToggleCond());
            var two = CreateRegistered(directory, "two", new ToggleCond(), scope: "party", repeatable: true);
            var three = CreateRegistered(directory, "three", new ToggleCond());

            var expected = new[]
            {
                one,
                two,
                three
            };
            Assert.That(directory.GetEngines(), Is.EqualTo(expected));
            var expected2 = new[]
            {
                one,
                three
            };
            Assert.That(directory.GetEngines("world"), Is.EqualTo(expected2));
            Assert.That(directory.GetEngines("none"), Is.Empty);

            two.Activate();
            two.Force(ObjectiveStatus.Cancelled);
            var repeated = two.CreateRepeat("two-2");

            var expected3 = new[]
            {
                repeated
            };
            Assert.That(directory.GetEngines("party"), Is.EqualTo(expected3));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>NonAlloc 조회는 호출자의 목록을 비우고 채운다.
        /// <br/>채운 뒤나 순회 중의 등록 변경에도 안전하다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_NonAlloc조회는목록을비우고채운다()
        {
            var directory = new ObjectiveDirectory();
            var one = CreateRegistered(directory, "one", new ToggleCond(), repeatable: true);
            var two = CreateRegistered(directory, "two", new ToggleCond(), scope: "party");
            var results = new List<ObjectiveEngine>
            {
                two,
                two,
                two
            };
            var both = new ObjectiveEngine[]
            {
                one,
                two
            };
            var onlyTwo = new ObjectiveEngine[]
            {
                two
            };

            Assert.That(directory.GetEnginesNonAlloc(results), Is.EqualTo(2));
            Assert.That(results, Is.EqualTo(both));
            Assert.That(directory.GetEnginesNonAlloc(results, "party"), Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(onlyTwo));
            Assert.That(directory.GetEnginesNonAlloc(results, "none"), Is.Zero);
            Assert.That(results, Is.Empty);
            Assert.Throws<ArgumentNullException>(() => directory.GetEnginesNonAlloc(null));

            one.Activate();
            one.Force(ObjectiveStatus.Cancelled);
            directory.GetEnginesNonAlloc(results);
            foreach (var engine in results)
            {
                if (engine.Instance.IsTerminal)
                {
                    engine.CreateRepeat("one-2");
                }
            }

            Assert.That(results, Is.EqualTo(both));
            Assert.That(directory.GetEngines().Count, Is.EqualTo(2));
            Assert.That(directory.TryGetEngine("world", "one", out var current), Is.True);
            Assert.That(current, Is.Not.SameAs(one));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 일괄 시간은 Active Engine에만 더하고 그 결과로 dirty가 된 Engine을 함께 갱신한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_일괄시간은Active에만더한다()
        {
            var directory = new ObjectiveDirectory();
            var timed = CreateRegistered(directory, "timed", ObjectiveCond.MinElapsed(5));
            var watcher = CreateRegistered(directory, "watcher", ObjectiveCond.DependsOn("timed", ObjectiveStatus.Succeeded));
            var idle = CreateRegistered(directory, "idle", new ToggleCond());
            timed.Activate();
            watcher.Activate();

            directory.AdvanceTime(5);

            Assert.That(timed.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(idle.Instance.ElapsedTime, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => directory.AdvanceTime(0));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>한 Engine의 시간 가산이 실패해도 나머지에는 시간을 더하고 예외를 던진다.
        /// <br/>실패한 Engine은 시간이 더해지지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_한Engine이실패해도나머지에시간을더한다()
        {
            var directory = new ObjectiveDirectory();
            var broken = CreateRegistered(directory, "broken", new FailsAfterStart());
            var timed = CreateRegistered(directory, "timed", new ToggleCond());
            broken.Activate();
            timed.Activate();

            Assert.Throws<InvalidOperationException>(() => directory.AdvanceTime(5));

            Assert.That(broken.Instance.ElapsedTime, Is.Zero);
            Assert.That(timed.Instance.ElapsedTime, Is.EqualTo(5));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 알림 핸들러가 AdvanceTime을 다시 불러도 순회 목록이 섞이지 않고 모든 Engine에 더해진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_AdvanceTime재진입에도순회가섞이지않는다()
        {
            var directory = new ObjectiveDirectory();
            var first = CreateRegistered(directory, "first", new ToggleCond());
            var second = CreateRegistered(directory, "second", new ToggleCond());
            first.Activate();
            second.Activate();
            bool nested = false;
            first.Changed += () =>
            {
                if (!nested)
                {
                    nested = true;
                    directory.AdvanceTime(1);
                }
            };

            directory.AdvanceTime(1);

            Assert.That(first.Instance.ElapsedTime, Is.EqualTo(2));
            Assert.That(second.Instance.ElapsedTime, Is.EqualTo(2));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>AdvanceTime의 알림은 모든 Engine에 시간을 더한 뒤 전달된다.
        /// <br/>구독자는 다른 Engine도 갱신이 끝난 상태를 본다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_AdvanceTime알림은모든Engine에시간을더한뒤전달된다()
        {
            var directory = new ObjectiveDirectory();
            var first = CreateRegistered(directory, "first", new ToggleCond());
            var second = CreateRegistered(directory, "second", new ToggleCond());
            first.Activate();
            second.Activate();
            double secondSeen = -1;
            double firstSeen = -1;
            first.Changed += () => secondSeen = second.Instance.ElapsedTime;
            second.Changed += () => firstSeen = first.Instance.ElapsedTime;

            directory.AdvanceTime(1);

            Assert.That(secondSeen, Is.EqualTo(1));
            Assert.That(firstSeen, Is.EqualTo(1));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>RefreshDirty의 알림은 한 회차의 모든 Engine을 갱신한 뒤 전달된다.
        /// <br/>같은 회차의 다른 Engine도 갱신이 끝난 상태로 보인다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_RefreshDirty알림은한회차를모두갱신한뒤전달된다()
        {
            var directory = new ObjectiveDirectory();
            var source = CreateRegistered(directory, "source", new ToggleCond());
            var first = CreateRegistered(directory, "first", ObjectiveCond.DependsOn("source", ObjectiveStatus.Succeeded));
            var second = CreateRegistered(directory, "second", ObjectiveCond.DependsOn("source", ObjectiveStatus.Succeeded));
            source.Activate();
            first.Activate();
            second.Activate();
            var secondSeen = ObjectiveStatus.Inactive;
            first.Changed += () => secondSeen = second.Instance.Status;
            source.Force(ObjectiveStatus.Succeeded);

            directory.RefreshDirty();

            Assert.That(first.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
            Assert.That(secondSeen, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>일괄 처리 중 구독자가 새 명령을 내려도 알림은 순서대로 전달된다.
        /// <br/>새 명령의 알림은 대기열 끝에 붙고, 전달을 미루는 동안에는 나가지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_일괄알림중새명령의알림도순서대로전달된다()
        {
            var directory = new ObjectiveDirectory();
            var first = CreateRegistered(directory, "first", new ToggleCond());
            var second = CreateRegistered(directory, "second", new ToggleCond());
            first.Activate();
            second.Activate();
            var order = new List<string>();
            bool nested = false;
            first.Changed += () =>
            {
                order.Add("first");
                if (!nested)
                {
                    nested = true;
                    second.AdvanceTime(1);
                }
            };
            second.Changed += () => order.Add("second");

            directory.AdvanceTime(1);

            var expected = new string[]
            {
                "first",
                "second",
                "second"
            };
            Assert.That(order, Is.EqualTo(expected));
        }

    #endregion

    #region 등록 변경 전파

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>읽던 대상이 나중에 등록되면 그것을 읽던 Engine은 대상이 바뀌지 않았어도
        /// <br/>RefreshDirty에서 다시 평가한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_대상이나중에등록되면읽던Engine이갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var watcher = CreateRegistered(directory, "watcher", ObjectiveCond.DependsOn("source", ObjectiveStatus.Inactive));
            watcher.Activate();
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            CreateRegistered(directory, "source", new ToggleCond());

            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>새 회차로 교체되면 새 회차가 아직 아무 알림도 내지 않아도 읽던 Engine이 새 회차
        /// <br/>기준으로 다시 평가한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_회차가교체되면읽던Engine이갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var source = CreateRegistered(directory, "source", new ToggleCond(), repeatable: true);
            var watcher = CreateRegistered(directory, "watcher", ObjectiveCond.DependsOn("source", ObjectiveStatus.Inactive));
            source.Activate();
            watcher.Activate();
            source.Force(ObjectiveStatus.Succeeded);
            directory.RefreshDirty();
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            source.CreateRepeat("run-2");

            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 대상이 등록에서 빠지면 읽던 Engine은 대상이 없는 상태로 다시 평가한다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_대상이등록에서빠지면읽던Engine이갱신한다()
        {
            var directory = new ObjectiveDirectory();
            var source = CreateRegistered(directory, "source", new ToggleCond());
            var watcher = CreateRegistered(directory, "watcher", Cond.NOT(ObjectiveCond.DependsOn("source", ObjectiveStatus.Active)));
            source.Activate();
            watcher.Activate();
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));

            Assert.That(directory.Unregister(source), Is.True);

            Assert.That(directory.RefreshDirty(), Is.EqualTo(1));
            Assert.That(watcher.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>다른 Directory로 만든 Engine은 등록할 수 없고, Directory 없이 만든 Engine은
        /// <br/>조회 대상으로 등록할 수 있다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveDirectory_다른Directory의Engine은등록을거부한다()
        {
            var directory = new ObjectiveDirectory();
            var other = new ObjectiveDirectory();
            var spec = new ObjectiveSpec("quest", 1, new TaskSpec("root", new ToggleCond()));

            var foreign = new ObjectiveEngine(spec, new ObjectiveInstance("quest", 1, "run-a", "world"), directory: other);
            Assert.Throws<ArgumentException>(() => directory.Register(foreign));
            Assert.That(directory.GetEngines(), Is.Empty);

            var standalone = new ObjectiveEngine(spec, new ObjectiveInstance("quest", 1, "run-b", "world"));
            directory.Register(standalone);
            Assert.That(directory.TryGetEngine("world", "quest", out var found), Is.True);
            Assert.That(found, Is.SameAs(standalone));
        }

    #endregion

    }
}
