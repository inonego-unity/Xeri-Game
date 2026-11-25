/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveMigration.cs
수정일 : 2026-11-25

# 설명
Spec Version 변환이 저장된 Instance의 키·ID를 새 Version으로 옮기고, 맞지 않으면 복원을 거부하는지 검증한다.

# 특이사항, 제약사항
원본 Instance는 바꾸지 않고 Bind가 새 Instance의 Engine을 돌려준다.
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

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// Version 변환을 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveMigration
    {

    #region 테스트 정의

        // r1: first(kills 카운터) → second. Entry·Group·Key 이름이 r2에서 바뀐다.
        private static ObjectiveSpec Spec(int version, string killKey, string firstEntry, string groupID)
        {
            var localProgressIDs2 = new[]
            {
                killKey
            };
            var kills = new TaskSpec("kills-node", ObjectiveCond.MinValueI(killKey, 3), localProgressIDs: localProgressIDs2);
            var localProgressIDs3 = new[]
            {
                "report"
            };
            var report = new TaskSpec("report-node", ObjectiveCond.HasFlag("report"), localProgressIDs: localProgressIDs3);
            var entries = new TaskChildEntry[]
            {
                new(firstEntry, kills, revalidationMode: TaskRevalidationMode.OncePassed),
                new("report-entry", report)
            };
            var root = new TaskGroupSpec(groupID, TaskExecutionMode.Sequential, TaskGroupCond.All(), entries);
            return new ObjectiveSpec("quest", version, root);
        }

        private static ObjectiveInstance SavedAtVersion1()
        {
            var engine = new ObjectiveEngine(Spec(1, "kills", "kill-entry", "root"), new ObjectiveInstance("quest", 1, "run", "world"));
            engine.Activate();
            engine.EditProgress("kills", values => values.SetI("kills", 3));
            engine.AdvanceTime(7);
            return engine.Instance;
        }

        private static IReadOnlyDictionary<string, string> Map(params (string From, string To)[] pairs)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in pairs)
            {
                result.Add(pair.From, pair.To);
            }

            return result;
        }

    #endregion

    #region 변환

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>상태·시간·값은 그대로 두고 Progress 키, EntryID, Group ID 이름만 새
        /// <br/>Version으로 옮긴다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_이름만옮기고값과이력은보존한다()
        {
            var saved = SavedAtVersion1();
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Spec(2, "defeats", "defeat-entry", "chapter"));
            var migration = new ObjectiveMigration
            (
                "quest", 1, 2, Map(("kills", "defeats")), Map(("kill-entry", "defeat-entry")), Map(("root", "chapter"))
            );
            registry.RegisterMigration(migration);

            var saved2 = new[]
            {
                saved
            };
            var engine = ObjectiveRuntimeBinding.Bind(registry, saved2)[0];

            Assert.That(engine.Instance.SpecVersion, Is.EqualTo(2));
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(engine.Instance.ElapsedTime, Is.EqualTo(7));
            Assert.That(engine.Instance.Progress.Values.GetI("defeats"), Is.EqualTo(3));
            Assert.That(engine.Instance.Progress.Values.Has("kills"), Is.False);
            Assert.That(engine.Instance.HasPassed("defeat-entry"), Is.True);
            Assert.That(engine.Instance.TryGetBoundary("chapter", out string reached), Is.True);
            Assert.That(reached, Is.EqualTo("report-entry"));
            Assert.That(engine.EditProgress("report", values => values.SetFlag("report")), Is.True);
            Assert.That(engine.Instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 원본 저장 Instance는 바뀌지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_원본은바꾸지않는다()
        {
            var saved = SavedAtVersion1();
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Spec(2, "defeats", "defeat-entry", "chapter"));
            var migration = new ObjectiveMigration
            (
                "quest", 1, 2, Map(("kills", "defeats")), Map(("kill-entry", "defeat-entry")), Map(("root", "chapter"))
            );
            registry.RegisterMigration(migration);

            var saved2 = new[]
            {
                saved
            };
            ObjectiveRuntimeBinding.Bind(registry, saved2);

            Assert.That(saved.SpecVersion, Is.EqualTo(1));
            Assert.That(saved.Progress.Values.GetI("kills"), Is.EqualTo(3));
            Assert.That(saved.HasPassed("kill-entry"), Is.True);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 여러 단계를 이어 등록된 Version까지 옮긴다. 이미 등록된 Version은 옮기지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_체인을따라등록된Version까지옮긴다()
        {
            var saved = SavedAtVersion1();
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Spec(3, "defeats", "kill-entry", "root"));
            registry.RegisterMigration(new ObjectiveMigration("quest", 1, 2, Map(("kills", "slain"))));
            registry.RegisterMigration(new ObjectiveMigration("quest", 2, 3, Map(("slain", "defeats"))));

            Assert.That(registry.TryMigrate(saved, out var migrated), Is.True);
            Assert.That(migrated.SpecVersion, Is.EqualTo(3));
            Assert.That(migrated.Progress.Values.GetI("defeats"), Is.EqualTo(3));
            Assert.That(registry.TryMigrate(migrated, out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 변환이 새 Spec과 맞지 않거나 체인이 끊기면 복원을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_맞지않으면복원을거부한다()
        {
            var saved = SavedAtVersion1();
            var registry = new ObjectiveSpecRegistry();
            registry.Register(Spec(2, "defeats", "defeat-entry", "chapter"));

            // 변환이 없다.
            var saved2 = new[]
            {
                saved
            };
            Assert.Throws<ArgumentException>(() => ObjectiveRuntimeBinding.Bind(registry, saved2));

            // Entry·Group 이름을 옮기지 않아 새 Spec이 모르는 이력이 남는다.
            registry.RegisterMigration(new ObjectiveMigration("quest", 1, 2, Map(("kills", "defeats"))));
            var saved3 = new[]
            {
                saved
            };
            Assert.Throws<InvalidOperationException>(() => ObjectiveRuntimeBinding.Bind(registry, saved3));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// null로 매핑한 이름의 값과 이력은 버리고, 이름을 서로 바꿔도 값이 섞이지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_버리기와교환을지원한다()
        {
            var sourceOwned = new string[]
            {
                "a", "b", "dropped", "done"
            };
            var source = new ObjectiveSpec
            (
                "swap", 1, new TaskSpec("root", ObjectiveCond.HasFlag("done"), localProgressIDs: sourceOwned)
            );
            var engine = new ObjectiveEngine(source, new ObjectiveInstance("swap", 1, "run", "world"));
            engine.Activate();
            engine.ForceProgress
            (
                values =>
                {
                    values.SetI("a", 1);
                    values.SetI("b", 2);
                    values.SetI("dropped", 9);
                }
            );
            var targetOwned = new string[]
            {
                "a", "b", "done"
            };
            var target = new ObjectiveSpec
            (
                "swap", 2, new TaskSpec("root", ObjectiveCond.HasFlag("done"), localProgressIDs: targetOwned)
            );
            var registry = new ObjectiveSpecRegistry();
            registry.Register(target);
            var migration = new ObjectiveMigration
            (
                "swap", 1, 2, Map(("a", "b"), ("b", "a"), ("dropped", null))
            );
            registry.RegisterMigration(migration);

            var saved = new[]
            {
                engine.Instance
            };
            var restored = ObjectiveRuntimeBinding.Bind(registry, saved)[0];

            Assert.That(restored.Instance.Progress.Values.GetI("a"), Is.EqualTo(2));
            Assert.That(restored.Instance.Progress.Values.GetI("b"), Is.EqualTo(1));
            Assert.That(restored.Instance.Progress.Values.Has("dropped"), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 변환 선언은 만들 때와 등록할 때 검사한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_선언을검사한다()
        {
            Assert.Throws<ArgumentException>(() => new ObjectiveMigration("quest", 2, 2));
            Assert.Throws<ArgumentException>(() => new ObjectiveMigration("quest", 3, 2));
            Assert.Throws<ArgumentException>(() => new ObjectiveMigration(" ", 1, 2));
            Assert.Throws<ArgumentException>(() => new ObjectiveMigration("quest", 1, 2, Map((" ", "x"))));
            Assert.Throws<ArgumentException>(() => new ObjectiveMigration("quest", 1, 2, Map(("a", "x"), ("b", "x"))));

            var registry = new ObjectiveSpecRegistry();
            registry.RegisterMigration(new ObjectiveMigration("quest", 1, 2));
            Assert.Throws<ArgumentException>(() => registry.RegisterMigration(new ObjectiveMigration("quest", 1, 3)));
            Assert.Throws<ArgumentNullException>(() => registry.RegisterMigration(null));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 옮긴 새 이름이 옮기지 않은 기존 이름과 겹치면 false로 숨기지 않고 예외로 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveMigration_기존이름과겹치면예외로거부한다()
        {
            var tasks = new[]
            {
                "a",
                "b",
                "done"
            };
            var source = new ObjectiveSpec("clash", 1, new TaskSpec("root", ObjectiveCond.HasFlag("done"), localProgressIDs: tasks));
            var engine = new ObjectiveEngine(source, new ObjectiveInstance("clash", 1, "run", "world"));
            engine.Activate();
            engine.ForceProgress
            (
                values =>
                {
                    values.SetI("a", 1);
                    values.SetI("b", 2);
                }
            );
            var registry = new ObjectiveSpecRegistry();
            registry.Register(new ObjectiveSpec("clash", 2, new TaskSpec("root", ObjectiveCond.HasFlag("done"), localProgressIDs: tasks)));
            registry.RegisterMigration(new ObjectiveMigration("clash", 1, 2, Map(("a", "b"))));

            Assert.Throws<InvalidOperationException>(() => registry.TryMigrate(engine.Instance, out _));
        }

    #endregion

    }
}
