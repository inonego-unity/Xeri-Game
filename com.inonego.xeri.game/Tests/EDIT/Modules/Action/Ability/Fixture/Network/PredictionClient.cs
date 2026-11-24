/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : PredictionClient.cs
수정일 : 2026-11-19

# 설명
L2 예측 계층의 테스트 전용 Client를 제공한다.
사용 직전 State 복사본으로 로컬 사용을 예측하고 서버 결과로 확정하거나 되돌리며 예측 투사체를 권한 Key로 인계한다.
승인된 예측 Session은 권한 측에서 그 실행이 끝나면 같은 종료 사유로 끝낸다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.TEST._Ability.Fixture
{
    using Xeri;
    using Xeri.Game;

    // ============================================================
    /// <summary>
    /// 예측 투사체이다. 권한 Key Registry와 분리된 표현 전용 객체이다.
    /// </summary>
    // ============================================================
    internal sealed class PredictedProjectile
    {
        internal int RequestID;
        internal int SpawnIndex;
        internal ulong Key;
        internal bool IsPredicted = true;
        internal int PresentationHits;
        internal int CuePlays;
        internal AbilityUseOrigin Origin;
        internal string DespawnReason;
    }

    // ============================================================
    /// <summary>
    /// 결과를 기다리는 예측 사용 하나와 되돌리기용 보관 상태이다.
    /// </summary>
    // ============================================================
    internal sealed class PredictedUse
    {
        internal int RequestID;
        internal AbilityState Kept;
        internal long KeptTick;
        internal int KeptSnapshots;
        internal bool HasKept;
        internal ExecutionSession Session;
        internal Lease Motion;
        internal ulong TemporaryID;
        internal object External;
        internal AbilityUseOrigin Origin;
        internal readonly List<PredictedProjectile> Projectiles = new();
    }

    // ================================================================================
    /// <summary>
    /// <br/> 사용 직전 State 복사본을 보관하고 로컬에서 TryActivate를 예측한 뒤 서버 결과로
    /// <br/> 확정하거나 되돌린다. 되돌릴 때는 보관 뒤 흐른 Tick만큼 Advance로 보정한다.
    /// <br/> 보관한 복사본이 부족하거나 보관 뒤 권한 Snapshot이 적용됐으면 복원하지 않고
    /// <br/> 권한 상태에 맡긴다. 순서 때문에 거절된 Stale은 그 요청의 예측만 취소한다.
    /// </summary>
    // ================================================================================
    internal sealed class PredictionClient
    {

    #region 구성

        private readonly FakeAbilityNetwork<AuthorityPacket> network;
        private readonly AuthorityClient client;
        private readonly int historyCapacity;
        private readonly Func<object> captureExternal;
        private readonly Dictionary<int, PredictedUse> pending = new();
        private readonly List<int> order = new();
        private readonly List<PredictedUse> accepted = new();
        private readonly Dictionary<ulong, ExecutionEndReason> endedBeforeAccept = new();
        private ulong nextTemporaryID = 9000;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Client의 결과·세션 전환·Owner 제거·자기 실행 종료 통지를
        /// <br/> 이 예측 계층이 받도록 연결한다.
        /// <br/> Client 하나에는 계층 하나만 연결할 수 있다.
        /// <br/> 보관 복사본은 최근 historyCapacity개까지만 유지한다.
        /// </summary>
        // ------------------------------------------------------------
        internal PredictionClient
        (
            FakeAbilityNetwork<AuthorityPacket> network,
            AuthorityClient client,
            int historyCapacity = int.MaxValue,
            Func<object> captureExternal = null
        )
        {
            this.network = network;
            this.client = client;
            this.historyCapacity = historyCapacity;
            this.captureExternal = captureExternal;
            client.Attach(Handle, Reset, Despawn, EndConfirmed);
        }

    #endregion

    #region 관측 기록

        internal readonly Dictionary<ulong, ulong> IdMap = new();
        internal readonly List<string> Log = new();
        internal readonly List<PredictedUse> RejectedUses = new();
        internal int Restores;
        internal int Corrections;
        internal int Confirmed;
        internal int Rejected;
        internal int Derived;
        internal int Superseded;
        internal int Begins;
        internal int Ignored;
        internal int Abandoned;
        internal int MotionReleased;

    #endregion

    #region 예측 시작

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 사용 직전 State 복사본을 보관하고 Predicted 모드 Session으로 로컬 사용을
        /// <br/> 예측해 요청을 보낸다. 입력을 받을 수 없거나 로컬 사용이 거부되면 null이다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal PredictedUse Predict(int delayTicks, int spawnCount = 0)
        {
            if (!client.AcceptsInput) return null;

            var kept = client.Kit.State.Clone();
            var keptTick = network.Tick;
            var keptSnapshots = client.AppliedCount(client.Owner, client.Kit.State.Key);
            var external = captureExternal?.Invoke();
            PredictedUse use = null;
            bool Begin(in AbilityUse<string> started)
            {
                use = new PredictedUse
                {
                    Kept = kept, KeptTick = keptTick, KeptSnapshots = keptSnapshots, HasKept = true,
                    TemporaryID = nextTemporaryID++, Origin = started.Origin, External = external,
                };
                StartPredictedSession(use);
                return true;
            }

            if (!client.Kit.TryUse(AuthorityKit.EntryName, client.Owner, 1, Begin, out _, out _))
            {
                return null;
            }

            var request = client.Create();
            use.RequestID = request.RequestID;
            SpawnPredicted(use, spawnCount);
            pending.Add(use.RequestID, use);
            order.Add(use.RequestID);
            TrimHistory();
            client.Send(request, delayTicks);
            return use;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 예측 사용의 Predicted 모드 Session과 Motion Lease를 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        private void StartPredictedSession(PredictedUse use)
        {
            var flow = new FixedTimelineExecutionFlowSpec(10f);
            var context = new ExecutionContext(client.Owner, client.Owner, ExecutionMode.Predicted);
            var session = new ExecutionSession(use.TemporaryID, context, flow.CreateRuntime(), maximumTime: flow.MaximumDuration);
            var motion = new Lease(() => MotionReleased++);
            session.OnEnded += (_, _) => motion.Release();
            session.Begin();
            Begins++;
            use.Session = session;
            use.Motion = motion;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 보관 복사본을 최근 historyCapacity개만 남기고 나머지는 버린다.
        /// </summary>
        // ------------------------------------------------------------
        private void TrimHistory()
        {
            var kept = 0;
            for (var index = order.Count - 1; index >= 0; index--)
            {
                var use = pending[order[index]];
                if (!use.HasKept) continue;

                kept++;
                if (kept > historyCapacity)
                {
                    use.HasKept = false;
                }
            }
        }

    #endregion

    #region 결과 처리

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 서버 결과를 처리한다. 이미 처리한 요청의 결과는 무시한다.
        /// <br/> 거절이면 그 위에 쌓인 뒤의 예측도 함께 취소한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Handle(AuthorityResult result)
        {
            if (!pending.TryGetValue(result.RequestID, out var use))
            {
                Ignored++;
                return;
            }

            var index = order.IndexOf(use.RequestID);
            pending.Remove(use.RequestID);
            order.RemoveAt(index);
            if (result.Status == AuthorityStatus.Accepted)
            {
                Accept(use, result);
                return;
            }

            if (result.Status == AuthorityStatus.Stale)
            {
                Supersede(use);
                return;
            }

            Reject(use, index == 0);
            while (index < order.Count)
            {
                // 거절된 예측 위에 쌓인 뒤의 예측도 같은 기반을 잃으므로 함께 취소한다.
                var derived = pending[order[index]];
                pending.Remove(order[index]);
                order.RemoveAt(index);
                Cancel(derived, "PredictionDerived");
                Derived++;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 예측을 확정하고 임시 ID를 권한 ID에 대응시키며 투사체를 인계한다.
        /// </summary>
        // ------------------------------------------------------------
        private void Accept(PredictedUse use, AuthorityResult result)
        {
            Confirmed++;
            accepted.Add(use);
            use.HasKept = false;
            IdMap[use.TemporaryID] = result.ExecutionID;
            Log.Add("accept:" + use.RequestID);
            HandOver(use, result);

            // 결과 채널보다 먼저 도착한 권한 종료가 있으면 확정한 Session을 바로 그 사유로 끝낸다.
            if (endedBeforeAccept.Remove(result.ExecutionID, out var reason))
            {
                End(use, reason);
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 권한 측에서 끝난 자기 실행의 승인된 예측 Session을 같은 사유로 끝낸다.
        /// <br/> 아직 승인 결과가 오지 않았으면 기억해 두었다가 승인할 때 끝낸다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void EndConfirmed(ulong executionID, ExecutionEndReason reason)
        {
            var use = accepted.Find(item => IdMap.TryGetValue(item.TemporaryID, out var id) && id == executionID);
            if (use != null)
            {
                End(use, reason);
                return;
            }

            // 대기 중인 예측이 없으면 나중에 승인될 실행도 없다.
            if (pending.Count > 0)
            {
                endedBeforeAccept[executionID] = reason;
            }
        }

        private void End(PredictedUse use, ExecutionEndReason reason)
        {
            accepted.Remove(use);
            if (use.Session.IsActive)
            {
                use.Session.End(reason);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 거절된 예측을 취소한다. 가장 오래된 예측이고 보관 복사본이 있으며 보관 뒤
        /// <br/> 예측한 그 Ability에 권한 Snapshot이 적용되지 않았을 때만 복사본으로 복원하고
        /// <br/> 흐른 Tick만큼 진행한다. 아니면 그 Ability에 이미 적용된 권한 값에 맡긴다.
        /// <br/> 다른 Ability의 Snapshot은 예측한 Ability를 덮지 않으므로 복원을 막지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Reject(PredictedUse use, bool oldest)
        {
            Rejected++;
            Log.Add("reject:" + use.RequestID);
            RejectedUses.Add(use);
            Cancel(use, "PredictionRejected");
            var applied = client.AppliedCount(client.Owner, client.Kit.State.Key);
            if (use.HasKept && oldest && applied == use.KeptSnapshots)
            {
                client.Kit.State.CloneFrom(use.Kept);
                client.Kit.State.Advance((network.Tick - use.KeptTick) * network.TickDelta);
                Restores++;
            }
            else
            {
                Corrections++;
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 요청 순서 때문에 서버가 거부한 예측은 상태 기반 거절이 아니므로 그 예측만 취소한다.
        /// <br/> 뒤의 예측은 자기 결과를 기다리고 State는 권한 Snapshot이 정정한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Supersede(PredictedUse use)
        {
            Superseded++;
            Log.Add("stale:" + use.RequestID);
            Cancel(use, "PredictionStale");
        }

    #endregion

    #region 예측 취소

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 새 권한 세대로 전환하면 이전 세대의 예측은 확정될 수 없으므로 모두 취소하고,
        /// <br/> 승인된 Session도 종료한다. 이전 세대 결과에 묶인 예측 Session은 새 세대의
        /// <br/> 결과·관측과 대응되지 않으며, 서버에서 계속되는 실행은 진행 중 관측으로 다시 받는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        private void Reset()
        {
            endedBeforeAccept.Clear();
            var failures = CancelAll("SessionReset");
            EndAccepted(ref failures);
            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Owner가 제거되면 대기 중인 예측은 확정될 수 없으므로 모두 취소하고 비운다.
        /// <br/> 이미 승인된 실행은 Owner에 속하므로 Session만 종료한다. 권한 Key를 받아
        /// <br/> 독립 수명이 된 투사체는 그대로 둔다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Despawn()
        {
            endedBeforeAccept.Clear();
            var failures = CancelAll("OwnerDespawned");
            EndAccepted(ref failures);
            AuthorityRig.Rethrow(failures);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 승인된 예측 Session을 Cancelled로 끝내고 비우며 종료 예외는 모은다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void EndAccepted(ref List<Exception> failures)
        {
            foreach (var use in accepted)
            {
                try
                {
                    if (use.Session.IsActive)
                    {
                        use.Session.End(ExecutionEndReason.Cancelled);
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            accepted.Clear();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 대기 중인 예측을 모두 취소하고 비우며 취소 예외는 모아서 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        private List<Exception> CancelAll(string reason)
        {
            List<Exception> failures = null;
            foreach (var id in order)
            {
                try
                {
                    Cancel(pending[id], reason);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }

                Abandoned++;
            }

            pending.Clear();
            order.Clear();
            return failures;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 예측 Session을 Cancelled로 끝내고 아직 예측 상태인 투사체를 제거한다.
        /// <br/> 승인 전에 권한 Key를 받은 투사체는 독립 수명이므로 남긴다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Cancel(PredictedUse use, string reason)
        {
            if (use.Session.IsActive)
            {
                use.Session.End(ExecutionEndReason.Cancelled);
            }

            foreach (var projectile in use.Projectiles)
            {
                if (projectile.IsPredicted)
                {
                    Remove(projectile, reason);
                }
            }
        }

    #endregion

    #region 투사체

        internal readonly List<PredictedProjectile> Projectiles = new();
        internal readonly List<PredictedProjectile> Removed = new();
        internal readonly AbilityFixtureResults<AbilityUseOrigin> ConfirmedHits = new();
        internal int Created;

        // ------------------------------------------------------------
        /// <summary>
        /// 예측 사용의 투사체 표현을 SpawnIndex 순서로 만든다.
        /// </summary>
        // ------------------------------------------------------------
        private void SpawnPredicted(PredictedUse use, int spawnCount)
        {
            for (var index = 0; index < spawnCount; index++)
            {
                var projectile = new PredictedProjectile
                {
                    RequestID = use.RequestID, SpawnIndex = index
                };
                projectile.CuePlays++;
                use.Projectiles.Add(projectile);
                Projectiles.Add(projectile);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 승인된 Spawn에 대응하는 예측 투사체를 인계하고 나머지는 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        private void HandOver(PredictedUse use, AuthorityResult result)
        {
            var matched = new HashSet<PredictedProjectile>();
            foreach (var spawn in result.Spawns)
            {
                var projectile = use.Projectiles.Find(item => item.SpawnIndex == spawn.SpawnIndex);
                if (projectile == null) continue;

                Adopt(projectile, use, spawn.Key);
                matched.Add(projectile);
            }

            foreach (var projectile in use.Projectiles)
            {
                if (!matched.Contains(projectile))
                {
                    Remove(projectile, "Unmatched");
                }
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 권한 Entity가 복제되어 왔을 때 인계된 표현을 반환한다.
        /// <br/> 승인 전에 도착해도 대기 중인 예측의 (RequestID, SpawnIndex)에
        /// <br/> 대응하는 표현이 있으면 그때 인계하고, 없으면 새 표현을 만든다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal PredictedProjectile AuthoritativeSpawnArrived(ulong key, int requestID, int spawnIndex)
        {
            var existing = Projectiles.Find(item => item.Key == key);
            if (existing != null) return existing;

            if (pending.TryGetValue(requestID, out var use))
            {
                var predicted = use.Projectiles.Find(item => item.SpawnIndex == spawnIndex);
                if (predicted != null && Projectiles.Contains(predicted))
                {
                    Adopt(predicted, use, key);
                    return predicted;
                }
            }

            var created = new PredictedProjectile
            {
                Key = key, IsPredicted = false
            };
            created.CuePlays++;
            Projectiles.Add(created);
            Created++;
            return created;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Hit을 처리한다. 예측 객체의 Hit은 연출 값으로만 남기고, 확정 결과는 권한
        /// <br/> Key를 받은 객체의 Hit만 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal void OnHit(PredictedProjectile projectile)
        {
            if (projectile.DespawnReason != null) return;

            if (projectile.IsPredicted)
            {
                projectile.PresentationHits++;
                return;
            }

            ConfirmedHits.Record(projectile.Origin, 9, 1);
        }

        private static void Adopt(PredictedProjectile projectile, PredictedUse use, ulong key)
        {
            projectile.Key = key;
            projectile.Origin = use.Origin;
            projectile.IsPredicted = false;
        }

        private void Remove(PredictedProjectile projectile, string reason)
        {
            if (!Projectiles.Remove(projectile)) return;

            projectile.DespawnReason = reason;
            Removed.Add(projectile);
        }

    #endregion

    }
}
