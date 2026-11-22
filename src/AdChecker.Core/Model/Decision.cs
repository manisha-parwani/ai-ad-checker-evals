namespace AdChecker.Core.Model;

/// <summary>Final decision for an ad. Serialized as "approved", "rejected" or "needs_review".</summary>
public enum Decision
{
    Approved,
    Rejected,
    NeedsReview,
}
