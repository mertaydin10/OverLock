using Overlock.Core;
using Xunit;

namespace Overlock.Core.Tests;

public class SequenceDocumentTests
{
    [Fact]
    public void Sequential_inserts_build_text()
    {
        var doc = new SequenceDocument("a");
        doc.Insert(0, "Hel");
        doc.Insert(3, "lo");

        Assert.Equal("Hello", doc.Text);
        Assert.Equal(5, doc.Length);
    }

    [Fact]
    public void Concurrent_inserts_at_same_index_converge()
    {
        var a = new SequenceDocument("alice");
        var b = new SequenceDocument("bob");

        var opA = a.Insert(0, "A").Single();
        var opB = b.Insert(0, "B").Single();

        a.Apply(opB);
        b.Apply(opA);

        Assert.Equal(a.Text, b.Text);
        Assert.Equal(2, a.Length);
    }

    [Fact]
    public void Concurrent_inserts_in_the_middle_converge()
    {
        var seed = new SequenceDocument("seed");
        seed.Insert(0, "ac");

        var a = SequenceDocument.FromSnapshot("alice", seed.Export());
        var b = SequenceDocument.FromSnapshot("bob", seed.Export());

        var opA = a.Insert(1, "X").Single();
        var opB = b.Insert(1, "Y").Single();

        a.Apply(opB);
        b.Apply(opA);

        Assert.Equal(a.Text, b.Text);
        Assert.Contains("X", a.Text);
        Assert.Contains("Y", a.Text);
        Assert.StartsWith("a", a.Text);
        Assert.EndsWith("c", a.Text);
    }

    [Fact]
    public void Delete_tombstones_do_not_forget_identity()
    {
        var a = new SequenceDocument("a");
        a.Insert(0, "Hi");
        a.Delete(0, 1);

        Assert.Equal("i", a.Text);

        var b = SequenceDocument.FromSnapshot("b", a.Export());
        b.Insert(0, "X");

        foreach (var op in b.Log)
        {
            a.Apply(op);
        }

        Assert.Equal("Xi", a.Text);
        Assert.Equal(a.Text, b.Text);
    }

    [Fact]
    public void Out_of_order_ops_buffer_until_causal_deps_arrive()
    {
        var a = new SequenceDocument("a");
        var ops = a.Insert(0, "ab").ToList();

        var b = new SequenceDocument("b");
        b.Apply(ops[1]);
        Assert.Equal(1, b.PendingCount);
        Assert.Equal("", b.Text);

        b.Apply(ops[0]);
        Assert.Equal(0, b.PendingCount);
        Assert.Equal("ab", b.Text);
    }

    [Fact]
    public void Emoji_is_one_atom()
    {
        var doc = new SequenceDocument("a");
        doc.Insert(0, "A👨‍👩‍👧B");

        Assert.Equal(3, doc.Length);
        Assert.Equal("A👨‍👩‍👧B", doc.Text);
    }

    [Fact]
    public void Snapshot_round_trip_preserves_text()
    {
        var a = new SequenceDocument("a");
        a.Insert(0, "Overlock");
        a.Delete(1, 2);

        var b = SequenceDocument.FromSnapshot("b", a.Export());
        Assert.Equal(a.Text, b.Text);
    }

    [Fact]
    public void Three_replicas_converge_after_full_mesh_merge()
    {
        var a = new SequenceDocument("a");
        var b = new SequenceDocument("b");
        var c = new SequenceDocument("c");

        a.Insert(0, "Hello");
        b.Insert(0, " ");
        c.Insert(0, "World");

        a.Delete(1, 1);

        Mesh(a, b, c);

        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Text, c.Text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(2026)]
    public void Random_edits_converge(int seed)
    {
        var rng = new Random(seed);
        var replicas = new[]
        {
            new SequenceDocument("a"),
            new SequenceDocument("b"),
            new SequenceDocument("c")
        };

        for (var step = 0; step < 40; step++)
        {
            var doc = replicas[rng.Next(replicas.Length)];
            if (doc.Length == 0 || rng.NextDouble() < 0.7)
            {
                var index = rng.Next(doc.Length + 1);
                var ch = ((char)('a' + rng.Next(26))).ToString();
                doc.Insert(index, ch);
            }
            else
            {
                var index = rng.Next(doc.Length);
                doc.Delete(index);
            }
        }

        Mesh(replicas[0], replicas[1], replicas[2]);

        Assert.Equal(replicas[0].Text, replicas[1].Text);
        Assert.Equal(replicas[0].Text, replicas[2].Text);
        Assert.Equal(0, replicas[0].PendingCount);
        Assert.Equal(0, replicas[1].PendingCount);
        Assert.Equal(0, replicas[2].PendingCount);
    }

    private static void Mesh(params SequenceDocument[] docs)
    {
        for (var pass = 0; pass < docs.Length; pass++)
        {
            foreach (var src in docs)
            {
                foreach (var dest in docs)
                {
                    if (!ReferenceEquals(src, dest))
                    {
                        dest.MergeFrom(src);
                    }
                }
            }
        }
    }
}
