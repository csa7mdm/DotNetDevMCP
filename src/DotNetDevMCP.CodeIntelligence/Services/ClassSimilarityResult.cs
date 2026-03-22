using System.Collections.Generic;

namespace DotNetDevMCP.CodeIntelligence.Services;

public record ClassSimilarityResult(
    List<ClassSemanticFeatures> SimilarClasses,
    double AverageSimilarityScore
);
