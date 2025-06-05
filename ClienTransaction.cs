using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace XClientTransaction;

public class ClientTransaction
{
    public static readonly int AdditionalRandomNumber = 3;
    public static readonly string DefaultKeyword = "obfiowerehiring";

    private static readonly Regex OnDemandFileRegex =
        new(@"['""]ondemand\.s['""]:\s*['""]([\w]+)['""]", RegexOptions.Compiled);

    private static readonly Regex IndicesRegex = new(@"\(\w\[(\d{1,2})\],\s*16\)", RegexOptions.Compiled);

    private static HttpClient? _httpClient;
    private readonly HtmlDocument _homePage;
    private string? _animationKey;
    private List<int>? _defaultKeyBytesIndices;
    private int _defaultRowIndex;
    private string? _key;
    private List<byte>? _keyBytes;

    private ClientTransaction(HtmlDocument homePage)
    {
        _homePage = homePage;
    }

    public static async Task<ClientTransaction> CreateAsync(HttpClient client)
    {
        _httpClient = client;
        var page = await HandleXMigrationAsync();
        var tx = new ClientTransaction(page);
        await tx.InitAsync();
        return tx;
    }

    private async Task InitAsync()
    {
        var (rowIndex, keyIndices) = await GetIndicesAsync();
        _defaultRowIndex = rowIndex;
        _defaultKeyBytesIndices = keyIndices;
        _key = GetKey();
        _keyBytes = GetKeyBytes(_key);
        _animationKey = GetAnimationKey();
    }

    public static async Task<HtmlDocument> HandleXMigrationAsync()
    {
        if (_httpClient == null)
            throw new InvalidOperationException("HttpClient is not initialized");

        const string homeUrl = "https://x.com";
       
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36");

        var resp = await _httpClient.GetAsync(homeUrl);
        var html = await resp.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Migration regex
        var migrationRegex = new Regex(@"(https?:\/\/(?:www\.)?(?:twitter|x)\.com(?:\/x)?\/migrate[\/?]tok=[A-Za-z0-9%\-_]+)", RegexOptions.Compiled);

        // Check meta refresh tag
        var metaRefresh = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='refresh']");
        Match? migMatch = null;

        if (metaRefresh != null)
        {
            var metaContent = metaRefresh.OuterHtml;
            migMatch = migrationRegex.Match(metaContent);
        }

        migMatch ??= migrationRegex.Match(html);

        if (migMatch.Success)
        {
            resp = await _httpClient.GetAsync(migMatch.Groups[1].Value);
            html = await resp.Content.ReadAsStringAsync();
            doc = new HtmlDocument();
            doc.LoadHtml(html);
        }

        // Check for form-based migration
        var form = doc.DocumentNode.SelectSingleNode("//form[@name='f']")
            ?? doc.DocumentNode.SelectSingleNode("//form[@action='https://x.com/x/migrate']");

        if (form != null)
        {
            var actionUrl = form.GetAttributeValue("action", "https://x.com/x/migrate");
            var method = form.GetAttributeValue("method", "POST").ToUpperInvariant();

            var inputs = form.SelectNodes(".//input");
            var data = new Dictionary<string, string>();
            if (inputs != null)
            {
                foreach (var input in inputs)
                {
                    var name = input.GetAttributeValue("name", null);
                    var val = input.GetAttributeValue("value", "");
                    if (!string.IsNullOrEmpty(name))
                        data[name] = val;
                }
            }

            if (method == "GET")
            {
                var query = await new FormUrlEncodedContent(data).ReadAsStringAsync();
                var urlWithParams = $"{actionUrl}?{query}";
                resp = await _httpClient.GetAsync(urlWithParams);
            }
            else
            {
                var content = new FormUrlEncodedContent(data);
                resp = await _httpClient.PostAsync(actionUrl, content);
            }
            html = await resp.Content.ReadAsStringAsync();
            doc = new HtmlDocument();
            doc.LoadHtml(html);
        }

