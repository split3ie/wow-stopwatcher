using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// Keys protected with DPAPI (current user scope), one <c>&lt;id&gt;.key</c> file per database in a folder under LocalApplicationData.
/// DPAPI is called through P/Invoke of crypt32, so no package is needed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDatabaseKeyStore(string folder) : IDatabaseKeyStore
{
    private const int CryptProtectUiForbidden = 0x1;

    private static readonly byte[] Entropy = "WowStopwatcher.DatabaseKey.v1"u8.ToArray();

    public bool IsAvailable => true;

    /// <summary><c>%LocalAppData%\WowStopwatcher\keys</c>.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WowStopwatcher", "keys");

    public DatabaseKey? Get(Guid databaseId)
    {
        var path = PathFor(databaseId);
        byte[] blob;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            blob = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseKeyStoreException("The database key file could not be read.", ex);
        }

        byte[]? plain = null;
        try
        {
            plain = Transform(blob, protect: false);
            if (plain.Length != DatabaseKey.Length)
            {
                throw new DatabaseKeyStoreException("The database key file is damaged.");
            }

            return DatabaseKey.FromBytes(plain);
        }
        finally
        {
            if (plain is not null)
            {
                Array.Clear(plain);
            }
        }
    }

    public void Save(Guid databaseId, DatabaseKey key)
    {
        var path = PathFor(databaseId);
        var plain = key.ToBytes();
        try
        {
            var blob = Transform(plain, protect: true);
            Directory.CreateDirectory(folder);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, blob);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseKeyStoreException("The database key file could not be written.", ex);
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    public void Delete(Guid databaseId)
    {
        try
        {
            File.Delete(PathFor(databaseId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseKeyStoreException("The database key file could not be deleted.", ex);
        }
    }

    private string PathFor(Guid databaseId) => Path.Combine(folder, databaseId.ToString("D") + ".key");

    private static byte[] Transform(byte[] data, bool protect)
    {
        var inputHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        var input = new DataBlob { Size = data.Length, Data = inputHandle.AddrOfPinnedObject() };
        var entropy = new DataBlob { Size = Entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
        var output = new DataBlob();
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                throw new DatabaseKeyStoreException(
                    protect
                        ? "The database key could not be protected for this user."
                        : "The database key file could not be decrypted for this user (it is damaged or belongs to another account).",
                    new Win32Exception(error));
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                LocalFree(output.Data);
            }

            inputHandle.Free();
            entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
