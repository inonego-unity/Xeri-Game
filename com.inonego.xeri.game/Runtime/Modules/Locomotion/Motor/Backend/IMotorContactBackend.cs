/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IMotorContactBackend.cs
수정일 : 2026-11-25

# 설명
Backend가 제공하는 실제 접촉 읽기와 접촉 표본의 수명(Lifecycle, 관측 세대) 계약을 정의한다.

# 특이사항, 제약사항
Lifecycle은 읽기 계약의 기반이고 ObservationRevision은 센서 재활성화를 알리는 선택 구현이다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// <br/> Physics Step에서 발생한 실제 접촉을 읽는 기능입니다.
    /// <br/> 복사 가능한 수는 호출자가 제공한 배열 크기로 제한됩니다.
    /// </summary>
    // ============================================================
    public interface IMotorContactBackend<TContact> : IMotorContactLifecycle
    where TContact : struct
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Caller-owned 배열에 실제 접촉을 복사합니다.
        /// </summary>
        // ------------------------------------------------------------
        int ReadContacts(TContact[] results);
    }

    // ============================================================
    /// <summary>
    /// Contact 샘플의 수명을 관리하는 Backend 기능입니다.
    /// </summary>
    // ============================================================
    public interface IMotorContactLifecycle
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 실제 Contact 관측을 제공하는지 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        bool SupportsContactObservation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Step 전에 이전 Contact 샘플을 비웁니다.
        /// </summary>
        // ------------------------------------------------------------
        void BeginContactStep();

        // ------------------------------------------------------------
        /// <summary>
        /// Correction/Restore 시 Contact 샘플을 무효화합니다.
        /// </summary>
        // ------------------------------------------------------------
        void ClearContactObservation();
    }

    // ============================================================
    /// <summary>
    /// <br/> 센서 재활성화 등의 관측 수명 변경을 알리는 선택적 계약입니다.
    /// <br/> 기존 Contact Backend 구현에 필수로 요구하지 않습니다.
    /// </summary>
    // ============================================================
    public interface IMotorContactObservationRevision
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 현재 센서 관측 수명을 구분하는 단조 증가 값입니다.
        /// </summary>
        // ------------------------------------------------------------
        Revision ContactObservationRevision { get; }
    }
}
