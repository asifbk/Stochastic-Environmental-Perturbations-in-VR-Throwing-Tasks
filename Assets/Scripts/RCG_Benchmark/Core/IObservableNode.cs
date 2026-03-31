namespace RCG.Core
{
    /// <summary>
    /// Non-generic handle used by RCGResolver to propagate dirty observables
    /// without knowing their type at the call site.
    /// </summary>
    public interface IObservableNode
    {
        void Propagate();
    }
}
