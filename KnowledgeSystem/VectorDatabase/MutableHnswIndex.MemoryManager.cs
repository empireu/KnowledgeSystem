// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Represents a large, fixed-size portion of memory meant to allocate blocks of <paramref name="allocationLength"/> elements. 
    /// </summary>
    internal sealed class AllocationPage<T>(ArenaAllocator<T> allocator, int pageIndex, int allocationLength, int pageCapacity) where T : struct
    {
        internal readonly ArenaAllocator<T> Allocator = allocator;
        
        /// <summary>
        ///     The index of the page in the wider allocator.
        /// </summary>
        internal readonly int PageIndex = pageIndex;
        
        internal readonly T[] Data = new T[allocationLength * pageCapacity];

        /// <summary>
        ///     The number of vectors currently allocated.
        /// </summary>
        internal int Count;

        /// <summary>
        ///     If true, the page is full and cannot allocate any more blocks.
        /// </summary>
        public bool IsFull => Count == pageCapacity;
        
        /// <summary>
        ///     Allocates a block out of the page.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Thrown if the page is full.</exception>
        public Allocation<T> Allocate()
        {
            if (Count == pageCapacity)
            {
                throw new InvalidOperationException("Cannot allocate: page full");
            }

            var index = Count;
            var result = Data.AsMemory(Count * allocationLength, allocationLength);
            Count++;

            return new Allocation<T>(this, result, index);
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
        ///     Allocates a new block.
        ///     Will either allocate it from an existing page (if there is a page that isn't full), or will allocate a new page.
        /// </summary>
        /// <returns></returns>
        public Allocation<T> AllocateBlock()
        {
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
        public readonly  int IndexInPage = indexInPage;
    }
}