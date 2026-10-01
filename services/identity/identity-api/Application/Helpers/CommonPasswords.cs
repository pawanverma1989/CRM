namespace IdentityApi.Application.Helpers;

public static class CommonPasswords
{
    private static readonly HashSet<string> _list = new(StringComparer.OrdinalIgnoreCase)
    {
        "password1234","password123456","qwerty12345","qwerty123456","123456789012",
        "1234567890123","admin12345","admin123456","letmein1234","welcome1234",
        "iloveyou123","monkey12345","dragon12345","master12345","superman123",
        "batman12345","trustno1234","sunshine12345","princess1234","shadow12345",
        "michael1234","jessica1234","charlie1234","thomas12345","andrew12345",
        "12345678901","password1111","password9876","abc1234567","zxcvbn12345",
        "passw0rd123","passw0rd1234","hello12345678","welcome12345","test123456",
        "test12345678","root12345678","toor12345678","pass1234567","pass12345678",
        "secret123456","baseball1234","football1234","soccer12345","hockey12345",
        "freedom12345","whatever1234","mustang1234","access123456","starwars1234"
    };

    public static bool Contains(string password) => _list.Contains(password);
}
