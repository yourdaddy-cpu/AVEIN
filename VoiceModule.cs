using System;
using System.Linq;
using System.Speech.Synthesis;
using System.Speech.Recognition;
using System.Threading.Tasks;

namespace AVEIN
{
    public static class VoiceModule
    {
        private static SpeechSynthesizer _synth;

        public static void Speak(string text)
        {
            try
            {
                if (_synth == null)
                {
                    _synth = new SpeechSynthesizer();
                    var femaleVoice = _synth.GetInstalledVoices()
                        .FirstOrDefault(v => v.VoiceInfo.Gender == VoiceGender.Female);
                    if (femaleVoice != null)
                        _synth.SelectVoice(femaleVoice.VoiceInfo.Name);
                }

                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch { }
        }

        public static void StopSpeaking()
        {
            try { _synth?.SpeakAsyncCancelAll(); } catch { }
        }

        public static async Task<string> ListenOnceAsync()
        {
            try
            {
                using (var recognizer = new SpeechRecognitionEngine())
                {
                    recognizer.SetInputToDefaultAudioDevice();
                    recognizer.LoadGrammar(new DictationGrammar());

                    var tcs = new TaskCompletionSource<string>();

                    recognizer.SpeechRecognized += (s, e) => tcs.TrySetResult(e.Result.Text);
                    recognizer.RecognizeAsync(RecognizeMode.Single);

                    var timeoutTask = Task.Delay(8000);
                    var completed = await Task.WhenAny(tcs.Task, timeoutTask);

                    recognizer.RecognizeAsyncStop();

                    if (completed == timeoutTask) return null;
                    return tcs.Task.Result;
                }
            }
            catch { return null; }
        }
    }
}
