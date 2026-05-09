using KnowledgeSystem.Retrieval.Embeddings;

var a = new OpenAiEmbeddingService(
    "http://localhost:1234/v1",
    "text-embedding-mxbai-embed-large-v1",
    "none",
    1024
);

var x = await a.EmbedAsync("Represent this sentence for searching relevant passages: domain expansion");

return;