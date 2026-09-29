namespace JellyJob.Core.ValueObjects
{
    /// One detected ad break, as offsets from the start of the recording.
    public record AdBreak(TimeSpan Start, TimeSpan End);
}
