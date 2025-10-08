# LiveAudioConversationFunctionCall

Vertex AI の [Live API](https://cloud.google.com/vertex-ai/generative-ai/docs/live-api) を使った双方向音声対話のデモアプリケーションです。[Google Gen AI .Net SDK](https://github.com/googleapis/dotnet-genai) を利用しています。

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
cd LiveAudioConversationFunctionCall

# NuGetパッケージの復元
dotnet restore

# プロジェクトのビルド
dotnet build
```

## 認証設定

### オプション1: Vertex AI（デフォルト、推奨）

```powershell
# Windows PowerShell
# 初回のみ: Application Default Credentialsでログイン
gcloud auth application-default login

# 環境変数の設定
$env:GOOGLE_CLOUD_PROJECT = "your-project-id"
$env:GOOGLE_CLOUD_LOCATION = "us-central1"  # オプション
```

### オプション2: Gemini API

```powershell
# Windows PowerShell
$env:GOOGLE_API_KEY = "your-api-key-here"
```

## デモの実行

### 基本的な使い方

```bash
dotnet run
```

### Gemini APIを使用する場合

```bash
dotnet run -- --vertexai false
```

### 以前のセッションを再開する場合

```bash
dotnet run -- --resumptionHandle "your-handle-from-file"
```

### カスタムシステム命令を使用する場合

```bash
dotnet run -- --systemInstruction "あなたは親切なアシスタントです。"
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

- **LiveAudioConversationFunctionCallResponse.json** - サーバーからの応答メッセージの履歴
- **LiveAudioConversationFunctionCallResumptionHandle.txt** - セッション再開用のハンドル（生成された場合）
