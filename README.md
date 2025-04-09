# Vertex AI Gemini C# Multi-turn Chat Sample

Vertex AI の Gemini 2.0 Flash モデルを使用したマルチターンチャットのサンプルアプリケーションです。
ストリーミングレスポンスに対応し、AIの応答をリアルタイムで表示します。

## 前提条件

- .NET 8.0以上
- Google Cloud Project
- Vertex AI APIの有効化
- Cloud Storage API の有効化
- `gcloud` CLIツールのインストール
- お使いのユーザーアカウントがプロジェクトの Owner 権限を持っている

## セットアップ

1. Google Cloud Projectの設定
   - [Google Cloud Console](https://console.cloud.google.com/)で新しいプロジェクトを作成
   - Vertex AI APIを有効化
   - Cloud Storage APIを有効化
   - プロジェクトの課金を有効化

2. `gcloud` CLIの設定
   ```bash
   # gcloud CLIのインストール（まだの場合）
   # macOS: brew install google-cloud-sdk
   # Windows: https://cloud.google.com/sdk/docs/install-sdk#windows

   # Google Cloudにログイン
   gcloud auth login

   # プロジェクトの設定
   gcloud config set project YOUR_PROJECT_ID
   ```

3. Cloud Storageバケットの作成とPDFファイルのアップロード
   ```bash
   # バケットを作成（グローバルで一意の名前を指定）
   gcloud storage buckets create gs://YOUR_BUCKET_NAME --location=us-central1

   # PDFファイルをアップロード（例：マップや資料など）
   gcloud storage cp path/to/your/file.pdf gs://YOUR_BUCKET_NAME/

   # ファイルの情報を確認
   gcloud storage ls -l gs://YOUR_BUCKET_NAME/file.pdf
   ```

   注: アップロードしたPDFファイルのGCSパスは `gs://YOUR_BUCKET_NAME/file.pdf` の形式になります。
   これをコード内の `SystemInstructionPdfUri` 変数に設定して使用します。

4. サービスアカウントの設定と長期アクセストークンの取得
   ```bash
   # サービスアカウントを作成（まだ作成していない場合）
   gcloud iam service-accounts create gemini-api-client \
     --display-name="Gemini API Client"

   # サービスアカウントに必要な権限を付与
   gcloud projects add-iam-policy-binding YOUR_PROJECT_ID \
     --member="serviceAccount:gemini-api-client@YOUR_PROJECT_ID.iam.gserviceaccount.com" \
     --role="roles/aiplatform.user"

   # サービスアカウントに対して、特定のユーザーがトークンを生成できる権限を付与
   gcloud iam service-accounts add-iam-policy-binding \
     gemini-api-client@YOUR_PROJECT_ID.iam.gserviceaccount.com \
     --member="user:YOUR_EMAIL@example.com" \
     --role="roles/iam.serviceAccountTokenCreator"

   # サービスアカウント認証を使用してアクセストークンを取得（12時間有効）
   # Mac/Linux (bash/zsh)の場合:
   export GOOGLE_ACCESS_TOKEN=$(gcloud auth print-access-token --lifetime=43200 --impersonate-service-account=gemini-api-client@YOUR_PROJECT_ID.iam.gserviceaccount.com)
   ```

   Windows環境の場合:
   ```powershell
   # Windows PowerShellの場合
   $env:GOOGLE_ACCESS_TOKEN = $(gcloud auth print-access-token --lifetime=43200 --impersonate-service-account=gemini-api-client@YOUR_PROJECT_ID.iam.gserviceaccount.com)
   ```

   注: `--lifetime=43200` は秒単位で12時間（12時間 × 60分 × 60秒 = 43200秒）のトークン有効期限を指定しています。

5. プロジェクトの設定
   - `VertexGeminiChat.cs`の`ProjectId`変数を自分のGoogle Cloud Project IDに変更
   ```csharp
   private static readonly string ProjectId = "YOUR_PROJECT_ID";
   ```

6. システムインストラクションとPDFファイルの設定
   - `SystemInstructionText`を必要に応じて変更
   - 先ほどアップロードしたPDFファイルのGCSパスを`SystemInstructionPdfUri`変数に設定
   ```csharp
   private static readonly string SystemInstructionPdfUri = "gs://YOUR_BUCKET_NAME/file.pdf";
   ```

## 実行方法

```bash
dotnet run
```

## 機能

- マルチターンチャット：会話の文脈を保持
- ストリーミングレスポンス：AIの応答をリアルタイムで表示
- 会話履歴の管理：最大10ターンまでの会話を保持
- 添付ファイル（PDF）のサポート：地図などの情報をAIに提供可能
- Gemini 2.0 Flash モデルの活用：高速で自然な応答生成
- 生成パラメータの調整：温度（Temperature）と上位確率（TopP）による応答の多様性制御

## 生成パラメータの設定

コード内で以下のパラメータを調整できます：

```csharp
// 生成パラメータの設定
private static readonly float Temperature = 1.0f; // 値が大きいほど多様な応答（0.0-2.0）
private static readonly float TopP = 0.95f; // 確率質量の上位P%のトークンのみを考慮（0.0-1.0）
```

- **Temperature**：値が高いほど創造的で多様な応答になり、低いほど一貫性のある応答になります
- **TopP**：次のトークンを選択する際に考慮する確率分布の上位P%を制御します

## 使用方法

1. アプリケーションを起動すると、チャットインターフェースが表示されます
2. プロンプトに質問や指示を入力します
3. AIからの応答がリアルタイムで表示されます
4. 会話を終了するには 'exit' と入力します

## コードの構造

- **ChatMessage**：チャットメッセージを表すクラス（テキストとファイル添付をサポート）
- **InitializeSystemMessage**：システムメッセージの初期化
- **RunChatLoop**：メインのチャットループを実行
- **TrimChatHistory**：チャット履歴のサイズ管理
- **SendRequestStreamingAsync**：ストリーミングAPIリクエストの送信と処理
- **CreateContentsFromChatHistory**：チャット履歴からAPIリクエスト用データの作成

## 注意事項

- APIの利用には料金が発生する場合があります
- アクセストークンは定期的に更新が必要です（12時間で期限切れ）
- 本番環境での使用には、適切なエラーハンドリングとセキュリティ対策を実装してください
- アクセストークンは環境変数として安全に管理してください 