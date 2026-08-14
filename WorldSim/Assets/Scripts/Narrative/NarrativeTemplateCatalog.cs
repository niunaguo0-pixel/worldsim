namespace WorldSim.Narrative
{
    using System;
    using WorldSim.Simulation.Core;

    /// <summary>templateId → 可读标题/正文/显著性。未知模板走通用回退。</summary>
    public static class NarrativeTemplateCatalog
    {
        public static ChronicleEntry FromEvent(SimEvent simEvent)
        {
            string id = simEvent.templateId ?? "";
            Resolve(id, simEvent, out string title, out string body, out ChronicleSignificance significance);
            return new ChronicleEntry(
                simEvent.gameMonth,
                simEvent.category,
                simEvent.sourceId,
                id,
                title,
                body,
                simEvent.magnitude,
                significance);
        }

        public static void Resolve(
            string templateId,
            SimEvent simEvent,
            out string title,
            out string body,
            out ChronicleSignificance significance)
        {
            switch (templateId)
            {
                case "civ.individual.death":
                    title = "重要人物辞世";
                    body = $"实体 {simEvent.sourceId} 在第 {simEvent.gameMonth} 月离世（量度 {Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.High;
                    return;
                case "civ.individual.inheritance":
                    title = "权力继承";
                    body = $"实体 {simEvent.sourceId} 完成继承交接（量度 {Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.High;
                    return;
                case "civ.generation.milestone":
                    title = "世代里程碑";
                    body = $"政体/氏族 {simEvent.sourceId} 进入新世代节点。";
                    significance = ChronicleSignificance.Normal;
                    return;
                case "civ.polity.turnover":
                    title = "政体更替";
                    body = $"政体 {simEvent.sourceId} 合法性震荡（{Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.High;
                    return;
                case "civ.war":
                case "civ.war.declared":
                    title = "战事爆发";
                    body = $"源 {simEvent.sourceId} 卷入战争（{Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.Critical;
                    return;
                case "civ.war.resolved":
                    title = "战事平息";
                    body = $"源 {simEvent.sourceId} 一侧战事落定（比值 {Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.High;
                    return;
                case "civ.era":
                case "civ.era.transition":
                    title = "时代跃迁";
                    body = $"实体 {simEvent.sourceId} 触发时代过渡（利用率 {Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.Critical;
                    return;
                case "civ.stability.warning":
                    title = "稳定度告警";
                    body = $"政体 {simEvent.sourceId} 稳定度降至 {Format(simEvent.magnitude)}。";
                    significance = ChronicleSignificance.High;
                    return;
                case "ecology.disaster":
                    title = "生态灾害";
                    body = $"区域源 {simEvent.sourceId} 爆发灾害（种群冲击 {Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.Critical;
                    return;
                case "ecology.warning.critical":
                case "ecology.warning.emergency":
                case "ecology.warning.stress":
                    title = "生态前兆";
                    body = $"源 {simEvent.sourceId} 发出 {templateId}（{Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.High;
                    return;
                case "intervene.shield.absorb":
                    title = "护盾吸收";
                    body = $"紧急护盾在源 {simEvent.sourceId} 生效（{Format(simEvent.magnitude)}）。";
                    significance = ChronicleSignificance.Normal;
                    return;
                default:
                    title = CategoryFallbackTitle(simEvent.category);
                    body = string.IsNullOrEmpty(templateId)
                        ? $"未命名事件 · 源 {simEvent.sourceId} · {Format(simEvent.magnitude)}"
                        : $"{templateId} · 源 {simEvent.sourceId} · {Format(simEvent.magnitude)}";
                    significance = simEvent.category == SimEventCategory.War
                        || simEvent.category == SimEventCategory.Disaster
                        || simEvent.category == SimEventCategory.Era
                        ? ChronicleSignificance.High
                        : ChronicleSignificance.Normal;
                    return;
            }
        }

        private static string CategoryFallbackTitle(SimEventCategory category)
        {
            switch (category)
            {
                case SimEventCategory.Ecology: return "生态纪事";
                case SimEventCategory.Civ: return "文明纪事";
                case SimEventCategory.War: return "战事纪事";
                case SimEventCategory.Disaster: return "灾害纪事";
                case SimEventCategory.Era: return "时代纪事";
                case SimEventCategory.Chronicle: return "编年附录";
                default: return "世界纪事";
            }
        }

        private static string Format(double magnitude) =>
            magnitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
