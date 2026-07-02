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

static string WriteFile(string filePath, string content)
{
    File.WriteAllText(filePath, content);
    return $"Successfully wrote to file: {filePath}";
}

static (string, string) ExecuteBashCommand(string command)
{
    var processInfo = new System.Diagnostics.ProcessStartInfo()
    {
        FileName = "/bin/sh",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = Directory.GetCurrentDirectory()
    };

    processInfo.ArgumentList.Add("-c");
    processInfo.ArgumentList.Add(command);

    using var process = new System.Diagnostics.Process
    { 
        StartInfo = processInfo
    };
    
    process.Start();
    
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    
    process.WaitForExit();

    return (stdout, stderr);
}

/// <summary>
/// link: https://github.com/openai/openai-dotnet?utm_source=chatgpt.com#how-to-use-chat-completions-with-tools-and-function-calling
/// </summary> 
ChatTool readTool = ChatTool.CreateFunctionTool(
    functionName: "Read",
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

ChatTool writeTool = ChatTool.CreateFunctionTool(
    functionName: "Write",
    functionDescription: "Write content to a file",
    functionParameters: BinaryData.FromBytes("""
    {
        "type": "object",
        "properties": {
            "file_path": {
                "type": "string",
                "description": "The path to the file to write"
            },
            "content": {
                "type": "string",
                "description": "The content to write to the file"
            }
        },
        "required": ["file_path", "content"]
    }
    """u8.ToArray())
    );

ChatTool bashTool = ChatTool.CreateFunctionTool(
    functionName: "Bash",
    functionDescription: "Execute a shell command",
    functionParameters: BinaryData.FromBytes("""
    {
        "type": "object",
        "properties": {
            "command": {
                "type": "string",
                "description": "The command to execute"
            }
        },
        "required": ["command"]
    }
    """u8.ToArray())
    );

ChatCompletionOptions options = new ChatCompletionOptions
{
    Tools = { readTool, writeTool, bashTool }
};

UserChatMessage userMessage = new UserChatMessage(prompt);

List<ChatMessage> messages =
[
    userMessage
];

while (true)
{ 
    ChatCompletion response = client.CompleteChat(
        messages,
        options
    );

    messages.Add(new AssistantChatMessage(response));

    if (response.FinishReason != ChatFinishReason.ToolCalls)
    {
        if (response.Content == null || response.Content.Count == 0)
        {
            throw new Exception("No choices in response");
        }
        Console.Write(response.Content[0].Text);
        return;
    }

    foreach (ChatToolCall toolCall in response.ToolCalls)
    {
        if (toolCall.FunctionName == "Read")
        {
            using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);
            bool hasFile = argumentsJson.RootElement.TryGetProperty("file_path", out JsonElement filePath);

            string toolResult = hasFile ? ReadFile(filePath.GetString()!) : "The file_path argument must not be empty.";
            messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
        }
        else if (toolCall.FunctionName == "Write")
        {
            using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);
            bool hasFile = argumentsJson.RootElement.TryGetProperty("file_path", out JsonElement filePath);

            string toolResult = hasFile ? WriteFile(filePath.GetString()!, argumentsJson.RootElement.GetProperty("content").GetString()!) : "The file_path argument must not be empty.";
            messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
        }
        else if (toolCall.FunctionName == "Bash")
        {
            using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);
            bool hasCommand = argumentsJson.RootElement.TryGetProperty("command", out JsonElement command);
            
            (string stdout, string stderr) = hasCommand ? ExecuteBashCommand(command.GetString()!) : ("", "The command argument must not be empty.");

            string toolResult = stdout is not null ? stdout : stderr;

            messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
        }
    }
}