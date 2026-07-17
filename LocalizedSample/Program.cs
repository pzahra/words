
namespace PatTech.Localization;

public static class Program
{
    [Localized]
    static string Bye => "goodbye";

    public static void Main()
    {
        WriteLocal(GetLocalizedMessage());
        WriteLocal("welcome");
        WriteLocal(Bye);
    }

    static void WriteLocal([Localized] string message) {
        Console.WriteLine(message);
    }

    [return:Localized]
    static string GetLocalizedMessage() {
        return "hello";
    }
}
