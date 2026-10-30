using System.Runtime.InteropServices;
using System.Text;

public sealed unsafe class Pkcs11
{
    const nuint CKR_USER_ALREADY_LOGGED_IN = 0x100;
    const nuint CKF_SERIAL_SESSION = 0x4;
    const nuint CKU_USER = 1;
    const nuint CKA_CLASS = 0x0;
    const nuint CKA_LABEL = 0x3;
    const nuint CKO_PRIVATE_KEY = 3;
    const nuint CKM_SHA256_RSA_PKCS = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    struct CkAttribute
    {
        public nuint Type;
        public void* Value;
        public nuint ValueLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CkMechanism
    {
        public nuint Mechanism;
        public void* Parameter;
        public nuint ParameterLength;
    }

    readonly delegate* unmanaged<nuint, CkMechanism*, nuint, nuint> signInit;
    readonly delegate* unmanaged<nuint, byte*, nuint, byte*, nuint*, nuint> sign;
    readonly nuint session;
    readonly nuint privateKey;
    readonly object gate = new();

    public Pkcs11(string libraryPath, int slotIndex, string pin, string keyLabel)
    {
        var library = NativeLibrary.Load(libraryPath);
        var initialize = (delegate* unmanaged<void*, nuint>)NativeLibrary.GetExport(library, "C_Initialize");
        var getSlotList = (delegate* unmanaged<byte, nuint*, nuint*, nuint>)NativeLibrary.GetExport(library, "C_GetSlotList");
        var openSession = (delegate* unmanaged<nuint, nuint, void*, void*, nuint*, nuint>)NativeLibrary.GetExport(library, "C_OpenSession");
        var login = (delegate* unmanaged<nuint, nuint, byte*, nuint, nuint>)NativeLibrary.GetExport(library, "C_Login");
        var findObjectsInit = (delegate* unmanaged<nuint, CkAttribute*, nuint, nuint>)NativeLibrary.GetExport(library, "C_FindObjectsInit");
        var findObjects = (delegate* unmanaged<nuint, nuint*, nuint, nuint*, nuint>)NativeLibrary.GetExport(library, "C_FindObjects");
        var findObjectsFinal = (delegate* unmanaged<nuint, nuint>)NativeLibrary.GetExport(library, "C_FindObjectsFinal");
        signInit = (delegate* unmanaged<nuint, CkMechanism*, nuint, nuint>)NativeLibrary.GetExport(library, "C_SignInit");
        sign = (delegate* unmanaged<nuint, byte*, nuint, byte*, nuint*, nuint>)NativeLibrary.GetExport(library, "C_Sign");

        Check(initialize(null), "C_Initialize");

        nuint slotCount = 0;
        Check(getSlotList(1, null, &slotCount), "C_GetSlotList");
        var slots = new nuint[slotCount];
        fixed (nuint* slotList = slots)
        {
            Check(getSlotList(1, slotList, &slotCount), "C_GetSlotList");
        }
        if (slotIndex < 0 || (nuint)slotIndex >= slotCount)
        {
            throw new InvalidOperationException($"Slot index {slotIndex} not available, {slotCount} slot(s) with a token present");
        }
        SlotId = slots[slotIndex];

        nuint handle;
        Check(openSession(SlotId, CKF_SERIAL_SESSION, null, null, &handle), "C_OpenSession");
        session = handle;

        var pinBytes = Encoding.UTF8.GetBytes(pin);
        fixed (byte* pinPointer = pinBytes)
        {
            var loginResult = login(session, CKU_USER, pinPointer, (nuint)pinBytes.Length);
            if (loginResult != CKR_USER_ALREADY_LOGGED_IN)
            {
                Check(loginResult, "C_Login");
            }
        }

        nuint keyClass = CKO_PRIVATE_KEY;
        var labelBytes = Encoding.UTF8.GetBytes(keyLabel);
        fixed (byte* labelPointer = labelBytes)
        {
            var template = stackalloc CkAttribute[2];
            template[0] = new CkAttribute { Type = CKA_CLASS, Value = &keyClass, ValueLength = (nuint)sizeof(nuint) };
            template[1] = new CkAttribute { Type = CKA_LABEL, Value = labelPointer, ValueLength = (nuint)labelBytes.Length };
            Check(findObjectsInit(session, template, 2), "C_FindObjectsInit");
        }
        nuint keyHandle = 0;
        nuint found = 0;
        var findResult = findObjects(session, &keyHandle, 1, &found);
        Check(findObjectsFinal(session), "C_FindObjectsFinal");
        Check(findResult, "C_FindObjects");
        if (found == 0)
        {
            throw new InvalidOperationException($"Private key with label '{keyLabel}' not found in slot {SlotId}");
        }
        privateKey = keyHandle;
    }

    public nuint SlotId { get; }

    public byte[] Sign(byte[] data)
    {
        lock (gate)
        {
            var mechanism = new CkMechanism { Mechanism = CKM_SHA256_RSA_PKCS };
            Check(signInit(session, &mechanism, privateKey), "C_SignInit");
            var signature = new byte[1024];
            var signatureLength = (nuint)signature.Length;
            fixed (byte* input = data)
            fixed (byte* output = signature)
            {
                Check(sign(session, input, (nuint)data.Length, output, &signatureLength), "C_Sign");
            }
            return signature[..(int)signatureLength];
        }
    }

    static void Check(nuint result, string function)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{function} failed with CK_RV 0x{(ulong)result:X}");
        }
    }
}
