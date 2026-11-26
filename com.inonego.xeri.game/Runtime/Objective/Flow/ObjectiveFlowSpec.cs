/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveFlowSpec.cs
수정일 : 2026-11-25

# 설명
Objective 사이의 단방향 선행 관계와 단회 배타 선택을 정의한다.

# 특이사항, 제약사항
Flow는 Objective 내부 Task 분기를 대체하지 않는다. 안정 SpecID를 사용한다.
선행은 모두 성공해야 하는 기본 요구이고, 그 밖의 개방 규칙(OR·실패 후·조건)은 ActivationCond로 표현한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 한 Flow 실행에서 Objective 하나와 선행 완료 요구를 정의한다.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveFlowEntry
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Objective의 SpecID.
        /// </summary>
        // ------------------------------------------------------------
        public string ObjectiveID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Objective가 시작하기 전에 성공해야 하는 선행 ID.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<string> PredecessorIDs => new(predecessorIDs);
        private readonly string[] predecessorIDs;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>선행과 Choice 요구에 더해 시작 전에 참이어야 하는 선택 조건.
        /// <br/>없으면 항상 참이다.
        /// <br/>"A 또는 B 성공 후", "A 실패 후", 외부 Fact 같은 규칙을 ObjectiveCond와 Core
        /// <br/>Cond로 표현한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ICond<ObjectiveContext> ActivationCond { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 켜면 시작 조건이 충족되는 순간 Flow가 이 Objective를 자동으로 시작한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool Auto { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 하나의 Objective와 선행 요구, 선택적 시작 조건, 자동 시작 여부를 생성한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public ObjectiveFlowEntry
        (
            string objectiveID, IEnumerable<string> predecessorIDs = null,
            ICond<ObjectiveContext> activationCond = null, bool auto = false
        )
        {
            ObjectiveID = objectiveID;
            this.predecessorIDs = predecessorIDs == null
                ? Array.Empty<string>() : new List<string>(predecessorIDs).ToArray();
            ActivationCond = activationCond;
            Auto = auto;
        }
    }

    // ============================================================
    /// <summary>
    /// 선택 후 나머지 Objective를 취소하는 단회 Choice 그룹.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveFlowChoice
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Choice 그룹의 안정 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public string ChoiceID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 배타적으로 선택할 수 있는 Objective의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<string> ObjectiveIDs => new(objectiveIDs);
        private readonly string[] objectiveIDs;

        // 소속 조회용 집합. ObjectiveIDs와 같은 대안을 담는다.
        private readonly HashSet<string> members;

        // ------------------------------------------------------------
        /// <summary>
        /// 두 개 이상의 배타 Objective를 가진 Choice를 정의한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveFlowChoice(string choiceID, IEnumerable<string> objectiveIDs)
        {
            ChoiceID = choiceID;
            this.objectiveIDs = objectiveIDs == null
                ? Array.Empty<string>() : new List<string>(objectiveIDs).ToArray();
            members = new HashSet<string>(this.objectiveIDs, StringComparer.Ordinal);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 그 Objective가 이 Choice의 대안인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Includes(string objectiveID)
        {
            return objectiveID != null && members.Contains(objectiveID);
        }
    }

    // ============================================================
    /// <summary>
    /// Objective들의 연결과 선택을 구조적으로 검증한 Flow 명세.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveFlowSpec
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 하나의 Flow 실행 종류를 식별하는 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string FlowID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 저장 상태가 정확히 연결할 정의 버전.
        /// </summary>
        // ------------------------------------------------------------
        public int Version { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Flow에서 참조하는 Objective들의 정적 목록.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<ObjectiveFlowEntry> Entries => new(entries);
        private readonly ObjectiveFlowEntry[] entries;

        // ------------------------------------------------------------
        /// <summary>
        /// 한 실행에서 각각 한 번 선택할 수 있는 Choice 목록.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<ObjectiveFlowChoice> Choices => new(choices);
        private readonly ObjectiveFlowChoice[] choices;

    #endregion

    #region 조회

        private readonly Dictionary<string, ObjectiveFlowChoice> choiceByObjective = new(StringComparer.Ordinal);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 그 Objective가 속한 Choice를 조회한다. 어느 Choice에도 속하지 않으면 false다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool TryGetChoice(string objectiveID, out ObjectiveFlowChoice choice)
        {
            if (objectiveID != null)
            {
                return choiceByObjective.TryGetValue(objectiveID, out choice);
            }

            choice = null;
            return false;
        }

    #endregion

    #region 생성·검증

        // ------------------------------------------------------------
        /// <summary>
        /// 선행 참조와 배타 선택이 닫힌 DAG 구조인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveFlowSpec
        (
            string flowID, IEnumerable<ObjectiveFlowEntry> entries,
            IEnumerable<ObjectiveFlowChoice> choices = null, int version = 1
        )
        {
            FlowID = flowID;
            Version = version;
            this.entries = entries == null
                ? Array.Empty<ObjectiveFlowEntry>() : new List<ObjectiveFlowEntry>(entries).ToArray();
            this.choices = choices == null
                ? Array.Empty<ObjectiveFlowChoice>() : new List<ObjectiveFlowChoice>(choices).ToArray();
            Validate();

            // 검증이 한 Objective는 한 Choice에만 속함을 보장하므로 소속 조회표를 만든다.
            foreach (var choice in Choices)
            {
                foreach (var id in choice.ObjectiveIDs)
                {
                    choiceByObjective.Add(id, choice);
                }
            }
        }

        private void Validate()
        {
            RequireID(FlowID);
            if (Version <= 0)
            {
                throw new ArgumentException("Flow Version must be positive.");
            }

            var map = new Dictionary<string, ObjectiveFlowEntry>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry == null)
                {
                    throw new ArgumentException("Null Flow Entry.");
                }

                RequireID(entry.ObjectiveID);
                if (!map.TryAdd(entry.ObjectiveID, entry))
                {
                    throw new ArgumentException("Duplicate Flow Objective.");
                }
            }

            if (map.Count == 0)
            {
                throw new ArgumentException("Flow requires at least one Objective.");
            }

            foreach (var entry in Entries)
            {
                var predecessors = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in entry.PredecessorIDs)
                {
                    RequireID(id);
                    if (id == entry.ObjectiveID || !map.ContainsKey(id) || !predecessors.Add(id))
                    {
                        throw new ArgumentException("Invalid predecessor reference.");
                    }
                }
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                Visit(entry.ObjectiveID, map, visiting, visited);
            }

            var choiceIDs = new HashSet<string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in Choices)
            {
                if (choice == null)
                {
                    throw new ArgumentException("Null Choice.");
                }

                RequireID(choice.ChoiceID);
                if (!choiceIDs.Add(choice.ChoiceID) || choice.ObjectiveIDs.Count < 2)
                {
                    throw new ArgumentException("Invalid Choice group.");
                }

                foreach (var id in choice.ObjectiveIDs)
                {
                    RequireID(id);
                    if (!map.ContainsKey(id) || !used.Add(id))
                    {
                        throw new ArgumentException("Duplicate or unknown Choice alternative.");
                    }
                }

                RequireNoPredecessorWithin(choice, map);
            }
        }

        // 한 대안을 고르면 나머지는 Cancelled가 되므로, 어떤 대안의 (간접 포함) 선행이 같은 Choice의 다른 대안이면 그 대안은 시작할 수 없다.
        private static void RequireNoPredecessorWithin
        (
            ObjectiveFlowChoice choice, Dictionary<string, ObjectiveFlowEntry> map
        )
        {
            foreach (var id in choice.ObjectiveIDs)
            {
                var ancestors = new HashSet<string>(StringComparer.Ordinal);
                CollectPredecessors(id, map, ancestors);
                foreach (var other in choice.ObjectiveIDs)
                {
                    if (other != id && ancestors.Contains(other))
                    {
                        throw new ArgumentException
                        (
                            $"Choice alternative '{id}' requires '{other}' of the same Choice and could never start."
                        );
                    }
                }
            }
        }

        private static void CollectPredecessors
        (
            string id, Dictionary<string, ObjectiveFlowEntry> map, HashSet<string> result
        )
        {
            foreach (var predecessor in map[id].PredecessorIDs)
            {
                if (result.Add(predecessor))
                {
                    CollectPredecessors(predecessor, map, result);
                }
            }
        }

        private static void Visit
        (
            string id, Dictionary<string, ObjectiveFlowEntry> map,
            HashSet<string> visiting, HashSet<string> visited
        )
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!visiting.Add(id))
            {
                throw new ArgumentException("Cyclic Flow predecessor graph.");
            }

            foreach (var predecessor in map[id].PredecessorIDs)
            {
                Visit(predecessor, map, visiting, visited);
            }

            visiting.Remove(id);
            visited.Add(id);
        }

        private static void RequireID(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Flow ID must not be blank.");
            }
        }

    #endregion

    }
}
