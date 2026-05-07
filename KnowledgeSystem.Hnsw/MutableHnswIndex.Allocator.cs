// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.Hnsw;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Represents a large, fixed-size portion of memory meant to allocate (at most) <paramref name="pageCapacity"/> blocks of <paramref name="allocationLength"/> elements. 
    /// </summary>
    internal sealed class AllocationPage<T>(ArenaAllocator<T> allocator, int pageIndex, int allocationLength, int pageCapacity) where T : struct
    {
        internal readonly ArenaAllocator<T> Allocator = allocator;
        
        /// <summary>
        ///     The index of the page in the wider allocator.
        /// </summary>
        internal readonly int PageIndex = pageIndex;

        /// <summary>
        ///     The number of slots currently allocated (including freed slots that haven't been reused yet).
        /// </summary>
        internal int SlotCount;

        /// <summary>
        ///     Stack of freed indices within this page that can be reused.
        /// </summary>
        internal readonly Stack<int> FreeIndices = new();

        /// <summary>
        ///     Set of indices currently in the free list, used to detect double-free errors.
        /// </summary>
        internal readonly HashSet<int> FreedIndexSet = [];

        /// <summary>
        ///     If true, the page has no free slots and cannot allocate any more blocks.
        /// </summary>
        public bool IsFull => SlotCount == pageCapacity && FreeIndices.Count == 0;

        /// <summary>
        ///     If true, the page has at least one freed slot that can be reused.
        /// </summary>
        public bool HasFreeSlots => FreeIndices.Count > 0;
        
        /// <summary>
        ///     The actual backing storage.
        /// </summary>
        private readonly T[] _data = new T[allocationLength * pageCapacity];
        
        /// <summary>
        ///     Allocates a block out of the page.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Thrown if the page is full.</exception>
        public Allocation<T> Allocate()
        {
            if (IsFull)
            {
                throw new InvalidOperationException("Cannot allocate: page full");
            }

            if (FreeIndices.TryPop(out var index))
            {
                if(!FreedIndexSet.Remove(index))
                {
                    throw new InvalidOperationException("Expected to  find freed index");
                }
                
                var result = _data.AsMemory(index * allocationLength, allocationLength);
                return new Allocation<T>(this, result, index);
            }

            var newIndex = SlotCount;
            var newResult = _data.AsMemory(SlotCount * allocationLength, allocationLength);
            SlotCount++;

            return new Allocation<T>(this, newResult, newIndex);
        }

        /// <summary>
        ///     Deallocates a block from the page by its index, making it available for reuse.
        /// </summary>
        public void Deallocate(int indexInPage)
        {
            if (indexInPage < 0 || indexInPage >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(indexInPage), $"Slot {indexInPage} was never allocated; SlotCount is {SlotCount}.");
            }

            if (!FreedIndexSet.Add(indexInPage))
            {
                throw new InvalidOperationException($"Slot {indexInPage} is already freed; double-free detected.");
            }

            FreeIndices.Push(indexInPage);
        }
    }
    
    /// <summary>
    ///     Allocator for vectors and graph data.
    /// </summary>
    /// <param name="allocationLength">The fixed-size length of the allocated block.</param>
    /// <param name="pageCapacity">The capacity of each contiguous segment allocated from the runtime.</param>
    internal sealed  class ArenaAllocator<T>(int allocationLength, int pageCapacity) where T : struct
    {
        internal AllocationPage<T>[] Pages = [];

        /// <summary>
        ///     Set of page indices that have at least one freed slot available for reuse.
        /// </summary>
        internal readonly HashSet<int> PagesWithReusedSlots = [];
        
        /// <summary>
        ///     Allocates a new block.
        ///     Will first try pages with freed slots, then any non-full page, then allocate a new page.
        ///     <b></b>
        /// </summary>
        public Allocation<T> Allocate()
        {
            // Prefer pages with freed slots for reuse:
            if (PagesWithReusedSlots.Count > 0)
            {
                var pageIndex = PagesWithReusedSlots.First();
                var page = Pages[pageIndex];
                var allocation = page.Allocate();

                if (!page.HasFreeSlots)
                {
                    PagesWithReusedSlots.Remove(pageIndex);
                }

                return allocation;
            }

            // Look for space in the existing pages:
            for (var pageIndex = 0; pageIndex < Pages.Length; pageIndex++)
            {
                var page = Pages[pageIndex];

                if (!page.IsFull)
                {
                    return page.Allocate();
                }
            }
            
            // Allocate new page:
            var newPage = new AllocationPage<T>(this, Pages.Length, allocationLength, pageCapacity);
            Array.Resize(ref Pages, Pages.Length + 1);
            Pages[^1] = newPage;

            return newPage.Allocate();
        }

        /// <summary>
        ///     Deallocates a previously allocated block, making its slot available for reuse.
        /// </summary>
        public void Deallocate(in Allocation<T> allocation)
        {
            var page = allocation.Page;
            page.Deallocate(allocation.IndexInPage);
            PagesWithReusedSlots.Add(page.PageIndex);
        }
    }

    /// <summary>
    ///     Represents an allocated block from a page.
    /// </summary>
    /// <param name="page">The backing page.</param>
    /// <param name="block">The section of memory allocated from the page.</param>
    internal readonly struct Allocation<T>(AllocationPage<T> page, Memory<T> block, int indexInPage) where T : struct
    {
        /// <summary>
        ///     The backing page for this allocation.
        /// </summary>
        public readonly AllocationPage<T> Page = page;
            
        /// <summary>
        ///     The portion of memory allocated from the page.
        /// </summary>
        public readonly Memory<T> Block = block;
            
        /// <summary>
        ///     The allocation index, local to the source page.
        /// </summary>
        public readonly int IndexInPage = indexInPage;

        public void Deallocate()
        {
            Page.Allocator.Deallocate(in this);
        }
    }

    /// <summary>
    ///     Multiple arena allocators for a small number of consecutively-sized arrays.
    /// </summary>
    /// <param name="basePageCapacity">The page size for the 1-length allocators. The page size will be halved each time the allocation length doubles. Should be a power-of-two.</param>
    /// <param name="minPageSize">The minimum page size.</param>
    /// <typeparam name="T"></typeparam>
    internal sealed class BucketArenaAllocator<T>(int basePageCapacity, int minPageSize) where T : struct
    {
        internal readonly Dictionary<int, ArenaAllocator<T>> Allocators = new();

        public Allocation<T> Allocate(int allocationLength)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(allocationLength, 1);

            if (Allocators.TryGetValue(allocationLength, out var allocator))
            {
                return allocator.Allocate();
            }
            
            var value = allocationLength;
            var pageCapacity = basePageCapacity;
            while (value > 1 && pageCapacity > minPageSize)
            {
                value >>= 1;
                pageCapacity >>= 1;
            }
            
            allocator = new ArenaAllocator<T>(allocationLength, Math.Max(pageCapacity, minPageSize));
            Allocators.Add(allocationLength, allocator);
            
            return allocator.Allocate();
        }
    }
}