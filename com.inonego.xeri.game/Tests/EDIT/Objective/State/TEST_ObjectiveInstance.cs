/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_ObjectiveInstance.cs
수정일 : 2026-11-25

# 설명
ObjectiveInstance의 영속 소유권, 원자 변경, 상태 전이와 Unity 복원을 검증한다.

# 특이사항, 제약사항
Composite 평가와 외부 Fact는 Engine 테스트가 맡고, 이 파일은 Instance의 내부 변경 API를 직접 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using NUnit;
using NUnit.Framework;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Game.Objective;

namespace inonego.Xeri.Game.TEST.Objective
{
    // ============================================================
    /// <summary>
    /// Objective의 직접 영속 상태와 단일 트랜잭션 경계를 검증한다.
    /// </summary>
    // ============================================================
    public sealed class TEST_ObjectiveInstance
    {

    #region 테스트 데이터

        [Serializable]
        private sealed class SaveRoot
        {
            [SerializeField] private ObjectiveInstance[] objectives;

            internal SaveRoot(ObjectiveInstance[] objectives)
            {
                this.objectives = objectives;
            }

            internal ObjectiveInstance[] GetInstances() => objectives;
        }

        private static string[] CreateDefinitions()
        {
            return new string[]
            {
                "kills",
                "talked"
            };
        }

