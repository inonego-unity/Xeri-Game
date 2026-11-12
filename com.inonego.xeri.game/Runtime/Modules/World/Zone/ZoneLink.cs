/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ZoneLink.cs
수정일 : 2026-08-24

# 설명
두 Zone ID 사이의 연결 방향과 원인별 runtime 통행 제한을 관리하는 직렬화 가능한 모델.

# 제약사항
특정 Actor의 현재 Zone, Unity Collider, 제한 원인의 지속 상태를 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections.Generic;

using UnityEngine;

using inonego.Xeri;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 두 Zone node 사이의 topology 연결과 runtime passability를 표현한다.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ZoneLink
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Link의 0번 끝점 Zone ID.
        /// </summary>
        // ------------------------------------------------------------
        public string Zone0ID => zone0ID;

        [SerializeField]
        private string zone0ID = "";

        // ------------------------------------------------------------
        /// <summary>
        /// Link의 1번 끝점 Zone ID.
        /// </summary>
        // ------------------------------------------------------------
        public string Zone1ID => zone1ID;

        [SerializeField]
        private string zone1ID = "";

        // ------------------------------------------------------------
        /// <summary>
        /// Zone 1에서 Zone 0 방향 이동도 허용할지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBidirectional => isBidirectional;

        [SerializeField]
        private bool isBidirectional = true;

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 runtime 제한 원인이 해제되어 현재 통과 가능한지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsPassable => Blocks.Count == 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Link를 막고 있는 독립 원인의 수.
        /// </summary>
        // ------------------------------------------------------------
        public int BlockCount => Blocks.Count;

        private Dictionary<int, string> Blocks => blocks ??= new Dictionary<int, string>();

        [NonSerialized]
        private Dictionary<int, string> blocks = null;

        [NonSerialized]
        private int nextBlockID = 1;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// runtime 통행 가능 상태가 변경된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ZoneLink> OnPassabilityChanged = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Serializer용 기본 생성자.
        /// </summary>
        // ------------------------------------------------------------
        public ZoneLink() { }

        // ------------------------------------------------------------
        /// <summary>
        /// 두 Zone ID와 방향 정책으로 Link를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public ZoneLink(string zone0ID, string zone1ID, bool isBidirectional = true)
        {
            if (string.IsNullOrWhiteSpace(zone0ID))
            {
                throw new ArgumentException("Zone Link의 0번 끝점 ID를 비워 둘 수 없습니다.", nameof(zone0ID));
            }

            if (string.IsNullOrWhiteSpace(zone1ID))
            {
                throw new ArgumentException("Zone Link의 1번 끝점 ID를 비워 둘 수 없습니다.", nameof(zone1ID));
            }

            if (string.Equals(zone0ID, zone1ID, StringComparison.Ordinal))
            {
                throw new ArgumentException("Zone Link의 양 끝점은 서로 달라야 합니다.");
            }

            this.zone0ID = zone0ID;
            this.zone1ID = zone1ID;
            this.isBidirectional = isBidirectional;
        }

    #endregion

    #region 메서드
        // ------------------------------------------------------------
        /// <summary>
        /// source Zone ID에서 이 Link를 통해 이동할 목적지 Zone ID를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetDestinationID(string sourceZoneID, out string destinationZoneID)
        {
            if (string.Equals(sourceZoneID, zone0ID, StringComparison.Ordinal))
            {
                destinationZoneID = zone1ID;
                return !string.IsNullOrWhiteSpace(destinationZoneID);
            }

            if (isBidirectional && string.Equals(sourceZoneID, zone1ID, StringComparison.Ordinal))
            {
                destinationZoneID = zone0ID;
                return !string.IsNullOrWhiteSpace(destinationZoneID);
            }

            destinationZoneID = null;
            return false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화된 양 끝점이 유효하고 서로 다른지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasDistinctEndpoints()
        {
            return !string.IsNullOrWhiteSpace(zone0ID)
                && !string.IsNullOrWhiteSpace(zone1ID)
                && !string.Equals(zone0ID, zone1ID, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 통행을 막는 새 runtime 원인을 추가하고 해당 원인 전용 Lease를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public Lease AcquireBlock(string cause)
        {
            if (string.IsNullOrWhiteSpace(cause))
            {
                throw new ArgumentException("통행 제한 원인을 비워 둘 수 없습니다.", nameof(cause));
            }

            if (nextBlockID <= 0)
            {
                nextBlockID = 1;
            }

            var wasPassable = IsPassable;
            var blockID = nextBlockID++;

            // 같은 이름의 원인도 서로 다른 Lease로 유지해 각 소유자가 자신의 제한만 해제하게 한다.
            Blocks.Add(blockID, cause);

            if (wasPassable)
            {
                OnPassabilityChanged?.Invoke(this);
            }

            return new Lease(() => ReleaseBlock(blockID));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 하나의 Lease가 소유한 runtime 통행 제한만 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        private void ReleaseBlock(int blockID)
        {
            if (!Blocks.Remove(blockID) || !IsPassable)
            {
                return;
            }

            // 마지막 제한이 해제된 순간만 passability 전환을 알린다.
            OnPassabilityChanged?.Invoke(this);
        }

    #endregion
    }
}
