/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveRuntimeBinding.cs
수정일 : 2026-11-25

# 설명
Save Owner가 역직렬화한 ObjectiveInstance들을 검증해 비영속 Engine을 재구성한다.

# 특이사항, 제약사항
외부 Fact Owner 준비 이후 호출한다. 저장 I/O와 영속 상태 변경·이벤트 발행은 담당하지 않는다.
Directory에는 초기 평가 전에 등록해 서로 조회하는 Condition이 같은 배치를 본다. 배치가 실패하면 등록을 모두 되돌린다.
같은 Scope·Spec은 목록에서 나중 Engine이 현재 회차다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 저장된 Objective 실행 집합을 원자적으로 검증하고 재바인딩한다.
    /// </summary>
    // ============================================================
    public static class ObjectiveRuntimeBinding
    {

    #region 복원

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>외부 Fact 준비 이후 저장된 실행들을 재바인딩한다.
        /// <br/>전체 검증과 초기 평가 성공 후에만 Engine 집합을 반환한다.
        /// <br/>directory가 있으면 초기 평가 동안 배치의 Engine을 등록해 두고, 실패하면 이전 등록과
        /// <br/>등록 순서로 되돌린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public static IReadOnlyList<ObjectiveEngine> Bind
        (
            IObjectiveSpecResolver resolver,
            IEnumerable<ObjectiveInstance> savedInstances,
            IObjectiveFactScope factScope = null,
            ObjectiveDirectory directory = null
        )
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (savedInstances == null)
            {
                throw new ArgumentNullException(nameof(savedInstances));
            }

            var values = new List<ObjectiveInstance>();
            var ids = HashSetPool<(string ScopeID, string InstanceID)>.Get();

            try
            {
                foreach (var saved in savedInstances)
                {
                    // 정확한 Version이 없으면 등록된 변환 체인으로 옮긴 새 Instance를 쓴다. 원본 저장 데이터는 바꾸지 않는다.
                    var instance = saved;
                    if (saved != null && resolver is IObjectiveInstanceMigrator migrator
                        && migrator.TryMigrate(saved, out var migrated))
                    {
                        instance = migrated;
                    }

                    if (instance == null || string.IsNullOrWhiteSpace(instance.ScopeID)
                        || string.IsNullOrWhiteSpace(instance.InstanceID))
                    {
                        throw new InvalidOperationException("Saved Objective instance is missing or invalid.");
                    }

                    if (!ids.Add((instance.ScopeID, instance.InstanceID)))
                    {
                        throw new InvalidOperationException
                        (
                            $"Duplicate Objective InstanceID '{instance.InstanceID}' in Scope '{instance.ScopeID}'."
                        );
                    }

                    values.Add(instance);
                }
            }
            finally
            {
                HashSetPool<(string ScopeID, string InstanceID)>.Release(ids);
            }

            // 배치 전체의 명세·Progress·이력을 확인한 뒤 초기 파생 평가를 구성한다.
            // 실패 시 Engine 집합을 외부에 전달하지 않고 영속 데이터는 변경하지 않는다.
            var engines = new List<ObjectiveEngine>(values.Count);
            foreach (var instance in values)
            {
                engines.Add(new ObjectiveEngine(resolver, instance, factScope, directory));
            }

            // 배치 안의 Condition이 서로를 조회할 수 있도록 초기 평가 전에 등록하고, 실패하면 이전 등록으로 되돌린다.
            var displaced = new List<(ObjectiveEngine Engine, ObjectiveEngine Previous)>(engines.Count);
            var registered = directory?.GetEngines();
            try
            {
                if (directory != null)
                {
                    foreach (var engine in engines)
                    {
                        directory.TryGetEngine(engine.Instance.ScopeID, engine.Spec.SpecID, out var previous);
                        directory.Register(engine);
                        displaced.Add((engine, previous));
                    }
                }

                foreach (var engine in engines)
                {
                    engine.InitializeEvaluation();
                }
            }
            catch
            {
                if (directory != null)
                {
                    for (int index = displaced.Count - 1; index >= 0; index--)
                    {
                        directory.Unregister(displaced[index].Engine);
                        if (displaced[index].Previous != null)
                        {
                            directory.Register(displaced[index].Previous);
                        }
                    }

                    directory.RestoreOrder(registered);
                }

                throw;
            }

            return engines.AsReadOnly();
        }

    #endregion

    }
}
