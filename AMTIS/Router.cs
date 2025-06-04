using System;
using System.Collections.Generic;
using System.Linq;
using DeliveryOptimizer; // Update the namespace reference

namespace AMTIS
{
    public class Router
    {
        private readonly Dictionary<string, Dictionary<string, int>> _adj;
        private readonly Dictionary<string, int> _pickupCache;
        private readonly List<Package> _packages;
        private readonly Dictionary<string, HashSet<string>> _cityConnections;

        public Router()
        {
            _adj = new Dictionary<string, Dictionary<string, int>>();
            _pickupCache = new Dictionary<string, int>();
            _packages = new List<Package>();
            _cityConnections = new Dictionary<string, HashSet<string>>();
        }

        private void PreComputeCityConnections()
        {
            var cities = _pickupCache.Keys.Union(_packages.Select(p => p.   To ?? string.Empty)).Distinct();
            foreach (var city in cities)
            {
                _cityConnections[city] = new HashSet<string>();
            }

            foreach (var city in cities)
            {
                var connectedCities = _adj.TryGetValue(city, out var neighbors) ? neighbors.Keys : Enumerable.Empty<string>();
                _cityConnections[city].UnionWith(connectedCities);
            }
        }
    }
}

namespace AMTIS.Models
{
    public class Package
    {
        public required string To { get; set; } // Ensure 'required' keyword is used correctly
        public required string From { get; set; }
        public int Weight { get; set; }
    }
}