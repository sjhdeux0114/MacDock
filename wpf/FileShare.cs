using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacDock;

internal static class FileShare
{
    internal static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacDock", "FileShare");
    internal sealed record State(string SharedRoot, int ConfiguredPort, bool AutoStart, bool NeedsSetup, bool Running, int ActivePort, string[] PhoneUrls);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [DllImport("MacDockShare", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr MacDockShare([MarshalAs(UnmanagedType.LPUTF8Str)] string folder,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string action, [MarshalAs(UnmanagedType.LPUTF8Str)] string payload);
    [DllImport("MacDockShare", CallingConvention = CallingConvention.Cdecl)]
    static extern void MacDockShareFree(IntPtr result);

    internal static State Command(string action, object? settings = null, string? folder = null)
    {
        IntPtr result = MacDockShare(folder ?? Folder, action, JsonSerializer.Serialize(settings, Json));
        try {
            string response = Marshal.PtrToStringUTF8(result) ?? throw new InvalidDataException("서버 응답이 없습니다.");
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
            return JsonSerializer.Deserialize<State>(response, Json) ?? throw new InvalidDataException("서버 설정을 읽을 수 없습니다.");
        } finally { MacDockShareFree(result); }
    }
}
