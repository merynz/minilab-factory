namespace MiniLab.Core.Policy
{
    public enum ConsentState
    {
        Unknown = 0,
        NotRequired = 1,
        RequiredAndDenied = 2,
        RequiredAndGranted = 3
    }
}
