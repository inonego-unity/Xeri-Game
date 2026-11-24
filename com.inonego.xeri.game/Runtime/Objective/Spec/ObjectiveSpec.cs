/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveSpec.cs
수정일 : 2026-11-25

# 설명
하나의 Root Composite와 Commit 정책을 묶은 정적 Objective 정의.

# 특이사항, 제약사항
실제 저작 소스나 Unity Asset 타입은 강제하지 않는다. Registry에서 Validator를 통과해야 한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 영속 Instance가 ID/Version으로 해결할 불변 실행 명세.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveSpec
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 정의를 식별하는 영속 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string SpecID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 저장된 실행이 연결할 정의의 정확한 Version.
        /// </summary>
        // ------------------------------------------------------------
        public int Version { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 정확히 하나의 Root TaskNode.
        /// </summary>
        // ------------------------------------------------------------
        public TaskNodeSpec Root { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 성공 조건이 참일 때 자동 확정 여부.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveCommitMode SuccessCommitMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실패 조건이 참일 때 자동 확정 여부.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveCommitMode FailureCommitMode { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 종료 이후 새 인스턴스를 만들 수 있는 정책.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveRepeatMode RepeatMode { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Root와 불변 실행 정책을 보관한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveSpec
        (
            string specID, int version, TaskNodeSpec root,
            ObjectiveCommitMode successCommitMode = ObjectiveCommitMode.Automatic,
            ObjectiveCommitMode failureCommitMode = ObjectiveCommitMode.Automatic,
            ObjectiveRepeatMode repeatMode = ObjectiveRepeatMode.Once
        )
        {
            SpecID = specID;
            Version = version;
            Root = root;
            SuccessCommitMode = successCommitMode;
            FailureCommitMode = failureCommitMode;
            RepeatMode = repeatMode;
        }

    #endregion

    #region 내부 캐시

        // ----------------------------------------------------------------------
        /// <summary>
        /// Validator를 통과했는지. Spec은 불변이므로 한 번 통과하면 다시 검증하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool IsValidated { get; set; }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Spec만 알면 정해지는 조회표(색인). 처음 읽을 때 한 번 만들어 모든 Engine이 공유한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal SpecIndex Index
        {
            get
            {
                index ??= SpecIndex.Build(this);
                return index;
            }
        }
        private SpecIndex index;

    #endregion

    }
}
