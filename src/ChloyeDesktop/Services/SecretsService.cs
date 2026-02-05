using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ChloyeDesktop.Services;

public class SecretsService
{
    private readonly ILogger<SecretsService> _logger;
    private const string CredentialPrefix = "ChloyeDesktop";

    public SecretsService(ILogger<SecretsService> logger)
    {
        _logger = logger;
    }

    public void SetSecret(string key, string value)
    {
        var targetName = $"{CredentialPrefix}/{key}";
        var credentialBlob = Encoding.Unicode.GetBytes(value);

        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE.GENERIC,
            TargetName = targetName,
            CredentialBlobSize = (uint)credentialBlob.Length,
            CredentialBlob = Marshal.AllocHGlobal(credentialBlob.Length),
            Persist = CRED_PERSIST.LOCAL_MACHINE,
            UserName = Environment.UserName
        };

        try
        {
            Marshal.Copy(credentialBlob, 0, credential.CredentialBlob, credentialBlob.Length);

            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"Failed to write credential: {error}");
            }

            _logger.LogInformation("Secret stored: {Key}", key);
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    public string? GetSecret(string key)
    {
        var targetName = $"{CredentialPrefix}/{key}";

        if (!CredRead(targetName, CRED_TYPE.GENERIC, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, (int)credential.CredentialBlobSize);
            return Encoding.Unicode.GetString(blob);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public bool DeleteSecret(string key)
    {
        var targetName = $"{CredentialPrefix}/{key}";
        var result = CredDelete(targetName, CRED_TYPE.GENERIC, 0);
        if (result)
        {
            _logger.LogInformation("Secret deleted: {Key}", key);
        }
        return result;
    }

    public bool HasSecret(string key)
    {
        return GetSecret(key) != null;
    }

    #region Windows Credential Manager P/Invoke

    private enum CRED_TYPE : uint
    {
        GENERIC = 1,
    }

    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, CRED_TYPE type, uint flags);

    #endregion
}
