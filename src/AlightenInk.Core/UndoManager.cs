using System;
using System.Collections.Generic;

namespace AlightenInk.Core
{
    /// <summary>Something that can hold drawn items (one overlay window per monitor implements this).</summary>
    public interface IItemHost<T>
    {
        void AddItem(T item);
        void RemoveItem(T item);
    }

    public readonly struct Change<T>
    {
        public Change(IItemHost<T> host, T item, bool added)
        {
            Host = host;
            Item = item;
            Added = added;
        }

        public IItemHost<T> Host { get; }
        public T Item { get; }

        /// <summary>True = the item was added by this change, false = it was removed.</summary>
        public bool Added { get; }
    }

    /// <summary>
    /// Global undo/redo across all hosts. Each step is a list of add/remove changes, so one gesture
    /// (an eraser drag, "clear all" across monitors) is one step. <see cref="Forget"/> drops every
    /// reference to an item that is gone for good (faded ink) so undo/redo can never bring it back.
    /// </summary>
    public sealed class UndoManager<T> where T : class
    {
        private readonly List<List<Change<T>>> _undo = new List<List<Change<T>>>();
        private readonly List<List<Change<T>>> _redo = new List<List<Change<T>>>();
        private List<Change<T>>? _group;
        private int _groupDepth;

        public UndoManager(int maxSteps = 500) => MaxSteps = maxSteps;

        public int MaxSteps { get; }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public bool InGroup => _groupDepth > 0;

        public event EventHandler? Changed;

        /// <summary>Starts a step that collects every change until the returned scope is disposed. Nests.</summary>
        public IDisposable BeginGroup()
        {
            if (_groupDepth++ == 0) _group = new List<Change<T>>();
            return new GroupScope(this);
        }

        public void EndGroup()
        {
            if (_groupDepth == 0) return;
            if (--_groupDepth > 0) return;
            var g = _group;
            _group = null;
            if (g != null && g.Count > 0) Push(g);
        }

        public void RecordAdd(IItemHost<T> host, T item) => Record(new Change<T>(host, item, true));
        public void RecordRemove(IItemHost<T> host, T item) => Record(new Change<T>(host, item, false));

        private void Record(Change<T> change)
        {
            if (_group != null) _group.Add(change);
            else Push(new List<Change<T>> { change });
        }

        private void Push(List<Change<T>> step)
        {
            _undo.Add(step);
            if (_undo.Count > MaxSteps) _undo.RemoveAt(0);
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var step = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            for (int i = step.Count - 1; i >= 0; i--)
            {
                var c = step[i];
                if (c.Added) c.Host.RemoveItem(c.Item);
                else c.Host.AddItem(c.Item);
            }
            _redo.Add(step);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var step = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            foreach (var c in step)
            {
                if (c.Added) c.Host.AddItem(c.Item);
                else c.Host.RemoveItem(c.Item);
            }
            _undo.Add(step);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Removes every change that mentions <paramref name="item"/>; empty steps disappear.</summary>
        public void Forget(T item)
        {
            bool any = Strip(_undo, item) | Strip(_redo, item);
            if (_group != null) _group.RemoveAll(c => ReferenceEquals(c.Item, item));
            if (any) Changed?.Invoke(this, EventArgs.Empty);
        }

        private static bool Strip(List<List<Change<T>>> stack, T item)
        {
            bool changed = false;
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i].RemoveAll(c => ReferenceEquals(c.Item, item)) > 0)
                {
                    changed = true;
                    if (stack[i].Count == 0) stack.RemoveAt(i);
                }
            }
            return changed;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private sealed class GroupScope : IDisposable
        {
            private UndoManager<T>? _owner;
            public GroupScope(UndoManager<T> owner) => _owner = owner;

            public void Dispose()
            {
                _owner?.EndGroup();
                _owner = null;
            }
        }
    }
}
