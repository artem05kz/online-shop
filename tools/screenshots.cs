#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Снимки страниц в headless Chrome через DevTools: вход под тестовым менеджером и полностраничные PNG.
// dotnet run shots.cs -- <папка> <ширина> <логин|-> <пароль|-> <путь1> [путь2 ...]
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var outDir = args[0];
var width = int.Parse(args[1]);
var (login, password) = (args[2], args[3]);
var paths = args[4..];
const string Base = "http://localhost:5080";
Directory.CreateDirectory(outDir);

var chrome = Process.Start(new ProcessStartInfo(@"C:\Program Files\Google\Chrome\Application\chrome.exe",
    ["--headless=new", "--disable-gpu", "--hide-scrollbars", "--remote-debugging-port=9333",
     $"--user-data-dir={Path.Combine(outDir, ".profile")}", $"--window-size={width},900", "about:blank"]))!;

using var http = new HttpClient();
JsonArray? targets = null;
for (var i = 0; i < 50 && targets is null; i++)
{
    try { targets = JsonNode.Parse(await http.GetStringAsync("http://127.0.0.1:9333/json"))!.AsArray(); }
    catch { await Task.Delay(200); }
}
var wsUrl = targets!.First(t => t!["type"]!.GetValue<string>() == "page")!["webSocketDebuggerUrl"]!.GetValue<string>();
using var ws = new ClientWebSocket();
ws.Options.KeepAliveInterval = TimeSpan.Zero;
await ws.ConnectAsync(new Uri(wsUrl), default);
var nextId = 0;

async Task<JsonNode> Send(string method, object? parameters = null)
{
    var id = ++nextId;
    var payload = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } });
    await ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, default);
    var buffer = new byte[1 << 20];
    while (true)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do { r = await ws.ReceiveAsync(buffer, default); ms.Write(buffer, 0, r.Count); } while (!r.EndOfMessage);
        var msg = JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()))!;
        if (msg["id"]?.GetValue<int>() == id) return msg;
    }
}
async Task<string?> Eval(string expr) =>
    (await Send("Runtime.evaluate", new { expression = expr, returnByValue = true }))["result"]?["result"]?["value"]?.ToString();
async Task Go(string url)
{
    await Send("Page.navigate", new { url });
    for (var i = 0; i < 60; i++) { await Task.Delay(150); if (await Eval("document.readyState") == "complete") break; }
    await Task.Delay(500);
}

await Send("Page.enable");
await Send("Emulation.setDeviceMetricsOverride", new { width, height = 900, deviceScaleFactor = 1, mobile = width < 600 });
if (login != "-")
{
    await Go($"{Base}/accounts/login");
    await Eval($"document.getElementById('Input_UserName').value={JsonSerializer.Serialize(login)};" +
               $"document.getElementById('Input_Password').value={JsonSerializer.Serialize(password)};document.querySelector('main form').submit();1");
    await Task.Delay(1500);
}
foreach (var entry in paths)
{
    // «адрес|скрипт»: выполнить действие на странице перед снимком (например, набрать текст в поиск).
    var parts = entry.Split('|', 2);
    var path = parts[0];
    await Go(Base + path);
    if (parts.Length > 1)
    {
        await Eval(parts[1] + ";1");
        await Task.Delay(1500);
    }
    // Отложенная загрузка картинок не срабатывает при съёмке в полный рост: грузим все сразу.
    await Eval("document.querySelectorAll('img[loading=lazy]').forEach(i=>i.loading='eager');1");
    await Task.Delay(1500);
    var shot = await Send("Page.captureScreenshot", new { format = "png", captureBeyondViewport = parts.Length == 1 });
    var name = System.Text.RegularExpressions.Regex.Replace(path, "[^a-zA-Z0-9]+", "_").Trim('_') + (parts.Length > 1 ? "_action" : "");
    File.WriteAllBytes(Path.Combine(outDir, (name.Length == 0 ? "home" : name) + ".png"),
        Convert.FromBase64String(shot["result"]!["data"]!.GetValue<string>()));
    Console.WriteLine("снято " + name);
}
chrome.Kill(true);
