using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace DeliveryOptimizer
{
    public record Package
    {
        public string Name { get; }
        public string From { get; }
        public string To { get; }
        public int Weight { get; }

        public Package(string name, string from, string to, int weight)
        {
            Name = !string.IsNullOrWhiteSpace(name) ? name 
                : throw new ArgumentException("Name cannot be empty", nameof(name));
            From = !string.IsNullOrWhiteSpace(from) ? from 
                : throw new ArgumentException("From city cannot be empty", nameof(from));
            To = !string.IsNullOrWhiteSpace(to) ? to 
                : throw new ArgumentException("To city cannot be empty", nameof(to));
            Weight = weight > 0 ? weight 
                : throw new ArgumentException("Weight must be positive", nameof(weight));
        }
    }

    public class Bus
    {
        public int Capacity { get; }
        public int CurrentLoad { get; private set; }
        private readonly HashSet<Package> _load = new();
        private readonly Dictionary<string, List<Package>> _destinationCache = new(); // Cache packages by destination

        public Bus(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be positive", nameof(capacity));
            Capacity = capacity;
        }

        public IReadOnlyCollection<Package> Load => _load;

        public bool TryLoad(Package pkg)
        {
            if (CurrentLoad + pkg.Weight > Capacity)
                return false;
                
            _load.Add(pkg);
            CurrentLoad += pkg.Weight;
            
            // Cache package by destination
            if (!_destinationCache.TryGetValue(pkg.To, out var packages))
            {
                packages = new List<Package>();
                _destinationCache[pkg.To] = packages;
            }
            packages.Add(pkg);
            
            return true;
        }

        public List<Package> UnloadAt(string city)
        {
            // Use cached packages by destination instead of filtering
            if (!_destinationCache.TryGetValue(city, out var toUnload))
                return new List<Package>();

            CurrentLoad -= toUnload.Sum(p => p.Weight);
            foreach (var pkg in toUnload)
                _load.Remove(pkg);
                
            _destinationCache.Remove(city);
            return toUnload;
        }
    }

    public class Graph
    {
        private readonly Dictionary<string, Dictionary<string, int>> _adj = new();
        private readonly Dictionary<string, Dictionary<string, int>> _distCache = new();
        private readonly HashSet<string> _cities = new(); // Cache for quick city lookup

        public void AddEdge(string a, string b, int distance)
        {
            if (distance < 0) throw new ArgumentException("Distance cannot be negative", nameof(distance));
            
            if (!_adj.TryGetValue(a, out var aEdges))
            {
                aEdges = new Dictionary<string, int>();
                _adj[a] = aEdges;
                _cities.Add(a);
            }
            if (!_adj.TryGetValue(b, out var bEdges))
            {
                bEdges = new Dictionary<string, int>();
                _adj[b] = bEdges;
                _cities.Add(b);
            }

            aEdges[b] = Math.Min(distance, aEdges.GetValueOrDefault(b, int.MaxValue));
            bEdges[a] = Math.Min(distance, bEdges.GetValueOrDefault(a, int.MaxValue));
            _distCache.Clear();
        }

        public int ShortestDistance(string start, string end)
        {
            ArgumentNullException.ThrowIfNull(start);
            ArgumentNullException.ThrowIfNull(end);
            
            if (start == end) return 0;
            if (_distCache.TryGetValue(start, out var dist) && dist.TryGetValue(end, out var cached))
                return cached;

            var distances = ComputeAllDistances(start);
            _distCache[start] = distances;
            return distances.GetValueOrDefault(end, int.MaxValue);
        }

        private Dictionary<string, int> ComputeAllDistances(string start)
        {
            var dist = new Dictionary<string, int>(_cities.Count); // Pre-size dictionary
            var pq = new PriorityQueue<string, int>(_cities.Count); // Pre-size queue

            foreach (var city in _cities) // Use cached cities
                dist[city] = int.MaxValue;

            dist[start] = 0;
            pq.Enqueue(start, 0);

            while (pq.Count > 0)
            {
                if (!pq.TryDequeue(out var city, out var d) || city == null) 
                    continue;

                if (d > dist[city]) // Direct access is safe here
                    continue;

                var adjacentCities = _adj[city];
                foreach (var (nbr, w) in adjacentCities)
                {
                    var nd = d + w;
                    if (nd < dist[nbr])
                    {
                        dist[nbr] = nd;
                        pq.Enqueue(nbr, nd);
                    }
                }
            }
            return dist;
        }

        public Dictionary<string, int> GetConnectedCities(string city)
        {
            return _adj.GetValueOrDefault(city, new Dictionary<string, int>());
        }
    }

    public record RouteStep(string From, string To, List<Package> PickUp, List<Package> DropOff);

    public class Router
    {
        private readonly Graph _graph;
        private readonly IReadOnlyList<Package> _packages;
        private readonly string _depot;
        private readonly Dictionary<string, List<Package>> _pickupCache;
        private readonly Dictionary<(string, string), double> _routeCache = new(); // Cache route costs
        private readonly Dictionary<string, HashSet<string>> _cityConnections = new();

        public Router(Graph graph, IEnumerable<Package> packages, string depot)
        {
            _graph = graph;
            _packages = packages.ToList();
            _depot = depot;
            
            // Pre-compute package grouping
            _pickupCache = new Dictionary<string, List<Package>>(_packages.Count / 2);
            foreach (var package in _packages)
            {
                if (!_pickupCache.TryGetValue(package.From, out var cityPackages))
                {
                    cityPackages = new List<Package>();
                    _pickupCache[package.From] = cityPackages;
                }
                cityPackages.Add(package);
            }

            // Pre-compute city connections for faster path finding
            PreComputeCityConnections();
        }

        private void PreComputeCityConnections()
        {
            var cities = _pickupCache.Keys.Union(_packages.Select(p => p.To)).Distinct();
            foreach (var city in cities)
            {
                _cityConnections[city] = new HashSet<string>();
            }

            foreach (var city in cities)
            {
                var connectedCities = _graph.GetConnectedCities(city);
                _cityConnections[city].UnionWith(connectedCities.Keys);
            }
        }

        public List<RouteStep> GenerateShortestRoute(int capacity)
            => GenerateRoute(capacity, (dist, _) => dist);

        public List<RouteStep> GenerateFuelEfficientRoute(int capacity)
            => GenerateRoute(capacity, (dist, load) => dist * (1.0 + load / (double)capacity));

        private List<RouteStep> GenerateRoute(int capacity, Func<int, int, double> costFunc)
        {
            var bus = new Bus(capacity);
            var remainingPickups = new Dictionary<string, Queue<Package>>(_pickupCache.Count);
            var routeSteps = new List<RouteStep>(_pickupCache.Count * 2); // Pre-allocate with estimated size
            
            // Initialize remaining pickups with pre-sized queues
            foreach (var kvp in _pickupCache)
            {
                remainingPickups[kvp.Key] = new Queue<Package>(kvp.Value.Count);
                foreach (var pkg in kvp.Value.OrderBy(p => p.Weight)) // Load lighter packages first
                {
                    remainingPickups[kvp.Key].Enqueue(pkg);
                }
            }

            var current = _depot;
            var visitedCities = new HashSet<string>(_pickupCache.Count) { _depot };

            while (true)
            {
                ProcessCurrentCity(bus, remainingPickups, routeSteps, current);
                visitedCities.Add(current);

                if (IsRouteComplete(bus, remainingPickups))
                {
                    if (current != _depot)
                        routeSteps.Add(new RouteStep(current, _depot, new(), new()));
                    break;
                }

                current = GetNextCityOptimized(bus, remainingPickups, current, costFunc, visitedCities);
                if (!visitedCities.Contains(current))
                {
                    routeSteps.Add(new RouteStep(current, current, new(), new()));
                }
            }

            return routeSteps;
        }

        private static void ProcessCurrentCity(Bus bus, Dictionary<string, Queue<Package>> remainingPickups, 
            List<RouteStep> route, string current)
        {
            var drop = bus.UnloadAt(current);
            var pick = new List<Package>();

            if (remainingPickups.TryGetValue(current, out var packages))
            {
                while (packages.Count > 0 && bus.TryLoad(packages.Peek()))
                {
                    var pkg = packages.Dequeue();
                    pick.Add(pkg);
                }
                if (packages.Count == 0)
                    remainingPickups.Remove(current);
            }

            route.Add(new RouteStep(current, current, pick, drop));
        }

        private static bool IsRouteComplete(Bus bus, Dictionary<string, Queue<Package>> remainingPickups)
            => !bus.Load.Any() && !remainingPickups.Any();

        private string GetNextCityOptimized(Bus bus, Dictionary<string, Queue<Package>> remainingPickups, 
            string current, Func<int, int, double> costFunc, HashSet<string> visitedCities)
        {
            var targets = bus.Load.Any()
                ? bus.Load.Select(p => p.To).Distinct().ToArray() // Materialize once
                : remainingPickups.Keys.ToArray();

            if (targets.Length == 0)
                return _depot;

            string bestCity = targets[0];
            double bestCost = double.MaxValue;

            foreach (var city in targets)
            {
                if (visitedCities.Contains(city))
                    continue;

                var cacheKey = (current, city);
                if (!_routeCache.TryGetValue(cacheKey, out var cost))
                {
                    cost = costFunc(_graph.ShortestDistance(current, city), bus.CurrentLoad);
                    _routeCache[cacheKey] = cost;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestCity = city;
                }
            }

            return bestCity;
        }
    }

    internal class Program
    {
        private static void Main()
        {
            AnsiConsole.Write(new FigletText("Delivery Optimizer").Centered().Color(Color.Green));

            var depot = AnsiConsole.Prompt(
                new TextPrompt<string>("[yellow]Enter depot city[/]:")
                    .Validate(s => string.IsNullOrWhiteSpace(s) ? ValidationResult.Error("Depot cannot be empty") : ValidationResult.Success()));

            var graph = new Graph();
            AnsiConsole.MarkupLine("[underline]Enter roads (CityA CityB Distance), type 'END' to finish:[/]");
            while (true)
            {
                var input = AnsiConsole.Prompt(new TextPrompt<string>("[green]Road:[/]").AllowEmpty());
                if (input.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var parts = input.Split(' ');
                if (parts.Length == 3 && int.TryParse(parts[2], out var dist))
                    graph.AddEdge(parts[0], parts[1], dist);
                else
                    AnsiConsole.MarkupLine("[red]Invalid format![/] Use CityA CityB Distance.");
            }

            var packages = new List<Package>();
            AnsiConsole.MarkupLine("[underline]Enter packages (Name From To Weight), type 'END' to finish:[/]");
            while (true)
            {
                var input = AnsiConsole.Prompt(new TextPrompt<string>("[green]Package:[/]").AllowEmpty());
                if (input.Equals("END", StringComparison.OrdinalIgnoreCase)) break;
                var parts = input.Split(' ');
                if (parts.Length == 4 && int.TryParse(parts[3], out var w))
                    packages.Add(new Package(parts[0], parts[1], parts[2], w));
                else
                    AnsiConsole.MarkupLine("[red]Invalid format![/] Use Name From To Weight.");
            }

            var capacity = AnsiConsole.Prompt(
                new TextPrompt<int>("[yellow]Enter van capacity[/]:")
                    .Validate(n => n <= 0 ? ValidationResult.Error("Capacity must be positive") : ValidationResult.Success()));

            var router = new Router(graph, packages, depot);
            var shortestRoute = router.GenerateShortestRoute(capacity);
            var fuelRoute = router.GenerateFuelEfficientRoute(capacity);

            void RenderTable(string title, List<RouteStep> route)
            {
                var table = new Table().Border(TableBorder.Rounded).Centered();
                table.AddColumn("Step"); table.AddColumn("From"); table.AddColumn("To"); table.AddColumn("Pick"); table.AddColumn("Drop");
                int idx = 1;
                foreach (var s in route)
                {
                    table.AddRow(
                        idx++.ToString(), s.From, s.To,
                        s.PickUp.Any() ? string.Join(", ", s.PickUp.Select(p => p.Name)) : "-",
                        s.DropOff.Any() ? string.Join(", ", s.DropOff.Select(p => p.Name)) : "-");
                }
                AnsiConsole.Write(new Panel(table).Header($"[bold green]{title}[/]").Expand());
            }

            AnsiConsole.WriteLine();
            RenderTable("Shortest Distance Route", shortestRoute);
            AnsiConsole.WriteLine();
            RenderTable("Fuel Efficient Route", fuelRoute);
        }
    }
}
