
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

        Show("greeting");
        Show("greetng");
    }

    static void WriteLocal([Localized] string message) {
        Console.WriteLine(message);
    }

    static void Show([WordsKey] string key) {
        Console.WriteLine(key);
    }

    [return:Localized]
    static string GetLocalizedMessage() {
        return "hello";
    }
}
