namespace WorldSim.Simulation.WorldMap
{
    using System;
    using WorldSim.Simulation.Core.WorldGeography;

    public static class EquirectangularProjection
    {
        public static int Width(MapLodLevel lod) => lod == MapLodLevel.High ? 720 : lod == MapLodLevel.Mid ? 360 : 180;
        public static int Height(MapLodLevel lod) => Width(lod) / 2;

        public static int ToTileId(GeoCoordinate coordinate, MapLodLevel lod)
        {
            int width = Width(lod);
            int height = Height(lod);
            double lon = GeoCoordinate.NormalizeLongitude(coordinate.Longitude);
            int x = Math.Min(width - 1, Math.Max(0, (int)Math.Floor((lon + 180.0) / 360.0 * width)));
            int y = Math.Min(height - 1, Math.Max(0, (int)Math.Floor((90.0 - coordinate.Latitude) / 180.0 * height)));
            return EncodeTileId(lod, x, y);
        }

        public static GeoCoordinate ToCoordinate(int tileId)
        {
            DecodeTileId(tileId, out MapLodLevel lod, out int x, out int y);
            int width = Width(lod);
            int height = Height(lod);
            double lon = -180.0 + (x + 0.5) * 360.0 / width;
            double lat = 90.0 - (y + 0.5) * 180.0 / height;
            return new GeoCoordinate(lat, lon);
        }

        public static int EncodeTileId(MapLodLevel lod, int x, int y)
        {
            int width = Width(lod);
            int height = Height(lod);
            x = ((x % width) + width) % width;
            if (y < 0 || y >= height) throw new ArgumentOutOfRangeException(nameof(y));
            return ((int)lod + 1) * 1000000 + y * width + x;
        }

        public static void DecodeTileId(int tileId, out MapLodLevel lod, out int x, out int y)
        {
            int band = tileId / 1000000;
            if (band < 1 || band > 3) throw new ArgumentOutOfRangeException(nameof(tileId));
            lod = (MapLodLevel)(band - 1);
            int local = tileId % 1000000;
            int width = Width(lod);
            x = local % width;
            y = local / width;
            if (y >= Height(lod)) throw new ArgumentOutOfRangeException(nameof(tileId));
        }

        public static double WrappedLongitudeDistance(double a, double b)
        {
            double delta = Math.Abs(GeoCoordinate.NormalizeLongitude(a) - GeoCoordinate.NormalizeLongitude(b));
            return Math.Min(delta, 360.0 - delta);
        }
    }
}
