using System.Runtime.InteropServices;
using System.Text;

namespace TimetableAlert.Services;

/// <summary>A secret read out of Windows Credential Manager.</summary>
/// <param name="UserName">The user name stored alongside it, if any.</param>
/// <param name="Secret">The secret itself.</param>
internal sealed record StoredCredential(string? UserName, string Secret);

/// <summary>
/// Reads and writes secrets in Windows Credential Manager, so neither the calendar feed URL nor
/// the parent's password is ever written to a file of ours. The feed URL is kept here too: it
/// carries its own token, and so is every bit as sensitive as a password.
/// </summary>
internal static partial class CredentialStore
{
    /// <summary>The feed URL, whose token is the only thing needed to download lessons.</summary>
    internal const string FeedTarget = "TimetableAlert:FeedUrl";

    /// <summary>The parent Canvas login, needed only to look up teacher names.</summary>
    internal const string CanvasTarget = "TimetableAlert:Canvas";

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    /// <summary>Reads a secret, or null when nothing is stored under that name.</summary>
    internal static StoredCredential? Read(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var handle))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            var secret = Encoding.Unicode.GetString(blob);
            Array.Clear(blob);

            return new StoredCredential(Marshal.PtrToStringUni(credential.UserName), secret);
        }
        finally
        {
            CredFree(handle);
        }
    }

    /// <summary>Stores a secret under a name, replacing whatever was there.</summary>
    /// <returns>False if Windows refused to store it.</returns>
    internal static bool Write(string target, string? userName, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(secret);

        var blob = Encoding.Unicode.GetBytes(secret);
        var targetPtr = IntPtr.Zero;
        var userPtr = IntPtr.Zero;
        var blobPtr = IntPtr.Zero;

        try
        {
            targetPtr = Marshal.StringToCoTaskMemUni(target);
            userPtr = Marshal.StringToCoTaskMemUni(userName ?? string.Empty);
            blobPtr = Marshal.AllocCoTaskMem(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = userPtr,
            };

            return CredWrite(in credential, 0);
        }
        finally
        {
            Array.Clear(blob);
            FreeIfSet(blobPtr);
            FreeIfSet(userPtr);
            FreeIfSet(targetPtr);
        }
    }

    /// <summary>Removes a stored secret, saying nothing if there was none.</summary>
    internal static void Delete(string target) => _ = CredDelete(target, CredTypeGeneric, 0);

    private static void FreeIfSet(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in Credential credential, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);

    /// <summary>
    /// Win32 CREDENTIALW. Declared with raw pointers rather than marshalled strings so the whole
    /// struct stays blittable, which is what <c>[LibraryImport]</c> requires; the strings are
    /// marshalled by hand either side of the call.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        internal uint Flags;
        internal uint Type;
        internal IntPtr TargetName;
        internal IntPtr Comment;
        internal long LastWritten;
        internal uint CredentialBlobSize;
        internal IntPtr CredentialBlob;
        internal uint Persist;
        internal uint AttributeCount;
        internal IntPtr Attributes;
        internal IntPtr TargetAlias;
        internal IntPtr UserName;
    }
}
