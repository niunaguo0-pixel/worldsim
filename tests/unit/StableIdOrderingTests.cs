// Phase 4 port target: WorldSim/Assets/Scripts/Tests/Unit/StableIdOrderingTests.cs
// asmdef: WorldSim.Tests
//
// 稳定 ID 排序遍历 (G0-3 / 铁律 3, 架构 §3.5, S4 §7.3)
// 覆盖: SortedByStableId 结果与插入序无关; activeEntities(HashSet) 迭代必须排序后遍历.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Gate0Determinism")]
    public class StableIdOrderingTests
    {
        /// <summary>模拟 SortedByStableId: 对稳定 ID 升序后遍历 (铁律 3).</summary>
        private static List<int> SortedByStableId(IEnumerable<int> ids)
        {
            var list = new List<int>(ids);
            list.Sort(); // 稳定 ID 升序
            return list;
        }

        [Test]
        public void SortedByStableId_IndependentOfInsertionOrder()
        {
            var a = new List<int> { 5, 1, 9, 2, 7 };
            var b = new List<int> { 2, 9, 7, 1, 5 }; // 不同插入序
            CollectionAssert.AreEqual(SortedByStableId(a), SortedByStableId(b));
        }

        [Test]
        public void HashSet_IterationIsNonDeterministic_RequiresSort()
        {
            // HashSet 迭代序在不同运行/平台不稳定 => 必须排序后遍历 (S4 §7.3 铁律 3)
            var set = new HashSet<int> { 1, 2, 3, 4, 5 };
            // 直接迭代序不可依赖; 取排序快照
            var deterministic = SortedByStableId(set);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, deterministic);
        }

        [Test]
        public void ActiveEntities_WeeklySubSettlement_UsesSortedOrder()
        {
            // 周级子结算只遍历 activeEntities, 且必须按稳定 ID 序 (架构 §3.5)
            var active = new HashSet<int> { 12, 3, 8, 3, 21 };
            var seq = SortedByStableId(active);
            CollectionAssert.AreEqual(new[] { 3, 8, 12, 21 }, seq);
        }
    }
}
