using System.Diagnostics;
using KnowledgeSystem.EmdParser.ExtendedMarkdown;
using KnowledgeSystem.EmdParser.MarkdownTree;
using KnowledgeSystem.Hnsw;
using KnowledgeSystem.Retrieval.Engine;
using Microsoft.Extensions.Logging;

// ReSharper disable ForCanBeConvertedToForeach

namespace KnowledgeSystem;

public sealed class Foo
{
    private readonly ILogger<Foo> _logger;
    private readonly RagEngine _engine;
    private readonly string _query;

    public readonly HashSet<int> VisitedIndices = [];
    public readonly Dictionary<EmdDocument, ReferencedDocument> ReferencedDocuments = [];
    
    public Foo(ILogger<Foo> logger, RagEngine engine, string query)
    {
        _logger = logger;
        _engine = engine;
        _query = query;
    }

    /// <summary>
    ///     Fetches more results for the query and updates the bounding tree of the results.
    /// </summary>
    /// <param name="count">The number of top results to fetch.</param>
    /// <returns></returns>
    public async Task<int> Step(int count)
    {
        var vectorResults = await _engine.SearchAsync(
            _query, 
            count,
            excludedIndices: VisitedIndices
        );

        // Create the referenced documents and fetch their logical parents:
        for (var resultIndex = 0; resultIndex < vectorResults.Length; resultIndex++)
        {
            var vectorSearchResult = vectorResults[resultIndex];
            VisitedIndices.Add(vectorSearchResult.Index);
            
            var chunk = _engine.GetChunkByHnswId(vectorSearchResult.Index);

            if (!ReferencedDocuments.TryGetValue(chunk.Node.Document, out var referencedDocument))
            {
                referencedDocument = new ReferencedDocument(chunk.Node.Document);
                ReferencedDocuments.Add(chunk.Node.Document, referencedDocument);
            }
            
            referencedDocument.References.Add(new ReferencedDocument.Reference
            {
                VectorResult = vectorSearchResult,
                Chunk = chunk
            });

            var rawNode = chunk.Node.RawNode;
            
            Debug.Assert(rawNode.NodeType != MarkdownNode.Type.Document);
            
            if (!rawNode.NodeType.IsHeading())
            {
                // Should be some sort of content node.
                // Logically, the minimal context should be everything in the vicinity of the node.
                // We exclude the document itself (in the case we pulled a top-level heading as a result), because it would break the logical isolation.
                if (rawNode.Parent != null && rawNode.Parent.NodeType != MarkdownNode.Type.Document)
                {
                    referencedDocument.LogicalParents.Add(rawNode.Parent);
                }
            }
        }

        // Now, we can compute the bounding trees and the total amount of text found so far:
        var characterCount = 0;
        foreach (var referencedDocument in ReferencedDocuments.Values)
        {
            referencedDocument.ComputeBoundingTrees(out var documentCharacterCount);
            characterCount += documentCharacterCount;
        }

        return characterCount;
    }

    public sealed class ReferencedDocument(EmdDocument document)
    {
        public readonly EmdDocument Document = document;
        
        /// <summary>
        ///     All the search results found.
        ///     Populated by <see cref="Step"/>.
        /// </summary>
        public readonly List<Reference> References = [];
        
        /// <summary>
        ///     Nodes one level higher than the found nodes.
        ///     These will usually be headings.
        ///     Populated by <see cref="Step"/>.
        /// </summary>
        public readonly HashSet<MarkdownNode> LogicalParents = [];
        
        private readonly Dictionary<MarkdownNode, BoundingTree> _boundingTreesByRoot = [];
        private readonly List<BoundingTree> _boundingTreesSorted = [];
        private readonly Stack<Reference> _referencesForBuild = [];
        
        /// <summary>
        ///     Gets the bounding trees computed by <see cref="ComputeBoundingTrees"/>, sorted by their score.
        /// </summary>
        public IReadOnlyList<BoundingTree> BoundingTreesSorted => _boundingTreesSorted;

        /// <summary>
        ///     The average score across all fetched trees.
        /// </summary>
        public double AverageScore => _boundingTreesSorted.Count == 0
            ? 0.0
            : _boundingTreesSorted.Sum(x => x.AverageScore) / _boundingTreesSorted.Count; 
        
        /// <summary>
        ///     Computes all bounding trees and stores them in <see cref="BoundingTreesSorted"/>.
        /// </summary>
        /// <param name="characterCount">The total number of characters found.</param>
        /// <exception cref="Exception"></exception>
        public void ComputeBoundingTrees(out int characterCount)
        {
            _boundingTreesByRoot.Clear();
            _boundingTreesSorted.Clear();

            for (var index = 0; index < References.Count; index++)
            {
                var reference = References[index];
                _referencesForBuild.Push(reference);
            }

            // For each search result, we find the top parent that is part of the added logical parents.
            // This will merge all the results we found in disjoint trees.
            while (_referencesForBuild.Count > 0)
            {
                var front = _referencesForBuild.Pop();
                var currentNode = front.Chunk.Node.RawNode;
                
                // Keeps the highest logical parent found upstream:
                MarkdownNode? topParent = null;
                
                while (currentNode != null)
                {
                    if (LogicalParents.Contains(currentNode))
                    {
                        // Found a potential root node for the tree containing this node.
                        // Note that this ordering allows a reference to be its own parent.
                        topParent = currentNode;
                    }

                    currentNode = currentNode.Parent;
                }

                if (topParent == null)
                {
                    throw new Exception(
                        $"Failed to isolate logical parent for " +
                        $"{front.Chunk.Node.Document.Path}:" +
                        $"{front.Chunk.Node.RawNode.StartOffset}," +
                        $"{front.Chunk.Node.RawNode.EndOffset}"
                    );
                }

                if (!_boundingTreesByRoot.TryGetValue(topParent, out var boundingTree))
                {
                    boundingTree = new BoundingTree(topParent);
                    _boundingTreesByRoot.Add(topParent, boundingTree);
                }

                boundingTree.ReferenceCount++;
                boundingTree.ScoreSum += front.VectorResult.Score;
            }

            characterCount = 0;
            
            // Now, we can populate the list and sort it:
            foreach (var tree in _boundingTreesByRoot.Values)
            {
                _boundingTreesSorted.Add(tree);

                var root = tree.Root;
                characterCount += root.EndOffset - root.StartOffset;
            }
            
            _boundingTreesSorted.Sort((a, b) => a.AverageScore.CompareTo(b.AverageScore));
        }
        
        /// <summary>
        ///     Represents a single vector search result within this document.
        /// </summary>
        public readonly struct Reference
        {
            public required VectorSearchResult VectorResult { get; init; }
            public required EmdChunk Chunk { get; init; }
        }

        /// <summary>
        ///     Tree within the document. Traversing down, one can find a subset of the nodes found in this document. 
        /// </summary>
        public sealed class BoundingTree(MarkdownNode root)
        {
            /// <summary>
            ///     The uppermost logical parent (usually a heading).
            /// </summary>
            public readonly MarkdownNode Root = root;

            /// <summary>
            ///     The number of references inside this tree.
            /// </summary>
            public int ReferenceCount;

            /// <summary>
            ///     The sum of the vector search scores inside this tree.
            /// </summary>
            public double ScoreSum;
            
            /// <summary>
            ///     The average vector score.
            /// </summary>
            public double AverageScore => ScoreSum / ReferenceCount;
        }
    }
}