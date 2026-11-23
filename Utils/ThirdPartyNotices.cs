using System;
using System.IO;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The open-source parts inside Tempo.exe and the notices their licences ask a copy to carry, for About Tempo's
    /// "Open-source licences" link. The texts are the packages' own (MIT, and Microsoft's BSD-style terms for
    /// WebView2), copied from the NuGet packages Tempo is built from — and, since round 26, the face models' (YuNet: MIT;
    /// SFace and the ported OpenCV code: Apache 2.0, its text built in); since round 34 the speaker recognition model's
    /// (WeSpeaker, CC BY 4.0); since round 35 sherpa-onnx (Apache 2.0) with ONNX Runtime (MIT), and the Nemotron models'
    /// licences (NVIDIA Open Model License, OpenMDW 1.1). Kept in English: they are legal texts.
    /// </summary>
    internal static class ThirdPartyNotices
    {
        private const string Mit =
            "Permission is hereby granted, free of charge, to any person obtaining a copy\r\n" +
            "of this software and associated documentation files (the \"Software\"), to deal\r\n" +
            "in the Software without restriction, including without limitation the rights\r\n" +
            "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\r\n" +
            "copies of the Software, and to permit persons to whom the Software is\r\n" +
            "furnished to do so, subject to the following conditions:\r\n" +
            "\r\n" +
            "The above copyright notice and this permission notice shall be included in all\r\n" +
            "copies or substantial portions of the Software.\r\n" +
            "\r\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\r\n" +
            "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\r\n" +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\r\n" +
            "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\r\n" +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\r\n" +
            "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\r\n" +
            "SOFTWARE.\r\n";

        private const string WebView2 =
            "Copyright (C) Microsoft Corporation. All rights reserved.\r\n" +
            "\r\n" +
            "Redistribution and use in source and binary forms, with or without\r\n" +
            "modification, are permitted provided that the following conditions are\r\n" +
            "met:\r\n" +
            "\r\n" +
            "   * Redistributions of source code must retain the above copyright\r\n" +
            "notice, this list of conditions and the following disclaimer.\r\n" +
            "   * Redistributions in binary form must reproduce the above\r\n" +
            "copyright notice, this list of conditions and the following disclaimer\r\n" +
            "in the documentation and/or other materials provided with the\r\n" +
            "distribution.\r\n" +
            "   * The name of Microsoft Corporation, or the names of its contributors\r\n" +
            "may not be used to endorse or promote products derived from this\r\n" +
            "software without specific prior written permission.\r\n" +
            "\r\n" +
            "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS\r\n" +
            "\"AS IS\" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT\r\n" +
            "LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR\r\n" +
            "A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT\r\n" +
            "OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,\r\n" +
            "SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT\r\n" +
            "LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,\r\n" +
            "DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY\r\n" +
            "THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT\r\n" +
            "(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE\r\n" +
            "OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.\r\n";

        private static string Rule => new string('-', 78) + "\r\n";

        /// <summary>Every notice, as one text.</summary>
        public static string Text
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append("Tempo ").Append(AppVersion.Text).Append(" - open-source software inside it\r\n");
                sb.Append(Rule);
                sb.Append("Tempo is built with the open-source components below. Their licences ask for these\r\n");
                sb.Append("notices to travel with every copy; this is where Tempo keeps them.\r\n\r\n");

                Part(sb, ".NET (runtime and Windows Forms)", "https://github.com/dotnet/runtime",
                     "MIT License", "Copyright (c) .NET Foundation and Contributors", Mit);
                Part(sb, "Whisper.net (Live Captions speech engine wrapper)", "https://github.com/sandrohanea/whisper.net",
                     "MIT License", "Copyright (c) 2024 sandrohanea", Mit);
                Part(sb, "whisper.cpp and ggml (the speech engine itself, CPU and Vulkan builds)", "https://github.com/ggml-org/whisper.cpp",
                     "MIT License", "Copyright (c) 2023-2024 The ggml authors", Mit);
                Part(sb, "Silero VAD (voice detection for Live Captions; ggml conversion)", "https://github.com/snakers4/silero-vad",
                     "MIT License", "Copyright (c) 2020-present Silero Team; ggml conversion (c) The ggml authors", Mit);
                Part(sb, "NAudio (audio capture)", "https://github.com/naudio/NAudio",
                     "MIT License", "Copyright 2020 Mark Heath", Mit);
                Part(sb, "Microsoft Edge WebView2 SDK", "https://aka.ms/webview",
                     "BSD-style licence (Microsoft)", null, WebView2);
                Part(sb, "YuNet face detector (caption speakers' face analysis; the model is built in)",
                     "https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet",
                     "MIT License", "Copyright (c) 2020 Shiqi Yu <shiqi.yu@gmail.com>", Mit);
                Part(sb, "OpenCV (FaceDetectorYN's decoding and FaceRecognizerSF's alignment and matching, ported to C#)",
                     "https://github.com/opencv/opencv",
                     "Apache License 2.0 (text below)",
                     "Copyright (C) the OpenCV team and contributors.\r\n" +
                     "Changed: rewritten in C# for Windows' machine-learning engine (Tempo's Utils/FaceAi.cs); the face\r\n" +
                     "alignment's similarity transform is solved in closed form instead of by SVD.",
                     "Licensed under the Apache License, Version 2.0; see below.\r\n");
                // Round 35: the engine for NVIDIA's Nemotron streaming models (Utils/NemotronStreaming.cs), from the NuGet
                // packages org.k2fsa.sherpa.onnx 2.0.1 (Apache-2.0, "Copyright 2019-2023 Xiaomi Corporation"); its native
                // library carries ONNX Runtime 1.28.2 (Microsoft, MIT).
                Part(sb, "sherpa-onnx (the engine for the Nemotron streaming speech models)", "https://github.com/k2-fsa/sherpa-onnx",
                     "Apache License 2.0 (text below)",
                     "Copyright 2019-2023 Xiaomi Corporation (the Next-gen Kaldi development team)",
                     "Licensed under the Apache License, Version 2.0; see below.\r\n");
                Part(sb, "ONNX Runtime (inside sherpa-onnx's engine)", "https://github.com/microsoft/onnxruntime",
                     "MIT License", "Copyright (c) Microsoft Corporation", Mit);

                sb.Append(Rule);
                sb.Append("Speech models you download for Live Captions (OpenAI Whisper) come under the MIT License\r\n");
                sb.Append("of their authors, as published with them. Tempo does not include them.\r\n\r\n");
                // Round 35: the Nemotron models, downloaded only when picked, from sherpa-onnx's releases (their int8 exports).
                sb.Append("NVIDIA's Nemotron streaming speech models, downloaded when you pick one (sherpa-onnx's int8 exports,\r\n");
                sb.Append("https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models), come under their own licences:\r\n");
                sb.Append("  - Nemotron Speech Streaming EN 0.6B, https://huggingface.co/nvidia/nemotron-speech-streaming-en-0.6b:\r\n");
                sb.Append("    Licensed by NVIDIA Corporation under the NVIDIA Open Model License,\r\n");
                sb.Append("    https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-license/\r\n");
                sb.Append("  - Nemotron 3.5 ASR Streaming 0.6B, https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b:\r\n");
                sb.Append("    the OpenMDW License 1.1, as published with it.\r\n");
                sb.Append("Tempo does not include them and does not change them.\r\n\r\n");
                sb.Append("The face recognition model Tempo downloads the first time face analysis runs (SFace,\r\n");
                sb.Append("https://github.com/opencv/opencv_zoo/tree/main/models/face_recognition_sface; Copyright (C)\r\n");
                sb.Append("2021, Shenzhen Institute of Artificial Intelligence and Robotics for Society) comes under the\r\n");
                sb.Append("Apache License 2.0 below. Tempo does not include it.\r\n\r\n");
                // Round 34: the speaker recognition model (Utils/VoicePrint.cs). CC BY 4.0 asks for credit, a link to the licence
                // and whether it was changed.
                sb.Append("The speaker recognition model Tempo downloads the first time speaker labels run (WeSpeaker's\r\n");
                sb.Append("ResNet34 trained on VoxCeleb, \"voxceleb_resnet34_LM\", https://github.com/wenet-e2e/wespeaker;\r\n");
                sb.Append("by the WeSpeaker authors; the ONNX file as the sherpa-onnx project publishes it,\r\n");
                sb.Append("https://github.com/k2-fsa/sherpa-onnx) comes, like the VoxCeleb data it was trained on, under the\r\n");
                sb.Append("Creative Commons Attribution 4.0 International licence: https://creativecommons.org/licenses/by/4.0/\r\n");
                sb.Append("Tempo does not include it and does not change it; the features it is given are computed the way\r\n");
                sb.Append("WeSpeaker computes them (a Kaldi-style filterbank), written anew in C# (Tempo's Utils/VoicePrint.cs).\r\n");

                sb.Append(Rule);
                sb.Append(ApacheLicence());
                return sb.ToString();
            }
        }

        /// <summary>The Apache License 2.0's text, as published with SFace (Assets/licences/Apache-2.0.txt, built in).</summary>
        private static string ApacheLicence()
        {
            try
            {
                using (Stream s = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream("Tempo.Apache-2.0.txt"))
                {
                    if (s != null)
                    {
                        using (var r = new StreamReader(s, Encoding.UTF8)) { return r.ReadToEnd(); }
                    }
                }
            }
            catch { }
            return "Apache License, Version 2.0: http://www.apache.org/licenses/LICENSE-2.0\r\n";
        }

        private static void Part(StringBuilder sb, string name, string url, string licence, string copyright, string terms)
        {
            sb.Append(Rule);
            sb.Append(name).Append("\r\n");
            sb.Append(url).Append("\r\n");
            sb.Append(licence).Append("\r\n\r\n");
            if (!string.IsNullOrEmpty(copyright)) { sb.Append(copyright).Append("\r\n\r\n"); }
            sb.Append(terms).Append("\r\n");
        }

        /// <summary>
        /// Writes the notices to a text file in the temp folder and opens it in the user's text editor.
        /// False (and nothing opened) when that fails.
        /// </summary>
        public static bool Open()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "Tempo open-source licences.txt");
                File.WriteAllText(path, Text, new UTF8Encoding(false));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[About] could not open the open-source licences: " + ex.Message);
                return false;
            }
        }
    }
}
