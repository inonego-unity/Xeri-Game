/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : Motor.cs
수정일 : 2026-11-21

# 설명
Body의 Tick 합성 결과 적용·현재 상태 교정·선택적 과거 복원·Physics 관측 수명을 연결한다.
불연속 종류(MotorDiscontinuityKind)와 복원 결과(MotorRestoreStatus) 열거형을 함께 둔다.

# 특이사항, 제약사항
Physics Step과 입력 History는 실행 Host 소유다. Backend 관측 시작이 성공할 때만 진행 상태로 전환한다. Rigidbody World History는 지원하지 않는다.
관측·History·Correction 수명을 바꾸거나 그 수명에 의존하는 작업은 하나의 작업 구간에서만 실행하며, 그 구간의 콜백에서 이런 작업을 다시 시작하면 InvalidOperationException으로 거절한다.
수명에 참여하지 않는 ReadCurrentState·TryApply는 작업 구간과 관계없이 호출할 수 있다.
TryApply는 BeginPhysicsStep 사이에 한 번만 받는다. Physics Step 중이거나 이미 적용했으면 거절한다.
History·불연속 상태는 Body를 바꾸는 Backend 호출이 성공한 뒤에만 확정한다.
Contact 읽기는 MotorContactReader가 맡고, Motor는 Contact 표본의 시작·무효화 수명만 관리한다.
History 세대는 외부 교정(ApplyCurrentState)에서만 올린다. 과거 복원은 같은 History 안의 이동이므로 다른 Snapshot을 무효화하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// 현재 상태 적용과 과거 복원으로 발생한 불연속의 종류입니다.
    /// </summary>
    // ============================================================
    public enum MotorDiscontinuityKind
    {
        None,
        CurrentStateApply,
        HistoricalRestore
    }

    // ============================================================
    /// <summary>
    /// 과거 상태 복원을 시도한 결과입니다.
    /// </summary>
    // ============================================================
    public enum MotorRestoreStatus
    {
        Restored,
        Unsupported,
        ActorMismatch,
        StaleHistory,
        BackendRejected
    }

    // ============================================================
    /// <summary>
    /// <br/> 하나의 Actor에 속한 Body 상태·운동 적용을 관리합니다.
    /// <br/> 외부 Tick·Rollback Driver에는 의존하지 않습니다.
    /// </summary>
    // ============================================================
    public sealed class Motor<TState> where TState : unmanaged
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Backend를 쓰는 관측 객체가 기능을 확인할 때 사용합니다.
        /// </summary>
        // ------------------------------------------------------------
        internal IMotorBackend<TState> Backend => backend;
        private readonly IMotorBackend<TState> backend;

        private readonly IMotorHistoricalBackend<TState> historical;
        private readonly IMotorContactLifecycle contactLifecycle;
        private readonly IMotorContactObservationRevision contactRevision;
        private long historyGeneration = 1;
        private bool operationInProgress;
        private bool physicsStepStarted;
        private bool frameApplied;
        private bool contactsObserved;
        private bool contactStepSupported;
        private long contactObservationRevision;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Motor를 소유하는 실행 Scope와 Actor입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Backend가 과거 Owned State 복원을 지원하는지 알려줍니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool SupportsHistoricalRestore => historical != null;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 최근 완료된 Physics Step의 Contact 관측 시점입니다.
        /// <br/> Correction 또는 Restore 이후에는 -1입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long ObservedPhysicsStep => observedPhysicsStep;
        private long observedPhysicsStep = -1;

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Step Index 재사용 시에도 관측 수명을 구분합니다.
        /// </summary>
        // ------------------------------------------------------------
        public long PhysicsObservationVersion => physicsObservationVersion;
        private long physicsObservationVersion;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막으로 발생한 상태 불연속의 누적 번호입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long DiscontinuityVersion => discontinuityVersion;
        private long discontinuityVersion;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막으로 발생한 불연속의 종류입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorDiscontinuityKind LastDiscontinuity { get; private set; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Actor와 Body Backend를 연결합니다.
        /// </summary>
        // ------------------------------------------------------------
        public Motor
        (
            MotionActorKey actor,
            IMotorBackend<TState> backend
        )
        {
            if (actor.ScopeID <= 0 || actor.ActorID <= 0 || actor.Generation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(actor));
            }

            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            historical = backend as IMotorHistoricalBackend<TState>;
            contactLifecycle = backend as IMotorContactLifecycle;
            contactRevision = backend as IMotorContactObservationRevision;
            Actor = actor;
        }

    #endregion

    #region 작업 구간

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 수명을 바꾸거나 수명에 의존하는 작업 구간을 시작합니다.
        /// <br/> 이미 진행 중이면 재진입으로 보고 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        internal void EnterOperation()
        {
            if (operationInProgress)
            {
                throw new InvalidOperationException("Motor 작업 중에는 같은 Motor의 작업을 다시 시작할 수 없습니다.");
            }

            operationInProgress = true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// EnterOperation으로 시작한 작업 구간을 끝냅니다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ExitOperation()
        {
            operationInProgress = false;
        }

    #endregion

    #region 현재 상태와 운동 적용

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 Body에서 현재 상태를 읽습니다.
        /// </summary>
        // ------------------------------------------------------------
        public TState ReadCurrentState() => backend.ReadCurrentState();

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 서버 확정 상태 또는 Teleport를 현재 Body에 적용합니다.
        /// <br/> 이전 History Snapshot은 이 호출 이후 무효화됩니다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyCurrentState(in TState state)
        {
            EnterOperation();

            try
            {
                backend.ApplyCurrentState(state);

                // Body가 바뀐 뒤에는 과거 Snapshot과 이전 관측을 다시 쓰지 못하게 먼저 확정한다.
                historyGeneration = checked(historyGeneration + 1);
                ResetObservation();
                MarkDiscontinuity(MotorDiscontinuityKind.CurrentStateApply);
                ClearBackendContacts();
            }
            finally
            {
                ExitOperation();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 같은 Actor의 Tick 합성 결과를 Body에 한 번 적용합니다.
        /// <br/> 다른 Actor 결과나 Backend가 거절한 값은 false입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryApply(in MotionFrame frame)
        {
            if (!frame.Actor.Equals(Actor))
            {
                return false;
            }

            // Tick당 한 번만 Body에 쓰도록 Physics Step 경계로 적용 횟수를 제한한다.
            if (physicsStepStarted)
            {
                throw new InvalidOperationException("Physics Step 중에는 합성 결과를 적용할 수 없습니다.");
            }

            if (frameApplied)
            {
                throw new InvalidOperationException("다음 BeginPhysicsStep 전에 합성 결과를 다시 적용할 수 없습니다.");
            }

            if (!backend.TryApply(frame))
            {
                return false;
            }

            frameApplied = true;
            return true;
        }

    #endregion

    #region Physics 관측

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 Physics Step 전에 호출하며 중복 시작은 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void BeginPhysicsStep()
        {
            if (physicsStepStarted)
            {
                throw new InvalidOperationException("진행 중인 Physics Step을 다시 시작할 수 없습니다.");
            }

            EnterOperation();

            try
            {
                ResetObservation();
                ClearBackendContacts();

                // Backend의 관측 시작이 실패하면 진행 상태로 바꾸지 않아 다음 Step을 다시 시작할 수 있다.
                var supported = IsContactLifecycleSupported();

                if (supported)
                {
                    contactLifecycle.BeginContactStep();
                }

                contactObservationRevision = ReadContactObservationRevision();
                contactStepSupported = supported;
                physicsStepStarted = true;
                frameApplied = false;
            }
            finally
            {
                ExitOperation();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Host가 Physics Step을 완료한 시점에 호출합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void CompletePhysicsStep(long step)
        {
            if (step < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            EnterOperation();

            try
            {
                if (!physicsStepStarted)
                {
                    throw new InvalidOperationException("먼저 BeginPhysicsStep을 호출해야 합니다.");
                }

                physicsStepStarted = false;
                observedPhysicsStep = step;
                contactsObserved = contactStepSupported;
            }
            finally
            {
                ExitOperation();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 마지막 완료 Step의 Contact 표본이 아직 유효한지 확인합니다.
        /// <br/> 진행 중 Step·Correction·센서 세대 변경 뒤에는 무효입니다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool IsContactObservationCurrent() =>
            contactsObserved && !physicsStepStarted &&
            ReadContactObservationRevision() == contactObservationRevision;

        private long ReadContactObservationRevision() =>
            contactRevision != null ? contactRevision.ContactObservationRevision : 0;

    #endregion

    #region Historical State

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 지원하는 Backend의 과거 Owned State를 캡처합니다.
        /// <br/> 외부 Host가 Snapshot 보관 기간을 결정합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryCaptureHistorical(long step, out MotorSnapshot<TState> snapshot)
        {
            if (step < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            if (historical == null)
            {
                snapshot = default;
                return false;
            }

            EnterOperation();

            try
            {
                var state = historical.CaptureHistoricalState();
                snapshot = new MotorSnapshot<TState>
                (
                    Actor, step, historyGeneration, state
                );
                return true;
            }
            finally
            {
                ExitOperation();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Actor·History 수명이 일치하는 과거 상태만 복원합니다.
        /// <br/> 외부 World 조건이 다르면 Backend가 복원을 거절합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorRestoreStatus RestoreHistorical(in MotorSnapshot<TState> snapshot)
        {
            if (!snapshot.Actor.Equals(Actor))
            {
                return MotorRestoreStatus.ActorMismatch;
            }

            if (historical == null)
            {
                return MotorRestoreStatus.Unsupported;
            }

            if (snapshot.HistoryGeneration != historyGeneration || snapshot.Step < 0)
            {
                return MotorRestoreStatus.StaleHistory;
            }

            EnterOperation();

            try
            {
                if (!historical.TryRestoreHistoricalState(snapshot.Step, snapshot.State))
                {
                    return MotorRestoreStatus.BackendRejected;
                }

                // 복원된 Body와 이전 관측이 섞이지 않도록 Backend 통지 전에 관측을 무효화한다.
                ResetObservation();
                MarkDiscontinuity(MotorDiscontinuityKind.HistoricalRestore);
                ClearBackendContacts();
                return MotorRestoreStatus.Restored;
            }
            finally
            {
                ExitOperation();
            }
        }

    #endregion

    #region 유틸

        private bool IsContactLifecycleSupported() =>
            contactLifecycle != null && contactLifecycle.SupportsContactObservation;

        private void ResetObservation()
        {
            contactsObserved = false;
            contactStepSupported = false;
            physicsStepStarted = false;
            observedPhysicsStep = -1;
            physicsObservationVersion = checked(physicsObservationVersion + 1);
        }

        private void ClearBackendContacts()
        {
            if (IsContactLifecycleSupported())
            {
                contactLifecycle.ClearContactObservation();
            }
        }

        private void MarkDiscontinuity(MotorDiscontinuityKind kind)
        {
            discontinuityVersion = checked(discontinuityVersion + 1);
            LastDiscontinuity = kind;
        }

    #endregion

    }
}