        private static ObjectiveInstance CreateActiveInstance()
        {
            var instance = new ObjectiveInstance("quest", 4, "run-1", "world-1");
            Assert.That(instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active)), Is.True);
            return instance;
        }

    #endregion

    #region 소유권·상태 전이

        // ------------------------------------------------------------
        /// <summary>
        /// 초기 객체는 영속 ID와 독립 Progress를 직접 소유한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_초기상태와식별자()
        {
            var instance = new ObjectiveInstance("quest", 4, "run-1", "world-1");
            Assert.That(instance.SpecID, Is.EqualTo("quest"));
            Assert.That(instance.SpecVersion, Is.EqualTo(4));
            Assert.That(instance.InstanceID, Is.EqualTo("run-1"));
            Assert.That(instance.ScopeID, Is.EqualTo("world-1"));
            Assert.That(instance.Status, Is.EqualTo(ObjectiveStatus.Inactive));
            Assert.That(instance.IsTerminal, Is.False);
            Assert.That(instance.ElapsedTime, Is.Zero);
            Assert.That(instance.PassedEntryIDs, Is.Empty);
            Assert.That(instance.SequentialBoundaries, Is.Empty);
            Assert.That(instance.Progress, Is.InstanceOf<ProgressState>());
            int count = instance.Progress.Values.GetI("kills");
            Assert.That(count, Is.Zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 완료·실패·취소는 Terminal이며 정상 재활성화되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_Status전이와Terminal불변()
        {
            var instance = CreateActiveInstance();
            Assert.That(instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active)), Is.False);
            Assert.Throws<InvalidOperationException>(() => instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Inactive)));

            Assert.That(instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Succeeded)), Is.True);
            Assert.That(instance.IsTerminal, Is.True);
            Assert.Throws<InvalidOperationException>(() => instance.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Active)));
            Assert.Throws<InvalidOperationException>(() => instance.Apply(candidate => candidate.EditProgress("kills", values => values.AddI("kills", 1))));
            Assert.That(instance.Status, Is.EqualTo(ObjectiveStatus.Succeeded));

            var neverStarted = new ObjectiveInstance("quest", 4, "run-2", "world-1");
            Assert.That(neverStarted.Apply(candidate => candidate.ChangeStatus(ObjectiveStatus.Cancelled)), Is.True);
            Assert.That(neverStarted.IsTerminal, Is.True);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 식별자와 Version이 잘못된 객체는 생성되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_필수식별자를검증한다()
        {
            Assert.Throws<ArgumentException>(() => new ObjectiveInstance("", 1, "run", "scope"));
            Assert.Throws<ArgumentException>(() => new ObjectiveInstance("quest", 1, " ", "scope"));
            Assert.Throws<ArgumentException>(() => new ObjectiveInstance("quest", 1, "run", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectiveInstance("quest", 0, "run", "scope"));
        }

    #endregion

    #region 원자 변경·이력

        // ------------------------------------------------------------
        /// <summary>
        /// Progress·시간·통과·경계를 한 번의 논리 변경으로 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_전체변경이함께확정된다()
        {
            var instance = CreateActiveInstance();
            var reader = instance.Progress;
            Action<ObjectiveInstance> change = candidate =>
            {
                candidate.EditProgress("kills", values => values.AddI("kills", 2));
                candidate.EditProgress("talked", values => values.SetFlag("talked"));
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-b");
                candidate.AdvanceTime(1.25);
            };
            Assert.That(instance.Apply(change), Is.True);

            Assert.That(instance.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(instance.ElapsedTime, Is.EqualTo(1.25).Within(0.000001));
            // Reader는 생성 당시의 조회 Snapshot이며 원본 Commit 뒤에도 변하지 않는다.
            int previous = reader.Values.GetI("kills");
            Assert.That(previous, Is.Zero);
            bool before = reader.Values.HasFlag("talked");
            Assert.That(before, Is.False);

            int current = instance.Progress.Values.GetI("kills");
            Assert.That(current, Is.EqualTo(2));
            bool completed = instance.Progress.Values.HasFlag("talked");
            Assert.That(completed, Is.True);
            Assert.That(instance.HasPassed("entry-a"), Is.True);
            Assert.That(instance.TryGetBoundary("group-1", out string entryID), Is.True);
            Assert.That(entryID, Is.EqualTo("entry-b"));

            Action<ObjectiveInstance> repeat = candidate =>
            {
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-b");
                candidate.EditProgress("talked", values => values.SetFlag("talked"));
                candidate.EditProgress("kills", values => values.SetI("kills", 2));
            };
            Assert.That(instance.Apply(repeat), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 중간 예외가 발생하면 영속 값 어느 것도 변경되지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_후보예외는원본에영향이없다()
        {
            var instance = CreateActiveInstance();
            string before = JsonUtility.ToJson(instance);

            Action<ObjectiveInstance> invalid = candidate =>
            {
                candidate.EditProgress("kills", values => values.AddI("kills", 2));
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-b");
                candidate.AdvanceTime(2);
                throw new ArgumentException("Injected mutation failure.");
            };
            Assert.Throws<ArgumentException>(() => instance.Apply(invalid));

            Assert.That(JsonUtility.ToJson(instance), Is.EqualTo(before));
            int current = instance.Progress.Values.GetI("kills");
            Assert.That(current, Is.Zero);
            Assert.That(instance.TryGetBoundary("group-1", out _), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 내부 재진입과 작업 후보 유출이 영속 원본을 바꾸지 못한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_재진입과후보유출을차단한다()
        {
            var instance = CreateActiveInstance();
            Assert.Throws<InvalidOperationException>(() => instance.Apply(candidate => instance.Apply(other => other.EditProgress("kills", values => values.AddI("kills", 1)))));
            int current = instance.Progress.Values.GetI("kills");
            Assert.That(current, Is.Zero);

            ObjectiveInstance leaked = null;
            Action<ObjectiveInstance> capture = candidate =>
            {
                leaked = candidate;
                candidate.EditProgress("kills", values => values.AddI("kills", 2));
            };
            Assert.That(instance.Apply(capture), Is.True);

            // 확정한 후보는 폐기되어 더 변경할 수 없다.
            Assert.Throws<InvalidOperationException>(() => leaked.EditProgress("kills", values => values.SetI("kills", 0)));
            current = instance.Progress.Values.GetI("kills");
            Assert.That(current, Is.EqualTo(2));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Reset과 이력 후퇴는 현재 Active 후보에서만 수행한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_이력과Progress초기화()
        {
            var instance = CreateActiveInstance();
            Action<ObjectiveInstance> setup = candidate =>
            {
                candidate.EditProgress("kills", values => values.AddI("kills", 3));
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-b");
            };
            instance.Apply(setup);

            Action<ObjectiveInstance> rollback = candidate =>
            {
                candidate.ResetProgress("kills");
                candidate.ClearPassed("entry-a");
                candidate.ClearBoundary("group-1");
            };
            Assert.That(instance.Apply(rollback), Is.True);

            int current = instance.Progress.Values.GetI("kills");
            Assert.That(current, Is.Zero);
            Assert.That(instance.HasPassed("entry-a"), Is.False);
            Assert.That(instance.TryGetBoundary("group-1", out _), Is.False);
            Assert.That(instance.Apply(candidate => candidate.ClearBoundary("group-1")), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 경과 시간의 잘못된 입력과 유한 범위 초과를 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_경과시간범위()
        {
            var instance = CreateActiveInstance();
            Assert.Throws<ArgumentOutOfRangeException>(() => instance.Apply(candidate => candidate.AdvanceTime(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => instance.Apply(candidate => candidate.AdvanceTime(double.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => instance.Apply(candidate => candidate.AdvanceTime(double.PositiveInfinity)));
            Assert.That(instance.ElapsedTime, Is.Zero);
        }

    #endregion

    #region 저장·복원

        // ------------------------------------------------------------
        /// <summary>
        /// Save 루트에서 직접 직렬화하고 조회 인덱스를 재연결한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_JsonUtility전체상태왕복()
        {
            var source = CreateActiveInstance();
            Action<ObjectiveInstance> setup = candidate =>
            {
                candidate.EditProgress("kills", values => values.AddI("kills", 2));
                candidate.EditProgress("talked", values => values.SetFlag("talked"));
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-b");
                candidate.AdvanceTime(2.75);
            };
            source.Apply(setup);

            var entries = new ObjectiveInstance[]
            {
                source
            };
            var root = new SaveRoot(entries);
            string json = JsonUtility.ToJson(root);
            Assert.That(json, Does.Contain("objectives"));
            Assert.That(json, Does.Not.Contain("target"));
            Assert.That(json, Does.Not.Contain("Dictionary"));

            var loadedRoot = JsonUtility.FromJson<SaveRoot>(json);
            var restored = loadedRoot.GetInstances()[0];
            Assert.That(restored, Is.Not.SameAs(source));
            Assert.That(restored.SpecVersion, Is.EqualTo(4));
            Assert.That(restored.ElapsedTime, Is.EqualTo(2.75).Within(0.000001));
            Assert.That(restored.Status, Is.EqualTo(ObjectiveStatus.Active));
            Assert.That(restored.HasPassed("entry-a"), Is.True);
            Assert.That(restored.TryGetBoundary("group-1", out string boundary), Is.True);
            Assert.That(boundary, Is.EqualTo("entry-b"));

            Assert.That(restored.Progress.Values.GetI("kills"), Is.EqualTo(2));
            restored.Validate();
            int current = restored.Progress.Values.GetI("kills");
            Assert.That(current, Is.EqualTo(2));
            bool completed = restored.Progress.Values.HasFlag("talked");
            Assert.That(completed, Is.True);

            Assert.That(restored.Apply(candidate => candidate.EditProgress("kills", values => values.AddI("kills", 1))), Is.True);
            current = restored.Progress.Values.GetI("kills");
            Assert.That(current, Is.EqualTo(3));
            current = source.Progress.Values.GetI("kills");
            Assert.That(current, Is.EqualTo(2));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 손상된 Status·ID·중복 이력은 로드 바인딩에서 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_무효영속상태거부()
        {
            var source = CreateActiveInstance();
            Action<ObjectiveInstance> setup = candidate =>
            {
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-a");
            };
            source.Apply(setup);

            string json = JsonUtility.ToJson(source);
            string duplicate = json.Replace("\"passedEntryIDs\":[\"entry-a\"]", "\"passedEntryIDs\":[\"entry-a\",\"entry-a\"]");
            Assert.That(duplicate, Is.Not.EqualTo(json));
            var invalidHistory = JsonUtility.FromJson<ObjectiveInstance>(duplicate);
            Assert.Throws<InvalidOperationException>(() => invalidHistory.Validate());

            string invalidVersion = json.Replace("\"specVersion\":4", "\"specVersion\":0");
            var invalidID = JsonUtility.FromJson<ObjectiveInstance>(invalidVersion);
            Assert.Throws<InvalidOperationException>(() => invalidID.Validate());

            string invalidStatus = json.Replace("\"status\":1", "\"status\":99");
            var invalid = JsonUtility.FromJson<ObjectiveInstance>(invalidStatus);
            Assert.Throws<InvalidOperationException>(() => invalid.Validate());
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장 복원 후에도 후보 Registry에서 새 Key를 등록할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_복원Registry추가등록()
        {
            var source = CreateActiveInstance();
            source.Apply(candidate => candidate.EditProgress("kills", values => values.SetI("kills", 2)));
            var loaded = JsonUtility.FromJson<ObjectiveInstance>(JsonUtility.ToJson(source));
            loaded.Validate();
            loaded.Apply(candidate => candidate.EditProgress("choice", values => values.SetS("choice", "accept")));
            Assert.That(loaded.Progress.Values.GetI("kills"), Is.EqualTo(2));
            Assert.That(loaded.Progress.Values.GetS("choice"), Is.EqualTo("accept"));
            Assert.That(source.Progress.Values.HasS("choice"), Is.False);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 노출된 컬렉션을 바꿔도 Instance 내부 상태는 바뀌지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_ObjectiveInstance_외부컬렉션변경차단()
        {
            var source = CreateActiveInstance();
            Action<ObjectiveInstance> setup = candidate =>
            {
                candidate.MarkPassed("entry-a");
                candidate.SetBoundary("group-1", "entry-a");
            };
            source.Apply(setup);

            var passed = source.PassedEntryIDs;
            Assert.That(passed, Is.Not.Empty);
            Assert.That((object)passed, Is.Not.InstanceOf<IList<string>>());

            Assert.That(source.HasPassed("entry-a"), Is.True);
            Assert.That(typeof(ObjectiveInstance).GetProperty("Status").CanWrite, Is.False);
            Assert.That(typeof(ObjectiveInstance).GetProperty("Progress").PropertyType, Is.EqualTo(typeof(ProgressState)));
        }

    #endregion

    }
}
