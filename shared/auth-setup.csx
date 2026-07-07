#r "nuget: BCrypt.Net-Next, 4.0.3"

Console.Write("Enter desired password: ");
var password = Console.ReadLine()?.Trim();
if (string.IsNullOrEmpty(password))
{
    Console.WriteLine("Password cannot be empty.");
    return;
}

var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

Console.WriteLine();
Console.WriteLine("Add these to your vault/.env:");
Console.WriteLine($"JWT_SECRET={secret}");
Console.WriteLine($"AUTH_USERNAME=admin");
Console.WriteLine($"AUTH_PASSWORD_HASH={hash}");
Console.WriteLine();
Console.WriteLine("Add JWT_SECRET to all other module .env files too.");
