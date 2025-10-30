using System.Collections.Concurrent;
using System.Text.Json;
using Google.GenAI;
using NAudio.Wave;
using Types = Google.GenAI.Types;

public static class LiveAudioConversationRAG
{
    // Configuration Constants
    private const string ModelName = "gemini-live-2.5-flash-preview-native-audio-09-2025";
    private const string VoiceName = "Leda";
    private const string LanguageCode = "ja-JP";

    // Audio Constants
    private const int SampleRate = 16000;
    private const int OutputSampleRate = 24000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int BufferMilliseconds = 32; // ~512 frames at 16kHz
    private const int DefaultMicrophoneDeviceNumber = 0;
    private const int DefaultSpeakerDeviceNumber = -1;

    // File Path Constants
    private static readonly string AudioResponseOutputPath = Path.GetFullPath("LiveAudioConversationRAGResponse.json");
    private static readonly string ResumptionHandleOutputPath = Path.GetFullPath("LiveAudioConversationRAGResumptionHandle.txt");

    // Threading and State Management
    private static Task? _audioStreamingTask;
    private static Task? _responseProcessingTask;
    private static AsyncSession? _session;
    private static BlockingCollection<byte[]>? _audioQueue;
    private static CancellationTokenSource? _audioCaptureCts;

    // NAudio Fields
    private static WaveInEvent? _waveIn;
    private static WaveOutEvent? _waveOut;
    private static BufferedWaveProvider? _bufferedWaveProvider;

    public static async Task RunConversation(string systemInstructionString,
                                           string? resumptionHandle = null,
                                           string? knowledgeCorpusId = null,
                                           string? memoryCorpusId = null)
    {
        _audioCaptureCts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down gracefully...");
            _audioCaptureCts?.Cancel();
        };

