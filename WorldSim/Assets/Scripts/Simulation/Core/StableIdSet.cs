namespace WorldSim.Simulation.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// 活跃实体脏集合 (铁律 3): HashSet 仅存成员判定, 遍历前必须按稳定 ID 升序.
    /// 禁止直接迭代 HashSet 自然序进逻辑 (S4 §7.3). 纯 System.*.
    /// </summary>
    public sealed class StableIdSet
    {
        private readonly HashSet<int> _ids = new HashSet<int>();

        public void Add(int id) => _ids.Add(id);
        public void Remove(int id) => _ids.Remove(id);
        public bool Contains(int id) => _ids.Contains(id);
        public int Count => _ids.Count;

        /// <summary>确定性遍历序: 稳定 ID 升序 (禁止依赖 HashSet 自然迭代序).</summary>
        public List<int> SortedStableIds()
        {
            var list = new List<int>(_ids);
            list.Sort();
            return list;
        }
    }
}
