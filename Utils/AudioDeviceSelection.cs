using System;
using NAudio.CoreAudioApi;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The user's CHOICE of audio devices for the caption stack. Windows always has
    /// a "default" speaker and microphone, and until now Tempo could only use those.
    /// PCs with several devices (headset + monitor speakers, webcam mic + USB mic)
    /// can now pick which one captions listen through; empty id = follow Windows'
    /// default, exactly the old behaviour.
    ///
    /// One static home for the choice so every consumer — the caption engine, the
    /// voice profiler, the silence keep-alive — resolves the SAME device without
    /// threading settings through all of them. MainForm writes it from settings at
    /// startup and whenever the pickers change.
    /// </summary>
    /// <summary>
    /// One audio endpoint as the pickers see it: what it is, and whether Windows will
    /// actually let Tempo open it.
    ///
    /// The lists used to hold only ACTIVE endpoints, which meant a device Windows knows
    /// about but has unplugged or disabled simply did not exist as far as Tempo was
    /// concerned. From the user's side that is indistinguishable from "Tempo can't detect
    /// my device" — the headset is right there in Windows' own sound panel. Carrying the
    /// state lets the picker list it and say why it can't be used, which is a different
    /// message from not listing it at all.
    /// </summary>
    public sealed class AudioEndpointInfo
    {
        public string Id;
        public string Name;
        public DeviceState State;

        /// <summary>Only an Active endpoint can actually be opened for capture.</summary>
        public bool Usable => State == DeviceState.Active;

        /// <summary>Why this endpoint cannot be used, or "" when it can.</summary>
        public string StateNote
        {
            get
            {
                switch (State)
                {
                    case DeviceState.Active: return "";
                    case DeviceState.Unplugged: return Localization.T("not plugged in");
                    case DeviceState.Disabled: return Localization.T("disabled in Windows");
                    case DeviceState.NotPresent: return Localization.T("driver not present");
                    default: return Localization.T("unavailable");
                }
            }
        }

        /// <summary>The picker's label: the model, plus the reason when there is one.</summary>
        public string Label
        {
            get
            {
                string note = StateNote;
                return note.Length == 0 ? Name : Name + " — " + note;
            }
        }
    }

    public static class AudioDeviceSelection
    {
        /// <summary>Endpoint id of the chosen speaker; "" = Windows default.</summary>
        public static volatile string SpeakerId = "";
        /// <summary>Endpoint id of the chosen microphone; "" = Windows default.</summary>
        public static volatile string MicrophoneId = "";

        /// <summary>
        /// Resolves the chosen (or default) device for a flow. Returns null when
        /// nothing usable exists. The CALLER owns/disposes the returned device.
        /// A chosen device that has vanished falls back to the default — captions
        /// keep working when the picked headset is unplugged.
        /// </summary>
        public static MMDevice Resolve(MMDeviceEnumerator enumerator, DataFlow flow, out bool usedFallback)
        {
            usedFallback = false;
            string id = flow == DataFlow.Render ? SpeakerId : MicrophoneId;
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    MMDevice dev = enumerator.GetDevice(id);
                    if (dev != null && dev.State == DeviceState.Active && dev.DataFlow == flow)
                    {
                        return dev;
                    }
                    try { dev?.Dispose(); } catch { }
                }
                catch { }
                usedFallback = true;               // chosen device gone — tell the user
            }
            try
            {
                return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The endpoint id <see cref="Resolve"/> would pick right now, or null when nothing
        /// usable exists. Opens and releases the device; nothing is captured.
        ///
        /// Lets a capture ask "would following the device change actually move me?"
        /// before tearing itself down. Every Windows default-speaker switch used to reopen
        /// both caption captures even when the user had PINNED a speaker that never
        /// changed — dropping audio for a reopen that landed on the very same device.
        /// </summary>
        public static string ResolveId(DataFlow flow)
        {
            try
            {
                using (var en = new MMDeviceEnumerator())
                using (MMDevice dev = Resolve(en, flow, out _))
                {
                    return dev?.ID;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Name of a device, honest when the model can't be read.</summary>
        public static string NameOf(MMDevice dev, DataFlow flow)
        {
            try
            {
                string n = dev?.FriendlyName;
                if (!string.IsNullOrWhiteSpace(n))
                {
                    return n;
                }
            }
            catch { }
            return flow == DataFlow.Render
                ? "Unknown speaker (model not reported)"
                : "Unknown microphone (model not reported)";
        }
    }
}
