/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveMigration.cs
수정일 : 2026-11-25

# 설명
Spec Version이 바뀔 때 저장된 Instance의 키·ID를 새 Version에 맞게 옮기는 선언형 변환.

# 특이사항, 제약사항
값 자체는 바꾸지 않고 이름(Progress 키, EntryID, 순차 Group의 NodeID)만 옮기거나 버린다.
매핑에 없는 이름은 그대로 둔다. 서로 다른 이름을 같은 새 이름으로 옮기는 선언은 만들 때 거부한다.
옮긴 결과가 새 Spec과 맞지 않으면 Engine 생성이 거부한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 한 Spec의 Version 하나를 다음 Version으로 옮기는 이름 매핑.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveMigration
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 변환할 Spec의 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string SpecID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 변환 전 Version.
        /// </summary>
        // ------------------------------------------------------------
        public int FromVersion { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 변환 후 Version.
        /// </summary>
        // ------------------------------------------------------------
        public int ToVersion { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Progress 키 이름 변경(이전 → 새). 값이 null이면 그 키를 버린다.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyDictionaryView<string, string> ProgressKeys => new(progressKeys);
        private readonly Dictionary<string, string> progressKeys;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// EntryID 변경(이전 → 새). 값이 null이면 그 Entry의 통과·경계·시각 기록을 버린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ReadOnlyDictionaryView<string, string> EntryIDs => new(entryIDs);
        private readonly Dictionary<string, string> entryIDs;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 순차 Group의 NodeID 변경(이전 → 새). 값이 null이면 그 Group의 경계를 버린다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ReadOnlyDictionaryView<string, string> GroupIDs => new(groupIDs);
        private readonly Dictionary<string, string> groupIDs;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 이름 매핑을 복사해 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveMigration
        (
            string specID, int fromVersion, int toVersion,
            IReadOnlyDictionary<string, string> progressKeys = null,
            IReadOnlyDictionary<string, string> entryIDs = null,
            IReadOnlyDictionary<string, string> groupIDs = null
        )
        {
            if (string.IsNullOrWhiteSpace(specID))
            {
                throw new ArgumentException("Spec ID must be specified.", nameof(specID));
            }

            if (fromVersion <= 0 || toVersion <= fromVersion)
            {
                throw new ArgumentException
                (
                    $"Migration must go to a higher version: r{fromVersion} -> r{toVersion}."
                );
            }

            SpecID = specID;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            this.progressKeys = Copy(progressKeys, nameof(progressKeys));
            this.entryIDs = Copy(entryIDs, nameof(entryIDs));
            this.groupIDs = Copy(groupIDs, nameof(groupIDs));
        }

        private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> source, string name)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var targets = new HashSet<string>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (var pair in source)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || (pair.Value != null && string.IsNullOrWhiteSpace(pair.Value)))
                    {
                        throw new ArgumentException($"Migration names must not be blank: '{pair.Key}' -> '{pair.Value}'.", name);
                    }

                    if (pair.Value != null && !targets.Add(pair.Value))
                    {
                        throw new ArgumentException($"Migration maps more than one name to '{pair.Value}'.", name);
                    }

                    result.Add(pair.Key, pair.Value);
                }
            }

            return result;
        }

    #endregion

    #region 이름 매핑

        // --------------------------------------------------------------------------------
        /// <summary>
        /// EntryID 하나를 새 이름으로 옮긴다. 매핑에 없으면 그대로 두고, 값이 null이면 버린다(false).
        /// </summary>
        // --------------------------------------------------------------------------------
        internal bool TryMapEntryID(string entryID, out string mapped) => TryMap(EntryIDs, entryID, out mapped);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>순차 Group의 NodeID 하나를 새 이름으로 옮긴다.
        /// <br/>매핑에 없으면 그대로 두고, 값이 null이면 버린다(false).
        /// </summary>
        // ------------------------------------------------------------
        internal bool TryMapGroupID(string groupID, out string mapped) => TryMap(GroupIDs, groupID, out mapped);

        private static bool TryMap(ReadOnlyDictionaryView<string, string> map, string name, out string mapped)
        {
            if (map.TryGetValue(name, out mapped))
            {
                return mapped != null;
            }

            mapped = name;
            return true;
        }

    #endregion

    }
}
