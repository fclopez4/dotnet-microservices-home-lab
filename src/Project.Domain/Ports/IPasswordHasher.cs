namespace Project.Domain.Ports;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
