namespace AutoScenarioX.Api.DTOs.Prioritization;

public sealed record CompareScenariosRequest(IReadOnlyList<Guid> ScenarioVersionIds, string Method = "gower", double ClusterThreshold = .25);
public sealed record SimilarityPairResponse(Guid LeftVersionId, Guid RightVersionId, double Distance, double Similarity, double RawDistance);
public sealed record SimilarityClusterResponse(int ClusterId, IReadOnlyList<Guid> ScenarioVersionIds);
public sealed record SimilarityResponse(Guid Id, string Method, DateTimeOffset CreatedAt,
    IReadOnlyList<SimilarityClusterResponse> Clusters, IReadOnlyList<SimilarityPairResponse> Pairs,
    IReadOnlyList<string> FeatureNames, string Note);
