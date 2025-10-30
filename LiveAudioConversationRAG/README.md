# LiveAudioConversationRAG

Vertex AI の [Live API](https://cloud.google.com/vertex-ai/generative-ai/docs/live-api) を使った双方向音声対話のデモアプリケーションで、RAG 及び Function calling を実装しています。[Google Gen AI .Net SDK](https://github.com/googleapis/dotnet-genai) を利用しています。

## 前提条件

- Windows 11
- .NET 8.0以降
- マイクとスピーカー（マイク・スピーカーへのアクセス許可が必要）
- [gcloud CLI](https://cloud.google.com/sdk/docs/install) がインストールされていること
- [Google.GenAI NuGetパッケージ](https://www.nuget.org/packages/Google.GenAI)
- [NAudio NuGetパッケージ](https://www.nuget.org/packages/NAudio)

## インストール

```bash
# このディレクトリに移動
cd LiveAudioConversationRAG

# NuGetパッケージの復元
dotnet restore

# プロジェクトのビルド
dotnet build
```

## 認証設定

```powershell
# Windows PowerShell
# 初回のみ: Application Default Credentialsでログイン
gcloud auth application-default login

# 環境変数の設定
$env:GOOGLE_CLOUD_PROJECT = "your-project-id"
$env:GOOGLE_CLOUD_LOCATION = "us-central1"  # オプション
```

## デモの実行

### 基本的な使い方

```bash
dotnet run
```

### ヘルプの表示

```bash
dotnet run -- --help
# または
dotnet run -- -h
```

### オプション

- **セッションの再開:**
  ```bash
  dotnet run -- --resumption-handle "your-handle-from-file"
  ```

- **システム命令の指定（テキスト）:**
  ```bash
  dotnet run -- --system-instruction "あなたは猫です。猫のように話してください。"
  ```

- **システム命令の指定（ファイル）:**
  ```bash
  dotnet run -- --system-instruction-file "path/to/your/instruction.txt"
  ```

- **ナレッジコーパスの利用 (RAG):**
  ```bash
  dotnet run -- --knowledge-corpus "corpora/your-corpus-id"
  ```

- **メモリコーパスの利用 (RAG):**
  ```bash
  dotnet run -- --memory-corpus "corpora/your-corpus-id"
  ```

## 使い方

1. デモを起動し、接続を待ちます
2. マイクに向かって話しかけます
3. Geminiの応答を聞きます
4. 次のように尋ねてみてください（日本語で）:
   - "今何時ですか？"
   - "東京の天気はどうですか？"
5. Ctrl+Cを押して終了します

## 利用可能な関数

デモには以下、2 つの Function calling が含まれています:

1. **getCurrentDateTime**: 現在の日時を日本語形式で返します
2. **getWeatherInfo**: 日本の都市（東京、大阪、名古屋、福岡）のモック天気情報を返します

## 出力ファイル

実行中に以下のファイルが生成されます:

- **LiveAudioConversationRAGResponse.json** - サーバーからの応答メッセージの履歴
- **LiveAudioConversationRAGResumptionHandle.txt** - セッション再開用のハンドル（生成された場合）
