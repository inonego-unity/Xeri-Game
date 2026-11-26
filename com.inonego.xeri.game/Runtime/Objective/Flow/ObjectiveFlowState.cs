/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveFlowState.cs
수정일 : 2026-11-25

# 설명
Flow 실행의 선택 기록만 직접 소유하는 직렬화 가능한 상태 객체.

# 특이사항, 제약사항
Objective의 Status/Progress를 중복하지 않으며 Flow Owner의 Save 루트에 직접 포함한다.
한 상태는 살아 있는 ObjectiveFlow 하나만 실행한다. 실행 소유는 저장하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 한 Choice에서 확정한 ObjectiveID의 불변 기록.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ObjectiveFlowSelection
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 선택한 Choice 그룹의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string ChoiceID => choiceID;
        [SerializeField] private string choiceID;

        // ------------------------------------------------------------
        /// <summary>
        /// 확정한 Objective의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string ObjectiveID => objectiveID;
        [SerializeField] private string objectiveID;

    #endregion

    #region 생성자

        internal ObjectiveFlowSelection(string choiceID, string objectiveID)
        {
            this.choiceID = choiceID;
            this.objectiveID = objectiveID;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// Flow 실행의 ID·Scope와 단회 선택을 직접 영속화한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ObjectiveFlowState
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 연결할 ObjectiveFlowSpec의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string FlowID => flowID;
        [SerializeField] private string flowID;

        // ------------------------------------------------------------
        /// <summary>
        /// 저장된 선택 기록이 연결할 Flow Version.
        /// </summary>
        // ------------------------------------------------------------
        public int Version => version;
        [SerializeField] private int version;

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Flow 실행이 소유한 Scope의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string ScopeID => scopeID;
        [SerializeField] private string scopeID;

        [SerializeField] private List<ObjectiveFlowSelection> selections = new();

    #endregion

    #region 생성자·검증

        // ------------------------------------------------------------
        /// <summary>
        /// 새로운 Flow 실행의 빈 선택 기록을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveFlowState(string flowID, string scopeID, int version = 1)
        {
            this.flowID = flowID;
            this.scopeID = scopeID;
            this.version = version;
            Validate();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 복원된 ID·중복 선택·Null 목록을 거부한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(flowID) || string.IsNullOrWhiteSpace(scopeID)
                || version <= 0 || selections == null)
            {
                throw new InvalidOperationException("Invalid Flow state.");
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in selections)
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.ChoiceID)
                    || string.IsNullOrWhiteSpace(choice.ObjectiveID)
                    || !unique.Add(choice.ChoiceID))
                {
                    throw new InvalidOperationException("Invalid Flow selection.");
                }
            }
        }

    #endregion

    #region 선택

        // ------------------------------------------------------------
        /// <summary>
        /// 이미 선택된 그룹에서 선택한 Objective를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetSelection(string choiceID, out string objectiveID)
        {
            if (choiceID != null && selections != null)
            {
                foreach (var choice in selections)
                {
                    if (choice.ChoiceID == choiceID)
                    {
                        objectiveID = choice.ObjectiveID;
                        return true;
                    }
                }
            }

            objectiveID = null;
            return false;
        }

        internal void Select(string choiceID, string objectiveID)
        {
            if (TryGetSelection(choiceID, out _))
            {
                throw new InvalidOperationException("Flow Choice is already selected.");
            }

            selections.Add(new ObjectiveFlowSelection(choiceID, objectiveID));
        }

        // 확정한 선택을 지운다. Flow가 모든 대안을 새 회차로 교체하면서 Choice를 다시 열 때만 쓴다.
        internal void ClearSelection(string choiceID)
        {
            var removed = false;

            for (var index = selections.Count - 1; index >= 0; index--)
            {
                if (selections[index].ChoiceID == choiceID)
                {
                    selections.RemoveAt(index);
                    removed = true;
                }
            }

            if (!removed)
            {
                throw new InvalidOperationException("Flow Choice is not selected.");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장용 원본을 노출하지 않는 확정 Choice 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<ObjectiveFlowSelection> Selections =>
            selections == null ? null : selections.ToArray();

    #endregion

    #region 실행 소유

        // 이 상태를 실행하는 살아 있는 Flow. 한 상태는 Flow 하나만 실행한다. 저장하지 않는다.
        internal ObjectiveFlow Owner
        {
            get => owner;
            set => owner = value;
        }
        [NonSerialized] private ObjectiveFlow owner;

    #endregion

    }
}
