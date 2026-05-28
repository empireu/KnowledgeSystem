using KnowledgeSystem.Vector.Hnsw;

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
    [Fact]
    public void AllocationPage_AllocateBlock_ReturnsCorrectSize()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 10, pageCapacity: 5);
        var block = page.Allocate().Block;

        Assert.Equal(10, block.Length);
        Assert.Equal(1, page.SlotCount);
    }

    [Fact]
    public void AllocationPage_AllocateMultipleBlocks_ReturnsDistinctOrderedMemory()
    {
        unsafe
        {
            var page = new MutableHnswIndex.AllocationPage<float>(null!, pageIndex: 0, allocationLength: 4, pageCapacity: 3);
            var block1 = page.Allocate().Block;
            var block2 = page.Allocate().Block;

            using var h1 = block1.Pin();
            using var h2 = block2.Pin();

            Assert.False(h1.Pointer == h2.Pointer);
            Assert.True((float*)h2.Pointer == (float*)h1.Pointer + 4);
            Assert.Equal(2, page.SlotCount);
        }
    }

    [Fact]
    public void AllocationPage_IsFull_ReturnsTrueWhenFull()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 2, pageCapacity: 2);

        Assert.False(page.IsFull);
        page.Allocate();
        Assert.False(page.IsFull);
        page.Allocate();
        Assert.True(page.IsFull);
    }

    [Fact]
    public void AllocationPage_AllocateWhenFull_ThrowsInvalidOperationException()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 1);
        page.Allocate();

        Assert.Throws<InvalidOperationException>(() => page.Allocate());
    }

    [Fact]
    public void ArenaAllocator_AllocateBlock_UsesFirstPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<double>(allocationLength: 3, pageCapacity: 10);
        var block = allocator.Allocate().Block;

        Assert.Equal(3, block.Length);
        Assert.Single(allocator.Pages);
        Assert.Equal(1, allocator.Pages[0].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_AllocateMultipleBlocks_ReusesPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<float>(allocationLength: 2, pageCapacity: 5);

        for (var i = 0; i < 5; i++)
        {
            allocator.Allocate();
        }

        Assert.Single(allocator.Pages);
        Assert.Equal(5, allocator.Pages[0].SlotCount);
        Assert.True(allocator.Pages[0].IsFull);
    }

    [Fact]
    public void ArenaAllocator_PageFull_CreatesNewPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 2);

        allocator.Allocate();
        allocator.Allocate();
        Assert.Single(allocator.Pages);

        allocator.Allocate();
        Assert.Equal(2, allocator.Pages.Length);
        Assert.Equal(1, allocator.Pages[1].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_MultiplePages_UsesNonFullPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<long>(allocationLength: 1, pageCapacity: 2);

        allocator.Allocate();
        allocator.Allocate();
        allocator.Allocate();
        Assert.Equal(2, allocator.Pages.Length);

        allocator.Allocate();
        Assert.Equal(2, allocator.Pages.Length);
        Assert.Equal(2, allocator.Pages[1].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_LargeAllocation_CreatesMultiplePages()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 10, pageCapacity: 5);
        const int totalAllocations = 17;

        for (var i = 0; i < totalAllocations; i++)
        {
            allocator.Allocate();
        }

        Assert.Equal(4, allocator.Pages.Length);
        Assert.True(allocator.Pages[0].IsFull);
        Assert.True(allocator.Pages[1].IsFull);
        Assert.True(allocator.Pages[2].IsFull);
        Assert.Equal(2, allocator.Pages[3].SlotCount);
    }

    [Fact]
    public void ArenaAllocator_BlockData_IsPersisted()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<float>(allocationLength: 4, pageCapacity: 3);

        var block1 = allocator.Allocate().Block;
        block1.Span[0] = 1.0f;
        block1.Span[1] = 2.0f;
        block1.Span[2] = 3.0f;
        block1.Span[3] = 4.0f;

        var block2 = allocator.Allocate().Block;
        block2.Span[0] = 5.0f;
        block2.Span[1] = 6.0f;
        block2.Span[2] = 7.0f;
        block2.Span[3] = 8.0f;

        Assert.Equal(1.0f, block1.Span[0]);
        Assert.Equal(2.0f, block1.Span[1]);
        Assert.Equal(3.0f, block1.Span[2]);
        Assert.Equal(4.0f, block1.Span[3]);

        Assert.Equal(5.0f, block2.Span[0]);
        Assert.Equal(6.0f, block2.Span[1]);
        Assert.Equal(7.0f, block2.Span[2]);
        Assert.Equal(8.0f, block2.Span[3]);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_SlotIsReused()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 3);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        var c = allocator.Allocate();
        // ReSharper restore UnusedVariable
        Assert.True(allocator.Pages[0].IsFull);

        allocator.Deallocate(in b);
        Assert.False(allocator.Pages[0].IsFull);
        Assert.True(allocator.Pages[0].HasFreeSlots);
        Assert.Contains(0, allocator.PagesWithReusedSlots);

        var d = allocator.Allocate();
        Assert.Equal(1, d.IndexInPage);
        Assert.True(allocator.Pages[0].IsFull);
        Assert.DoesNotContain(0, allocator.PagesWithReusedSlots);
        Assert.Single(allocator.Pages);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_PrefersFreeSlotsOverNewPage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 2);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        // ReSharper restore UnusedVariable
        Assert.Single(allocator.Pages);

        allocator.Deallocate(in a);
        var c = allocator.Allocate();
        Assert.Equal(0, c.IndexInPage);
        Assert.Single(allocator.Pages);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_TracksPagesWithFreeSlots()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 2);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        var c = allocator.Allocate();
        // ReSharper restore UnusedVariable

        allocator.Deallocate(in a);
        Assert.Contains(0, allocator.PagesWithReusedSlots);

        allocator.Deallocate(in c);
        Assert.Contains(0, allocator.PagesWithReusedSlots);
        Assert.Contains(1, allocator.PagesWithReusedSlots);

        allocator.Allocate();
        Assert.DoesNotContain(0, allocator.PagesWithReusedSlots);
        allocator.Allocate();
        Assert.DoesNotContain(1, allocator.PagesWithReusedSlots);
    }

    [Fact]
    public void ArenaAllocator_Deallocate_MultipleFreesOnSamePage()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 3);

        // ReSharper disable UnusedVariable
        var a = allocator.Allocate();
        var b = allocator.Allocate();
        var c = allocator.Allocate();
        // ReSharper restore UnusedVariable

        allocator.Deallocate(in a);
        allocator.Deallocate(in c);

        Assert.Equal(2, allocator.Pages[0].FreeIndices.Count);

        var d = allocator.Allocate();
        Assert.Equal(2, d.IndexInPage);

        var e = allocator.Allocate();
        Assert.Equal(0, e.IndexInPage);
    }

    [Fact]
    public void AllocationPage_Deallocate_MakesFullPageNotFull()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 1);
        page.Allocate();
        Assert.True(page.IsFull);

        page.Deallocate(0);
        Assert.False(page.IsFull);
        Assert.True(page.HasFreeSlots);
    }

    [Fact]
    public void AllocationPage_Deallocate_DoubleFreeThrows()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 3);
        page.Allocate();
        page.Allocate();

        page.Deallocate(0);
        Assert.Throws<InvalidOperationException>(() => page.Deallocate(0));
    }

    [Fact]
    public void ArenaAllocator_Deallocate_DoubleFreeThrows()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 1, pageCapacity: 3);
        var a = allocator.Allocate();

        allocator.Deallocate(in a);
        Assert.Throws<InvalidOperationException>(() => allocator.Deallocate(in a));
    }

    [Fact]
    public void ArenaAllocator_Deallocate_ReusePreservesDataIntegrity()
    {
        var allocator = new MutableHnswIndex.ArenaAllocator<int>(allocationLength: 2, pageCapacity: 3);

        var a = allocator.Allocate();
        a.Block.Span[0] = 10;
        a.Block.Span[1] = 20;

        var b = allocator.Allocate();
        b.Block.Span[0] = 30;
        b.Block.Span[1] = 40;

        var c = allocator.Allocate();
        c.Block.Span[0] = 50;
        c.Block.Span[1] = 60;

        allocator.Deallocate(in b);

        var d = allocator.Allocate();
        Assert.Equal(1, d.IndexInPage);
        d.Block.Span[0] = 99;
        d.Block.Span[1] = 88;

        Assert.Equal(10, a.Block.Span[0]);
        Assert.Equal(20, a.Block.Span[1]);
        Assert.Equal(50, c.Block.Span[0]);
        Assert.Equal(60, c.Block.Span[1]);

        Assert.Equal(99, d.Block.Span[0]);
        Assert.Equal(88, d.Block.Span[1]);
    }

    [Fact]
    public void AllocationPage_Deallocate_UnallocatedIndexThrows()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 5);
        page.Allocate();
        page.Allocate();

        Assert.Throws<ArgumentOutOfRangeException>(() => page.Deallocate(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Deallocate(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Deallocate(-1));
    }

    [Fact]
    public void AllocationPage_Allocate_PrefersFreedSlotsOverBump()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 5);
        page.Allocate();
        page.Allocate();
        page.Allocate();

        page.Deallocate(1);

        var allocation = page.Allocate();
        Assert.Equal(1, allocation.IndexInPage);
        Assert.Equal(3, page.SlotCount);
    }

    [Fact]
    public void AllocationPage_AllSlotsFreedThenReallocated()
    {
        var page = new MutableHnswIndex.AllocationPage<int>(null!, pageIndex: 0, allocationLength: 1, pageCapacity: 3);
        page.Allocate();
        page.Allocate();
        page.Allocate();

        page.Deallocate(0);
        page.Deallocate(1);
        page.Deallocate(2);

        Assert.Equal(3, page.FreeIndices.Count);
        Assert.False(page.IsFull);

        page.Allocate();
        page.Allocate();
        page.Allocate();

        Assert.True(page.IsFull);
        Assert.Empty(page.FreeIndices);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_CreatesBucketAndReturnsCorrectBlockSize()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);
        var block = bucket.Allocate(5).Block;

        Assert.Equal(5, block.Length);
        Assert.Single(bucket.Allocators);
        Assert.True(bucket.Allocators.ContainsKey(5));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_SameBucket_ReusesAllocator()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<float>(basePageCapacity: 64, minPageSize: 4);

        bucket.Allocate(3);
        bucket.Allocate(3);
        bucket.Allocate(3);

        Assert.Single(bucket.Allocators);
        Assert.Equal(3, bucket.Allocators[3].Pages[0].SlotCount);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_DifferentLengths_CreatesSeparateBuckets()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<double>(basePageCapacity: 64, minPageSize: 4);

        bucket.Allocate(1);
        bucket.Allocate(2);
        bucket.Allocate(3);

        Assert.Equal(3, bucket.Allocators.Count);
        Assert.True(bucket.Allocators.ContainsKey(1));
        Assert.True(bucket.Allocators.ContainsKey(2));
        Assert.True(bucket.Allocators.ContainsKey(3));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_PageCapacityScalesWithAllocationLength()
    {
        const int basePageCapacity = 64;
        const int minPageSize = 4;
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity, minPageSize);

        Assert.Equal(64, FillAndGetSlots(bucket, 1));
        Assert.Equal(32, FillAndGetSlots(bucket, 2));
        Assert.Equal(16, FillAndGetSlots(bucket, 4));
        Assert.Equal(8, FillAndGetSlots(bucket, 8));
        Assert.Equal(minPageSize, FillAndGetSlots(bucket, 16));
        return;

        static int FillAndGetSlots(MutableHnswIndex.BucketArenaAllocator<int> b, int length)
        {
            b.Allocate(length);
            var page = b.Allocators[length].Pages[0];
            while (!page.IsFull)
            {
                b.Allocate(length);
            }

            return page.SlotCount;
        }
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_MinPageSizeIsRespected()
    {
        const int minPageSize = 8;
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 16, minPageSize);

        bucket.Allocate(32);
        var page = bucket.Allocators[32].Pages[0];
        while (!page.IsFull)
        {
            bucket.Allocate(32);
        }

        Assert.Equal(minPageSize, page.SlotCount);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_ZeroLength_ThrowsArgumentOutOfRangeException()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => bucket.Allocate(0));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => bucket.Allocate(-1));
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_DataIsPersistedAcrossBlocks()
    {
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity: 64, minPageSize: 4);

        var block1 = bucket.Allocate(3).Block;
        block1.Span[0] = 10;
        block1.Span[1] = 20;
        block1.Span[2] = 30;

        var block2 = bucket.Allocate(5).Block;
        block2.Span[0] = 100;
        block2.Span[1] = 200;
        block2.Span[2] = 300;
        block2.Span[3] = 400;
        block2.Span[4] = 500;

        Assert.Equal(10, block1.Span[0]);
        Assert.Equal(20, block1.Span[1]);
        Assert.Equal(30, block1.Span[2]);

        Assert.Equal(100, block2.Span[0]);
        Assert.Equal(200, block2.Span[1]);
        Assert.Equal(300, block2.Span[2]);
        Assert.Equal(400, block2.Span[3]);
        Assert.Equal(500, block2.Span[4]);
    }

    [Fact]
    public void BucketArenaAllocator_Allocate_ManyAllocations_CreatesMultiplePages()
    {
        const int basePageCapacity = 4;
        const int minPageSize = 2;
        var bucket = new MutableHnswIndex.BucketArenaAllocator<int>(basePageCapacity, minPageSize);

        for (var i = 0; i < 5; i++)
        {
            bucket.Allocate(1);
        }

        Assert.Equal(2, bucket.Allocators[1].Pages.Length);
        Assert.True(bucket.Allocators[1].Pages[0].IsFull);
        Assert.Equal(1, bucket.Allocators[1].Pages[1].SlotCount);
    }
}