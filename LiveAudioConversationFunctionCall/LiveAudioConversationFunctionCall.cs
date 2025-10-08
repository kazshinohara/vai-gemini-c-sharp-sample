using System.Collections.Concurrent;
using System.Text.Json;

using Google.GenAI;

using NAudio.Wave;

using Types = Google.GenAI.Types;

public static class LiveAudioConversationRealtimeInput
{
  private static Task? audioStreamingTask;
  private static Task? responseProcessingTask;

  private static AsyncSession? session;

  private static BlockingCollection<byte[]>? audioQueue;
  private static CancellationTokenSource? audioCaptureCts;
  private static int microphoneDeviceNumber;
  private static int speakerDeviceNumber;

  // NAudio fields
  private static WaveInEvent? waveIn;
  private static WaveOutEvent? waveOut;
  private static BufferedWaveProvider? bufferedWaveProvider;
  private static string audioResponseOutputPath =
      Path.GetFullPath("LiveAudioConversationFunctionCallResponse.json");
  private static string resumptionHandleOutputPath =
      Path.GetFullPath("LiveAudioConversationFunctionCallResumptionHandle.txt");

  private const int SAMPLE_RATE = 16000;
  private const int OUTPUT_SAMPLE_RATE = 24000;
  private const int CHANNELS = 1;
  private const int BITS_PER_SAMPLE = 16;
  private const int BUFFER_MILLISECONDS = 32;  // ~512 frames at 16kHz

  // Function calling helper methods
  private static string GetCurrentDateTime()
  {
    var now = DateTime.Now;
    return $"{now:yyyy年MM月dd日 HH時mm分ss秒} ({now:dddd})";
  }

  private static string GetWeatherInfo(string? location = null)
  {
    // 模擬的な天気データを返す
    var locations = new Dictionary<string, string>
    {
      ["東京"] = "晴れ、気温25℃、湿度60%",
      ["大阪"] = "曇り、気温23℃、湿度65%",
      ["名古屋"] = "雨、気温20℃、湿度80%",
      ["福岡"] = "晴れ、気温28℃、湿度55%"
    };

    string targetLocation = location ?? "東京";

    // 部分一致で場所を検索
    var foundLocation = locations.Keys.FirstOrDefault(k => k.Contains(targetLocation) || targetLocation.Contains(k));

    if (foundLocation != null)
    {
      return $"{foundLocation}の天気: {locations[foundLocation]}";
    }
    else
    {
      return $"{targetLocation}の天気情報は利用できませんが、一般的に今日は穏やかな天気です。";
    }
  }

