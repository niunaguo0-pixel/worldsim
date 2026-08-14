namespace WorldSim.Narrative
{
    using System;
    using System.Collections.Generic;
    using WorldSim.Simulation.Core;

    /// <summary>
    /// 同月事件模式检测：把多条 SimEvent 合成更高可读的编年条目。
    /// 纯函数式扫描，不持有 WorldState。
    /// </summary>
    public static class NarrativePatternDetector
    {
        /// <summary>
        /// 对「本批新增」事件做模式合成。输入须已按时间序；返回的复合条目标记 IsComposite。
        /// </summary>
        public static void Detect(
            IReadOnlyList<SimEvent> batch,
            List<ChronicleEntry> output)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (batch.Count == 0) return;

            int i = 0;
            while (i < batch.Count)
            {
                int month = batch[i].gameMonth;
                int start = i;
                while (i < batch.Count && batch[i].gameMonth == month) i++;
                int end = i;
                TryWarArc(batch, start, end, output);
                TryEraWithTurmoil(batch, start, end, output);
                TryDisasterCluster(batch, start, end, output);
            }
        }

        private static void TryWarArc(
            IReadOnlyList<SimEvent> batch,
            int start,
            int end,
            List<ChronicleEntry> output)
        {
            bool declared = false;
            bool resolved = false;
            int sourceId = 0;
            double mag = 0;
            int month = batch[start].gameMonth;
            for (int i = start; i < end; i++)
            {
                var e = batch[i];
                if (IsWarDeclared(e.templateId) || e.category == SimEventCategory.War && e.templateId == "civ.war")
                {
                    declared = true;
                    sourceId = e.sourceId;
                    mag = Math.Max(mag, e.magnitude);
                }
                if (string.Equals(e.templateId, "civ.war.resolved", StringComparison.Ordinal))
                {
                    resolved = true;
                    sourceId = e.sourceId;
                    mag = Math.Max(mag, e.magnitude);
                }
            }

            if (declared && resolved)
            {
                output.Add(new ChronicleEntry(
                    month,
                    SimEventCategory.War,
                    sourceId,
                    "pattern.war.arc",
                    "一战役事落幕",
                    $"第 {month} 月内，源 {sourceId} 相关战事自爆发至平息（峰值 {mag:0.###}）。",
                    mag,
                    ChronicleSignificance.Critical,
                    isComposite: true));
            }
        }

        private static void TryEraWithTurmoil(
            IReadOnlyList<SimEvent> batch,
            int start,
            int end,
            List<ChronicleEntry> output)
        {
            bool era = false;
            bool turmoil = false;
            int sourceId = 0;
            double mag = 0;
            int month = batch[start].gameMonth;
            for (int i = start; i < end; i++)
            {
                var e = batch[i];
                if (e.category == SimEventCategory.Era
                    || string.Equals(e.templateId, "civ.era.transition", StringComparison.Ordinal)
                    || string.Equals(e.templateId, "civ.era", StringComparison.Ordinal))
                {
                    era = true;
                    sourceId = e.sourceId;
                    mag = Math.Max(mag, e.magnitude);
                }
                if (string.Equals(e.templateId, "civ.stability.warning", StringComparison.Ordinal)
                    || string.Equals(e.templateId, "civ.polity.turnover", StringComparison.Ordinal)
                    || e.category == SimEventCategory.War)
                {
                    turmoil = true;
                    mag = Math.Max(mag, e.magnitude);
                }
            }

            if (era && turmoil)
            {
                output.Add(new ChronicleEntry(
                    month,
                    SimEventCategory.Era,
                    sourceId,
                    "pattern.era.turmoil",
                    "乱世中的时代跃迁",
                    $"第 {month} 月时代过渡与动荡同现（源 {sourceId}）。",
                    mag,
                    ChronicleSignificance.Critical,
                    isComposite: true));
            }
        }

        private static void TryDisasterCluster(
            IReadOnlyList<SimEvent> batch,
            int start,
            int end,
            List<ChronicleEntry> output)
        {
            int disasterCount = 0;
            int sourceId = 0;
            double mag = 0;
            int month = batch[start].gameMonth;
            for (int i = start; i < end; i++)
            {
                var e = batch[i];
                if (e.category == SimEventCategory.Disaster
                    || string.Equals(e.templateId, "ecology.disaster", StringComparison.Ordinal))
                {
                    disasterCount++;
                    sourceId = e.sourceId;
                    mag += e.magnitude;
                }
            }

            if (disasterCount >= 2)
            {
                output.Add(new ChronicleEntry(
                    month,
                    SimEventCategory.Disaster,
                    sourceId,
                    "pattern.disaster.cluster",
                    "连发灾害",
                    $"第 {month} 月内记录 {disasterCount} 起灾害（累计冲击 {mag:0.###}）。",
                    mag,
                    ChronicleSignificance.Critical,
                    isComposite: true));
            }
        }

        private static bool IsWarDeclared(string templateId) =>
            string.Equals(templateId, "civ.war.declared", StringComparison.Ordinal)
            || string.Equals(templateId, "civ.war", StringComparison.Ordinal);
    }
}
