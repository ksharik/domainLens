namespace Partial.Sample;

public class BranchBase
{
}

#if FEATURE_BRANCH
public class FeatureBranchDerived : BranchBase
{
}
#else
public class BranchDerived : BranchBase
{
}
#endif
