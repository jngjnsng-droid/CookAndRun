namespace CookAndRun.Progression
{
    /// <summary>Shop/result/ready states are outside the chapter's business timer.</summary>
    public enum SessionState
    {
        Ready,
        Playing,
        Result,
        Shop,
        Ending
    }
}
