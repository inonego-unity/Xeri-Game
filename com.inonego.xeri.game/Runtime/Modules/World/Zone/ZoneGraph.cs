/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ZoneGraph.cs
수정일 : 2026-08-24

# 설명
Zone와 ZoneLink collection을 소유하고 topology lookup·validation을 제공하는 직렬화 가능한 공간 그래프.

# 제약사항
전역 CurrentZone, Actor 이동 상태, Unity Scene 수명, Stage 진행 상태를 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Actor cursor 없이 Zone topology 자체만 표현하는 공간 그래프.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ZoneGraph
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Graph가 소유한 Zone 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<Zone> Zones => zones;

        [SerializeField]
        private Zone[] zones = Array.Empty<Zone>();

        // ------------------------------------------------------------
        /// <summary>
        /// Graph가 소유한 Zone Link 목록.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<ZoneLink> ZoneLinks => zoneLinks;

        [SerializeField]
        private ZoneLink[] zoneLinks = Array.Empty<ZoneLink>();

        [NonSerialized]
        private Dictionary<string, Zone> zonesByID = null;

        [NonSerialized]
        private HashSet<ZoneLink> registeredLinks = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Serializer용 기본 생성자.
        /// </summary>
        // ------------------------------------------------------------
        public ZoneGraph() { }

        // ------------------------------------------------------------
        /// <summary>
        /// Zone와 Link collection으로 유효한 topology graph를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public ZoneGraph(IEnumerable<Zone> zones, IEnumerable<ZoneLink> zoneLinks)
        {
            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            if (zoneLinks == null)
            {
                throw new ArgumentNullException(nameof(zoneLinks));
            }

            this.zones = new List<Zone>(zones).ToArray();
            this.zoneLinks = new List<ZoneLink>(zoneLinks).ToArray();

            RebuildRegistry();
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화된 Graph 구성이 유효한지 확인하고 runtime lookup을 재구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Validate()
        {
            RebuildRegistry();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 안정 ID로 Graph의 Zone을 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetZone(string zoneID, out Zone zone)
        {
            EnsureRegistry();
            return zonesByID.TryGetValue(zoneID, out zone);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 Link와 source Zone으로 topology 목적지 Zone을 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetDestination(ZoneLink link, Zone source, out Zone destination)
        {
            destination = null;

            if (link == null || source == null)
            {
                return false;
            }

            EnsureRegistry();

            if (!registeredLinks.Contains(link))
            {
                return false;
            }

            if (!link.TryGetDestinationID(source.ZoneID, out var destinationID))
            {
                return false;
            }

            return zonesByID.TryGetValue(destinationID, out destination);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 Link가 이 Graph의 collection에 포함되는지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ContainsLink(ZoneLink link)
        {
            EnsureRegistry();
            return link != null && registeredLinks.Contains(link);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// runtime lookup이 아직 없으면 현재 serialized collection에서 재구성한다.
        /// </summary>
        // ------------------------------------------------------------
        private void EnsureRegistry()
        {
            if (zonesByID == null || registeredLinks == null)
            {
                RebuildRegistry();
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Zone ID와 Link endpoint를 검증하면서 runtime lookup을 다시 만든다.
        /// </summary>
        // ------------------------------------------------------------
        private void RebuildRegistry()
        {
            zones ??= Array.Empty<Zone>();
            zoneLinks ??= Array.Empty<ZoneLink>();

            zonesByID = new Dictionary<string, Zone>(StringComparer.Ordinal);
            registeredLinks = new HashSet<ZoneLink>();

            foreach (var zone in zones)
            {
                if (zone == null)
                {
                    throw new InvalidOperationException("ZoneGraph Zones 목록에는 null을 포함할 수 없습니다.");
                }

                if (!zone.IsDefined)
                {
                    throw new InvalidOperationException("ZoneGraph Zone에는 비어 있지 않은 Zone ID가 필요합니다.");
                }

                if (!zonesByID.TryAdd(zone.ZoneID, zone))
                {
                    throw new InvalidOperationException($"ZoneGraph에 중복 Zone ID가 있습니다: {zone.ZoneID}");
                }
            }

            foreach (var link in zoneLinks)
            {
                if (link == null)
                {
                    throw new InvalidOperationException("ZoneGraph Zone Links 목록에는 null을 포함할 수 없습니다.");
                }

                if (!link.HasDistinctEndpoints())
                {
                    throw new InvalidOperationException("ZoneGraph Zone Link에는 서로 다른 두 Zone 끝점이 필요합니다.");
                }

                if (!zonesByID.ContainsKey(link.Zone0ID) || !zonesByID.ContainsKey(link.Zone1ID))
                {
                    throw new InvalidOperationException("ZoneGraph Zone Link의 양 끝점 ID는 Zones 목록에 포함되어야 합니다.");
                }

                registeredLinks.Add(link);
            }
        }

    #endregion
    }
}
