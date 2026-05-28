using System.Collections.Concurrent;
using KnowledgeSystem.Vector;
using KnowledgeSystem.Vector.Hnsw;

// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.Tests;

public partial class MutableHnswIndexTests
{
    [Theory]
    [InlineData(500, 4)]
    [InlineData(1000, 8)]
    [InlineData(2000, 4)]
    public void ConcurrentInsert_ProducesCorrectVectorCount(int vectorCount, int parallelism)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            i => { index.Insert(corpus[i]); });

        var liveCount = 0;
        for (var i = 0; i < index.VectorsInternal.Count; i++)
        {
            if (index.VectorsInternal[i] != null)
            {
                liveCount++;
            }
        }

        Assert.Equal(vectorCount, liveCount);
    }

    [Theory]
    [InlineData(500, 4)]
    [InlineData(1000, 8)]
    public void ConcurrentInsert_GraphIntegrity(int vectorCount, int parallelism)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            i => { index.Insert(corpus[i]); });

        AssertGraphIntegrity(index);
    }

    [Fact]
    public void ConcurrentInsert_RecallIsAcceptable()
    {
        for (var repeat = 0; repeat < 10; repeat++)
        {
            const int vectorCount = 1000;
            const int k = 10;
            const int queryCount = 500;

            var corpus = BuildRandomCorpusArray(vectorCount);
            var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

            // Track the actual stored vector index for each corpus entry:
            var storedVectors = new IStoredVector[vectorCount];

            Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = 8 },
                i => { storedVectors[i] = index.Insert(corpus[i]); });

            AssertGraphIntegrity(index);

            // Build a lookup from HNSW index to corpus data for brute-force comparison:
            var indexToCorpus = new Dictionary<int, float[]>();
            for (var i = 0; i < vectorCount; i++)
            {
                indexToCorpus[storedVectors[i].Index] = corpus[i];
            }

            var random = new Random(Seed + 99);
            var totalRecall = 0.0;
            for (var q = 0; q < queryCount; q++)
            {
                var query = GetTestVector(random, Dimension);
                var hnswResults = index.Search(query, k, efSearch: 200);

                // Brute-force: compute distance from query to every stored vector, sort, take k:
                var bruteForceResults = indexToCorpus
                    .Select(kv => (Index: kv.Key, Score: VectorObjective.AdjustedCosineSimilarity(kv.Value, query)))
                    .OrderBy(x => x.Score)
                    .Take(k)
                    .Select(x => x.Index)
                    .ToHashSet();

                var hnswIndices = new HashSet<int>(hnswResults.Select(r => r.Index));
                var hits = hnswIndices.Count(bruteForceResults.Contains);
                totalRecall += (double)hits / k;
            }

            var averageRecall = totalRecall / queryCount;
            output.WriteLine($"Concurrent insert recall: {averageRecall:P1}");

            // Concurrent build may have slightly lower recall than sequential due to insertion order effects.
            Assert.True(averageRecall >= 0.75, $"Bad average recall after concurrent insert: {averageRecall:P1}");
        }
    }

    [Fact]
    public async Task ConcurrentInsert_NoDeadlock()
    {
        const int vectorCount = 3000;
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        var completed = false;
        var task = Task.Run(() =>
        {
            Parallel.For(0, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                i => { index.Insert(corpus[i]); });

            completed = true;
        });

        await task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(completed, "Concurrent insert did not complete");
    }

    [Fact]
    public async Task ConcurrentInsert_WhileSearching_DoesNotCrash()
    {
        const int vectorCount = 1000;
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, 16, 32, efConstruction: 200, seed: Seed);

        // Insert an initial batch so search has something to work with:
        for (var i = 0; i < 50; i++)
        {
            index.Insert(corpus[i]);
        }

        var searchExceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        // Start a background search loop:
        var searchTask = Task.Run(() =>
        {
            var random = new Random(Seed + 42);
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var query = GetTestVector(random, Dimension);
                    index.Search(query, 5, efSearch: 50);
                }
                catch (Exception ex)
                {
                    searchExceptions.Add(ex);
                }
            }
        }, cts.Token);

        // Insert remaining vectors in parallel:
        Parallel.For(50, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => { index.Insert(corpus[i]); });

        await cts.CancelAsync();
        // ReSharper disable once MethodSupportsCancellation
        await searchTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(searchExceptions);
    }
    
    [Theory]
    [InlineData(500, 4, 8)]
    [InlineData(1000, 4, 12)]
    public async Task ConcurrentInsert_SmallMaxConnections_NoCrash(int vectorCount, int laneConnections, int denseConnections)
    {
        var corpus = BuildRandomCorpusArray(vectorCount);
        var index = new MutableHnswIndex(Dimension, laneConnections, denseConnections, efConstruction: 50, seed: Seed);

        var searchExceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        // Insert an initial batch so search has something to work with:
        for (var i = 0; i < 20; i++)
        {
            index.Insert(corpus[i]);
        }

        // Start a background search loop with tiny efSearch to stress stale-edge sensitivity:
        var searchTask = Task.Run(() =>
        {
            var random = new Random(Seed + 42);
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var query = GetTestVector(random, Dimension);
                    index.Search(query, 5, efSearch: 2);
                }
                catch (Exception ex)
                {
                    searchExceptions.Add(ex);
                }
            }
        }, cts.Token);

        // Insert remaining vectors in parallel:
        Parallel.For(20, vectorCount, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => { index.Insert(corpus[i]); });

        await cts.CancelAsync();

        // ReSharper disable once MethodSupportsCancellation
        await searchTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(searchExceptions);
    }
    
    [Fact]
    public void ConcurrentInsert_WhileSearching_TimeBased_NoCrash()
    {
        const int durationSeconds = 10;
        const int threadCount = 4;
        var index = new MutableHnswIndex(Dimension, 4, 8, efConstruction: 50, seed: Seed);
        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        var seedRandom = new Random(Seed);
        for (var i = 0; i < 30; i++)
        {
            index.Insert(GetTestVector(seedRandom, Dimension));
        }

        var insertThreads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var random = new Random(Seed + t);
            insertThreads[t] = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        index.Insert(GetTestVector(random, Dimension));
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })
            {
                IsBackground = true
            };
        }

        var searchThreads = new Thread[2];
        for (var t = 0; t < searchThreads.Length; t++)
        {
            var random = new Random(Seed + 100 + t);
            searchThreads[t] = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var query = GetTestVector(random, Dimension);
                        index.Search(query, 5, efSearch: 2);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })
            {
                IsBackground = true
            };
        }

        foreach (var thread in insertThreads) thread.Start();
        foreach (var thread in searchThreads) thread.Start();

        Thread.Sleep(TimeSpan.FromSeconds(durationSeconds));
        cts.Cancel();

        foreach (var thread in insertThreads) thread.Join(TimeSpan.FromSeconds(5));
        foreach (var thread in searchThreads) thread.Join(TimeSpan.FromSeconds(5));

        Assert.Empty(exceptions);
    }
}