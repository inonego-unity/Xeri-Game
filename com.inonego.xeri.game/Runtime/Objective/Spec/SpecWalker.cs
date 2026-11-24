/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : SpecWalker.cs
수정일 : 2026-11-25

# 설명
Spec 트리를 전위 순서로 돌며 Node와 Entry를 방문하는 분석용 공용 순회 기반.

# 특이사항, 제약사항
트리의 모양(Group→Entry→Node)을 이 클래스만 안다. 검증·사실 수집 같은 분석은 이를 상속해 필요한 방문만 재정의한다.
순환·공유 Node는 더 내려가지 않고 알림 훅으로 알리므로 잘못된 Spec에서도 끝난다. 훅의 기본은 아무것도 하지 않는다.
명령·틱마다 도는 평가 순회(EvaluateNode 등)는 단락·순서가 의미를 가지므로 이 기반을 쓰지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Spec 트리를 한 번 도는 분석의 공통 기반.
    /// </summary>
    // ============================================================
    internal abstract class SpecWalker
    {

    #region 상태

        // 방문한 Node. 값이 true이면 지금 내려가는 중(순환), false이면 이미 끝난 Node(공유 참조)다.
        private readonly Dictionary<TaskNodeSpec, bool> visits = new();

    #endregion

    #region 순회

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>root부터 트리를 전위 순서로 한 번 돈다.
        /// <br/>path는 root의 경로이며 하위 경로는 NodeID를 /로, Entry는 #EntryID로 잇는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Walk(TaskNodeSpec root, string path) => VisitNode(root, path);

        // 순환이나 같은 인스턴스의 중복 참조는 더 내려가지 않는다.
        private void VisitNode(TaskNodeSpec node, string path)
        {
            if (node == null)
            {
                OnNullNode(path);
                return;
            }

            if (visits.TryGetValue(node, out bool inProgress))
            {
                if (inProgress)
                {
                    OnCycle(node, path);
                }
                else
                {
                    OnShared(node, path);
                }

                return;
            }

            visits.Add(node, true);
            try
            {
                Enter(node, path);

                if (node is TaskSpec task)
                {
                    VisitTask(task, path);
                }
                else if (node is TaskGroupSpec group)
                {
                    VisitGroup(group, path);
                }
                else
                {
                    OnUnknown(node, path);
                }
            }
            finally
            {
                visits[node] = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Node에 처음 들어갈 때 종류를 가르기 전에 한 번 불린다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void Enter(TaskNodeSpec node, string path)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Task를 방문한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void VisitTask(TaskSpec task, string path)
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Group을 방문한다.
        /// <br/>기본은 모든 Entry를 순서대로 방문한다.
        /// <br/>재정의하면 자식 방문 여부와 순서를 정한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void VisitGroup(TaskGroupSpec group, string path) => VisitEntries(group, path);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Group의 Entry를 정의한 순서대로 방문한다. null Entry는 알림 훅으로 알리고 건너뛴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        protected void VisitEntries(TaskGroupSpec group, string path)
        {
            for (int index = 0; index < group.Entries.Count; index++)
            {
                var entry = group.Entries[index];
                if (entry == null)
                {
                    OnNullEntry(index, path);
                }
                else
                {
                    VisitEntry(group, entry, index, path);
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Entry를 방문한다.
        /// <br/>기본은 Entry가 가리키는 Node로 내려간다.
        /// <br/>재정의하면 Descend로 내려가는 시점을 정한다.
        /// </summary>
        // ------------------------------------------------------------
        protected virtual void VisitEntry(TaskGroupSpec group, TaskChildEntry entry, int index, string path) =>
            Descend(entry, path);

        // ----------------------------------------------------------------------
        /// <summary>
        /// Entry가 가리키는 Node로 내려간다. Node가 null이면 Entry 경로로 알린다.
        /// </summary>
        // ----------------------------------------------------------------------
        protected void Descend(TaskChildEntry entry, string path) =>
            VisitNode(entry.Node, entry.Node == null ? EntryPath(path, entry) : $"{path}/{Name(entry.Node.NodeID)}");

    #endregion

    #region 알림 훅

        protected virtual void OnNullNode(string path)
        {
            // NONE
        }

        protected virtual void OnCycle(TaskNodeSpec node, string path)
        {
            // NONE
        }

        protected virtual void OnShared(TaskNodeSpec node, string path)
        {
            // NONE
        }

        protected virtual void OnUnknown(TaskNodeSpec node, string path)
        {
            // NONE
        }

        protected virtual void OnNullEntry(int index, string path)
        {
            // NONE
        }

    #endregion

    #region 경로

        // ------------------------------------------------------------
        /// <summary>
        /// 부모 Node 경로 뒤에 #EntryID를 붙인 Entry의 경로.
        /// </summary>
        // ------------------------------------------------------------
        protected static string EntryPath(string path, TaskChildEntry entry) => $"{path}#{Name(entry.EntryID)}";

        // ------------------------------------------------------------
        /// <summary>
        /// 경로에 쓸 ID. 비어 있으면 "(blank)"다.
        /// </summary>
        // ------------------------------------------------------------
        internal static string Name(string id) => string.IsNullOrWhiteSpace(id) ? "(blank)" : id;

    #endregion

    }
}