        return doc;
    }

    private string GetHomePageHtml()
    {
        return _homePage.DocumentNode.OuterHtml;
    }

    public async Task<(int, List<int>)> GetIndicesAsync()
    {
        if (_httpClient == null)
            throw new InvalidOperationException("HttpClient is not initialized");

        var html = GetHomePageHtml();
        var match = OnDemandFileRegex.Match(html);
        if (!match.Success || string.IsNullOrEmpty(match.Groups[1].Value))
            throw new InvalidOperationException("Couldn't get on-demand file hash");

        var hash = match.Groups[1].Value;
        var url = $"https://abs.twimg.com/responsive-web/client-web/ondemand.s.{hash}a.js";
        var resp = await _httpClient.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync();

        var indices = new List<int>();
        var matches = IndicesRegex.Matches(text);
        foreach (Match m in matches) 
            indices.Add(int.Parse(m.Groups[1].Value));
        
        if (indices.Count < 2)
            throw new InvalidOperationException("Couldn't get KEY_BYTE indices");

        return (indices[0], indices.GetRange(1, indices.Count - 1));
    }

    private string GetKey()
    {
        var meta = _homePage.DocumentNode.SelectSingleNode("//meta[@name='twitter-site-verification']");
        if (meta == null)
            throw new InvalidOperationException("Couldn't get key from the page source");
        
        var content = meta.GetAttributeValue("content", "");
        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException("Couldn't get key from the page source");
        
        return content;
    }

    private static List<byte> GetKeyBytes(string key)
    {
        return Convert.FromBase64String(key).ToList();
    }

    private List<HtmlNode> GetFrames()
    {
        return _homePage.DocumentNode
                   .SelectNodes("//*[starts-with(@id, 'loading-x-anim')]")
                   ?.ToList()
               ?? new List<HtmlNode>();
    }

    private List<List<int>> Get2dArray()
    {
        if (_keyBytes == null)
            throw new InvalidOperationException("Key bytes not initialized");

        var frames = GetFrames();
        var idx = _keyBytes[5] % 4;
        if (idx < 0 || idx >= frames.Count)
            throw new ArgumentOutOfRangeException(nameof(idx), $"Frame index {idx} is out of range.");

        var el = frames[idx];
        var g = el.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element)
            .FirstOrDefault();
        
        if (g == null)
            throw new InvalidOperationException("Couldn't find <g> element as a child.");

        // Get the second child of <g>
        var gChildren = g.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element)
            .ToList();

        if (gChildren.Count < 2)
            throw new InvalidOperationException("Not enough children in <g> element.");

        var pathEl = gChildren[1]; // Second element

        var d = pathEl.GetAttributeValue("d", null);
        if (string.IsNullOrEmpty(d))
            throw new InvalidOperationException("Couldn't find path 'd' attribute");
        
        var rest = d[9..]; // Substring(9)
        var segments = rest.Split('C');

        var result = new List<List<int>>();
        foreach (var item in segments)
        {
            var numberStrings = Regex.Replace(item, @"[^\d]+", " ")
                .Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var numbers = numberStrings.Select(int.Parse).ToList();
            result.Add(numbers);
        }

        return result;
    }

    private static double Solve(double value, double minVal, double maxVal, bool rounding)
    {
        var res = value * (maxVal - minVal) / 255 + minVal;
        return rounding ? Math.Floor(res) : Math.Round(res, 2);
    }

    private static string Animate(List<int> frames, double targetTime)
    {
        var fromColor = frames.GetRange(0, 3).Select(v => (double)v).Concat(new double[] { 1 }).ToArray();
        var toColor = frames.GetRange(3, 3).Select(v => (double)v).Concat(new double[] { 1 }).ToArray();
        var fromRot = new double[] { 0 };
        var toRot = new[] { Solve(frames[6], 60, 360, true) };
        var curves = frames.Skip(7).Select((v, i) => Solve(v, Utils.IsOdd(i), 1, false)).ToArray();
        var cubic = new Cubic(curves);
        var f = cubic.GetValue(targetTime);
        var color = Utils.Interpolate(fromColor, toColor, f).Select(v => Math.Max(0, Math.Min(255, v))).ToArray();
        var rot = Utils.Interpolate(fromRot, toRot, f);
        var matrix = Utils.ConvertRotationToMatrix(rot[0]);

        var hexArr = new List<string>();
        foreach (var v in color.Take(color.Length - 1))
            hexArr.Add(((int)Math.Round(v)).ToString("x"));
        
        foreach (var val in matrix)
        {
            var rv = Math.Abs(Math.Round(val, 2));
            var hx = Utils.FloatToHex(rv);
            if (hx.StartsWith('.'))
                hexArr.Add(("0" + hx).ToLower());
            else if (!string.IsNullOrEmpty(hx))
                hexArr.Add(hx.ToLower());
            else
                hexArr.Add("0");
        }

        hexArr.Add("0");
        hexArr.Add("0");
        return string.Join("", hexArr).Replace(".", "").Replace("-", "");
    }

    private string GetAnimationKey()
    {
        if (_keyBytes == null || _defaultKeyBytesIndices == null)
            throw new InvalidOperationException("Key bytes or indices not initialized");

        const int total = 4096;
        var rowIndex = _keyBytes[_defaultRowIndex] % 16;
        double frameTime = _defaultKeyBytesIndices.Select(i => _keyBytes[i] % 16).Aggregate(1, (a, b) => a * b);
        frameTime = Math.Round(frameTime / 10) * 10;
        var grid = Get2dArray();
        var row = grid[rowIndex];
        var t = frameTime / total;
        return Animate(row, t);
    }

    public string GenerateTransactionId(string method, string path, int? timeNow = null)
    {
        if (_keyBytes == null || _animationKey == null)
            throw new InvalidOperationException("Client transaction not properly initialized");

        var now = timeNow ?? (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1682924400);
        var timeBytes = new byte[4];
        for (var i = 0; i < 4; i++)
            timeBytes[i] = (byte)((now >> (i * 8)) & 0xff);
        
        var hashInput = $"{method}!{path}!{now}{DefaultKeyword}{_animationKey}";
        byte[] hashBytes;
        using (var sha = SHA256.Create())
        {
            hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        }

        var rnd = Random.Shared.Next(0, 256);
        var arr = new List<byte>();
        arr.AddRange(_keyBytes);
        arr.AddRange(timeBytes);
        arr.AddRange(hashBytes.Take(16));
        arr.Add((byte)AdditionalRandomNumber);

        var obfuscated = new List<byte> { (byte)rnd };
        obfuscated.AddRange(arr.Select(x => (byte)(x ^ rnd)));

        return Convert.ToBase64String(obfuscated.ToArray()).TrimEnd('=');
    }
}