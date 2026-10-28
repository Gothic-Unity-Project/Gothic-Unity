using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gothic.Core.Domain.Culling
{
    /// <summary>
    /// Objects waiting to be built, handed out nearest to a reference point (the camera) first. What the player sees
    /// first is built first, and objects still missing appear far away.
    /// Re-sorted only when objects came in or the reference point moved, at most every MinSortInterval seconds:
    /// a sort of a few thousand entries takes a few tenths of a millisecond.
    /// With a focus (SetFocus), only entries within its radius are handed out, nearest to its center first.
    /// </summary>
    public class NearestFirstQueue<T> where T : class
    {
        private const float _minSortInterval = 0.25f; // s
        private const float _minSortMove = 4f; // m

        // Sorted farthest first, so the nearest entry is taken from the end without moving the others.
        private static readonly Comparison<(Vector3 Position, float SqrDistance, T Item)> _farthestFirst =
            (a, b) => b.SqrDistance.CompareTo(a.SqrDistance);

        private readonly List<(Vector3 Position, float SqrDistance, T Item)> _entries = new();
        private bool _isSorted = true;
        private float _lastSortTime = float.NegativeInfinity;
        private Vector3 _lastSortReference;
        private Vector3 _focusCenter;
        private float _focusRadius = float.PositiveInfinity;

        public int Count => _entries.Count;
        public bool IsFocused => !float.IsPositiveInfinity(_focusRadius);

        public void Enqueue(T item, Vector3 position)
        {
            _entries.Add((position, 0f, item));
            _isSorted = false;
        }

        public void Clear()
        {
            _entries.Clear();
            _isSorted = true;
        }

        /// <summary>
        /// Until ClearFocus(), only entries within radius of center are handed out, nearest to center first.
        /// </summary>
        public void SetFocus(Vector3 center, float radius)
        {
            _focusCenter = center;
            _focusRadius = radius;
            _isSorted = false;
        }

        public void ClearFocus()
        {
            _focusRadius = float.PositiveInfinity;
            _isSorted = false;
        }

        /// <summary>
        /// Sorts again if objects came in or the reference moved. While focused, the focus center is the reference
        /// and the order is kept up to date every call (every frame counts while the player waits for it).
        /// </summary>
        public void UpdateOrder(Vector3 reference)
        {
            if (IsFocused)
                reference = _focusCenter;

            var hasMoved = (reference - _lastSortReference).sqrMagnitude >= _minSortMove * _minSortMove;
            if (_isSorted && !hasMoved)
                return;
            if (!IsFocused && Time.unscaledTime - _lastSortTime < _minSortInterval)
                return;

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                entry.SqrDistance = (entry.Position - reference).sqrMagnitude;
                _entries[i] = entry;
            }

            _entries.Sort(_farthestFirst);
            _isSorted = true;
            _lastSortTime = Time.unscaledTime;
            _lastSortReference = reference;
        }

        /// <summary>
        /// The nearest entry as of the last UpdateOrder(). Entries added since then come first.
        /// </summary>
        public bool TryDequeue(out T item)
        {
            var last = _entries.Count - 1;
            if (last < 0 || (IsFocused && (_entries[last].Position - _focusCenter).sqrMagnitude >
                    _focusRadius * _focusRadius))
            {
                item = null;
                return false;
            }

            item = _entries[last].Item;
            _entries.RemoveAt(last);
            return true;
        }

        public bool HasEntryWithin(Vector3 center, float radius)
        {
            var sqrRadius = radius * radius;
            foreach (var entry in _entries)
            {
                if ((entry.Position - center).sqrMagnitude <= sqrRadius)
                    return true;
            }

            return false;
        }
    }
}
