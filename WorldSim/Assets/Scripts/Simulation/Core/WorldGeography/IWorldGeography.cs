namespace WorldSim.Simulation.Core.WorldGeography
{
    /// <summary>S3 只读地理约束；实现留在 WorldMap，文明不得改写地貌。</summary>
    public interface IWorldGeography
    {
        bool HasWaterNearby(int worldTileId);
        double GetSlope(int worldTileId);
    }
}