        try
        {
            var client = CreateLiveClient();
            var tools = CreateTools(knowledgeCorpusId, memoryCorpusId);
            var config = CreateLiveConnectConfig(systemInstructionString, resumptionHandle, tools);

            Console.WriteLine("Connecting to Gemini Live API...");
            _session = await client.Live.ConnectAsync(ModelName, config, _audioCaptureCts.Token);
            Console.WriteLine("Connected.");

            await StartAudioProcessingTasks(_session, _audioCaptureCts.Token);
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
            await Cleanup();
        }
    }

    private static Client CreateLiveClient()
    {
        string project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? string.Empty;
        if (string.IsNullOrEmpty(project))
            throw new InvalidOperationException("Project ID is not set in the environment variable GOOGLE_CLOUD_PROJECT.");

        string location = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_LOCATION") ?? "us-central1";

        return new Client(project: project, location: location, vertexAI: true);
    }

    private static List<Types.Tool> CreateTools(string? knowledgeCorpusId, string? memoryCorpusId)
    {
        var functionTools = new Types.Tool
        {
            FunctionDeclarations = new List<Types.FunctionDeclaration>
            {
                new()
                {
                    Name = "getCurrentDateTime",
                    Description = "現在の日時を正確に取得する関数です。年月日、時分秒、曜日を日本語で返します。時刻や日付に関する質問があった場合に使用してください。",
                    Parameters = new Types.Schema { Type = Types.Type.OBJECT, Properties = new Dictionary<string, Types.Schema>(), Required = new List<string>() }
                },
                new()
                {
                    Name = "getWeatherInfo",
                    Description = "指定された場所の天気情報を取得する関数です。場所が指定されない場合は東京の天気を返します。",
                    Parameters = new Types.Schema
                    {
                        Type = Types.Type.OBJECT,
                        Properties = new Dictionary<string, Types.Schema>
                        {
                            ["location"] = new() { Type = Types.Type.STRING, Description = "天気情報を取得したい場所の名前（例：東京、大阪、名古屋）" }
                        },
                        Required = new List<string>()
                    }
                }
            }
        };

        var tools = new List<Types.Tool> { functionTools };

        if (!string.IsNullOrEmpty(knowledgeCorpusId))
        {
            tools.Add(new Types.Tool
            {
                Retrieval = new Types.Retrieval
                {
                    VertexRagStore = new Types.VertexRagStore
                    {
                        RagResources = new List<Types.VertexRagStoreRagResource> { new() { RagCorpus = knowledgeCorpusId } },
                        StoreContext = false
                    }
                }
            });
        }

        if (!string.IsNullOrEmpty(memoryCorpusId))
        {
            tools.Add(new Types.Tool
            {
                Retrieval = new Types.Retrieval
                {
                    VertexRagStore = new Types.VertexRagStore
                    {
                        RagResources = new List<Types.VertexRagStoreRagResource> { new() { RagCorpus = memoryCorpusId } },
                        StoreContext = true
                    }
                }
            });
        }

        return tools;
    }

    private static Types.LiveConnectConfig CreateLiveConnectConfig(string systemInstruction, string? resumptionHandle, List<Types.Tool> tools)
    {
        return new Types.LiveConnectConfig
        {
            SessionResumption = new Types.SessionResumptionConfig { Transparent = true, Handle = resumptionHandle },
            ResponseModalities = new List<Types.Modality> { Types.Modality.AUDIO },
            SpeechConfig = new Types.SpeechConfig
            {
                VoiceConfig = new Types.VoiceConfig { PrebuiltVoiceConfig = new Types.PrebuiltVoiceConfig { VoiceName = VoiceName } },
                LanguageCode = LanguageCode
            },
            InputAudioTranscription = new Types.AudioTranscriptionConfig { },
            RealtimeInputConfig = new Types.RealtimeInputConfig
            {
                AutomaticActivityDetection = new Types.AutomaticActivityDetection
                {
                    SilenceDurationMs = 10,
                    Disabled = false,
                    StartOfSpeechSensitivity = Types.StartSensitivity.START_SENSITIVITY_HIGH,
                    EndOfSpeechSensitivity = Types.EndSensitivity.END_SENSITIVITY_HIGH,
                    PrefixPaddingMs = 10,
                }
            },
            SystemInstruction = new Types.Content { Parts = new List<Types.Part> { new() { Text = systemInstruction } }, Role = "user" },
            Tools = tools,
            Temperature = 0.5f,
            TopP = 0.9f,
            TopK = 40,
        };
    }

    private static async Task StartAudioProcessingTasks(AsyncSession session, CancellationToken token)
    {
        _audioQueue = new BlockingCollection<byte[]>();

        Console.WriteLine("Starting NAudio capture...");
        StartAudioCapture(_audioQueue, token);

        Console.WriteLine("Starting audio streaming and response processing tasks...");
        _audioStreamingTask = Task.Run(() => StreamInputAudioFromQueueAsync(session, _audioQueue, token), token);
        _responseProcessingTask = Task.Run(() => ProcessResponseAudioAsync(session, token), token);

        Console.WriteLine("Audio streaming started. Press Ctrl+C to exit...");
        await Task.WhenAll(_audioStreamingTask, _responseProcessingTask);
    }

    private static async Task Cleanup()
    {
        Console.WriteLine("Shutting down...");
        if (_audioCaptureCts != null && !_audioCaptureCts.IsCancellationRequested)
            _audioCaptureCts.Cancel();

        StopAudioCapture();
        _audioQueue?.Dispose();
        _audioCaptureCts?.Dispose();

        if (_session != null)
        {
            await _session.DisposeAsync();
            Console.WriteLine("Live API session closed.");
        }
        Console.WriteLine("Shutdown complete.");
    }

    private static async Task StreamInputAudioFromQueueAsync(AsyncSession activeSession, BlockingCollection<byte[]> queue, CancellationToken cancellationToken)
    {
        Console.WriteLine("Audio streaming task started, waiting for data...");
        try
        {
            foreach (var chunk in queue.GetConsumingEnumerable(cancellationToken))
            {
                if (chunk.Length > 0)
                {
                    var realtimeInput = new Types.LiveSendRealtimeInputParameters
                    {
                        Media = new Types.Blob { Data = chunk, MimeType = $"audio/l16;rate={SampleRate}" },
                    };
                    await activeSession.SendRealtimeInputAsync(realtimeInput, cancellationToken);
                }
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

    private static async Task ProcessResponseAudioAsync(AsyncSession activeSession, CancellationToken cancellationToken)
    {
        Console.WriteLine("Response processing task started.");
        try
        {
            InitPlayback();
            while (!cancellationToken.IsCancellationRequested)
            {
                var serverMessage = await activeSession.ReceiveAsync(cancellationToken);
                if (serverMessage == null)
                {
                    Console.WriteLine("Connection closed by server.");
                    break;
                }

                await File.AppendAllTextAsync(AudioResponseOutputPath, JsonSerializer.Serialize(serverMessage) + "\n\n", cancellationToken);

                if (serverMessage.SessionResumptionUpdate?.NewHandle != null)
                {
                    await File.WriteAllTextAsync(ResumptionHandleOutputPath, serverMessage.SessionResumptionUpdate.NewHandle, cancellationToken);
                }

                if (serverMessage.ToolCall?.FunctionCalls != null)
                {
                    await HandleFunctionCalls(activeSession, serverMessage.ToolCall.FunctionCalls, cancellationToken);
                }

                if (serverMessage.ServerContent?.ModelTurn?.Parts != null)
                {
                    foreach (var part in serverMessage.ServerContent.ModelTurn.Parts)
                    {
                        if (part?.InlineData?.Data != null)
                        {
                            await PlayAudioBytesAsync(part.InlineData.Data, cancellationToken);
                        }
                    }
                }
                else if (serverMessage.ServerContent?.TurnComplete == true)
                {
                    Console.WriteLine("[Server Turn Complete]");
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
    
    private static async Task HandleFunctionCalls(AsyncSession session, IEnumerable<Types.FunctionCall> functionCalls, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Function Call Detected] Found {functionCalls.Count()} function calls");
        foreach (var functionCall in functionCalls)
        {
            if (string.IsNullOrEmpty(functionCall.Name))
            {
                Console.WriteLine($"[Function Call Error] Function name is null - Id: {functionCall.Id}");
                continue;
            }

            string functionId = functionCall.Id ?? Guid.NewGuid().ToString();
            Console.WriteLine($"[Function Call Details] Name: {functionCall.Name}, Id: '{functionId}'");

            try
            {
                await ExecuteFunctionCall(session, functionCall.Name, functionId, functionCall.Args, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Function Call Handler Error] Failed to execute {functionCall.Name}: {ex.Message}");
                Console.WriteLine($"[Function Call Handler Error Stack Trace] {ex.StackTrace}");
            }
        }
    }

    private static async Task ExecuteFunctionCall(AsyncSession session, string functionName, string functionId, object? args, CancellationToken cancellationToken)
    {
        string result;
        Console.WriteLine($"[Function Call Start] Executing {functionName} with ID: {functionId}");

        try
        {
            result = functionName switch
            {
                "getCurrentDateTime" => GetCurrentDateTime(),
                "getWeatherInfo" => GetWeatherInfo(ExtractLocation(args)),
                _ => $"Unknown function: {functionName}"
            };
            Console.WriteLine($"[Function Call] {functionName}: {result}");
        }
        catch (Exception ex)
        {
            result = $"Function execution error: {ex.Message}";
            Console.WriteLine($"[Function Call Exception] {functionName}: {ex.Message}\n{ex.StackTrace}");
        }

        await SendFunctionResponse(session, functionName, functionId, result, cancellationToken);
    }

    private static string? ExtractLocation(object? args)
    {
        if (args == null) return null;

        Console.WriteLine($"[Function Call] Args received: {args}");
        if (args is JsonElement { ValueKind: JsonValueKind.Object } jsonElement)
        {
            return jsonElement.TryGetProperty("location", out var locationProp) ? locationProp.GetString() : null;
        }

        if (args is Dictionary<string, object> argsDict)
        {
            return argsDict.TryGetValue("location", out var locationObj) ? locationObj?.ToString() : null;
        }

        return null;
    }

    private static async Task SendFunctionResponse(AsyncSession session, string functionName, string functionId, string result, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"[Function Response] Preparing response for {functionName}");
            var functionResponse = new Types.FunctionResponse
            {
                Name = functionName,
                Id = functionId,
                Response = new Dictionary<string, object> { ["content"] = result }
            };

            var toolResponseParams = new Types.LiveSendToolResponseParameters
            {
                FunctionResponses = new List<Types.FunctionResponse> { functionResponse }
            };

            Console.WriteLine($"[Function Response] Sending response to server for ID: {functionId}...");
            await session.SendToolResponseAsync(toolResponseParams, cancellationToken);
            Console.WriteLine($"[Function Response] Response sent successfully for {functionName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Function Response Error] Failed to send response for {functionName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static string GetCurrentDateTime() => DateTime.Now.ToString("yyyy年MM月dd日 HH時mm分ss秒 (dddd)");

    private static string GetWeatherInfo(string? location = null)
    {
        var locations = new Dictionary<string, string>
        {
            ["東京"] = "晴れ、気温25℃、湿度60%",
            ["大阪"] = "曇り、気温23℃、湿度65%",
            ["名古屋"] = "雨、気温20℃、湿度80%",
            ["福岡"] = "晴れ、気温28℃、湿度55%"
        };

        string targetLocation = location ?? "東京";
        var foundLocation = locations.Keys.FirstOrDefault(k => k.Contains(targetLocation) || targetLocation.Contains(k));

        return foundLocation != null
            ? $"{foundLocation}の天気: {locations[foundLocation]}"
            : $"{targetLocation}の天気情報は利用できませんが、一般的に今日は穏やかな天気です。";
    }

    // --- NAudio Microphone Capture ---
    public static void StartAudioCapture(BlockingCollection<byte[]> queue, CancellationToken cancellationToken)
    {
        if (_waveIn != null)
        {
            Console.WriteLine("Audio capture already started.");
            return;
        }
        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = DefaultMicrophoneDeviceNumber,
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = BufferMilliseconds
            };
            _waveIn.DataAvailable += (s, e) =>
            {
                if (!queue.IsAddingCompleted && e.BytesRecorded > 0)
                {
                    queue.TryAdd(e.Buffer.Take(e.BytesRecorded).ToArray());
                }
            };
            _waveIn.RecordingStopped += (s, e) => Console.WriteLine("Microphone recording stopped.");
            cancellationToken.Register(StopAudioCapture);
            _waveIn.StartRecording();
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
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;
            Console.WriteLine("NAudio microphone capture stopped.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping NAudio capture: {ex.Message}");
        }
        _audioQueue?.CompleteAdding();
    }

    // --- NAudio Playback ---
    private static void InitPlayback()
    {
        if (_waveOut != null) return;

        _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(OutputSampleRate, BitsPerSample, Channels))
        {
            DiscardOnBufferOverflow = false,
            BufferDuration = TimeSpan.FromSeconds(30)
        };
        _waveOut = new WaveOutEvent { DeviceNumber = DefaultSpeakerDeviceNumber };
        _waveOut.Init(_bufferedWaveProvider);
        _waveOut.Play();
    }

    private static async Task PlayAudioBytesAsync(byte[] audioBytes, CancellationToken cancellationToken)
    {
        if (_bufferedWaveProvider == null || audioBytes.Length == 0) return;

        while (_bufferedWaveProvider.BufferedBytes + audioBytes.Length > _bufferedWaveProvider.BufferLength * 0.9)
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
            _bufferedWaveProvider.AddSamples(audioBytes, 0, audioBytes.Length);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error adding samples to buffer: {ex}");
        }
    }

    private static void CleanupPlayback()
    {
        try
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
            _bufferedWaveProvider = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during playback cleanup: {ex.Message}");
        }
    }
}