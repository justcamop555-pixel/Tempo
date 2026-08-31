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
