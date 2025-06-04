public class Graph
{
    internal readonly Dictionary<string, Dictionary<string, int>> _adjacencyMap = new();
    private readonly Dictionary<string, Dictionary<string, int>> _distanceCache = new();
    private readonly HashSet<string> _cities = new();

    public Dictionary<string, int> GetConnectedCities(string city)
    {
        return _adjacencyMap.GetValueOrDefault(city, new Dictionary<string, int>());
    }

    public Dictionary<string, Dictionary<string, int>> AdjacencyMap => _adjacencyMap;

    public void AddEdge(string cityA, string cityB, int distance)
    {
        if (distance < 0) throw new ArgumentException("Distance cannot be negative", nameof(distance));

        if (!_adjacencyMap.TryGetValue(cityA, out var cityAEdges))
        {
            cityAEdges = new Dictionary<string, int>();
            _adjacencyMap[cityA] = cityAEdges;
            _cities.Add(cityA);
        }
        if (!_adjacencyMap.TryGetValue(cityB, out var cityBEdges))
        {
            cityBEdges = new Dictionary<string, int>();
            _adjacencyMap[cityB] = cityBEdges;
            _cities.Add(cityB);
        }

        cityAEdges[cityB] = Math.Min(distance, cityAEdges.GetValueOrDefault(cityB, int.MaxValue));
        cityBEdges[cityA] = Math.Min(distance, cityBEdges.GetValueOrDefault(cityA, int.MaxValue));
        _distanceCache.Clear();
    }

    public int ShortestDistance(string startCity, string endCity)
    {
        ArgumentNullException.ThrowIfNull(startCity);
        ArgumentNullException.ThrowIfNull(endCity);

        if (startCity == endCity) return 0;
        if (_distanceCache.TryGetValue(startCity, out var distances) && distances.TryGetValue(endCity, out var cachedDistance))
            return cachedDistance;

        var allDistances = ComputeAllDistances(startCity);
        _distanceCache[startCity] = allDistances;
        return allDistances.GetValueOrDefault(endCity, int.MaxValue);
    }

    private Dictionary<string, int> ComputeAllDistances(string startCity)
    {
        var cityDistances = new Dictionary<string, int>(_cities.Count);
        var priorityQueue = new PriorityQueue<string, int>(_cities.Count);

        foreach (var city in _cities)
            cityDistances[city] = int.MaxValue;

        cityDistances[startCity] = 0;
        priorityQueue.Enqueue(startCity, 0);

        while (priorityQueue.Count > 0)
        {
            if (!priorityQueue.TryDequeue(out var currentCity, out var currentDistance) || currentCity == null)
                continue;

            if (currentDistance > cityDistances[currentCity])
                continue;

            var neighborCities = _adjacencyMap[currentCity];
            foreach (var (neighborCity, edgeWeight) in neighborCities)
            {
                var newDistance = currentDistance + edgeWeight;
                if (newDistance < cityDistances[neighborCity])
                {
                    cityDistances[neighborCity] = newDistance;
                    priorityQueue.Enqueue(neighborCity, newDistance);
                }
            }
        }
        return cityDistances;
    }
}