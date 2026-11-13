using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// Keys as Keychain generic passwords (service <see cref="Service"/>, account = database id), through Security.framework.
/// The secret is passed in memory, never on a command line. The key is stored as its 64 hex digits.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacDatabaseKeyStore : IDatabaseKeyStore
{
    public const string Service = "com.split3ie.wowstopwatcher.dbkey";

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public bool IsAvailable => true;

    public DatabaseKey? Get(Guid databaseId)
    {
        var status = Find(databaseId, out var length, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return null;
        }

        Check(status, "read");
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            var key = DatabaseKey.FromHex(Encoding.UTF8.GetString(bytes));
            Array.Clear(bytes);
            return key ?? throw new DatabaseKeyStoreException("The Keychain entry for the database key is damaged.");
        }
        finally
        {
            Release(item, data);
        }
    }

    public void Save(Guid databaseId, DatabaseKey key)
    {
        var secret = Encoding.UTF8.GetBytes(key.ToHex());
        try
        {
            var status = Find(databaseId, out _, out var data, out var item);
            if (status == ErrSecItemNotFound)
            {
                var service = Encoding.UTF8.GetBytes(Service);
                var account = Encoding.UTF8.GetBytes(databaseId.ToString("D"));
                status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, (uint)secret.Length, secret, out var added);
                Check(status, "write");
                CFRelease(added);
                return;
            }

            Check(status, "write");
            try
            {
                Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret), "write");
            }
            finally
            {
                Release(item, data);
            }
        }
        finally
        {
            Array.Clear(secret);
        }
    }

    public void Delete(Guid databaseId)
    {
        var status = Find(databaseId, out _, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return;
        }

        Check(status, "delete");
        try
        {
            Check(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            Release(item, data);
        }
    }

    private static int Find(Guid databaseId, out uint length, out IntPtr data, out IntPtr item)
    {
        var service = Encoding.UTF8.GetBytes(Service);
        var account = Encoding.UTF8.GetBytes(databaseId.ToString("D"));
        return SecKeychainFindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out length, out data, out item);
    }

    private static void Release(IntPtr item, IntPtr data)
    {
        if (data != IntPtr.Zero)
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
        }

        if (item != IntPtr.Zero)
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string action)
    {
        if (status != ErrSecSuccess)
        {
            throw new DatabaseKeyStoreException($"The macOS Keychain refused to {action} the database key (status {status}). It may be locked or access was denied.");
        }
    }

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
