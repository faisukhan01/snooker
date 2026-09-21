// SnookerKit — fixed-capacity ring buffers (CONTRACTS §2). Add() reuses the backing array, so the sliding
// windows in <see cref="PerformanceManager"/> never allocate per frame (§0.6 — no per-frame heap allocations).
using System;

namespace SnookerKit
{
    /// <summary>Generic fixed-capacity FIFO ring buffer. Once full, Add overwrites the oldest entry. Index 0 is
    /// the oldest retained item, Count-1 the newest. Add/Count/indexer are allocation-free; only ToArray copies.</summary>
    public class RingBuffer<T>
    {
        private readonly T[] _items;
        private int _head; // index of the oldest retained item
        private int _count;

        /// <summary>Creates a buffer holding exactly <paramref name="capacity"/> items.</summary>
        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity", "RingBuffer capacity must be positive");
            _items = new T[capacity];
        }

        /// <summary>Number of items currently retained (≤ <see cref="Capacity"/>).</summary>
        public int Count { get { return _count; } }

        /// <summary>Fixed maximum number of items.</summary>
        public int Capacity { get { return _items.Length; } }

        /// <summary>Appends an item; when the buffer is full the oldest item is evicted. Zero allocations.</summary>
        public void Add(T item)
        {
            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = item;
                _count++;
                return;
            }
            _items[_head] = item;
            _head = (_head + 1) % _items.Length;
        }

        /// <summary>Item at <paramref name="index"/>; 0 = oldest … Count-1 = newest.</summary>
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException("index");
                return _items[(_head + index) % _items.Length];
            }
        }

        /// <summary>Snapshot oldest → newest. Allocates a new array — poll sites should read the indexer instead.</summary>
        public T[] ToArray()
        {
            T[] copy = new T[_count];
            for (int i = 0; i < _count; i++) copy[i] = _items[(_head + i) % _items.Length];
            return copy;
        }

        /// <summary>Drops every item and releases references held by the backing array.</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
            Array.Clear(_items, 0, _items.Length);
        }
    }

    /// <summary>Float-specialized ring buffer with streaming statistics: Average is O(1) (running sum in double
    /// precision) and Max is O(n) over at most Capacity floats. Used by <see cref="PerformanceManager"/>.</summary>
    public class RingBufferFloat
    {
        private readonly float[] _items;
        private int _head; // index of the oldest retained sample
        private int _count;
        private double _sum;

        /// <summary>Creates a buffer holding exactly <paramref name="capacity"/> samples.</summary>
        public RingBufferFloat(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity", "RingBufferFloat capacity must be positive");
            _items = new float[capacity];
        }

        /// <summary>Number of samples currently retained (≤ <see cref="Capacity"/>).</summary>
        public int Count { get { return _count; } }

        /// <summary>Fixed maximum number of samples.</summary>
        public int Capacity { get { return _items.Length; } }

        /// <summary>Appends a sample; when full the oldest sample is evicted (and subtracted from the sum). Zero allocations.</summary>
        public void Add(float value)
        {
            if (_count < _items.Length)
            {
                _items[(_head + _count) % _items.Length] = value;
                _count++;
                _sum += value;
                return;
            }
            _sum -= _items[_head];
            _items[_head] = value;
            _sum += value;
            _head = (_head + 1) % _items.Length;
        }

        /// <summary>Sample at <paramref name="index"/>; 0 = oldest … Count-1 = newest.</summary>
        public float this[int index]
        {
            get
            {
                if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException("index");
                return _items[(_head + index) % _items.Length];
            }
        }

        /// <summary>Mean of the retained samples; 0 when empty (guarded division).</summary>
        public float Average()
        {
            return _count == 0 ? 0f : (float)(_sum / _count);
        }

        /// <summary>Largest retained sample; 0 when empty.</summary>
        public float Max()
        {
            if (_count == 0) return 0f;
            float max = _items[0]; // slot 0 is always a retained sample (head starts at 0 and only moves when full)
            for (int i = 1; i < _count; i++)
            {
                float v = _items[(_head + i) % _items.Length];
                if (v > max) max = v;
            }
            return max;
        }

        /// <summary>Drops every sample and resets the running sum.</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
            _sum = 0.0;
            Array.Clear(_items, 0, _items.Length);
        }
    }
}
