using System.Collections.Generic;
using AlightenInk.Core;
using Xunit;

namespace AlightenInk.Tests
{
    public class UndoManagerTests
    {
        private sealed class Host : IItemHost<object>
        {
            public List<object> Items { get; } = new List<object>();
            public void AddItem(object item) { if (!Items.Contains(item)) Items.Add(item); }
            public void RemoveItem(object item) => Items.Remove(item);
        }

        private static object Draw(UndoManager<object> u, Host h, string name)
        {
            var item = new object();
            h.AddItem(item);
            u.RecordAdd(h, item);
            return item;
        }

        [Fact]
        public void UndoRedo_SingleAdds()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            var a = Draw(u, h, "a");
            var b = Draw(u, h, "b");

            Assert.True(u.Undo());
            Assert.Equal(new[] { a }, h.Items);
            Assert.True(u.Undo());
            Assert.Empty(h.Items);
            Assert.False(u.Undo());

            Assert.True(u.Redo());
            Assert.True(u.Redo());
            Assert.Equal(new[] { a, b }, h.Items);
            Assert.False(u.Redo());
        }

        [Fact]
        public void NewAction_ClearsRedo()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            Draw(u, h, "a");
            u.Undo();
            Assert.True(u.CanRedo);
            Draw(u, h, "b");
            Assert.False(u.CanRedo);
        }

        [Fact]
        public void EraserDrag_IsOneStep()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            var a = Draw(u, h, "a");
            var b = Draw(u, h, "b");
            var c = Draw(u, h, "c");

            using (u.BeginGroup())
            {
                foreach (var x in new[] { a, c })
                {
                    h.RemoveItem(x);
                    u.RecordRemove(h, x);
                }
            }

            Assert.Equal(4, u.UndoCount);
            Assert.Equal(new[] { b }, h.Items);
            u.Undo();
            Assert.Equal(3, h.Items.Count);
            u.Redo();
            Assert.Equal(new[] { b }, h.Items);
        }

        [Fact]
        public void EmptyGroup_RecordsNothing()
        {
            var u = new UndoManager<object>();
            using (u.BeginGroup()) { }
            Assert.False(u.CanUndo);
        }

        [Fact]
        public void NestedGroups_FlattenIntoOneStep()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            using (u.BeginGroup())
            {
                Draw(u, h, "a");
                using (u.BeginGroup())
                {
                    Draw(u, h, "b");
                }
                Assert.True(u.InGroup);
                Draw(u, h, "c");
            }
            Assert.Equal(1, u.UndoCount);
            u.Undo();
            Assert.Empty(h.Items);
        }

        [Fact]
        public void ClearAcrossHosts_IsOneStep()
        {
            var u = new UndoManager<object>();
            var h1 = new Host();
            var h2 = new Host();
            var a = Draw(u, h1, "a");
            var b = Draw(u, h2, "b");
            using (u.BeginGroup())
            {
                h1.RemoveItem(a); u.RecordRemove(h1, a);
                h2.RemoveItem(b); u.RecordRemove(h2, b);
            }
            u.Undo();
            Assert.Equal(new[] { a }, h1.Items);
            Assert.Equal(new[] { b }, h2.Items);
        }

        [Fact]
        public void Forget_FadedItemNeverComesBack()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            var a = Draw(u, h, "a");
            var faded = Draw(u, h, "faded");
            // Fade finished: removed for good.
            h.RemoveItem(faded);
            u.Forget(faded);

            Assert.Equal(1, u.UndoCount);
            u.Undo(); // undoes "a" only
            Assert.Empty(h.Items);
            u.Redo();
            Assert.Equal(new[] { a }, h.Items);
            Assert.DoesNotContain(faded, h.Items);
        }

        [Fact]
        public void Forget_InsideEraseGroup_KeepsTheRest()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            var a = Draw(u, h, "a");
            var b = Draw(u, h, "b");
            using (u.BeginGroup())
            {
                h.RemoveItem(a); u.RecordRemove(h, a);
                h.RemoveItem(b); u.RecordRemove(h, b);
            }
            // b was a fading item that expired while erased; undoing the erase must not restore it.
            u.Forget(b);
            u.Undo();
            Assert.Equal(new[] { a }, h.Items);
        }

        [Fact]
        public void Forget_ItemInRedoStack()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            var a = Draw(u, h, "a");
            u.Undo();
            u.Forget(a);
            Assert.False(u.CanRedo);
            Assert.False(u.Redo());
            Assert.Empty(h.Items);
        }

        [Fact]
        public void Forget_DuringOpenGroup()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            object x;
            using (u.BeginGroup())
            {
                x = Draw(u, h, "x");
                u.Forget(x);
            }
            Assert.False(u.CanUndo);
        }

        [Fact]
        public void MaxSteps_DropsOldest()
        {
            var u = new UndoManager<object>(maxSteps: 3);
            var h = new Host();
            for (int i = 0; i < 5; i++) Draw(u, h, "i" + i);
            Assert.Equal(3, u.UndoCount);
            while (u.Undo()) { }
            Assert.Equal(2, h.Items.Count);
        }

        [Fact]
        public void Changed_IsRaised()
        {
            var u = new UndoManager<object>();
            var h = new Host();
            int n = 0;
            u.Changed += (_, __) => n++;
            Draw(u, h, "a");
            u.Undo();
            u.Redo();
            Assert.Equal(3, n);
        }
    }
}
