using System.Collections.Generic;
using UnityEngine;

namespace GUZ.Core.Models.WayNet
{
    public class DijkstraWaypoint
    {
        // Used as index to find other data like position, underwater and probably isFree
        public string Name;

        // Initialized to a large number to represent infinity at the start of the algorithm.
        public double SummedDistance = 99999;
        // This is used for the priority queue to determine which node to visit next (smaller distance = higher priority)

        public Dictionary<string, float> DistanceToNeighbors = new(); // Stores the distances to neighboring nodes
        public List<string> Neighbors = new(); // Stores the neighboring nodes
        public Vector3 Position = new(); // Stores the position of the node

        public DijkstraWaypoint(string name)
        {
            Name = name;
        }
    }
}
