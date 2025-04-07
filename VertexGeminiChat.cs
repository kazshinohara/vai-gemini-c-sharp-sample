using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Headers;

// 会話メッセージを表すクラス
class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? FileUri { get; set; } = null; // ファイル（PDFなど）のGCSパス
    public string? FileMimeType { get; set; } = null; // ファイルのMIMEタイプ
    public bool HasFile => !string.IsNullOrEmpty(FileUri) && !string.IsNullOrEmpty(FileMimeType);
}

class VertexGeminiChat
{
    // APIの設定
    private static readonly string ProjectId = "kzs-lab1"; // Google Cloud Project ID
    private static readonly string Location = "us-central1"; // Vertex AI APIのロケーション
    private static readonly string Model = "gemini-2.0-flash-001"; // 使用するモデル名
    private static readonly string Endpoint = $"https://{Location}-aiplatform.googleapis.com/v1/projects/{ProjectId}/locations/{Location}/publishers/google/models/{Model}:streamGenerateContent";
    
    // チャット設定
    private static readonly int MaxHistorySize = 10; // 保存する会話履歴の最大数
    
    // 生成パラメータの設定
    private static readonly float Temperature = 1.0f; // 値が大きいほど多様な応答（0.0-2.0）
    private static readonly float TopP = 0.95f; // 確率質量の上位P%のトークンのみを考慮（0.0-1.0）
    
    // システムインストラクション（テキスト部分）
    private static readonly string SystemInstructionText = @"あなたは上野動物園の道案内エージェントです。
以下のルールに従って回答してください：
1. 添付している PDF ファイル「ueno_zoo_map.pdf」に記載されている内容のみを参考に回答を生成してください
2. 道順を聞かれた場合は、目印となる施設の名前を省略せずに宛先までの最短の道順を案内してください
3. 西園と東園の間は「いそっぷ橋」を通るルートを案内してください。
4. 地図上にある動物園通りは動物園の外です、ルートとしては使用することを避けてください
5. 質問には具体的かつ丁寧に回答してください
6. 不確かな情報は「〜かもしれません」と表現してください
7. 絵文字の使用は控えてください";

    // システムインストラクション用のPDFファイルのGCSパス
    private static readonly string SystemInstructionPdfUri = "gs://kzs-ml-test2/ueno_zoo_map.pdf";
    private static readonly string SystemInstructionPdfMimeType = "application/pdf";

    static async Task Main(string[] args)
    {
        try
        {
            // 環境変数からトークンを取得
            var token = Environment.GetEnvironmentVariable("GOOGLE_ACCESS_TOKEN");
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("環境変数 GOOGLE_ACCESS_TOKEN が設定されていません。");
            }
            
            using var httpClient = new HttpClient();
            var chatHistory = new List<ChatMessage>();
            
            // システムメッセージを初期化
            InitializeSystemMessage(chatHistory);
            
            Console.WriteLine("Gemini Chat Sample (終了するには 'exit' と入力してください)");
            Console.WriteLine("----------------------------------------");

            // メインチャットループ
            await RunChatLoop(httpClient, chatHistory, token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラーが発生しました: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"詳細: {ex.InnerException.Message}");
            }
        }
    }

    // システムメッセージを初期化
    private static void InitializeSystemMessage(List<ChatMessage> chatHistory)
    {
        chatHistory.Add(new ChatMessage { 
            Role = "user", 
            Content = SystemInstructionText,
            FileUri = SystemInstructionPdfUri,
            FileMimeType = SystemInstructionPdfMimeType
        });
    }

    // メインのチャットループを実行
    private static async Task RunChatLoop(HttpClient httpClient, List<ChatMessage> chatHistory, string token)
    {
        while (true)
        {
            Console.Write("\nユーザー: ");
            var userInput = Console.ReadLine();

            if (string.IsNullOrEmpty(userInput) || userInput.ToLower() == "exit")
                break;

            // ユーザーの入力をチャット履歴に追加
            chatHistory.Add(new ChatMessage { Role = "user", Content = userInput });
            
            // 履歴数が最大数を超えないように調整
            TrimChatHistory(chatHistory);

            // リクエストの作成と送信
            var modelResponse = await SendRequestStreamingAsync(httpClient, chatHistory, token);
            
            // モデルの応答をチャット履歴に追加
            chatHistory.Add(new ChatMessage { Role = "model", Content = modelResponse });
        }
    }

    // チャット履歴を適切なサイズに調整
    private static void TrimChatHistory(List<ChatMessage> chatHistory)
    {
        if (chatHistory.Count > MaxHistorySize * 2 + 1) // システムメッセージ + ユーザーとモデルのペア
        {
            // システムメッセージを除いた古い会話を削除（ユーザーとモデルのペアを維持するため2つずつ削除）
            chatHistory.RemoveRange(1, 2);
        }
    }
    
    // ストリーミングリクエストを送信する
    private static async Task<string> SendRequestStreamingAsync(HttpClient client, List<ChatMessage> chatHistory, string token)
    {
        // HTTPクライアントの設定
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        // チャット履歴からコンテンツを作成
        var contents = CreateContentsFromChatHistory(chatHistory);
        
        // 生成設定を作成
        var generationConfig = new {
            temperature = Temperature,
            topP = TopP
        };
        
        // リクエストボディを構築
        var requestBody = new { 
            contents, 
            generationConfig 
        };

        // リクエストを作成
        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
            
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = content
        };

        // ストリーミングレスポンスを受け取る（サーバーがデータを送信し始めたらすぐにレスポンスを返す）
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
            
        response.EnsureSuccessStatusCode();
        
        // レスポンスのストリームを直接処理（ストリームが閉じられるまで待機せずに処理）
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        
        Console.Write("\nエージェント: ");
        var fullResponse = new StringBuilder();
        
        // チャンクが届くたびにリアルタイムで処理
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            
            try
            {
                // 各行（チャンク）からテキストを抽出して表示
                var textMatches = Regex.Matches(line, @"""text""\s*:\s*""([^""\\]*(\\.[^""\\]*)*)""");
                foreach (Match match in textMatches)
                {
                    if (match.Groups.Count > 1)
                    {
                        string text = match.Groups[1].Value;
                        // エスケープシーケンスを処理
                        text = text.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
                        Console.Write(text); // リアルタイムで出力
                        fullResponse.Append(text);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nチャンク処理エラー: {ex.Message}");
            }
        }
        
        Console.WriteLine();
        return fullResponse.ToString();
    }

    // チャット履歴からAPIリクエスト用のコンテンツを作成
    private static IEnumerable<object> CreateContentsFromChatHistory(List<ChatMessage> chatHistory)
    {
        return chatHistory.Select(msg => {
            // パーツのリスト（テキストは常に含まれる）
            var parts = new List<object> { new { text = msg.Content } };
            
            // ファイルパートがある場合は追加
            if (msg.HasFile)
            {
                parts.Add(new {
                    fileData = new {
                        mimeType = msg.FileMimeType,
                        fileUri = msg.FileUri
                    }
                });
            }
            
            // ロールとパーツを含むメッセージオブジェクトを返す
            return new {
                role = msg.Role,
                parts = parts.ToArray()
            };
        }).ToList<object>();
    }
}
