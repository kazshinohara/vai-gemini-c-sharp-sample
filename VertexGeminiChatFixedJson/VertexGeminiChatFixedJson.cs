using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

// 会話メッセージを表すクラス
class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? FileUri { get; set; } = null; // ファイル（PDFなど）のGCSパス
    public string? FileMimeType { get; set; } = null; // ファイルのMIMEタイプ
    public bool HasFile => !string.IsNullOrEmpty(FileUri) && !string.IsNullOrEmpty(FileMimeType);
}

// レスポンスモデルを表すクラス
class ModelResponse
{
    public string Response { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Information { get; set; } = string.Empty;
}

class VertexGeminiChat
{
    // APIの設定
    private static readonly string ProjectId = "kzs-lab1"; // Google Cloud Project ID
    private static readonly string Location = "us-central1"; // Vertex AI APIのロケーション
    private static readonly string Model = "gemini-2.0-flash-001"; // 使用するモデル名
    private static readonly string Endpoint = $"https://{Location}-aiplatform.googleapis.com/v1/projects/{ProjectId}/locations/{Location}/publishers/google/models/{Model}:generateContent";
    
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
            Console.Write("\nエージェント: ");
            var modelResponse = await SendRequestAsync(httpClient, chatHistory, token);
            
            // モデルの応答を表示
            Console.WriteLine(modelResponse.Response);
            Console.WriteLine($"[理由]: {modelResponse.Reason}");
            Console.WriteLine($"[情報源]: {modelResponse.Information}");
            
            // モデルの応答をチャット履歴に追加（JSONレスポンスからResponseフィールドの内容を使用）
            chatHistory.Add(new ChatMessage { Role = "model", Content = modelResponse.Response });
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
    
    // 非ストリーミングリクエストを送信する
    private static async Task<ModelResponse> SendRequestAsync(HttpClient client, List<ChatMessage> chatHistory, string token)
    {
        // HTTPクライアントの設定
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        // チャット履歴からコンテンツを作成
        var contents = CreateContentsFromChatHistory(chatHistory);
        
        // レスポンススキーマを定義（JSON　オブジェクトとして）
        var responseSchema = new 
        {
            type = "object",
            properties = new 
            {
                Response = new 
                {
                    type = "string",
                    description = "Gemini からの返信"
                },
                Reason = new 
                {
                    type = "string",
                    description = "返信の内容を生成した理由"
                },
                Information = new 
                {
                    type = "string",
                    description = "返信するのに使った情報源（プロンプト、学習データなど）"
                }
            },
            required = new[] { "Response", "Reason", "Information" }
        };
        
        // 生成設定を作成
        var generationConfig = new {
            temperature = Temperature,
            topP = TopP,
            responseMimeType = "application/json",
            responseSchema = responseSchema
        };
        
        // リクエストボディを構築
        var requestBody = new { 
            contents, 
            generationConfig
        };

        // リクエストを作成
        var jsonContent = JsonSerializer.Serialize(requestBody);
        
        var content = new StringContent(
            jsonContent,
            Encoding.UTF8,
            "application/json");
            
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = content
        };

        try
        {
            // 非ストリーミングレスポンスを受け取る
            var response = await client.SendAsync(request);
                
            // エラーが発生した場合、詳細を表示
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"エラーレスポンス: {errorContent}");
                throw new HttpRequestException($"APIエラー: {response.StatusCode}, 詳細: {errorContent}");
            }
            
            // レスポンスを直接読み込む
            var responseContent = await response.Content.ReadAsStringAsync();
            
            // レスポンスからJSONデータを抽出
            return ParseResponseJson(responseContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nAPIリクエストエラー: {ex.Message}");
            
            // エラー時はデフォルト値を返す
            return new ModelResponse
            {
                Response = "APIリクエストエラーが発生しました",
                Reason = "APIリクエスト処理中のエラー",
                Information = ex.Message
            };
        }
    }

    // レスポンスJSONからModelResponseオブジェクトを作成
    private static ModelResponse ParseResponseJson(string responseJson)
    {
        // レスポンスJSONオブジェクトをパース
        using JsonDocument doc = JsonDocument.Parse(responseJson);
        JsonElement root = doc.RootElement;

        // candidatesから　　text を抽出
        if (!root.TryGetProperty("candidates", out JsonElement candidates) || 
            candidates.GetArrayLength() == 0 ||
            !candidates[0].TryGetProperty("content", out JsonElement content) ||
            !content.TryGetProperty("parts", out JsonElement parts) ||
            parts.GetArrayLength() == 0 ||
            !parts[0].TryGetProperty("text", out JsonElement text))
        {
            return new ModelResponse
            {
                Response = "レスポンスから応答テキストを抽出できませんでした",
                Reason = "レスポンス構造が予期しない形式",
                Information = "API応答の構造解析に失敗"
            };
        }

        // textのコンテンツを取得
        string textContent = text.GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(textContent))
        {
            return new ModelResponse
            {
                Response = "空の応答が返されました",
                Reason = "テキスト内容が空",
                Information = "N/A"
            };
        }

        // textコンテンツをJSON　としてパース
        return ParseTextContentAsJson(textContent);
    }

    // textのコンテンツをJSONとしてパースする
    private static ModelResponse ParseTextContentAsJson(string textContent)
    {
        try
        {
            JsonNode? jsonNode = JsonNode.Parse(textContent);
            if (jsonNode != null &&
                jsonNode["Response"] != null &&
                jsonNode["Reason"] != null &&
                jsonNode["Information"] != null)
            {
                return new ModelResponse
                {
                    Response = jsonNode["Response"]!.GetValue<string>(),
                    Reason = jsonNode["Reason"]!.GetValue<string>(),
                    Information = jsonNode["Information"]!.GetValue<string>()
                };
            }
            
            // JSON形式だが必要なプロパティがない場合
            return new ModelResponse
            {
                Response = textContent,
                Reason = "必要なJSONプロパティがありません",
                Information = "N/A"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"テキストコンテンツ JSON パースエラー: {ex.Message}");
            // JSONとしてパースできない場合は、textのコンテンツ全体を返す
            return new ModelResponse
            {
                Response = textContent,
                Reason = "テキストコンテンツがJSON形式ではありませんでした",
                Information = "N/A"
            };
        }
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