  private static async Task ExecuteFunctionCall(AsyncSession session, string functionName,
                                             string functionId, object? args,
                                             CancellationToken cancellationToken)
  {
    string result = "";

    Console.WriteLine($"[Function Call Start] Executing {functionName} with ID: {functionId}");

    try
    {
      switch (functionName)
      {
        case "getCurrentDateTime":
          result = GetCurrentDateTime();
          Console.WriteLine($"[Function Call] {functionName}: {result}");
          break;

        case "getWeatherInfo":
          string? location = null;

          // argsからlocationパラメータを抽出
          if (args != null)
          {
            Console.WriteLine($"[Function Call] Args received: {args}");
            if (args is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
              if (jsonElement.TryGetProperty("location", out var locationProp))
              {
                location = locationProp.GetString();
              }
            }
            else if (args is Dictionary<string, object> argsDict)
            {
              if (argsDict.TryGetValue("location", out var locationObj))
              {
                location = locationObj?.ToString();
              }
            }
          }

          result = GetWeatherInfo(location);
          Console.WriteLine($"[Function Call] {functionName}(location: {location ?? "null"}): {result}");
          break;

        default:
          result = $"Unknown function: {functionName}";
          Console.WriteLine($"[Function Call Error] {result}");
          break;
      }
    }
    catch (Exception ex)
    {
      result = $"Function execution error: {ex.Message}";
      Console.WriteLine($"[Function Call Exception] {functionName}: {ex.Message}");
      Console.WriteLine($"[Function Call Exception Stack Trace] {ex.StackTrace}");
    }

    try
    {
      Console.WriteLine($"[Function Response] Preparing response for {functionName}");

      // Send the function response back to the model
      var functionResponse = new Types.FunctionResponse
      {
        Name = functionName,
        Id = functionId,
        Response = new Dictionary<string, object>
        {
          ["content"] = result
        }
      };

      Console.WriteLine($"[Function Response] Creating response with ID: {functionId}, content: {result}");

      var toolResponseParams = new Types.LiveSendToolResponseParameters
      {
        FunctionResponses = new List<Types.FunctionResponse> { functionResponse }
      };

      Console.WriteLine($"[Function Response] Sending response to server...");
      await session.SendToolResponseAsync(toolResponseParams, cancellationToken);
      Console.WriteLine($"[Function Response] Response sent successfully for {functionName}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"[Function Response Error] Failed to send response for {functionName}: {ex.Message}");
      Console.WriteLine($"[Function Response Error Stack Trace] {ex.StackTrace}");
    }
  }

  public static async Task RunConversation(string systemInstructionString,
                                           string? resumptionHandle = null,
                                           bool useVertexAI = true)
  {
    // Create cancellation token source for graceful shutdown
    audioCaptureCts = new CancellationTokenSource();

    // Handle Ctrl+C for graceful shutdown
    Console.CancelKeyPress += (sender, e) =>
    {
      e.Cancel = true; // Prevent immediate termination
      Console.WriteLine("\nShutting down gracefully...");
      audioCaptureCts?.Cancel();
    };

    Client client;
    string model;

    if (useVertexAI)
    {
      // Vertex AI with ADC authentication
      string project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? string.Empty;
      if (string.IsNullOrEmpty(project))
        throw new InvalidOperationException(
            "Project ID is not set in the environment variable GOOGLE_CLOUD_PROJECT.");

      string location = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_LOCATION") ?? "us-central1";

      client = new Client(project: project, location: location, vertexAI: true);
      model = "gemini-live-2.5-flash-preview-native-audio-09-2025";
    }
    else
    {
      // Gemini API with API key
      string apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ?? string.Empty;
      if (string.IsNullOrEmpty(apiKey))
        throw new InvalidOperationException(
            "API key is not set in the environment variable GOOGLE_API_KEY.");

      client = new Client(apiKey: apiKey);
      model = "gemini-2.0-flash-live-001";
    }

    string voiceName = "Leda";
    string languageCode = "ja-JP";

    // Define function calling tools
    var getCurrentDateTimeFunctionDeclaration = new Types.FunctionDeclaration
    {
      Name = "getCurrentDateTime",
      Description = "現在の日時を正確に取得する関数です。年月日、時分秒、曜日を日本語で返します。時刻や日付に関する質問があった場合に使用してください。",
      Parameters = new Types.Schema
      {
        Type = Types.Type.OBJECT,
        Properties = new Dictionary<string, Types.Schema>(),
        Required = new List<string>()
      }
    };

    var getWeatherInfoFunctionDeclaration = new Types.FunctionDeclaration
    {
      Name = "getWeatherInfo",
      Description = "指定された場所の天気情報を取得する関数です。場所が指定されない場合は東京の天気を返します。",
      Parameters = new Types.Schema
      {
        Type = Types.Type.OBJECT,
        Properties = new Dictionary<string, Types.Schema>
        {
          ["location"] = new Types.Schema
          {
            Type = Types.Type.STRING,
            Description = "天気情報を取得したい場所の名前（例：東京、大阪、名古屋）"
          }
        },
        Required = new List<string>()
      }
    };

    var tools = new List<Types.Tool>
    {
      new Types.Tool
      {
        FunctionDeclarations = new List<Types.FunctionDeclaration>
        {
          getCurrentDateTimeFunctionDeclaration,
          getWeatherInfoFunctionDeclaration
        }
      }
    };

    var config = new Types.LiveConnectConfig
    {
      SessionResumption =
          new Types.SessionResumptionConfig { Transparent = true, Handle = resumptionHandle },
      ResponseModalities = new List<Types.Modality> { Types.Modality.AUDIO },
      SpeechConfig =
          new Types.SpeechConfig
          {
            VoiceConfig =
                new Types.VoiceConfig
                {
                  PrebuiltVoiceConfig =
                      new Types.PrebuiltVoiceConfig { VoiceName = voiceName }
                },
            LanguageCode = languageCode
          },
      InputAudioTranscription = new Types.AudioTranscriptionConfig { },
      RealtimeInputConfig =
          new Types.RealtimeInputConfig
          {
            AutomaticActivityDetection =
                new Types.AutomaticActivityDetection
                {
                  SilenceDurationMs = 10,
                  Disabled = false,
                  StartOfSpeechSensitivity = Types.StartSensitivity.START_SENSITIVITY_HIGH,
                  EndOfSpeechSensitivity = Types.EndSensitivity.END_SENSITIVITY_HIGH,
                  PrefixPaddingMs = 10,
                }
          },
      SystemInstruction = new Types.Content
      {
        Parts = new List<Types.Part> { new Types.Part { Text = systemInstructionString } },
        Role = "user"
      },
      Tools = tools,
      Temperature = 0.5f,
      TopP = 0.9f,
      TopK = 40,
    };

    microphoneDeviceNumber = 0;  // default microphone
    speakerDeviceNumber = -1;    // default speaker

    audioQueue = new BlockingCollection<byte[]>();

    try
    {
      Console.WriteLine("Connecting to Gemini Live API...");
      session = await client.Live.ConnectAsync(model, config, audioCaptureCts.Token);
      Console.WriteLine("Connected.");

      Console.WriteLine("Starting NAudio capture...");
      StartAudioCapture(audioQueue, audioCaptureCts.Token);

      Console.WriteLine("Starting audio streaming and response processing tasks...");
      audioStreamingTask = Task.Run(
          () => StreamInputAudioFromQueueAsync(session, audioQueue, audioCaptureCts.Token),
          audioCaptureCts.Token);
      responseProcessingTask =
          Task.Run(() => ProcessResponseAudioAsync(session, audioCaptureCts.Token),
                   audioCaptureCts.Token);

      Console.WriteLine("Audio streaming started. Press Ctrl+C to exit...");
      await Task.WhenAll(audioStreamingTask, responseProcessingTask);
    }
    catch (OperationCanceledException)
    {
      Console.WriteLine("Operation cancelled.");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"An error occurred: {ex}");
    }
    finally
    {
      Console.WriteLine("Shutting down...");
      if (audioCaptureCts != null && !audioCaptureCts.IsCancellationRequested)
        audioCaptureCts.Cancel();
      StopAudioCapture();
      audioQueue?.Dispose();
      audioCaptureCts?.Dispose();
      Console.WriteLine("Shutdown complete.");
      if (session != null)
      {
        await session.DisposeAsync();
        Console.WriteLine("Live API session closed.");
      }
    }
  }

