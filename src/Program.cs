using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

//https://github.com/openai/openai-dotnet

if (args.Length < 2 || args[0] != "-p")
{
    throw new Exception("Usage: program -p <prompt>");
}

var prompt = args[1];

if (string.IsNullOrEmpty(prompt))
{
    throw new Exception("Prompt must not be empty");
}

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
var baseUrl = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1";

if (string.IsNullOrEmpty(apiKey))
{
    throw new Exception("OPENROUTER_API_KEY is not set");
}

var client = new ChatClient(
    model: "anthropic/claude-haiku-4.5",
    credential: new ApiKeyCredential(apiKey),
    options: new OpenAIClientOptions { Endpoint = new Uri(baseUrl) }
);

static string ReadFile(string filePath)
{
    if (!File.Exists(filePath))
    {
        throw new Exception($"File not found: {filePath}");
    }
    return File.ReadAllText(filePath);
}

/// <summary>
/// link: https://github.com/openai/openai-dotnet?utm_source=chatgpt.com#how-to-use-chat-completions-with-tools-and-function-calling
/// </summary> 
ChatTool readTool = ChatTool.CreateFunctionTool(
    functionName: nameof(ReadFile),
    functionDescription: "Read and return the contents of a file",
    functionParameters: BinaryData.FromBytes("""
    {
        "type": "object",
        "properties": {
            "file_path": {
                "type": "string",
                "description": "The path to the file to read"
            }
        },
        "required": ["file_path"]
    }
    """u8.ToArray())
    );

ChatCompletionOptions options = new ChatCompletionOptions
{
    Tools = { readTool }
};

UserChatMessage userMessage = new UserChatMessage(prompt);

List<ChatMessage> messages =
[
    userMessage
];

ChatCompletion response = client.CompleteChat(
    messages,
    options
);

/// <summary>
/// link: https://github.com/openai/openai-dotnet?utm_source=chatgpt.com#how-to-use-chat-completions-with-tools-and-function-calling
/// ToolsCalls
/// </summary>

if (response.FinishReason == ChatFinishReason.ToolCalls)
{
    //messages.Add(new AssistantChatMessage(response));
    foreach (ChatToolCall toolCall in response.ToolCalls)
    {
        if (toolCall.FunctionName == nameof(ReadFile))
        {
            using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);
            bool hasFile = argumentsJson.RootElement.TryGetProperty("file_path", out JsonElement filePath);

            string toolResult = hasFile ? ReadFile(filePath.GetString()!) : "The file_path argument must not be empty.";
            Console.Write(toolResult);
            return;
            //messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
        }
        else
        {
            Console.Write(response.Content[0].Text);
        }
    }
}

//ChatCompletion completion = client.CompleteChat(messages, options);


if (response.Content == null || response.Content.Count == 0)
{
    throw new Exception("No choices in response");
}

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.Error.WriteLine("Logs from your program will appear here!");

// TODO: Uncomment the line below to pass the first stage
Console.Write(response.Content[0].Text);

