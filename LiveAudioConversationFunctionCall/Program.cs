public class Program
{
  public static async Task Main(string[] args)
  {
    bool useVertexAI = true; // Default to Vertex AI
    string? resumptionHandle = null;
    string? systemInstruction = null;
    string defaultSystemInstruction = "あなたは親切で知的なアシスタントです。ユーザーの質問に簡潔に答えてください（1〜2文で）。現在の日時や時刻が必要な場合は、必ずgetCurrentDateTime関数を使用して正確な時刻を取得してください。天気を聞かれた場合はgetWeatherInfo関数を使用してください。関数を使った結果を自然な日本語で説明してください。";

    for (int i = 0; i < args.Length; i++)
    {
      switch (args[i].ToLower())
      {
        case "--vertexai":
          if (i + 1 < args.Length)
          {
            useVertexAI = bool.Parse(args[++i]);
          }
          else
          {
            Console.WriteLine("Error: --vertexai flag requires a value (true/false).");
            return;
          }
          break;
        case "--resumptionhandle":
          if (i + 1 < args.Length)
          {
            resumptionHandle = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --resumptionHandle flag requires a value.");
            return;
          }
          break;
        case "--systeminstruction":
          if (i + 1 < args.Length)
          {
            systemInstruction = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --systemInstruction flag requires a value.");
            return;
          }
          break;
        case "--help":
        case "-h":
          PrintHelp();
          return;
      }
    }

    string apiType = useVertexAI ? "Vertex AI" : "Gemini API";
    Console.WriteLine($"API Type: {apiType}");

    if (resumptionHandle != null)
    {
      Console.WriteLine($"Resumption Handle: {resumptionHandle}");
    }
    else
    {
      Console.WriteLine("No resumption handle provided. Starting a new session.");
    }

    if (systemInstruction != null)
    {
      Console.WriteLine($"System Instruction: {systemInstruction}");
    }
    else
    {
      Console.WriteLine($"Default System Instruction: {defaultSystemInstruction}");
      systemInstruction = defaultSystemInstruction;
    }

    Console.WriteLine("\n=== Live Audio Conversation with Function Calling ===");
    Console.WriteLine("このデモでは、以下の関数が利用可能です：");
    Console.WriteLine("- getCurrentDateTime: 現在の日時を取得");
    Console.WriteLine("- getWeatherInfo: 天気情報を取得（模擬データ）");
    Console.WriteLine("\n試してみてください：");
    Console.WriteLine("- 「今何時ですか？」");
    Console.WriteLine("- 「今日の日付を教えて」");
    Console.WriteLine("- 「東京の天気はどうですか？」");
    Console.WriteLine("==============================\n");

    await LiveAudioConversationRealtimeInput.RunConversation(
        systemInstruction,
        resumptionHandle,
        useVertexAI);
  }

  private static void PrintHelp()
  {
    Console.WriteLine("Live Audio Conversation with Function Calling Demo");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --vertexai <true|false>          Use Vertex AI (true) or Gemini API (false). Default: true");
    Console.WriteLine("  --resumptionHandle <handle>      Resume a previous session with the given handle");
    Console.WriteLine("  --systemInstruction <text>       Custom system instruction for the model");
    Console.WriteLine("  --help, -h                       Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("  For Vertex AI (default):");
    Console.WriteLine("    GOOGLE_CLOUD_PROJECT           Your GCP project ID (required)");
    Console.WriteLine("    GOOGLE_CLOUD_LOCATION          GCP location (optional, default: us-central1)");
    Console.WriteLine("    Requires: gcloud auth application-default login");
    Console.WriteLine();
    Console.WriteLine("  For Gemini API:");
    Console.WriteLine("    GOOGLE_API_KEY                 Your Gemini API key (required)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  # Use Vertex AI (default)");
    Console.WriteLine("  dotnet run");
    Console.WriteLine();
    Console.WriteLine("  # Use Gemini API");
    Console.WriteLine("  dotnet run -- --vertexai false");
    Console.WriteLine();
    Console.WriteLine("  # Resume a previous session");
    Console.WriteLine("  dotnet run -- --resumptionHandle \"your-handle-here\"");
    Console.WriteLine();
    Console.WriteLine("  # Custom system instruction");
    Console.WriteLine("  dotnet run -- --systemInstruction \"You are a helpful assistant.\"");
  }
}
