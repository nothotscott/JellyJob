namespace JellyJob.Core.ValueObjects
{
    /// What to do with the ad breaks comskip finds.
    public enum AdHandling
    {
        /// Transcode the whole recording and write an .edl beside it marking the breaks as commercials
        /// (type 3), which the EdlToMediaSegments Jellyfin plugin turns into skippable segments. A wrong
        /// detection costs nothing.
        Mark,

        /// Remove the breaks during the transcode. A wrong detection permanently loses part of the show.
        Cut,
    }
}
