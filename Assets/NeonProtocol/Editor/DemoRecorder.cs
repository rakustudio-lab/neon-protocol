using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Encoder;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Records real gameplay (Game view + audio) to MP4 for the demo video. Call Start() in play mode, Stop() after.
    public static class DemoRecorder
    {
        static RecorderController controller;

        public static string Start(string fileNoExt, int width = 1920, int height = 1080, int fps = 30)
        {
            if (!Application.isPlaying) return "enter play mode first";
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Demo"; movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings { Codec = CoreEncoderSettings.OutputCodec.MP4, EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High };
            movie.CaptureAudio = true;
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
            movie.OutputFile = System.IO.Path.GetFullPath(fileNoExt).Replace(System.IO.Path.DirectorySeparatorChar, '/');
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = fps;
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.CapFrameRate = true;
            controller = new RecorderController(settings);
            controller.PrepareRecording();
            bool ok = controller.StartRecording();
            var hud = Object.FindFirstObjectByType<DevHud>(); if (hud) hud.showStats = false;
            return ok ? "recording -> " + movie.OutputFile + ".mp4" : "failed to start";
        }

        public static string Stop()
        {
            if (controller == null) return "not recording";
            controller.StopRecording(); controller = null;
            return "stopped";
        }
    }
}
