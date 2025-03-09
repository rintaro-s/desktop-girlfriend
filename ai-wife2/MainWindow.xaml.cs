using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Speech.Recognition;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Net.WebSockets;
using System.Threading;

namespace ai_wife2
{
    public partial class MainWindow : Window
    {
        private readonly string imagePath = "character.png"; // PNG画像のパス
        private readonly HttpClient httpClient = new HttpClient();
        private readonly List<string> chatHistory = new List<string>();
        private readonly Random random = new Random();
        private DispatcherTimer idleTimer;
        private SpeechRecognitionEngine recognizer;

        private int idleChangeInterval = 100000; // ランダム待機アニメーションの切り替え間隔（ミリ秒）

        public MainWindow()
        {
            InitializeComponent();
            SetImage(imagePath);

            this.Topmost = true;

            // ランダム待機アニメーション用のタイマー
            idleTimer = new DispatcherTimer();
            idleTimer.Interval = TimeSpan.FromMilliseconds(idleChangeInterval);
            idleTimer.Tick += IdleTimer_Tick;
            idleTimer.Start();

            // 音声認識エンジンの初期化
            recognizer = new SpeechRecognitionEngine();
            recognizer.SetInputToDefaultAudioDevice();
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SpeechRecognized += Recognizer_SpeechRecognized;

            // Alt + Xで音声認識を開始
            var startRecognitionCommand = new RoutedCommand();
            startRecognitionCommand.InputGestures.Add(new KeyGesture(Key.X, ModifierKeys.Alt));
            CommandBindings.Add(new CommandBinding(startRecognitionCommand, StartRecognition));
        }

        private void SetImage(string file)
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
            BitmapImage bitmap = new BitmapImage(new Uri(path, UriKind.Absolute));
            CharacterImage.Source = bitmap;
        }

