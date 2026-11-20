using System;

namespace IntsLiveCoach
{
    // A UI-to-poll-thread snapshot. Neither producer nor consumer can mutate its stored lap.
    public sealed class ReferenceLiveSettings
    {
        readonly ReferenceLap reference;
        public readonly bool Enabled, AllowVersionMismatch, AllowMotecAlignment;
        public readonly string ConfirmedSourceCar, ConfirmedSourceTrack, ConfirmedSourceLayout;
        public ReferenceLap Reference { get { return Copy(reference); } }

        public ReferenceLiveSettings(ReferenceLap reference, bool enabled, bool allowVersionMismatch,
            bool allowMotecAlignment, string confirmedSourceCar="", string confirmedSourceTrack="", string confirmedSourceLayout="")
        {
            this.reference=Copy(reference);
            Enabled=enabled; AllowVersionMismatch=allowVersionMismatch; AllowMotecAlignment=allowMotecAlignment;
            ConfirmedSourceCar=confirmedSourceCar??""; ConfirmedSourceTrack=confirmedSourceTrack??""; ConfirmedSourceLayout=confirmedSourceLayout??"";
        }
        static ReferenceLap Copy(ReferenceLap source)
        {return source==null?null:ReferenceLap.LoadJson(source.ToJson());}
    }
}
