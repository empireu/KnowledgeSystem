using System.Text.Json;
using KnowledgeSystem.Ai;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Vector;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeSystem.Plugins.Surveillance.Database;

/// <summary>
///     Resolves raw <see cref="ExtractedEntityRecord"/> rows into canonical entities via a three-stage dedup pipeline.
///     <para><b>Pipeline:</b></para>
///     <list type="number">
///         <item>
///             <b>Exact name grouping</b> - groups raw records by case-insensitive name, collects aliases from <c>AliasesJson</c> across all records in each group, and picks the most specific type and longest description. This trivially collapses the same entity extracted from multiple chunks. Aliases include the entity's own primary name so that future alias lookups work regardless of which name variant was used.
///         </item>
///         <item>
///             <b>Name embedding similarity</b> - embeds each group's primary name, then computes pairwise cosine similarity. Pairs within threshold become candidates for gating. This catches typos, bracket mangling, singular/plural variants, and faction tags that exact matching misses.
///         </item>
///         <item>
///             <b>Logprob gating</b> - for each candidate pair where at least one entity has a description, sends a prompt to the local model via <see cref="ILogprobGatingService"/>. The prompt encodes rules to ignore case, plurals, leading "the", trailing punctuation, and faction tags, while respecting semantic differences in descriptions (e.g. "The Expanse" the TV series is not equal to "Expanse" the game server). Confirmed pairs are merged. Pairs with no descriptions on either side are skipped (insufficient data).
///         </item>
///     </list>
///
///     <para><b>Merge resolution:</b></para>
///     <para>
///         Confirmed merges are applied via union-find to handle transitive chains (A=B, B=C => A=B=C).
///         The surviving entity keeps the longest type and description from its absorbed members.
///     </para>
///
///     <para><b>Output:</b></para>
///     <para>
///         Canonical entities are inserted into <c>canonical_entities</c> with their aliases in <c>canonical_entity_aliases</c>, and name-only embeddings in <c>canonical_entity_embeddings</c>.
///         Returns a <c>Dictionary&lt;string, long&gt;</c> mapping every known name variant to its canonical entity ID, used by <see cref="ClaimMigrator"/> for claim resolution.
///     </para>
/// </summary>
public sealed class EntityResolver(
    IngestionDbContext rawDb,
    CanonicalDbContext canonicalDb,
    IEmbeddingService embeddingService,
    ILogprobGatingService gateService
)
{
    /// <summary>
    ///     Run the full three-stage resolution pipeline and populate <c>canonical_entities</c>,
    ///     <c>canonical_entity_aliases</c>, and <c>canonical_entity_embeddings</c>.
    ///
    ///     <para>
    ///         Reads raw <see cref="ExtractedEntityRecord"/> rows via EF Core, runs the three dedup stages
    ///         (exact grouping → embedding similarity → logprob gate), resolves transitive merges via union-find,
    ///         and inserts the resulting canonical entities.
    ///     </para>
    ///
    ///     <returns>
    ///         A map from every known name variant (primary name + all aliases) to its canonical entity ID.
    ///         Used by <see cref="ClaimMigrator"/> to resolve <c>SubjectName</c> and <c>ObjectEntityName</c>
    ///         on raw claims.
    ///     </returns>
    /// </summary>
    public async Task<Dictionary<string, long>> ResolveAsync(CancellationToken cancellationToken)
    {
        var records = await rawDb.Entities.ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return new Dictionary<string, long>();
        }

        // Stage 1: Exact name grouping:
        var groups = GroupByName(records);

        // Stage 2: Similarity:
        var names = groups.Select(g => g.PrimaryName).ToArray();
        var nameEmbeddings = await embeddingService.EmbedBatchAsync(names, cancellationToken);
        var candidates = FindCrossNameCandidates(nameEmbeddings, groups);

        // Stage 3: LLM:
        var merges = await GateCandidatesAsync(candidates, groups, cancellationToken);

        // Resolve transitive merges and produce final entity set:
        var resolved = ResolveMerges(groups, merges);

        // Insert canonical entities, aliases, and embeddings:
        var nameToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in resolved)
        {
            var id = canonicalDb.InsertCanonicalEntity(
                group.PrimaryName, 
                group.Type,
                group.Description
            );

            foreach (var alias in group.Aliases)
            {
                canonicalDb.AddAlias(id, alias);
            }

            var embeddingIndex = Array.IndexOf(names, group.PrimaryName);

            if (embeddingIndex >= 0)
            {
                canonicalDb.InsertEntityEmbedding(id, nameEmbeddings[embeddingIndex].Span);
            }

            nameToId[group.PrimaryName] = id;

            foreach (var alias in group.Aliases)
            {
                nameToId[alias] = id;
            }
        }

        return nameToId;
    }

    private static List<CanonicalEntityPrototype> GroupByName(List<ExtractedEntityRecord> records)
    {
        var byName = records
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = new List<CanonicalEntityPrototype>(byName.Count);

        foreach (var g in byName)
        {
            var allAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var descriptions = new List<string>();
            string? bestType = null;
            string? bestDescription = null;

            foreach (var record in g)
            {
                if (record.AliasesJson != "[]")
                {
                    var aliases = JsonSerializer.Deserialize<string[]>(record.AliasesJson);

                    if (aliases != null)
                    {
                        foreach (var alias in aliases)
                        {
                            allAliases.Add(alias);
                        }
                    }
                }

                allAliases.Add(record.Name);

                if (record.Type != null && (bestType == null || record.Type.Length > bestType.Length))
                {
                    bestType = record.Type;
                }

                if (!string.IsNullOrWhiteSpace(record.Description))
                {
                    descriptions.Add(record.Description);
                }
            }

            if (descriptions.Count > 0)
            {
                bestDescription = descriptions
                    .OrderByDescending(d => d.Length)
                    .First();
            }

            var primaryName = g.Key;
            allAliases.Remove(primaryName);

            groups.Add(new CanonicalEntityPrototype
            {
                PrimaryName = primaryName,
                Type = bestType,
                Description = bestDescription,
                Aliases = allAliases.ToList()
            });
        }

        return groups;
    }
    
    private static List<(int I, int J)> FindCrossNameCandidates(ReadOnlyMemory<float>[] embeddings, List<CanonicalEntityPrototype> groups)
    {
        const float threshold = 0.30f;
        var candidates = new List<(int, int)>();

        for (var i = 0; i < groups.Count; i++)
        {
            for (var j = i + 1; j < groups.Count; j++)
            {
                var score = VectorObjective.AdjustedCosineSimilarity(embeddings[i].Span, embeddings[j].Span);

                if (score <= threshold)
                {
                    candidates.Add((i, j));
                }
            }
        }

        return candidates;
    }
    
    private async Task<List<(int I, int J)>> GateCandidatesAsync(List<(int I, int J)> candidates, List<CanonicalEntityPrototype> groups, CancellationToken cancellationToken)
    {
        const double gateThreshold = 0.6;
        var confirmed = new List<(int, int)>();

        foreach (var (i, j) in candidates)
        {
            var a = groups[i];
            var b = groups[j];

            if (string.IsNullOrWhiteSpace(a.Description) && string.IsNullOrWhiteSpace(b.Description))
            {
                continue;
            }

            var prompt = BuildGatePrompt(a, b);
            var same = await gateService.ExecuteAsync(prompt, gateThreshold, cancellationToken);

            if (same)
            {
                confirmed.Add((i, j));
            }
        }

        return confirmed;
    }
    
    // P.S. Disjoint-set time?
    private static List<CanonicalEntityPrototype> ResolveMerges(List<CanonicalEntityPrototype> groups, List<(int I, int J)> merges)
    {
        var parent = new int[groups.Count];

        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        foreach (var (i, j) in merges)
        {
            Union(i, j);
        }

        var merged = new Dictionary<int, CanonicalEntityPrototype>();

        for (var i = 0; i < groups.Count; i++)
        {
            var root = Find(i);

            if (!merged.TryGetValue(root, out var proto))
            {
                proto = new CanonicalEntityPrototype
                {
                    PrimaryName = groups[i].PrimaryName,
                    Type = groups[i].Type,
                    Description = groups[i].Description,
                    Aliases = new List<string>(groups[i].Aliases)
                };

                merged[root] = proto;
            }
            else
            {
                proto.Aliases.Add(groups[i].PrimaryName);

                foreach (var alias in groups[i].Aliases)
                {
                    if (!proto.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    {
                        proto.Aliases.Add(alias);
                    }
                }

                if (proto.Type == null && groups[i].Type != null || groups[i].Type != null && groups[i].Type!.Length > (proto.Type?.Length ?? 0))
                {
                    proto.Type = groups[i].Type;
                }

                if (proto.Description == null && groups[i].Description != null || groups[i].Description != null && groups[i].Description!.Length > (proto.Description?.Length ?? 0))
                {
                    proto.Description = groups[i].Description;
                }
            }
        }

        return merged.Values.ToList();

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);

            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }
    }

    private static string BuildGatePrompt(CanonicalEntityPrototype a, CanonicalEntityPrototype b)
    {
        var aliasesA = a.Aliases.Count > 0 ? string.Join(", ", a.Aliases) : "none";
        var aliasesB = b.Aliases.Count > 0 ? string.Join(", ", b.Aliases) : "none";

        return "You are an entity deduplication classifier. "
            + "You will be given two entity records. Determine if they refer to the same real-world entity. "
            + "Apply these rules:\n"
            + "- Singular vs plural is NOT a difference (rail=rails, boat=boats).\n"
            + "- Letter case is NOT a difference (KESS=kess).\n"
            + "- Leading 'the' is NOT a difference (the pitt=Pitt).\n"
            + "- Trailing punctuation like ? or ! is NOT a difference.\n"
            + "- Faction/clan tags in brackets or parens like [CLAW], (FCN), -GBS- are decorations, not identity. "
            + "The core name without the tag is what matters.\n"
            + "- If the names differ only by these rules and the types are the same, answer YES.\n"
            + "- If the descriptions describe clearly different things (e.g. a TV series vs a game server), answer NO even if names are similar.\n"
            + "Answer only YES or NO.\n\n"
            + $"Entity A: name='{a.PrimaryName}', type={a.Type ?? "unknown"}, aliases=[{aliasesA}]\n  description: {a.Description ?? "none"}\n\n"
            + $"Entity B: name='{b.PrimaryName}', type={b.Type ?? "unknown"}, aliases=[{aliasesB}]\n  description: {b.Description ?? "none"}\n\n"
            + "Answer:";
    }
    
    private sealed class CanonicalEntityPrototype
    {
        public required string PrimaryName { get; init; }
        
        public string? Type { get; set; }
        
        public string? Description { get; set; }
        
        public List<string> Aliases { get; init; } = [];
    }
}