        // ランダムに待機アニメーションを変更する処理
        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            // ここでは何もしない
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (SpeechBubble.Visibility == Visibility.Visible) return;
            // なでなで.mp4を再生する代わりに何もしない
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (SpeechBubble.Visibility != Visibility.Visible)
            {
                SpeechBubble.Visibility = Visibility.Visible;
                var pos = e.GetPosition(this);
                double bubbleWidth = SpeechBubble.ActualWidth;
                double bubbleHeight = SpeechBubble.ActualHeight;

                // 吹き出しが画面外に出ないように位置を調整
                double left = pos.X;
                double top = pos.Y;

                if (left + bubbleWidth > this.ActualWidth)
                {
                    left = this.ActualWidth - bubbleWidth;
                }

                if (top + bubbleHeight > this.ActualHeight)
                {
                    top = this.ActualHeight - bubbleHeight;
                }

                SpeechBubble.Margin = new Thickness(left, top, 0, 0);
            }
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (SpeechBubble.Visibility == Visibility.Visible) return;
            // 移動開始時に何もしない
            base.OnPreviewMouseLeftButtonDown(e);
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (SpeechBubble.Visibility == Visibility.Visible) return;
            // 移動終了時に何もしない
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async void SendSpeech_Click(object sender, RoutedEventArgs e)
        {
            SpeechBubble.Visibility = Visibility.Collapsed;
            string userText = SpeechTextBox.Text;
            SpeechTextBox.Clear();
            chatHistory.Add("ユーザー: " + userText);
            string reply = await ProcessChat(userText);
            chatHistory.Add("彼女: " + reply);
            string filteredReply = RemoveCodeBlocks(reply);
            SpeechText.Text = filteredReply;
            SpeechBubble.Visibility = Visibility.Visible;

            // 文章の長さに応じて表示時間を計算
            int displayTime = CalculateDisplayTime(filteredReply);

            // 計算された時間後に吹き出しをクリアして非表示にする
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayTime) };
            timer.Tick += (s, args) =>
            {
                SpeechBubble.Visibility = Visibility.Collapsed;
                SpeechText.Text = string.Empty;
                timer.Stop();
            };
            timer.Start();

            // 返答中のコードブロックをVSCodeに送信
            await SendCodeBlocksToVSCode(reply);
            await ProcessTTS(filteredReply);
        }


        private async Task<string> ProcessChat(string text)
        {
            string url = "http://127.0.0.1:1234/v1/chat/completions";
            var req = new
            {
                model = "llama-3-elyza-jp-8b",
                messages = new[]
                {
                    new { role = "system", content = "あなたはユーザーの妹です。ユーザーからの文章を、かわいい妹のような口調で返信して。プログラミングを頼まれた場合は、pythonで書いて。簡潔に話して。" },
                    new { role = "user", content = text }
                },
                temperature = 0.7,
                max_tokens = -1,
                stream = false
            };
            string json = JsonConvert.SerializeObject(req);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                var responseData = await response.Content.ReadAsStringAsync();
                dynamic jsonResponse = JsonConvert.DeserializeObject(responseData);
                return jsonResponse.choices[0].message.content;
            }
            return "エラーが発生しました";
        }

        // TTS用テキストからコードブロック部分を除去する
        private string RemoveCodeBlocks(string text)
        {
            return Regex.Replace(text, @"```[\s\S]*?```", "");
        }
        private async Task ProcessTTS(string text)
        {
            string filteredText = RemoveCodeBlocks(text);
            // 音声合成クエリの作成
            string queryUrl = $"http://localhost:50021/audio_query?text={Uri.EscapeDataString(filteredText)}&speaker=58";
            var queryResponse = await httpClient.PostAsync(queryUrl, null);
            if (!queryResponse.IsSuccessStatusCode)
            {
                MessageBox.Show("音声合成クエリの作成に失敗しました。");
                return;
            }
            var queryData = await queryResponse.Content.ReadAsStringAsync();

            // 音声合成
            string synthesisUrl = "http://localhost:50021/synthesis?speaker=58";
            var content = new StringContent(queryData, Encoding.UTF8, "application/json");
            var synthesisResponse = await httpClient.PostAsync(synthesisUrl, content);
            if (!synthesisResponse.IsSuccessStatusCode)
            {
                MessageBox.Show("音声合成に失敗しました。");
                return;
            }

            // 音声ファイルの保存と再生
            var audioStream = await synthesisResponse.Content.ReadAsStreamAsync();
            PlayAudio(audioStream);
        }

        // 返答テキスト中のコードブロックを抽出し、VSCodeに送信する
        private async Task SendCodeBlocksToVSCode(string text)
        {
            var codeBlockPattern = new Regex(@"```(\w+)?\n([\s\S]*?)```", RegexOptions.Multiline);
            var matches = codeBlockPattern.Matches(text);
            foreach (Match match in matches)
            {
                string code = match.Groups[2].Value;
                await VscodeIntegration.SendCodeToVscode(code);
            }
        }

        private void PlayAudio(Stream audioStream)
        {
            MediaPlayer player = new MediaPlayer();
            string tempFile = System.IO.Path.GetTempFileName() + ".wav";
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                audioStream.CopyTo(fileStream);
            }
            player.Open(new Uri(tempFile, UriKind.Absolute));
            player.Play();
            player.MediaEnded += (s, e) => player.Close();
            Task.Run(async () => {
                await Task.Delay(5000);
                try { System.IO.File.Delete(tempFile); } catch { }
            });
        }

        private void StartRecognition(object sender, ExecutedRoutedEventArgs e)
        {
            Overlay.Visibility = Visibility.Visible;
            RecordingText.Visibility = Visibility.Visible;
            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            this.MouseLeftButtonDown += StopRecognition;
        }

        private void StopRecognition(object sender, MouseButtonEventArgs e)
        {
            recognizer.RecognizeAsyncStop();
            Overlay.Visibility = Visibility.Collapsed;
            RecordingText.Visibility = Visibility.Collapsed;
            this.MouseLeftButtonDown -= StopRecognition;
        }

        private async void Recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            string recognizedText = e.Result.Text;
            chatHistory.Add("ユーザー: " + recognizedText);
            string reply = await ProcessChat(recognizedText);
            chatHistory.Add("彼女: " + reply);
            string filteredReply = RemoveCodeBlocks(reply);
            SpeechText.Text = filteredReply;
            SpeechBubble.Visibility = Visibility.Visible;

            // 文章の長さに応じて表示時間を計算
            int displayTime = CalculateDisplayTime(filteredReply);

            // 計算された時間後に吹き出しをクリアして非表示にする
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayTime) };
            timer.Tick += (s, args) =>
            {
                SpeechBubble.Visibility = Visibility.Collapsed;
                SpeechText.Text = string.Empty;
                timer.Stop();
            };
            timer.Start();

            // 返答中のコードブロックをVSCodeに送信
            await SendCodeBlocksToVSCode(reply);
            await ProcessTTS(filteredReply);
        }
        private int CalculateDisplayTime(string text)
        {
            // 1文字あたり0.1秒とし、最低でも8秒表示する
            int baseTime = 8;
            int additionalTime = (int)(text.Length * 0.2);
            return Math.Max(baseTime, additionalTime);
        }
        private void SpeechTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}