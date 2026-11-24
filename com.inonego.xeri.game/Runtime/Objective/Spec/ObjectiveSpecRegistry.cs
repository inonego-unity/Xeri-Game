/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveSpecRegistry.cs
수정일 : 2026-11-25

# 설명
저작 소스에 독립적으로 ID/Version별 Objective Spec을 등록·해결한다.

# 특이사항, 제약사항
한 배치에 오류가 있으면 등록 결과가 부분 변경되지 않는다.
Version 변환(ObjectiveMigration)을 함께 등록하면 정확한 Version이 없는 저장 Instance를 등록된 Version까지 옮긴다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Engine과 Save 복원 경계가 Spec을 해결하는 최소 계약.
    /// </summary>
    // ============================================================
    public interface IObjectiveSpecResolver
    {
        // ------------------------------------------------------------
        /// <summary>
        /// ID와 정확한 Version으로 등록된 정의를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        bool TryResolve(string specID, int version, out ObjectiveSpec spec);
    }

    // ============================================================
    /// <summary>
    /// 검증된 정적 Objective Spec의 조회 인덱스.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveSpecRegistry : IObjectiveSpecResolver, IObjectiveInstanceMigrator
    {

    #region 필드

        private readonly Dictionary<(string ID, int Version), ObjectiveSpec> specs = new();
        private readonly Dictionary<(string ID, int Version), ObjectiveMigration> migrations = new();

    #endregion

    #region 등록

        // ------------------------------------------------------------
        /// <summary>
        /// 단일 Spec을 검증하고 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Register(ObjectiveSpec spec)
        {
            var single = new[]
            {
                spec
            };
            RegisterRange(single);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 여러 소스의 Spec을 검증한 뒤 한 번에 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        public void RegisterRange(IEnumerable<ObjectiveSpec> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var pending = new Dictionary<(string ID, int Version), ObjectiveSpec>();
            foreach (var spec in source)
            {
                ObjectiveSpecValidator.Validate(spec);
                var key = (spec.SpecID, spec.Version);
                if (specs.ContainsKey(key) || !pending.TryAdd(key, spec))
                {
                    throw new ArgumentException
                    (
                        $"Spec ID/Version already registered: '{spec.SpecID}' r{spec.Version}.", nameof(source)
                    );
                }
            }

            foreach (var item in pending)
            {
                specs.Add(item.Key, item.Value);
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// 정확한 Version이 없으면 다른 Version으로 대체하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryResolve(string specID, int version, out ObjectiveSpec spec)
        {
            if (string.IsNullOrWhiteSpace(specID) || version <= 0)
            {
                spec = null;
                return false;
            }

            return specs.TryGetValue((specID, version), out spec);
        }

    #endregion

    #region Version 변환

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>한 Version에서 다음 Version으로 옮기는 변환을 등록한다.
        /// <br/>같은 출발 Version의 변환은 하나만 둔다.
        /// </summary>
        // ------------------------------------------------------------
        public void RegisterMigration(ObjectiveMigration migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (!migrations.TryAdd((migration.SpecID, migration.FromVersion), migration))
            {
                throw new ArgumentException
                (
                    $"Migration already registered: '{migration.SpecID}' from r{migration.FromVersion}.",
                    nameof(migration)
                );
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>정확한 Version이 등록돼 있지 않은 Instance를 변환 체인을 따라 등록된 Version까지
        /// <br/>옮긴다.
        /// <br/>이미 등록된 Version이거나 체인이 등록된 Version에 닿지 않으면 false다.
        /// <br/>원본은 바꾸지 않는다.
        /// <br/>변환 결과가 유효하지 않으면(매핑한 새 이름이 기존 이름과 겹침) 예외로 거부하며 false로
        /// <br/>숨기지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool TryMigrate(ObjectiveInstance instance, out ObjectiveInstance migrated)
        {
            migrated = null;
            if (instance == null || TryResolve(instance.SpecID, instance.SpecVersion, out _))
            {
                return false;
            }

            var current = instance;
            while (!TryResolve(current.SpecID, current.SpecVersion, out _))
            {
                if (!migrations.TryGetValue((current.SpecID, current.SpecVersion), out var migration))
                {
                    return false;
                }

                current = current.CreateMigrated(migration);
            }

            migrated = current;
            return true;
        }

    #endregion

    }
}
