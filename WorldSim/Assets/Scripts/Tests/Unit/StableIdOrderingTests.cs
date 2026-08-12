// 稳定 ID 排序遍历 — 使用生产 StableIdSet (G0-3 / 铁律 3).

using System.Collections.Generic;
using NUnit.Framework;
using WorldSim.Simulation.Core;

namespace WorldSim.Tests.Unit
{
    [TestFixture]
    [Category("Gate0Determinism")]
    public class StableIdOrderingTests
    {
        [Test]
        public void SortedByStableId_IndependentOfInsertionOrder()
        {
            var a = new List<int> { 5, 1, 9, 2, 7 };
            var b = new List<int> { 2, 9, 7, 1, 5 };
            a.Sort();
            b.Sort();
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void StableIdSet_SortedStableIds_IgnoresHashSetOrder()
        {
            var set = new StableIdSet();
            set.Add(12);
            set.Add(3);
            set.Add(8);
            set.Add(3);
            set.Add(21);
            CollectionAssert.AreEqual(new[] { 3, 8, 12, 21 }, set.SortedStableIds());
        }

        [Test]
        public void ActiveEntities_WeeklySubSettlement_UsesSortedOrder()
        {
            var active = new StableIdSet();
            foreach (var id in new[] { 12, 3, 8, 21 }) active.Add(id);
            CollectionAssert.AreEqual(new[] { 3, 8, 12, 21 }, active.SortedStableIds());
        }
    }
}
