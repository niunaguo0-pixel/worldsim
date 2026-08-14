namespace WorldSim.Presentation
{
    using System;
    using WorldSim.Simulation.Core;

    /// <summary>
    /// Epic 6 P3：月/周边界间表现插值纯函数。插值结果绝不回写 WorldState。
    /// </summary>
    public static class PresentationInterpolator
    {
        /// <summary>
        /// 当前边界区间内进度 ∈ [0,1]；gameClock 落在
        /// [boundaryIndex * seconds, (boundaryIndex+1) * seconds)。
        /// </summary>
        public static double BoundaryAlpha(double gameClock, int boundaryIndex, double boundarySeconds)
        {
            if (boundarySeconds <= 0.0) throw new ArgumentOutOfRangeException(nameof(boundarySeconds));
            if (boundaryIndex < 0) throw new ArgumentOutOfRangeException(nameof(boundaryIndex));
            if (double.IsNaN(gameClock) || double.IsInfinity(gameClock))
                throw new ArgumentOutOfRangeException(nameof(gameClock));

            double start = boundaryIndex * boundarySeconds;
            double t = (gameClock - start) / boundarySeconds;
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t;
        }

        public static double MonthAlpha(in TimeDriver time) =>
            BoundaryAlpha(time.gameClock, time.monthIndex, TimeDriver.MONTH_SECONDS);

        public static double WeekAlpha(in TimeDriver time) =>
            BoundaryAlpha(time.gameClock, time.weekIndex, TimeDriver.WEEK_SECONDS);

        /// <summary>优先用周进度（更细），暂停时冻结在当前 alpha。</summary>
        public static double DisplayAlpha(in TimeDriver time)
        {
            if (time.paused) return WeekAlpha(time);
            return WeekAlpha(time);
        }

        public static double SmoothStep(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - 2.0 * t);
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static float LerpF(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>逻辑态在边界上的表现采样（只读副本字段）。</summary>
    public struct PresentationLogicSample
    {
        public int MonthIndex;
        public int WeekIndex;
        public double TotalPopulation;
        public double FoodReserve;
        public double PrimarySettlementPopulation;
        public float EntityPosX;
        public float EntityPosY;
        public float EntityPosZ;
        public float ResourceVisualAmount;
        public float CameraFocusX;
        public float CameraFocusY;
        public float CameraFocusZ;
        public float CameraDistance;
    }

    /// <summary>插值后的 WorldView 表现快照（只读，不持有 WorldState 引用）。</summary>
    public readonly struct WorldViewSnapshot
    {
        public double Alpha { get; }
        public double SmoothedAlpha { get; }
        public double Population { get; }
        public double FoodReserve { get; }
        public float EntityPosX { get; }
        public float EntityPosY { get; }
        public float EntityPosZ { get; }
        public float ResourceVisualAmount { get; }
        public float CameraFocusX { get; }
        public float CameraFocusY { get; }
        public float CameraFocusZ { get; }
        public float CameraDistance { get; }

        public WorldViewSnapshot(
            double alpha,
            double smoothedAlpha,
            double population,
            double foodReserve,
            float entityPosX,
            float entityPosY,
            float entityPosZ,
            float resourceVisualAmount,
            float cameraFocusX,
            float cameraFocusY,
            float cameraFocusZ,
            float cameraDistance)
        {
            Alpha = alpha;
            SmoothedAlpha = smoothedAlpha;
            Population = population;
            FoodReserve = foodReserve;
            EntityPosX = entityPosX;
            EntityPosY = entityPosY;
            EntityPosZ = entityPosZ;
            ResourceVisualAmount = resourceVisualAmount;
            CameraFocusX = cameraFocusX;
            CameraFocusY = cameraFocusY;
            CameraFocusZ = cameraFocusZ;
            CameraDistance = cameraDistance;
        }
    }

    /// <summary>
    /// P3 WorldView：在周/月边界采样逻辑态，边界间平滑插值；永不写回 WorldState。
    /// </summary>
    public sealed class PresentationWorldView
    {
        private PresentationLogicSample _from;
        private PresentationLogicSample _to;
        private int _trackedMonth = int.MinValue;
        private int _trackedWeek = int.MinValue;
        private bool _initialized;
        private WorldViewSnapshot _latest;

        public WorldViewSnapshot Latest => _latest;
        public bool IsInitialized => _initialized;

        public void Reset()
        {
            _initialized = false;
            _trackedMonth = int.MinValue;
            _trackedWeek = int.MinValue;
            _latest = default;
        }

        /// <summary>从 WorldState 只读采样；优先 Civ/Eco v2，缺省回退 Slice 桩。不修改任何逻辑字段。</summary>
        public static PresentationLogicSample Capture(WorldState world, float sphereRadius = 5f)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            double population = 0.0;
            double primaryPop = 0.0;
            int primaryId = 0;
            bool usedCiv = false;
            if (world.Civilization?.Settlements != null && world.Civilization.Settlements.Count > 0)
            {
                usedCiv = true;
                primaryId = world.Civilization.Settlements[0].stableId;
                primaryPop = world.Civilization.Settlements[0].population;
                for (int i = 0; i < world.Civilization.Settlements.Count; i++)
                {
                    var s = world.Civilization.Settlements[i];
                    population += s.population;
                    if (s.stableId < primaryId)
                    {
                        primaryId = s.stableId;
                        primaryPop = s.population;
                    }
                }
            }
            else if (world.Settlements != null && world.Settlements.Count > 0)
            {
                primaryId = world.Settlements[0].stableId;
                primaryPop = world.Settlements[0].population;
                for (int i = 0; i < world.Settlements.Count; i++)
                {
                    population += world.Settlements[i].population;
                    if (world.Settlements[i].stableId < primaryId)
                    {
                        primaryId = world.Settlements[i].stableId;
                        primaryPop = world.Settlements[i].population;
                    }
                }
            }

            double food = 0.0;
            if (world.Civilization?.Economies != null && world.Civilization.Economies.Count > 0)
            {
                for (int i = 0; i < world.Civilization.Economies.Count; i++)
                    food += world.Civilization.Economies[i].food;
            }
            else if (world.Ecology?.Resources != null && world.Ecology.Resources.Count > 0)
            {
                for (int i = 0; i < world.Ecology.Resources.Count; i++)
                    food += world.Ecology.Resources[i].currentAmount;
            }
            else if (world.Resources != null)
            {
                for (int i = 0; i < world.Resources.Count; i++)
                {
                    if (string.Equals(world.Resources[i].name, "Food", StringComparison.Ordinal))
                        food += world.Resources[i].currentAmount;
                }
            }

            // 个体/聚落表现位置：球面附近高度随人口变化（纯表现公式）
            float height = sphereRadius + 0.35f + (float)Math.Min(1.5, primaryPop / 5000.0);
            float yaw = (float)((primaryId % 36) * (Math.PI / 18.0));
            float x = (float)(Math.Sin(yaw) * 0.15);
            float z = (float)(Math.Cos(yaw) * 0.15);
            float resourceVisual = (float)Math.Max(0.15, Math.Min(2.5, food / 100.0));
            if (usedCiv) resourceVisual = (float)Math.Max(0.15, Math.Min(2.5, food / 40.0));

            return new PresentationLogicSample
            {
                MonthIndex = world.Time.monthIndex,
                WeekIndex = world.Time.weekIndex,
                TotalPopulation = population,
                FoodReserve = food,
                PrimarySettlementPopulation = primaryPop,
                EntityPosX = x,
                EntityPosY = height,
                EntityPosZ = z,
                ResourceVisualAmount = resourceVisual,
                CameraFocusX = x,
                CameraFocusY = height * 0.2f,
                CameraFocusZ = z,
                CameraDistance = 12f + (float)Math.Min(6.0, population / 10000.0)
            };
        }

        /// <summary>
        /// 同步边界采样并计算插值快照。仅读取 world；保证不写回。
        /// </summary>
        public WorldViewSnapshot Sync(WorldState world, float sphereRadius = 5f)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var sample = Capture(world, sphereRadius);

            if (!_initialized)
            {
                _from = sample;
                _to = sample;
                _trackedMonth = sample.MonthIndex;
                _trackedWeek = sample.WeekIndex;
                _initialized = true;
            }
            else if (sample.MonthIndex != _trackedMonth || sample.WeekIndex != _trackedWeek)
            {
                // 跨越月/周边界：从旧目标滑向新逻辑态
                _from = _to;
                _to = sample;
                _trackedMonth = sample.MonthIndex;
                _trackedWeek = sample.WeekIndex;
            }
            else
            {
                // 边界内逻辑态不变；刷新 to 以防同边界内外部校正（仍只读）
                _to = sample;
            }

            double alpha = PresentationInterpolator.DisplayAlpha(world.Time);
            double s = PresentationInterpolator.SmoothStep(alpha);
            _latest = Evaluate(_from, _to, alpha, s);
            return _latest;
        }

        public static WorldViewSnapshot Evaluate(
            in PresentationLogicSample from,
            in PresentationLogicSample to,
            double alpha,
            double smoothedAlpha)
        {
            float t = (float)smoothedAlpha;
            return new WorldViewSnapshot(
                alpha,
                smoothedAlpha,
                PresentationInterpolator.Lerp(from.TotalPopulation, to.TotalPopulation, smoothedAlpha),
                PresentationInterpolator.Lerp(from.FoodReserve, to.FoodReserve, smoothedAlpha),
                PresentationInterpolator.LerpF(from.EntityPosX, to.EntityPosX, t),
                PresentationInterpolator.LerpF(from.EntityPosY, to.EntityPosY, t),
                PresentationInterpolator.LerpF(from.EntityPosZ, to.EntityPosZ, t),
                PresentationInterpolator.LerpF(from.ResourceVisualAmount, to.ResourceVisualAmount, t),
                PresentationInterpolator.LerpF(from.CameraFocusX, to.CameraFocusX, t),
                PresentationInterpolator.LerpF(from.CameraFocusY, to.CameraFocusY, t),
                PresentationInterpolator.LerpF(from.CameraFocusZ, to.CameraFocusZ, t),
                PresentationInterpolator.LerpF(from.CameraDistance, to.CameraDistance, t));
        }
    }
}
