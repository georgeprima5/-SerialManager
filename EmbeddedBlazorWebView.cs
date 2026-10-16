using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.FileProviders;

namespace SerialManager;

/// <summary>
/// 畫面檔（wwwroot）和 Blazor 啟動檔（_framework）都從 exe 裡面讀，不讀硬碟上的資料夾。
/// 這樣打包成單一 exe 後，只要那一個檔案就能執行。
/// </summary>
public class EmbeddedBlazorWebView : BlazorWebView
{
    public override IFileProvider CreateFileProvider(string contentRootDir)
    {
        var assembly = typeof(EmbeddedBlazorWebView).Assembly;
        return new CompositeFileProvider(
            new ManifestEmbeddedFileProvider(assembly, "wwwroot"),
            new EmbeddedFileProvider(assembly, "SerialManager.webview"));
    }
}
