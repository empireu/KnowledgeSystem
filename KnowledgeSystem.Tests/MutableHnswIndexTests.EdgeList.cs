using KnowledgeSystem.Vector.Hnsw;

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
    private static MutableHnswIndex.EdgeList CreateEdgeList(int capacity)
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(capacity + 1, 4);
        return new MutableHnswIndex.EdgeList(capacity, allocator.Allocate());
    }

    [Fact]
    public void EdgeList_Add_IncrementsCount()
    {
        var list = CreateEdgeList(4);
        Assert.Equal(0, list.Count);

        list.Add(10);
        Assert.Equal(1, list.Count);

        list.Add(20);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Add_StoresValues()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        Assert.Equal(10, list[0]);
        Assert.Equal(20, list[1]);
        Assert.Equal(30, list[2]);
    }

    [Fact]
    public void EdgeList_Add_FullThrows()
    {
        var list = CreateEdgeList(2);
        list.Add(1);
        list.Add(2);

        Assert.Throws<InvalidOperationException>(() => list.Add(3));
    }

    [Fact]
    public void EdgeList_Remove_ReturnsTrueWhenFound()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        Assert.True(list.Remove(20));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Remove_ReturnsFalseWhenNotFound()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);

        Assert.False(list.Remove(99));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Remove_SwapsLastElement()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.Remove(10);

        Assert.Equal(30, list[0]);
        Assert.Equal(20, list[1]);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void EdgeList_Remove_LastElement_JustDecrements()
    {
        var list = CreateEdgeList(4);
        list.Add(10);
        list.Add(20);

        list.Remove(20);

        Assert.Equal(1, list.Count);
        Assert.Equal(10, list[0]);
    }

    [Fact]
    public void EdgeList_Remove_EmptyList_ReturnsFalse()
    {
        var list = CreateEdgeList(4);
        Assert.False(list.Remove(1));
    }
}