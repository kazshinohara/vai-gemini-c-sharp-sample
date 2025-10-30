public class Program
{
  public static async Task Main(string[] args)
  {
    string? resumptionHandle = null;
    string? systemInstruction = null;
    string? systemInstructionFile = null;
    string? knowledgeCorpusId = null;
    string? memoryCorpusId = null;
    string defaultSystemInstruction = "あなたは親切で知的なアシスタントです。時刻情報と天気についてユーザーからの質問へ回答します。尚、現在の日時や時刻が必要な場合は、必ずgetCurrentDateTime関数を使用して正確な時刻を取得してください。天気を聞かれた場合はgetWeatherInfo関数を使用してください。関数を使った結果を自然な日本語で説明してください。";

    for (int i = 0; i < args.Length; i++)
    {
      switch (args[i].ToLower())
      {
        case "--resumption-handle":
          if (i + 1 < args.Length)
          {
            resumptionHandle = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --resumption-handle flag requires a value.");
            return;
          }
          break;
        case "--system-instruction":
          if (i + 1 < args.Length)
          {
            systemInstruction = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --system-instruction flag requires a value.");
            return;
          }
          break;
        case "--system-instruction-file":
          if (i + 1 < args.Length)
          {
            systemInstructionFile = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --system-instruction-file flag requires a value.");
            return;
          }
          break;
        case "--knowledge-corpus":
          if (i + 1 < args.Length)
          {
            knowledgeCorpusId = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --knowledge-corpus flag requires a value.");
            return;
          }
          break;
        case "--memory-corpus":
          if (i + 1 < args.Length)
          {
            memoryCorpusId = args[++i];
          }
          else
          {
            Console.WriteLine("Error: --memory-corpus flag requires a value.");
            return;
          }
          break;
        case "--help":
        case "-h":
          PrintHelp();
          return;
      }
    }

    Console.WriteLine($"API Type: Vertex AI");

    if (resumptionHandle != null)
    {
      Console.WriteLine($"Resumption Handle: {resumptionHandle}");
    }
    else
    {
      Console.WriteLine("No resumption handle provided. Starting a new session.");
    }

    if (!string.IsNullOrEmpty(systemInstructionFile))
    {
        try
        {
            systemInstruction = File.ReadAllText(systemInstructionFile);
            Console.WriteLine($"System Instruction from file: {systemInstructionFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading system instruction file: {ex.Message}");
            return;
        }
    }

    if (systemInstruction != null)
    {
      Console.WriteLine($"System Instruction:\n {systemInstruction}");
    }
    else
    {
      Console.WriteLine($"Default System Instruction: {defaultSystemInstruction}");
      systemInstruction = defaultSystemInstruction;
    }

    if (knowledgeCorpusId != null)
    {
        Console.WriteLine($"Knowledge Corpus ID: {knowledgeCorpusId}");
    }

    if (memoryCorpusId != null)
    {
        Console.WriteLine($"Memory Corpus ID: {memoryCorpusId}");
    }

    Console.WriteLine("\n=== Live Audio Conversation with RAG ===");

    await LiveAudioConversationRAG.RunConversation(
        systemInstruction,
        resumptionHandle,
        knowledgeCorpusId,
        memoryCorpusId);
  }

  private static void PrintHelp()
  {
    Console.WriteLine("Live Audio Conversation with RAG Demo");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --resumption-handle <handle>      Resume a previous session with the given handle");
    Console.WriteLine("  --system-instruction <text>       Custom system instruction for the model");
    Console.WriteLine("  --system-instruction-file <path> Read system instruction from a file");
    Console.WriteLine("  --knowledge-corpus <corpus_id>    Use RAG for grounding with the given knowledge corpus ID");
    Console.WriteLine("  --memory-corpus <corpus_id>      Use RAG for context memory with the given memory corpus ID");
    Console.WriteLine("  --help, -h                       Show this help message");
    Console.WriteLine();
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("    GOOGLE_CLOUD_PROJECT           Your GCP project ID (required)");
    Console.WriteLine("    GOOGLE_CLOUD_LOCATION          GCP location (optional, default: us-central1)");
    Console.WriteLine("    Requires: gcloud auth application-default login");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  # Run the demo");
    Console.WriteLine("  dotnet run");
    Console.WriteLine();
    Console.WriteLine("  # Use RAG for grounding with a knowledge corpus");
    Console.WriteLine("  dotnet run -- --knowledge-corpus \"corpora/your-corpus-id\"");
    Console.WriteLine();
    Console.WriteLine("  # Use RAG for context memory with a memory corpus");
    Console.WriteLine("  dotnet run -- --memory-corpus \"corpora/your-corpus-id\"");
    Console.WriteLine();
    Console.WriteLine("  # Use system instruction from a file");
    Console.WriteLine("  dotnet run -- --system-instruction-file \"path/to/instruction.txt\"");
  }
}