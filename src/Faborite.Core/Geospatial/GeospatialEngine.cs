using Microsoft.Extensions.Logging;

namespace Faborite.Core.Geospatial;

/// <summary>
/// Geospatial functions for location-based analysis.
/// Issue #54
/// </summary>
public class GeospatialEngine
{
    private readonly ILogger<GeospatialEngine> _logger;
    private const double EarthRadiusKm = 6371.0;

    public GeospatialEngine(ILogger<GeospatialEngine> logger)
    {
        _logger = logger;
    }

    public double CalculateDistance(GeoPoint point1, GeoPoint point2, DistanceUnit unit = DistanceUnit.Kilometers)
    {
        // Haversine formula
        var lat1Rad = DegreesToRadians(point1.Latitude);
        var lat2Rad = DegreesToRadians(point2.Latitude);
        var deltaLat = DegreesToRadians(point2.Latitude - point1.Latitude);
        var deltaLon = DegreesToRadians(point2.Longitude - point1.Longitude);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distanceKm = EarthRadiusKm * c;

        return unit switch
        {
            DistanceUnit.Kilometers => distanceKm,
            DistanceUnit.Miles => distanceKm * 0.621371,
            DistanceUnit.Meters => distanceKm * 1000,
            _ => distanceKm
        };
    }

    public bool IsWithinRadius(GeoPoint center, GeoPoint point, double radius, DistanceUnit unit = DistanceUnit.Kilometers)
    {
        return CalculateDistance(center, point, unit) <= radius;
    }

    public List<GeoPoint> FindPointsWithinRadius(GeoPoint center, List<GeoPoint> points, double radius, DistanceUnit unit = DistanceUnit.Kilometers)
    {
        return points.Where(p => IsWithinRadius(center, p, radius, unit)).ToList();
    }

    public BoundingBox CalculateBoundingBox(List<GeoPoint> points)
    {
        var minLat = points.Min(p => p.Latitude);
        var maxLat = points.Max(p => p.Latitude);
        var minLon = points.Min(p => p.Longitude);
        var maxLon = points.Max(p => p.Longitude);

        return new BoundingBox(
            SouthWest: new GeoPoint(minLat, minLon),
            NorthEast: new GeoPoint(maxLat, maxLon)
        );
    }

    public GeoPoint CalculateCentroid(List<GeoPoint> points)
    {
        var avgLat = points.Average(p => p.Latitude);
        var avgLon = points.Average(p => p.Longitude);
        return new GeoPoint(avgLat, avgLon);
    }

    public bool IsPointInPolygon(GeoPoint point, List<GeoPoint> polygon)
    {
        // Ray casting algorithm
        var inside = false;
        var j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            if ((polygon[i].Longitude > point.Longitude) != (polygon[j].Longitude > point.Longitude) &&
                point.Latitude < (polygon[j].Latitude - polygon[i].Latitude) * (point.Longitude - polygon[i].Longitude) /
                (polygon[j].Longitude - polygon[i].Longitude) + polygon[i].Latitude)
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    private double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

public enum DistanceUnit { Kilometers, Miles, Meters }

public record GeoPoint(double Latitude, double Longitude);
public record BoundingBox(GeoPoint SouthWest, GeoPoint NorthEast);
