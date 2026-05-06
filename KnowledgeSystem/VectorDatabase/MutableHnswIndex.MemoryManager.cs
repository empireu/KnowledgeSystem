// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem.VectorDatabase;

public sealed partial class MutableHnswIndex
{
    /// <summary>
    ///     Represents a large, fixed-size portion of memory meant to allocate blocks of <paramref name="allocationLength"/> elements. 
    /// </summary>
    internal sealed class AllocationPage<T>(int pageIndex, int allocationLength, int pageCapacity) where T : struct
    {
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
        public Memory<T> AllocateBlock()
        {
            if (Count == pageCapacity)
            {
                throw new InvalidOperationException("Cannot allocate: page full");
            }

            var result = Data.AsMemory(Count * allocationLength, allocationLength);
            Count++;

            return result;
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
        public Memory<T> AllocateBlock()
        {
            // Look for space in the existing pages:
            for (var pageIndex = 0; pageIndex < Pages.Length; pageIndex++)
            {
                var page = Pages[pageIndex];

                if (!page.IsFull)
                {
                    return page.AllocateBlock();
                }
            }
            
            // Allocate new page:
            var newPage = new AllocationPage<T>(Pages.Length, allocationLength, pageCapacity);
            Array.Resize(ref Pages, Pages.Length + 1);
            Pages[^1] = newPage;

            return newPage.AllocateBlock();
        }
    }
}