  private static async Task StreamInputAudioFromQueueAsync(AsyncSession activeSession,
                                                           BlockingCollection<byte[]> queue,
                                                           CancellationToken cancellationToken)
  {
    Console.WriteLine("Audio streaming task started, waiting for data...");
    try
    {
      foreach (var chunk in queue.GetConsumingEnumerable(cancellationToken))
      {
        if (chunk == null || chunk.Length == 0)
          continue;
        var realtimeInput = new Types.LiveSendRealtimeInputParameters
        {
          Media = new Types.Blob { Data = chunk, MimeType = $"audio/l16;rate={SAMPLE_RATE}" },
        };
        await activeSession.SendRealtimeInputAsync(realtimeInput, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      Console.WriteLine("Audio streaming task cancelled.");
    }
    catch (InvalidOperationException)
    {
      Console.WriteLine("Audio streaming queue completed.");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error in audio streaming task: {ex}");
    }
    finally
    {
      Console.WriteLine("Audio streaming task finished.");
    }
  }

  private static async Task ProcessResponseAudioAsync(AsyncSession activeSession,
                                                      CancellationToken cancellationToken)
  {
    Console.WriteLine("Response processing task started.");
    try
    {
      InitPlayback();
      while (!cancellationToken.IsCancellationRequested)
      {
        try
        {
          // Use nullable return - null means connection closed
          var serverMessage = await activeSession.ReceiveAsync(cancellationToken);

          if (serverMessage == null)
          {
            Console.WriteLine("Connection closed by server.");
            break;
          }

          if (serverMessage.SessionResumptionUpdate?.NewHandle != null)
          {
            string newHandle = serverMessage.SessionResumptionUpdate.NewHandle;
            await System.IO.File.WriteAllTextAsync(resumptionHandleOutputPath, newHandle, cancellationToken);
          }

          string responseJson = System.Text.Json.JsonSerializer.Serialize(
              serverMessage, System.Text.Json.JsonSerializerOptions.Default);

          await System.IO.File.AppendAllTextAsync(audioResponseOutputPath, responseJson + "\n\n", cancellationToken);

          if (cancellationToken.IsCancellationRequested)
            break;

          // Handle function calls
          if (serverMessage.ToolCall?.FunctionCalls != null)
          {
            Console.WriteLine($"[Function Call Detected] Found {serverMessage.ToolCall.FunctionCalls.Count} function calls");
            foreach (var functionCall in serverMessage.ToolCall.FunctionCalls)
            {
              Console.WriteLine($"[Function Call Details] Name: {functionCall.Name}, Id: '{functionCall.Id ?? "null"}'");
              if (functionCall.Name != null)
              {
                try
                {
                  // IdがnullでもGUIDを生成して処理を続行
                  string functionId = functionCall.Id ?? Guid.NewGuid().ToString();
                  Console.WriteLine($"[Function Call] Using ID: {functionId}");
                  await ExecuteFunctionCall(activeSession, functionCall.Name, functionId,
                                          functionCall.Args, cancellationToken);
                }
                catch (Exception ex)
                {
                  Console.WriteLine($"[Function Call Handler Error] Failed to execute {functionCall.Name}: {ex.Message}");
                  Console.WriteLine($"[Function Call Handler Error Stack Trace] {ex.StackTrace}");
                }
              }
              else
              {
                Console.WriteLine($"[Function Call Error] Function name is null - Name: {functionCall.Name}, Id: {functionCall.Id}");
              }
            }
          }

          if (serverMessage.ServerContent?.ModelTurn?.Parts != null)
          {
            foreach (var part in serverMessage.ServerContent.ModelTurn.Parts)
            {
              if (part?.InlineData?.Data != null)
              {
                byte[] audioBytes = part.InlineData.Data;
                await PlayAudioBytesAsync(audioBytes, cancellationToken);
              }
            }
          }
          else if (serverMessage.ServerContent?.TurnComplete == true)
          {
            Console.WriteLine("[Server Turn Complete]");
          }
        }
        catch (OperationCanceledException)
        {
          Console.WriteLine("Response processing cancelled.");
          break;
        }
        catch (Exception ex)
        {
          Console.WriteLine($"[Response Processing Error] {ex.Message}");
          Console.WriteLine($"[Response Processing Error Stack Trace] {ex.StackTrace}");
          // Continue processing other messages
        }
      }
    }
    catch (OperationCanceledException)
    {
      Console.WriteLine("Response processing task cancelled.");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error processing responses: {ex}");
    }
    finally
    {
      Console.WriteLine("Response processing task finished.");
      CleanupPlayback();
    }
  }

  // --- NAudio Microphone Capture ---
  public static void StartAudioCapture(BlockingCollection<byte[]> queue,
                                       CancellationToken cancellationToken)
  {
    if (waveIn != null)
    {
      Console.WriteLine("Audio capture already started.");
      return;
    }
    try
    {
      waveIn =
          new WaveInEvent
          {
            DeviceNumber = microphoneDeviceNumber,
            WaveFormat = new WaveFormat(SAMPLE_RATE, BITS_PER_SAMPLE, CHANNELS),
            BufferMilliseconds = BUFFER_MILLISECONDS
          };
      waveIn.DataAvailable += (s, e) =>
      {
        if (!queue.IsAddingCompleted && e.BytesRecorded > 0)
        {
          // Copy only the recorded bytes
          byte[] buffer = new byte[e.BytesRecorded];
          Array.Copy(e.Buffer, buffer, e.BytesRecorded);
          queue.TryAdd(buffer);
        }
      };
      waveIn.RecordingStopped += (s, e) => { Console.WriteLine("Microphone recording stopped."); };
      cancellationToken.Register(StopAudioCapture);
      waveIn.StartRecording();
      Console.WriteLine("NAudio microphone capture started.");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Failed to start NAudio capture: {ex}");
      StopAudioCapture();
      throw;
    }
  }

  public static void StopAudioCapture()
  {
    try
    {
      if (waveIn != null)
      {
        waveIn.StopRecording();
        waveIn.Dispose();
        waveIn = null;
        Console.WriteLine("NAudio microphone capture stopped.");
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error stopping NAudio capture: {ex.Message}");
    }
    audioQueue?.CompleteAdding();
  }

  // --- NAudio Playback ---
  private static void InitPlayback()
  {
    if (waveOut != null)
      return;
    bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(
        OUTPUT_SAMPLE_RATE, BITS_PER_SAMPLE, CHANNELS))
    {
      DiscardOnBufferOverflow = false,
      BufferDuration = TimeSpan.FromSeconds(30)
    };
    waveOut = new WaveOutEvent
    {
      DeviceNumber = speakerDeviceNumber,
    };
    waveOut.Init(bufferedWaveProvider);
    waveOut.Play();
  }

  private static async Task PlayAudioBytesAsync(byte[] audioBytes,
                                                CancellationToken cancellationToken)
  {
    if (bufferedWaveProvider == null || audioBytes == null || audioBytes.Length == 0)
      return;

    // Wait if the buffer is more than 90% full
    while (bufferedWaveProvider.BufferedBytes + audioBytes.Length >
           bufferedWaveProvider.BufferLength * 0.9)
    {
      if (cancellationToken.IsCancellationRequested)
      {
        Console.WriteLine("Playback cancelled while waiting for buffer space.");
        return;
      }
      await Task.Delay(100, cancellationToken);
    }

    try
    {
      bufferedWaveProvider.AddSamples(audioBytes, 0, audioBytes.Length);
    }
    catch (InvalidOperationException ex)
    {
      Console.WriteLine(
          $"Error adding samples to buffer (it might be full despite waiting): {ex}");
    }
    catch (OperationCanceledException)
    {
      Console.WriteLine("Playback delay cancelled.");
    }
  }

  private static void CleanupPlayback()
  {
    try
    {
      waveOut?.Stop();
      waveOut?.Dispose();
      waveOut = null;
      bufferedWaveProvider = null;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error during playback cleanup: {ex.Message}");
    }
  }
}
