using Azure.AI.OpenAI;

var builder = WebApplication.CreateBuilder();
var app = builder.Build();

// Alter this if needed.
var configuration = new
{
    SourceFile = "d:/anki.txt",
    DestinationFile = "d:/anki.csv"
};

var ai = new OpenAIClient(app.Configuration["OpenAIKey"]);

async Task<string> DoAsync(AnkiCardInfo info)
{
    var contextPart = "If this word (phrase) has multiple meanings, get the correct one using this example as context: ";
    var transcription = await GetAiResponseAsync($"Give me the phonetic transcription in English language for how to pronounce this: \"{info.Phrase}\". Just return the transcription, nothing else.");
    var definition = (await GetAiResponseAsync($"Give me definition in English language for: \"{info.Phrase}\". Do not include the word itself in the description as it will be used for flashcards. {contextPart}{info.Example}.")).Replace(info.Phrase, "...", StringComparison.InvariantCultureIgnoreCase);
    var synonyms = await GetAiResponseAsync($"Give me up to 5 synonyms for this: \"{info.Phrase}\", without new lines, split them by comma. {contextPart}{info.Example}.");
    var antonyms = await GetAiResponseAsync($"Give me up to 5 antonyms for this: \"{info.Phrase}\", without new lines, split them by comma. {contextPart}{info.Example}.");
    var origin = await GetAiResponseAsync($"Give me the origin of this English word: \"{info.Phrase}\". {contextPart}{info.Example}.");
    var translation = await GetAiResponseAsync($"Give me up to 5 closest Russian translations of this: \"{info.Phrase}\", without new lines, split them by comma. {contextPart}{info.Example}.");
    var help = await GetAiResponseAsync($"For the following sentence, replace the word/phrase \"{info.Phrase}\" with this: \" [...] \" so that I can use the sentence to learn this word. This is the sentence: {info.Example}");
    if (help.Contains(info.Phrase, StringComparison.InvariantCultureIgnoreCase))
        help = help.Replace(info.Phrase, " [...] ", StringComparison.InvariantCultureIgnoreCase);

    var moreExamples = await GetAiResponseAsync($"Generate up to 3 different examples of using the word/phrase: \"{info.Phrase}\", split them by newline. {contextPart}{info.Example}.");
    var phraseAudio = await GetAiAudioResponseAsync(info.Phrase);
    var exampleAudio = await GetAiAudioResponseAsync(info.Example);

    var folder = $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/AppData/Roaming/Anki2/User 1/collection.media/";

    var audioName = info.Phrase.Replace(' ', '_');
    await File.WriteAllBytesAsync($"{folder}/acf-{audioName}.mp3", phraseAudio);
    await File.WriteAllBytesAsync($"{folder}/acf-{audioName}-example.mp3", exampleAudio);

    var result = $"{info.Phrase}|{transcription}\n[sound:acf-{audioName}.mp3]|{definition}|{synonyms}\n[ {antonyms} ]|{origin}|{translation}|[sound:acf-{audioName}-example.mp3]\n{info.Example}\n\n{moreExamples}||{help}|"
        .Replace("\r", string.Empty)
        .Replace("\n", "</br>");
    return result;
}

async Task<string> DoAllAsync(IEnumerable<AnkiCardInfo> infos)
{
    var tasks = infos.Select(DoAsync).ToList();
    await Task.WhenAll(tasks);

    return string.Join('\n', tasks.Select(x => x.Result));
}

var content = File.ReadAllText(configuration.SourceFile);
var infos = content.Split("\n")
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .Select(line => new AnkiCardInfo(line.Split("|")[0], line.Split("|")[1]))
    .ToList();

var result = await DoAllAsync(infos);
File.WriteAllText(configuration.DestinationFile, result);
Console.WriteLine("Done");

ChatRequestMessage[] GetContext(string request)
{
    return new ChatRequestMessage[]
    {
        new ChatRequestSystemMessage(
            "You are an AI that is strictly returning only the answer to the question without any ambient information. Just output the data. " +
            request)
    };
}

async ValueTask<string> GetAiResponseAsync(string request)
{
    var context = GetContext(request);

    var response = await ai.GetChatCompletionsAsync(new ChatCompletionsOptions("gpt-4o-mini", context));
    return response.Value.Choices[0].Message.Content;
}

async ValueTask<byte[]> GetAiAudioResponseAsync(string text)
{
    var response = await ai.GenerateSpeechFromTextAsync(new SpeechGenerationOptions
    {
        Voice = SpeechVoice.Fable,
        Input = text,
        DeploymentName = "tts-1"
    });

    return response.Value.ToArray();
}

public sealed record AnkiCardInfo(string Phrase, string Example);
