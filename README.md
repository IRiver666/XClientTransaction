# XClientTransaction
generate x-client-transaction-id (https://x.com/)

# Example

var uri = new Uri("https://x.com/i/api/graphql/1VOOyvKkiI3FMmkeDNxM9A/UserByScreenName");
var method = HttpMethod.Get;

HttpClient httpClient = new(new HttpClientHandler()
{
    AutomaticDecompression = DecompressionMethods.All
});

var xclient = await XClientTransaction.ClientTransaction.CreateAsync(httpClient);
var xtid = xclient.GenerateTransactionId(method.Method.ToUpper(), uri.AbsolutePath);

Console.WriteLine(xtid);
