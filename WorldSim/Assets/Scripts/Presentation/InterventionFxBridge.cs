namespace WorldSim.Presentation
{
    using UnityEngine;
    using WorldSim.Simulation.Intervention;

    /// <summary>
    /// S1-4 表现桥: 消费因果链节点（游戏月锚定），驱动轻量落点/渐变提示.
    /// 不回写 WorldState（架构 §2.7）.
    /// </summary>
    public sealed class InterventionFxBridge : MonoBehaviour
    {
        private InterventionSystem _sys;
        private int _seenCausal;
        private string _lastFx = "";

        public string LastFx => _lastFx;
        public int SeenCausalCount => _seenCausal;

        public void Bind(InterventionSystem interventions)
        {
            _sys = interventions;
            _seenCausal = 0;
            _lastFx = "";
        }

        private void Update()
        {
            if (_sys == null) return;
            var chain = _sys.CausalChain;
            while (_seenCausal < chain.Count)
            {
                var n = chain[_seenCausal++];
                // 即时落点 vs 渐变响应：仅表现层分支
                if (n.EventTemplateId != null && n.EventTemplateId.Contains("drop.instant"))
                    _lastFx = $"M{n.MonthExecuted} DROP {n.ActionKey} mag={n.Magnitude:0.###}";
                else
                    _lastFx = $"M{n.MonthExecuted} FX {n.EventTemplateId} mag={n.Magnitude:0.###}";
            }
        }
    }
}
